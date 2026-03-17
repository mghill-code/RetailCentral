namespace RetailCentral.Api.Models.Dashboard.Deployments
{
    public class DeploymentDeviceRowViewModel
    {
        public Guid DeviceId { get; set; }
        public string? StoreNumber { get; set; }
        public string? Hostname { get; set; }
        public string Status { get; set; } = string.Empty;
        public int DownloadStatus { get; set; }
        public int ExecuteStatus { get; set; }
        public string? ResultMessage { get; set; }
        public int? ExitCode { get; set; }
        public DateTime? LastHeartbeatUtc { get; set; }
    }
}