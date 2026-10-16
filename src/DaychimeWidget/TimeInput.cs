#nullable enable
using System;
using System.Globalization;

namespace DaychimeWidget;

internal static class TimeInput
{
    // AM/PM markers accepted before or after the time, in every display language.
    private static readonly (string Marker, bool Pm)[] Meridiems =
        { ("a.m.", false), ("p.m.", true), ("am", false), ("pm", true), ("上午", false), ("下午", true) };

    // Accept 24-hour times (H:mm, HH:mm, or exactly four HHmm digits) and 12-hour times with an
    // explicit AM/PM marker (9:30 PM, 9 pm, 下午 9:30). Normalize only equivalent characters,
    // never guess a missing minute in 24-hour form or a missing AM/PM.
    internal static bool TryParse(string? value, out TimeOnly time)
    {
        time = default;
        if (value is null) return false;
        var normalized = value.ToCharArray();
        for (var index = 0; index < normalized.Length; index++)
        {
            var character = normalized[index];
            // Full-width forms (０-９, ：, Ａ-ｚ, ．) are the same characters typed with a CJK IME.
            if (character >= '！' && character <= '～') character = (char)(character - '！' + '!');
            if (character == '　') character = ' ';
            normalized[index] = char.ToLowerInvariant(character);
        }
        var text = new string(normalized).Trim();

        foreach (var (marker, pm) in Meridiems)
        {
            string rest;
            if (text.StartsWith(marker, StringComparison.Ordinal)) rest = text[marker.Length..].TrimStart();
            else if (text.EndsWith(marker, StringComparison.Ordinal)) rest = text[..^marker.Length].TrimEnd();
            else continue;
            if (!TryParseClock(rest, allowBareHour: true, out var hour, out var minute) || hour < 1 || hour > 12) return false;
            time = new TimeOnly(hour % 12 + (pm ? 12 : 0), minute);
            return true;
        }

        if (!TryParseClock(text, allowBareHour: false, out var hour24, out var minute24) || hour24 > 23) return false;
        time = new TimeOnly(hour24, minute24);
        return true;
    }

    // Formats a time the way the card displays it, so a prefilled input parses back unchanged.
    // pattern12 receives {0} hour (1-12), {1} minute and {2} the AM/PM designator.
    internal static string Format(TimeOnly time, bool use24Hour, string pattern12, string am, string pm)
    {
        if (use24Hour) return time.ToString("HH:mm", CultureInfo.InvariantCulture);
        var hour = time.Hour % 12 == 0 ? 12 : time.Hour % 12;
        return string.Format(CultureInfo.InvariantCulture, pattern12, hour, time.Minute, time.Hour < 12 ? am : pm);
    }

    // H:mm, HH:mm, HHmm, and with allowBareHour also H or HH. ASCII digits only.
    private static bool TryParseClock(string text, bool allowBareHour, out int hour, out int minute)
    {
        hour = minute = 0;
        foreach (var character in text)
            if ((character < '0' || character > '9') && character != ':') return false;
        var colon = text.IndexOf(':');
        int hourLength;
        if (colon == -1 && text.Length == 4) hourLength = 2;
        else if (colon == -1 && allowBareHour && text.Length is 1 or 2)
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out hour);
        else if ((colon == 1 || colon == 2) && colon == text.Length - 3) hourLength = colon;
        else return false;
        var minuteStart = colon == -1 ? 2 : colon + 1;
        return int.TryParse(text.AsSpan(0, hourLength), NumberStyles.None, CultureInfo.InvariantCulture, out hour)
            && int.TryParse(text.AsSpan(minuteStart), NumberStyles.None, CultureInfo.InvariantCulture, out minute)
            && minute <= 59;
    }
}
