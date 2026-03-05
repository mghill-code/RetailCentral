using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data;
using RetailCentral.Api.Dtos;
using RetailCentral.Api.Models;
using RetailCentral.Api.Security;
using System.Linq;
using System.Text.Json;

namespace RetailCentral.Api.Controllers
{
    [ApiController]
    [Route("api/agent/v1")]
    public class AgentController : ControllerBase
    {
        private readonly RetailCentralDbContext _db;
        private readonly DeviceSecretProtection _secretProtection;

        public AgentController(RetailCentralDbContext db, DeviceSecretProtection secretProtection)
        {
            _db = db;
            _secretProtection = secretProtection;
        }

        // ===== Helper: identify device by header (X-Device-Id) =====
        private async Task<Device?> GetDeviceFromHeader()
        {
            if (!Request.Headers.TryGetValue("X-Device-Id", out var values))
                return null;

            if (!Guid.TryParse(values.FirstOrDefault(), out var deviceId))
                return null;

            return await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
        }

        // ===== Commands: poll pending (atomic lock) =====
        [HttpGet("commands/pending")]
        public async Task<ActionResult<List<CommandDto>>> GetPending([FromQuery] int max = 5)
        {
            if (max <= 0) max = 1;
            if (max > 20) max = 20;

            var device = await GetDeviceFromHeader();
            if (device == null)
                return Unauthorized("Missing/invalid X-Device-Id or device not found.");

            var deviceId = device.DeviceId;

            // Atomic lock + return via OUTPUT inserted.*
            var sql = @"
;WITH cte AS (
  SELECT TOP (@max) *
  FROM Commands WITH (UPDLOCK, READPAST, ROWLOCK)
  WHERE Status = 'Pending'
    AND (ExpiresUtc IS NULL OR ExpiresUtc > SYSUTCDATETIME())
    AND (
      DeviceId = @deviceId
      OR (DeviceId IS NULL AND StoreNumber = @storeNumber)
    )
  ORDER BY Priority ASC, CreatedUtc ASC
)
UPDATE cte
SET Status = 'InProgress',
    LockedByDeviceId = @deviceId,
    LockedUtc = SYSUTCDATETIME()
OUTPUT inserted.CommandId, inserted.Type, inserted.Scope, inserted.PayloadJson, inserted.CreatedUtc, inserted.ExpiresUtc;
";

            var results = new List<CommandDto>();
            var conn = _db.Database.GetDbConnection();
            await _db.Database.OpenConnectionAsync();

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                var pMax = cmd.CreateParameter();
                pMax.ParameterName = "@max";
                pMax.Value = max;
                cmd.Parameters.Add(pMax);

                var pDid = cmd.CreateParameter();
                pDid.ParameterName = "@deviceId";
                pDid.Value = deviceId;
                cmd.Parameters.Add(pDid);

                var pStore = cmd.CreateParameter();
                pStore.ParameterName = "@storeNumber";
                pStore.Value = device.StoreNumber;
                cmd.Parameters.Add(pStore);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(new CommandDto
                    {
                        CommandId = reader.GetGuid(0),
                        Type = reader.GetString(1),
                        Scope = reader.GetString(2),
                        PayloadJson = reader.IsDBNull(3) ? null : reader.GetString(3),
                        CreatedUtc = reader.GetDateTime(4),
                        ExpiresUtc = reader.IsDBNull(5) ? null : reader.GetDateTime(5)
                    });
                }
            }
            finally
            {
                await _db.Database.CloseConnectionAsync();
            }

