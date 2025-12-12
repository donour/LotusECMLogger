namespace BinaryFileMonitor;

/// <summary>
/// Monitors a binary file for changes by periodically reloading it from disk
/// and detecting 32-bit word-level modifications.
/// </summary>
/// <remarks>
/// This class loads a binary file into memory and spawns a background thread
/// that continuously monitors the file for changes. When 32-bit words change, it emits
/// events with the offset and new value information.
///
/// Monitoring operates on 4-byte aligned words. File size should ideally be a multiple of 4.
/// Thread-safe for concurrent access to the buffer.
/// </remarks>
public class BinaryFileMonitor : IDisposable
{
    private byte[] _currentBuffer;
    private readonly string _filePath;
    private readonly int _scanIntervalMs;
    private Thread? _monitorThread;
    private volatile bool _isRunning;
    private readonly object _bufferLock = new();
    private bool _disposed;

    /// <summary>
    /// Raised when a 32-bit word in the monitored file changes.
    /// </summary>
    public event EventHandler<WordChangedEventArgs>? WordChanged;

    /// <summary>
    /// Raised after the file has been reloaded and all changes have been detected.
    /// </summary>
    public event EventHandler<FileReloadedEventArgs>? FileReloaded;

    /// <summary>
    /// Raised when an error occurs during file monitoring (e.g., file deleted, access denied).
    /// </summary>
    public event EventHandler<FileMonitorErrorEventArgs>? MonitorError;

    /// <summary>
    /// Gets whether the monitor is currently running.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Gets the path of the file being monitored.
    /// </summary>
    public string FilePath => _filePath;

    /// <summary>
    /// Gets the current size of the buffer in bytes.
    /// </summary>
    public int BufferSize
    {
        get
        {
            lock (_bufferLock)
            {
                return _currentBuffer.Length;
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BinaryFileMonitor"/> class.
    /// </summary>
    /// <param name="filePath">The path to the binary file to monitor.</param>
    /// <param name="scanIntervalMs">The interval in milliseconds between file scans. Default is 100ms.</param>
    /// <exception cref="ArgumentNullException">Thrown when filePath is null.</exception>
    /// <exception cref="ArgumentException">Thrown when filePath is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when scanIntervalMs is less than or equal to 0.</exception>
    public BinaryFileMonitor(string filePath, int scanIntervalMs = 100)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be null or whitespace.", nameof(filePath));
        }

        if (scanIntervalMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scanIntervalMs), "Scan interval must be greater than 0.");
        }

