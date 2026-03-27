using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data;
using RetailCentral.Api.Models;
using RetailCentral.Api.Models.Deployments;
using RetailCentral.Api.Services;
using RetailCentral.Api.Services.Deployments;
using RetailCentral.Api.ViewModels;
using RetailCentral.Api.ViewModels.Deployments;
using System.Security.Cryptography;
using System.Text;

namespace RetailCentral.Api.Controllers
{
    [Authorize(Policy = "DashboardViewer")]
    public class DashboardController : Controller
    {
        private readonly RetailCentralDbContext _db;
        private readonly IDeploymentService _deploymentService;
        private readonly IWebHostEnvironment _environment;
        private readonly AuditService _auditService;
        private readonly IConfiguration _config;

        public DashboardController(
            RetailCentralDbContext db,
            IDeploymentService deploymentService,
            IWebHostEnvironment environment,
            AuditService auditService,
            IConfiguration config)
        {
            _db = db;
            _deploymentService = deploymentService;
            _environment = environment;
            _auditService = auditService;
            _config = config;
        }

        private static DateTime OnlineCutoffUtc => DateTime.UtcNow.AddMinutes(-5);

        private string CurrentActor()
        {
            return User?.Identity?.Name ?? "Dashboard";
        }

        private async Task AuditAsync(
            string action,
            string? targetType = null,
            string? targetId = null,
            object? details = null,
            bool success = true,
            string? errorMessage = null,
            CancellationToken cancellationToken = default)
        {
            await _auditService.LogAsync(
                action: action,
                targetType: targetType,
                targetId: targetId,
                details: details,
                success: success,
                errorMessage: errorMessage,
                cancellationToken: cancellationToken);
        }

        private static List<string> GetAvailableCommandTypes()
        {
            return new List<string>
    {
        "Echo",
        "CollectSystemInfo",
        "CollectProcessStatus",
        "CollectSoftwareInventory",
        "RunProcess",
        "DownloadFile"
    };
        }

        private static List<CommandTemplateOptionViewModel> GetCommandTemplates()
        {
            return new List<CommandTemplateOptionViewModel>
            {
                new CommandTemplateOptionViewModel
                {
                    Name = "Echo - Hello",
                    CommandType = "Echo",
                    PayloadJson = "{\n  \"message\": \"Hello from dashboard\"\n}",
                    Description = "Simple test message"
                },
                new CommandTemplateOptionViewModel
                {
                    Name = "Collect System Info",
                    CommandType = "CollectSystemInfo",
                    PayloadJson = "{}",
                    Description = "Collect machine details"
                },
                new CommandTemplateOptionViewModel
                {
                    Name = "RunProcess - cmd echo hi",
                    CommandType = "RunProcess",
                    PayloadJson = "{\n  \"fileName\": \"cmd.exe\",\n  \"arguments\": \"/c echo hi\",\n  \"timeoutSeconds\": 10\n}",
                    Description = "Basic command test"
                },
                new CommandTemplateOptionViewModel
                {
                    Name = "RunProcess - ipconfig",
                    CommandType = "RunProcess",
                    PayloadJson = "{\n  \"fileName\": \"cmd.exe\",\n  \"arguments\": \"/c ipconfig\",\n  \"timeoutSeconds\": 20\n}",
                    Description = "Network info"
                },
                new CommandTemplateOptionViewModel
                {
                    Name = "DownloadFile - download only",
                    CommandType = "DownloadFile",
                    PayloadJson = "{\n  \"url\": \"http://localhost:8088/hello.txt\",\n  \"destinationFileName\": \"hello.txt\",\n  \"execute\": false\n}",
                    Description = "Download a file without executing"
                },
                new CommandTemplateOptionViewModel
                {
                    Name = "DownloadFile - download and execute",
                    CommandType = "DownloadFile",
                    PayloadJson = "{\n  \"url\": \"http://localhost:8088/runme.cmd\",\n  \"destinationFileName\": \"runme.cmd\",\n  \"execute\": true,\n  \"arguments\": \"\"\n}",
                    Description = "Download and execute a command file"
                }
            };
        }

        private static List<HelpdeskCommandOptionViewModel> GetHelpdeskCommands()
        {
            return new List<HelpdeskCommandOptionViewModel>
            {
                new HelpdeskCommandOptionViewModel
                {
                    Key = "CollectSystemInfo",
                    Name = "Collect System Info",
                    Description = "Collect the latest hardware, OS, memory, disk, network, and inventory details.",
                    Category = "Diagnostics",
                    DeviceSupported = true,
                    StoreSupported = true,
                    GroupSupported = true,
                    Destructive = false
                },
                new HelpdeskCommandOptionViewModel
                {
                    Key = "NetworkInfo",
                    Name = "Network Info",
                    Description = "Runs ipconfig /all and returns detailed network information.",
                    Category = "Diagnostics",
                    DeviceSupported = true,
                    StoreSupported = true,
                    GroupSupported = true,
                    Destructive = false
                },
                new HelpdeskCommandOptionViewModel
                {
                    Key = "RestartPOS",
                    Name = "Restart POS",
                    Description = "Placeholder command that calls POSRestart.cmd on the device.",
                    Category = "Application Recovery",
                    DeviceSupported = true,
                    StoreSupported = true,
                    GroupSupported = true,
                    Destructive = true
                },
                new HelpdeskCommandOptionViewModel
                {
                    Key = "RestartRetailShell",
                    Name = "Restart RetailShell",
                    Description = "Placeholder command that calls RetailShellRestart.cmd on the device.",
                    Category = "Application Recovery",
                    DeviceSupported = true,
                    StoreSupported = true,
                    GroupSupported = true,
                    Destructive = true
                },
                new HelpdeskCommandOptionViewModel
                {
                    Key = "RestartAgent",
                    Name = "Restart Agent",
                    Description = "Placeholder command that calls AgentRestart.cmd on the device.",
                    Category = "Application Recovery",
                    DeviceSupported = true,
                    StoreSupported = true,
                    GroupSupported = true,
                    Destructive = true
                },
                new HelpdeskCommandOptionViewModel
                {
                    Key = "RebootDevice",
                    Name = "Reboot Device",
                    Description = "Performs a forced reboot after a short delay.",
                    Category = "Device Recovery",
                    DeviceSupported = true,
                    StoreSupported = true,
                    GroupSupported = true,
                    Destructive = true
                },
                new HelpdeskCommandOptionViewModel
                {
                    Key = "RequeueFailed",
                    Name = "Requeue Failed Commands",
                    Description = "Requeues recent failed commands for a single device only.",
                    Category = "Recovery",
                    DeviceSupported = true,
                    StoreSupported = false,
                    GroupSupported = false,
                    Destructive = false
                }
            };
        }

        public async Task<IActionResult> Index()
        {
            var now = DateTime.UtcNow;
            var cutoff = now.AddMinutes(-5);
            var since24 = now.AddHours(-24);

            var devices = await _db.Devices
                .Where(d => d.IsEnabled)
                .OrderByDescending(d => d.LastSeenUtc)
                .Take(10)
                .Select(d => new DeviceSummaryViewModel
                {
                    DeviceId = d.DeviceId,
                    StoreNumber = d.StoreNumber,
                    Hostname = d.Hostname,
                    AgentVersion = d.AgentVersion,
                    LastSeenUtc = d.LastSeenUtc,
                    IsOnline = d.LastSeenUtc >= cutoff
                })
                .ToListAsync();

            var commands = await _db.Commands
                .OrderByDescending(c => c.CreatedUtc)
                .Take(10)
                .Select(c => new CommandSummaryViewModel
                {
                    CommandId = c.CommandId,
                    Type = c.Type,
                    Scope = c.Scope,
                    StoreNumber = c.StoreNumber,
                    GroupName = c.GroupName,
                    DeviceId = c.DeviceId,
                    Status = c.Status,
                    CreatedUtc = c.CreatedUtc
                })
                .ToListAsync();

            var allDevices = await _db.Devices
                .Where(d => d.IsEnabled)
                .ToListAsync();

            var inventoryRows = await _db.RegisterInventories.ToListAsync();

            var failedCommandDeviceIds = await _db.CommandResults
                .Where(r => r.Status == "Failed" && r.FinishedUtc >= since24)
                .Select(r => r.DeviceId)
                .Distinct()
                .ToListAsync();

            var failedSet = failedCommandDeviceIds.ToHashSet();

            var inventoryByDevice = inventoryRows
                .GroupBy(r => r.DeviceId)
                .ToDictionary(g => g.Key, g => g.First());

            var deviceHealth = allDevices
                .OrderBy(d => d.StoreNumber)
                .ThenBy(d => d.Hostname)
                .Select(d =>
                {
                    inventoryByDevice.TryGetValue(d.DeviceId, out var inv);
                    return BuildDeviceHealth(d, inv, failedSet, cutoff, now);
                })
                .OrderBy(x => x.Score)
                .ThenBy(x => x.StoreNumber)
                .ThenBy(x => x.Hostname)
                .ToList();

            var healthyDevices = deviceHealth.Count(x => x.Status == "Healthy");
            var warningDevices = deviceHealth.Count(x => x.Status == "Warning");
            var criticalDevices = deviceHealth.Count(x => x.Status == "Critical");

            var totalDevices = allDevices.Count;
            var onlineDevices = allDevices.Count(d => d.LastSeenUtc >= cutoff);
            var offlineDevices = totalDevices - onlineDevices;

            var versionSummary = allDevices
                .GroupBy(d => d.AgentVersion ?? "Unknown")
                .Select(g => new VersionSummaryViewModel
                {
                    AgentVersion = g.Key,
                    DeviceCount = g.Count(),
                    OnlineCount = g.Count(d => d.LastSeenUtc >= cutoff),
                    OfflineCount = g.Count(d => d.LastSeenUtc < cutoff)
                })
                .OrderByDescending(x => x.DeviceCount)
                .ToList();

            var storeSummary = allDevices
                .GroupBy(d => d.StoreNumber ?? "Unknown")
                .Select(g => new StoreSummaryTileViewModel
                {
                    StoreNumber = g.Key,
                    TotalDevices = g.Count(),
                    OnlineDevices = g.Count(d => d.LastSeenUtc >= cutoff),
                    OfflineDevices = g.Count(d => d.LastSeenUtc < cutoff)
                })
                .OrderBy(x => x.StoreNumber)
                .Take(20)
                .ToList();

            var storeOutages = storeSummary
                .Where(s => s.TotalDevices > 0)
                .Select(s => new StoreOutageTileViewModel
                {
                    StoreNumber = s.StoreNumber,
                    TotalDevices = s.TotalDevices,
                    OfflineDevices = s.OfflineDevices,
                    IsOutage = s.OfflineDevices == s.TotalDevices
                })
                .Where(x => x.IsOutage || x.OfflineDevices > 0)
                .OrderByDescending(x => x.IsOutage)
                .ThenByDescending(x => x.OfflineDevices)
                .Take(20)
                .ToList();

            var heatmap = allDevices
                 .OrderBy(d => d.StoreNumber)
                 .ThenBy(d => d.Hostname)
                 .Take(60)
                 .Select(d => new DeviceHeatmapTileViewModel
                 {
                     DeviceId = d.DeviceId, // <-- ADD THIS LINE
                     StoreNumber = d.StoreNumber ?? "",
                     Hostname = d.Hostname ?? "",
                     IsOnline = d.LastSeenUtc >= cutoff,
                     LastSeenUtc = d.LastSeenUtc,
                     Severity = GetHeatSeverity(d.LastSeenUtc, cutoff)
                 })
                 .ToList();

            var pendingCount = await _db.Commands.CountAsync(c => c.Status == "Pending");
            var inProgressCount = await _db.Commands.CountAsync(c => c.Status == "InProgress");
            var failed24Count = await _db.Commands.CountAsync(c => c.Status == "Failed" && c.CreatedUtc >= since24);
            var succeeded24Count = await _db.Commands.CountAsync(c => c.Status == "Succeeded" && c.CreatedUtc >= since24);

            var progressTotal = Math.Max(1, pendingCount + inProgressCount + failed24Count + succeeded24Count);

            var commandProgress = new List<CommandProgressViewModel>
            {
                new CommandProgressViewModel
                {
                    Label = "Pending",
                    Count = pendingCount,
                    Total = progressTotal,
                    Percent = Percentage(pendingCount, progressTotal),
                    CssClass = "pending"
                },
                new CommandProgressViewModel
                {
                    Label = "In Progress",
                    Count = inProgressCount,
                    Total = progressTotal,
                    Percent = Percentage(inProgressCount, progressTotal),
                    CssClass = "inprogress"
                },
                new CommandProgressViewModel
                {
                    Label = "Succeeded (24h)",
                    Count = succeeded24Count,
                    Total = progressTotal,
                    Percent = Percentage(succeeded24Count, progressTotal),
                    CssClass = "success"
                },
                new CommandProgressViewModel
                {
                    Label = "Failed (24h)",
                    Count = failed24Count,
                    Total = progressTotal,
                    Percent = Percentage(failed24Count, progressTotal),
                    CssClass = "failed"
                }
            };

            var model = new DashboardIndexViewModel
            {
                TotalDevices = totalDevices,
                OnlineDevices = onlineDevices,
                OfflineDevices = offlineDevices,
                CommandsPending = pendingCount,
                CommandsInProgress = inProgressCount,
                CommandsFailedLast24Hours = failed24Count,
                CommandsSucceededLast24Hours = succeeded24Count,
                RecentDevices = devices,
                RecentCommands = commands,
                VersionSummary = versionSummary,
                StoreSummary = storeSummary,
                StoreOutages = storeOutages,
                DeviceHeatmap = heatmap,
                CommandProgress = commandProgress,
                DeviceHealth = deviceHealth,
                HealthyDevices = healthyDevices,
                WarningDevices = warningDevices,
                CriticalDevices = criticalDevices
            };

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> DeviceActivityData(Guid id)
        {
            var userActivity = await _db.UserActivityInventories
                .Where(x => x.DeviceId == id)
                .OrderByDescending(x => x.UpdatedUtc)
                .FirstOrDefaultAsync();

            if (userActivity == null)
                return Json(null);

            return Json(new
            {
                sessionState = userActivity.SessionState,
                consoleUser = userActivity.ConsoleUserName,
                idleSeconds = userActivity.IdleSeconds,
                idleDisplay = userActivity.IdleSeconds.HasValue
                    ? TimeSpan.FromSeconds(userActivity.IdleSeconds.Value).ToString(@"hh\:mm\:ss")
                    : "Unknown",
                isUserActive = userActivity.IsUserActive,
                isPosForeground = userActivity.IsPosForeground,
                lastInput = userActivity.LastInputUtc,
                updatedUtc = userActivity.UpdatedUtc
            });
        }

        [Authorize(Policy = "DashboardViewer")]
        [HttpGet]
        public IActionResult About()
        {
            var model = _config.GetSection("AboutPage").Get<AboutPageConfig>() ?? new AboutPageConfig();

            ViewData["Title"] = "About";
            return View(model);
        }

        [Authorize(Policy = "DashboardAdmin")]
        [HttpGet]
        public async Task<IActionResult> LogsDashboard(CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var d30 = now.AddDays(-30);
            var d60 = now.AddDays(-60);
            var d90 = now.AddDays(-90);

            var reactivatedAuditRows = await _db.AuditLogs
                .Where(a => a.Action == "DeviceAutoReactivated")
                .OrderByDescending(a => a.TimestampUtc)
                .Take(200)
                .Select(a => new
                {
                    a.TimestampUtc,
                    a.TargetId,
                    a.Details
                })
                .ToListAsync(ct);
            var reactivated = reactivatedAuditRows
                .Select(a =>
                {
                    string? hostname = null;
                    string? storeNumber = null;
                    string? remoteIp = null;
                    string? reason = null;

                    if (!string.IsNullOrWhiteSpace(a.Details))
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(a.Details);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("Hostname", out var h))
                            hostname = h.GetString();

                        if (root.TryGetProperty("StoreNumber", out var s))
                            storeNumber = s.GetString();

                        if (root.TryGetProperty("remoteIp", out var ip))
                            remoteIp = ip.GetString();

                        if (root.TryGetProperty("Reason", out var r))
                            reason = r.GetString();
                    }

                    return new ReactivatedDeviceLogViewModel
                    {
                        TimestampUtc = a.TimestampUtc,
                        DeviceId = a.TargetId,
                        Hostname = hostname,
                        StoreNumber = storeNumber,
                        RemoteIp = remoteIp,
                        Reason = reason
                    };
                })
                .ToList();

