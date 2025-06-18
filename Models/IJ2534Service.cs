using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LotusECMLogger.Models
{
    /// <summary>
    /// Interface for J2534 device communication and data logging
    /// </summary>
    public interface IJ2534Service
    {
        /// <summary>
        /// Event raised when new data is received from the ECU
        /// </summary>
        event Action<IEnumerable<LiveReading>> DataReceived;

        /// <summary>
        /// Event raised when ECU coding data is updated
        /// </summary>
        event Action<IEnumerable<ECUCodingOption>> CodingDataUpdated;

        /// <summary>
        /// Event raised when an error occurs during communication
        /// </summary>
        event Action<Exception> ErrorOccurred;

        /// <summary>
        /// Whether the service is currently logging data
        /// </summary>
        bool IsLogging { get; }

        /// <summary>
        /// The current log file path, if logging
        /// </summary>
        string? CurrentLogFile { get; }

        /// <summary>
        /// The current refresh rate in Hz
        /// </summary>
        double RefreshRate { get; }

        /// <summary>
        /// Start logging with the specified configuration
        /// </summary>
        /// <param name="outputFile">Path to the output CSV file</param>
        /// <param name="configName">Name of the OBD configuration to use</param>
        /// <returns>Task representing the async operation</returns>
        Task StartLogging(string outputFile, string configName);

        /// <summary>
        /// Stop the current logging session
        /// </summary>
        /// <returns>Task representing the async operation</returns>
        Task StopLogging();

        /// <summary>
        /// Get the currently available J2534 devices
        /// </summary>
        /// <returns>List of available device names</returns>
        Task<IEnumerable<string>> GetAvailableDevices();

        /// <summary>
        /// Get the current ECU coding options
        /// </summary>
        /// <returns>Current coding options or null if not available</returns>
        Task<IEnumerable<ECUCodingOption>?> GetCurrentCodingOptions();
    }
} 