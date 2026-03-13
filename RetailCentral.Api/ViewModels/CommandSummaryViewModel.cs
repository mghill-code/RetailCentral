namespace RetailCentral.Api.ViewModels
{
    public class CommandSummaryViewModel
    {
        public Guid CommandId { get; set; }
        public string Type { get; set; } = "";
        public string Scope { get; set; } = "";
        public string? StoreNumber { get; set; }
        public string? GroupName { get; set; }
        public Guid? DeviceId { get; set; }
        public string Status { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
    }
}