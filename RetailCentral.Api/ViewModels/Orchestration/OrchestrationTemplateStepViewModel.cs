namespace RetailCentral.Api.ViewModels.Orchestration
{
    public class OrchestrationTemplateStepViewModel
    {
        public int Id { get; set; }
        public int StepOrder { get; set; }
        public string Name { get; set; } = "";
        public string StepType { get; set; } = "";
        public string CommandType { get; set; } = "";
        public string? ParametersJson { get; set; }
        public string? SuccessCriteriaJson { get; set; }
        public int TimeoutSeconds { get; set; }
        public int MaxRetries { get; set; }
        public string OnFailureAction { get; set; } = "";
        public bool ContinueOnFailure { get; set; }
        public int? RollbackTemplateStepId { get; set; }
        public bool HasRunHistory { get; set; }
    }
}