using Microsoft.Windows.Widgets.Providers;
using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Windows.Storage;
using Windows.UI.Notifications;

namespace CsConsoleWidgetProvider;

internal sealed class WorkdayWidget : WidgetImplBase
{
    public static string DefinitionId => "Workday_Widget";
    private const string FinishReminderTag = "finish-reminder";
    private const string FinishReminderGroup = "workday";
    private const string EditingPrefix = "editing|";
    private const string EditingErrorPrefix = "editing-error|";
    private const string ConfirmClearPrefix = "confirm-clear|";
    public WorkdayWidget(string widgetId, string startingState) : base(widgetId, startingState) { }

    public override void OnActionInvoked(WidgetActionInvokedArgs args)
    {
        NormalizeStateForToday();
        switch (args.Verb)
        {
            case "clockIn": state = DateTimeOffset.Now.ToString("O"); break;
            case "edit":
                if (!string.IsNullOrEmpty(State) && !State.StartsWith(EditingPrefix))
                    state = EditingPrefix + State;
                break;
            case "cancelEdit": state = State.StartsWith(EditingPrefix) ? State[EditingPrefix.Length..] : State; break;
            case "toggleTimeFormat": ToggleUse24Hour(); break;
            case "toggleAutoClockIn": ToggleAutoClockInOnUnlock(); break;
            case "askClear": state = ConfirmClearPrefix + GetClockInState(State); break;
            case "cancelClear": state = State.StartsWith(ConfirmClearPrefix) ? State[ConfirmClearPrefix.Length..] : State; break;
            case "clear": state = string.Empty; break;
            case "saveTime":
                using (var data = JsonDocument.Parse(args.Data))
                {
                    if (data.RootElement.TryGetProperty("clockIn", out var time) &&
                        TryParseClockIn(time.GetString(), out var parsed))
                        state = new DateTimeOffset(DateTime.Today.Add(parsed.ToTimeSpan())).ToString("O");
                    else
                        state = EditingErrorPrefix + GetClockInState(State);
                }
                break;
        }
        UpdateFinishReminder();
        var update = new WidgetUpdateRequestOptions(Id) { Data = GetDataForWidget(), CustomState = State };
        WidgetManager.GetDefault().UpdateWidget(update);
    }

    public override string GetTemplateForWidget() => ReadPackageFileFromUri("ms-appx:///Templates/WorkdayWidgetTemplate.json");

    public override string GetDataForWidget()
    {
        NormalizeStateForToday();
        var strings = JsonNode.Parse(ReadPackageFileFromUri(GetStringsUri()))!.AsObject();
        var confirmingClear = State.StartsWith(ConfirmClearPrefix);
        var isEditing = State.StartsWith(EditingPrefix) || State.StartsWith(EditingErrorPrefix);
        var hasTimeError = State.StartsWith(EditingErrorPrefix);
        var effectiveState = GetClockInState(State);
        var clockedIn = DateTimeOffset.TryParse(effectiveState, out var start);
        var timezoneChanged = false;
        if (clockedIn)
        {
            var originalOffset = start.Offset;
            start = start.ToLocalTime();
            timezoneChanged = originalOffset != start.Offset;
        }
        var finish = clockedIn ? start.AddHours(9) : default;
        var use24Hour = GetUse24Hour();
        var timeFormat = use24Hour ? "HH:mm" : CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern;
        return new JsonObject {
            ["date"] = DateTime.Today.ToString("D", CultureInfo.CurrentCulture),
            ["clockedIn"] = clockedIn,
            ["canEdit"] = clockedIn && !confirmingClear && !isEditing,
            ["isEditing"] = clockedIn && isEditing,
            ["hasTimeError"] = clockedIn && hasTimeError,
            ["confirmingClear"] = confirmingClear,
            ["showFormatToggle"] = !isEditing && !confirmingClear,
            ["timezoneChanged"] = timezoneChanged,
            ["clockIn"] = clockedIn ? start.ToString(timeFormat, CultureInfo.CurrentCulture) : "--:--",
            ["clockInValue"] = clockedIn ? start.ToString("HH:mm") : "",
            ["finish"] = clockedIn ? finish.ToString(timeFormat, CultureInfo.CurrentCulture) : "--:--",
            ["now"] = DateTimeOffset.Now.ToString(timeFormat, CultureInfo.CurrentCulture),
            ["use24Hour"] = use24Hour,
            ["formatBadge"] = use24Hour ? "24h" : "12h",
            ["formatToggleTitle"] = use24Hour ? GetString(strings, "switchTo12Hour") : GetString(strings, "switchTo24Hour"),
            ["autoClockInStatus"] = GetAutoClockInOnUnlock() ? GetString(strings, "autoClockInOnLabel") : GetString(strings, "autoClockInOffLabel"),
            ["autoClockInToggleTitle"] = GetAutoClockInOnUnlock() ? GetString(strings, "turnOffAutoClockIn") : GetString(strings, "turnOnAutoClockIn"),
            ["clockInLabel"] = GetString(strings, "clockInLabel"),
            ["finishLabel"] = GetString(strings, "finishLabel"),
            ["clockInNow"] = GetString(strings, "clockInNow"),
            ["editTime"] = GetString(strings, "editTime"),
            ["clockInTimeLabel"] = GetString(strings, "clockInTimeLabel"),
            ["invalidTime"] = GetString(strings, "invalidTime"),
            ["saveChanges"] = GetString(strings, "saveChanges"),
            ["clear"] = GetString(strings, "clear"),
            ["clearLink"] = GetString(strings, "clearLink"),
            ["timezoneAdjustedNote"] = GetString(strings, "timezoneAdjustedNote"),
            ["confirmClear"] = GetString(strings, "confirmClear"),
            ["cancel"] = GetString(strings, "cancel"),
            ["breakSummary"] = GetString(strings, "breakSummary"),
            ["notClockedInYet"] = GetString(strings, "notClockedInYet"),
            ["clockInHint"] = GetString(strings, "clockInHint"),
            ["nowLabel"] = GetString(strings, "nowLabel"),
            ["cancelEdit"] = GetString(strings, "cancelEdit")
        }.ToJsonString();
    }

