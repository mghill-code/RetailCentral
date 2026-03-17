using RetailCentral.Api.Data.Entities;
using RetailCentral.Api.Models.Deployments;

namespace RetailCentral.Api.Services.Deployments
{
    public interface IDeploymentService
    {
        Task<Package> CreatePackageAsync(CreatePackageRequest request, string? createdBy, CancellationToken cancellationToken);
        Task<Package?> GetPackageByIdAsync(int packageId, CancellationToken cancellationToken);
        Task<Package> UpdatePackageAsync(int packageId, CreatePackageRequest request, string? updatedBy, CancellationToken cancellationToken);
        Task DisablePackageAsync(int packageId, CancellationToken cancellationToken);
        Task DeletePackageAsync(int packageId, CancellationToken cancellationToken);

        Task<Deployment> CreateDeploymentAsync(CreateDeploymentRequest request, string? createdBy, CancellationToken cancellationToken);
        Task<Deployment?> GetDeploymentByIdAsync(int deploymentId, CancellationToken cancellationToken);
        Task<Deployment> UpdateDeploymentAsync(int deploymentId, CreateDeploymentRequest request, string? updatedBy, CancellationToken cancellationToken);
        Task<DeploymentDetailResponse?> GetDeploymentAsync(int deploymentId, CancellationToken cancellationToken);
        Task<List<Package>> GetPackagesAsync(CancellationToken cancellationToken);
        Task<List<Deployment>> GetDeploymentsAsync(CancellationToken cancellationToken);
        Task<int> RetryFailedDevicesAsync(int deploymentId, string? createdBy, CancellationToken cancellationToken);
        Task CancelDeploymentAsync(int deploymentId, CancellationToken cancellationToken);
        Task DeleteDeploymentAsync(int deploymentId, CancellationToken cancellationToken);
    }
}
