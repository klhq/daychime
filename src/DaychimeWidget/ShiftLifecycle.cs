#nullable enable

using System;
using System.Globalization;

namespace DaychimeWidget;

internal readonly record struct Shift(DateTimeOffset Start, double WorkHours, DateTimeOffset? ClockOut = null, string? ReminderRevision = null)
{
    public DateTimeOffset Finish => Start.AddHours(WorkHours);
}

internal readonly record struct ShiftSnapshot(Shift? Current, Shift? Previous, string? LastCycle);

internal static class ShiftLifecycle
{
    internal static readonly double[] WorkHoursPresets = { 8, 8.5, 9, 9.5, 10 };
    internal static string GetReminderKey(Shift shift) =>
        $"{shift.Start.UtcTicks}:{shift.WorkHours.ToString("R", CultureInfo.InvariantCulture)}:{shift.ReminderRevision}";

    internal static Shift Revise(Shift shift) => shift with { ReminderRevision = Guid.NewGuid().ToString("N") };

    internal static ShiftSnapshot ClockOutFromReminder(ShiftSnapshot snapshot, string reminderKey, DateTimeOffset now)
    {
        snapshot = Normalize(snapshot, now);
        return snapshot.Current is Shift current && GetReminderKey(current) == reminderKey
            ? ClockOut(snapshot, now) : snapshot;
    }

    internal static string GetCycle(DateTimeOffset now, TimeOnly earliest)
    {
        var local = now.LocalDateTime;
        var date = local.TimeOfDay < earliest.ToTimeSpan() ? local.Date.AddDays(-1) : local.Date;
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    internal static bool CanAutoClockIn(ShiftSnapshot snapshot, DateTimeOffset now, TimeOnly earliest)
    {
        if (TimeOnly.FromDateTime(now.LocalDateTime) < earliest) return false;
        if (snapshot.LastCycle == GetCycle(now, earliest)) return false;
        return snapshot.Current is not Shift current || current.Finish <= now;
    }

    internal static ShiftSnapshot Start(ShiftSnapshot snapshot, DateTimeOffset now, double workHours, TimeOnly earliest) =>
        snapshot with {
            Current = Revise(new Shift(now, workHours)),
            Previous = snapshot.Current ?? snapshot.Previous,
            LastCycle = GetCycle(now, earliest)
        };

    internal static ShiftSnapshot ClockOut(ShiftSnapshot snapshot, DateTimeOffset now) =>
        snapshot.Current is Shift current && now >= current.Start
            ? snapshot with { Current = null, Previous = current with { ClockOut = now } }
            : snapshot;

    internal static ShiftSnapshot UndoClockOut(ShiftSnapshot snapshot) =>
        snapshot.Current is null && snapshot.Previous is Shift { ClockOut: not null } previous
            ? snapshot with { Current = Revise(previous with { ClockOut = null }), Previous = null }
            : snapshot;

    internal static ShiftSnapshot Normalize(ShiftSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.Current is Shift current && current.Finish.AddHours(24) <= now)
            snapshot = snapshot with { Current = null, Previous = current };
        if (snapshot.Previous is Shift previous && (previous.ClockOut ?? previous.Finish).AddHours(24) <= now)
            snapshot = snapshot with { Previous = null };
        return snapshot;
    }
}
