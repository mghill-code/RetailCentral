using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RetailCentral.Api.Data;
using RetailCentral.Api.Data.Entities;
using RetailCentral.Api.Data.Enums;
using RetailCentral.Api.Dtos;
using RetailCentral.Api.Models;
using RetailCentral.Api.Security;
using RetailCentral.Api.Services;
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
        private readonly AuditService _auditService;
        private readonly AgentPollingHintsOptions _pollingHints;
        private readonly ILogger<AgentController> _logger;

        public AgentController(
            RetailCentralDbContext db,
            DeviceSecretProtection secretProtection,
            IConfiguration config,
            AuditService auditService,
            IOptions<AgentPollingHintsOptions> pollingHints,
            ILogger<AgentController> logger)
        {
            _db = db;
            _secretProtection = secretProtection;
            _config = config;
            _auditService = auditService;
            _pollingHints = pollingHints.Value;
            _logger = logger;
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
        public async Task<ActionResult<PendingCommandsResponseDto>> GetPending([FromQuery] int max = 5)
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

            var pollAfterSeconds = results.Count > 0
         ? _pollingHints.BusyPollSeconds
         : _pollingHints.IdlePollSeconds;

            _logger.LogInformation(
                "Returning {CommandCount} pending commands for DeviceId={DeviceId} with PollAfterSeconds={PollAfterSeconds}",
                results.Count,
                deviceId,
                pollAfterSeconds);

            return Ok(new PendingCommandsResponseDto
            {
                Commands = results,
                PollAfterSeconds = pollAfterSeconds
            });
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

            var wasDisabled = !device.IsEnabled;

            if (wasDisabled)
            {
                device.IsEnabled = true;
            }

            device.LastSeenUtc = now;
            device.AgentVersion = req.AgentVersion ?? device.AgentVersion;
            device.OsVersion = req.OsVersion ?? device.OsVersion;
            device.LastIp = remoteIp;

            var payload = JsonSerializer.Serialize(new
            {
                req.TimestampUtc,
                req.StoreNumber,
                req.Hostname,
                req.AgentVersion,
                req.OsVersion,
                req.Metrics,
                req.Inventory,
                req.UserActivity,
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
            await UpsertUserActivityFromHeartbeat(device.DeviceId, req.UserActivity);

            if (wasDisabled)
            {
                await _auditService.LogAsync(
                    action: "DeviceAutoReactivated",
                    targetType: "Device",
                    targetId: device.DeviceId.ToString(),
                    details: new
                    {
                        device.DeviceId,
                        device.Hostname,
                        req.StoreNumber,
                        remoteIp,
                        Reason = "Heartbeat received from retired device"
                    },
                    success: true);
            }

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
            cmd.LastError = string.Equals(req.Status, "Failed", StringComparison.OrdinalIgnoreCase)
                ? (req.StdErr ?? req.StdOut)
                : null;

            await _db.SaveChangesAsync();

            if (string.Equals(cmd.Type, "CollectSystemInfo", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(req.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                await UpsertRegisterInventoryFromSystemInfo(device.DeviceId, req.StdOut);
            }

            if (string.Equals(cmd.Type, "CollectSoftwareInventory", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(req.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                await UpsertSoftwareInventory(device.DeviceId, req.StdOut);
            }

            if (string.Equals(cmd.Type, "InstallPackage", StringComparison.OrdinalIgnoreCase))
            {
                await UpdateDeploymentTrackingAsync(cmd, device.DeviceId, req);
            }

            if (string.Equals(cmd.Type, "CollectProcessStatus", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(req.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                await UpsertProcessStatusInventory(device.DeviceId, req.StdOut);
            }

            return Ok(new { serverUtc = DateTime.UtcNow });
        }

        private async Task UpsertProcessStatusInventory(Guid deviceId, string? stdOut)
        {
            if (string.IsNullOrWhiteSpace(stdOut))
                return;

            using var doc = JsonDocument.Parse(stdOut);
            var root = doc.RootElement;

            var row = await _db.ProcessStatusInventories
                .FirstOrDefaultAsync(x => x.DeviceId == deviceId);

            if (row == null)
            {
                row = new ProcessStatusInventory
                {
                    DeviceId = deviceId
                };
                _db.ProcessStatusInventories.Add(row);
            }

            if (root.TryGetProperty("PosProcessName", out var posName))
                row.PosProcessName = posName.GetString();

            if (root.TryGetProperty("RetailShellProcessName", out var shellName))
                row.RetailShellProcessName = shellName.GetString();

            if (root.TryGetProperty("AgentProcessName", out var agentName))
                row.AgentProcessName = agentName.GetString();

            if (root.TryGetProperty("Pos", out var pos))
            {
                var isRunning = TryGetBool(pos, "IsRunning") ?? false;

                row.PosRunning = isRunning;
                row.PosProcessCount = TryGetInt(pos, "ProcessCount") ?? 0;
                row.PosCpuPercent = isRunning ? TryGetDecimal(pos, "CpuPercent") : null;
                row.PosWorkingSetMb = isRunning ? TryGetDecimal(pos, "WorkingSetMb") : null;
                row.PosStartedAtLocal = isRunning ? TryGetDateTime(pos, "StartedAtLocal") : null;
            }

            if (root.TryGetProperty("RetailShell", out var shell))
            {
                var isRunning = TryGetBool(shell, "IsRunning") ?? false;

                row.RetailShellRunning = isRunning;
                row.RetailShellProcessCount = TryGetInt(shell, "ProcessCount") ?? 0;
                row.RetailShellCpuPercent = isRunning ? TryGetDecimal(shell, "CpuPercent") : null;
                row.RetailShellWorkingSetMb = isRunning ? TryGetDecimal(shell, "WorkingSetMb") : null;
                row.RetailShellStartedAtLocal = isRunning ? TryGetDateTime(shell, "StartedAtLocal") : null;
            }

            if (root.TryGetProperty("Agent", out var agent))
            {
                var isRunning = TryGetBool(agent, "IsRunning") ?? false;

                row.AgentRunning = isRunning;
                row.AgentProcessCount = TryGetInt(agent, "ProcessCount") ?? 0;
                row.AgentCpuPercent = isRunning ? TryGetDecimal(agent, "CpuPercent") : null;
                row.AgentWorkingSetMb = isRunning ? TryGetDecimal(agent, "WorkingSetMb") : null;
                row.AgentStartedAtLocal = isRunning ? TryGetDateTime(agent, "StartedAtLocal") : null;
            }

            row.UpdatedUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        private async Task UpdateDeploymentTrackingAsync(Command cmd, Guid deviceId, CommandResultRequest req)
        {
            var deploymentId = TryGetDeploymentId(cmd.PayloadJson);
            if (deploymentId == null)
                return;

            var deployment = await _db.Deployments
                .Include(x => x.DeploymentDevices)
                .FirstOrDefaultAsync(x => x.Id == deploymentId.Value);

            if (deployment == null)
                return;

            var row = deployment.DeploymentDevices.FirstOrDefault(x => x.DeviceId == deviceId);
            if (row == null)
                return;

            var nowUtc = DateTime.UtcNow;
            row.LastHeartbeatUtc = nowUtc;
            row.ExitCode = req.ExitCode;
            row.ResultMessage = BuildResultMessage(req);
            row.AttemptCount = Math.Max(row.AttemptCount, 1);
            row.DownloadStartedUtc ??= req.StartedUtc ?? nowUtc;
            row.ExecuteStartedUtc ??= req.StartedUtc ?? nowUtc;

            if (string.Equals(req.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                row.Status = (int)DeploymentDeviceStatus.Completed;
                row.DownloadStatus = (int)DeploymentDownloadStatus.Downloaded;
                row.ExecuteStatus = (int)DeploymentExecuteStatus.Completed;
                row.DownloadCompletedUtc ??= req.FinishedUtc ?? nowUtc;
                row.ExecuteCompletedUtc = req.FinishedUtc ?? nowUtc;
                row.FilePath = TryExtractFilePath(req.StdOut);
            }
            else if (string.Equals(req.Status, "Deferred", StringComparison.OrdinalIgnoreCase))
            {
                row.Status = (int)DeploymentDeviceStatus.WaitingForWindow;
                row.DownloadStatus = (int)DeploymentDownloadStatus.Downloaded;
                row.ExecuteStatus = (int)DeploymentExecuteStatus.Waiting;
                row.DownloadCompletedUtc ??= req.FinishedUtc ?? nowUtc;
            }
            else
            {
                row.Status = MapFailedStatus(req);
                row.DownloadStatus = row.Status == (int)DeploymentDeviceStatus.FailedExecution || row.Status == (int)DeploymentDeviceStatus.TimedOut
                    ? (int)DeploymentDownloadStatus.Downloaded
                    : (int)DeploymentDownloadStatus.Failed;
                row.ExecuteStatus = row.Status == (int)DeploymentDeviceStatus.TimedOut
                    ? (int)DeploymentExecuteStatus.TimedOut
                    : (int)DeploymentExecuteStatus.Failed;
                row.DownloadCompletedUtc ??= req.FinishedUtc ?? nowUtc;
                row.ExecuteCompletedUtc = req.FinishedUtc ?? nowUtc;
            }

            RecalculateDeploymentRollup(deployment);
            await _db.SaveChangesAsync();
        }

        private static int? TryGetDeploymentId(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("deploymentId", out var prop) && prop.TryGetInt32(out var deploymentId))
                    return deploymentId;
            }
            catch
            {
            }

            return null;
        }

        private static int MapFailedStatus(CommandResultRequest req)
        {
            var error = $"{req.StdErr}\n{req.StdOut}";

            if (req.ExitCode == 902 || error.Contains("SHA256 mismatch", StringComparison.OrdinalIgnoreCase))
                return (int)DeploymentDeviceStatus.FailedHash;

            if (req.ExitCode == 124 || error.Contains("Timed out", StringComparison.OrdinalIgnoreCase))
                return (int)DeploymentDeviceStatus.TimedOut;

            if (error.Contains("HttpRequestException", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("No such host is known", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("DownloadAsync", StringComparison.OrdinalIgnoreCase))
                return (int)DeploymentDeviceStatus.FailedDownload;

            return (int)DeploymentDeviceStatus.FailedExecution;
        }

        private static string? TryExtractFilePath(string? stdOut)
        {
            if (string.IsNullOrWhiteSpace(stdOut))
                return null;

            const string marker = "Downloaded to: ";
            var idx = stdOut.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return null;

            var remainder = stdOut[(idx + marker.Length)..];
            var line = remainder.Split('\n', '\r').FirstOrDefault();
            return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
        }

        private static string? BuildResultMessage(CommandResultRequest req)
        {
            if (!string.IsNullOrWhiteSpace(req.StdErr))
                return Truncate(req.StdErr, 1800);

            if (!string.IsNullOrWhiteSpace(req.StdOut))
                return Truncate(req.StdOut, 1800);

            return string.IsNullOrWhiteSpace(req.Status) ? null : req.Status;
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            return value.Length <= maxLength ? value : value[..maxLength];
        }

        private static void RecalculateDeploymentRollup(Deployment deployment)
        {
            var devices = deployment.DeploymentDevices.ToList();
            if (devices.Count == 0)
                return;

            var allCancelled = devices.All(x => x.Status == (int)DeploymentDeviceStatus.Cancelled);
            var allCompleted = devices.All(x => x.Status == (int)DeploymentDeviceStatus.Completed);
            var allTerminal = devices.All(x => IsTerminalDeploymentStatus(x.Status));
            var anyFailure = devices.Any(x =>
                x.Status == (int)DeploymentDeviceStatus.FailedDownload ||
                x.Status == (int)DeploymentDeviceStatus.FailedHash ||
                x.Status == (int)DeploymentDeviceStatus.FailedExecution ||
                x.Status == (int)DeploymentDeviceStatus.TimedOut);

            deployment.StartedUtc ??= DateTime.UtcNow;

            if (allCancelled)
            {
                deployment.Status = (int)DeploymentStatus.Cancelled;
                deployment.CompletedUtc ??= DateTime.UtcNow;
                return;
            }

            if (allCompleted)
            {
                deployment.Status = (int)DeploymentStatus.Completed;
                deployment.CompletedUtc ??= DateTime.UtcNow;
                return;
            }

            if (allTerminal && anyFailure)
            {
                deployment.Status = (int)DeploymentStatus.PartialFailure;
                deployment.CompletedUtc ??= DateTime.UtcNow;
                return;
            }

            deployment.Status = (int)DeploymentStatus.Active;
            deployment.CompletedUtc = null;
        }

        private static bool IsTerminalDeploymentStatus(int status)
        {
            return status == (int)DeploymentDeviceStatus.Completed ||
                   status == (int)DeploymentDeviceStatus.FailedDownload ||
                   status == (int)DeploymentDeviceStatus.FailedHash ||
                   status == (int)DeploymentDeviceStatus.FailedExecution ||
                   status == (int)DeploymentDeviceStatus.TimedOut ||
                   status == (int)DeploymentDeviceStatus.Cancelled;
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

            row.ComputerName =
                req.Inventory?.ComputerName
                ?? req.Hostname
                ?? row.ComputerName;

            row.Store =
                req.Inventory?.Store
                ?? req.StoreNumber
                ?? device.StoreNumber
                ?? row.Store;

            if (!string.IsNullOrWhiteSpace(req.Inventory?.RegisterNumber))
                row.RegisterNumber = req.Inventory.RegisterNumber;

            row.IPAddress =
                !string.IsNullOrWhiteSpace(req.Inventory?.IPAddress)
                    ? req.Inventory.IPAddress
                    : !string.IsNullOrWhiteSpace(remoteIp)
                        ? remoteIp
                        : row.IPAddress;

            if (!string.IsNullOrWhiteSpace(req.Inventory?.MACAddress))
                row.MACAddress = req.Inventory.MACAddress;

            if (!string.IsNullOrWhiteSpace(req.Inventory?.Domain))
                row.Domain = req.Inventory.Domain;

            row.OSVersion =
                req.Inventory?.OSVersion
                ?? req.OsVersion
                ?? row.OSVersion;

            if (!string.IsNullOrWhiteSpace(req.Inventory?.CPUArch))
                row.CPUArch = req.Inventory.CPUArch;

            row.LastHeartbeatUtc = DateTime.UtcNow;
            row.UpdatedUtc = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(req.Inventory?.StoreName))
                row.StoreName = req.Inventory.StoreName;

            if (!string.IsNullOrWhiteSpace(req.Inventory?.StoreAddress))
                row.StoreAddress = req.Inventory.StoreAddress;

            if (!string.IsNullOrWhiteSpace(req.Inventory?.StoreCity))
                row.StoreCity = req.Inventory.StoreCity;

            if (!string.IsNullOrWhiteSpace(req.Inventory?.StoreState))
                row.StoreState = req.Inventory.StoreState;

            if (!string.IsNullOrWhiteSpace(req.Inventory?.StoreZipCode))
                row.StoreZipCode = req.Inventory.StoreZipCode;

            if (!string.IsNullOrWhiteSpace(req.Inventory?.ReleaseLevel))
                row.ReleaseLevel = req.Inventory.ReleaseLevel;

            if (!string.IsNullOrWhiteSpace(req.Inventory?.ReleaseApplied))
                row.ReleaseApplied = req.Inventory.ReleaseApplied;

            if (!string.IsNullOrWhiteSpace(req.Inventory?.VerifoneModel))
                row.VerifoneModel = req.Inventory.VerifoneModel;

            if (!string.IsNullOrWhiteSpace(req.Inventory?.VerifoneIP))
                row.VerifoneIP = req.Inventory.VerifoneIP;

            if (!string.IsNullOrWhiteSpace(req.Inventory?.ScannerName))
                row.ScannerName = req.Inventory.ScannerName;

            if (!string.IsNullOrWhiteSpace(req.Inventory?.ScannerSerialNumber))
                row.ScannerSerialNumber = req.Inventory.ScannerSerialNumber;

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

        private async Task UpsertSoftwareInventory(Guid deviceId, string? stdOut)
        {
            if (string.IsNullOrWhiteSpace(stdOut))
                return;

            using var doc = JsonDocument.Parse(stdOut);
            var root = doc.RootElement;

            var existingSoftware = _db.Set<InstalledSoftware>()
                .Where(x => x.DeviceId == deviceId);

            var existingUpdates = _db.Set<InstalledWindowsUpdate>()
                .Where(x => x.DeviceId == deviceId);

            _db.RemoveRange(existingSoftware);
            _db.RemoveRange(existingUpdates);

            if (root.TryGetProperty("InstalledSoftware", out var softwareArray) &&
                softwareArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in softwareArray.EnumerateArray())
                {
                    _db.Add(new InstalledSoftware
                    {
                        DeviceId = deviceId,
                        Name = TryGetString(item, "Name"),
                        Version = TryGetString(item, "Version"),
                        Publisher = TryGetString(item, "Publisher"),
                        InstallDate = TryGetString(item, "InstallDate"),
                        UpdatedUtc = DateTime.UtcNow
                    });
                }
            }

            if (root.TryGetProperty("WindowsUpdates", out var updatesArray) &&
                updatesArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in updatesArray.EnumerateArray())
                {
                    _db.Add(new InstalledWindowsUpdate
                    {
                        DeviceId = deviceId,
                        HotFixId = TryGetString(item, "HotFixId"),
                        Description = TryGetString(item, "Description"),
                        InstalledOn = TryGetString(item, "InstalledOn"),
                        UpdatedUtc = DateTime.UtcNow
                    });
                }
            }

            await _db.SaveChangesAsync();
        }

        private async Task UpsertUserActivityFromHeartbeat(Guid deviceId, HeartbeatUserActivityDto? dto)
        {
            if (dto == null)
                return;

            var nowUtc = DateTime.UtcNow;

            var row = await _db.UserActivityInventories
                .FirstOrDefaultAsync(x => x.DeviceId == deviceId);

            if (row == null)
            {
                row = new UserActivityInventory
                {
                    DeviceId = deviceId
                };
                _db.UserActivityInventories.Add(row);
            }

            row.CapturedUtc = dto.CapturedUtc;
            row.LastInputUtc = dto.LastInputUtc;
            row.IdleSeconds = dto.IdleSeconds;
            row.SessionState = dto.SessionState;
            row.ConsoleUserName = dto.ConsoleUserName;
            row.IsUserActive = dto.IsUserActive;
            row.IsPosForeground = dto.IsPosForeground;
            row.UpdatedUtc = nowUtc;

            var historyCutoffUtc = nowUtc.AddMinutes(-5);
            var lastHistory = await _db.UserActivityHistories
                .Where(x => x.DeviceId == deviceId)
                .OrderByDescending(x => x.CapturedUtc)
                .FirstOrDefaultAsync();

            var shouldWriteHistory = lastHistory == null
                || lastHistory.SessionState != dto.SessionState
                || lastHistory.IsUserActive != dto.IsUserActive
                || lastHistory.IsPosForeground != dto.IsPosForeground
                || GetIdleBucket(lastHistory.IdleSeconds) != GetIdleBucket(dto.IdleSeconds)
                || lastHistory.CapturedUtc <= historyCutoffUtc;

            if (shouldWriteHistory)
            {
                _db.UserActivityHistories.Add(new UserActivityHistory
                {
                    DeviceId = deviceId,
                    CapturedUtc = dto.CapturedUtc ?? nowUtc,
                    LastInputUtc = dto.LastInputUtc,
                    IdleSeconds = dto.IdleSeconds,
                    SessionState = dto.SessionState,
                    IsUserActive = dto.IsUserActive,
                    IsPosForeground = dto.IsPosForeground,
                    CreatedUtc = nowUtc
                });
            }

            await _db.SaveChangesAsync();
        }

        private static string GetIdleBucket(int? idleSeconds)
        {
            if (!idleSeconds.HasValue)
                return "Unknown";

            if (idleSeconds.Value < 60)
                return "Active";

            if (idleSeconds.Value < 300)
                return "Warm";

            if (idleSeconds.Value < 1800)
                return "Idle";

            return "LongIdle";
        }

        private static bool? TryGetBool(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(propertyName, out var prop) &&
                (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False))
            {
                return prop.GetBoolean();
            }

            return null;
        }

        private static int? TryGetInt(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(propertyName, out var prop) &&
                prop.ValueKind == JsonValueKind.Number &&
                prop.TryGetInt32(out var value))
            {
                return value;
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

        public sealed class PendingCommandsResponseDto
        {
            public List<CommandDto> Commands { get; set; } = new();
            public int PollAfterSeconds { get; set; }
        }
    }
}