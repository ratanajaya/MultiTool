using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using MultiTool.Modules.PowerTimer;
using MultiTool.Core.Scheduling;
using MultiTool.Presentation.Commands;
using MultiTool.Presentation.Dialogs;

namespace MultiTool.Presentation.ViewModels;

public sealed class PowerTimerViewModel : INotifyPropertyChanged
{
    private readonly PowerTimerService _module;
    private readonly IConfirmationService _confirmationService;
    private readonly IClock _clock;
    private readonly Dispatcher _dispatcher;
    private readonly AsyncRelayCommand _scheduleCommand;
    private readonly AsyncRelayCommand _cancelCommand;
    private readonly AsyncRelayCommand _runNowCommand;
    private PowerActionOption _selectedAction;
    private bool _useCountdown = true;
    private int _countdownHours;
    private int _countdownMinutes = 30;
    private DateTime? _exactDate;
    private int _exactHour;
    private int _exactMinute;
    private string _status = "Idle";
    private string _currentAction = "None";
    private string _targetTime = "Not scheduled";
    private string _remaining = "--:--:--";
    private string _message = "Ready";

    public PowerTimerViewModel(
        PowerTimerService module,
        IConfirmationService confirmationService,
        IClock clock)
    {
        _module = module;
        _confirmationService = confirmationService;
        _clock = clock;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _module.StatusChanged += HandleStatusChanged;

        Actions = Enum.GetValues<PowerAction>()
            .Select(action => new PowerActionOption(
                action,
                _module.IsActionAvailable(action)
                    ? GetActionName(action)
                    : $"{GetActionName(action)} (unavailable)",
                _module.IsActionAvailable(action)))
            .ToArray();
        _selectedAction = Actions.First(option => option.IsAvailable);

        var initialExactTime = DateTime.Now.AddMinutes(30);
        _exactDate = initialExactTime.Date;
        _exactHour = initialExactTime.Hour;
        _exactMinute = initialExactTime.Minute;

        Hours = Enumerable.Range(0, 24).ToArray();
        Minutes = Enumerable.Range(0, 60).ToArray();

        _scheduleCommand = new AsyncRelayCommand(
            () => RunSafelyAsync(ScheduleAsync),
            () => SelectedAction.IsAvailable);
        _cancelCommand = new AsyncRelayCommand(
            () => RunSafelyAsync(CancelAsync),
            () => HasActiveTimer);
        _runNowCommand = new AsyncRelayCommand(
            () => RunSafelyAsync(RunNowAsync),
            () => HasActiveTimer);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<PowerActionOption> Actions { get; }

    public IReadOnlyList<int> Hours { get; }

    public IReadOnlyList<int> Minutes { get; }

    public PowerActionOption SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetField(ref _selectedAction, value))
            {
                _scheduleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool UseCountdown
    {
        get => _useCountdown;
        set
        {
            if (SetField(ref _useCountdown, value))
            {
                OnPropertyChanged(nameof(UseExactTime));
            }
        }
    }

    public bool UseExactTime
    {
        get => !UseCountdown;
        set
        {
            if (value)
            {
                UseCountdown = false;
            }
        }
    }

    public int CountdownHours
    {
        get => _countdownHours;
        set => SetField(ref _countdownHours, value);
    }

    public int CountdownMinutes
    {
        get => _countdownMinutes;
        set => SetField(ref _countdownMinutes, value);
    }

    public DateTime? ExactDate
    {
        get => _exactDate;
        set => SetField(ref _exactDate, value);
    }

    public int ExactHour
    {
        get => _exactHour;
        set => SetField(ref _exactHour, value);
    }

    public int ExactMinute
    {
        get => _exactMinute;
        set => SetField(ref _exactMinute, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string CurrentAction
    {
        get => _currentAction;
        private set => SetField(ref _currentAction, value);
    }

    public string TargetTime
    {
        get => _targetTime;
        private set => SetField(ref _targetTime, value);
    }

    public string Remaining
    {
        get => _remaining;
        private set => SetField(ref _remaining, value);
    }

    public string Message
    {
        get => _message;
        private set => SetField(ref _message, value);
    }

    public bool HasActiveTimer => _module.ActiveSchedule is not null;

    public string ScheduleButtonLabel => HasActiveTimer ? "Reschedule" : "Schedule";

    public ICommand ScheduleCommand => _scheduleCommand;

    public ICommand CancelCommand => _cancelCommand;

    public ICommand RunNowCommand => _runNowCommand;

    public async Task InitializeAsync(PowerTimerSettings settings)
    {
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            Message = $"Saved settings are invalid: {string.Join(" ", errors)}";
        }

        await _module.InitializeAsync(settings.ActiveSchedule);
        RefreshRuntimeStatus();
    }

    public async Task ShutdownAsync()
    {
        _module.StatusChanged -= HandleStatusChanged;
        await _module.DisposeAsync();
    }

    private async Task ScheduleAsync()
    {
        if (HasActiveTimer && !_confirmationService.Confirm(
                "Replace power timer",
                "Replace the currently active power timer?"))
        {
            return;
        }

        var targetUtc = UseCountdown
            ? BuildCountdownTarget()
            : BuildExactTarget();

        await _module.ScheduleAsync(SelectedAction.Value, targetUtc);
        Message = $"{GetActionName(SelectedAction.Value)} scheduled for {targetUtc.ToLocalTime():G}.";
    }

    private async Task CancelAsync()
    {
        await _module.CancelAsync();
        Message = "The power timer was cancelled.";
    }

    private async Task RunNowAsync()
    {
        var schedule = _module.ActiveSchedule;
        if (schedule is null)
        {
            return;
        }

        if (!_confirmationService.Confirm(
                $"Run {GetActionName(schedule.Action).ToLowerInvariant()} now",
                $"Run {GetActionName(schedule.Action).ToLowerInvariant()} now?"))
        {
            return;
        }

        Message = $"Requesting {GetActionName(schedule.Action).ToLowerInvariant()}...";
        await _module.RunNowAsync();
    }

    private DateTimeOffset BuildCountdownTarget()
        => PowerTimerTargetCalculator.FromCountdown(
            _clock.UtcNow,
            CountdownHours,
            CountdownMinutes);

    private DateTimeOffset BuildExactTarget()
    {
        if (ExactDate is null)
        {
            throw new InvalidOperationException("Select a date for the power timer.");
        }

        return PowerTimerTargetCalculator.FromLocalTime(
            ExactDate.Value,
            ExactHour,
            ExactMinute,
            TimeZoneInfo.Local);
    }

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            Message = exception.Message;
        }
        finally
        {
            RefreshRuntimeStatus();
        }
    }

