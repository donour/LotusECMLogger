namespace LotusECMLogger.Services
{
    public interface IVinSetService
    {
        /// <summary>
        /// Programs a 17-character VIN into the ECU using OBD-II Mode 0x3B.
        /// The ECU only writes positions 3-16; positions 0-2 (WMI) are firmware-protected.
        /// The engine must be off — running engines silently reject the write.
        /// </summary>
        (bool success, string errorMessage) SetVin(string vin);
    }
}
