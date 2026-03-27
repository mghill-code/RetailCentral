namespace RetailCentral.Api.Models
{
    public class Command
    {
        public Guid CommandId { get; set; }

        // Targeting
        public Guid? DeviceId { get; set; }          // null => store-wide
        public string? StoreNumber { get; set; }     // used for store-targeted commands
        public string? GroupName { get; set; }       // used for group-targeted commands

        // Scope: Device | StoreAllDevices | StoreOnce | Group | Fleet
        public string Scope { get; set; } = "Device";

        public string Type { get; set; } = "";       // e.g. Echo, RestartPOS, CollectProcessStatus
        public string? PayloadJson { get; set; }

        // Status: Pending | InProgress | Succeeded | Failed | Canceled
        // Note: expired pending commands are marked Failed with LastError explaining why.
        public string Status { get; set; } = "Pending";

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresUtc { get; set; }
        public int Priority { get; set; } = 100;

        // Locking (atomic pickup)
        public Guid? LockedByDeviceId { get; set; }
        public DateTime? LockedUtc { get; set; }

        // Retry / diagnostics
        public int AttemptCount { get; set; } = 0;
        public int MaxAttempts { get; set; } = 3;
        public DateTime? LastAttemptUtc { get; set; }
        public string? LastError { get; set; }

        // Audit
        public string? IssuedBy { get; set; }
        public DateTime? IssuedUtc { get; set; }
    }
}
