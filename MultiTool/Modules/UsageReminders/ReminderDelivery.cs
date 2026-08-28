namespace MultiTool.Modules.UsageReminders;

public enum ReminderDeliveryStatus
{
    Delivered,
    Acknowledged,
    Snoozed,
    Missed
}

public sealed record ReminderDelivery
{
    public string DeliveryId { get; init; } = string.Empty;

    public Guid ProfileId { get; init; }

    public string ProfileName { get; init; } = string.Empty;

    public DateTimeOffset ScheduledUtc { get; init; }

    public DateTimeOffset RecordedUtc { get; init; }

    public ReminderDeliveryStatus Status { get; init; }
}

public sealed record PendingReminderSnooze
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid ProfileId { get; init; }

    public DateTimeOffset DueUtc { get; init; }
}
