using RetailCentral.Api.Data.Entities.Orchestration;

namespace RetailCentral.Api.Services.Orchestration
{
    public interface IOrchestrationEngine
    {
        Task<int> AdvancePendingRunsAsync(CancellationToken cancellationToken);
        Task ProcessCommandResultAsync(Guid commandId, CancellationToken cancellationToken);

        Task<OrchestrationRun> CreateRunFromTemplateAsync(
            int templateId,
            Guid? deviceId,
            int? agentId,
            int? storeId,
            int? registerId,
            OrchestrationTriggerSource triggerSource,
            string? requestedBy,
            string? parametersJson,
            CancellationToken cancellationToken);
    }
}