        _filePath = filePath;
        _scanIntervalMs = scanIntervalMs;
        _currentBuffer = Array.Empty<byte>();
    }

    /// <summary>
    /// Starts monitoring the file for changes.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the monitor is already running.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the specified file does not exist.</exception>
    /// <exception cref="IOException">Thrown when the file cannot be read.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the monitor has been disposed.</exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_isRunning)
        {
            throw new InvalidOperationException("Monitor is already running.");
        }

        // Load initial file contents
        try
        {
            lock (_bufferLock)
            {
                _currentBuffer = File.ReadAllBytes(_filePath);
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Failed to load file: {_filePath}", ex);
        }

        // Start monitoring thread
        _isRunning = true;
        _monitorThread = new Thread(MonitorLoop)
        {
            IsBackground = true,
            Name = $"BinaryFileMonitor-{Path.GetFileName(_filePath)}"
        };
        _monitorThread.Start();
    }

    /// <summary>
    /// Stops monitoring the file.
    /// </summary>
    /// <remarks>
    /// This method blocks until the monitoring thread has exited.
    /// </remarks>
    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _monitorThread?.Join();
        _monitorThread = null;
    }

    /// <summary>
    /// Gets a copy of the current buffer contents.
    /// </summary>
    /// <returns>A byte array containing a copy of the current buffer.</returns>
    public byte[] GetCurrentBuffer()
    {
        lock (_bufferLock)
        {
            return (byte[])_currentBuffer.Clone();
        }
    }

    /// <summary>
    /// Gets a copy of a specific range of bytes from the buffer.
    /// </summary>
    /// <param name="offset">The starting offset.</param>
    /// <param name="length">The number of bytes to retrieve.</param>
    /// <returns>A byte array containing the requested range.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when offset or length is invalid.</exception>
    public byte[] GetBufferRange(int offset, int length)
    {
        lock (_bufferLock)
        {
            if (offset < 0 || offset >= _currentBuffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (length < 0 || offset + length > _currentBuffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            byte[] result = new byte[length];
            Array.Copy(_currentBuffer, offset, result, 0, length);
            return result;
        }
    }

    /// <summary>
    /// Gets a 32-bit word from the buffer at the specified byte offset.
    /// </summary>
    /// <param name="byteOffset">The byte offset of the word to retrieve (must be 4-byte aligned).</param>
    /// <returns>The 32-bit unsigned integer value at the specified offset.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when offset is invalid or not 4-byte aligned.</exception>
    public uint GetWord(int byteOffset)
    {
        lock (_bufferLock)
        {
            if (byteOffset < 0 || byteOffset + 4 > _currentBuffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(byteOffset));
            }

            if (byteOffset % 4 != 0)
            {
                throw new ArgumentException("Byte offset must be 4-byte aligned.", nameof(byteOffset));
            }

            return BitConverter.ToUInt32(_currentBuffer, byteOffset);
        }
    }

    /// <summary>
    /// Gets the number of 32-bit words in the buffer.
    /// </summary>
    public int WordCount
    {
        get
        {
            lock (_bufferLock)
            {
                return _currentBuffer.Length / 4;
            }
        }
    }

    private void MonitorLoop()
    {
        while (_isRunning)
        {
            try
            {
                Thread.Sleep(_scanIntervalMs);

                if (!_isRunning)
                {
                    break;
                }

                ReloadAndCompare();
            }
            catch (ThreadInterruptedException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnMonitorError(new FileMonitorErrorEventArgs(ex));
            }
        }
    }

    private void ReloadAndCompare()
    {
        byte[] newBuffer;

        try
        {
            newBuffer = File.ReadAllBytes(_filePath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            OnMonitorError(new FileMonitorErrorEventArgs(ex));
            return;
        }

        int changeCount = 0;

        lock (_bufferLock)
        {
            // Handle size changes
            if (newBuffer.Length != _currentBuffer.Length)
            {
                // Emit changes for all 32-bit words in the common range that differ
                int minLength = Math.Min(_currentBuffer.Length, newBuffer.Length);
                int wordCount = minLength / 4;

                for (int i = 0; i < wordCount; i++)
                {
                    int byteOffset = i * 4;
                    uint oldWord = BitConverter.ToUInt32(_currentBuffer, byteOffset);
                    uint newWord = BitConverter.ToUInt32(newBuffer, byteOffset);

                    if (oldWord != newWord)
                    {
                        OnWordChanged(new WordChangedEventArgs(byteOffset, oldWord, newWord));
                        changeCount++;
                    }
                }

                // Update buffer
                _currentBuffer = newBuffer;
                OnFileReloaded(new FileReloadedEventArgs(changeCount, true));
                return;
            }

            // Compare word-by-word (32-bit aligned)
            int totalWords = _currentBuffer.Length / 4;
            for (int i = 0; i < totalWords; i++)
            {
                int byteOffset = i * 4;
                uint oldWord = BitConverter.ToUInt32(_currentBuffer, byteOffset);
                uint newWord = BitConverter.ToUInt32(newBuffer, byteOffset);

                if (oldWord != newWord)
                {
                    OnWordChanged(new WordChangedEventArgs(byteOffset, oldWord, newWord));

                    // Update the word in current buffer
                    Array.Copy(newBuffer, byteOffset, _currentBuffer, byteOffset, 4);
                    changeCount++;
                }
            }
        }

        if (changeCount > 0)
        {
            OnFileReloaded(new FileReloadedEventArgs(changeCount, false));
        }
    }

    /// <summary>
    /// Raises the <see cref="WordChanged"/> event.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    protected virtual void OnWordChanged(WordChangedEventArgs e)
    {
        WordChanged?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the <see cref="FileReloaded"/> event.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    protected virtual void OnFileReloaded(FileReloadedEventArgs e)
    {
        FileReloaded?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the <see cref="MonitorError"/> event.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    protected virtual void OnMonitorError(FileMonitorErrorEventArgs e)
    {
        MonitorError?.Invoke(this, e);
    }

    /// <summary>
    /// Disposes the monitor and stops monitoring if running.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
