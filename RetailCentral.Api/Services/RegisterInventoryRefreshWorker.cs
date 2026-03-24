using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data;
using RetailCentral.Api.Models;

namespace RetailCentral.Api.Services
{
    public sealed class RegisterInventoryRefreshWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RegisterInventoryRefreshWorker> _logger;

        public RegisterInventoryRefreshWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<RegisterInventoryRefreshWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await QueueDailyInventoryRefreshAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "RegisterInventoryRefreshWorker failed.");
                }

                try
                {
                    await timer.WaitForNextTickAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task QueueDailyInventoryRefreshAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RetailCentralDbContext>();

            var now = DateTime.UtcNow;
            var staleCutoff = now.AddDays(-1);
            var onlineCutoff = now.AddMinutes(-5);

            var devices = await db.Devices
                .Where(d => d.IsEnabled && d.LastSeenUtc >= onlineCutoff)
                .Select(d => new
                {
                    d.DeviceId,
                    d.StoreNumber,
                    d.LastSeenUtc
                })
                .ToListAsync(ct);

            var inventoryByDevice = await db.RegisterInventories
                .Select(r => new
                {
                    r.DeviceId,
                    r.UpdatedUtc
                })
                .ToDictionaryAsync(x => x.DeviceId, x => x.UpdatedUtc, ct);

            var queuedDeviceIds = await db.Commands
                .Where(c =>
                    c.Type == "CollectSystemInfo" &&
                    (c.Status == "Pending" || c.Status == "InProgress") &&
                    c.DeviceId != null)
                .Select(c => c.DeviceId!.Value)
                .Distinct()
                .ToListAsync(ct);

            var queuedSet = queuedDeviceIds.ToHashSet();
            var toAdd = new List<Command>();

            foreach (var device in devices)
            {
                if (queuedSet.Contains(device.DeviceId))
                    continue;

                var hasInventory = inventoryByDevice.TryGetValue(device.DeviceId, out var updatedUtc);
                var needsRefresh = !hasInventory || updatedUtc < staleCutoff;

                if (!needsRefresh)
                    continue;

                toAdd.Add(new Command
                {
                    CommandId = Guid.NewGuid(),
                    DeviceId = device.DeviceId,
                    StoreNumber = device.StoreNumber,
                    Scope = "Device",
                    Type = "CollectSystemInfo",
                    PayloadJson = "{}",
                    Status = "Pending",
                    Priority = 90,
                    CreatedUtc = now,
                    AttemptCount = 0,
                    MaxAttempts = 3,
                    IssuedBy = "System-DailyInventoryRefresh",
                    IssuedUtc = now
                });
            }

            if (toAdd.Count == 0)
            {
                _logger.LogDebug("Register inventory refresh scan complete. No online devices needed refresh.");
                return;
            }

            db.Commands.AddRange(toAdd);
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Register inventory refresh queued {Count} CollectSystemInfo command(s).",
                toAdd.Count);
        }
    }
}