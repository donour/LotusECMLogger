using System.Buffers.Binary;
using System.Text;
using LotusECMLogger.Services;
using Xunit;

namespace LotusECMLogger.Tests;

public sealed class AbsPumpOperationsTests
{
    // Literal protocol fixtures are independent of the runner's PumpOn/PumpOff properties.
    private const string On = "3106FF2200000000000000000000";
    private const string Off = "3106002200000000000000000000";
    private static readonly string[] HappyOrder =
        ["1A85", "1A87", "1089", "2104", On, "3306", Off, "3306", "3206", "3306", "1081"];

    [Fact]
    public void OneSecondPulseUsesExactOemPayloadsAndIndependentOffStopAndSessionCleanup()
    {
        var fake = new PumpFixture();
        var result = fake.Run();

        Assert.Equal(HappyOrder, fake.Requests);
        Assert.True(result.Completed);
        Assert.True(result.ActivationAttempted);
        Assert.True(result.CleanupRequired);
        Assert.True(result.OffCommandCompleted);
        Assert.True(result.StopConfirmed);
        Assert.True(result.SessionRestored);
        Assert.False(result.Cancelled);
        Assert.Equal(1000d, fake.Calls.Single(c => c.Hex == Off).AtMilliseconds);
        Assert.Equal(14, Convert.FromHexString(On).Length);
        Assert.Equal(14, Convert.FromHexString(Off).Length);
        Assert.Equal(fake.Requests, result.Exchanges.Select(e => e.RequestHex));
        Assert.Equal(result.Exchanges, fake.Journal);
        Assert.All(result.Exchanges, e => Assert.True(e.Success));
        Assert.All(result.Exchanges, e => Assert.Equal(TimeSpan.Zero, e.TimestampUtc.Offset));
        Assert.Equal(result.Exchanges.Select(e => e.ElapsedMilliseconds).OrderBy(x => x),
            result.Exchanges.Select(e => e.ElapsedMilliseconds));

        // Status 02 finishes command processing; it is not an observed motor-off state.
        Assert.Contains(result.Rows, r => r.Field == "Stop routine" &&
            r.Detail.Contains("not physical", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Rows, r => r.Field == "Requested duration" && r.Detail.Contains("not a thermal rating"));
        Assert.Contains(result.Rows, r => r.Field == "Required firmware reference" && r.Detail.Contains("does not verify"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void AcceptedDurationHoldsUntilTargetAndNeverRefreshesWithAnotherOn(int seconds)
    {
        var fake = new PumpFixture();
        var result = fake.Run(seconds);

        Assert.True(result.Completed);
        Assert.Equal(seconds * 1000d, fake.Calls.Single(c => c.Hex == Off).AtMilliseconds);
        Assert.Single(fake.Calls, c => c.Hex == On);
        Assert.Single(fake.Calls, c => c.Hex == Off);
        var between = fake.Calls.SkipWhile(c => c.Hex != On).Skip(1).TakeWhile(c => c.Hex != Off);
        Assert.All(between, c => Assert.Equal("3306", c.Hex));
        Assert.All(fake.Waits, ms => Assert.InRange(ms, 1, 100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(int.MaxValue)]
    public void InvalidDurationIsRefusedBeforeAnyTransmission(int seconds)
    {
        var fake = new PumpFixture();
        var result = fake.Run(seconds);
        Assert.Empty(fake.Calls);
        Assert.False(result.Completed);
        Assert.False(result.ActivationAttempted);
        Assert.Contains(result.Rows, r => r.Field == "Error");
    }

    [Fact]
    public void OperatorConfirmationIsRequiredEvenWhenSimulatedWheelsWouldBeZero()
    {
        var fake = new PumpFixture();
        var result = fake.Run(operatorConfirmed: false);
        Assert.Empty(fake.Calls);
        Assert.False(result.ActivationAttempted);
        Assert.False(result.Completed);
    }

    [Theory]
    [InlineData("1A85", "5A856E6F742D7468652D6275696C64")]
    [InlineData("1A87", "5A87413133324A30333134412000")]
    [InlineData("1A85", "5A86")]
    public void MissingOrDifferentIdentityCannotReachSessionEntry(string request, string reply)
    {
        var fake = new PumpFixture { Respond = c => c.Hex == request ? Positive(reply) : null };
        var result = fake.Run();
        Assert.Equal(new[] { "1A85", "1A87" }, fake.Requests);
        Assert.False(result.ActivationAttempted);
        Assert.False(result.Completed);
    }

    [Fact]
    public void FailedIdentityReadCannotBeReplacedByTheOtherMatchingRecord()
    {
        var fake = new PumpFixture
        {
            Respond = c => c.Hex == "1A85" ? KwpResponse.Failure("No build response") : null,
        };
        var result = fake.Run();
        Assert.Equal(new[] { "1A85", "1A87" }, fake.Requests);
        Assert.False(result.ActivationAttempted);
        Assert.Contains(result.Exchanges, e => e.RequestHex == "1A85" && !e.Success);
    }

    [Theory]
    [InlineData("7F1022")]
    [InlineData("5081")]
    [InlineData("508900")]
    public void DeniedOrMismatchedTesterSessionNeverStartsPumpAndStillRestoresDefault(string reply)
    {
        var fake = new PumpFixture { Respond = c => c.Hex == "1089" ? WireReply(reply) : null };
        var result = fake.Run();
        Assert.Equal(new[] { "1A85", "1A87", "1089", "1081" }, fake.Requests);
        Assert.False(result.ActivationAttempted);
        Assert.True(result.SessionRestored);
        Assert.False(result.Completed);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 47)] // A numerically unreachable wire value must not be treated as zero.
    [InlineData(2, 0x3FFF)] // Fault sentinel is distinct from zero.
    [InlineData(3, 0xFFFF)]
    public void EveryWheelMustHaveAnActualZeroRecord(int wheel, int raw)
    {
        byte[] live = LiveReply();
        BinaryPrimitives.WriteUInt16LittleEndian(live.AsSpan(2 + wheel * 2, 2), (ushort)raw);
        var fake = new PumpFixture { Respond = c => c.Hex == "2104" ? KwpResponse.Positive(live) : null };
        var result = fake.Run();
        Assert.Equal(new[] { "1A85", "1A87", "1089", "2104", "1081" }, fake.Requests);
        Assert.False(result.ActivationAttempted);
        Assert.False(result.Completed);
    }

    [Theory]
    [InlineData("6104")]
    [InlineData("61030000000000000000000000000000000000000000")]
    [InlineData("7F2122")]
    public void MissingMalformedOrWrongLiveRecordNeverStartsPump(string reply)
    {
        var fake = new PumpFixture { Respond = c => c.Hex == "2104" ? WireReply(reply) : null };
        var result = fake.Run();
        Assert.DoesNotContain(On, fake.Requests);
        Assert.DoesNotContain(Off, fake.Requests);
        Assert.DoesNotContain("3206", fake.Requests);
        Assert.True(result.SessionRestored);
        Assert.False(result.Completed);
    }

    [Fact]
    public void AlreadyCancelledRequestPerformsNoVehicleExchange()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var fake = new PumpFixture();
        var result = fake.Run(token: cancellation.Token);
        Assert.Empty(fake.Calls);
        Assert.True(result.Cancelled);
        Assert.False(result.ActivationAttempted);
    }

    [Fact]
    public void CancellationAfterLiveCheckStillPerformsNoActuation()
    {
        using var cancellation = new CancellationTokenSource();
        var fake = new PumpFixture
        {
            JournalHook = e => { if (e.RequestHex == "2104") cancellation.Cancel(); },
        };
        var result = fake.Run(token: cancellation.Token);
        Assert.True(result.Cancelled);
        Assert.False(result.ActivationAttempted);
        Assert.Equal(new[] { "1A85", "1A87", "1089", "2104", "1081" }, fake.Requests);
        AssertCleanupTokensAreIndependent(fake, cancellation.Token);
    }

    [Fact]
    public void CancellationDuringHoldStillSendsOffStopAndDefaultWithLiveCleanupTokens()
    {
        using var cancellation = new CancellationTokenSource();
        var fake = new PumpFixture
        {
            WaitHook = (_, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); },
        };
        var result = fake.Run(token: cancellation.Token);
        Assert.True(result.Cancelled);
        Assert.False(result.Completed);
        Assert.True(result.OffCommandCompleted);
        Assert.True(result.StopConfirmed);
        Assert.True(result.SessionRestored);
        Assert.Equal(HappyOrder, fake.Requests);
        AssertCleanupTokensAreIndependent(fake, cancellation.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationInsideOnTransportIsReportedAndStillCleansUp(bool throwCancellation)
    {
        using var cancellation = new CancellationTokenSource();
        var fake = new PumpFixture
        {
            Respond = c =>
            {
                if (c.Hex != On) return null;
                cancellation.Cancel();
                if (throwCancellation) throw new OperationCanceledException(cancellation.Token);
                return KwpResponse.Failure("Transport cancelled after possible transmission");
            },
        };
        var result = fake.Run(token: cancellation.Token);
        Assert.True(result.Cancelled);
        Assert.True(result.ActivationAttempted);
        Assert.False(result.Completed);
        AssertCleanupTail(fake);
        AssertCleanupTokensAreIndependent(fake, cancellation.Token);
        Assert.Contains(result.Exchanges, e => e.RequestHex == On && !e.Success);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("throw")]
    [InlineData("pending")]
    [InlineData("journal")]
    public void UncertainOnOutcomeAlwaysArmsOffAndStopCleanup(string failure)
    {
        var fake = new PumpFixture
        {
            Respond = c => c.Hex != On ? null : failure switch
            {
                "timeout" => KwpResponse.Failure("Lost ON response"),
                "throw" => throw new IOException("Driver failed after write"),
                "pending" => KwpResponse.Negative([0x7F, 0x31, 0x78]),
                _ => null,
            },
            JournalHook = e =>
            {
                if (failure == "journal" && e.RequestHex == On) throw new IOException("Journal flush failed");
            },
        };
        var result = fake.Run();
        Assert.True(result.ActivationAttempted);
        Assert.False(result.Completed);
        Assert.True(result.OffCommandCompleted);
        Assert.True(result.StopConfirmed);
        Assert.True(result.SessionRestored);
        AssertCleanupTail(fake);
        Assert.Contains(result.Exchanges, e => e.RequestHex == On);
    }

    [Theory]
    [InlineData(0x21)]
    [InlineData(0x22)]
    [InlineData(0x31)]
    [InlineData(0x33)]
    public void KnownStartRefusalDoesNotSendCommandsThatCouldChangeAnotherActuator(int nrc)
    {
        var fake = new PumpFixture
        {
            Respond = c => c.Hex == On ? KwpResponse.Negative([0x7F, 0x31, (byte)nrc]) : null,
        };
        var result = fake.Run();
        Assert.Equal(new[] { "1A85", "1A87", "1089", "2104", On, "1081" }, fake.Requests);
        Assert.True(result.ActivationAttempted);
        Assert.False(result.CleanupRequired);
        Assert.False(result.OffCommandCompleted);
        Assert.False(result.StopConfirmed);
        Assert.True(result.SessionRestored);
        Assert.False(result.Completed);
    }

    [Theory]
    [InlineData("71")]
    [InlineData("7105")]
    [InlineData("710600")]
    [InlineData("7206")]
    public void ShortWrongEchoOrWrongSidOnReplyCannotBeAcceptedAsStart(string reply)
    {
        var fake = new PumpFixture { Respond = c => c.Hex == On ? Positive(reply) : null };
        var result = fake.Run();
        Assert.False(result.Completed);
        Assert.True(result.ActivationAttempted);
        AssertCleanupTail(fake);
        Assert.Contains(result.Exchanges, e => e.RequestHex == On && !e.Success && e.ResponseHex == reply);
    }

    [Fact]
    public void JournalFailureAfterDefiniteOnRefusalDoesNotStopAnExistingRoutine()
    {
        var fake = new PumpFixture
        {
            Respond = c => c.Hex == On ? KwpResponse.Negative([0x7F, 0x31, 0x22]) : null,
            JournalHook = e =>
            {
                if (e.RequestHex == On) throw new IOException("Journal flush failed after refusal");
            },
        };
        var result = fake.Run();
        Assert.Equal(new[] { "1A85", "1A87", "1089", "2104", On, "1081" }, fake.Requests);
        Assert.True(result.ActivationAttempted);
        Assert.False(result.CleanupRequired);
        Assert.False(result.Completed);
        Assert.True(result.SessionRestored);
        Assert.Contains(result.Exchanges, e => e.RequestHex == On && e.ResponseHex == "7F3122");
        Assert.Contains(result.Rows, r => r.Field == "Error" && r.Value.Contains("Journal flush failed"));
    }

    [Fact]
    public void ProcessingPollsAreRetriedAndStatusTwoDoesNotReplaceOffOrStop()
    {
        int polls = 0;
        var fake = new PumpFixture
        {
            Respond = c => c.Hex == "3306" && ++polls <= 2 ? Positive("730601") : null,
        };
        var result = fake.Run();
        Assert.True(result.Completed);
        Assert.Equal(5, polls);
        Assert.Equal(2, fake.Waits.Count(ms => ms == 50));
        AssertCleanupTail(fake);
    }

    [Theory]
    [InlineData("730602")]
    [InlineData("7305020000000000000000")]
    [InlineData("7306070000000000000000")]
    [InlineData("7306030000000000000000")]
    [InlineData("730602000000000000000000")]
    public void WrongOrIncompleteOnResultTriggersCleanup(string reply)
    {
        bool firstPoll = true;
        var fake = new PumpFixture
        {
            Respond = c =>
            {
                if (c.Hex != "3306" || !firstPoll) return null;
                firstPoll = false;
                return Positive(reply);
            },
        };
        var result = fake.Run();
        Assert.False(result.Completed);
        Assert.True(result.ActivationAttempted);
        AssertCleanupTail(fake);
    }

    [Fact]
    public void NeverEndingProcessingStatusHasABoundedWaitAndStillCleansUp()
    {
        var fake = new PumpFixture();
        fake.Respond = c => c.Hex == "3306" && !fake.Requests.Contains(Off) ? Positive("730601") : null;
        var result = fake.Run();
        Assert.False(result.Completed);
        Assert.Equal(1000d, fake.Calls.Single(c => c.Hex == Off).AtMilliseconds);
        Assert.InRange(fake.Calls.Count(c => c.Hex == "3306"), 3, 32);
        AssertCleanupTail(fake);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOffAcknowledgementNeverSuppressesStopOrDefault(bool throws)
    {
        var fake = new PumpFixture
        {
            Respond = c => c.Hex != Off ? null : throws
                ? throw new IOException("OFF transport error") : KwpResponse.Failure("Lost OFF acknowledgement"),
        };
        var result = fake.Run();
        Assert.False(result.Completed);
        Assert.False(result.OffCommandCompleted);
        Assert.True(result.StopConfirmed);
        Assert.True(result.SessionRestored);
        Assert.Equal(new[] { Off, "3206", "3306", "1081" }, fake.Requests.SkipWhile(x => x != Off));
    }

    [Fact]
    public void WrongOffResultDoesNotSuppressStopAndCannotCountAsCompleted()
    {
        var fake = new PumpFixture();
        fake.Respond = c => c.Hex == "3306" && fake.Requests.Contains(Off) && !fake.Requests.Contains("3206")
            ? KwpResponse.Positive(ResultReply(7)) : null;
        var result = fake.Run();
        Assert.False(result.Completed);
        Assert.False(result.OffCommandCompleted);
        Assert.True(result.StopConfirmed);
        Assert.True(result.SessionRestored);
        AssertCleanupTail(fake);
    }

    [Theory]
    [InlineData("lost_ack")]
    [InlineData("wrong_echo")]
    [InlineData("wrong_status")]
    [InlineData("short_status")]
    public void UnconfirmedStopIsNeverCompletedAndDefaultIsStillAttempted(string failure)
    {
        var fake = new PumpFixture();
        fake.Respond = c =>
        {
            if (c.Hex == "3206" && failure == "lost_ack") return KwpResponse.Failure("Lost stop acknowledgement");
            if (c.Hex == "3206" && failure == "wrong_echo") return Positive("7205");
            if (c.Hex == "3306" && fake.Requests.Contains("3206"))
                return failure == "wrong_status" ? KwpResponse.Positive(ResultReply(2)) : Positive("730607");
            return null;
        };
        var result = fake.Run();
        Assert.False(result.Completed);
        Assert.True(result.OffCommandCompleted);
        Assert.False(result.StopConfirmed);
        Assert.True(result.SessionRestored);
        Assert.Equal("1081", fake.Requests[^1]);
    }

    [Fact]
    public void DefaultSessionFailureIsReportedDespiteConfirmedOffAndStop()
    {
        var fake = new PumpFixture { Respond = c => c.Hex == "1081" ? KwpResponse.Failure("Default session timeout") : null };
        var result = fake.Run();
        Assert.True(result.OffCommandCompleted);
        Assert.True(result.StopConfirmed);
        Assert.False(result.SessionRestored);
        Assert.False(result.Completed);
    }

    [Fact]
    public void JournalFailureDuringCleanupDoesNotSuppressAnyShutdownStage()
    {
        var fake = new PumpFixture();
        fake.JournalHook = _ => { if (fake.Requests.Contains(Off)) throw new IOException("Disk is full"); };
        var result = fake.Run();
        Assert.Equal(HappyOrder, fake.Requests);
        Assert.True(result.OffCommandCompleted);
        Assert.True(result.StopConfirmed);
        Assert.True(result.SessionRestored);
        Assert.False(result.Completed);
        Assert.Contains(result.Rows, r => r.Field == "Error" && r.Value.Contains("journal failed during cleanup"));
    }

    [Fact]
    public void BrokenCleanupProgressSubscriberCannotBlockShutdownRequests()
    {
        var fake = new PumpFixture();
        var progress = new InlineProgress(p =>
        {
            if (p.Phase.StartsWith("Sending") || p.Phase.StartsWith("Stopping") || p.Phase.StartsWith("Restoring"))
                throw new InvalidOperationException("UI subscriber disposed");
        });
        var result = fake.Run(progress: progress);
        Assert.Equal(HappyOrder, fake.Requests);
        Assert.True(result.Completed);
    }

    [Fact]
    public void RunnerCannotBeReusedWithAnOldIdentityOrState()
    {
        var fake = new PumpFixture();
        var runner = fake.CreateRunner();
        Assert.True(runner.Run(1, true, null, CancellationToken.None).Completed);
        int count = fake.Calls.Count;
        Assert.Throws<InvalidOperationException>(() => runner.Run(1, true, null, CancellationToken.None));
        Assert.Equal(count, fake.Calls.Count);
    }

    private static void AssertCleanupTail(PumpFixture fake) =>
        Assert.Equal(new[] { Off, "3306", "3206", "3306", "1081" }, fake.Requests.SkipWhile(x => x != Off));

    private static void AssertCleanupTokensAreIndependent(PumpFixture fake, CancellationToken caller)
    {
        var cleanup = fake.Calls.Where(c => c.Hex is Off or "3206" or "1081" ||
            c.Hex == "3306" && fake.Calls.Any(prior => prior.Index < c.Index && prior.Hex == Off));
        Assert.NotEmpty(cleanup);
        Assert.All(cleanup, c =>
        {
            Assert.NotEqual(caller, c.Token);
            Assert.False(c.WasCancelledAtEntry);
        });
    }

    private static KwpResponse Positive(string hex) => KwpResponse.Positive(Convert.FromHexString(hex));
    private static KwpResponse WireReply(string hex) => hex.StartsWith("7F", StringComparison.Ordinal)
        ? KwpResponse.Negative(Convert.FromHexString(hex)) : Positive(hex);
    private static byte[] LiveReply()
    {
        var reply = new byte[22];
        reply[0] = 0x61;
        reply[1] = 0x04;
        reply[19] = 150;
        return reply;
    }
    private static byte[] ResultReply(byte status) => [0x73, 0x06, status, 0, 0, 0, 0, 0, 0, 0, 0];

    private sealed record Call(int Index, string Hex, CancellationToken Token, bool WasCancelledAtEntry, double AtMilliseconds);
    private sealed class InlineProgress(Action<AbsRoutineProgress> report) : IProgress<AbsRoutineProgress>
    {
        public void Report(AbsRoutineProgress value) => report(value);
    }

    private sealed class PumpFixture
    {
        public List<Call> Calls { get; } = [];
        public List<AbsDiagnosticExchange> Journal { get; } = [];
        public List<int> Waits { get; } = [];
        public string[] Requests => Calls.Select(c => c.Hex).ToArray();
        public Func<Call, KwpResponse?>? Respond { get; set; }
        public Action<int, CancellationToken>? WaitHook { get; set; }
        public Action<AbsDiagnosticExchange>? JournalHook { get; set; }
        private double milliseconds;

        public AbsPumpOperations CreateRunner() => new(Request, exchange =>
        {
            Journal.Add(exchange);
            JournalHook?.Invoke(exchange);
        }, () => milliseconds, (duration, token) =>
        {
            token.ThrowIfCancellationRequested();
            Waits.Add(duration);
            WaitHook?.Invoke(duration, token);
            milliseconds += duration;
        });

        public AbsRoutineResult Run(int seconds = 1, bool operatorConfirmed = true,
            CancellationToken token = default, IProgress<AbsRoutineProgress>? progress = null) =>
            CreateRunner().Run(seconds, operatorConfirmed, progress, token);

        private KwpResponse Request(byte[] bytes, CancellationToken token)
        {
            var call = new Call(Calls.Count, Convert.ToHexString(bytes), token, token.IsCancellationRequested, milliseconds);
            Calls.Add(call);
            KwpResponse? changed = Respond?.Invoke(call);
            if (changed is { } response) return response;
            return call.Hex switch
            {
                "1A85" => KwpResponse.Positive([0x5A, 0x85, .. Encoding.ASCII.GetBytes("6863802010000"), .. new byte[13]]),
                "1A87" => KwpResponse.Positive([0x5A, 0x87, .. Encoding.ASCII.GetBytes("A132J0314A ")]),
                "1089" => Positive("5089"),
                "1081" => Positive("5081"),
                "2104" => KwpResponse.Positive(LiveReply()),
                On or Off => Positive("7106"),
                "3206" => Positive("7206"),
                "3306" => KwpResponse.Positive(ResultReply(Requests.Contains("3206") ? (byte)7 : (byte)2)),
                _ => throw new InvalidOperationException("Unexpected simulated request " + call.Hex),
            };
        }
    }
}
