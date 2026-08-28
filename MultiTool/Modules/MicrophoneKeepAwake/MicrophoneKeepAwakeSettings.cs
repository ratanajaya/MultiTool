namespace MultiTool.Modules.MicrophoneKeepAwake;

public sealed record MicrophoneKeepAwakeSettings
{
    public bool Enabled { get; init; }

    public string? DeviceId { get; init; }

    public bool RestartOnDeviceChange { get; init; } = true;

    public IReadOnlyList<string> Validate()
    {
        if (DeviceId is { Length: > 1_024 })
        {
            return ["The microphone device identifier is invalid."];
        }

        return [];
    }
}
