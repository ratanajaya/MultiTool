using MultiTool.Core.Settings;
using MultiTool.Modules.AudioOutputKeepAlive;
using MultiTool.Modules.MicrophoneKeepAwake;

namespace MultiTool.Tests.Core.Settings;

[TestClass]
public sealed class JsonSettingsStoreTests
{
    [TestMethod]
    public async Task SaveAndLoad_RoundTripsModuleSettings()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "MultiTool.Tests",
            Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(testDirectory, "settings.json");

        try
        {
            var store = new JsonSettingsStore(settingsPath);
            var expected = new MultiToolSettings
            {
                AudioOutputKeepAlive = new AudioOutputKeepAliveSettings
                {
                    Enabled = true,
                    DeviceId = "device-42",
                    IntervalSeconds = 120,
                    BurstDurationMilliseconds = 300,
                    Amplitude = 0.0001f
                },
                MicrophoneKeepAwake = new MicrophoneKeepAwakeSettings
                {
                    Enabled = true,
                    DeviceId = "microphone-42",
                    RestartOnDeviceChange = false
                }
            };

            await store.SaveAsync(expected);
            var actual = await store.LoadAsync();

            Assert.AreEqual(expected, actual);
            Assert.IsFalse(File.Exists(settingsPath + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task Update_MergesOneModuleWithoutResettingAnother()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "MultiTool.Tests",
            Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(testDirectory, "settings.json");

        try
        {
            var store = new JsonSettingsStore(settingsPath);
            await store.SaveAsync(new MultiToolSettings
            {
                AudioOutputKeepAlive = new AudioOutputKeepAliveSettings { Enabled = true }
            });

            await store.UpdateAsync(settings => settings with
            {
                MicrophoneKeepAwake = new MicrophoneKeepAwakeSettings
                {
                    Enabled = true,
                    DeviceId = "microphone-42"
                }
            });

            var actual = await store.LoadAsync();
            Assert.IsTrue(actual.AudioOutputKeepAlive.Enabled);
            Assert.IsTrue(actual.MicrophoneKeepAwake.Enabled);
            Assert.AreEqual("microphone-42", actual.MicrophoneKeepAwake.DeviceId);
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }
}
