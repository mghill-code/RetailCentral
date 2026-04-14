using RetailCentral.Api.Data.Entities.Orchestration;

namespace RetailCentral.Api.DTOs.Orchestration
{
    public class OrchestrationRunDto
    {
        public long Id { get; set; }
        public int TemplateId { get; set; }
        public OrchestrationRunStatus Status { get; set; }
        public int? CurrentStepOrder { get; set; }
        public string CorrelationId { get; set; } = null!;
        public DateTime StartedUtc { get; set; }
        public DateTime? CompletedUtc { get; set; }
        public List<OrchestrationStepDto> Steps { get; set; } = new();
    }
}