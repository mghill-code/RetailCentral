using RetailCentral.Api.ViewModels;

namespace RetailCentral.Api.ViewModels
{
    public class LogsDashboardViewModel
    {
        public List<ReactivatedDeviceLogViewModel> ReactivatedDevices { get; set; } = new();

        public int Inactive30Days { get; set; }
        public int Inactive60Days { get; set; }
        public int Inactive90Days { get; set; }

        public int New30Days { get; set; }
        public int New60Days { get; set; }
        public int New90Days { get; set; }
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