using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MultiTool.Presentation.ViewModels;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using IDataObject = System.Windows.IDataObject;
using InputCursors = System.Windows.Input.Cursors;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

namespace MultiTool.Modules.BatchFileOrganizer.Views;

public partial class BatchFileOrganizerPage : UserControl
{
    private Point _dragStart;
    private BatchFileItem? _dragCandidate;
    private BatchFileItem[] _draggedItems = [];
    private BatchFileItem? _dropTarget;
    private ScrollViewer? _fileScrollViewer;
    private bool _isReordering;
    private bool _isPointerInsideFileList;

    public BatchFileOrganizerPage()
    {
        InitializeComponent();
    }

    private void HandleFolderDragOver(object sender, DragEventArgs eventArgs)
    {
        if (DataContext is BatchFileOrganizerViewModel { HasOpenDialog: true })
        {
            eventArgs.Effects = DragDropEffects.None;
            eventArgs.Handled = true;
            return;
        }

        if (!eventArgs.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        eventArgs.Effects = IsOneFolder(eventArgs.Data)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        eventArgs.Handled = true;
    }

    private async void HandleFolderDrop(object sender, DragEventArgs eventArgs)
    {
        if (DataContext is BatchFileOrganizerViewModel { HasOpenDialog: true })
        {
            eventArgs.Effects = DragDropEffects.None;
            eventArgs.Handled = true;
            return;
        }

        if (!eventArgs.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        eventArgs.Handled = true;
        if (DataContext is not BatchFileOrganizerViewModel viewModel)
        {
            return;
        }

        var paths = eventArgs.Data.GetData(DataFormats.FileDrop) as string[];
        if (paths is not { Length: 1 } || !Directory.Exists(paths[0]))
        {
            viewModel.ReportFolderDropError("Drop exactly one existing folder, not individual files.");
            return;
        }

        await viewModel.LoadFolderAsync(paths[0]);
    }

    private void HandleFileMouseDown(object sender, MouseButtonEventArgs eventArgs)
    {
        _dragStart = eventArgs.GetPosition(FileList);
        var container = FindAncestor<ListBoxItem>((DependencyObject)eventArgs.OriginalSource);
        _dragCandidate = container?.DataContext as BatchFileItem;

        if (_dragCandidate is not null
            && FileList.SelectedItems.Contains(_dragCandidate)
            && FileList.SelectedItems.Count > 1
            && Keyboard.Modifiers == ModifierKeys.None)
        {
            container?.Focus();
            eventArgs.Handled = true;
        }
    }

    private void HandleFileMouseMove(object sender, MouseEventArgs eventArgs)
    {
        if (DataContext is not BatchFileOrganizerViewModel { CanEdit: true } viewModel)
        {
            CancelReorder();
            return;
        }

        var current = eventArgs.GetPosition(FileList);
        if (!_isReordering)
        {
            if (eventArgs.LeftButton != MouseButtonState.Pressed
                || (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
                    && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance))
            {
                return;
            }

            BeginReorder(viewModel);
        }

        if (!_isReordering)
        {
            return;
        }

        UpdateDropTarget(current);
        eventArgs.Handled = true;
    }

    private void HandleFileKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape && _isReordering)
        {
            CancelReorder();
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key != Key.Delete
            || DataContext is not BatchFileOrganizerViewModel { CanEdit: true } viewModel)
        {
            return;
        }

        var selectedFiles = FileList.SelectedItems
            .OfType<BatchFileItem>()
            .ToArray();
        if (selectedFiles.Length == 0)
        {
            return;
        }

        viewModel.RequestDeleteFiles(selectedFiles);
        eventArgs.Handled = viewModel.IsDeleteConfirmationOpen;
    }

    private void HandleFileMouseUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (!_isReordering)
        {
            _dragCandidate = null;
            return;
        }

