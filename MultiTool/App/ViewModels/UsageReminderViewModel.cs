using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using MultiTool.Modules.UsageReminders;
using MultiTool.Presentation.Commands;
using MultiTool.Presentation.Dialogs;

namespace MultiTool.Presentation.ViewModels;

public sealed class UsageReminderViewModel : INotifyPropertyChanged
{
    private readonly UsageReminderService _module;
    private readonly IConfirmationService _confirmationService;
    private readonly Dispatcher _dispatcher;
    private readonly AsyncRelayCommand _newCommand;
    private readonly AsyncRelayCommand _saveCommand;
    private readonly AsyncRelayCommand _deleteCommand;
    private readonly AsyncRelayCommand _previewCommand;
    private UsageReminderProfileItem? _selectedProfile;
    private Guid _draftId = Guid.NewGuid();
    private DateTimeOffset _draftCreatedUtc = DateTimeOffset.UtcNow;
    private string _name = "Reminder";
    private bool _isEnabled = true;
    private int _hour = 18;
    private int _minute;
    private string _title = "Time for a break";
    private string _reminderMessage = "Finish what you're doing and save your work.";
    private bool _showCornerCard = true;
    private bool _showWindowsNotification = true;
    private int _snoozeMinutes = 10;
    private int _gracePeriodMinutes = 5;
    private ReminderDays _activeDays = ReminderDays.Weekdays;
    private string _activity = "Ready";

    public UsageReminderViewModel(
        UsageReminderService module,
        IConfirmationService confirmationService)
    {
        _module = module;
        _confirmationService = confirmationService;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _module.StatusChanged += HandleStatusChanged;
        Hours = Enumerable.Range(0, 24).ToArray();
        Minutes = Enumerable.Range(0, 60).ToArray();
        SnoozeOptions = [5, 10, 15, 30];
        _newCommand = new AsyncRelayCommand(NewAsync);
        _saveCommand = new AsyncRelayCommand(() => RunSafelyAsync(SaveAsync));
        _deleteCommand = new AsyncRelayCommand(
            () => RunSafelyAsync(DeleteAsync),
            () => SelectedProfile is not null);
        _previewCommand = new AsyncRelayCommand(() => RunSafelyAsync(PreviewAsync));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<UsageReminderProfileItem> Profiles { get; } = [];

    public ObservableCollection<ReminderHistoryItem> RecentHistory { get; } = [];

    public IReadOnlyList<int> Hours { get; }

    public IReadOnlyList<int> Minutes { get; }

    public IReadOnlyList<int> SnoozeOptions { get; }

    public UsageReminderProfileItem? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetField(ref _selectedProfile, value) && value is not null)
            {
                LoadProfile(value.Profile);
            }

