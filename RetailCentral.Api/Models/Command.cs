namespace RetailCentral.Api.Models
{
    public class Command
    {
        public Guid CommandId { get; set; }

        // Targeting
        public Guid? DeviceId { get; set; }          // null => store-wide
        public string? StoreNumber { get; set; }     // required for store-wide

        // Scope: Device | StoreAllDevices | StoreOnce
        public string Scope { get; set; } = "Device";

        public string Type { get; set; } = "";       // e.g., Echo, RestartPOS
        public string? PayloadJson { get; set; }

        // Status: Pending | InProgress | Succeeded | Failed | Expired | Canceled
        public string Status { get; set; } = "Pending";

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresUtc { get; set; }
        public int Priority { get; set; } = 100;

        // Locking (atomic pickup)
        public Guid? LockedByDeviceId { get; set; }
        public DateTime? LockedUtc { get; set; }
        public int AttemptCount { get; set; } = 0;
        public int MaxAttempts { get; set; } = 3;
        public DateTime? LastAttemptUtc { get; set; }
        public string? LastError { get; set; }
    }
}