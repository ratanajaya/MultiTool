namespace MultiTool.Presentation.ViewModels;

public sealed record ModuleNavigationItem(
    ModulePage Page,
    string Title,
    string Availability)
{
    public static IReadOnlyList<ModuleNavigationItem> CreateDefaultNavigation() =>
    [
        new(ModulePage.AudioOutputKeepAlive, "Audio output keep-alive", "Available"),
        new(ModulePage.MicrophoneKeepAwake, "Microphone keep-awake", "Planned"),
        new(ModulePage.PowerTimer, "Power timer", "Planned"),
        new(ModulePage.UsageReminders, "Usage reminders", "Planned"),
        new(ModulePage.MongoDbBackup, "MongoDB backup", "Planned")
    ];
}