            _deleteCommand.RaiseCanExecuteChanged();
        }
    }

    public string Name { get => _name; set => SetField(ref _name, value); }

    public bool IsEnabled { get => _isEnabled; set => SetField(ref _isEnabled, value); }

    public int Hour { get => _hour; set => SetField(ref _hour, value); }

    public int Minute { get => _minute; set => SetField(ref _minute, value); }

    public string Title { get => _title; set => SetField(ref _title, value); }

    public string ReminderMessage
    {
        get => _reminderMessage;
        set => SetField(ref _reminderMessage, value);
    }

    public bool ShowCornerCard
    {
        get => _showCornerCard;
        set => SetField(ref _showCornerCard, value);
    }

    public bool ShowWindowsNotification
    {
        get => _showWindowsNotification;
        set => SetField(ref _showWindowsNotification, value);
    }

    public int SnoozeMinutes
    {
        get => _snoozeMinutes;
        set => SetField(ref _snoozeMinutes, value);
    }

    public int GracePeriodMinutes
    {
        get => _gracePeriodMinutes;
        set => SetField(ref _gracePeriodMinutes, value);
    }

    public bool Monday { get => HasDay(ReminderDays.Monday); set => SetDay(ReminderDays.Monday, value); }
    public bool Tuesday { get => HasDay(ReminderDays.Tuesday); set => SetDay(ReminderDays.Tuesday, value); }
    public bool Wednesday { get => HasDay(ReminderDays.Wednesday); set => SetDay(ReminderDays.Wednesday, value); }
    public bool Thursday { get => HasDay(ReminderDays.Thursday); set => SetDay(ReminderDays.Thursday, value); }
    public bool Friday { get => HasDay(ReminderDays.Friday); set => SetDay(ReminderDays.Friday, value); }
    public bool Saturday { get => HasDay(ReminderDays.Saturday); set => SetDay(ReminderDays.Saturday, value); }
    public bool Sunday { get => HasDay(ReminderDays.Sunday); set => SetDay(ReminderDays.Sunday, value); }

    public string Activity { get => _activity; private set => SetField(ref _activity, value); }

    public ICommand NewCommand => _newCommand;

    public ICommand SaveCommand => _saveCommand;

    public ICommand DeleteCommand => _deleteCommand;

    public ICommand PreviewCommand => _previewCommand;

    public async Task InitializeAsync(UsageReminderSettings settings)
    {
        await _module.InitializeAsync(settings);
        RefreshFromModule();
        SelectedProfile = Profiles.FirstOrDefault();
    }

    public async Task ShutdownAsync()
    {
        _module.StatusChanged -= HandleStatusChanged;
        await _module.DisposeAsync();
    }

    private Task NewAsync()
    {
        SelectedProfile = null;
        LoadProfile(new UsageReminderProfile());
        Activity = "Creating a new reminder.";
        return Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        var profile = BuildProfile();
        EnsureValid(profile);
        var profiles = _module.Settings.Profiles.ToList();
        var existingIndex = profiles.FindIndex(item => item.Id == profile.Id);
        if (existingIndex >= 0)
        {
            profiles[existingIndex] = profile;
        }
        else
        {
            profiles.Add(profile);
        }

        await _module.UpdateProfilesAsync(profiles);
        Activity = existingIndex >= 0 ? "Reminder updated." : "Reminder created.";
        RefreshFromModule(profile.Id);
    }

    private async Task DeleteAsync()
    {
        if (SelectedProfile is null
            || !_confirmationService.Confirm(
                "Delete reminder",
                $"Delete the ‘{SelectedProfile.Name}’ reminder?"))
        {
            return;
        }

        var deletedId = SelectedProfile.Profile.Id;
        await _module.UpdateProfilesAsync(
            _module.Settings.Profiles.Where(item => item.Id != deletedId).ToArray());
        RefreshFromModule();
        SelectedProfile = Profiles.FirstOrDefault();
        if (SelectedProfile is null)
        {
            await NewAsync();
        }

        Activity = "Reminder deleted.";
    }

    private Task PreviewAsync()
    {
        var profile = BuildProfile();
        EnsureValid(profile);
        _module.Preview(profile);
        Activity = "Preview displayed. Previewing does not affect delivery history.";
        return Task.CompletedTask;
    }

    private UsageReminderProfile BuildProfile() => new()
    {
        Id = _draftId,
        CreatedUtc = _draftCreatedUtc,
        Name = Name.Trim(),
        Enabled = IsEnabled,
        Hour = Hour,
        Minute = Minute,
        ActiveDays = _activeDays,
        Title = Title.Trim(),
        Message = ReminderMessage.Trim(),
        ShowCornerCard = ShowCornerCard,
        ShowWindowsNotification = ShowWindowsNotification,
        SnoozeMinutes = SnoozeMinutes,
        GracePeriodMinutes = GracePeriodMinutes
    };

    private void LoadProfile(UsageReminderProfile profile)
    {
        _draftId = profile.Id;
        _draftCreatedUtc = profile.CreatedUtc;
        Name = profile.Name;
        IsEnabled = profile.Enabled;
        Hour = profile.Hour;
        Minute = profile.Minute;
        Title = profile.Title;
        ReminderMessage = profile.Message;
        ShowCornerCard = profile.ShowCornerCard;
        ShowWindowsNotification = profile.ShowWindowsNotification;
        SnoozeMinutes = profile.SnoozeMinutes;
        GracePeriodMinutes = profile.GracePeriodMinutes;
        _activeDays = profile.ActiveDays;
        NotifyDayProperties();
    }

    private void RefreshFromModule(Guid? selectedId = null)
    {
        selectedId ??= SelectedProfile?.Profile.Id;
        Profiles.Clear();
        foreach (var profile in _module.Settings.Profiles)
        {
            var next = _module.GetNextOccurrence(profile);
            Profiles.Add(new UsageReminderProfileItem(
                profile,
                next?.ToLocalTime().ToString("g") ?? (profile.Enabled ? "No occurrence" : "Disabled")));
        }

        RecentHistory.Clear();
        foreach (var delivery in _module.Settings.RecentDeliveries.OrderByDescending(item => item.RecordedUtc))
        {
            RecentHistory.Add(new ReminderHistoryItem(delivery));
        }

        if (selectedId is not null)
        {
            SelectedProfile = Profiles.FirstOrDefault(item => item.Profile.Id == selectedId);
        }

        if (!string.IsNullOrWhiteSpace(_module.LastError))
        {
            Activity = _module.LastError;
        }
    }

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            Activity = exception.Message;
        }
    }

    private void HandleStatusChanged(object? sender, EventArgs eventArgs)
    {
        if (_dispatcher.CheckAccess())
        {
            RefreshFromModule();
        }
        else
        {
            _dispatcher.BeginInvoke(RefreshFromModule);
        }
    }

    private bool HasDay(ReminderDays day) => (_activeDays & day) != 0;

    private void SetDay(ReminderDays day, bool selected)
    {
        _activeDays = selected ? _activeDays | day : _activeDays & ~day;
        NotifyDayProperties();
    }

    private void NotifyDayProperties()
    {
        OnPropertyChanged(nameof(Monday));
        OnPropertyChanged(nameof(Tuesday));
        OnPropertyChanged(nameof(Wednesday));
        OnPropertyChanged(nameof(Thursday));
        OnPropertyChanged(nameof(Friday));
        OnPropertyChanged(nameof(Saturday));
        OnPropertyChanged(nameof(Sunday));
    }

    private static void EnsureValid(UsageReminderProfile profile)
    {
        var errors = profile.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }
    }

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
