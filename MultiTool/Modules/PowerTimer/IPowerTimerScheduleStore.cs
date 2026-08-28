namespace MultiTool.Modules.PowerTimer;

public interface IPowerTimerScheduleStore
{
    Task SaveAsync(
        PowerTimerSchedule? schedule,
        CancellationToken cancellationToken = default);
}
