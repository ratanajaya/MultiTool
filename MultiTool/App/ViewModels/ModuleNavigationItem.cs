namespace MultiTool.Presentation.ViewModels;

public sealed record ModuleNavigationItem(
    ModulePage Page,
    string Title,
    string Availability)
{
    public static IReadOnlyList<ModuleNavigationItem> CreateDefaultNavigation() =>
    [
        new(ModulePage.AudioOutputKeepAlive, "Audio output keep-alive", "Available"),
        new(ModulePage.MicrophoneKeepAwake, "Microphone keep-awake", "Available"),
        new(ModulePage.PowerTimer, "Power timer", "Available"),
        new(ModulePage.UsageReminders, "Usage reminders", "Available"),
        new(ModulePage.MongoDbBackup, "MongoDB backup", "Available"),
        new(ModulePage.BatchFileOrganizer, "Batch file organizer", "Available")
    ];
}
