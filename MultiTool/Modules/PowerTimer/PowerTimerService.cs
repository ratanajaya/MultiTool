using MultiTool.Core.Notifications;
using MultiTool.Core.Scheduling;

namespace MultiTool.Modules.PowerTimer;

public sealed class PowerTimerService : IAsyncDisposable
{
    internal static readonly TimeSpan WarningLeadTime = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan SchedulingClockTolerance = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan SchedulerInterval = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan MaximumObservedGap = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan DueTolerance = TimeSpan.FromSeconds(5);

    private readonly IPowerActionService _powerActions;
    private readonly INotificationService _notifications;
    private readonly IPowerTimerScheduleStore _scheduleStore;
    private readonly IClock _clock;
    private readonly IAsyncDelay _delay;
    private readonly ISystemTimeEvents _systemTimeEvents;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateGate = new();
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private PowerTimerSchedule? _activeSchedule;
    private PowerTimerPhase _phase = PowerTimerPhase.Idle;
    private string? _lastError;
    private int _systemTimeChanged;
    private bool _disposed;

    public PowerTimerService(
        IPowerActionService powerActions,
        INotificationService notifications,
        IPowerTimerScheduleStore scheduleStore,
        IClock clock,
        IAsyncDelay delay,
        ISystemTimeEvents systemTimeEvents)
    {
        _powerActions = powerActions;
        _notifications = notifications;
        _scheduleStore = scheduleStore;
        _clock = clock;
        _delay = delay;
        _systemTimeEvents = systemTimeEvents;
        _systemTimeEvents.Resumed += HandleSystemTimeChanged;
        _systemTimeEvents.TimeChanged += HandleSystemTimeChanged;
    }

    public event EventHandler? StatusChanged;

    public PowerTimerPhase Phase
    {
        get
        {
            lock (_stateGate)
            {
                return _phase;
            }
        }
    }

    public PowerTimerSchedule? ActiveSchedule
    {
        get
        {
            lock (_stateGate)
            {
                return _activeSchedule;
            }
        }
    }

    public string? LastError
    {
        get
        {
            lock (_stateGate)
            {
                return _lastError;
            }
        }
    }

    public TimeSpan? Remaining
    {
        get
        {
            var schedule = ActiveSchedule;
            return schedule is null
                ? null
                : TimeSpan.FromTicks(Math.Max(0, (schedule.TargetUtc - _clock.UtcNow).Ticks));
        }
    }

    public bool IsActionAvailable(PowerAction action) => _powerActions.IsAvailable(action);

    public async Task InitializeAsync(
        PowerTimerSchedule? savedSchedule,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (savedSchedule is null)
        {
            SetState(null, PowerTimerPhase.Idle, null);
            return;
        }

        var errors = savedSchedule.Validate();
        if (errors.Count > 0)
        {
            await _scheduleStore.SaveAsync(null, cancellationToken);
            SetState(null, PowerTimerPhase.Faulted, string.Join(" ", errors));
            return;
        }

        if (savedSchedule.TargetUtc <= _clock.UtcNow)
        {
            await _scheduleStore.SaveAsync(null, cancellationToken);
            SetState(
                null,
                PowerTimerPhase.Expired,
                "The saved timer expired while MultiTool was unavailable and was not executed.");
            return;
        }

        if (!_powerActions.IsAvailable(savedSchedule.Action))
        {
            await _scheduleStore.SaveAsync(null, cancellationToken);
            SetState(
                null,
                PowerTimerPhase.Faulted,
                $"{GetActionName(savedSchedule.Action)} is no longer available on this computer.");
            return;
        }

        await StartScheduleAsync(savedSchedule, persist: false, cancellationToken);
    }

    public Task ScheduleAsync(
        PowerAction action,
        DateTimeOffset targetUtc,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_powerActions.IsAvailable(action))
        {
            throw new InvalidOperationException($"{GetActionName(action)} is not available on this computer.");
        }

