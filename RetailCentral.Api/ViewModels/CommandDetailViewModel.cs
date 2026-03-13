namespace RetailCentral.Api.ViewModels
{
    public class CommandDetailViewModel
    {
        public Guid CommandId { get; set; }
        public string Type { get; set; } = "";
        public string Scope { get; set; } = "";
        public string? StoreNumber { get; set; }
        public string? GroupName { get; set; }
        public Guid? DeviceId { get; set; }
        public string Status { get; set; } = "";
        public string? PayloadJson { get; set; }
        public int Priority { get; set; }
        public int AttemptCount { get; set; }
        public int MaxAttempts { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? LockedUtc { get; set; }
        public Guid? LockedByDeviceId { get; set; }
        public string? LastError { get; set; }
        public string? IssuedBy { get; set; }
        public DateTime? IssuedUtc { get; set; }

        public CommandResultDetailViewModel? Result { get; set; }
    }
}