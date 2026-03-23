namespace RetailCentral.Api.ViewModels
{
    public class InstalledSoftwareViewModel
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Publisher { get; set; }
        public string? InstallDate { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }
}