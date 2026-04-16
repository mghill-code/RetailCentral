using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RetailCentral.Api.Configuration;
using RetailCentral.Api.Dtos;
using RetailCentral.Api.Data;
using RetailCentral.Api.Data.Enums;
using RetailCentral.Api.Models;
using RetailCentral.Api.Models.Dashboard.Deployments;
using RetailCentral.Api.Models.Deployments;
using RetailCentral.Api.Services;
using RetailCentral.Api.Services.Deployments;
using RetailCentral.Api.ViewModels;
using RetailCentral.Api.ViewModels.Deployments;
using RetailCentral.Api.ViewModels.Orchestration;
using RetailCentral.Api.Data.Entities.Orchestration;
using System.Security.Cryptography;
using System.Text;

namespace RetailCentral.Api.Controllers
{
    [Authorize(Policy = "DashboardViewer")]
    public class DashboardController : Controller
    {
        // Core data access for dashboard queries and command creation.
        private readonly RetailCentralDbContext _db;

        // Deployment subsystem service.
        private readonly IDeploymentService _deploymentService;

        // Used for package/distro file handling.
        private readonly IWebHostEnvironment _environment;

        // Central audit logger for dashboard actions.
        private readonly AuditService _auditService;

        // General configuration for About page and other simple reads.
        private readonly IConfiguration _config;

        // Shared server-side command validation service.
        // This keeps dashboard command creation aligned with Admin API rules.
        private readonly CommandValidationService _commandValidationService;

        // Bound command policy options used to shape dropdowns and helpdesk offerings.
        private readonly CommandPolicyOptions _commandPolicy;

        public DashboardController(
             RetailCentralDbContext db,
             IDeploymentService deploymentService,
             IWebHostEnvironment environment,
             AuditService auditService,
             IConfiguration config,
             CommandValidationService commandValidationService,
             IOptions<CommandPolicyOptions> commandPolicy)
        {
            _db = db;
            _deploymentService = deploymentService;
            _environment = environment;
            _auditService = auditService;
            _config = config;
            _commandValidationService = commandValidationService;
            _commandPolicy = commandPolicy.Value;
        }

        public enum LogsReportType
        {
            Reactivated,
            Inactive,
            New
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

        /// <summary>
        /// Returns the command types allowed for the engineer-facing Issue Command page.
        /// This is driven by server policy, not hardcoded shell-oriented behavior.
        /// </summary>
        private List<string> GetAvailableCommandTypes()
        {
            return _commandPolicy.AllowedCommandTypes
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
        }

        /// <summary>
        /// Returns safer canned templates for the engineer-facing Issue Command page.
        /// These templates are filtered by the configured command policy.
        /// </summary>
        private List<CommandTemplateOptionViewModel> GetCommandTemplates()
        {
            var templates = new List<CommandTemplateOptionViewModel>
                {
                    new CommandTemplateOptionViewModel
                    {
                        Name = "Echo - Hello",
                        CommandType = "Echo",
                        PayloadJson = """
            {
              "message": "Hello from dashboard"
            }
            """,
                        Description = "Simple test message"
                    },
                    new CommandTemplateOptionViewModel
                    {
                        Name = "Collect System Info",
                        CommandType = "CollectSystemInfo",
                        PayloadJson = """
            {}
            """,
                        Description = "Collect machine details"
                    },
                    new CommandTemplateOptionViewModel
                    {
                        Name = "Collect Process Status",
                        CommandType = "CollectProcessStatus",
                        PayloadJson = """
            {}
            """,
                        Description = "Collect POS / shell / agent process state"
                    },
                    new CommandTemplateOptionViewModel
                    {
                        Name = "Collect Software Inventory",
                        CommandType = "CollectSoftwareInventory",
                        PayloadJson = """
            {}
            """,
                        Description = "Collect installed software and Windows updates"
                    },
                    new CommandTemplateOptionViewModel
                    {
                        Name = "Restart POS",
                        CommandType = "RestartPOS",
                        PayloadJson = """
            {}
            """,
                        Description = "Restart the POS application using the approved named command"
                    },
                    new CommandTemplateOptionViewModel
                    {
                        Name = "Restart RetailShell",
                        CommandType = "RestartRetailShell",
                        PayloadJson = """
            {}
            """,
                        Description = "Restart RetailShell using the approved named command"
                    },
                    new CommandTemplateOptionViewModel
                    {
                        Name = "Restart Agent",
                        CommandType = "RestartAgent",
                        PayloadJson = """
            {}
            """,
                        Description = "Restart the agent using the approved named command"
                    },
                    new CommandTemplateOptionViewModel
                    {
                        Name = "Reboot Device",
                        CommandType = "RebootDevice",
                        PayloadJson = """
            {}
            """,
                        Description = "Reboot the endpoint using the approved named command"
                    },
                    new CommandTemplateOptionViewModel
                    {
                        Name = "DownloadFile - download only",
                        CommandType = "DownloadFile",
                        PayloadJson = """
            {
              "url": "https://artifacts.company.com/example/hello.txt",
              "destinationFileName": "hello.txt",
              "execute": false
            }
            """,
                        Description = "Download a file without executing it"
                    },
                    new CommandTemplateOptionViewModel
                    {
                        Name = "DownloadFile - download and execute",
                        CommandType = "DownloadFile",
                        PayloadJson = """
            {
              "url": "https://artifacts.company.com/example/runme.cmd",
              "destinationFileName": "runme.cmd",
              "execute": true,
              "sha256": "REPLACE_WITH_SHA256",
              "arguments": ""
            }
            """,
                        Description = "Download and execute a file with hash validation"
                    },
                    new CommandTemplateOptionViewModel
                    {
                        Name = "Install Package - MSI profile",
                        CommandType = "InstallPackage",
                        PayloadJson = """
            {
              "packageId": 0,
              "downloadUrl": "https://artifacts.company.com/example/app.msi",
              "fileName": "app.msi",
              "sha256": "REPLACE_WITH_SHA256",
              "installCommand": "MsiexecInstall",
              "installArguments": "/i {file} /qn /norestart",
              "timeoutSeconds": 600,
              "executeMode": "Immediate"
            }
            """,
                        Description = "Install a package using an approved install profile"
                    }
                };

                        if (_commandPolicy.AllowRunProcess)
                        {
                            templates.Add(new CommandTemplateOptionViewModel
                            {
                                Name = "RunProcess - Advanced (policy enabled)",
                                CommandType = "RunProcess",
                                PayloadJson = """
            {
              "fileName": "C:\\Approved\\Tool.exe",
              "arguments": "",
              "timeoutSeconds": 30
            }
            """,
                                Description = "Advanced process execution template. Use carefully."
                            });
                        }

                        var allowedSet = _commandPolicy.AllowedCommandTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);

                        return templates
                            .Where(t => allowedSet.Contains(t.CommandType))
                            .ToList();
                    }

        /// <summary>
        /// Returns the safe helpdesk command catalog.
        /// These entries represent business-friendly actions, but they now map to
        /// named commands rather than raw shell/process payloads.
        /// </summary>
        private List<HelpdeskCommandOptionViewModel> GetHelpdeskCommands()
        {
            var commands = new List<HelpdeskCommandOptionViewModel>
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
            Key = "CollectProcessStatus",
            Name = "Collect Process Status",
            Description = "Collect POS, RetailShell, and Agent process health and runtime data.",
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
            Description = "Restart the POS application using the approved named command.",
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
            Description = "Restart RetailShell using the approved named command.",
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
            Description = "Restart the agent service using the approved named command.",
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
            Description = "Reboot the endpoint using the approved named command.",
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

            var allowedSet = _commandPolicy.HelpdeskAllowedCommandTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);

            return commands
                .Where(c => c.Key == "RequeueFailed" || allowedSet.Contains(c.Key))
                .ToList();
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
            var orchestrationRunsActive = await _db.OrchestrationRuns.CountAsync(x =>
                x.Status == OrchestrationRunStatus.Pending ||
                x.Status == OrchestrationRunStatus.Running ||
                x.Status == OrchestrationRunStatus.WaitingForRetry);

            var orchestrationRunsFailedLast24Hours = await _db.OrchestrationRuns.CountAsync(x =>
                (x.Status == OrchestrationRunStatus.Failed || x.Status == OrchestrationRunStatus.Cancelled) &&
                x.StartedUtc >= since24);

            var orchestrationTemplatesActive = await _db.OrchestrationTemplates.CountAsync(x => x.IsActive);

            var orchestrationRunsWaitingForRetry = await _db.OrchestrationRuns.CountAsync(x =>
                x.Status == OrchestrationRunStatus.WaitingForRetry);

            var completedOrchestrationRunsLast24Hours = await _db.OrchestrationRuns.CountAsync(x =>
                x.Status == OrchestrationRunStatus.Completed &&
                x.StartedUtc >= since24);

            var activeProfilesUsingInactiveTemplates = await _db.ProvisioningProfiles
                .CountAsync(x => x.IsActive && !x.Template.IsActive);

            var activeTemplatesWithoutProfiles = await _db.OrchestrationTemplates
                .CountAsync(x => x.IsActive && !x.ProvisioningProfiles.Any(p => p.IsActive));

            var provisioningProfilesActive = await _db.ProvisioningProfiles.CountAsync(x => x.IsActive);
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
            var recentOrchestrationRuns = await _db.OrchestrationRuns
                .AsNoTracking()
                .Include(x => x.Template)
                .OrderByDescending(x => x.StartedUtc)
                .Take(10)
                .Select(x => new OrchestrationHomepageRunViewModel
                {
                    Id = x.Id,
                    TemplateName = x.Template != null ? (x.Template.Name ?? $"Template {x.TemplateId}") : $"Template {x.TemplateId}",
                    TemplateVersion = x.Template != null ? x.Template.Version : 0,
                    DeviceId = x.DeviceId,
                    Status = x.Status.ToString(),
                    CurrentStepOrder = x.CurrentStepOrder,
                    RequestedBy = x.RequestedBy ?? string.Empty,
                    StartedUtc = x.StartedUtc,
                    CompletedUtc = x.CompletedUtc
                })
                .ToListAsync();

            var model = new DashboardIndexViewModel
            {
                TotalDevices = totalDevices,
                OnlineDevices = onlineDevices,
                OfflineDevices = offlineDevices,
                CommandsPending = pendingCount,
                CommandsInProgress = inProgressCount,
                CommandsFailedLast24Hours = failed24Count,
                CommandsSucceededLast24Hours = succeeded24Count,
                OrchestrationRunsActive = orchestrationRunsActive,
                OrchestrationRunsFailedLast24Hours = orchestrationRunsFailedLast24Hours,
                OrchestrationTemplatesActive = orchestrationTemplatesActive,
                RecentOrchestrationRuns = recentOrchestrationRuns,
                ProvisioningProfilesActive = provisioningProfilesActive,
                OrchestrationRunsWaitingForRetry = orchestrationRunsWaitingForRetry,
                ActiveProfilesUsingInactiveTemplates = activeProfilesUsingInactiveTemplates,
                ActiveTemplatesWithoutProfiles = activeTemplatesWithoutProfiles,
                CompletedOrchestrationRunsLast24Hours = completedOrchestrationRunsLast24Hours,
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
        public async Task<IActionResult> LogsDashboard(string? report = "reactivated", CancellationToken ct = default)
        {
            var selectedReport = string.IsNullOrWhiteSpace(report)
                ? "reactivated"
                : report.Trim().ToLowerInvariant();

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
                SelectedReport = selectedReport,
                ReactivatedDevices = reactivated,
                Inactive30Days = inactive30,
                Inactive60Days = inactive60,
                Inactive90Days = inactive90,
                New30Days = new30,
                New60Days = new60,
                New90Days = new90
            };

            if (selectedReport == "inactive")
            {
                model.InactiveDevices = await _db.Devices
                    .Where(d => d.IsEnabled && d.LastSeenUtc < d30)
                    .OrderBy(d => d.LastSeenUtc)
                    .Take(200)
                    .Select(d => new LogsDashboardDeviceRowViewModel
                    {
                        DeviceId = d.DeviceId,
                        StoreNumber = d.StoreNumber,
                        Hostname = d.Hostname,
                        AgentVersion = d.AgentVersion,
                        FirstSeenUtc = d.FirstSeenUtc,
                        LastSeenUtc = d.LastSeenUtc,
                        IsEnabled = d.IsEnabled
                    })
                    .ToListAsync(ct);
            }
            else if (selectedReport == "new")
            {
                model.NewDevices = await _db.Devices
                    .Where(d => d.FirstSeenUtc >= d30)
                    .OrderByDescending(d => d.FirstSeenUtc)
                    .Take(200)
                    .Select(d => new LogsDashboardDeviceRowViewModel
                    {
                        DeviceId = d.DeviceId,
                        StoreNumber = d.StoreNumber,
                        Hostname = d.Hostname,
                        AgentVersion = d.AgentVersion,
                        FirstSeenUtc = d.FirstSeenUtc,
                        LastSeenUtc = d.LastSeenUtc,
                        IsEnabled = d.IsEnabled
                    })
                    .ToListAsync(ct);
            }

            return View(model);
        }

