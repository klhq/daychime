using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Security.Principal;

namespace DaychimeWidget;

internal static class SessionActivityHistory
{
    // Read only the current work cycle. Log access is optional; never request elevation.
    internal static List<DateTimeOffset> Read(DateTimeOffset now, TimeOnly earliest)
    {
        var start = now.LocalDateTime.Date.Add(earliest.ToTimeSpan());
        if (start > now.LocalDateTime) start = start.AddDays(-1);
        var fromUtc = new DateTimeOffset(start).UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        var toUtc = now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        using var identity = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();
        var sessionId = process.SessionId;
        var result = new List<DateTimeOffset>();
        ReadLog("Microsoft-Windows-TerminalServices-LocalSessionManager/Operational", "EventID=21 or EventID=25");
        ReadLog("Security", "EventID=4801");
        return result;

        void ReadLog(string channel, string ids)
        {
            try
            {
                var query = new EventLogQuery(channel, PathType.LogName,
                    $"*[System[({ids}) and TimeCreated[@SystemTime >= '{fromUtc}' and @SystemTime <= '{toUtc}']]]") {
                    ReverseDirection = false
                };
                using var reader = new EventLogReader(query);
                for (var count = 0; count < 2048; count++)
                {
                    using var entry = reader.ReadEvent(TimeSpan.FromSeconds(1));
                    if (entry is null) break;
                    if (SessionActivityEvidence.TryRead(entry.ToXml(), identity.Name, identity.User?.Value ?? "", sessionId, out var time))
                        result.Add(time);
                }
            }
            catch (Exception ex) when (ex is EventLogException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                ProviderDiagnostics.Write($"Session history unavailable ({channel}): {ex.Message}");
            }
        }
    }
}
