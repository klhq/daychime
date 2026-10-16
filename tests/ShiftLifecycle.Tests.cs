using DaychimeWidget;

// Work cycles use the machine's local wall clock. Keep fixtures local too, so
// the same scenarios run on UTC CI workers and developer machines in any zone.
static DateTimeOffset At(int day, int hour) => new(new DateTime(2026, 9, day, hour, 0, 0, DateTimeKind.Unspecified));
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
var recoveryNow = At(29, 17).AddMinutes(31);
var morning = At(29, 9).AddMinutes(6);
Check(SessionActivityEvidence.FindStart(default, new[] { morning, At(29, 13) }, recoveryNow, dayStart, null) == morning,
    "opening the board late recovers the first morning activity rather than the opening time");
Check(SessionActivityEvidence.FindStart(default, Array.Empty<DateTimeOffset>(), recoveryNow, dayStart, null) is null,
    "missing history cannot fabricate an afternoon clock-in");
Check(SessionActivityEvidence.FindStart(default, new[] { At(28, 9), recoveryNow.AddMinutes(1) }, recoveryNow, dayStart, null) is null,
    "previous-cycle and future activity cannot become today's clock-in");
Check(SessionActivityEvidence.FindStart(default, new[] { morning }, recoveryNow, dayStart, At(29, 10)) is null,
    "enabling auto clock-in midday does not backfill earlier activity");
var handled = ShiftLifecycle.Start(default, morning, 9, dayStart);
Check(SessionActivityEvidence.FindStart(handled, new[] { morning, recoveryNow }, recoveryNow, dayStart, null) is null,
    "session recovery cannot overwrite a handled work cycle");
Check(SessionActivityEvidence.FindStart(night, new[] { At(29, 1) }, At(29, 3), nightStart, null) is null,
    "a live overnight shift cannot be replaced by recovered activity");
Check(SessionActivityEvidence.FindStart(default, new[] { At(28, 19) }, At(29, 2), nightStart, null) == At(28, 19),
    "opening after midnight can recover the current night-shift cycle");
var eventXml = "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Microsoft-Windows-TerminalServices-LocalSessionManager'/><EventID>21</EventID><TimeCreated SystemTime='2026-10-15T01:06:47Z'/></System><UserData><EventXML xmlns='Event_NS'><User>PC\\Worker</User><SessionID>1</SessionID></EventXML></UserData></Event>";
Check(SessionActivityEvidence.TryRead(eventXml, "PC\\Worker", "S-1-5-21-123", 1, out var logonTime)
    && logonTime == DateTimeOffset.Parse("2026-10-15T01:06:47Z"), "session logon parser preserves the actual timestamp");
Check(!SessionActivityEvidence.TryRead(eventXml, "PC\\Other", "S-1-5-21-123", 1, out _)
    && !SessionActivityEvidence.TryRead(eventXml, "PC\\Worker", "S-1-5-21-123", 2, out _), "other users and other Windows sessions are excluded");
var unlockXml = "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Microsoft-Windows-Security-Auditing'/><EventID>4801</EventID><TimeCreated SystemTime='2026-10-15T01:07:00Z'/></System><EventData><Data Name='TargetUserSid'>S-1-5-21-123</Data><Data Name='SessionId'>1</Data></EventData></Event>";
Check(SessionActivityEvidence.TryRead(unlockXml, "PC\\Worker", "S-1-5-21-123", 1, out _)
    && !SessionActivityEvidence.TryRead(unlockXml, "PC\\Worker", "S-1-5-21-456", 1, out _), "unlock parser requires the correct account SID");
Check(!SessionActivityEvidence.TryRead("invalid XML", "PC\\Worker", "sid", 1, out _), "malformed session history is ignored");
TimeInputTests.Run();
ShiftTimeEditorTests.Run();
Console.WriteLine("All shift lifecycle and time-input scenarios passed.");
