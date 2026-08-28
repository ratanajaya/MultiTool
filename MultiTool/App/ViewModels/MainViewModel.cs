using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using MultiTool.Presentation.Commands;
using MultiTool.Core.Modules;
using MultiTool.Core.Settings;
using MultiTool.Modules.AudioOutputKeepAlive;

namespace MultiTool.Presentation.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly AudioOutputKeepAliveService _module;
    private readonly JsonSettingsStore _settingsStore;
    private readonly Dispatcher _dispatcher;
    private readonly AsyncRelayCommand _applyCommand;
    private readonly AsyncRelayCommand _startStopCommand;
    private readonly AsyncRelayCommand _playTestCommand;
    private readonly AsyncRelayCommand _refreshDevicesCommand;
    private ModuleNavigationItem _selectedNavigationItem;
    private AudioOutputDeviceOption? _selectedDevice;
    private bool _isEnabled;
    private int _intervalSeconds = 60;
    private int _burstDurationMilliseconds = 250;
    private float _amplitude = 0.00008f;
    private string _status = "Stopped";
    private string _lastBurst = "Never";
    private string _message = "Ready";

    public MainViewModel(
        AudioOutputKeepAliveService module,
        JsonSettingsStore settingsStore,
        MicrophoneKeepAwakeViewModel microphone)
    {
        _module = module;
        _settingsStore = settingsStore;
        _dispatcher = Dispatcher.CurrentDispatcher;
        Microphone = microphone;
        _module.StatusChanged += HandleStatusChanged;

        NavigationItems = ModuleNavigationItem.CreateDefaultNavigation();
        _selectedNavigationItem = NavigationItems[0];

        _applyCommand = new AsyncRelayCommand(() => RunSafelyAsync(ApplyAsync));
        _startStopCommand = new AsyncRelayCommand(() => RunSafelyAsync(StartStopAsync));
        _playTestCommand = new AsyncRelayCommand(() => RunSafelyAsync(PlayTestAsync));
        _refreshDevicesCommand = new AsyncRelayCommand(() => RunSafelyAsync(RefreshDevicesAsync));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AudioOutputDeviceOption> OutputDevices { get; } = [];

    public MicrophoneKeepAwakeViewModel Microphone { get; }

    public IReadOnlyList<ModuleNavigationItem> NavigationItems { get; }

    public ModuleNavigationItem SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set
        {
            if (SetField(ref _selectedNavigationItem, value))
            {
                OnPropertyChanged(nameof(SelectedPage));
            }
        }
    }

    public ModulePage SelectedPage => SelectedNavigationItem.Page;

    public AudioOutputDeviceOption? SelectedDevice
    {
        get => _selectedDevice;
        set => SetField(ref _selectedDevice, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }

    public int IntervalSeconds
    {
        get => _intervalSeconds;
        set => SetField(ref _intervalSeconds, value);
    }

    public int BurstDurationMilliseconds
    {
        get => _burstDurationMilliseconds;
        set => SetField(ref _burstDurationMilliseconds, value);
    }

    public float Amplitude
    {
        get => _amplitude;
        set => SetField(ref _amplitude, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string LastBurst
    {
        get => _lastBurst;
        private set => SetField(ref _lastBurst, value);
    }

    public string Message
    {
        get => _message;
        private set => SetField(ref _message, value);
    }

    public string StartStopLabel =>
        _module.State is ModuleState.Stopped ? "Start" : "Stop";

    public ICommand ApplyCommand => _applyCommand;

    public ICommand StartStopCommand => _startStopCommand;

    public ICommand PlayTestCommand => _playTestCommand;

    public ICommand RefreshDevicesCommand => _refreshDevicesCommand;

    public async Task InitializeAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        var moduleSettings = settings.AudioOutputKeepAlive;

        IsEnabled = moduleSettings.Enabled;
        IntervalSeconds = moduleSettings.IntervalSeconds;
        BurstDurationMilliseconds = moduleSettings.BurstDurationMilliseconds;
        Amplitude = moduleSettings.Amplitude;
        RefreshDevices(moduleSettings.DeviceId);

        var errors = moduleSettings.Validate();
        if (errors.Count > 0)
        {
            Message = $"Saved settings are invalid: {string.Join(" ", errors)}";
            IsEnabled = false;
        }
        else if (moduleSettings.Enabled)
        {
            await _module.StartAsync(moduleSettings);
        }

        RefreshRuntimeStatus();
        await Microphone.InitializeAsync(settings.MicrophoneKeepAwake);
    }

    public async Task ShutdownAsync()
    {
        _module.StatusChanged -= HandleStatusChanged;
        try
        {
            await _module.DisposeAsync();
        }
        finally
        {
            await Microphone.ShutdownAsync();
        }
    }

    private async Task ApplyAsync()
    {
        var settings = BuildSettings();
        EnsureValid(settings);
        await SaveAsync(settings);

        if (_module.State is not ModuleState.Stopped)
        {
            await _module.StopAsync();
        }

        if (settings.Enabled)
        {
            await _module.StartAsync(settings);
        }

        Message = settings.Enabled
            ? "Settings saved. Audio output keep-alive is running."
            : "Settings saved. Audio output keep-alive is stopped.";
    }

    private async Task StartStopAsync()
    {
        if (_module.State is not ModuleState.Stopped)
        {
            await _module.StopAsync();
            IsEnabled = false;
            await SaveAsync(BuildSettings());
            Message = "Audio output keep-alive stopped.";
            return;
        }

        IsEnabled = true;
        var settings = BuildSettings();
        EnsureValid(settings);
        await SaveAsync(settings);
        await _module.StartAsync(settings);
        Message = "Audio output keep-alive started.";
    }

    private async Task PlayTestAsync()
    {
        var settings = BuildSettings();
        EnsureValid(settings);
        Message = "Playing a test burst...";
        await _module.PlayNowAsync(settings);
        Message = "Test burst completed.";
    }

    private Task RefreshDevicesAsync()
    {
        RefreshDevices(SelectedDevice?.Id);
        Message = $"Found {Math.Max(0, OutputDevices.Count - 1)} active output device(s).";
        return Task.CompletedTask;
    }

    private void RefreshDevices(string? selectedDeviceId)
    {
        OutputDevices.Clear();
        OutputDevices.Add(new AudioOutputDeviceOption(null, "Windows default output (recommended)"));

        try
        {
            foreach (var device in _module.GetActiveOutputDevices())
            {
                OutputDevices.Add(new AudioOutputDeviceOption(device.Id, device.Name));
            }
        }
        catch (Exception exception)
        {
            Message = $"Output devices could not be listed: {exception.Message}";
        }

        SelectedDevice = OutputDevices.FirstOrDefault(
            option => string.Equals(option.Id, selectedDeviceId, StringComparison.Ordinal));

        if (SelectedDevice is null && !string.IsNullOrWhiteSpace(selectedDeviceId))
        {
            SelectedDevice = new AudioOutputDeviceOption(
                selectedDeviceId,
                $"Unavailable device ({selectedDeviceId})");
            OutputDevices.Add(SelectedDevice);
        }

        SelectedDevice ??= OutputDevices[0];
    }

    private AudioOutputKeepAliveSettings BuildSettings() => new()
    {
        Enabled = IsEnabled,
        DeviceId = SelectedDevice?.Id,
        IntervalSeconds = IntervalSeconds,
        BurstDurationMilliseconds = BurstDurationMilliseconds,
        Amplitude = Amplitude
    };

    private static void EnsureValid(AudioOutputKeepAliveSettings settings)
    {
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }
    }

    private Task SaveAsync(AudioOutputKeepAliveSettings moduleSettings) =>
        _settingsStore.UpdateAsync(settings => settings with
        {
            AudioOutputKeepAlive = moduleSettings
        });

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
        Status = _module.State.ToString();
        LastBurst = _module.LastBurstAt?.ToLocalTime().ToString("G") ?? "Never";
        if (!string.IsNullOrWhiteSpace(_module.LastError))
        {
            Message = _module.LastError;
        }

        OnPropertyChanged(nameof(StartStopLabel));
        _applyCommand.RaiseCanExecuteChanged();
        _startStopCommand.RaiseCanExecuteChanged();
        _playTestCommand.RaiseCanExecuteChanged();
        _refreshDevicesCommand.RaiseCanExecuteChanged();
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
