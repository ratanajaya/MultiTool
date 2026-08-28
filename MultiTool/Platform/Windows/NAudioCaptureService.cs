using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using MultiTool.Modules.MicrophoneKeepAwake;

namespace MultiTool.Platform.Windows;

public sealed class NAudioCaptureService : IAudioCaptureService
{
    private const int AccessDeniedHResult = unchecked((int)0x80070005);
    private const int DeviceNotFoundHResult = unchecked((int)0x80070490);
    private const int AudioDeviceInvalidatedHResult = unchecked((int)0x88890004);

    public IReadOnlyList<AudioCaptureDevice> GetActiveCaptureDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(device => new AudioCaptureDevice(device.ID, device.FriendlyName))
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task CaptureAndDiscardAsync(
        string? deviceId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = string.IsNullOrWhiteSpace(deviceId)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
                : enumerator.GetDevice(deviceId);
            using var capture = new WasapiCapture(device);
            var recordingStopped = new TaskCompletionSource<Exception?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var endpointChanged = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var notificationClient = new CaptureEndpointNotificationClient(
                deviceId,
                endpointChanged);

            capture.DataAvailable += DiscardCapturedBuffer;
            capture.RecordingStopped += HandleRecordingStopped;
            enumerator.RegisterEndpointNotificationCallback(notificationClient);
            try
            {
                capture.StartRecording();
                using var cancellationRegistration = cancellationToken.Register(
                    static state => StopCapture((WasapiCapture)state!),
                    capture);
                var completion = await Task.WhenAny(recordingStopped.Task, endpointChanged.Task)
                    .WaitAsync(cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                if (completion == endpointChanged.Task)
                {
                    StopCapture(capture);
                    throw new MicrophoneDeviceUnavailableException(
                        string.IsNullOrWhiteSpace(deviceId)
                            ? "The default microphone changed."
                            : "The selected microphone was disconnected or became unavailable.");
                }

                var recordingError = await recordingStopped.Task;
                if (recordingError is not null)
                {
                    throw MapException(recordingError);
                }

                throw new MicrophoneCaptureInterruptedException(
                    "Microphone capture stopped unexpectedly.");
            }
            finally
            {
                enumerator.UnregisterEndpointNotificationCallback(notificationClient);
                capture.DataAvailable -= DiscardCapturedBuffer;
                capture.RecordingStopped -= HandleRecordingStopped;
            }

            void HandleRecordingStopped(object? sender, StoppedEventArgs eventArgs) =>
                recordingStopped.TrySetResult(eventArgs.Exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw MapException(exception);
        }
    }

    private static void DiscardCapturedBuffer(object? sender, WaveInEventArgs eventArgs)
    {
        // Privacy boundary: the supplied buffer is deliberately never read, retained,
        // logged, analyzed, or passed outside this callback.
    }

    private static void StopCapture(WasapiCapture capture)
    {
        try
        {
            capture.StopRecording();
        }
        catch
        {
            // The capture may already have stopped because its endpoint disappeared.
        }
    }

    private static Exception MapException(Exception exception)
    {
        if (exception is MicrophoneAccessDeniedException
            or MicrophoneDeviceUnavailableException
            or MicrophoneCaptureInterruptedException)
        {
            return exception;
        }

        if (exception is UnauthorizedAccessException
            || FindHResult(exception, AccessDeniedHResult))
        {
            return new MicrophoneAccessDeniedException(
                "Microphone access was denied. Review Windows microphone privacy settings.",
                exception);
        }

        if (FindHResult(exception, AudioDeviceInvalidatedHResult)
            || FindHResult(exception, DeviceNotFoundHResult))
        {
            return new MicrophoneDeviceUnavailableException(
                "The selected microphone is unavailable or was disconnected.",
                exception);
        }

        return new MicrophoneCaptureInterruptedException(
            $"Microphone capture failed: {exception.Message}",
            exception);
    }

    private static bool FindHResult(Exception exception, int hResult)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current.HResult == hResult
                || current is COMException comException && comException.ErrorCode == hResult)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class CaptureEndpointNotificationClient(
        string? selectedDeviceId,
        TaskCompletionSource endpointChanged) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            if (!string.IsNullOrWhiteSpace(selectedDeviceId)
                && string.Equals(deviceId, selectedDeviceId, StringComparison.Ordinal)
                && newState != DeviceState.Active)
            {
                endpointChanged.TrySetResult();
            }
        }

        public void OnDeviceAdded(string deviceId)
        {
        }

        public void OnDeviceRemoved(string deviceId)
        {
            if (!string.IsNullOrWhiteSpace(selectedDeviceId)
                && string.Equals(deviceId, selectedDeviceId, StringComparison.Ordinal))
            {
                endpointChanged.TrySetResult();
            }
        }

        public void OnDefaultDeviceChanged(
            DataFlow flow,
            Role role,
            string defaultDeviceId)
        {
            if (string.IsNullOrWhiteSpace(selectedDeviceId)
                && flow == DataFlow.Capture
                && role == Role.Multimedia)
            {
                endpointChanged.TrySetResult();
            }
        }

        public void OnPropertyValueChanged(string deviceId, PropertyKey propertyKey)
        {
        }
    }
}
