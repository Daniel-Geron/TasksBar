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
