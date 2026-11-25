using LotusECMLogger.Services.Links;

namespace LotusECMLogger.Services
{
    public sealed class J2534DtcService : IDtcService
    {
        private readonly ILinkProvider _links;

        public J2534DtcService() : this(J2534LinkProvider.Instance) { }

        public J2534DtcService(ILinkProvider links) => _links = links ?? throw new ArgumentNullException(nameof(links));

        public (bool success, string errorMessage, DtcReadResult result) ReadCodes()
        {
            try
            {
                // One link at a time: the plan reads every engine service before the TCU, and a
                // link may own the adapter, so the engine link is closed before the TCU's opens.
                using var links = new SequentialLinks(_links);
                var result = DtcReadPlan.ReadCodes(
                    (ecu, mode, timeout) => new Iso15765Service(links.For(ecu))
                        .ReadDtcs(mode, (int)timeout.TotalMilliseconds),
                    includeTcu: true);
                return (true, "", result);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, new DtcReadResult());
            }
        }

        public (bool success, string errorMessage) ClearCodes() => Clear(ECUDefinition.ECM);

        // Engine and transmission clears are separate on purpose: the UI exposes the TCU clear
        // as its own button so clearing engine codes never touches the transmission.
        public (bool success, string errorMessage) ClearTcuCodes() => Clear(ECUDefinition.TCM);

        private (bool success, string errorMessage) Clear(ECUDefinition ecu)
        {
            try
            {
                using var link = _links.OpenDiagnostic(ecu);
                return new Iso15765Service(link).ClearDiagnosticInformation();
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Keeps one link open, to whichever ECU was asked for last. A TCU link that cannot be
        /// opened (for example the adapter refuses the 0x7E9 filter) is reported as an
        /// <see cref="IOException"/>, which the read plan turns into a note on the TCU.
        /// </summary>
        private sealed class SequentialLinks(ILinkProvider provider) : IDisposable
        {
            private IDiagnosticLink? _current;

            public IDiagnosticLink For(ECUDefinition ecu)
            {
                if (_current is not null && _current.Ecu.ResponseId == ecu.ResponseId)
                    return _current;

                _current?.Dispose();
                _current = null;

                try
                {
                    _current = provider.OpenDiagnostic(ecu);
                }
                // The engine link's failure stays as it is: Read Codes fails without the engine.
                catch (Exception ex) when (ecu.ResponseId != ECUDefinition.ECM.ResponseId)
                {
                    throw new IOException(
                        $"The adapter could not open a link to the {ecu.Name} at 0x{ecu.ResponseId:X3}: {ex.Message}", ex);
                }
                return _current;
            }

            public void Dispose() => _current?.Dispose();
        }
    }
}
