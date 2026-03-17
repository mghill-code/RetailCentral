namespace RetailCentral.Api.Models.Deployments
{
    public class DeploymentDetailResponse
    {
        public int DeploymentId { get; set; }
        public string PackageName { get; set; } = string.Empty;
        public string? PackageVersion { get; set; }
        public int TargetType { get; set; }
        public string TargetValue { get; set; } = string.Empty;
        public int ExecuteMode { get; set; }
        public TimeSpan? WindowStartLocal { get; set; }
        public TimeSpan? WindowEndLocal { get; set; }
        public int Status { get; set; }
        public DateTime CreatedUtc { get; set; }
        public List<DeploymentDeviceItemResponse> Devices { get; set; } = new();
    }

    public class DeploymentDeviceItemResponse
    {
        public Guid DeviceId { get; set; }
        public string? Hostname { get; set; }
        public string? StoreNumber { get; set; }
        public int Status { get; set; }
        public int DownloadStatus { get; set; }
        public int ExecuteStatus { get; set; }
        public int? ExitCode { get; set; }
        public string? ResultMessage { get; set; }
        public DateTime? LastHeartbeatUtc { get; set; }
    }
}