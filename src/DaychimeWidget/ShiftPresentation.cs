#nullable enable
using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace DaychimeWidget;

internal enum WorkdayPhase { Idle, Working, Overtime, Missing, ClockedOut }

// Pure display rules for the widget card, kept free of WinRT so they can be unit tested.
internal static class ShiftPresentation
{
    internal static WorkdayPhase GetPhase(ShiftSnapshot snapshot, DateTimeOffset now, bool autoClockIn, TimeOnly earliest)
    {
        // Auto clock-in is on, the cycle has started and nothing recorded it: ask for the start.
        if (autoClockIn && ShiftLifecycle.CanAutoClockIn(snapshot, now, earliest)) return WorkdayPhase.Missing;
        if (snapshot.Current is Shift current) return current.Finish <= now ? WorkdayPhase.Overtime : WorkdayPhase.Working;
        // A clock-out stays the headline until the next work cycle begins.
        if (snapshot.Previous is Shift { ClockOut: DateTimeOffset clockOut }
            && ShiftLifecycle.GetCycle(clockOut, earliest) == ShiftLifecycle.GetCycle(now, earliest)) return WorkdayPhase.ClockedOut;
        return WorkdayPhase.Idle;
    }

    // Fraction of the planned workday that has elapsed, clamped to [0, 1].
    internal static double GetProgress(Shift shift, DateTimeOffset now)
    {
        var total = (shift.Finish - shift.Start).TotalMinutes;
        return total <= 0 ? 1 : Math.Clamp((now - shift.Start).TotalMinutes / total, 0, 1);
    }

    // Elapsed time rounds down; time left rounds up, so "0:00 left" never shows before the finish.
    internal static TimeSpan Worked(Shift shift, DateTimeOffset now) => Floor(now - shift.Start);
    internal static TimeSpan Left(Shift shift, DateTimeOffset now) => Ceiling(shift.Finish - now);
    internal static TimeSpan Over(Shift shift, DateTimeOffset now) => Floor(now - shift.Finish);

    internal static string FormatClock(TimeSpan span) => $"{(int)span.TotalHours}:{span.Minutes:00}";

    private static TimeSpan Floor(TimeSpan span) => TimeSpan.FromMinutes(Math.Max(0, Math.Floor(span.TotalMinutes)));
    private static TimeSpan Ceiling(TimeSpan span) => TimeSpan.FromMinutes(Math.Max(0, Math.Ceiling(span.TotalMinutes)));

    // Content width is 268px at every widget size; draw at 2x for high-DPI displays.
    internal const int BarWidth = 536;
    internal const int BarHeight = 12;

    // Progress bar as a PNG data URI: Adaptive Cards hosts accept PNG data URIs everywhere,
    // and Widgets host configs have no container backgrounds to draw one natively.
    internal static string BuildProgressBar(double progress, bool complete, bool dark)
    {
        uint fill = complete ? (dark ? 0xFF6CCB5Fu : 0xFF0F7B0Fu) : (dark ? 0xFF60CDFFu : 0xFF005FB8u);
        uint track = dark ? 0x29FFFFFFu : 0x1A000000u;
        var radius = BarHeight / 2.0;
        var fillEnd = progress <= 0 ? 0 : Math.Max(BarHeight, BarWidth * Math.Clamp(progress, 0, 1));
        var pixels = new byte[BarHeight * (1 + BarWidth * 4)];
        for (var y = 0; y < BarHeight; y++)
        {
            var row = y * (1 + BarWidth * 4);
            for (var x = 0; x < BarWidth; x++)
            {
                var px = x + 0.5;
                var py = y + 0.5;
                var color = Composite(Shade(track, Capsule(px, py, 0, BarWidth, radius)), Shade(fill, Capsule(px, py, 0, fillEnd, radius)));
                var offset = row + 1 + x * 4;
                pixels[offset] = (byte)(color >> 16);
                pixels[offset + 1] = (byte)(color >> 8);
                pixels[offset + 2] = (byte)color;
                pixels[offset + 3] = (byte)(color >> 24);
            }
        }
        return "data:image/png;base64," + Convert.ToBase64String(EncodePng(pixels));
    }

    // Anti-aliased coverage of a horizontal capsule spanning [start, end].
    private static double Capsule(double x, double y, double start, double end, double radius)
    {
        if (end - start < radius * 2) return 0;
        var cx = Math.Clamp(x, start + radius, end - radius);
        var distance = Math.Sqrt((x - cx) * (x - cx) + (y - radius) * (y - radius));
        return Math.Clamp(radius - distance + 0.5, 0, 1);
    }

    private static uint Shade(uint argb, double coverage) =>
        ((uint)Math.Round((argb >> 24) * coverage) << 24) | (argb & 0xFFFFFF);

    // Source-over compositing of straight-alpha ARGB values.
    private static uint Composite(uint below, uint above)
    {
        double a = (above >> 24) / 255.0, b = (below >> 24) / 255.0;
        var outAlpha = a + b * (1 - a);
        if (outAlpha <= 0) return 0;
        byte Channel(int shift) => (byte)Math.Round((((above >> shift) & 0xFF) * a + ((below >> shift) & 0xFF) * b * (1 - a)) / outAlpha);
        return ((uint)Math.Round(outAlpha * 255) << 24) | ((uint)Channel(16) << 16) | ((uint)Channel(8) << 8) | Channel(0);
    }

    private static byte[] EncodePng(byte[] filteredRows)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, BarWidth);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), BarHeight);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA
        WriteChunk(output, "IHDR", header);
        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(filteredRows);
            WriteChunk(output, "IDAT", compressed.ToArray());
        }
        WriteChunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var part in new[] { type, data })
            foreach (var value in part)
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        return ~crc;
    }
}
