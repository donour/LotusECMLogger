using SAE.J2534;
using Message = SAE.J2534.Message;

namespace LotusECMLogger.Services;

internal sealed partial class AbsPressurePoller
{
    internal AbsPressurePoller(J2534Channel channel, bool raw, Func<bool> stopping)
        : this(frame => channel.SendMessage(frame, raw ? channel.DefaultTxFlags :
            channel.DefaultTxFlags | TxFlag.ISO15765_FRAME_PAD).ThrowIfError(), raw, stopping: stopping)
    {
        if (raw)
        {
            foreach (uint id in new[] { AbsPressureProtocol.RequestId, AbsPressureProtocol.ResponseId })
                channel.StartMessageFilter(new MessageFilter { FilterType = Filter.PASS_FILTER,
                    Mask = [0xFF, 0xFF, 0xFF, 0xFF], Pattern = Header(id) }).ThrowIfError();
        }
        else channel.StartMessageFilter(new MessageFilter { FilterType = Filter.FLOW_CONTROL_FILTER,
            Mask = [0xFF, 0xFF, 0xFF, 0xFF], Pattern = Header(AbsPressureProtocol.ResponseId),
            FlowControl = Header(AbsPressureProtocol.RequestId) }).ThrowIfError();
    }

    internal bool Receive(Message message) => message.Data.Length >= 4 && Receive(
        (uint)((message.Data[0] << 24) | (message.Data[1] << 16) | (message.Data[2] << 8) | message.Data[3]),
        message.Data.AsSpan(4), IsData(message));

    internal static bool IsData(Message message) =>
        (message.RxStatus & (RxFlag.TX_MSG_TYPE | RxFlag.TX_INDICATION | RxFlag.START_OF_MESSAGE | RxFlag.ISO15765_PADDING_ERROR)) == 0;
}
