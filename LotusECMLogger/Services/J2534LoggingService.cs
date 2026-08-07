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
        private Thread? loggerThread;
        private Thread? csvWriterThread;
        private J2534Session? session;

        /// <summary>
        /// The single stop signal for the logger thread. A token carries the memory barriers that a
        /// plain <c>bool</c> field does not: the polling loop is guaranteed to observe a
        /// <see cref="CancellationTokenSource.Cancel"/> from the UI thread rather than caching the
        /// flag in a register and spinning forever — which would leave the J2534 device open until
        /// the process exits, and the next start failing because the device is still busy.
        /// </summary>
        private readonly CancellationTokenSource cts = new();

        /// <summary>
        /// Hand-off from the logger thread to the CSV writer. <c>CompleteAdding()</c> is the writer's
        /// stop signal and its wake-up in one, so there is no separate flag or wait handle that
        /// shutdown could dispose out from under a thread still parked on it.
        /// </summary>
        private readonly BlockingCollection<List<LiveDataReading>> csvWriteQueue = new();

        /// <summary>
        /// Set once both workers have provably exited. Guards the disposal of everything they touch.
        /// </summary>
        private bool workersStopped;

        /// <summary>
        /// Whether to prefix reading names with ECU name (useful when logging from multiple ECUs)
        /// </summary>
        public bool PrefixReadingsWithEcuName { get; set; } = true;

        public bool IsConnected => session != null && !cts.IsCancellationRequested;

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
            if (cts.IsCancellationRequested)
                return; // Already stopping/stopped

            cts.Cancel();

            // Ends the writer's consuming loop once it has drained whatever is still queued.
            csvWriteQueue.CompleteAdding();

            // Bounded waits so a wedged worker can never hang the UI thread. Both are background
            // threads, so anything outliving these joins dies with the process rather than keeping
            // it alive — but it may still be holding the device, hence the warning.
            bool loggerStopped = loggerThread?.Join(2000) ?? true;
            bool writerStopped = csvWriterThread?.Join(1000) ?? true;

            workersStopped = loggerStopped && writerStopped;
            if (!workersStopped)
                Debug.WriteLine("[J2534LoggingService] A worker thread did not stop within its join timeout.");
        }

        public void Start()
        {
            // Cancellation is one-way, so a stopped session cannot be restarted. The UI builds a
            // fresh service per run; this makes the alternative fail loudly instead of silently
            // starting threads that exit immediately.
            if (cts.IsCancellationRequested)
                throw new InvalidOperationException(
                    "This logging session has already been stopped. Create a new J2534LoggingService.");

            session = J2534Session.Open();
            try
            {
                // Start CSV writer thread first
                csvWriterThread = new Thread(RunCSVWriter)
                {
                    IsBackground = true,
                    Name = "CSV Writer"
                };
                csvWriterThread.Start();

                // Start main logger thread
                loggerThread = new Thread(RunLoggerWithExceptionHandling)
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

        private void RunLoggerWithExceptionHandling()
        {
            try
            {
                RunLogger();
            }
            catch (Exception ex)
            {
                OnExceptionOccurred(ex);
            }
        }

        private void OnDataLogged(List<LiveDataReading> data)
        {
            if (!cts.IsCancellationRequested)
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
            if (!cts.IsCancellationRequested)
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
                using var writer = new CSVWriter(outputFilename);

                // Blocks until a batch arrives, and ends only once CompleteAdding() has been called
                // AND the queue is empty — so rows already queued at shutdown are still written.
                foreach (var readings in csvWriteQueue.GetConsumingEnumerable())
                {
                    writer.WriteLine(readings);
                }
            }
            catch (Exception ex)
            {
                OnExceptionOccurred(ex);
            }
        }

        private void RunLogger()
        {
            try
            {
                J2534Channel Channel = session!.OpenIso15765();

                // Set up flow control filters for ALL ECUs in the configuration
                var filters = multiEcuConfig.GetAllFlowControlFilters().ToList();
                foreach (var filter in filters)
                {
                    Channel.StartMessageFilter(filter).ThrowIfError();
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
                while (!cts.IsCancellationRequested)
                {
                    List<LiveDataReading> readings = [];

                    // Send requests to each ECU and collect responses
                    foreach (var (ecu, messages) in messagesByEcu)
                    {
                        foreach (var chunk in messages.Chunk(5))
                        {
                            Channel.SendMessages(Array.ConvertAll(chunk, b => new SAE.J2534.Message(b, Channel.DefaultTxFlags)));
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
                session?.Dispose();
                session = null;
            }
        }

        /// <summary>
        /// Queue data for background CSV writing (non-blocking operation)
        /// </summary>
        /// <param name="readings">Data to write to CSV</param>
        private void QueueDataForCSVWriting(List<LiveDataReading> readings)
        {
            try
            {
                // Create a copy to avoid shared memory issues between threads
                csvWriteQueue.Add(new List<LiveDataReading>(readings));
            }
            catch (InvalidOperationException)
            {
                // Stop() called CompleteAdding() between this loop's cancellation check and the Add.
                // The batch is dropped deliberately rather than reopening a queue already draining.
            }
        }

        /// <summary>
        /// Read pending messages and parse them with ECU context
        /// </summary>
        /// <param name="channel">J2534 channel</param>
        /// <param name="expectedEcu">ECU we expect responses from (for context-aware parsing)</param>
        private List<LiveDataReading> ReadPendingMessages(J2534Channel channel, ECUDefinition expectedEcu)
        {
            List<LiveDataReading> readings = [];

            // v2 ReadMessages returns an empty GetMessagesResult on timeout (Messages.Length == 0
            // ends the loop) rather than throwing, so no timeout handling is needed here.
            GetMessagesResult resp;
            do
            {
                resp = channel.ReadMessages(1, 0);
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

            return readings;
        }

        public void Dispose()
        {
            Stop();

            // Only release what the workers touch once they have provably exited. A thread that
            // blew its join timeout may still be reading the token or adding to the queue, and
            // disposing underneath it would throw ObjectDisposedException on a background thread.
            // Leaking them in that (already degraded) case is the cheaper failure.
            if (workersStopped)
            {
                cts.Dispose();
                csvWriteQueue.Dispose();
            }

            GC.SuppressFinalize(this);
        }
    }
}
