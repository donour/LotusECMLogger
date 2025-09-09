using SAE.J2534;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// Represents a single OBD-II parameter reading with primitive types
    /// </summary>
    public record VehicleParameterReading
    {
        public required string Name { get; init; }
        public required string Value { get; init; }
        public required string Unit { get; init; }
    }

    public interface IVehicleInfoService
    {
        /// <summary>
        /// Loads vehicle information data from the ECU
        /// </summary>
        /// <returns>List of VehicleParameterReading containing vehicle information</returns>
        List<VehicleParameterReading> LoadVehicleData();
    }
}