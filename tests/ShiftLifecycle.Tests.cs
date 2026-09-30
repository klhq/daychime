using DaychimeWidget;

static DateTimeOffset At(int day, int hour) => new(2026, 9, day, hour, 0, 0, TimeSpan.FromHours(8));
static void Check(bool result, string scenario)
{
    if (!result) throw new Exception($"Failed: {scenario}");
    Console.WriteLine($"Passed: {scenario}");
}

var nightStart = new TimeOnly(18, 0);
var night = ShiftLifecycle.Start(default, At(28, 19), 9, nightStart);
Check(!ShiftLifecycle.CanAutoClockIn(night, At(29, 3), nightStart), "overnight shift remains active across midnight");
Check(ShiftLifecycle.CanAutoClockIn(night, At(29, 19), nightStart), "next evening can auto clock in after a missed clock-out");

var secondNight = ShiftLifecycle.Start(night, At(29, 19), 9, nightStart);
Check(secondNight.Current?.Start == At(29, 19), "new shift uses the actual observed time");
Check(secondNight.Previous?.Start == At(28, 19) && secondNight.Previous?.ClockOut is null, "previous shift is retained without a fabricated clock-out");
Check(!ShiftLifecycle.CanAutoClockIn(secondNight, At(29, 20), nightStart), "repeat unlock in the same cycle does not double clock in");

var finished = ShiftLifecycle.ClockOut(night, At(29, 5));
Check(finished.Current is null && finished.Previous?.ClockOut == At(29, 5), "clock-out saves the actual time");
Check(ShiftLifecycle.CanAutoClockIn(finished, At(29, 19), nightStart), "clock-out does not block the next cycle");
Check(ShiftLifecycle.UndoClockOut(finished).Current?.ClockOut is null, "an accidental clock-out can be undone");
Check(ShiftLifecycle.Normalize(finished, At(30, 5)).Previous is null, "recent shift expires 24 hours after actual clock-out");

var dayStart = new TimeOnly(6, 0);
var stale = ShiftLifecycle.Start(default, At(24, 9), 9, dayStart);
Check(ShiftLifecycle.Normalize(stale, At(29, 9)).Current is null, "last week's unfinished shift is cleared when reopened");

var overlap = ShiftLifecycle.Start(default, At(28, 23), 10, dayStart);
Check(!ShiftLifecycle.CanAutoClockIn(overlap, At(29, 6), dayStart), "an active overnight shift defers the next cycle");
Check(ShiftLifecycle.CanAutoClockIn(overlap, At(29, 10), dayStart), "a later unlock in that cycle can start after the shift ends");

var key = ShiftLifecycle.GetReminderKey(night.Current!.Value);
var fromToast = ShiftLifecycle.ClockOutFromReminder(night, key, At(29, 6));
Check(fromToast.Previous?.ClockOut == At(29, 6), "notification records click time rather than estimated finish");
Check(ShiftLifecycle.ClockOutFromReminder(fromToast, key, At(29, 7)) == fromToast, "duplicate notification click preserves original clock-out");
Check(ShiftLifecycle.ClockOutFromReminder(secondNight, key, At(29, 20)) == secondNight, "old notification cannot clock out the next shift");
Check(ShiftLifecycle.ClockOutFromReminder(night, "invalid", At(29, 6)) == night, "invalid notification does not change the shift");
Check(ShiftLifecycle.ClockOutFromReminder(night, key, At(30, 5)) == ShiftLifecycle.Normalize(night, At(30, 5)), "expired shift cannot be clocked out from an old notification");
var edited = night with { Current = night.Current.Value with { WorkHours = 8 } };
Check(ShiftLifecycle.ClockOutFromReminder(edited, key, At(29, 6)) == edited, "notification from before a shift edit is ignored");
var undone = ShiftLifecycle.UndoClockOut(fromToast);
Check(ShiftLifecycle.ClockOutFromReminder(undone, key, At(29, 7)) == undone, "a delayed repeat click cannot reverse an undo");
var changedBack = night with { Current = ShiftLifecycle.Revise(ShiftLifecycle.Revise(night.Current.Value with { WorkHours = 8 }) with { WorkHours = 9 }) };
Check(ShiftLifecycle.ClockOutFromReminder(changedBack, key, At(29, 6)) == changedBack, "changing work hours back does not revive an old notification");
var restarted = ShiftLifecycle.Start(default, night.Current.Value.Start, 9, nightStart);
Check(ShiftLifecycle.ClockOutFromReminder(restarted, key, At(29, 6)) == restarted, "a new shift with identical times has a distinct notification identity");
Check(ShiftLifecycle.ClockOutFromReminder(night, key, At(28, 18)) == night, "clock rollback before clock-in cannot create negative elapsed time");
var shiftedZone = night with { Current = night.Current.Value with { Start = night.Current.Value.Start.ToOffset(TimeSpan.FromHours(-5)) } };
Check(ShiftLifecycle.GetReminderKey(shiftedZone.Current.Value) == key, "timezone offset change does not invalidate the same instant");
Check(ShiftLifecycle.ClockOutFromReminder(night, key, night.Current.Value.Finish.AddHours(24)) == ShiftLifecycle.Normalize(night, night.Current.Value.Finish.AddHours(24)), "exact expiry boundary rejects clock-out");
Check(ShiftStateCodec.TryRead(ShiftStateCodec.Serialize(night.Current), out var restored) && restored == night.Current, "restart preserves notification revision and precise start time");
Check(ShiftStateCodec.TryRead(ShiftStateCodec.Serialize(fromToast.Previous), out var restoredFinish) && restoredFinish == fromToast.Previous, "restart preserves actual clock-out and revision");
Check(ShiftStateCodec.TryRead($"{At(28, 19):O};9;", out var legacy) && legacy?.Start == At(28, 19) && legacy?.ReminderRevision is null, "old three-field shift state still loads");
Check(ShiftStateCodec.TryRead(ShiftStateCodec.Serialize(legacy), out var migrated) && migrated == legacy, "legacy state migrates without changing the shift");
Check(!ShiftStateCodec.TryRead($"{At(28, 19):O};9;;malformed", out _), "invalid persisted revision is rejected");
Check(!ShiftStateCodec.TryRead($"{At(28, 19):O};NaN;", out _), "invalid persisted work hours are rejected");
Console.WriteLine("All shift lifecycle scenarios passed.");
