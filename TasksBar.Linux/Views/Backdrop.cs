using System;
using Avalonia.Controls;
using Avalonia.Media;

namespace TasksBar.Linux.Views
{
    /// <summary>
    /// The window background the Windows app gets from Mica/Acrylic: acrylic where the desktop can blur
    /// behind windows (KDE), otherwise the solid Windows 11 base color. Follows light/dark changes.
    /// </summary>
    public static class Backdrop
    {
        public static void Attach(Window window, Border chrome)
        {
            void Apply()
            {
                // Without blur, anything showing through looks like a glitch rather than acrylic, so stay solid
                var level = window.ActualTransparencyLevel;
                bool blurs = level == WindowTransparencyLevel.AcrylicBlur || level == WindowTransparencyLevel.Blur;
                double opacity = blurs && AppConfig.Settings.UseAcrylic ? 0.72 : 1;

                var baseColor = (window.FindResource(window.ActualThemeVariant, "SolidBackgroundFillColorBaseBrush") as ISolidColorBrush)?.Color
                                ?? Color.Parse("#202020");
                chrome.Background = new SolidColorBrush(baseColor, opacity);
            }

            window.Opened += (s, e) => Apply();
            window.PropertyChanged += (s, e) =>
            {
                if (e.Property == TopLevel.ActualTransparencyLevelProperty) Apply();
            };
            window.ActualThemeVariantChanged += (s, e) => Apply();
        }
    }
}
