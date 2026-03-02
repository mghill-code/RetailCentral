namespace RetailCentral.Api.Dtos
{
    public class EnrollRequest
    {
        public string StoreNumber { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string? AgentVersion { get; set; }
        public string? OsVersion { get; set; }
    }
}