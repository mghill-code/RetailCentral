using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace RetailCentral.Api.ViewModels.Orchestration
{
    public class CreateOrchestrationRunViewModel
    {
        [Required]
        public int TemplateId { get; set; }

        public string TemplateName { get; set; } = string.Empty;
        public int TemplateVersion { get; set; }
        public string DeviceType { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Target Device")]
        public Guid? DeviceId { get; set; }

        [Display(Name = "Requested By")]
        [StringLength(200)]
        public string RequestedBy { get; set; } = string.Empty;

        [Display(Name = "Correlation ID")]
        [StringLength(100)]
        public string? CorrelationId { get; set; }

        public List<SelectListItem> DeviceOptions { get; set; } = new();
    }
}