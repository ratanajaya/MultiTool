namespace MultiTool.Modules.MongoDbBackup;

public interface IMongoDumpRunner
{
    string? FindExecutable(string? preferredPath);

    Task<MongoDumpToolInfo> ValidateToolAsync(
        string executablePath,
        CancellationToken cancellationToken = default);

    Task CleanupStaleFilesAsync(CancellationToken cancellationToken = default);

    Task<MongoDbBackupResult> RunAsync(
        MongoDumpRunRequest request,
        string connectionString,
        Action processStarted,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
