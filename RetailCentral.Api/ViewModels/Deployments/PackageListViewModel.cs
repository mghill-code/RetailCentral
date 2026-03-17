using RetailCentral.Api.Data.Entities;

namespace RetailCentral.Api.ViewModels.Deployments
{
    public class PackageListViewModel
    {
        public List<Package> Packages { get; set; } = new();
        public string? Message { get; set; }
    }
}