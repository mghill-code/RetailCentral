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
            _logger.LogInformation("CommandTimeoutWorker started.");

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

            _logger.LogInformation("CommandTimeoutWorker stopped.");
        }

        private async Task SweepOnce(CancellationToken ct)
        {
            var opts = _options.Value;
            var cutoff = DateTime.UtcNow.AddSeconds(-opts.InProgressTimeoutSeconds);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RetailCentralDbContext>();

            // Find stuck in-progress commands
            var stuck = await db.Commands
                .Where(c => c.Status == "InProgress"
                            && c.LockedUtc != null
                            && c.LockedUtc < cutoff)
                .OrderBy(c => c.LockedUtc)
                .Take(opts.MaxRowsPerSweep)
                .ToListAsync(ct);

            if (stuck.Count == 0) return;

            foreach (var c in stuck)
            {
                c.AttemptCount += 1;
                c.LastAttemptUtc = DateTime.UtcNow;

                if (c.AttemptCount >= c.MaxAttempts)
                {
                    c.Status = "Failed";
                    c.LastError = $"Timed out after {c.AttemptCount} attempts (timeout {opts.InProgressTimeoutSeconds}s).";
                }
                else
                {
                    // Requeue
                    c.Status = "Pending";
                    c.LastError = $"Timed out (timeout {opts.InProgressTimeoutSeconds}s). Requeued attempt {c.AttemptCount}/{c.MaxAttempts}.";
                }

                c.LockedUtc = null;
                c.LockedByDeviceId = null;
            }

            await db.SaveChangesAsync(ct);

            _logger.LogWarning("Reaped {Count} stuck commands.", stuck.Count);
        }
    }

    public class CommandTimeoutOptions
    {
        public int InProgressTimeoutSeconds { get; set; } = 180; // 3 minutes
        public int SweepSeconds { get; set; } = 60;             // run once per minute
        public int MaxRowsPerSweep { get; set; } = 200;
    }
}