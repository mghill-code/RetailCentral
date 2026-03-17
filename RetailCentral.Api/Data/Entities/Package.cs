using System.ComponentModel.DataAnnotations;

namespace RetailCentral.Api.Data.Entities
{
    public class Package
    {
        public int Id { get; set; }

        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Version { get; set; }

        [MaxLength(1000)]
        public string? Description { get; set; }

        public int PackageType { get; set; }

        [MaxLength(260)]
        public string FileName { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string StoragePath { get; set; } = string.Empty;

        [MaxLength(128)]
        public string Sha256 { get; set; } = string.Empty;

        public long? FileSizeBytes { get; set; }

        [MaxLength(500)]
        public string? ExecutionCommand { get; set; }

        [MaxLength(2000)]
        public string? ExecutionArguments { get; set; }

        [MaxLength(500)]
        public string? WorkingDirectory { get; set; }

        public int TimeoutSeconds { get; set; } = 1800;

        public int RebootBehavior { get; set; } = 0;

        public bool IsEnabled { get; set; } = true;

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        [MaxLength(100)]
        public string? CreatedBy { get; set; }

        public ICollection<Deployment> Deployments { get; set; } = new List<Deployment>();
    }
}