            var inactive30 = await _db.Devices.CountAsync(d => d.IsEnabled && d.LastSeenUtc < d30, ct);
            var inactive60 = await _db.Devices.CountAsync(d => d.IsEnabled && d.LastSeenUtc < d60, ct);
            var inactive90 = await _db.Devices.CountAsync(d => d.IsEnabled && d.LastSeenUtc < d90, ct);

            var new30 = await _db.Devices.CountAsync(d => d.FirstSeenUtc >= d30, ct);
            var new60 = await _db.Devices.CountAsync(d => d.FirstSeenUtc >= d60, ct);
            var new90 = await _db.Devices.CountAsync(d => d.FirstSeenUtc >= d90, ct);

            var model = new LogsDashboardViewModel
            {
                ReactivatedDevices = reactivated,
                Inactive30Days = inactive30,
                Inactive60Days = inactive60,
                Inactive90Days = inactive90,
                New30Days = new30,
                New60Days = new60,
                New90Days = new90
            };

            return View(model);
        }

        [Authorize(Policy = "DashboardAdmin")]
        [HttpGet]
        public async Task<IActionResult> Audit(AuditViewModel model, CancellationToken ct)
        {
            var query = _db.AuditLogs.AsQueryable();

            if (!string.IsNullOrWhiteSpace(model.UserName))
                query = query.Where(x => x.UserName.Contains(model.UserName));

            if (!string.IsNullOrWhiteSpace(model.ActionName))
                query = query.Where(x => x.Action.Contains(model.ActionName));

            if (!string.IsNullOrWhiteSpace(model.TargetType))
                query = query.Where(x => x.TargetType == model.TargetType);

            if (!string.IsNullOrWhiteSpace(model.TargetId))
                query = query.Where(x => x.TargetId == model.TargetId);

            if (model.FromUtc.HasValue)
                query = query.Where(x => x.TimestampUtc >= model.FromUtc.Value);

            if (model.ToUtc.HasValue)
                query = query.Where(x => x.TimestampUtc <= model.ToUtc.Value);

            if (model.Success.HasValue)
                query = query.Where(x => x.Success == model.Success.Value);

            model.Results = await query
                .OrderByDescending(x => x.TimestampUtc)
                .Take(500) // safety cap
                .ToListAsync(ct);

            return View(model);
        }

        [Authorize(Policy = "DashboardAdmin")]
        [HttpGet]
        public async Task<IActionResult> ExportAudit(AuditViewModel model, CancellationToken ct)
        {
            var query = _db.AuditLogs.AsQueryable();

            // same filters as above
            if (!string.IsNullOrWhiteSpace(model.UserName))
                query = query.Where(x => x.UserName.Contains(model.UserName));

            if (!string.IsNullOrWhiteSpace(model.ActionName))
                query = query.Where(x => x.Action.Contains(model.ActionName));

            if (!string.IsNullOrWhiteSpace(model.TargetType))
                query = query.Where(x => x.TargetType == model.TargetType);

            if (!string.IsNullOrWhiteSpace(model.TargetId))
                query = query.Where(x => x.TargetId == model.TargetId);

            if (model.FromUtc.HasValue)
                query = query.Where(x => x.TimestampUtc >= model.FromUtc.Value);

            if (model.ToUtc.HasValue)
                query = query.Where(x => x.TimestampUtc <= model.ToUtc.Value);

            if (model.Success.HasValue)
                query = query.Where(x => x.Success == model.Success.Value);

            var data = await query
                .OrderByDescending(x => x.TimestampUtc)
                .Take(5000)
                .ToListAsync(ct);

            var lines = new List<string>
    {
        "TimestampUtc,UserName,Action,TargetType,TargetId,Success,IpAddress,ErrorMessage"
    };

            foreach (var x in data)
            {
                lines.Add($"\"{x.TimestampUtc:O}\",\"{x.UserName}\",\"{x.Action}\",\"{x.TargetType}\",\"{x.TargetId}\",\"{x.Success}\",\"{x.IpAddress}\",\"{x.ErrorMessage}\"");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines));

