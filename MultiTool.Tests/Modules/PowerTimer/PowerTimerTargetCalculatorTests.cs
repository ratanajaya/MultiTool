using MultiTool.Modules.PowerTimer;

namespace MultiTool.Tests.Modules.PowerTimer;

[TestClass]
public sealed class PowerTimerTargetCalculatorTests
{
    [TestMethod]
    public void Countdown_AddsHoursAndMinutesToUtcNow()
    {
        var now = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);

        var target = PowerTimerTargetCalculator.FromCountdown(now, 2, 15);

        Assert.AreEqual(now.AddHours(2).AddMinutes(15), target);
    }

    [TestMethod]
    public void Countdown_RejectsLessThanOneMinute()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            PowerTimerTargetCalculator.FromCountdown(DateTimeOffset.UtcNow, 0, 0));
    }

    [TestMethod]
    public void LocalTime_ConvertsAnUnambiguousTimeToUtc()
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");

        var target = PowerTimerTargetCalculator.FromLocalTime(
            new DateTime(2026, 8, 28),
            17,
            30,
            timeZone);

        Assert.AreEqual(
            new DateTimeOffset(2026, 8, 28, 10, 30, 0, TimeSpan.Zero),
            target);
    }

    [TestMethod]
    public void LocalTime_RejectsInvalidAndAmbiguousDaylightSavingTimes()
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        Assert.ThrowsException<ArgumentException>(() =>
            PowerTimerTargetCalculator.FromLocalTime(
                new DateTime(2026, 3, 8),
                2,
                30,
                timeZone));
        Assert.ThrowsException<ArgumentException>(() =>
            PowerTimerTargetCalculator.FromLocalTime(
                new DateTime(2026, 11, 1),
                1,
                30,
                timeZone));
    }
}
