namespace LotusECMLogger.Services
{
	public interface IObdResetService
	{
		(bool success, string errorMessage) PerformLearningReset();
	}
}


