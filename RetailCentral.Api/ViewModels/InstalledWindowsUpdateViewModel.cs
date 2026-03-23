namespace RetailCentral.Api.ViewModels
{
    public class InstalledWindowsUpdateViewModel
    {
        public string? HotFixId { get; set; }
        public string? Description { get; set; }
        public string? InstalledOn { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }
}