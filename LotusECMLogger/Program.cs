using System.Text;
using LotusECMLogger.Services;

namespace LotusECMLogger
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Anything that escapes a WinForms event handler — including this app's many
            // `async void` handlers, whose exceptions the sync context rethrows on the UI thread —
            // reaches ThreadException. Catching it keeps the message loop alive, so a failed
            // diagnostic operation no longer takes the process down mid-log.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ReportCrash(e.Exception, terminating: false);

            // Background threads (logger, CSV writer, telemetry monitors) cannot be rescued — the
            // runtime tears the process down regardless — but they can at least leave a report.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                ReportCrash(e.ExceptionObject as Exception, terminating: e.IsTerminating);

            // Read the DTC description table up front so the Diagnostic Trouble Codes tab can label
            // codes without touching disk mid-scan. A missing catalog only costs the labels.
            DtcDescriptionCatalog.Preload();

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Application.Run(new MainWindow());
        }

        private static readonly object crashLock = new();
        private static bool reporting;

        /// <summary>
        /// Writes a crash report next to the logs and tells the user where it went. Never throws:
        /// a fault in the reporter would mask the fault being reported.
        /// </summary>
        private static void ReportCrash(Exception? ex, bool terminating)
        {
            // A cascade of failures (or a fault raised from inside the dialog's own message pump)
            // must not stack modal dialogs on top of each other.
            lock (crashLock)
            {
                if (reporting)
                    return;
                reporting = true;
            }

            try
            {
                string? reportPath = TryWriteCrashReport(ex, terminating);

                var message = new StringBuilder();
                message.AppendLine(terminating
                    ? "LotusECMLogger hit an unrecoverable error and has to close."
                    : "LotusECMLogger hit an unexpected error.");
                message.AppendLine();
                message.AppendLine(ex?.Message ?? "No exception details were supplied.");
                message.AppendLine();
                message.AppendLine(reportPath is null
                    ? "The crash report could not be saved."
                    : $"Details saved to:\n{reportPath}");

                if (!terminating)
                {
                    message.AppendLine();
                    message.AppendLine(
                        "The application will keep running, but any operation in progress has stopped. " +
                        "Check that logging is still active before relying on it.");
                }

                MessageBox.Show(message.ToString(), "LotusECMLogger",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
                // Reporting is best-effort — never let it take the process down itself.
            }
            finally
            {
                lock (crashLock)
                {
                    reporting = false;
                }
            }
        }

        /// <summary>
        /// Writes the report alongside the CSV logs, where users already look for output.
        /// Returns the path written, or null if it could not be saved.
        /// </summary>
        private static string? TryWriteCrashReport(Exception? ex, bool terminating)
        {
            try
            {
                string path = LoggerPaths.UniquePath(LoggerPaths.TimestampedPath("crash", "txt"));
                LoggerPaths.EnsureParentDirectory(path);

                var report = new StringBuilder();
                report.AppendLine($"LotusECMLogger crash report");
                report.AppendLine($"Time:        {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                report.AppendLine($"Version:     {Application.ProductVersion}");
                report.AppendLine($"OS:          {Environment.OSVersion}");
                report.AppendLine($"Runtime:     {Environment.Version} ({(Environment.Is64BitProcess ? "x64" : "x86")})");
                report.AppendLine($"Terminating: {terminating}");
                report.AppendLine();
                // ToString() carries the stack trace and the full inner-exception chain.
                report.AppendLine(ex?.ToString() ?? "No exception object was supplied.");

                File.WriteAllText(path, report.ToString());
                return path;
            }
            catch
            {
                return null;
            }
        }
    }
}
