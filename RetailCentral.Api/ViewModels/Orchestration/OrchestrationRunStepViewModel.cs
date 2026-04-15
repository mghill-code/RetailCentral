using System;

namespace RetailCentral.Api.ViewModels.Orchestration
{
    public class OrchestrationRunStepViewModel
    {
        public long Id { get; set; }
        public int StepOrder { get; set; }
        public string Name { get; set; } = "";
        public string CommandType { get; set; } = "";
        public string StepType { get; set; } = "";
        public string Status { get; set; } = "";
        public int AttemptCount { get; set; }
        public Guid? CommandId { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? StartedUtc { get; set; }
        public DateTime? CompletedUtc { get; set; }
    }
}