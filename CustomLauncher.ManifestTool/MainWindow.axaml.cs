using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace CustomLauncher.ManifestTool;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ManifestToolViewModel viewModel)
                viewModel.FolderPicker = PickFolderAsync;
        };
    }

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "배포 폴더 선택",
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    private async void BrowseClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ManifestToolViewModel viewModel) await viewModel.BrowseAsync();
    }

    private async void GenerateClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ManifestToolViewModel viewModel) await viewModel.GenerateAsync();
    }
}
