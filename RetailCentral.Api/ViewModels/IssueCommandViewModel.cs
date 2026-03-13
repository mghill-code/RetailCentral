namespace RetailCentral.Api.ViewModels
{
    public class IssueCommandViewModel
    {
        public Guid? DeviceId { get; set; }
        public string? StoreNumber { get; set; }
        public string? GroupName { get; set; }

        public string Type { get; set; } = "";
        public string? TemplateName { get; set; }
        public bool UseCustomJson { get; set; }
        public string? PayloadJson { get; set; }

        public int Priority { get; set; } = 100;
        public int MaxAttempts { get; set; } = 3;
        public DateTime? ExpiresUtc { get; set; }

        public List<DeviceSummaryViewModel> AvailableDevices { get; set; } = new();
        public List<string> AvailableStores { get; set; } = new();
        public List<string> AvailableGroups { get; set; } = new();

        public List<string> AvailableCommandTypes { get; set; } = new();
        public List<CommandTemplateOptionViewModel> AvailableTemplates { get; set; } = new();

        public string? Message { get; set; }
        public Guid? CreatedCommandId { get; set; }
    }

    public class CommandTemplateOptionViewModel
    {
        public string Name { get; set; } = "";
        public string CommandType { get; set; } = "";
        public string PayloadJson { get; set; } = "";
        public string? Description { get; set; }
    }
}