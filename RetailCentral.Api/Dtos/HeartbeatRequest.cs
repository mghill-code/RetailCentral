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

        // flexible: any metrics payload
        public JsonElement Metrics { get; set; }
        public JsonElement? Extra { get; set; }
    }
}