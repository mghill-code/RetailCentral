namespace RetailCentral.Api.Models
{
    public class UserActivityHistory
    {
        public long UserActivityHistoryId { get; set; }
        public Guid DeviceId { get; set; }
        public DateTime CapturedUtc { get; set; }
        public DateTime? LastInputUtc { get; set; }
        public int? IdleSeconds { get; set; }
        public string? SessionState { get; set; }
        public bool? IsUserActive { get; set; }
        public bool? IsPosForeground { get; set; }
        public DateTime CreatedUtc { get; set; }
    }
}