    private void HandleStatusChanged(object? sender, EventArgs eventArgs)
    {
        if (_dispatcher.CheckAccess())
        {
            RefreshRuntimeStatus();
        }
        else
        {
            _dispatcher.BeginInvoke(RefreshRuntimeStatus);
        }
    }

    private void RefreshRuntimeStatus()
    {
        var schedule = _module.ActiveSchedule;
        Status = _module.Phase.ToString();
        CurrentAction = schedule is null ? "None" : GetActionName(schedule.Action);
        TargetTime = schedule?.TargetUtc.ToLocalTime().ToString("G") ?? "Not scheduled";
        Remaining = FormatRemaining(_module.Remaining);

        if (!string.IsNullOrWhiteSpace(_module.LastError))
        {
            Message = _module.LastError;
        }

        OnPropertyChanged(nameof(HasActiveTimer));
        OnPropertyChanged(nameof(ScheduleButtonLabel));
        _scheduleCommand.RaiseCanExecuteChanged();
        _cancelCommand.RaiseCanExecuteChanged();
        _runNowCommand.RaiseCanExecuteChanged();
    }

    private static string FormatRemaining(TimeSpan? remaining)
    {
        if (remaining is null)
        {
            return "--:--:--";
        }

        var value = remaining.Value;
        return value.TotalDays >= 1
            ? $"{(int)value.TotalDays}d {value.Hours:00}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    }

    private static string GetActionName(PowerAction action) => action switch
    {
        PowerAction.Shutdown => "Shutdown",
        PowerAction.Sleep => "Sleep",
        PowerAction.Hibernate => "Hibernate",
        _ => "Power action"
    };

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
