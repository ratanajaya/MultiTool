using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using MultiTool.Modules.BatchFileOrganizer;
using MultiTool.Presentation.Commands;

namespace MultiTool.Presentation.ViewModels;

public sealed class BatchFileOrganizerViewModel : INotifyPropertyChanged
{
    private static readonly HashSet<string> SupportedImageExtensions = new(
        [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".ico", ".webp"],
        StringComparer.OrdinalIgnoreCase);

    private readonly IBatchFileSystem _fileSystem;
    private readonly BatchFileRenameService _renameService;
    private readonly IBatchFileOrganizerFolderPicker _folderPicker;
    private readonly IImagePreviewLoader _previewLoader;
    private readonly AsyncRelayCommand _browseCommand;
    private readonly AsyncRelayCommand _reloadCommand;
    private readonly AsyncRelayCommand _openRenameDialogCommand;
    private readonly AsyncRelayCommand _closeRenameDialogCommand;
    private readonly AsyncRelayCommand _renameCommand;
    private readonly AsyncRelayCommand _confirmDeleteCommand;
    private readonly AsyncRelayCommand _cancelDeleteCommand;
    private string _folderPath = string.Empty;
    private BatchFileItem? _selectedFile;
    private string _prefix = "File_";
    private string _startNumberText = "1";
    private string _paddingText = "3";
    private string _status = "Drop a folder here or choose Browse.";
    private string _validationMessage = string.Empty;
    private string _previewMessage = "Select an image to preview it.";
    private ImageSource? _previewSource;
    private bool _isBusy;
    private bool _isPreviewLoading;
    private bool _isRenameDialogOpen;
    private string _renameOperationMessage = string.Empty;
    private bool _isDeleteConfirmationOpen;
    private string _deleteConfirmationHeading = string.Empty;
    private string _deleteConfirmationMessage = string.Empty;
    private IReadOnlyList<BatchFileItem> _pendingDeleteFiles = [];
    private BatchRenamePlan _currentPlan = new();
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _thumbnailCancellation;
    private CancellationTokenSource? _renameCancellation;
    private Task<BatchRenameResult>? _activeRenameTask;
    private CancellationTokenSource? _deleteCancellation;
    private Task? _activeDeleteTask;
    private int _sessionGeneration;

    public BatchFileOrganizerViewModel(
        IBatchFileSystem fileSystem,
        BatchFileRenameService renameService,
        IBatchFileOrganizerFolderPicker folderPicker,
        IImagePreviewLoader previewLoader)
    {
        _fileSystem = fileSystem;
        _renameService = renameService;
        _folderPicker = folderPicker;
        _previewLoader = previewLoader;
        _browseCommand = new AsyncRelayCommand(BrowseAsync, () => !IsBusy && !HasOpenDialog);
        _reloadCommand = new AsyncRelayCommand(
            ReloadAsync,
            () => HasFolder && !IsBusy && !HasOpenDialog);
        _openRenameDialogCommand = new AsyncRelayCommand(
            OpenRenameDialogAsync,
            () => CanOpenRenameDialog);
        _closeRenameDialogCommand = new AsyncRelayCommand(
            CloseRenameDialogAsync,
            () => IsRenameDialogOpen && !IsBusy);
        _renameCommand = new AsyncRelayCommand(RenameAsync, () => CanRename);
        _confirmDeleteCommand = new AsyncRelayCommand(DeleteConfirmedAsync, () => CanDelete);
        _cancelDeleteCommand = new AsyncRelayCommand(
            CancelDeleteAsync,
            () => IsDeleteConfirmationOpen && !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<BatchFileItem> Files { get; } = [];

    public string FolderPath
    {
        get => _folderPath;
        private set
        {
            if (SetField(ref _folderPath, value))
            {
                OnPropertyChanged(nameof(HasFolder));
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanOpenRenameDialog));
                OnPropertyChanged(nameof(CanDelete));
                RaiseCommandStates();
            }
        }
    }

    public bool HasFolder => !string.IsNullOrWhiteSpace(FolderPath);

    public string FileCountText => Files.Count == 1 ? "1 file" : $"{Files.Count} files";

