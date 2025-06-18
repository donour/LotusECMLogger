using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LotusECMLogger.Models;

namespace LotusECMLogger.Services
{
    public class LoggingService : ILoggingService, IDisposable
    {
        private StreamWriter? _writer;
        private bool _isDisposed;
        private readonly object _writeLock = new();
        private readonly string _logDirectory;
        private readonly List<string> _recentFiles = new();
        private const int MaxRecentFiles = 100;

        public event Action<string> LoggingStarted = delegate { };
        public event Action LoggingStopped = delegate { };
        public event Action<Exception> ErrorOccurred = delegate { };

        public bool IsLogging => _writer != null && !_isDisposed;
        public string? CurrentLogFile { get; private set; }

        public LoggingService()
        {
            _logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "LotusECMLogs"
            );
            Directory.CreateDirectory(_logDirectory);
        }

        public async Task StartLogging(string filePath)
        {
            if (IsLogging)
            {
                throw new InvalidOperationException("Logging is already in progress");
            }

            try
            {
                // Ensure directory exists
                var directory = Path.GetDirectoryName(filePath);
                if (directory != null)
                {
                    Directory.CreateDirectory(directory);
                }

                // Create or overwrite file
                _writer = new StreamWriter(filePath, false, Encoding.UTF8);
                CurrentLogFile = filePath;

                // Write header
                await WriteHeader();

                // Add to recent files
                AddToRecentFiles(filePath);

                LoggingStarted(filePath);
            }
            catch (Exception ex)
            {
                ErrorOccurred(ex);
                throw;
            }
        }

        public async Task StopLogging()
        {
            if (_writer != null)
            {
                try
                {
                    await _writer.FlushAsync();
                    _writer.Dispose();
                    _writer = null;
                    CurrentLogFile = null;
                    LoggingStopped();
                }
                catch (Exception ex)
                {
                    ErrorOccurred(ex);
                    throw;
                }
            }
        }

        public async Task WriteReadings(IEnumerable<LiveReading> readings)
        {
            if (!IsLogging || _writer == null)
                return;

            try
            {
                var timestamp = DateTime.Now;
                var lines = readings.Select(r => FormatReading(r, timestamp));

                lock (_writeLock)
                {
                    foreach (var line in lines)
                    {
                        await _writer.WriteLineAsync(line);
                    }
                    await _writer.FlushAsync();
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred(ex);
                throw;
            }
        }

        public async Task WriteCodingData(IEnumerable<ECUCodingOption> codingOptions)
        {
            if (!IsLogging || _writer == null)
                return;

            try
            {
                await _writer.WriteLineAsync("\n--- ECU Coding Data ---");
                foreach (var option in codingOptions)
                {
                    await _writer.WriteLineAsync($"{option.Name}: {option.Value} (Raw: {option.RawValue})");
                }
                await _writer.WriteLineAsync("--------------------\n");
                await _writer.FlushAsync();
            }
            catch (Exception ex)
            {
                ErrorOccurred(ex);
                throw;
            }
        }

        public async Task<IEnumerable<string>> GetRecentLogFiles(int count = 10)
        {
            return await Task.FromResult(_recentFiles.Take(count).ToList());
        }

        public string GetDefaultLogDirectory()
        {
            return _logDirectory;
        }

        private async Task WriteHeader()
        {
            if (_writer == null) return;

            await _writer.WriteLineAsync("Timestamp,Parameter,Value,Unit");
            await _writer.FlushAsync();
        }

        private static string FormatReading(LiveReading reading, DateTime timestamp)
        {
            return $"{timestamp:yyyy-MM-dd HH:mm:ss.fff},{reading.ParameterName},{reading.Value},{reading.Unit ?? ""}";
        }

        private void AddToRecentFiles(string filePath)
        {
            _recentFiles.Remove(filePath); // Remove if exists
            _recentFiles.Insert(0, filePath); // Add to start

            // Trim list if too long
            while (_recentFiles.Count > MaxRecentFiles)
            {
                _recentFiles.RemoveAt(_recentFiles.Count - 1);
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;
            _writer?.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Get the suggested log file path for a new logging session
        /// </summary>
        public string GetSuggestedLogFilePath()
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(_logDirectory, $"LotusECMLog_{timestamp}.csv");
        }

        /// <summary>
        /// Check if a file is currently being logged to
        /// </summary>
        public bool IsFileInUse(string filePath)
        {
            return CurrentLogFile?.Equals(filePath, StringComparison.OrdinalIgnoreCase) ?? false;
        }
    }
} 