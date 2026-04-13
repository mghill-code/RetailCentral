public class ProvisioningProfile
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;
    public string? DeviceType { get; set; }
    public string? StoreGroup { get; set; }
    public string? Environment { get; set; }

    public int TemplateId { get; set; }
    public OrchestrationTemplate Template { get; set; } = null!;

    public string? ParametersJson { get; set; }

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}