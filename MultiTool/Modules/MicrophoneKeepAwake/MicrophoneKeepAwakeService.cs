using MultiTool.Core.Modules;
using MultiTool.Core.Scheduling;

namespace MultiTool.Modules.MicrophoneKeepAwake;

public sealed class MicrophoneKeepAwakeService : IAsyncDisposable
{
    internal const int MaximumCaptureAttempts = 3;
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    private readonly IAudioCaptureService _audioCapture;
    private readonly IAsyncDelay _delay;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;

    public MicrophoneKeepAwakeService(IAudioCaptureService audioCapture, IAsyncDelay delay)
    {
        _audioCapture = audioCapture;
        _delay = delay;
    }

    public event EventHandler? StatusChanged;

    public ModuleState State { get; private set; } = ModuleState.Stopped;

    public DateTimeOffset? StartedAt { get; private set; }

    public string? LastError { get; private set; }

    public IReadOnlyList<AudioCaptureDevice> GetActiveCaptureDevices() =>
        _audioCapture.GetActiveCaptureDevices();

    public async Task StartAsync(
        MicrophoneKeepAwakeSettings settings,
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

            _runCancellation?.Dispose();
            _runCancellation = new CancellationTokenSource();
            SetStatus(ModuleState.Starting, null);
            _runTask = RunAsync(settings, _runCancellation.Token);
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
                SetStopped();
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
                // Cancellation is the normal capture shutdown path.
            }

            _runCancellation.Dispose();
            _runCancellation = null;
            _runTask = null;
            SetStopped();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task RunAsync(
        MicrophoneKeepAwakeSettings settings,
        CancellationToken cancellationToken)
    {
        var attempt = 0;

        while (!cancellationToken.IsCancellationRequested && attempt < MaximumCaptureAttempts)
        {
            try
            {
                attempt++;
                StartedAt = DateTimeOffset.Now;
                SetStatus(ModuleState.Running, null);
                await _audioCapture.CaptureAndDiscardAsync(settings.DeviceId, cancellationToken);

                if (!cancellationToken.IsCancellationRequested)
                {
                    throw new MicrophoneCaptureInterruptedException(
                        "Microphone capture stopped unexpectedly.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (MicrophoneAccessDeniedException exception)
            {
                SetStatus(ModuleState.Faulted, exception.Message);
                return;
            }
            catch (MicrophoneDeviceUnavailableException exception)
                when (!settings.RestartOnDeviceChange)
            {
                SetStatus(ModuleState.Faulted, exception.Message);
                return;
            }
            catch (Exception exception)
            {
                SetStatus(ModuleState.Faulted, exception.Message);
            }

            if (attempt >= MaximumCaptureAttempts)
            {
                return;
            }

            try
            {
                await _delay.DelayAsync(RetryDelay, cancellationToken);
                SetStatus(ModuleState.Starting, LastError);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private void SetStopped()
    {
        StartedAt = null;
        SetStatus(ModuleState.Stopped, LastError);
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
    }
}
