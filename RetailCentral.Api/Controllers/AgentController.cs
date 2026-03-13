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
        private readonly IConfiguration _config;

        public AgentController(
            RetailCentralDbContext db,
            DeviceSecretProtection secretProtection,
            IConfiguration config)
        {
            _db = db;
            _secretProtection = secretProtection;
            _config = config;
        }

        private async Task<Device?> GetDeviceFromHeader()
        {
            if (!Request.Headers.TryGetValue("X-Device-Id", out var values))
                return null;

            if (!Guid.TryParse(values.FirstOrDefault(), out var deviceId))
                return null;

            return await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
        }

        [HttpGet("commands/pending")]
        public async Task<ActionResult<List<CommandDto>>> GetPending([FromQuery] int max = 5)
        {
            if (max <= 0) max = 1;
            if (max > 20) max = 20;

            var device = await GetDeviceFromHeader();
            if (device == null)
                return Unauthorized("Missing/invalid X-Device-Id or device not found.");

            var deviceId = device.DeviceId;

            var sql = @"
;WITH cte AS (
  SELECT TOP (@max) *
  FROM Commands WITH (UPDLOCK, READPAST, ROWLOCK)
  WHERE Status = 'Pending'
    AND (ExpiresUtc IS NULL OR ExpiresUtc > SYSUTCDATETIME())
    AND (
      DeviceId = @deviceId
      OR (DeviceId IS NULL AND StoreNumber = @storeNumber)
      OR (
          DeviceId IS NULL
          AND StoreNumber IS NULL
          AND GroupName IS NOT NULL
          AND GroupName IN (
              SELECT g.GroupName
              FROM DeviceGroupMembers m
              INNER JOIN DeviceGroups g ON g.DeviceGroupId = m.DeviceGroupId
              WHERE m.DeviceId = @deviceId
          )
      )
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
                existing.MachineName = req.MachineName ?? existing.MachineName;
                existing.MachineGuid = req.MachineGuid ?? existing.MachineGuid;

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
                    DeviceSecret = returnedSecret,
                    HeartbeatSeconds = 300,
                    PollSeconds = 30,
                    ServerUtc = DateTime.UtcNow
                });
            }

            var expectedBootstrapKey = _config["Enrollment:BootstrapKey"];
            if (string.IsNullOrWhiteSpace(expectedBootstrapKey))
                return StatusCode(500, "Enrollment bootstrap key is not configured on server.");

            if (string.IsNullOrWhiteSpace(req.BootstrapKey) || req.BootstrapKey != expectedBootstrapKey)
                return Unauthorized("Invalid bootstrap key.");

            var now = DateTime.UtcNow;

            var newSecretBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var plaintextSecret = Convert.ToBase64String(newSecretBytes);

            var device = new Device
            {
                DeviceId = Guid.NewGuid(),
                StoreNumber = req.StoreNumber.Trim(),
                Hostname = req.Hostname.Trim(),
                MachineName = req.MachineName?.Trim(),
                MachineGuid = req.MachineGuid?.Trim(),
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
                DeviceSecret = plaintextSecret,
                HeartbeatSeconds = 300,
                PollSeconds = 30,
                ServerUtc = DateTime.UtcNow
            });
        }

        [HttpPost("heartbeat")]
        public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequest req)
        {
            var device = await GetDeviceFromHeader();
            if (device == null)
                return Unauthorized("Missing/invalid X-Device-Id or device not found.");

            var now = DateTime.UtcNow;
            var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();

            var hadInventoryRow = await _db.RegisterInventories
                .AnyAsync(x => x.DeviceId == device.DeviceId);

            device.LastSeenUtc = now;
            device.AgentVersion = req.AgentVersion ?? device.AgentVersion;
            device.OsVersion = req.OsVersion ?? device.OsVersion;
            device.LastIp = remoteIp;

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

            await UpsertRegisterInventoryFromHeartbeat(device, req, remoteIp);

            if (!hadInventoryRow)
            {
                await EnsureCollectSystemInfoQueuedAsync(
                    device,
                    issuedBy: "System-FirstHeartbeat",
                    priority: 50);
            }

            return Ok(new { serverUtc = DateTime.UtcNow, nextHeartbeatSeconds = 300 });
        }

        [HttpPost("commands/{commandId:guid}/result")]
        public async Task<IActionResult> PostResult([FromRoute] Guid commandId, [FromBody] CommandResultRequest req)
        {
            var device = await GetDeviceFromHeader();
            if (device == null)
                return Unauthorized("Missing/invalid X-Device-Id or device not found.");

            var cmd = await _db.Commands.FirstOrDefaultAsync(c => c.CommandId == commandId);
            if (cmd == null)
                return NotFound("Command not found.");

            var existingResult = await _db.CommandResults
                .FirstOrDefaultAsync(r => r.CommandId == commandId);

            if (existingResult != null)
            {
                return Ok(new { serverUtc = DateTime.UtcNow, note = "Result already recorded" });
            }

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

            cmd.Status = req.Status;

            await _db.SaveChangesAsync();

            if (string.Equals(cmd.Type, "CollectSystemInfo", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(req.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                await UpsertRegisterInventoryFromSystemInfo(device.DeviceId, req.StdOut);
            }

            return Ok(new { serverUtc = DateTime.UtcNow });
        }

        private async Task EnsureCollectSystemInfoQueuedAsync(Device device, string issuedBy, int priority)
        {
            var exists = await _db.Commands.AnyAsync(c =>
                c.DeviceId == device.DeviceId &&
                c.Type == "CollectSystemInfo" &&
                (c.Status == "Pending" || c.Status == "InProgress"));

            if (exists)
                return;

            _db.Commands.Add(new Command
            {
                CommandId = Guid.NewGuid(),
                DeviceId = device.DeviceId,
                StoreNumber = device.StoreNumber,
                Scope = "Device",
                Type = "CollectSystemInfo",
                PayloadJson = "{}",
                Status = "Pending",
                Priority = priority,
                CreatedUtc = DateTime.UtcNow,
                AttemptCount = 0,
                MaxAttempts = 3,
                IssuedBy = issuedBy,
                IssuedUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }

        private async Task UpsertRegisterInventoryFromHeartbeat(Device device, HeartbeatRequest req, string? remoteIp)
        {
            var row = await _db.RegisterInventories
                .FirstOrDefaultAsync(x => x.DeviceId == device.DeviceId);

            if (row == null)
            {
                row = new RegisterInventory
                {
                    DeviceId = device.DeviceId
                };
                _db.RegisterInventories.Add(row);
            }

            row.ComputerName = req.Hostname ?? row.ComputerName;
            row.Store = req.StoreNumber ?? device.StoreNumber ?? row.Store;

            row.IPAddress = !string.IsNullOrWhiteSpace(remoteIp)
                ? remoteIp
                : row.IPAddress;

            row.OSVersion = req.OsVersion ?? row.OSVersion;
            row.LastHeartbeatUtc = DateTime.UtcNow;
            row.UpdatedUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        private async Task UpsertRegisterInventoryFromSystemInfo(Guid deviceId, string? stdOut)
        {
            if (string.IsNullOrWhiteSpace(stdOut))
                return;

            using var doc = JsonDocument.Parse(stdOut);
            var root = doc.RootElement;

            var row = await _db.RegisterInventories
                .FirstOrDefaultAsync(x => x.DeviceId == deviceId);

            if (row == null)
            {
                row = new RegisterInventory
                {
                    DeviceId = deviceId
                };
                _db.RegisterInventories.Add(row);
            }

            row.ComputerName = TryGetString(root, "MachineName") ?? row.ComputerName;
            row.Domain = TryGetString(root, "DomainName") ?? row.Domain;
            row.OSVersion = TryGetString(root, "OSVersion") ?? row.OSVersion;

            row.CPUArch = root.TryGetProperty("Is64BitOS", out var is64BitOsProp) &&
                          is64BitOsProp.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? (is64BitOsProp.GetBoolean() ? "X64" : "X86")
                : row.CPUArch;

            if (root.TryGetProperty("ComputerSystem", out var cs))
            {
                row.Manufacturer = TryGetString(cs, "Manufacturer") ?? row.Manufacturer;
                row.Model = TryGetString(cs, "Model") ?? row.Model;
            }

            if (root.TryGetProperty("Bios", out var bios))
            {
                row.SerialNumber = TryGetString(bios, "SerialNumber") ?? row.SerialNumber;
            }

            if (root.TryGetProperty("Memory", out var mem))
            {
                var totalGb = TryGetDecimal(mem, "TotalPhysicalMemoryGB");
                if (totalGb != null)
                    row.Memory = $"{totalGb:0.##} GB";
            }

            if (root.TryGetProperty("OperatingSystem", out var os))
            {
                row.LastReboot = TryGetDateTime(os, "LastBootUpTimeUtc") ?? row.LastReboot;
                row.SystemBuildDate = TryGetDateTime(os, "InstallDateUtc") ?? row.SystemBuildDate;
            }

            if (root.TryGetProperty("Network", out var net) && net.ValueKind == JsonValueKind.Array)
            {
                var first = net.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object)
                {
                    row.MACAddress = TryGetString(first, "MacAddress") ?? row.MACAddress;

                    if (first.TryGetProperty("IPv4Addresses", out var ipv4) &&
                        ipv4.ValueKind == JsonValueKind.Array)
                    {
                        var ip = ipv4.EnumerateArray()
                            .Select(x => x.GetString())
                            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                        row.IPAddress = ip ?? row.IPAddress;
                    }
                }
            }

            if (root.TryGetProperty("Drives", out var drives) && drives.ValueKind == JsonValueKind.Array)
            {
                JsonElement firstDrive = default;

                foreach (var drive in drives.EnumerateArray())
                {
                    if (drive.ValueKind == JsonValueKind.Object &&
                        (TryGetString(drive, "Name")?.StartsWith("C:", StringComparison.OrdinalIgnoreCase) ?? false))
                    {
                        firstDrive = drive;
                        break;
                    }

                    if (firstDrive.ValueKind == JsonValueKind.Undefined)
                        firstDrive = drive;
                }

                if (firstDrive.ValueKind == JsonValueKind.Object)
                {
                    var sizeGb = TryGetDecimal(firstDrive, "TotalSizeGB");
                    var freeGb = TryGetDecimal(firstDrive, "AvailableFreeSpaceGB");

                    if (sizeGb != null)
                        row.HardDriveSize = $"{sizeGb:0.##} GB";

                    if (freeGb != null)
                        row.HardDriveFreeSpace = $"{freeGb:0.##} GB";
                }
            }

            row.UpdatedUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        private static string? TryGetString(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(propertyName, out var prop) &&
                prop.ValueKind != JsonValueKind.Null)
            {
                return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
            }

            return null;
        }

        private static decimal? TryGetDecimal(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(propertyName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var val))
                    return val;
            }

            return null;
        }

        private static DateTime? TryGetDateTime(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(propertyName, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(prop.GetString(), out var dt))
                    return dt;
            }

            return null;
        }
    }
}