using MultiTool.Core.Modules;
using MultiTool.Core.Scheduling;
using MultiTool.Modules.AudioOutputKeepAlive;

namespace MultiTool.Tests.Modules.AudioOutputKeepAlive;

[TestClass]
public sealed class AudioOutputKeepAliveServiceTests
{
    [TestMethod]
    public async Task Start_PlaysImmediately_AndStopCancelsTheSchedule()
    {
        var audio = new FakeAudioOutputService();
        var delay = new ControlledDelay();
        await using var service = new AudioOutputKeepAliveService(audio, delay);

        await service.StartAsync(new AudioOutputKeepAliveSettings { IntervalSeconds = 42 });
        await WaitUntilAsync(() => audio.PlayCount == 1 && delay.DelayCount == 1);

        Assert.AreEqual(ModuleState.Running, service.State);
        Assert.AreEqual(TimeSpan.FromSeconds(42), delay.LastDelay);
        Assert.IsNotNull(service.LastBurstAt);

        await service.StopAsync();

        Assert.AreEqual(ModuleState.Stopped, service.State);
        Assert.IsTrue(delay.LastCancellationToken.IsCancellationRequested);
    }

    [TestMethod]
    public async Task Loop_RetriesAfterPlaybackFailure_AndRecovers()
    {
        var audio = new FakeAudioOutputService { FailuresRemaining = 1 };
        var delay = new ControlledDelay();
        await using var service = new AudioOutputKeepAliveService(audio, delay);

        await service.StartAsync(new AudioOutputKeepAliveSettings());
        await WaitUntilAsync(() => service.State == ModuleState.Faulted && delay.DelayCount == 1);

        Assert.AreEqual("Simulated device failure.", service.LastError);

        delay.Advance();
        await WaitUntilAsync(() => audio.PlayCount == 2 && service.State == ModuleState.Running);

        Assert.IsNull(service.LastError);
        Assert.IsNotNull(service.LastBurstAt);
    }

    [TestMethod]
    public async Task StartAndStop_AreIdempotent()
    {
        var audio = new FakeAudioOutputService();
        var delay = new ControlledDelay();
        await using var service = new AudioOutputKeepAliveService(audio, delay);
        var settings = new AudioOutputKeepAliveSettings();

        await service.StartAsync(settings);
        await service.StartAsync(settings);
        await WaitUntilAsync(() => audio.PlayCount == 1);
        await service.StopAsync();
        await service.StopAsync();

        Assert.AreEqual(1, audio.PlayCount);
        Assert.AreEqual(ModuleState.Stopped, service.State);
    }

    [TestMethod]
    public async Task Stop_CancelsPlaybackInProgress()
    {
        var audio = new FakeAudioOutputService { BlockPlayback = true };
        await using var service = new AudioOutputKeepAliveService(audio, new ControlledDelay());

        await service.StartAsync(new AudioOutputKeepAliveSettings());
        await WaitUntilAsync(() => audio.PlayCount == 1);
        await service.StopAsync();

        Assert.IsTrue(audio.LastCancellationToken.IsCancellationRequested);
        Assert.AreEqual(ModuleState.Stopped, service.State);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class FakeAudioOutputService : IAudioOutputService
    {
        public int PlayCount { get; private set; }

        public int FailuresRemaining { get; init; }

        public bool BlockPlayback { get; init; }

        public CancellationToken LastCancellationToken { get; private set; }

        public IReadOnlyList<AudioOutputDevice> GetActiveOutputDevices() =>
            [new AudioOutputDevice("test-device", "Test device")];

        public async Task PlayBurstAsync(
            string? deviceId,
            TimeSpan duration,
            float amplitude,
            CancellationToken cancellationToken)
        {
            PlayCount++;
            LastCancellationToken = cancellationToken;

            if (PlayCount <= FailuresRemaining)
            {
                throw new InvalidOperationException("Simulated device failure.");
            }

            if (BlockPlayback)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        }
    }

    private sealed class ControlledDelay : IAsyncDelay
    {
        private TaskCompletionSource _nextDelay = CreateCompletionSource();

        public int DelayCount { get; private set; }

        public TimeSpan LastDelay { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            DelayCount++;
            LastDelay = delay;
            LastCancellationToken = cancellationToken;
            var currentDelay = _nextDelay;
            await currentDelay.Task.WaitAsync(cancellationToken);
        }

        public void Advance()
        {
            var currentDelay = _nextDelay;
            _nextDelay = CreateCompletionSource();
            currentDelay.TrySetResult();
        }

        private static TaskCompletionSource CreateCompletionSource() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
