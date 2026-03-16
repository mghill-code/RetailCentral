namespace RetailCentral.Api.ViewModels
{
    public class HelpdeskCommandsViewModel
    {
        public string TargetType { get; set; } = "Device";

        public Guid? DeviceId { get; set; }
        public string? StoreNumber { get; set; }
        public string? GroupName { get; set; }

        public string CommandKey { get; set; } = "CollectSystemInfo";
        public string? Message { get; set; }

        public List<DeviceSummaryViewModel> AvailableDevices { get; set; } = new();
        public List<string> AvailableStores { get; set; } = new();
        public List<string> AvailableGroups { get; set; } = new();

        public List<HelpdeskCommandOptionViewModel> AvailableCommands { get; set; } = new();
    }

    public class HelpdeskCommandOptionViewModel
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";

        public bool DeviceSupported { get; set; } = true;
        public bool StoreSupported { get; set; } = true;
        public bool GroupSupported { get; set; } = true;

        public bool Destructive { get; set; }
    }
}