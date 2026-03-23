using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace RetailCentral.Api.ViewModels.Deployments
{
    public class CreatePackageViewModel
    {
        [Required]
        public string Name { get; set; } = "";

        public string? Version { get; set; }
        public string? Description { get; set; }

        [Required]
        public int PackageType { get; set; }

        public string? FileName { get; set; }
        public string? StoragePath { get; set; }
        public string? Sha256 { get; set; }

        public long FileSizeBytes { get; set; }

        public string? ExecutionCommand { get; set; }
        public string? ExecutionArguments { get; set; }
        public string? WorkingDirectory { get; set; }

        public int TimeoutSeconds { get; set; } = 1800;
        public int RebootBehavior { get; set; }

        public bool StoreInDistro { get; set; }
        public IFormFile? UploadFile { get; set; }

        public string? HostedFileUrl { get; set; }

        public List<SelectListItem> PackageTypes { get; set; } = new();
        public List<SelectListItem> RebootBehaviors { get; set; } = new();
    }
}
