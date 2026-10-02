using LotusECMLogger.Services;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// Covers the classification of frames received during the ECU coding read (Mode 22 PIDs
    /// 0x0263 and 0x0264). The read loop only completes on two classified coding values, so a
    /// frame wrongly accepted here would load the wrong coding, and one wrongly ignored would
    /// turn a working read into a timeout.
    /// </summary>
    public sealed class EcuCodingResponseTests
    {
        /// <summary>Prepends the ECM response header the J2534 channel delivers.</summary>
        private static byte[] Response(params byte[] tail) => [0x00, 0x00, 0x07, 0xE8, .. tail];

        private static J2534EcuCodingService.CodingResponse Parse(byte[] data) =>
            J2534EcuCodingService.ParseCodingResponse(data);

        [Theory]
        [InlineData(0x63)]
        [InlineData(0x64)]
        public void PositiveReply_ReturnsPidAndFourCodingBytes(byte pid)
        {
            var reply = Parse(Response(0x62, 0x02, pid, 0x11, 0x22, 0x33, 0x44));

            Assert.Equal(pid, reply.Pid);
            Assert.Equal([0x11, 0x22, 0x33, 0x44], reply.Value);
            Assert.Null(reply.Nrc);
        }

        [Fact]
        public void PositiveReply_IgnoresTrailingPadding()
        {
            var reply = Parse(Response(0x62, 0x02, 0x63, 0x11, 0x22, 0x33, 0x44, 0xAA, 0xAA));

            Assert.Equal([0x11, 0x22, 0x33, 0x44], reply.Value);
        }

        [Fact]
        public void PositiveReply_TooShort_IsIgnored()
        {
            // Three coding bytes instead of four: accepting it would slice past the end.
            var reply = Parse(Response(0x62, 0x02, 0x63, 0x11, 0x22, 0x33));

            Assert.Null(reply.Value);
            Assert.Null(reply.Nrc);
        }

        [Fact]
        public void PositiveReplyForOtherPid_IsIgnored()
        {
            var reply = Parse(Response(0x62, 0x02, 0x65, 0x11, 0x22, 0x33, 0x44));

            Assert.Null(reply.Value);
            Assert.Null(reply.Nrc);
        }

        [Fact]
        public void EchoOfOwnRequest_IsIgnored()
        {
            // The request goes out on 0x7E0; a TX echo must never count as the ECU's answer.
            byte[] echo = [0x00, 0x00, 0x07, 0xE0, 0x22, 0x02, 0x63];

            var reply = Parse(echo);

            Assert.Null(reply.Value);
            Assert.Null(reply.Nrc);
        }

        [Fact]
        public void ReplyFromOtherModule_IsIgnored()
        {
            // Same payload shape, but from 0x7E9 rather than the ECM's 0x7E8.
            byte[] other = [0x00, 0x00, 0x07, 0xE9, 0x62, 0x02, 0x63, 0x11, 0x22, 0x33, 0x44];

            Assert.Null(Parse(other).Value);
        }

        [Fact]
        public void NegativeReply_ReturnsNrc()
        {
            var reply = Parse(Response(0x7F, 0x22, 0x31)); // requestOutOfRange

            Assert.Equal((byte)0x31, reply.Nrc);
            Assert.Null(reply.Value);
        }

        [Fact]
        public void ResponsePending_IsReportedAsNrc()
        {
            // The read loop keeps waiting on 0x78 rather than failing; the parser just reports it.
            Assert.Equal((byte)0x78, Parse(Response(0x7F, 0x22, 0x78)).Nrc);
        }

        [Fact]
        public void NegativeReplyToOtherService_IsIgnored()
        {
            var reply = Parse(Response(0x7F, 0x21, 0x31));

            Assert.Null(reply.Nrc);
            Assert.Null(reply.Value);
        }

        [Theory]
        [InlineData(new byte[] { })]
        [InlineData(new byte[] { 0x00, 0x00, 0x07, 0xE8 })]
        [InlineData(new byte[] { 0x00, 0x00, 0x07, 0xE8, 0x62, 0x02 })]
        public void ShortFrames_AreIgnoredWithoutThrowing(byte[] data)
        {
            var reply = Parse(data);

            Assert.Null(reply.Value);
            Assert.Null(reply.Nrc);
        }
    }
}
