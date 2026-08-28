using System.ComponentModel;
using System.Windows;
using MultiTool.Presentation.Shell;
using MultiTool.Presentation.Tray;
using MultiTool.Presentation.ViewModels;
using MultiTool.Core.Scheduling;
using MultiTool.Core.Settings;
using MultiTool.Modules.AudioOutputKeepAlive;
using MultiTool.Modules.MicrophoneKeepAwake;
using MultiTool.Platform.Windows;

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
            _viewModel = new MainViewModel(module, settingsStore, microphoneViewModel);
            _mainWindow = new MainWindow(_viewModel);
            _mainWindow.Closing += HandleWindowClosing;
            _trayIcon = new TrayIcon(_mainWindow, ExitAsync);

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

        _isExiting = true;
        if (_viewModel is not null)
        {
            await _viewModel.ShutdownAsync();
        }

        _trayIcon?.Dispose();
        _mainWindow?.Close();
        Shutdown();
    }
}
