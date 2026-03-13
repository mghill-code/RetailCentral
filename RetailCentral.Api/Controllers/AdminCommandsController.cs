using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data;
using RetailCentral.Api.Models;

namespace RetailCentral.Api.Controllers
{
    [ApiController]
    [Route("api/admin/v1")]
    public class AdminCommandsController : ControllerBase
    {
        private readonly RetailCentralDbContext _db;

        public AdminCommandsController(RetailCentralDbContext db)
        {
            _db = db;
        }

        [HttpPost("commands")]
        public async Task<IActionResult> CreateCommand([FromBody] CreateCommandRequest req)
        {
            if (req == null)
                return BadRequest("Body required.");

            if (string.IsNullOrWhiteSpace(req.Type))
                return BadRequest("Type is required.");

            var targetCount = 0;
            if (req.DeviceId != null) targetCount++;
            if (!string.IsNullOrWhiteSpace(req.StoreNumber)) targetCount++;
            if (!string.IsNullOrWhiteSpace(req.GroupName)) targetCount++;

            if (targetCount != 1)
                return BadRequest("Provide exactly one target: DeviceId, StoreNumber, or GroupName.");

            var now = DateTime.UtcNow;
            var createdCommands = new List<Command>();

            if (req.DeviceId != null)
            {
                var device = await _db.Devices
                    .FirstOrDefaultAsync(d => d.DeviceId == req.DeviceId.Value);

                if (device == null)
                    return NotFound("Selected device was not found.");

                createdCommands.Add(BuildDeviceCommand(
                    device,
                    req.Type.Trim(),
                    string.IsNullOrWhiteSpace(req.PayloadJson) ? null : req.PayloadJson,
                    req.Priority ?? 100,
                    req.MaxAttempts ?? 3,
                    req.ExpiresUtc,
                    issuedBy: "AdminApi",
                    issuedUtc: now,
                    groupName: null));
            }
            else if (!string.IsNullOrWhiteSpace(req.StoreNumber))
            {
                var normalizedStore = req.StoreNumber.Trim();

                var storeDevices = await _db.Devices
                    .Where(d => d.StoreNumber == normalizedStore)
                    .OrderBy(d => d.Hostname)
                    .ToListAsync();

                if (storeDevices.Count == 0)
                    return BadRequest("No devices were found for the selected store.");

                foreach (var device in storeDevices)
                {
                    createdCommands.Add(BuildDeviceCommand(
                        device,
                        req.Type.Trim(),
                        string.IsNullOrWhiteSpace(req.PayloadJson) ? null : req.PayloadJson,
                        req.Priority ?? 100,
                        req.MaxAttempts ?? 3,
                        req.ExpiresUtc,
                        issuedBy: "AdminApi",
                        issuedUtc: now,
                        groupName: null));
                }
            }
            else if (!string.IsNullOrWhiteSpace(req.GroupName))
            {
                var normalizedGroup = req.GroupName.Trim();

                var groupDevices = await _db.DeviceGroupMembers
                    .Where(m => _db.DeviceGroups.Any(g =>
                        g.DeviceGroupId == m.DeviceGroupId &&
                        g.GroupName == normalizedGroup))
                    .Join(
                        _db.Devices,
                        m => m.DeviceId,
                        d => d.DeviceId,
                        (m, d) => d)
                    .OrderBy(d => d.StoreNumber)
                    .ThenBy(d => d.Hostname)
                    .ToListAsync();

                if (groupDevices.Count == 0)
                    return BadRequest("No devices were found for the selected group.");

                foreach (var device in groupDevices)
                {
                    createdCommands.Add(BuildDeviceCommand(
                        device,
                        req.Type.Trim(),
                        string.IsNullOrWhiteSpace(req.PayloadJson) ? null : req.PayloadJson,
                        req.Priority ?? 100,
                        req.MaxAttempts ?? 3,
                        req.ExpiresUtc,
                        issuedBy: "AdminApi",
                        issuedUtc: now,
                        groupName: normalizedGroup));
                }
            }

            _db.Commands.AddRange(createdCommands);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                createdCount = createdCommands.Count,
                commands = createdCommands.Select(c => new
                {
                    c.CommandId,
                    c.Status,
                    c.Type,
                    c.Scope,
                    c.DeviceId,
                    c.StoreNumber,
                    c.GroupName,
                    c.CreatedUtc,
                    c.IssuedBy,
                    c.IssuedUtc
                }).ToList()
            });
        }

        [HttpPost("commands/{commandId:guid}/requeue")]
        public async Task<IActionResult> Requeue([FromRoute] Guid commandId)
        {
            var cmd = await _db.Commands.FirstOrDefaultAsync(c => c.CommandId == commandId);
            if (cmd == null)
                return NotFound();

            cmd.Status = "Pending";
            cmd.LockedUtc = null;
            cmd.LockedByDeviceId = null;
            cmd.LastError = "Requeued by admin";

            await _db.SaveChangesAsync();

            return Ok(new
            {
                cmd.CommandId,
                cmd.Status
            });
        }

        private static Command BuildDeviceCommand(
            Device device,
            string type,
            string? payloadJson,
            int priority,
            int maxAttempts,
            DateTime? expiresUtc,
            string issuedBy,
            DateTime issuedUtc,
            string? groupName)
        {
            return new Command
            {
                CommandId = Guid.NewGuid(),
                Type = type,
                Scope = "Device",
                PayloadJson = payloadJson,
                Status = "Pending",
                Priority = priority,
                CreatedUtc = issuedUtc,
                ExpiresUtc = expiresUtc,

                DeviceId = device.DeviceId,
                StoreNumber = device.StoreNumber,
                GroupName = groupName,

                AttemptCount = 0,
                MaxAttempts = maxAttempts,

                IssuedBy = issuedBy,
                IssuedUtc = issuedUtc
            };
        }
    }

    public class CreateCommandRequest
    {
        public Guid? DeviceId { get; set; }
        public string? StoreNumber { get; set; }
        public string? GroupName { get; set; }
        public string Type { get; set; } = "";
        public string? PayloadJson { get; set; }
        public int? Priority { get; set; }
        public int? MaxAttempts { get; set; }
        public DateTime? ExpiresUtc { get; set; }
    }
}