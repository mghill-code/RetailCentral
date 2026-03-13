namespace RetailCentral.Api.ViewModels
{
    public class VersionSummaryViewModel
    {
        public string? AgentVersion { get; set; }
        public int DeviceCount { get; set; }
        public int OnlineCount { get; set; }
        public int OfflineCount { get; set; }
    }
}