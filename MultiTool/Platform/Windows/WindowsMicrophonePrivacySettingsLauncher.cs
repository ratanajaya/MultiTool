using System.Diagnostics;
using MultiTool.Modules.MicrophoneKeepAwake;

namespace MultiTool.Platform.Windows;

public sealed class WindowsMicrophonePrivacySettingsLauncher : IMicrophonePrivacySettingsLauncher
{
    public void Open()
    {
        Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone")
        {
            UseShellExecute = true
        });
    }
}
