using System;
using System.IO;

namespace CsConsoleWidgetProvider;

internal static class ProviderDiagnostics
{
    private static readonly object Sync = new();

    internal static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkdayWidget");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, "provider.log");
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never interrupt the widget provider.
        }
    }
}
