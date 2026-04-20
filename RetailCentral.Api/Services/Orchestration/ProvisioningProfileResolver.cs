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

            List<string> deviceGroups = new();

            if (deviceId.HasValue)
            {
                deviceGroups = await _db.Set<RetailCentral.Api.Models.DeviceGroupMember>()
                    .Where(m => m.DeviceId == deviceId.Value)
                    .Join(
                        _db.Set<RetailCentral.Api.Models.DeviceGroup>(),
                        m => m.DeviceGroupId,
                        g => g.DeviceGroupId,
                        (m, g) => g.GroupName)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .ToListAsync(cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(deviceType) && !string.IsNullOrWhiteSpace(environment))
            {
                if (deviceGroups.Count > 0)
                {
                    var groupMatch = await query
                        .Where(x =>
                            x.DeviceType == deviceType &&
                            x.Environment == environment &&
                            !string.IsNullOrWhiteSpace(x.StoreGroup) &&
                            deviceGroups.Contains(x.StoreGroup!))
                            .OrderByDescending(x => x.Priority)
                            .ThenByDescending(x => x.IsDefault)
                            .ThenByDescending(x => x.Id)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (groupMatch != null)
                        return groupMatch;
                }

                var exactNoGroup = await query
                    .Where(x =>
                        x.DeviceType == deviceType &&
                        x.Environment == environment &&
                        (x.StoreGroup == null || x.StoreGroup == ""))
                        .OrderByDescending(x => x.Priority)
                        .ThenByDescending(x => x.IsDefault)
                        .ThenByDescending(x => x.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (exactNoGroup != null)
                    return exactNoGroup;
            }

            return await query
                .Where(x => x.IsDefault)
                .OrderByDescending(x => x.Priority)
                .ThenByDescending(x => x.IsDefault)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}