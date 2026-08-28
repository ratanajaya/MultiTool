using MultiTool.Modules.UsageReminders;

namespace MultiTool.Core.Settings;

public sealed class JsonUsageReminderSettingsStore(JsonSettingsStore settingsStore)
    : IUsageReminderSettingsStore
{
    public Task SaveAsync(
        UsageReminderSettings settings,
        CancellationToken cancellationToken = default) =>
        settingsStore.UpdateAsync(
            current => current with { UsageReminders = settings },
            cancellationToken);
}
