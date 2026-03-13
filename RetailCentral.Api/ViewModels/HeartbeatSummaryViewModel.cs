namespace RetailCentral.Api.ViewModels
{
    public class HeartbeatSummaryViewModel
    {
        public long HeartbeatId { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string PayloadJson { get; set; } = "";
    }
}