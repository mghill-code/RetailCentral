namespace RetailCentral.Api.ViewModels
{
    public class FleetMapViewModel
    {
        public string Mode { get; set; } = "All";

        public int TotalStores { get; set; }
        public int TotalDevices { get; set; }
        public int OnlineDevices { get; set; }
        public int OfflineDevices { get; set; }

        public List<FleetMapStateSummaryViewModel> States { get; set; } = new();
        public List<FleetMapStoreViewModel> Stores { get; set; } = new();
    }

    public class FleetMapStateSummaryViewModel
    {
        public string State { get; set; } = "";
        public int StoreCount { get; set; }
        public int TotalDevices { get; set; }
        public int OnlineDevices { get; set; }
        public int OfflineDevices { get; set; }
        public string Severity { get; set; } = "Healthy";
    }

    public class FleetMapStoreViewModel
    {
        public string StoreNumber { get; set; } = "";
        public string StoreName { get; set; } = "";
        public string StoreAddress { get; set; } = "";
        public string StoreCity { get; set; } = "";
        public string StoreState { get; set; } = "";
        public string StoreZipCode { get; set; } = "";

        public int TotalDevices { get; set; }
        public int OnlineDevices { get; set; }
        public int OfflineDevices { get; set; }

        public string Severity { get; set; } = "Healthy";
        public DateTime? LastSeenUtc { get; set; }

        public bool HasCoordinates { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
    }
}