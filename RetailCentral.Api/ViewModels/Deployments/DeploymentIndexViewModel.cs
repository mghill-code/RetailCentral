using RetailCentral.Api.Data.Entities;

namespace RetailCentral.Api.ViewModels.Deployments
{
    public class DeploymentIndexViewModel
    {
        public List<Package> Packages { get; set; } = new();
        public List<Deployment> Deployments { get; set; } = new();
    }
}