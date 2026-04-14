namespace RetailCentral.Api.DTOs.Orchestration
{
    public class CreateProvisioningProfileRequest
    {
        public string Name { get; set; } = null!;
        public string? DeviceType { get; set; }
        public string? StoreGroup { get; set; }
        public string? Environment { get; set; }
        public int TemplateId { get; set; }
        public string? ParametersJson { get; set; }
        public bool IsDefault { get; set; }
    }
}