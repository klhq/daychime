using Microsoft.Windows.Widgets.Providers;
using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CsConsoleWidgetProvider;

internal sealed class WorkdayWidget : WidgetImplBase
{
    public static string DefinitionId => "Workday_Widget";
    public WorkdayWidget(string widgetId, string startingState) : base(widgetId, startingState) { }

    public override void OnActionInvoked(WidgetActionInvokedArgs args)
    {
        switch (args.Verb)
        {
            case "clockIn": state = DateTimeOffset.Now.ToString("O"); break;
            case "askClear": state = "confirm-clear|" + State; break;
            case "cancelClear": state = State.StartsWith("confirm-clear|") ? State[14..] : State; break;
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
        var update = new WidgetUpdateRequestOptions(Id) { Data = GetDataForWidget(), CustomState = State };
        WidgetManager.GetDefault().UpdateWidget(update);
    }

    public override string GetTemplateForWidget() => ReadPackageFileFromUri("ms-appx:///Templates/WorkdayWidgetTemplate.json");

    public override string GetDataForWidget()
    {
        var strings = JsonNode.Parse(ReadPackageFileFromUri("ms-appx:///Resources/Strings.en.json"))!.AsObject();
        var confirmingClear = State.StartsWith("confirm-clear|");
        var effectiveState = confirmingClear ? State[14..] : State;
        var clockedIn = DateTimeOffset.TryParse(effectiveState, out var start);
        var finish = clockedIn ? start.AddHours(9) : default;
        return new JsonObject {
            ["date"] = DateTime.Today.ToString("dddd, MMMM d", CultureInfo.CurrentCulture),
            ["clockedIn"] = clockedIn,
            ["editable"] = clockedIn && !confirmingClear,
            ["confirmingClear"] = confirmingClear,
            ["clockIn"] = clockedIn ? start.ToString("HH:mm") : "--:--",
            ["finish"] = clockedIn ? finish.ToString("HH:mm") : "--:--",
            ["clockInLabel"] = strings["clockInLabel"],
            ["finishLabel"] = strings["finishLabel"],
            ["clockInNow"] = strings["clockInNow"],
            ["editTime"] = strings["editTime"],
            ["saveChanges"] = strings["saveChanges"],
            ["clear"] = strings["clear"],
            ["confirmClear"] = strings["confirmClear"],
            ["cancel"] = strings["cancel"],
            ["breakSummary"] = strings["breakSummary"]
        }.ToJsonString();
    }
}
