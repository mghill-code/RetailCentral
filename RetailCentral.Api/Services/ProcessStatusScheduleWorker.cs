using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data;
using RetailCentral.Api.Models;

namespace RetailCentral.Api.Services
{
    public sealed class ProcessStatusScheduleWorker : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<ProcessStatusScheduleWorker> _logger;
        private readonly IConfiguration _config;

        public ProcessStatusScheduleWorker(
            IServiceProvider services,
            ILogger<ProcessStatusScheduleWorker> logger,
            IConfiguration config)
        {
            _services = services;
            _logger = logger;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Process status scheduler started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var enabled = GetBool("ProcessStatusSchedule:Enabled", true);
                    var runEveryMinutes = GetInt("ProcessStatusSchedule:RunEveryMinutes", 5);

                    if (!enabled)
                    {
                        _logger.LogInformation("Process status scheduler is disabled. Sleeping for 5 minutes.");
                        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                        continue;
                    }

                    await QueueProcessStatusCommandsAsync(stoppingToken);

                    _logger.LogInformation(
                        "Process status scheduler sleeping for {Minutes} minute(s).",
                        runEveryMinutes);

                    await Task.Delay(TimeSpan.FromMinutes(runEveryMinutes), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Process status scheduler failed. Retrying in 1 minute.");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }

            _logger.LogInformation("Process status scheduler stopped.");
        }

        private async Task QueueProcessStatusCommandsAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RetailCentralDbContext>();

            var nowUtc = DateTime.UtcNow;
            var activeDeviceDays = GetInt("ProcessStatusSchedule:ActiveDeviceDays", 7);
            var successFreshMinutes = GetInt("ProcessStatusSchedule:SuccessFreshMinutes", 5);
            var priority = GetInt("ProcessStatusSchedule:Priority", 40);
            var maxAttempts = GetInt("ProcessStatusSchedule:MaxAttempts", 2);

            var activeCutoffUtc = nowUtc.AddDays(-activeDeviceDays);
            var successCutoffUtc = nowUtc.AddMinutes(-successFreshMinutes);

            var candidateDevices = await db.Devices
                .Where(d => d.IsEnabled && d.LastSeenUtc >= activeCutoffUtc)
                .OrderBy(d => d.StoreNumber)
                .ThenBy(d => d.Hostname)
                .ToListAsync(ct);

            if (candidateDevices.Count == 0)
            {
                _logger.LogInformation("No active devices eligible for process status scheduling.");
                return;
            }

            var deviceIds = candidateDevices.Select(d => d.DeviceId).ToList();

            var devicesWithPendingOrInProgress = await db.Commands
                .Where(c =>
                    c.DeviceId != null &&
                    deviceIds.Contains(c.DeviceId.Value) &&
                    c.Type == "CollectProcessStatus" &&
                    (c.Status == "Pending" || c.Status == "InProgress"))
                .Select(c => c.DeviceId!.Value)
                .Distinct()
                .ToListAsync(ct);

            var devicesWithRecentSuccess = await (
                from c in db.Commands
                join r in db.CommandResults on c.CommandId equals r.CommandId
                where c.DeviceId != null
                      && deviceIds.Contains(c.DeviceId.Value)
                      && c.Type == "CollectProcessStatus"
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
                    Type = "CollectProcessStatus",
                    PayloadJson = "{}",
                    Status = "Pending",
                    CreatedUtc = nowUtc,
                    ExpiresUtc = nowUtc.AddHours(1),
                    Priority = priority,
                    AttemptCount = 0,
                    MaxAttempts = maxAttempts,
                    LastAttemptUtc = null,
                    LastError = null,
                    IssuedBy = "System-ProcessStatusScheduler",
                    IssuedUtc = nowUtc
                });
            }

            if (commandsToCreate.Count == 0)
            {
                _logger.LogInformation("Process status scheduler found no devices requiring a new command.");
                return;
            }

            db.Commands.AddRange(commandsToCreate);
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Queued {Count} CollectProcessStatus command(s).",
                commandsToCreate.Count);
        }

        private int GetInt(string key, int defaultValue)
        {
            return int.TryParse(_config[key], out var value) ? value : defaultValue;
        }

        private bool GetBool(string key, bool defaultValue)
        {
            return bool.TryParse(_config[key], out var value) ? value : defaultValue;
        }
    }
}