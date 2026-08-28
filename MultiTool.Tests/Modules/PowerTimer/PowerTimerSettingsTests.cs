using MultiTool.Modules.PowerTimer;

namespace MultiTool.Tests.Modules.PowerTimer;

[TestClass]
public sealed class PowerTimerSettingsTests
{
    [TestMethod]
    public void Defaults_AreValidAndHaveNoActiveSchedule()
    {
        var settings = new PowerTimerSettings();

        Assert.IsNull(settings.ActiveSchedule);
        Assert.AreEqual(0, settings.Validate().Count);
    }

    [TestMethod]
    public void Validate_RejectsAnInvalidPersistedSchedule()
    {
        var settings = new PowerTimerSettings
        {
            ActiveSchedule = new PowerTimerSchedule
            {
                Id = Guid.Empty,
                Action = (PowerAction)999,
                TargetUtc = DateTimeOffset.UtcNow.AddHours(1)
            }
        };

        Assert.AreEqual(2, settings.Validate().Count);
    }
}
