using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data;

namespace RetailCentral.Api.Services
{
    public sealed class DataRetentionService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<DataRetentionService> _logger;
        private readonly IConfiguration _config;

        public DataRetentionService(
            IServiceProvider services,
            ILogger<DataRetentionService> logger,
            IConfiguration config)
        {
            _services = services;
            _logger = logger;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Data retention service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var runHourUtc = GetInt("DataRetention:RunDailyAtHourUtc", 3);
                    var delay = GetDelayUntilNextRunUtc(runHourUtc);

                    _logger.LogInformation("Next data retention run scheduled in {Delay}.", delay);
                    await Task.Delay(delay, stoppingToken);

                    await RunCleanupAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Data retention service failed. Will retry in 1 hour.");
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }

            _logger.LogInformation("Data retention service stopped.");
        }

        private async Task RunCleanupAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RetailCentralDbContext>();

            var heartbeatDays = GetInt("DataRetention:HeartbeatDays", 30);
            var commandResultDays = GetInt("DataRetention:CommandResultDays", 90);
            var completedCommandDays = GetInt("DataRetention:CompletedCommandDays", 90);
            var deleteDevicesAfterDays = GetInt("DataRetention:DeleteDevicesAfterDays", 0);

            var nowUtc = DateTime.UtcNow;

            var heartbeatCutoff = nowUtc.AddDays(-heartbeatDays);
            var commandResultCutoff = nowUtc.AddDays(-commandResultDays);
            var completedCommandCutoff = nowUtc.AddDays(-completedCommandDays);

            _logger.LogInformation(
                "Running data retention cleanup. Heartbeat cutoff={HeartbeatCutoff}, CommandResult cutoff={CommandResultCutoff}, CompletedCommand cutoff={CompletedCommandCutoff}, DeleteDevicesAfterDays={DeleteDevicesAfterDays}",
                heartbeatCutoff,
                commandResultCutoff,
                completedCommandCutoff,
                deleteDevicesAfterDays);

            var deletedHeartbeats = await db.Heartbeats
                .Where(x => x.TimestampUtc < heartbeatCutoff)
                .ExecuteDeleteAsync(ct);

            var deletedCommandResults = await db.CommandResults
                .Where(x => x.FinishedUtc != null && x.FinishedUtc < commandResultCutoff)
                .ExecuteDeleteAsync(ct);

            var deletedCompletedCommands = await db.Commands
                .Where(x =>
                    (x.Status == "Succeeded" || x.Status == "Failed" || x.Status == "Cancelled" || x.Status == "Expired")
                    && x.CreatedUtc < completedCommandCutoff)
                .ExecuteDeleteAsync(ct);

            _logger.LogInformation(
                "Data retention deleted: Heartbeats={DeletedHeartbeats}, CommandResults={DeletedCommandResults}, Commands={DeletedCompletedCommands}",
                deletedHeartbeats,
                deletedCommandResults,
                deletedCompletedCommands);

            if (deleteDevicesAfterDays > 0)
            {
                var deviceCutoff = nowUtc.AddDays(-deleteDevicesAfterDays);

                _logger.LogWarning(
                    "Device deletion is ENABLED. Devices last seen before {DeviceCutoff} will be removed.",
                    deviceCutoff);

                var staleDeviceIds = await db.Devices
                    .Where(d => d.LastSeenUtc < deviceCutoff)
                    .Select(d => d.DeviceId)
                    .ToListAsync(ct);

                if (staleDeviceIds.Count > 0)
                {
                    await db.RegisterInventories
                        .Where(x => staleDeviceIds.Contains(x.DeviceId))
                        .ExecuteDeleteAsync(ct);

                    await db.Devices
                        .Where(x => staleDeviceIds.Contains(x.DeviceId))
                        .ExecuteDeleteAsync(ct);

                    _logger.LogWarning("Deleted {Count} stale devices and their register inventories.", staleDeviceIds.Count);
                }
            }
        }

        private int GetInt(string key, int defaultValue)
        {
            return int.TryParse(_config[key], out var value) ? value : defaultValue;
        }

        private static TimeSpan GetDelayUntilNextRunUtc(int hourUtc)
        {
            var now = DateTime.UtcNow;
            var next = new DateTime(now.Year, now.Month, now.Day, hourUtc, 0, 0, DateTimeKind.Utc);

            if (next <= now)
                next = next.AddDays(1);

            return next - now;
        }
    }
}