namespace MultiTool.Modules.MicrophoneKeepAwake;

public interface IAudioCaptureService
{
    IReadOnlyList<AudioCaptureDevice> GetActiveCaptureDevices();

    Task CaptureAndDiscardAsync(string? deviceId, CancellationToken cancellationToken);
}
