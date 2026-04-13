public class OrchestrationTemplateStep
{
    public int Id { get; set; }

    public int TemplateId { get; set; }
    public OrchestrationTemplate Template { get; set; } = null!;

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

    public int? RollbackTemplateStepId { get; set; }
}