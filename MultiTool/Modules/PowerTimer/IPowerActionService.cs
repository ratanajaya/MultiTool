namespace MultiTool.Modules.PowerTimer;

public interface IPowerActionService
{
    bool IsAvailable(PowerAction action);

    Task ExecuteAsync(PowerAction action, CancellationToken cancellationToken);
}
