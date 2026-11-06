using System.Text;

namespace LotusECMLogger.Services.Ccp;

public enum CodingCommandOutcome
{
    /// <summary>The firmware took the command: it cleared <c>coding_cmd</c> back to 0.</summary>
    Executed,
    /// <summary>
    /// The command was not taken within the timeout (the ECU is locked, or this is not its coding_cmd), so the byte
    /// was put back to 0.
    /// </summary>
    NotExecuted,
}

/// <summary>
/// The firmware's coding commands (e.g. 0x04 erase model info), sent over CCP. <c>service_coding_333ms</c> runs
/// whatever command is in the one-byte <c>coding_cmd</c> register and clears it, but only while
/// <c>ecu_unlocked</c> is set: that flag is computed at boot from the calibration's unlock field
/// (<c>CAL_ecu_unlock_magic</c> = "WTF?"), so no transport can bypass it. Writing over CCP needs no unlock of its
/// own, and unlike the old RMA write the result can be confirmed by reading the register back.
/// </summary>
public static class CcpCodingCommand
{
    /// <summary>The calibration unlock field's value on an unlocked ECU.</summary>
    public const string UnlockMagic = "WTF?";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Writes <paramref name="command"/> to <c>coding_cmd</c> and waits up to <paramref name="timeout"/> for the
    /// firmware to clear it (it runs every 333 ms). If it does not, the register is put back to 0 so the command
    /// cannot run later by surprise.
    /// </summary>
    public static async Task<CodingCommandOutcome> SendAsync(CcpMaster master, uint codingCmdAddress, byte command,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(master);
        if (command == 0) throw new ArgumentOutOfRangeException(nameof(command), "0 is no command.");
        var register = new CcpMemoryRange(codingCmdAddress, 1);
        try
        {
            await master.WriteVerifiedAsync(codingCmdAddress, new[] { command }, register, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CcpWriteVerificationException)
        {
            // The read-back already differs: the firmware may have taken the command in between. The poll below decides.
        }
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            byte now = (await master.ShortUploadAsync(codingCmdAddress, 1, cancellationToken).ConfigureAwait(false))[0];
            if (now == 0) return CodingCommandOutcome.Executed;
            if (DateTime.UtcNow >= deadline) break;
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
        await master.WriteVerifiedAsync(codingCmdAddress, new byte[] { 0 }, register, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return CodingCommandOutcome.NotExecuted;
    }

    /// <summary>The boot-time <c>ecu_unlocked</c> flag, read from its RAM address.</summary>
    public static async Task<bool> ReadUnlockedFlagAsync(CcpMaster master, uint ecuUnlockedAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(master);
        return (await master.ShortUploadAsync(ecuUnlockedAddress, 1, cancellationToken).ConfigureAwait(false))[0] != 0;
    }

    /// <summary>
    /// Whether the calibration's unlock field (in calibration RAM) holds <see cref="UnlockMagic"/>. The flag itself is
    /// set from this at boot, so this is what the flag will be unless calibration RAM was changed since.
    /// </summary>
    public static async Task<bool> ReadUnlockMagicAsync(CcpMaster master, uint unlockMagicAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(master);
        var bytes = await master.ShortUploadAsync(unlockMagicAddress, UnlockMagic.Length, cancellationToken).ConfigureAwait(false);
        return Encoding.ASCII.GetString(bytes) == UnlockMagic;
    }
}