        if (targetUtc - _clock.UtcNow < WarningLeadTime - SchedulingClockTolerance)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetUtc),
                "The timer must be scheduled at least one minute in the future.");
        }

        var schedule = new PowerTimerSchedule
        {
            Action = action,
            TargetUtc = targetUtc.ToUniversalTime()
        };

        return StartScheduleAsync(schedule, persist: true, cancellationToken);
    }

    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            await _scheduleStore.SaveAsync(null, cancellationToken);
            CancelRunLoop();
            SetState(null, PowerTimerPhase.Idle, null);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task RunNowAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var schedule = ActiveSchedule;
        if (schedule is null)
        {
            return;
        }

        await ExecuteAsync(schedule.Id, cancellationToken);
    }

    private async Task StartScheduleAsync(
        PowerTimerSchedule schedule,
        bool persist,
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            if (Phase == PowerTimerPhase.Executing)
            {
                throw new InvalidOperationException(
                    "A power action is already being executed.");
            }

            if (persist)
            {
                await _scheduleStore.SaveAsync(schedule, cancellationToken);
            }

            CancelRunLoop();
            var runCancellation = new CancellationTokenSource();
            _runCancellation = runCancellation;
            SetState(
                schedule,
                schedule.WarningSent ? PowerTimerPhase.WarningSent : PowerTimerPhase.Scheduled,
                null);
            _runTask = Task.Run(() => RunSafelyAsync(schedule.Id, runCancellation.Token));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task RunAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        var previousObservation = _clock.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            var schedule = ActiveSchedule;
            if (schedule is null || schedule.Id != scheduleId)
            {
                return;
            }

            var now = _clock.UtcNow;
            var observationGap = now - previousObservation;
            var systemTimeChanged = Interlocked.Exchange(ref _systemTimeChanged, 0) != 0;

            if (now >= schedule.TargetUtc)
            {
                var missed = systemTimeChanged
                    || observationGap > MaximumObservedGap
                    || now - schedule.TargetUtc > DueTolerance;

                if (missed)
                {
                    await ExpireAsync(scheduleId, cancellationToken);
                }
                else
                {
                    await ExecuteAsync(scheduleId, cancellationToken);
                }

                return;
            }

            if (!schedule.WarningSent && schedule.TargetUtc - now <= WarningLeadTime)
            {
                await SendWarningAsync(scheduleId, cancellationToken);
            }

            StatusChanged?.Invoke(this, EventArgs.Empty);
            previousObservation = now;

            try
            {
                await _delay.DelayAsync(SchedulerInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task RunSafelyAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        try
        {
            await RunAsync(scheduleId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation is the normal reschedule, cancellation, and shutdown path.
        }
        catch (Exception exception)
        {
            var schedule = ActiveSchedule;
            if (schedule?.Id == scheduleId)
            {
                SetState(schedule, PowerTimerPhase.Faulted, exception.Message);
            }
        }
    }

    private async Task SendWarningAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var schedule = ActiveSchedule;
            if (schedule is null || schedule.Id != scheduleId || schedule.WarningSent)
            {
                return;
            }

            schedule = schedule with { WarningSent = true };
            await _scheduleStore.SaveAsync(schedule, cancellationToken);
            SetState(schedule, PowerTimerPhase.WarningSent, null);

            try
            {
                _notifications.Show(
                    "MultiTool power timer",
                    $"{GetActionName(schedule.Action)} in 1 minute. Open MultiTool to cancel.");
            }
            catch (Exception exception)
            {
                SetState(schedule, PowerTimerPhase.WarningSent, exception.Message);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task ExpireAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var schedule = ActiveSchedule;
            if (schedule is null || schedule.Id != scheduleId)
            {
                return;
            }

            await _scheduleStore.SaveAsync(null, cancellationToken);
            CancelRunLoop();
            SetState(
                null,
                PowerTimerPhase.Expired,
                "The timer deadline was missed and the power action was not executed.");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task ExecuteAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        PowerAction action;

        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var schedule = ActiveSchedule;
            if (schedule is null || schedule.Id != scheduleId)
            {
                return;
            }

            action = schedule.Action;
            await _scheduleStore.SaveAsync(null, cancellationToken);
            CancelRunLoop();
            SetState(null, PowerTimerPhase.Executing, null);
        }
        finally
        {
            _operationGate.Release();
        }

        try
        {
            await _powerActions.ExecuteAsync(action, CancellationToken.None);
            SetState(null, PowerTimerPhase.Idle, null);
        }
        catch (Exception exception)
        {
            SetState(null, PowerTimerPhase.Faulted, exception.Message);
        }
    }

    private void CancelRunLoop()
    {
        var cancellation = _runCancellation;
        _runCancellation = null;
        _runTask = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private void HandleSystemTimeChanged(object? sender, EventArgs eventArgs) =>
        Interlocked.Exchange(ref _systemTimeChanged, 1);

    private void SetState(
        PowerTimerSchedule? schedule,
        PowerTimerPhase phase,
        string? error)
    {
        lock (_stateGate)
        {
            _activeSchedule = schedule;
            _phase = phase;
            _lastError = error;
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string GetActionName(PowerAction action) => action switch
    {
        PowerAction.Shutdown => "Shutdown",
        PowerAction.Sleep => "Sleep",
        PowerAction.Hibernate => "Hibernate",
        _ => "Power action"
    };

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await CancelAsync();
        _disposed = true;
        _systemTimeEvents.Resumed -= HandleSystemTimeChanged;
        _systemTimeEvents.TimeChanged -= HandleSystemTimeChanged;
        _systemTimeEvents.Dispose();
        _operationGate.Dispose();
    }
}
