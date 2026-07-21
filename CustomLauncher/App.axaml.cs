using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CustomLauncher.Core;
using CustomLauncher.ViewModels;
using CustomLauncher.Views;

namespace CustomLauncher;

public partial class App : Application
{
    private DebugLogger? _logger;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = new AppPaths();
            var settings = new AppSettingsManager(paths);
            _logger = new DebugLogger(paths);
            RegisterUnhandledExceptionLogging();
            var viewModel = new MainViewModel(paths, settings, _logger);
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.Exit += (_, _) => _ = DisposeViewModelAsync(viewModel);
            _ = InitializeViewModelAsync(viewModel);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeViewModelAsync(MainViewModel viewModel)
    {
        try { await viewModel.InitializeAsync(); }
        catch (Exception exception)
        {
            if (_logger is not null)
                await _logger.WriteAsync(LauncherLogLevel.Error, "Unhandled startup failure", exception);
        }
    }

    private async Task DisposeViewModelAsync(MainViewModel viewModel)
    {
        try { await viewModel.DisposeAsync(); }
        catch (Exception exception)
        {
            if (_logger is not null)
                await _logger.WriteAsync(LauncherLogLevel.Error, "Application shutdown failure", exception);
        }
    }

    private void RegisterUnhandledExceptionLogging()
    {
        // Commands route failures here when the view model supplied no local handler.
        ViewModels.AsyncCommand.UnhandledError += exception =>
            _ = _logger?.WriteAsync(LauncherLogLevel.Error, "Command failed", exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception
                ?? new InvalidOperationException(args.ExceptionObject?.ToString() ?? "Unknown fatal error");
            _logger?.WriteAsync(LauncherLogLevel.Error, "Unhandled application exception", exception)
                .GetAwaiter().GetResult();
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _ = _logger?.WriteAsync(LauncherLogLevel.Error, "Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }
}
