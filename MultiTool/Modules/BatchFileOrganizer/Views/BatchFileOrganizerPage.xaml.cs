using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MultiTool.Presentation.ViewModels;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using IDataObject = System.Windows.IDataObject;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

namespace MultiTool.Modules.BatchFileOrganizer.Views;

public partial class BatchFileOrganizerPage : UserControl
{
    private const string ReorderDataFormat = "MultiTool.BatchFileOrganizer.File";
    private Point _dragStart;
    private BatchFileItem? _draggedItem;
    private BatchFileItem? _dropTarget;

    public BatchFileOrganizerPage()
    {
        InitializeComponent();
    }

    private void HandleFolderDragOver(object sender, DragEventArgs eventArgs)
    {
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
        _draggedItem = FindAncestor<ListBoxItem>((DependencyObject)eventArgs.OriginalSource)?.DataContext
            as BatchFileItem;
    }

    private void HandleFileMouseMove(object sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.LeftButton != MouseButtonState.Pressed
            || DataContext is not BatchFileOrganizerViewModel { CanEdit: true })
        {
            return;
        }

        var current = eventArgs.GetPosition(FileList);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (_draggedItem is not BatchFileItem item
            || FileList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container)
        {
            return;
        }

        var data = new DataObject(ReorderDataFormat, item);
        DragDrop.DoDragDrop(container, data, DragDropEffects.Move);
        _draggedItem = null;
        ClearDropIndicator();
    }

    private void HandleFileDragOver(object sender, DragEventArgs eventArgs)
    {
        if (!eventArgs.Data.GetDataPresent(ReorderDataFormat))
        {
            return;
        }

        var container = FindAncestor<ListBoxItem>((DependencyObject)eventArgs.OriginalSource);
        if (container?.DataContext is not BatchFileItem target)
        {
            ClearDropIndicator();
            eventArgs.Effects = DragDropEffects.Move;
            eventArgs.Handled = true;
            return;
        }

        var position = eventArgs.GetPosition(container).Y < container.ActualHeight / 2
            ? FileDropPosition.Before
            : FileDropPosition.After;
        SetDropIndicator(target, position);
        eventArgs.Effects = DragDropEffects.Move;
        eventArgs.Handled = true;
    }

    private void HandleFileDrop(object sender, DragEventArgs eventArgs)
    {
        if (!eventArgs.Data.GetDataPresent(ReorderDataFormat)
            || eventArgs.Data.GetData(ReorderDataFormat) is not BatchFileItem source
            || DataContext is not BatchFileOrganizerViewModel viewModel)
        {
            return;
        }

        var insertionIndex = viewModel.Files.Count;
        if (_dropTarget is not null)
        {
            insertionIndex = viewModel.Files.IndexOf(_dropTarget);
            if (_dropTarget.DropPosition == FileDropPosition.After)
            {
                insertionIndex++;
            }
        }

        ClearDropIndicator();
        viewModel.MoveFile(source, insertionIndex);
        eventArgs.Effects = DragDropEffects.Move;
        eventArgs.Handled = true;
    }

    private void HandleFileDragLeave(object sender, DragEventArgs eventArgs)
    {
        if (!FileList.IsMouseOver)
        {
            ClearDropIndicator();
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
}
