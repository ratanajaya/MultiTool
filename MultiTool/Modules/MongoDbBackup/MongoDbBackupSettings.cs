namespace MultiTool.Modules.MongoDbBackup;

public sealed record MongoDbBackupSettings
{
    public string? MongoDumpExecutablePath { get; init; }

    public string? LastDestinationFolder { get; init; }
}
