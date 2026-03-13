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
    }
}