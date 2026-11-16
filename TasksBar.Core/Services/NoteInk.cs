using System;
using System.Collections.Generic;
using System.IO;

namespace TasksBar.Services
{
    public struct InkPoint
    {
        public double X;
        public double Y;
        public float Pressure; // 0..1, 0.5 for a mouse

        public InkPoint(double x, double y, float pressure = 0.5f)
        {
            X = x;
            Y = y;
            Pressure = pressure;
        }
    }

    public class InkStroke
    {
        public double Width { get; set; } = 2;
        public double Height { get; set; } = 2;
        public List<InkPoint> Points { get; } = new List<InkPoint>();
    }

    /// <summary>
    /// The drawing format both the Windows and the Linux app read and write (a note's InkData).
    /// "tbink1:" followed by base64 of: a version byte, the stroke count, and per stroke its pen size
    /// and points. Coordinates are in 1/20 of a pixel, stored as zigzag varints of the step from the
    /// previous point, so a drawing is about as small as WPF's own ink format.
    /// Notes drawn with TasksBar for Windows before 1.3 hold WPF's ink format (ISF) instead: only the
    /// Windows app can read that, and it converts those notes to this format (see <see cref="IsLegacy"/>).
    /// </summary>
    public static class NoteInk
    {
        public const string Prefix = "tbink1:";
        private const byte Version = 1;
        private const double Scale = 20;

        private const byte FlagPerPointPressure = 1;

        public static bool IsPortable(string? ink) => ink != null && ink.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>Drawing data in WPF's ink format, from an older Windows version.</summary>
        public static bool IsLegacy(string? ink) => !string.IsNullOrEmpty(ink) && !IsPortable(ink);

        public static string Encode(IReadOnlyCollection<InkStroke> strokes)
        {
            if (strokes.Count == 0) return "";

            using var ms = new MemoryStream();
            ms.WriteByte(Version);
            WriteVarint(ms, (ulong)strokes.Count);
            foreach (var stroke in strokes)
            {
                WriteVarint(ms, (ulong)Math.Max(0, Math.Round(stroke.Width * Scale)));
                WriteVarint(ms, (ulong)Math.Max(0, Math.Round(stroke.Height * Scale)));
                WriteVarint(ms, (ulong)stroke.Points.Count);

                // A mouse gives every point the same pressure; store it once
                byte first = stroke.Points.Count > 0 ? PressureByte(stroke.Points[0].Pressure) : (byte)128;
                bool varies = false;
                foreach (var p in stroke.Points) varies |= PressureByte(p.Pressure) != first;
                ms.WriteByte(varies ? FlagPerPointPressure : (byte)0);
                if (!varies) ms.WriteByte(first);

                long lastX = 0, lastY = 0;
                foreach (var p in stroke.Points)
                {
                    long x = (long)Math.Round(p.X * Scale);
                    long y = (long)Math.Round(p.Y * Scale);
                    WriteVarint(ms, ZigZag(x - lastX));
                    WriteVarint(ms, ZigZag(y - lastY));
                    lastX = x;
                    lastY = y;
                    if (varies) ms.WriteByte(PressureByte(p.Pressure));
                }
            }
            return Prefix + Convert.ToBase64String(ms.ToArray());
        }

        /// <summary>The strokes in portable drawing data. Empty for no drawing, legacy or damaged data.</summary>
        public static List<InkStroke> Decode(string? ink)
        {
            var strokes = new List<InkStroke>();
            if (!IsPortable(ink)) return strokes;

            try
            {
                byte[] bytes = Convert.FromBase64String(ink!.Substring(Prefix.Length));
                int pos = 0;
                if (bytes.Length == 0 || bytes[pos++] != Version) return strokes;

                ulong count = ReadVarint(bytes, ref pos);
                for (ulong s = 0; s < count; s++)
                {
                    var stroke = new InkStroke
                    {
                        Width = ReadVarint(bytes, ref pos) / Scale,
                        Height = ReadVarint(bytes, ref pos) / Scale
                    };
                    ulong points = ReadVarint(bytes, ref pos);
                    byte flags = bytes[pos++];
                    bool varies = (flags & FlagPerPointPressure) != 0;
                    float pressure = varies ? 0.5f : bytes[pos++] / 255f;

                    long x = 0, y = 0;
                    for (ulong i = 0; i < points; i++)
                    {
                        x += UnZigZag(ReadVarint(bytes, ref pos));
                        y += UnZigZag(ReadVarint(bytes, ref pos));
                        float p = varies ? bytes[pos++] / 255f : pressure;
                        stroke.Points.Add(new InkPoint(x / Scale, y / Scale, p));
                    }
                    if (stroke.Points.Count > 0) strokes.Add(stroke);
                }
            }
            catch
            {
                // Damaged data: show what was read so far rather than nothing
            }
            return strokes;
        }

        /// <summary>Whether portable data holds at least one stroke (legacy data can only be checked by the Windows app).</summary>
        public static bool HasStrokes(string? ink) => IsPortable(ink) && Decode(ink).Count > 0;

        private static byte PressureByte(float pressure) => (byte)Math.Clamp((int)Math.Round(pressure * 255), 0, 255);

        private static ulong ZigZag(long v) => (ulong)((v << 1) ^ (v >> 63));

        private static long UnZigZag(ulong v) => (long)(v >> 1) ^ -(long)(v & 1);

        private static void WriteVarint(Stream s, ulong v)
        {
            while (v >= 0x80)
            {
                s.WriteByte((byte)(v | 0x80));
                v >>= 7;
            }
            s.WriteByte((byte)v);
        }

        private static ulong ReadVarint(byte[] bytes, ref int pos)
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                byte b = bytes[pos++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
                if (shift > 63) throw new InvalidDataException("Bad varint");
            }
        }
    }
}
