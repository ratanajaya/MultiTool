using System.ComponentModel;
using System.Windows;
using MultiTool.Presentation.Shell;
using MultiTool.Presentation.Tray;
using MultiTool.Presentation.ViewModels;
using MultiTool.Core.Scheduling;
using MultiTool.Core.Settings;
using MultiTool.Modules.AudioOutputKeepAlive;
using MultiTool.Modules.BatchFileOrganizer;
using MultiTool.Modules.MicrophoneKeepAwake;
using MultiTool.Modules.MongoDbBackup;
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
            var mongoDbBackupModule = new MongoDbBackupService(
                new WindowsMongoDumpRunner(),
                new JsonMongoDbBackupSettingsStore(settingsStore),
                _trayIcon);
            var mongoDbBackupViewModel = new MongoDbBackupViewModel(
                mongoDbBackupModule,
                new WindowsMongoDbBackupFileDialogService());
            var batchFileSystem = new WindowsBatchFileSystem();
            var batchFileOrganizerViewModel = new BatchFileOrganizerViewModel(
                batchFileSystem,
                new BatchFileRenameService(batchFileSystem),
                new WindowsBatchFileOrganizerFolderPicker(),
                new SkiaImagePreviewLoader());
            _viewModel = new MainViewModel(
                module,
                settingsStore,
                microphoneViewModel,
                powerTimerViewModel,
                usageReminderViewModel,
                mongoDbBackupViewModel,
                batchFileOrganizerViewModel);
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

        var hasPowerTimer = _viewModel?.PowerTimer.HasActiveTimer == true;
        var hasMongoDbBackup = _viewModel?.MongoDbBackup.IsActive == true;
        if (hasPowerTimer || hasMongoDbBackup)
        {
            var activeWork = hasPowerTimer && hasMongoDbBackup
                ? "the active power timer and MongoDB backup"
                : hasPowerTimer
                    ? "the active power timer"
                    : "the active MongoDB backup";
            var result = System.Windows.MessageBox.Show(
                $"Exiting MultiTool will cancel {activeWork}. Exit?",
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
                "MongoDbBackup" => ModulePage.MongoDbBackup,
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
