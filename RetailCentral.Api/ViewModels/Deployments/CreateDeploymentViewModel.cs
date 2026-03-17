using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace RetailCentral.Api.ViewModels.Deployments
{
    public class CreateDeploymentViewModel
    {
        [Required]
        public int PackageId { get; set; }

        [Required]
        public int TargetType { get; set; }

        [Required, MaxLength(200)]
        public string TargetValue { get; set; } = string.Empty;

        [Required]
        public int ExecuteMode { get; set; }

        [Display(Name = "Window Start Local")]
        public TimeSpan? WindowStartLocal { get; set; }

        [Display(Name = "Window End Local")]
        public TimeSpan? WindowEndLocal { get; set; }

        public bool UseStoreLocalTime { get; set; } = true;

        public bool AllowOutsideWindow { get; set; } = false;

        [Range(0, 10)]
        public int RetryCount { get; set; } = 0;

        [MaxLength(1000)]
        public string? Notes { get; set; }

        public List<SelectListItem> Packages { get; set; } = new();
        public List<SelectListItem> TargetTypes { get; set; } = new();
        public List<SelectListItem> ExecuteModes { get; set; } = new();

        public string? Message { get; set; }
    }
}