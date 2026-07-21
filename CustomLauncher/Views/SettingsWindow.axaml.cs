using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CustomLauncher.Models;

namespace CustomLauncher.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();
    public SettingsWindow(LauncherSettings settings) : this() => DataContext = settings;

    private async void BrowseFolderClicked(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Minecraft 설치 폴더 선택",
            AllowMultiple = false
        });
        if (folders.Count > 0 && DataContext is LauncherSettings settings)
            settings.InstallPath = folders[0].Path.LocalPath;
    }

    private void SaveClicked(object? sender, RoutedEventArgs e) => Close();
    private async void AboutClicked(object? sender, RoutedEventArgs e) => await new AboutWindow().ShowDialog(this);
}