        [Authorize(Policy = "DashboardAdmin")]
        [HttpGet]
        public async Task<IActionResult> Audit(AuditViewModel model, CancellationToken ct)
        {
            var baseQuery = _db.AuditLogs.AsQueryable();

            // Global totals for the cards (Command Center style)
            model.TotalRowsReturnedCount = await baseQuery.CountAsync(ct);
            model.SuccessfulCount = await baseQuery.CountAsync(x => x.Success, ct);
            model.FailedCount = await baseQuery.CountAsync(x => !x.Success, ct);

            var query = baseQuery;

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
        public async Task<IActionResult> CreateProvisioningProfile()
        {
            var model = new EditProvisioningProfileViewModel
            {
                IsActive = true,
                DeviceType = "Register",
                Environment = "Production"
            };

            await PopulateProvisioningProfileLists(model);

            ViewData["Title"] = "Create Provisioning Profile";
            ViewData["FormAction"] = nameof(CreateProvisioningProfile);
            ViewData["SubmitText"] = "Create Profile";
            ViewData["FormMode"] = "Create";

            return View("EditProvisioningProfile", model);
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateProvisioningProfile(EditProvisioningProfileViewModel model, CancellationToken cancellationToken)
        {
            if (!model.TemplateId.HasValue)
            {
                ModelState.AddModelError(nameof(model.TemplateId), "Template is required.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateProvisioningProfileLists(model);
                ViewData["Title"] = "Create Provisioning Profile";
                ViewData["FormAction"] = nameof(CreateProvisioningProfile);
                ViewData["SubmitText"] = "Create Profile";
                ViewData["FormMode"] = "Create";
                return View("EditProvisioningProfile", model);
            }

            var selectedTemplateId = model.TemplateId.GetValueOrDefault();

            var templateExists = await _db.OrchestrationTemplates
                .AnyAsync(x => x.Id == selectedTemplateId && x.IsActive, cancellationToken);

            if (!templateExists)
            {
                ModelState.AddModelError(nameof(model.TemplateId), "Selected template was not found or is inactive.");
            }

            var duplicateName = await _db.ProvisioningProfiles
                .AnyAsync(x => x.Name == model.Name, cancellationToken);

            if (duplicateName)
            {
                ModelState.AddModelError(nameof(model.Name), "A provisioning profile with this name already exists.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateProvisioningProfileLists(model);
                ViewData["Title"] = "Create Provisioning Profile";
                ViewData["FormAction"] = nameof(CreateProvisioningProfile);
                ViewData["SubmitText"] = "Create Profile";
                ViewData["FormMode"] = "Create";
                return View("EditProvisioningProfile", model);
            }

            var entity = new ProvisioningProfile
            {
                Name = model.Name.Trim(),
                DeviceType = string.IsNullOrWhiteSpace(model.DeviceType) ? null : model.DeviceType.Trim(),
                StoreGroup = string.IsNullOrWhiteSpace(model.StoreGroup) ? null : model.StoreGroup.Trim(),
                Environment = string.IsNullOrWhiteSpace(model.Environment) ? null : model.Environment.Trim(),
                TemplateId = selectedTemplateId,
                IsDefault = model.IsDefault,
                IsActive = model.IsActive,
                ParametersJson = null,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };

            _db.ProvisioningProfiles.Add(entity);
            await _db.SaveChangesAsync(cancellationToken);

            await AuditAsync(
                "CreateProvisioningProfile",
                "ProvisioningProfile",
                entity.Id.ToString(),
                new
                {
                    entity.Id,
                    entity.Name,
                    entity.DeviceType,
                    entity.StoreGroup,
                    entity.Environment,
                    entity.TemplateId,
                    entity.IsDefault,
                    entity.IsActive
                },
                true,
                cancellationToken: cancellationToken);

            TempData["ProvisioningProfileMessage"] = $"Provisioning profile '{entity.Name}' created successfully.";
            return RedirectToAction(nameof(ProvisioningProfiles));
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpGet]
        public async Task<IActionResult> EditProvisioningProfile(int id)
        {
            var entity = await _db.ProvisioningProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (entity == null)
                return NotFound();

            var model = new EditProvisioningProfileViewModel
            {
                Id = entity.Id,
                Name = entity.Name ?? string.Empty,
                DeviceType = entity.DeviceType,
                StoreGroup = entity.StoreGroup,
                Environment = entity.Environment,
                TemplateId = entity.TemplateId,
                IsDefault = entity.IsDefault,
                IsActive = entity.IsActive
            };

            await PopulateProvisioningProfileLists(model);

            ViewData["Title"] = $"Edit Provisioning Profile #{entity.Id}";
            ViewData["FormAction"] = nameof(EditProvisioningProfile);
            ViewData["SubmitText"] = "Save Profile Changes";
            ViewData["FormMode"] = "Edit";

            return View("EditProvisioningProfile", model);
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProvisioningProfile(int id, EditProvisioningProfileViewModel model, CancellationToken cancellationToken)
        {
            var entity = await _db.ProvisioningProfiles
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (entity == null)
                return NotFound();

            if (!model.TemplateId.HasValue)
            {
                ModelState.AddModelError(nameof(model.TemplateId), "Template is required.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateProvisioningProfileLists(model);
                ViewData["Title"] = $"Edit Provisioning Profile #{id}";
                ViewData["FormAction"] = nameof(EditProvisioningProfile);
                ViewData["SubmitText"] = "Save Profile Changes";
                ViewData["FormMode"] = "Edit";
                return View(model);
            }

            var selectedTemplateId = model.TemplateId.GetValueOrDefault();

            var templateExists = await _db.OrchestrationTemplates
                .AnyAsync(x => x.Id == selectedTemplateId && x.IsActive, cancellationToken);

            if (!templateExists)
            {
                ModelState.AddModelError(nameof(model.TemplateId), "Selected template was not found or is inactive.");
            }

            var duplicateName = await _db.ProvisioningProfiles
                .AnyAsync(x => x.Id != id && x.Name == model.Name, cancellationToken);

            if (duplicateName)
            {
                ModelState.AddModelError(nameof(model.Name), "A provisioning profile with this name already exists.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateProvisioningProfileLists(model);
                ViewData["Title"] = $"Edit Provisioning Profile #{id}";
                ViewData["FormAction"] = nameof(EditProvisioningProfile);
                ViewData["SubmitText"] = "Save Profile Changes";
                ViewData["FormMode"] = "Edit";
                return View(model);
            }

            entity.Name = model.Name.Trim();
            entity.DeviceType = string.IsNullOrWhiteSpace(model.DeviceType) ? null : model.DeviceType.Trim();
            entity.StoreGroup = string.IsNullOrWhiteSpace(model.StoreGroup) ? null : model.StoreGroup.Trim();
            entity.Environment = string.IsNullOrWhiteSpace(model.Environment) ? null : model.Environment.Trim();
            entity.TemplateId = selectedTemplateId;
            entity.IsDefault = model.IsDefault;
            entity.IsActive = model.IsActive;
            entity.UpdatedUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            await AuditAsync(
                "EditProvisioningProfile",
                "ProvisioningProfile",
                entity.Id.ToString(),
                new
                {
                    entity.Id,
                    entity.Name,
                    entity.DeviceType,
                    entity.StoreGroup,
                    entity.Environment,
                    entity.TemplateId,
                    entity.IsDefault,
                    entity.IsActive
                },
                true,
                cancellationToken: cancellationToken);

            TempData["ProvisioningProfileMessage"] = $"Provisioning profile '{entity.Name}' updated successfully.";
            return RedirectToAction(nameof(ProvisioningProfiles));
        }




        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeactivateProvisioningProfile(int id, CancellationToken cancellationToken)
        {
            var entity = await _db.ProvisioningProfiles.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (entity == null)
                return NotFound();

            entity.IsActive = false;
            entity.UpdatedUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            await AuditAsync(
                "DeactivateProvisioningProfile",
                "ProvisioningProfile",
                entity.Id.ToString(),
                new { entity.Id, entity.Name },
                true,
                cancellationToken: cancellationToken);

            TempData["ProvisioningProfileMessage"] = $"Provisioning profile '{entity.Name}' deactivated.";
            return RedirectToAction(nameof(ProvisioningProfiles));
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteProvisioningProfile(int id, CancellationToken cancellationToken)
        {
            var entity = await _db.ProvisioningProfiles.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (entity == null)
                return NotFound();

            _db.ProvisioningProfiles.Remove(entity);
            await _db.SaveChangesAsync(cancellationToken);

            await AuditAsync(
                "DeleteProvisioningProfile",
                "ProvisioningProfile",
                id.ToString(),
                new { Id = id, entity.Name },
                true,
                cancellationToken: cancellationToken);

            TempData["ProvisioningProfileMessage"] = $"Provisioning profile '{entity.Name}' deleted.";
            return RedirectToAction(nameof(ProvisioningProfiles));
        }
        [Authorize(Policy = "DashboardEngineer")]
        [HttpGet]
        public async Task<IActionResult> CreateOrchestrationTemplate()
        {
            var model = new EditOrchestrationTemplateViewModel
            {
                Version = 1,
                DeviceType = "Register",
                Environment = "Production",
                TriggerType = (int)OrchestrationTriggerType.Manual,
                IsActive = false
            };

            await PopulateOrchestrationTemplateLists(model);

            ViewData["Title"] = "Create Orchestration Template";
            ViewData["FormAction"] = nameof(CreateOrchestrationTemplate);
            ViewData["SubmitText"] = "Create Template";

            return View("EditOrchestrationTemplate", model);
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateOrchestrationTemplate(EditOrchestrationTemplateViewModel model, CancellationToken cancellationToken)
        {
            if (!model.TriggerType.HasValue)
            {
                ModelState.AddModelError(nameof(model.TriggerType), "Trigger type is required.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateOrchestrationTemplateLists(model);
                ViewData["Title"] = "Create Orchestration Template";
                ViewData["FormAction"] = nameof(CreateOrchestrationTemplate);
                ViewData["SubmitText"] = "Create Template";
                return View("EditOrchestrationTemplate", model);
            }

            var duplicate = await _db.OrchestrationTemplates.AnyAsync(x =>
                x.Name == model.Name &&
                x.Version == model.Version,
                cancellationToken);

            if (duplicate)
            {
                ModelState.AddModelError(nameof(model.Name), "A template with this name and version already exists.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateOrchestrationTemplateLists(model);
                ViewData["Title"] = "Create Orchestration Template";
                ViewData["FormAction"] = nameof(CreateOrchestrationTemplate);
                ViewData["SubmitText"] = "Create Template";
                return View("EditOrchestrationTemplate", model);
            }

            var entity = new OrchestrationTemplate
            {
                Name = model.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
                Version = model.Version,
                DeviceType = model.DeviceType.Trim(),
                Environment = model.Environment.Trim(),
                TriggerType = (OrchestrationTriggerType)model.TriggerType.GetValueOrDefault(),
                IsActive = model.IsActive,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };

            _db.OrchestrationTemplates.Add(entity);
            await _db.SaveChangesAsync(cancellationToken);

            await AuditAsync(
                "CreateOrchestrationTemplate",
                "OrchestrationTemplate",
                entity.Id.ToString(),
                new
                {
                    entity.Id,
                    entity.Name,
                    entity.Version,
                    entity.DeviceType,
                    entity.Environment,
                    entity.TriggerType,
                    entity.IsActive
                },
                true,
                cancellationToken: cancellationToken);

            TempData["OrchestrationTemplateMessage"] = $"Template '{entity.Name}' created successfully.";
            return RedirectToAction(nameof(OrchestrationTemplate), new { id = entity.Id });
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpGet]
        public async Task<IActionResult> EditOrchestrationTemplate(int id)
        {
            var entity = await _db.OrchestrationTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (entity == null)
                return NotFound();

            var model = new EditOrchestrationTemplateViewModel
            {
                Id = entity.Id,
                Name = entity.Name ?? string.Empty,
                Description = entity.Description,
                Version = entity.Version,
                DeviceType = entity.DeviceType ?? string.Empty,
                Environment = entity.Environment ?? string.Empty,
                TriggerType = (int)entity.TriggerType,
                IsActive = entity.IsActive
            };

            await PopulateOrchestrationTemplateLists(model);

            ViewData["Title"] = $"Edit Template #{entity.Id}";
            ViewData["FormAction"] = nameof(EditOrchestrationTemplate);
            ViewData["SubmitText"] = "Save Template Changes";

            return View("EditOrchestrationTemplate", model);
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditOrchestrationTemplate(int id, EditOrchestrationTemplateViewModel model, CancellationToken cancellationToken)
        {
            var entity = await _db.OrchestrationTemplates
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (entity == null)
                return NotFound();

            if (!model.TriggerType.HasValue)
            {
                ModelState.AddModelError(nameof(model.TriggerType), "Trigger type is required.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateOrchestrationTemplateLists(model);
                ViewData["Title"] = $"Edit Template #{id}";
                ViewData["FormAction"] = nameof(EditOrchestrationTemplate);
                ViewData["SubmitText"] = "Save Template Changes";
                return View("EditOrchestrationTemplate", model);
            }

            var duplicate = await _db.OrchestrationTemplates.AnyAsync(x =>
                x.Id != id &&
                x.Name == model.Name &&
                x.Version == model.Version,
                cancellationToken);

            if (duplicate)
            {
                ModelState.AddModelError(nameof(model.Name), "A template with this name and version already exists.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateOrchestrationTemplateLists(model);
                ViewData["Title"] = $"Edit Template #{id}";
                ViewData["FormAction"] = nameof(EditOrchestrationTemplate);
                ViewData["SubmitText"] = "Save Template Changes";
                return View("EditOrchestrationTemplate", model);
            }

            entity.Name = model.Name.Trim();
            entity.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
            entity.Version = model.Version;
            entity.DeviceType = model.DeviceType.Trim();
            entity.Environment = model.Environment.Trim();
            entity.TriggerType = (OrchestrationTriggerType)model.TriggerType.GetValueOrDefault();
            entity.IsActive = model.IsActive;
            entity.UpdatedUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            await AuditAsync(
                "EditOrchestrationTemplate",
                "OrchestrationTemplate",
                entity.Id.ToString(),
                new
                {
                    entity.Id,
                    entity.Name,
                    entity.Version,
                    entity.DeviceType,
                    entity.Environment,
                    entity.TriggerType,
                    entity.IsActive
                },
                true,
                cancellationToken: cancellationToken);

            TempData["OrchestrationTemplateMessage"] = $"Template '{entity.Name}' updated successfully.";
            return RedirectToAction(nameof(OrchestrationTemplate), new { id = entity.Id });
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CloneOrchestrationTemplate(int id, CancellationToken cancellationToken)
        {
            var entity = await _db.OrchestrationTemplates
                .Include(x => x.Steps)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (entity == null)
                return NotFound();

            var nextVersion = await _db.OrchestrationTemplates
                .Where(x => x.Name == entity.Name)
                .MaxAsync(x => (int?)x.Version, cancellationToken) ?? entity.Version;

            var clone = new OrchestrationTemplate
            {
                Name = entity.Name,
                Description = entity.Description,
                Version = nextVersion + 1,
                DeviceType = entity.DeviceType,
                Environment = entity.Environment,
                TriggerType = entity.TriggerType,
                IsActive = false,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow,
                Steps = entity.Steps
                    .OrderBy(x => x.StepOrder)
                    .Select(step => new OrchestrationTemplateStep
                    {
                        StepOrder = step.StepOrder,
                        Name = step.Name,
                        StepType = step.StepType,
                        CommandType = step.CommandType,
                        ParametersJson = step.ParametersJson,
                        SuccessCriteriaJson = step.SuccessCriteriaJson,
                        TimeoutSeconds = step.TimeoutSeconds,
                        MaxRetries = step.MaxRetries,
                        OnFailureAction = step.OnFailureAction,
                        ContinueOnFailure = step.ContinueOnFailure,
                        RollbackTemplateStepId = step.RollbackTemplateStepId
                    })
                    .ToList()
            };

            _db.OrchestrationTemplates.Add(clone);
            await _db.SaveChangesAsync(cancellationToken);

            await AuditAsync(
                "CloneOrchestrationTemplate",
                "OrchestrationTemplate",
                clone.Id.ToString(),
                new
                {
                    SourceTemplateId = entity.Id,
                    ClonedTemplateId = clone.Id,
                    clone.Name,
                    clone.Version
                },
                true,
                cancellationToken: cancellationToken);

            TempData["OrchestrationTemplateMessage"] = $"Template cloned as version {clone.Version}.";
            return RedirectToAction(nameof(EditOrchestrationTemplate), new { id = clone.Id });
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActivateOrchestrationTemplate(int id, CancellationToken cancellationToken)
        {
            var entity = await _db.OrchestrationTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (entity == null)
                return NotFound();

            entity.IsActive = true;
            entity.UpdatedUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            await AuditAsync(
                "ActivateOrchestrationTemplate",
                "OrchestrationTemplate",
                entity.Id.ToString(),
                new { entity.Id, entity.Name, entity.Version },
                true,
                cancellationToken: cancellationToken);

            TempData["OrchestrationTemplateMessage"] = $"Template '{entity.Name}' activated.";
            return RedirectToAction(nameof(OrchestrationTemplate), new { id = entity.Id });
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeactivateOrchestrationTemplate(int id, CancellationToken cancellationToken)
        {
            var entity = await _db.OrchestrationTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (entity == null)
                return NotFound();

            entity.IsActive = false;
            entity.UpdatedUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            await AuditAsync(
                "DeactivateOrchestrationTemplate",
                "OrchestrationTemplate",
                entity.Id.ToString(),
                new { entity.Id, entity.Name, entity.Version },
                true,
                cancellationToken: cancellationToken);

            TempData["OrchestrationTemplateMessage"] = $"Template '{entity.Name}' deactivated.";
            return RedirectToAction(nameof(OrchestrationTemplate), new { id = entity.Id });
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteOrchestrationTemplate(int id, CancellationToken cancellationToken)
        {
            var entity = await _db.OrchestrationTemplates
                .Include(x => x.ProvisioningProfiles)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (entity == null)
                return NotFound();

            var hasRuns = await _db.OrchestrationRuns.AnyAsync(x => x.TemplateId == id, cancellationToken);
            if (hasRuns)
            {
                TempData["OrchestrationTemplateMessage"] = "Cannot delete template because orchestration runs exist for it.";
                return RedirectToAction(nameof(OrchestrationTemplate), new { id });
            }

            if (entity.ProvisioningProfiles.Any())
            {
                TempData["OrchestrationTemplateMessage"] = "Cannot delete template because provisioning profiles still reference it.";
                return RedirectToAction(nameof(OrchestrationTemplate), new { id });
            }

            _db.OrchestrationTemplates.Remove(entity);
            await _db.SaveChangesAsync(cancellationToken);

            await AuditAsync(
                "DeleteOrchestrationTemplate",
                "OrchestrationTemplate",
                id.ToString(),
                new { entity.Id, entity.Name, entity.Version },
                true,
                cancellationToken: cancellationToken);

            TempData["OrchestrationTemplateMessage"] = $"Template '{entity.Name}' deleted.";
            return RedirectToAction(nameof(OrchestrationTemplates));
        }

        [Authorize(Policy = "DashboardViewer")]
        [HttpGet]
        public async Task<IActionResult> OrchestrationRuns(string? status, string? search, int take = 200)
        {
            take = Math.Clamp(take, 25, 500);

            var query = _db.OrchestrationRuns
                .AsNoTracking()
                .Include(x => x.Steps)
                .Include(x => x.Template)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) &&
                Enum.TryParse<OrchestrationRunStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(x => x.Status == parsedStatus);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();

                query = query.Where(x =>
                    (x.CorrelationId ?? string.Empty).Contains(s) ||
                    (x.RequestedBy ?? string.Empty).Contains(s) ||
                    (x.DeviceId != null && x.DeviceId.ToString()!.Contains(s)) ||
                    (x.Template != null && (x.Template.Name ?? string.Empty).Contains(s)));
            }

            var runs = await query
                .OrderByDescending(x => x.StartedUtc)
                .Take(take)
                .Select(x => new OrchestrationRunListItemViewModel
                {
                    Id = x.Id,
                    TemplateId = x.TemplateId,
                    TemplateName = x.Template != null ? (x.Template.Name ?? $"Template {x.TemplateId}") : $"Template {x.TemplateId}",
                    TemplateVersion = x.Template != null ? x.Template.Version : 0,
                    DeviceId = x.DeviceId,
                    AgentId = x.AgentId,
                    StoreId = x.StoreId,
                    RegisterId = x.RegisterId,
                    Status = x.Status.ToString(),
                    CurrentStepOrder = x.CurrentStepOrder,
                    CorrelationId = x.CorrelationId ?? string.Empty,
                    RequestedBy = x.RequestedBy ?? string.Empty,
                    TriggerSource = x.TriggerSource.ToString(),
                    StartedUtc = x.StartedUtc,
                    CompletedUtc = x.CompletedUtc,
                    TotalSteps = x.Steps.Count,
                    CompletedSteps = x.Steps.Count(step => step.Status == OrchestrationRunStepStatus.Succeeded),
                    FailedSteps = x.Steps.Count(step =>
                        step.Status == OrchestrationRunStepStatus.Failed ||
                        step.Status == OrchestrationRunStepStatus.TimedOut)
                })
                .ToListAsync();

            var allRuns = _db.OrchestrationRuns.AsNoTracking();

            var model = new OrchestrationRunsPageViewModel
            {
                StatusFilter = status,
                Search = search,
                TotalRuns = await allRuns.CountAsync(),
                PendingRuns = await allRuns.CountAsync(x => x.Status == OrchestrationRunStatus.Pending),
                RunningRuns = await allRuns.CountAsync(x =>
                    x.Status == OrchestrationRunStatus.Running ||
                    x.Status == OrchestrationRunStatus.WaitingForRetry),
                CompletedRuns = await allRuns.CountAsync(x => x.Status == OrchestrationRunStatus.Completed),
                FailedRuns = await allRuns.CountAsync(x =>
                    x.Status == OrchestrationRunStatus.Failed ||
                    x.Status == OrchestrationRunStatus.Cancelled),
                Runs = runs
            };

            return View(model);
        }

        [Authorize(Policy = "DashboardViewer")]
        [HttpGet]
        public async Task<IActionResult> OrchestrationRun(long id)
        {
            var run = await _db.OrchestrationRuns
                .AsNoTracking()
                .Include(x => x.Template)
                .Include(x => x.Steps)
                    .ThenInclude(x => x.TemplateStep)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (run == null)
                return NotFound();

            var model = new OrchestrationRunDetailViewModel
            {
                Id = run.Id,
                TemplateId = run.TemplateId,
                TemplateName = run.Template != null ? (run.Template.Name ?? $"Template {run.TemplateId}") : $"Template {run.TemplateId}",
                TemplateVersion = run.Template != null ? run.Template.Version : 0,
                DeviceId = run.DeviceId,
                AgentId = run.AgentId,
                StoreId = run.StoreId,
                RegisterId = run.RegisterId,
                Status = run.Status.ToString(),
                CurrentStepOrder = run.CurrentStepOrder,
                CorrelationId = run.CorrelationId ?? string.Empty,
                RequestedBy = run.RequestedBy ?? string.Empty,
                TriggerSource = run.TriggerSource.ToString(),
                StartedUtc = run.StartedUtc,
                CompletedUtc = run.CompletedUtc,
                Steps = run.Steps
                    .OrderBy(step => step.StepOrder)
                    .Select(step => new OrchestrationRunStepViewModel
                    {
                        Id = step.Id,
                        StepOrder = step.StepOrder,
                        Name = step.TemplateStep != null ? (step.TemplateStep.Name ?? $"Step {step.StepOrder}") : $"Step {step.StepOrder}",
                        CommandType = step.TemplateStep != null ? (step.TemplateStep.CommandType ?? string.Empty) : string.Empty,
                        StepType = step.TemplateStep != null ? step.TemplateStep.StepType.ToString() : string.Empty,
                        Status = step.Status.ToString(),
                        AttemptCount = step.AttemptCount,
                        CommandId = step.CommandId,
                        ErrorMessage = step.ErrorMessage,
                        StartedUtc = step.StartedUtc,
                        CompletedUtc = step.CompletedUtc
                    })
                    .ToList()
            };

            return View(model);
        }

        [Authorize(Policy = "DashboardViewer")]
        [HttpGet]
        public async Task<IActionResult> OrchestrationTemplates(
    string? search,
    string? deviceType,
    string? environment,
    bool? isActive,
    string? filter,
    int take = 200)
        {
            take = Math.Clamp(take, 25, 500);

            var query = _db.OrchestrationTemplates
                .AsNoTracking()
                .Include(x => x.Steps)
                .Include(x => x.ProvisioningProfiles)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(x =>
                    (x.Name ?? string.Empty).Contains(s) ||
                    (x.Description ?? string.Empty).Contains(s));
            }

            if (!string.IsNullOrWhiteSpace(deviceType))
            {
                var dt = deviceType.Trim();
                query = query.Where(x => (x.DeviceType ?? string.Empty) == dt);
            }

            if (!string.IsNullOrWhiteSpace(environment))
            {
                var env = environment.Trim();
                query = query.Where(x => (x.Environment ?? string.Empty) == env);
            }

            if (isActive.HasValue)
            {
                query = query.Where(x => x.IsActive == isActive.Value);
            }

            if (string.Equals(filter, "unused-active", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => x.IsActive && !x.ProvisioningProfiles.Any(p => p.IsActive));
            }

            var templates = await query
                .OrderBy(x => x.Name)
                .ThenByDescending(x => x.Version)
                .Take(take)
                .Select(x => new OrchestrationTemplateListItemViewModel
                {
                    Id = x.Id,
                    Name = x.Name ?? string.Empty,
                    Description = x.Description,
                    Version = x.Version,
                    DeviceType = x.DeviceType ?? string.Empty,
                    Environment = x.Environment ?? string.Empty,
                    TriggerType = x.TriggerType.ToString(),
                    IsActive = x.IsActive,
                    StepCount = x.Steps.Count,
                    ProvisioningProfileCount = x.ProvisioningProfiles.Count,
                    CreatedUtc = x.CreatedUtc,
                    UpdatedUtc = x.UpdatedUtc
                })
                .ToListAsync();

            var allTemplates = _db.OrchestrationTemplates.AsNoTracking();

            var model = new OrchestrationTemplatesPageViewModel
            {
                Search = search,
                DeviceTypeFilter = deviceType,
                EnvironmentFilter = environment,
                ActiveFilter = isActive,
                FilterMode = filter,
                TotalTemplates = await allTemplates.CountAsync(),
                ActiveTemplates = await allTemplates.CountAsync(x => x.IsActive),
                InactiveTemplates = await allTemplates.CountAsync(x => !x.IsActive),
                Templates = templates
            };

            return View(model);
        }

        [Authorize(Policy = "DashboardViewer")]
        [HttpGet]
        public async Task<IActionResult> OrchestrationTemplate(int id)
        {
            var template = await _db.OrchestrationTemplates
                .AsNoTracking()
                .Include(x => x.Steps)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (template == null)
                return NotFound();

            var referencedStepIds = await _db.OrchestrationRunSteps
                .AsNoTracking()
                .Select(x => x.TemplateStepId)
                .Distinct()
                .ToListAsync();

            var referencedStepIdSet = referencedStepIds.ToHashSet();

            var model = new OrchestrationTemplateDetailViewModel
            {
                Id = template.Id,
                Name = template.Name ?? string.Empty,
                Description = template.Description,
                Version = template.Version,
                DeviceType = template.DeviceType ?? string.Empty,
                Environment = template.Environment ?? string.Empty,
                TriggerType = template.TriggerType.ToString(),
                IsActive = template.IsActive,
                CreatedUtc = template.CreatedUtc,
                UpdatedUtc = template.UpdatedUtc,
                Steps = template.Steps
                    .OrderBy(step => step.StepOrder)
                    .Select(step => new OrchestrationTemplateStepViewModel
                    {
                        Id = step.Id,
                        StepOrder = step.StepOrder,
                        Name = step.Name ?? $"Step {step.StepOrder}",
                        StepType = step.StepType.ToString(),
                        CommandType = step.CommandType ?? string.Empty,
                        ParametersJson = step.ParametersJson,
                        SuccessCriteriaJson = step.SuccessCriteriaJson,
                        TimeoutSeconds = step.TimeoutSeconds,
                        MaxRetries = step.MaxRetries,
                        OnFailureAction = step.OnFailureAction.ToString(),
                        ContinueOnFailure = step.ContinueOnFailure,
                        RollbackTemplateStepId = step.RollbackTemplateStepId,
                        HasRunHistory = referencedStepIdSet.Contains(step.Id)
                    })
                    .ToList()
            };

            return View(model);
        }

        [Authorize(Policy = "DashboardViewer")]
        [HttpGet]
        public async Task<IActionResult> ProvisioningProfiles(
    string? search,
    string? deviceType,
    string? environment,
    bool? isActive,
    string? filter,
    int take = 200)
        {
            take = Math.Clamp(take, 25, 500);

            var query = _db.ProvisioningProfiles
                .AsNoTracking()
                .Include(x => x.Template)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(x =>
                    (x.Name ?? string.Empty).Contains(s) ||
                    (x.StoreGroup ?? string.Empty).Contains(s) ||
                    (x.Template != null && (x.Template.Name ?? string.Empty).Contains(s)));
            }

            if (!string.IsNullOrWhiteSpace(deviceType))
            {
                var dt = deviceType.Trim();
                query = query.Where(x => (x.DeviceType ?? string.Empty) == dt);
            }

            if (!string.IsNullOrWhiteSpace(environment))
            {
                var env = environment.Trim();
                query = query.Where(x => (x.Environment ?? string.Empty) == env);
            }

            if (isActive.HasValue)
            {
                query = query.Where(x => x.IsActive == isActive.Value);
            }

            if (string.Equals(filter, "inactive-template", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => x.IsActive && x.Template != null && !x.Template.IsActive);
            }

            var profiles = await query
                .OrderBy(x => x.Name)
                .Take(take)
                .Select(x => new ProvisioningProfileListItemViewModel
                {
                    Id = x.Id,
                    Name = x.Name ?? string.Empty,
                    DeviceType = x.DeviceType ?? string.Empty,
                    StoreGroup = x.StoreGroup ?? string.Empty,
                    Environment = x.Environment ?? string.Empty,
                    TemplateId = x.TemplateId,
                    TemplateName = x.Template != null ? (x.Template.Name ?? $"Template {x.TemplateId}") : $"Template {x.TemplateId}",
                    TemplateVersion = x.Template != null ? x.Template.Version : 0,
                    IsDefault = x.IsDefault,
                    IsActive = x.IsActive,
                    ParametersJson = x.ParametersJson
                })
                .ToListAsync();

            var allProfiles = _db.ProvisioningProfiles.AsNoTracking();

            var model = new ProvisioningProfilesPageViewModel
            {
                Search = search,
                DeviceTypeFilter = deviceType,
                EnvironmentFilter = environment,
                ActiveFilter = isActive,
                FilterMode = filter,
                TotalProfiles = await allProfiles.CountAsync(),
                ActiveProfiles = await allProfiles.CountAsync(x => x.IsActive),
                DefaultProfiles = await allProfiles.CountAsync(x => x.IsDefault),
                Profiles = profiles
            };

            return View(model);
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
            var response = await _deploymentService.GetDeploymentAsync(id, cancellationToken);
            if (response == null)
            {
                return NotFound();
            }

            var vm = MapDeploymentDetailsViewModel(response);

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

            if (!IsEditableDeploymentStatus(deployment.Status))
            {
                TempData["DeploymentMessage"] = "Completed or cancelled deployments cannot be edited.";
                return RedirectToAction(nameof(DeploymentDetail), new { id });
            }

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

        private static DeploymentDetailsViewModel MapDeploymentDetailsViewModel(DeploymentDetailResponse response)
        {
            return new DeploymentDetailsViewModel
            {
                Id = response.DeploymentId,
                PackageName = response.PackageName,
                PackageVersion = response.PackageVersion,
                TargetType = ((DeploymentTargetType)response.TargetType).ToString(),
                TargetValue = response.TargetValue,
                ExecuteMode = ((DeploymentExecuteMode)response.ExecuteMode).ToString(),
                Status = ((DeploymentStatus)response.Status).ToString(),
                CreatedUtc = response.CreatedUtc,

                // These are not present on DeploymentDetailResponse today.
                // Keep them null unless/until the service starts returning them.
                CreatedBy = null,
                Notes = null,

                WindowStartLocal = response.WindowStartLocal,
                WindowEndLocal = response.WindowEndLocal,

                DeviceRows = response.Devices
                    .Select(d => new DeploymentDeviceRowViewModel
                    {
                        DeviceId = d.DeviceId,
                        StoreNumber = d.StoreNumber,
                        Hostname = d.Hostname,
                        Status = ((DeploymentDeviceStatus)d.Status).ToString(),
                        DownloadStatus = d.DownloadStatus,
                        ExecuteStatus = d.ExecuteStatus,
                        ResultMessage = d.ResultMessage,
                        ExitCode = d.ExitCode,
                        LastHeartbeatUtc = d.LastHeartbeatUtc
                    })
                    .ToList()
            };
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

        private static bool IsEditableDeploymentStatus(int status)
        {
            return status != (int)DeploymentStatus.Completed
                && status != (int)DeploymentStatus.Cancelled;
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
        public async Task<IActionResult> Commands(string? status, string? search, string? mode, bool expiredPendingOnly = false, int take = 200)
        {
            take = Math.Clamp(take, 25, 500);

            var nowUtc = DateTime.UtcNow;
            var normalizedMode = string.IsNullOrWhiteSpace(mode) ? null : mode.Trim();
            var allCommands = _db.Commands.AsNoTracking();
            var baseQuery = allCommands;

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
                    (c.PayloadJson != null && c.PayloadJson.Contains(trimmedSearch)) ||
                    (c.LastError != null && c.LastError.Contains(trimmedSearch)));
            }

            if (expiredPendingOnly)
            {
                baseQuery = baseQuery.Where(c =>
                    c.Status == "Pending" &&
                    c.ExpiresUtc != null &&
                    c.ExpiresUtc <= nowUtc);
            }

            if (!string.IsNullOrWhiteSpace(normalizedMode))
            {
                if (string.Equals(normalizedMode, "Retrying", StringComparison.OrdinalIgnoreCase))
                {
                    baseQuery = baseQuery.Where(c =>
                        c.Status == "Pending" &&
                        c.AttemptCount > 0 &&
                        (c.ExpiresUtc == null || c.ExpiresUtc > nowUtc));
                }
                else if (string.Equals(normalizedMode, "FinalFailure", StringComparison.OrdinalIgnoreCase))
                {
                    baseQuery = baseQuery.Where(c =>
                        c.Status == "Failed" &&
                        c.AttemptCount >= c.MaxAttempts);
                }
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

            var retryingCount = await allCommands.CountAsync(c =>
                c.Status == "Pending" &&
                c.AttemptCount > 0 &&
                (c.ExpiresUtc == null || c.ExpiresUtc > nowUtc));

            var finalFailureCount = await allCommands.CountAsync(c =>
                c.Status == "Failed" &&
                c.AttemptCount >= c.MaxAttempts);

            ViewData["RetryingCount"] = retryingCount;
            ViewData["FinalFailureCount"] = finalFailureCount;
            ViewData["CommandMode"] = normalizedMode;

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
                await AuditAsync(
                    "RunHelpdeskCommandValidationFailed",
                    normalizedTargetType,
                    model.DeviceId?.ToString() ?? model.StoreNumber ?? model.GroupName,
                    new { model.CommandKey, model.TargetType, model.DeviceId, model.StoreNumber, model.GroupName },
                    false,
                    "ModelState invalid");

                await PopulateHelpdeskCommandLists(model, cutoff);
                return View("HelpdeskCommands", model);
            }

            // Dashboard-only helper path.
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

            // Build a shared DTO so helpdesk issuance follows the same validation rules
            // as the Admin API and Issue Command flow.
            var mapped = MapHelpdeskCommand(model.CommandKey);

            var request = new CreateCommandRequest
            {
                DeviceId = normalizedTargetType.Equals("Device", StringComparison.OrdinalIgnoreCase) ? model.DeviceId : null,
                StoreNumber = normalizedTargetType.Equals("Store", StringComparison.OrdinalIgnoreCase) ? model.StoreNumber?.Trim() : null,
                GroupName = normalizedTargetType.Equals("Group", StringComparison.OrdinalIgnoreCase) ? model.GroupName?.Trim() : null,
                Type = mapped.Type,
                PayloadJson = mapped.PayloadJson,
                Priority = 100,
                MaxAttempts = 3,
                ExpiresUtc = null
            };

            var validation = _commandValidationService.Validate(request, helpdeskMode: true);
            if (!validation.IsValid)
            {
                foreach (var error in validation.Errors)
                {
                    ModelState.AddModelError("", error);
                }

                await AuditAsync(
                    "RunHelpdeskCommandRejected",
                    normalizedTargetType,
                    model.DeviceId?.ToString() ?? model.StoreNumber ?? model.GroupName,
                    new
                    {
                        model.CommandKey,
                        RequestType = request.Type,
                        request.DeviceId,
                        request.StoreNumber,
                        request.GroupName,
                        validation.Errors
                    },
                    false,
                    string.Join(" | ", validation.Errors));

                await PopulateHelpdeskCommandLists(model, cutoff);
                return View("HelpdeskCommands", model);
            }

            var now = DateTime.UtcNow;
            var createdCommands = new List<Command>();

            if (request.DeviceId != null)
            {
                var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == request.DeviceId.Value && d.IsEnabled);
                if (device == null)
                {
                    ModelState.AddModelError("", "Selected device was not found.");
                }
                else
                {
                    createdCommands.Add(BuildDeviceCommand(
                        device,
                        validation.NormalizedType,
                        request.PayloadJson,
                        request.Priority,
                        request.MaxAttempts,
                        request.ExpiresUtc,
                        issuedBy: CurrentActor(),
                        issuedUtc: now,
                        groupName: null));
                }
            }
            else if (!string.IsNullOrWhiteSpace(request.StoreNumber))
            {
                var storeDevices = await _db.Devices
                    .Where(d => d.IsEnabled && d.StoreNumber == request.StoreNumber)
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
                            validation.NormalizedType,
                            request.PayloadJson,
                            request.Priority,
                            request.MaxAttempts,
                            request.ExpiresUtc,
                            issuedBy: CurrentActor(),
                            issuedUtc: now,
                            groupName: null));
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(request.GroupName))
            {
                var groupDevices = await _db.DeviceGroupMembers
                    .Where(m => _db.DeviceGroups.Any(g => g.DeviceGroupId == m.DeviceGroupId && g.GroupName == request.GroupName))
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
                    {
                        createdCommands.Add(BuildDeviceCommand(
                            device,
                            validation.NormalizedType,
                            request.PayloadJson,
                            request.Priority,
                            request.MaxAttempts,
                            request.ExpiresUtc,
                            issuedBy: CurrentActor(),
                            issuedUtc: now,
                            groupName: request.GroupName));
                    }
                }
            }

            if (!ModelState.IsValid)
            {
                await AuditAsync(
                    "RunHelpdeskCommandValidationFailed",
                    normalizedTargetType,
                    model.DeviceId?.ToString() ?? model.StoreNumber ?? model.GroupName,
                    new
                    {
                        model.CommandKey,
                        request.Type,
                        request.DeviceId,
                        request.StoreNumber,
                        request.GroupName
                    },
                    false,
                    "Target validation failed");

                await PopulateHelpdeskCommandLists(model, cutoff);
                return View("HelpdeskCommands", model);
            }

            _db.Commands.AddRange(createdCommands);
            await _db.SaveChangesAsync();

            await AuditAsync(
                "RunHelpdeskCommand",
                normalizedTargetType,
                model.DeviceId?.ToString() ?? model.StoreNumber ?? model.GroupName,
                new
                {
                    model.CommandKey,
                    NormalizedType = validation.NormalizedType,
                    request.DeviceId,
                    request.StoreNumber,
                    request.GroupName,
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
                cmd.NextAttemptUtc = DateTime.UtcNow;
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
            var commandTypes = GetAvailableCommandTypes();

            var defaultTemplate = templates.FirstOrDefault()
                ?? new CommandTemplateOptionViewModel
                {
                    Name = "Collect System Info",
                    CommandType = "CollectSystemInfo",
                    PayloadJson = "{}"
                };

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

                AvailableCommandTypes = commandTypes,
                AvailableTemplates = templates,

                Priority = 100,
                MaxAttempts = 3,
                Type = defaultTemplate.CommandType,
                TemplateName = defaultTemplate.Name,
                UseCustomJson = false,
                PayloadJson = defaultTemplate.PayloadJson
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

            // Route dashboard issuance through the same request model and validation
            // used by the Admin API.
            var request = new CreateCommandRequest
            {
                DeviceId = model.DeviceId,
                StoreNumber = string.IsNullOrWhiteSpace(model.StoreNumber) ? null : model.StoreNumber.Trim(),
                GroupName = string.IsNullOrWhiteSpace(model.GroupName) ? null : model.GroupName.Trim(),
                Type = model.Type?.Trim() ?? "",
                PayloadJson = string.IsNullOrWhiteSpace(model.PayloadJson) ? null : model.PayloadJson,
                Priority = model.Priority,
                MaxAttempts = model.MaxAttempts,
                ExpiresUtc = model.ExpiresUtc
            };

            var validation = _commandValidationService.Validate(request, helpdeskMode: false);
            if (!validation.IsValid)
            {
                foreach (var error in validation.Errors)
                {
                    ModelState.AddModelError("", error);
                }
            }

            if (!ModelState.IsValid)
            {
                await AuditAsync(
                    "IssueCommandValidationFailed",
                    request.DeviceId != null ? "Device" : !string.IsNullOrWhiteSpace(request.StoreNumber) ? "Store" : !string.IsNullOrWhiteSpace(request.GroupName) ? "Group" : null,
                    request.DeviceId?.ToString() ?? request.StoreNumber ?? request.GroupName,
                    new
                    {
                        request.Type,
                        request.DeviceId,
                        request.StoreNumber,
                        request.GroupName,
                        request.Priority,
                        request.MaxAttempts,
                        validation.Errors
                    },
                    false,
                    "ModelState invalid");

                await PopulateIssueCommandLists(model, cutoff, commandTypes, templates);
                return View(model);
            }

            var now = DateTime.UtcNow;
            var createdCommands = new List<Command>();

            if (request.DeviceId != null)
            {
                var device = await _db.Devices
                    .FirstOrDefaultAsync(d => d.DeviceId == request.DeviceId.Value && d.IsEnabled);

                if (device == null)
                {
                    ModelState.AddModelError("", "Selected device was not found.");
                }
                else
                {
                    createdCommands.Add(BuildDeviceCommand(
                        device,
                        validation.NormalizedType,
                        request.PayloadJson,
                        request.Priority,
                        request.MaxAttempts,
                        request.ExpiresUtc,
                        issuedBy: CurrentActor(),
                        issuedUtc: now,
                        groupName: null));
                }
            }
            else if (!string.IsNullOrWhiteSpace(request.StoreNumber))
            {
                var storeDevices = await _db.Devices
                    .Where(d => d.IsEnabled && d.StoreNumber == request.StoreNumber)
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
                            validation.NormalizedType,
                            request.PayloadJson,
                            request.Priority,
                            request.MaxAttempts,
                            request.ExpiresUtc,
                            issuedBy: CurrentActor(),
                            issuedUtc: now,
                            groupName: null));
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(request.GroupName))
            {
                var groupDevices = await _db.DeviceGroupMembers
                    .Where(m => _db.DeviceGroups.Any(g =>
                        g.DeviceGroupId == m.DeviceGroupId &&
                        g.GroupName == request.GroupName))
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
                            validation.NormalizedType,
                            request.PayloadJson,
                            request.Priority,
                            request.MaxAttempts,
                            request.ExpiresUtc,
                            issuedBy: CurrentActor(),
                            issuedUtc: now,
                            groupName: request.GroupName));
                    }
                }
            }

            if (!ModelState.IsValid)
            {
                await AuditAsync(
                    "IssueCommandValidationFailed",
                    request.DeviceId != null ? "Device" : !string.IsNullOrWhiteSpace(request.StoreNumber) ? "Store" : !string.IsNullOrWhiteSpace(request.GroupName) ? "Group" : null,
                    request.DeviceId?.ToString() ?? request.StoreNumber ?? request.GroupName,
                    new
                    {
                        request.Type,
                        request.DeviceId,
                        request.StoreNumber,
                        request.GroupName,
                        request.Priority,
                        request.MaxAttempts
                    },
                    false,
                    "Target validation failed");

                await PopulateIssueCommandLists(model, cutoff, commandTypes, templates);
                return View(model);
            }

            if (createdCommands.Count == 0)
            {
                await AuditAsync(
                    "IssueCommandValidationFailed",
                    request.DeviceId != null ? "Device" : !string.IsNullOrWhiteSpace(request.StoreNumber) ? "Store" : !string.IsNullOrWhiteSpace(request.GroupName) ? "Group" : null,
                    request.DeviceId?.ToString() ?? request.StoreNumber ?? request.GroupName,
                    new
                    {
                        request.Type,
                        request.DeviceId,
                        request.StoreNumber,
                        request.GroupName,
                        request.Priority,
                        request.MaxAttempts
                    },
                    false,
                    "No matching target devices were found.");

                ModelState.AddModelError("", "No matching target devices were found.");
                await PopulateIssueCommandLists(model, cutoff, commandTypes, templates);
                return View(model);
            }

            _db.Commands.AddRange(createdCommands);
            await _db.SaveChangesAsync();

            await AuditAsync(
                "IssueCommand",
                request.DeviceId != null ? "Device" : !string.IsNullOrWhiteSpace(request.StoreNumber) ? "Store" : "Group",
                request.DeviceId?.ToString() ?? request.StoreNumber ?? request.GroupName,
                new
                {
                    request.Type,
                    NormalizedType = validation.NormalizedType,
                    request.DeviceId,
                    request.StoreNumber,
                    request.GroupName,
                    request.Priority,
                    request.MaxAttempts,
                    request.ExpiresUtc,
                    CreatedCommandCount = createdCommands.Count,
                    CommandIds = createdCommands.Select(c => c.CommandId).ToList()
                });

            if (createdCommands.Count == 1)
            {
                if (request.DeviceId != null)
                {
                    TempData["DeviceMessage"] = $"Command '{validation.NormalizedType}' created successfully.";
                    return RedirectToAction(nameof(Device), new { id = request.DeviceId.Value });
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

        private static bool RequiresCommandType(OrchestrationStepType stepType)
        {
            var name = stepType.ToString();

            return name.Contains("Command", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Validate", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Validation", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Install", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Restart", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Collect", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Write", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Apply", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Import", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Registry", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Script", StringComparison.OrdinalIgnoreCase);
        }

        private static bool SupportsGuidedParameters(string? commandType)
        {
            if (string.IsNullOrWhiteSpace(commandType))
                return false;

            return commandType.Equals("ValidateProcess", StringComparison.OrdinalIgnoreCase)
                || commandType.Equals("RestartPOS", StringComparison.OrdinalIgnoreCase)
                || commandType.Equals("CollectSystemInfo", StringComparison.OrdinalIgnoreCase)
                || commandType.Equals("InstallPackage", StringComparison.OrdinalIgnoreCase)
                || commandType.Equals("WriteFile", StringComparison.OrdinalIgnoreCase)
                || commandType.Equals("ImportRegistryFile", StringComparison.OrdinalIgnoreCase)
                || commandType.Equals("ValidateRegistry", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetGuidedParameterDescription(string? commandType)
        {
            if (string.IsNullOrWhiteSpace(commandType))
                return "Choose a command type to see guided parameter options.";

            if (commandType.Equals("ValidateProcess", StringComparison.OrdinalIgnoreCase))
                return "ValidateProcess requires a process name and will store it as structured step parameters.";

            if (commandType.Equals("RestartPOS", StringComparison.OrdinalIgnoreCase))
                return "RestartPOS does not require additional parameters.";

            if (commandType.Equals("CollectSystemInfo", StringComparison.OrdinalIgnoreCase))
                return "CollectSystemInfo does not require additional parameters.";

            if (commandType.Equals("InstallPackage", StringComparison.OrdinalIgnoreCase))
                return "InstallPackage uses a guided package selector and generates the install payload automatically.";

            if (commandType.Equals("WriteFile", StringComparison.OrdinalIgnoreCase))
                return "WriteFile writes content to a trusted local path using either inline content or an uploaded file.";

            if (commandType.Equals("ImportRegistryFile", StringComparison.OrdinalIgnoreCase))
                return "ImportRegistryFile uses a guided package selector and imports a trusted .reg file on the endpoint.";

            if (commandType.Equals("ValidateRegistry", StringComparison.OrdinalIgnoreCase))
                return "ValidateRegistry checks that a registry value exists and matches the expected value.";

            if (IsRecognizedButNotYetImplementedCommandType(commandType))
                return "This command type is recognized, but guided parameter support is not implemented yet.";

            return "Guided parameters are not available yet for this command type.";
        }

        private static bool IsRecognizedButNotYetImplementedStepType(OrchestrationStepType stepType)
        {
            var name = stepType.ToString();

            return name.Contains("Write", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Apply", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Import", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Registry", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Script", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRecognizedButNotYetImplementedCommandType(string? commandType)
        {
            if (string.IsNullOrWhiteSpace(commandType))
                return false;

            return commandType.Equals("ApplyConfiguration", StringComparison.OrdinalIgnoreCase)
                || commandType.Equals("ValidateFile", StringComparison.OrdinalIgnoreCase)
                || commandType.Equals("RunScript", StringComparison.OrdinalIgnoreCase);
        }
        private static void ApplyCommandTypeMetadata(EditOrchestrationTemplateStepViewModel model)
        {
            model.SupportsGuidedParameters = SupportsGuidedParameters(model.CommandType);
            model.GuidedParameterDescription = GetGuidedParameterDescription(model.CommandType);

            if (string.Equals(model.CommandType, "ImportRegistryFile", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(model.ImportRegistrySourceMode))
            {
                model.ImportRegistrySourceMode = "Package";
            }

            if (!string.Equals(model.CommandType, "ValidateRegistry", StringComparison.OrdinalIgnoreCase))
            {
                model.ValidateRegistryHive = null;
                model.ValidateRegistryKeyPath = null;
                model.ValidateRegistryValueName = null;
                model.ValidateRegistryExpectedValue = null;
            }

            if (!string.Equals(model.CommandType, "ValidateProcess", StringComparison.OrdinalIgnoreCase))
            {
                model.ValidateProcessName = null;
            }

            if (!string.Equals(model.CommandType, "InstallPackage", StringComparison.OrdinalIgnoreCase))
            {
                model.InstallPackageId = null;
                model.InstallPackageExecuteMode = null;
                model.SelectedPackageSummary = null;
            }

            if (string.Equals(model.CommandType, "InstallPackage", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(model.InstallPackageExecuteMode))
            {
                model.InstallPackageExecuteMode = "Immediate";
            }

            if (!string.Equals(model.CommandType, "ImportRegistryFile", StringComparison.OrdinalIgnoreCase))
            {
                model.ImportRegistryPackageId = null;
                model.ImportRegistryExecuteMode = null;
                model.SelectedRegistryPackageSummary = null;
            }

            if (string.Equals(model.CommandType, "ImportRegistryFile", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(model.ImportRegistryExecuteMode))
            {
                model.ImportRegistryExecuteMode = "Immediate";
            }

            if (string.Equals(model.CommandType, "WriteFile", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(model.WriteFileSourceMode))
            {
                model.WriteFileSourceMode = "Inline";
            }
            if (!string.Equals(model.CommandType, "WriteFile", StringComparison.OrdinalIgnoreCase))
            {
                model.WriteFilePath = null;
                model.WriteFileContent = null;
                model.WriteFileEncoding = null;
                model.WriteFileOverwrite = false;
                model.WriteFileSourceMode = null;
                model.UploadedWriteFileName = null;
                model.UploadedWriteFileDownloadUrl = null;
            }

            if (string.Equals(model.CommandType, "WriteFile", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(model.WriteFileEncoding))
            {
                model.WriteFileEncoding = "utf-8";
            }
        }

        private static string? BuildParametersJson(EditOrchestrationTemplateStepViewModel model, InstallPackageOptionData? selectedPackage = null)
        {
            if (string.IsNullOrWhiteSpace(model.CommandType))
                return null;

            if (model.CommandType.Equals("ValidateProcess", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(model.ValidateProcessName))
                    return null;

                return System.Text.Json.JsonSerializer.Serialize(new
                {
                    processName = model.ValidateProcessName.Trim()
                });
            }

            if (model.CommandType.Equals("InstallPackage", StringComparison.OrdinalIgnoreCase))
            {
                if (selectedPackage == null)
                    return null;

                var payload = new InstallPackagePayloadModel
                {
                    PackageId = selectedPackage.Id,
                    DownloadUrl = selectedPackage.StoragePath ?? string.Empty,
                    FileName = selectedPackage.FileName ?? string.Empty,
                    Sha256 = selectedPackage.Sha256 ?? string.Empty,
                    InstallCommand = selectedPackage.ExecutionCommand ?? string.Empty,
                    InstallArguments = selectedPackage.ExecutionArguments,
                    TimeoutSeconds = model.TimeoutSeconds,
                    ExecuteMode = string.IsNullOrWhiteSpace(model.InstallPackageExecuteMode)
                        ? "Immediate"
                        : model.InstallPackageExecuteMode.Trim()
                };

                return System.Text.Json.JsonSerializer.Serialize(payload);
            }

            if (model.CommandType.Equals("ImportRegistryFile", StringComparison.OrdinalIgnoreCase))
            {
                if (selectedPackage == null)
                    return null;

                var payload = new
                {
                    sourceMode = "Package",
                    packageId = selectedPackage.Id,
                    downloadUrl = selectedPackage.StoragePath ?? string.Empty,
                    fileName = selectedPackage.FileName ?? string.Empty,
                    sha256 = selectedPackage.Sha256 ?? string.Empty,
                    timeoutSeconds = model.TimeoutSeconds,
                    executeMode = string.IsNullOrWhiteSpace(model.ImportRegistryExecuteMode)
                        ? "Immediate"
                        : model.ImportRegistryExecuteMode.Trim()
                };

                return System.Text.Json.JsonSerializer.Serialize(payload);
            }

            if (model.CommandType.Equals("ValidateRegistry", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(model.ValidateRegistryHive)
                    || string.IsNullOrWhiteSpace(model.ValidateRegistryKeyPath)
                    || string.IsNullOrWhiteSpace(model.ValidateRegistryValueName))
                {
                    return null;
                }

                return System.Text.Json.JsonSerializer.Serialize(new
                {
                    hive = model.ValidateRegistryHive.Trim(),
                    keyPath = model.ValidateRegistryKeyPath.Trim(),
                    valueName = model.ValidateRegistryValueName.Trim(),
                    expectedValue = model.ValidateRegistryExpectedValue ?? string.Empty
                });
            }

            if (model.CommandType.Equals("WriteFile", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(model.WriteFileSourceMode, "Upload", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(model.WriteFilePath)
                    || string.IsNullOrWhiteSpace(model.WriteFileEncoding))
                {
                    return null;
                }

                return System.Text.Json.JsonSerializer.Serialize(new
                {
                    sourceMode = "Inline",
                    path = model.WriteFilePath.Trim(),
                    content = model.WriteFileContent ?? string.Empty,
                    encoding = model.WriteFileEncoding.Trim(),
                    overwrite = model.WriteFileOverwrite
                });
            }

            if (model.CommandType.Equals("RestartPOS", StringComparison.OrdinalIgnoreCase))
                return null;

            if (model.CommandType.Equals("CollectSystemInfo", StringComparison.OrdinalIgnoreCase))
                return null;

            return null;
        }

        private static void PopulateGuidedParametersFromJson(EditOrchestrationTemplateStepViewModel model, string? parametersJson)
        {
            ApplyCommandTypeMetadata(model);

            if (string.IsNullOrWhiteSpace(parametersJson) || string.IsNullOrWhiteSpace(model.CommandType))
                return;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(parametersJson);
                var root = doc.RootElement;

                if (model.CommandType.Equals("ValidateProcess", StringComparison.OrdinalIgnoreCase))
                {
                    if (root.TryGetProperty("processName", out var processName))
                    {
                        model.ValidateProcessName = processName.GetString();
                    }

                    return;
                }

                if (model.CommandType.Equals("InstallPackage", StringComparison.OrdinalIgnoreCase))
                {
                    if (root.TryGetProperty("packageId", out var packageId) && packageId.TryGetInt32(out var parsedPackageId))
                    {
                        model.InstallPackageId = parsedPackageId;
                    }

                    if (root.TryGetProperty("executeMode", out var executeMode))
                    {
                        model.InstallPackageExecuteMode = executeMode.GetString();
                    }

                    return;
                }

                if (model.CommandType.Equals("ImportRegistryFile", StringComparison.OrdinalIgnoreCase))
                {
                    if (root.TryGetProperty("sourceMode", out var sourceModeProp))
                    {
                        model.ImportRegistrySourceMode = sourceModeProp.GetString();
                    }

                    if (root.TryGetProperty("packageId", out var packageId) && packageId.TryGetInt32(out var parsedPackageId))
                    {
                        model.ImportRegistryPackageId = parsedPackageId;
                    }

                    if (root.TryGetProperty("fileName", out var fileNameProp))
                    {
                        model.UploadedRegistryFileName = fileNameProp.GetString();
                    }

                    if (root.TryGetProperty("downloadUrl", out var downloadUrlProp))
                    {
                        model.UploadedRegistryDownloadUrl = downloadUrlProp.GetString();
                    }

                    if (root.TryGetProperty("executeMode", out var executeMode))
                    {
                        model.ImportRegistryExecuteMode = executeMode.GetString();
                    }

                    return;
                }

                if (model.CommandType.Equals("ValidateRegistry", StringComparison.OrdinalIgnoreCase))
                {
                    if (root.TryGetProperty("hive", out var hiveProp))
                    {
                        model.ValidateRegistryHive = hiveProp.GetString();
                    }

                    if (root.TryGetProperty("keyPath", out var keyPathProp))
                    {
                        model.ValidateRegistryKeyPath = keyPathProp.GetString();
                    }

                    if (root.TryGetProperty("valueName", out var valueNameProp))
                    {
                        model.ValidateRegistryValueName = valueNameProp.GetString();
                    }

                    if (root.TryGetProperty("expectedValue", out var expectedValueProp))
                    {
                        model.ValidateRegistryExpectedValue = expectedValueProp.GetString();
                    }

                    return;
                }

                if (model.CommandType.Equals("WriteFile", StringComparison.OrdinalIgnoreCase))
                {
                    if (root.TryGetProperty("sourceMode", out var sourceModeProp))
                    {
                        model.WriteFileSourceMode = sourceModeProp.GetString();
                    }

                    if (root.TryGetProperty("path", out var pathProp))
                    {
                        model.WriteFilePath = pathProp.GetString();
                    }

                    if (root.TryGetProperty("content", out var contentProp))
                    {
                        model.WriteFileContent = contentProp.GetString();
                    }

                    if (root.TryGetProperty("encoding", out var encodingProp))
                    {
                        model.WriteFileEncoding = encodingProp.GetString();
                    }

                    if (root.TryGetProperty("overwrite", out var overwriteProp))
                    {
                        model.WriteFileOverwrite = overwriteProp.GetBoolean();
                    }

                    if (root.TryGetProperty("fileName", out var fileNameProp))
                    {
                        model.UploadedWriteFileName = fileNameProp.GetString();
                    }

                    if (root.TryGetProperty("downloadUrl", out var downloadUrlProp))
                    {
                        model.UploadedWriteFileDownloadUrl = downloadUrlProp.GetString();
                    }

                    return;
                }
            }
            catch
            {
                // Ignore malformed historical JSON and allow the form to load.
            }
        }
        private static string GetStepTypeDescription(OrchestrationStepType stepType)
        {
            if (RequiresCommandType(stepType))
            {
                if (IsRecognizedButNotYetImplementedStepType(stepType))
                    return "This step type is recognized as executable, but guided command mapping is not fully implemented yet.";

                return "This step type is currently treated as command-backed and requires a Command Type.";
            }

            return "This step type is currently treated as non-command orchestration behavior. Command Type does not apply.";
        }

        private static void ApplyStepTypeMetadata(EditOrchestrationTemplateStepViewModel model)
        {
            if (!model.StepType.HasValue)
            {
                model.RequiresCommandType = false;
                model.StepTypeDescription = "Choose a step type to see how this step behaves.";
                return;
            }

            var selectedStepType = (OrchestrationStepType)model.StepType.GetValueOrDefault();
            model.RequiresCommandType = RequiresCommandType(selectedStepType);
            model.StepTypeDescription = GetStepTypeDescription(selectedStepType);

            if (!model.RequiresCommandType)
            {
                model.CommandType = null;
            }
        }

        private async Task NormalizeTemplateStepOrderAsync(int templateId, CancellationToken cancellationToken)
        {
            var steps = await _db.OrchestrationTemplateSteps
                .Where(x => x.TemplateId == templateId)
                .OrderBy(x => x.StepOrder)
                .ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);

            for (var i = 0; i < steps.Count; i++)
            {
                steps[i].StepOrder = i + 1;
            }
        }
        private async Task PopulateOrchestrationTemplateStepLists(EditOrchestrationTemplateStepViewModel model, CancellationToken cancellationToken = default)
        {
            model.ImportRegistrySourceModeOptions = new List<SelectListItem>
            {
                new SelectListItem { Value = "Package", Text = "Existing Package" },
                new SelectListItem { Value = "Upload", Text = "Upload .reg File" }
            };
            model.WriteFileSourceModeOptions = new List<SelectListItem>
            {
                new SelectListItem { Value = "Inline", Text = "Inline Content" },
                new SelectListItem { Value = "Upload", Text = "Upload File" }
            };

            model.StepTypeOptions = Enum.GetValues(typeof(OrchestrationStepType))
                .Cast<OrchestrationStepType>()
                .Select(x => new SelectListItem
                {
                    Value = ((int)x).ToString(),
                    Text = x.ToString()
                })
                .ToList();

            model.OnFailureActionOptions = Enum.GetValues(typeof(OrchestrationFailureAction))
                .Cast<OrchestrationFailureAction>()
                .Select(x => new SelectListItem
                {
                    Value = ((int)x).ToString(),
                    Text = x.ToString()
                })
                .ToList();

            model.CommandTypeOptions = GetAvailableCommandTypes()
                .Select(x => new SelectListItem
                {
                    Value = x,
                    Text = x
                })
                .ToList();

            var installPackages = await GetInstallPackageOptionsAsync(cancellationToken);

            model.InstallPackageOptions = installPackages
                .Select(x => new SelectListItem
                {
                    Value = x.Id.ToString(),
                    Text = string.IsNullOrWhiteSpace(x.Version)
                        ? $"{x.Name} ({x.FileName ?? "no file"})"
                        : $"{x.Name} {x.Version} ({x.FileName ?? "no file"})"
                })
                .ToList();

            model.ImportRegistryPackageOptions = installPackages
                .Where(x => !string.IsNullOrWhiteSpace(x.FileName) &&
                            x.FileName.EndsWith(".reg", StringComparison.OrdinalIgnoreCase))
                .Select(x => new SelectListItem
                {
                    Value = x.Id.ToString(),
                    Text = string.IsNullOrWhiteSpace(x.Version)
                        ? $"{x.Name} ({x.FileName ?? "no file"})"
                        : $"{x.Name} {x.Version} ({x.FileName ?? "no file"})"
                })
                .ToList();

            model.ImportRegistryExecuteModeOptions = new List<SelectListItem>
            {
                new SelectListItem { Value = "Immediate", Text = "Immediate" },
                new SelectListItem { Value = "StagedOnly", Text = "Staged Only" }
            };
            
            model.ValidateRegistryHiveOptions = new List<SelectListItem>
            {
                new SelectListItem { Value = "HKEY_LOCAL_MACHINE", Text = "HKEY_LOCAL_MACHINE" },
                new SelectListItem { Value = "HKEY_CURRENT_USER", Text = "HKEY_CURRENT_USER" },
                new SelectListItem { Value = "HKEY_USERS", Text = "HKEY_USERS" },
                new SelectListItem { Value = "HKEY_CLASSES_ROOT", Text = "HKEY_CLASSES_ROOT" },
                new SelectListItem { Value = "HKEY_CURRENT_CONFIG", Text = "HKEY_CURRENT_CONFIG" }
            };

            model.InstallPackageExecuteModeOptions = new List<SelectListItem>
            {
                new SelectListItem { Value = "Immediate", Text = "Immediate" },
                new SelectListItem { Value = "StagedOnly", Text = "Staged Only" }
            };

            model.WriteFileEncodingOptions = new List<SelectListItem>
            {
                new SelectListItem { Value = "utf-8", Text = "UTF-8" },
                new SelectListItem { Value = "utf-8-bom", Text = "UTF-8 with BOM" },
                new SelectListItem { Value = "ascii", Text = "ASCII" },
                new SelectListItem { Value = "unicode", Text = "Unicode (UTF-16 LE)" }
            };

            if (model.InstallPackageId.HasValue)
            {
                var selectedPackage = installPackages.FirstOrDefault(x => x.Id == model.InstallPackageId.Value);
                if (selectedPackage != null)
                {
                    model.SelectedPackageSummary =
                        $"File: {selectedPackage.FileName ?? "-"} | " +
                        $"Source: {selectedPackage.StoragePath ?? "-"} | " +
                        $"Install Command: {selectedPackage.ExecutionCommand ?? "-"}";
                }
            }

            if (model.ImportRegistryPackageId.HasValue)
            {
                var selectedRegistryPackage = installPackages.FirstOrDefault(x => x.Id == model.ImportRegistryPackageId.Value);
                if (selectedRegistryPackage != null)
                {
                    model.SelectedRegistryPackageSummary =
                        $"File: {selectedRegistryPackage.FileName ?? "-"} | " +
                        $"Source: {selectedRegistryPackage.StoragePath ?? "-"}";
                }
            }

            if (string.Equals(model.ImportRegistrySourceMode, "Upload", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(model.UploadedRegistryFileName)
                && string.IsNullOrWhiteSpace(model.SelectedRegistryPackageSummary))
            {
                model.SelectedRegistryPackageSummary =
                    !string.IsNullOrWhiteSpace(model.UploadedRegistryDownloadUrl)
                        ? $"Uploaded file: {model.UploadedRegistryFileName} | Source: {model.UploadedRegistryDownloadUrl}"
                        : $"Uploaded file: {model.UploadedRegistryFileName}";
            }

            ApplyStepTypeMetadata(model);
            ApplyCommandTypeMetadata(model);
        }
        private Task PopulateOrchestrationTemplateLists(EditOrchestrationTemplateViewModel model)
        {
            model.DeviceTypeOptions = new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "Select device type..." },
                new SelectListItem { Value = "Register", Text = "Register" },
                new SelectListItem { Value = "Server", Text = "Server" },
                new SelectListItem { Value = "Kiosk", Text = "Kiosk" },
                new SelectListItem { Value = "Mobile", Text = "Mobile" }
            };

                    model.EnvironmentOptions = new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "Select environment..." },
                new SelectListItem { Value = "Production", Text = "Production" },
                new SelectListItem { Value = "Staging", Text = "Staging" },
                new SelectListItem { Value = "QA", Text = "QA" },
                new SelectListItem { Value = "Dev", Text = "Dev" },
                new SelectListItem { Value = "Lab", Text = "Lab" }
            };

            model.TriggerTypeOptions = Enum.GetValues(typeof(OrchestrationTriggerType))
                .Cast<OrchestrationTriggerType>()
                .Select(x => new SelectListItem
                {
                    Value = ((int)x).ToString(),
                    Text = x.ToString()
                })
                .ToList();

            return Task.CompletedTask;
        }
        private sealed class InstallPackagePayloadModel
        {
            public int PackageId { get; set; }
            public string DownloadUrl { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
            public string Sha256 { get; set; } = string.Empty;
            public string InstallCommand { get; set; } = string.Empty;
            public string? InstallArguments { get; set; }
            public int TimeoutSeconds { get; set; }
            public string ExecuteMode { get; set; } = "Immediate";
        }

        private sealed class InstallPackageOptionData
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string? Version { get; set; }
            public string? FileName { get; set; }
            public string? StoragePath { get; set; }
            public string? Sha256 { get; set; }
            public string? ExecutionCommand { get; set; }
            public string? ExecutionArguments { get; set; }
            public int TimeoutSeconds { get; set; }
        }

        private async Task<List<InstallPackageOptionData>> GetInstallPackageOptionsAsync(CancellationToken cancellationToken)
        {
            var packages = await _deploymentService.GetPackagesAsync(cancellationToken);

            return packages
                .OrderBy(p => p.Name)
                .ThenBy(p => p.Version)
                .Select(p => new InstallPackageOptionData
                {
                    Id = p.Id,
                    Name = p.Name ?? $"Package {p.Id}",
                    Version = p.Version,
                    FileName = p.FileName,
                    StoragePath = p.StoragePath,
                    Sha256 = p.Sha256,
                    ExecutionCommand = p.ExecutionCommand,
                    ExecutionArguments = p.ExecutionArguments,
                    TimeoutSeconds = p.TimeoutSeconds
                })
                .ToList();
        }
        private async Task PopulateCreateOrchestrationRunLists(CreateOrchestrationRunViewModel model, CancellationToken cancellationToken)
        {
            var cutoff = OnlineCutoffUtc;

            model.DeviceOptions = await _db.Devices
                .AsNoTracking()
                .Where(d => d.IsEnabled)
                .OrderBy(d => d.StoreNumber)
                .ThenBy(d => d.Hostname)
                .Select(d => new SelectListItem
                {
                    Value = d.DeviceId.ToString(),
                    Text = $"{d.StoreNumber} - {d.Hostname} {(d.LastSeenUtc >= cutoff ? "(Online)" : "(Offline)")}"
                })
                .ToListAsync(cancellationToken);
        }
        private async Task PopulateProvisioningProfileLists(EditProvisioningProfileViewModel model)
        {
            model.TemplateOptions = await _db.OrchestrationTemplates
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Name)
                .ThenByDescending(x => x.Version)
                .Select(x => new SelectListItem
                {
                    Value = x.Id.ToString(),
                    Text = string.IsNullOrWhiteSpace(x.DeviceType) && string.IsNullOrWhiteSpace(x.Environment)
                        ? $"{x.Name} v{x.Version}"
                        : $"{x.Name} v{x.Version} ({x.DeviceType ?? "-"} / {x.Environment ?? "-"})"
                })
                .ToListAsync();

            model.DeviceTypeOptions = new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "Select device type..." },
                new SelectListItem { Value = "Register", Text = "Register" },
                new SelectListItem { Value = "Server", Text = "Server" },
                new SelectListItem { Value = "Kiosk", Text = "Kiosk" },
                new SelectListItem { Value = "Mobile", Text = "Mobile" }
            };

                    model.EnvironmentOptions = new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "Select environment..." },
                new SelectListItem { Value = "Production", Text = "Production" },
                new SelectListItem { Value = "Staging", Text = "Staging" },
                new SelectListItem { Value = "QA", Text = "QA" },
                new SelectListItem { Value = "Dev", Text = "Dev" },
                new SelectListItem { Value = "Lab", Text = "Lab" }
            };
        }

        /// <summary>
        /// Maps a helpdesk command key to the actual queued command type and payload.
        /// These map to safer named commands rather than generic process execution.
        /// </summary>
        private static (string Type, string? PayloadJson) MapHelpdeskCommand(string commandKey)
        {
            return commandKey switch
            {
                "CollectSystemInfo" => ("CollectSystemInfo", "{}"),
                "CollectProcessStatus" => ("CollectProcessStatus", "{}"),
                "RestartPOS" => ("RestartPOS", "{}"),
                "RestartRetailShell" => ("RestartRetailShell", "{}"),
                "RestartAgent" => ("RestartAgent", "{}"),
                "RebootDevice" => ("RebootDevice", "{}"),
                _ => throw new InvalidOperationException($"Unknown helpdesk command key '{commandKey}'.")
            };
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
                NextAttemptUtc = issuedUtc,
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
            var cutoff = OnlineCutoffUtc;

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

            var deviceOptions = await _db.Devices
                .Where(d => d.IsEnabled)
                .OrderBy(d => d.StoreNumber)
                .ThenBy(d => d.Hostname)
                .Select(d => new
                {
                    value = d.DeviceId.ToString(),
                    text = (d.StoreNumber ?? "") + " - " + (d.Hostname ?? "") + (d.LastSeenUtc >= cutoff ? " (Online)" : " (Offline)")
                })
                .ToListAsync(cancellationToken);

            var storeOptions = await _db.Devices
                .Where(d => d.IsEnabled && d.StoreNumber != null && d.StoreNumber != "")
                .Select(d => d.StoreNumber!)
                .Distinct()
                .OrderBy(s => s)
                .Select(s => new
                {
                    value = s,
                    text = s
                })
                .ToListAsync(cancellationToken);

            var groupOptions = await _db.DeviceGroups
                .OrderBy(g => g.GroupName)
                .Select(g => new
                {
                    value = g.GroupName,
                    text = g.GroupName
                })
                .ToListAsync(cancellationToken);

            var fleetOptions = new[]
            {
                new { value = "FLEET", text = "Entire Fleet" }
            };

            ViewData["DeploymentDeviceOptionsJson"] = System.Text.Json.JsonSerializer.Serialize(deviceOptions);
            ViewData["DeploymentStoreOptionsJson"] = System.Text.Json.JsonSerializer.Serialize(storeOptions);
            ViewData["DeploymentGroupOptionsJson"] = System.Text.Json.JsonSerializer.Serialize(groupOptions);
            ViewData["DeploymentFleetOptionsJson"] = System.Text.Json.JsonSerializer.Serialize(fleetOptions);
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

                var normalizedFileName = string.IsNullOrWhiteSpace(model.FileName) ? "" : model.FileName.Trim();
                var normalizedStoragePath = string.IsNullOrWhiteSpace(model.StoragePath) ? "" : model.StoragePath.Trim();
                var normalizedSha256 = string.IsNullOrWhiteSpace(model.Sha256) ? "" : model.Sha256.Trim();

                string? executionCommand;
                string? executionArguments;
                string? workingDirectory = null;

                switch (model.PackageType)
                {
                    case 1: // MSI
                        executionCommand = "MsiexecInstall";
                        executionArguments = "/i {file} /qn /norestart";
                        break;

                    case 2: // EXE
                        executionCommand = "__PACKAGE_EXE__";
                        executionArguments = string.IsNullOrWhiteSpace(model.ExecutionArguments)
                            ? null
                            : model.ExecutionArguments.Trim();
                        break;

                    default:
                        executionCommand = string.IsNullOrWhiteSpace(model.ExecutionCommand)
                            ? null
                            : model.ExecutionCommand.Trim();

                        executionArguments = string.IsNullOrWhiteSpace(model.ExecutionArguments)
                            ? null
                            : model.ExecutionArguments.Trim();

                        workingDirectory = string.IsNullOrWhiteSpace(model.WorkingDirectory)
                            ? null
                            : model.WorkingDirectory.Trim();
                        break;
                }

                var request = new CreatePackageRequest
                {
                    Name = model.Name.Trim(),
                    Version = model.Version?.Trim(),
                    Description = model.Description?.Trim(),
                    PackageType = model.PackageType,
                    FileName = normalizedFileName,
                    StoragePath = normalizedStoragePath,
                    Sha256 = normalizedSha256,
                    FileSizeBytes = model.FileSizeBytes,
                    ExecutionCommand = executionCommand,
                    ExecutionArguments = executionArguments,
                    WorkingDirectory = workingDirectory,
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
        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateOrchestrationTemplateStep(EditOrchestrationTemplateStepViewModel model, CancellationToken cancellationToken)
        {
            var template = await _db.OrchestrationTemplates
                .FirstOrDefaultAsync(x => x.Id == model.TemplateId, cancellationToken);

            if (template == null)
                return NotFound();

            model.TemplateName = template.Name ?? $"Template {template.Id}";
            ApplyStepTypeMetadata(model);
            ApplyCommandTypeMetadata(model);
            var installPackages = await GetInstallPackageOptionsAsync(cancellationToken);
            InstallPackageOptionData? selectedPackage = null;

            if (string.Equals(model.CommandType, "InstallPackage", StringComparison.OrdinalIgnoreCase))
            {
                if (!model.InstallPackageId.HasValue)
                {
                    ModelState.AddModelError(nameof(model.InstallPackageId), "Package selection is required for InstallPackage.");
                }
                else
                {
                    selectedPackage = installPackages.FirstOrDefault(x => x.Id == model.InstallPackageId.Value);

                    if (selectedPackage == null)
                    {
                        ModelState.AddModelError(nameof(model.InstallPackageId), "Selected package was not found.");
                    }
                    else if (string.IsNullOrWhiteSpace(selectedPackage.StoragePath)
                        || string.IsNullOrWhiteSpace(selectedPackage.FileName)
                        || string.IsNullOrWhiteSpace(selectedPackage.ExecutionCommand))
                    {
                        ModelState.AddModelError(nameof(model.InstallPackageId), "Selected package is missing required install metadata.");
                    }
                }
            }

            if (string.Equals(model.CommandType, "ImportRegistryFile", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(model.ImportRegistrySourceMode))
                {
                    ModelState.AddModelError(nameof(model.ImportRegistrySourceMode), "Registry source mode is required.");
                }
                else if (string.Equals(model.ImportRegistrySourceMode, "Package", StringComparison.OrdinalIgnoreCase))
                {
                    if (!model.ImportRegistryPackageId.HasValue)
                    {
                        ModelState.AddModelError(nameof(model.ImportRegistryPackageId), "Package selection is required for ImportRegistryFile.");
                    }
                    else
                    {
                        selectedPackage = installPackages.FirstOrDefault(x => x.Id == model.ImportRegistryPackageId.Value);

                        if (selectedPackage == null)
                        {
                            ModelState.AddModelError(nameof(model.ImportRegistryPackageId), "Selected registry package was not found.");
                        }
                        else if (string.IsNullOrWhiteSpace(selectedPackage.StoragePath)
                            || string.IsNullOrWhiteSpace(selectedPackage.FileName)
                            || !selectedPackage.FileName.EndsWith(".reg", StringComparison.OrdinalIgnoreCase))
                        {
                            ModelState.AddModelError(nameof(model.ImportRegistryPackageId), "Selected package must be a valid .reg package.");
                        }
                    }
                }
                else if (string.Equals(model.ImportRegistrySourceMode, "Upload", StringComparison.OrdinalIgnoreCase))
                {
                    if (model.ImportRegistryUploadFile == null || model.ImportRegistryUploadFile.Length == 0)
                    {
                        ModelState.AddModelError(nameof(model.ImportRegistryUploadFile), "A .reg file upload is required.");
                    }
                    else if (!Path.GetExtension(model.ImportRegistryUploadFile.FileName).Equals(".reg", StringComparison.OrdinalIgnoreCase))
                    {
                        ModelState.AddModelError(nameof(model.ImportRegistryUploadFile), "Only .reg files are supported.");
                    }
                }
            }

            if (string.Equals(model.CommandType, "ValidateRegistry", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(model.ValidateRegistryHive))
                {
                    ModelState.AddModelError(nameof(model.ValidateRegistryHive), "Registry hive is required for ValidateRegistry.");
                }

                if (string.IsNullOrWhiteSpace(model.ValidateRegistryKeyPath))
                {
                    ModelState.AddModelError(nameof(model.ValidateRegistryKeyPath), "Registry key path is required for ValidateRegistry.");
                }

                if (string.IsNullOrWhiteSpace(model.ValidateRegistryValueName))
                {
                    ModelState.AddModelError(nameof(model.ValidateRegistryValueName), "Registry value name is required for ValidateRegistry.");
                }
            }

            if (!model.StepType.HasValue)
            {
                ModelState.AddModelError(nameof(model.StepType), "Step type is required.");
            }

            if (!model.OnFailureAction.HasValue)
            {
                ModelState.AddModelError(nameof(model.OnFailureAction), "On failure action is required.");
            }

            if (model.StepType.HasValue)
            {
                var stepTypeForValidation = (OrchestrationStepType)model.StepType.GetValueOrDefault();
                if (RequiresCommandType(stepTypeForValidation) && string.IsNullOrWhiteSpace(model.CommandType))
                {
                    ModelState.AddModelError(nameof(model.CommandType), "Command type is required for the selected step type.");
                }
            }

            if (string.Equals(model.CommandType, "ValidateProcess", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(model.ValidateProcessName))
            {
                ModelState.AddModelError(nameof(model.ValidateProcessName), "Process name is required for ValidateProcess.");
            }

            if (string.Equals(model.CommandType, "WriteFile", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(model.WriteFilePath))
                {
                    ModelState.AddModelError(nameof(model.WriteFilePath), "Destination path is required for WriteFile.");
                }

                if (string.IsNullOrWhiteSpace(model.WriteFileSourceMode))
                {
                    ModelState.AddModelError(nameof(model.WriteFileSourceMode), "Source mode is required for WriteFile.");
                }
                else if (string.Equals(model.WriteFileSourceMode, "Inline", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(model.WriteFileEncoding))
                    {
                        ModelState.AddModelError(nameof(model.WriteFileEncoding), "Encoding is required for WriteFile inline mode.");
                    }
                }
                else if (string.Equals(model.WriteFileSourceMode, "Upload", StringComparison.OrdinalIgnoreCase))
                {
                    if (model.WriteFileUploadFile == null || model.WriteFileUploadFile.Length == 0)
                    {
                        ModelState.AddModelError(nameof(model.WriteFileUploadFile), "A file upload is required for WriteFile upload mode.");
                    }
                }
            }
            if (string.Equals(model.CommandType, "ImportRegistryFile", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(model.ImportRegistrySourceMode))
                {
                    ModelState.AddModelError(nameof(model.ImportRegistrySourceMode), "Registry source mode is required for ImportRegistryFile.");
                }

                if (string.IsNullOrWhiteSpace(model.ImportRegistryExecuteMode))
                {
                    ModelState.AddModelError(nameof(model.ImportRegistryExecuteMode), "Execute mode is required for ImportRegistryFile.");
                }
            }

            if (!ModelState.IsValid)
            {
                await PopulateOrchestrationTemplateStepLists(model, cancellationToken);
                ViewData["Title"] = $"Add Step to {model.TemplateName}";
                ViewData["FormAction"] = nameof(CreateOrchestrationTemplateStep);
                ViewData["SubmitText"] = "Add Step";
                return View("EditOrchestrationTemplateStep", model);
            }

            var duplicateOrder = await _db.OrchestrationTemplateSteps.AnyAsync(
                x => x.TemplateId == model.TemplateId && x.StepOrder == model.StepOrder,
                cancellationToken);

            if (duplicateOrder)
            {
                ModelState.AddModelError(nameof(model.StepOrder), "Another step already uses this step order.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateOrchestrationTemplateStepLists(model, cancellationToken);
                ViewData["Title"] = $"Add Step to {model.TemplateName}";
                ViewData["FormAction"] = nameof(CreateOrchestrationTemplateStep);
                ViewData["SubmitText"] = "Add Step";
                return View("EditOrchestrationTemplateStep", model);
            }

            var selectedStepTypeValue = model.StepType.GetValueOrDefault();
            var selectedStepType = (OrchestrationStepType)selectedStepTypeValue;
            var normalizedCommandType = RequiresCommandType(selectedStepType)
                ? model.CommandType?.Trim()
                : null;

            string? parametersJson = null;

            if (string.Equals(model.CommandType, "WriteFile", StringComparison.OrdinalIgnoreCase)
                && string.Equals(model.WriteFileSourceMode, "Upload", StringComparison.OrdinalIgnoreCase))
            {
                var uploadValidationError = ValidateDistroUpload(model.WriteFileUploadFile!);
                if (uploadValidationError != null)
                {
                    ModelState.AddModelError(nameof(model.WriteFileUploadFile), uploadValidationError);

                    await PopulateOrchestrationTemplateStepLists(model, cancellationToken);
                    ViewData["Title"] = $"Add Step to {model.TemplateName}";
                    ViewData["FormAction"] = nameof(CreateOrchestrationTemplateStep);
                    ViewData["SubmitText"] = "Add Step";
                    return View("EditOrchestrationTemplateStep", model);
                }

                var distroResult = await SaveFileToDistroAsync(model.WriteFileUploadFile!, cancellationToken);
                model.UploadedWriteFileName = distroResult.FileName;
                model.UploadedWriteFileDownloadUrl = BuildDistroUrl(distroResult.FileName);

                parametersJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    sourceMode = "Upload",
                    downloadUrl = model.UploadedWriteFileDownloadUrl,
                    fileName = distroResult.FileName,
                    path = model.WriteFilePath?.Trim() ?? string.Empty,
                    overwrite = model.WriteFileOverwrite
                });
            }
            else if (string.Equals(model.CommandType, "ImportRegistryFile", StringComparison.OrdinalIgnoreCase)
                && string.Equals(model.ImportRegistrySourceMode, "Upload", StringComparison.OrdinalIgnoreCase))
            {
                var uploadValidationError = ValidateDistroUpload(model.ImportRegistryUploadFile!);
                if (uploadValidationError != null)
                {
                    ModelState.AddModelError(nameof(model.ImportRegistryUploadFile), uploadValidationError);

                    await PopulateOrchestrationTemplateStepLists(model, cancellationToken);
                    ViewData["Title"] = $"Add Step to {model.TemplateName}";
                    ViewData["FormAction"] = nameof(CreateOrchestrationTemplateStep);
                    ViewData["SubmitText"] = "Add Step";
                    return View("EditOrchestrationTemplateStep", model);
                }

                var distroResult = await SaveFileToDistroAsync(model.ImportRegistryUploadFile!, cancellationToken);
                model.UploadedRegistryFileName = distroResult.FileName;
                model.UploadedRegistryDownloadUrl = BuildDistroUrl(distroResult.FileName);

                parametersJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    sourceMode = "Upload",
                    downloadUrl = model.UploadedRegistryDownloadUrl,
                    fileName = distroResult.FileName,
                    sha256 = distroResult.Sha256,
                    timeoutSeconds = model.TimeoutSeconds,
                    executeMode = string.IsNullOrWhiteSpace(model.ImportRegistryExecuteMode)
                        ? "Immediate"
                        : model.ImportRegistryExecuteMode.Trim()
                });
            }
            else
            {
                parametersJson = BuildParametersJson(model, selectedPackage);
            }

            var entity = new OrchestrationTemplateStep
            {
                TemplateId = model.TemplateId,
                StepOrder = model.StepOrder,
                Name = model.Name.Trim(),
                StepType = selectedStepType,
                CommandType = normalizedCommandType,
                ParametersJson = parametersJson,
                SuccessCriteriaJson = null,
                TimeoutSeconds = model.TimeoutSeconds,
                MaxRetries = model.MaxRetries,
                OnFailureAction = (OrchestrationFailureAction)model.OnFailureAction.GetValueOrDefault(),
                ContinueOnFailure = model.ContinueOnFailure,
                RollbackTemplateStepId = null
            };

            _db.OrchestrationTemplateSteps.Add(entity);
            template.UpdatedUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            await AuditAsync(
                "CreateOrchestrationTemplateStep",
                "OrchestrationTemplateStep",
                entity.Id.ToString(),
                new
                {
                    entity.Id,
                    entity.TemplateId,
                    entity.StepOrder,
                    entity.Name,
                    entity.StepType,
                    entity.CommandType,
                    entity.ParametersJson,
                    entity.TimeoutSeconds,
                    entity.MaxRetries,
                    entity.OnFailureAction,
                    entity.ContinueOnFailure
                },
                true,
                cancellationToken: cancellationToken);

            TempData["OrchestrationTemplateMessage"] = $"Step '{entity.Name}' added.";
            return RedirectToAction(nameof(OrchestrationTemplate), new { id = model.TemplateId });
        }



        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditOrchestrationTemplateStep(int id, EditOrchestrationTemplateStepViewModel model, CancellationToken cancellationToken)
        {
            var entity = await _db.OrchestrationTemplateSteps
                .Include(x => x.Template)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (entity == null)
                return NotFound();

            model.TemplateId = entity.TemplateId;
            model.TemplateName = entity.Template?.Name ?? $"Template {entity.TemplateId}";
            ApplyStepTypeMetadata(model);
            ApplyCommandTypeMetadata(model);
            var installPackages = await GetInstallPackageOptionsAsync(cancellationToken);
            InstallPackageOptionData? selectedPackage = null;

            if (string.Equals(model.CommandType, "InstallPackage", StringComparison.OrdinalIgnoreCase))
            {
                if (!model.InstallPackageId.HasValue)
                {
                    ModelState.AddModelError(nameof(model.InstallPackageId), "Package selection is required for InstallPackage.");
                }
                else
                {
                    selectedPackage = installPackages.FirstOrDefault(x => x.Id == model.InstallPackageId.Value);

                    if (selectedPackage == null)
                    {
                        ModelState.AddModelError(nameof(model.InstallPackageId), "Selected package was not found.");
                    }
                    else if (string.IsNullOrWhiteSpace(selectedPackage.StoragePath)
                        || string.IsNullOrWhiteSpace(selectedPackage.FileName)
                        || string.IsNullOrWhiteSpace(selectedPackage.ExecutionCommand))
                    {
                        ModelState.AddModelError(nameof(model.InstallPackageId), "Selected package is missing required install metadata.");
                    }
                }
            }

            if (string.Equals(model.CommandType, "ImportRegistryFile", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(model.ImportRegistrySourceMode))
                {
                    ModelState.AddModelError(nameof(model.ImportRegistrySourceMode), "Registry source mode is required.");
                }
                else if (string.Equals(model.ImportRegistrySourceMode, "Package", StringComparison.OrdinalIgnoreCase))
                {
                    if (!model.ImportRegistryPackageId.HasValue)
                    {
                        ModelState.AddModelError(nameof(model.ImportRegistryPackageId), "Package selection is required for ImportRegistryFile.");
                    }
                    else
                    {
                        selectedPackage = installPackages.FirstOrDefault(x => x.Id == model.ImportRegistryPackageId.Value);

                        if (selectedPackage == null)
                        {
                            ModelState.AddModelError(nameof(model.ImportRegistryPackageId), "Selected registry package was not found.");
                        }
                        else if (string.IsNullOrWhiteSpace(selectedPackage.StoragePath)
                            || string.IsNullOrWhiteSpace(selectedPackage.FileName)
                            || !selectedPackage.FileName.EndsWith(".reg", StringComparison.OrdinalIgnoreCase))
                        {
                            ModelState.AddModelError(nameof(model.ImportRegistryPackageId), "Selected package must be a valid .reg package.");
                        }
                    }
                }
                else if (string.Equals(model.ImportRegistrySourceMode, "Upload", StringComparison.OrdinalIgnoreCase))
                {
                    if (model.ImportRegistryUploadFile == null || model.ImportRegistryUploadFile.Length == 0)
                    {
                        ModelState.AddModelError(nameof(model.ImportRegistryUploadFile), "A .reg file upload is required.");
                    }
                    else if (!Path.GetExtension(model.ImportRegistryUploadFile.FileName).Equals(".reg", StringComparison.OrdinalIgnoreCase))
                    {
                        ModelState.AddModelError(nameof(model.ImportRegistryUploadFile), "Only .reg files are supported.");
                    }
                }
            }

            if (string.Equals(model.CommandType, "ValidateRegistry", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(model.ValidateRegistryHive))
                {
                    ModelState.AddModelError(nameof(model.ValidateRegistryHive), "Registry hive is required for ValidateRegistry.");
                }

                if (string.IsNullOrWhiteSpace(model.ValidateRegistryKeyPath))
                {
                    ModelState.AddModelError(nameof(model.ValidateRegistryKeyPath), "Registry key path is required for ValidateRegistry.");
                }

                if (string.IsNullOrWhiteSpace(model.ValidateRegistryValueName))
                {
                    ModelState.AddModelError(nameof(model.ValidateRegistryValueName), "Registry value name is required for ValidateRegistry.");
                }
            }

            if (!model.StepType.HasValue)
            {
                ModelState.AddModelError(nameof(model.StepType), "Step type is required.");
            }

            if (!model.OnFailureAction.HasValue)
            {
                ModelState.AddModelError(nameof(model.OnFailureAction), "On failure action is required.");
            }

            if (model.StepType.HasValue)
            {
                var stepTypeForValidation = (OrchestrationStepType)model.StepType.GetValueOrDefault();
                if (RequiresCommandType(stepTypeForValidation) && string.IsNullOrWhiteSpace(model.CommandType))
                {
                    ModelState.AddModelError(nameof(model.CommandType), "Command type is required for the selected step type.");
                }
            }

            if (string.Equals(model.CommandType, "ValidateProcess", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(model.ValidateProcessName))
            {
                ModelState.AddModelError(nameof(model.ValidateProcessName), "Process name is required for ValidateProcess.");
            }

            if (string.Equals(model.CommandType, "WriteFile", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(model.WriteFilePath))
                {
                    ModelState.AddModelError(nameof(model.WriteFilePath), "Destination path is required for WriteFile.");
                }

                if (string.IsNullOrWhiteSpace(model.WriteFileSourceMode))
                {
                    ModelState.AddModelError(nameof(model.WriteFileSourceMode), "Source mode is required for WriteFile.");
                }
                else if (string.Equals(model.WriteFileSourceMode, "Inline", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(model.WriteFileEncoding))
                    {
                        ModelState.AddModelError(nameof(model.WriteFileEncoding), "Encoding is required for WriteFile inline mode.");
                    }
                }
                else if (string.Equals(model.WriteFileSourceMode, "Upload", StringComparison.OrdinalIgnoreCase))
                {
                    if (model.WriteFileUploadFile == null || model.WriteFileUploadFile.Length == 0)
                    {
                        ModelState.AddModelError(nameof(model.WriteFileUploadFile), "A file upload is required for WriteFile upload mode.");
                    }
                }
            }

            if (string.Equals(model.CommandType, "ImportRegistryFile", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(model.ImportRegistrySourceMode))
                {
                    ModelState.AddModelError(nameof(model.ImportRegistrySourceMode), "Registry source mode is required for ImportRegistryFile.");
                }

                if (string.IsNullOrWhiteSpace(model.ImportRegistryExecuteMode))
                {
                    ModelState.AddModelError(nameof(model.ImportRegistryExecuteMode), "Execute mode is required for ImportRegistryFile.");
                }
            }

            if (!ModelState.IsValid)
            {
                await PopulateOrchestrationTemplateStepLists(model, cancellationToken);
                ViewData["Title"] = $"Edit Step: {model.Name}";
                ViewData["FormAction"] = nameof(EditOrchestrationTemplateStep);
                ViewData["SubmitText"] = "Save Step Changes";
                return View("EditOrchestrationTemplateStep", model);
            }

            var duplicateOrder = await _db.OrchestrationTemplateSteps.AnyAsync(
                x => x.TemplateId == entity.TemplateId && x.Id != id && x.StepOrder == model.StepOrder,
                cancellationToken);

            if (duplicateOrder)
            {
                ModelState.AddModelError(nameof(model.StepOrder), "Another step already uses this step order.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateOrchestrationTemplateStepLists(model, cancellationToken);
                ViewData["Title"] = $"Edit Step: {model.Name}";
                ViewData["FormAction"] = nameof(EditOrchestrationTemplateStep);
                ViewData["SubmitText"] = "Save Step Changes";
                return View("EditOrchestrationTemplateStep", model);
            }

            var selectedStepTypeValue = model.StepType.GetValueOrDefault();
            var selectedStepType = (OrchestrationStepType)selectedStepTypeValue;
            var normalizedCommandType = RequiresCommandType(selectedStepType)
                ? model.CommandType?.Trim()
                : null;

            string? parametersJson = null;

            if (string.Equals(model.CommandType, "WriteFile", StringComparison.OrdinalIgnoreCase)
                && string.Equals(model.WriteFileSourceMode, "Upload", StringComparison.OrdinalIgnoreCase))
            {
                var uploadValidationError = ValidateDistroUpload(model.WriteFileUploadFile!);
                if (uploadValidationError != null)
                {
                    ModelState.AddModelError(nameof(model.WriteFileUploadFile), uploadValidationError);

                    await PopulateOrchestrationTemplateStepLists(model, cancellationToken);
                    ViewData["Title"] = $"Edit Step: {model.Name}";
                    ViewData["FormAction"] = nameof(EditOrchestrationTemplateStep);
                    ViewData["SubmitText"] = "Save Step Changes";
                    return View("EditOrchestrationTemplateStep", model);
                }

                var distroResult = await SaveFileToDistroAsync(model.WriteFileUploadFile!, cancellationToken);
                model.UploadedWriteFileName = distroResult.FileName;
                model.UploadedWriteFileDownloadUrl = BuildDistroUrl(distroResult.FileName);

                parametersJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    sourceMode = "Upload",
                    downloadUrl = model.UploadedWriteFileDownloadUrl,
                    fileName = distroResult.FileName,
                    path = model.WriteFilePath?.Trim() ?? string.Empty,
                    overwrite = model.WriteFileOverwrite
                });
            }
            else if (string.Equals(model.CommandType, "ImportRegistryFile", StringComparison.OrdinalIgnoreCase)
                && string.Equals(model.ImportRegistrySourceMode, "Upload", StringComparison.OrdinalIgnoreCase))
            {
                var uploadValidationError = ValidateDistroUpload(model.ImportRegistryUploadFile!);
                if (uploadValidationError != null)
                {
                    ModelState.AddModelError(nameof(model.ImportRegistryUploadFile), uploadValidationError);

                    await PopulateOrchestrationTemplateStepLists(model, cancellationToken);
                    ViewData["Title"] = $"Edit Step: {model.Name}";
                    ViewData["FormAction"] = nameof(EditOrchestrationTemplateStep);
                    ViewData["SubmitText"] = "Save Step Changes";
                    return View("EditOrchestrationTemplateStep", model);
                }

                var distroResult = await SaveFileToDistroAsync(model.ImportRegistryUploadFile!, cancellationToken);
                model.UploadedRegistryFileName = distroResult.FileName;
                model.UploadedRegistryDownloadUrl = BuildDistroUrl(distroResult.FileName);

                parametersJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    sourceMode = "Upload",
                    downloadUrl = model.UploadedRegistryDownloadUrl,
                    fileName = distroResult.FileName,
                    sha256 = distroResult.Sha256,
                    timeoutSeconds = model.TimeoutSeconds,
                    executeMode = string.IsNullOrWhiteSpace(model.ImportRegistryExecuteMode)
                        ? "Immediate"
                        : model.ImportRegistryExecuteMode.Trim()
                });
            }
            else
            {
                parametersJson = BuildParametersJson(model, selectedPackage);
            }
            entity.StepOrder = model.StepOrder;
            entity.Name = model.Name.Trim();
            entity.StepType = selectedStepType;
            entity.CommandType = normalizedCommandType;
            entity.ParametersJson = parametersJson;
            entity.TimeoutSeconds = model.TimeoutSeconds;
            entity.MaxRetries = model.MaxRetries;
            entity.OnFailureAction = (OrchestrationFailureAction)model.OnFailureAction.GetValueOrDefault();
            entity.ContinueOnFailure = model.ContinueOnFailure;

            if (entity.Template != null)
            {
                entity.Template.UpdatedUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(cancellationToken);

            await AuditAsync(
                "EditOrchestrationTemplateStep",
                "OrchestrationTemplateStep",
                entity.Id.ToString(),
                new
                {
                    entity.Id,
                    entity.TemplateId,
                    entity.StepOrder,
                    entity.Name,
                    entity.StepType,
                    entity.CommandType,
                    entity.ParametersJson,
                    entity.TimeoutSeconds,
                    entity.MaxRetries,
                    entity.OnFailureAction,
                    entity.ContinueOnFailure
                },
                true,
                cancellationToken: cancellationToken);

            TempData["OrchestrationTemplateMessage"] = $"Step '{entity.Name}' updated.";
            return RedirectToAction(nameof(OrchestrationTemplate), new { id = entity.TemplateId });
        }



        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteOrchestrationTemplateStep(int id, CancellationToken cancellationToken)
        {
            var entity = await _db.OrchestrationTemplateSteps
                .Include(x => x.Template)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (entity == null)
                return NotFound();

            var templateId = entity.TemplateId;
            var stepName = entity.Name ?? $"Step {entity.StepOrder}";

            var isReferencedByRuns = await _db.OrchestrationRunSteps
                .AnyAsync(x => x.TemplateStepId == id, cancellationToken);

            if (isReferencedByRuns)
            {
                TempData["OrchestrationTemplateMessage"] =
                    $"Step '{stepName}' cannot be deleted because it is referenced by existing orchestration runs.";

                await AuditAsync(
                    "DeleteOrchestrationTemplateStepBlocked",
                    "OrchestrationTemplateStep",
                    id.ToString(),
                    new
                    {
                        Id = id,
                        TemplateId = templateId,
                        Name = stepName,
                        Reason = "Referenced by orchestration run history"
                    },
                    false,
                    "Step is referenced by orchestration run history.",
                    cancellationToken);

                return RedirectToAction(nameof(OrchestrationTemplate), new { id = templateId });
            }

            _db.OrchestrationTemplateSteps.Remove(entity);

            if (entity.Template != null)
            {
                entity.Template.UpdatedUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(cancellationToken);

            await AuditAsync(
                 "DeleteOrchestrationTemplateStep",
                 "OrchestrationTemplateStep",
                 id.ToString(),
                 new
                 {
                     Id = id,
                     TemplateId = templateId,
                     Name = stepName
                 },
                 true,
                 cancellationToken: cancellationToken);

            TempData["OrchestrationTemplateMessage"] = $"Step '{stepName}' deleted.";
            return RedirectToAction(nameof(OrchestrationTemplate), new { id = templateId });
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpGet]
        public async Task<IActionResult> CreateOrchestrationTemplateStep(int templateId)
        {
            var template = await _db.OrchestrationTemplates
                .AsNoTracking()
                .Include(x => x.Steps)
                .FirstOrDefaultAsync(x => x.Id == templateId);

            if (template == null)
                return NotFound();

            var nextStepOrder = template.Steps.Any() ? template.Steps.Max(x => x.StepOrder) + 1 : 1;

            var model = new EditOrchestrationTemplateStepViewModel
            {
                TemplateId = template.Id,
                TemplateName = template.Name ?? $"Template {template.Id}",
                StepOrder = nextStepOrder,
                TimeoutSeconds = 300,
                MaxRetries = 1,
                ContinueOnFailure = false
            };

            await PopulateOrchestrationTemplateStepLists(model);

            ViewData["Title"] = $"Add Step to {model.TemplateName}";
            ViewData["FormAction"] = nameof(CreateOrchestrationTemplateStep);
            ViewData["SubmitText"] = "Add Step";

            return View("EditOrchestrationTemplateStep", model);
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpGet]
        public async Task<IActionResult> CreateOrchestrationRun(int templateId, CancellationToken cancellationToken)
        {
            var template = await _db.OrchestrationTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == templateId, cancellationToken);

            if (template == null)
                return NotFound();

            var model = new CreateOrchestrationRunViewModel
            {
                TemplateId = template.Id,
                TemplateName = template.Name ?? $"Template {template.Id}",
                TemplateVersion = template.Version,
                DeviceType = template.DeviceType ?? string.Empty,
                Environment = template.Environment ?? string.Empty,
                RequestedBy = CurrentActor(),
                CorrelationId = Guid.NewGuid().ToString()
            };

            await PopulateCreateOrchestrationRunLists(model, cancellationToken);

            ViewData["Title"] = $"Run Template: {model.TemplateName}";
            return View("CreateOrchestrationRun", model);
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateOrchestrationRun(CreateOrchestrationRunViewModel model, CancellationToken cancellationToken)
        {
            var template = await _db.OrchestrationTemplates
                .Include(x => x.Steps.OrderBy(s => s.StepOrder))
                .FirstOrDefaultAsync(x => x.Id == model.TemplateId, cancellationToken);

            if (template == null)
                return NotFound();

            model.TemplateName = template.Name ?? $"Template {template.Id}";
            model.TemplateVersion = template.Version;
            model.DeviceType = template.DeviceType ?? string.Empty;
            model.Environment = template.Environment ?? string.Empty;

            if (!template.IsActive)
            {
                ModelState.AddModelError(string.Empty, "Only active templates can be run.");
            }

            if (!model.DeviceId.HasValue)
            {
                ModelState.AddModelError(nameof(model.DeviceId), "Target device is required.");
            }

            Device? device = null;

            if (model.DeviceId.HasValue)
            {
                device = await _db.Devices
                    .FirstOrDefaultAsync(d => d.DeviceId == model.DeviceId.Value && d.IsEnabled, cancellationToken);

                if (device == null)
                {
                    ModelState.AddModelError(nameof(model.DeviceId), "Selected device was not found or is disabled.");
                }
            }

            if (!ModelState.IsValid)
            {
                await PopulateCreateOrchestrationRunLists(model, cancellationToken);
                ViewData["Title"] = $"Run Template: {model.TemplateName}";
                return View("CreateOrchestrationRun", model);
            }

            var correlationId = string.IsNullOrWhiteSpace(model.CorrelationId)
                ? Guid.NewGuid().ToString()
                : model.CorrelationId.Trim();

            var requestedBy = string.IsNullOrWhiteSpace(model.RequestedBy)
                ? CurrentActor()
                : model.RequestedBy.Trim();

            var parsedStoreId = int.TryParse(device!.StoreNumber, out var storeIdValue)
                ? storeIdValue
                : (int?)null;

            var parsedRegisterId = int.TryParse(device.Hostname, out var registerIdValue)
                ? registerIdValue
                : (int?)null;

            var run = new OrchestrationRun
            {
                TemplateId = template.Id,
                DeviceId = device.DeviceId,
                AgentId = null,
                StoreId = parsedStoreId,
                RegisterId = parsedRegisterId,
                Status = OrchestrationRunStatus.Pending,
                CurrentStepOrder = template.Steps.OrderBy(s => s.StepOrder).Select(s => (int?)s.StepOrder).FirstOrDefault(),
                CorrelationId = correlationId,
                RequestedBy = requestedBy,
                TriggerSource = (OrchestrationTriggerSource)template.TriggerType,
                StartedUtc = DateTime.UtcNow,
                CompletedUtc = null,
                Steps = template.Steps
                    .OrderBy(s => s.StepOrder)
                    .Select(s => new OrchestrationRunStep
                    {
                        TemplateStepId = s.Id,
                        StepOrder = s.StepOrder,
                        Status = OrchestrationRunStepStatus.Pending,
                        AttemptCount = 0,
                        CommandId = null,
                        ErrorMessage = null,
                        StartedUtc = null,
                        CompletedUtc = null
                    })
                    .ToList()
            };

            _db.OrchestrationRuns.Add(run);
            await _db.SaveChangesAsync(cancellationToken);

            await AuditAsync(
                "CreateOrchestrationRun",
                "OrchestrationRun",
                run.Id.ToString(),
                new
                {
                    run.Id,
                    run.TemplateId,
                    run.DeviceId,
                    run.StoreId,
                    run.RegisterId,
                    run.CorrelationId,
                    run.RequestedBy,
                    StepCount = run.Steps.Count
                },
                true,
                cancellationToken: cancellationToken);

            TempData["OrchestrationTemplateMessage"] = $"Run {run.Id} created successfully.";
            return RedirectToAction(nameof(OrchestrationRun), new { id = run.Id });
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpGet]
        public async Task<IActionResult> EditOrchestrationTemplateStep(int id)
        {
            var entity = await _db.OrchestrationTemplateSteps
                .AsNoTracking()
                .Include(x => x.Template)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (entity == null)
                return NotFound();

            var model = new EditOrchestrationTemplateStepViewModel
            {
                Id = entity.Id,
                TemplateId = entity.TemplateId,
                TemplateName = entity.Template?.Name ?? $"Template {entity.TemplateId}",
                StepOrder = entity.StepOrder,
                Name = entity.Name ?? string.Empty,
                StepType = (int)entity.StepType,
                CommandType = entity.CommandType,
                TimeoutSeconds = entity.TimeoutSeconds,
                MaxRetries = entity.MaxRetries,
                OnFailureAction = (int)entity.OnFailureAction,
                ContinueOnFailure = entity.ContinueOnFailure
            };

            PopulateGuidedParametersFromJson(model, entity.ParametersJson);
            await PopulateOrchestrationTemplateStepLists(model);

            ViewData["Title"] = $"Edit Step: {model.Name}";
            ViewData["FormAction"] = nameof(EditOrchestrationTemplateStep);
            ViewData["SubmitText"] = "Save Step Changes";

            return View("EditOrchestrationTemplateStep", model);
        }


        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveOrchestrationTemplateStepUp(int id, CancellationToken cancellationToken)
        {
            var step = await _db.OrchestrationTemplateSteps
                .Include(x => x.Template)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (step == null)
                return NotFound();

            await NormalizeTemplateStepOrderAsync(step.TemplateId, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            var currentOrder = step.StepOrder;

            var previousStep = await _db.OrchestrationTemplateSteps
                .Where(x => x.TemplateId == step.TemplateId && x.StepOrder < currentOrder)
                .OrderByDescending(x => x.StepOrder)
                .FirstOrDefaultAsync(cancellationToken);

            if (previousStep == null)
            {
                TempData["OrchestrationTemplateMessage"] = "Step is already at the top.";
                return RedirectToAction(nameof(OrchestrationTemplate), new { id = step.TemplateId });
            }

            var previousOrder = previousStep.StepOrder;
            var tempOrder = -1;

            step.StepOrder = tempOrder;
            await _db.SaveChangesAsync(cancellationToken);

            previousStep.StepOrder = currentOrder;
            await _db.SaveChangesAsync(cancellationToken);

            step.StepOrder = previousOrder;

            if (step.Template != null)
            {
                step.Template.UpdatedUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(cancellationToken);

            await AuditAsync(
                "MoveOrchestrationTemplateStepUp",
                "OrchestrationTemplateStep",
                step.Id.ToString(),
                new
                {
                    StepId = step.Id,
                    step.TemplateId,
                    step.Name,
                    NewStepOrder = step.StepOrder
                },
                true,
                cancellationToken: cancellationToken);

            TempData["OrchestrationTemplateMessage"] = $"Step '{step.Name}' moved up.";
            return RedirectToAction(nameof(OrchestrationTemplate), new { id = step.TemplateId });
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveOrchestrationTemplateStepDown(int id, CancellationToken cancellationToken)
        {
            var step = await _db.OrchestrationTemplateSteps
                .Include(x => x.Template)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (step == null)
                return NotFound();

            await NormalizeTemplateStepOrderAsync(step.TemplateId, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            var currentOrder = step.StepOrder;

            var nextStep = await _db.OrchestrationTemplateSteps
                .Where(x => x.TemplateId == step.TemplateId && x.StepOrder > currentOrder)
                .OrderBy(x => x.StepOrder)
                .FirstOrDefaultAsync(cancellationToken);

            if (nextStep == null)
            {
                TempData["OrchestrationTemplateMessage"] = "Step is already at the bottom.";
                return RedirectToAction(nameof(OrchestrationTemplate), new { id = step.TemplateId });
            }

            var nextOrder = nextStep.StepOrder;
            var tempOrder = -1;

            step.StepOrder = tempOrder;
            await _db.SaveChangesAsync(cancellationToken);

            nextStep.StepOrder = currentOrder;
            await _db.SaveChangesAsync(cancellationToken);

            step.StepOrder = nextOrder;

            if (step.Template != null)
            {
                step.Template.UpdatedUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(cancellationToken);

            await AuditAsync(
                "MoveOrchestrationTemplateStepDown",
                "OrchestrationTemplateStep",
                step.Id.ToString(),
                new
                {
                    StepId = step.Id,
                    step.TemplateId,
                    step.Name,
                    NewStepOrder = step.StepOrder
                },
                true,
                cancellationToken: cancellationToken);

            TempData["OrchestrationTemplateMessage"] = $"Step '{step.Name}' moved down.";
            return RedirectToAction(nameof(OrchestrationTemplate), new { id = step.TemplateId });
        }

        [Authorize(Policy = "DashboardEngineer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NormalizeOrchestrationTemplateStepOrder(int templateId, CancellationToken cancellationToken)
        {
            var template = await _db.OrchestrationTemplates
                .FirstOrDefaultAsync(x => x.Id == templateId, cancellationToken);

            if (template == null)
                return NotFound();

            await NormalizeTemplateStepOrderAsync(templateId, cancellationToken);
            template.UpdatedUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            await AuditAsync(
                "NormalizeOrchestrationTemplateStepOrder",
                "OrchestrationTemplate",
                template.Id.ToString(),
                new
                {
                    template.Id,
                    template.Name,
                    template.Version
                },
                true,
                cancellationToken: cancellationToken);

            TempData["OrchestrationTemplateMessage"] = "Step order normalized.";
            return RedirectToAction(nameof(OrchestrationTemplate), new { id = templateId });
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
                ".zip",
                ".reg",
                ".txt",
                ".json",
                ".xml",
                ".ini",
                ".config",
                ".csv"
            };

            if (string.IsNullOrWhiteSpace(extension) || !allowedExtensions.Contains(extension))
            {
                return "Only .msi, .exe, .zip, and .reg files are supported for distro uploads.";
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