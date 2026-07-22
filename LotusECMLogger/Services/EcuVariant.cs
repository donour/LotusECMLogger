namespace LotusECMLogger.Services
{
	/// <summary>
	/// ECU hardware/firmware generation. Each variant has its own flash memory map; addresses
	/// come from the zone table in the reference lotusecu-tools dumper (lib/ltacc.py).
	/// </summary>
	public enum EcuVariant
	{
		/// <summary>T4e (MPC563): 512KB flash, 32KB RAM, 2KB DECRAM.</summary>
		T4e,

		/// <summary>K4 (29F200 external flash): 256KB flash, 128KB RAM.</summary>
		K4,

		/// <summary>T4 (29F400 external flash): 512KB flash, 128KB RAM.</summary>
		T4,

		/// <summary>T6 (MPC5534): 1MB flash, 64KB RAM.</summary>
		T6,
	}
}
