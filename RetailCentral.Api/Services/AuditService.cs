using System.Text.Json;
using RetailCentral.Api.Data;
using RetailCentral.Api.Models;

namespace RetailCentral.Api.Services
{
    public class AuditService
    {
        private readonly RetailCentralDbContext _db;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditService(RetailCentralDbContext db, IHttpContextAccessor httpContextAccessor)
        {
            _db = db;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task LogAsync(
            string action,
            string? targetType = null,
            string? targetId = null,
            object? details = null,
            bool success = true,
            string? errorMessage = null,
            CancellationToken cancellationToken = default)
        {
            var http = _httpContextAccessor.HttpContext;
            var userName = http?.User?.Identity?.Name ?? "UNKNOWN";
            var ipAddress = http?.Connection?.RemoteIpAddress?.ToString();

            var log = new AuditLog
            {
                TimestampUtc = DateTime.UtcNow,
                UserName = userName,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                Details = details == null ? null : JsonSerializer.Serialize(details),
                IpAddress = ipAddress,
                Success = success,
                ErrorMessage = errorMessage
            };

            _db.AuditLogs.Add(log);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}