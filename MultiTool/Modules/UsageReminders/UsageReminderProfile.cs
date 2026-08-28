namespace MultiTool.Modules.UsageReminders;

public sealed record UsageReminderProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public string Name { get; init; } = "Reminder";

    public bool Enabled { get; init; } = true;

    public int Hour { get; init; } = 18;

    public int Minute { get; init; }

    public ReminderDays ActiveDays { get; init; } = ReminderDays.Weekdays;

    public string Title { get; init; } = "Time for a break";

    public string Message { get; init; } = "Finish what you're doing and save your work.";

    public bool ShowCornerCard { get; init; } = true;

    public bool ShowWindowsNotification { get; init; } = true;

    public int SnoozeMinutes { get; init; } = 10;

    public int GracePeriodMinutes { get; init; } = 5;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Id == Guid.Empty)
        {
            errors.Add("The reminder identifier is invalid.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("Enter a reminder name.");
        }

        if (Hour is < 0 or > 23 || Minute is < 0 or > 59)
        {
            errors.Add("Select a valid reminder time.");
        }

        if (ActiveDays == ReminderDays.None || (ActiveDays & ~ReminderDays.EveryDay) != 0)
        {
            errors.Add("Select at least one weekday.");
        }

        if (string.IsNullOrWhiteSpace(Title))
        {
            errors.Add("Enter a reminder title.");
        }

        if (string.IsNullOrWhiteSpace(Message))
        {
            errors.Add("Enter a reminder message.");
        }

        if (!ShowCornerCard && !ShowWindowsNotification)
        {
            errors.Add("Select the corner card, Windows notification, or both.");
        }

        if (SnoozeMinutes is not (5 or 10 or 15 or 30))
        {
            errors.Add("Select a supported snooze duration.");
        }

        if (GracePeriodMinutes is < 1 or > 60)
        {
            errors.Add("The grace period must be between 1 and 60 minutes.");
        }

        return errors;
    }
}
