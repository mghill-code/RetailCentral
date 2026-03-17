
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data;
using RetailCentral.Api.Data.Entities;
using RetailCentral.Api.Data.Enums;
using RetailCentral.Api.Models;
using RetailCentral.Api.Models.Deployments;

namespace RetailCentral.Api.Services.Deployments
{
    public class DeploymentService : IDeploymentService
    {
        private readonly RetailCentralDbContext _db;

        public DeploymentService(RetailCentralDbContext db)
        {
            _db = db;
        }

        public async Task<Package> CreatePackageAsync(CreatePackageRequest request, string? createdBy, CancellationToken cancellationToken)
        {
            ValidatePackageRequest(request);

            var entity = new Package
            {
                Name = request.Name.Trim(),
                Version = request.Version?.Trim(),
                Description = request.Description?.Trim(),
                PackageType = request.PackageType,
                FileName = request.FileName.Trim(),
                StoragePath = request.StoragePath.Trim(),
                Sha256 = request.Sha256.Trim(),
                FileSizeBytes = request.FileSizeBytes,
                ExecutionCommand = request.ExecutionCommand?.Trim(),
                ExecutionArguments = request.ExecutionArguments?.Trim(),
                WorkingDirectory = request.WorkingDirectory?.Trim(),
                TimeoutSeconds = request.TimeoutSeconds,
                RebootBehavior = request.RebootBehavior,
                IsEnabled = true,
                CreatedUtc = DateTime.UtcNow,
                CreatedBy = createdBy
            };

            _db.Packages.Add(entity);
            await _db.SaveChangesAsync(cancellationToken);
            return entity;
        }

        public async Task<Package?> GetPackageByIdAsync(int packageId, CancellationToken cancellationToken)
        {
            return await _db.Packages.FirstOrDefaultAsync(x => x.Id == packageId, cancellationToken);
        }

        public async Task<Package> UpdatePackageAsync(int packageId, CreatePackageRequest request, string? updatedBy, CancellationToken cancellationToken)
        {
            ValidatePackageRequest(request);

            var entity = await _db.Packages.FirstOrDefaultAsync(x => x.Id == packageId, cancellationToken)
                ?? throw new InvalidOperationException("Package not found.");

            entity.Name = request.Name.Trim();
            entity.Version = request.Version?.Trim();
            entity.Description = request.Description?.Trim();
            entity.PackageType = request.PackageType;
            entity.FileName = request.FileName.Trim();
            entity.StoragePath = request.StoragePath.Trim();
            entity.Sha256 = request.Sha256.Trim();
            entity.FileSizeBytes = request.FileSizeBytes;
            entity.ExecutionCommand = request.ExecutionCommand?.Trim();
            entity.ExecutionArguments = request.ExecutionArguments?.Trim();
            entity.WorkingDirectory = request.WorkingDirectory?.Trim();
            entity.TimeoutSeconds = request.TimeoutSeconds;
            entity.RebootBehavior = request.RebootBehavior;
            entity.CreatedBy = entity.CreatedBy ?? updatedBy;

            await _db.SaveChangesAsync(cancellationToken);
            return entity;
        }

