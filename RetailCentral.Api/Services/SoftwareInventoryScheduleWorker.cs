using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data;
using RetailCentral.Api.Models;

namespace RetailCentral.Api.Services
{
    public sealed class SoftwareInventoryScheduleWorker : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<SoftwareInventoryScheduleWorker> _logger;
        private readonly IConfiguration _config;

        public SoftwareInventoryScheduleWorker(
            IServiceProvider services,
            ILogger<SoftwareInventoryScheduleWorker> logger,
            IConfiguration config)
        {
            _services = services;
            _logger = logger;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Software inventory scheduler started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var runHourUtc = GetInt("SoftwareInventorySchedule:RunDailyAtHourUtc", 2);
                    var delay = GetDelayUntilNextRunUtc(runHourUtc);

                    _logger.LogInformation(
                        "Next software inventory scheduling run in {Delay}.",
                        delay);

                    await Task.Delay(delay, stoppingToken);

                    await QueueSoftwareInventoryCommandsAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Software inventory scheduler failed. Retrying in 1 hour.");
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }

            _logger.LogInformation("Software inventory scheduler stopped.");
        }

        private async Task QueueSoftwareInventoryCommandsAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RetailCentralDbContext>();

            var nowUtc = DateTime.UtcNow;
            var activeDeviceCutoffDays = GetInt("SoftwareInventorySchedule:ActiveDeviceDays", 7);
            var successFreshHours = GetInt("SoftwareInventorySchedule:SuccessFreshHours", 24);
            var priority = GetInt("SoftwareInventorySchedule:Priority", 60);
            var maxAttempts = GetInt("SoftwareInventorySchedule:MaxAttempts", 3);

            var activeCutoffUtc = nowUtc.AddDays(-activeDeviceCutoffDays);
            var successCutoffUtc = nowUtc.AddHours(-successFreshHours);

            _logger.LogInformation(
                "Running software inventory scheduling. ActiveCutoffUtc={ActiveCutoffUtc}, SuccessCutoffUtc={SuccessCutoffUtc}",
                activeCutoffUtc,
                successCutoffUtc);

            var candidateDevices = await db.Devices
                .Where(d => d.IsEnabled && d.LastSeenUtc >= activeCutoffUtc)
                .OrderBy(d => d.StoreNumber)
                .ThenBy(d => d.Hostname)
                .ToListAsync(ct);

            if (candidateDevices.Count == 0)
            {
                _logger.LogInformation("No active devices eligible for software inventory scheduling.");
                return;
            }

            var deviceIds = candidateDevices.Select(d => d.DeviceId).ToList();

            var devicesWithPendingOrInProgress = await db.Commands
                .Where(c =>
                    c.DeviceId != null &&
                    deviceIds.Contains(c.DeviceId.Value) &&
                    c.Type == "CollectSoftwareInventory" &&
                    (c.Status == "Pending" || c.Status == "InProgress"))
                .Select(c => c.DeviceId!.Value)
                .Distinct()
                .ToListAsync(ct);

            var devicesWithRecentSuccess = await (
                from c in db.Commands
                join r in db.CommandResults on c.CommandId equals r.CommandId
                where c.DeviceId != null
                      && deviceIds.Contains(c.DeviceId.Value)
                      && c.Type == "CollectSoftwareInventory"
                      && r.Status == "Succeeded"
                      && r.FinishedUtc != null
                      && r.FinishedUtc >= successCutoffUtc
                select c.DeviceId!.Value
            )
            .Distinct()
            .ToListAsync(ct);

            var pendingSet = devicesWithPendingOrInProgress.ToHashSet();
            var successSet = devicesWithRecentSuccess.ToHashSet();

            var commandsToCreate = new List<Command>();

            foreach (var device in candidateDevices)
            {
                if (pendingSet.Contains(device.DeviceId))
                    continue;

                if (successSet.Contains(device.DeviceId))
                    continue;

                commandsToCreate.Add(new Command
                {
                    CommandId = Guid.NewGuid(),
                    DeviceId = device.DeviceId,
                    StoreNumber = device.StoreNumber,
                    GroupName = null,
                    Scope = "Device",
                    Type = "CollectSoftwareInventory",
                    PayloadJson = "{}",
                    Status = "Pending",
                    CreatedUtc = nowUtc,
                    ExpiresUtc = nowUtc.AddDays(2),
                    Priority = priority,
                    AttemptCount = 0,
                    MaxAttempts = maxAttempts,
                    LastAttemptUtc = null,
                    LastError = null,
                    IssuedBy = "System-SoftwareInventoryScheduler",
                    IssuedUtc = nowUtc
                });
            }

            if (commandsToCreate.Count == 0)
            {
                _logger.LogInformation("Software inventory scheduler found no devices requiring a new command.");
                return;
            }

            db.Commands.AddRange(commandsToCreate);
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Queued {Count} CollectSoftwareInventory command(s).",
                commandsToCreate.Count);
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