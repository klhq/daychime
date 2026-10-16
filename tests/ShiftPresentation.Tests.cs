using DaychimeWidget;

internal static class ShiftPresentationTests
{
    internal static void Run()
    {
        // Local wall-clock fixtures, as in the lifecycle tests.
        DateTimeOffset At(int day, int hour, int minute = 0, int second = 0) =>
            new(new DateTime(2026, 10, day, hour, minute, second, DateTimeKind.Unspecified));
        void Check(bool passed, string scenario)
        {
            if (!passed) throw new Exception($"Failed: {scenario}");
            Console.WriteLine($"Passed: {scenario}");
        }
        var dayStart = new TimeOnly(6, 0);

        Check(ShiftPresentation.GetPhase(default, At(16, 7), autoClockIn: false, dayStart) == WorkdayPhase.Idle,
            "no shift without auto clock-in is idle");
        Check(ShiftPresentation.GetPhase(default, At(16, 7), autoClockIn: true, dayStart) == WorkdayPhase.Missing,
            "an unrecorded cycle with auto clock-in asks for the start time");
        Check(ShiftPresentation.GetPhase(default, At(16, 5), autoClockIn: true, dayStart) == WorkdayPhase.Idle,
            "before the earliest auto clock-in time the card stays idle");

        var working = ShiftLifecycle.Start(default, At(16, 9), 9, dayStart);
        Check(ShiftPresentation.GetPhase(working, At(16, 14), autoClockIn: true, dayStart) == WorkdayPhase.Working,
            "a running shift is working");
        Check(ShiftPresentation.GetPhase(working, At(16, 18), autoClockIn: true, dayStart) == WorkdayPhase.Overtime,
            "the estimated finish itself counts as time to clock out");
        Check(ShiftPresentation.GetPhase(working, At(17, 7), autoClockIn: true, dayStart) == WorkdayPhase.Missing,
            "a stale shift in the next cycle asks for the new start");

        var clockedOut = ShiftLifecycle.ClockOut(working, At(16, 18, 40));
        Check(ShiftPresentation.GetPhase(clockedOut, At(16, 20), autoClockIn: true, dayStart) == WorkdayPhase.ClockedOut,
            "a clock-out stays the headline for the rest of the cycle");
        Check(ShiftPresentation.GetPhase(clockedOut, At(17, 5), autoClockIn: true, dayStart) == WorkdayPhase.ClockedOut,
            "an overnight clock-out stays until the next cycle starts");
        Check(ShiftPresentation.GetPhase(clockedOut, At(17, 7), autoClockIn: false, dayStart) == WorkdayPhase.Idle,
            "the next cycle returns to idle");

        var shift = working.Current!.Value;
        var nearlyThere = At(16, 14, 32, 30);
        Check(ShiftPresentation.FormatClock(ShiftPresentation.Worked(shift, nearlyThere)) == "5:32"
            && ShiftPresentation.FormatClock(ShiftPresentation.Left(shift, nearlyThere)) == "3:28",
            "worked time rounds down and time left rounds up");
        Check(ShiftPresentation.FormatClock(ShiftPresentation.Left(shift, At(16, 17, 59, 59))) == "0:01",
            "time left never reads 0:00 before the finish");
        Check(ShiftPresentation.FormatClock(ShiftPresentation.Over(shift, At(16, 18, 25, 59))) == "0:25"
            && ShiftPresentation.FormatClock(ShiftPresentation.Left(shift, At(16, 19))) == "0:00",
            "overtime rounds down and time left stops at zero");
        Check(ShiftPresentation.FormatClock(TimeSpan.FromHours(26)) == "26:00", "long durations keep total hours");

        Check(ShiftPresentation.GetProgress(shift, At(16, 8)) == 0 && ShiftPresentation.GetProgress(shift, At(16, 19)) == 1
            && Math.Abs(ShiftPresentation.GetProgress(shift, At(16, 13, 30)) - 0.5) < 1e-9,
            "progress is clamped to the planned workday");

        foreach (var (progress, complete, dark) in new[] { (0.0, false, false), (0.61, false, true), (1.0, true, false) })
        {
            var uri = ShiftPresentation.BuildProgressBar(progress, complete, dark);
            var png = Convert.FromBase64String(uri["data:image/png;base64,".Length..]);
            Check(uri.StartsWith("data:image/png;base64,", StringComparison.Ordinal)
                && png[1] == (byte)'P' && png[2] == (byte)'N' && png[3] == (byte)'G'
                && System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)) == ShiftPresentation.BarWidth
                && System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)) == ShiftPresentation.BarHeight
                && uri.Length < 4096,
                $"progress bar is a compact {ShiftPresentation.BarWidth}x{ShiftPresentation.BarHeight} PNG ({progress:P0}, dark: {dark})");
        }
    }
}
