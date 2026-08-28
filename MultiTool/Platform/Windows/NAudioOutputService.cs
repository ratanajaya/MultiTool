using NAudio.CoreAudioApi;
using NAudio.Wave;
using MultiTool.Modules.AudioOutputKeepAlive;

namespace MultiTool.Platform.Windows;

public sealed class NAudioOutputService : IAudioOutputService
{
    private const int SampleRate = 48_000;
    private const int Channels = 2;

    public IReadOnlyList<AudioOutputDevice> GetActiveOutputDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(device => new AudioOutputDevice(device.ID, device.FriendlyName))
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task PlayBurstAsync(
        string? deviceId,
        TimeSpan duration,
        float amplitude,
        CancellationToken cancellationToken)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = string.IsNullOrWhiteSpace(deviceId)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            : enumerator.GetDevice(deviceId);
        using var output = new WasapiOut(
            device,
            AudioClientShareMode.Shared,
            useEventSync: true,
            latency: 100);

        var provider = new NearSilentNoiseProvider(SampleRate, Channels, amplitude, duration);
        var playbackFinished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        output.PlaybackStopped += HandlePlaybackStopped;
        try
        {
            output.Init(provider);
            using var cancellationRegistration = cancellationToken.Register(output.Stop);
            cancellationToken.ThrowIfCancellationRequested();
            output.Play();
            await playbackFinished.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            output.PlaybackStopped -= HandlePlaybackStopped;
        }

        return;

        void HandlePlaybackStopped(object? sender, StoppedEventArgs eventArgs)
        {
            if (eventArgs.Exception is not null)
            {
                playbackFinished.TrySetException(eventArgs.Exception);
            }
            else
            {
                playbackFinished.TrySetResult();
            }
        }
    }
}
