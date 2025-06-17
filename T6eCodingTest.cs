using System;

namespace LotusECMLogger
{
    /// <summary>
    /// Test program demonstrating the T6eCodingDecoder functionality
    /// </summary>
    public class T6eCodingTest
    {
        public static void Main()
        {
            Console.WriteLine("T6e ECU Coding Decoder Test");
            Console.WriteLine("===========================");
            Console.WriteLine();

            // Example coding data (2 bytes)
            byte[] codingData = { 0x12, 0x34 }; // Example values
            
            try
            {
                // Create decoder instance
                var decoder = new T6eCodingDecoder(codingData);
                
                Console.WriteLine($"Raw coding data: {decoder.ToHexString()}");
                Console.WriteLine($"Bit field: 0x{decoder.BitField:X16}");
                Console.WriteLine();

                // Display all options
                Console.WriteLine("Decoded Options:");
                Console.WriteLine("---------------");
                var allOptions = decoder.GetAllOptions();
                foreach (var option in allOptions)
                {
                    Console.WriteLine($"{option.Key}: {option.Value}");
                }
                
                Console.WriteLine();
                
                // Display raw values
                Console.WriteLine("Raw Values:");
                Console.WriteLine("-----------");
                var rawValues = decoder.GetAllRawValues();
                foreach (var value in rawValues)
                {
                    Console.WriteLine($"{value.Key}: {value.Value}");
                }
                
                Console.WriteLine();
                
                // Example of accessing individual properties
                Console.WriteLine("Individual Properties:");
                Console.WriteLine("---------------------");
                Console.WriteLine($"Driver Position: {decoder.DriverPosition}");
                Console.WriteLine($"Speed Units: {decoder.SpeedUnits}");
                Console.WriteLine($"Transmission Type: {decoder.TransmissionType}");
                Console.WriteLine($"Number of Gears: {decoder.NumberOfGears}");
                Console.WriteLine($"Fuel Tank Capacity: {decoder.FuelTankCapacity}");
                Console.WriteLine($"Sport Button: {decoder.SportButton}");
                Console.WriteLine($"Launch Mode: {decoder.LaunchMode}");
                
                Console.WriteLine();
                
                // Example of using GetOptionValue method
                Console.WriteLine("Using GetOptionValue method:");
                Console.WriteLine("---------------------------");
                Console.WriteLine($"Oil Cooling System: {decoder.GetOptionValue("Oil Cooling System")}");
                Console.WriteLine($"Cruise System: {decoder.GetOptionValue("Cruise System")}");
                Console.WriteLine($"Wheel Profile: {decoder.GetOptionValue("Wheel Profile")}");
                
                Console.WriteLine();
                
                // Display available options
                Console.WriteLine("Available Options:");
                Console.WriteLine("-----------------");
                string[] availableOptions = decoder.GetAvailableOptions();
                foreach (string option in availableOptions)
                {
                    Console.WriteLine($"- {option}");
                }
                
                Console.WriteLine();
                Console.WriteLine("Test completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
} 