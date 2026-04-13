namespace RetailCentral.Api.Services.Orchestration
{
    public interface IEnrollmentOrchestrationService
    {
        Task<long?> StartProvisioningForEnrollmentAsync(
            Guid? deviceId,
            int? agentId,
            string? deviceType,
            string? environment,
            CancellationToken cancellationToken);
    }
}