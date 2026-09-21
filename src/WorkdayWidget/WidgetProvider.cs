// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CsConsoleWidgetProvider;
using Microsoft.Windows.Widgets.Providers;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

[ComVisible(true)]
[ComDefaultInterface(typeof(IWidgetProvider))]
[Guid("80DAE84C-DE3C-4F02-8ECD-DCDD1CDECC15")]
public sealed class WidgetProvider : IWidgetProvider
{
    public WidgetProvider()
    {
        RecoverRunningWidgets();
    }

    private static bool HaveRecoveredWidgets { get; set; } = false;
    private static void RecoverRunningWidgets()
    {
        if (HaveRecoveredWidgets)
            return;

        try
        {
            var widgetManager = WidgetManager.GetDefault();
            var widgetInfos = widgetManager.GetWidgetInfos();
            if (widgetInfos is null)
            {
                ProviderDiagnostics.Write("Recovery deferred because the Widgets host returned no widget list.");
                return;
            }

            foreach (var widgetInfo in widgetInfos)
            {
                try
                {
                    var context = widgetInfo.WidgetContext;
                    if (context is null)
                    {
                        ProviderDiagnostics.Write("Skipping a widget recovery entry with no context.");
                        continue;
                    }

                    ProviderDiagnostics.Write($"Recovering widget {context.Id} ({context.DefinitionId}).");
                    if (!WidgetInstances.ContainsKey(context.Id))
                    {
                        if (WidgetImpls.ContainsKey(context.DefinitionId))
                        {
                            // Need to recover this instance
                            var widgetInstance = WidgetImpls[context.DefinitionId](context.Id, widgetInfo.CustomState);
                            WidgetInstances[context.Id] = widgetInstance;
                            WidgetManager.GetDefault().UpdateWidget(new WidgetUpdateRequestOptions(context.Id)
                            {
                                Template = widgetInstance.GetTemplateForWidget(),
                                Data = widgetInstance.GetDataForWidget(),
                                CustomState = widgetInstance.State
                            });
                            ProviderDiagnostics.Write($"Recovered content sent for {context.Id}.");
                        }
                        else
                        {
                            // this provider doesn't know about this type of Widget (any more?) delete it
                            widgetManager.DeleteWidget(context.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ProviderDiagnostics.Write($"Recovery error for one widget: {ex}");
                }
            }

            HaveRecoveredWidgets = true;
        }
        catch (Exception ex)
        {
            ProviderDiagnostics.Write($"Recovery deferred: {ex}");
        }
    }

    private static readonly Dictionary<string, WidgetCreateDelegate> WidgetImpls = new() {
        [WorkdayWidget.DefinitionId] = (widgetId, initialState) => new WorkdayWidget(widgetId, initialState)
    };

    private static Dictionary<string, WidgetImplBase> WidgetInstances = new();

    // Handle the CreateWidget call. During this function call you should store
    // the WidgetId value so you can use it to update corresponding widget.
    // It is our way of notifying you that the user has pinned your widget
    // and you should start pushing updates.
    public void CreateWidget(WidgetContext widgetContext)
    {
        ProviderDiagnostics.Write($"CreateWidget {widgetContext.Id} ({widgetContext.DefinitionId}).");
        Console.WriteLine($"CreateWidget id: {widgetContext.Id} definitionId: {widgetContext.DefinitionId}");

        if (!WidgetImpls.ContainsKey(widgetContext.DefinitionId))
        {
            Console.WriteLine($"ERROR: Requested unknown Widget Definition ${widgetContext.DefinitionId}");
            throw new Exception($"Invalid definition requested: {widgetContext.DefinitionId}");
        }

        var widgetInstance = WidgetImpls[widgetContext.DefinitionId](widgetContext.Id, "");
        WidgetInstances[widgetContext.Id] = widgetInstance;

        WidgetUpdateRequestOptions options = new WidgetUpdateRequestOptions(widgetContext.Id);
        options.Template = widgetInstance.GetTemplateForWidget();
        options.Data = widgetInstance.GetDataForWidget();
        options.CustomState = widgetInstance.State;

        Console.WriteLine("Sending payload:");
        Console.WriteLine($"---Template---\n{options.Template}\n---\n");
        Console.WriteLine($"---Data---\n{options.Data}\n---\n");
        Console.WriteLine($"---Custom State---\n{options.CustomState}\n---\n");

        WidgetManager.GetDefault().UpdateWidget(options);
        ProviderDiagnostics.Write($"Initial update sent for {widgetContext.Id}.");
    }

    // Handle the DeleteWidget call. This is notifying you that
    // you don't need to provide new content for the given WidgetId
    // since the user has unpinned the widget or it was deleted by the Host
    // for any other reason.
    public void DeleteWidget(string widgetId, string _)
    {
        Console.WriteLine($"DeleteWidget id: {widgetId}");

        WidgetInstances.Remove(widgetId);

        var widgetIds = WidgetManager.GetDefault().GetWidgetIds();
        if (widgetIds != null)
        {
            Console.WriteLine($"  {widgetIds.Length.ToString()} Remaining Widgets:");
            foreach (var remainingWidgetId in widgetIds)
            {
                Console.WriteLine($"    {remainingWidgetId}");
            }
        }
        else if (widgetIds == null || widgetIds.Length == 0)
        {
            Console.WriteLine("  (no more Widgets outstanding)");
        }
    }

    // Handle the OnActionInvoked call. This function call is fired when the user's
    // interaction with the widget resulted in an execution of one of the defined
    // actions that you've indicated in the template of the Widget.
    // For example: clicking a button or submitting input.
    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        ProviderDiagnostics.Write($"Action {actionInvokedArgs.Verb} on {actionInvokedArgs.WidgetContext.Id}.");
        Console.WriteLine($"OnActionInvoked id: {actionInvokedArgs.WidgetContext.Id} definitionId: {actionInvokedArgs.WidgetContext.DefinitionId}");

        if (!WidgetInstances.TryGetValue(actionInvokedArgs.WidgetContext.Id, out var widget))
        {
            RecoverRunningWidgets();
            WidgetInstances.TryGetValue(actionInvokedArgs.WidgetContext.Id, out widget);
        }

        if (widget is null)
        {
            ProviderDiagnostics.Write($"Action ignored because widget {actionInvokedArgs.WidgetContext.Id} could not be recovered.");
            return;
        }

        widget.OnActionInvoked(actionInvokedArgs);
    }

    // Handle the WidgetContextChanged call. This function is called when the context a widget
    // has changed. Currently it only signals that the user has changed the size of the widget.
    // There are 2 ways to respond to this event:
    // 1) Call UpdateWidget() with the new data/template to fit the new requested size.
    // 2) If previously sent data/template accounts for various sizes based on $host.widgetSize - you can use this event solely for telemtry.
    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        Console.WriteLine($"OnWidgetContextChanged id: {contextChangedArgs.WidgetContext.Id} definitionId: {contextChangedArgs.WidgetContext.DefinitionId}");
        if (WidgetInstances.TryGetValue(contextChangedArgs.WidgetContext.Id, out var widget))
            widget.OnWidgetContextChanged(contextChangedArgs);
    }

    // Handle the Activate call. This function is called when widgets host starts listening
    // to the widget updates. It generally happens when the widget becomes visible and the updates
    // will be promptly displayed to the user. It's recommended to start sending updates from this moment
    // until Deactivate function was called.
    public void Activate(WidgetContext widgetContext)
    {
        Console.WriteLine($"Activate id: {widgetContext.Id} definitionId: {widgetContext.DefinitionId}");

        if (!WidgetInstances.TryGetValue(widgetContext.Id, out var widget))
        {
            RecoverRunningWidgets();
            WidgetInstances.TryGetValue(widgetContext.Id, out widget);
        }

        if (widget is null)
        {
            ProviderDiagnostics.Write($"Activate ignored because widget {widgetContext.Id} could not be recovered.");
            return;
        }

        widget.Activate(widgetContext);
        WidgetManager.GetDefault().UpdateWidget(new WidgetUpdateRequestOptions(widgetContext.Id)
        {
            Data = widget.GetDataForWidget(),
            CustomState = widget.State
        });
    }

    // Handle the Deactivate call. This function is called when widgets host stops listening
    // to the widget updates. It generally happens when the widget is not visible to the user
    // anymore and any further updates won't be displayed until the widget is visible again.
    // It's recommended to stop sending updates until Activate function was called.
    public void Deactivate(string widgetId)
    {
        Console.WriteLine($"Deactivate id: {widgetId}");
        if (WidgetInstances.TryGetValue(widgetId, out var widget))
            widget.Deactivate();
    }
}
