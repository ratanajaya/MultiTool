using MultiTool.Modules.PowerTimer;

namespace MultiTool.Core.Settings;

public sealed class JsonPowerTimerScheduleStore : IPowerTimerScheduleStore
{
    private readonly JsonSettingsStore _settingsStore;

    public JsonPowerTimerScheduleStore(JsonSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public Task SaveAsync(
        PowerTimerSchedule? schedule,
        CancellationToken cancellationToken = default) =>
        _settingsStore.UpdateAsync(
            settings => settings with
            {
                PowerTimer = settings.PowerTimer with { ActiveSchedule = schedule }
            },
            cancellationToken);
}
