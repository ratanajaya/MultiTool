namespace MultiTool.Modules.UsageReminders;

public interface IUsageReminderSettingsStore
{
    Task SaveAsync(
        UsageReminderSettings settings,
        CancellationToken cancellationToken = default);
}
