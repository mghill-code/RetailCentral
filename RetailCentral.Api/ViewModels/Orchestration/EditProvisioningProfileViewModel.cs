using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace RetailCentral.Api.ViewModels.Orchestration
{
    public class EditProvisioningProfileViewModel
    {
        public int? Id { get; set; }

        [Required]
        [StringLength(200)]
        [Display(Name = "Profile Name")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Device Type")]
        [StringLength(100)]
        public string? DeviceType { get; set; }

        [Display(Name = "Store Group")]
        [StringLength(200)]
        public string? StoreGroup { get; set; }

        [Display(Name = "Environment")]
        [StringLength(100)]
        public string? Environment { get; set; }

        [Required]
        [Display(Name = "Template")]
        public int? TemplateId { get; set; }

        [Display(Name = "Default Profile")]
        public bool IsDefault { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        public List<SelectListItem> TemplateOptions { get; set; } = new();
        public List<SelectListItem> DeviceTypeOptions { get; set; } = new();
        public List<SelectListItem> EnvironmentOptions { get; set; } = new();
    }
}