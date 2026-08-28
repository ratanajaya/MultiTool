namespace MultiTool.Modules.PowerTimer;

public interface ISystemTimeEvents : IDisposable
{
    event EventHandler? Resumed;

    event EventHandler? TimeChanged;
}
