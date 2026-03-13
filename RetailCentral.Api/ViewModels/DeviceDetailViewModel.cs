namespace RetailCentral.Api.ViewModels
{
    public class DeviceDetailViewModel
    {
        public Guid DeviceId { get; set; }
        public string StoreNumber { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string? MachineName { get; set; }
        public string? MachineGuid { get; set; }
        public string? AgentVersion { get; set; }
        public string? OsVersion { get; set; }
        public string? LastIp { get; set; }
        public bool IsEnabled { get; set; }
        public DateTime FirstSeenUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public bool IsOnline { get; set; }

        public List<CommandSummaryViewModel> RecentCommands { get; set; } = new();
        public List<HeartbeatSummaryViewModel> RecentHeartbeats { get; set; } = new();
        public List<string> Groups { get; set; } = new();
        public DeviceHealthViewModel? Health { get; set; }

        public string HealthLegendHeartbeat { get; set; } = "Heartbeat healthy (last 5 minutes) = +40";
        public string HealthLegendDisk { get; set; } = "Disk space OK (>= 15% free) = +25";
        public string HealthLegendMemory { get; set; } = "Memory OK (>= 4GB) = +15";
        public string HealthLegendFailures { get; set; } = "No command failures in last 24h = +10";
        public string HealthLegendInventory { get; set; } = "Inventory refreshed in last 24h = +10";
    }
}