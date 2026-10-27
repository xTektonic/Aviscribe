using System;

namespace Aviscribe.Core.Capture;

public sealed class CaptureStateChangedEventArgs(
    CaptureState previous,
    CaptureState current) : EventArgs
{
    public CaptureState Previous { get; } = previous;
    public CaptureState Current { get; } = current;
}

public sealed class CaptureErrorEventArgs(
    string message,
    Exception? exception = null,
    bool deviceDisconnected = false) : EventArgs
{
    public string Message { get; } = message;
    public Exception? Exception { get; } = exception;
    public bool DeviceDisconnected { get; } = deviceDisconnected;
}
