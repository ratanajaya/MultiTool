using MultiTool.Core.Modules;
using MultiTool.Core.Scheduling;

namespace MultiTool.Modules.AudioOutputKeepAlive;

public sealed class AudioOutputKeepAliveService : IAsyncDisposable
{
    private readonly IAudioOutputService _audioOutput;
    private readonly IAsyncDelay _delay;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _playbackGate = new(1, 1);
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;

    public AudioOutputKeepAliveService(IAudioOutputService audioOutput, IAsyncDelay delay)
    {
        _audioOutput = audioOutput;
        _delay = delay;
    }

    public event EventHandler? StatusChanged;

    public ModuleState State { get; private set; } = ModuleState.Stopped;

    public DateTimeOffset? LastBurstAt { get; private set; }

    public string? LastError { get; private set; }

    public IReadOnlyList<AudioOutputDevice> GetActiveOutputDevices() =>
        _audioOutput.GetActiveOutputDevices();

    public async Task StartAsync(
        AudioOutputKeepAliveSettings settings,
        CancellationToken cancellationToken = default)
    {
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(settings));
        }

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_runTask is { IsCompleted: false })
            {
                return;
            }

            SetStatus(ModuleState.Starting, null);
            _runCancellation = new CancellationTokenSource();
            SetStatus(ModuleState.Running, null);
            _runTask = Task.Run(() => RunAsync(settings, _runCancellation.Token));
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_runTask is null)
            {
                SetStatus(ModuleState.Stopped, LastError);
                return;
            }

            SetStatus(ModuleState.Stopping, LastError);
            _runCancellation!.Cancel();

            try
            {
                await _runTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (_runCancellation.IsCancellationRequested)
            {
                // Normal module shutdown.
            }

            _runCancellation.Dispose();
            _runCancellation = null;
            _runTask = null;
            SetStatus(ModuleState.Stopped, LastError);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task PlayNowAsync(
        AudioOutputKeepAliveSettings settings,
        CancellationToken cancellationToken = default)
    {
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(settings));
        }

        return PlayBurstAsync(settings, cancellationToken);
    }

    private async Task RunAsync(AudioOutputKeepAliveSettings settings, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PlayBurstAsync(settings, cancellationToken);
                SetStatus(ModuleState.Running, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                SetStatus(ModuleState.Faulted, exception.Message);
            }

            try
            {
                await _delay.DelayAsync(
                    TimeSpan.FromSeconds(settings.IntervalSeconds),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task PlayBurstAsync(
        AudioOutputKeepAliveSettings settings,
        CancellationToken cancellationToken)
    {
        await _playbackGate.WaitAsync(cancellationToken);
        try
        {
            await _audioOutput.PlayBurstAsync(
                settings.DeviceId,
                TimeSpan.FromMilliseconds(settings.BurstDurationMilliseconds),
                settings.Amplitude,
                cancellationToken);
            LastBurstAt = DateTimeOffset.Now;
            LastError = null;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    private void SetStatus(ModuleState state, string? error)
    {
        State = state;
        LastError = error;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycleGate.Dispose();
        _playbackGate.Dispose();
    }
}
