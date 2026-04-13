using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data;
using RetailCentral.Api.Data.Entities.Orchestration;

namespace RetailCentral.Api.Services.Orchestration
{
    public class ProvisioningProfileResolver : IProvisioningProfileResolver
    {
        private readonly RetailCentralDbContext _db;

        public ProvisioningProfileResolver(RetailCentralDbContext db)
        {
            _db = db;
        }

        public async Task<ProvisioningProfile?> ResolveProfileAsync(
            Guid? deviceId,
            int? agentId,
            string? deviceType,
            string? environment,
            CancellationToken cancellationToken)
        {
            var query = _db.ProvisioningProfiles
                .Where(x => x.IsActive);

            if (!string.IsNullOrWhiteSpace(deviceType))
            {
                var exact = await query
                    .Where(x => x.DeviceType == deviceType && x.Environment == environment)
                    .OrderByDescending(x => x.IsDefault)
                    .FirstOrDefaultAsync(cancellationToken);

                if (exact != null)
                    return exact;
            }

            return await query
                .Where(x => x.IsDefault)
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}