            return Ok(results);
        }

        // ===== Enroll =====
        // Day 3 change: store protected DeviceSecret at rest, return plaintext ONLY when created
        [HttpPost("enroll")]
        public async Task<ActionResult<EnrollResponse>> Enroll([FromBody] EnrollRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.StoreNumber) || string.IsNullOrWhiteSpace(req.Hostname))
                return BadRequest("StoreNumber and Hostname are required.");

            var existing = await _db.Devices
                .FirstOrDefaultAsync(d => d.StoreNumber == req.StoreNumber && d.Hostname == req.Hostname);

            if (existing != null)
            {
                existing.LastSeenUtc = DateTime.UtcNow;
                existing.AgentVersion = req.AgentVersion ?? existing.AgentVersion;
                existing.OsVersion = req.OsVersion ?? existing.OsVersion;
                existing.LastIp = HttpContext.Connection.RemoteIpAddress?.ToString();

                // If device is missing a secret (or you imported devices), mint one and return it once.
                string? returnedSecret = null;
                if (string.IsNullOrWhiteSpace(existing.DeviceSecret))
                {
                    var secretBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
                    returnedSecret = Convert.ToBase64String(secretBytes);
                    existing.DeviceSecret = _secretProtection.Protect(returnedSecret);
                }

                await _db.SaveChangesAsync();

                return Ok(new EnrollResponse
                {
                    DeviceId = existing.DeviceId,
                    DeviceSecret = returnedSecret, // usually null
                    HeartbeatSeconds = 300,
                    PollSeconds = 30,
                    ServerUtc = DateTime.UtcNow
                });
            }

            var now = DateTime.UtcNow;

            // New device: generate plaintext secret and store protected
            var newSecretBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var plaintextSecret = Convert.ToBase64String(newSecretBytes);

            var device = new Device
            {
                DeviceId = Guid.NewGuid(),
                StoreNumber = req.StoreNumber.Trim(),
                Hostname = req.Hostname.Trim(),
                FirstSeenUtc = now,
                LastSeenUtc = now,
                AgentVersion = req.AgentVersion,
                OsVersion = req.OsVersion,
                LastIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
                IsEnabled = true,
                DeviceSecret = _secretProtection.Protect(plaintextSecret)
            };

            _db.Devices.Add(device);
            await _db.SaveChangesAsync();

            return Ok(new EnrollResponse
            {
                DeviceId = device.DeviceId,
                DeviceSecret = plaintextSecret, // return once
                HeartbeatSeconds = 300,
                PollSeconds = 30,
                ServerUtc = DateTime.UtcNow
            });
        }

        // ===== Heartbeat =====
        [HttpPost("heartbeat")]
        public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequest req)
        {
            var device = await GetDeviceFromHeader();
            if (device == null)
                return Unauthorized("Missing/invalid X-Device-Id or device not found.");

            var now = DateTime.UtcNow;

            device.LastSeenUtc = now;
            device.AgentVersion = req.AgentVersion ?? device.AgentVersion;
            device.OsVersion = req.OsVersion ?? device.OsVersion;
            device.LastIp = HttpContext.Connection.RemoteIpAddress?.ToString();

            var payload = JsonSerializer.Serialize(new
            {
                req.TimestampUtc,
                req.StoreNumber,
                req.Hostname,
                req.Metrics,
                req.Extra
            });

            _db.Heartbeats.Add(new Heartbeat
            {
                DeviceId = device.DeviceId,
                TimestampUtc = now,
                PayloadJson = payload
            });

            await _db.SaveChangesAsync();

            return Ok(new { serverUtc = DateTime.UtcNow, nextHeartbeatSeconds = 300 });
        }

        // ===== Commands: post result =====
        [HttpPost("commands/{commandId:guid}/result")]
        public async Task<IActionResult> PostResult([FromRoute] Guid commandId, [FromBody] CommandResultRequest req)
        {
            var device = await GetDeviceFromHeader();
            if (device == null)
                return Unauthorized("Missing/invalid X-Device-Id or device not found.");

            var cmd = await _db.Commands.FirstOrDefaultAsync(c => c.CommandId == commandId);
            if (cmd == null) return NotFound("Command not found.");

            _db.CommandResults.Add(new CommandResult
            {
                CommandId = commandId,
                DeviceId = device.DeviceId,
                Status = req.Status,
                ExitCode = req.ExitCode,
                StdOut = req.StdOut,
                StdErr = req.StdErr,
                StartedUtc = req.StartedUtc,
                FinishedUtc = req.FinishedUtc
            });

            var existingResult = await _db.CommandResults
                .FirstOrDefaultAsync(r => r.CommandId == commandId);

            if (existingResult != null)
            {
                return Ok(new { serverUtc = DateTime.UtcNow, note = "Result already recorded" });
            }

            cmd.Status = req.Status;

            await _db.SaveChangesAsync();

            return Ok(new { serverUtc = DateTime.UtcNow });
        }
    }
}
