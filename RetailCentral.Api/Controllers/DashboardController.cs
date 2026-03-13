using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data;
using RetailCentral.Api.Models;
using RetailCentral.Api.ViewModels;
using System.Text;

namespace RetailCentral.Api.Controllers
{
    public class DashboardController : Controller
    {
        private readonly RetailCentralDbContext _db;

        public DashboardController(RetailCentralDbContext db)
        {
            _db = db;
        }

        private static DateTime OnlineCutoffUtc => DateTime.UtcNow.AddMinutes(-5);

        private static List<string> GetAvailableCommandTypes()
        {
            return new List<string>
            {
                "Echo",
                "CollectSystemInfo",
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

        public async Task<IActionResult> Index()
        {
            var now = DateTime.UtcNow;
            var cutoff = now.AddMinutes(-5);
            var since24 = now.AddHours(-24);

            var devices = await _db.Devices
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

            var allDevices = await _db.Devices.ToListAsync();
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
                .Take(25)
                .Select(d =>
                {
                    inventoryByDevice.TryGetValue(d.DeviceId, out var inv);
                    return BuildDeviceHealth(d, inv, failedSet, cutoff, now);
                })
                .OrderBy(x => x.Score)
                .ThenBy(x => x.StoreNumber)
                .ThenBy(x => x.Hostname)
                .ToList();

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
                DeviceHealth = deviceHealth
            };

            return View(model);
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

        public async Task<IActionResult> Devices(string? search, string? storeNumber, string? groupName, string? status = "All")
        {
            var cutoff = OnlineCutoffUtc;

            var baseQuery = _db.Devices.AsQueryable();

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

            if (string.Equals(status, "Online", StringComparison.OrdinalIgnoreCase))
            {
                baseQuery = baseQuery.Where(d => d.LastSeenUtc >= cutoff);
            }
            else if (string.Equals(status, "Offline", StringComparison.OrdinalIgnoreCase))
            {
                baseQuery = baseQuery.Where(d => d.LastSeenUtc < cutoff);
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
                    IsOnline = d.LastSeenUtc >= cutoff
                })
                .ToListAsync();

            var stores = await _db.Devices
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
                Status = status,
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
                Health = health
            };

            return View(model);
        }

        public async Task<IActionResult> Commands()
        {
            var commands = await _db.Commands
                .OrderByDescending(c => c.CreatedUtc)
                .Take(100)
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

            return View(commands);
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

            return View(model);
        }

        public async Task<IActionResult> Failures()
        {
            var failures = await _db.Commands
                .Where(c => c.Status == "Failed")
                .OrderByDescending(c => c.CreatedUtc)
                .Take(100)
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

            return View(failures);
        }

        public async Task<IActionResult> Versions()
        {
            var cutoff = OnlineCutoffUtc;

            var devices = await _db.Devices.ToListAsync();

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

            TempData["GroupMessage"] = $"Group '{group.GroupName}' created.";
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
                TempData["GroupMessage"] = $"Added {device.Hostname} to {group.GroupName}.";
            }
            else
            {
                TempData["GroupMessage"] = $"{device.Hostname} is already in {group.GroupName}.";
            }

            return RedirectToAction(nameof(Group), new { groupName });
        }

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

            TempData["GroupMessage"] = $"Removed {device.Hostname} from {group.GroupName}.";
            return RedirectToAction(nameof(Group), new { groupName });
        }

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

            TempData["DeviceMessage"] = $"Requeued {failedCommands.Count} failed command(s).";
            return RedirectToAction(nameof(Device), new { id = deviceId });
        }

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

            TempData["GroupMessage"] = $"Group '{group.GroupName}' deleted.";
            return RedirectToAction(nameof(Groups));
        }

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
                await PopulateIssueCommandLists(model, cutoff, commandTypes, templates);
                return View(model);
            }

            var now = DateTime.UtcNow;
            var createdCommands = new List<Command>();

            if (model.DeviceId != null)
            {
                var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == model.DeviceId.Value);
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
                        issuedBy: "Dashboard",
                        issuedUtc: now,
                        groupName: null));
                }
            }
            else if (!string.IsNullOrWhiteSpace(model.StoreNumber))
            {
                var normalizedStore = model.StoreNumber.Trim();

                var storeDevices = await _db.Devices
                    .Where(d => d.StoreNumber == normalizedStore)
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
                            issuedBy: "Dashboard",
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
                        _db.Devices,
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
                            issuedBy: "Dashboard",
                            issuedUtc: now,
                            groupName: normalizedGroup));
                    }
                }
            }

            if (!ModelState.IsValid)
            {
                await PopulateIssueCommandLists(model, cutoff, commandTypes, templates);
                return View(model);
            }

            if (createdCommands.Count == 0)
            {
                ModelState.AddModelError("", "No matching target devices were found.");
                await PopulateIssueCommandLists(model, cutoff, commandTypes, templates);
                return View(model);
            }

            _db.Commands.AddRange(createdCommands);
            await _db.SaveChangesAsync();

            if (createdCommands.Count == 1)
            {
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

        private async Task PopulateIssueCommandLists(
            IssueCommandViewModel model,
            DateTime cutoff,
            List<string> commandTypes,
            List<CommandTemplateOptionViewModel> templates)
        {
            model.AvailableDevices = await _db.Devices
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
    }
}