using MultiTool.Core.Modules;
using MultiTool.Core.Scheduling;
using MultiTool.Modules.MicrophoneKeepAwake;

namespace MultiTool.Tests.Modules.MicrophoneKeepAwake;

[TestClass]
public sealed class MicrophoneKeepAwakeServiceTests
{
    [TestMethod]
    public async Task Start_OpensContinuousCapture_AndStopCancelsIt()
    {
        var capture = new FakeAudioCaptureService();
        await using var service = new MicrophoneKeepAwakeService(capture, new ImmediateDelay());

        await service.StartAsync(new MicrophoneKeepAwakeSettings { DeviceId = "microphone-42" });
        await WaitUntilAsync(() => capture.CaptureCount == 1);

        Assert.AreEqual(ModuleState.Running, service.State);
        Assert.AreEqual("microphone-42", capture.LastDeviceId);
        Assert.IsNotNull(service.StartedAt);

        await service.StopAsync();

        Assert.AreEqual(ModuleState.Stopped, service.State);
        Assert.IsTrue(capture.LastCancellationToken.IsCancellationRequested);
        Assert.IsNull(service.StartedAt);
    }

    [TestMethod]
    public async Task PermissionDenial_FaultsWithoutRetrying()
    {
        var capture = new FakeAudioCaptureService();
        capture.Failures.Enqueue(new MicrophoneAccessDeniedException("Access denied."));
        var delay = new ImmediateDelay();
        await using var service = new MicrophoneKeepAwakeService(capture, delay);

        await service.StartAsync(new MicrophoneKeepAwakeSettings());
        await WaitUntilAsync(() => service.State == ModuleState.Faulted);

        Assert.AreEqual(1, capture.CaptureCount);
        Assert.AreEqual(0, delay.DelayCount);
        Assert.AreEqual("Access denied.", service.LastError);
    }

    [TestMethod]
    public async Task DeviceLoss_DoesNotRestartWhenSettingIsDisabled()
    {
        var capture = new FakeAudioCaptureService();
        capture.Failures.Enqueue(new MicrophoneDeviceUnavailableException("Disconnected."));
        var delay = new ImmediateDelay();
        await using var service = new MicrophoneKeepAwakeService(capture, delay);

        await service.StartAsync(new MicrophoneKeepAwakeSettings
        {
            RestartOnDeviceChange = false
        });
        await WaitUntilAsync(() => service.State == ModuleState.Faulted);

        Assert.AreEqual(1, capture.CaptureCount);
        Assert.AreEqual(0, delay.DelayCount);
    }

    [TestMethod]
    public async Task DeviceLoss_RestartsAndRecoversWhenSettingIsEnabled()
    {
        var capture = new FakeAudioCaptureService();
        capture.Failures.Enqueue(new MicrophoneDeviceUnavailableException("Disconnected."));
        var delay = new ImmediateDelay();
        await using var service = new MicrophoneKeepAwakeService(capture, delay);

        await service.StartAsync(new MicrophoneKeepAwakeSettings
        {
            RestartOnDeviceChange = true
        });
        await WaitUntilAsync(() => capture.CaptureCount == 2);

        Assert.AreEqual(ModuleState.Running, service.State);
        Assert.AreEqual(1, delay.DelayCount);
        Assert.IsNull(service.LastError);
    }

    [TestMethod]
    public async Task RepeatedCaptureFailures_StopAfterBoundedAttemptCount()
    {
        var capture = new FakeAudioCaptureService();
        for (var index = 0; index < MicrophoneKeepAwakeService.MaximumCaptureAttempts; index++)
        {
            capture.Failures.Enqueue(new MicrophoneCaptureInterruptedException("Interrupted."));
        }

        var delay = new ImmediateDelay();
        await using var service = new MicrophoneKeepAwakeService(capture, delay);

        await service.StartAsync(new MicrophoneKeepAwakeSettings());
        await WaitUntilAsync(() => capture.CaptureCount == 3 && service.State == ModuleState.Faulted);

        Assert.AreEqual(MicrophoneKeepAwakeService.MaximumCaptureAttempts, capture.CaptureCount);
        Assert.AreEqual(MicrophoneKeepAwakeService.MaximumCaptureAttempts - 1, delay.DelayCount);
    }

    [TestMethod]
    public async Task StartAndStop_AreIdempotent()
    {
        var capture = new FakeAudioCaptureService();
        await using var service = new MicrophoneKeepAwakeService(capture, new ImmediateDelay());
        var settings = new MicrophoneKeepAwakeSettings();

        await service.StartAsync(settings);
        await service.StartAsync(settings);
        await WaitUntilAsync(() => capture.CaptureCount == 1);
        await service.StopAsync();
        await service.StopAsync();

        Assert.AreEqual(1, capture.CaptureCount);
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

    private sealed class FakeAudioCaptureService : IAudioCaptureService
    {
        public Queue<Exception> Failures { get; } = new();

        public int CaptureCount { get; private set; }

        public string? LastDeviceId { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public IReadOnlyList<AudioCaptureDevice> GetActiveCaptureDevices() =>
            [new AudioCaptureDevice("test-device", "Test microphone")];

        public Task CaptureAndDiscardAsync(
            string? deviceId,
            CancellationToken cancellationToken)
        {
            CaptureCount++;
            LastDeviceId = deviceId;
            LastCancellationToken = cancellationToken;

            return Failures.TryDequeue(out var exception)
                ? Task.FromException(exception)
                : Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class ImmediateDelay : IAsyncDelay
    {
        public int DelayCount { get; private set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            DelayCount++;
            return Task.CompletedTask;
        }
    }
}
