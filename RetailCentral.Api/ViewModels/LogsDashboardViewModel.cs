namespace RetailCentral.Api.ViewModels
{
    public class LogsDashboardViewModel
    {
        public string SelectedReport { get; set; } = "reactivated";

        public List<ReactivatedDeviceLogViewModel> ReactivatedDevices { get; set; } = new();
        public List<LogsDashboardDeviceRowViewModel> InactiveDevices { get; set; } = new();
        public List<LogsDashboardDeviceRowViewModel> NewDevices { get; set; } = new();

        public int Inactive30Days { get; set; }
        public int Inactive60Days { get; set; }
        public int Inactive90Days { get; set; }

        public int New30Days { get; set; }
        public int New60Days { get; set; }
        public int New90Days { get; set; }
    }

    public class LogsDashboardDeviceRowViewModel
    {
        public Guid DeviceId { get; set; }
        public string? StoreNumber { get; set; }
        public string? Hostname { get; set; }
        public string? AgentVersion { get; set; }
        public DateTime FirstSeenUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public bool IsEnabled { get; set; }
    }

    public class ReactivatedDeviceLogViewModel
    {
        public DateTime TimestampUtc { get; set; }
        public string? DeviceId { get; set; }
        public string? Hostname { get; set; }
        public string? StoreNumber { get; set; }
        public string? RemoteIp { get; set; }
        public string? Reason { get; set; }
    }
}