        public async Task DisablePackageAsync(int packageId, CancellationToken cancellationToken)
        {
            var entity = await _db.Packages.FirstOrDefaultAsync(x => x.Id == packageId, cancellationToken)
                ?? throw new InvalidOperationException("Package not found.");

            entity.IsEnabled = false;
            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task DeletePackageAsync(int packageId, CancellationToken cancellationToken)
        {
            var entity = await _db.Packages
                .Include(x => x.Deployments)
                .FirstOrDefaultAsync(x => x.Id == packageId, cancellationToken)
                ?? throw new InvalidOperationException("Package not found.");

            if (entity.Deployments.Any())
                throw new InvalidOperationException("Package cannot be hard deleted while deployments still reference it. Delete those deployments first.");

            _db.Packages.Remove(entity);
            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task<List<Package>> GetPackagesAsync(CancellationToken cancellationToken)
        {
            return await _db.Packages
                .OrderByDescending(x => x.CreatedUtc)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<Deployment>> GetDeploymentsAsync(CancellationToken cancellationToken)
        {
            return await _db.Deployments
                .Include(x => x.Package)
                .Include(x => x.DeploymentDevices)
                .OrderByDescending(x => x.CreatedUtc)
                .ToListAsync(cancellationToken);
        }

        public async Task<Deployment?> GetDeploymentByIdAsync(int deploymentId, CancellationToken cancellationToken)
        {
            return await _db.Deployments
                .Include(x => x.Package)
                .Include(x => x.DeploymentDevices)
                .FirstOrDefaultAsync(x => x.Id == deploymentId, cancellationToken);
        }

        public async Task<Deployment> CreateDeploymentAsync(CreateDeploymentRequest request, string? createdBy, CancellationToken cancellationToken)
        {
            ValidateDeploymentRequest(request);

            var package = await _db.Packages
                .FirstOrDefaultAsync(x => x.Id == request.PackageId && x.IsEnabled, cancellationToken)
                ?? throw new InvalidOperationException("Package not found or disabled.");

            var targetValue = request.TargetValue?.Trim() ?? string.Empty;
            var targetDevices = await ResolveTargetDevicesAsync(request.TargetType, targetValue, cancellationToken);
            if (targetDevices.Count == 0)
                throw new InvalidOperationException("No matching enabled devices were found for the selected target.");

            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

            var nowUtc = DateTime.UtcNow;
            var deployment = new Deployment
            {
                PackageId = request.PackageId,
                TargetType = request.TargetType,
                TargetValue = targetValue,
                ExecuteMode = request.ExecuteMode,
                WindowStartLocal = request.WindowStartLocal,
                WindowEndLocal = request.WindowEndLocal,
                UseStoreLocalTime = request.UseStoreLocalTime,
                AllowOutsideWindow = request.AllowOutsideWindow,
                RetryCount = request.RetryCount,
                Notes = request.Notes?.Trim(),
                Status = (int)DeploymentStatus.Queued,
                CreatedUtc = nowUtc,
                CreatedBy = createdBy
            };

            _db.Deployments.Add(deployment);
            await _db.SaveChangesAsync(cancellationToken);

            foreach (var device in targetDevices)
            {
                _db.DeploymentDevices.Add(new DeploymentDevice
                {
                    DeploymentId = deployment.Id,
                    DeviceId = device.DeviceId,
                    StoreNumber = device.StoreNumber,
                    Hostname = device.Hostname,
                    Status = (int)DeploymentDeviceStatus.Queued,
                    DownloadStatus = (int)DeploymentDownloadStatus.Queued,
                    ExecuteStatus = (int)DeploymentExecuteStatus.Waiting,
                    LastHeartbeatUtc = device.LastSeenUtc,
                    AttemptCount = 0
                });

                _db.Commands.Add(BuildDeviceCommand(deployment, package, device, nowUtc));
            }

            deployment.Status = (int)DeploymentStatus.Active;
            deployment.StartedUtc = nowUtc;

            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return deployment;
        }

        public async Task<Deployment> UpdateDeploymentAsync(int deploymentId, CreateDeploymentRequest request, string? updatedBy, CancellationToken cancellationToken)
        {
            ValidateDeploymentRequest(request);

            var deployment = await _db.Deployments
                .Include(x => x.Package)
                .Include(x => x.DeploymentDevices)
                .FirstOrDefaultAsync(x => x.Id == deploymentId, cancellationToken)
                ?? throw new InvalidOperationException("Deployment not found.");

            if (deployment.Status == (int)DeploymentStatus.Completed || deployment.Status == (int)DeploymentStatus.Cancelled)
                throw new InvalidOperationException("Completed or cancelled deployments cannot be edited.");

            var normalizedTargetValue = request.TargetValue?.Trim() ?? string.Empty;
            if (deployment.TargetType != request.TargetType || !string.Equals(deployment.TargetValue, normalizedTargetValue, StringComparison.Ordinal))
                throw new InvalidOperationException("Target type and target value cannot be changed after deployment creation.");

            var package = await _db.Packages
                .FirstOrDefaultAsync(x => x.Id == request.PackageId && x.IsEnabled, cancellationToken)
                ?? throw new InvalidOperationException("Package not found or disabled.");

            deployment.PackageId = request.PackageId;
            deployment.ExecuteMode = request.ExecuteMode;
            deployment.WindowStartLocal = request.WindowStartLocal;
            deployment.WindowEndLocal = request.WindowEndLocal;
            deployment.UseStoreLocalTime = request.UseStoreLocalTime;
            deployment.AllowOutsideWindow = request.AllowOutsideWindow;
            deployment.RetryCount = request.RetryCount;
            deployment.Notes = request.Notes?.Trim();

            var payloadMatch = $"\"deploymentId\":{deploymentId}";
            var pendingCommands = await _db.Commands
                .Where(c => c.Type == "InstallPackage" &&
                            c.Status == "Pending" &&
                            c.PayloadJson != null &&
                            c.PayloadJson.Contains(payloadMatch))
                .ToListAsync(cancellationToken);

            foreach (var cmd in pendingCommands)
            {
                cmd.PayloadJson = BuildInstallPackagePayload(deployment, package);
                cmd.MaxAttempts = Math.Max(1, deployment.RetryCount + 1);
                cmd.LastError = "Deployment updated from dashboard";
                cmd.IssuedBy = updatedBy ?? cmd.IssuedBy;
            }

            await _db.SaveChangesAsync(cancellationToken);
            return deployment;
        }

        public async Task<DeploymentDetailResponse?> GetDeploymentAsync(int deploymentId, CancellationToken cancellationToken)
        {
            var deployment = await _db.Deployments
                .Include(x => x.Package)
                .Include(x => x.DeploymentDevices)
                .FirstOrDefaultAsync(x => x.Id == deploymentId, cancellationToken);

            if (deployment == null)
                return null;

            return new DeploymentDetailResponse
            {
                DeploymentId = deployment.Id,
                PackageName = deployment.Package?.Name ?? string.Empty,
                PackageVersion = deployment.Package?.Version,
                TargetType = deployment.TargetType,
                TargetValue = deployment.TargetValue,
                ExecuteMode = deployment.ExecuteMode,
                WindowStartLocal = deployment.WindowStartLocal,
                WindowEndLocal = deployment.WindowEndLocal,
                Status = deployment.Status,
                CreatedUtc = deployment.CreatedUtc,
                Devices = deployment.DeploymentDevices
                    .OrderBy(x => x.StoreNumber)
                    .ThenBy(x => x.Hostname)
                    .Select(x => new DeploymentDeviceItemResponse
                    {
                        DeviceId = x.DeviceId,
                        Hostname = x.Hostname,
                        StoreNumber = x.StoreNumber,
                        Status = x.Status,
                        DownloadStatus = x.DownloadStatus,
                        ExecuteStatus = x.ExecuteStatus,
                        ExitCode = x.ExitCode,
                        ResultMessage = x.ResultMessage,
                        LastHeartbeatUtc = x.LastHeartbeatUtc
                    })
                    .ToList()
            };
        }

        public async Task<int> RetryFailedDevicesAsync(int deploymentId, string? createdBy, CancellationToken cancellationToken)
        {
            var deployment = await _db.Deployments
                .Include(x => x.Package)
                .Include(x => x.DeploymentDevices)
                .FirstOrDefaultAsync(x => x.Id == deploymentId, cancellationToken)
                ?? throw new InvalidOperationException("Deployment not found.");

            if (deployment.Status == (int)DeploymentStatus.Cancelled)
                throw new InvalidOperationException("Cancelled deployments cannot be retried.");

            var package = deployment.Package ?? throw new InvalidOperationException("Deployment package not found.");
            var nowUtc = DateTime.UtcNow;

            var retryable = deployment.DeploymentDevices
                .Where(x => x.Status == (int)DeploymentDeviceStatus.FailedDownload ||
                            x.Status == (int)DeploymentDeviceStatus.FailedHash ||
                            x.Status == (int)DeploymentDeviceStatus.FailedExecution ||
                            x.Status == (int)DeploymentDeviceStatus.TimedOut)
                .ToList();

            foreach (var item in retryable)
            {
                item.Status = (int)DeploymentDeviceStatus.Queued;
                item.DownloadStatus = (int)DeploymentDownloadStatus.Queued;
                item.ExecuteStatus = (int)DeploymentExecuteStatus.Waiting;
                item.DownloadStartedUtc = null;
                item.DownloadCompletedUtc = null;
                item.ExecuteStartedUtc = null;
                item.ExecuteCompletedUtc = null;
                item.ExitCode = null;
                item.ResultMessage = null;
                item.FilePath = null;
                item.AttemptCount += 1;
                item.LastHeartbeatUtc ??= nowUtc;

                _db.Commands.Add(BuildDeviceCommand(
                    deployment,
                    package,
                    new DeviceTargetInfo
                    {
                        DeviceId = item.DeviceId,
                        StoreNumber = item.StoreNumber,
                        Hostname = item.Hostname,
                        LastSeenUtc = item.LastHeartbeatUtc
                    },
                    nowUtc,
                    createdBy));
            }

            deployment.Status = (int)DeploymentStatus.Active;
            deployment.CompletedUtc = null;
            deployment.StartedUtc ??= nowUtc;

            await _db.SaveChangesAsync(cancellationToken);
            return retryable.Count;
        }

        public async Task CancelDeploymentAsync(int deploymentId, CancellationToken cancellationToken)
        {
            var deployment = await _db.Deployments
                .Include(x => x.DeploymentDevices)
                .FirstOrDefaultAsync(x => x.Id == deploymentId, cancellationToken)
                ?? throw new InvalidOperationException("Deployment not found.");

            if (deployment.Status == (int)DeploymentStatus.Completed || deployment.Status == (int)DeploymentStatus.Cancelled)
                return;

            deployment.Status = (int)DeploymentStatus.Cancelled;
            deployment.CompletedUtc = DateTime.UtcNow;

            foreach (var item in deployment.DeploymentDevices.Where(x => !IsTerminalDeviceStatus(x.Status)))
            {
                item.Status = (int)DeploymentDeviceStatus.Cancelled;
                item.ResultMessage = string.IsNullOrWhiteSpace(item.ResultMessage)
                    ? "Cancelled from dashboard"
                    : item.ResultMessage;
                item.ExecuteStatus = item.ExecuteStatus == (int)DeploymentExecuteStatus.Completed
                    ? item.ExecuteStatus
                    : (int)DeploymentExecuteStatus.Failed;
                item.LastHeartbeatUtc ??= DateTime.UtcNow;
            }

            var payloadMatch = $"\"deploymentId\":{deploymentId}";
            var pendingCommands = await _db.Commands
                .Where(c => c.Type == "InstallPackage" &&
                            (c.Status == "Pending" || c.Status == "InProgress") &&
                            c.PayloadJson != null &&
                            c.PayloadJson.Contains(payloadMatch))
                .ToListAsync(cancellationToken);

            foreach (var cmd in pendingCommands)
            {
                cmd.Status = "Canceled";
                cmd.LastError = "Cancelled from dashboard";
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task DeleteDeploymentAsync(int deploymentId, CancellationToken cancellationToken)
        {
            var deployment = await _db.Deployments
                .Include(x => x.DeploymentDevices)
                .FirstOrDefaultAsync(x => x.Id == deploymentId, cancellationToken)
                ?? throw new InvalidOperationException("Deployment not found.");

            var payloadMatch = $"\"deploymentId\":{deploymentId}";
            var commands = await _db.Commands
                .Where(c => c.Type == "InstallPackage" &&
                            c.PayloadJson != null &&
                            c.PayloadJson.Contains(payloadMatch))
                .ToListAsync(cancellationToken);

            var commandIds = commands.Select(x => x.CommandId).ToList();
            if (commandIds.Count > 0)
            {
                var results = await _db.CommandResults
                    .Where(r => commandIds.Contains(r.CommandId))
                    .ToListAsync(cancellationToken);

                if (results.Count > 0)
                    _db.CommandResults.RemoveRange(results);

                _db.Commands.RemoveRange(commands);
            }

            if (deployment.DeploymentDevices.Count > 0)
                _db.DeploymentDevices.RemoveRange(deployment.DeploymentDevices);

            _db.Deployments.Remove(deployment);
            await _db.SaveChangesAsync(cancellationToken);
        }

        private async Task<List<DeviceTargetInfo>> ResolveTargetDevicesAsync(int targetType, string targetValue, CancellationToken cancellationToken)
        {
            targetValue = (targetValue ?? string.Empty).Trim();

            switch (targetType)
            {
                case (int)DeploymentTargetType.Device:
                    if (!Guid.TryParse(targetValue, out var deviceId))
                        throw new InvalidOperationException("TargetValue must be a valid DeviceId GUID for Device target type.");

                    return await _db.Devices
                        .Where(d => d.IsEnabled && d.DeviceId == deviceId)
                        .Select(d => new DeviceTargetInfo
                        {
                            DeviceId = d.DeviceId,
                            StoreNumber = d.StoreNumber,
                            Hostname = d.Hostname,
                            LastSeenUtc = d.LastSeenUtc
                        })
                        .ToListAsync(cancellationToken);

                case (int)DeploymentTargetType.Store:
                    if (string.IsNullOrWhiteSpace(targetValue))
                        throw new InvalidOperationException("TargetValue is required for Store target type.");

                    return await _db.Devices
                        .Where(d => d.IsEnabled && d.StoreNumber == targetValue)
                        .OrderBy(d => d.Hostname)
                        .Select(d => new DeviceTargetInfo
                        {
                            DeviceId = d.DeviceId,
                            StoreNumber = d.StoreNumber,
                            Hostname = d.Hostname,
                            LastSeenUtc = d.LastSeenUtc
                        })
                        .ToListAsync(cancellationToken);

                case (int)DeploymentTargetType.Group:
                    if (string.IsNullOrWhiteSpace(targetValue))
                        throw new InvalidOperationException("TargetValue is required for Group target type.");

                    return await _db.DeviceGroupMembers
                        .Where(m => _db.DeviceGroups.Any(g => g.DeviceGroupId == m.DeviceGroupId && g.GroupName == targetValue))
                        .Join(
                            _db.Devices.Where(d => d.IsEnabled),
                            m => m.DeviceId,
                            d => d.DeviceId,
                            (m, d) => new DeviceTargetInfo
                            {
                                DeviceId = d.DeviceId,
                                StoreNumber = d.StoreNumber,
                                Hostname = d.Hostname,
                                LastSeenUtc = d.LastSeenUtc
                            })
                        .OrderBy(d => d.StoreNumber)
                        .ThenBy(d => d.Hostname)
                        .ToListAsync(cancellationToken);

                case (int)DeploymentTargetType.Fleet:
                    return await _db.Devices
                        .Where(d => d.IsEnabled)
                        .OrderBy(d => d.StoreNumber)
                        .ThenBy(d => d.Hostname)
                        .Select(d => new DeviceTargetInfo
                        {
                            DeviceId = d.DeviceId,
                            StoreNumber = d.StoreNumber,
                            Hostname = d.Hostname,
                            LastSeenUtc = d.LastSeenUtc
                        })
                        .ToListAsync(cancellationToken);

                default:
                    throw new InvalidOperationException($"Unsupported target type '{targetType}'.");
            }
        }

        private Command BuildDeviceCommand(Deployment deployment, Package package, DeviceTargetInfo device, DateTime issuedUtc, string? issuedByOverride = null)
        {
            var payload = BuildInstallPackagePayload(deployment, package);

            return new Command
            {
                CommandId = Guid.NewGuid(),
                Type = "InstallPackage",
                Scope = "Device",
                PayloadJson = payload,
                Status = "Pending",
                Priority = 100,
                CreatedUtc = issuedUtc,
                ExpiresUtc = null,
                DeviceId = device.DeviceId,
                StoreNumber = device.StoreNumber,
                GroupName = deployment.TargetType == (int)DeploymentTargetType.Group ? deployment.TargetValue : null,
                AttemptCount = 0,
                MaxAttempts = Math.Max(1, deployment.RetryCount + 1),
                IssuedBy = issuedByOverride ?? deployment.CreatedBy ?? "Deployments",
                IssuedUtc = issuedUtc
            };
        }

        private string BuildInstallPackagePayload(Deployment deployment, Package package)
        {
            var (installCommand, installArguments) = ResolveInstallCommand(package);

            var payload = new
            {
                deploymentId = deployment.Id,
                packageId = package.Id,
                packageName = package.Name,
                fileName = package.FileName,
                downloadUrl = package.StoragePath,
                sha256 = package.Sha256,
                executeMode = ToExecuteModeString(deployment.ExecuteMode),
                windowStartLocal = deployment.WindowStartLocal?.ToString(@"hh\:mm\:ss"),
                windowEndLocal = deployment.WindowEndLocal?.ToString(@"hh\:mm\:ss"),
                useStoreLocalTime = deployment.UseStoreLocalTime,
                installCommand = installCommand,
                installArguments = installArguments,
                workingDirectory = string.IsNullOrWhiteSpace(package.WorkingDirectory)
                    ? $@"C:\RetailCentral\Agent\staging\{package.Id}"
                    : package.WorkingDirectory,
                timeoutSeconds = package.TimeoutSeconds,
                rebootBehavior = ToRebootBehaviorString(package.RebootBehavior)
            };

            return JsonSerializer.Serialize(payload);
        }

        private static (string InstallCommand, string InstallArguments) ResolveInstallCommand(Package package)
        {
            if (!string.IsNullOrWhiteSpace(package.ExecutionCommand))
                return (package.ExecutionCommand!, package.ExecutionArguments ?? string.Empty);

            return package.PackageType switch
            {
                (int)PackageType.Msi => ("msiexec.exe", $"/i {{file}} {package.ExecutionArguments}".Trim()),
                (int)PackageType.PowerShell => ("powershell.exe", $"-ExecutionPolicy Bypass -File {{file}} {package.ExecutionArguments}".Trim()),
                (int)PackageType.Cmd => ("cmd.exe", $"/c {{file}} {package.ExecutionArguments}".Trim()),
                (int)PackageType.Bat => ("cmd.exe", $"/c {{file}} {package.ExecutionArguments}".Trim()),
                (int)PackageType.Exe => ("cmd.exe", $"/c {{file}} {package.ExecutionArguments}".Trim()),
                _ => ("cmd.exe", $"/c {{file}} {package.ExecutionArguments}".Trim())
            };
        }

        private static bool IsTerminalDeviceStatus(int status)
        {
            return status == (int)DeploymentDeviceStatus.Completed ||
                   status == (int)DeploymentDeviceStatus.FailedDownload ||
                   status == (int)DeploymentDeviceStatus.FailedHash ||
                   status == (int)DeploymentDeviceStatus.FailedExecution ||
                   status == (int)DeploymentDeviceStatus.TimedOut ||
                   status == (int)DeploymentDeviceStatus.Cancelled;
        }

        private static string ToExecuteModeString(int executeMode)
        {
            return executeMode switch
            {
                (int)DeploymentExecuteMode.Immediate => "Immediate",
                (int)DeploymentExecuteMode.Windowed => "Windowed",
                (int)DeploymentExecuteMode.StagedOnly => "StagedOnly",
                _ => "Immediate"
            };
        }

        private static string ToRebootBehaviorString(int rebootBehavior)
        {
            return rebootBehavior switch
            {
                1 => "Allow",
                2 => "Require",
                _ => "None"
            };
        }

        private static void ValidatePackageRequest(CreatePackageRequest request)
        {
            if (request == null)
                throw new InvalidOperationException("Package request is required.");
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new InvalidOperationException("Package name is required.");
            if (string.IsNullOrWhiteSpace(request.FileName))
                throw new InvalidOperationException("File name is required.");
            if (string.IsNullOrWhiteSpace(request.StoragePath))
                throw new InvalidOperationException("Storage path is required.");
            if (string.IsNullOrWhiteSpace(request.Sha256))
                throw new InvalidOperationException("SHA256 is required.");
            if (request.TimeoutSeconds <= 0)
                throw new InvalidOperationException("Timeout must be greater than zero.");
        }

        private static void ValidateDeploymentRequest(CreateDeploymentRequest request)
        {
            if (request == null)
                throw new InvalidOperationException("Deployment request is required.");

            if (request.ExecuteMode == (int)DeploymentExecuteMode.Windowed &&
                (!request.WindowStartLocal.HasValue || !request.WindowEndLocal.HasValue))
            {
                throw new InvalidOperationException("Window start and end are required for windowed deployments.");
            }

            var targetValue = request.TargetValue?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(targetValue))
                throw new InvalidOperationException("Target value is required.");
        }

        private sealed class DeviceTargetInfo
        {
            public Guid DeviceId { get; set; }
            public string? StoreNumber { get; set; }
            public string? Hostname { get; set; }
            public DateTime? LastSeenUtc { get; set; }
        }
    }
}
