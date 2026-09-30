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
    private const string AutoClockInEnabledKey = "AutoClockInOnUnlock";
    private const string AutoClockInAfterKey = "AutoClockInAfter";
    private const string LastAutoClockInCycleKey = "LastAutoClockInCycle";
    private static readonly double[] WorkHoursPresets = ShiftLifecycle.WorkHoursPresets;

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
            case "cancelEdit": state = State.StartsWith(EditingPrefix, StringComparison.Ordinal) ? GetBaseState(State) : State; break;
            case "toggleTimeFormat": ToggleUse24Hour(); break;
            case "configureAutoClockIn": state = AutoSettingsPrefix + GetBaseState(State); break;
            case "cancelAutoClockIn": state = State.StartsWith(AutoSettingsPrefix, StringComparison.Ordinal) ? GetBaseState(State) : State; break;
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
        var isAutoSettings = State.StartsWith(AutoSettingsPrefix, StringComparison.Ordinal);
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
            ["confirmingClear"] = confirmingClear,
            ["showFormatToggle"] = !isEditing && !confirmingClear && !isAutoSettings,
            ["clockIn"] = clockedIn ? start.ToString(timeFormat, CultureInfo.CurrentCulture) : "--:--",
            ["clockInValue"] = clockedIn ? start.ToString("HH:mm", CultureInfo.InvariantCulture) : "",
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
            ["autoClockInAfter"] = autoClockInAfter.ToString("HH:mm", CultureInfo.InvariantCulture),
            ["autoClockInStatus"] = GetAutoClockInOnUnlock() ? string.Format(CultureInfo.CurrentCulture, GetString(strings, "autoClockInAfterLabel"), autoClockInAfter.ToString(timeFormat, CultureInfo.CurrentCulture)) : GetString(strings, "autoClockInOffLabel"),
            ["autoClockInSettingsTitle"] = GetString(strings, "autoClockInSettingsTitle"),
            ["autoClockInAfterTitle"] = GetString(strings, "autoClockInAfterTitle"),
            ["autoClockInExplanation"] = GetString(strings, "autoClockInExplanation"),
            ["autoClockInToggleTitle"] = GetString(strings, "configureAutoClockIn"),
            ["autoClockInPrimaryAction"] = GetString(strings, GetAutoClockInOnUnlock() ? "saveChanges" : "turnOnAutoClockIn"),
            ["turnOffAutoClockIn"] = GetString(strings, "turnOffAutoClockIn"),
            ["clockInLabel"] = GetString(strings, "clockInLabel"),
            ["finishLabel"] = GetString(strings, "finishLabel"),
            ["workdayComplete"] = GetString(strings, "workdayComplete"),
            ["endsTomorrow"] = GetString(strings, "endsTomorrow"),
            ["clockInActionTitle"] = GetString(strings, isWorkdayComplete ? "startNewWorkday" : "clockInNow"),
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

    public override void OnSessionUnlock() => TryAutoClockIn();

    public override void OnWidgetOpened()
    {
        TryAutoClockIn();
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

    private void TryAutoClockIn()
    {
        if (!GetAutoClockInOnUnlock()) return;
        var now = GetCurrentTime();
        var earliest = GetAutoClockInAfter();
        if (TimeOnly.FromDateTime(now.LocalDateTime) < earliest) return;
        var cycle = ShiftLifecycle.GetCycle(now, earliest);
        var snapshot = ReadSnapshot();
        if (snapshot.LastCycle == cycle)
        {
            ProviderDiagnostics.Write($"Auto clock-in skipped for {Id}: cycle {cycle} already handled.");
            return;
        }
        if (!ShiftLifecycle.CanAutoClockIn(snapshot, now, earliest))
        {
            ProviderDiagnostics.Write($"Auto clock-in deferred for {Id}: current shift is still active.");
            return;
        }
        StartShift(now);
        ProviderDiagnostics.Write($"Auto clock-in recorded for {Id} at {now:O} (cycle {cycle}).");
        TryUpdateFinishReminder();
    }

    private void StartShift(DateTimeOffset startedAt)
    {
        WriteSnapshot(ShiftLifecycle.Start(ReadSnapshot(), startedAt, GetDefaultWorkHours(), GetAutoClockInAfter()));
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
        try
        {
            using var document = JsonDocument.Parse(data);
            if (document.RootElement.TryGetProperty("clockIn", out var time) && TryParseClockIn(time.GetString(), out var parsed))
            {
                var snapshot = ReadSnapshot();
                if (snapshot.Current is not Shift existing) return;
                var changedStart = GetMostRecentOccurrence(parsed, GetCurrentTime());
                WriteSnapshot(snapshot with {
                    Current = ShiftLifecycle.Revise(existing with { Start = changedStart }),
                    LastCycle = ShiftLifecycle.GetCycle(changedStart, GetAutoClockInAfter())
                });
                return;
            }
        }
        catch (JsonException) { }
        state = EditingErrorPrefix + GetBaseState(State);
    }

    private void SaveAutoClockInSettings(string data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            var after = GetAutoClockInAfter();
            if (document.RootElement.TryGetProperty("autoClockInAfter", out var afterValue) && !TryParseClockIn(afterValue.GetString(), out after)) return;
            ApplicationData.Current.LocalSettings.Values[AutoClockInEnabledKey] = true;
            ApplicationData.Current.LocalSettings.Values[AutoClockInAfterKey] = after.ToString("HH:mm", CultureInfo.InvariantCulture);
            state = GetBaseState(State);
        }
        catch (JsonException) { }
    }

    private void TurnOffAutoClockIn()
    {
        ApplicationData.Current.LocalSettings.Values[AutoClockInEnabledKey] = false;
        state = GetBaseState(State);
    }

    private void CycleWorkHours()
    {
        var snapshot = ReadSnapshot();
        var current = snapshot.Current?.WorkHours ?? GetDefaultWorkHours();
        var next = WorkHoursPresets[(Array.IndexOf(WorkHoursPresets, current) + 1) % WorkHoursPresets.Length];
        ApplicationData.Current.LocalSettings.Values["WorkHours"] = next;
        if (snapshot.Current is Shift shift)
            WriteSnapshot(snapshot with { Current = ShiftLifecycle.Revise(shift with { WorkHours = next }) });
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
        foreach (var prefix in new[] { ConfirmClearPrefix, EditingPrefix, EditingErrorPrefix, AutoSettingsPrefix })
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

    private static DateTimeOffset GetMostRecentOccurrence(TimeOnly time, DateTimeOffset now)
    {
        var localNow = now.LocalDateTime;
        var candidate = localNow.Date.Add(time.ToTimeSpan());
        if (candidate > localNow) candidate = candidate.AddDays(-1);
        return new DateTimeOffset(candidate);
    }

    private static bool TryParseClockIn(string value, out TimeOnly parsed)
    {
        if (string.IsNullOrWhiteSpace(value)) { parsed = default; return false; }
        return TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed) || TimeOnly.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed);
    }

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
