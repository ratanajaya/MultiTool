using Microsoft.Win32;
using MultiTool.Modules.PowerTimer;

namespace MultiTool.Platform.Windows;

public sealed class WindowsSystemTimeEvents : ISystemTimeEvents
{
    private bool _disposed;

    public WindowsSystemTimeEvents()
    {
        SystemEvents.PowerModeChanged += HandlePowerModeChanged;
        SystemEvents.TimeChanged += HandleTimeChanged;
    }

    public event EventHandler? Resumed;

    public event EventHandler? TimeChanged;

    private void HandlePowerModeChanged(object? sender, PowerModeChangedEventArgs eventArgs)
    {
        if (eventArgs.Mode == PowerModes.Resume)
        {
            Resumed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void HandleTimeChanged(object? sender, EventArgs eventArgs) =>
        TimeChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.PowerModeChanged -= HandlePowerModeChanged;
        SystemEvents.TimeChanged -= HandleTimeChanged;
    }
}
