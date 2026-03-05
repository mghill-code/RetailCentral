namespace RetailCentral.Api.Models
{
    public class CommandResult
    {
        public long CommandResultId { get; set; }

        public Guid CommandId { get; set; }
        public Guid DeviceId { get; set; }

        public string Status { get; set; } = "";   // Succeeded | Failed
        public int? ExitCode { get; set; }
        public string? StdOut { get; set; }
        public string? StdErr { get; set; }

        public DateTime? StartedUtc { get; set; }
        public DateTime? FinishedUtc { get; set; }
    }
}