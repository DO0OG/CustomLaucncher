using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CustomLauncher.ViewModels;

namespace CustomLauncher.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm) vm.SettingsRequested += ShowSettings;
        };
        Activated += (_, _) => { if (DataContext is MainViewModel vm) vm.WindowActive = true; };
        Deactivated += (_, _) => { if (DataContext is MainViewModel vm) vm.WindowActive = false; };
    }

    private void TitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private void CloseClicked(object? sender, RoutedEventArgs e) => Close();

    private async void CopyDeviceCodeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && Clipboard is not null)
            await Clipboard.SetTextAsync(vm.DeviceCode);
    }

    private void OpenDeviceCodeUrlClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && Uri.TryCreate(vm.DeviceCodeUrl, UriKind.Absolute, out var uri))
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private async void ShowSettings(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        // The settings view model owns an HttpClient and cancellation scopes for the content tabs.
        using var settings = vm.CreateSettingsViewModel();
        var window = new SettingsWindow(settings);
        await window.ShowDialog<bool>(this);
    }
}
