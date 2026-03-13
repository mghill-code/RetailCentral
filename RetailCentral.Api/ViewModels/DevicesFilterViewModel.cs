namespace RetailCentral.Api.ViewModels
{
    public class DevicesFilterViewModel
    {
        public string? Search { get; set; }
        public string? StoreNumber { get; set; }
        public string? GroupName { get; set; }
        public string? Status { get; set; }   // Online / Offline / All

        public List<string> AvailableStores { get; set; } = new();
        public List<string> AvailableGroups { get; set; } = new();

        public List<DeviceSummaryViewModel> Devices { get; set; } = new();
    }
}