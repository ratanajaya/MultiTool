namespace MultiTool.Modules.MicrophoneKeepAwake;

public sealed class MicrophoneAccessDeniedException : Exception
{
    public MicrophoneAccessDeniedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class MicrophoneDeviceUnavailableException : Exception
{
    public MicrophoneDeviceUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class MicrophoneCaptureInterruptedException : Exception
{
    public MicrophoneCaptureInterruptedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
