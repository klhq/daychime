// Copyright (C) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Windows.Widgets.Providers;
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using WidgetHelper;

namespace DaychimeWidget
{
    /// <summary>
    /// Main provider entrypoint.
    /// </summary>
    public static class Program
    {
        private const uint MessageBoxInformation = 0x00000040;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);

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
                ProviderDiagnostics.Write("Launched directly; showing Widgets Board instructions.");
                MessageBox(
                    IntPtr.Zero,
                    GetDirectLaunchMessage(),
                    GetDirectLaunchTitle(),
                    MessageBoxInformation);
            }
        }

        private static string GetDirectLaunchTitle() => IsTraditionalChinese() ? "Daychime 已準備完成" : IsSimplifiedChinese() ? "Daychime 已准备就绪" : "Daychime is ready";

        private static string GetDirectLaunchMessage() => IsTraditionalChinese()
            ? "Daychime 位於 Windows 小工具。\n\n按 Win + W，選取「新增小工具」，然後加入 Daychime。"
            : IsSimplifiedChinese()
                ? "Daychime 位于 Windows 小组件。\n\n按 Win + W，选择“添加小组件”，然后添加 Daychime。"
                : "Daychime lives in the Windows Widgets Board.\n\nPress Win + W, select Add widgets, then add Daychime.";

        private static bool IsTraditionalChinese() => CultureInfo.CurrentUICulture.Name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) || CultureInfo.CurrentUICulture.Name.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) || CultureInfo.CurrentUICulture.Name.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase);

        private static bool IsSimplifiedChinese() => CultureInfo.CurrentUICulture.Name.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase) || CultureInfo.CurrentUICulture.Name.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase) || CultureInfo.CurrentUICulture.Name.StartsWith("zh-SG", StringComparison.OrdinalIgnoreCase);

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
