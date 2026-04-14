using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data;
using RetailCentral.Api.Data.Entities.Orchestration;
using RetailCentral.Api.DTOs.Orchestration;

namespace RetailCentral.Api.Controllers
{
    [ApiController]
    [Route("api/provisioning")]
    public class ProvisioningController : ControllerBase
    {
        private readonly RetailCentralDbContext _db;
        private readonly ILogger<ProvisioningController> _logger;

        public ProvisioningController(
            RetailCentralDbContext db,
            ILogger<ProvisioningController> logger)
        {
            _db = db;
            _logger = logger;
        }

        [HttpPost("profiles")]
        public async Task<IActionResult> CreateProfile(
            [FromBody] CreateProvisioningProfileRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Profile name is required.");

            var templateExists = await _db.OrchestrationTemplates
                .AnyAsync(x => x.Id == request.TemplateId, cancellationToken);

            if (!templateExists)
                return BadRequest($"Template {request.TemplateId} does not exist.");

            var profile = new ProvisioningProfile
            {
                Name = request.Name,
                DeviceType = request.DeviceType,
                StoreGroup = request.StoreGroup,
                Environment = request.Environment,
                TemplateId = request.TemplateId,
                ParametersJson = request.ParametersJson,
                IsDefault = request.IsDefault,
                IsActive = true,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };

            _db.ProvisioningProfiles.Add(profile);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created provisioning profile {ProfileId} ({ProfileName})", profile.Id, profile.Name);

            return Ok(new
            {
                profile.Id,
                profile.Name,
                profile.TemplateId,
                profile.IsDefault
            });
        }

        [HttpGet("profiles")]
        public async Task<IActionResult> GetProfiles(CancellationToken cancellationToken)
        {
            var profiles = await _db.ProvisioningProfiles
                .OrderBy(x => x.Name)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.DeviceType,
                    x.Environment,
                    x.TemplateId,
                    x.IsDefault,
                    x.IsActive
                })
                .ToListAsync(cancellationToken);

            return Ok(profiles);
        }
    }
}