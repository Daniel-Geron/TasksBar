using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TasksBar
{
    /// <summary>
    /// The little celebration when a task is checked off: a ring ripple and a burst of particles
    /// from the check circle, then the task slides away and its row collapses smoothly.
    /// </summary>
    public static class TaskCompletionEffects
    {
        private static readonly Random _random = new Random();

        /// <summary>Ripple ring + particle burst centered on <paramref name="center"/> (in the layer's coordinates).</summary>
        public static void PlayBurst(Canvas layer, Point center, Color accent)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            // Expanding ring
            const double ringSize = 18;
            var ringScale = new ScaleTransform(1, 1);
            var ring = new System.Windows.Shapes.Ellipse
            {
                Width = ringSize,
                Height = ringSize,
                Stroke = new SolidColorBrush(accent),
                StrokeThickness = 2,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = ringScale,
                Opacity = 0.9
            };
            Place(layer, ring, center, ringSize);
            var ringGrow = new DoubleAnimation(1, 3.2, TimeSpan.FromMilliseconds(500)) { EasingFunction = ease };
            ringScale.BeginAnimation(ScaleTransform.ScaleXProperty, ringGrow);
            ringScale.BeginAnimation(ScaleTransform.ScaleYProperty, ringGrow);
            var ringFade = new DoubleAnimation(0.9, 0, TimeSpan.FromMilliseconds(500));
            ringFade.Completed += (s, e) => layer.Children.Remove(ring);
            ring.BeginAnimation(UIElement.OpacityProperty, ringFade);

            // Particles in shades of the Windows accent color
            Color[] palette = { accent, Lighten(accent, 0.45), Darken(accent, 0.3), Lighten(accent, 0.75) };
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

                var particle = new System.Windows.Shapes.Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(palette[i % palette.Length]),
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = group
                };
                Place(layer, particle, center, size);

                var duration = TimeSpan.FromMilliseconds(480 + _random.Next(0, 120));
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Math.Cos(angle) * distance, duration) { EasingFunction = ease });
                move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Math.Sin(angle) * distance, duration) { EasingFunction = ease });
                var smaller = new DoubleAnimation(1, 0.3, duration);
                shrink.BeginAnimation(ScaleTransform.ScaleXProperty, smaller);
                shrink.BeginAnimation(ScaleTransform.ScaleYProperty, smaller);

                var fade = new DoubleAnimation(1, 0, duration) { BeginTime = TimeSpan.FromMilliseconds(150) };
                fade.Completed += (s, e) => layer.Children.Remove(particle);
                particle.BeginAnimation(UIElement.OpacityProperty, fade);
            }
        }

        /// <summary>Slides the row out to the right while fading, then collapses its height so the list closes the gap.</summary>
        public static Task SlideOutAndCollapseAsync(FrameworkElement row)
        {
            var done = new TaskCompletionSource<bool>();

            var slide = new TranslateTransform();
            row.RenderTransform = slide;
            var slideOut = TimeSpan.FromMilliseconds(260);
            slide.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, 40, slideOut) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            row.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, slideOut));

            var collapseEase = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var collapse = new DoubleAnimation(row.ActualHeight, 0, TimeSpan.FromMilliseconds(220))
            {
                BeginTime = TimeSpan.FromMilliseconds(200),
                EasingFunction = collapseEase
            };
            collapse.Completed += (s, e) => done.TrySetResult(true);
            row.BeginAnimation(FrameworkElement.HeightProperty, collapse);
            row.BeginAnimation(FrameworkElement.MarginProperty,
                new ThicknessAnimation(row.Margin, new Thickness(0), TimeSpan.FromMilliseconds(220))
                {
                    BeginTime = TimeSpan.FromMilliseconds(200),
                    EasingFunction = collapseEase
                });

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
                accent, Lighten(accent, 0.5), ShiftHue(accent, 40), ShiftHue(accent, -40),
                ShiftHue(accent, 160), Color.FromRgb(0xFF, 0xD1, 0x4D), Colors.White
            };

            for (int i = 0; i < count; i++)
            {
                // Mostly upward fan, a bit wider than a half circle
                double angle = (-90 + (_random.NextDouble() * 2 - 1) * 75) * Math.PI / 180;
                double power = 45 + _random.NextDouble() * 55;
                double dx = Math.Cos(angle) * power;
                double peak = Math.Sin(angle) * power;            // negative = up
                double fall = 30 + _random.NextDouble() * 40;      // how far below the peak it drops

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

                FrameworkElement piece = strip
                    ? new System.Windows.Shapes.Rectangle { RadiusX = 1, RadiusY = 1 }
                    : new System.Windows.Shapes.Ellipse();
                ((System.Windows.Shapes.Shape)piece).Fill = new SolidColorBrush(palette[_random.Next(palette.Length)]);
                piece.Width = w;
                piece.Height = h;
                piece.RenderTransformOrigin = new Point(0.5, 0.5);
                piece.RenderTransform = group;

                Canvas.SetLeft(piece, center.X - w / 2);
                Canvas.SetTop(piece, center.Y - h / 2);
                layer.Children.Add(piece);

                double rise = 0.32 + _random.NextDouble() * 0.1;   // seconds to reach the peak
                double total = rise + 0.75 + _random.NextDouble() * 0.35;
                var delay = TimeSpan.FromMilliseconds(_random.Next(0, 70));

                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dx, TimeSpan.FromSeconds(total))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });

                // Up fast, then fall with gravity
                var y = new DoubleAnimationUsingKeyFrames { BeginTime = delay };
                y.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(rise)),
                    new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                y.KeyFrames.Add(new EasingDoubleKeyFrame(peak + fall + Math.Abs(peak) * 0.4, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(total)),
                    new QuadraticEase { EasingMode = EasingMode.EaseIn }));
                move.BeginAnimation(TranslateTransform.YProperty, y);

                double turns = (_random.NextDouble() * 2 - 1) * 540;
                spin.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(spin.Angle, spin.Angle + turns, TimeSpan.FromSeconds(total)) { BeginTime = delay });

                // Paper-like flutter
                flip.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, -1, TimeSpan.FromMilliseconds(180 + _random.Next(0, 160)))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                });

                var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.35))
                {
                    BeginTime = delay + TimeSpan.FromSeconds(total - 0.35)
                };
                fade.Completed += (s, e) => layer.Children.Remove(piece);
                piece.BeginAnimation(UIElement.OpacityProperty, fade);
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
            // Keep pale accents colorful enough to read as confetti
            s = Math.Max(s, 0.55);

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

        public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

        private static void Place(Canvas layer, FrameworkElement element, Point center, double size)
        {
            Canvas.SetLeft(element, center.X - size / 2);
            Canvas.SetTop(element, center.Y - size / 2);
            layer.Children.Add(element);
        }

        private static Color Darken(Color color, double amount) => Color.FromRgb(
            (byte)(color.R * (1 - amount)),
            (byte)(color.G * (1 - amount)),
            (byte)(color.B * (1 - amount)));

        private static Color Lighten(Color color, double amount) => Color.FromRgb(
            (byte)(color.R + (255 - color.R) * amount),
            (byte)(color.G + (255 - color.G) * amount),
            (byte)(color.B + (255 - color.B) * amount));
    }
}