    private static string GetStringsUri()
    {
        var language = CultureInfo.CurrentUICulture.Name;
        if (language.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) ||
            language.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) ||
            language.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase))
            return "ms-appx:///Locales/TraditionalChinese.json";
        if (language.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase) ||
            language.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase) ||
            language.StartsWith("zh-SG", StringComparison.OrdinalIgnoreCase))
            return "ms-appx:///Locales/SimplifiedChinese.json";
        return "ms-appx:///Resources/Strings.en.json";
    }

    private static string GetString(JsonObject strings, string key) => strings[key]!.GetValue<string>();

    private static string GetClockInState(string currentState)
    {
        if (currentState.StartsWith(ConfirmClearPrefix))
            return currentState[ConfirmClearPrefix.Length..];
        if (currentState.StartsWith(EditingPrefix))
            return currentState[EditingPrefix.Length..];
        if (currentState.StartsWith(EditingErrorPrefix))
            return currentState[EditingErrorPrefix.Length..];
        return currentState;
    }

    private static bool GetUse24Hour()
    {
        var value = ApplicationData.Current.LocalSettings.Values["Use24Hour"];
        return value is bool enabled
            ? enabled
            : CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H');
    }

    private static void ToggleUse24Hour()
    {
        ApplicationData.Current.LocalSettings.Values["Use24Hour"] = !GetUse24Hour();
    }

    private static bool GetAutoClockInOnUnlock() =>
        ApplicationData.Current.LocalSettings.Values["AutoClockInOnUnlock"] is bool enabled && enabled;

    private static void ToggleAutoClockInOnUnlock()
    {
        ApplicationData.Current.LocalSettings.Values["AutoClockInOnUnlock"] = !GetAutoClockInOnUnlock();
    }

    public override void OnSessionUnlock()
    {
        NormalizeStateForToday();
        if (GetAutoClockInOnUnlock() && !DateTimeOffset.TryParse(GetClockInState(State), out _))
        {
            state = DateTimeOffset.Now.ToString("O");
            UpdateFinishReminder();
        }
    }

    private void NormalizeStateForToday()
    {
        if (DateTimeOffset.TryParse(GetClockInState(State), out var start) &&
            start.LocalDateTime.Date < DateTime.Today)
            state = string.Empty;
    }

    private static bool TryParseClockIn(string value, out TimeOnly parsed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = default;
            return false;
        }
        return TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed) ||
            TimeOnly.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed);
    }

    private void UpdateFinishReminder()
    {
        var notifier = ToastNotificationManager.CreateToastNotifier();
        foreach (var scheduled in notifier.GetScheduledToastNotifications())
        {
            if (scheduled.Tag == FinishReminderTag && scheduled.Group == FinishReminderGroup)
                notifier.RemoveFromSchedule(scheduled);
        }

        if (!DateTimeOffset.TryParse(GetClockInState(State), out var start))
            return;
        start = start.ToLocalTime();

        var finish = start.AddHours(9);
        if (finish <= DateTimeOffset.Now)
            return;

        var strings = JsonNode.Parse(ReadPackageFileFromUri(GetStringsUri()))!.AsObject();
        var toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
        var text = toastXml.GetElementsByTagName("text");
        text[0]!.InnerText = strings["finishNotificationTitle"]!.GetValue<string>();
        text[1]!.InnerText = string.Format(CultureInfo.CurrentCulture,
            strings["finishNotificationBody"]!.GetValue<string>(), finish.ToString(GetUse24Hour() ? "HH:mm" : CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern, CultureInfo.CurrentCulture));

        var reminder = new ScheduledToastNotification(toastXml, finish)
        {
            Tag = FinishReminderTag,
            Group = FinishReminderGroup
        };
        notifier.AddToSchedule(reminder);
    }
}
