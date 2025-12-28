using SAE.J2534;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace LotusECMLogger.Services
{
    internal class J2534LoggingService : IDisposable
    {
        public readonly int LogFileToUIRatio = 8; // UI update every 8th log entry
        public event Action<List<LiveDataReading>>? DataLogged;
        public event Action<Exception>? ExceptionOccurred;

        private readonly string outputFilename;
        private readonly MultiECUConfiguration multiEcuConfig;
        private bool terminate = false;
        private Thread? loggerThread;
        private Thread? csvWriterThread;
        private Device? device;

        /// <summary>
        /// Whether to prefix reading names with ECU name (useful when logging from multiple ECUs)
        /// </summary>
        public bool PrefixReadingsWithEcuName { get; set; } = true;

        public bool IsConnected => device != null && !terminate;

        // CSV writer thread coordination
        private readonly ConcurrentQueue<List<LiveDataReading>> csvWriteQueue = new();
        private readonly ManualResetEvent csvDataAvailable = new(false);
        private volatile bool csvWriterShouldStop = false;

        /// <summary>
        /// Creates logger with multi-ECU configuration for logging from multiple control units
        /// </summary>
        /// <param name="filename">Output CSV file path</param>
        /// <param name="logger_DataLogged">Data received callback</param>
        /// <param name="exceptionHandler">Exception handler callback</param>
        /// <param name="configuration">Multi-ECU configuration</param>
        public J2534LoggingService(
            string filename,
            Action<List<LiveDataReading>> logger_DataLogged,
            Action<Exception> exceptionHandler,
            MultiECUConfiguration configuration)
        {
            this.outputFilename = filename;
            this.multiEcuConfig = configuration;
            this.DataLogged += logger_DataLogged;
            this.ExceptionOccurred += exceptionHandler;

            // Auto-enable prefix if multiple ECUs are configured
            PrefixReadingsWithEcuName = configuration.ECUGroups.Count > 1;
        }

        /// <summary>
        /// Creates logger with legacy single-ECU OBD configuration (backward compatible)
        /// </summary>
        public J2534LoggingService(
            string filename,
            Action<List<LiveDataReading>> logger_DataLogged,
            Action<Exception> exceptionHandler,
            OBDConfiguration configuration)
            : this(filename, logger_DataLogged, exceptionHandler,
                   MultiECUConfiguration.FromLegacyConfig(configuration))
        {
            // Legacy mode: don't prefix readings
            PrefixReadingsWithEcuName = false;
        }

        public void Stop()
        {
            if (terminate)
                return; // Already stopping/stopped

            terminate = true;

            // Signal CSV writer to stop and wait for it to finish
            csvWriterShouldStop = true;
            csvDataAvailable.Set();

            // Wait for threads to finish with timeout
            loggerThread?.Join(2000); // Wait up to 2 seconds for main logger
            csvWriterThread?.Join(1000); // Wait up to 1 second for CSV writer
        }

        public void Start()
        {
            string DllFileName = APIFactory.GetAPIinfo().First().Filename;
            API API = APIFactory.GetAPI(DllFileName);
            device = API.GetDevice();
            try
            {
                // Start CSV writer thread first
                csvWriterShouldStop = false;
                csvWriterThread = new Thread(RunCSVWriter)
                {
                    IsBackground = true,
                    Name = "CSV Writer"
                };
                csvWriterThread.Start();

                // Start main logger thread
                loggerThread = new Thread(() => RunLoggerWithExceptionHandling(device))
                {
                    IsBackground = true,
                    Name = "J2534 Logger"
                };
                loggerThread.Start();
            }
            catch (Exception ex)
            {
                OnExceptionOccurred(ex);
            }
        }

        private void RunLoggerWithExceptionHandling(Device device)
        {
            try
            {
                RunLogger(device);
            }
            catch (Exception ex)
            {
                OnExceptionOccurred(ex);
            }
        }

        private void OnDataLogged(List<LiveDataReading> data)
        {
            if (terminate == false)
            {
                try
                {
                    DataLogged?.Invoke(data);
                }
                catch (ObjectDisposedException)
                {
                    // UI was disposed, ignore
                }
                catch (InvalidOperationException)
                {
                    // UI handle was destroyed, ignore
                }
            }
        }

        private void OnExceptionOccurred(Exception ex)
        {
            if (terminate == false)
            {
                try
                {
                    ExceptionOccurred?.Invoke(ex);
                }
                catch (ObjectDisposedException)
                {
                    // UI was disposed, ignore
                }
                catch (InvalidOperationException)
                {
                    // UI handle was destroyed, ignore
                }
            }
        }

        /// <summary>
        /// Background thread that handles CSV writing to improve J2534 communication performance
        /// </summary>
        private void RunCSVWriter()
        {
            try
            {
                using (var writer = new CSVWriter(outputFilename))
                {
                    while (!csvWriterShouldStop)
                    {
                        // Wait for data to be available
                        csvDataAvailable.WaitOne();

                        // Process all queued data
                        while (csvWriteQueue.TryDequeue(out var readings))
                        {
                            writer.WriteLine(readings);
                        }

                        // Reset the signal if queue is empty
                        if (csvWriteQueue.IsEmpty)
                        {
                            csvDataAvailable.Reset();
                        }
                    }

                    // Process any remaining data in the queue before shutdown
                    while (csvWriteQueue.TryDequeue(out var readings))
                    {
                        writer.WriteLine(readings);
                    }
                }
            }
            catch (Exception ex)
            {
                OnExceptionOccurred(ex);
            }
        }

        private void RunLogger(Device Device)
        {
            try
            {
                using Channel Channel = Device.GetChannel(Protocol.ISO15765, Baud.ISO15765, ConnectFlag.NONE);

                // Set up flow control filters for ALL ECUs in the configuration
                var filters = multiEcuConfig.GetAllFlowControlFilters().ToList();
                foreach (var filter in filters)
                {
                    Channel.StartMsgFilter(filter);
                    Debug.WriteLine($"Added flow control filter: Pattern=0x{BitConverter.ToString(filter.Pattern).Replace("-", "")}, FlowControl=0x{BitConverter.ToString(filter.FlowControl).Replace("-", "")}");
                }

                // Build all messages grouped by ECU
                var messagesByEcu = multiEcuConfig.BuildAllMessagesByECU();

                // Log configuration for debugging
                Debug.WriteLine($"Multi-ECU logging configured with {multiEcuConfig.ECUGroups.Count} ECU(s):");
                foreach (var group in multiEcuConfig.ECUGroups)
                {
                    Debug.WriteLine($"  {group.ECU.Name} (0x{group.ECU.RequestId:X3}/0x{group.ECU.ResponseId:X3}): {group.Requests.Count} requests");
                    foreach (var request in group.Requests)
                    {
                        Debug.WriteLine($"    - {request.Name} (Mode 0x{request.Mode:X2})");
                    }
                }

                uint ui_update_counter = 0;
                while (terminate == false)
                {
                    List<LiveDataReading> readings = [];

                    // Send requests to each ECU and collect responses
                    foreach (var (ecu, messages) in messagesByEcu)
                    {
                        foreach (var chunk in messages.Chunk(5))
                        {
                            Channel.SendMessages(chunk);
                            readings.AddRange(ReadPendingMessages(Channel, ecu));
                        }
                    }

                    if (readings.Count > 0)
                    {
                        var tr = new LiveDataReading
                        {
                            name = "time (s)",
                            value_f = DateTime.Now.TimeOfDay.TotalSeconds
                        };
                        readings.Add(tr);

                        if (ui_update_counter++ % LogFileToUIRatio == 0)
                        {
                            OnDataLogged(readings);
                        }

                        // Queue data for background CSV writing (non-blocking)
                        QueueDataForCSVWriting(readings);
                    }
                }
            }
            finally
            {
                device?.Dispose();
            }
        }

        /// <summary>
        /// Queue data for background CSV writing (non-blocking operation)
        /// </summary>
        /// <param name="readings">Data to write to CSV</param>
        private void QueueDataForCSVWriting(List<LiveDataReading> readings)
        {
            // Create a copy to avoid shared memory issues between threads
            var readingsCopy = new List<LiveDataReading>(readings);
            csvWriteQueue.Enqueue(readingsCopy);
            csvDataAvailable.Set();
        }

        /// <summary>
        /// Read pending messages and parse them with ECU context
        /// </summary>
        /// <param name="channel">J2534 channel</param>
        /// <param name="expectedEcu">ECU we expect responses from (for context-aware parsing)</param>
        private List<LiveDataReading> ReadPendingMessages(Channel channel, ECUDefinition expectedEcu)
        {
            List<LiveDataReading> readings = [];

            try
            {
                GetMessageResults resp;
                do
                {
                    resp = channel.GetMessages(1, 0);
                    if (resp.Messages.Length > 0)
                    {
                        var mesg = resp.Messages[0];

                        // Try to find which ECU this response is from
                        var matchingEcu = multiEcuConfig.FindECUForResponse(mesg.Data);

                        if (matchingEcu != null)
                        {
                            // Parse with the matching ECU context
                            readings.AddRange(LiveDataReading.ParseCanResponse(
                                mesg.Data,
                                matchingEcu,
                                PrefixReadingsWithEcuName));
                        }
                        else
                        {
                            // Unknown ECU response - try legacy parsing
                            readings.AddRange(LiveDataReading.ParseCanResponse(mesg.Data));
                        }
                    }
                } while (resp.Messages.Length > 0);
            }
            catch (TimeoutException)
            {
                // skip timeout, no messages received
            }
            return readings;
        }

        /// <summary>
        /// Dispose resources including the ManualResetEvent
        /// </summary>
        public void Dispose()
        {
            Stop();
            csvDataAvailable?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
