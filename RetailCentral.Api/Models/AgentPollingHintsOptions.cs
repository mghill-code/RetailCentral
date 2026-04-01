namespace RetailCentral.Api.Models
{
    public sealed class AgentPollingHintsOptions
    {
        public int BusyPollSeconds { get; set; } = 5;
        public int IdlePollSeconds { get; set; } = 30;
    }
}