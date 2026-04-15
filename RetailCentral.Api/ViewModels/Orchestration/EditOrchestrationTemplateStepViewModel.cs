using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace RetailCentral.Api.ViewModels.Orchestration
{
    public class EditOrchestrationTemplateStepViewModel
    {
        public int? Id { get; set; }

        [Required]
        public int TemplateId { get; set; }

        [Required]
        [Range(1, 999)]
        [Display(Name = "Step Order")]
        public int StepOrder { get; set; } = 1;

        [Required]
        [StringLength(200)]
        [Display(Name = "Step Name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Step Type")]
        public int? StepType { get; set; }

        [StringLength(100)]
        [Display(Name = "Command Type")]
        public string? CommandType { get; set; }

        [Range(1, 3600)]
        [Display(Name = "Timeout Seconds")]
        public int TimeoutSeconds { get; set; } = 300;

        [Range(0, 20)]
        [Display(Name = "Max Retries")]
        public int MaxRetries { get; set; } = 1;

        [Required]
        [Display(Name = "On Failure Action")]
        public int? OnFailureAction { get; set; }

        [Display(Name = "Continue On Failure")]
        public bool ContinueOnFailure { get; set; }

        public string TemplateName { get; set; } = string.Empty;

        public bool RequiresCommandType { get; set; }
        public string StepTypeDescription { get; set; } = string.Empty;

        public bool SupportsGuidedParameters { get; set; }
        public string GuidedParameterDescription { get; set; } = string.Empty;

        [Display(Name = "Process Name")]
        [StringLength(200)]
        public string? ValidateProcessName { get; set; }

        [Display(Name = "Package")]
        public int? InstallPackageId { get; set; }

        [Display(Name = "Execute Mode")]
        [StringLength(50)]
        public string? InstallPackageExecuteMode { get; set; }

        public string? SelectedPackageSummary { get; set; }

        [Display(Name = "Destination Path")]
        [StringLength(500)]
        public string? WriteFilePath { get; set; }

        [Display(Name = "File Content")]
        public string? WriteFileContent { get; set; }

        [Display(Name = "Encoding")]
        [StringLength(50)]
        public string? WriteFileEncoding { get; set; }

        [Display(Name = "Overwrite Existing File")]
        public bool WriteFileOverwrite { get; set; }

        public List<SelectListItem> StepTypeOptions { get; set; } = new();
        public List<SelectListItem> CommandTypeOptions { get; set; } = new();
        public List<SelectListItem> OnFailureActionOptions { get; set; } = new();
        public List<SelectListItem> InstallPackageOptions { get; set; } = new();
        public List<SelectListItem> InstallPackageExecuteModeOptions { get; set; } = new();
        public List<SelectListItem> WriteFileEncodingOptions { get; set; } = new();
    }
}
