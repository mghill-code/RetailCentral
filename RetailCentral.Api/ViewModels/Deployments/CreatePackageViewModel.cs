using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace RetailCentral.Api.ViewModels.Deployments
{
    public class CreatePackageViewModel
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Version { get; set; }

        [MaxLength(1000)]
        public string? Description { get; set; }

        [Required]
        public int PackageType { get; set; }

        [Required, MaxLength(260)]
        public string FileName { get; set; } = string.Empty;

        [Required, MaxLength(1000)]
        public string StoragePath { get; set; } = string.Empty;

        [Required, MaxLength(128)]
        public string Sha256 { get; set; } = string.Empty;

        public long? FileSizeBytes { get; set; }

        [MaxLength(500)]
        public string? ExecutionCommand { get; set; }

        [MaxLength(2000)]
        public string? ExecutionArguments { get; set; }

        [MaxLength(500)]
        public string? WorkingDirectory { get; set; }

        [Range(1, 86400)]
        public int TimeoutSeconds { get; set; } = 1800;

        public int RebootBehavior { get; set; } = 0;

        public List<SelectListItem> PackageTypes { get; set; } = new();
        public List<SelectListItem> RebootBehaviors { get; set; } = new();
    }
}