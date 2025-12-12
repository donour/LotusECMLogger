namespace BinaryFileMonitor;

/// <summary>
/// Provides data for the <see cref="BinaryFileMonitor.FileReloaded"/> event.
/// </summary>
public class FileReloadedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the number of bytes that changed during this reload.
    /// </summary>
    public int ChangeCount { get; }

    /// <summary>
    /// Gets a value indicating whether the file size changed.
    /// </summary>
    public bool SizeChanged { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileReloadedEventArgs"/> class.
    /// </summary>
    /// <param name="changeCount">The number of bytes that changed.</param>
    /// <param name="sizeChanged">Whether the file size changed.</param>
    public FileReloadedEventArgs(int changeCount, bool sizeChanged)
    {
        ChangeCount = changeCount;
        SizeChanged = sizeChanged;
    }
}
