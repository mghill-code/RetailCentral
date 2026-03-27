using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RetailCentral.Api.Data;

namespace RetailCentral.Api.Services
{
    public class CommandTimeoutWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CommandTimeoutWorker> _logger;
        private readonly IOptions<CommandTimeoutOptions> _options;

        public CommandTimeoutWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<CommandTimeoutWorker> logger,
            IOptions<CommandTimeoutOptions> options)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _options = options;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogDebug("CommandTimeoutWorker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SweepOnce(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "CommandTimeoutWorker sweep failed.");
                }

                await Task.Delay(TimeSpan.FromSeconds(_options.Value.SweepSeconds), stoppingToken);
            }

            _logger.LogDebug("CommandTimeoutWorker stopped.");
        }

        private async Task SweepOnce(CancellationToken ct)
        {
            var opts = _options.Value;
            var nowUtc = DateTime.UtcNow;
            var inProgressCutoff = nowUtc.AddSeconds(-opts.InProgressTimeoutSeconds);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RetailCentralDbContext>();

            var expiredPending = await db.Commands
                .Where(c =>
                    c.Status == "Pending" &&
                    c.ExpiresUtc != null &&
                    c.ExpiresUtc <= nowUtc)
                .OrderBy(c => c.ExpiresUtc)
                .Take(opts.MaxRowsPerSweep)
                .ToListAsync(ct);

            foreach (var c in expiredPending)
            {
                c.Status = "Failed";
                c.LastError = "Command expired before pickup by agent.";
                c.LastAttemptUtc = nowUtc;
            }

            var stuckInProgress = await db.Commands
                .Where(c =>
                    c.Status == "InProgress" &&
                    c.LockedUtc != null &&
                    c.LockedUtc < inProgressCutoff)
                .OrderBy(c => c.LockedUtc)
                .Take(opts.MaxRowsPerSweep)
                .ToListAsync(ct);

            foreach (var c in stuckInProgress)
            {
                c.AttemptCount += 1;
                c.LastAttemptUtc = nowUtc;

                if (c.AttemptCount >= c.MaxAttempts)
                {
                    c.Status = "Failed";
                    c.LastError = $"Timed out after {c.AttemptCount} attempts (timeout {opts.InProgressTimeoutSeconds}s).";
                }
                else
                {
                    c.Status = "Pending";
                    c.LastError = $"Timed out (timeout {opts.InProgressTimeoutSeconds}s). Requeued attempt {c.AttemptCount}/{c.MaxAttempts}.";
                }

                c.LockedUtc = null;
                c.LockedByDeviceId = null;
            }

            if (expiredPending.Count == 0 && stuckInProgress.Count == 0)
            {
                return;
            }

            await db.SaveChangesAsync(ct);

            if (expiredPending.Count > 0)
            {
                _logger.LogWarning("Marked {Count} expired pending commands as Failed.", expiredPending.Count);
            }

            if (stuckInProgress.Count > 0)
            {
                _logger.LogWarning("Reaped {Count} stuck in-progress commands.", stuckInProgress.Count);
            }
        }
    }

    public class CommandTimeoutOptions
    {
        public int InProgressTimeoutSeconds { get; set; } = 180; // 3 minutes
        public int SweepSeconds { get; set; } = 60;              // run once per minute
        public int MaxRowsPerSweep { get; set; } = 200;
    }
}
