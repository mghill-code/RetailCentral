using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data;
using RetailCentral.Api.Dtos;
using RetailCentral.Api.Models;

namespace RetailCentral.Api.Controllers
{
    [ApiController]
    [Route("api/admin/v1/device-groups")]
    public class AdminDeviceGroupsController : ControllerBase
    {
        private readonly RetailCentralDbContext _db;

        public AdminDeviceGroupsController(RetailCentralDbContext db)
        {
            _db = db;
        }

        [HttpPost]
        public async Task<IActionResult> CreateGroup([FromBody] CreateDeviceGroupRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.GroupName))
                return BadRequest("GroupName is required.");

            var normalized = req.GroupName.Trim();

            var exists = await _db.DeviceGroups.AnyAsync(g => g.GroupName == normalized);
            if (exists)
                return Conflict("Group already exists.");

            var group = new DeviceGroup
            {
                GroupName = normalized,
                Description = req.Description?.Trim(),
                CreatedUtc = DateTime.UtcNow
            };

            _db.DeviceGroups.Add(group);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                group.DeviceGroupId,
                group.GroupName,
                group.Description,
                group.CreatedUtc
            });
        }

        [HttpGet]
        public async Task<IActionResult> ListGroups()
        {
            var groups = await _db.DeviceGroups
                .OrderBy(g => g.GroupName)
                .Select(g => new
                {
                    g.DeviceGroupId,
                    g.GroupName,
                    g.Description,
                    g.CreatedUtc
                })
                .ToListAsync();

            return Ok(groups);
        }

        [HttpPost("{groupName}/members")]
        public async Task<IActionResult> AddMember([FromRoute] string groupName, [FromBody] AddDeviceToGroupRequest req)
        {
            var group = await _db.DeviceGroups.FirstOrDefaultAsync(g => g.GroupName == groupName);
            if (group == null)
                return NotFound("Group not found.");

            var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == req.DeviceId);
            if (device == null)
                return NotFound("Device not found.");

            var exists = await _db.DeviceGroupMembers
                .AnyAsync(m => m.DeviceGroupId == group.DeviceGroupId && m.DeviceId == req.DeviceId);

            if (exists)
            {
                return Ok(new
                {
                    group.GroupName,
                    req.DeviceId,
                    note = "Device already in group"
                });
            }

            var member = new DeviceGroupMember
            {
                DeviceGroupId = group.DeviceGroupId,
                DeviceId = req.DeviceId,
                AddedUtc = DateTime.UtcNow
            };

            _db.DeviceGroupMembers.Add(member);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                group.GroupName,
                req.DeviceId,
                member.AddedUtc
            });
        }

        [HttpGet("{groupName}/members")]
        public async Task<IActionResult> ListMembers([FromRoute] string groupName)
        {
            var group = await _db.DeviceGroups.FirstOrDefaultAsync(g => g.GroupName == groupName);
            if (group == null)
                return NotFound("Group not found.");

            var members = await _db.DeviceGroupMembers
                .Where(m => m.DeviceGroupId == group.DeviceGroupId)
                .Join(_db.Devices,
                    m => m.DeviceId,
                    d => d.DeviceId,
                    (m, d) => new
                    {
                        d.DeviceId,
                        d.StoreNumber,
                        d.Hostname,
                        d.MachineName,
                        m.AddedUtc
                    })
                .OrderBy(x => x.Hostname)
                .ToListAsync();

            return Ok(new
            {
                group.GroupName,
                Members = members
            });
        }
    }
}