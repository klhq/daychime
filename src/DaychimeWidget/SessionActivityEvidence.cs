#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace DaychimeWidget;

internal static class SessionActivityEvidence
{
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
