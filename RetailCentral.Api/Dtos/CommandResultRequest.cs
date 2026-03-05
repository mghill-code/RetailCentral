using System;

namespace RetailCentral.Api.Dtos
{
    public class CommandResultRequest
    {
        public string Status { get; set; } = ""; // Succeeded | Failed
        public int? ExitCode { get; set; }
        public string? StdOut { get; set; }
        public string? StdErr { get; set; }
        public DateTime? StartedUtc { get; set; }
        public DateTime? FinishedUtc { get; set; }
    }
}