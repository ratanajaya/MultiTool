namespace MultiTool.Modules.UsageReminders;

public sealed record ReminderPresentation
{
    public Guid OccurrenceId { get; init; } = Guid.NewGuid();

    public string ProfileName { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public DateTimeOffset ScheduledUtc { get; init; }

    public DateTimeOffset ExpiresUtc { get; init; }

    public int SnoozeMinutes { get; init; } = 10;

    public bool IsPreview { get; init; }
}

public enum ReminderResponse
{
    Acknowledge,
    Snooze
}

public sealed class ReminderResponseEventArgs(
    Guid occurrenceId,
    ReminderResponse response) : EventArgs
{
    public Guid OccurrenceId { get; } = occurrenceId;

    public ReminderResponse Response { get; } = response;
}
