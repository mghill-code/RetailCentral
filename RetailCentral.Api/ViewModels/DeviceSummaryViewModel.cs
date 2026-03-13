namespace RetailCentral.Api.ViewModels
{
    public class DeviceSummaryViewModel
    {
        public Guid DeviceId { get; set; }
        public string StoreNumber { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string? AgentVersion { get; set; }
        public DateTime? LastSeenUtc { get; set; }
        public bool IsOnline { get; set; }
    }
}