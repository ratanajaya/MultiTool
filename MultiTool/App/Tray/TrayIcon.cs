using System.Drawing;
using Forms = System.Windows.Forms;
using MultiTool.Presentation.Shell;

namespace MultiTool.Presentation.Tray;

internal sealed class TrayIcon : IDisposable
{
    private readonly MainWindow _window;
    private readonly Forms.NotifyIcon _notifyIcon;

    public TrayIcon(MainWindow window, Func<Task> exitAsync)
    {
        _window = window;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open MultiTool", null, (_, _) => ShowWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await exitAsync());

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "MultiTool",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
    }

    private void ShowWindow()
    {
        _window.Dispatcher.Invoke(() =>
        {
            _window.Show();
            if (_window.WindowState == System.Windows.WindowState.Minimized)
            {
                _window.WindowState = System.Windows.WindowState.Normal;
            }

            _window.Activate();
        });
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
