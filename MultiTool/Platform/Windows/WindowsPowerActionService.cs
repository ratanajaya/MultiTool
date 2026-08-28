using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MultiTool.Modules.PowerTimer;

namespace MultiTool.Platform.Windows;

public sealed class WindowsPowerActionService : IPowerActionService
{
    public bool IsAvailable(PowerAction action) => action switch
    {
        PowerAction.Shutdown => true,
        PowerAction.Sleep => IsPwrSuspendAllowed(),
        PowerAction.Hibernate => IsPwrHibernateAllowed(),
        _ => false
    };

    public async Task ExecuteAsync(PowerAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsAvailable(action))
        {
            throw new InvalidOperationException($"{action} is not available on this computer.");
        }

        switch (action)
        {
            case PowerAction.Shutdown:
                StartShutdown();
                break;
            case PowerAction.Sleep:
                await SuspendAsync(hibernate: false, cancellationToken);
                break;
            case PowerAction.Hibernate:
                await SuspendAsync(hibernate: true, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    private static void StartShutdown()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = "/s /t 0",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (process is null)
        {
            throw new InvalidOperationException("Windows did not accept the shutdown request.");
        }
    }

    private static Task SuspendAsync(bool hibernate, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SetSuspendState(hibernate, forceCritical: false, disableWakeEvent: false))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        hibernate
                            ? "Windows could not hibernate the computer."
                            : "Windows could not put the computer to sleep.");
                }
            },
            cancellationToken);

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.U1)] bool hibernate,
        [MarshalAs(UnmanagedType.U1)] bool forceCritical,
        [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);

    [DllImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool IsPwrSuspendAllowed();

    [DllImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool IsPwrHibernateAllowed();
}
