using System.ComponentModel.DataAnnotations;

namespace RetailCentral.Api.Data.Entities
{
    public class Deployment
    {
        public int Id { get; set; }

        public int PackageId { get; set; }

        public int TargetType { get; set; }

        [MaxLength(200)]
        public string TargetValue { get; set; } = string.Empty;

        public int ExecuteMode { get; set; }

        public TimeSpan? WindowStartLocal { get; set; }

        public TimeSpan? WindowEndLocal { get; set; }

        public bool UseStoreLocalTime { get; set; } = true;

        public bool AllowOutsideWindow { get; set; } = false;

        public int RetryCount { get; set; } = 0;

        public int Status { get; set; } = 1;

        [MaxLength(1000)]
        public string? Notes { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        [MaxLength(100)]
        public string? CreatedBy { get; set; }

        public DateTime? StartedUtc { get; set; }

        public DateTime? CompletedUtc { get; set; }

        public Package? Package { get; set; }

        public ICollection<DeploymentDevice> DeploymentDevices { get; set; } = new List<DeploymentDevice>();
    }
}