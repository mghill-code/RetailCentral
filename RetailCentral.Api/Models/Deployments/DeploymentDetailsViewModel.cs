namespace RetailCentral.Api.Models.Dashboard.Deployments
{
    public class DeploymentDetailsViewModel
    {
        public int Id { get; set; }
        public string PackageName { get; set; } = string.Empty;
        public string? PackageVersion { get; set; }
        public string TargetType { get; set; } = string.Empty;
        public string TargetValue { get; set; } = string.Empty;
        public string ExecuteMode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public string? CreatedBy { get; set; }
        public TimeSpan? WindowStartLocal { get; set; }
        public TimeSpan? WindowEndLocal { get; set; }
        public string? Notes { get; set; }
        public List<DeploymentDeviceRowViewModel> DeviceRows { get; set; } = new();
    }
}