namespace MultiTool.Modules.AudioOutputKeepAlive;

public interface IAudioOutputService
{
    IReadOnlyList<AudioOutputDevice> GetActiveOutputDevices();

    Task PlayBurstAsync(
        string? deviceId,
        TimeSpan duration,
        float amplitude,
        CancellationToken cancellationToken);
}
