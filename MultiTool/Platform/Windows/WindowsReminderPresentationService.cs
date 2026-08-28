using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using MultiTool.Modules.UsageReminders;
using MultiTool.Modules.UsageReminders.Views;

namespace MultiTool.Platform.Windows;

internal sealed class WindowsReminderPresentationService : IReminderPresentationService
{
    private readonly Dispatcher _dispatcher;
    private readonly IFullScreenDetector _fullScreenDetector;
    private readonly Dictionary<Guid, ReminderOverlayWindow> _windows = [];
    private readonly Dictionary<Guid, ReminderPresentation> _pending = [];
    private readonly DispatcherTimer _pendingTimer;
    private bool _disposed;

    public WindowsReminderPresentationService(IFullScreenDetector fullScreenDetector)
    {
        _dispatcher = System.Windows.Application.Current.Dispatcher;
        _fullScreenDetector = fullScreenDetector;
        _pendingTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(2),
            DispatcherPriority.Background,
            HandlePendingTimer,
            _dispatcher);
    }

    public event EventHandler<ReminderResponseEventArgs>? ResponseRequested;

    public void Show(ReminderPresentation presentation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _dispatcher.BeginInvoke(() => ShowCore(presentation));
    }

    public void Close(Guid occurrenceId)
    {
        if (_disposed)
        {
            return;
        }

        _dispatcher.BeginInvoke(() => CloseCore(occurrenceId));
    }

    private void ShowCore(ReminderPresentation presentation)
    {
        if (_disposed || _windows.ContainsKey(presentation.OccurrenceId))
        {
            return;
        }

        if (_fullScreenDetector.IsForegroundWindowFullScreen())
        {
            _pending[presentation.OccurrenceId] = presentation;
            _pendingTimer.Start();
            return;
        }

        var window = new ReminderOverlayWindow(presentation);
        window.ResponseRequested += HandleWindowResponseRequested;
        window.SizeModeChanged += HandleWindowSizeModeChanged;
        window.Closed += HandleWindowClosed;
        _windows.Add(presentation.OccurrenceId, window);
        window.Show();
        RepositionWindows();
    }

    private void HandlePendingTimer(object? sender, EventArgs eventArgs)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var expired in _pending.Values.Where(item => item.ExpiresUtc < now).ToArray())
        {
            _pending.Remove(expired.OccurrenceId);
        }

        if (!_fullScreenDetector.IsForegroundWindowFullScreen())
        {
            var ready = _pending.Values.ToArray();
            _pending.Clear();
            foreach (var presentation in ready)
            {
                ShowCore(presentation);
            }
        }

        if (_pending.Count == 0)
        {
            _pendingTimer.Stop();
        }
    }

    private void HandleWindowResponseRequested(object? sender, ReminderResponseEventArgs eventArgs) =>
        ResponseRequested?.Invoke(this, eventArgs);

    private void HandleWindowSizeModeChanged(object? sender, EventArgs eventArgs) =>
        _dispatcher.BeginInvoke(RepositionWindows, DispatcherPriority.Loaded);

    private void HandleWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is not ReminderOverlayWindow window)
        {
            return;
        }

        window.ResponseRequested -= HandleWindowResponseRequested;
        window.SizeModeChanged -= HandleWindowSizeModeChanged;
        window.Closed -= HandleWindowClosed;
        _windows.Remove(window.OccurrenceId);
        RepositionWindows();
    }

    private void CloseCore(Guid occurrenceId)
    {
        _pending.Remove(occurrenceId);
        if (_windows.Remove(occurrenceId, out var window))
        {
            window.ResponseRequested -= HandleWindowResponseRequested;
            window.SizeModeChanged -= HandleWindowSizeModeChanged;
            window.Closed -= HandleWindowClosed;
            window.Close();
        }

        RepositionWindows();
    }

    private void RepositionWindows()
    {
        if (_windows.Count == 0)
        {
            return;
        }

        var cursor = Control.MousePosition;
        var screen = Screen.FromPoint(cursor);
        var dpi = VisualTreeHelper.GetDpi(_windows.Values.First());
        var workLeft = screen.WorkingArea.Left / dpi.DpiScaleX;
        var workTop = screen.WorkingArea.Top / dpi.DpiScaleY;
        var workRight = screen.WorkingArea.Right / dpi.DpiScaleX;
        var workBottom = screen.WorkingArea.Bottom / dpi.DpiScaleY;
        var bottom = workBottom - 12;

        foreach (var window in _windows.Values.Reverse())
        {
            window.Left = Math.Max(workLeft + 12, workRight - window.ActualWidth - 12);
            bottom -= window.ActualHeight;
            window.Top = Math.Max(workTop + 12, bottom);
            bottom -= 10;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dispatcher.Invoke(() =>
        {
            _pendingTimer.Stop();
            _pending.Clear();
            foreach (var window in _windows.Values.ToArray())
            {
                window.Close();
            }

            _windows.Clear();
        });
    }
}
