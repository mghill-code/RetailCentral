using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data;
using RetailCentral.Api.Dtos;
using RetailCentral.Api.Models;
using System.Text.Json;

namespace RetailCentral.Api.Controllers
{
    [ApiController]
    [Route("api/agent/v1")]
    public class AgentController : ControllerBase
    {
        private readonly RetailCentralDbContext _db;

        public AgentController(RetailCentralDbContext db)
        {
            _db = db;
        }

        [HttpPost("enroll")]
        public async Task<ActionResult<EnrollResponse>> Enroll([FromBody] EnrollRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.StoreNumber) || string.IsNullOrWhiteSpace(req.Hostname))
                return BadRequest("StoreNumber and Hostname are required.");

            // If you want: prevent duplicates by store+hostname
            var existing = await _db.Devices
                .FirstOrDefaultAsync(d => d.StoreNumber == req.StoreNumber && d.Hostname == req.Hostname);

            if (existing != null)
            {
                existing.LastSeenUtc = DateTime.UtcNow;
                existing.AgentVersion = req.AgentVersion ?? existing.AgentVersion;
                existing.OsVersion = req.OsVersion ?? existing.OsVersion;
                existing.LastIp = HttpContext.Connection.RemoteIpAddress?.ToString();

                await _db.SaveChangesAsync();

                return Ok(new EnrollResponse
                {
                    DeviceId = existing.DeviceId,
                    HeartbeatSeconds = 300,
                    PollSeconds = 30,
                    ServerUtc = DateTime.UtcNow
                });
            }

            var now = DateTime.UtcNow;

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
                IsEnabled = true
            };

            _db.Devices.Add(device);
            await _db.SaveChangesAsync();

            return Ok(new EnrollResponse
            {
                DeviceId = device.DeviceId,
                HeartbeatSeconds = 300,
                PollSeconds = 30,
                ServerUtc = DateTime.UtcNow
            });
        }

        [HttpPost("heartbeat")]
        public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequest req)
        {
            // Day 1: identify by StoreNumber+Hostname (we haven't added DeviceId headers yet)
            var device = await _db.Devices
                .FirstOrDefaultAsync(d => d.StoreNumber == req.StoreNumber && d.Hostname == req.Hostname);

            if (device == null)
                return NotFound("Device not enrolled.");

            var now = DateTime.UtcNow;

            device.LastSeenUtc = now;
            device.AgentVersion = req.AgentVersion ?? device.AgentVersion;
            device.OsVersion = req.OsVersion ?? device.OsVersion;
            device.LastIp = HttpContext.Connection.RemoteIpAddress?.ToString();

            // store payload as JSON blob
            var payload = JsonSerializer.Serialize(new
            {
                req.TimestampUtc,
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
    }
}