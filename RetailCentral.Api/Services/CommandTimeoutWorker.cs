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
        private readonly RetryBackoffService _retryBackoffService;

        public CommandTimeoutWorker(
             IServiceScopeFactory scopeFactory,
             ILogger<CommandTimeoutWorker> logger,
             IOptions<CommandTimeoutOptions> options,
             RetryBackoffService retryBackoffService)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _options = options;
            _retryBackoffService = retryBackoffService;
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
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "CommandTimeoutWorker sweep failed.");
                }

                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(Math.Max(5, _options.Value.SweepSeconds)),
                        stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.LogInformation("CommandTimeoutWorker stopped.");
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

            foreach (var command in expiredPending)
            {
                command.Status = "Failed";
                command.NextAttemptUtc = null;
                command.LastError = "Command expired before pickup by agent.";
                command.LastAttemptUtc ??= command.CreatedUtc;
                command.LockedUtc = null;
                command.LockedByDeviceId = null;
            }

            var stuckInProgress = await db.Commands
                .Where(c =>
                    c.Status == "InProgress" &&
                    c.LockedUtc != null &&
                    c.LockedUtc < inProgressCutoff)
                .OrderBy(c => c.LockedUtc)
                .Take(opts.MaxRowsPerSweep)
                .ToListAsync(ct);

            foreach (var command in stuckInProgress)
            {
                var reachedMaxAttempts = command.AttemptCount >= Math.Max(1, command.MaxAttempts);

                if (reachedMaxAttempts)
                {
                    command.Status = "Failed";
                    command.NextAttemptUtc = null;
                    command.LastError =
                        $"Command timed out after {command.AttemptCount} attempt(s) " +
                        $"(timeout threshold: {opts.InProgressTimeoutSeconds}s).";
                }
                else
                {
                    var nextAttemptUtc = _retryBackoffService.GetNextAttemptUtc(command.AttemptCount, nowUtc);
                    command.Status = "Pending";
                    command.NextAttemptUtc = nextAttemptUtc;
                    command.LastError =
                        $"Recovered stale in-progress command. Requeued for retry at {nextAttemptUtc:O}.";
                }

                // AttemptCount was already incremented when the command was claimed.
                command.LockedUtc = null;
                command.LockedByDeviceId = null;
            }

            if (expiredPending.Count == 0 && stuckInProgress.Count == 0)
            {
                _logger.LogDebug("CommandTimeoutWorker sweep completed. No expired or stuck commands found.");
                return;
            }

            await db.SaveChangesAsync(ct);

            if (expiredPending.Count > 0)
            {
                _logger.LogWarning(
                    "Marked {Count} pending command(s) as Failed because they expired before pickup.",
                    expiredPending.Count);
            }

            if (stuckInProgress.Count > 0)
            {
                var requeuedCount = stuckInProgress.Count(c => c.Status == "Pending");
                var failedCount = stuckInProgress.Count(c => c.Status == "Failed");

                _logger.LogWarning(
                    "Recovered {Total} stale in-progress command(s). Requeued={Requeued}, Failed={Failed}.",
                    stuckInProgress.Count,
                    requeuedCount,
                    failedCount);
            }
        }

        public class CommandTimeoutOptions
        {
            public int InProgressTimeoutSeconds { get; set; } = 180; // 3 minutes
            public int SweepSeconds { get; set; } = 60;              // run once per minute
            public int MaxRowsPerSweep { get; set; } = 200;
        }
    }
}