    public BatchFileItem? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (SetField(ref _selectedFile, value))
            {
                StartPreview(value);
            }
        }
    }

    public string Prefix
    {
        get => _prefix;
        set
        {
            if (SetField(ref _prefix, value))
            {
                RecomputePlan();
            }
        }
    }

    public string StartNumberText
    {
        get => _startNumberText;
        set
        {
            if (SetField(ref _startNumberText, value))
            {
                RecomputePlan();
            }
        }
    }

    public string PaddingText
    {
        get => _paddingText;
        set
        {
            if (SetField(ref _paddingText, value))
            {
                RecomputePlan();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetField(ref _validationMessage, value);
    }

    public string PreviewMessage
    {
        get => _previewMessage;
        private set => SetField(ref _previewMessage, value);
    }

    public ImageSource? PreviewSource
    {
        get => _previewSource;
        private set
        {
            if (SetField(ref _previewSource, value))
            {
                OnPropertyChanged(nameof(HasPreview));
            }
        }
    }

    public bool HasPreview => PreviewSource is not null;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanRename));
                OnPropertyChanged(nameof(CanOpenRenameDialog));
                OnPropertyChanged(nameof(CanDelete));
                RaiseCommandStates();
            }
        }
    }

    public bool IsPreviewLoading
    {
        get => _isPreviewLoading;
        private set => SetField(ref _isPreviewLoading, value);
    }

    public bool IsRenameDialogOpen
    {
        get => _isRenameDialogOpen;
        private set
        {
            if (SetField(ref _isRenameDialogOpen, value))
            {
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanRename));
                OnPropertyChanged(nameof(CanOpenRenameDialog));
                OnPropertyChanged(nameof(HasOpenDialog));
                RaiseCommandStates();
            }
        }
    }

    public string RenameOperationMessage
    {
        get => _renameOperationMessage;
        private set => SetField(ref _renameOperationMessage, value);
    }

    public bool IsDeleteConfirmationOpen
    {
        get => _isDeleteConfirmationOpen;
        private set
        {
            if (SetField(ref _isDeleteConfirmationOpen, value))
            {
                OnPropertyChanged(nameof(HasOpenDialog));
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanOpenRenameDialog));
                OnPropertyChanged(nameof(CanDelete));
                RaiseCommandStates();
            }
        }
    }

    public string DeleteConfirmationHeading
    {
        get => _deleteConfirmationHeading;
        private set => SetField(ref _deleteConfirmationHeading, value);
    }

    public string DeleteConfirmationMessage
    {
        get => _deleteConfirmationMessage;
        private set => SetField(ref _deleteConfirmationMessage, value);
    }

    public bool HasOpenDialog => IsRenameDialogOpen || IsDeleteConfirmationOpen;

    public bool CanEdit => HasFolder && !IsBusy && !HasOpenDialog;

    public bool CanOpenRenameDialog =>
        HasFolder
        && Files.Count > 0
        && !IsBusy
        && !HasOpenDialog;

    public bool CanRename =>
        HasFolder
        && Files.Count > 0
        && !IsBusy
        && IsRenameDialogOpen
        && _currentPlan.IsValid
        && _currentPlan.HasChanges
        && string.IsNullOrWhiteSpace(ValidationMessage);

    public bool CanDelete =>
        IsDeleteConfirmationOpen
        && _pendingDeleteFiles.Count > 0
        && !IsBusy;

    public ICommand BrowseCommand => _browseCommand;

    public ICommand ReloadCommand => _reloadCommand;

    public ICommand OpenRenameDialogCommand => _openRenameDialogCommand;

    public ICommand CloseRenameDialogCommand => _closeRenameDialogCommand;

    public ICommand RenameCommand => _renameCommand;

    public ICommand ConfirmDeleteCommand => _confirmDeleteCommand;

    public ICommand CancelDeleteCommand => _cancelDeleteCommand;

    public async Task LoadFolderAsync(string folderPath)
    {
        if (IsBusy)
        {
            Status = "Wait for the current file operation to finish.";
            return;
        }

        if (string.IsNullOrWhiteSpace(folderPath) || !_fileSystem.DirectoryExists(folderPath))
        {
            ReportFolderDropError("Drop one existing folder, not an individual file.");
            return;
        }

        var fullPath = Path.GetFullPath(folderPath);
        IsRenameDialogOpen = false;
        RenameOperationMessage = string.Empty;
        ClearDeleteConfirmation();
        var generation = ++_sessionGeneration;
        CancelPreview();
        Status = "Loading folder...";
        try
        {
            var paths = await Task.Run(() => _fileSystem.EnumerateTopLevelFiles(fullPath));
            if (generation != _sessionGeneration)
            {
                return;
            }

            ReplaceFiles(fullPath, paths);
            Status = Files.Count == 0
                ? "The selected folder contains no files."
                : $"Loaded {Files.Count} file(s). Use Ctrl or Shift to select multiple files, then drag tiles to set their order.";
        }
        catch (Exception exception)
        {
            if (generation == _sessionGeneration)
            {
                ClearFolderState();
                Status = $"The folder could not be loaded: {exception.Message}";
            }
        }
    }

    public void MoveFile(BatchFileItem item, int insertionIndex) =>
        MoveFiles([item], insertionIndex);

    public void MoveFiles(IEnumerable<BatchFileItem> items, int insertionIndex)
    {
        if (!CanEdit)
        {
            return;
        }

        var orderedItems = items
            .Distinct()
            .Select(item => new { Item = item, Index = Files.IndexOf(item) })
            .Where(entry => entry.Index >= 0)
            .OrderBy(entry => entry.Index)
            .ToArray();
        if (orderedItems.Length == 0)
        {
            return;
        }

        var boundedInsertionIndex = Math.Clamp(insertionIndex, 0, Files.Count);
        var adjustedInsertionIndex = boundedInsertionIndex
            - orderedItems.Count(entry => entry.Index < boundedInsertionIndex);
        var movedItems = orderedItems.Select(entry => entry.Item).ToArray();
        var movedItemSet = movedItems.ToHashSet();
        var desiredOrder = Files.Where(item => !movedItemSet.Contains(item)).ToList();
        adjustedInsertionIndex = Math.Clamp(adjustedInsertionIndex, 0, desiredOrder.Count);
        desiredOrder.InsertRange(adjustedInsertionIndex, movedItems);
        if (Files.SequenceEqual(desiredOrder))
        {
            return;
        }

        for (var index = 0; index < desiredOrder.Count; index++)
        {
            var currentIndex = Files.IndexOf(desiredOrder[index]);
            if (currentIndex != index)
            {
                Files.Move(currentIndex, index);
            }
        }

        if (SelectedFile is null || !movedItemSet.Contains(SelectedFile))
        {
            SelectedFile = movedItems[0];
        }

        RecomputePlan();
        Status = movedItems.Length == 1
            ? "Order updated. Files will be numbered in tile order."
            : $"Moved {movedItems.Length} files. Files will be numbered in tile order.";
    }

    public void ReportFolderDropError(string message) => Status = message;

    public void RequestDeleteFiles(IEnumerable<BatchFileItem> files)
    {
        if (!CanEdit)
        {
            return;
        }

        var requestedFiles = files.ToHashSet();
        var orderedFiles = Files.Where(requestedFiles.Contains).ToArray();
        if (orderedFiles.Length == 0)
        {
            return;
        }

        _pendingDeleteFiles = orderedFiles;
        DeleteConfirmationHeading = orderedFiles.Length == 1
            ? "Delete this file?"
            : $"Delete {orderedFiles.Length} files?";
        var displayedNames = orderedFiles
            .Take(4)
            .Select(file => $"• {file.CurrentName}")
            .ToList();
        if (orderedFiles.Length > displayedNames.Count)
        {
            displayedNames.Add($"• and {orderedFiles.Length - displayedNames.Count} more");
        }

        DeleteConfirmationMessage =
            $"These files will be permanently deleted:{Environment.NewLine}{Environment.NewLine}"
            + string.Join(Environment.NewLine, displayedNames)
            + $"{Environment.NewLine}{Environment.NewLine}This cannot be undone.";
        IsDeleteConfirmationOpen = true;
    }

    public void LeaveModule()
    {
        _sessionGeneration++;
        CancelPreview();
        CancelThumbnailLoading();
        _renameCancellation?.Cancel();
        _deleteCancellation?.Cancel();
        ClearFolderState();
        Status = "Drop a folder here or choose Browse.";
    }

    public async Task ShutdownAsync()
    {
        LeaveModule();
        var activeRename = _activeRenameTask;
        if (activeRename is not null)
        {
            try
            {
                await activeRename;
            }
            catch
            {
                // RenameCore converts move failures into a result. Shutdown must still continue
                // if validation or another unexpected failure escapes before that point.
            }
        }

        var activeDelete = _activeDeleteTask;
        if (activeDelete is not null)
        {
            try
            {
                await activeDelete;
            }
            catch
            {
                // Delete failures are reported per file. Shutdown must continue if an
                // unexpected failure escapes before those results are produced.
            }
        }
    }

    private async Task BrowseAsync()
    {
        try
        {
            var selectedFolder = _folderPicker.SelectFolder();
            if (!string.IsNullOrWhiteSpace(selectedFolder))
            {
                await LoadFolderAsync(selectedFolder);
            }
        }
        catch (Exception exception)
        {
            Status = $"The folder picker could not be opened: {exception.Message}";
        }
    }

    private Task ReloadAsync() => LoadFolderAsync(FolderPath);

    private Task OpenRenameDialogAsync()
    {
        RecomputePlan();
        RenameOperationMessage = string.Empty;
        IsRenameDialogOpen = true;
        return Task.CompletedTask;
    }

    private Task CloseRenameDialogAsync()
    {
        IsRenameDialogOpen = false;
        RenameOperationMessage = string.Empty;
        return Task.CompletedTask;
    }

    private Task CancelDeleteAsync()
    {
        ClearDeleteConfirmation();
        return Task.CompletedTask;
    }

    private async Task DeleteConfirmedAsync()
    {
        if (!CanDelete)
        {
            return;
        }

        var files = _pendingDeleteFiles.ToArray();
        var generation = _sessionGeneration;
        var firstIndex = files
            .Select(Files.IndexOf)
            .Where(index => index >= 0)
            .DefaultIfEmpty(0)
            .Min();
        CancelPreview();
        CancelThumbnailLoading();
        _deleteCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _deleteCancellation = cancellation;
        IsBusy = true;
        Status = files.Length == 1 ? "Deleting file..." : $"Deleting {files.Length} files...";
        try
        {
            var deleteTask = Task.Run(
                () => DeleteFiles(files, cancellation.Token),
                CancellationToken.None);
            _activeDeleteTask = deleteTask;
            var results = await deleteTask;
            if (generation != _sessionGeneration)
            {
                return;
            }

            var deletedFiles = results
                .Where(result => result.Error is null)
                .Select(result => result.File)
                .ToArray();
            var failedFiles = results
                .Where(result => result.Error is not null)
                .ToArray();

            SelectedFile = null;
            foreach (var file in deletedFiles)
            {
                Files.Remove(file);
            }

            OnPropertyChanged(nameof(FileCountText));
            OnPropertyChanged(nameof(CanOpenRenameDialog));
            RecomputePlan();
            StartThumbnailLoading(Files.ToArray());

            var failedSelection = failedFiles
                .Select(result => result.File)
                .FirstOrDefault(Files.Contains);
            var nextSelection = failedSelection ?? Files.ElementAtOrDefault(
                Math.Min(firstIndex, Math.Max(0, Files.Count - 1)));
            if (nextSelection is null)
            {
                PreviewSource = null;
                PreviewMessage = "This folder contains no files.";
            }
            else
            {
                SelectedFile = nextSelection;
            }

            Status = CreateDeleteResultMessage(deletedFiles.Length, failedFiles);
            ClearDeleteConfirmation();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            IsBusy = false;
            if (ReferenceEquals(_deleteCancellation, cancellation))
            {
                _deleteCancellation = null;
            }

            cancellation.Dispose();
            _activeDeleteTask = null;
        }
    }

    private List<(BatchFileItem File, string? Error)> DeleteFiles(
        IReadOnlyList<BatchFileItem> files,
        CancellationToken cancellationToken)
    {
        var results = new List<(BatchFileItem File, string? Error)>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _fileSystem.DeleteFile(file.CurrentPath);
                results.Add((file, null));
            }
            catch (Exception exception)
            {
                results.Add((file, exception.Message));
            }
        }

        return results;
    }

    private static string CreateDeleteResultMessage(
        int deletedCount,
        IReadOnlyList<(BatchFileItem File, string? Error)> failedFiles)
    {
        if (failedFiles.Count == 0)
        {
            return deletedCount == 1 ? "Deleted 1 file." : $"Deleted {deletedCount} files.";
        }

        var failureDetails = string.Join(
            "; ",
            failedFiles.Take(3).Select(result => $"{result.File.CurrentName}: {result.Error}"));
        var summary = deletedCount == 0
            ? $"No files were deleted. {failedFiles.Count} failed."
            : $"Deleted {deletedCount} file(s); {failedFiles.Count} failed.";
        return $"{summary} {failureDetails}";
    }

    private void ClearDeleteConfirmation()
    {
        _pendingDeleteFiles = [];
        DeleteConfirmationHeading = string.Empty;
        DeleteConfirmationMessage = string.Empty;
        IsDeleteConfirmationOpen = false;
    }

    private async Task RenameAsync()
    {
        RecomputePlan();
        if (!CanRename)
        {
            return;
        }

        var folderPath = FolderPath;
        var plan = _currentPlan;
        var generation = _sessionGeneration;
        CancelPreview();
        _renameCancellation?.Dispose();
        _renameCancellation = new CancellationTokenSource();
        IsBusy = true;
        Status = "Renaming files...";
        RenameOperationMessage = "Renaming files...";
        try
        {
            var renameTask = _renameService.RenameAsync(
                folderPath,
                plan,
                _renameCancellation.Token);
            _activeRenameTask = renameTask;
            var result = await renameTask;
            if (generation != _sessionGeneration)
            {
                return;
            }

            if (result.Succeeded)
            {
                for (var index = 0; index < Files.Count; index++)
                {
                    Files[index].ApplyRename(plan.Requests[index].DestinationPath);
                }

                RecomputePlan();
                Status = result.Message;
                RenameOperationMessage = string.Empty;
                IsRenameDialogOpen = false;
                StartPreview(SelectedFile);
            }
            else
            {
                await ReloadAfterFailureAsync(folderPath, result.Message, generation);
            }
        }
        catch (Exception exception)
        {
            if (generation == _sessionGeneration)
            {
                await ReloadAfterFailureAsync(folderPath, exception.Message, generation);
            }
        }
        finally
        {
            IsBusy = false;
            _renameCancellation?.Dispose();
            _renameCancellation = null;
            _activeRenameTask = null;
        }
    }

    private async Task ReloadAfterFailureAsync(string folderPath, string message, int generation)
    {
        try
        {
            var paths = await Task.Run(() => _fileSystem.EnumerateTopLevelFiles(folderPath));
            if (generation == _sessionGeneration)
            {
                ReplaceFiles(folderPath, paths);
                Status = message;
                RenameOperationMessage = message;
            }
        }
        catch (Exception reloadException)
        {
            if (generation == _sessionGeneration)
            {
                ClearFolderState();
                Status = $"{message} The folder could not be reloaded: {reloadException.Message}";
            }
        }
    }

    private void ReplaceFiles(string folderPath, IEnumerable<string> paths)
    {
        CancelPreview();
        CancelThumbnailLoading();
        SelectedFile = null;
        Files.Clear();
        foreach (var path in paths
                     .OrderBy(path => Path.GetFileName(path), NaturalFileNameComparer.Instance)
                     .ThenBy(path => path, StringComparer.Ordinal))
        {
            Files.Add(new BatchFileItem(Path.GetFullPath(path)));
        }

        FolderPath = folderPath;
        OnPropertyChanged(nameof(FileCountText));
        RecomputePlan();
        StartThumbnailLoading(Files.ToArray());
        SelectedFile = Files.FirstOrDefault();
    }

    private void ClearFolderState()
    {
        CancelPreview();
        CancelThumbnailLoading();
        _selectedFile = null;
        OnPropertyChanged(nameof(SelectedFile));
        Files.Clear();
        FolderPath = string.Empty;
        PreviewSource = null;
        PreviewMessage = "Select an image to preview it.";
        ValidationMessage = string.Empty;
        IsRenameDialogOpen = false;
        RenameOperationMessage = string.Empty;
        ClearDeleteConfirmation();
        _currentPlan = new BatchRenamePlan();
        OnPropertyChanged(nameof(FileCountText));
        OnPropertyChanged(nameof(CanRename));
        RaiseCommandStates();
    }

    private void RecomputePlan()
    {
        if (!HasFolder)
        {
            _currentPlan = new BatchRenamePlan();
            ValidationMessage = string.Empty;
            OnPropertyChanged(nameof(CanRename));
            RaiseCommandStates();
            return;
        }

        var plan = BatchRenamePlanner.Create(
            FolderPath,
            Files.Select(file => file.CurrentPath).ToArray(),
            Prefix,
            StartNumberText,
            PaddingText);
        for (var index = 0; index < Files.Count; index++)
        {
            Files[index].Position = index + 1;
            Files[index].ProposedName = index < plan.ProposedFileNames.Count
                ? plan.ProposedFileNames[index]
                : string.Empty;
        }

        var conflict = plan.IsValid ? _renameService.ValidateCurrentConflicts(plan) : null;
        ValidationMessage = plan.Error ?? conflict ?? string.Empty;
        _currentPlan = conflict is null
            ? plan
            : plan with { Error = conflict };
        OnPropertyChanged(nameof(CanRename));
        RaiseCommandStates();
    }

    private void StartPreview(BatchFileItem? file)
    {
        CancelPreview();
        PreviewSource = null;
        if (file is null)
        {
            PreviewMessage = Files.Count == 0 && HasFolder
                ? "This folder contains no files."
                : "Select an image to preview it.";
            return;
        }

        if (!SupportedImageExtensions.Contains(file.Extension))
        {
            PreviewMessage = "Preview is not available for this file type.";
            return;
        }

        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        var generation = _sessionGeneration;
        _ = LoadPreviewAsync(file, generation, cancellation);
    }

    private async Task LoadPreviewAsync(
        BatchFileItem file,
        int generation,
        CancellationTokenSource cancellation)
    {
        IsPreviewLoading = true;
        PreviewMessage = "Loading preview...";
        try
        {
            var source = await _previewLoader.LoadAsync(
                file.CurrentPath,
                cancellationToken: cancellation.Token);
            if (!cancellation.IsCancellationRequested
                && generation == _sessionGeneration
                && ReferenceEquals(file, SelectedFile))
            {
                PreviewSource = source;
                PreviewMessage = string.Empty;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!cancellation.IsCancellationRequested
                && generation == _sessionGeneration
                && ReferenceEquals(file, SelectedFile))
            {
                PreviewSource = null;
                PreviewMessage = $"The image could not be previewed: {exception.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation))
            {
                _previewCancellation = null;
                IsPreviewLoading = false;
            }

            cancellation.Dispose();
        }
    }

    private void CancelPreview()
    {
        _previewCancellation?.Cancel();
        _previewCancellation = null;
        IsPreviewLoading = false;
    }

    private void StartThumbnailLoading(IReadOnlyList<BatchFileItem> files)
    {
        CancelThumbnailLoading();
        var imageFiles = files
            .Where(file => SupportedImageExtensions.Contains(file.Extension))
            .ToArray();
        if (imageFiles.Length == 0)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _thumbnailCancellation = cancellation;
        var generation = _sessionGeneration;
        _ = LoadThumbnailsAsync(imageFiles, generation, cancellation);
    }

    private async Task LoadThumbnailsAsync(
        IReadOnlyList<BatchFileItem> files,
        int generation,
        CancellationTokenSource cancellation)
    {
        try
        {
            foreach (var file in files)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                try
                {
                    var thumbnail = await _previewLoader.LoadAsync(
                        file.CurrentPath,
                        maximumPixelDimension: 192,
                        cancellationToken: cancellation.Token);
                    if (generation == _sessionGeneration && Files.Contains(file))
                    {
                        file.ThumbnailSource = thumbnail;
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Keep the file-type tile when a thumbnail cannot be decoded.
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_thumbnailCancellation, cancellation))
            {
                _thumbnailCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void CancelThumbnailLoading()
    {
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation = null;
    }

    private void RaiseCommandStates()
    {
        _browseCommand.RaiseCanExecuteChanged();
        _reloadCommand.RaiseCanExecuteChanged();
        _openRenameDialogCommand.RaiseCanExecuteChanged();
        _closeRenameDialogCommand.RaiseCanExecuteChanged();
        _renameCommand.RaiseCanExecuteChanged();
        _confirmDeleteCommand.RaiseCanExecuteChanged();
        _cancelDeleteCommand.RaiseCanExecuteChanged();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
