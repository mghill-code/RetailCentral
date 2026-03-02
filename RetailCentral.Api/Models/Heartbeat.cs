namespace RetailCentral.Api.Models
{
    public class Heartbeat
    {
        public long HeartbeatId { get; set; }
        public Guid DeviceId { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string PayloadJson { get; set; } = "{}";
        public Device? Device { get; set; }
    }
}   