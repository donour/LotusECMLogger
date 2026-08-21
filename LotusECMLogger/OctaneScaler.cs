namespace LotusECMLogger
{
    /// <summary>
    /// The ECU's learned per-cylinder octane scaler, published over Mode 22 as PIDs 0x0218-0x021B,
    /// 0x024D and 0x024E. Both the live-data decoder and the vehicle information reader query these,
    /// and they previously disagreed about what the bytes mean, so the two firmware facts involved
    /// live here instead of in each caller.
    /// </summary>
    /// <remarks>
    /// Verified against the decompiled T6e firmware (C132E0278):
    /// <list type="bullet">
    /// <item><description>
    /// <c>obd_ii_mode22_processing</c> packs <c>LEA_octane_scaler[0..5]</c> for those PIDs in order,
    /// and the array is cylinder-indexed: the same index feeds <c>LEA_ign_knock_retard[0]</c> into
    /// <c>ign_adv_base_cyl1</c>, so slot 0 is cylinder 1.
    /// </description></item>
    /// <item><description>
    /// The knock path consumes the value as <c>(x * (s &gt;&gt; 8)) &gt;&gt; 8</c>, i.e. <c>x * s / 65536</c>.
    /// It is a Q16 fraction, so full scale is 65536 rather than 65535.
    /// </description></item>
    /// </list>
    /// <para>
    /// Note that this ordering is not shared by the misfire counters, whose PIDs 0x0234-0x0237 map to
    /// <c>misfires_per_cyl[0]</c>, <c>[2]</c>, <c>[3]</c>, <c>[1]</c> - cylinders 1, 3, 4, 2, the
    /// four-cylinder firing order. Assuming the octane PIDs were permuted the same way is exactly the
    /// error this type exists to prevent.
    /// </para>
    /// </remarks>
    internal static class OctaneScaler
    {
        /// <summary>
        /// Mode 22 PID low byte to cylinder number; the high byte is 0x02 for all of them.
        /// </summary>
        public static readonly IReadOnlyDictionary<byte, int> CylinderByPid = new Dictionary<byte, int>
        {
            [0x18] = 1,
            [0x19] = 2,
            [0x1A] = 3,
            [0x1B] = 4,
            [0x4D] = 5,
            [0x4E] = 6,
        };

        /// <summary>Converts the raw 16-bit Q16 scaler to a percentage.</summary>
        public static double ToPercent(int raw) => raw * 100.0 / 65536.0;
    }
}
