using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CustomLauncher.Core;
using CustomLauncher.ViewModels;
using CustomLauncher.Views;

namespace CustomLauncher;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = new AppPaths();
            var settings = new AppSettingsManager(paths);
            var logger = new DebugLogger(paths);
            var viewModel = new MainViewModel(paths, settings, logger);
            await viewModel.InitializeAsync();
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.Exit += async (_, _) => await viewModel.DisposeAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
