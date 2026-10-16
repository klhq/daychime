using Microsoft.Windows.Widgets.Providers;
using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Windows.Storage;
using Windows.UI.Notifications;
using System.Xml.Linq;

namespace DaychimeWidget;

internal sealed class Daychime : WidgetImplBase
{
    public static string DefinitionId => "Daychime_Widget";
    private const string FinishReminderTag = "finish-reminder";
    private const string FinishReminderGroup = "workday";
    private const string ShiftPrefix = "shift|";
    private const string ShiftStatePrefix = "v2|";
    private const string EditingPrefix = "editing|";
    private const string EditingErrorPrefix = "editing-error|";
    private const string ConfirmClearPrefix = "confirm-clear|";
    private const string AutoSettingsPrefix = "auto-settings|";
    private const string AutoSettingsErrorPrefix = "auto-settings-error|";
    private const string OptionsPrefix = "options|";
    private const string AutoClockInEnabledKey = "AutoClockInOnUnlock";
    private const string AutoClockInAfterKey = "AutoClockInAfter";
    private const string AutoClockInEnabledSinceKey = "AutoClockInEnabledSince";
    private const string LastAutoClockInCycleKey = "LastAutoClockInCycle";
    private static readonly double[] WorkHoursPresets = ShiftLifecycle.WorkHoursPresets;
    private string invalidClockInValue;
    private ShiftTimeEditError clockInEditError;
    private string invalidAutoClockInValue;

    public Daychime(string widgetId, string startingState) : base(widgetId, startingState) { }

    public override void OnActionInvoked(WidgetActionInvokedArgs args)
    {
        var stored = ReadSnapshot();
        var current = ShiftLifecycle.Normalize(stored, GetCurrentTime());
        if (current != stored) WriteSnapshot(current);
        switch (args.Verb)
        {
            case "clockIn":
                var clockInTime = GetCurrentTime();
                if (ReadSnapshot().Current is not Shift runningShift || runningShift.Finish <= clockInTime)
                    StartShift(clockInTime);
                break;
            case "clockOut": ClockOut(GetCurrentTime()); break;
            case "undoClockOut": UndoClockOut(); break;
            case "edit":
                if (ReadSnapshot().Current is not null && !State.StartsWith(EditingPrefix, StringComparison.Ordinal))
                    state = EditingPrefix + GetBaseState(State);
                break;
            case "cancelEdit": state = GetBaseState(State); break;
            case "showOptions": state = OptionsPrefix + GetBaseState(State); break;
            case "closeOptions": state = GetBaseState(State); break;
            case "toggleTimeFormat": ToggleUse24Hour(); break;
            case "configureAutoClockIn": state = AutoSettingsPrefix + GetBaseState(State); break;
            case "cancelAutoClockIn": state = GetBaseState(State); break;
            case "turnOffAutoClockIn": TurnOffAutoClockIn(); break;
            case "saveAutoClockIn": SaveAutoClockInSettings(args.Data); break;
            case "cycleWorkHours": CycleWorkHours(); break;
            case "askClear": state = ConfirmClearPrefix + GetBaseState(State); break;
            case "cancelClear": state = State.StartsWith(ConfirmClearPrefix, StringComparison.Ordinal) ? GetBaseState(State) : State; break;
            case "clear": ClearCurrentShift(); break;
            case "saveTime": SaveClockInTime(args.Data); break;
        }
        TryUpdateFinishReminder();
        UpdateWidget();
    }

    public override string GetTemplateForWidget() => ReadPackageFileFromUri("ms-appx:///Templates/DaychimeTemplate.json");

