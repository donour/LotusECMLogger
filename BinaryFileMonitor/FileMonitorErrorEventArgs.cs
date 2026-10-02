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
    /// Gets how many scans in a row have now failed, including this one. It returns to 1 on the
    /// first failure after a successful read, so a subscriber can tell a brief hiccup (an editor
    /// replacing the file while saving) from a file that has gone away.
    /// </summary>
    public int ConsecutiveFailures { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileMonitorErrorEventArgs"/> class.
    /// </summary>
    /// <param name="exception">The exception that occurred.</param>
    /// <param name="consecutiveFailures">Scans failed in a row, including this one.</param>
    public FileMonitorErrorEventArgs(Exception exception, int consecutiveFailures = 1)
    {
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        ConsecutiveFailures = consecutiveFailures;
    }
}
