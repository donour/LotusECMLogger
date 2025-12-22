namespace BinaryFileMonitor;

/// <summary>
/// Provides data for the <see cref="BinaryFileMonitor.MonitorError"/> event.
/// </summary>
public class FileMonitorErrorEventArgs : EventArgs
{
    /// <summary>
    /// Gets the exception that occurred during monitoring.
    /// </summary>
    public Exception Exception { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileMonitorErrorEventArgs"/> class.
    /// </summary>
    /// <param name="exception">The exception that occurred.</param>
    public FileMonitorErrorEventArgs(Exception exception)
    {
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
    }
}
