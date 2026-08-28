using MultiTool.Core.Settings;
using MultiTool.Modules.AudioOutputKeepAlive;

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
}
