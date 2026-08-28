using MultiTool.Modules.AudioOutputKeepAlive;

namespace MultiTool.Core.Settings;

public sealed record MultiToolSettings
{
    public int Version { get; init; } = 1;

    public AudioOutputKeepAliveSettings AudioOutputKeepAlive { get; init; } = new();
}
