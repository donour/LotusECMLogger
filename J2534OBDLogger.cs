using SAE.J2534;
using System.Diagnostics;

namespace LotusECMLogger
{
    internal class J2534OBDLogger
    {
        public event Action<List<LiveDataReading>> DataLogged;
        public event Action<Exception> ExceptionOccurred;

        private readonly String output_filename;
        private readonly OBDConfiguration obdConfig;
        private bool terminate = false;
        private Thread? loggerThread;
        private Device? device;

        public J2534OBDLogger(String filename, Action<List<LiveDataReading>> logger_DataLogged, Action<Exception> exceptionHandler)
            : this(filename, logger_DataLogged, exceptionHandler, OBDConfiguration.CreateLotusDefault())
        {
        }

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
        /// // Use diagnostic mode (more parameters)
        /// var logger = new J2534OBDLogger("log.csv", OnData, OnError, 
        ///     OBDConfiguration.CreateDiagnosticMode());
        /// 
        /// // Create custom configuration
        /// var customConfig = new OBDConfiguration();
        /// customConfig.Requests.Add(new Mode01Request("RPM Only", 0x0C));
        /// var logger = new J2534OBDLogger("log.csv", OnData, OnError, customConfig);
        /// </example>
        public J2534OBDLogger(String filename, Action<List<LiveDataReading>> logger_DataLogged, Action<Exception> exceptionHandler, OBDConfiguration configuration)
        {
            this.output_filename = filename;
            this.obdConfig = configuration;
            this.DataLogged += logger_DataLogged;
            this.ExceptionOccurred += exceptionHandler;
        }

        public void Stop()
        {
            terminate = true;
        }

        public void Start()
        {
            string DllFileName = APIFactory.GetAPIinfo().First().Filename;
            API API = APIFactory.GetAPI(DllFileName);
            device = API.GetDevice();
            try
            {
                loggerThread = new Thread(() => RunLoggerWithExceptionHandling(device))
                //loggerThread = new Thread(() => test())
                {
                    IsBackground = true
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
                DataLogged?.Invoke(data);
            }
        }

        private void OnExceptionOccurred(Exception ex)
        {
            if (terminate == false)
            {
                ExceptionOccurred?.Invoke(ex);
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
        private void RunLogger(Device Device)
        {
            try
            {
                using (Channel Channel = Device.GetChannel(Protocol.ISO15765, Baud.ISO15765, ConnectFlag.NONE))
                {
                    MessageFilter FlowControlFilter = new()
                    {
                        FilterType = Filter.FLOW_CONTROL_FILTER,
                        Mask = [0xFF, 0xFF, 0xFF, 0xFF],
                        Pattern = [0x00, 0x00, 0x07, 0xE8],
                        FlowControl = [0x00, 0x00, 0x07, 0xE0]
                    };
                    Channel.StartMsgFilter(FlowControlFilter);

                    // Build all OBD messages from configuration
                    var allMessages = obdConfig.BuildAllMessages();
                    
                    // Log configuration for debugging
                    Debug.WriteLine($"Loaded {obdConfig.Requests.Count} OBD requests:");
                    foreach (var request in obdConfig.Requests)
                    {
                        Debug.WriteLine($"  - {request.Name} (Mode 0x{request.Mode:X2})");
                    }

                    using (var writer = new CSVWriter(output_filename))
                    {
                        uint ui_update_counter = 0;
                        while (terminate == false)
                        {
                            List<LiveDataReading> readings = [];

                            // Send all configured OBD requests
                            foreach (var message in allMessages)
                            {
                                Channel.SendMessage(message);
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

                                if (ui_update_counter++ % 10 == 0)
                                {
                                    OnDataLogged(readings);
                                }
                                // TODO: performance would be improved if this happens in a different thread.
                                writer.WriteLine(readings);
                            }
                        }
                    }
                }
            }
            finally
            {
                device?.Dispose();
            }
        }

        private static List<LiveDataReading> ReadPendingMessages(Channel Channel)
        {
            List<LiveDataReading> readings = [];

            try
            {
                GetMessageResults resp;
                do
                {
                    resp = Channel.GetMessages(1, 1);
                    if (resp.Messages.Length > 0)
                    {
                        var mesg = resp.Messages[0];
                        readings.AddRange(LiveDataReading.parseCanResponse(mesg.Data));
                    }
                } while (resp.Messages.Length > 0);
            }
            catch (TimeoutException)
            {
                // skip timeout, no messages received
            }
            return readings;
        }
    }
}