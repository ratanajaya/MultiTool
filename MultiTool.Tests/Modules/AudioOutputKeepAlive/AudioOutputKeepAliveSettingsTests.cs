using MultiTool.Modules.AudioOutputKeepAlive;

namespace MultiTool.Tests.Modules.AudioOutputKeepAlive;

[TestClass]
public sealed class AudioOutputKeepAliveSettingsTests
{
    [TestMethod]
    public void Defaults_AreValidAndMatchLegacyBehavior()
    {
        var settings = new AudioOutputKeepAliveSettings();

        Assert.AreEqual(60, settings.IntervalSeconds);
        Assert.AreEqual(250, settings.BurstDurationMilliseconds);
        Assert.AreEqual(0.00008f, settings.Amplitude);
        Assert.AreEqual(0, settings.Validate().Count);
    }

    [TestMethod]
    public void Validate_ReportsEveryOutOfRangeSetting()
    {
        var settings = new AudioOutputKeepAliveSettings
        {
            IntervalSeconds = 0,
            BurstDurationMilliseconds = 9,
            Amplitude = 0.5f
        };

        var errors = settings.Validate();

        Assert.AreEqual(3, errors.Count);
    }

    [TestMethod]
    public void Validate_RejectsNonFiniteAmplitude()
    {
        var settings = new AudioOutputKeepAliveSettings { Amplitude = float.NaN };

        Assert.AreEqual(1, settings.Validate().Count);
    }
}
