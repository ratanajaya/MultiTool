namespace MultiTool.Modules.UsageReminders;

public sealed record UsageReminderSettings
{
    public IReadOnlyList<UsageReminderProfile> Profiles { get; init; } = CreateDefaultProfiles();

    public IReadOnlyList<PendingReminderSnooze> PendingSnoozes { get; init; } = [];

    public IReadOnlyList<ReminderDelivery> RecentDeliveries { get; init; } = [];

    public IReadOnlyList<string> Validate()
    {
        var errors = Profiles.SelectMany(profile => profile.Validate()).ToList();
        if (Profiles.GroupBy(profile => profile.Id).Any(group => group.Count() > 1))
        {
            errors.Add("Reminder identifiers must be unique.");
        }

        return errors;
    }

    private static IReadOnlyList<UsageReminderProfile> CreateDefaultProfiles() =>
    [
        new UsageReminderProfile
        {
            Name = "Wrap up",
            Hour = 17,
            Minute = 30,
            Title = "Time to wrap up",
            Message = "Finish what you're doing and save your work."
        },
        new UsageReminderProfile
        {
            Name = "Sign off",
            Hour = 18,
            Title = "Time to sign off",
            Message = "Wrap up your PC usage and step away for the day."
        }
    ];
}
