using SAE.J2534;
using System.Diagnostics;
using System.Text;

namespace LotusECMLogger.Services
{
    /// <summary>Reads the Evora engine ECU's persistent Mode 22 0x03xx performance history.</summary>
    public sealed class J2534PerformanceHistoryService : IPerformanceHistoryService
    {
        private static readonly ushort[] SupportPages = [0x0300, 0x0320, 0x0340, 0x0360];
        private static readonly IReadOnlyDictionary<ushort, int> DataPids = BuildDataPidMap();

        public PerformanceHistorySnapshot LoadPerformanceHistory()
        {
            using var session = J2534Session.Open();
            J2534Channel channel = session.OpenIso15765();
            channel.StartMessageFilter(ECUDefinition.ECM.CreateFlowControlFilter()).ThrowIfError();

            string calibrationId = ReadCalibrationId(channel);
            var supportedPids = new HashSet<ushort>();
            var availableSupportPages = new HashSet<ushort>();

            foreach (ushort page in SupportPages)
            {
                byte[]? bitmap = ReadMode22Payload(channel, page, 4);
                if (bitmap == null)
                    continue;

                availableSupportPages.Add(page);
                supportedPids.UnionWith(PerformanceHistoryDecoder.DecodeSupportedPids(page, bitmap));
            }

            var payloads = new Dictionary<ushort, byte[]>();
            foreach ((ushort pid, int width) in DataPids)
            {
                ushort page = (ushort)(pid & 0xFFE0);
                if (availableSupportPages.Contains(page) && !supportedPids.Contains(pid))
                    continue;

                byte[]? payload = ReadMode22Payload(channel, pid, width);
                if (payload != null)
                    payloads[pid] = payload;
            }

            if (payloads.Count == 0)
                throw new IOException("The ECU did not return any Mode 22 0x03xx performance-history data.");

            return PerformanceHistoryDecoder.Decode(calibrationId, payloads);
        }

        private static string ReadCalibrationId(J2534Channel channel)
        {
            var identifiers = new List<string>();

            // Mode 09 PID 04 normally carries the Lotus part/calibration number used to select
            // a profile (for example C132E0278). Keep the Mode 22 program string as a second
            // source because some calibrations leave one of the two identifiers blank.
            byte[] mode09 = new Iso15765Service(channel)
                .GetPID(OBDIIMode.RequestVehicleInformation, 0x04);
            AddIdentifier(identifiers, mode09);

            ushort[] pids = [0x021C, 0x021D, 0x021E, 0x021F, 0x0221, 0x0222, 0x0223, 0x0224];
            var mode22 = new List<byte>(32);
            foreach (ushort pid in pids)
            {
                byte[]? chunk = ReadMode22Payload(channel, pid, 4);
                if (chunk != null)
                    mode22.AddRange(chunk);
            }
            AddIdentifier(identifiers, [.. mode22]);

            return string.Join(" / ", identifiers.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private static void AddIdentifier(ICollection<string> target, byte[] bytes)
        {
            if (bytes.Length == 0)
                return;

            string value = Encoding.ASCII.GetString(bytes)
                .TrimEnd('\0', '\xFF', ' ', '\r', '\n', '\t');
            if (!string.IsNullOrWhiteSpace(value))
                target.Add(value);
        }

        private static byte[]? ReadMode22Payload(J2534Channel channel, ushort pid, int payloadLength)
        {
            try
            {
                byte pidHigh = (byte)(pid >> 8);
                byte pidLow = (byte)pid;
                channel.SendMessage([0x00, 0x00, 0x07, 0xE0, 0x22, pidHigh, pidLow]);

                for (int retry = 0; retry < 8; retry++)
                {
                    GetMessagesResult response = channel.ReadMessages(1, 150);
                    if (response.Messages.Length == 0)
                        continue;

                    byte[] data = response.Messages[0].Data;
                    // Skip transmit echoes/confirmations and unrelated traffic. A positive response is
                    // [7E8 header] 62 <pid high> <pid low> <payload>.
                    if (data.Length >= 7 + payloadLength &&
                        data[2] == 0x07 && data[3] == 0xE8 &&
                        data[4] == 0x62 && data[5] == pidHigh && data[6] == pidLow)
                        return data[7..(7 + payloadLength)];

                    // A matching negative response means retrying the same unsupported PID is pointless.
                    if (data.Length >= 7 && data[2] == 0x07 && data[3] == 0xE8 &&
                        data[4] == 0x7F && data[5] == 0x22)
                        return null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to read performance-history PID 0x{pid:X4}: {ex.Message}");
            }

            return null;
        }

        private static IReadOnlyDictionary<ushort, int> BuildDataPidMap()
        {
            var result = new SortedDictionary<ushort, int>();

            AddRange(result, 0x0301, 0x0318, 4);
            AddRange(result, 0x031A, 0x031F, 4);
            AddRange(result, 0x0321, 0x0323, 4);
            foreach (ushort pid in new ushort[] { 0x0324, 0x0326, 0x0328, 0x032A, 0x032C })
                result[pid] = 4; // Coolant bytes are zero-extended to four bytes by the firmware.
            foreach (ushort pid in new ushort[] { 0x0325, 0x0327, 0x0329, 0x032B, 0x032E })
                result[pid] = 4;
            AddRange(result, 0x032F, 0x0333, 2);
            AddRange(result, 0x0334, 0x0337, 1);
            result[0x0338] = 4;
            result[0x0339] = 2;
            result[0x033A] = 5; // S2 firmware prepends the request length to the distance value.
            AddRange(result, 0x033B, 0x033F, 4);
            result[0x0341] = 4;

            for (ushort first = 0x0342; first <= 0x0351; first += 0x000F)
            {
                // Three 12-byte records, each exposed as byte, byte, u16, i16, u32 PIDs.
                for (int record = 0; record < 3; record++)
                {
                    ushort pid = (ushort)(first + record * 5);
                    result[pid] = 1;
                    result[(ushort)(pid + 1)] = 1;
                    result[(ushort)(pid + 2)] = 2;
                    result[(ushort)(pid + 3)] = 2;
                    result[(ushort)(pid + 4)] = 4;
                }
            }

            result[0x0361] = 1;
            return result;
        }

        private static void AddRange(IDictionary<ushort, int> target, ushort first, ushort last, int width)
        {
            for (ushort pid = first; pid <= last; pid++)
                target[pid] = width;
        }
    }
}
