using RetailCentral.Api.Data.Entities.Orchestration;

namespace RetailCentral.Api.DTOs.Orchestration
{
    public class OrchestrationStepDto
    {
        public long Id { get; set; }
        public int StepOrder { get; set; }

        // New: include descriptive template step details for debugging / UI visibility
        public string? Name { get; set; }
        public string? CommandType { get; set; }
        public OrchestrationStepType? StepType { get; set; }

        public OrchestrationRunStepStatus Status { get; set; }
        public int AttemptCount { get; set; }
        public Guid? CommandId { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? StartedUtc { get; set; }
        public DateTime? CompletedUtc { get; set; }
    }
}