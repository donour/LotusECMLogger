namespace LotusECMLogger.Services
{
	public interface IDynoModeService
	{
		(bool success, string errorMessage) EnableDynoMode();
	}
}
