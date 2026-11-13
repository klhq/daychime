#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Xml.Linq;

namespace DaychimeWidget;

internal static class SessionActivityEvidence
{
    internal static bool? ConnectionIsRemote(string xml, string account, int sessionId, out DateTimeOffset time)
    {
        time = default;
        try
        {
            var root = XElement.Parse(xml);
            XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
            var system = root.Element(ns + "System");
            if ((string?)system?.Element(ns + "Provider")?.Attribute("Name") != "Microsoft-Windows-TerminalServices-LocalSessionManager"
                || (int?)system?.Element(ns + "EventID") is not (21 or 25)) return null;
            string? Field(string name) => root.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
            if (!string.Equals(Field("User"), account, StringComparison.OrdinalIgnoreCase)
                || Field("SessionID") != sessionId.ToString(CultureInfo.InvariantCulture)
                || !DateTimeOffset.TryParse((string?)system?.Element(ns + "TimeCreated")?.Attribute("SystemTime"), out time)) return null;
            var address = Field("Address");
            if (IPAddress.TryParse(address, out var ip)) return !IPAddress.IsLoopback(ip);
            if (string.Equals(address, "LOCAL", StringComparison.OrdinalIgnoreCase) || address is "本機" or "本地") return false;
            // Unknown addresses must never make a remote session look local.
            return true;
        }
        catch (System.Xml.XmlException) { return null; }
    }

    internal static bool IsLocalAt(DateTimeOffset activity, IEnumerable<(DateTimeOffset Time, bool Remote)> connections) =>
        connections.Where(c => c.Time <= activity).OrderByDescending(c => c.Time).Select(c => (bool?)!c.Remote).FirstOrDefault() == true;
    internal static bool TryRead(string xml, string account, string sid, int sessionId, out DateTimeOffset time)
    {
        time = default;
        try
        {
            var root = XElement.Parse(xml);
            XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
            var system = root.Element(ns + "System");
            var provider = (string?)system?.Element(ns + "Provider")?.Attribute("Name");
            var eventId = (int?)system?.Element(ns + "EventID");
            string? Field(string name) => root.Descendants().FirstOrDefault(e =>
                e.Name.LocalName == name || (e.Name.LocalName == "Data" && (string?)e.Attribute("Name") == name))?.Value;
            var matches = provider == "Microsoft-Windows-TerminalServices-LocalSessionManager"
                && (eventId == 21 || eventId == 25)
                && string.Equals(Field("User"), account, StringComparison.OrdinalIgnoreCase)
                && Field("SessionID") == sessionId.ToString(CultureInfo.InvariantCulture);
            matches |= provider == "Microsoft-Windows-Security-Auditing" && eventId == 4801
                && Field("TargetUserSid") == sid
                && Field("SessionId") == sessionId.ToString(CultureInfo.InvariantCulture);
            // Winlogon logs one 811 per notification subscriber; Sens is always present.
            // Event 2 is logon and 5 is unlock (undocumented, verified against live unlocks).
            // Readable without elevation, but it carries only the SID, not the session ID.
            matches |= provider == "Microsoft-Windows-Winlogon" && eventId == 811
                && Field("SubscriberName") == "Sens"
                && (Field("Event") == "2" || Field("Event") == "5")
                && !string.IsNullOrEmpty(sid)
                && (string?)system?.Element(ns + "Security")?.Attribute("UserID") == sid;
            return matches && DateTimeOffset.TryParse((string?)system?.Element(ns + "TimeCreated")?.Attribute("SystemTime"),
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time);
        }
        catch (System.Xml.XmlException) { return false; }
        catch (FormatException) { return false; }
    }

    internal static DateTimeOffset? FindStart(ShiftSnapshot snapshot, IEnumerable<DateTimeOffset> activity,
        DateTimeOffset now, TimeOnly earliest, DateTimeOffset? enabledSince)
    {
        var cycle = ShiftLifecycle.GetCycle(now, earliest);
        foreach (var candidate in activity.OrderBy(t => t))
            if (candidate <= now && (!enabledSince.HasValue || candidate >= enabledSince.Value)
                && ShiftLifecycle.GetCycle(candidate, earliest) == cycle
                && ShiftLifecycle.CanAutoClockIn(snapshot, candidate, earliest)) return candidate;
        return null;
    }
}
