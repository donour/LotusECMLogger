using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace LotusECMLogger.Tests
{
    public class J2534OBDLoggerTests
    {

        [Fact]
        public void ReturnsEmptyList_WhenDataIsNotFromECU()
        {
            var data = new byte[] { 0x00, 0x00, 0x07, 0xE7, 0x41, 0x0C, 0x1A, 0x2B };
            var result = LiveDataReading.parseCanResponse(data);
            Assert.Empty(result);
        }

        [Fact]
        public void ParsesEngineSpeedCorrectly()
        {
            // 0x00 0x00 0x07 0xE8 0x41 0x0C 0x1F 0x40
            // 0x1F40 = 8000, /4 = 2000 RPM
            var data = new byte[] { 0x00, 0x00, 0x07, 0xE8, 0x41, 0x0C, 0x1F, 0x40 };
            var result = LiveDataReading.parseCanResponse(data);
            Assert.Single(result);
            var reading = result[0];
            var nameProp = reading.GetType().GetField("name");
            var valueProp = reading.GetType().GetField("value_f");
            Assert.Equal("Engine Speed", nameProp.GetValue(reading));
            Assert.True((2000-reading.value_f) < 1e-10);
        }

        [Fact]
        public void ParsesVehicleSpeedCorrectly()
        {
            // 0x00 0x00 0x07 0xE8 0x41 0x0D 0x64
            // 0x64 = 100 km/h
            var data = new byte[] { 0x00, 0x00, 0x07, 0xE8, 0x41, 0x0D, 0x64 };
            var result = LiveDataReading.parseCanResponse(data);
            Assert.Single(result);
            var reading = result[0];
            var nameProp = reading.GetType().GetField("name");
            var valueProp = reading.GetType().GetField("value_f");
            Assert.Equal("Vehicle Speed", nameProp.GetValue(reading));
            Assert.True((100 - reading.value_f) < 1e-10);
        }

        [Fact]
        public void ParsesTimingAdvanceCorrectly()
        {
            // 0x00 0x00 0x07 0xE8 0x41 0x0E 0x90
            // (0x90 / 2) - 64 = (144/2)-64 = 72-64 = 8
            var data = new byte[] { 0x00, 0x00, 0x07, 0xE8, 0x41, 0x0E, 0x90 };
            var result = LiveDataReading.parseCanResponse(data);
            Assert.Single(result);
            var reading = result[0];
            var nameProp = reading.GetType().GetField("name");
            var valueProp = reading.GetType().GetField("value_f");
            Assert.Equal("Timing Advance", nameProp.GetValue(reading));
            Assert.Equal(8, Convert.ToInt32(valueProp.GetValue(reading)));
        }

        [Fact]
        public void ParsesThrottlePositionCorrectly()
        {
            // 0x00 0x00 0x07 0xE8 0x41 0x11 0xFF
            // 0xFF * 100 / 255 = 100
            var data = new byte[] { 0x00, 0x00, 0x07, 0xE8, 0x41, 0x11, 0xFF };
            var result = LiveDataReading.parseCanResponse(data);
            Assert.Single(result);
            var reading = result[0];
            var nameProp = reading.GetType().GetField("name");
            var valueProp = reading.GetType().GetField("value_f");
            Assert.Equal("Throttle Position", nameProp.GetValue(reading));
            Assert.True((100 - reading.value_f) < 1e-10);
        }

        [Fact]
        public void ParsesMultipleReadings()
        {
            // Engine speed (0x0C), Vehicle speed (0x0D), Throttle position (0x11)
            var data = new byte[] { 0x00, 0x00, 0x07, 0xE8, 0x41, 0x0C, 0x1F, 0x40, 0x0D, 0x64, 0x11, 0xFF };
            var result = LiveDataReading.parseCanResponse(data);
            Assert.Equal(3, result.Count);
        }

        [Fact]
        public void ParsesOctaneRatingMode22()
        {
            // 0x00 0x00 0x07 0xE8 0x62 0x02 0x18 0x8A
            // Octane Rating 18 = 0x8A = 138
            var data = new byte[] { 0x00, 0x00, 0x07, 0xE8, 0x62, 0x02, 0x18, 0x8A };
            var result = LiveDataReading.parseCanResponse(data);
            Assert.Single(result);
            var reading = result[0];
            var nameProp = reading.GetType().GetField("name");
            var valueProp = reading.GetType().GetField("value_f");
            Assert.Equal("Octane Rating 18", nameProp.GetValue(reading));
            Assert.True((138 - reading.value_f) < 1e-10);
        }
    }
}
