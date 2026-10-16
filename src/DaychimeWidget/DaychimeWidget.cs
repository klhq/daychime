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
    private const string CustomizePrefix = "customize|";
    private const string CustomizeErrorPrefix = "customize-error|";
    // View prefixes from earlier versions; stripped when reading state, then shown as the main view.
    private static readonly string[] LegacyViewPrefixes = { "auto-settings|", "auto-settings-error|", "options|" };
    private const string AutoClockInEnabledKey = "AutoClockInOnUnlock";
    private const string AutoClockInAfterKey = "AutoClockInAfter";
    private const string AutoClockInEnabledSinceKey = "AutoClockInEnabledSince";
    private const string LastAutoClockInCycleKey = "LastAutoClockInCycle";
    private const string Use24HourKey = "Use24Hour";
    private const string WorkHoursKey = "WorkHours";
    private static readonly double[] WorkHoursPresets = ShiftLifecycle.WorkHoursPresets;
    private TimeOnly? submittedClockIn;
    private ShiftTimeEditError clockInEditError;

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
            case "saveTime": SaveClockInTime(args.Data); break;
            case "askClear": state = ConfirmClearPrefix + GetBaseState(State); break;
            case "cancelClear": state = State.StartsWith(ConfirmClearPrefix, StringComparison.Ordinal) ? GetBaseState(State) : State; break;
            case "clear": ClearCurrentShift(); break;
            case "saveCustomization": SaveCustomization(args.Data); break;
            case "cancelCustomization": state = GetBaseState(State); break;
        }
        TryUpdateFinishReminder();
        UpdateWidget();
    }

    // Opened from the Widgets Board "Customize widget" menu.
    internal void EnterCustomization() => state = CustomizePrefix + GetBaseState(State);

    public override string GetTemplateForWidget() => ReadPackageFileFromUri("ms-appx:///Templates/DaychimeTemplate.json");

    public override string GetDataForWidget()
    {
        // The provider can outlive a timezone change (travel, sleep); .NET caches the local zone.
        TimeZoneInfo.ClearCachedData();
        var now = GetCurrentTime();
        var stored = ReadSnapshot();
        var snapshot = ShiftLifecycle.Normalize(stored, now);
        if (snapshot != stored) WriteSnapshot(snapshot);
        var s = JsonNode.Parse(ReadPackageFileFromUri(GetStringsUri()))!.AsObject();
        string T(string key) => GetString(s, key);
        string F(string key, params object[] values) => string.Format(CultureInfo.CurrentCulture, T(key), values);

        var use24Hour = GetUse24Hour();
        // Displayed times and the hour drop-downs follow the card's 12/24-hour setting.
        string Clock(TimeOnly value) => FormatClock(value, use24Hour, s);
        string Time(DateTimeOffset value) => Clock(TimeOnly.FromDateTime(value.ToLocalTime().DateTime));
        string Duration(TimeSpan span) => span.TotalMinutes >= 60
            ? F("durationHoursMinutes", (int)span.TotalHours, span.Minutes) : F("durationMinutes", span.Minutes);

        var autoClockIn = GetAutoClockInOnUnlock();
        var autoClockInAfter = GetAutoClockInAfter();
        var phase = ShiftPresentation.GetPhase(snapshot, now, autoClockIn, autoClockInAfter);
        var showShift = phase is WorkdayPhase.Working or WorkdayPhase.Overtime;
        var isOvertime = phase == WorkdayPhase.Overtime;
        var shift = snapshot.Current.GetValueOrDefault();
        var clockedIn = snapshot.Current.HasValue;
        var workHours = clockedIn ? shift.WorkHours : GetDefaultWorkHours();

        var view = State.StartsWith(EditingPrefix, StringComparison.Ordinal) || State.StartsWith(EditingErrorPrefix, StringComparison.Ordinal) ? "edit"
            : State.StartsWith(ConfirmClearPrefix, StringComparison.Ordinal) ? "confirmClear"
            : State.StartsWith(CustomizePrefix, StringComparison.Ordinal) || State.StartsWith(CustomizeErrorPrefix, StringComparison.Ordinal) ? "customize"
            : "main";
        // Shift views need a shift; if it expired meanwhile, fall back to the main card.
        if (view is "edit" or "confirmClear" && !clockedIn) view = "main";
        var hasTimeError = view == "edit" && State.StartsWith(EditingErrorPrefix, StringComparison.Ordinal);
        var hasAutoTimeError = view == "customize" && State.StartsWith(CustomizeErrorPrefix, StringComparison.Ordinal);

        var data = new JsonObject
        {
            ["view"] = view,
            ["showShift"] = showShift,
            ["isOvertime"] = isOvertime,
            ["date"] = now.ToString(T("dateFormat"), CultureInfo.CurrentCulture),
            ["now"] = F("nowLine", Time(now)),
            ["editShort"] = T("editShort"),
            ["editTitle"] = T("editTime"),
            ["previousCaption"] = T("previousCaption"),
            ["settingsCaption"] = T("settingsCaption"),
            ["settingsLine"] = F("settingsLine", FormatHours(GetDefaultWorkHours()), T(use24Hour ? "timeFormat24" : "timeFormat12"),
                autoClockIn ? F("autoOnSummary", Clock(autoClockInAfter)) : T("autoOffSummary")),
            ["timezoneAdjusted"] = showShift && ShiftLifecycle.IsStartShownInDifferentOffset(shift, TimeZoneInfo.Local),
            ["timezoneAdjustedNote"] = T("timezoneAdjustedNote"),
            ["hasPrevious"] = snapshot.Previous is not null && phase != WorkdayPhase.ClockedOut,
            ["previousLine"] = FormatPreviousShift(snapshot.Previous, s, Time),
            ["previousShort"] = snapshot.Previous is Shift previous
                ? previous.ClockOut is DateTimeOffset previousOut ? F("previousCompact", Time(previous.Start), Time(previousOut)) : T("previousMissingCompact")
                : ""
        };

        switch (phase)
        {
            case WorkdayPhase.Working or WorkdayPhase.Overtime:
                var finish = shift.Finish.ToLocalTime();
                var left = ShiftPresentation.Left(shift, now);
                var over = ShiftPresentation.Over(shift, now);
                var progress = ShiftPresentation.GetProgress(shift, now);
                data["finish"] = Time(finish);
                data["finishCaption"] = T(isOvertime ? "finishCaptionReached" : finish.Date > now.LocalDateTime.Date ? "finishCaptionTomorrow" : "finishCaption");
                data["remaining"] = isOvertime ? F("overtimeLong", Duration(over)) : F("remainingLong", Duration(left));
                data["statusShort"] = isOvertime ? F("overtimeShort", ShiftPresentation.FormatClock(over)) : F("remainingShort", ShiftPresentation.FormatClock(left));
                data["barLight"] = ShiftPresentation.BuildProgressBar(progress, isOvertime, dark: false);
                data["barDark"] = ShiftPresentation.BuildProgressBar(progress, isOvertime, dark: true);
                data["progressAlt"] = F("progressAlt", (int)Math.Floor(progress * 100));
                data["startCaption"] = T("startCaption");
                data["startShort"] = Time(shift.Start);
                data["workedCaption"] = T("workedCaption");
                data["workedShort"] = ShiftPresentation.FormatClock(ShiftPresentation.Worked(shift, now));
                data["leftCaption"] = T(isOvertime ? "overCaption" : "leftCaption");
                data["leftShort"] = isOvertime ? "+" + ShiftPresentation.FormatClock(over) : ShiftPresentation.FormatClock(left);
                data["startedLine"] = F("startedLine", Time(shift.Start), FormatHours(shift.WorkHours));
                SetPrimary(data, s, "clockOut", "clockOutShort", "clockOutLong");
                break;
            case WorkdayPhase.Missing:
                data["headline"] = T("headlineMissing");
                data["statusShort"] = T("statusMissingShort");
                data["statusLong"] = T("statusMissingLong");
                SetPrimary(data, s, "clockIn", "clockInShort", "clockInNowLong");
                break;
            case WorkdayPhase.ClockedOut:
                var done = snapshot.Previous!.Value;
                var clockedOut = done.ClockOut!.Value;
                var total = TimeSpan.FromMinutes(Math.Max(0, Math.Round((clockedOut - done.Start).TotalMinutes)));
                data["headline"] = T("headlineClockedOut");
                data["statusShort"] = F("statusClockedOutShort", Time(clockedOut), ShiftPresentation.FormatClock(total));
                data["statusLong"] = F("statusClockedOutLong", Time(done.Start), Time(clockedOut), Duration(total));
                SetPrimary(data, s, "undoClockOut", "undoShort", "undoClockOut");
                break;
            default:
                data["headline"] = T("headlineIdle");
                data["statusShort"] = T(autoClockIn ? "statusIdleAutoShort" : "statusIdleManualShort");
                data["statusLong"] = autoClockIn ? F("statusIdleAutoLong", Clock(autoClockInAfter)) : T("statusIdleManualLong");
                SetPrimary(data, s, "clockIn", "clockInShort", "clockInLong");
                break;
        }

        if (view == "edit")
        {
            var start = shift.Start.ToLocalTime();
            data["editHint"] = start.ToString(T("dateFormat"), CultureInfo.CurrentCulture);
            data["editLabel"] = T("clockInTimeLabel");
            data["editLabelShort"] = F("editLabelShort", start.ToString("M/d", CultureInfo.CurrentCulture));
            // After a rejected save the time picker keeps the submitted time.
            data["clockInValue"] = TimeValue(hasTimeError && submittedClockIn is TimeOnly submitted ? submitted : TimeOnly.FromDateTime(start.DateTime));
            data["save"] = T("saveShort");
            data["cancel"] = T("cancel");
            data["hasTimeError"] = hasTimeError;
            data["clockInTimeError"] = T(clockInEditError switch {
                ShiftTimeEditError.Future => "futureClockInShort",
                ShiftTimeEditError.InvalidLocalTime => "invalidLocalTimeShort",
                _ => "invalidTimeShort"
            });
            data["clearTitle"] = T("clearLink");
        }
        else if (view == "confirmClear")
        {
            data["confirmClear"] = T("confirmClear");
            data["confirmClearHint"] = T("confirmClearHint");
            data["clear"] = T("clear");
            data["cancel"] = T("cancel");
        }
        else if (view == "customize")
        {
            var hourChoices = new JsonArray();
            foreach (var hours in WorkHoursPresets)
                hourChoices.Add(new JsonObject { ["title"] = F("hoursChoice", FormatHours(hours)), ["value"] = FormatHours(hours) });
            data["workHoursLabel"] = T("workHoursLabel");
            data["workHoursValue"] = FormatHours(workHours);
            data["workHoursChoices"] = hourChoices;
            data["timeFormatLabel"] = T("timeFormatLabel");
            data["timeFormatValue"] = ApplicationData.Current.LocalSettings.Values[Use24HourKey] is bool explicit24 ? (explicit24 ? "24" : "12") : "system";
            data["timeFormatChoices"] = new JsonArray(
                new JsonObject { ["title"] = T("timeFormatSystem"), ["value"] = "system" },
                new JsonObject { ["title"] = T("timeFormat12"), ["value"] = "12" },
                new JsonObject { ["title"] = T("timeFormat24"), ["value"] = "24" });
            data["autoToggleTitle"] = T("autoToggleTitle");
            data["autoValue"] = autoClockIn ? "true" : "false";
            data["autoAfterLabel"] = T("autoAfterLabel");
            data["autoAfterValue"] = TimeValue(autoClockInAfter);
            data["hasAutoTimeError"] = hasAutoTimeError;
            data["invalidTimeShort"] = T("invalidTimeShort");
            data["autoClockInExplanation"] = F("autoClockInExplanation", Clock(new TimeOnly(18, 0)));
            data["customizeSmallHint"] = T("customizeSmallHint");
            data["done"] = T("done");
            data["cancel"] = T("cancel");
        }
        return data.ToJsonString();
    }

    // Input.Time takes and submits HH:mm; the host's picker shows it in Windows' clock format.
    private static string TimeValue(TimeOnly value) => value.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static void SetPrimary(JsonObject data, JsonObject strings, string verb, string shortKey, string longKey)
    {
        data["primaryVerb"] = verb;
        data["primaryShort"] = GetString(strings, shortKey);
        data["primaryLong"] = GetString(strings, longKey);
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
        submittedClockIn = null;
        clockInEditError = ShiftTimeEditError.None;
        try
        {
            using var document = JsonDocument.Parse(data);
            if (TryReadTime(document.RootElement, "clockIn", out var parsed))
            {
                submittedClockIn = parsed;
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

    // Applies the Customize widget form: nothing changes unless every value is valid.
    private void SaveCustomization(string data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            string Read(string name) => ReadString(root, name);
            var autoOn = Read("autoClockIn") == "true";
            var hasAfter = TryReadTime(root, "autoClockInAfter", out var after);
            // A blank or invalid time only blocks saving when auto clock-in is on.
            if (autoOn && !hasAfter)
            {
                state = CustomizeErrorPrefix + GetBaseState(State);
                return;
            }

            var settings = ApplicationData.Current.LocalSettings.Values;
            if (double.TryParse(Read("workHours"), NumberStyles.Number, CultureInfo.InvariantCulture, out var hours)
                && Array.IndexOf(WorkHoursPresets, hours) >= 0)
            {
                settings[WorkHoursKey] = hours;
                var snapshot = ReadSnapshot();
                if (snapshot.Current is Shift shift && shift.WorkHours != hours)
                    WriteSnapshot(snapshot with { Current = ShiftLifecycle.Revise(shift with { WorkHours = hours }) });
            }
            switch (Read("timeFormat"))
            {
                case "12": settings[Use24HourKey] = false; break;
                case "24": settings[Use24HourKey] = true; break;
                case "system": settings.Remove(Use24HourKey); break;
            }
            if (autoOn && !GetAutoClockInOnUnlock())
                settings[AutoClockInEnabledSinceKey] = GetCurrentTime().ToString("O", CultureInfo.InvariantCulture);
            settings[AutoClockInEnabledKey] = autoOn;
            if (hasAfter) settings[AutoClockInAfterKey] = after.ToString("HH:mm", CultureInfo.InvariantCulture);
            state = GetBaseState(State);
        }
        catch (JsonException) { state = CustomizeErrorPrefix + GetBaseState(State); }
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
        foreach (var prefix in new[] { ConfirmClearPrefix, EditingPrefix, EditingErrorPrefix, CustomizePrefix, CustomizeErrorPrefix })
            if (currentState.StartsWith(prefix, StringComparison.Ordinal)) return currentState[prefix.Length..];
        foreach (var prefix in LegacyViewPrefixes)
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

    private static string FormatPreviousShift(Shift? previous, JsonObject strings, Func<DateTimeOffset, string> time)
    {
        if (previous is not Shift shift) return string.Empty;
        string FormatTime(DateTimeOffset value) => $"{value.ToLocalTime().ToString("M/d", CultureInfo.CurrentCulture)} {time(value)}";
        if (shift.ClockOut is DateTimeOffset clockOut)
        {
            var elapsedMinutes = (long)Math.Round((clockOut - shift.Start).TotalMinutes);
            var elapsed = string.Format(CultureInfo.CurrentCulture, GetString(strings, "elapsedHoursMinutes"), elapsedMinutes / 60, elapsedMinutes % 60);
            return string.Format(CultureInfo.CurrentCulture, GetString(strings, "previousRange"), FormatTime(shift.Start), FormatTime(clockOut), elapsed);
        }
        return string.Format(CultureInfo.CurrentCulture, GetString(strings, "previousRangeMissing"), FormatTime(shift.Start), FormatTime(shift.Finish));
    }

    // 12-hour times use the display language's pattern and AM/PM words, not the system culture's,
    // so the 12-hour setting works in every culture and TimeInput can parse what is shown.
    private static string FormatClock(TimeOnly time, bool use24Hour, JsonObject strings) =>
        TimeInput.Format(time, use24Hour, GetString(strings, "time12"), GetString(strings, "am"), GetString(strings, "pm"));

    private static bool GetUse24Hour()
    {
        var value = ApplicationData.Current.LocalSettings.Values[Use24HourKey];
        return value is bool enabled ? enabled : CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H');
    }

    private static bool GetAutoClockInOnUnlock() => ApplicationData.Current.LocalSettings.Values[AutoClockInEnabledKey] is bool enabled && enabled;

    private static TimeOnly GetAutoClockInAfter()
    {
        if (ApplicationData.Current.LocalSettings.Values[AutoClockInAfterKey] is string stored && TryParseClockIn(stored, out var parsed)) return parsed;
        return new TimeOnly(6, 0);
    }

    private static double GetDefaultWorkHours()
    {
        var value = ApplicationData.Current.LocalSettings.Values[WorkHoursKey];
        return value is double hours && Array.IndexOf(WorkHoursPresets, hours) >= 0 ? hours : 9.0;
    }

    private static string FormatHours(double hours) => hours.ToString("0.#", CultureInfo.InvariantCulture);

    private static bool TryParseClockIn(string value, out TimeOnly parsed) => TimeInput.TryParse(value, out parsed);

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool TryReadTime(JsonElement root, string name, out TimeOnly time) =>
        TimeInput.TryParse(ReadString(root, name), out time);

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
        var body = string.Format(CultureInfo.CurrentCulture, GetString(strings, "finishNotificationBody"),
            FormatClock(TimeOnly.FromDateTime(finish.DateTime), GetUse24Hour(), strings));
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
