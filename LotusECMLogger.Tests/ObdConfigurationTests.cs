namespace LotusECMLogger.Tests
{
    /// <summary>
    /// Lints the logging configurations the app ships, through the same discovery and loading path
    /// the app uses. These cover the layer between a config file and the wire: that every shipped
    /// config still loads, that the requests it builds are well formed and within protocol limits,
    /// and that the PIDs it asks for are ones the decoder can actually make use of.
    /// </summary>
    /// <remarks>
    /// A config can be wrong in ways no decoder test would notice - asking for more PIDs than a
    /// request may carry, or asking for a PID nothing can decode - and the symptom is a quietly
    /// incomplete log rather than a failure. This is where that gets caught.
    /// </remarks>
    public sealed class ObdConfigurationTests
    {
        /// <summary>SAE J1979 permits at most six PIDs in a single Mode 01 request.</summary>
        private const int MaxPidsPerMode01Request = 6;

        public static TheoryData<string> ShippedConfigurations()
        {
            var data = new TheoryData<string>();
            foreach (string name in MultiECUConfigurationLoader.GetAvailableConfigurations())
                data.Add(name);
            return data;
        }

        /// <summary>
        /// Guards every <see cref="TheoryData{T}"/>-driven test below: if discovery returns nothing
        /// they all pass vacuously, which would make this whole file worthless without saying so.
        /// </summary>
        [Fact]
        public void Discovery_FindsTheShippedConfigurations()
        {
            var found = MultiECUConfigurationLoader.GetAvailableConfigurations();

            Assert.NotEmpty(found);
            Assert.Contains("lotus-default", found);
            Assert.Contains("lotus-diagnostic", found);
        }

        [Theory]
        [MemberData(nameof(ShippedConfigurations))]
        public void EveryConfiguration_LoadsWithAtLeastOneRequest(string configName)
        {
            var config = MultiECUConfigurationLoader.LoadByName(configName);

            Assert.NotEmpty(config.ECUGroups);
            Assert.True(config.TotalRequestCount > 0, $"'{configName}' defines no requests.");
            Assert.All(config.ECUGroups, group => Assert.NotEmpty(group.Requests));
        }

        /// <summary>
        /// Every request must serialise to its ECU's header followed by the mode byte and the PID
        /// selector, because that framing is what the response matching on the way back assumes.
        /// </summary>
        [Theory]
        [MemberData(nameof(ShippedConfigurations))]
        public void EveryConfiguration_BuildsWellFormedRequestMessages(string configName)
        {
            var config = MultiECUConfigurationLoader.LoadByName(configName);

            foreach (var group in config.ECUGroups)
            {
                byte[] header = group.ECU.GetRequestHeader();

                foreach (var request in group.Requests)
                {
                    byte[] message = request.BuildMessage(header);
                    string where = $"{configName}/{group.ECU.Name}/{request.Name}";

                    Assert.Equal(header, message[..header.Length]);
                    Assert.Equal(request.Mode, message[header.Length]);

                    int expectedLength = request switch
                    {
                        Mode01Request mode01 => header.Length + 1 + mode01.PIDs.Length,
                        Mode22Request => header.Length + 3,   // mode + PID high + PID low
                        _ => message.Length,
                    };
                    Assert.True(message.Length == expectedLength,
                        $"{where}: expected a {expectedLength}-byte request, got {message.Length}.");
                }
            }
        }

        [Theory]
        [MemberData(nameof(ShippedConfigurations))]
        public void EveryMode01Request_FitsTheProtocolLimit(string configName)
        {
            var config = MultiECUConfigurationLoader.LoadByName(configName);

            foreach (var request in AllRequests(config).OfType<Mode01Request>())
            {
                Assert.True(request.PIDs.Length <= MaxPidsPerMode01Request,
                    $"{configName}/{request.Name} asks for {request.PIDs.Length} PIDs in one Mode 01 " +
                    $"request; J1979 allows {MaxPidsPerMode01Request}.");
            }
        }

        /// <summary>
        /// A Mode 01 reply packs its PIDs end to end, so the decoder can only reach the PIDs behind
        /// one it does not recognise if it knows how wide that PID is. A requested PID with neither a
        /// decoder nor a declared width therefore costs every reading after it in the same reply.
        /// </summary>
        [Theory]
        [MemberData(nameof(ShippedConfigurations))]
        public void EveryRequestedMode01Pid_CanBeSteppedOver(string configName)
        {
            var config = MultiECUConfigurationLoader.LoadByName(configName);

            foreach (var request in AllRequests(config).OfType<Mode01Request>())
            {
                foreach (byte pid in request.PIDs)
                {
                    Assert.True(
                        ObdPidDecoders.Mode01.ContainsKey(pid) ||
                        ObdPidDecoders.Mode01StandardWidths.ContainsKey(pid),
                        $"{configName}/{request.Name} requests Mode 01 PID 0x{pid:X2}, which has neither " +
                        $"a decoder nor a declared width, so it would truncate the rest of the reply.");
                }
            }
        }

        /// <summary>
        /// PIDs the shipped configs ask for that nothing can decode. Requesting one is not incorrect
        /// - the reply is simply discarded - but it spends bus time for no column, so the set is
        /// pinned rather than tolerated silently: adding another one fails this test, and decoding
        /// one is a deliberate edit here.
        /// </summary>
        /// <remarks>
        /// This doubles as the decoder backlog. Every entry is a parameter someone wanted logged
        /// badly enough to add to a config, and the names come from those configs.
        /// </remarks>
        [Fact]
        public void RequestedPidsWithoutDecoders_MatchTheKnownBacklog()
        {
            string[] expected =
            [
                "Mode01 0x21",   // distance travelled with MIL on
                "Mode01 0x31",   // distance since DTCs cleared
                "Mode22 0x0201", // System Leak Status
                "Mode22 0x020A", // Cat Diag Pre-Cat Sw B1
                "Mode22 0x020B", // Cat Diag Pre-Cat Max Sw
                "Mode22 0x022B", // Cam Angle Error (B1 inlet)
                "Mode22 0x022C", // Idle Speed Error
                "Mode22 0x0232", // Long Term Fuel Trim (B1)
                "Mode22 0x0233", // Short Term Fuel Trim (B1)
                "Mode22 0x023D", // Cam Angle Error (B1 exhaust)
                "Mode22 0x024A", // Engine Speed Error
            ];

            var undecodable = new SortedSet<string>(StringComparer.Ordinal);

            foreach (string configName in MultiECUConfigurationLoader.GetAvailableConfigurations())
            {
                foreach (var request in AllRequests(MultiECUConfigurationLoader.LoadByName(configName)))
                {
                    switch (request)
                    {
                        case Mode01Request mode01:
                            foreach (byte pid in mode01.PIDs)
                            {
                                if (!ObdPidDecoders.Mode01.ContainsKey(pid))
                                    undecodable.Add($"Mode01 0x{pid:X2}");
                            }
                            break;

                        case Mode22Request mode22:
                            ushort pid22 = (ushort)((mode22.PIDHigh << 8) | mode22.PIDLow);
                            if (!ObdPidDecoders.Mode22.ContainsKey(pid22))
                                undecodable.Add($"Mode22 0x{pid22:X4}");
                            break;
                    }
                }
            }

            Assert.Equal(expected, undecodable);
        }

        private static IEnumerable<IOBDRequest> AllRequests(MultiECUConfiguration config) =>
            config.ECUGroups.SelectMany(group => group.Requests);
    }
}
