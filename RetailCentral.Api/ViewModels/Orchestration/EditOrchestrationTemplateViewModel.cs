using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace RetailCentral.Api.ViewModels.Orchestration
{
    public class EditOrchestrationTemplateViewModel
    {
        public int? Id { get; set; }

        [Required]
        [StringLength(200)]
        [Display(Name = "Template Name")]
        public string Name { get; set; } = string.Empty;

        [StringLength(1000)]
        [Display(Name = "Description")]
        public string? Description { get; set; }

        [Range(1, 9999)]
        [Display(Name = "Version")]
        public int Version { get; set; } = 1;

        [Required]
        [StringLength(100)]
        [Display(Name = "Device Type")]
        public string DeviceType { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Display(Name = "Environment")]
        public string Environment { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Trigger Type")]
        public int? TriggerType { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; }

        public List<SelectListItem> DeviceTypeOptions { get; set; } = new();
        public List<SelectListItem> EnvironmentOptions { get; set; } = new();
        public List<SelectListItem> TriggerTypeOptions { get; set; } = new();
    }
}