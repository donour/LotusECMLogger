namespace LotusECMLogger.Services
{
	public sealed class InvalidEcuCodingDataException : InvalidOperationException
	{
		public InvalidEcuCodingDataException(byte[] codingDataLow, byte[] codingDataHigh, Exception innerException)
			: base($"Invalid ECU coding data: {innerException.Message}", innerException)
		{
			CodingDataLow = (byte[])codingDataLow.Clone();
			CodingDataHigh = (byte[])codingDataHigh.Clone();
		}

		public byte[] CodingDataLow { get; }

		public byte[] CodingDataHigh { get; }
	}
}
