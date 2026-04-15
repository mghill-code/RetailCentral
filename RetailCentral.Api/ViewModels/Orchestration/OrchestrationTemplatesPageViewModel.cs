using System.Collections.Generic;

namespace RetailCentral.Api.ViewModels.Orchestration
{
    public class OrchestrationTemplatesPageViewModel
    {
        public string? Search { get; set; }
        public string? DeviceTypeFilter { get; set; }
        public string? EnvironmentFilter { get; set; }
        public bool? ActiveFilter { get; set; }

        public int TotalTemplates { get; set; }
        public int ActiveTemplates { get; set; }
        public int InactiveTemplates { get; set; }
        public string? FilterMode { get; set; }
        public List<OrchestrationTemplateListItemViewModel> Templates { get; set; } = new();
    }
}