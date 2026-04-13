using RetailCentral.Api.Data.Entities.Orchestration;

namespace RetailCentral.Api.Services.Orchestration
{
    public interface IProvisioningProfileResolver
    {
        Task<ProvisioningProfile?> ResolveProfileAsync(
            Guid? deviceId,
            int? agentId,
            string? deviceType,
            string? environment,
            CancellationToken cancellationToken);
    }
}