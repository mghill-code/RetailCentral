namespace RetailCentral.Api.ViewModels.Orchestration
{
    public class ProvisioningProfileListItemViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string DeviceType { get; set; } = "";
        public string StoreGroup { get; set; } = "";
        public string Environment { get; set; } = "";
        public int Priority { get; set; }
        public int TemplateId { get; set; }
        public string TemplateName { get; set; } = "";
        public int TemplateVersion { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
        public string? ParametersJson { get; set; }
    }
}