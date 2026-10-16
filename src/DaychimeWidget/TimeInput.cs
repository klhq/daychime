#nullable enable
using System;
using System.Globalization;

namespace DaychimeWidget;

internal static class TimeInput
{
    // Accept explicit 24-hour times only: H:mm, HH:mm, or exactly four HHmm digits.
    // Normalize only equivalent characters, never guess a missing hour/minute.
    internal static bool TryParse(string? value, out TimeOnly time)
    {
        time = default;
        if (value is null) return false;
        var normalized = value.Trim().ToCharArray();
        if (normalized.Length != 4 && normalized.Length != 5) return false;
        for (var index = 0; index < normalized.Length; index++)
        {
            var character = normalized[index];
            if (character >= '\uFF10' && character <= '\uFF19') character = (char)('0' + character - '\uFF10');
            if (character == '\uFF1A') character = ':';
            if ((character < '0' || character > '9') && character != ':') return false;
            normalized[index] = character;
        }

        var text = normalized.AsSpan();
        var colon = text.IndexOf(':');
        int hourLength;
        int minuteStart;
        if (colon == -1 && text.Length == 4)
        {
            hourLength = 2;
            minuteStart = 2;
        }
        else if ((colon == 1 || colon == 2) && colon == text.Length - 3)
        {
            hourLength = colon;
            minuteStart = colon + 1;
        }
        else return false;

        if (!int.TryParse(text[..hourLength], NumberStyles.None, CultureInfo.InvariantCulture, out var hour)
            || !int.TryParse(text[minuteStart..], NumberStyles.None, CultureInfo.InvariantCulture, out var minute)
            || hour > 23 || minute > 59) return false;
        time = new TimeOnly(hour, minute);
        return true;
    }
}
