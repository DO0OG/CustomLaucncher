using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using CustomLauncher.ViewModels;

namespace CustomLauncher.Views;

/// <summary>
/// Resource pack ordering. Drag-and-drop is the primary gesture; the ▲▼ buttons in the view are the
/// keyboard/accessibility equivalent, so neither is the only way to reorder.
/// </summary>
public partial class ResourcePackTab : UserControl
{
    private static readonly DataFormat<string> DragFormat =
        DataFormat.CreateStringApplicationFormat("customlauncher-resource-pack");

    private Point _pressPosition;
    private ResourcePackItem? _pressedItem;

    public ResourcePackTab()
    {
        InitializeComponent();
        PackList.AddHandler(PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        PackList.AddHandler(PointerMovedEvent, OnPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        PackList.AddHandler(PointerReleasedEvent, OnPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        PackList.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        PackList.AddHandler(DragDrop.DropEvent, OnDrop);
        PackList.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
    }

    private ResourcePackViewModel? ViewModel => PackList.DataContext as ResourcePackViewModel;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(PackList).Properties.IsLeftButtonPressed) return;
        _pressPosition = e.GetPosition(PackList);
        _pressedItem = ResolveItem(e.Source as Visual);
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedItem is null || !e.GetCurrentPoint(PackList).Properties.IsLeftButtonPressed) return;
        // Only start a drag once the pointer has clearly moved, so plain clicks still select.
        var delta = e.GetPosition(PackList) - _pressPosition;
        if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4) return;

        var item = _pressedItem;
        _pressedItem = null;
        using var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DragFormat, item.Id));
        try
        {
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        finally
        {
            ViewModel?.SetDropTarget(null);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e) => _pressedItem = null;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var index = ResolveIndex(e);
        e.DragEffects = index is null ? DragDropEffects.None : DragDropEffects.Move;
        ViewModel?.SetDropTarget(index);
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => ViewModel?.SetDropTarget(null);

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        ViewModel?.SetDropTarget(null);
        var id = e.DataTransfer.TryGetValue(DragFormat);
        var index = ResolveIndex(e);
        e.Handled = true;
        if (ViewModel is null || string.IsNullOrEmpty(id) || index is null) return;
        await ViewModel.MoveToAsync(id, index.Value);
    }

    /// <summary>Index of the row under the pointer, or the last row when dropped past the end.</summary>
    private int? ResolveIndex(DragEventArgs e)
    {
        if (ViewModel is null || ViewModel.Packs.Count == 0) return null;
        var target = ResolveItem(e.Source as Visual);
        if (target is not null) return ViewModel.Packs.IndexOf(target);
        return e.GetPosition(PackList).Y > 0 ? ViewModel.Packs.Count - 1 : 0;
    }

    private static ResourcePackItem? ResolveItem(Visual? source) =>
        source?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext as ResourcePackItem;
}
