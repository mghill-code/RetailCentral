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

        // ✅ Add this
        [HttpPost("commands")]
        public async Task<IActionResult> CreateCommand([FromBody] CreateCommandRequest req)
        {
            if (req == null) return BadRequest("Body required.");
            if (string.IsNullOrWhiteSpace(req.Type)) return BadRequest("Type is required.");
            if (req.DeviceId == null && string.IsNullOrWhiteSpace(req.StoreNumber))
                return BadRequest("Provide DeviceId or StoreNumber.");

            var now = DateTime.UtcNow;

            var cmd = new Command
            {
                CommandId = Guid.NewGuid(),
                Type = req.Type.Trim(),
                Scope = req.DeviceId != null ? "Device" : "Store",
                PayloadJson = string.IsNullOrWhiteSpace(req.PayloadJson) ? null : req.PayloadJson,
                Status = "Pending",
                Priority = req.Priority ?? 100,
                CreatedUtc = now,
                ExpiresUtc = req.ExpiresUtc,

                DeviceId = req.DeviceId,
                StoreNumber = req.DeviceId != null ? null : req.StoreNumber?.Trim(),

                AttemptCount = 0,
                MaxAttempts = req.MaxAttempts ?? 3
            };

            _db.Commands.Add(cmd);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                cmd.CommandId,
                cmd.Status,
                cmd.Type,
                cmd.Scope,
                cmd.DeviceId,
                cmd.StoreNumber,
                cmd.CreatedUtc
            });
        }

        // keep your existing requeue endpoint
        [HttpPost("commands/{commandId:guid}/requeue")]
        public async Task<IActionResult> Requeue([FromRoute] Guid commandId)
        {
            var cmd = await _db.Commands.FirstOrDefaultAsync(c => c.CommandId == commandId);
            if (cmd == null) return NotFound();

            cmd.Status = "Pending";
            cmd.LockedUtc = null;
            cmd.LockedByDeviceId = null;
            cmd.LastError = "Requeued by admin";

            await _db.SaveChangesAsync();

            return Ok(new { cmd.CommandId, cmd.Status });
        }
    }

    public class CreateCommandRequest
    {
        public Guid? DeviceId { get; set; }
        public string? StoreNumber { get; set; }
        public string Type { get; set; } = "";
        public string? PayloadJson { get; set; }
        public int? Priority { get; set; }
        public int? MaxAttempts { get; set; }
        public DateTime? ExpiresUtc { get; set; }
    }
}
