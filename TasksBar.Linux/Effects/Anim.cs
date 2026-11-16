using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace TasksBar.Linux.Effects
{
    /// <summary>Shortcuts for animating the properties the TasksBar effects use.</summary>
    public static class Anim
    {
        public static void Opacity(this Visual v, double to, double ms, Func<double, double>? ease = null,
                                   double delay = 0, double? from = null, Action? done = null) =>
            Tween.To(v, "Opacity", () => v.Opacity, x => v.Opacity = x, to, ms, ease, delay, from, done);

        /// <summary>Animates both axes of a scale transform together.</summary>
        public static void Scale(this ScaleTransform s, double to, double ms, Func<double, double>? ease = null,
                                 double delay = 0, double? from = null, Action? done = null,
                                 bool repeat = false, bool autoReverse = false) =>
            Tween.To(s, "Scale", () => s.ScaleX, x => { s.ScaleX = x; s.ScaleY = x; }, to, ms, ease, delay, from, done, repeat, autoReverse);

        public static void X(this TranslateTransform t, double to, double ms, Func<double, double>? ease = null,
                             double delay = 0, double? from = null, Action? done = null) =>
            Tween.To(t, "X", () => t.X, x => t.X = x, to, ms, ease, delay, from, done);

        public static void Y(this TranslateTransform t, double to, double ms, Func<double, double>? ease = null,
                             double delay = 0, double? from = null, Action? done = null) =>
            Tween.To(t, "Y", () => t.Y, x => t.Y = x, to, ms, ease, delay, from, done);

        public static void Angle(this RotateTransform r, double to, double ms, Func<double, double>? ease = null,
                                 double delay = 0, double? from = null, Action? done = null) =>
            Tween.To(r, "Angle", () => r.Angle, x => r.Angle = x, to, ms, ease, delay, from, done);

        public static void DashOffset(this Shape s, double to, double ms, Func<double, double>? ease = null,
                                      double delay = 0, double? from = null, Action? done = null) =>
            Tween.To(s, "Dash", () => s.StrokeDashOffset, x => s.StrokeDashOffset = x, to, ms, ease, delay, from, done);

        public static void Top(this Control c, double to, double ms, Func<double, double>? ease = null,
                               double delay = 0, double? from = null, Action? done = null) =>
            Tween.To(c, "Top", () => Canvas.GetTop(c), x => Canvas.SetTop(c, x), to, ms, ease, delay, from, done);

        public static void Height(this Control c, double to, double ms, Func<double, double>? ease = null,
                                  double delay = 0, double? from = null, Action? done = null) =>
            Tween.To(c, "Height", () => double.IsNaN(c.Height) ? c.Bounds.Height : c.Height, x => c.Height = x, to, ms, ease, delay, from, done);

        public static Color WithAlpha(this Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

        public static Color Lighten(this Color c, double amount) => Color.FromRgb(
            (byte)(c.R + (255 - c.R) * amount), (byte)(c.G + (255 - c.G) * amount), (byte)(c.B + (255 - c.B) * amount));

        public static Color Darken(this Color c, double amount) => Color.FromRgb(
            (byte)(c.R * (1 - amount)), (byte)(c.G * (1 - amount)), (byte)(c.B * (1 - amount)));
    }
}
