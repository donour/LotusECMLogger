using SAE.J2534;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// KWP2000 (ISO 14230) diagnostic client for the Bosch ESP8 ABS/ESP module over ISO-TP.
    /// The ABS uses CAN IDs 0x7E2 (request) / 0x7EA (response) — distinct from the engine ECU
    /// (0x7E0/0x7E8) and with a different SID table — so it needs its own flow-control filter
    /// and request header.
    ///
    /// <see cref="ReadModuleInfo"/> unlocks the module (SecurityAccess level 1, which the
    /// firmware only grants in the programming session) and reads its ECU identification record.
    /// It performs no write, clear, or routine services, so it cannot modify the module — the
    /// unlock here only grants read access.
    /// </summary>
    public sealed class J2534AbsService : IAbsService
    {
        private static readonly ECUDefinition Abs = ECUDefinition.ABS;

        // KWP2000 service IDs used here.
        private const byte SidStartDiagnosticSession = 0x10;
        private const byte SidReadEcuIdentification = 0x1A;
        private const byte SidSecurityAccess = 0x27;

        // A positive KWP response echoes the request SID with bit 6 set (SID | 0x40).
        private const byte PositiveResponseFlag = 0x40;

        private const byte DiagnosticSession89 = 0x89; // session byte the reference tester uses
        private const byte IdentificationRecordAll = 0x87;

        // SecurityAccess sub-functions and seed size.
        private const byte SecurityRequestSeed = 0x01;
        private const byte SecuritySendKey = 0x02;
        private const int SeedLength = 4;

        private const byte NrcResponsePending = 0x78;
        private const byte NrcSecurityAccessDenied = 0x33;
        private const byte NegativeResponseSid = 0x7F;

        // SecurityAccess key derivation: key[i] = SBOX[seed[i]]. This 256-byte substitution
        // table is transcribed from the ESP8 firmware (flash 0xB8530); see CAN_DIAGNOSTICS_GUIDE.md.
        // Verified properties: SBOX[0]=0, full permutation, XOR-linear.
        private static readonly byte[] SBox =
        [
            0x00, 0x1D, 0x3A, 0x27, 0x74, 0x69, 0x4E, 0x53, 0xE8, 0xF5, 0xD2, 0xCF, 0x9C, 0x81, 0xA6, 0xBB,
            0xCD, 0xD0, 0xF7, 0xEA, 0xB9, 0xA4, 0x83, 0x9E, 0x25, 0x38, 0x1F, 0x02, 0x51, 0x4C, 0x6B, 0x76,
            0x87, 0x9A, 0xBD, 0xA0, 0xF3, 0xEE, 0xC9, 0xD4, 0x6F, 0x72, 0x55, 0x48, 0x1B, 0x06, 0x21, 0x3C,
            0x4A, 0x57, 0x70, 0x6D, 0x3E, 0x23, 0x04, 0x19, 0xA2, 0xBF, 0x98, 0x85, 0xD6, 0xCB, 0xEC, 0xF1,
            0x13, 0x0E, 0x29, 0x34, 0x67, 0x7A, 0x5D, 0x40, 0xFB, 0xE6, 0xC1, 0xDC, 0x8F, 0x92, 0xB5, 0xA8,
            0xDE, 0xC3, 0xE4, 0xF9, 0xAA, 0xB7, 0x90, 0x8D, 0x36, 0x2B, 0x0C, 0x11, 0x42, 0x5F, 0x78, 0x65,
            0x94, 0x89, 0xAE, 0xB3, 0xE0, 0xFD, 0xDA, 0xC7, 0x7C, 0x61, 0x46, 0x5B, 0x08, 0x15, 0x32, 0x2F,
            0x59, 0x44, 0x63, 0x7E, 0x2D, 0x30, 0x17, 0x0A, 0xB1, 0xAC, 0x8B, 0x96, 0xC5, 0xD8, 0xFF, 0xE2,
            0x26, 0x3B, 0x1C, 0x01, 0x52, 0x4F, 0x68, 0x75, 0xCE, 0xD3, 0xF4, 0xE9, 0xBA, 0xA7, 0x80, 0x9D,
            0xEB, 0xF6, 0xD1, 0xCC, 0x9F, 0x82, 0xA5, 0xB8, 0x03, 0x1E, 0x39, 0x24, 0x77, 0x6A, 0x4D, 0x50,
            0xA1, 0xBC, 0x9B, 0x86, 0xD5, 0xC8, 0xEF, 0xF2, 0x49, 0x54, 0x73, 0x6E, 0x3D, 0x20, 0x07, 0x1A,
            0x6C, 0x71, 0x56, 0x4B, 0x18, 0x05, 0x22, 0x3F, 0x84, 0x99, 0xBE, 0xA3, 0xF0, 0xED, 0xCA, 0xD7,
            0x35, 0x28, 0x0F, 0x12, 0x41, 0x5C, 0x7B, 0x66, 0xDD, 0xC0, 0xE7, 0xFA, 0xA9, 0xB4, 0x93, 0x8E,
            0xF8, 0xE5, 0xC2, 0xDF, 0x8C, 0x91, 0xB6, 0xAB, 0x10, 0x0D, 0x2A, 0x37, 0x64, 0x79, 0x5E, 0x43,
            0xB2, 0xAF, 0x88, 0x95, 0xC6, 0xDB, 0xFC, 0xE1, 0x5A, 0x47, 0x60, 0x7D, 0x2E, 0x33, 0x14, 0x09,
            0x7F, 0x62, 0x45, 0x58, 0x0B, 0x16, 0x31, 0x2C, 0x97, 0x8A, 0xAD, 0xB0, 0xE3, 0xFE, 0xD9, 0xC4,
        ];

        public (bool success, string errorMessage, AbsModuleInfo result) ReadModuleInfo(IProgress<string>? progress)
        {
            try
            {
                using var session = J2534Session.Open();
                J2534Channel channel = session.OpenIso15765();
                channel.StartMessageFilter(Abs.CreateFlowControlFilter()).ThrowIfError();

                var rows = new List<KeyValuePair<string, string>>();

                // Enter the tester's diagnostic session (0x89) — no SecurityAccess, no writes.
                var sess = Request(channel, [SidStartDiagnosticSession, DiagnosticSession89]);
                rows.Add(new KeyValuePair<string, string>("Session 10 89", sess.ok ? "accepted" : sess.error));

                // Identification records (ReadEcuIdentification 1A 80-9F). No unlock needed; the
                // module NRC-rejects unknown records quickly, so the scan is fast.
                progress?.Report("Reading identification records (1A 80-9F)…");
                int idFound = 0;
                for (byte record = 0x80; record <= 0x9F; record++)
                {
                    var r = Request(channel, [SidReadEcuIdentification, record]);
                    if (r.ok && r.payload.Length > 1)
                    {
                        rows.Add(new KeyValuePair<string, string>(IdentificationLabel(record), FormatData(r.payload[1..])));
                        idFound++;
                    }
                }

                // Coding / configuration records (ReadDataByLocalId 21 00-FF, 1-byte local id). The
                // guide's F1 90 / F1 91 were wrong (2-byte); scan the real 1-byte id space instead.
                progress?.Report("Scanning coding records (21 00-FF)…");
                int codeFound = 0;
                for (int lid = 0x00; lid <= 0xFF; lid++)
                {
                    if ((lid & 0x1F) == 0)
                        progress?.Report($"Scanning coding records… 0x{lid:X2}/0xFF");

                    var r = Request(channel, [0x21, (byte)lid]);
                    if (r.ok && r.payload.Length > 1)
                    {
                        rows.Add(new KeyValuePair<string, string>($"Coding 21 {lid:X2}", FormatData(r.payload[1..])));
                        codeFound++;
                    }
                }

                rows.Add(new KeyValuePair<string, string>("Scan summary",
                    $"{idFound} identification + {codeFound} coding record(s) — no unlock needed"));

                return (true, "", new AbsModuleInfo { Fields = rows });
            }
            catch (Exception ex)
            {
                return (false, ex.Message, AbsModuleInfo.Empty);
            }
        }

        // Friendly names for the ReadEcuIdentification records seen on the ESP8. Traceable via the
        // record number and inferred from the data content, so easy to correct against a real tester.
        private static string IdentificationLabel(byte record) => record switch
        {
            0x85 => "Serial number (1A 85)",
            0x86 => "Lotus part number (1A 86)",
            0x93 => "Bosch part number (1A 93)",
            0x9C => "Config byte (1A 9C)",
            _ => $"ECU Id 1A {record:X2}",
        };

        // Formats read data as hex, adding a printable-ASCII rendering when it looks like text, and
        // truncating long blocks (e.g. a calibration dump) so a single row stays readable.
        private static string FormatData(byte[] data)
        {
            const int max = 24;
            byte[] shown = data.Length > max ? data[..max] : data;
            string hex = BitConverter.ToString(shown) + (data.Length > max ? $" … ({data.Length} bytes)" : "");
            string ascii = ToPrintable(shown);
            return ascii.Trim('.').Length >= 3 ? $"{hex}  \"{ascii}\"" : hex;
        }

        private static string ToPrintable(byte[] data) =>
            new string(data.Select(b => b >= 0x20 && b < 0x7F ? (char)b : '.').ToArray());

        public (bool success, string errorMessage, AbsDtcResult result) ReadDtcs()
        {
            try
            {
                using var session = J2534Session.Open();
                J2534Channel channel = session.OpenIso15765();
                channel.StartMessageFilter(Abs.CreateFlowControlFilter()).ThrowIfError();

                // ReadDtcByStatus: report all DTCs by status mask (18 00 FF 00) — the exact request
                // the reference tester used, which the ABS answers without any session or unlock.
                var result = Request(channel, [0x18, 0x00, 0xFF, 0x00]);
                if (!result.ok)
                    return (false, $"Failed to read ABS DTCs: {result.error}", AbsDtcResult.Empty);

                return (true, "", AbsDtcResult.FromResponse(result.payload));
            }
            catch (Exception ex)
            {
                return (false, ex.Message, AbsDtcResult.Empty);
            }
        }

        public (bool success, string errorMessage, AbsProbeResult result) ProbeConnection()
        {
            try
            {
                using var session = J2534Session.Open();
                J2534Channel channel = session.OpenCan();

                // Pass-all filter so we can learn the broadcast baseline and catch a reply on any id.
                channel.StartMessageFilter(new MessageFilter
                {
                    FilterType = Filter.PASS_FILTER,
                    Mask = [0x00, 0x00, 0x00, 0x00],
                    Pattern = [0x00, 0x00, 0x00, 0x00],
                }).ThrowIfError();

                var rows = new List<KeyValuePair<string, string>>();

                // Baseline — learn the periodic broadcast ids so request-triggered replies stand
                // out. Also confirms the channel receives at all.
                var baseline = new HashSet<uint>(Listen(channel, 1200).Keys);
                rows.Add(new KeyValuePair<string, string>("Bus",
                    baseline.Count == 0
                        ? "SILENT — no CAN traffic received at all"
                        : $"alive — {baseline.Count} broadcast id(s): {SampleIds(baseline)}"));

                // Named control probes — always reported, even when silent.
                void Named(string label, uint reqId, byte[] payload)
                {
                    var hits = Probe(channel, reqId, payload, baseline, 250);
                    if (hits.Count == 0)
                        rows.Add(new KeyValuePair<string, string>(label, "no diagnostic reply"));
                    else
                        rows.AddRange(hits.Select(h => new KeyValuePair<string, string>(label, h)));
                }

                // ECM control proves the request/response path and that the bus is awake.
                Named("ECM control (0x7E0, 01 00)", 0x7E0, [0x01, 0x00]);

                // Shared-mailbox hypothesis: the ABS may share the ECU's 0x7E0 request id (its real
                // diagnostic id is set by the ERCOSEK COM config, not visible in firmware). Send
                // requests the ABS supports to 0x7E0 and capture every reply. 1A 87 discriminates:
                // the ABS answers 5A 87 or 7F 1A 33 (security), the ECU answers 7F 1A 11 or ignores.
                // 10 02 then 1A 87 also covers modules that only answer after a session is opened.
                Named("0x7E0 TesterPresent (3E 00)", 0x7E0, [0x3E, 0x00]);
                Named("0x7E0 ReadEcuId (1A 87)", 0x7E0, [0x1A, 0x87]);
                Named("0x7E0 ProgSession (10 02)", 0x7E0, [0x10, 0x02]);
                Named("0x7E0 ReadEcuId in-session (1A 87)", 0x7E0, [0x1A, 0x87]);

                // Functional broadcast — every module must listen on 0x7DF.
                Named("Functional (0x7DF, 3E 00)", 0x7DF, [0x3E, 0x00]);
                Named("Functional (0x7DF, 01 00)", 0x7DF, [0x01, 0x00]);

                // Physical scan — StartDiagnosticSession(default) to every 8th id across the whole
                // 11-bit diagnostic range (0x600-0x7F8), skipping the ECM and any id a node already
                // broadcasts on (to avoid an arbitration clash). Any NEW responder on ANY id is
                // reported. 10 01 selects the DEFAULT (normal) session, so it changes nothing.
                int responders = 0;
                int scanned = 0;
                for (uint reqId = 0x600; reqId <= 0x7F8; reqId += 0x08)
                {
                    if (reqId == 0x7E0 || baseline.Contains(reqId))
                        continue;
                    scanned++;
                    foreach (string hit in Probe(channel, reqId, [0x10, 0x01], baseline, 80))
                    {
                        rows.Add(new KeyValuePair<string, string>($"Scan 0x{reqId:X3}", hit));
                        responders++;
                    }
                }
                rows.Add(new KeyValuePair<string, string>("Scan",
                    $"swept {scanned} ids (0x600-0x7F8, step 8) with 10 01 — {responders} responder(s)"));

                return (true, "", new AbsProbeResult { Rows = rows });
            }
            catch (Exception ex)
            {
                return (false, ex.Message, AbsProbeResult.Empty);
            }
        }

        /// <summary>
        /// Sends a single-frame request to <paramref name="requestId"/> and returns a description of
        /// every distinct reply (any id) that was NOT already broadcasting in the baseline (empty
        /// when silent). Distinct id+payload pairs are kept, so two modules answering on one id — or
        /// a reply on a second id — are both surfaced. The payloads used here are read-only: OBD
        /// Mode 01, TesterPresent, ReadEcuIdentification, and StartDiagnosticSession (which changes
        /// only volatile session state, never module data).
        /// </summary>
        private static List<string> Probe(
            J2534Channel channel, uint requestId, byte[] payload, HashSet<uint> baseline, int listenMs)
        {
            // Raw ISO-TP single frame: [4-byte CAN id][PCI = payload length][payload] padded to 8.
            byte[] frame = new byte[12];
            frame[0] = (byte)((requestId >> 24) & 0xFF);
            frame[1] = (byte)((requestId >> 16) & 0xFF);
            frame[2] = (byte)((requestId >> 8) & 0xFF);
            frame[3] = (byte)(requestId & 0xFF);
            frame[4] = (byte)payload.Length;
            Array.Copy(payload, 0, frame, 5, payload.Length);
            channel.SendMessage(frame);

            var results = new List<string>();
            var seen = new HashSet<string>();
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(listenMs);
            while (DateTime.UtcNow < deadline)
            {
                var read = channel.ReadMessages(64, 25);
                foreach (var msg in read.Messages)
                {
                    byte[] data = msg.Data;
                    if (data is null || data.Length < 5)
                        continue;

                    uint id = (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);
                    // Capture a reply on ANY new id — the ABS may respond outside 0x7E8-0x7EF.
                    if (id == requestId || baseline.Contains(id))
                        continue;

                    string desc = $"0x{id:X3}{ModuleName(id)} → {BitConverter.ToString(data, 4)}";
                    if (seen.Add(desc))
                        results.Add(desc);
                }
            }

            return results;
        }

        /// <summary>
        /// Listens for <paramref name="durationMs"/> ms and returns the latest payload seen for
        /// every distinct CAN id (id -> hex of the bytes after the 4-byte id).
        /// </summary>
        private static Dictionary<uint, string> Listen(J2534Channel channel, int durationMs)
        {
            var seen = new Dictionary<uint, string>();
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(durationMs);

            while (DateTime.UtcNow < deadline)
            {
                var result = channel.ReadMessages(64, 25);
                foreach (var msg in result.Messages)
                {
                    byte[] data = msg.Data;
                    if (data is null || data.Length < 4)
                        continue;

                    uint id = (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);
                    seen[id] = data.Length > 4 ? BitConverter.ToString(data, 4) : "(no data)";
                }
            }

            return seen;
        }

        private static string SampleIds(HashSet<uint> ids)
        {
            var sample = ids.OrderBy(x => x).Take(8).Select(x => $"0x{x:X3}");
            string text = string.Join(", ", sample);
            return ids.Count > 8 ? text + ", …" : text;
        }

        private static string ModuleName(uint id) => id switch
        {
            0x7E8 => " (ECM)",
            0x7E9 => " (TCM)",
            0x7EA => " (ABS)",
            0x7EB => " (Body)",
            _ => "",
        };

        public (bool success, string errorMessage, AbsSniffResult result) SniffBus(
            int captureSeconds, IProgress<string>? progress)
        {
            try
            {
                using var session = J2534Session.Open();
                // CAN_ID_BOTH so we also capture 29-bit ids, in case the ABS uses extended addressing.
                J2534Channel channel = session.OpenChannel(Protocol.CAN, Baud.CAN, ConnectFlag.CAN_ID_BOTH);
                channel.StartMessageFilter(new MessageFilter
                {
                    FilterType = Filter.PASS_FILTER,
                    Mask = [0x00, 0x00, 0x00, 0x00],
                    Pattern = [0x00, 0x00, 0x00, 0x00],
                }).ThrowIfError();

                // Phase 1 — learn the periodic broadcast ids while the tester is idle.
                progress?.Report("Learning bus baseline (5s) — keep the reference tester idle…");
                var baseline = new HashSet<uint>();
                DateTime b0 = DateTime.UtcNow;
                while ((DateTime.UtcNow - b0).TotalSeconds < 5)
                    foreach (var m in channel.ReadMessages(64, 50).Messages)
                        if (m.Data is { Length: >= 4 })
                            baseline.Add(FrameId(m.Data));

                // Phase 2 — log every frame on an id that was NOT broadcasting in the baseline. The
                // tester↔ABS diagnostic exchange appears here because those ids are only active
                // while the tester is talking.
                progress?.Report($"Capturing {captureSeconds}s — run the reference tester's ABS read NOW…");
                var frames = new List<string>();
                var counts = new SortedDictionary<uint, int>();
                DateTime start = DateTime.UtcNow;
                while ((DateTime.UtcNow - start).TotalSeconds < captureSeconds && frames.Count < 20000)
                {
                    foreach (var m in channel.ReadMessages(64, 50).Messages)
                    {
                        byte[] data = m.Data;
                        if (data is null || data.Length < 4)
                            continue;

                        uint id = FrameId(data);
                        if (baseline.Contains(id))
                            continue;

                        double ms = (DateTime.UtcNow - start).TotalMilliseconds;
                        string payload = data.Length > 4 ? BitConverter.ToString(data, 4) : "";
                        frames.Add($"{ms,8:F0} ms  0x{id:X3}  {payload}");
                        counts[id] = counts.GetValueOrDefault(id) + 1;
                    }
                }

                progress?.Report("Sniff complete");
                return (true, "", new AbsSniffResult
                {
                    BaselineIdCount = baseline.Count,
                    NewIds = counts.Select(kv => $"0x{kv.Key:X3}{ModuleName(kv.Key)} — {kv.Value} frame(s)").ToList(),
                    Frames = frames,
                });
            }
            catch (Exception ex)
            {
                return (false, ex.Message, AbsSniffResult.Empty);
            }
        }

        private static uint FrameId(byte[] data) =>
            (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);

        /// <summary>
        /// Performs the SecurityAccess level 1 unlock: request the seed (0x27 0x01), derive the
        /// key from the SBOX, and send it (0x27 0x02). Must already be in the programming session.
        /// This unlocks read access only — no protected write is performed by this service.
        /// </summary>
        private static (bool ok, string error) SecurityAccess(J2534Channel channel)
        {
            var seedResult = Request(channel, [SidSecurityAccess, SecurityRequestSeed]);
            if (!seedResult.ok)
                return (false, $"SecurityAccess seed request failed: {seedResult.error}");

            // seedResult.payload = [0x01 (echoed sub-function), seed bytes...].
            if (seedResult.payload.Length < 1 + SeedLength)
                return (false, "SecurityAccess seed response was too short.");

            byte[] seed = seedResult.payload[1..(1 + SeedLength)];

            // Convention: an all-zero seed means the module is already unlocked — skip the key.
            if (Array.TrueForAll(seed, b => b == 0))
                return (true, "");

            byte[] key = ComputeKey(seed);

            byte[] keyPayload = new byte[2 + key.Length];
            keyPayload[0] = SidSecurityAccess;
            keyPayload[1] = SecuritySendKey;
            Array.Copy(key, 0, keyPayload, 2, key.Length);

            var keyResult = Request(channel, keyPayload);
            if (!keyResult.ok)
                return (false, $"SecurityAccess key rejected: {keyResult.error}");

            return (true, "");
        }

        private static byte[] ComputeKey(byte[] seed)
        {
            byte[] key = new byte[seed.Length];
            for (int i = 0; i < seed.Length; i++)
                key[i] = SBox[seed[i]];
            return key;
        }

        /// <summary>
        /// Sends a single-frame KWP2000 request to the ABS and waits for its response. The
        /// J2534 ISO15765 channel handles ISO-TP framing/reassembly, so this works for
        /// multi-frame responses too. NRC 0x78 (responsePending) is transparently awaited.
        /// </summary>
        /// <returns>
        /// <c>ok</c> with the response payload after the positive-response SID byte, or an
        /// error string plus the NRC (0 when none) on a negative response or timeout.
        /// </returns>
        private static (bool ok, string error, byte[] payload, byte nrc) Request(
            J2534Channel channel, byte[] kwpPayload)
        {
            byte[] header = Abs.GetRequestHeader();
            byte[] message = new byte[header.Length + kwpPayload.Length];
            Array.Copy(header, message, header.Length);
            Array.Copy(kwpPayload, 0, message, header.Length, kwpPayload.Length);
            channel.SendMessage(message);

            byte requestSid = kwpPayload[0];
            byte expectedResponseSid = (byte)(requestSid | PositiveResponseFlag);

            // The budget spans the responsePending window (P2*): repeated 250 ms reads.
            for (int attempt = 0; attempt < 20; attempt++)
            {
                var response = channel.ReadMessages(1, 250);
                if (response.Messages.Length == 0)
                    continue;

                byte[] data = response.Messages[0].Data;
                // Accept only frames addressed from the ABS response id; skip our own TX
                // echoes/confirmations and unrelated bus traffic.
                if (data.Length < 5 || !Abs.MatchesResponse(data))
                    continue;

                byte sid = data[4];
                if (sid == expectedResponseSid)
                    return (true, "", data[5..], 0);

                // Negative response: [header] 7F <requestSid> <nrc>
                if (sid == NegativeResponseSid && data.Length >= 7 && data[5] == requestSid)
                {
                    byte nrc = data[6];
                    if (nrc == NrcResponsePending)
                        continue; // module still working — keep waiting for the final response
                    return (false, $"NRC 0x{nrc:X2} ({NrcName(nrc)})", [], nrc);
                }
                // Some other frame from the ABS id — ignore and keep reading.
            }

            return (false, "No response from ABS module (timeout).", [], 0);
        }

        private static string NrcName(byte nrc) => nrc switch
        {
            0x10 => "generalReject",
            0x11 => "serviceNotSupported",
            0x12 => "subFunctionNotSupported",
            0x13 => "incorrectMessageLength",
            0x22 => "conditionsNotCorrect",
            0x24 => "requestSequenceError",
            0x31 => "requestOutOfRange",
            0x33 => "securityAccessDenied",
            0x34 => "requiredTimeDelayNotExpired",
            0x35 => "invalidKey",
            0x36 => "exceedNumberOfAttempts",
            0x78 => "responsePending",
            _ => "unknown",
        };
    }
}
