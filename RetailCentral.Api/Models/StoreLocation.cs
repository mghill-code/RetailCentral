namespace RetailCentral.Api.Models
{
    public class StoreLocation
    {
        public int StoreLocationId { get; set; }

        public string StoreNumber { get; set; } = string.Empty;
        public string? StoreName { get; set; }
        public string? StoreAddress { get; set; }
        public string? StoreCity { get; set; }
        public string? StoreState { get; set; }
        public string? StoreZipCode { get; set; }

        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }

        public string? CoordinatesSource { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }
}