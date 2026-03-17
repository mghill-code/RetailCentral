using Microsoft.AspNetCore.Mvc;
using RetailCentral.Api.Models.Deployments;
using RetailCentral.Api.Services.Deployments;

namespace RetailCentral.Api.Controllers
{
    [ApiController]
    [Route("api/admin/deployments")]
    public class DeploymentsController : ControllerBase
    {
        private readonly IDeploymentService _deploymentService;

        public DeploymentsController(IDeploymentService deploymentService)
        {
            _deploymentService = deploymentService;
        }

        [HttpGet("packages")]
        public async Task<IActionResult> GetPackages(CancellationToken cancellationToken)
        {
            var result = await _deploymentService.GetPackagesAsync(cancellationToken);
            return Ok(result);
        }

        [HttpPost("packages")]
        public async Task<IActionResult> CreatePackage([FromBody] CreatePackageRequest request, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var createdBy = User?.Identity?.Name ?? "system";
            var result = await _deploymentService.CreatePackageAsync(request, createdBy, cancellationToken);
            return Ok(result);
        }

        [HttpGet]
        public async Task<IActionResult> GetDeployments(CancellationToken cancellationToken)
        {
            var result = await _deploymentService.GetDeploymentsAsync(cancellationToken);
            return Ok(result);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetDeployment(int id, CancellationToken cancellationToken)
        {
            var result = await _deploymentService.GetDeploymentAsync(id, cancellationToken);
            if (result == null)
            {
                return NotFound();
            }

            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> CreateDeployment([FromBody] CreateDeploymentRequest request, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var createdBy = User?.Identity?.Name ?? "system";
            var result = await _deploymentService.CreateDeploymentAsync(request, createdBy, cancellationToken);
            return Ok(new { result.Id });
        }
    }
}