using System.Globalization;
using DaychimeWidget;

internal static class TimeInputTests
{
    internal static void Run()
    {
        var valid = new Dictionary<string, TimeOnly> {
            ["09:30"] = new(9, 30), ["9:30"] = new(9, 30), ["0930"] = new(9, 30),
            ["  0930  "] = new(9, 30), ["\t9:30\r\n"] = new(9, 30),
            ["９：３０"] = new(9, 30), ["０９：３０"] = new(9, 30), ["０９３０"] = new(9, 30),
            ["　０９：３０　"] = new(9, 30), ["0９:３0"] = new(9, 30),
            ["00:00"] = new(0, 0), ["0:00"] = new(0, 0), ["0000"] = new(0, 0),
            ["23:59"] = new(23, 59), ["2359"] = new(23, 59), ["1730"] = new(17, 30),
            // 12-hour times need an explicit marker; 12 AM is midnight and 12 PM is noon.
            ["9:30 AM"] = new(9, 30), ["09:30 PM"] = new(21, 30), ["9:30pm"] = new(21, 30), ["9:30 p.m."] = new(21, 30),
            ["5 PM"] = new(17, 0), ["5pm"] = new(17, 0), ["12:00 AM"] = new(0, 0), ["12 am"] = new(0, 0),
            ["12:30 PM"] = new(12, 30), ["11:59 PM"] = new(23, 59), ["0930 pm"] = new(21, 30),
            ["上午 9:30"] = new(9, 30), ["下午 9:30"] = new(21, 30), ["下午5:30"] = new(17, 30), ["下午 12:15"] = new(12, 15),
            ["9:30 下午"] = new(21, 30), ["上午12:05"] = new(0, 5), ["ｐｍ ９：３０"] = new(21, 30), ["　下午　５：３０　"] = new(17, 30)
        };
        string?[] invalid = { null, "", " ", "9", "09", "930", "123", "9:3", "09:3", "9:300", "09300",
            "25:00", "24:00", "2400", "09:75", "0960", "-9:30", "9.30", "9 30", "09::30", ":930",
            "9:30a", "9:30 p", "9:30 a.m", "2026-10-15 09:30", "٠٩:٣٠", "0 930", "９３０",
            "PM", "下午", "13:00 PM", "0:30 AM", "00 am", "9:75 PM", "下午 9:30 PM", "9:30 PM PM", "晚上 9:30", "9:30 ص" };
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "en-US", "zh-TW", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                foreach (var (input, expected) in valid)
                {
                    if (!TimeInput.TryParse(input, out var actual) || actual != expected)
                        throw new Exception($"Time input failed for '{input}' in {culture}.");
                    var canonical = actual.ToString("HH:mm", CultureInfo.InvariantCulture);
                    if (!TimeInput.TryParse(canonical, out var roundTrip) || roundTrip != expected)
                        throw new Exception($"Canonical time did not round-trip: {canonical}.");
                }
                foreach (var input in invalid)
                    if (TimeInput.TryParse(input, out _)) throw new Exception($"Ambiguous or invalid time was accepted: '{input}'.");
                // Every minute, formatted as the card shows it, parses back to the same time.
                foreach (var (pattern, am, pm) in new[] { ("{0}:{1:00} {2}", "AM", "PM"), ("{2} {0}:{1:00}", "上午", "下午") })
                    foreach (var use24Hour in new[] { false, true })
                        for (var minutes = 0; minutes < 24 * 60; minutes++)
                        {
                            var expected = new TimeOnly(minutes / 60, minutes % 60);
                            var shown = TimeInput.Format(expected, use24Hour, pattern, am, pm);
                            if (!TimeInput.TryParse(shown, out var parsed) || parsed != expected)
                                throw new Exception($"Displayed time '{shown}' did not parse back to {expected:HH:mm}.");
                        }
            }
            if (TimeInput.Format(new TimeOnly(0, 5), false, "{0}:{1:00} {2}", "AM", "PM") != "12:05 AM"
                || TimeInput.Format(new TimeOnly(12, 0), false, "{2} {0}:{1:00}", "上午", "下午") != "下午 12:00"
                || TimeInput.Format(new TimeOnly(9, 5), true, "", "", "") != "09:05")
                throw new Exception("Time display format changed.");
        }
        finally { CultureInfo.CurrentCulture = original; }
        Console.WriteLine("Passed: 24-hour and explicit AM/PM formats, equivalent full-width characters, boundaries, display round-trip and culture-independent rejection.");
    }
}
