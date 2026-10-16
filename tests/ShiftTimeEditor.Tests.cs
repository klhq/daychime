using DaychimeWidget;

internal static class ShiftTimeEditorTests
{
    internal static void Run()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Edit tests +08", TimeSpan.FromHours(8), "Edit tests", "Edit tests");
        DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, zone.BaseUtcOffset);
        void Check(bool passed, string scenario)
        {
            if (!passed) throw new Exception($"Failed: {scenario}");
            Console.WriteLine($"Passed: {scenario}");
        }

        var night = ShiftLifecycle.Revise(new Shift(At(14, 19), 9));
        Check(ShiftTimeEditor.TryEdit(night, new(19, 30), At(15, 20), zone, out var edited, out _)
            && edited.Start == At(14, 19, 30) && edited.Finish == At(15, 4, 30),
            "editing yesterday's night shift after today's same time preserves its date and overnight finish");
        Check(edited.WorkHours == night.WorkHours && edited.ClockOut == night.ClockOut
            && edited.ReminderRevision != night.ReminderRevision,
            "time edit preserves shift settings but invalidates the old reminder");
        var snapshot = new ShiftSnapshot(edited, null, "2026-10-14");
        Check(ShiftLifecycle.ClockOutFromReminder(snapshot, ShiftLifecycle.GetReminderKey(night), At(15, 20)) == snapshot,
            "a reminder from before editing cannot clock out the edited shift");
        Check(ShiftTimeEditor.TryEdit(night, new(20, 0), At(15, 1), zone, out edited, out _)
            && edited.Start == At(14, 20), "editing after midnight keeps the original evening date");
        Check(ShiftTimeEditor.TryEdit(night, new(0, 0), At(15, 1), zone, out edited, out _)
            && edited.Start == At(14, 0), "midnight means midnight on the displayed date, not the next day");

        var today = ShiftLifecycle.Revise(new Shift(At(15, 9), 9));
        Check(!ShiftTimeEditor.TryEdit(today, new(19, 30), At(15, 10), zone, out edited, out var error)
            && error == ShiftTimeEditError.Future && edited == today,
            "future input is rejected without rolling back a day or changing the shift");
        Check(ShiftTimeEditor.TryEdit(today, new(10, 0), At(15, 10), zone, out edited, out _)
            && edited.Start == At(15, 10), "exactly now is a valid start");
        var utcEncoded = night with { Start = night.Start.ToUniversalTime() };
        Check(ShiftTimeEditor.TryEdit(utcEncoded, new(19, 30), At(15, 20), zone, out edited, out _)
            && edited.Start == At(14, 19, 30), "stored offset does not change the date displayed in the current timezone");

        // Custom DST rules keep these tests independent of the OS timezone database.
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 8),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1));
        var dstZone = TimeZoneInfo.CreateCustomTimeZone("Edit DST tests", TimeSpan.FromHours(-5), "DST tests", "Standard", "Daylight", new[] { rule });
        var spring = new Shift(new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.FromHours(-5)), 9);
        Check(!ShiftTimeEditor.TryEdit(spring, new(2, 30), spring.Start.AddHours(12), dstZone, out edited, out error)
            && error == ShiftTimeEditError.InvalidLocalTime && edited == spring,
            "nonexistent spring-forward times are rejected without shifting the date or hour");
        Check(ShiftTimeEditor.TryEdit(spring, new(3, 30), spring.Start.AddHours(12), dstZone, out edited, out _)
            && edited.Start.Offset == TimeSpan.FromHours(-4), "editing across a DST transition uses the edited time's offset");
        foreach (var offset in new[] { -4, -5 })
        {
            var autumn = new Shift(new DateTimeOffset(2026, 11, 1, 1, 15, 0, TimeSpan.FromHours(offset)), 9);
            Check(ShiftTimeEditor.TryEdit(autumn, new(1, 30), autumn.Start.AddHours(12), dstZone, out edited, out _)
                && edited.Start.Offset == autumn.Start.Offset,
                $"repeated autumn hour preserves the original occurrence ({offset})");
        }
    }
}
