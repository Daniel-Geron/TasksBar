using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Threading;
using TasksBar.Services;
using TasksBar.ViewModels.Pages;
using TasksBar.ViewModels.Windows;
using TasksBar.Views.Pages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;

namespace TasksBar
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        // The.NET Generic Host provides dependency injection, configuration, logging, and other services.
        // https://docs.microsoft.com/dotnet/core/extensions/generic-host
        // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
        // https://docs.microsoft.com/dotnet/core/extensions/configuration
        // https://docs.microsoft.com/dotnet/core/extensions/logging
        private static readonly IHost _host = Host
            .CreateDefaultBuilder()
            .ConfigureAppConfiguration(c => { c.SetBasePath(Path.GetDirectoryName(AppContext.BaseDirectory)); })
            .ConfigureServices((context, services) =>
            {
                // --- CORE WPF UI SERVICES (REQUIRED FOR ENGINE) ---
                services.AddNavigationViewPageProvider();
                services.AddHostedService<ApplicationHostService>();
                services.AddSingleton<IThemeService, ThemeService>();
                services.AddSingleton<ITaskBarService, TaskBarService>();
                services.AddSingleton<INavigationService, NavigationService>();

                // --- YOUR APP WINDOWS ---
                services.AddSingleton<TasksFlyoutWindow>();

            }).Build();

        /// <summary>
        /// Gets services.
        /// </summary>
        public static IServiceProvider Services
        {
            get { return _host.Services; }
        }

        /// <summary>
        /// Occurs when the application is loading.
        /// </summary>
        private async void OnStartup(object sender, StartupEventArgs e)
        {
            Wpf.Ui.Appearance.ApplicationThemeManager.Changed += OnAppThemeChanged;
            await _host.StartAsync();
        }

        private void OnAppThemeChanged(Wpf.Ui.Appearance.ApplicationTheme theme, System.Windows.Media.Color accent)
        {
            // WPF UI makes a window's background transparent with SetCurrentValue, which the theme's
            // resource refresh silently undoes, leaving an opaque background with no Mica/Acrylic.
            // Re-apply every window's backdrop once that refresh has finished.
            Dispatcher.BeginInvoke(new Action(ReapplyBackdrops), DispatcherPriority.Loaded);
        }

        private static void ReapplyBackdrops()
        {
            var expected = AppConfig.Settings.UseAcrylic ? Wpf.Ui.Controls.WindowBackdropType.Acrylic : Wpf.Ui.Controls.WindowBackdropType.Mica;
            foreach (var window in Current.Windows.OfType<Wpf.Ui.Controls.FluentWindow>())
            {
                window.WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None;
                window.WindowBackdropType = expected;
                if (window is StickyNoteWindow note) note.UpdateInkColor();
            }
        }

        /// <summary>
        /// Occurs when the application is closing.
        /// </summary>
        private async void OnExit(object sender, ExitEventArgs e)
        {
            await _host.StopAsync();

            _host.Dispose();
        }

        /// <summary>
        /// Occurs when an exception is thrown by an application but not handled.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // For more info see https://docs.microsoft.com/en-us/dotnet/api/system.windows.application.dispatcherunhandledexception?view=windowsdesktop-6.0
        }
    }
}
