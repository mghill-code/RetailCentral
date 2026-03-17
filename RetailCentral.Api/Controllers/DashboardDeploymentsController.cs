using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data;
using RetailCentral.Api.Data.Enums;
using RetailCentral.Api.Models.Dashboard.Deployments;

namespace RetailCentral.Api.Controllers
{
    public class DashboardDeploymentsController : Controller
    {
        private readonly RetailCentralDbContext _db;

        public DashboardDeploymentsController(RetailCentralDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var model = new List<DeploymentListItemViewModel>();

            var deployments = await _db.Deployments
                .Include(x => x.Package)
                .Include(x => x.DeploymentDevices)
                .OrderByDescending(x => x.CreatedUtc)
                .ToListAsync();

            foreach (var item in deployments)
            {
                model.Add(new DeploymentListItemViewModel
                {
                    Id = item.Id,
                    PackageName = item.Package?.Name ?? "(unknown)",
                    TargetType = ((DeploymentTargetType)item.TargetType).ToString(),
                    TargetValue = item.TargetValue,
                    ExecuteMode = ((DeploymentExecuteMode)item.ExecuteMode).ToString(),
                    Status = ((DeploymentStatus)item.Status).ToString(),
                    CreatedUtc = item.CreatedUtc,
                    DeviceCount = item.DeploymentDevices.Count,
                    CompletedCount = item.DeploymentDevices.Count(d => d.Status == (int)DeploymentDeviceStatus.Completed),
                    FailedCount = item.DeploymentDevices.Count(d =>
                        d.Status == (int)DeploymentDeviceStatus.FailedDownload ||
                        d.Status == (int)DeploymentDeviceStatus.FailedHash ||
                        d.Status == (int)DeploymentDeviceStatus.FailedExecution ||
                        d.Status == (int)DeploymentDeviceStatus.TimedOut)
                });
            }

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var model = new CreateDeploymentPageViewModel
            {
                Packages = await _db.Packages
                    .Where(x => x.IsEnabled)
                    .OrderBy(x => x.Name)
                    .Select(x => new PackageOptionViewModel
                    {
                        Id = x.Id,
                        DisplayName = x.Version == null
                            ? x.Name
                            : $"{x.Name} ({x.Version})"
                    })
                    .ToListAsync()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateDeploymentInputModel input)
        {
            if (!ModelState.IsValid)
            {
                var reloadModel = new CreateDeploymentPageViewModel
                {
                    Input = input,
                    Packages = await _db.Packages
                        .Where(x => x.IsEnabled)
                        .OrderBy(x => x.Name)
                        .Select(x => new PackageOptionViewModel
                        {
                            Id = x.Id,
                            DisplayName = x.Version == null
                                ? x.Name
                                : $"{x.Name} ({x.Version})"
                        })
                        .ToListAsync()
                };

                return View(reloadModel);
            }

            var package = await _db.Packages
                .FirstOrDefaultAsync(x => x.Id == input.PackageId && x.IsEnabled);

            if (package == null)
            {
                ModelState.AddModelError("", "Selected package was not found or is disabled.");

                var reloadModel = new CreateDeploymentPageViewModel
                {
                    Input = input,
                    Packages = await _db.Packages
                        .Where(x => x.IsEnabled)
                        .OrderBy(x => x.Name)
                        .Select(x => new PackageOptionViewModel
                        {
                            Id = x.Id,
                            DisplayName = x.Version == null
                                ? x.Name
                                : $"{x.Name} ({x.Version})"
                        })
                        .ToListAsync()
                };

                return View(reloadModel);
            }

            if (input.ExecuteMode == (int)DeploymentExecuteMode.Windowed &&
                (!input.WindowStartLocal.HasValue || !input.WindowEndLocal.HasValue))
            {
                ModelState.AddModelError("", "Windowed deployments require start and end times.");

                var reloadModel = new CreateDeploymentPageViewModel
                {
                    Input = input,
                    Packages = await _db.Packages
                        .Where(x => x.IsEnabled)
                        .OrderBy(x => x.Name)
                        .Select(x => new PackageOptionViewModel
                        {
                            Id = x.Id,
                            DisplayName = x.Version == null
                                ? x.Name
                                : $"{x.Name} ({x.Version})"
                        })
                        .ToListAsync()
                };

                return View(reloadModel);
            }

            var deployment = new Data.Entities.Deployment
            {
                PackageId = input.PackageId,
                TargetType = input.TargetType,
                TargetValue = input.TargetValue.Trim(),
                ExecuteMode = input.ExecuteMode,
                WindowStartLocal = input.WindowStartLocal,
                WindowEndLocal = input.WindowEndLocal,
                UseStoreLocalTime = input.UseStoreLocalTime,
                AllowOutsideWindow = input.AllowOutsideWindow,
                RetryCount = input.RetryCount,
                Notes = input.Notes?.Trim(),
                Status = (int)DeploymentStatus.Queued,
                CreatedUtc = DateTime.UtcNow,
                CreatedBy = User?.Identity?.Name ?? "dashboard"
            };

            _db.Deployments.Add(deployment);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id = deployment.Id });
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var deployment = await _db.Deployments
                .Include(x => x.Package)
                .Include(x => x.DeploymentDevices)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (deployment == null)
                return NotFound();

            var model = new DeploymentDetailsViewModel
            {
                Id = deployment.Id,
                PackageName = deployment.Package?.Name ?? "(unknown)",
                PackageVersion = deployment.Package?.Version,
                TargetType = ((DeploymentTargetType)deployment.TargetType).ToString(),
                TargetValue = deployment.TargetValue,
                ExecuteMode = ((DeploymentExecuteMode)deployment.ExecuteMode).ToString(),
                Status = ((DeploymentStatus)deployment.Status).ToString(),
                CreatedUtc = deployment.CreatedUtc,
                CreatedBy = deployment.CreatedBy,
                WindowStartLocal = deployment.WindowStartLocal,
                WindowEndLocal = deployment.WindowEndLocal,
                Notes = deployment.Notes,
                DeviceRows = deployment.DeploymentDevices
                    .OrderBy(x => x.StoreNumber)
                    .ThenBy(x => x.Hostname)
                    .Select(x => new DeploymentDeviceRowViewModel
                    {
                        DeviceId = x.DeviceId,
                        StoreNumber = x.StoreNumber,
                        Hostname = x.Hostname,
                        Status = ((DeploymentDeviceStatus)x.Status).ToString(),
                        DownloadStatus = x.DownloadStatus,
                        ExecuteStatus = x.ExecuteStatus,
                        ResultMessage = x.ResultMessage,
                        ExitCode = x.ExitCode,
                        LastHeartbeatUtc = x.LastHeartbeatUtc
                    })
                    .ToList()
            };

            return View(model);
        }
    }
}