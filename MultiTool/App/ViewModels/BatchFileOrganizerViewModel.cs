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
    private BatchRenamePlan _currentPlan = new();
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _thumbnailCancellation;
    private CancellationTokenSource? _renameCancellation;
    private Task<BatchRenameResult>? _activeRenameTask;
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
        _browseCommand = new AsyncRelayCommand(BrowseAsync, () => !IsBusy && !IsRenameDialogOpen);
        _reloadCommand = new AsyncRelayCommand(
            ReloadAsync,
            () => HasFolder && !IsBusy && !IsRenameDialogOpen);
        _openRenameDialogCommand = new AsyncRelayCommand(
            OpenRenameDialogAsync,
            () => CanOpenRenameDialog);
        _closeRenameDialogCommand = new AsyncRelayCommand(
            CloseRenameDialogAsync,
            () => IsRenameDialogOpen && !IsBusy);
        _renameCommand = new AsyncRelayCommand(RenameAsync, () => CanRename);
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
                RaiseCommandStates();
            }
        }
    }

    public string RenameOperationMessage
    {
        get => _renameOperationMessage;
        private set => SetField(ref _renameOperationMessage, value);
    }

    public bool CanEdit => HasFolder && !IsBusy && !IsRenameDialogOpen;

    public bool CanOpenRenameDialog =>
        HasFolder
        && Files.Count > 0
        && !IsBusy
        && !IsRenameDialogOpen;

    public bool CanRename =>
        HasFolder
        && Files.Count > 0
        && !IsBusy
        && IsRenameDialogOpen
        && _currentPlan.IsValid
        && _currentPlan.HasChanges
        && string.IsNullOrWhiteSpace(ValidationMessage);

    public ICommand BrowseCommand => _browseCommand;

    public ICommand ReloadCommand => _reloadCommand;

    public ICommand OpenRenameDialogCommand => _openRenameDialogCommand;

    public ICommand CloseRenameDialogCommand => _closeRenameDialogCommand;

    public ICommand RenameCommand => _renameCommand;

    public async Task LoadFolderAsync(string folderPath)
    {
        if (IsBusy)
        {
            Status = "Wait for the current rename operation to finish.";
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

    public void LeaveModule()
    {
        _sessionGeneration++;
        CancelPreview();
        CancelThumbnailLoading();
        _renameCancellation?.Cancel();
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
