public class OrchestrationRunStep
{
    public long Id { get; set; }

    public long RunId { get; set; }
    public OrchestrationRun Run { get; set; } = null!;

    public int TemplateStepId { get; set; }
    public OrchestrationTemplateStep TemplateStep { get; set; } = null!;

    public int StepOrder { get; set; }
    public OrchestrationRunStepStatus Status { get; set; }

    public int AttemptCount { get; set; }

    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }

    public string? ResultJson { get; set; }
    public string? ErrorMessage { get; set; }
    public string? LogsJson { get; set; }

    // Important: future correlation to your existing command table
    public long? CommandId { get; set; }
}