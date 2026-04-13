using RetailCentral.Api.Data.Entities.Orchestration;

public class OrchestrationTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int Version { get; set; }

    public string? DeviceType { get; set; }
    public string? Environment { get; set; }

    public bool IsActive { get; set; } = true;
    public OrchestrationTriggerType TriggerType { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public ICollection<OrchestrationTemplateStep> Steps { get; set; } = new List<OrchestrationTemplateStep>();
    public ICollection<ProvisioningProfile> ProvisioningProfiles { get; set; } = new List<ProvisioningProfile>();
    public ICollection<OrchestrationRun> Runs { get; set; } = new List<OrchestrationRun>();
}