using Microsoft.Extensions.Options;

namespace RetailCentral.Api.Services
{
    public class RetryBackoffService
    {
        private readonly RetryBackoffOptions _options;

        public RetryBackoffService(IOptions<RetryBackoffOptions> options)
        {
            _options = options.Value;
        }

        public DateTime GetNextAttemptUtc(int currentAttemptCount, DateTime nowUtc)
        {
            var delaySeconds = GetDelaySeconds(currentAttemptCount);
            return nowUtc.AddSeconds(delaySeconds);
        }

        public int GetDelaySeconds(int currentAttemptCount)
        {
            var attempt = Math.Max(1, currentAttemptCount);

            if (!_options.UseExponentialBackoff)
            {
                return Math.Min(_options.BaseDelaySeconds, _options.MaxDelaySeconds);
            }

            var multiplier = Math.Pow(2, attempt - 1);
            var computed = (int)Math.Round(_options.BaseDelaySeconds * multiplier);

            return Math.Min(computed, _options.MaxDelaySeconds);
        }
    }
}