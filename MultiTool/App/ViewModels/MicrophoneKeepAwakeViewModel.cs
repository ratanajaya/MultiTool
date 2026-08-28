using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using MultiTool.Core.Modules;
using MultiTool.Core.Settings;
using MultiTool.Modules.MicrophoneKeepAwake;
using MultiTool.Presentation.Commands;

namespace MultiTool.Presentation.ViewModels;

public sealed class MicrophoneKeepAwakeViewModel : INotifyPropertyChanged
{
    private readonly MicrophoneKeepAwakeService _module;
    private readonly JsonSettingsStore _settingsStore;
    private readonly IMicrophonePrivacySettingsLauncher _privacySettingsLauncher;
    private readonly Dispatcher _dispatcher;
    private readonly AsyncRelayCommand _applyCommand;
    private readonly AsyncRelayCommand _startStopCommand;
    private readonly AsyncRelayCommand _refreshDevicesCommand;
    private readonly AsyncRelayCommand _openPrivacySettingsCommand;
    private AudioCaptureDeviceOption? _selectedDevice;
    private bool _isEnabled;
    private bool _restartOnDeviceChange = true;
    private string _status = "Stopped";
    private string _startedAt = "Not running";
    private string _message = "Ready";

    public MicrophoneKeepAwakeViewModel(
        MicrophoneKeepAwakeService module,
        JsonSettingsStore settingsStore,
        IMicrophonePrivacySettingsLauncher privacySettingsLauncher)
    {
        _module = module;
        _settingsStore = settingsStore;
        _privacySettingsLauncher = privacySettingsLauncher;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _module.StatusChanged += HandleStatusChanged;

        _applyCommand = new AsyncRelayCommand(() => RunSafelyAsync(ApplyAsync));
        _startStopCommand = new AsyncRelayCommand(() => RunSafelyAsync(StartStopAsync));
        _refreshDevicesCommand = new AsyncRelayCommand(() => RunSafelyAsync(RefreshDevicesAsync));
        _openPrivacySettingsCommand = new AsyncRelayCommand(
            () => RunSafelyAsync(OpenPrivacySettingsAsync));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AudioCaptureDeviceOption> InputDevices { get; } = [];

    public AudioCaptureDeviceOption? SelectedDevice
    {
        get => _selectedDevice;
        set => SetField(ref _selectedDevice, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }

    public bool RestartOnDeviceChange
    {
        get => _restartOnDeviceChange;
        set => SetField(ref _restartOnDeviceChange, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string StartedAt
    {
        get => _startedAt;
        private set => SetField(ref _startedAt, value);
    }

    public string Message
    {
        get => _message;
        private set => SetField(ref _message, value);
    }

    public string StartStopLabel =>
        _module.State is ModuleState.Stopped or ModuleState.Faulted ? "Start" : "Stop";

    public ICommand ApplyCommand => _applyCommand;

    public ICommand StartStopCommand => _startStopCommand;

    public ICommand RefreshDevicesCommand => _refreshDevicesCommand;

    public ICommand OpenPrivacySettingsCommand => _openPrivacySettingsCommand;

    public async Task InitializeAsync(MicrophoneKeepAwakeSettings settings)
    {
        IsEnabled = settings.Enabled;
        RestartOnDeviceChange = settings.RestartOnDeviceChange;
        RefreshDevices(settings.DeviceId);

        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            Message = $"Saved settings are invalid: {string.Join(" ", errors)}";
            IsEnabled = false;
            return;
        }

        if (settings.Enabled)
        {
            await _module.StartAsync(settings);
        }

        RefreshRuntimeStatus();
    }

    public async Task ShutdownAsync()
    {
        _module.StatusChanged -= HandleStatusChanged;
        await _module.DisposeAsync();
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
            ? "Settings saved. Microphone keep-awake is running."
            : "Settings saved. Microphone keep-awake is stopped.";
    }

    private async Task StartStopAsync()
    {
        if (_module.State is not (ModuleState.Stopped or ModuleState.Faulted))
        {
            await _module.StopAsync();
            IsEnabled = false;
            await SaveAsync(BuildSettings());
            Message = "Microphone keep-awake stopped.";
            return;
        }

        if (_module.State is ModuleState.Faulted)
        {
            await _module.StopAsync();
        }

        IsEnabled = true;
        var settings = BuildSettings();
        EnsureValid(settings);
        await SaveAsync(settings);
        await _module.StartAsync(settings);
        Message = "Microphone keep-awake started.";
    }

    private Task RefreshDevicesAsync()
    {
        RefreshDevices(SelectedDevice?.Id);
        Message = $"Found {Math.Max(0, InputDevices.Count - 1)} active microphone(s).";
        return Task.CompletedTask;
    }

    private Task OpenPrivacySettingsAsync()
    {
        _privacySettingsLauncher.Open();
        Message = "Windows microphone privacy settings opened.";
        return Task.CompletedTask;
    }

    private void RefreshDevices(string? selectedDeviceId)
    {
        InputDevices.Clear();
        InputDevices.Add(new AudioCaptureDeviceOption(
            null,
            "Windows default microphone (recommended)"));

        try
        {
            foreach (var device in _module.GetActiveCaptureDevices())
            {
                InputDevices.Add(new AudioCaptureDeviceOption(device.Id, device.Name));
            }
        }
        catch (Exception exception)
        {
            Message = $"Microphones could not be listed: {exception.Message}";
        }

        SelectedDevice = InputDevices.FirstOrDefault(
            option => string.Equals(option.Id, selectedDeviceId, StringComparison.Ordinal));

        if (SelectedDevice is null && !string.IsNullOrWhiteSpace(selectedDeviceId))
        {
            SelectedDevice = new AudioCaptureDeviceOption(
                selectedDeviceId,
                $"Unavailable microphone ({selectedDeviceId})");
            InputDevices.Add(SelectedDevice);
        }

        SelectedDevice ??= InputDevices[0];
    }

    private MicrophoneKeepAwakeSettings BuildSettings() => new()
    {
        Enabled = IsEnabled,
        DeviceId = SelectedDevice?.Id,
        RestartOnDeviceChange = RestartOnDeviceChange
    };

    private static void EnsureValid(MicrophoneKeepAwakeSettings settings)
    {
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }
    }

    private Task SaveAsync(MicrophoneKeepAwakeSettings moduleSettings) =>
        _settingsStore.UpdateAsync(settings => settings with
        {
            MicrophoneKeepAwake = moduleSettings
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
        StartedAt = _module.StartedAt?.ToLocalTime().ToString("G") ?? "Not running";
        if (!string.IsNullOrWhiteSpace(_module.LastError))
        {
            Message = _module.LastError;
        }

        OnPropertyChanged(nameof(StartStopLabel));
        _applyCommand.RaiseCanExecuteChanged();
        _startStopCommand.RaiseCanExecuteChanged();
        _refreshDevicesCommand.RaiseCanExecuteChanged();
        _openPrivacySettingsCommand.RaiseCanExecuteChanged();
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
