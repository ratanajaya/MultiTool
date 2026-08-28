namespace MultiTool.Modules.MongoDbBackup;

public sealed record MongoDbBackupResult
{
    public required MongoDbBackupPhase Phase { get; init; }

    public required DateTimeOffset StartedUtc { get; init; }

    public required DateTimeOffset FinishedUtc { get; init; }

    public string? ArchivePath { get; init; }

    public long? ArchiveSizeBytes { get; init; }

    public string Message { get; init; } = string.Empty;

    public TimeSpan Duration => FinishedUtc - StartedUtc;
}
