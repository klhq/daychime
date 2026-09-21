using Microsoft.Windows.Widgets.Providers;
using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Windows.UI.Notifications;

namespace CsConsoleWidgetProvider;

internal sealed class WorkdayWidget : WidgetImplBase
{
    public static string DefinitionId => "Workday_Widget";
    private const string FinishReminderTag = "finish-reminder";
    private const string FinishReminderGroup = "workday";
    private const string EditingPrefix = "editing|";
    private const string ConfirmClearPrefix = "confirm-clear|";
    public WorkdayWidget(string widgetId, string startingState) : base(widgetId, startingState) { }

    public override void OnActionInvoked(WidgetActionInvokedArgs args)
    {
        switch (args.Verb)
        {
            case "clockIn": state = DateTimeOffset.Now.ToString("O"); break;
            case "edit":
                if (!string.IsNullOrEmpty(State) && !State.StartsWith(EditingPrefix))
                    state = EditingPrefix + State;
                break;
            case "cancelEdit": state = State.StartsWith(EditingPrefix) ? State[EditingPrefix.Length..] : State; break;
            case "askClear": state = ConfirmClearPrefix + GetClockInState(State); break;
            case "cancelClear": state = State.StartsWith(ConfirmClearPrefix) ? State[ConfirmClearPrefix.Length..] : State; break;
            case "clear": state = string.Empty; break;
            case "saveTime":
                using (var data = JsonDocument.Parse(args.Data))
                {
                    if (data.RootElement.TryGetProperty("clockIn", out var time) &&
                        TimeOnly.TryParseExact(time.GetString(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                        state = new DateTimeOffset(DateTime.Today.Add(parsed.ToTimeSpan())).ToString("O");
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
        var strings = JsonNode.Parse(ReadPackageFileFromUri(GetStringsUri()))!.AsObject();
        var confirmingClear = State.StartsWith(ConfirmClearPrefix);
        var isEditing = State.StartsWith(EditingPrefix);
        var effectiveState = GetClockInState(State);
        var clockedIn = DateTimeOffset.TryParse(effectiveState, out var start);
        var finish = clockedIn ? start.AddHours(9) : default;
        return new JsonObject {
            ["date"] = DateTime.Today.ToString("dddd, MMMM d", CultureInfo.CurrentCulture),
            ["clockedIn"] = clockedIn,
            ["canEdit"] = clockedIn && !confirmingClear && !isEditing,
            ["isEditing"] = clockedIn && isEditing,
            ["confirmingClear"] = confirmingClear,
            ["clockIn"] = clockedIn ? start.ToString("HH:mm") : "--:--",
            ["finish"] = clockedIn ? finish.ToString("HH:mm") : "--:--",
            ["now"] = DateTimeOffset.Now.ToString("HH:mm"),
            ["clockInLabel"] = GetString(strings, "clockInLabel"),
            ["finishLabel"] = GetString(strings, "finishLabel"),
            ["clockInNow"] = GetString(strings, "clockInNow"),
            ["editTime"] = GetString(strings, "editTime"),
            ["saveChanges"] = GetString(strings, "saveChanges"),
            ["clear"] = GetString(strings, "clear"),
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
        return currentState;
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

        var finish = start.AddHours(9);
        if (finish <= DateTimeOffset.Now)
            return;

        var strings = JsonNode.Parse(ReadPackageFileFromUri(GetStringsUri()))!.AsObject();
        var toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
        var text = toastXml.GetElementsByTagName("text");
        text[0]!.InnerText = strings["finishNotificationTitle"]!.GetValue<string>();
        text[1]!.InnerText = string.Format(CultureInfo.CurrentCulture,
            strings["finishNotificationBody"]!.GetValue<string>(), finish.ToString("HH:mm"));

        var reminder = new ScheduledToastNotification(toastXml, finish)
        {
            Tag = FinishReminderTag,
            Group = FinishReminderGroup
        };
        notifier.AddToSchedule(reminder);
    }
}
