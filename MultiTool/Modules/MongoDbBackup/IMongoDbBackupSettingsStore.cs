namespace MultiTool.Modules.MongoDbBackup;

public interface IMongoDbBackupSettingsStore
{
    Task SaveAsync(
        MongoDbBackupSettings settings,
        CancellationToken cancellationToken = default);
}
