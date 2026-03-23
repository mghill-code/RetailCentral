namespace RetailCentral.Api.Models
{
    public class ProcessStatusInventory
    {
        public long ProcessStatusInventoryId { get; set; }
        public Guid DeviceId { get; set; }

        public string? PosProcessName { get; set; }
        public bool PosRunning { get; set; }
        public int PosProcessCount { get; set; }
        public decimal? PosCpuPercent { get; set; }
        public decimal? PosWorkingSetMb { get; set; }
        public DateTime? PosStartedAtLocal { get; set; }

        public string? RetailShellProcessName { get; set; }
        public bool RetailShellRunning { get; set; }
        public int RetailShellProcessCount { get; set; }
        public decimal? RetailShellCpuPercent { get; set; }
        public decimal? RetailShellWorkingSetMb { get; set; }
        public DateTime? RetailShellStartedAtLocal { get; set; }

        public string? AgentProcessName { get; set; }
        public bool AgentRunning { get; set; }
        public int AgentProcessCount { get; set; }
        public decimal? AgentCpuPercent { get; set; }
        public decimal? AgentWorkingSetMb { get; set; }
        public DateTime? AgentStartedAtLocal { get; set; }

        public DateTime UpdatedUtc { get; set; }
    }
}