namespace BinaryFileMonitor;

/// <summary>
/// Provides data for the <see cref="BinaryFileMonitor.WordChanged"/> event.
/// </summary>
public class WordChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the byte offset (address) where the 32-bit word starts, relative to the start of the file.
    /// This offset is always 4-byte aligned (0, 4, 8, 12, etc.).
    /// </summary>
    public int ByteOffset { get; }

    /// <summary>
    /// Gets the word index (offset / 4) of the changed word.
    /// </summary>
    public int WordIndex => ByteOffset / 4;

    /// <summary>
    /// Gets the previous 32-bit value before the change.
    /// </summary>
    public uint OldValue { get; }

    /// <summary>
    /// Gets the new 32-bit value after the change.
    /// </summary>
    public uint NewValue { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="WordChangedEventArgs"/> class.
    /// </summary>
    /// <param name="byteOffset">The byte offset of the changed word (must be 4-byte aligned).</param>
    /// <param name="oldValue">The previous 32-bit word value.</param>
    /// <param name="newValue">The new 32-bit word value.</param>
    public WordChangedEventArgs(int byteOffset, uint oldValue, uint newValue)
    {
        ByteOffset = byteOffset;
        OldValue = oldValue;
        NewValue = newValue;
    }

    /// <summary>
    /// Returns a string representation of the word change.
    /// </summary>
    /// <returns>A formatted string showing the offset and value change.</returns>
    public override string ToString()
    {
        return $"Word[{WordIndex}] @ 0x{ByteOffset:X4}: 0x{OldValue:X8} -> 0x{NewValue:X8}";
    }
}
