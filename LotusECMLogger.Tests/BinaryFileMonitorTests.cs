using System.Collections.Concurrent;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// Covers the monitor's run-of-failures count, which live tuning uses to tell an editor
    /// briefly replacing the file during a save from a file that has gone away.
    /// </summary>
    public sealed class BinaryFileMonitorTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        /// <summary>The monitor may hold the file open for a scan, which makes a delete fail briefly.</summary>
        private static void DeleteWithRetry(string path)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Delete(path);
                    return;
                }
                catch (IOException) when (attempt < 50)
                {
                    Thread.Sleep(10);
                }
            }
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("Condition not met in time.");
                await Task.Delay(10);
            }
        }

        [Fact]
        public async Task MissingFile_CountsConsecutiveFailures_AndASuccessfulReadResetsTheCount()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, new byte[8]);

                var failures = new ConcurrentQueue<int>();
                var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var monitor = new BinaryFileMonitor.BinaryFileMonitor(path, 10);
                monitor.MonitorError += (_, e) => failures.Enqueue(e.ConsecutiveFailures);
                monitor.WordChanged += (_, _) => changed.TrySetResult();
                monitor.Start();

                // The file goes away: every scan fails, and the count climbs by one each time.
                DeleteWithRetry(path);
                await WaitUntilAsync(() => failures.Count >= 3);
                Assert.Equal([1, 2, 3], failures.Take(3));

                // It comes back with different contents: the read succeeds (reported as a change).
                File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6, 7, 8]);
                await changed.Task.WaitAsync(Timeout);

                // A new outage starts counting from one again.
                failures.Clear();
                DeleteWithRetry(path);
                await WaitUntilAsync(() => !failures.IsEmpty);
                Assert.True(failures.TryPeek(out int first));
                Assert.Equal(1, first);
            }
            finally
            {
                if (File.Exists(path))
                    DeleteWithRetry(path);
            }
        }

        [Fact]
        public void ErrorArgs_DefaultToASingleFailure()
        {
            var args = new BinaryFileMonitor.FileMonitorErrorEventArgs(new IOException("x"));

            Assert.Equal(1, args.ConsecutiveFailures);
        }
    }
}
