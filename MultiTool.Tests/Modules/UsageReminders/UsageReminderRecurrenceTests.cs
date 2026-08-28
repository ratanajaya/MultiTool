using MultiTool.Modules.UsageReminders;

namespace MultiTool.Tests.Modules.UsageReminders;

[TestClass]
public sealed class UsageReminderRecurrenceTests
{
    [TestMethod]
    public void NextOccurrence_UsesSelectedLocalWeekdays()
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        var profile = new UsageReminderProfile
        {
            Hour = 17,
            Minute = 30,
            ActiveDays = ReminderDays.Monday
        };
        var fridayUtc = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

        var next = UsageReminderRecurrence.GetNextOccurrence(profile, fridayUtc, timeZone);

        Assert.AreEqual(
            new DateTimeOffset(2026, 8, 31, 10, 30, 0, TimeSpan.Zero),
            next);
    }

    [TestMethod]
    public void MostRecentOccurrence_ReturnsTodaysDueOccurrence()
    {
        var profile = new UsageReminderProfile
        {
            Hour = 10,
            ActiveDays = ReminderDays.EveryDay
        };
        var now = new DateTimeOffset(2026, 8, 28, 10, 3, 0, TimeSpan.Zero);

        var occurrence = UsageReminderRecurrence.GetMostRecentOccurrence(
            profile,
            now,
            TimeZoneInfo.Utc);

        Assert.AreEqual(now.AddMinutes(-3), occurrence);
    }

    [TestMethod]
    public void DaylightSaving_InvalidTimeIsSkippedAndAmbiguousTimeUsesEarlierInstant()
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var invalid = new UsageReminderProfile
        {
            Hour = 2,
            Minute = 30,
            ActiveDays = ReminderDays.Sunday | ReminderDays.Monday
        };

        var after = new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero);
        var next = UsageReminderRecurrence.GetNextOccurrence(invalid, after, timeZone);

        Assert.AreEqual(
            new DateTimeOffset(2026, 3, 9, 6, 30, 0, TimeSpan.Zero),
            next);

        var ambiguousLocal = new DateTime(2026, 11, 1, 1, 30, 0, DateTimeKind.Unspecified);
        var ambiguousUtc = UsageReminderRecurrence.ConvertLocalToUtc(ambiguousLocal, timeZone);
        Assert.AreEqual(
            new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero),
            ambiguousUtc);
    }
}
