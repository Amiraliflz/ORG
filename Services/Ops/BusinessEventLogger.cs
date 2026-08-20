namespace Application.Services.Ops
{
    public interface IBusinessEventLogger
    {
        void LogEvent(string category, string message, object? data = null);
    }

    public class BusinessEventLogger : IBusinessEventLogger
    {
        private readonly ILogger<BusinessEventLogger> _logger;

        public BusinessEventLogger(ILogger<BusinessEventLogger> logger)
        {
            _logger = logger;
        }

        public void LogEvent(string category, string message, object? data = null)
        {
            if (data is null)
                _logger.LogInformation("[Business:{Category}] {Message}", category, message);
            else
                _logger.LogInformation("[Business:{Category}] {Message} {@Data}", category, message, data);
        }
    }
}
