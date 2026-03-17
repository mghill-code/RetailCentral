namespace RetailCentral.Api.Models.Dashboard.Deployments
{
    public class DeploymentListItemViewModel
    {
        public int Id { get; set; }
        public string PackageName { get; set; } = string.Empty;
        public string TargetType { get; set; } = string.Empty;
        public string TargetValue { get; set; } = string.Empty;
        public string ExecuteMode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public int DeviceCount { get; set; }
        public int CompletedCount { get; set; }
        public int FailedCount { get; set; }
    }
}