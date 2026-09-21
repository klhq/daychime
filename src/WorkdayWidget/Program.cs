// Copyright (C) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Windows.Widgets.Providers;
using System;
using WidgetHelper;

namespace CsConsoleWidgetProvider
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
            Console.WriteLine("CsConsoleWidgetProvider Starting...");
            if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
            {
                WinRT.ComWrappersSupport.InitializeComWrappers();
                using (var manager = RegistrationManager<WidgetProvider>.RegisterProvider())
                {
                    ProviderDiagnostics.Write("COM provider registered.");
                    Console.WriteLine("Widget Provider registered.");

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
    }
}
