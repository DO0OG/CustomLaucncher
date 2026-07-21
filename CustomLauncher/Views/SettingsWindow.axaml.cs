using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CustomLauncher.ViewModels;

namespace CustomLauncher.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();
    public SettingsWindow(SettingsViewModel viewModel) : this() => DataContext = viewModel;

    private async void BrowseFolderClicked(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Minecraft 설치 폴더 선택",
            AllowMultiple = false
        });
        if (folders.Count > 0 && DataContext is SettingsViewModel viewModel)
            viewModel.InstallPath = folders[0].Path.LocalPath;
    }

    private async void SaveClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel && await viewModel.SaveAsync()) Close(true);
    }
    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(false);
    private async void AboutClicked(object? sender, RoutedEventArgs e) => await new AboutWindow().ShowDialog(this);
}
