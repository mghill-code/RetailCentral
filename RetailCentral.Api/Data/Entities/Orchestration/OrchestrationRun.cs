using System;
using System.Collections.Generic;

namespace RetailCentral.Api.Data.Entities.Orchestration
{
    public class OrchestrationRun
    {
        public long Id { get; set; }

        public int TemplateId { get; set; }
        public OrchestrationTemplate Template { get; set; } = null!;

        public int? StoreId { get; set; }
        public int? RegisterId { get; set; }
        public int? AgentId { get; set; }
        public Guid? DeviceId { get; set; }

        public OrchestrationRunStatus Status { get; set; }
        public int? CurrentStepOrder { get; set; }

        public OrchestrationTriggerSource TriggerSource { get; set; }
        public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");

        public string? RequestedBy { get; set; }
        public string? ParametersJson { get; set; }

        public DateTime StartedUtc { get; set; }
        public DateTime? CompletedUtc { get; set; }

        public ICollection<OrchestrationRunStep> Steps { get; set; } = new List<OrchestrationRunStep>();
    }
}