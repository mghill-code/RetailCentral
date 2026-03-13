namespace RetailCentral.Api.Dtos
{
    public class EnrollRequest
    {
        public string StoreNumber { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string? AgentVersion { get; set; }
        public string? OsVersion { get; set; }

        // Day 7 bootstrap fields
        public string? BootstrapKey { get; set; }
        public string? MachineName { get; set; }
        public string? MachineGuid { get; set; }
    }
}