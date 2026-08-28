namespace MultiTool.Modules.MongoDbBackup;

public interface IMongoDbBackupFileDialogService
{
    string? SelectMongoDumpExecutable(string? currentPath);

    MongoDbArchiveSelection? SelectArchive(
        string? initialDirectory,
        string defaultFileName);
}

public sealed record MongoDbArchiveSelection(string Path, bool OverwriteExisting);
