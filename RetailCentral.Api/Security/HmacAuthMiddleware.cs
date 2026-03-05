using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using RetailCentral.Api.Data;
using System.Security.Cryptography;
using System.Text;

namespace RetailCentral.Api.Security
{
    public class HmacAuthMiddleware
    {
        private readonly RequestDelegate _next;

        // 5 minutes skew window
        private const long AllowedSkewMs = 300_000;

        public HmacAuthMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, RetailCentralDbContext db, DeviceSecretProtection secretProtection)
        {
            // Protect agent endpoints except enroll
            var path = context.Request.Path.Value ?? "";
            if (!path.StartsWith("/api/agent/v1", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/agent/v1/enroll", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // Required headers
            if (!context.Request.Headers.TryGetValue("X-Device-Id", out StringValues deviceIdValues) ||
                !Guid.TryParse(deviceIdValues.FirstOrDefault(), out var deviceId))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Missing/invalid X-Device-Id");
                return;
            }

            if (!context.Request.Headers.TryGetValue("X-Device-Timestamp", out StringValues tsValues) ||
                !long.TryParse(tsValues.FirstOrDefault(), out var tsMs))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Missing/invalid X-Device-Timestamp");
                return;
            }

            if (!context.Request.Headers.TryGetValue("X-Device-Signature", out StringValues sigValues))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Missing X-Device-Signature");
                return;
            }

            // Timestamp skew check (milliseconds)
            var nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (Math.Abs(nowUnixMs - tsMs) > AllowedSkewMs)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Timestamp skew too large");
                return;
            }

            // Load device + secret
            var device = await db.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
            if (device == null || !device.IsEnabled)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unknown/disabled device");
                return;
            }

            if (string.IsNullOrWhiteSpace(device.DeviceSecret))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Device has no secret (re-enroll)");
                return;
            }

            // Replay protection:
            // Reject same or older timestamp for this device.
            // NOTE: LastAuthTimestampUnix now stores Unix MILLISECONDS (name kept for compatibility).
            if (device.LastAuthTimestampUnix.HasValue && tsMs <= device.LastAuthTimestampUnix.Value)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Replay detected");
                return;
            }

            // Compute body SHA256 hex
            context.Request.EnableBuffering();

            byte[] bodyBytes;
            using (var ms = new MemoryStream())
            {
                await context.Request.Body.CopyToAsync(ms);
                bodyBytes = ms.ToArray();
                context.Request.Body.Position = 0;
            }

            var bodyShaHex = ToLowerHex(SHA256.HashData(bodyBytes));

            // Canonical string (timestamp is now ms)
            var method = context.Request.Method.ToUpperInvariant();
            var pathAndQuery = context.Request.Path + context.Request.QueryString; // must match client exactly
            var canonical = $"{tsMs}\n{method}\n{pathAndQuery}\n{bodyShaHex}";

            // Unprotect secret (DeviceSecret stored protected at rest; supports legacy plaintext)
            if (!secretProtection.TryUnprotect(device.DeviceSecret, out var plaintextSecretBase64))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Device has no secret (re-enroll)");
                return;
            }

            // HMAC verify
            byte[] secretBytes;
            try
            {
                secretBytes = Convert.FromBase64String(plaintextSecretBase64);
            }
            catch
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Invalid DeviceSecret encoding");
                return;
            }

            var expected = HmacSha256Base64(secretBytes, canonical);
            var provided = sigValues.FirstOrDefault() ?? "";

            if (!FixedTimeEqualsBase64(expected, provided))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Invalid signature");
                return;
            }

            // Persist the latest accepted timestamp (prevents replay)
            device.LastAuthTimestampUnix = tsMs;
            await db.SaveChangesAsync();

            // Optional: attach device for controller usage
            context.Items["Device"] = device;

            await _next(context);
        }

        private static string HmacSha256Base64(byte[] key, string data)
        {
            using var hmac = new HMACSHA256(key);
            var bytes = Encoding.UTF8.GetBytes(data);
            return Convert.ToBase64String(hmac.ComputeHash(bytes));
        }

        private static bool FixedTimeEqualsBase64(string a, string b)
        {
            try
            {
                var ba = Convert.FromBase64String(a);
                var bb = Convert.FromBase64String(b);
                return CryptographicOperations.FixedTimeEquals(ba, bb);
            }
            catch
            {
                return false;
            }
        }

        private static string ToLowerHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var bt in bytes) sb.Append(bt.ToString("x2"));
            return sb.ToString();
        }
    }
}
