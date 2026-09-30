#nullable enable
using System;
using System.Globalization;

namespace DaychimeWidget;

internal static class ShiftStateCodec
{
    internal static string Serialize(Shift? shift) => shift is Shift value
        ? $"{value.Start:O};{value.WorkHours.ToString("0.#", CultureInfo.InvariantCulture)};{value.ClockOut?.ToString("O", CultureInfo.InvariantCulture)};{value.ReminderRevision}" : string.Empty;

    internal static bool TryRead(string raw, out Shift? shift)
    {
        shift = null;
        if (string.IsNullOrEmpty(raw)) return true;
        var parts = raw.Split(';');
        if ((parts.Length != 3 && parts.Length != 4) ||
            !DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start) ||
            !double.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var hours) ||
            Array.IndexOf(ShiftLifecycle.WorkHoursPresets, hours) < 0) return false;
        DateTimeOffset? clockOut = null;
        if (parts[2].Length > 0)
        {
            if (!DateTimeOffset.TryParse(parts[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) || parsed < start)
                return false;
            clockOut = parsed;
        }
        var revision = parts.Length == 4 && parts[3].Length > 0 ? parts[3] : null;
        if (revision is not null && !Guid.TryParseExact(revision, "N", out _)) return false;
        shift = new Shift(start, hours, clockOut, revision);
        return true;
    }
}
