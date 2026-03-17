using System.ComponentModel.DataAnnotations;

namespace RetailCentral.Api.Data.Entities
{
    public class DeploymentDevice
    {
        public long Id { get; set; }

        public int DeploymentId { get; set; }

        public Guid DeviceId { get; set; }

        [MaxLength(50)]
        public string? StoreNumber { get; set; }

        [MaxLength(200)]
        public string? Hostname { get; set; }

        public int Status { get; set; } = 1;

        public int DownloadStatus { get; set; } = 0;

        public int ExecuteStatus { get; set; } = 0;

        public DateTime? DownloadStartedUtc { get; set; }

        public DateTime? DownloadCompletedUtc { get; set; }

        public DateTime? ExecuteStartedUtc { get; set; }

        public DateTime? ExecuteCompletedUtc { get; set; }

        public int? ExitCode { get; set; }

        [MaxLength(2000)]
        public string? ResultMessage { get; set; }

        public DateTime? LastHeartbeatUtc { get; set; }

        [MaxLength(500)]
        public string? FilePath { get; set; }

        public int AttemptCount { get; set; } = 0;

        public Deployment? Deployment { get; set; }
    }
}