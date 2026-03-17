using System.ComponentModel.DataAnnotations;

namespace RetailCentral.Api.Models.Deployments
{
    public class CreateDeploymentRequest
    {
        [Required]
        public int PackageId { get; set; }

        [Required]
        public int TargetType { get; set; }

        [Required, MaxLength(200)]
        public string TargetValue { get; set; } = string.Empty;

        [Required]
        public int ExecuteMode { get; set; }

        public TimeSpan? WindowStartLocal { get; set; }

        public TimeSpan? WindowEndLocal { get; set; }

        public bool UseStoreLocalTime { get; set; } = true;

        public bool AllowOutsideWindow { get; set; } = false;

        [Range(0, 10)]
        public int RetryCount { get; set; } = 0;

        [MaxLength(1000)]
        public string? Notes { get; set; }
    }
}