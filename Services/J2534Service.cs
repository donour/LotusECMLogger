using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LotusECMLogger.Models;
using SAE.J2534;

namespace LotusECMLogger.Services
{
    public class J2534Service : IJ2534Service, IDisposable
    {
        private J2534OBDLogger? _logger;
        private readonly IConfigurationService _configService;
        private readonly ILoggingService _loggingService;
        private DateTime _lastUpdateTime = DateTime.Now;
        private bool _isDisposed;

        public event Action<IEnumerable<LiveReading>> DataReceived = delegate { };
        public event Action<IEnumerable<ECUCodingOption>> CodingDataUpdated = delegate { };
        public event Action<Exception> ErrorOccurred = delegate { };

        public bool IsLogging => _logger != null;
        public string? CurrentLogFile => _loggingService.CurrentLogFile;
        public double RefreshRate { get; private set; }

        public J2534Service(IConfigurationService configService, ILoggingService loggingService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        public async Task StartLogging(string outputFile, string configName)
        {
            if (_logger != null)
            {
                throw new InvalidOperationException("Logging is already in progress");
            }

            try
            {
                // Load configuration
                var config = await _configService.LoadConfiguration(configName);

                // Start logging service
                await _loggingService.StartLogging(outputFile);

                // Create and start logger
                _logger = new J2534OBDLogger(
                    outputFile,
                    HandleDataLogged,
                    HandleError,
                    config
                );

                _logger.CodingDataUpdated += HandleCodingDataUpdated;
                _logger.Start();
            }
            catch (Exception ex)
            {
                await StopLogging();
                throw new Exception("Failed to start logging", ex);
            }
        }

        public async Task StopLogging()
        {
            if (_logger != null)
            {
                _logger.Stop();
                _logger.Dispose();
                _logger = null;
            }

            await _loggingService.StopLogging();
        }

        public async Task<IEnumerable<string>> GetAvailableDevices()
        {
            var devices = new List<string>();
            try
            {
                string DllFileName = APIFactory.GetAPIinfo().First().Filename;
                API API = APIFactory.GetAPI(DllFileName);
                var device = API.GetDevice();
                devices.Add(device.Name);
            }
            catch (Exception ex)
            {
                HandleError(ex);
            }
            return await Task.FromResult(devices);
        }

        public async Task<IEnumerable<ECUCodingOption>?> GetCurrentCodingOptions()
        {
            if (_logger?.CodingDecoder == null)
                return null;

            var options = _logger.CodingDecoder.GetAllOptions();
            var codingOptions = new List<ECUCodingOption>();

            foreach (var option in options)
            {
                var rawValue = _logger.CodingDecoder.GetOptionRawValue(option.Key);
                var possibleValues = GetPossibleValues(option.Key);
                
                codingOptions.Add(ECUCodingOption.FromDecoderOption(
                    option.Key,
                    option.Value,
                    rawValue,
                    possibleValues
                ));
            }

            return await Task.FromResult(codingOptions);
        }

        private string[] GetPossibleValues(string optionName)
        {
            // This would need to be implemented based on T6eCodingDecoder's internal knowledge
            // of possible values for each option. For now, returning empty array.
            return Array.Empty<string>();
        }

        private void HandleDataLogged(List<LiveDataReading> readings)
        {
            if (_isDisposed) return;

            var liveReadings = readings.Select(r => LiveReading.FromLiveDataReading(r)).ToList();
            
            // Calculate refresh rate
            DateTime now = DateTime.Now;
            RefreshRate = _logger?.LogFileToUIRatio * 1000.0 / (now - _lastUpdateTime).TotalMilliseconds ?? 0;
            _lastUpdateTime = now;

            // Notify subscribers
            DataReceived(liveReadings);

            // Log to file
            _loggingService.WriteReadings(liveReadings).ConfigureAwait(false);
        }

        private void HandleCodingDataUpdated(T6eCodingDecoder decoder)
        {
            if (_isDisposed) return;

            var options = decoder.GetAllOptions();
            var codingOptions = new List<ECUCodingOption>();

            foreach (var option in options)
            {
                var rawValue = decoder.GetOptionRawValue(option.Key);
                var possibleValues = GetPossibleValues(option.Key);
                
                codingOptions.Add(ECUCodingOption.FromDecoderOption(
                    option.Key,
                    option.Value,
                    rawValue,
                    possibleValues
                ));
            }

            CodingDataUpdated(codingOptions);
            _loggingService.WriteCodingData(codingOptions).ConfigureAwait(false);
        }

        private void HandleError(Exception ex)
        {
            if (_isDisposed) return;
            ErrorOccurred(ex);
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;
            _logger?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
} 