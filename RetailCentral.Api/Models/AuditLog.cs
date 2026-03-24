namespace RetailCentral.Api.Models
{
    public class AuditLog
    {
        public int Id { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string UserName { get; set; } = default!;
        public string Action { get; set; } = default!;
        public string? TargetType { get; set; }
        public string? TargetId { get; set; }
        public string? Details { get; set; }
        public string? IpAddress { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}