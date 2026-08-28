using System.ComponentModel;
using System.Windows;
using MultiTool.Presentation.Shell;
using MultiTool.Presentation.Tray;
using MultiTool.Presentation.ViewModels;
using MultiTool.Core.Scheduling;
using MultiTool.Core.Settings;
using MultiTool.Modules.AudioOutputKeepAlive;
using MultiTool.Modules.MicrophoneKeepAwake;
using MultiTool.Modules.PowerTimer;
using MultiTool.Modules.UsageReminders;
using MultiTool.Platform.Windows;
using MultiTool.Presentation.Dialogs;

namespace MultiTool;

public partial class App : System.Windows.Application
{
    private MainViewModel? _viewModel;
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;
    private bool _isExiting;

    protected override async void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

        try
        {
            var settingsStore = new JsonSettingsStore();
            _trayIcon = new TrayIcon(ShowMainWindow, ExitAsync);
            _trayIcon.Activated += HandleNotificationActivated;
            var module = new AudioOutputKeepAliveService(
                new NAudioOutputService(),
                new SystemAsyncDelay());
            var microphoneModule = new MicrophoneKeepAwakeService(
                new NAudioCaptureService(),
                new SystemAsyncDelay());
            var microphoneViewModel = new MicrophoneKeepAwakeViewModel(
                microphoneModule,
                settingsStore,
                new WindowsMicrophonePrivacySettingsLauncher());
            var clock = new SystemClock();
            var powerTimerModule = new PowerTimerService(
                new WindowsPowerActionService(),
                _trayIcon,
                new JsonPowerTimerScheduleStore(settingsStore),
                clock,
                new SystemAsyncDelay(),
                new WindowsSystemTimeEvents());
            var powerTimerViewModel = new PowerTimerViewModel(
                powerTimerModule,
                new WindowsConfirmationService(),
                clock);
            var reminderPresenter = new WindowsReminderPresentationService(
                new WindowsFullScreenDetector());
            var usageReminderModule = new UsageReminderService(
                new JsonUsageReminderSettingsStore(settingsStore),
                reminderPresenter,
                _trayIcon,
                clock,
                new SystemAsyncDelay(),
                new WindowsSystemTimeEvents());
            var usageReminderViewModel = new UsageReminderViewModel(
                usageReminderModule,
                new WindowsConfirmationService());
            _viewModel = new MainViewModel(
                module,
                settingsStore,
                microphoneViewModel,
                powerTimerViewModel,
                usageReminderViewModel);
            _mainWindow = new MainWindow(_viewModel);
            _mainWindow.Closing += HandleWindowClosing;

            await _viewModel.InitializeAsync();
            _mainWindow.Show();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"MultiTool could not start.\n\n{exception.Message}",
                "MultiTool",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            _trayIcon?.Dispose();
            Shutdown(1);
        }
    }

    private void HandleWindowClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_isExiting)
        {
            return;
        }

        eventArgs.Cancel = true;
        _mainWindow?.Hide();
    }

    private async Task ExitAsync()
    {
        if (_isExiting)
        {
            return;
        }

        if (_viewModel?.PowerTimer.HasActiveTimer == true)
        {
            var result = System.Windows.MessageBox.Show(
                "Exiting MultiTool will cancel the active power timer. Exit?",
                "Exit MultiTool",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        _isExiting = true;
        if (_viewModel is not null)
        {
            await _viewModel.ShutdownAsync();
        }

        _trayIcon?.Dispose();
        _mainWindow?.Close();
        Shutdown();
    }

    private void HandleNotificationActivated(
        object? sender,
        Core.Notifications.NotificationActivatedEventArgs eventArgs)
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Dispatcher.BeginInvoke(() =>
        {
            _viewModel?.NavigateTo(eventArgs.Context switch
            {
                "UsageReminders" => ModulePage.UsageReminders,
                _ => ModulePage.PowerTimer
            });
            ShowMainWindow();
        });
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Dispatcher.Invoke(() =>
        {
            _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }

            _mainWindow.Activate();
        });
    }
}
