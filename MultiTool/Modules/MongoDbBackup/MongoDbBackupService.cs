using System.IO;
using MultiTool.Core.Notifications;

namespace MultiTool.Modules.MongoDbBackup;

public sealed class MongoDbBackupService : IAsyncDisposable
{
    private const string NotificationContext = "MongoDbBackup";
    private readonly IMongoDumpRunner _runner;
    private readonly IMongoDbBackupSettingsStore _settingsStore;
    private readonly INotificationService _notifications;
    private readonly object _stateGate = new();
    private CancellationTokenSource? _runCancellation;
    private Task? _activeTask;
    private MongoDbBackupSettings _settings = new();
    private MongoDumpToolInfo? _toolInfo;
    private MongoDbBackupPhase _phase = MongoDbBackupPhase.Idle;
    private MongoDbBackupResult? _latestResult;
    private string _activity = "Ready";
    private DateTimeOffset? _startedUtc;
    private bool _disposed;

    public MongoDbBackupService(
        IMongoDumpRunner runner,
        IMongoDbBackupSettingsStore settingsStore,
        INotificationService notifications)
    {
        _runner = runner;
        _settingsStore = settingsStore;
        _notifications = notifications;
    }

    public event EventHandler? StatusChanged;

    public event EventHandler? BackupProcessStarted;

    public MongoDbBackupSettings Settings
    {
        get
        {
            lock (_stateGate)
            {
                return _settings;
            }
        }
    }

    public MongoDumpToolInfo? ToolInfo
    {
        get
        {
            lock (_stateGate)
            {
                return _toolInfo;
            }
        }
    }

    public MongoDbBackupPhase Phase
    {
        get
        {
            lock (_stateGate)
            {
                return _phase;
            }
        }
    }

    public MongoDbBackupResult? LatestResult
    {
        get
        {
            lock (_stateGate)
            {
                return _latestResult;
            }
        }
    }

    public string Activity
    {
        get
        {
            lock (_stateGate)
            {
                return _activity;
            }
        }
    }

    public DateTimeOffset? StartedUtc
    {
        get
        {
            lock (_stateGate)
            {
                return _startedUtc;
            }
        }
    }

    public bool IsActive => Phase is
        MongoDbBackupPhase.Validating or
        MongoDbBackupPhase.Running or
        MongoDbBackupPhase.Cancelling;

    public async Task InitializeAsync(
        MongoDbBackupSettings settings,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        lock (_stateGate)
        {
            _settings = settings;
        }

        try
        {
            await _runner.CleanupStaleFilesAsync(cancellationToken);
        }
        catch
        {
            SetState(
                MongoDbBackupPhase.Faulted,
                "A stale MongoDB backup temporary file could not be removed. Check the MultiTool temporary folder.");
            return;
        }

        var executablePath = _runner.FindExecutable(settings.MongoDumpExecutablePath);
        if (executablePath is null)
        {
            SetState(
                MongoDbBackupPhase.Idle,
                "mongodump 100.3.0 or newer was not found. Select mongodump.exe to continue.");
            return;
        }

        try
        {
            await ValidateToolAsync(executablePath, cancellationToken);
        }
        catch (Exception exception)
        {
            var fallbackPath = _runner.FindExecutable(null);
            if (fallbackPath is not null
                && !string.Equals(fallbackPath, executablePath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await ValidateToolAsync(fallbackPath, cancellationToken);
                    return;
                }
                catch
                {
                    // Report the preferred executable's error below.
                }
            }

            SetToolInfo(null);
            SetState(MongoDbBackupPhase.Idle, exception.Message);
        }
    }

    public async Task<MongoDumpToolInfo> ValidateToolAsync(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (IsActive)
        {
            throw new InvalidOperationException("The tool cannot be changed while a backup is active.");
        }

        var toolInfo = await _runner.ValidateToolAsync(executablePath, cancellationToken);
        MongoDbBackupSettings settings;
        lock (_stateGate)
        {
            _toolInfo = toolInfo;
            _settings = _settings with { MongoDumpExecutablePath = toolInfo.ExecutablePath };
            settings = _settings;
        }

        await _settingsStore.SaveAsync(settings, cancellationToken);
        SetState(MongoDbBackupPhase.Idle, $"mongodump {toolInfo.DisplayVersion} is ready.");
        return toolInfo;
    }

    public async Task SetLastDestinationFolderAsync(
        string? folder,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        MongoDbBackupSettings settings;
        lock (_stateGate)
        {
            _settings = _settings with { LastDestinationFolder = folder };
            settings = _settings;
        }

        await _settingsStore.SaveAsync(settings, cancellationToken);
        OnStatusChanged();
    }

    public Task StartBackupAsync(
        string connectionString,
        string archivePath,
        bool overwriteExisting,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var validationErrors = MongoDbConnectionStringValidator.Validate(connectionString);
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", validationErrors));
        }

        var normalizedArchivePath = ValidateArchivePath(archivePath, overwriteExisting);
        MongoDumpToolInfo toolInfo;
        CancellationTokenSource runCancellation;
        lock (_stateGate)
        {
            if (_activeTask is not null)
            {
                throw new InvalidOperationException("A MongoDB backup is already active.");
            }

            toolInfo = _toolInfo
                ?? throw new InvalidOperationException("Select and validate mongodump.exe before starting a backup.");
            runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runCancellation = runCancellation;
            _phase = MongoDbBackupPhase.Validating;
            _activity = "Preparing the protected backup workspace...";
            _latestResult = null;
            _startedUtc = null;
            _activeTask = RunCoreAsync(
                new MongoDumpRunRequest
                {
                    ExecutablePath = toolInfo.ExecutablePath,
                    ArchivePath = normalizedArchivePath,
                    OverwriteExisting = overwriteExisting
                },
                connectionString,
                runCancellation);
        }

