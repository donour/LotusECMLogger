using SAE.J2534;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace LotusECMLogger
{
    internal class J2534OBDLogger
    {
        private String output_filename;
        private bool terminate = false;
        private Thread loggerThread;

        public J2534OBDLogger(String filename)
        {
            this.output_filename = filename;
        }

        public String getOutputFilename()
        {
            return this.output_filename;
        }

        public void stop()
        {
            terminate = true;
            if (loggerThread != null && loggerThread.IsAlive)
            {
                loggerThread.Join(); // wait for the thread to finish
            }
        }

        public void start()
        {
            loggerThread = new Thread(test);
            loggerThread.IsBackground = true;
            loggerThread.Start();
        }

        private void test()
        {
            while (true)
            {
                Thread.Sleep(100);
                if (terminate)
                {
                    return;
                }
            }
        }
        private void runLogger()
        {
  
            string DllFileName = APIFactory.GetAPIinfo().First().Filename;
            using (API API = APIFactory.GetAPI(DllFileName))
            using (Device Device = API.GetDevice())
            using (Channel Channel = Device.GetChannel(Protocol.ISO15765, Baud.ISO15765, ConnectFlag.NONE))
            {
                MessageFilter FlowControlFilter = new MessageFilter()
                {
                    FilterType = Filter.FLOW_CONTROL_FILTER,
                    Mask = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF },
                    Pattern = new byte[] { 0x00, 0x00, 0x07, 0xE8 },
                    FlowControl = new byte[] { 0x00, 0x00, 0x07, 0xE0 }
                };
                Channel.StartMsgFilter(FlowControlFilter);


                byte[] ecm_obd_head = [0x00, 0x00, 0x07, 0xE0];
                //engine speed, tps,  timing
                byte[] obd_basic_request = [0x01,
                        0x0C, // engine speed
                        0x11, // throttle position
                        0x0E, // timing advance
                        ];
                byte[] obd_secondary_request = [0x01,
                        0x05, // coolant temperature
                        0x43, // absolute load
                        0x0B, // intake manifold absolute pressure
                        ];

                byte[] obd_coolant_request = [0x01, 0x05];
                byte[] obd_mode22_sport_button = [0x22, 0x02, 0x5D];
                byte[] obd_mode22_accel_pedal = [0x22, 0x02, 0x45]; // two bytes
                byte[] obd_mode22_tps = [0x22, 0x02, 0x46]; // two bytes
                byte[] obd_mode22_octane1 = [0x22, 0x02, 0x18];
                byte[] obd_mode22_octane2 = [0x22, 0x02, 0x1B];
                byte[] obd_mode22_octane3 = [0x22, 0x02, 0x19];
                byte[] obd_mode22_octane4 = [0x22, 0x02, 0x1A];
                byte[] obd_mode22_octane5 = [0x22, 0x02, 0x4D];
                byte[] obd_mode22_octane6 = [0x22, 0x02, 0x4E];

                List<byte[]> obd_mode22_octane_requests = new List<byte[]>
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
                // create a message for each obd_mode22_octane_requests
                byte[][] obd_mode22_octane_messages = obd_mode22_octane_requests.Select(req => ecm_obd_head.Concat(req).ToArray()).ToArray();


                DateTime start = DateTime.Now;

                var writer = new CSVWriter(output_filename);
                while (true)
                {
                    List<LiveDataReading> readings = new List<LiveDataReading>();
                    var tr = new LiveDataReading
                    {
                        name = "time (s)",
                        value_f = DateTime.Now.TimeOfDay.TotalSeconds
                    };
                    readings.Add(tr);

                    Channel.SendMessages(obd_mode22_octane_messages);
                    readings.AddRange(readPendingMessages(Channel));
                    Channel.SendMessage(obd_secondary_message);
                    readings.AddRange(readPendingMessages(Channel));
                    Channel.SendMessage(obd_basic_message);
                    readings.AddRange(readPendingMessages(Channel));

                    // output range of readings per second
                    Debug.WriteLine(readings.Count / (DateTime.Now - start).TotalSeconds + " hz");
                    start = DateTime.Now;

                    writer.WriteLine(readings);
                }
            }
  
        }

        private List<LiveDataReading> readPendingMessages(Channel Channel)
        {
            List<LiveDataReading> readings = new List<LiveDataReading>();

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
            //foreach (LiveDataReading reading in readings)
            //{
            //    Debug.WriteLine(reading.ToString());
            //}
            return readings;
        }
    }
}