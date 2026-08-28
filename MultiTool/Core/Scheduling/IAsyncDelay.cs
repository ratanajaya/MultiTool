namespace MultiTool.Core.Scheduling;

public interface IAsyncDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class SystemAsyncDelay : IAsyncDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
