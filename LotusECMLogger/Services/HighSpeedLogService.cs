using System.Diagnostics;
using System.Globalization;
using System.Text;
using LotusECMLogger.Models;
using SAE.J2534;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// Implements the T6e high-speed CAN channel logger (developer telemetry facility).
    ///
    /// The PC configures the ECU as a programmable sampler by sending 8-byte command frames on
    /// CAN ID 0x350; the ECU then autonomously streams the selected memory locations back on 0x351.
    /// Protocol reverse-engineered in <c>ChannelLogger.md</c> and verified against the firmware
    /// dispatch <c>diag_comm_queue_mgmt_unknown</c> (C132E0278.c:46654). Configuration is RAM-only on
    /// the ECU and wiped on every boot, so the full open → define → arm sequence runs on each start.
    ///
    /// CAN frames go through J2534 raw-CAN, where the buffer is [00,00,IDhi,IDlo][payload…] — the same
    /// encoding used by <see cref="T6RMAService"/>.
    /// </summary>
    public sealed class HighSpeedLogService : IHighSpeedLogService
    {
        // Command (PC→ECU) and stream/response (ECU→PC) CAN IDs.
        private static readonly byte[] CommandIdHeader = [0x00, 0x00, 0x03, 0x50]; // 0x350
        private const uint StreamCanId = 0x351;

        // Command opcodes.
        private const byte CmdOpenSession = 0x01;
        private const byte CmdInitGroup = 0x14;
        private const byte CmdSelectSlot = 0x15;
        private const byte CmdSetChannel = 0x16;
        private const byte CmdConfigure = 0x06;
        private const byte CmdStartStopAll = 0x08;
        private const byte CmdEndSession = 0x07;
        private const byte CmdIdentify = 0x17;

        private const byte ResponseMarker = 0xFF; // first payload byte of a command reply
        private const int AckTimeoutMs = 400;
        private const int StreamReadTimeoutMs = 100;
        private const int CsvFlushEveryRows = 100;

        private readonly object _lock = new();

        private J2534Session? _session;
        private J2534Channel? _channel;
        private Thread? _loggingThread;
        private bool _isLogging;
        private byte _seq;

        private LoggingPlan? _plan;
        private List<string> _columnNames = [];
        private readonly Dictionary<string, double> _latest = new();
        private StreamWriter? _csvWriter;
        private string? _csvFilePath;
        private int _csvRowsSinceFlush;

        public event EventHandler<HighSpeedSampleEventArgs>? DataReceived;
        public event EventHandler<string>? ErrorOccurred;

        public bool IsLogging
        {
            get { lock (_lock) { return _isLogging; } }
        }

        public void StartLogging(IReadOnlyList<(HighSpeedChannel channel, int rateHz)> channels, string csvFilePath)
        {
            lock (_lock)
            {
                if (_isLogging)
                    throw new InvalidOperationException("High-speed logging is already active. Stop the current session first.");

                if (string.IsNullOrWhiteSpace(csvFilePath))
                    throw new ArgumentException("CSV file path cannot be empty", nameof(csvFilePath));

                // Build (and validate) the ECU channel program before touching hardware.
                _plan = HighSpeedLogPlanner.Plan(channels);

                // CSV columns = selected channels in selection order (de-duplicated by name).
                _columnNames = channels.Select(c => c.channel.Name).Distinct().ToList();
                _latest.Clear();
                _csvFilePath = csvFilePath;

                try
                {
                    InitializeDevice();
                    InitializeCsvFile(channels);

                    _isLogging = true;
                    _loggingThread = new Thread(LoggingThreadProc)
                    {
                        Name = "HighSpeedLog Thread",
                        IsBackground = true,
                    };
                    _loggingThread.Start();

                    Debug.WriteLine($"HighSpeedLog: started, {_plan.Groups.Count} group(s), {_columnNames.Count} channel(s)");
                }
                catch (Exception ex)
                {
                    CleanupResources();
                    throw new InvalidOperationException($"Failed to start high-speed logging: {ex.Message}", ex);
                }
            }
        }

        public void StopLogging()
        {
            Thread? thread;
            lock (_lock)
            {
                if (!_isLogging && _loggingThread == null)
                    return;
                _isLogging = false;
                thread = _loggingThread;
            }

            // Let the streaming thread exit (and send its 0x07 stop) before disposing the channel.
            // Guard against a self-join if Stop is ever invoked from the logging thread itself.
            if (thread != null && thread != Thread.CurrentThread)
                thread.Join(TimeSpan.FromSeconds(5));

            lock (_lock)
            {
                CleanupResources();
                Debug.WriteLine("HighSpeedLog: stopped");
            }
        }

        public void Dispose() => StopLogging();

        public HighSpeedIdentifyResult Identify()
        {
            lock (_lock)
            {
                if (_isLogging)
                    throw new InvalidOperationException("Stop high-speed logging before testing the connection.");
            }

            const int probeTimeoutMs = 400;
            J2534Session? tempSession = null;
            try
            {
                tempSession = J2534Session.Open();
                var channel = tempSession.OpenCan();
                channel.StartMessageFilter(StreamPassFilter()).ThrowIfError();

                // Open a session. This is always accepted, and a reply proves the 0x350 RX mailbox —
                // and therefore the diagnostic bus (CAL_ecu_flexcan_diag_bus_select ≠ 0) — is live.
                byte seq = 0;
                channel.SendMessage(BuildFrame(CmdOpenSession, seq, [0x00, 0x00]));
                if (ReadAck(channel, seq, probeTimeoutMs) == null)
                    return new HighSpeedIdentifyResult { SessionOpened = false };

                // Identify. Reply payload = [0xFF, status, echo, idLen, magic(4 bytes, big-endian)].
                seq++;
                channel.SendMessage(BuildFrame(CmdIdentify, seq, []));
                byte[]? id = ReadAck(channel, seq, probeTimeoutMs);

                // Best-effort: close the session we opened so we leave the ECU as we found it.
                seq++;
                try { channel.SendMessage(BuildFrame(CmdEndSession, seq, [0x01])); }
                catch (Exception ex) { Debug.WriteLine($"HighSpeedLog: identify cleanup failed: {ex.Message}"); }

                if (id == null)
                    return new HighSpeedIdentifyResult { SessionOpened = true, Identified = false };

                int idLen = id.Length > 7 ? id[7] : 0;
                uint magic = id.Length >= 12
                    ? (uint)((id[8] << 24) | (id[9] << 16) | (id[10] << 8) | id[11])
                    : 0;

                return new HighSpeedIdentifyResult
                {
                    SessionOpened = true,
                    Identified = true,
                    FirmwareIdLength = idLen,
                    Magic = magic,
                };
            }
            finally
            {
                tempSession?.Dispose();
            }
        }

        private void InitializeDevice()
        {
            _session = J2534Session.Open();
            _channel = _session.OpenCan(); // raw CAN @ 500 kbaud
            _channel.StartMessageFilter(StreamPassFilter()).ThrowIfError();
        }

        /// <summary>PASS filter that admits only 0x351 (the stream + command responses).</summary>
        private static MessageFilter StreamPassFilter() => new()
        {
            FilterType = Filter.PASS_FILTER,
            Mask = [0x00, 0x00, 0x07, 0xFF],
            Pattern = [0x00, 0x00, 0x03, 0x51],
        };

        private void InitializeCsvFile(IReadOnlyList<(HighSpeedChannel channel, int rateHz)> channels)
        {
            _csvWriter = new StreamWriter(_csvFilePath!, false, Encoding.UTF8) { AutoFlush = false };
            _csvRowsSinceFlush = 0;

            _csvWriter.WriteLine("# Lotus High-Speed CAN Channel Log");
            _csvWriter.WriteLine($"# Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            foreach (var (channel, rateHz) in channels)
                _csvWriter.WriteLine($"# {channel.Name}: 0x{channel.Address:X8} size={channel.Size} {rateHz}Hz unit={channel.Unit}");

            var header = new StringBuilder("Timestamp,RelativeTime_ms,Label");
            foreach (var name in _columnNames)
                header.Append(',').Append(EscapeCsv(name));
            _csvWriter.WriteLine(header.ToString());
            _csvWriter.Flush();
        }

        private void LoggingThreadProc()
        {
            try
            {
                ConfigureEcu();
                StreamLoop();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HighSpeedLog: thread error: {ex}");
                lock (_lock) { _isLogging = false; }
                ErrorOccurred?.Invoke(this, ex.Message);
            }
            finally
            {
                TrySendStop();
            }
        }

        /// <summary>Runs the open → init/define → arm command sequence against the ECU.</summary>
        private void ConfigureEcu()
        {
            // Open a fresh config session (also stops any prior logging on the ECU).
            SendAndExpectAck(CmdOpenSession, 0x00, 0x00);

            foreach (var group in _plan!.Groups)
            {
                SendAndExpectAck(CmdInitGroup, (byte)group.GroupIndex);

                foreach (var frame in group.Frames)
                {
                    for (int chan = 0; chan < frame.Channels.Count; chan++)
                    {
                        var ch = frame.Channels[chan].Channel;
                        SendAndExpectAck(CmdSelectSlot, (byte)group.GroupIndex, (byte)frame.FrameIndex, (byte)chan);
                        // [size, 0x00, addr big-endian]
                        SendAndExpectAck(CmdSetChannel,
                            ch.Size, 0x00,
                            (byte)(ch.Address >> 24), (byte)(ch.Address >> 16),
                            (byte)(ch.Address >> 8), (byte)ch.Address);
                    }
                }

                // Configure-only (mode 2): stage the group's timing without starting it yet.
                SendAndExpectAck(CmdConfigure,
                    0x02, (byte)group.GroupIndex, (byte)group.FrameCount, (byte)group.Slot,
                    (byte)(group.Divider >> 8), (byte)(group.Divider & 0xFF));
            }

            // Start all staged groups together.
            SendAndExpectAck(CmdStartStopAll, 0x01);
        }

        // The ECU stream is timestamped by the J2534 adapter (Message.Timestamp, microseconds). We anchor
        // wall-clock to the first frame and derive each row's time from the hardware timestamp, so frames
        // read together in one batch get distinct, accurate times instead of one shared processing time.
        private void StreamLoop()
        {
            DateTime wallAnchor = default;
            bool haveAnchor = false;
            uint rawT0 = 0, rawLast = 0;
            ulong wrapAccum = 0; // accumulated 2^32-µs rollovers (the counter wraps ~every 71.6 min)

            while (IsLogging)
            {
                var result = _channel!.ReadMessages(16, StreamReadTimeoutMs);
                foreach (var msg in result.Messages)
                {
                    if (msg.Data == null || msg.Data.Length < 5)
                        continue;

                    byte b0 = msg.Data[4];
                    if (b0 == ResponseMarker)
                        continue; // stray command response during streaming

                    // Advance the hardware-timestamp clock for every stream frame (handles rollover).
                    uint raw = msg.Timestamp;
                    if (!haveAnchor)
                    {
                        wallAnchor = DateTime.Now;
                        rawT0 = rawLast = raw;
                        haveAnchor = true;
                    }
                    else if (raw < rawLast)
                    {
                        wrapAccum += 0x1_0000_0000UL;
                    }
                    rawLast = raw;
                    ulong elapsedUs = wrapAccum + raw - rawT0;

                    byte label = (byte)(b0 & 0x7F);
                    bool overflow = (b0 & 0x80) != 0;
                    if (!_plan!.LayoutByLabel.TryGetValue(label, out var layout))
                        continue;

                    DateTime timestamp = wallAnchor.AddTicks((long)elapsedUs * TimeSpan.TicksPerMicrosecond);
                    DecodeFrame(msg.Data, label, overflow, layout, timestamp, elapsedUs);
                }
            }
        }

        private void DecodeFrame(byte[] data, byte label, bool overflow, IReadOnlyList<HighSpeedChannel> layout, DateTime timestamp, ulong elapsedUs)
        {
            ReadOnlySpan<byte> payload = data.AsSpan(5); // bytes after [header][label]

            var readings = new List<HighSpeedReading>(layout.Count);
            int offset = 0;
            foreach (var ch in layout)
            {
                if (offset >= payload.Length)
                    break;
                int len = Math.Min(ch.Size, payload.Length - offset);
                double value = ch.Decode(payload.Slice(offset, len));
                readings.Add(new HighSpeedReading { Name = ch.Name, Value = value, Unit = ch.Unit });
                _latest[ch.Name] = value;
                offset += ch.Size;
            }

            WriteCsvRow(timestamp, elapsedUs, label);

            DataReceived?.Invoke(this, new HighSpeedSampleEventArgs
            {
                Timestamp = timestamp,
                Label = label,
                Overflow = overflow,
                Readings = readings,
            });
        }

        private void WriteCsvRow(DateTime timestamp, ulong elapsedUs, byte label)
        {
            if (_csvWriter == null)
                return;

            var sb = new StringBuilder();
            sb.Append(timestamp.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture));
            sb.Append(',').Append((elapsedUs / 1000.0).ToString("F3", CultureInfo.InvariantCulture));
            sb.Append(',').Append(label);
            foreach (var name in _columnNames)
            {
                sb.Append(',');
                if (_latest.TryGetValue(name, out var v))
                    sb.Append(v.ToString("G", CultureInfo.InvariantCulture));
            }
            _csvWriter.WriteLine(sb.ToString());

            if (++_csvRowsSinceFlush >= CsvFlushEveryRows)
            {
                _csvWriter.Flush();
                _csvRowsSinceFlush = 0;
            }
        }

        /// <summary>Sends a command and waits for its matching ACK; throws on NAK or timeout.</summary>
        private void SendAndExpectAck(byte cmd, params byte[] parameters)
        {
            byte seq = _seq++;
            _channel!.SendMessage(BuildFrame(cmd, seq, parameters));

            byte? status = WaitForAck(seq, AckTimeoutMs);
            if (status == null)
                throw new TimeoutException($"No ECU response to command 0x{cmd:X2} (seq {seq}). Is the diag bus enabled?");
            if (status.Value != 0)
                throw new InvalidOperationException($"ECU rejected command 0x{cmd:X2} with status 0x{status.Value:X2}.");
        }

        private byte? WaitForAck(byte seq, int timeoutMs) => ReadAck(_channel!, seq, timeoutMs)?[5];

        /// <summary>
        /// Reads 0x351 until the ACK echoing <paramref name="seq"/> arrives; returns the full RX frame
        /// (<c>[00,00,03,51, 0xFF, status, echo, …]</c>) or null on timeout.
        /// </summary>
        private static byte[]? ReadAck(J2534Channel channel, byte seq, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                var res = channel.ReadMessages(8, 20);
                foreach (var m in res.Messages)
                {
                    // ACK payload = [0xFF, status, echo-seq, …] at data[4..]
                    if (m.Data is { Length: >= 7 } && m.Data[4] == ResponseMarker && m.Data[6] == seq)
                        return m.Data;
                }
            }
            return null;
        }

        private static byte[] BuildFrame(byte cmd, byte seq, byte[] parameters)
        {
            // [00,00,03,50][cmd][seq][params…]
            var frame = new byte[CommandIdHeader.Length + 2 + parameters.Length];
            CommandIdHeader.CopyTo(frame, 0);
            frame[4] = cmd;
            frame[5] = seq;
            parameters.CopyTo(frame, 6);
            return frame;
        }

        /// <summary>Best-effort stop: tell the ECU to stop all groups and end the session.</summary>
        private void TrySendStop()
        {
            try
            {
                if (_channel == null)
                    return;
                _channel.SendMessage(BuildFrame(CmdEndSession, _seq++, [0x01]));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HighSpeedLog: stop send failed: {ex.Message}");
            }
        }

        private void CleanupResources()
        {
            try
            {
                _csvWriter?.Flush();
                _csvWriter?.Dispose();
                _csvWriter = null;

                _session?.Dispose(); // disposes channel + device too
                _session = null;
                _channel = null;
                _loggingThread = null;
                _plan = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HighSpeedLog: cleanup error: {ex.Message}");
            }
        }

        private static string EscapeCsv(string s)
            => s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }
}