        CompleteReorder();
        eventArgs.Handled = true;
    }

    private void HandleFileMouseWheel(object sender, MouseWheelEventArgs eventArgs)
    {
        if (!_isReordering)
        {
            return;
        }

        _fileScrollViewer ??= FindDescendant<ScrollViewer>(FileList);
        if (_fileScrollViewer is null)
        {
            return;
        }

        var distancePerNotch = SystemParameters.WheelScrollLines < 0
            ? _fileScrollViewer.ViewportHeight
            : Math.Max(1, SystemParameters.WheelScrollLines) * 48.0;
        var offset = _fileScrollViewer.VerticalOffset - (eventArgs.Delta / 120.0 * distancePerNotch);
        _fileScrollViewer.ScrollToVerticalOffset(offset);
        FileList.UpdateLayout();
        UpdateDropTarget(Mouse.GetPosition(FileList));
        eventArgs.Handled = true;
    }

    private void HandleFileLostMouseCapture(object sender, MouseEventArgs eventArgs)
    {
        if (_isReordering && !ReferenceEquals(Mouse.Captured, FileList))
        {
            CancelReorder();
        }
    }

    private void BeginReorder(BatchFileOrganizerViewModel viewModel)
    {
        if (_dragCandidate is not BatchFileItem item)
        {
            return;
        }

        var selectedItems = FileList.SelectedItems
            .OfType<BatchFileItem>()
            .ToHashSet();
        if (!selectedItems.Contains(item))
        {
            selectedItems = [item];
        }

        _draggedItems = viewModel.Files
            .Where(selectedItems.Contains)
            .ToArray();
        if (_draggedItems.Length == 0)
        {
            return;
        }

        _isReordering = true;
        Mouse.OverrideCursor = InputCursors.SizeAll;
        if (!Mouse.Capture(FileList, CaptureMode.SubTree))
        {
            EndReorder();
        }
    }

    private void UpdateDropTarget(Point position)
    {
        _isPointerInsideFileList = position.X >= 0
            && position.X <= FileList.ActualWidth
            && position.Y >= 0
            && position.Y <= FileList.ActualHeight;
        if (!_isPointerInsideFileList)
        {
            ClearDropIndicator();
            return;
        }

        var hit = FileList.InputHitTest(position) as DependencyObject;
        var container = FindAncestor<ListBoxItem>(hit);
        if (container?.DataContext is not BatchFileItem target)
        {
            ClearDropIndicator();
            return;
        }

        var dropPosition = position.X - container.TranslatePoint(new Point(), FileList).X
            < container.ActualWidth / 2
            ? FileDropPosition.Before
            : FileDropPosition.After;
        SetDropIndicator(target, dropPosition);
    }

    private void CompleteReorder()
    {
        if (DataContext is not BatchFileOrganizerViewModel viewModel
            || !_isPointerInsideFileList
            || _draggedItems.Length == 0)
        {
            CancelReorder();
            return;
        }

        var draggedItems = _draggedItems;
        var selectedFile = viewModel.SelectedFile;
        var insertionIndex = viewModel.Files.Count;
        if (_dropTarget is not null)
        {
            insertionIndex = viewModel.Files.IndexOf(_dropTarget);
            if (_dropTarget.DropPosition == FileDropPosition.After)
            {
                insertionIndex++;
            }
        }

        EndReorder();
        viewModel.MoveFiles(draggedItems, insertionIndex);
        FileList.SelectedItems.Clear();
        foreach (var source in viewModel.Files.Where(draggedItems.Contains))
        {
            FileList.SelectedItems.Add(source);
        }

        if (selectedFile is not null && draggedItems.Contains(selectedFile))
        {
            viewModel.SelectedFile = selectedFile;
        }
    }

    private void CancelReorder()
    {
        if (_isReordering)
        {
            EndReorder();
        }
        else
        {
            _dragCandidate = null;
        }
    }

    private void EndReorder()
    {
        _isReordering = false;
        _isPointerInsideFileList = false;
        _dragCandidate = null;
        _draggedItems = [];
        ClearDropIndicator();
        Mouse.OverrideCursor = null;
        if (ReferenceEquals(Mouse.Captured, FileList))
        {
            Mouse.Capture(null);
        }
    }

    private void SetDropIndicator(BatchFileItem target, FileDropPosition position)
    {
        if (!ReferenceEquals(_dropTarget, target))
        {
            ClearDropIndicator();
            _dropTarget = target;
        }

        target.DropPosition = position;
    }

    private void ClearDropIndicator()
    {
        if (_dropTarget is not null)
        {
            _dropTarget.DropPosition = FileDropPosition.None;
            _dropTarget = null;
        }
    }

    private static bool IsOneFolder(IDataObject data)
    {
        var paths = data.GetData(DataFormats.FileDrop) as string[];
        return paths is { Length: 1 } && Directory.Exists(paths[0]);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject current) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
        {
            var child = VisualTreeHelper.GetChild(current, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
