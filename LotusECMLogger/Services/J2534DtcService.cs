using SAE.J2534;

namespace LotusECMLogger.Services
{
    public sealed class J2534DtcService : IDtcService
    {
        public (bool success, string errorMessage, DtcReadResult result) ReadCodes()
        {
            try
            {
                using var session = J2534Session.Open();
                J2534Channel channel = session.OpenIso15765();
                channel.StartMessageFilter(ECUDefinition.ECM.CreateFlowControlFilter()).ThrowIfError();

                // The TCU is optional: a manual car has none, and an adapter with a filter
                // limit must still read the engine. Install its filter beside the ECM's (the
                // same one-channel, several-filters pattern the logger uses) and degrade to a
                // note instead of failing the whole read.
                var tcuFilter = channel.StartMessageFilter(ECUDefinition.TCM.CreateFlowControlFilter());

                var iso = new Iso15765Service(channel);
                var result = DtcReadPlan.ReadCodes(
                    (ecu, mode, timeout) => iso.ReadDtcs(mode, ecu, timeout),
                    includeTcu: !tcuFilter.IsError);

                if (tcuFilter.IsError)
                {
                    result = result with
                    {
                        Tcu = new DtcModuleResult
                        {
                            StoredError =
                                $"The adapter could not route 0x{ECUDefinition.TCM.ResponseId:X3} replies: {tcuFilter.ErrorMessage}",
                        },
                    };
                }

                return (true, "", result);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, new DtcReadResult());
            }
        }

        public (bool success, string errorMessage) ClearCodes()
        {
            try
            {
                using var session = J2534Session.Open();
                J2534Channel channel = session.OpenIso15765();
                channel.StartMessageFilter(ECUDefinition.ECM.CreateFlowControlFilter()).ThrowIfError();

                // Engine-only by design: transmission codes live in another module and are
                // cleared by ClearTcuCodes, which the UI exposes as a separate button.
                var iso = new Iso15765Service(channel);
                return iso.ClearDiagnosticInformation(ECUDefinition.ECM);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public (bool success, string errorMessage) ClearTcuCodes()
        {
            try
            {
                using var session = J2534Session.Open();
                J2534Channel channel = session.OpenIso15765();
                channel.StartMessageFilter(ECUDefinition.TCM.CreateFlowControlFilter()).ThrowIfError();

                var iso = new Iso15765Service(channel);
                return iso.ClearDiagnosticInformation(ECUDefinition.TCM);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
