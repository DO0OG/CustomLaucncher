using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CustomLauncher.ViewModels;

namespace CustomLauncher.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    public SettingsWindow(SettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
        // The view owns the file dialogs; the view models only ask for a path.
        viewModel.Java.FilePicker = () => PickFileAsync("Java 실행 파일 선택", null);
        if (viewModel.Modules is not null)
            viewModel.Modules.FilePicker = () => PickFileAsync("모드 jar 선택", ["*.jar"]);
        if (viewModel.Shaders is not null)
            viewModel.Shaders.FilePicker = () => PickFileAsync("셰이더팩 zip 선택", ["*.zip"]);
        if (viewModel.ResourcePacks is not null)
            viewModel.ResourcePacks.FilePicker = () => PickFileAsync("리소스팩 zip 선택", ["*.zip"]);
    }

    private async Task<string?> PickFileAsync(string title, string[]? patterns)
    {
        var options = new FilePickerOpenOptions { Title = title, AllowMultiple = false };
        if (patterns is not null)
            options.FileTypeFilter = [new FilePickerFileType(title) { Patterns = patterns }];
        var files = await StorageProvider.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

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
