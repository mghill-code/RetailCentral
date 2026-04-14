using RetailCentral.Api.Data.Entities.Orchestration;

namespace RetailCentral.Api.DTOs.Orchestration
{
    public class CreateOrchestrationTemplateRequest
    {
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public int Version { get; set; } = 1;
        public string? DeviceType { get; set; }
        public string? Environment { get; set; }
        public OrchestrationTriggerType TriggerType { get; set; }
        public List<CreateOrchestrationTemplateStepRequest> Steps { get; set; } = new();
    }

    public class CreateOrchestrationTemplateStepRequest
    {
        public int StepOrder { get; set; }
        public string Name { get; set; } = null!;
        public OrchestrationStepType StepType { get; set; }
        public string? CommandType { get; set; }
        public string? ParametersJson { get; set; }
        public string? SuccessCriteriaJson { get; set; }
        public int TimeoutSeconds { get; set; } = 300;
        public int MaxRetries { get; set; } = 0;
        public OrchestrationFailureAction OnFailureAction { get; set; }
        public bool ContinueOnFailure { get; set; }
    }
}