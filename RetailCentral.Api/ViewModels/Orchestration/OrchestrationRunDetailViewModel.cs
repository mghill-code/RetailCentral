using System;
using System.Collections.Generic;

namespace RetailCentral.Api.ViewModels.Orchestration
{
    public class OrchestrationRunDetailViewModel
    {
        public long Id { get; set; }
        public int TemplateId { get; set; }
        public string TemplateName { get; set; } = "";
        public int TemplateVersion { get; set; }

        public Guid? DeviceId { get; set; }
        public int? AgentId { get; set; }
        public int? StoreId { get; set; }
        public int? RegisterId { get; set; }

        public string Status { get; set; } = "";
        public int? CurrentStepOrder { get; set; }
        public string CorrelationId { get; set; } = "";
        public string RequestedBy { get; set; } = "";
        public string TriggerSource { get; set; } = "";

        public DateTime StartedUtc { get; set; }
        public DateTime? CompletedUtc { get; set; }

        public List<OrchestrationRunStepViewModel> Steps { get; set; } = new();
    }
}