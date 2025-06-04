using SAE.J2534;
using System.Diagnostics;

namespace LotusECMLogger
{
    internal class J2534OBDLogger
    {
        public event Action<List<LiveDataReading>> DataLogged;
        public event Action<Exception> ExceptionOccurred;

        private readonly String output_filename;
        private bool terminate = false;
        private Thread? loggerThread;
        private Device? device;

        public J2534OBDLogger(String filename, Action<List<LiveDataReading>> logger_DataLogged)
        {
            this.output_filename = filename;
            this.DataLogged += logger_DataLogged;
        }

        public J2534OBDLogger(String filename, Action<List<LiveDataReading>> logger_DataLogged, Action<Exception> exceptionHandler)
        {
            this.output_filename = filename;
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


                // TODO: this should be configurable
                byte[] ecm_obd_head = [0x00, 0x00, 0x07, 0xE0];
                //engine speed, tps,  timing
                byte[] obd_basic_request = [0x01,
                        0x0C, // engine speed
                        0x11, // throttle position
                        0x43, // absolute load
                        ];
                byte[] obd_secondary_request = [0x01,
                        0x05, // coolant temperature
                        0x0E, // timing advance
                        0x0B, // intake manifold absolute pressure
                        ];

                byte[] obd_coolant_request = [0x01, 0x05];
                byte[] obd_mode22_sport_button = [0x22, 0x02, 0x5D];
                byte[] obd_mode22_tps = [0x22, 0x02, 0x45]; // two bytes
                byte[] obd_mode22_accel_pedal = [0x22, 0x02, 0x46]; // two bytes
                byte[] obd_mode22_manifold_templ = [0x22, 0x02, 0x72];
                byte[] obd_mode22_octane1 = [0x22, 0x02, 0x18];
                byte[] obd_mode22_octane2 = [0x22, 0x02, 0x1B];
                byte[] obd_mode22_octane3 = [0x22, 0x02, 0x19];
                byte[] obd_mode22_octane4 = [0x22, 0x02, 0x1A];
                byte[] obd_mode22_octane5 = [0x22, 0x02, 0x4D];
                byte[] obd_mode22_octane6 = [0x22, 0x02, 0x4E];

                List<byte[]> obd_mode22_octane_requests = new()
                {
                        obd_mode22_octane1,
                        obd_mode22_octane2,
                        obd_mode22_octane3,
                        obd_mode22_octane4,
                        obd_mode22_octane5,
                        obd_mode22_octane6
                    };

                byte[] obd_basic_message = ecm_obd_head.Concat(obd_basic_request).ToArray();
                byte[] obd_secondary_message = ecm_obd_head.Concat(obd_secondary_request).ToArray();
                byte[] obd_pedal_pos_message = ecm_obd_head.Concat(obd_mode22_accel_pedal).ToArray();
                byte[] obd_manifold_temp_message = ecm_obd_head.Concat(obd_mode22_manifold_templ).ToArray();
                byte[][] obd_mode22_octane_messages = obd_mode22_octane_requests.Select(req => ecm_obd_head.Concat(req).ToArray()).ToArray();

                using (var writer = new CSVWriter(output_filename))
                {
                    uint ui_update_counter = 0;
                    while (terminate == false)
                    {

                        List<LiveDataReading> readings = new List<LiveDataReading>();

                        try
                        {
                            Channel.SendMessages(obd_mode22_octane_messages);
                            readings.AddRange(ReadPendingMessages(Channel));
                            Channel.SendMessage(obd_pedal_pos_message);
                            readings.AddRange(ReadPendingMessages(Channel));
                            Channel.SendMessage(obd_manifold_temp_message);
                            readings.AddRange(ReadPendingMessages(Channel));
                            Channel.SendMessage(obd_secondary_message);
                            readings.AddRange(ReadPendingMessages(Channel));
                            Channel.SendMessage(obd_basic_message);
                            readings.AddRange(ReadPendingMessages(Channel));
                        }
                        catch (J2534Exception ex | TimeoutException ex)
                        {
                            // Log specific J2534 communication errors but continue trying
                            Debug.WriteLine($"J2534 Communication Error: {ex.Message}");
                            // Skip this iteration but don't terminate the logger
                            continue;
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

            if (device != null)
            {
                device.Dispose();
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