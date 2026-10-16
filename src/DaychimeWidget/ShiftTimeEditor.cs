using System;

namespace DaychimeWidget;

internal enum ShiftTimeEditError { None, Future, InvalidLocalTime }

internal static class ShiftTimeEditor
{
    internal static bool TryEdit(Shift original, TimeOnly time, DateTimeOffset now,
        TimeZoneInfo zone, out Shift revised, out ShiftTimeEditError error)
    {
        revised = original;
        error = ShiftTimeEditError.InvalidLocalTime;
        var localStart = TimeZoneInfo.ConvertTime(original.Start, zone);
        var local = DateTime.SpecifyKind(localStart.Date.Add(time.ToTimeSpan()), DateTimeKind.Unspecified);
        // Never move a nonexistent DST time or guess a different date.
        if (zone.IsInvalidTime(local)) return false;
        var offset = zone.GetUtcOffset(local);
        if (zone.IsAmbiguousTime(local))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(local);
            if (Array.IndexOf(offsets, localStart.Offset) < 0) return false;
            offset = localStart.Offset;
        }
        var candidate = new DateTimeOffset(local, offset);
        if (candidate > now)
        {
            error = ShiftTimeEditError.Future;
            return false;
        }
        revised = ShiftLifecycle.Revise(original with { Start = candidate });
        error = ShiftTimeEditError.None;
        return true;
    }
}
