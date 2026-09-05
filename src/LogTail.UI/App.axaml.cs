using System.IO;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using LogTail.Core.Logging;
using LogTail.Core.Models;
using LogTail.Core.Persistence;
using LogTail.Core.Sources;
using LogTail.UI.ViewModels;
using LogTail.UI.Views;
using ReactiveUI;

namespace LogTail.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Build dependencies.
            var logger = new ConsoleLogger();

            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "log-tail");
            Directory.CreateDirectory(appDataDir);

            var settings = new SettingsStore(appDataDir, logger);
            var factory = new LogSourceFactory(logger);

            var viewModel = new MainWindowViewModel(settings, factory);

            // Apply saved theme.
            var loaded = settings.Load();
            ApplyTheme(loaded.Theme);

            // Observe theme changes (marshal to UI thread so RequestedThemeVariant
            // is only mutated from the dispatcher that owns the Application).
            viewModel.WhenAnyValue(x => x.CurrentTheme)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(ApplyTheme);

            // Optional headless demo bypass: when LOGTAIL_AUTO_OPEN_FILE is set,
            // auto-tail that file at startup. No effect in normal use.
            // Bypass OpenFileCommand: it triggers ShowOpenFileDialog which has no
            // handler at this point (MainWindow.WhenActivated hasn't run yet).
            var autoOpen = Environment.GetEnvironmentVariable("LOGTAIL_AUTO_OPEN_FILE");
            if (!string.IsNullOrEmpty(autoOpen) && System.IO.File.Exists(autoOpen))
            {
                viewModel.CurrentFilePath = autoOpen;
                _ = viewModel.OpenFileAndAddTabAsync(autoOpen);
            }

            var mainWindow = new MainWindow
            {
                DataContext = viewModel
            };

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyTheme(ThemeMode mode)
    {
        RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}
