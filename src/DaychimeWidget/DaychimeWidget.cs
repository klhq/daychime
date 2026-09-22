using Microsoft.Windows.Widgets.Providers;
using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Windows.Storage;
using Windows.UI.Notifications;

namespace DaychimeWidget;

internal sealed class Daychime : WidgetImplBase
{
    public static string DefinitionId => "Daychime_Widget";
    private const string FinishReminderTag = "finish-reminder";
    private const string FinishReminderGroup = "workday";
    private const string ShiftPrefix = "shift|";
    private const string EditingPrefix = "editing|";
    private const string EditingErrorPrefix = "editing-error|";
    private const string ConfirmClearPrefix = "confirm-clear|";
    private const string AutoSettingsPrefix = "auto-settings|";
    private const string AutoClockInEnabledKey = "AutoClockInOnUnlock";
    private const string AutoClockInAfterKey = "AutoClockInAfter";
    private const string LastAutoClockInCycleKey = "LastAutoClockInCycle";
    private static readonly double[] WorkHoursPresets = { 8, 8.5, 9, 9.5, 10 };

    private readonly record struct Shift(DateTimeOffset Start, double WorkHours)
    {
        public DateTimeOffset Finish => Start.AddHours(WorkHours);
    }

    public Daychime(string widgetId, string startingState) : base(widgetId, startingState) { }

    public override void OnActionInvoked(WidgetActionInvokedArgs args)
    {
        switch (args.Verb)
        {
            case "clockIn": StartShift(GetCurrentTime()); break;
            case "edit":
                if (TryGetShift(State, out _) && !State.StartsWith(EditingPrefix, StringComparison.Ordinal))
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
            case "clear": state = string.Empty; break;
            case "saveTime": SaveClockInTime(args.Data); break;
        }
        TryUpdateFinishReminder();
        UpdateWidget();
    }

    public override string GetTemplateForWidget() => ReadPackageFileFromUri("ms-appx:///Templates/DaychimeTemplate.json");

    public override string GetDataForWidget()
    {
        var strings = JsonNode.Parse(ReadPackageFileFromUri(GetStringsUri()))!.AsObject();
        var confirmingClear = State.StartsWith(ConfirmClearPrefix, StringComparison.Ordinal);
        var isEditing = State.StartsWith(EditingPrefix, StringComparison.Ordinal) || State.StartsWith(EditingErrorPrefix, StringComparison.Ordinal);
        var hasTimeError = State.StartsWith(EditingErrorPrefix, StringComparison.Ordinal);
        var isAutoSettings = State.StartsWith(AutoSettingsPrefix, StringComparison.Ordinal);
        var clockedIn = TryGetShift(State, out var shift);
        var start = clockedIn ? shift.Start.ToLocalTime() : default;
        var finish = clockedIn ? shift.Finish.ToLocalTime() : default;
        var now = GetCurrentTime();
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
            ["finishLabel"] = GetString(strings, isWorkdayComplete ? "finishedAtLabel" : "finishLabel"),
            ["workdayComplete"] = GetString(strings, "workdayComplete"),
            ["endsTomorrow"] = GetString(strings, "endsTomorrow"),
            ["clockInActionTitle"] = GetString(strings, isWorkdayComplete ? "startNewWorkday" : "clockInNow"),
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

    public override void OnSessionUnlock()
    {
        if (!GetAutoClockInOnUnlock()) return;
        var now = GetCurrentTime();
        var earliest = GetAutoClockInAfter();
        if (TimeOnly.FromDateTime(now.LocalDateTime) < earliest) return;
        var cycle = GetAutoClockInCycle(now, earliest);
        if (ApplicationData.Current.LocalSettings.Values[LastAutoClockInCycleKey] is string lastCycle && lastCycle == cycle) return;
        if (TryGetShift(State, out var activeShift) && activeShift.Finish > now)
        {
            ApplicationData.Current.LocalSettings.Values[LastAutoClockInCycleKey] = cycle;
            return;
        }
        StartShift(now);
        ApplicationData.Current.LocalSettings.Values[LastAutoClockInCycleKey] = cycle;
        TryUpdateFinishReminder();
    }

    private void StartShift(DateTimeOffset startedAt)
    {
        state = SerializeShift(new Shift(startedAt, GetDefaultWorkHours()));
        ApplicationData.Current.LocalSettings.Values[LastAutoClockInCycleKey] = GetAutoClockInCycle(startedAt, GetAutoClockInAfter());
    }

    private void SaveClockInTime(string data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            if (document.RootElement.TryGetProperty("clockIn", out var time) && TryParseClockIn(time.GetString(), out var parsed))
            {
                var workHours = TryGetShift(State, out var existing) ? existing.WorkHours : GetDefaultWorkHours();
                state = SerializeShift(new Shift(GetMostRecentOccurrence(parsed, GetCurrentTime()), workHours));
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
        var current = TryGetShift(State, out var shift) ? shift.WorkHours : GetDefaultWorkHours();
        var next = WorkHoursPresets[(Array.IndexOf(WorkHoursPresets, current) + 1) % WorkHoursPresets.Length];
        ApplicationData.Current.LocalSettings.Values["WorkHours"] = next;
        if (TryGetShift(State, out shift)) state = SerializeShift(new Shift(shift.Start, next));
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
#if DEBUG
        return new DateTimeOffset(DateTime.Today.AddHours(18));
#else
        return DateTimeOffset.Now;
#endif
    }

    private static string GetBaseState(string currentState)
    {
        foreach (var prefix in new[] { ConfirmClearPrefix, EditingPrefix, EditingErrorPrefix, AutoSettingsPrefix })
            if (currentState.StartsWith(prefix, StringComparison.Ordinal)) return currentState[prefix.Length..];
        return currentState;
    }

    private static bool TryGetShift(string currentState, out Shift shift)
    {
        var raw = GetBaseState(currentState);
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

    private static string SerializeShift(Shift shift) => $"{ShiftPrefix}{shift.Start:O}|{shift.WorkHours.ToString("0.#", CultureInfo.InvariantCulture)}";

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

    private static string GetAutoClockInCycle(DateTimeOffset now, TimeOnly earliest)
    {
        var local = now.LocalDateTime;
        var date = local.TimeOfDay < earliest.ToTimeSpan() ? local.Date.AddDays(-1) : local.Date;
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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
        foreach (var scheduled in notifier.GetScheduledToastNotifications())
            if (scheduled.Tag == FinishReminderTag && scheduled.Group == FinishReminderGroup) notifier.RemoveFromSchedule(scheduled);
        if (!TryGetShift(State, out var shift)) return;
        var finish = shift.Finish.ToLocalTime();
        if (finish <= GetCurrentTime()) return;
        var strings = JsonNode.Parse(ReadPackageFileFromUri(GetStringsUri()))!.AsObject();
        var toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
        var text = toastXml.GetElementsByTagName("text");
        text[0]!.InnerText = strings["finishNotificationTitle"]!.GetValue<string>();
        text[1]!.InnerText = string.Format(CultureInfo.CurrentCulture, strings["finishNotificationBody"]!.GetValue<string>(), finish.ToString(GetUse24Hour() ? "HH:mm" : CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern, CultureInfo.CurrentCulture));
        notifier.AddToSchedule(new ScheduledToastNotification(toastXml, finish) { Tag = FinishReminderTag, Group = FinishReminderGroup });
    }
}
