using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Controls
{
    /// <summary>What the CCP tab's sub-panels share: the session, its identity, one operation at a time, and the log.</summary>
    internal interface ICcpTabContext
    {
        /// <summary>The open session's master while the tab holds a lease, else null.</summary>
        CcpMaster? Master { get; }
        /// <summary>The shared session host, for consumers with their own lease (a tuning recording's logger).</summary>
        CcpSessionHost? Host { get; }
        bool IsConnected { get; }
        CcpBuildIdentification? Identification { get; }
        /// <summary>The variable table, once read (by the tab's button or a panel).</summary>
        T6SymbolRegistry? Registry { get; set; }
        bool IsBusy { get; }
        /// <summary>Where backups of ECU memory go before anything is written.</summary>
        string BackupDirectory { get; }

        /// <summary>Raised on the UI thread after the session, identity, registry or busy state changes.</summary>
        event EventHandler? SessionChanged;

        /// <summary>Runs one operation with the tab's controls disabled and Cancel enabled, logging any failure.</summary>
        Task RunAsync(string what, Func<CancellationToken, Task> action);
        void CancelOperation();
        void Log(string message);
    }
}
