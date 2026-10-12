using LotusECMLogger.Models;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// The live-validated logger's open → init/define → arm command sequence, moved verbatim out of
    /// <see cref="HighSpeedLogService"/> so its frames can be frozen as test fixtures before the logger is
    /// ported onto CcpMaster. Each entry is (opcode, parameters); the sender inserts the sequence byte.
    /// Temporary: removed once the port's regression test compares against the fixtures instead.
    /// </summary>
    public static class HighSpeedLogLegacyCommands
    {
        public const byte CmdOpenSession = 0x01;
        public const byte CmdInitGroup = 0x14;
        public const byte CmdSelectSlot = 0x15;
        public const byte CmdSetChannel = 0x16;
        public const byte CmdConfigure = 0x06;
        public const byte CmdStartStopAll = 0x08;

        public static IEnumerable<(byte Command, byte[] Parameters)> Configure(LoggingPlan plan)
        {
            // Open a fresh config session (also stops any prior logging on the ECU).
            yield return (CmdOpenSession, [0x00, 0x00]);

            foreach (var group in plan.Groups)
            {
                yield return (CmdInitGroup, [(byte)group.GroupIndex]);

                foreach (var frame in group.Frames)
                {
                    for (int chan = 0; chan < frame.Channels.Count; chan++)
                    {
                        var ch = frame.Channels[chan].Channel;
                        yield return (CmdSelectSlot, [(byte)group.GroupIndex, (byte)frame.FrameIndex, (byte)chan]);
                        // [size, 0x00, addr big-endian]
                        yield return (CmdSetChannel,
                        [
                            ch.Size, 0x00,
                            (byte)(ch.Address >> 24), (byte)(ch.Address >> 16),
                            (byte)(ch.Address >> 8), (byte)ch.Address,
                        ]);
                    }
                }

                // Configure-only (mode 2): stage the group's timing without starting it yet.
                yield return (CmdConfigure,
                [
                    0x02, (byte)group.GroupIndex, (byte)group.FrameCount, (byte)group.Slot,
                    (byte)(group.Divider >> 8), (byte)(group.Divider & 0xFF),
                ]);
            }

            // Start all staged groups together.
            yield return (CmdStartStopAll, [0x01]);
        }
    }
}
