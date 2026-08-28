using MultiTool.Modules.AudioOutputKeepAlive;
using MultiTool.Modules.MicrophoneKeepAwake;
using MultiTool.Modules.MongoDbBackup;
using MultiTool.Modules.PowerTimer;
using MultiTool.Modules.UsageReminders;

namespace MultiTool.Core.Settings;

public sealed record MultiToolSettings
{
    public int Version { get; init; } = 1;

    public AudioOutputKeepAliveSettings AudioOutputKeepAlive { get; init; } = new();

    public MicrophoneKeepAwakeSettings MicrophoneKeepAwake { get; init; } = new();

    public PowerTimerSettings PowerTimer { get; init; } = new();

    public UsageReminderSettings UsageReminders { get; init; } = new();

    public MongoDbBackupSettings MongoDbBackup { get; init; } = new();
}