        OnStatusChanged();
        return _activeTask;
    }

    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Task? activeTask;
        CancellationTokenSource? runCancellation;
        lock (_stateGate)
        {
            activeTask = _activeTask;
            runCancellation = _runCancellation;
            if (activeTask is null || runCancellation is null)
            {
                return;
            }

            _phase = MongoDbBackupPhase.Cancelling;
            _activity = "Cancelling mongodump and removing incomplete files...";
        }

        OnStatusChanged();
        runCancellation.Cancel();
        await activeTask.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Task? activeTask;
        lock (_stateGate)
        {
            activeTask = _activeTask;
            _runCancellation?.Cancel();
        }

        if (activeTask is not null)
        {
            try
            {
                await activeTask;
            }
            catch
            {
                // Shutdown must continue after the runner has attempted cleanup.
            }
        }
    }

    private async Task RunCoreAsync(
        MongoDumpRunRequest request,
        string connectionString,
        CancellationTokenSource runCancellation)
    {
        await Task.Yield();
        try
        {
            var progress = new InlineProgress<string>(message =>
            {
                var safeMessage = MongoDbOutputRedactor.Redact(message, connectionString);
                var updated = false;
                lock (_stateGate)
                {
                    if (_phase is MongoDbBackupPhase.Validating or MongoDbBackupPhase.Running)
                    {
                        _activity = safeMessage;
                        updated = true;
                    }
                }

                if (updated)
                {
                    OnStatusChanged();
                }
            });
            var result = await _runner.RunAsync(
                request,
                connectionString,
                HandleProcessStarted,
                progress,
                runCancellation.Token);
            result = result with
            {
                Message = MongoDbOutputRedactor.Redact(result.Message, connectionString)
            };

            lock (_stateGate)
            {
                _latestResult = result;
                _phase = result.Phase;
                _activity = result.Message;
                _startedUtc = null;
            }

            if (result.Phase == MongoDbBackupPhase.Succeeded)
            {
                _notifications.Show(
                    "MongoDB backup completed",
                    $"The archive was saved as {Path.GetFileName(result.ArchivePath)}.",
                    NotificationContext);
            }
            else if (result.Phase == MongoDbBackupPhase.Faulted)
            {
                _notifications.Show(
                    "MongoDB backup failed",
                    "Open MultiTool for the redacted error details.",
                    NotificationContext);
            }
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            lock (_stateGate)
            {
                _latestResult = new MongoDbBackupResult
                {
                    Phase = MongoDbBackupPhase.Cancelled,
                    StartedUtc = _startedUtc ?? now,
                    FinishedUtc = now,
                    Message = "Backup cancelled. Incomplete files were removed."
                };
                _phase = MongoDbBackupPhase.Cancelled;
                _activity = _latestResult.Message;
                _startedUtc = null;
            }
        }
        catch (Exception exception)
        {
            var now = DateTimeOffset.UtcNow;
            var safeMessage = MongoDbOutputRedactor.Redact(exception.Message, connectionString);
            lock (_stateGate)
            {
                _latestResult = new MongoDbBackupResult
                {
                    Phase = MongoDbBackupPhase.Faulted,
                    StartedUtc = _startedUtc ?? now,
                    FinishedUtc = now,
                    Message = safeMessage
                };
                _phase = MongoDbBackupPhase.Faulted;
                _activity = safeMessage;
                _startedUtc = null;
            }

            _notifications.Show(
                "MongoDB backup failed",
                "Open MultiTool for the redacted error details.",
                NotificationContext);
        }
        finally
        {
            connectionString = string.Empty;
            lock (_stateGate)
            {
                _runCancellation = null;
                _activeTask = null;
            }

            runCancellation.Dispose();
            OnStatusChanged();
        }
    }

    private void HandleProcessStarted()
    {
        lock (_stateGate)
        {
            _phase = MongoDbBackupPhase.Running;
            _startedUtc = DateTimeOffset.UtcNow;
            _activity = "mongodump is creating the compressed archive...";
        }

        BackupProcessStarted?.Invoke(this, EventArgs.Empty);
        OnStatusChanged();
    }

    private static string ValidateArchivePath(string archivePath, bool overwriteExisting)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new InvalidOperationException("Select an archive destination.");
        }

        var fullPath = Path.GetFullPath(archivePath);
        if (!fullPath.EndsWith(".archive.gz", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The backup filename must end with .archive.gz.");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (directory is null || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("The selected destination folder does not exist.");
        }

        if (File.Exists(fullPath) && !overwriteExisting)
        {
            throw new IOException("The selected archive already exists. Choose another name or approve replacement.");
        }

        return fullPath;
    }

    private void SetToolInfo(MongoDumpToolInfo? toolInfo)
    {
        lock (_stateGate)
        {
            _toolInfo = toolInfo;
        }
    }

    private void SetState(MongoDbBackupPhase phase, string activity)
    {
        lock (_stateGate)
        {
            _phase = phase;
            _activity = activity;
        }

        OnStatusChanged();
    }

    private void OnStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
