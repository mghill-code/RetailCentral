using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data;
using RetailCentral.Api.Dtos;
using RetailCentral.Api.Models;
using RetailCentral.Api.Services;

namespace RetailCentral.Api.Controllers
{
    /// <summary>
    /// Administrative command API.
    ///
    /// This controller routes command creation through the shared
    /// CommandValidationService so unsafe or malformed commands are rejected
    /// before they are inserted into the queue.
    /// </summary>
    [ApiController]
    [Route("api/admin/v1")]
    public class AdminCommandsController : ControllerBase
    {
        private readonly RetailCentralDbContext _db;
        private readonly CommandValidationService _commandValidationService;
        private readonly AuditService _auditService;

        public AdminCommandsController(
            RetailCentralDbContext db,
            CommandValidationService commandValidationService,
            AuditService auditService)
        {
            _db = db;
            _commandValidationService = commandValidationService;
            _auditService = auditService;
        }

        [HttpPost("commands")]
        public async Task<IActionResult> CreateCommand([FromBody] CreateCommandRequest req, CancellationToken cancellationToken)
        {
            if (req == null)
                return BadRequest("Body required.");

            var validation = _commandValidationService.Validate(req, helpdeskMode: false);

            if (!validation.IsValid)
            {
                await _auditService.LogAsync(
                    action: "AdminApiCreateCommandRejected",
                    targetType: ResolveTargetType(req),
                    targetId: ResolveTargetId(req),
                    details: new
                    {
                        req.Type,
                        req.DeviceId,
                        req.StoreNumber,
                        req.GroupName,
                        req.Priority,
                        req.MaxAttempts,
                        req.ExpiresUtc,
                        Errors = validation.Errors
                    },
                    success: false,
                    errorMessage: string.Join(" | ", validation.Errors),
                    cancellationToken: cancellationToken);

                return BadRequest(new
                {
                    message = "Command validation failed.",
                    errors = validation.Errors
                });
            }

            var now = DateTime.UtcNow;
            var createdCommands = new List<Command>();

            if (req.DeviceId != null)
            {
                var device = await _db.Devices
                    .FirstOrDefaultAsync(d => d.DeviceId == req.DeviceId.Value && d.IsEnabled, cancellationToken);

                if (device == null)
                    return NotFound("Selected device was not found.");

                createdCommands.Add(BuildDeviceCommand(
                    device,
                    validation.NormalizedType,
                    NormalizePayload(req.PayloadJson),
                    req.Priority,
                    req.MaxAttempts,
                    req.ExpiresUtc,
                    issuedBy: CurrentActor(),
                    issuedUtc: now,
                    groupName: null));
            }
            else if (!string.IsNullOrWhiteSpace(req.StoreNumber))
            {
                var normalizedStore = req.StoreNumber.Trim();

                var storeDevices = await _db.Devices
                    .Where(d => d.IsEnabled && d.StoreNumber == normalizedStore)
                    .OrderBy(d => d.Hostname)
                    .ToListAsync(cancellationToken);

                if (storeDevices.Count == 0)
                    return BadRequest("No devices were found for the selected store.");

                foreach (var device in storeDevices)
                {
                    createdCommands.Add(BuildDeviceCommand(
                        device,
                        validation.NormalizedType,
                        NormalizePayload(req.PayloadJson),
                        req.Priority,
                        req.MaxAttempts,
                        req.ExpiresUtc,
                        issuedBy: CurrentActor(),
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
                        _db.Devices.Where(d => d.IsEnabled),
                        m => m.DeviceId,
                        d => d.DeviceId,
                        (m, d) => d)
                    .OrderBy(d => d.StoreNumber)
                    .ThenBy(d => d.Hostname)
                    .ToListAsync(cancellationToken);

                if (groupDevices.Count == 0)
                    return BadRequest("No devices were found for the selected group.");

                foreach (var device in groupDevices)
                {
                    createdCommands.Add(BuildDeviceCommand(
                        device,
                        validation.NormalizedType,
                        NormalizePayload(req.PayloadJson),
                        req.Priority,
                        req.MaxAttempts,
                        req.ExpiresUtc,
                        issuedBy: CurrentActor(),
                        issuedUtc: now,
                        groupName: normalizedGroup));
                }
            }

            _db.Commands.AddRange(createdCommands);
            await _db.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync(
                action: "AdminApiCreateCommand",
                targetType: ResolveTargetType(req),
                targetId: ResolveTargetId(req),
                details: new
                {
                    req.Type,
                    NormalizedType = validation.NormalizedType,
                    req.DeviceId,
                    req.StoreNumber,
                    req.GroupName,
                    req.Priority,
                    req.MaxAttempts,
                    req.ExpiresUtc,
                    CreatedCommandCount = createdCommands.Count,
                    CommandIds = createdCommands.Select(c => c.CommandId).ToList()
                },
                success: true,
                cancellationToken: cancellationToken);

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
        public async Task<IActionResult> Requeue([FromRoute] Guid commandId, CancellationToken cancellationToken)
        {
            var cmd = await _db.Commands.FirstOrDefaultAsync(c => c.CommandId == commandId, cancellationToken);
            if (cmd == null)
                return NotFound();

            cmd.Status = "Pending";
            cmd.LockedUtc = null;
            cmd.LockedByDeviceId = null;
            cmd.LastError = "Requeued by admin API";

            await _db.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync(
                action: "AdminApiRequeueCommand",
                targetType: "Command",
                targetId: commandId.ToString(),
                details: new { commandId },
                success: true,
                cancellationToken: cancellationToken);

            return Ok(new
            {
                cmd.CommandId,
                cmd.Status
            });
        }

        private string CurrentActor()
        {
            return User?.Identity?.Name ?? "AdminApi";
        }

        private static string? ResolveTargetId(CreateCommandRequest req)
        {
            return req.DeviceId?.ToString() ?? req.StoreNumber ?? req.GroupName;
        }

        private static string? ResolveTargetType(CreateCommandRequest req)
        {
            if (req.DeviceId != null) return "Device";
            if (!string.IsNullOrWhiteSpace(req.StoreNumber)) return "Store";
            if (!string.IsNullOrWhiteSpace(req.GroupName)) return "Group";
            return null;
        }

        private static string? NormalizePayload(string? payloadJson)
        {
            return string.IsNullOrWhiteSpace(payloadJson)
                ? null
                : payloadJson.Trim();
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
}