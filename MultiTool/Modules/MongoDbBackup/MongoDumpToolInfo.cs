namespace MultiTool.Modules.MongoDbBackup;

public sealed record MongoDumpToolInfo(
    string ExecutablePath,
    Version Version,
    string DisplayVersion);
