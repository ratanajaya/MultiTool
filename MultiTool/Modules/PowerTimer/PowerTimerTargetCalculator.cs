namespace MultiTool.Modules.PowerTimer;

public static class PowerTimerTargetCalculator
{
    public static DateTimeOffset FromCountdown(
        DateTimeOffset utcNow,
        int hours,
        int minutes)
    {
        if (hours < 0 || minutes is < 0 or > 59)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minutes),
                "Countdown hours must be positive and minutes must be between 0 and 59.");
        }

        var duration = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
        if (duration < PowerTimerService.WarningLeadTime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minutes),
                "The countdown must be at least one minute.");
        }

        return utcNow.Add(duration);
    }

    public static DateTimeOffset FromLocalTime(
        DateTime date,
        int hour,
        int minute,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        if (hour is < 0 or > 23 || minute is < 0 or > 59)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hour),
                "Hour must be between 0 and 23 and minute must be between 0 and 59.");
        }

        var localTime = DateTime.SpecifyKind(
            date.Date.AddHours(hour).AddMinutes(minute),
            DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(localTime))
        {
            throw new ArgumentException(
                "The selected local time does not exist because of a daylight-saving transition.",
                nameof(date));
        }

        if (timeZone.IsAmbiguousTime(localTime))
        {
            throw new ArgumentException(
                "The selected local time is ambiguous because of a daylight-saving transition.",
                nameof(date));
        }

        return new DateTimeOffset(localTime, timeZone.GetUtcOffset(localTime)).ToUniversalTime();
    }
}
