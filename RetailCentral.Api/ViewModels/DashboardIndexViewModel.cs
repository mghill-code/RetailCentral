using System;
using System.Collections.Generic;

namespace RetailCentral.Api.ViewModels
{
    public class DashboardIndexViewModel
    {
        public int TotalDevices { get; set; }
        public int OnlineDevices { get; set; }
        public int OfflineDevices { get; set; }

        public int CommandsPending { get; set; }
        public int CommandsInProgress { get; set; }
        public int CommandsFailedLast24Hours { get; set; }
        public int CommandsSucceededLast24Hours { get; set; }

        public List<DeviceSummaryViewModel> RecentDevices { get; set; } = new();
        public List<CommandSummaryViewModel> RecentCommands { get; set; } = new();
        public List<OrchestrationHomepageRunViewModel> RecentOrchestrationRuns { get; set; } = new();
        public List<VersionSummaryViewModel> VersionSummary { get; set; } = new();
        public List<StoreSummaryTileViewModel> StoreSummary { get; set; } = new();
        public List<StoreOutageTileViewModel> StoreOutages { get; set; } = new();
        public List<DeviceHeatmapTileViewModel> DeviceHeatmap { get; set; } = new();
        public List<CommandProgressViewModel> CommandProgress { get; set; } = new();

        public List<DeviceHealthViewModel> DeviceHealth { get; set; } = new();
        public int HealthyDevices { get; set; }
        public int WarningDevices { get; set; }
        public int CriticalDevices { get; set; }
        public int OrchestrationRunsActive { get; set; }
        public int OrchestrationRunsFailedLast24Hours { get; set; }
        public int OrchestrationTemplatesActive { get; set; }
        public int OrchestrationRunsWaitingForRetry { get; set; }
        public int ActiveProfilesUsingInactiveTemplates { get; set; }
        public int ActiveTemplatesWithoutProfiles { get; set; }
        public int CompletedOrchestrationRunsLast24Hours { get; set; }
        public int ProvisioningProfilesActive { get; set; }
    }

    public class StoreSummaryTileViewModel
    {
        public string StoreNumber { get; set; } = "";
        public int TotalDevices { get; set; }
        public int OnlineDevices { get; set; }
        public int OfflineDevices { get; set; }
    }

    public class StoreOutageTileViewModel
    {
        public string StoreNumber { get; set; } = "";
        public int TotalDevices { get; set; }
        public int OfflineDevices { get; set; }
        public bool IsOutage { get; set; }
    }

    public class DeviceHeatmapTileViewModel
    {
        public Guid DeviceId { get; set; }
        public string StoreNumber { get; set; } = "";
        public string Hostname { get; set; } = "";
        public bool IsOnline { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public string Severity { get; set; } = "";
    }

    public class CommandProgressViewModel
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
        public int Total { get; set; }
        public int Percent { get; set; }
        public string CssClass { get; set; } = "";
    }

    public class DeviceHealthViewModel
    {
        public Guid DeviceId { get; set; }
        public string StoreNumber { get; set; } = "";
        public string Hostname { get; set; } = "";

        public int Score { get; set; }
        public string Status { get; set; } = "Unknown";

        public bool HeartbeatHealthy { get; set; }
        public bool DiskHealthy { get; set; }
        public bool MemoryHealthy { get; set; }
        public bool FailuresHealthy { get; set; }
        public bool InventoryFresh { get; set; }

        public string? HardDriveFreeSpace { get; set; }
        public string? HardDriveSize { get; set; }
        public string? Memory { get; set; }
        public DateTime? LastHeartbeatUtc { get; set; }
        public DateTime? InventoryUpdatedUtc { get; set; }

    }
    public class OrchestrationHomepageRunViewModel
    {
        public long Id { get; set; }
        public string TemplateName { get; set; } = "";
        public int TemplateVersion { get; set; }
        public Guid? DeviceId { get; set; }
        public string Status { get; set; } = "";
        public int? CurrentStepOrder { get; set; }
        public string RequestedBy { get; set; } = "";
        public DateTime StartedUtc { get; set; }
        public DateTime? CompletedUtc { get; set; }
    }
}