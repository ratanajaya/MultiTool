using MultiTool.Modules.UsageReminders;

namespace MultiTool.Tests.Modules.UsageReminders;

[TestClass]
public sealed class UsageReminderSettingsTests
{
    [TestMethod]
    public void Defaults_ProvideEditableWrapUpAndSignOffProfilesWithoutSoundSettings()
    {
        var settings = new UsageReminderSettings();

        CollectionAssert.AreEqual(
            new[] { "Wrap up", "Sign off" },
            settings.Profiles.Select(profile => profile.Name).ToArray());
        Assert.IsTrue(settings.Profiles.All(profile => profile.ShowCornerCard));
        Assert.IsTrue(settings.Profiles.All(profile => profile.ShowWindowsNotification));
        Assert.AreEqual(0, settings.Validate().Count);
    }

    [TestMethod]
    public void ProfileValidation_RejectsNoDaysAndNoPresentationMethod()
    {
        var profile = new UsageReminderProfile
        {
            ActiveDays = ReminderDays.None,
            ShowCornerCard = false,
            ShowWindowsNotification = false
        };

        var errors = profile.Validate();

        Assert.IsTrue(errors.Any(error => error.Contains("weekday", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(errors.Any(error => error.Contains("corner card", StringComparison.OrdinalIgnoreCase)));
    }
}
