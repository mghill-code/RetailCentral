using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data;
using RetailCentral.Api.Data.Entities.Orchestration;
using RetailCentral.Api.Services.Orchestration;

namespace RetailCentral.Api.BackgroundServices
{
    public class OrchestrationWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OrchestrationWorker> _logger;

        public OrchestrationWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<OrchestrationWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Orchestration worker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<RetailCentralDbContext>();
                    var engine = scope.ServiceProvider.GetRequiredService<IOrchestrationEngine>();

                    var advanced = await engine.AdvancePendingRunsAsync(stoppingToken);

                    var unprocessedCommandIds = await db.OrchestrationRunSteps
                        .Where(x => x.CommandId != null &&
                                    (x.Status == OrchestrationRunStepStatus.Dispatched ||
                                     x.Status == OrchestrationRunStepStatus.Running))
                        .Select(x => x.CommandId!.Value)
                        .ToListAsync(stoppingToken);

                    foreach (var commandId in unprocessedCommandIds)
                    {
                        await engine.ProcessCommandResultAsync(commandId, stoppingToken);
                    }

                    if (advanced > 0)
                    {
                        _logger.LogInformation("Advanced {Count} orchestration runs.", advanced);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while running orchestration worker.");
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }

            _logger.LogInformation("Orchestration worker stopping.");
        }
    }
}