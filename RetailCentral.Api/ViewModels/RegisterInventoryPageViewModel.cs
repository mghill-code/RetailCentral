namespace RetailCentral.Api.ViewModels
{
    public class RegisterInventoryPageViewModel
    {
        public string? Search { get; set; }
        public string? StoreFilter { get; set; }
        public string? StateFilter { get; set; }
        public string? ManufacturerFilter { get; set; }
        public string? ModelFilter { get; set; }
        public string? ReleaseLevelFilter { get; set; }
        public string? ReleaseAppliedFilter { get; set; }
        public string? ScannerNameFilter { get; set; }
        public string? VerifoneModelFilter { get; set; }
        public string? StatusFilter { get; set; }

        public string SortBy { get; set; } = "ComputerName";
        public string SortDir { get; set; } = "asc";

        public List<string> AvailableStores { get; set; } = new();
        public List<string> AvailableStates { get; set; } = new();

        public List<RegisterInventoryRowViewModel> Rows { get; set; } = new();
    }
}