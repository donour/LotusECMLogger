using System.Runtime.ExceptionServices;

namespace LotusECMLogger.Tests;

/// <summary>
/// Hosts WinForms UI tests the way the app runs: inside <see cref="Application.Run(Form)"/>, so async handlers resume
/// on the WinForms synchronization context. Polling with <c>Application.DoEvents</c> on a bare STA thread let their
/// continuations touch controls from thread-pool threads and crashed the test host.
/// </summary>
internal static class UiTestHost
{
    /// <summary>
    /// Runs <paramref name="body"/> on an off-screen form inside a message loop on its own STA thread, then rethrows
    /// whatever it threw.
    /// </summary>
    public static void RunInMessageLoop(Func<Form, Task> body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            using var form = new Form
            {
                StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000),
                ShowInTaskbar = false, Size = new Size(1100, 820),
            };
            form.Shown += async (_, _) =>
            {
                try { await body(form); }
                catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
                finally { form.Close(); }
            };
            Application.Run(form);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(3))) Assert.Fail("The UI test did not finish.");
        failure?.Throw();
    }
}
