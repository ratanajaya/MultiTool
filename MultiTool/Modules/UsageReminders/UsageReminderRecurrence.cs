namespace MultiTool.Modules.UsageReminders;

public static class UsageReminderRecurrence
{
    public static DateTimeOffset? GetNextOccurrence(
        UsageReminderProfile profile,
        DateTimeOffset afterUtc,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(timeZone);

        var localAfter = TimeZoneInfo.ConvertTime(afterUtc, timeZone);
        for (var dayOffset = 0; dayOffset <= 7; dayOffset++)
        {
            var date = localAfter.Date.AddDays(dayOffset);
            if (!IsActive(profile.ActiveDays, date.DayOfWeek))
            {
                continue;
            }

            var local = DateTime.SpecifyKind(
                date.AddHours(profile.Hour).AddMinutes(profile.Minute),
                DateTimeKind.Unspecified);
            var occurrence = ConvertLocalToUtc(local, timeZone);
            if (occurrence is not null && occurrence > afterUtc)
            {
                return occurrence;
            }
        }

        return null;
    }

    public static DateTimeOffset? GetMostRecentOccurrence(
        UsageReminderProfile profile,
        DateTimeOffset atUtc,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(timeZone);

        var localAt = TimeZoneInfo.ConvertTime(atUtc, timeZone);
        for (var dayOffset = 0; dayOffset >= -7; dayOffset--)
        {
            var date = localAt.Date.AddDays(dayOffset);
            if (!IsActive(profile.ActiveDays, date.DayOfWeek))
            {
                continue;
            }

            var local = DateTime.SpecifyKind(
                date.AddHours(profile.Hour).AddMinutes(profile.Minute),
                DateTimeKind.Unspecified);
            var occurrence = ConvertLocalToUtc(local, timeZone);
            if (occurrence is not null && occurrence <= atUtc)
            {
                return occurrence;
            }
        }

        return null;
    }

    internal static DateTimeOffset? ConvertLocalToUtc(DateTime local, TimeZoneInfo timeZone)
    {
        if (timeZone.IsInvalidTime(local))
        {
            return null;
        }

        if (timeZone.IsAmbiguousTime(local))
        {
            var offset = timeZone.GetAmbiguousTimeOffsets(local).Max();
            return new DateTimeOffset(local, offset).ToUniversalTime();
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    private static bool IsActive(ReminderDays days, DayOfWeek dayOfWeek)
    {
        var flag = dayOfWeek switch
        {
            DayOfWeek.Sunday => ReminderDays.Sunday,
            DayOfWeek.Monday => ReminderDays.Monday,
            DayOfWeek.Tuesday => ReminderDays.Tuesday,
            DayOfWeek.Wednesday => ReminderDays.Wednesday,
            DayOfWeek.Thursday => ReminderDays.Thursday,
            DayOfWeek.Friday => ReminderDays.Friday,
            DayOfWeek.Saturday => ReminderDays.Saturday,
            _ => ReminderDays.None
        };
        return (days & flag) != 0;
    }
}
