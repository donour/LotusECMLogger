using LotusECMLogger.Models;
using LotusECMLogger.Services;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Ccp.Tests;

/// <summary>A HighSpeedLogService whose sessions run against one fake T6, with every CRO recorded.</summary>
internal sealed class LoggerHarness : IDisposable
{
    public LoggerHarness(double speedFactor = 20)
    {
        Host = new CcpSessionHost(() =>
        {
            Simulation = new SimulatedCcpTransport(Slave, speedFactor);
            Script = new ScriptedTransport(Simulation);
            Interlocked.Increment(ref _transportsOpened);
            return Script;
        }, new CcpMasterOptions { CommandTimeout = TimeSpan.FromMilliseconds(150) });
        Service = new HighSpeedLogService(Host);
        CsvPath = Path.Combine(Path.GetTempPath(), $"hsl-test-{Guid.NewGuid():N}.csv");
    }

    private int _transportsOpened;

    public FakeCcpSlave Slave { get; } = new();
    public CcpSessionHost Host { get; }
    public int TransportsOpened => Volatile.Read(ref _transportsOpened);
    /// <summary>The latest session's transports (a new one opens whenever the host has no open session).</summary>
    public SimulatedCcpTransport? Simulation { get; private set; }
    public ScriptedTransport? Script { get; private set; }
    public HighSpeedLogService Service { get; }
    public string CsvPath { get; }

    /// <summary>Starts logging and returns once the first decoded frame arrives.</summary>
    public void RunUntilStreaming(IReadOnlyList<(HighSpeedChannel Channel, int RateHz)> selection, bool pollAem = false)
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? error = null;
        void OnData(object? _, HighSpeedSampleEventArgs e) => first.TrySetResult();
        void OnError(object? _, string message) { error = message; first.TrySetResult(); }
        Service.DataReceived += OnData;
        Service.ErrorOccurred += OnError;
        try
        {
            Service.StartLogging(selection.Select(s => (s.Channel, s.RateHz)).ToList(), CsvPath, pollAem);
            Assert.True(first.Task.Wait(TimeSpan.FromSeconds(10)), "The logger never streamed.");
            Assert.Null(error);
        }
        finally
        {
            Service.DataReceived -= OnData;
            Service.ErrorOccurred -= OnError;
        }
    }

    /// <summary>The CSV's column header and data rows (after the '#' preamble).</summary>
    public (string[] Header, List<string[]> Rows) ReadCsv()
    {
        var lines = File.ReadAllLines(CsvPath).Where(l => !l.StartsWith('#')).ToList();
        return (lines[0].Split(','), lines.Skip(1).Select(l => l.Split(',')).ToList());
    }

    public void Dispose()
    {
        Service.Dispose();
        Host.Dispose();
        Slave.Dispose();
        try { File.Delete(CsvPath); } catch (IOException) { }
    }
}
