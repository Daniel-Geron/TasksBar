using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace TasksBar.Linux.Effects
{
    /// <summary>
    /// The celebration when a task is checked off (port of TaskCompletionEffects in the Windows app):
    /// a ring ripple and particle burst from the circle, the row sliding away, and confetti for the toast.
    /// </summary>
    public static class CompletionEffects
    {
        private static readonly Random _random = new();

        /// <summary>Ripple ring + particle burst centered on <paramref name="center"/> (layer coordinates).</summary>
        public static void PlayBurst(Canvas layer, Point center, Color accent)
        {
            const double ringSize = 18;
            var ringScale = new ScaleTransform(1, 1);
            var ring = new Ellipse
            {
                Width = ringSize, Height = ringSize,
                Stroke = new SolidColorBrush(accent), StrokeThickness = 2,
                RenderTransform = ringScale, Opacity = 0.9, IsHitTestVisible = false
            };
            Place(layer, ring, center, ringSize);
            ringScale.Scale(3.2, 500, Ease.CubicOut, from: 1);
            ring.Opacity(0, 500, from: 0.9, done: () => layer.Children.Remove(ring));

            // Particles in shades of the accent color
            Color[] palette = { accent, accent.Lighten(0.45), accent.Darken(0.3), accent.Lighten(0.75) };
            const int count = 12;
            for (int i = 0; i < count; i++)
            {
                double angle = (i * 360.0 / count + _random.Next(-12, 12)) * Math.PI / 180;
                double distance = 26 + _random.NextDouble() * 14;
                double size = 5 + _random.NextDouble() * 2.5;

                var move = new TranslateTransform();
                var shrink = new ScaleTransform(1, 1);
                var group = new TransformGroup();
                group.Children.Add(shrink);
                group.Children.Add(move);

                var particle = new Ellipse
                {
                    Width = size, Height = size,
                    Fill = new SolidColorBrush(palette[i % palette.Length]),
                    RenderTransform = group, IsHitTestVisible = false
                };
                Place(layer, particle, center, size);

                double duration = 480 + _random.Next(0, 120);
                move.X(Math.Cos(angle) * distance, duration, Ease.CubicOut, from: 0);
                move.Y(Math.Sin(angle) * distance, duration, Ease.CubicOut, from: 0);
                shrink.Scale(0.3, duration, from: 1);
                particle.Opacity(0, duration, delay: 150, from: 1, done: () => layer.Children.Remove(particle));
            }
        }

        /// <summary>Slides the row out to the right while fading, then collapses its height so the list closes the gap.</summary>
        public static Task SlideOutAndCollapseAsync(Control row, double spacing)
        {
            var done = new TaskCompletionSource<bool>();

            var slide = new TranslateTransform();
            row.RenderTransform = slide;
            slide.X(40, 260, Ease.CubicIn, from: 0);
            row.Opacity(0, 260, from: 1);

            // Collapse the height and the list's spacing under it together, so nothing jumps at the end
            double height = row.Bounds.Height;
            row.ClipToBounds = true;
            row.Height(0, 220, Ease.CubicInOut, delay: 200, from: height);
            Tween.To(row, "MarginBottom", () => row.Margin.Bottom, v => row.Margin = new Thickness(0, 0, 0, v),
                     -spacing, 220, Ease.CubicInOut, 200, 0, () => done.TrySetResult(true));

            return done.Task;
        }

        /// <summary>
        /// Confetti that pops up out of <paramref name="center"/>, spins and falls with gravity.
        /// Colors are the accent plus a few neighboring hues so it feels festive but still matches.
        /// </summary>
        public static void PlayConfetti(Canvas layer, Point center, Color accent, int count = 22)
        {
            Color[] palette =
            {
                accent, accent.Lighten(0.5), ShiftHue(accent, 40), ShiftHue(accent, -40),
                ShiftHue(accent, 160), Color.FromRgb(0xFF, 0xD1, 0x4D), Colors.White
            };

            for (int i = 0; i < count; i++)
            {
                // Mostly upward fan, a bit wider than a half circle
                double angle = (-90 + (_random.NextDouble() * 2 - 1) * 75) * Math.PI / 180;
                double power = 45 + _random.NextDouble() * 55;
                double dx = Math.Cos(angle) * power;
                double peak = Math.Sin(angle) * power;       // negative = up
                double fall = 30 + _random.NextDouble() * 40; // how far below the peak it drops

                bool strip = _random.Next(3) > 0;
                double w = strip ? 3 + _random.NextDouble() * 2 : 5 + _random.NextDouble() * 2;
                double h = strip ? 7 + _random.NextDouble() * 4 : w;

                var move = new TranslateTransform();
                var spin = new RotateTransform(_random.Next(0, 360));
                var flip = new ScaleTransform(1, 1);
                var group = new TransformGroup();
                group.Children.Add(flip);
                group.Children.Add(spin);
                group.Children.Add(move);

                Shape piece = strip ? new Rectangle { RadiusX = 1, RadiusY = 1 } : new Ellipse();
                piece.Fill = new SolidColorBrush(palette[_random.Next(palette.Length)]);
                piece.Width = w;
                piece.Height = h;
                piece.RenderTransform = group;
                piece.IsHitTestVisible = false;
                Canvas.SetLeft(piece, center.X - w / 2);
                Canvas.SetTop(piece, center.Y - h / 2);
                layer.Children.Add(piece);

                double rise = 320 + _random.NextDouble() * 100;
                double total = rise + 750 + _random.NextDouble() * 350;
                double delay = _random.Next(0, 70);

                move.X(dx, total, Ease.CubicOut, delay, from: 0);
                // Up fast, then fall with gravity
                Tween.To(move, "Y", () => move.Y, v => move.Y = v, peak, rise, Ease.QuadOut, delay, 0, () =>
                    move.Y(peak + fall + Math.Abs(peak) * 0.4, total - rise, Ease.QuadIn));
                spin.Angle(spin.Angle + (_random.NextDouble() * 2 - 1) * 540, total, delay: delay);

                // Paper-like flutter
                Tween.To(flip, "Flip", () => flip.ScaleX, v => flip.ScaleX = v, -1, 180 + _random.Next(0, 160),
                         from: 1, repeatForever: true, autoReverse: true);

                piece.Opacity(0, 350, delay: delay + total - 350, from: 1, done: () =>
                {
                    Tween.Stop(flip, "Flip");
                    layer.Children.Remove(piece);
                });
            }
        }

        /// <summary>Rotates a color around the hue wheel by <paramref name="degrees"/>.</summary>
        public static Color ShiftHue(Color c, double degrees)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double v = max, d = max - min, s = max == 0 ? 0 : d / max, hue = 0;
            if (d > 0)
            {
                if (max == r) hue = 60 * (((g - b) / d) % 6);
                else if (max == g) hue = 60 * ((b - r) / d + 2);
                else hue = 60 * ((r - g) / d + 4);
            }
            hue = ((hue + degrees) % 360 + 360) % 360;
            s = Math.Max(s, 0.55); // Keep pale accents colorful enough to read as confetti

            double cc = v * s, x = cc * (1 - Math.Abs(hue / 60 % 2 - 1)), m = v - cc;
            (double r1, double g1, double b1) = hue switch
            {
                < 60 => (cc, x, 0.0),
                < 120 => (x, cc, 0.0),
                < 180 => (0.0, cc, x),
                < 240 => (0.0, x, cc),
                < 300 => (x, 0.0, cc),
                _ => (cc, 0.0, x)
            };
            return Color.FromRgb((byte)((r1 + m) * 255), (byte)((g1 + m) * 255), (byte)((b1 + m) * 255));
        }

        private static void Place(Canvas layer, Control element, Point center, double size)
        {
            Canvas.SetLeft(element, center.X - size / 2);
            Canvas.SetTop(element, center.Y - size / 2);
            layer.Children.Add(element);
        }
    }
}
