namespace MultiTool.Core.Notifications;

public interface INotificationService
{
    event EventHandler<NotificationActivatedEventArgs>? Activated
    {
        add { }
        remove { }
    }

    void Show(string title, string message);

    void Show(string title, string message, string? activationContext) =>
        Show(title, message);
}

public sealed class NotificationActivatedEventArgs(string? context) : EventArgs
{
    public string? Context { get; } = context;
}
