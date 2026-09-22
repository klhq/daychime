// Copyright (C) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Windows.Widgets.Providers;
using System;
using WidgetHelper;

namespace DaychimeWidget
{
    /// <summary>
    /// Main provider entrypoint.
    /// </summary>
    public static class Program
    {
        [MTAThread]
        static void Main(string[] args)
        {
            ProviderDiagnostics.Write($"Started: {string.Join(' ', args)}");
            Console.WriteLine("DaychimeWidget Starting...");
            if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
            {
                WinRT.ComWrappersSupport.InitializeComWrappers();
                using (var manager = RegistrationManager<WidgetProvider>.RegisterProvider())
                {
                    ProviderDiagnostics.Write("COM provider registered.");
                    Console.WriteLine("Widget Provider registered.");

                    RefreshExistingWidgets();

                    var existingWidgets = WidgetManager.GetDefault().GetWidgetIds();
                    if (existingWidgets != null)
                    {
                        ProviderDiagnostics.Write($"Existing widgets: {existingWidgets.Length}");
                        Console.WriteLine($"There are {existingWidgets.Length} Widgets currently outstanding:");
                        foreach (var widgetId in existingWidgets)
                        {
                            Console.WriteLine($"  {widgetId}");
                        }
                    }
                    // The provider must stay alive for the Widgets host; it has no user-facing window.
                    using (var disposedEvent = manager.GetDisposedEvent())
                    {
                        disposedEvent.WaitOne();
                    }
                }
            }
            else
            {
                ProviderDiagnostics.Write("Exited: no provider activation argument.");
                Console.WriteLine("Not being launched to service Widget Provider... exiting.");
            }
        }

    private static void RefreshExistingWidgets()
    {
        try
        {
            var widgetManager = WidgetManager.GetDefault();
            foreach (var widgetInfo in widgetManager.GetWidgetInfos() ?? [])
            {
                var context = widgetInfo.WidgetContext;
                if (context?.DefinitionId != Daychime.DefinitionId)
                    continue;

                var widget = new Daychime(context.Id, widgetInfo.CustomState);
                widgetManager.UpdateWidget(new WidgetUpdateRequestOptions(context.Id)
                {
                    Data = widget.GetDataForWidget(),
                    CustomState = widget.State
                });
                ProviderDiagnostics.Write($"Refreshed existing widget {context.Id}.");
            }
        }
        catch (Exception ex)
        {
            ProviderDiagnostics.Write($"Existing widget refresh failed: {ex}");
        }
    }

    }
}
