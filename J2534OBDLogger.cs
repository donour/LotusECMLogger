﻿using SAE.J2534;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace LotusECMLogger
{
    internal class J2534OBDLogger : IDisposable
    {
        public readonly int LogFileToUIRatio = 8; // UI update every 8th log entry
        public event Action<List<LiveDataReading>> DataLogged;
        public event Action<Exception> ExceptionOccurred;
        public event Action<T6eCodingDecoder> CodingDataUpdated;

        private T6eCodingDecoder codingDecoder = new(
            [0,0,0,0],
            [0,0,0,0]
        );

        public T6eCodingDecoder CodingDecoder
        {
            get => codingDecoder;
            private set
            {
                codingDecoder = value;
                CodingDataUpdated?.Invoke(value);
            }
        }

        /// <summary>
        /// Flow control filter for Lotus ECM communication
        /// Pattern: [0x00, 0x00, 0x07, 0xE8] - ECM response header
        /// FlowControl: [0x00, 0x00, 0x07, 0xE0] - ECM request header
        /// </summary>
        private static readonly MessageFilter FlowControlFilter = new()
        {
            FilterType = Filter.FLOW_CONTROL_FILTER,
            Mask = [0xFF, 0xFF, 0xFF, 0xFF],
            Pattern = [0x00, 0x00, 0x07, 0xE8],
            FlowControl = [0x00, 0x00, 0x07, 0xE0]
        };

        private readonly String output_filename;
        private readonly OBDConfiguration obdConfig;
        private bool terminate = false;
        private Thread? loggerThread;
        private Thread? csvWriterThread;
        private Device? device;

        // CSV writer thread coordination
        private readonly ConcurrentQueue<List<LiveDataReading>> csvWriteQueue = new();
        private readonly ManualResetEvent csvDataAvailable = new(false);
        private volatile bool csvWriterShouldStop = false;

        /// <summary>
        /// Creates logger with custom OBD configuration
        /// </summary>
        /// <param name="filename">Output CSV file path</param>
        /// <param name="logger_DataLogged">Data received callback</param>
        /// <param name="exceptionHandler">Exception handler callback</param>
        /// <param name="configuration">OBD request configuration</param>
        /// <example>
        /// // Use fast logging (fewer requests for better performance)
        /// var logger = new J2534OBDLogger("log.csv", OnData, OnError, 
        ///     OBDConfiguration.CreateFastLogging());
        /// 
        /// // Use complete Lotus configuration (ALL available parameters)
        /// var logger = new J2534OBDLogger("log.csv", OnData, OnError, 
        ///     OBDConfiguration.CreateCompleteLotusConfiguration());
        /// 
        /// // Use diagnostic mode (extended parameters)
        /// var logger = new J2534OBDLogger("log.csv", OnData, OnError, 
        ///     OBDConfiguration.CreateDiagnosticMode());
        /// 
        /// // Create custom configuration
        /// var customConfig = new OBDConfiguration();
        /// customConfig.Requests.Add(new Mode01Request("RPM Only", 0x0C));
        /// customConfig.Requests.Add(new Mode22Request("Sport Button", 0x02, 0x5D));
        /// var logger = new J2534OBDLogger("log.csv", OnData, OnError, customConfig);
        /// </example>
        public J2534OBDLogger(
            String filename, 
            Action<List<LiveDataReading>> logger_DataLogged, 
            Action<Exception> exceptionHandler,
            OBDConfiguration configuration)
        {
            this.output_filename = filename;
            this.obdConfig = configuration;
            this.DataLogged += logger_DataLogged;
            this.ExceptionOccurred += exceptionHandler;
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
                //loggerThread = new Thread(() => Test())
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
                using (var writer = new CSVWriter(output_filename))
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

        private void Test()
        {
            while (true)
            {
                LiveDataReading reading = new LiveDataReading
                {
                    name = DateTime.Now.Second.ToString(),
                    value_f = (float)(DateTime.Now.TimeOfDay.Milliseconds)
                };
                OnDataLogged([reading]);
                Thread.Sleep(10);
                if (terminate)
                {
                    return;
                }
            }
        }

        private static T6eCodingDecoder GetCodingData(Channel Channel)
        {
            byte[][] result = [[0, 0, 0, 0], [0,0,0,0]];

            byte[][] codingRequest =
            [
                [0x00, 0x00, 0x07, 0xE0, 0x22, 0x02, 0x63],
                [0x00, 0x00, 0x07, 0xE0, 0x22, 0x02, 0x64]
            ];
            int done = 0;
            do
            {
                Channel.SendMessages(codingRequest);
                GetMessageResults resp = Channel.GetMessages(1, 100);
                if (resp.Messages.Length > 0)
                {
                    var data = resp.Messages[0].Data;
                    if (data.Length >= 11)
                    {
                        if (data[4] == 0x62 && data[5] == 0x02)
                        {
                            if (data[6] == 0x63)
                            {
                                result[1] = data[7..11];
                                done |= 1;
                            }
                            if (data[6] == 0x64)
                            {
                                result[0] = data[7..11];
                                done |= 2;
                            }
                        }
                    }
                }
            } while (done != 3);

            return new T6eCodingDecoder(result[1], result[0]);
        }

        private void RunLogger(Device Device)
        {
            try
            {
                using Channel Channel = Device.GetChannel(Protocol.ISO15765, Baud.ISO15765, ConnectFlag.NONE);
                Channel.StartMsgFilter(FlowControlFilter);

                CodingDecoder = GetCodingData(Channel);

                // print each field of the coding decoder
                foreach (var field in codingDecoder.GetAllOptions())
                {
                    Debug.WriteLine($"{field.Key}: {field.Value}");
                }

                // Build all OBD messages from configuration
                var allMessages = obdConfig.BuildAllMessages();

                // Log configuration for debugging
                Debug.WriteLine($"Loaded {obdConfig.Requests.Count} OBD requests:");
                foreach (var request in obdConfig.Requests)
                {
                    Debug.WriteLine($"  - {request.Name} (Mode 0x{request.Mode:X2})");
                }

                uint ui_update_counter = 0;
                while (terminate == false)
                {
                    List<LiveDataReading> readings = [];

                    foreach (var chunk in allMessages.Chunk(5))
                    {
                        // TODO: allow no more than 6250 messages per second
                        // in order to not overload either the CAN bus are the ECM
                        Channel.SendMessages(chunk);
                        readings.AddRange(ReadPendingMessages(Channel));
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

        private static List<LiveDataReading> ReadPendingMessages(Channel Channel)
        {
            List<LiveDataReading> readings = [];

            try
            {
                GetMessageResults resp;
                do
                {
                    resp = Channel.GetMessages(1, 0);
                    if (resp.Messages.Length > 0)
                    {
                        var mesg = resp.Messages[0];
                        readings.AddRange(LiveDataReading.ParseCanResponse(mesg.Data));
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