    public override string GetDataForWidget()
    {
        var now = GetCurrentTime();
        var stored = ReadSnapshot();
        var snapshot = ShiftLifecycle.Normalize(stored, now);
        if (snapshot != stored) WriteSnapshot(snapshot);
        var strings = JsonNode.Parse(ReadPackageFileFromUri(GetStringsUri()))!.AsObject();
        var confirmingClear = State.StartsWith(ConfirmClearPrefix, StringComparison.Ordinal);
        var isEditing = State.StartsWith(EditingPrefix, StringComparison.Ordinal) || State.StartsWith(EditingErrorPrefix, StringComparison.Ordinal);
        var hasTimeError = State.StartsWith(EditingErrorPrefix, StringComparison.Ordinal);
        var hasAutoTimeError = State.StartsWith(AutoSettingsErrorPrefix, StringComparison.Ordinal);
        var isAutoSettings = State.StartsWith(AutoSettingsPrefix, StringComparison.Ordinal) || hasAutoTimeError;
        var isOptions = State.StartsWith(OptionsPrefix, StringComparison.Ordinal);
        var showMain = !isEditing && !confirmingClear && !isAutoSettings && !isOptions;
        var clockedIn = snapshot.Current.HasValue;
        var shift = snapshot.Current.GetValueOrDefault();
        var start = clockedIn ? shift.Start.ToLocalTime() : default;
        var finish = clockedIn ? shift.Finish.ToLocalTime() : default;
        var isWorkdayComplete = clockedIn && finish <= now;
        var use24Hour = GetUse24Hour();
        var timeFormat = use24Hour ? "HH:mm" : CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern;
        var workHours = clockedIn ? shift.WorkHours : GetDefaultWorkHours();
        var nextWorkHours = WorkHoursPresets[(Array.IndexOf(WorkHoursPresets, workHours) + 1) % WorkHoursPresets.Length];
        var autoClockInAfter = GetAutoClockInAfter();

        return new JsonObject {
            ["date"] = (clockedIn ? start : now).ToString("D", CultureInfo.CurrentCulture),
            ["clockedIn"] = clockedIn,
            ["canEdit"] = clockedIn && !confirmingClear && !isEditing && !isAutoSettings,
            ["isEditing"] = clockedIn && isEditing,
            ["hasTimeError"] = clockedIn && hasTimeError,
            ["isAutoSettings"] = isAutoSettings,
            ["hasAutoTimeError"] = hasAutoTimeError,
            ["isOptions"] = isOptions,
            ["showMain"] = showMain,
            ["hasUndoableShift"] = !clockedIn && snapshot.Previous is Shift { ClockOut: not null },
            ["finishCompact"] = clockedIn ? finish.ToString(timeFormat, CultureInfo.CurrentCulture) + (finish.Date > start.Date ? " +1" : "") : "--:--",
            ["finishCaption"] = GetString(strings, isWorkdayComplete ? "workdayCompleteShort" : "finishLabel"),
            ["workHoursCompact"] = FormatHours(workHours) + "h",
            ["previousShiftCompact"] = snapshot.Previous is Shift previous
                ? previous.ClockOut is DateTimeOffset actual
                    ? string.Format(CultureInfo.CurrentCulture, GetString(strings, "previousCompact"), previous.Start.ToLocalTime().ToString(timeFormat, CultureInfo.CurrentCulture), actual.ToLocalTime().ToString(timeFormat, CultureInfo.CurrentCulture))
                    : GetString(strings, "previousMissingCompact")
                : "",
            ["autoClockInCompact"] = GetAutoClockInOnUnlock() ? string.Format(CultureInfo.CurrentCulture, GetString(strings, "autoClockInOnCompact"), autoClockInAfter.ToString("HH:mm", CultureInfo.InvariantCulture)) : GetString(strings, "autoClockInOffCompact"),
            ["more"] = GetString(strings, "more"),
            ["done"] = GetString(strings, "done"),
            ["editShort"] = GetString(strings, "editShort"),
            ["saveShort"] = GetString(strings, "saveShort"),
            ["newShiftShort"] = GetString(strings, "newShiftShort"),
            ["clockInTimeCompactLabel"] = string.Format(CultureInfo.CurrentCulture, GetString(strings, "clockInTimeCompactLabel"), clockedIn ? start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""),
            ["clockInTimeError"] = GetString(strings, clockInEditError switch {
                ShiftTimeEditError.Future => "futureClockInShort",
                ShiftTimeEditError.InvalidLocalTime => "invalidLocalTimeShort",
                _ => "invalidTimeShort"
            }),
            ["autoAfterCompactLabel"] = GetString(strings, "autoAfterCompactLabel"),
            ["invalidTimeShort"] = GetString(strings, "invalidTimeShort"),
            ["autoClockInMissingShort"] = GetString(strings, "autoClockInMissingShort"),
            ["workdayCompleteShort"] = GetString(strings, "workdayCompleteShort"),
            ["confirmingClear"] = confirmingClear,
            ["showFormatToggle"] = showMain,
            ["clockIn"] = clockedIn ? start.ToString(timeFormat, CultureInfo.CurrentCulture) : "--:--",
            ["clockInValue"] = hasTimeError && invalidClockInValue is not null ? invalidClockInValue : clockedIn ? start.ToString("HH:mm", CultureInfo.InvariantCulture) : "",
            ["finish"] = clockedIn ? finish.ToString(timeFormat, CultureInfo.CurrentCulture) : "--:--",
            ["endsNextDay"] = clockedIn && finish.Date > start.Date,
            ["isWorkdayComplete"] = isWorkdayComplete,
            ["canClockOut"] = clockedIn && !confirmingClear && !isEditing && !isAutoSettings,
            ["canUndoClockOut"] = !clockedIn && snapshot.Previous is Shift { ClockOut: not null } && !confirmingClear && !isEditing && !isAutoSettings,
            ["hasPreviousShift"] = snapshot.Previous is not null,
            ["previousShiftSummary"] = FormatPreviousShift(snapshot.Previous, strings, timeFormat),
            ["now"] = now.ToString(timeFormat, CultureInfo.CurrentCulture),
            ["use24Hour"] = use24Hour,
            ["formatBadge"] = use24Hour ? "24h" : "12h",
            ["formatToggleTitle"] = use24Hour ? GetString(strings, "switchTo12Hour") : GetString(strings, "switchTo24Hour"),
            ["autoClockInEnabled"] = GetAutoClockInOnUnlock(),
            ["autoClockInAfter"] = hasAutoTimeError && invalidAutoClockInValue is not null ? invalidAutoClockInValue : autoClockInAfter.ToString("HH:mm", CultureInfo.InvariantCulture),
            ["autoClockInStatus"] = GetAutoClockInOnUnlock() ? string.Format(CultureInfo.CurrentCulture, GetString(strings, "autoClockInAfterLabel"), autoClockInAfter.ToString(timeFormat, CultureInfo.CurrentCulture)) : GetString(strings, "autoClockInOffLabel"),
            ["autoClockInSettingsTitle"] = GetString(strings, "autoClockInSettingsTitle"),
            ["autoClockInAfterTitle"] = GetString(strings, "autoClockInAfterTitle"),
            ["autoClockInExplanation"] = GetString(strings, "autoClockInExplanation"),
            ["autoClockInMissing"] = GetString(strings, "autoClockInMissing"),
            ["needsAutoClockInTime"] = GetAutoClockInOnUnlock() && !isEditing && !isAutoSettings && !confirmingClear
                && snapshot.LastCycle != ShiftLifecycle.GetCycle(now, autoClockInAfter)
                && TimeOnly.FromDateTime(now.LocalDateTime) >= autoClockInAfter
                && (!clockedIn || finish <= now),
            ["autoClockInToggleTitle"] = GetString(strings, "configureAutoClockIn"),
            ["autoClockInPrimaryAction"] = GetString(strings, GetAutoClockInOnUnlock() ? "saveChanges" : "turnOnAutoClockIn"),
            ["turnOffAutoClockIn"] = GetString(strings, "turnOffAutoClockIn"),
            ["clockInLabel"] = GetString(strings, "clockInLabel"),
            ["finishLabel"] = GetString(strings, "finishLabel"),
            ["workdayComplete"] = GetString(strings, "workdayComplete"),
            ["endsTomorrow"] = GetString(strings, "endsTomorrow"),
            ["clockInActionTitle"] = GetString(strings, isWorkdayComplete ? "startNewWorkday" : "clockInNow"),
            ["clockInNow"] = GetString(strings, "clockInNow"),
            ["clockOutActionTitle"] = GetString(strings, "clockOutNow"),
            ["undoClockOutActionTitle"] = GetString(strings, "undoClockOut"),
            ["editTime"] = GetString(strings, "editTime"),
            ["clockInTimeLabel"] = GetString(strings, "clockInTimeLabel"),
            ["invalidTime"] = GetString(strings, "invalidTime"),
            ["saveChanges"] = GetString(strings, "saveChanges"),
            ["saveAutoClockIn"] = GetString(strings, "saveAutoClockIn"),
            ["clear"] = GetString(strings, "clear"),
            ["clearLink"] = GetString(strings, "clearLink"),
            ["confirmClear"] = GetString(strings, "confirmClear"),
            ["cancel"] = GetString(strings, "cancel"),
            ["workHoursSummary"] = string.Format(CultureInfo.CurrentCulture, GetString(strings, "workHoursSummary"), FormatHours(workHours)),
            ["workHoursToggleTitle"] = string.Format(CultureInfo.CurrentCulture, GetString(strings, "workHoursToggleTitle"), FormatHours(nextWorkHours)),
            ["notClockedInYet"] = GetString(strings, "notClockedInYet"),
            ["clockInHint"] = GetString(strings, "clockInHint"),
            ["nowLabel"] = GetString(strings, "nowLabel"),
            ["cancelEdit"] = GetString(strings, "cancelEdit")
        }.ToJsonString();
    }

