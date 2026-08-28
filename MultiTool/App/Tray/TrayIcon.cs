using System.Drawing;
using Forms = System.Windows.Forms;
using MultiTool.Core.Notifications;

namespace MultiTool.Presentation.Tray;

internal sealed class TrayIcon : INotificationService, IDisposable
{
    private readonly Action _openRequested;
    private readonly Forms.NotifyIcon _notifyIcon;
    private string? _activationContext;

    public TrayIcon(Action openRequested, Func<Task> exitAsync)
    {
        _openRequested = openRequested;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open MultiTool", null, (_, _) => _openRequested());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await exitAsync());

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "MultiTool",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => _openRequested();
        _notifyIcon.BalloonTipClicked += (_, _) =>
            Activated?.Invoke(this, new NotificationActivatedEventArgs(_activationContext));
    }

    public event EventHandler<NotificationActivatedEventArgs>? Activated;

    public void Show(string title, string message) => Show(title, message, null);

    public void Show(string title, string message, string? activationContext)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _activationContext = activationContext;
            _notifyIcon.ShowBalloonTip(
                10_000,
                title,
                message,
                Forms.ToolTipIcon.Warning);
        });
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
