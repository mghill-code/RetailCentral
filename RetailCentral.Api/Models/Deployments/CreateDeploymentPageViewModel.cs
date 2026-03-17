namespace RetailCentral.Api.Models.Dashboard.Deployments
{
    public class CreateDeploymentPageViewModel
    {
        public CreateDeploymentInputModel Input { get; set; } = new();
        public List<PackageOptionViewModel> Packages { get; set; } = new();
    }
}