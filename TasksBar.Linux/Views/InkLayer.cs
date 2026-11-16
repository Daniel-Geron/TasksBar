using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using TasksBar.Services;

namespace TasksBar.Linux.Views
{
    /// <summary>
    /// The drawing on a sticky note: Avalonia's stand-in for WPF's InkCanvas, with the same pen
    /// (2px, follows pen pressure) and the same erase-whole-stroke eraser. Strokes use the shared
    /// <see cref="NoteInk"/> format, so drawings move freely between Windows and Linux.
    /// </summary>
    public class InkLayer : Control, Avalonia.Rendering.ICustomHitTest
    {
        public enum Mode { None, Ink, Erase }

        private const double PenSize = 2;
        private const double EraserRadius = 3;

        private readonly List<InkStroke> _strokes = new();
        private readonly Dictionary<InkStroke, Geometry?> _geometry = new();
        private InkStroke? _current;
        private Mode _mode = Mode.None;
        private bool _erasing;
        private Point _lastErase;

        /// <summary>Raised when a stroke is finished or erased (not while drawing).</summary>
        public event Action? StrokesChanged;

        public IBrush InkBrush { get; set; } = Brushes.White;

        public Mode EditingMode
        {
            get => _mode;
            set
            {
                _mode = value;
                // Clicks fall through to the text while typing, like the Windows app
                IsHitTestVisible = value != Mode.None;
                Cursor = value switch
                {
                    Mode.Ink => new Cursor(StandardCursorType.Cross),
                    Mode.Erase => new Cursor(StandardCursorType.Hand),
                    _ => Cursor.Default
                };
            }
        }

        public InkLayer()
        {
            ClipToBounds = true;
            EditingMode = Mode.None;
        }

        public bool HasStrokes => _strokes.Count > 0;

        // The whole layer takes the pen while drawing or erasing (clicks fall through otherwise)
        public bool HitTest(Point point) => _mode != Mode.None && new Rect(Bounds.Size).Contains(point);

        public void Load(string? ink)
        {
            _strokes.Clear();
            _geometry.Clear();
            _strokes.AddRange(NoteInk.Decode(ink));
            InvalidateVisual();
        }

        public string Save() => NoteInk.Encode(_strokes);

        // ------------------------------------------------------------------ input

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed && e.Pointer.Type != PointerType.Pen) return;
            e.Handled = true;
            e.Pointer.Capture(this);

            if (_mode == Mode.Ink)
            {
                _current = new InkStroke { Width = PenSize, Height = PenSize };
                _current.Points.Add(ToInkPoint(point));
                InvalidateVisual();
            }
            else if (_mode == Mode.Erase)
            {
                _erasing = true;
                _lastErase = point.Position;
                EraseAlong(point.Position, point.Position);
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_current != null)
            {
                // The in-between samples keep fast strokes smooth
                foreach (var point in e.GetIntermediatePoints(this)) _current.Points.Add(ToInkPoint(point));
                InvalidateVisual();
            }
            else if (_erasing)
            {
                var position = e.GetPosition(this);
                EraseAlong(_lastErase, position);
                _lastErase = position;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            Finish();
            e.Pointer.Capture(null);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            Finish();
        }

        private void Finish()
        {
            if (_current != null)
            {
                _strokes.Add(_current);
                _current = null;
                InvalidateVisual();
                StrokesChanged?.Invoke();
            }
            _erasing = false;
        }

        private static InkPoint ToInkPoint(PointerPoint point)
        {
            // A mouse reports 0.5, like in WPF; pens report their real pressure
            float pressure = point.Pointer.Type == PointerType.Pen ? point.Properties.Pressure : 0.5f;
            return new InkPoint(Math.Round(point.Position.X, 2), Math.Round(point.Position.Y, 2), pressure);
        }

        private void EraseAlong(Point from, Point to)
        {
            int removed = _strokes.RemoveAll(stroke => Touches(stroke, from, to));
            if (removed == 0) return;
            foreach (var key in _geometry.Keys.Where(k => !_strokes.Contains(k)).ToList()) _geometry.Remove(key);
            InvalidateVisual();
            StrokesChanged?.Invoke();
        }

        private static bool Touches(InkStroke stroke, Point from, Point to)
        {
            double reach = EraserRadius + stroke.Width / 2;
            var points = stroke.Points;
            if (points.Count == 1) return SegmentDistance(new Point(points[0].X, points[0].Y), from, to) <= reach;

            for (int i = 1; i < points.Count; i++)
            {
                var a = new Point(points[i - 1].X, points[i - 1].Y);
                var b = new Point(points[i].X, points[i].Y);
                if (SegmentsDistance(a, b, from, to) <= reach) return true;
            }
            return false;
        }

        private static double SegmentDistance(Point p, Point a, Point b)
        {
            var ab = b - a;
            double length = ab.X * ab.X + ab.Y * ab.Y;
            double t = length == 0 ? 0 : Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / length, 0, 1);
            var closest = a + ab * t;
            return Math.Sqrt((p.X - closest.X) * (p.X - closest.X) + (p.Y - closest.Y) * (p.Y - closest.Y));
        }

        // Good enough for an eraser: the closest endpoint-to-segment distance (segments are only a few pixels long)
        private static double SegmentsDistance(Point a, Point b, Point c, Point d) =>
            Math.Min(Math.Min(SegmentDistance(a, c, d), SegmentDistance(b, c, d)),
                     Math.Min(SegmentDistance(c, a, b), SegmentDistance(d, a, b)));

        // ------------------------------------------------------------------ drawing

        public override void Render(DrawingContext context)
        {
            foreach (var stroke in _strokes) DrawStroke(context, stroke, cache: true);
            if (_current != null) DrawStroke(context, _current, cache: false);
        }

        // WPF draws a stroke thicker or thinner around its pen size as pressure goes above or below 0.5
        private static double Thickness(InkStroke stroke, float pressure) =>
            Math.Max(0.3, stroke.Width * Math.Clamp(pressure / 0.5, 0.25, 2));

        private void DrawStroke(DrawingContext context, InkStroke stroke, bool cache)
        {
            var points = stroke.Points;
            bool samePressure = points.All(p => p.Pressure == points[0].Pressure);

            if (points.Count == 1)
            {
                double r = Thickness(stroke, points[0].Pressure) / 2;
                context.DrawEllipse(InkBrush, null, new Point(points[0].X, points[0].Y), r, r);
                return;
            }

            if (samePressure)
            {
                Geometry? geometry = null;
                if (!cache || !_geometry.TryGetValue(stroke, out geometry))
                {
                    geometry = BuildPolyline(points);
                    if (cache) _geometry[stroke] = geometry;
                }
                var pen = new Pen(InkBrush, Thickness(stroke, points[0].Pressure), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                if (geometry != null) context.DrawGeometry(null, pen, geometry);
                return;
            }

            // Pen pressure: each piece gets its own width; round ends hide the joins
            for (int i = 1; i < points.Count; i++)
            {
                var pen = new Pen(InkBrush, Thickness(stroke, (points[i - 1].Pressure + points[i].Pressure) / 2), lineCap: PenLineCap.Round);
                context.DrawLine(pen, new Point(points[i - 1].X, points[i - 1].Y), new Point(points[i].X, points[i].Y));
            }
        }

        private static Geometry BuildPolyline(List<InkPoint> points)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(points[0].X, points[0].Y), false);
                for (int i = 1; i < points.Count; i++) ctx.LineTo(new Point(points[i].X, points[i].Y));
                ctx.EndFigure(false);
            }
            return geometry;
        }
    }
}
