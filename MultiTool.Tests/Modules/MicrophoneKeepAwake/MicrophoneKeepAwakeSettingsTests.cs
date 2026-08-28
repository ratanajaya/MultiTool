using MultiTool.Modules.MicrophoneKeepAwake;

namespace MultiTool.Tests.Modules.MicrophoneKeepAwake;

[TestClass]
public sealed class MicrophoneKeepAwakeSettingsTests
{
    [TestMethod]
    public void Defaults_FollowWindowsDefaultDeviceAndRestartAfterDeviceChange()
    {
        var settings = new MicrophoneKeepAwakeSettings();

        Assert.IsNull(settings.DeviceId);
        Assert.IsTrue(settings.RestartOnDeviceChange);
        Assert.AreEqual(0, settings.Validate().Count);
    }

    [TestMethod]
    public void Validate_RejectsUnreasonablyLongDeviceIdentifier()
    {
        var settings = new MicrophoneKeepAwakeSettings
        {
            DeviceId = new string('x', 1_025)
        };

        Assert.AreEqual(1, settings.Validate().Count);
    }
}
