using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LotusECMLogger.Models
{
    /// <summary>
    /// Interface for logging ECU data to files
    /// </summary>
    public interface ILoggingService
    {
        /// <summary>
        /// Event raised when logging starts
        /// </summary>
        event Action<string> LoggingStarted;

        /// <summary>
        /// Event raised when logging stops
        /// </summary>
        event Action LoggingStopped;

        /// <summary>
        /// Event raised when an error occurs during logging
        /// </summary>
        event Action<Exception> ErrorOccurred;

        /// <summary>
        /// Whether logging is currently active
        /// </summary>
        bool IsLogging { get; }

        /// <summary>
        /// The current log file path
        /// </summary>
        string? CurrentLogFile { get; }

        /// <summary>
        /// Start logging to a new file
        /// </summary>
        /// <param name="filePath">Path to the log file</param>
        /// <returns>Task representing the async operation</returns>
        Task StartLogging(string filePath);

        /// <summary>
        /// Stop the current logging session
        /// </summary>
        /// <returns>Task representing the async operation</returns>
        Task StopLogging();

        /// <summary>
        /// Write readings to the log file
        /// </summary>
        /// <param name="readings">Readings to log</param>
        /// <returns>Task representing the async operation</returns>
        Task WriteReadings(IEnumerable<LiveReading> readings);

        /// <summary>
        /// Write ECU coding data to the log file
        /// </summary>
        /// <param name="codingOptions">Coding options to log</param>
        /// <returns>Task representing the async operation</returns>
        Task WriteCodingData(IEnumerable<ECUCodingOption> codingOptions);

        /// <summary>
        /// Get a list of recent log files
        /// </summary>
        /// <param name="count">Maximum number of files to return</param>
        /// <returns>List of recent log file paths</returns>
        Task<IEnumerable<string>> GetRecentLogFiles(int count = 10);

        /// <summary>
        /// Get the default directory for log files
        /// </summary>
        /// <returns>Path to the default log directory</returns>
        string GetDefaultLogDirectory();
    }
} 