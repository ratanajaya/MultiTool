namespace MultiTool.Modules.MongoDbBackup;

public sealed record MongoDumpRunRequest
{
    public required string ExecutablePath { get; init; }

    public required string ArchivePath { get; init; }

    public bool OverwriteExisting { get; init; }
}
