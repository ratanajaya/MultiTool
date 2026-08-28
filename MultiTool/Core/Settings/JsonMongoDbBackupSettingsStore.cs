using MultiTool.Modules.MongoDbBackup;

namespace MultiTool.Core.Settings;

public sealed class JsonMongoDbBackupSettingsStore(JsonSettingsStore settingsStore)
    : IMongoDbBackupSettingsStore
{
    public Task SaveAsync(
        MongoDbBackupSettings settings,
        CancellationToken cancellationToken = default) =>
        settingsStore.UpdateAsync(
            current => current with { MongoDbBackup = settings },
            cancellationToken);
}
