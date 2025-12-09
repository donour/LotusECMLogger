using SAE.J2534;

namespace LotusECMLogger.Services
{
	public interface IEcuCodingService
	{
		T6eCodingDecoder ReadCoding();
		(bool success, string errorMessage) WriteCoding(T6eCodingDecoder coding);
	}
}


