using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Ink;
using System.Windows.Input;

namespace TasksBar.Services
{
    /// <summary>Converts between WPF's InkCanvas strokes and the drawing format shared with the Linux app (<see cref="NoteInk"/>).</summary>
    public static class WpfInk
    {
        public static StrokeCollection ToStrokes(string? ink)
        {
            if (NoteInk.IsLegacy(ink)) return ReadLegacy(ink!) ?? new StrokeCollection();

            var strokes = new StrokeCollection();
            foreach (var stroke in NoteInk.Decode(ink))
            {
                var points = new StylusPointCollection(stroke.Points.Select(p =>
                    new StylusPoint(p.X, p.Y, Math.Clamp(p.Pressure, 0f, 1f))));
                strokes.Add(new Stroke(points)
                {
                    DrawingAttributes = new DrawingAttributes { Width = Math.Max(0.5, stroke.Width), Height = Math.Max(0.5, stroke.Height) }
                });
            }
            return strokes;
        }

        public static string FromStrokes(StrokeCollection strokes)
        {
            var list = new List<InkStroke>();
            foreach (var stroke in strokes)
            {
                var converted = new InkStroke { Width = stroke.DrawingAttributes.Width, Height = stroke.DrawingAttributes.Height };
                foreach (var point in stroke.StylusPoints) converted.Points.Add(new InkPoint(point.X, point.Y, point.PressureFactor));
                if (converted.Points.Count > 0) list.Add(converted);
            }
            return NoteInk.Encode(list);
        }

        /// <summary>Drawing data saved by TasksBar for Windows before 1.3: base64 of WPF's ink format (ISF).</summary>
        public static StrokeCollection? ReadLegacy(string ink)
        {
            try
            {
                using var ms = new MemoryStream(Convert.FromBase64String(ink));
                return new StrokeCollection(ms);
            }
            catch { return null; }
        }

        /// <summary>
        /// Rewrites notes still holding the old WPF format in the shared one, so the Linux app can show
        /// their drawings. They're marked for upload so the converted drawing reaches other devices.
        /// </summary>
        public static void ConvertLegacyNotes()
        {
            foreach (var note in LocalDataManager.ActiveNotes)
            {
                if (!NoteInk.IsLegacy(note.InkData)) continue;
                var strokes = ReadLegacy(note.InkData);
                if (strokes == null) continue; // Unreadable: leave it as it was
                note.InkData = FromStrokes(strokes);
                StickyNoteSync.MarkDirty(note);
            }
        }
    }
}
