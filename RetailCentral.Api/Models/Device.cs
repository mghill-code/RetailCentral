using System;

namespace RetailCentral.Api.Models
{
    public class Device
    {
        public Guid DeviceId { get; set; }

        public string StoreNumber { get; set; } = "";
        public string Hostname { get; set; } = "";

        // Day 1: placeholder. Day 2: add Secret storage for HMAC.
        public bool IsEnabled { get; set; } = true;

        public DateTime FirstSeenUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }

        public string? LastIp { get; set; }
        public string? AgentVersion { get; set; }
        public string? OsVersion { get; set; }
        public string? DeviceSecret { get; set; } // base64 secret for HMAC (we’ll harden later)

        public long? LastAuthTimestampUnix { get; set; }
        public string? MachineName { get; set; }
        public string? MachineGuid { get; set; }
    }
}