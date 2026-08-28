namespace MultiTool.Modules.PowerTimer;

public sealed record PowerTimerSettings
{
    public PowerTimerSchedule? ActiveSchedule { get; init; }

    public IReadOnlyList<string> Validate() => ActiveSchedule?.Validate() ?? [];
}
