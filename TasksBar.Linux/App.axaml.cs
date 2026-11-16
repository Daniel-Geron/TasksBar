using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using TasksBar.Linux.Effects;
using TasksBar.Linux.Views;

namespace TasksBar.Linux
{
    public partial class App : Application
    {
        private FlyoutWindow? _flyout;

        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            AppConfig.Load();
            TasksBar.Services.StickyNoteSync.Host = new Services.AvaloniaStickyNoteHost();
            ApplyTheme();
            ApplyAccent();

            // The desktop's accent color and light/dark preference can change while TasksBar runs
            if (PlatformSettings != null)
                PlatformSettings.ColorValuesChanged += (s, e) => ApplyAccent();

            _flyout = new FlyoutWindow();

            // Commands from `tasksbar --new-note` etc. (desktop shortcuts, the app menu, a second launch)
            Services.InstanceChannel.CommandReceived += RunCommand;
            Services.InstanceChannel.StartListening();
            Services.Autostart.Refresh();
            System.Threading.Tasks.Task.Run(Services.GlobalShortcuts.Initialize);

            if (Program.StartupCommand is { } command && command != Services.InstanceChannel.Show && command != Services.InstanceChannel.Toggle)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => RunCommand(command), Avalonia.Threading.DispatcherPriority.Background);
            else
                _flyout.ShowFlyout();

            base.OnFrameworkInitializationCompleted();
        }

        public FlyoutWindow? Flyout => _flyout;

        /// <summary>0 = follow the system, 1 = light, 2 = dark (same setting as the Windows app).</summary>
        public void ApplyTheme()
        {
            RequestedThemeVariant = AppConfig.Settings.AppTheme switch
            {
                1 => ThemeVariant.Light,
                2 => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }

        /// <summary>
        /// Uses the desktop's accent color (GNOME 47+ and KDE expose it), like the Windows app follows
        /// the Windows accent. As in Windows 11, dark mode uses a lighter shade and light mode a darker one.
        /// </summary>
        public void ApplyAccent()
        {
            var accent = PlatformSettings?.GetColorValues().AccentColor1 ?? Color.Parse("#0078D4");
            if (accent.A == 0) accent = Color.Parse("#0078D4");

            SetThemeBrush(ThemeVariant.Dark, "AccentFillColorDefaultBrush", accent.Lighten(0.42));
            SetThemeBrush(ThemeVariant.Dark, "TextOnAccentFillColorPrimaryBrush", Colors.Black);
            SetThemeBrush(ThemeVariant.Light, "AccentFillColorDefaultBrush", accent.Darken(0.12));
            SetThemeBrush(ThemeVariant.Light, "TextOnAccentFillColorPrimaryBrush", Colors.White);

            // Avalonia's own controls (focus rings, toggles) pick up the same accent
            Resources["SystemAccentColor"] = accent;
        }

        private void SetThemeBrush(ThemeVariant variant, string key, Color color)
        {
            if (Resources.ThemeDictionaries.TryGetValue(variant, out var provider) && provider is IResourceDictionary dictionary)
                dictionary[key] = new SolidColorBrush(color);
        }

        private void TrayIcon_Clicked(object? sender, EventArgs e) => _flyout?.ToggleFlyout();

        private void RunCommand(string command)
        {
            if (command == Services.InstanceChannel.Show) _flyout?.ShowFlyout();
            else if (command == Services.InstanceChannel.Toggle) _flyout?.ToggleFlyout();
            else Services.GlobalShortcuts.RunAction(command);
        }

        public void Quit()
        {
            Services.InstanceChannel.Stop();
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
        }
    }
}
