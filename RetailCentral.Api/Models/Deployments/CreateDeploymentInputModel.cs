using System.ComponentModel.DataAnnotations;

namespace RetailCentral.Api.Models.Dashboard.Deployments
{
    public class CreateDeploymentInputModel
    {
        [Required]
        public int PackageId { get; set; }

        [Required]
        public int TargetType { get; set; }

        [Required]
        [StringLength(200)]
        public string TargetValue { get; set; } = string.Empty;

        [Required]
        public int ExecuteMode { get; set; }

        public TimeSpan? WindowStartLocal { get; set; }
        public TimeSpan? WindowEndLocal { get; set; }

        public bool UseStoreLocalTime { get; set; } = true;
        public bool AllowOutsideWindow { get; set; }
        public int RetryCount { get; set; }
        public string? Notes { get; set; }
    }
}