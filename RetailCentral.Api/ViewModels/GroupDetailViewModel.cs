namespace RetailCentral.Api.ViewModels
{
    public class GroupDetailViewModel
    {
        public int DeviceGroupId { get; set; }
        public string GroupName { get; set; } = "";
        public string? Description { get; set; }
        public DateTime CreatedUtc { get; set; }

        public DeviceHealthViewModel? Health { get; set; }
        public List<DeviceSummaryViewModel> Members { get; set; } = new();
        public List<DeviceSummaryViewModel> AvailableDevices { get; set; } = new();

        public string? Message { get; set; }
    }
}