            return File(bytes, "text/csv", $"audit_{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpGet]
        public async Task<IActionResult> Packages(CancellationToken cancellationToken)
        {
            var vm = new PackageListViewModel
            {
                Packages = await _deploymentService.GetPackagesAsync(cancellationToken),
                Message = TempData["PackageMessage"]?.ToString()
            };

            return View(vm);
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpGet]
        public IActionResult CreatePackage()
        {
            var vm = new CreatePackageViewModel
            {
                TimeoutSeconds = 1800,
                RebootBehavior = 0,
                PackageType = 1,
                StoreInDistro = true
            };

            PopulateCreatePackageLists(vm);
            SetPackageFormViewData("Create", nameof(CreatePackage), "Create Package", "Create Package");
            return View(vm);
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpGet]
        public async Task<IActionResult> EditPackage(int id, CancellationToken cancellationToken)
        {
            var pkg = await _deploymentService.GetPackageByIdAsync(id, cancellationToken);
            if (pkg == null)
                return NotFound();

            var vm = new CreatePackageViewModel
            {
                Name = pkg.Name,
                Version = pkg.Version,
                Description = pkg.Description,
                PackageType = pkg.PackageType,
                FileName = pkg.FileName,
                StoragePath = pkg.StoragePath,
                Sha256 = pkg.Sha256,
                FileSizeBytes = pkg.FileSizeBytes ?? 0,
                ExecutionCommand = pkg.ExecutionCommand,
                ExecutionArguments = pkg.ExecutionArguments,
                WorkingDirectory = pkg.WorkingDirectory,
                TimeoutSeconds = pkg.TimeoutSeconds,
                RebootBehavior = pkg.RebootBehavior,
                StoreInDistro = IsDistroPath(pkg.StoragePath)
            };

            PopulateCreatePackageLists(vm);
            SetPackageFormViewData("Edit", nameof(EditPackage), "Save Package Changes", $"Edit Package #{pkg.Id}");
            ViewData["PackageId"] = pkg.Id;
            ViewData["PackageEnabled"] = pkg.IsEnabled;
            return View("CreatePackage", vm);
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreatePackage(CreatePackageViewModel model, CancellationToken cancellationToken)
        {
            return await SavePackageInternalAsync(model, null, cancellationToken);
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPackage(int id, CreatePackageViewModel model, CancellationToken cancellationToken)
        {
            return await SavePackageInternalAsync(model, id, cancellationToken);
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DisablePackage(int id, CancellationToken cancellationToken)
        {
            try
            {
                await _deploymentService.DisablePackageAsync(id, cancellationToken);
                await AuditAsync("DisablePackage", "Package", id.ToString(), new { PackageId = id }, true, cancellationToken: cancellationToken);
                TempData["PackageMessage"] = $"Package {id} disabled.";
            }
            catch (Exception ex)
            {
                await AuditAsync("DisablePackage", "Package", id.ToString(), new { PackageId = id }, false, ex.Message, cancellationToken);
                TempData["PackageMessage"] = $"Disable failed: {ex.Message}";
            }

            return RedirectToAction(nameof(Packages));
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePackage(int id, CancellationToken cancellationToken)
        {
            try
            {
                await _deploymentService.DeletePackageAsync(id, cancellationToken);
                await AuditAsync("DeletePackage", "Package", id.ToString(), new { PackageId = id }, true, cancellationToken: cancellationToken);
                TempData["PackageMessage"] = $"Package {id} deleted.";
            }
            catch (Exception ex)
            {
                await AuditAsync("DeletePackage", "Package", id.ToString(), new { PackageId = id }, false, ex.Message, cancellationToken);
                TempData["PackageMessage"] = $"Delete failed: {ex.Message}";
            }

            return RedirectToAction(nameof(Packages));
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpGet]
        public async Task<IActionResult> Deployments(CancellationToken cancellationToken)
        {
            ViewData["DeploymentMessage"] = TempData["DeploymentMessage"]?.ToString();

            var vm = new DeploymentIndexViewModel
            {
                Packages = await _deploymentService.GetPackagesAsync(cancellationToken),
                Deployments = await _deploymentService.GetDeploymentsAsync(cancellationToken)
            };

            return View(vm);
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpGet]
        public async Task<IActionResult> DeploymentDetail(int id, CancellationToken cancellationToken)
        {
            var vm = await _deploymentService.GetDeploymentAsync(id, cancellationToken);
            if (vm == null)
            {
                return NotFound();
            }

            ViewData["DeploymentMessage"] = TempData["DeploymentMessage"]?.ToString();
            return View(vm);
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpGet]
        public async Task<IActionResult> EditDeployment(int id, CancellationToken cancellationToken)
        {
            var deployment = await _deploymentService.GetDeploymentByIdAsync(id, cancellationToken);
            if (deployment == null)
                return NotFound();

            var vm = new CreateDeploymentViewModel
            {
                PackageId = deployment.PackageId,
                TargetType = deployment.TargetType,
                TargetValue = deployment.TargetValue,
                ExecuteMode = deployment.ExecuteMode,
                WindowStartLocal = deployment.WindowStartLocal,
                WindowEndLocal = deployment.WindowEndLocal,
                UseStoreLocalTime = deployment.UseStoreLocalTime,
                AllowOutsideWindow = deployment.AllowOutsideWindow,
                RetryCount = deployment.RetryCount,
                Notes = deployment.Notes
            };

            await PopulateCreateDeploymentLists(vm, cancellationToken);
            SetDeploymentFormViewData("Edit", nameof(EditDeployment), "Save Deployment Changes", $"Edit Deployment #{deployment.Id}");
            ViewData["DeploymentId"] = deployment.Id;
            return View("CreateDeployment", vm);
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RetryFailedDeploymentDevices(int id, CancellationToken cancellationToken)
        {
            try
            {
                var retried = await _deploymentService.RetryFailedDevicesAsync(
                    id,
                    CurrentActor(),
                    cancellationToken);

                await AuditAsync("RetryFailedDeploymentDevices", "Deployment", id.ToString(), new { DeploymentId = id, RetriedCount = retried }, true, cancellationToken: cancellationToken);

                TempData["DeploymentMessage"] = retried > 0
                    ? $"Requeued {retried} failed deployment device(s)."
                    : "No failed deployment devices were eligible for retry.";
            }
            catch (Exception ex)
            {
                await AuditAsync("RetryFailedDeploymentDevices", "Deployment", id.ToString(), new { DeploymentId = id }, false, ex.Message, cancellationToken);
                TempData["DeploymentMessage"] = $"Retry failed: {ex.Message}";
            }

            return RedirectToAction(nameof(DeploymentDetail), new { id });
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelDeployment(int id, CancellationToken cancellationToken)
        {
            try
            {
                await _deploymentService.CancelDeploymentAsync(id, cancellationToken);
                await AuditAsync("CancelDeployment", "Deployment", id.ToString(), new { DeploymentId = id }, true, cancellationToken: cancellationToken);
                TempData["DeploymentMessage"] = $"Deployment {id} cancelled.";
            }
            catch (Exception ex)
            {
                await AuditAsync("CancelDeployment", "Deployment", id.ToString(), new { DeploymentId = id }, false, ex.Message, cancellationToken);
                TempData["DeploymentMessage"] = $"Cancel failed: {ex.Message}";
            }

            return RedirectToAction(nameof(DeploymentDetail), new { id });
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDeployment(int id, CancellationToken cancellationToken)
        {
            try
            {
                await _deploymentService.DeleteDeploymentAsync(id, cancellationToken);
                await AuditAsync("DeleteDeployment", "Deployment", id.ToString(), new { DeploymentId = id }, true, cancellationToken: cancellationToken);
                TempData["DeploymentMessage"] = $"Deployment {id} deleted.";
            }
            catch (Exception ex)
            {
                await AuditAsync("DeleteDeployment", "Deployment", id.ToString(), new { DeploymentId = id }, false, ex.Message, cancellationToken);
                TempData["DeploymentMessage"] = $"Delete failed: {ex.Message}";
            }

            return RedirectToAction(nameof(Deployments));
        }

        private static int Percentage(int count, int total)
        {
            if (total <= 0) return 0;
            return (int)Math.Round((double)count * 100 / total);
        }

        private static string GetHeatSeverity(DateTime lastSeenUtc, DateTime onlineCutoffUtc)
        {
            if (lastSeenUtc >= onlineCutoffUtc) return "low";
            if (lastSeenUtc >= onlineCutoffUtc.AddMinutes(-15)) return "med";
            return "high";
        }
        private static void ApplyFriendlyOsNames(List<RegisterInventoryRowViewModel> rows)
        {
            foreach (var row in rows)
            {
                row.OSVersion = ToFriendlyOsName(row.OSVersion);
            }
        }

        private static string ToFriendlyOsName(string? osVersion)
        {
            if (string.IsNullOrWhiteSpace(osVersion))
                return "";

            var s = osVersion.Trim();
            var friendly = s;

            if (s.Contains("10.0.26200") || s.Contains("10.0.26100"))
                friendly = "Windows 11 24H2";
            else if (s.Contains("10.0.22631"))
                friendly = "Windows 11 23H2";
            else if (s.Contains("10.0.22621"))
                friendly = "Windows 11 22H2";
            else if (s.Contains("10.0.22000"))
                friendly = "Windows 11 21H2";
            else if (s.Contains("10.0.19045"))
                friendly = "Windows 10 22H2";
            else if (s.Contains("10.0.19044"))
                friendly = "Windows 10 21H2";
            else if (s.Contains("10.0.19043"))
                friendly = "Windows 10 21H1";
            else if (s.Contains("10.0.19042"))
                friendly = "Windows 10 20H2";
            else if (s.Contains("10.0.19041"))
                friendly = "Windows 10 2004";

            return friendly == s ? s : $"{friendly} ({s})";
        }

        private static DeviceHealthViewModel BuildDeviceHealth(
            Device device,
            RegisterInventory? inv,
            HashSet<Guid> failedSet,
            DateTime onlineCutoffUtc,
            DateTime nowUtc)
        {
            var score = 0;

            var heartbeatHealthy = device.LastSeenUtc >= onlineCutoffUtc;
            if (heartbeatHealthy) score += 40;

            var inventoryFresh = inv != null && inv.UpdatedUtc >= nowUtc.AddDays(-1);
            if (inventoryFresh) score += 10;

            var diskHealthy = IsDiskHealthy(inv?.HardDriveFreeSpace, inv?.HardDriveSize);
            if (diskHealthy) score += 25;

            var memoryHealthy = IsMemoryHealthy(inv?.Memory);
            if (memoryHealthy) score += 15;

            var failuresHealthy = !failedSet.Contains(device.DeviceId);
            if (failuresHealthy) score += 10;

            var status = score switch
            {
                >= 90 => "Healthy",
                >= 70 => "Warning",
                _ => "Critical"
            };

            return new DeviceHealthViewModel
            {
                DeviceId = device.DeviceId,
                StoreNumber = device.StoreNumber ?? "",
                Hostname = device.Hostname ?? "",
                Score = score,
                Status = status,
                HeartbeatHealthy = heartbeatHealthy,
                DiskHealthy = diskHealthy,
                MemoryHealthy = memoryHealthy,
                FailuresHealthy = failuresHealthy,
                InventoryFresh = inventoryFresh,
                HardDriveFreeSpace = inv?.HardDriveFreeSpace,
                HardDriveSize = inv?.HardDriveSize,
                Memory = inv?.Memory,
                LastHeartbeatUtc = inv?.LastHeartbeatUtc ?? device.LastSeenUtc,
                InventoryUpdatedUtc = inv?.UpdatedUtc
            };
        }

        private static bool IsMemoryHealthy(string? memoryText)
        {
            if (string.IsNullOrWhiteSpace(memoryText))
                return false;

            if (!TryParseLeadingNumber(memoryText, out var gb))
                return false;

            return gb >= 4;
        }

        private static bool IsDiskHealthy(string? freeText, string? totalText)
        {
            if (string.IsNullOrWhiteSpace(freeText) || string.IsNullOrWhiteSpace(totalText))
                return false;

            if (!TryParseLeadingNumber(freeText, out var freeGb))
                return false;

            if (!TryParseLeadingNumber(totalText, out var totalGb))
                return false;

            if (totalGb <= 0)
                return false;

            var pctFree = freeGb / totalGb * 100m;
            return pctFree >= 15m;
        }

        private static bool TryParseLeadingNumber(string text, out decimal value)
        {
            value = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            var numeric = new string(text
                .TakeWhile(c => char.IsDigit(c) || c == '.' || c == '-')
                .ToArray());

            return decimal.TryParse(numeric, out value);
        }

        public async Task<IActionResult> Devices(string? search, string? storeNumber, string? groupName, string? status = "Active")
        {
            var cutoff = OnlineCutoffUtc;

            var normalizedStatus = string.IsNullOrWhiteSpace(status)
                ? "Active"
                : status.Trim();

            var baseQuery = _db.Devices.AsQueryable();

            // Primary status filtering
            if (string.Equals(normalizedStatus, "Retired", StringComparison.OrdinalIgnoreCase))
            {
                baseQuery = baseQuery.Where(d => !d.IsEnabled);
            }
            else if (string.Equals(normalizedStatus, "Online", StringComparison.OrdinalIgnoreCase))
            {
                baseQuery = baseQuery.Where(d => d.IsEnabled && d.LastSeenUtc >= cutoff);
            }
            else if (string.Equals(normalizedStatus, "Offline", StringComparison.OrdinalIgnoreCase))
            {
                baseQuery = baseQuery.Where(d => d.IsEnabled && d.LastSeenUtc < cutoff);
            }
            else
            {
                // Default = active devices only
                normalizedStatus = "Active";
                baseQuery = baseQuery.Where(d => d.IsEnabled);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                baseQuery = baseQuery.Where(d =>
                    d.Hostname.Contains(s) ||
                    d.StoreNumber.Contains(s) ||
                    (d.AgentVersion != null && d.AgentVersion.Contains(s)) ||
                    (d.MachineName != null && d.MachineName.Contains(s)));
            }

            if (!string.IsNullOrWhiteSpace(storeNumber))
            {
                baseQuery = baseQuery.Where(d => d.StoreNumber == storeNumber);
            }

            if (!string.IsNullOrWhiteSpace(groupName))
            {
                baseQuery = baseQuery.Where(d =>
                    _db.DeviceGroupMembers.Any(m =>
                        m.DeviceId == d.DeviceId &&
                        _db.DeviceGroups.Any(g => g.DeviceGroupId == m.DeviceGroupId && g.GroupName == groupName)));
            }

            var devices = await baseQuery
                .OrderBy(d => d.StoreNumber)
                .ThenBy(d => d.Hostname)
                .Select(d => new DeviceSummaryViewModel
                {
                    DeviceId = d.DeviceId,
                    StoreNumber = d.StoreNumber,
                    Hostname = d.Hostname,
                    AgentVersion = d.AgentVersion,
                    LastSeenUtc = d.LastSeenUtc,
                    IsOnline = d.IsEnabled && d.LastSeenUtc >= cutoff
                })
                .ToListAsync();

            var stores = await _db.Devices
                .Where(d => d.IsEnabled)
                .Select(d => d.StoreNumber)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync();

            var groups = await _db.DeviceGroups
                .Select(g => g.GroupName)
                .OrderBy(g => g)
                .ToListAsync();

            var model = new DevicesFilterViewModel
            {
                Search = search,
                StoreNumber = storeNumber,
                GroupName = groupName,
                Status = normalizedStatus,
                AvailableStores = stores,
                AvailableGroups = groups,
                Devices = devices
            };

            return View(model);
        }

        public IActionResult OfflineDevices()
        {
            return RedirectToAction(nameof(Devices), new { status = "Offline" });
        }

        public async Task<IActionResult> Device(Guid id)
        {
            var now = DateTime.UtcNow;
            var cutoff = OnlineCutoffUtc;
            var since24 = now.AddHours(-24);

            var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == id);
            if (device == null)
                return NotFound();

            var recentCommands = await _db.Commands
                .Where(c => c.DeviceId == id || (c.DeviceId == null && c.StoreNumber == device.StoreNumber))
                .OrderByDescending(c => c.CreatedUtc)
                .Take(20)
                .Select(c => new CommandSummaryViewModel
                {
                    CommandId = c.CommandId,
                    Type = c.Type,
                    Scope = c.Scope,
                    StoreNumber = c.StoreNumber,
                    GroupName = c.GroupName,
                    DeviceId = c.DeviceId,
                    Status = c.Status,
                    CreatedUtc = c.CreatedUtc
                })
                .ToListAsync();

            var recentHeartbeats = await _db.Heartbeats
                .Where(h => h.DeviceId == id)
                .OrderByDescending(h => h.TimestampUtc)
                .Take(10)
                .Select(h => new HeartbeatSummaryViewModel
                {
                    HeartbeatId = h.HeartbeatId,
                    TimestampUtc = h.TimestampUtc,
                    PayloadJson = h.PayloadJson
                })
                .ToListAsync();

            var groups = await _db.DeviceGroupMembers
                .Where(m => m.DeviceId == id)
                .Join(_db.DeviceGroups,
                    m => m.DeviceGroupId,
                    g => g.DeviceGroupId,
                    (m, g) => g.GroupName)
                .OrderBy(x => x)
                .ToListAsync();

            var inventory = await _db.RegisterInventories
                .FirstOrDefaultAsync(r => r.DeviceId == id);

            var installedSoftware = await _db.InstalledSoftwares
                .Where(x => x.DeviceId == id)
                .OrderBy(x => x.Name)
                .Select(x => new InstalledSoftwareViewModel
                {
                    Name = x.Name,
                    Version = x.Version,
                    Publisher = x.Publisher,
                    InstallDate = x.InstallDate,
                    UpdatedUtc = x.UpdatedUtc
                })
                .ToListAsync();

            var installedWindowsUpdates = await _db.InstalledWindowsUpdates
                .Where(x => x.DeviceId == id)
                .OrderByDescending(x => x.InstalledOn)
                .ThenBy(x => x.HotFixId)
                .Select(x => new InstalledWindowsUpdateViewModel
                {
                    HotFixId = x.HotFixId,
                    Description = x.Description,
                    InstalledOn = x.InstalledOn,
                    UpdatedUtc = x.UpdatedUtc
                })
                .ToListAsync();
            var processStatus = await _db.ProcessStatusInventories
                .Where(x => x.DeviceId == id)
                .Select(x => new ProcessStatusInventoryViewModel
                {
                    PosProcessName = x.PosProcessName,
                    PosRunning = x.PosRunning,
                    PosProcessCount = x.PosProcessCount,
                    PosCpuPercent = x.PosCpuPercent,
                    PosWorkingSetMb = x.PosWorkingSetMb,
                    PosStartedAtLocal = x.PosStartedAtLocal,

                    RetailShellProcessName = x.RetailShellProcessName,
                    RetailShellRunning = x.RetailShellRunning,
                    RetailShellProcessCount = x.RetailShellProcessCount,
                    RetailShellCpuPercent = x.RetailShellCpuPercent,
                    RetailShellWorkingSetMb = x.RetailShellWorkingSetMb,
                    RetailShellStartedAtLocal = x.RetailShellStartedAtLocal,

                    AgentProcessName = x.AgentProcessName,
                    AgentRunning = x.AgentRunning,
                    AgentProcessCount = x.AgentProcessCount,
                    AgentCpuPercent = x.AgentCpuPercent,
                    AgentWorkingSetMb = x.AgentWorkingSetMb,
                    AgentStartedAtLocal = x.AgentStartedAtLocal,

                    UpdatedUtc = x.UpdatedUtc
                })
                .FirstOrDefaultAsync();

            var userActivity = await _db.UserActivityInventories
                .Where(x => x.DeviceId == id)
                .Select(x => new UserActivityInventoryViewModel
                {
                    CapturedUtc = x.CapturedUtc,
                    LastInputUtc = x.LastInputUtc,
                    IdleSeconds = x.IdleSeconds,
                    SessionState = x.SessionState,
                    ConsoleUserName = x.ConsoleUserName,
                    IsUserActive = x.IsUserActive,
                    IsPosForeground = x.IsPosForeground,
                    UpdatedUtc = x.UpdatedUtc
                })
                .FirstOrDefaultAsync();


            var hasRecentFailure = await _db.CommandResults
                .AnyAsync(r => r.DeviceId == id && r.Status == "Failed" && r.FinishedUtc >= since24);

            var failedSet = new HashSet<Guid>();
            if (hasRecentFailure)
            {
                failedSet.Add(id);
            }

            var health = BuildDeviceHealth(device, inventory, failedSet, cutoff, now);

            var model = new DeviceDetailViewModel
            {
                DeviceId = device.DeviceId,
                StoreNumber = device.StoreNumber,
                Hostname = device.Hostname,
                MachineName = device.MachineName,
                MachineGuid = device.MachineGuid,
                AgentVersion = device.AgentVersion,
                OsVersion = device.OsVersion,
                LastIp = device.LastIp,
                IsEnabled = device.IsEnabled,
                FirstSeenUtc = device.FirstSeenUtc,
                LastSeenUtc = device.LastSeenUtc,
                IsOnline = device.LastSeenUtc >= cutoff,
                RecentCommands = recentCommands,
                RecentHeartbeats = recentHeartbeats,
                Groups = groups,
                Health = health,
                InstalledSoftware = installedSoftware,
                InstalledWindowsUpdates = installedWindowsUpdates,
                ProcessStatus = processStatus,
                UserActivity = userActivity
            };

            return View(model);
        }


[HttpGet]
public async Task<IActionResult> Commands(string? status, string? search, bool expiredPendingOnly = false, int take = 200)
{
    take = Math.Clamp(take, 25, 500);

    var nowUtc = DateTime.UtcNow;
    var baseQuery = _db.Commands.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(status))
    {
        var normalizedStatus = status.Trim();
        baseQuery = baseQuery.Where(c => c.Status == normalizedStatus);
    }

    if (!string.IsNullOrWhiteSpace(search))
    {
        var trimmedSearch = search.Trim();

        baseQuery = baseQuery.Where(c =>
            c.Type.Contains(trimmedSearch) ||
            (c.StoreNumber != null && c.StoreNumber.Contains(trimmedSearch)) ||
            (c.GroupName != null && c.GroupName.Contains(trimmedSearch)) ||
            (c.IssuedBy != null && c.IssuedBy.Contains(trimmedSearch)) ||
            (c.PayloadJson != null && c.PayloadJson.Contains(trimmedSearch)));
    }

    if (expiredPendingOnly)
    {
        baseQuery = baseQuery.Where(c =>
            c.Status == "Pending" &&
            c.ExpiresUtc != null &&
            c.ExpiresUtc <= nowUtc);
    }

    var commands = await baseQuery
        .OrderByDescending(c => c.CreatedUtc)
        .Take(take)
        .Select(c => new CommandCenterRowViewModel
        {
            CommandId = c.CommandId,
            Type = c.Type,
            Scope = c.Scope,
            StoreNumber = c.StoreNumber,
            GroupName = c.GroupName,
            DeviceId = c.DeviceId,
            Status = c.Status,
            CreatedUtc = c.CreatedUtc,
            ExpiresUtc = c.ExpiresUtc,
            AttemptCount = c.AttemptCount,
            MaxAttempts = c.MaxAttempts,
            LastError = c.LastError,
            IssuedBy = c.IssuedBy
        })
        .ToListAsync();

    var allCommands = _db.Commands.AsNoTracking();

    var model = new CommandCenterViewModel
    {
        Status = status,
        Search = search,
        ShowExpiredPendingOnly = expiredPendingOnly,
        TotalCount = await allCommands.CountAsync(),
        PendingCount = await allCommands.CountAsync(c => c.Status == "Pending"),
        InProgressCount = await allCommands.CountAsync(c => c.Status == "InProgress"),
        FailedCount = await allCommands.CountAsync(c => c.Status == "Failed"),
        SucceededCount = await allCommands.CountAsync(c => c.Status == "Succeeded"),
        ExpiredPendingCount = await allCommands.CountAsync(c =>
            c.Status == "Pending" &&
            c.ExpiresUtc != null &&
            c.ExpiresUtc <= nowUtc),
        Commands = commands
    };

    return View(model);
}

[Authorize(Policy = "DashboardHelpdesk")]
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> FailExpiredPendingCommands()
{
    var nowUtc = DateTime.UtcNow;

    var expiredPending = await _db.Commands
        .Where(c =>
            c.Status == "Pending" &&
            c.ExpiresUtc != null &&
            c.ExpiresUtc <= nowUtc)
        .ToListAsync();

    foreach (var command in expiredPending)
    {
        command.Status = "Failed";
        command.LastAttemptUtc = nowUtc;

        if (string.IsNullOrWhiteSpace(command.LastError))
        {
            command.LastError = "Command expired before pickup by agent.";
        }
    }

    await _db.SaveChangesAsync();

    await AuditAsync(
        "FailExpiredPendingCommands",
        "Command",
        null,
        new
        {
            Count = expiredPending.Count,
            CommandIds = expiredPending.Select(c => c.CommandId).ToList()
        });

    TempData["CommandMessage"] = $"Marked {expiredPending.Count} expired pending command(s) as Failed.";
    return RedirectToAction(nameof(Commands), new { expiredPendingOnly = true });
}

[Authorize(Policy = "DashboardHelpdesk")]
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> FailExpiredPendingCommand(Guid id)
{
    var nowUtc = DateTime.UtcNow;

    var command = await _db.Commands.FirstOrDefaultAsync(c => c.CommandId == id);
    if (command == null)
    {
        TempData["CommandMessage"] = "Command not found.";
        return RedirectToAction(nameof(Commands));
    }

    if (command.Status == "Pending" && command.ExpiresUtc != null && command.ExpiresUtc <= nowUtc)
    {
        command.Status = "Failed";
        command.LastAttemptUtc = nowUtc;

        if (string.IsNullOrWhiteSpace(command.LastError))
        {
            command.LastError = "Command expired before pickup by agent.";
        }

        await _db.SaveChangesAsync();
        await AuditAsync("FailExpiredPendingCommand", "Command", command.CommandId.ToString(), new { command.CommandId });

        TempData["CommandMessage"] = $"Command {command.CommandId} marked Failed.";
    }
    else
    {
        TempData["CommandMessage"] = "Command is not an expired pending command.";
    }

    return RedirectToAction(nameof(Commands));
}

public async Task<IActionResult> Command(Guid id)
        {
            var cmd = await _db.Commands.FirstOrDefaultAsync(c => c.CommandId == id);
            if (cmd == null)
                return NotFound();

            var result = await _db.CommandResults
                .Where(r => r.CommandId == id)
                .Select(r => new CommandResultDetailViewModel
                {
                    CommandResultId = r.CommandResultId,
                    CommandId = r.CommandId,
                    DeviceId = r.DeviceId,
                    Status = r.Status,
                    ExitCode = r.ExitCode,
                    StdOut = r.StdOut,
                    StdErr = r.StdErr,
                    StartedUtc = r.StartedUtc,
                    FinishedUtc = r.FinishedUtc
                })
                .FirstOrDefaultAsync();

            var model = new CommandDetailViewModel
            {
                CommandId = cmd.CommandId,
                Type = cmd.Type,
                Scope = cmd.Scope,
                StoreNumber = cmd.StoreNumber,
                GroupName = cmd.GroupName,
                DeviceId = cmd.DeviceId,
                Status = cmd.Status,
                PayloadJson = cmd.PayloadJson,
                Priority = cmd.Priority,
                AttemptCount = cmd.AttemptCount,
                MaxAttempts = cmd.MaxAttempts,
                CreatedUtc = cmd.CreatedUtc,
                LockedUtc = cmd.LockedUtc,
                LockedByDeviceId = cmd.LockedByDeviceId,
                LastError = cmd.LastError,
                IssuedBy = cmd.IssuedBy,
                IssuedUtc = cmd.IssuedUtc,
                Result = result
            };

            ViewBag.ExpiresUtc = cmd.ExpiresUtc;
            ViewBag.IsExpiredPending = cmd.Status == "Pending" && cmd.ExpiresUtc != null && cmd.ExpiresUtc <= DateTime.UtcNow;
            ViewBag.PendingReason = cmd.Status == "Pending"
                ? (cmd.ExpiresUtc != null && cmd.ExpiresUtc <= DateTime.UtcNow
                    ? "Expired before pickup by agent."
                    : cmd.DeviceId != null
                        ? "Waiting for the targeted device to poll and claim the command."
                        : !string.IsNullOrWhiteSpace(cmd.StoreNumber)
                            ? "Waiting for matching store device polling."
                            : !string.IsNullOrWhiteSpace(cmd.GroupName)
                                ? "Waiting for matching group device polling."
                                : "Waiting to be picked up by an agent.")
                : null;

            return View(model);
        }

        public async Task<IActionResult> Versions()
        {
            var cutoff = OnlineCutoffUtc;

            var devices = await _db.Devices
                .Where(d => d.IsEnabled)
                .ToListAsync();

            var model = devices
                .GroupBy(d => d.AgentVersion)
                .Select(g => new VersionSummaryViewModel
                {
                    AgentVersion = g.Key,
                    DeviceCount = g.Count(),
                    OnlineCount = g.Count(d => d.LastSeenUtc >= cutoff),
                    OfflineCount = g.Count(d => d.LastSeenUtc < cutoff)
                })
                .OrderByDescending(x => x.DeviceCount)
                .ToList();

            return View(model);
        }
        [HttpGet]
        public async Task<IActionResult> CommandModal(Guid id)
        {
            var cmd = await _db.Commands.FirstOrDefaultAsync(c => c.CommandId == id);
            if (cmd == null)
                return NotFound();

            var result = await _db.CommandResults
                .Where(r => r.CommandId == id)
                .Select(r => new CommandResultDetailViewModel
                {
                    CommandResultId = r.CommandResultId,
                    CommandId = r.CommandId,
                    DeviceId = r.DeviceId,
                    Status = r.Status,
                    ExitCode = r.ExitCode,
                    StdOut = r.StdOut,
                    StdErr = r.StdErr,
                    StartedUtc = r.StartedUtc,
                    FinishedUtc = r.FinishedUtc
                })
                .FirstOrDefaultAsync();

            var model = new CommandDetailViewModel
            {
                CommandId = cmd.CommandId,
                Type = cmd.Type,
                Scope = cmd.Scope,
                StoreNumber = cmd.StoreNumber,
                GroupName = cmd.GroupName,
                DeviceId = cmd.DeviceId,
                Status = cmd.Status,
                PayloadJson = cmd.PayloadJson,
                Priority = cmd.Priority,
                AttemptCount = cmd.AttemptCount,
                MaxAttempts = cmd.MaxAttempts,
                CreatedUtc = cmd.CreatedUtc,
                LockedUtc = cmd.LockedUtc,
                LockedByDeviceId = cmd.LockedByDeviceId,
                LastError = cmd.LastError,
                IssuedBy = cmd.IssuedBy,
                IssuedUtc = cmd.IssuedUtc,
                Result = result
            };

            ViewBag.ExpiresUtc = cmd.ExpiresUtc;
            ViewBag.IsExpiredPending = cmd.Status == "Pending" && cmd.ExpiresUtc != null && cmd.ExpiresUtc <= DateTime.UtcNow;
            ViewBag.PendingReason = cmd.Status == "Pending"
                ? (cmd.ExpiresUtc != null && cmd.ExpiresUtc <= DateTime.UtcNow
                    ? "Expired before pickup by agent."
                    : cmd.DeviceId != null
                        ? "Waiting for the targeted device to poll and claim the command."
                        : !string.IsNullOrWhiteSpace(cmd.StoreNumber)
                            ? "Waiting for matching store device polling."
                            : !string.IsNullOrWhiteSpace(cmd.GroupName)
                                ? "Waiting for matching group device polling."
                                : "Waiting to be picked up by an agent.")
                : null;

            return PartialView("_CommandDetailModal", model);
        }

        [HttpGet]
        public async Task<IActionResult> Groups()
        {
            var groups = await _db.DeviceGroups
                .OrderBy(g => g.GroupName)
                .Select(g => new GroupSummaryViewModel
                {
                    DeviceGroupId = g.DeviceGroupId,
                    GroupName = g.GroupName,
                    Description = g.Description,
                    CreatedUtc = g.CreatedUtc,
                    MemberCount = _db.DeviceGroupMembers.Count(m => m.DeviceGroupId == g.DeviceGroupId)
                })
                .ToListAsync();

            return View(groups);
        }

        [Authorize(Policy = "DashboardHelpdesk")]
        [HttpGet]
        public async Task<IActionResult> HelpdeskCommands(Guid? deviceId, string? storeNumber, string? groupName)
        {
            var model = new HelpdeskCommandsViewModel
            {
                DeviceId = deviceId,
                StoreNumber = storeNumber,
                GroupName = groupName,
                TargetType = deviceId != null ? "Device"
                    : !string.IsNullOrWhiteSpace(storeNumber) ? "Store"
                    : !string.IsNullOrWhiteSpace(groupName) ? "Group"
                    : "Device",
                CommandKey = "CollectSystemInfo",
                Message = TempData["HelpdeskMessage"]?.ToString()
            };

            await PopulateHelpdeskCommandLists(model, OnlineCutoffUtc);
            return View(model);
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpGet]
        public async Task<IActionResult> CreateDeployment(int? packageId, int? targetType, string? targetValue, CancellationToken cancellationToken)
        {
            var vm = new CreateDeploymentViewModel();
            await PopulateCreateDeploymentLists(vm, cancellationToken);

            if (packageId.HasValue && packageId.Value > 0)
                vm.PackageId = packageId.Value;

            if (targetType.HasValue && targetType.Value > 0)
                vm.TargetType = targetType.Value;

            if (!string.IsNullOrWhiteSpace(targetValue))
                vm.TargetValue = targetValue.Trim();

            SetDeploymentFormViewData("Create", nameof(CreateDeployment), "Create Deployment", "Create Deployment");
            return View(vm);
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDeployment(CreateDeploymentViewModel model, CancellationToken cancellationToken)
        {
            return await SaveDeploymentInternalAsync(model, null, cancellationToken);
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditDeployment(int id, CreateDeploymentViewModel model, CancellationToken cancellationToken)
        {
            return await SaveDeploymentInternalAsync(model, id, cancellationToken);
        }

        [Authorize(Policy = "DashboardHelpdesk")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RunHelpdeskCommand(HelpdeskCommandsViewModel model)
        {
            var cutoff = OnlineCutoffUtc;
            var availableCommands = GetHelpdeskCommands();
            var selectedCommand = availableCommands.FirstOrDefault(c => c.Key == model.CommandKey);
            var normalizedTargetType = string.IsNullOrWhiteSpace(model.TargetType) ? "Device" : model.TargetType.Trim();

            if (selectedCommand == null)
            {
                ModelState.AddModelError(nameof(model.CommandKey), "Unsupported helpdesk command.");
            }

            if (normalizedTargetType.Equals("Device", StringComparison.OrdinalIgnoreCase) && model.DeviceId == null)
                ModelState.AddModelError("", "Select a device.");
            else if (normalizedTargetType.Equals("Store", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(model.StoreNumber))
                ModelState.AddModelError("", "Select a store.");
            else if (normalizedTargetType.Equals("Group", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(model.GroupName))
                ModelState.AddModelError("", "Select a group.");

            if (selectedCommand != null)
            {
                var allowed = normalizedTargetType switch
                {
                    "Device" => selectedCommand.DeviceSupported,
                    "Store" => selectedCommand.StoreSupported,
                    "Group" => selectedCommand.GroupSupported,
                    _ => false
                };

                if (!allowed)
                    ModelState.AddModelError("", $"{selectedCommand.Name} is not supported for the selected target type.");
            }

            if (!ModelState.IsValid)
            {
                await AuditAsync("RunHelpdeskCommandValidationFailed",
                    normalizedTargetType,
                    model.DeviceId?.ToString() ?? model.StoreNumber ?? model.GroupName,
                    new { model.CommandKey, model.TargetType, model.DeviceId, model.StoreNumber, model.GroupName },
                    false,
                    "ModelState invalid");
                await PopulateHelpdeskCommandLists(model, cutoff);
                return View("HelpdeskCommands", model);
            }

            if (string.Equals(model.CommandKey, "RequeueFailed", StringComparison.OrdinalIgnoreCase))
            {
                if (model.DeviceId == null)
                {
                    ModelState.AddModelError("", "Requeue Failed Commands requires a device target.");
                    await PopulateHelpdeskCommandLists(model, cutoff);
                    return View("HelpdeskCommands", model);
                }

                return await RequeueRecentFailedCommands(model.DeviceId.Value);
            }

            var now = DateTime.UtcNow;
            var createdCommands = new List<Command>();

            if (normalizedTargetType.Equals("Device", StringComparison.OrdinalIgnoreCase) && model.DeviceId != null)
            {
                var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == model.DeviceId.Value && d.IsEnabled);
                if (device == null)
                {
                    ModelState.AddModelError("", "Selected device was not found.");
                }
                else
                {
                    createdCommands.Add(BuildHelpdeskCommandForDevice(device, model.CommandKey, now));
                }
            }
            else if (normalizedTargetType.Equals("Store", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(model.StoreNumber))
            {
                var normalizedStore = model.StoreNumber.Trim();

                var storeDevices = await _db.Devices
                    .Where(d => d.IsEnabled && d.StoreNumber == normalizedStore)
                    .OrderBy(d => d.Hostname)
                    .ToListAsync();

                if (storeDevices.Count == 0)
                {
                    ModelState.AddModelError("", "No devices were found for the selected store.");
                }
                else
                {
                    foreach (var device in storeDevices)
                        createdCommands.Add(BuildHelpdeskCommandForDevice(device, model.CommandKey, now));
                }
            }
            else if (normalizedTargetType.Equals("Group", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(model.GroupName))
            {
                var normalizedGroup = model.GroupName.Trim();

                var groupDevices = await _db.DeviceGroupMembers
                    .Where(m => _db.DeviceGroups.Any(g => g.DeviceGroupId == m.DeviceGroupId && g.GroupName == normalizedGroup))
                    .Join(_db.Devices.Where(d => d.IsEnabled), m => m.DeviceId, d => d.DeviceId, (m, d) => d)
                    .OrderBy(d => d.StoreNumber)
                    .ThenBy(d => d.Hostname)
                    .ToListAsync();

                if (groupDevices.Count == 0)
                {
                    ModelState.AddModelError("", "No devices were found for the selected group.");
                }
                else
                {
                    foreach (var device in groupDevices)
                        createdCommands.Add(BuildHelpdeskCommandForDevice(device, model.CommandKey, now, normalizedGroup));
                }
            }

            if (!ModelState.IsValid)
            {
                await AuditAsync("RunHelpdeskCommandValidationFailed",
                    normalizedTargetType,
                    model.DeviceId?.ToString() ?? model.StoreNumber ?? model.GroupName,
                    new { model.CommandKey, model.TargetType, model.DeviceId, model.StoreNumber, model.GroupName },
                    false,
                    "Target validation failed");
                await PopulateHelpdeskCommandLists(model, cutoff);
                return View("HelpdeskCommands", model);
            }

            _db.Commands.AddRange(createdCommands);
            await _db.SaveChangesAsync();
            await AuditAsync("RunHelpdeskCommand",
                normalizedTargetType,
                model.DeviceId?.ToString() ?? model.StoreNumber ?? model.GroupName,
                new
                {
                    model.CommandKey,
                    model.TargetType,
                    model.DeviceId,
                    model.StoreNumber,
                    model.GroupName,
                    CreatedCommandCount = createdCommands.Count,
                    CommandIds = createdCommands.Select(c => c.CommandId).ToList()
                });

            var commandName = selectedCommand?.Name ?? model.CommandKey;
            TempData["HelpdeskMessage"] = createdCommands.Count == 1
                ? $"{commandName} command created successfully."
                : $"{commandName} created for {createdCommands.Count} device(s).";

            if (createdCommands.Count == 1)
            {
                if (model.DeviceId != null)
                {
                    TempData["DeviceMessage"] = $"{commandName} command created successfully.";
                    return RedirectToAction(nameof(Device), new { id = model.DeviceId.Value });
                }

                return RedirectToAction(nameof(Command), new { id = createdCommands[0].CommandId });
            }

            return RedirectToAction(nameof(Commands));
        }

        [Authorize(Policy = "DashboardAdmin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RetireDevice(Guid deviceId)
        {
            var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
            if (device == null)
                return NotFound();

            var cutoff = DateTime.UtcNow.AddMinutes(-5);

            if (device.LastSeenUtc >= cutoff)
            {
                await AuditAsync("RetireDevice", "Device", deviceId.ToString(), new { DeviceId = deviceId, Hostname = device.Hostname, Reason = "Device online" }, false, "Device cannot be retired because it is currently online.");
                TempData["DeviceMessage"] = "Device cannot be retired because it is currently online.";
                return RedirectToAction(nameof(Device), new { id = deviceId });
            }

            device.IsEnabled = false;

            var memberships = await _db.DeviceGroupMembers
                .Where(m => m.DeviceId == deviceId)
                .ToListAsync();

            if (memberships.Count > 0)
            {
                _db.DeviceGroupMembers.RemoveRange(memberships);
            }

            await _db.SaveChangesAsync();
            await AuditAsync("RetireDevice", "Device", deviceId.ToString(), new { DeviceId = deviceId, Hostname = device.Hostname, RemovedMembershipCount = memberships.Count });

            TempData["DeviceMessage"] = $"Device {device.Hostname} retired.";
            return RedirectToAction(nameof(Devices));
        }

        [Authorize(Policy = "DashboardAdmin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateGroup(string groupName, string? description)
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                TempData["GroupMessage"] = "Group name is required.";
                return RedirectToAction(nameof(Groups));
            }

            var normalized = groupName.Trim();

            var exists = await _db.DeviceGroups.AnyAsync(g => g.GroupName == normalized);
            if (exists)
            {
                TempData["GroupMessage"] = "Group already exists.";
                return RedirectToAction(nameof(Groups));
            }

            var group = new DeviceGroup
            {
                GroupName = normalized,
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                CreatedUtc = DateTime.UtcNow
            };

            _db.DeviceGroups.Add(group);
            await _db.SaveChangesAsync();
            await AuditAsync("CreateGroup", "Group", group.GroupName, new { group.DeviceGroupId, group.GroupName, group.Description });

            TempData["GroupMessage"] = $"Group '{group.GroupName}' created.";
            return RedirectToAction(nameof(Group), new { groupName = group.GroupName });
        }

        [Authorize(Policy = "DashboardAdmin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportGroupCsv(string groupName, IFormFile? csvFile)
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                TempData["GroupMessage"] = "Group name is required for CSV import.";
                return RedirectToAction(nameof(Groups));
            }

            if (csvFile == null || csvFile.Length == 0)
            {
                TempData["GroupMessage"] = "Please choose a CSV file to upload.";
                return RedirectToAction(nameof(Groups));
            }

            if (!Path.GetExtension(csvFile.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                TempData["GroupMessage"] = "Only .csv files are supported.";
                return RedirectToAction(nameof(Groups));
            }

            var normalizedGroupName = groupName.Trim();

            var group = await _db.DeviceGroups
                .FirstOrDefaultAsync(g => g.GroupName == normalizedGroupName);

            if (group == null)
            {
                group = new DeviceGroup
                {
                    GroupName = normalizedGroupName,
                    CreatedUtc = DateTime.UtcNow
                };

                _db.DeviceGroups.Add(group);
                await _db.SaveChangesAsync();
            }

            using var stream = csvFile.OpenReadStream();
            using var reader = new StreamReader(stream);

            var allText = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(allText))
            {
                TempData["GroupMessage"] = "CSV file was empty.";
                return RedirectToAction(nameof(Group), new { groupName = group.GroupName });
            }

            var lines = allText
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            if (lines.Count < 2)
            {
                TempData["GroupMessage"] = "CSV must include a header row and at least one data row.";
                return RedirectToAction(nameof(Group), new { groupName = group.GroupName });
            }

            var headers = SplitCsvLine(lines[0])
                .Select(h => h.Trim())
                .ToList();

            var storeIdx = headers.FindIndex(h => h.Equals("StoreNumber", StringComparison.OrdinalIgnoreCase));
            var hostIdx = headers.FindIndex(h => h.Equals("Hostname", StringComparison.OrdinalIgnoreCase));
            var deviceIdIdx = headers.FindIndex(h => h.Equals("DeviceId", StringComparison.OrdinalIgnoreCase));

            if (deviceIdIdx < 0 && hostIdx < 0)
            {
                TempData["GroupMessage"] = "CSV must contain either DeviceId, Hostname, or StoreNumber + Hostname columns.";
                return RedirectToAction(nameof(Group), new { groupName = group.GroupName });
            }

            var processed = 0;
            var added = 0;
            var alreadyMembers = 0;
            var notFound = 0;
            var skipped = 0;

            var existingMembers = (await _db.DeviceGroupMembers
                .Where(m => m.DeviceGroupId == group.DeviceGroupId)
                .Select(m => m.DeviceId)
                .ToListAsync())
                .ToHashSet();

            var seenDeviceIdsThisImport = new HashSet<Guid>();

            for (int i = 1; i < lines.Count; i++)
            {
                var rawLine = lines[i];
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    skipped++;
                    continue;
                }

                var cols = SplitCsvLine(rawLine);

                string? storeNumber = storeIdx >= 0 && storeIdx < cols.Count ? cols[storeIdx]?.Trim() : null;
                string? hostname = hostIdx >= 0 && hostIdx < cols.Count ? cols[hostIdx]?.Trim() : null;
                string? deviceIdText = deviceIdIdx >= 0 && deviceIdIdx < cols.Count ? cols[deviceIdIdx]?.Trim() : null;

                if (string.IsNullOrWhiteSpace(storeNumber) &&
                    string.IsNullOrWhiteSpace(hostname) &&
                    string.IsNullOrWhiteSpace(deviceIdText))
                {
                    skipped++;
                    continue;
                }

                processed++;

                Device? device = null;

                if (!string.IsNullOrWhiteSpace(deviceIdText) && Guid.TryParse(deviceIdText, out var parsedDeviceId))
                {
                    device = await _db.Devices
                        .FirstOrDefaultAsync(d => d.DeviceId == parsedDeviceId && d.IsEnabled);
                }
                else if (!string.IsNullOrWhiteSpace(storeNumber) && !string.IsNullOrWhiteSpace(hostname))
                {
                    device = await _db.Devices
                        .FirstOrDefaultAsync(d =>
                            d.IsEnabled &&
                            d.StoreNumber == storeNumber &&
                            d.Hostname == hostname);
                }
                else if (!string.IsNullOrWhiteSpace(hostname))
                {
                    device = await _db.Devices
                        .FirstOrDefaultAsync(d =>
                            d.IsEnabled &&
                            d.Hostname == hostname);
                }

                if (device == null)
                {
                    notFound++;
                    continue;
                }

                if (seenDeviceIdsThisImport.Contains(device.DeviceId))
                {
                    skipped++;
                    continue;
                }

                seenDeviceIdsThisImport.Add(device.DeviceId);

                if (existingMembers.Contains(device.DeviceId))
                {
                    alreadyMembers++;
                    continue;
                }

                _db.DeviceGroupMembers.Add(new DeviceGroupMember
                {
                    DeviceGroupId = group.DeviceGroupId,
                    DeviceId = device.DeviceId,
                    AddedUtc = DateTime.UtcNow
                });

                existingMembers.Add(device.DeviceId);
                added++;
            }

            await _db.SaveChangesAsync();
            await AuditAsync("ImportGroupCsv", "Group", group.GroupName, new
            {
                group.DeviceGroupId,
                group.GroupName,
                FileName = csvFile.FileName,
                Processed = processed,
                Added = added,
                AlreadyMembers = alreadyMembers,
                NotFound = notFound,
                Skipped = skipped
            });

            TempData["GroupMessage"] =
                $"CSV import complete for group '{group.GroupName}'. " +
                $"Processed: {processed}. Added: {added}. Already members: {alreadyMembers}. " +
                $"Not found: {notFound}. Skipped: {skipped}.";

            return RedirectToAction(nameof(Group), new { groupName = group.GroupName });
        }

        [HttpGet]
        public async Task<IActionResult> Group(string groupName)
        {
            var cutoff = OnlineCutoffUtc;

            var group = await _db.DeviceGroups.FirstOrDefaultAsync(g => g.GroupName == groupName);
            if (group == null)
                return NotFound();

            var members = await _db.DeviceGroupMembers
                .Where(m => m.DeviceGroupId == group.DeviceGroupId)
                .Join(_db.Devices,
                    m => m.DeviceId,
                    d => d.DeviceId,
                    (m, d) => new DeviceSummaryViewModel
                    {
                        DeviceId = d.DeviceId,
                        StoreNumber = d.StoreNumber,
                        Hostname = d.Hostname,
                        AgentVersion = d.AgentVersion,
                        LastSeenUtc = d.LastSeenUtc,
                        IsOnline = d.LastSeenUtc >= cutoff
                    })
                .OrderBy(d => d.StoreNumber)
                .ThenBy(d => d.Hostname)
                .ToListAsync();

            var memberIds = members.Select(m => m.DeviceId).ToHashSet();

            var availableDevices = await _db.Devices
                .Where(d => d.IsEnabled)
                .OrderBy(d => d.StoreNumber)
                .ThenBy(d => d.Hostname)
                .Select(d => new DeviceSummaryViewModel
                {
                    DeviceId = d.DeviceId,
                    StoreNumber = d.StoreNumber,
                    Hostname = d.Hostname,
                    AgentVersion = d.AgentVersion,
                    LastSeenUtc = d.LastSeenUtc,
                    IsOnline = d.LastSeenUtc >= cutoff
                })
                .ToListAsync();

            availableDevices = availableDevices
                .Where(d => !memberIds.Contains(d.DeviceId))
                .ToList();

            var model = new GroupDetailViewModel
            {
                DeviceGroupId = group.DeviceGroupId,
                GroupName = group.GroupName,
                Description = group.Description,
                CreatedUtc = group.CreatedUtc,
                Members = members,
                AvailableDevices = availableDevices,
                Message = TempData["GroupMessage"]?.ToString()
            };

            return View(model);
        }

        [Authorize(Policy = "DashboardAdmin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddDeviceToGroup(string groupName, Guid deviceId)
        {
            var group = await _db.DeviceGroups.FirstOrDefaultAsync(g => g.GroupName == groupName);
            if (group == null)
                return NotFound();

            var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
            if (device == null)
                return NotFound();

            var exists = await _db.DeviceGroupMembers
                .AnyAsync(m => m.DeviceGroupId == group.DeviceGroupId && m.DeviceId == deviceId);

            if (!exists)
            {
                _db.DeviceGroupMembers.Add(new DeviceGroupMember
                {
                    DeviceGroupId = group.DeviceGroupId,
                    DeviceId = deviceId,
                    AddedUtc = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();
                await AuditAsync("AddDeviceToGroup", "Group", group.GroupName, new { group.DeviceGroupId, group.GroupName, DeviceId = deviceId, device.Hostname });
                TempData["GroupMessage"] = $"Added {device.Hostname} to {group.GroupName}.";
            }
            else
            {
                TempData["GroupMessage"] = $"{device.Hostname} is already in {group.GroupName}.";
            }

            return RedirectToAction(nameof(Group), new { groupName });
        }

        [Authorize(Policy = "DashboardAdmin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveDeviceFromGroup(string groupName, Guid deviceId)
        {
            var group = await _db.DeviceGroups.FirstOrDefaultAsync(g => g.GroupName == groupName);
            if (group == null)
                return NotFound();

            var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
            if (device == null)
                return NotFound();

            var member = await _db.DeviceGroupMembers
                .FirstOrDefaultAsync(m => m.DeviceGroupId == group.DeviceGroupId && m.DeviceId == deviceId);

            if (member == null)
            {
                TempData["GroupMessage"] = $"{device.Hostname} is not a member of {group.GroupName}.";
                return RedirectToAction(nameof(Group), new { groupName });
            }

            _db.DeviceGroupMembers.Remove(member);
            await _db.SaveChangesAsync();
            await AuditAsync("RemoveDeviceFromGroup", "Group", group.GroupName, new { group.DeviceGroupId, group.GroupName, DeviceId = deviceId, device.Hostname });

            TempData["GroupMessage"] = $"Removed {device.Hostname} from {group.GroupName}.";
            return RedirectToAction(nameof(Group), new { groupName });
        }

        [Authorize(Policy = "DashboardHelpdesk")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequeueRecentFailedCommands(Guid deviceId)
        {
            var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
            if (device == null)
                return NotFound();

            var failedCommands = await _db.Commands
                .Where(c =>
                    c.Status == "Failed" &&
                    (c.DeviceId == deviceId || (c.DeviceId == null && c.StoreNumber == device.StoreNumber)))
                .OrderByDescending(c => c.CreatedUtc)
                .Take(10)
                .ToListAsync();

            foreach (var cmd in failedCommands)
            {
                cmd.Status = "Pending";
                cmd.LockedUtc = null;
                cmd.LockedByDeviceId = null;
                cmd.LastError = "Requeued from dashboard quick action";
            }

            await _db.SaveChangesAsync();
            await AuditAsync("RequeueRecentFailedCommands", "Device", deviceId.ToString(), new { DeviceId = deviceId, device.Hostname, RequeuedCount = failedCommands.Count, CommandIds = failedCommands.Select(c => c.CommandId).ToList() });

            TempData["DeviceMessage"] = $"Requeued {failedCommands.Count} failed command(s).";
            return RedirectToAction(nameof(Device), new { id = deviceId });
        }

        [Authorize(Policy = "DashboardAdmin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteGroup(string groupName)
        {
            var group = await _db.DeviceGroups.FirstOrDefaultAsync(g => g.GroupName == groupName);
            if (group == null)
                return NotFound();

            var memberCount = await _db.DeviceGroupMembers.CountAsync(m => m.DeviceGroupId == group.DeviceGroupId);
            if (memberCount > 0)
            {
                TempData["GroupMessage"] = $"Cannot delete group '{group.GroupName}' because it still has {memberCount} member(s). Remove members first.";
                return RedirectToAction(nameof(Group), new { groupName });
            }

            _db.DeviceGroups.Remove(group);
            await _db.SaveChangesAsync();
            await AuditAsync("DeleteGroup", "Group", group.GroupName, new { group.DeviceGroupId, group.GroupName });

            TempData["GroupMessage"] = $"Group '{group.GroupName}' deleted.";
            return RedirectToAction(nameof(Groups));
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpGet]
        public async Task<IActionResult> IssueCommand(Guid? deviceId, string? groupName, string? storeNumber)
        {
            var cutoff = OnlineCutoffUtc;
            var templates = GetCommandTemplates();

            var model = new IssueCommandViewModel
            {
                DeviceId = deviceId,
                GroupName = groupName,
                StoreNumber = storeNumber,

                AvailableDevices = await _db.Devices
                    .Where(d => d.IsEnabled)
                    .OrderBy(d => d.StoreNumber)
                    .ThenBy(d => d.Hostname)
                    .Select(d => new DeviceSummaryViewModel
                    {
                        DeviceId = d.DeviceId,
                        StoreNumber = d.StoreNumber,
                        Hostname = d.Hostname,
                        AgentVersion = d.AgentVersion,
                        LastSeenUtc = d.LastSeenUtc,
                        IsOnline = d.LastSeenUtc >= cutoff
                    })
                    .ToListAsync(),

                AvailableStores = await _db.Devices
                    .Where(d => d.IsEnabled)
                    .Select(d => d.StoreNumber)
                    .Distinct()
                    .OrderBy(s => s)
                    .ToListAsync(),

                AvailableGroups = await _db.DeviceGroups
                    .Select(g => g.GroupName)
                    .OrderBy(g => g)
                    .ToListAsync(),

                AvailableCommandTypes = GetAvailableCommandTypes(),
                AvailableTemplates = templates,

                Priority = 100,
                MaxAttempts = 3,
                Type = "Echo",
                TemplateName = "Echo - Hello",
                UseCustomJson = false,
                PayloadJson = templates.First(t => t.Name == "Echo - Hello").PayloadJson
            };

            return View(model);
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IssueCommand(IssueCommandViewModel model)
        {
            var cutoff = OnlineCutoffUtc;
            var templates = GetCommandTemplates();
            var commandTypes = GetAvailableCommandTypes();

            if (!model.UseCustomJson && !string.IsNullOrWhiteSpace(model.TemplateName))
            {
                var selectedTemplate = templates.FirstOrDefault(t => t.Name == model.TemplateName);
                if (selectedTemplate != null)
                {
                    model.Type = selectedTemplate.CommandType;
                    model.PayloadJson = selectedTemplate.PayloadJson;
                }
            }

            var targetCount = 0;
            if (model.DeviceId != null) targetCount++;
            if (!string.IsNullOrWhiteSpace(model.StoreNumber)) targetCount++;
            if (!string.IsNullOrWhiteSpace(model.GroupName)) targetCount++;

            if (string.IsNullOrWhiteSpace(model.Type))
            {
                ModelState.AddModelError(nameof(model.Type), "Type is required.");
            }

            if (targetCount != 1)
            {
                ModelState.AddModelError("", "Provide exactly one target: Device, Store, or Group.");
            }

            if (!commandTypes.Contains(model.Type))
            {
                ModelState.AddModelError(nameof(model.Type), "Unsupported command type.");
            }

            if (!ModelState.IsValid)
            {
                await AuditAsync("IssueCommandValidationFailed",
                    model.DeviceId != null ? "Device" : !string.IsNullOrWhiteSpace(model.StoreNumber) ? "Store" : !string.IsNullOrWhiteSpace(model.GroupName) ? "Group" : null,
                    model.DeviceId?.ToString() ?? model.StoreNumber ?? model.GroupName,
                    new { model.Type, model.DeviceId, model.StoreNumber, model.GroupName, model.Priority, model.MaxAttempts },
                    false,
                    "ModelState invalid");
                await PopulateIssueCommandLists(model, cutoff, commandTypes, templates);
                return View(model);
            }

            var now = DateTime.UtcNow;
            var createdCommands = new List<Command>();

            if (model.DeviceId != null)
            {
                var device = await _db.Devices
                    .FirstOrDefaultAsync(d => d.DeviceId == model.DeviceId.Value && d.IsEnabled);

                if (device == null)
                {
                    ModelState.AddModelError("", "Selected device was not found.");
                }
                else
                {
                    createdCommands.Add(BuildDeviceCommand(
                        device,
                        model.Type.Trim(),
                        string.IsNullOrWhiteSpace(model.PayloadJson) ? null : model.PayloadJson,
                        model.Priority,
                        model.MaxAttempts,
                        model.ExpiresUtc,
                        issuedBy: CurrentActor(),
                        issuedUtc: now,
                        groupName: null));
                }
            }
            else if (!string.IsNullOrWhiteSpace(model.StoreNumber))
            {
                var normalizedStore = model.StoreNumber.Trim();

                var storeDevices = await _db.Devices
                    .Where(d => d.IsEnabled && d.StoreNumber == normalizedStore)
                    .OrderBy(d => d.Hostname)
                    .ToListAsync();

                if (storeDevices.Count == 0)
                {
                    ModelState.AddModelError("", "No devices were found for the selected store.");
                }
                else
                {
                    foreach (var device in storeDevices)
                    {
                        createdCommands.Add(BuildDeviceCommand(
                            device,
                            model.Type.Trim(),
                            string.IsNullOrWhiteSpace(model.PayloadJson) ? null : model.PayloadJson,
                            model.Priority,
                            model.MaxAttempts,
                            model.ExpiresUtc,
                            issuedBy: CurrentActor(),
                            issuedUtc: now,
                            groupName: null));
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(model.GroupName))
            {
                var normalizedGroup = model.GroupName.Trim();

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
                    .ToListAsync();

                if (groupDevices.Count == 0)
                {
                    ModelState.AddModelError("", "No devices were found for the selected group.");
                }
                else
                {
                    foreach (var device in groupDevices)
                    {
                        createdCommands.Add(BuildDeviceCommand(
                            device,
                            model.Type.Trim(),
                            string.IsNullOrWhiteSpace(model.PayloadJson) ? null : model.PayloadJson,
                            model.Priority,
                            model.MaxAttempts,
                            model.ExpiresUtc,
                            issuedBy: CurrentActor(),
                            issuedUtc: now,
                            groupName: normalizedGroup));
                    }
                }
            }

            if (!ModelState.IsValid)
            {
                await AuditAsync("IssueCommandValidationFailed",
                    model.DeviceId != null ? "Device" : !string.IsNullOrWhiteSpace(model.StoreNumber) ? "Store" : !string.IsNullOrWhiteSpace(model.GroupName) ? "Group" : null,
                    model.DeviceId?.ToString() ?? model.StoreNumber ?? model.GroupName,
                    new { model.Type, model.DeviceId, model.StoreNumber, model.GroupName, model.Priority, model.MaxAttempts },
                    false,
                    "Target validation failed");
                await PopulateIssueCommandLists(model, cutoff, commandTypes, templates);
                return View(model);
            }

            if (createdCommands.Count == 0)
            {
                await AuditAsync("IssueCommandValidationFailed",
                    model.DeviceId != null ? "Device" : !string.IsNullOrWhiteSpace(model.StoreNumber) ? "Store" : !string.IsNullOrWhiteSpace(model.GroupName) ? "Group" : null,
                    model.DeviceId?.ToString() ?? model.StoreNumber ?? model.GroupName,
                    new { model.Type, model.DeviceId, model.StoreNumber, model.GroupName, model.Priority, model.MaxAttempts },
                    false,
                    "No matching target devices were found.");
                ModelState.AddModelError("", "No matching target devices were found.");
                await PopulateIssueCommandLists(model, cutoff, commandTypes, templates);
                return View(model);
            }

            _db.Commands.AddRange(createdCommands);
            await _db.SaveChangesAsync();
            await AuditAsync("IssueCommand",
                model.DeviceId != null ? "Device" : !string.IsNullOrWhiteSpace(model.StoreNumber) ? "Store" : "Group",
                model.DeviceId?.ToString() ?? model.StoreNumber ?? model.GroupName,
                new
                {
                    model.Type,
                    model.DeviceId,
                    model.StoreNumber,
                    model.GroupName,
                    model.Priority,
                    model.MaxAttempts,
                    model.ExpiresUtc,
                    CreatedCommandCount = createdCommands.Count,
                    CommandIds = createdCommands.Select(c => c.CommandId).ToList()
                });

            if (createdCommands.Count == 1)
            {
                if (model.DeviceId != null)
                {
                    TempData["DeviceMessage"] = $"Command '{model.Type}' created successfully.";
                    return RedirectToAction(nameof(Device), new { id = model.DeviceId.Value });
                }

                return RedirectToAction(nameof(Command), new { id = createdCommands[0].CommandId });
            }

            TempData["GroupMessage"] = $"Created {createdCommands.Count} device command(s).";
            return RedirectToAction(nameof(Commands));
        }

        [HttpGet]
        public async Task<IActionResult> RegisterInventory(
            string? search,
            string? storeFilter,
            string? stateFilter,
            string? manufacturerFilter,
            string? modelFilter,
            string? releaseLevelFilter,
            string? releaseAppliedFilter,
            string? scannerNameFilter,
            string? verifoneModelFilter,
            string? statusFilter,
            string sortBy = "ComputerName",
            string sortDir = "asc")
        {
            var rows = await BuildRegisterInventoryQuery(
                    search,
                    storeFilter,
                    stateFilter,
                    manufacturerFilter,
                    modelFilter,
                    releaseLevelFilter,
                    releaseAppliedFilter,
                    scannerNameFilter,
                    verifoneModelFilter,
                    statusFilter,
                    sortBy,
                    sortDir)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var cutoff = now.AddMinutes(-5);
            var since24 = now.AddHours(-24);

            var devices = await _db.Devices.ToDictionaryAsync(d => d.DeviceId);

            var failedDeviceIds = await _db.CommandResults
                .Where(r => r.Status == "Failed" && r.FinishedUtc >= since24)
                .Select(r => r.DeviceId)
                .Distinct()
                .ToListAsync();

            var failedSet = failedDeviceIds.ToHashSet();

            foreach (var row in rows)
            {
                if (!devices.TryGetValue(row.DeviceId, out var device))
                    continue;

                var score = 0;

                var heartbeatHealthy = device.LastSeenUtc >= cutoff;
                if (heartbeatHealthy) score += 40;

                var diskHealthy = IsDiskHealthy(row.HardDriveFreeSpace, row.HardDriveSize);
                if (diskHealthy) score += 25;

                var memoryHealthy = IsMemoryHealthy(row.Memory);
                if (memoryHealthy) score += 15;

                var failuresHealthy = !failedSet.Contains(device.DeviceId);
                if (failuresHealthy) score += 10;

                var inventoryFresh = row.LastHeartbeatUtc != null && row.LastHeartbeatUtc >= now.AddDays(-1);
                if (inventoryFresh) score += 10;

                row.HealthScore = score;
                row.HealthStatus = score >= 90 ? "Healthy"
                                 : score >= 70 ? "Warning"
                                 : "Critical";

                row.HeartbeatHealthy = heartbeatHealthy;
                row.DiskHealthy = diskHealthy;
                row.MemoryHealthy = memoryHealthy;
                row.FailuresHealthy = failuresHealthy;
                row.InventoryFresh = inventoryFresh;
            }

            ApplyFriendlyOsNames(rows);

            var model = new RegisterInventoryPageViewModel
            {
                Search = search,
                StoreFilter = storeFilter,
                StateFilter = stateFilter,
                ManufacturerFilter = manufacturerFilter,
                ModelFilter = modelFilter,
                ReleaseLevelFilter = releaseLevelFilter,
                ReleaseAppliedFilter = releaseAppliedFilter,
                ScannerNameFilter = scannerNameFilter,
                VerifoneModelFilter = verifoneModelFilter,
                StatusFilter = statusFilter,
                SortBy = sortBy,
                SortDir = sortDir,

                AvailableStores = await _db.RegisterInventories
                    .Where(x => x.Store != null && x.Store != "")
                    .Select(x => x.Store!)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToListAsync(),

                AvailableStates = await _db.RegisterInventories
                    .Where(x => x.StoreState != null && x.StoreState != "")
                    .Select(x => x.StoreState!)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToListAsync(),

                Rows = rows
            };

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> RegisterInventoryCsv(
            string? search,
            string? storeFilter,
            string? stateFilter,
            string? manufacturerFilter,
            string? modelFilter,
            string? releaseLevelFilter,
            string? releaseAppliedFilter,
            string? scannerNameFilter,
            string? verifoneModelFilter,
            string? statusFilter,
            string sortBy = "ComputerName",
            string sortDir = "asc")
        {
            var rows = await BuildRegisterInventoryQuery(
                    search,
                    storeFilter,
                    stateFilter,
                    manufacturerFilter,
                    modelFilter,
                    releaseLevelFilter,
                    releaseAppliedFilter,
                    scannerNameFilter,
                    verifoneModelFilter,
                    statusFilter,
                    sortBy,
                    sortDir)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var cutoff = now.AddMinutes(-5);
            var since24 = now.AddHours(-24);

            var devices = await _db.Devices.ToDictionaryAsync(d => d.DeviceId);

            var failedDeviceIds = await _db.CommandResults
                .Where(r => r.Status == "Failed" && r.FinishedUtc >= since24)
                .Select(r => r.DeviceId)
                .Distinct()
                .ToListAsync();

            var failedSet = failedDeviceIds.ToHashSet();

            foreach (var row in rows)
            {
                if (!devices.TryGetValue(row.DeviceId, out var device))
                    continue;

                var score = 0;

                var heartbeatHealthy = device.LastSeenUtc >= cutoff;
                if (heartbeatHealthy) score += 40;

                var diskHealthy = IsDiskHealthy(row.HardDriveFreeSpace, row.HardDriveSize);
                if (diskHealthy) score += 25;

                var memoryHealthy = IsMemoryHealthy(row.Memory);
                if (memoryHealthy) score += 15;

                var failuresHealthy = !failedSet.Contains(device.DeviceId);
                if (failuresHealthy) score += 10;

                var inventoryFresh = row.LastHeartbeatUtc != null && row.LastHeartbeatUtc >= now.AddDays(-1);
                if (inventoryFresh) score += 10;

                row.HealthScore = score;
                row.HealthStatus = score >= 90 ? "Healthy"
                                   : score >= 70 ? "Warning"
                                   : "Critical";

                row.HeartbeatHealthy = heartbeatHealthy;
                row.DiskHealthy = diskHealthy;
                row.MemoryHealthy = memoryHealthy;
                row.FailuresHealthy = failuresHealthy;
                row.InventoryFresh = inventoryFresh;
            }

            ApplyFriendlyOsNames(rows);

            var sb = new StringBuilder();

            sb.AppendLine(string.Join(",",
                Csv("ComputerName"),
                Csv("Store"),
                Csv("RegisterNumber"),
                Csv("IPAddress"),
                Csv("MACAddress"),
                Csv("Manufacturer"),
                Csv("Model"),
                Csv("SerialNumber"),
                Csv("Memory"),
                Csv("HardDriveSize"),
                Csv("HardDriveFreeSpace"),
                Csv("StoreName"),
                Csv("StoreAddress"),
                Csv("StoreCity"),
                Csv("StoreState"),
                Csv("StoreZipCode"),
                Csv("ReleaseLevel"),
                Csv("ReleaseApplied"),
                Csv("Domain"),
                Csv("LastReboot"),
                Csv("SystemBuildDate"),
                Csv("OSVersion"),
                Csv("CPUArch"),
                Csv("VerifoneModel"),
                Csv("VerifoneIP"),
                Csv("ScannerName"),
                Csv("ScannerSerialNumber"),
                Csv("LastHeartbeatUtc")
            ));

            foreach (var r in rows)
            {
                sb.AppendLine(string.Join(",",
                    Csv(r.ComputerName),
                    Csv(r.Store),
                    Csv(r.RegisterNumber),
                    Csv(r.IPAddress),
                    Csv(r.MACAddress),
                    Csv(r.Manufacturer),
                    Csv(r.Model),
                    Csv(r.SerialNumber),
                    Csv(r.Memory),
                    Csv(r.HardDriveSize),
                    Csv(r.HardDriveFreeSpace),
                    Csv(r.StoreName),
                    Csv(r.StoreAddress),
                    Csv(r.StoreCity),
                    Csv(r.StoreState),
                    Csv(r.StoreZipCode),
                    Csv(r.ReleaseLevel),
                    Csv(r.ReleaseApplied),
                    Csv(r.Domain),
                    Csv(r.LastReboot?.ToString("yyyy-MM-dd HH:mm:ss")),
                    Csv(r.SystemBuildDate?.ToString("yyyy-MM-dd HH:mm:ss")),
                    Csv(r.OSVersion),
                    Csv(r.CPUArch),
                    Csv(r.VerifoneModel),
                    Csv(r.VerifoneIP),
                    Csv(r.ScannerName),
                    Csv(r.ScannerSerialNumber),
                    Csv(r.LastHeartbeatUtc?.ToString("yyyy-MM-dd HH:mm:ss"))
                ));
            }

            var fileName = $"RegisterInventory_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", fileName);
        }

        private IQueryable<RegisterInventoryRowViewModel> BuildRegisterInventoryQuery(
            string? search,
            string? storeFilter,
            string? stateFilter,
            string? manufacturerFilter,
            string? modelFilter,
            string? releaseLevelFilter,
            string? releaseAppliedFilter,
            string? scannerNameFilter,
            string? verifoneModelFilter,
            string? statusFilter,
            string sortBy,
            string sortDir)
        {
            var query = _db.RegisterInventories.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();

                query = query.Where(x =>
                    (x.ComputerName ?? "").Contains(s) ||
                    (x.Store ?? "").Contains(s) ||
                    (x.RegisterNumber ?? "").Contains(s) ||
                    (x.IPAddress ?? "").Contains(s) ||
                    (x.MACAddress ?? "").Contains(s) ||
                    (x.Manufacturer ?? "").Contains(s) ||
                    (x.Model ?? "").Contains(s) ||
                    (x.SerialNumber ?? "").Contains(s) ||
                    (x.StoreName ?? "").Contains(s) ||
                    (x.StoreAddress ?? "").Contains(s) ||
                    (x.StoreCity ?? "").Contains(s) ||
                    (x.StoreState ?? "").Contains(s) ||
                    (x.StoreZipCode ?? "").Contains(s) ||
                    (x.ReleaseLevel ?? "").Contains(s) ||
                    (x.ReleaseApplied ?? "").Contains(s) ||
                    (x.Domain ?? "").Contains(s) ||
                    (x.OSVersion ?? "").Contains(s) ||
                    (x.CPUArch ?? "").Contains(s) ||
                    (x.VerifoneModel ?? "").Contains(s) ||
                    (x.VerifoneIP ?? "").Contains(s) ||
                    (x.ScannerName ?? "").Contains(s) ||
                    (x.ScannerSerialNumber ?? "").Contains(s));
            }

            if (!string.IsNullOrWhiteSpace(storeFilter))
                query = query.Where(x => x.Store == storeFilter);

            if (!string.IsNullOrWhiteSpace(stateFilter))
                query = query.Where(x => x.StoreState == stateFilter);

            if (!string.IsNullOrWhiteSpace(manufacturerFilter))
                query = query.Where(x => (x.Manufacturer ?? "").Contains(manufacturerFilter));

            if (!string.IsNullOrWhiteSpace(modelFilter))
                query = query.Where(x => (x.Model ?? "").Contains(modelFilter));

            if (!string.IsNullOrWhiteSpace(releaseLevelFilter))
                query = query.Where(x => (x.ReleaseLevel ?? "").Contains(releaseLevelFilter));

            if (!string.IsNullOrWhiteSpace(releaseAppliedFilter))
                query = query.Where(x => (x.ReleaseApplied ?? "").Contains(releaseAppliedFilter));

            if (!string.IsNullOrWhiteSpace(scannerNameFilter))
                query = query.Where(x => (x.ScannerName ?? "").Contains(scannerNameFilter));

            if (!string.IsNullOrWhiteSpace(verifoneModelFilter))
                query = query.Where(x => (x.VerifoneModel ?? "").Contains(verifoneModelFilter));

            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-5);

                if (string.Equals(statusFilter, "Online", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(x => x.LastHeartbeatUtc.HasValue && x.LastHeartbeatUtc.Value >= cutoff);

                if (string.Equals(statusFilter, "Offline", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(x => !x.LastHeartbeatUtc.HasValue || x.LastHeartbeatUtc.Value < cutoff);
            }

            var projected = query.Select(x => new RegisterInventoryRowViewModel
            {
                DeviceId = x.DeviceId,
                ComputerName = x.ComputerName,
                Store = x.Store,
                RegisterNumber = x.RegisterNumber,
                IPAddress = x.IPAddress,
                MACAddress = x.MACAddress,
                Manufacturer = x.Manufacturer,
                Model = x.Model,
                SerialNumber = x.SerialNumber,
                Memory = x.Memory,
                HardDriveSize = x.HardDriveSize,
                HardDriveFreeSpace = x.HardDriveFreeSpace,
                StoreName = x.StoreName,
                StoreAddress = x.StoreAddress,
                StoreCity = x.StoreCity,
                StoreState = x.StoreState,
                StoreZipCode = x.StoreZipCode,
                ReleaseLevel = x.ReleaseLevel,
                ReleaseApplied = x.ReleaseApplied,
                Domain = x.Domain,
                LastReboot = x.LastReboot,
                SystemBuildDate = x.SystemBuildDate,
                OSVersion = x.OSVersion,
                CPUArch = x.CPUArch,
                VerifoneModel = x.VerifoneModel,
                VerifoneIP = x.VerifoneIP,
                ScannerName = x.ScannerName,
                ScannerSerialNumber = x.ScannerSerialNumber,
                LastHeartbeatUtc = x.LastHeartbeatUtc
            });

            var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

            return (sortBy ?? "ComputerName") switch
            {
                "Store" => desc ? projected.OrderByDescending(x => x.Store) : projected.OrderBy(x => x.Store),
                "RegisterNumber" => desc ? projected.OrderByDescending(x => x.RegisterNumber) : projected.OrderBy(x => x.RegisterNumber),
                "IPAddress" => desc ? projected.OrderByDescending(x => x.IPAddress) : projected.OrderBy(x => x.IPAddress),
                "MACAddress" => desc ? projected.OrderByDescending(x => x.MACAddress) : projected.OrderBy(x => x.MACAddress),
                "Manufacturer" => desc ? projected.OrderByDescending(x => x.Manufacturer) : projected.OrderBy(x => x.Manufacturer),
                "Model" => desc ? projected.OrderByDescending(x => x.Model) : projected.OrderBy(x => x.Model),
                "SerialNumber" => desc ? projected.OrderByDescending(x => x.SerialNumber) : projected.OrderBy(x => x.SerialNumber),
                "Memory" => desc ? projected.OrderByDescending(x => x.Memory) : projected.OrderBy(x => x.Memory),
                "HardDriveSize" => desc ? projected.OrderByDescending(x => x.HardDriveSize) : projected.OrderBy(x => x.HardDriveSize),
                "HardDriveFreeSpace" => desc ? projected.OrderByDescending(x => x.HardDriveFreeSpace) : projected.OrderBy(x => x.HardDriveFreeSpace),
                "StoreName" => desc ? projected.OrderByDescending(x => x.StoreName) : projected.OrderBy(x => x.StoreName),
                "StoreAddress" => desc ? projected.OrderByDescending(x => x.StoreAddress) : projected.OrderBy(x => x.StoreAddress),
                "StoreCity" => desc ? projected.OrderByDescending(x => x.StoreCity) : projected.OrderBy(x => x.StoreCity),
                "StoreState" => desc ? projected.OrderByDescending(x => x.StoreState) : projected.OrderBy(x => x.StoreState),
                "StoreZipCode" => desc ? projected.OrderByDescending(x => x.StoreZipCode) : projected.OrderBy(x => x.StoreZipCode),
                "ReleaseLevel" => desc ? projected.OrderByDescending(x => x.ReleaseLevel) : projected.OrderBy(x => x.ReleaseLevel),
                "ReleaseApplied" => desc ? projected.OrderByDescending(x => x.ReleaseApplied) : projected.OrderBy(x => x.ReleaseApplied),
                "Domain" => desc ? projected.OrderByDescending(x => x.Domain) : projected.OrderBy(x => x.Domain),
                "LastReboot" => desc ? projected.OrderByDescending(x => x.LastReboot) : projected.OrderBy(x => x.LastReboot),
                "SystemBuildDate" => desc ? projected.OrderByDescending(x => x.SystemBuildDate) : projected.OrderBy(x => x.SystemBuildDate),
                "OSVersion" => desc ? projected.OrderByDescending(x => x.OSVersion) : projected.OrderBy(x => x.OSVersion),
                "CPUArch" => desc ? projected.OrderByDescending(x => x.CPUArch) : projected.OrderBy(x => x.CPUArch),
                "VerifoneModel" => desc ? projected.OrderByDescending(x => x.VerifoneModel) : projected.OrderBy(x => x.VerifoneModel),
                "VerifoneIP" => desc ? projected.OrderByDescending(x => x.VerifoneIP) : projected.OrderBy(x => x.VerifoneIP),
                "ScannerName" => desc ? projected.OrderByDescending(x => x.ScannerName) : projected.OrderBy(x => x.ScannerName),
                "ScannerSerialNumber" => desc ? projected.OrderByDescending(x => x.ScannerSerialNumber) : projected.OrderBy(x => x.ScannerSerialNumber),
                "LastHeartbeatUtc" => desc ? projected.OrderByDescending(x => x.LastHeartbeatUtc) : projected.OrderBy(x => x.LastHeartbeatUtc),
                _ => desc ? projected.OrderByDescending(x => x.ComputerName) : projected.OrderBy(x => x.ComputerName)
            };
        }

        private static string Csv(string? value)
        {
            value ??= "";
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private async Task PopulateHelpdeskCommandLists(HelpdeskCommandsViewModel model, DateTime cutoff)
        {
            model.AvailableDevices = await _db.Devices
                .Where(d => d.IsEnabled)
                .OrderBy(d => d.StoreNumber)
                .ThenBy(d => d.Hostname)
                .Select(d => new DeviceSummaryViewModel
                {
                    DeviceId = d.DeviceId,
                    StoreNumber = d.StoreNumber,
                    Hostname = d.Hostname,
                    AgentVersion = d.AgentVersion,
                    LastSeenUtc = d.LastSeenUtc,
                    IsOnline = d.LastSeenUtc >= cutoff
                })
                .ToListAsync();

            model.AvailableStores = await _db.Devices
                .Where(d => d.IsEnabled)
                .Select(d => d.StoreNumber)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync();

            model.AvailableGroups = await _db.DeviceGroups
                .Select(g => g.GroupName)
                .OrderBy(g => g)
                .ToListAsync();

            model.AvailableCommands = GetHelpdeskCommands();
        }

        private Command BuildHelpdeskCommandForDevice(Device device, string commandKey, DateTime issuedUtc, string? groupName = null)
        {
            var mapped = MapHelpdeskCommand(commandKey);

            return new Command
            {
                CommandId = Guid.NewGuid(),
                Type = mapped.Type,
                Scope = "Device",
                PayloadJson = mapped.PayloadJson,
                Status = "Pending",
                Priority = 100,
                CreatedUtc = issuedUtc,
                ExpiresUtc = null,
                DeviceId = device.DeviceId,
                StoreNumber = device.StoreNumber,
                GroupName = groupName,
                AttemptCount = 0,
                MaxAttempts = 3,
                IssuedBy = CurrentActor(),
                IssuedUtc = issuedUtc
            };
        }

        private static (string Type, string? PayloadJson) MapHelpdeskCommand(string commandKey)
        {
            return commandKey switch
            {
                "CollectSystemInfo" => ("CollectSystemInfo", "{}"),
                "NetworkInfo" => ("RunProcess", BuildRunProcessPayload("cmd.exe", "/c ipconfig /all", 20)),
                "RestartPOS" => ("RunProcess", BuildRunProcessPayload("cmd.exe", "/c POSRestart.cmd", 60)),
                "RestartRetailShell" => ("RunProcess", BuildRunProcessPayload("cmd.exe", "/c RetailShellRestart.cmd", 60)),
                "RestartAgent" => ("RunProcess", BuildRunProcessPayload("cmd.exe", "/c AgentRestart.cmd", 60)),
                "RebootDevice" => ("RunProcess", BuildRunProcessPayload("shutdown.exe", "/r /t 5 /f", 15)),
                _ => throw new InvalidOperationException($"Unknown helpdesk command key '{commandKey}'.")
            };
        }

        private static string BuildRunProcessPayload(string fileName, string arguments, int timeoutSeconds)
        {
            var safeArguments = arguments.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"{{\n  \"fileName\": \"{fileName}\",\n  \"arguments\": \"{safeArguments}\",\n  \"timeoutSeconds\": {timeoutSeconds}\n}}";
        }

        private async Task PopulateIssueCommandLists(
            IssueCommandViewModel model,
            DateTime cutoff,
            List<string> commandTypes,
            List<CommandTemplateOptionViewModel> templates)
        {
            model.AvailableDevices = await _db.Devices
                .Where(d => d.IsEnabled)
                .OrderBy(d => d.StoreNumber)
                .ThenBy(d => d.Hostname)
                .Select(d => new DeviceSummaryViewModel
                {
                    DeviceId = d.DeviceId,
                    StoreNumber = d.StoreNumber,
                    Hostname = d.Hostname,
                    AgentVersion = d.AgentVersion,
                    LastSeenUtc = d.LastSeenUtc,
                    IsOnline = d.LastSeenUtc >= cutoff
                })
                .ToListAsync();

            model.AvailableStores = await _db.Devices
                .Where(d => d.IsEnabled)
                .Select(d => d.StoreNumber)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync();

            model.AvailableGroups = await _db.DeviceGroups
                .Select(g => g.GroupName)
                .OrderBy(g => g)
                .ToListAsync();

            model.AvailableCommandTypes = commandTypes;
            model.AvailableTemplates = templates;
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

        private static void PopulateCreatePackageLists(CreatePackageViewModel model)
        {
            model.PackageTypes = new List<SelectListItem>
            {
                new SelectListItem { Value = "1", Text = "MSI" },
                new SelectListItem { Value = "2", Text = "EXE" },
                new SelectListItem { Value = "3", Text = "PowerShell" },
                new SelectListItem { Value = "4", Text = "CMD" },
                new SelectListItem { Value = "5", Text = "BAT" },
                new SelectListItem { Value = "6", Text = "ZIP" },
                new SelectListItem { Value = "99", Text = "Other" }
            };

            model.RebootBehaviors = new List<SelectListItem>
            {
                new SelectListItem { Value = "0", Text = "None" },
                new SelectListItem { Value = "1", Text = "Allow" },
                new SelectListItem { Value = "2", Text = "Require" }
            };
        }

        private async Task PopulateCreateDeploymentLists(CreateDeploymentViewModel model, CancellationToken cancellationToken)
        {
            var packages = await _deploymentService.GetPackagesAsync(cancellationToken);

            model.Packages = packages
                .OrderBy(p => p.Name)
                .Select(p => new SelectListItem
                {
                    Value = p.Id.ToString(),
                    Text = string.IsNullOrWhiteSpace(p.Version)
                        ? $"{p.Name} ({p.FileName})"
                        : $"{p.Name} {p.Version} ({p.FileName})"
                })
                .ToList();

            model.TargetTypes = new List<SelectListItem>
            {
                new SelectListItem { Value = "1", Text = "Device" },
                new SelectListItem { Value = "2", Text = "Store" },
                new SelectListItem { Value = "3", Text = "Group" },
                new SelectListItem { Value = "4", Text = "Fleet" }
            };

            model.ExecuteModes = new List<SelectListItem>
            {
                new SelectListItem { Value = "1", Text = "Immediate" },
                new SelectListItem { Value = "2", Text = "Windowed" },
                new SelectListItem { Value = "3", Text = "Staged Only" }
            };
        }

        private async Task<IActionResult> SavePackageInternalAsync(CreatePackageViewModel model, int? id, CancellationToken cancellationToken)
        {
            if (model.TimeoutSeconds <= 0)
            {
                model.TimeoutSeconds = 1800;
            }

            var uploadedFileProvided = model.UploadFile != null && model.UploadFile.Length > 0;

            if (uploadedFileProvided)
            {
                var uploadValidationError = ValidateDistroUpload(model.UploadFile!);
                if (uploadValidationError != null)
                {
                    ModelState.AddModelError(nameof(model.UploadFile), uploadValidationError);
                }
            }

            if (uploadedFileProvided && string.IsNullOrWhiteSpace(model.FileName))
            {
                model.FileName = Path.GetFileName(model.UploadFile!.FileName);
            }

            if (!uploadedFileProvided && string.IsNullOrWhiteSpace(model.FileName))
            {
                ModelState.AddModelError(nameof(model.FileName), "File Name is required.");
            }

            if (!uploadedFileProvided && string.IsNullOrWhiteSpace(model.StoragePath))
            {
                ModelState.AddModelError(nameof(model.StoragePath), "Storage Path is required when no file is uploaded.");
            }

            if (!uploadedFileProvided && string.IsNullOrWhiteSpace(model.Sha256))
            {
                ModelState.AddModelError(nameof(model.Sha256), "SHA256 is required when no file is uploaded.");
            }

            if (!ModelState.IsValid)
            {
                PopulateCreatePackageLists(model);
                SetPackageFormViewData(
                    id.HasValue ? "Edit" : "Create",
                    id.HasValue ? nameof(EditPackage) : nameof(CreatePackage),
                    id.HasValue ? "Save Package Changes" : "Create Package",
                    id.HasValue ? $"Edit Package #{id.Value}" : "Create Package");

                var modelErrors = ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .SelectMany(x => x.Value!.Errors.Select(e => $"{x.Key}: {e.ErrorMessage}"))
                    .ToList();

                if (modelErrors.Count > 0)
                {
                    ViewData["DebugErrors"] = string.Join(" | ", modelErrors);
                }

                if (id.HasValue)
                    ViewData["PackageId"] = id.Value;

                return View("CreatePackage", model);
            }

            try
            {
                if (uploadedFileProvided)
                {
                    var distroResult = await SaveFileToDistroAsync(model.UploadFile!, cancellationToken);

                    model.FileName = distroResult.FileName;
                    model.StoragePath = BuildDistroUrl(distroResult.FileName);
                    model.FileSizeBytes = distroResult.FileSizeBytes;
                    model.Sha256 = distroResult.Sha256;
                }

                var request = new CreatePackageRequest
                {
                    Name = model.Name.Trim(),
                    Version = model.Version?.Trim(),
                    Description = model.Description?.Trim(),
                    PackageType = model.PackageType,
                    FileName = string.IsNullOrWhiteSpace(model.FileName) ? "" : model.FileName.Trim(),
                    StoragePath = string.IsNullOrWhiteSpace(model.StoragePath) ? "" : model.StoragePath.Trim(),
                    Sha256 = string.IsNullOrWhiteSpace(model.Sha256) ? "" : model.Sha256.Trim(),
                    FileSizeBytes = model.FileSizeBytes,
                    ExecutionCommand = string.IsNullOrWhiteSpace(model.ExecutionCommand) ? null : model.ExecutionCommand.Trim(),
                    ExecutionArguments = string.IsNullOrWhiteSpace(model.ExecutionArguments) ? null : model.ExecutionArguments.Trim(),
                    WorkingDirectory = string.IsNullOrWhiteSpace(model.WorkingDirectory) ? null : model.WorkingDirectory.Trim(),
                    TimeoutSeconds = model.TimeoutSeconds,
                    RebootBehavior = model.RebootBehavior
                };

                if (id.HasValue)
                {
                    var updated = await _deploymentService.UpdatePackageAsync(
                        id.Value,
                        request,
                        CurrentActor(),
                        cancellationToken);

                    await AuditAsync("EditPackage", "Package", updated.Id.ToString(), new { updated.Id, updated.Name, updated.Version, request.FileName, request.StoragePath }, true, cancellationToken: cancellationToken);
                    TempData["PackageMessage"] = $"Package '{updated.Name}' updated successfully.";
                }
                else
                {
                    var created = await _deploymentService.CreatePackageAsync(
                        request,
                        CurrentActor(),
                        cancellationToken);

                    await AuditAsync("CreatePackage", "Package", created.Id.ToString(), new { created.Id, created.Name, created.Version, request.FileName, request.StoragePath }, true, cancellationToken: cancellationToken);
                    TempData["PackageMessage"] = $"Package '{created.Name}' created successfully (Id {created.Id}).";
                }

                return RedirectToAction(nameof(Packages));
            }
            catch (Exception ex)
            {
                await AuditAsync(id.HasValue ? "EditPackage" : "CreatePackage", "Package", id?.ToString(), new { model.Name, model.Version, model.FileName, model.StoragePath }, false, ex.Message, cancellationToken);
                PopulateCreatePackageLists(model);
                SetPackageFormViewData(
                    id.HasValue ? "Edit" : "Create",
                    id.HasValue ? nameof(EditPackage) : nameof(CreatePackage),
                    id.HasValue ? "Save Package Changes" : "Create Package",
                    id.HasValue ? $"Edit Package #{id.Value}" : "Create Package");

                if (id.HasValue)
                    ViewData["PackageId"] = id.Value;

                ModelState.AddModelError("", $"{(id.HasValue ? "EditPackage" : "CreatePackage")} failed: {ex.Message}");
                ViewData["DebugErrors"] = ex.ToString();
                return View("CreatePackage", model);
            }
        }

        private async Task<IActionResult> SaveDeploymentInternalAsync(CreateDeploymentViewModel model, int? id, CancellationToken cancellationToken)
        {
            if (model.ExecuteMode == 2)
            {
                if (!model.WindowStartLocal.HasValue || !model.WindowEndLocal.HasValue)
                {
                    ModelState.AddModelError("", "Window start and end are required for Windowed deployments.");
                }
            }

            if (!ModelState.IsValid)
            {
                await PopulateCreateDeploymentLists(model, cancellationToken);
                SetDeploymentFormViewData(
                    id.HasValue ? "Edit" : "Create",
                    id.HasValue ? nameof(EditDeployment) : nameof(CreateDeployment),
                    id.HasValue ? "Save Deployment Changes" : "Create Deployment",
                    id.HasValue ? $"Edit Deployment #{id.Value}" : "Create Deployment");

                if (id.HasValue)
                    ViewData["DeploymentId"] = id.Value;

                return View("CreateDeployment", model);
            }

            try
            {
                var request = new CreateDeploymentRequest
                {
                    PackageId = model.PackageId,
                    TargetType = model.TargetType,
                    TargetValue = model.TargetValue.Trim(),
                    ExecuteMode = model.ExecuteMode,
                    WindowStartLocal = model.WindowStartLocal,
                    WindowEndLocal = model.WindowEndLocal,
                    UseStoreLocalTime = model.UseStoreLocalTime,
                    AllowOutsideWindow = model.AllowOutsideWindow,
                    RetryCount = model.RetryCount,
                    Notes = model.Notes?.Trim()
                };

                if (id.HasValue)
                {
                    var updated = await _deploymentService.UpdateDeploymentAsync(
                        id.Value,
                        request,
                        CurrentActor(),
                        cancellationToken);

                    await AuditAsync("EditDeployment", "Deployment", updated.Id.ToString(), new { updated.Id, request.PackageId, request.TargetType, request.TargetValue, request.ExecuteMode }, true, cancellationToken: cancellationToken);
                    TempData["DeploymentMessage"] = $"Deployment {updated.Id} updated successfully.";
                    return RedirectToAction(nameof(DeploymentDetail), new { id = updated.Id });
                }
                else
                {
                    var created = await _deploymentService.CreateDeploymentAsync(
                        request,
                        CurrentActor(),
                        cancellationToken);

                    await AuditAsync("CreateDeployment", "Deployment", created.Id.ToString(), new { created.Id, request.PackageId, request.TargetType, request.TargetValue, request.ExecuteMode }, true, cancellationToken: cancellationToken);
                    TempData["DeploymentMessage"] = $"Deployment {created.Id} created successfully.";
                    return RedirectToAction(nameof(DeploymentDetail), new { id = created.Id });
                }
            }
            catch (Exception ex)
            {
                await AuditAsync(id.HasValue ? "EditDeployment" : "CreateDeployment", "Deployment", id?.ToString(), new { model.PackageId, model.TargetType, model.TargetValue, model.ExecuteMode }, false, ex.Message, cancellationToken);
                await PopulateCreateDeploymentLists(model, cancellationToken);
                SetDeploymentFormViewData(
                    id.HasValue ? "Edit" : "Create",
                    id.HasValue ? nameof(EditDeployment) : nameof(CreateDeployment),
                    id.HasValue ? "Save Deployment Changes" : "Create Deployment",
                    id.HasValue ? $"Edit Deployment #{id.Value}" : "Create Deployment");

                if (id.HasValue)
                    ViewData["DeploymentId"] = id.Value;

                ModelState.AddModelError("", $"{(id.HasValue ? "EditDeployment" : "CreateDeployment")} failed: {ex.Message}");
                return View("CreateDeployment", model);
            }
        }

        private void SetPackageFormViewData(string formMode, string formAction, string submitText, string title)
        {
            ViewData["FormMode"] = formMode;
            ViewData["FormAction"] = formAction;
            ViewData["SubmitText"] = submitText;
            ViewData["Title"] = title;
        }

        private void SetDeploymentFormViewData(string formMode, string formAction, string submitText, string title)
        {
            ViewData["FormMode"] = formMode;
            ViewData["FormAction"] = formAction;
            ViewData["SubmitText"] = submitText;
            ViewData["Title"] = title;
        }

        [Authorize(Policy = "DashboardHelpdesk")]
        [HttpGet]
        public async Task<IActionResult> DownloadShadow(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
                return BadRequest("An IP address or hostname is required.");

            var safeTarget = target.Trim();

            var script = new StringBuilder();
            script.AppendLine("@echo off");
            script.AppendLine("setlocal");
            script.AppendLine($"echo Launching Remote Desktop Shadow Session to {safeTarget}...");
            script.AppendLine($"mstsc /shadow:1 /v:{safeTarget} /noConsentPrompt /control");
            script.AppendLine("endlocal");

            var fileNameSafe = new string(safeTarget
                .Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_')
                .ToArray());

            if (string.IsNullOrWhiteSpace(fileNameSafe))
                fileNameSafe = "ShadowSession";

            await AuditAsync("DownloadShadow", "DeviceTarget", safeTarget, new { Target = safeTarget });

            return File(
                Encoding.UTF8.GetBytes(script.ToString()),
                "application/octet-stream",
                $"ShadowSession_{fileNameSafe}.cmd");
        }

        private string? ValidateDistroUpload(IFormFile file)
        {
            var extension = Path.GetExtension(file.FileName);
            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".msi",
                ".exe",
                ".ps1",
                ".cmd",
                ".bat",
                ".zip"
            };

            if (string.IsNullOrWhiteSpace(extension) || !allowedExtensions.Contains(extension))
            {
                return "Only .msi, .exe, .ps1, .cmd, .bat, and .zip files are supported for distro uploads.";
            }

            return null;
        }

        private async Task<(string FileName, long FileSizeBytes, string Sha256)> SaveFileToDistroAsync(IFormFile file, CancellationToken cancellationToken)
        {
            var distroRoot = Path.Combine(_environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "distro");
            Directory.CreateDirectory(distroRoot);

            var originalName = Path.GetFileName(file.FileName);
            var safeFileName = SanitizeFileName(originalName);
            if (string.IsNullOrWhiteSpace(safeFileName))
            {
                throw new InvalidOperationException("Uploaded file name is invalid.");
            }

            var destinationPath = Path.Combine(distroRoot, safeFileName);

            await using (var stream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            var sha256 = await ComputeSha256Async(destinationPath, cancellationToken);
            var fileInfo = new FileInfo(destinationPath);

            return (safeFileName, fileInfo.Length, sha256);
        }

        private string BuildDistroUrl(string fileName)
        {
            var request = HttpContext.Request;
            return $"{request.Scheme}://{request.Host}/distro/{Uri.EscapeDataString(fileName)}";
        }

        private static string SanitizeFileName(string fileName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(fileName.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return cleaned.Replace(" ", "_");
        }

        private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(stream, cancellationToken);
            return Convert.ToHexString(hash);
        }

        private static bool IsDistroPath(string? storagePath)
        {
            if (string.IsNullOrWhiteSpace(storagePath))
                return false;

            return storagePath.Contains("/distro/", StringComparison.OrdinalIgnoreCase)
                   || storagePath.Contains("\\distro\\", StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            if (line == null)
                return result;

            var current = new StringBuilder();
            var inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            result.Add(current.ToString());
            return result;
        }
    }
}