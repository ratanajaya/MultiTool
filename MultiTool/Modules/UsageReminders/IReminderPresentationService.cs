namespace MultiTool.Modules.UsageReminders;

public interface IReminderPresentationService : IDisposable
{
    event EventHandler<ReminderResponseEventArgs>? ResponseRequested;

    void Show(ReminderPresentation presentation);

    void Close(Guid occurrenceId);
}
