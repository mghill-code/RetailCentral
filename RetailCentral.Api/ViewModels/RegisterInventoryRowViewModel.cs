namespace RetailCentral.Api.ViewModels
{
    public class RegisterInventoryRowViewModel
    {
        public Guid DeviceId { get; set; }

        public string? ComputerName { get; set; }
        public string? Store { get; set; }
        public string? RegisterNumber { get; set; }

        public string? IPAddress { get; set; }
        public string? MACAddress { get; set; }

        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }

        public string? Memory { get; set; }
        public string? HardDriveSize { get; set; }
        public string? HardDriveFreeSpace { get; set; }

        public string? StoreName { get; set; }
        public string? StoreAddress { get; set; }
        public string? StoreCity { get; set; }
        public string? StoreState { get; set; }
        public string? StoreZipCode { get; set; }

        public string? ReleaseLevel { get; set; }
        public string? ReleaseApplied { get; set; }

        public string? Domain { get; set; }
        public DateTime? LastReboot { get; set; }
        public DateTime? SystemBuildDate { get; set; }

        public string? OSVersion { get; set; }
        public string? CPUArch { get; set; }

        public string? VerifoneModel { get; set; }
        public string? VerifoneIP { get; set; }

        public string? ScannerName { get; set; }
        public string? ScannerSerialNumber { get; set; }

        public DateTime? LastHeartbeatUtc { get; set; }
    }
}