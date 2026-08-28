using MultiTool.Core.Notifications;
using MultiTool.Modules.MongoDbBackup;

namespace MultiTool.Tests.Modules.MongoDbBackup;

[TestClass]
public sealed class MongoDbBackupServiceTests
{
    [TestMethod]
    public async Task SuccessfulBackup_NotifiesAndKeepsOnlyRedactedResult()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var observedActivity = new List<string>();
        service.StatusChanged += (_, _) => observedActivity.Add(service.Activity);
        await service.InitializeAsync(new MongoDbBackupSettings());
        var archivePath = fixture.CreateArchivePath();

        await service.StartBackupAsync(Fixture.ConnectionString, archivePath, overwriteExisting: false);

        Assert.AreEqual(MongoDbBackupPhase.Succeeded, service.Phase);
        Assert.AreEqual(1, fixture.Notifications.Messages.Count);
        Assert.AreEqual("MongoDbBackup", fixture.Notifications.Messages.Single().Context);
        Assert.IsFalse(service.Activity.Contains(Fixture.ConnectionString, StringComparison.Ordinal));
        Assert.IsFalse(service.LatestResult!.Message.Contains("secret", StringComparison.Ordinal));
        Assert.IsFalse(observedActivity.Any(message =>
            message.Contains("secret", StringComparison.Ordinal)
            || message.Contains("backup-user", StringComparison.Ordinal)));
        Assert.AreEqual(Fixture.ConnectionString, fixture.Runner.ReceivedConnectionString);
        await service.DisposeAsync();
        fixture.Dispose();
    }

    [TestMethod]
    public async Task StartBackup_RejectsConcurrentRun()
    {
        var fixture = new Fixture { HoldRunnerOpen = true };
        var service = fixture.CreateService();
        await service.InitializeAsync(new MongoDbBackupSettings());
        var firstRun = service.StartBackupAsync(
            Fixture.ConnectionString,
            fixture.CreateArchivePath(),
            overwriteExisting: false);
        await fixture.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.StartBackupAsync(
            Fixture.ConnectionString,
            fixture.CreateArchivePath("second.archive.gz"),
            overwriteExisting: false));

        fixture.Runner.Complete();
        await firstRun;
        await service.DisposeAsync();
        fixture.Dispose();
    }

    [TestMethod]
    public async Task Cancel_CancelsRunnerAndProducesCancelledResult()
    {
        var fixture = new Fixture { HoldRunnerOpen = true };
        var service = fixture.CreateService();
        await service.InitializeAsync(new MongoDbBackupSettings());
        var run = service.StartBackupAsync(
            Fixture.ConnectionString,
            fixture.CreateArchivePath(),
            overwriteExisting: false);
        await fixture.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await service.CancelAsync();
        await run;

        Assert.IsTrue(fixture.Runner.CancellationObserved);
        Assert.AreEqual(MongoDbBackupPhase.Cancelled, service.Phase);
        Assert.AreEqual(0, fixture.Notifications.Messages.Count);
        await service.DisposeAsync();
        fixture.Dispose();
    }

    [TestMethod]
    public async Task RunnerFailure_IsRedactedBeforeStatusAndNotification()
    {
        var fixture = new Fixture { ThrowSecretFailure = true };
        var service = fixture.CreateService();
        await service.InitializeAsync(new MongoDbBackupSettings());

        await service.StartBackupAsync(
            Fixture.ConnectionString,
            fixture.CreateArchivePath(),
            overwriteExisting: false);

        Assert.AreEqual(MongoDbBackupPhase.Faulted, service.Phase);
        Assert.IsFalse(service.Activity.Contains("secret", StringComparison.Ordinal));
        Assert.IsFalse(service.LatestResult!.Message.Contains("backup-user", StringComparison.Ordinal));
        Assert.AreEqual("MongoDB backup failed", fixture.Notifications.Messages.Single().Title);
        await service.DisposeAsync();
        fixture.Dispose();
    }

    [TestMethod]
    public async Task ValidateToolAndDestination_PersistOnlySafePreferences()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        await service.InitializeAsync(new MongoDbBackupSettings());
        var folder = fixture.TestDirectory;

        await service.SetLastDestinationFolderAsync(folder);

        Assert.AreEqual(fixture.Runner.ExecutablePath, fixture.Store.Saved!.MongoDumpExecutablePath);
        Assert.AreEqual(folder, fixture.Store.Saved.LastDestinationFolder);
        await service.DisposeAsync();
        fixture.Dispose();
    }

    private sealed class Fixture : IDisposable
    {
        public const string ConnectionString = "mongodb://backup-user:secret@localhost";

        public Fixture()
        {
            TestDirectory = Path.Combine(
                Path.GetTempPath(),
                "MultiTool.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TestDirectory);
            Runner = new FakeRunner(this);
        }

        public bool HoldRunnerOpen { get; init; }

        public bool ThrowSecretFailure { get; init; }

        public string TestDirectory { get; }

        public FakeRunner Runner { get; }

        public FakeStore Store { get; } = new();

        public FakeNotifications Notifications { get; } = new();

        public MongoDbBackupService CreateService() => new(Runner, Store, Notifications);

        public string CreateArchivePath(string fileName = "backup.archive.gz") =>
            Path.Combine(TestDirectory, fileName);

        public void Dispose()
        {
            if (Directory.Exists(TestDirectory))
            {
                Directory.Delete(TestDirectory, recursive: true);
            }
        }
    }

    private sealed class FakeRunner(Fixture fixture) : IMongoDumpRunner
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ExecutablePath { get; } = Path.Combine(fixture.TestDirectory, "mongodump.exe");

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? ReceivedConnectionString { get; private set; }

        public bool CancellationObserved { get; private set; }

        public string? FindExecutable(string? preferredPath) => ExecutablePath;

        public Task<MongoDumpToolInfo> ValidateToolAsync(
            string executablePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MongoDumpToolInfo(ExecutablePath, new Version(100, 10, 0), "100.10.0"));

        public Task CleanupStaleFilesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task<MongoDbBackupResult> RunAsync(
            MongoDumpRunRequest request,
            string connectionString,
            Action processStarted,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ReceivedConnectionString = connectionString;
            processStarted();
            Started.TrySetResult();
            progress?.Report($"working on {connectionString}");
            var started = DateTimeOffset.UtcNow;
            if (fixture.ThrowSecretFailure)
            {
                throw new InvalidOperationException($"failed for {connectionString}");
            }

            if (fixture.HoldRunnerOpen)
            {
                try
                {
                    await _release.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    CancellationObserved = true;
                    return new MongoDbBackupResult
                    {
                        Phase = MongoDbBackupPhase.Cancelled,
                        StartedUtc = started,
                        FinishedUtc = DateTimeOffset.UtcNow,
                        Message = "Backup cancelled. Incomplete files were removed."
                    };
                }
            }

            return new MongoDbBackupResult
            {
                Phase = MongoDbBackupPhase.Succeeded,
                StartedUtc = started,
                FinishedUtc = DateTimeOffset.UtcNow,
                ArchivePath = request.ArchivePath,
                ArchiveSizeBytes = 42,
                Message = "Backup completed successfully."
            };
        }

        public void Complete() => _release.TrySetResult();
    }

    private sealed class FakeStore : IMongoDbBackupSettingsStore
    {
        public MongoDbBackupSettings? Saved { get; private set; }

        public Task SaveAsync(
            MongoDbBackupSettings settings,
            CancellationToken cancellationToken = default)
        {
            Saved = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotifications : INotificationService
    {
        public List<(string Title, string Message, string? Context)> Messages { get; } = [];

        public void Show(string title, string message) => Show(title, message, null);

        public void Show(string title, string message, string? activationContext) =>
            Messages.Add((title, message, activationContext));
    }
}
