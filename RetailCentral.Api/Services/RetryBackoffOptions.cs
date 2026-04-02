namespace RetailCentral.Api.Services
{
    public class RetryBackoffOptions
    {
        public int BaseDelaySeconds { get; set; } = 30;
        public int MaxDelaySeconds { get; set; } = 300;
        public bool UseExponentialBackoff { get; set; } = true;
    }
}