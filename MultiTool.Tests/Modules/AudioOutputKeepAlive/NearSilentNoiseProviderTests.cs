using MultiTool.Modules.AudioOutputKeepAlive;

namespace MultiTool.Tests.Modules.AudioOutputKeepAlive;

[TestClass]
public sealed class NearSilentNoiseProviderTests
{
    [TestMethod]
    public void Read_ProducesExpectedNumberOfBoundedSamples()
    {
        const float amplitude = 0.001f;
        var provider = new NearSilentNoiseProvider(
            sampleRate: 1_000,
            channels: 2,
            amplitude,
            TimeSpan.FromMilliseconds(10));
        var samples = new float[32];

        var firstRead = provider.Read(samples, 4, samples.Length - 4);
        var secondRead = provider.Read(samples, 0, samples.Length);

        Assert.AreEqual(20, firstRead);
        Assert.AreEqual(0, secondRead);
        Assert.IsTrue(samples.Skip(4).Take(firstRead).Any(sample => sample != 0));
        Assert.IsTrue(samples.Skip(4).Take(firstRead).All(sample => Math.Abs(sample) <= amplitude));
    }
}