    public override void OnSessionUnlock() => TryAutoClockIn(observedUnlock: true);

    public override void OnWidgetOpened()
    {
        TryAutoClockIn(observedUnlock: false);
        TryUpdateFinishReminder();
    }

    internal void RestoreFinishReminder() => TryUpdateFinishReminder();

    internal void CancelFinishReminder()
    {
        try
        {
            var notifier = ToastNotificationManager.CreateToastNotifier();
            foreach (var scheduled in notifier.GetScheduledToastNotifications())
                if (scheduled.Tag == FinishReminderTag && scheduled.Group == FinishReminderGroup
                    && ReminderBelongsToThisWidget(scheduled.Content.GetXml()))
                    notifier.RemoveFromSchedule(scheduled);
            foreach (var toast in ToastNotificationManager.History.GetHistory())
                if (toast.Tag == FinishReminderTag && toast.Group == FinishReminderGroup
                    && ReminderBelongsToThisWidget(toast.Content.GetXml()))
                    ToastNotificationManager.History.Remove(toast.Tag, toast.Group);
        }
        catch (Exception ex) { ProviderDiagnostics.Write($"Finish reminder cancellation failed: {ex}"); }
    }

    private void TryAutoClockIn(bool observedUnlock)
    {
        if (!GetAutoClockInOnUnlock()) return;
        var now = GetCurrentTime();
        var earliest = GetAutoClockInAfter();
        var cycle = ShiftLifecycle.GetCycle(now, earliest);
        var snapshot = ReadSnapshot();
        if (snapshot.LastCycle == cycle)
        {
            ProviderDiagnostics.Write($"Auto clock-in skipped for {Id}: cycle {cycle} already handled.");
            return;
        }
        if (snapshot.Current is Shift active && active.Finish > now)
        {
            ProviderDiagnostics.Write($"Auto clock-in deferred for {Id}: current shift is still active.");
            return;
        }
        // Opening the board is not evidence of when work began. Recover an actual
        // logon/unlock first; only a live unlock callback may contribute 'now'.
        var activity = SessionActivityHistory.Read(now, earliest);
        if (observedUnlock) activity.Add(now);
        DateTimeOffset? enabledSince = null;
        if (ApplicationData.Current.LocalSettings.Values[AutoClockInEnabledSinceKey] is string since
            && DateTimeOffset.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var enabled)) enabledSince = enabled;
        var startedAt = SessionActivityEvidence.FindStart(snapshot, activity, now, earliest, enabledSince);
        if (startedAt is null)
        {
            ProviderDiagnostics.Write($"Auto clock-in needs a recorded logon/unlock for {Id}; opening the board did not clock in.");
            return;
        }
        StartShift(startedAt.Value);
        ProviderDiagnostics.Write($"Auto clock-in recorded for {Id} at {startedAt:O} (cycle {cycle}; recovered session evidence: {!observedUnlock || startedAt != now}).");
        TryUpdateFinishReminder();
    }

    private void StartShift(DateTimeOffset startedAt)
    {
        // Recovered session events are UTC; record the offset the user was actually in.
        WriteSnapshot(ShiftLifecycle.Start(ReadSnapshot(), startedAt.ToLocalTime(), GetDefaultWorkHours(), GetAutoClockInAfter()));
    }

    private void ClockOut(DateTimeOffset clockedOutAt)
    {
        var snapshot = ReadSnapshot();
        WriteSnapshot(ShiftLifecycle.ClockOut(snapshot, clockedOutAt));
    }

    internal bool ClockOutFromReminder(string reminderKey, DateTimeOffset clickedAt)
    {
        var snapshot = ReadSnapshot();
        if (ShiftLifecycle.Normalize(snapshot, clickedAt).Current is not Shift current
            || ShiftLifecycle.GetReminderKey(current) != reminderKey || clickedAt < current.Start) return false;
        var updated = ShiftLifecycle.ClockOutFromReminder(snapshot, reminderKey, clickedAt);
        var originalState = State;
        WriteSnapshot(updated);
        // Persist before cancelling the toast so a failed widget update remains retryable.
        try { UpdateWidget(); }
        catch { state = originalState; throw; }
        TryUpdateFinishReminder();
        ProviderDiagnostics.Write($"Notification clock-out recorded for {Id} at {clickedAt:O}.");
        return true;
    }

    private void UndoClockOut()
    {
        WriteSnapshot(ShiftLifecycle.UndoClockOut(ReadSnapshot()));
    }

    private void ClearCurrentShift()
    {
        var snapshot = ReadSnapshot();
        WriteSnapshot(snapshot with { Current = null });
    }

    private void SaveClockInTime(string data)
    {
        invalidClockInValue = null;
        clockInEditError = ShiftTimeEditError.None;
        try
        {
            using var document = JsonDocument.Parse(data);
            if (document.RootElement.TryGetProperty("clockIn", out var time)) invalidClockInValue = time.GetString();
            if (TryParseClockIn(invalidClockInValue, out var parsed))
            {
                var snapshot = ReadSnapshot();
                if (snapshot.Current is not Shift existing) return;
                if (!ShiftTimeEditor.TryEdit(existing, parsed, GetCurrentTime(), TimeZoneInfo.Local, out var revised, out clockInEditError))
                {
                    state = EditingErrorPrefix + GetBaseState(State);
                    return;
                }
                WriteSnapshot(snapshot with {
                    Current = revised,
                    LastCycle = ShiftLifecycle.GetCycle(revised.Start, GetAutoClockInAfter())
                });
                return;
            }
        }
        catch (JsonException) { }
        state = EditingErrorPrefix + GetBaseState(State);
    }

    private void SaveAutoClockInSettings(string data)
    {
        invalidAutoClockInValue = null;
        try
        {
            using var document = JsonDocument.Parse(data);
            if (document.RootElement.TryGetProperty("autoClockInAfter", out var afterValue)) invalidAutoClockInValue = afterValue.GetString();
            if (!TryParseClockIn(invalidAutoClockInValue, out var after))
            {
                state = AutoSettingsErrorPrefix + GetBaseState(State);
                return;
            }
            if (!GetAutoClockInOnUnlock())
                ApplicationData.Current.LocalSettings.Values[AutoClockInEnabledSinceKey] = GetCurrentTime().ToString("O", CultureInfo.InvariantCulture);
            ApplicationData.Current.LocalSettings.Values[AutoClockInEnabledKey] = true;
            ApplicationData.Current.LocalSettings.Values[AutoClockInAfterKey] = after.ToString("HH:mm", CultureInfo.InvariantCulture);
            state = GetBaseState(State);
        }
        catch (JsonException) { state = AutoSettingsErrorPrefix + GetBaseState(State); }
    }

    private void TurnOffAutoClockIn()
    {
        ApplicationData.Current.LocalSettings.Values[AutoClockInEnabledKey] = false;
        state = GetBaseState(State);
    }

    private void CycleWorkHours()
    {
        var keepOptionsOpen = State.StartsWith(OptionsPrefix, StringComparison.Ordinal);
        var snapshot = ReadSnapshot();
        var current = snapshot.Current?.WorkHours ?? GetDefaultWorkHours();
        var next = WorkHoursPresets[(Array.IndexOf(WorkHoursPresets, current) + 1) % WorkHoursPresets.Length];
        ApplicationData.Current.LocalSettings.Values["WorkHours"] = next;
        if (snapshot.Current is Shift shift)
            WriteSnapshot(snapshot with { Current = ShiftLifecycle.Revise(shift with { WorkHours = next }) });
        if (keepOptionsOpen) state = OptionsPrefix + GetBaseState(State);
    }

    private void UpdateWidget() => WidgetManager.GetDefault().UpdateWidget(new WidgetUpdateRequestOptions(Id) { Data = GetDataForWidget(), CustomState = State });

    private void TryUpdateFinishReminder()
    {
        try { UpdateFinishReminder(); }
        catch (Exception ex) { ProviderDiagnostics.Write($"Finish reminder update failed: {ex}"); }
    }

    private static string GetStringsUri()
    {
        var language = CultureInfo.CurrentUICulture.Name;
        if (language.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) || language.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) || language.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase)) return "ms-appx:///Locales/TraditionalChinese.json";
        if (language.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase) || language.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase) || language.StartsWith("zh-SG", StringComparison.OrdinalIgnoreCase)) return "ms-appx:///Locales/SimplifiedChinese.json";
        return "ms-appx:///Resources/Strings.en.json";
    }

    private static string GetString(JsonObject strings, string key) => strings[key]!.GetValue<string>();

    private static DateTimeOffset GetCurrentTime()
    {
        return DateTimeOffset.Now;
    }

    private static string GetBaseState(string currentState)
    {
        foreach (var prefix in new[] { ConfirmClearPrefix, EditingPrefix, EditingErrorPrefix, AutoSettingsPrefix, AutoSettingsErrorPrefix, OptionsPrefix })
            if (currentState.StartsWith(prefix, StringComparison.Ordinal)) return currentState[prefix.Length..];
        return currentState;
    }

    private ShiftSnapshot ReadSnapshot()
    {
        var raw = GetBaseState(State);
        if (raw.StartsWith(ShiftStatePrefix, StringComparison.Ordinal))
        {
            var parts = raw.Split('|');
            if (parts.Length == 4 && TryReadShift(parts[1], out var current) && TryReadShift(parts[2], out var previous))
                return new ShiftSnapshot(current, previous, string.IsNullOrEmpty(parts[3]) ? null : parts[3]);
            ProviderDiagnostics.Write($"Invalid shift state for widget {Id}.");
            return default;
        }

        var lastCycle = ApplicationData.Current.LocalSettings.Values[LastAutoClockInCycleKey] as string;
        return TryGetLegacyShift(raw, out var legacy) ? new ShiftSnapshot(legacy, null, lastCycle) : new ShiftSnapshot(null, null, lastCycle);
    }

    private static bool TryGetLegacyShift(string raw, out Shift shift)
    {
        if (raw.StartsWith(ShiftPrefix, StringComparison.Ordinal))
        {
            var parts = raw.Split('|');
            if (parts.Length == 3 && DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start) && double.TryParse(parts[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var hours) && Array.IndexOf(WorkHoursPresets, hours) >= 0)
            {
                shift = new Shift(start, hours);
                return true;
            }
        }
        if (DateTimeOffset.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.RoundtripKind, out var legacyStart))
        {
            shift = new Shift(legacyStart, GetDefaultWorkHours());
            return true;
        }
        shift = default;
        return false;
    }

    private static bool TryReadShift(string raw, out Shift? shift) => ShiftStateCodec.TryRead(raw, out shift);

    private static string SerializeShift(Shift? shift) => ShiftStateCodec.Serialize(shift);

    private void WriteSnapshot(ShiftSnapshot snapshot) =>
        state = $"{ShiftStatePrefix}{SerializeShift(snapshot.Current)}|{SerializeShift(snapshot.Previous)}|{snapshot.LastCycle}";

    private static string FormatPreviousShift(Shift? previous, JsonObject strings, string timeFormat)
    {
        if (previous is not Shift shift) return string.Empty;
        string FormatTime(DateTimeOffset value) => $"{value.ToLocalTime().ToString("M/d", CultureInfo.CurrentCulture)} {value.ToLocalTime().ToString(timeFormat, CultureInfo.CurrentCulture)}";
        if (shift.ClockOut is DateTimeOffset clockOut)
        {
            var elapsedMinutes = (long)Math.Round((clockOut - shift.Start).TotalMinutes);
            var elapsed = string.Format(CultureInfo.CurrentCulture, GetString(strings, "elapsedHoursMinutes"), elapsedMinutes / 60, elapsedMinutes % 60);
            return string.Format(CultureInfo.CurrentCulture, GetString(strings, "previousClockedOutSummary"), FormatTime(shift.Start), FormatTime(clockOut), elapsed);
        }
        return string.Format(CultureInfo.CurrentCulture, GetString(strings, "previousUnrecordedSummary"), FormatTime(shift.Start), FormatTime(shift.Finish));
    }

    private static bool GetUse24Hour()
    {
        var value = ApplicationData.Current.LocalSettings.Values["Use24Hour"];
        return value is bool enabled ? enabled : CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H');
    }

    private static void ToggleUse24Hour() => ApplicationData.Current.LocalSettings.Values["Use24Hour"] = !GetUse24Hour();
    private static bool GetAutoClockInOnUnlock() => ApplicationData.Current.LocalSettings.Values[AutoClockInEnabledKey] is bool enabled && enabled;

    private static TimeOnly GetAutoClockInAfter()
    {
        if (ApplicationData.Current.LocalSettings.Values[AutoClockInAfterKey] is string stored && TryParseClockIn(stored, out var parsed)) return parsed;
        return new TimeOnly(6, 0);
    }

    private static double GetDefaultWorkHours()
    {
        var value = ApplicationData.Current.LocalSettings.Values["WorkHours"];
        return value is double hours && Array.IndexOf(WorkHoursPresets, hours) >= 0 ? hours : 9.0;
    }

    private static string FormatHours(double hours) => hours.ToString("0.#", CultureInfo.InvariantCulture);

    private static bool TryParseClockIn(string value, out TimeOnly parsed) => TimeInput.TryParse(value, out parsed);

    private void UpdateFinishReminder()
    {
        var notifier = ToastNotificationManager.CreateToastNotifier();
        var scheduledReminders = notifier.GetScheduledToastNotifications();
        var shift = ShiftLifecycle.Normalize(ReadSnapshot(), GetCurrentTime()).Current;
        var arguments = shift is Shift current
            ? $"action=clockOut&widget={Uri.EscapeDataString(Id)}&shift={Uri.EscapeDataString(ShiftLifecycle.GetReminderKey(current))}" : null;
        // Keep the current reminder even when its delivery time has just passed: Windows may
        // still be delivering it. Only invalidated revisions are removed from the schedule/history.
        foreach (var scheduled in scheduledReminders)
            if (scheduled.Tag == FinishReminderTag && scheduled.Group == FinishReminderGroup
                && !HasReminderArguments(scheduled.Content.GetXml(), arguments)) notifier.RemoveFromSchedule(scheduled);
        foreach (var toast in ToastNotificationManager.History.GetHistory())
            if (toast.Tag == FinishReminderTag && toast.Group == FinishReminderGroup
                && !HasReminderArguments(toast.Content.GetXml(), arguments))
                ToastNotificationManager.History.Remove(toast.Tag, toast.Group);
        if (shift is not Shift activeShift) return;
        var finish = activeShift.Finish.ToLocalTime();
        if (finish <= GetCurrentTime()) return;
        var strings = JsonNode.Parse(ReadPackageFileFromUri(GetStringsUri()))!.AsObject();
        var body = string.Format(CultureInfo.CurrentCulture, strings["finishNotificationBody"]!.GetValue<string>(), finish.ToString(GetUse24Hour() ? "HH:mm" : CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern, CultureInfo.CurrentCulture));
        var payload = new XElement("toast", new XAttribute("launch", "action=view"),
            new XElement("visual", new XElement("binding", new XAttribute("template", "ToastGeneric"),
                new XElement("text", GetString(strings, "finishNotificationTitle")), new XElement("text", body))),
            new XElement("actions", new XElement("action", new XAttribute("content", GetString(strings, "clockOutNow")),
                new XAttribute("arguments", arguments), new XAttribute("activationType", "foreground"))));
        var toastXml = new Windows.Data.Xml.Dom.XmlDocument();
        toastXml.LoadXml(payload.ToString(SaveOptions.DisableFormatting));
        foreach (var scheduled in scheduledReminders)
            if (scheduled.Tag == FinishReminderTag && scheduled.Group == FinishReminderGroup
                && HasReminderArguments(scheduled.Content.GetXml(), arguments))
            {
                // Preserve an already valid reminder. Formatting-only changes must not create
                // a cancellation window or replace a notification about to be delivered.
                if (scheduled.DeliveryTime == finish) return;
                notifier.RemoveFromSchedule(scheduled);
            }
        notifier.AddToSchedule(new ScheduledToastNotification(toastXml, finish) { Tag = FinishReminderTag, Group = FinishReminderGroup });
        ProviderDiagnostics.Write($"Finish reminder scheduled for {Id} at {finish:O}; notifications: {notifier.Setting}.");
    }

    private static bool HasReminderArguments(string xml, string arguments)
    {
        if (arguments is null) return false;
        try
        {
            foreach (var action in XElement.Parse(xml).Descendants("action"))
                if ((string)action.Attribute("arguments") == arguments) return true;
        }
        catch (System.Xml.XmlException) { }
        return false;
    }

    private bool ReminderBelongsToThisWidget(string xml)
    {
        try
        {
            var prefix = $"action=clockOut&widget={Uri.EscapeDataString(Id)}&shift=";
            var hasAction = false;
            foreach (var action in XElement.Parse(xml).Descendants("action"))
            {
                hasAction = true;
                if (((string)action.Attribute("arguments"))?.StartsWith(prefix, StringComparison.Ordinal) == true) return true;
            }
            // Legacy text-only reminders predate widget-bound activation.
            return !hasAction;
        }
        catch (System.Xml.XmlException) { return false; }
    }
}
