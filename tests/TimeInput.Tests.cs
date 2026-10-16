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
            ["23:59"] = new(23, 59), ["2359"] = new(23, 59), ["1730"] = new(17, 30)
        };
        string?[] invalid = { null, "", " ", "9", "09", "930", "123", "9:3", "09:3", "9:300", "09300",
            "25:00", "24:00", "2400", "09:75", "0960", "-9:30", "9.30", "9 30", "09::30", ":930",
            "09:30 PM", "09:30 AM", "9:30a", "2026-10-15 09:30", "٠٩:٣٠", "0 930", "９３０" };
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
            }
        }
        finally { CultureInfo.CurrentCulture = original; }
        Console.WriteLine("Passed: explicit 24-hour formats, equivalent full-width characters, boundaries and culture-independent rejection.");
    }
}
