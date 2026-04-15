using System;

namespace RetailCentral.Api.ViewModels.Orchestration
{
    public class OrchestrationTemplateListItemViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public int Version { get; set; }
        public string DeviceType { get; set; } = "";
        public string Environment { get; set; } = "";
        public string TriggerType { get; set; } = "";
        public bool IsActive { get; set; }
        public int StepCount { get; set; }
        public int ProvisioningProfileCount { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }
}