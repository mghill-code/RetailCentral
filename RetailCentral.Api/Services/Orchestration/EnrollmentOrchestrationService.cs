using RetailCentral.Api.Data;
using RetailCentral.Api.Data.Entities.Orchestration;

namespace RetailCentral.Api.Services.Orchestration
{
    public class EnrollmentOrchestrationService : IEnrollmentOrchestrationService
    {
        private readonly RetailCentralDbContext _db;
        private readonly IProvisioningProfileResolver _profileResolver;
        private readonly IOrchestrationEngine _orchestrationEngine;
        private readonly ILogger<EnrollmentOrchestrationService> _logger;

        public EnrollmentOrchestrationService(
            RetailCentralDbContext db,
            IProvisioningProfileResolver profileResolver,
            IOrchestrationEngine orchestrationEngine,
            ILogger<EnrollmentOrchestrationService> logger)
        {
            _db = db;
            _profileResolver = profileResolver;
            _orchestrationEngine = orchestrationEngine;
            _logger = logger;
        }

        public async Task<long?> StartProvisioningForEnrollmentAsync(
            Guid? deviceId,
            int? agentId,
            string? deviceType,
            string? environment,
            CancellationToken cancellationToken)
        {
            var profile = await _profileResolver.ResolveProfileAsync(
                deviceId,
                agentId,
                deviceType,
                environment,
                cancellationToken);

            if (profile == null)
            {
                _logger.LogInformation(
                    "No provisioning profile resolved for device {DeviceId}, agent {AgentId}",
                    deviceId,
                    agentId);

                return null;
            }

            var run = await _orchestrationEngine.CreateRunFromTemplateAsync(
                profile.TemplateId,
                deviceId,
                agentId,
                null,
                null,
                OrchestrationTriggerSource.Enrollment,
                OrchestrationConstants.TriggeredByEnrollment,
                profile.ParametersJson,
                cancellationToken);

            var action = new EnrollmentAction
            {
                DeviceId = deviceId,
                AgentId = agentId,
                AssignedProfileId = profile.Id,
                InitialRunId = run.Id,
                Status = EnrollmentActionStatus.RunCreated,
                CreatedUtc = DateTime.UtcNow
            };

            _db.EnrollmentActions.Add(action);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Created enrollment action for device {DeviceId}, run {RunId}",
                deviceId,
                run.Id);

            return run.Id;
        }
    }
}