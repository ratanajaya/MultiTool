using MultiTool.Modules.UsageReminders;

namespace MultiTool.Presentation.ViewModels;

public sealed record UsageReminderProfileItem(
    UsageReminderProfile Profile,
    string NextOccurrence)
{
    public string Name => Profile.Name;

    public string Schedule => $"{Profile.Hour:00}:{Profile.Minute:00} · {FormatDays(Profile.ActiveDays)}";

    public string State => Profile.Enabled ? "Enabled" : "Disabled";

    private static string FormatDays(ReminderDays days)
    {
        if (days == ReminderDays.EveryDay)
        {
            return "Every day";
        }

        if (days == ReminderDays.Weekdays)
        {
            return "Weekdays";
        }

        var names = new[]
        {
            (ReminderDays.Monday, "Mon"),
            (ReminderDays.Tuesday, "Tue"),
            (ReminderDays.Wednesday, "Wed"),
            (ReminderDays.Thursday, "Thu"),
            (ReminderDays.Friday, "Fri"),
            (ReminderDays.Saturday, "Sat"),
            (ReminderDays.Sunday, "Sun")
        };
        return string.Join(", ", names.Where(item => (days & item.Item1) != 0).Select(item => item.Item2));
    }
}

public sealed record ReminderHistoryItem(ReminderDelivery Delivery)
{
    public string Name => Delivery.ProfileName;

    public string Time => Delivery.ScheduledUtc.ToLocalTime().ToString("g");

    public string Status => Delivery.Status.ToString();
}
