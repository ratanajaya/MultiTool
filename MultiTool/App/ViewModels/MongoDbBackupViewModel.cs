using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using MultiTool.Modules.MongoDbBackup;
using MultiTool.Presentation.Commands;

namespace MultiTool.Presentation.ViewModels;

public sealed class MongoDbBackupViewModel : INotifyPropertyChanged
{
    private readonly MongoDbBackupService _module;
    private readonly IMongoDbBackupFileDialogService _fileDialogs;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _elapsedTimer;
    private readonly AsyncRelayCommand _browseToolCommand;
    private readonly AsyncRelayCommand _validateToolCommand;
    private readonly AsyncRelayCommand _browseArchiveCommand;
    private readonly AsyncRelayCommand _cancelCommand;
    private string _executablePath = string.Empty;
    private string _archivePath = string.Empty;
    private bool _overwriteExisting;
    private string _toolVersion = "Not validated";
    private string _status = "Idle";
    private string _activity = "Ready";
    private string _elapsed = "00:00:00";
    private string _latestResult = "No backup has run in this session.";

    public MongoDbBackupViewModel(
        MongoDbBackupService module,
        IMongoDbBackupFileDialogService fileDialogs)
    {
        _module = module;
        _fileDialogs = fileDialogs;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _module.StatusChanged += HandleStatusChanged;
        _module.BackupProcessStarted += HandleBackupProcessStarted;
        _browseToolCommand = new AsyncRelayCommand(BrowseToolAsync, () => !IsActive);
        _validateToolCommand = new AsyncRelayCommand(
            () => RunSafelyAsync(ValidateToolAsync),
            () => !IsActive && !string.IsNullOrWhiteSpace(ExecutablePath));
        _browseArchiveCommand = new AsyncRelayCommand(BrowseArchiveAsync, () => !IsActive);
        _cancelCommand = new AsyncRelayCommand(
            () => RunSafelyAsync(() => _module.CancelAsync()),
            () => IsActive);
        _elapsedTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            RefreshElapsed();
        }, _dispatcher);
        _elapsedTimer.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? ConnectionInputConsumed;

    public string ExecutablePath
    {
        get => _executablePath;
        set
        {
            if (SetField(ref _executablePath, value))
            {
                OnPropertyChanged(nameof(CanStartBackup));
                RaiseCommandStates();
            }
        }
    }

    public string ArchivePath
    {
        get => _archivePath;
        private set
        {
            if (SetField(ref _archivePath, value))
            {
                OnPropertyChanged(nameof(CanStartBackup));
            }
        }
    }

    public string ToolVersion
    {
        get => _toolVersion;
        private set => SetField(ref _toolVersion, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string Activity
    {
        get => _activity;
        private set => SetField(ref _activity, value);
    }

    public string Elapsed
    {
        get => _elapsed;
        private set => SetField(ref _elapsed, value);
    }

    public string LatestResult
    {
        get => _latestResult;
        private set => SetField(ref _latestResult, value);
    }

    public bool IsActive => _module.IsActive;

    public bool IsRunning => _module.Phase == MongoDbBackupPhase.Running;

    public bool IsInputEnabled => !IsActive;

    public bool CanStartBackup =>
        !IsActive
        && _module.ToolInfo is not null
        && MatchesValidatedTool(ExecutablePath, _module.ToolInfo.ExecutablePath)
        && !string.IsNullOrWhiteSpace(ArchivePath);

    public ICommand BrowseToolCommand => _browseToolCommand;

    public ICommand ValidateToolCommand => _validateToolCommand;

    public ICommand BrowseArchiveCommand => _browseArchiveCommand;

    public ICommand CancelCommand => _cancelCommand;

    public async Task InitializeAsync(MongoDbBackupSettings settings)
    {
        ExecutablePath = settings.MongoDumpExecutablePath ?? string.Empty;
        await _module.InitializeAsync(settings);
        RefreshFromModule();
    }

    public async Task StartBackupAsync(string connectionString)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ArchivePath))
            {
                await BrowseArchiveAsync();
            }

            if (string.IsNullOrWhiteSpace(ArchivePath))
            {
                Activity = "Select an archive destination before starting the backup.";
                return;
            }

            await _module.StartBackupAsync(connectionString, ArchivePath, _overwriteExisting);
        }
        catch (Exception exception)
        {
            Activity = exception.Message;
        }
        finally
        {
            connectionString = string.Empty;
            RefreshFromModule();
        }
    }

    public async Task ShutdownAsync()
    {
        _elapsedTimer.Stop();
        _module.StatusChanged -= HandleStatusChanged;
        _module.BackupProcessStarted -= HandleBackupProcessStarted;
        await _module.DisposeAsync();
    }

    private Task BrowseToolAsync()
    {
        var selection = _fileDialogs.SelectMongoDumpExecutable(ExecutablePath);
        if (!string.IsNullOrWhiteSpace(selection))
        {
            ExecutablePath = selection;
            ToolVersion = "Not validated";
            Activity = "Click Validate to check this mongodump executable.";
        }

        return Task.CompletedTask;
    }

    private async Task ValidateToolAsync()
    {
        var toolInfo = await _module.ValidateToolAsync(ExecutablePath);
        ExecutablePath = toolInfo.ExecutablePath;
        ToolVersion = toolInfo.DisplayVersion;
        Activity = $"mongodump {toolInfo.DisplayVersion} is ready.";
    }

    private async Task BrowseArchiveAsync()
    {
        var selection = _fileDialogs.SelectArchive(
            _module.Settings.LastDestinationFolder,
            $"mongodb-backup-{DateTime.Now:yyyyMMdd-HHmmss}.archive.gz");
        if (selection is null)
        {
            return;
        }

        ArchivePath = selection.Path;
        _overwriteExisting = selection.OverwriteExisting;
        await _module.SetLastDestinationFolderAsync(Path.GetDirectoryName(selection.Path));
        Activity = "Archive destination selected.";
    }

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            Activity = exception.Message;
        }
        finally
        {
            RefreshFromModule();
        }
    }

    private void HandleStatusChanged(object? sender, EventArgs eventArgs)
    {
        if (_dispatcher.CheckAccess())
        {
            RefreshFromModule();
        }
        else
        {
            _dispatcher.BeginInvoke(RefreshFromModule);
        }
    }

    private void HandleBackupProcessStarted(object? sender, EventArgs eventArgs)
    {
        if (_dispatcher.CheckAccess())
        {
            ConnectionInputConsumed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _dispatcher.BeginInvoke(() => ConnectionInputConsumed?.Invoke(this, EventArgs.Empty));
        }
    }

    private void RefreshFromModule()
    {
        var toolInfo = _module.ToolInfo;
        if (toolInfo is not null)
        {
            ExecutablePath = toolInfo.ExecutablePath;
            ToolVersion = toolInfo.DisplayVersion;
        }

        Status = _module.Phase.ToString();
        Activity = _module.Activity;
        var result = _module.LatestResult;
        LatestResult = result is null
            ? "No backup has run in this session."
            : FormatResult(result);
        RefreshElapsed();
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsInputEnabled));
        OnPropertyChanged(nameof(CanStartBackup));
        RaiseCommandStates();
    }

    private void RefreshElapsed()
    {
        var started = _module.StartedUtc;
        var elapsed = started is null
            ? _module.LatestResult?.Duration ?? TimeSpan.Zero
            : DateTimeOffset.UtcNow - started.Value;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        Elapsed = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private static string FormatResult(MongoDbBackupResult result)
    {
        var size = result.ArchiveSizeBytes is long bytes
            ? $", {FormatBytes(bytes)}"
            : string.Empty;
        var archive = string.IsNullOrWhiteSpace(result.ArchivePath)
            ? string.Empty
            : $" {result.ArchivePath}";
        return $"{result.Phase} at {result.FinishedUtc.ToLocalTime():g} "
               + $"({result.Duration:hh\\:mm\\:ss}{size}).{archive} {result.Message}";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static bool MatchesValidatedTool(string candidate, string validatedPath)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(candidate),
                validatedPath,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void RaiseCommandStates()
    {
        _browseToolCommand.RaiseCanExecuteChanged();
        _validateToolCommand.RaiseCanExecuteChanged();
        _browseArchiveCommand.RaiseCanExecuteChanged();
        _cancelCommand.RaiseCanExecuteChanged();
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
