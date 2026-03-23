using System;
using System.Text.Json;

namespace RetailCentral.Api.Dtos
{
    public class HeartbeatRequest
    {
        public DateTime TimestampUtc { get; set; }
        public string StoreNumber { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string? AgentVersion { get; set; }
        public string? OsVersion { get; set; }

        public JsonElement Metrics { get; set; }
        public JsonElement? Extra { get; set; }

        public HeartbeatInventoryDto? Inventory { get; set; }
    }

    public class HeartbeatInventoryDto
    {
        public string? ComputerName { get; set; }
        public string? Store { get; set; }
        public string? RegisterNumber { get; set; }
        public string? IPAddress { get; set; }
        public string? MACAddress { get; set; }
        public string? Domain { get; set; }
        public string? OSVersion { get; set; }
        public string? CPUArch { get; set; }
        public string? StoreName { get; set; }
        public string? StoreAddress { get; set; }
        public string? StoreCity { get; set; }
        public string? StoreState { get; set; }
        public string? StoreZipCode { get; set; }
        public string? ReleaseLevel { get; set; }
        public string? ReleaseApplied { get; set; }
        public string? VerifoneModel { get; set; }
        public string? VerifoneIP { get; set; }
        public string? ScannerName { get; set; }
        public string? ScannerSerialNumber { get; set; }
    }
}