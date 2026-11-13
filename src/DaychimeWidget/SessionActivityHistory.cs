using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Security.Principal;
using System.Runtime.InteropServices;

namespace DaychimeWidget;

internal static class SessionActivityHistory
{
    // Read only the current work cycle. Log access is optional; never request elevation.
    internal static List<DateTimeOffset> Read(DateTimeOffset now, TimeOnly earliest, bool includeRemote)
    {
        var start = now.LocalDateTime.Date.Add(earliest.ToTimeSpan());
        if (start > now.LocalDateTime) start = start.AddDays(-1);
        var fromUtc = new DateTimeOffset(start).UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        var toUtc = now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        using var identity = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();
        var sessionId = process.SessionId;
        var result = new List<DateTimeOffset>();
        var connections = new List<(DateTimeOffset Time, bool Remote)>();
        if (!includeRemote) ReadConnections();
        ReadLog("Microsoft-Windows-TerminalServices-LocalSessionManager/Operational", "EventID=21 or EventID=25");
        ReadLog("Security", "EventID=4801");
        ReadLog("Microsoft-Windows-Winlogon/Operational", "EventID=811", " and *[EventData[Data[@Name='SubscriberName']='Sens']]");
        return includeRemote ? result : result.FindAll(t => SessionActivityEvidence.IsLocalAt(t, connections));

        void ReadConnections()
        {
            try
            {
                var since = now.AddDays(-30).UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
                var query = new EventLogQuery("Microsoft-Windows-TerminalServices-LocalSessionManager/Operational", PathType.LogName,
                    $"*[System[(EventID=21 or EventID=25) and TimeCreated[@SystemTime >= '{since}' and @SystemTime <= '{toUtc}']]]");
                using var reader = new EventLogReader(query);
                while (reader.ReadEvent() is EventRecord entry)
                {
                    using (entry)
                    {
                        var remote = SessionActivityEvidence.ConnectionIsRemote(entry.ToXml(), identity.Name, sessionId, out var time);
                        if (remote.HasValue) connections.Add((time, remote.Value));
                    }
                }
            }
            catch (Exception ex) when (ex is EventLogException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                ProviderDiagnostics.Write($"Session connection history unavailable: {ex.Message}");
            }
        }

        void ReadLog(string channel, string ids, string dataFilter = "")
        {
            try
            {
                var query = new EventLogQuery(channel, PathType.LogName,
                    $"*[System[({ids}) and TimeCreated[@SystemTime >= '{fromUtc}' and @SystemTime <= '{toUtc}']]]{dataFilter}") {
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

    // WTSClientProtocolType: 0 is console, 2 is RDP. Failure is unknown, not local.
    internal static bool IsCurrentSessionLocal()
    {
        using var process = Process.GetCurrentProcess();
        if (!WTSQuerySessionInformation(IntPtr.Zero, process.SessionId, 16, out var buffer, out var bytes)) return false;
        try { return bytes >= 2 && Marshal.ReadInt16(buffer) == 0; }
        finally { WTSFreeMemory(buffer); }
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);
    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr buffer);
}
