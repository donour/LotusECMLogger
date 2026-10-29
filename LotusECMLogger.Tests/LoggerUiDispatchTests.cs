using System.Reflection;
using System.Runtime.ExceptionServices;
using LotusECMLogger.Controls;

namespace LotusECMLogger.Tests;

public sealed class LoggerUiDispatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReceiveCallbackDoesNotWaitForUiAndDropsUpdatesAfterDisposal(bool disposeBeforeDispatch)
    {
        RunOnSta(() =>
        {
            using var control = new OBDLoggerControl();
            _ = control.Handle;
            int updates = 0;
            control.RefreshRateUpdated += _ => updates++;
            var callback = typeof(OBDLoggerControl)
                .GetMethod("Logger_DataLogged", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Action<List<LiveDataReading>>>(control);

            Exception? workerError = null;
            var worker = new Thread(() =>
            {
                try { callback([]); }
                catch (Exception ex) { workerError = ex; }
            }) { IsBackground = true };
            worker.Start();

            // Deliberately leave the UI message queue unpumped. CAN reception must return anyway.
            bool returnedBeforeDispatch = worker.Join(TimeSpan.FromSeconds(2));
            Assert.Equal(0, updates);
            if (disposeBeforeDispatch) control.Dispose();
            Application.DoEvents();

            Assert.True(worker.Join(TimeSpan.FromSeconds(2)), "Receive callback remained blocked.");
            Assert.Null(workerError);
            Assert.True(returnedBeforeDispatch, "Receive callback waited for the UI thread.");
            Assert.Equal(disposeBeforeDispatch ? 0 : 1, updates);
        });
    }

    [Fact]
    public void AnotherLoggerDisablesObdStartUntilItStops()
    {
        RunOnSta(() =>
        {
            using var control = new OBDLoggerControl();
            var start = Assert.IsType<Button>(control.Controls.Find("startLoggerButton", true).Single());
            Assert.True(start.Enabled);

            control.IsLoggerActive = true;
            Assert.False(start.Enabled);
            Assert.False(control.IsLogging);

            control.IsLoggerActive = false;
            Assert.True(start.Enabled);
            Assert.False(control.IsLogging);
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "UI test did not finish.");
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
