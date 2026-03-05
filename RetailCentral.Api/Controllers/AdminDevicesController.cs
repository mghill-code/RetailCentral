using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data;
using RetailCentral.Api.Security;

namespace RetailCentral.Api.Controllers
{
    [ApiController]
    [Route("api/admin/v1/devices")]
    public class AdminDevicesController : ControllerBase
    {
        private readonly RetailCentralDbContext _db;
        private readonly DeviceSecretProtection _secretProtection;

        public AdminDevicesController(RetailCentralDbContext db, DeviceSecretProtection secretProtection)
        {
            _db = db;
            _secretProtection = secretProtection;
        }

        [HttpPost("{deviceId:guid}/disable")]
        public async Task<IActionResult> Disable([FromRoute] Guid deviceId)
        {
            var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
            if (device == null) return NotFound();

            device.IsEnabled = false;
            await _db.SaveChangesAsync();
            return Ok(new { deviceId, device.IsEnabled });
        }

        [HttpPost("{deviceId:guid}/enable")]
        public async Task<IActionResult> Enable([FromRoute] Guid deviceId)
        {
            var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
            if (device == null) return NotFound();

            device.IsEnabled = true;
            await _db.SaveChangesAsync();
            return Ok(new { deviceId, device.IsEnabled });
        }

        [HttpPost("{deviceId:guid}/rotate-secret")]
        public async Task<IActionResult> RotateSecret([FromRoute] Guid deviceId)
        {
            var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
            if (device == null) return NotFound();

            var secretBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var plaintextSecret = Convert.ToBase64String(secretBytes);

            device.DeviceSecret = _secretProtection.Protect(plaintextSecret);
            device.LastAuthTimestampUnix = null; // reset replay tracking
            await _db.SaveChangesAsync();

            // Return plaintext ONCE (admin must securely deliver it to the register)
            return Ok(new { deviceId, deviceSecret = plaintextSecret });
        }
    }
}