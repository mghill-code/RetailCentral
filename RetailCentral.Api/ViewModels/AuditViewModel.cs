using RetailCentral.Api.Models;

namespace RetailCentral.Api.ViewModels
{
    public class AuditViewModel
    {
        public List<AuditLog> Results { get; set; } = new();

        public string? UserName { get; set; }
        public string? ActionName { get; set; }
        public string? TargetType { get; set; }
        public string? TargetId { get; set; }

        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }

        public bool? Success { get; set; }
        public int TotalRowsReturnedCount { get; set; }
        public int SuccessfulCount { get; set; }
        public int FailedCount { get; set; }
    }
}