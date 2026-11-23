using LotusECMLogger.Services.Links;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// Covers how a diagnostic link decides that a received message answers its request. The J2534
    /// link and the scripted test link both use this, so a reply wrongly accepted here would be
    /// taken for the wrong request on a car, and one wrongly ignored would turn into a timeout.
    /// </summary>
    public sealed class DiagnosticResponseMatcherTests
    {
        private const uint Ecm = 0x7E8;

        private static byte[] FromEcm(params byte[] tail) => [0x00, 0x00, 0x07, 0xE8, .. tail];

        private static DiagnosticMatch Match(byte[] request, int echo, byte[] message) =>
            DiagnosticResponseMatcher.Match(Ecm, request, echo, message, out _);

        [Fact]
        public void PositiveReply_WithMatchingEcho_Matches()
        {
            Assert.Equal(DiagnosticMatch.Positive,
                Match([0x22, 0x02, 0x63], 2, FromEcm(0x62, 0x02, 0x63, 0x11, 0x22, 0x33, 0x44)));
        }

        [Fact]
        public void PositiveReply_ForAnotherPid_IsUnrelated()
        {
            // A late reply to the previous request must not be taken for this one.
            Assert.Equal(DiagnosticMatch.Unrelated,
                Match([0x22, 0x02, 0x64], 2, FromEcm(0x62, 0x02, 0x63, 0x11, 0x22, 0x33, 0x44)));
        }

        [Fact]
        public void PositiveReply_WithoutEcho_MatchesOnServiceAlone()
        {
            Assert.Equal(DiagnosticMatch.Positive, Match([0x03], 0, FromEcm(0x43, 0x00)));
        }

        [Fact]
        public void PositiveReply_TooShortForTheEcho_IsUnrelated()
        {
            Assert.Equal(DiagnosticMatch.Unrelated, Match([0x02, 0x0C, 0x00], 2, FromEcm(0x42, 0x0C)));
        }

        [Fact]
        public void ReplyFromAnotherEcu_IsUnrelated()
        {
            Assert.Equal(DiagnosticMatch.Unrelated, Match([0x03], 0, [0x00, 0x00, 0x07, 0xE9, 0x43, 0x00]));
        }

        [Fact]
        public void EchoOfOwnRequest_IsUnrelated()
        {
            // The adapter can loop back the request on 0x7E0.
            Assert.Equal(DiagnosticMatch.Unrelated, Match([0x03], 0, [0x00, 0x00, 0x07, 0xE0, 0x03]));
        }

        [Fact]
        public void NegativeReply_ReturnsNrc()
        {
            var match = DiagnosticResponseMatcher.Match(Ecm, [0x22, 0x02, 0x63], 2, FromEcm(0x7F, 0x22, 0x31),
                out byte nrc);

            Assert.Equal(DiagnosticMatch.Negative, match);
            Assert.Equal(0x31, nrc);
        }

        [Fact]
        public void ResponsePending_IsPendingNotNegative()
        {
            Assert.Equal(DiagnosticMatch.Pending, Match([0x04], 0, FromEcm(0x7F, 0x04, 0x78)));
        }

        [Fact]
        public void NegativeReply_ForAnotherService_IsUnrelated()
        {
            Assert.Equal(DiagnosticMatch.Unrelated, Match([0x03], 0, FromEcm(0x7F, 0x07, 0x11)));
        }

        [Fact]
        public void TruncatedNegativeReply_IsUnrelated()
        {
            Assert.Equal(DiagnosticMatch.Unrelated, Match([0x03], 0, FromEcm(0x7F, 0x03)));
        }

        [Fact]
        public void EchoLongerThanTheRequest_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Match([0x03], 1, FromEcm(0x43, 0x00)));
        }
    }
}
