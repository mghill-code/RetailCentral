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
        public List<InstalledSoftwareViewModel> InstalledSoftware { get; set; } = new();
        public List<InstalledWindowsUpdateViewModel> InstalledWindowsUpdates { get; set; } = new();
        public ProcessStatusInventoryViewModel? ProcessStatus { get; set; }
        public DateTime? LastSoftwareInventoryUtc =>
            InstalledSoftware
                .Select(x => (DateTime?)x.UpdatedUtc)
                .Concat(InstalledWindowsUpdates.Select(x => (DateTime?)x.UpdatedUtc))
                .OrderByDescending(x => x)
                .FirstOrDefault();

        // ===== Health Legend =====
        public string HealthLegendHeartbeat { get; set; } = "Heartbeat healthy (last 5 minutes) = +40";
        public string HealthLegendDisk { get; set; } = "Disk space OK (>= 15% free) = +25";
        public string HealthLegendMemory { get; set; } = "Memory OK (>= 4GB) = +15";
        public string HealthLegendFailures { get; set; } = "No command failures in last 24h = +10";
        public string HealthLegendInventory { get; set; } = "Inventory refreshed in last 24h = +10";

        // ===== Remote Assistance =====

        // Prefer IP over hostname (DHCP-safe)
        public string? RemoteAssistanceTarget =>
            !string.IsNullOrWhiteSpace(LastIp)
                ? LastIp
                : !string.IsNullOrWhiteSpace(Hostname)
                    ? Hostname
                    : null;

        // Explicit values (optional for UI display)
        public string? RemoteAssistanceIpTarget =>
            !string.IsNullOrWhiteSpace(LastIp) ? LastIp : null;

        public string? RemoteAssistanceHostTarget =>
            !string.IsNullOrWhiteSpace(Hostname) ? Hostname : null;

        // Command built from preferred target
        public string? RemoteAssistanceCommand =>
            !string.IsNullOrWhiteSpace(RemoteAssistanceTarget)
                ? $"mstsc /shadow:1 /v:{RemoteAssistanceTarget} /noConsentPrompt /control"
                : null;

        public bool CanLaunchRemoteAssistance =>
            !string.IsNullOrWhiteSpace(RemoteAssistanceTarget);

        // Helpful UI label
        public string RemoteAssistanceTargetLabel =>
            !string.IsNullOrWhiteSpace(LastIp)
                ? "IP Address (Preferred)"
                : !string.IsNullOrWhiteSpace(Hostname)
                    ? "Hostname"
                    : "Unavailable";
    }
}