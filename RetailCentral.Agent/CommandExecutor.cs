using Microsoft.Extensions.Options;
using RetailCentral.Agent.Configuration;
using RetailCentral.Agent.Services;
using RetailCentral.ShellContracts;
using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

public sealed class CommandExecutor
{
    private readonly AgentConfig _cfg;
    private readonly FileDownloadService _downloader;
    private readonly DeploymentWindowService _windowService;
    private readonly PackageExecutionService _packageExecution;
    private readonly ExecutionPolicyService _policy;
    private readonly ExecutionOptions _executionOptions;
    private readonly DownloadsOptions _downloadsOptions;
    private readonly ShellCommandClient _shellClient;
    private readonly FileWriteOptions _fileWriteOptions;

    public CommandExecutor(
     AgentConfig cfg,
     FileDownloadService downloader,
     DeploymentWindowService windowService,
     PackageExecutionService packageExecution,
     ExecutionPolicyService policy,
     ShellCommandClient shellClient,
     IOptions<ExecutionOptions> executionOptions,
     IOptions<DownloadsOptions> downloadsOptions,
     IOptions<FileWriteOptions> fileWriteOptions)
    {
        _cfg = cfg;
        _downloader = downloader;
        _windowService = windowService;
        _packageExecution = packageExecution;
        _policy = policy;
        _shellClient = shellClient;
        _executionOptions = executionOptions.Value;
        _downloadsOptions = downloadsOptions.Value;
        _fileWriteOptions = fileWriteOptions.Value;
    }

    public async Task<(string Status, int ExitCode, string StdOut, string StdErr, DateTime StartedUtc, DateTime FinishedUtc)> ExecuteAsync(
        string type,
        string? payloadJson,
        CancellationToken ct)
    {
        var started = DateTime.UtcNow;

        if (!_policy.IsCommandAllowed(type))
        {
            var finishedNotAllowed = DateTime.UtcNow;
            return
            (
                "Failed",
                901,
                "",
                $"Command type '{type}' is not allowed by policy.",
                started,
                finishedNotAllowed
            );
        }

        try
        {
            switch (type)
            {
                case "Echo":
                    {
                        var msg = payloadJson ?? "";
                        var finishedEcho = DateTime.UtcNow;
                        return ("Succeeded", 0, $"Echo: {msg}", "", started, finishedEcho);
                    }

                case "WriteFile":
                    {
                        var doc = JsonDocument.Parse(payloadJson ?? "{}");
                        var root = doc.RootElement;

                        var path = root.TryGetProperty("path", out var pathProp)
                            ? pathProp.GetString()
                            : null;

                        if (string.IsNullOrWhiteSpace(path))
                            throw new Exception("path required");

                        if (_fileWriteOptions.RequireAbsolutePath && !Path.IsPathRooted(path))
                            throw new Exception($"Path must be absolute: '{path}'");

                        if (_fileWriteOptions.DisallowUNCPaths && path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
                            throw new Exception($"UNC paths are not allowed: '{path}'");

                        var fullPath = Path.GetFullPath(path);

                        var underTrustedRoot = _fileWriteOptions.TrustedWriteRoots
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(Path.GetFullPath)
                            .Any(rootPath => fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase));

                        if (!underTrustedRoot)
                            throw new Exception($"Path is outside trusted write roots: '{fullPath}'");

                        foreach (var blocked in _fileWriteOptions.BlockedWritePathPrefixes.Where(x => !string.IsNullOrWhiteSpace(x)))
                        {
                            var blockedNormalized = Path.GetFullPath(blocked);
                            if (fullPath.StartsWith(blockedNormalized, StringComparison.OrdinalIgnoreCase))
                                throw new Exception($"Path is under a blocked write prefix: '{fullPath}'");
                        }

                        var content = root.TryGetProperty("content", out var contentProp)
                            ? contentProp.GetString() ?? string.Empty
                            : string.Empty;

                        var encodingName = root.TryGetProperty("encoding", out var encodingProp)
                            ? encodingProp.GetString()
                            : "utf-8";

                        var overwrite = root.TryGetProperty("overwrite", out var overwriteProp) && overwriteProp.GetBoolean();

                        if (File.Exists(fullPath) && !overwrite)
                        {
                            var finishedExists = DateTime.UtcNow;
                            return
                            (
                                "Failed",
                                906,
                                "",
                                $"File already exists and overwrite is false: {fullPath}",
                                started,
                                finishedExists
                            );
                        }

                        var directory = Path.GetDirectoryName(fullPath);
                        if (string.IsNullOrWhiteSpace(directory))
                            throw new Exception("Parent directory could not be determined.");

                        Directory.CreateDirectory(directory);

                        var encoding = ResolveTextEncoding(encodingName);
                        File.WriteAllText(fullPath, content, encoding);

                        var fileInfo = new FileInfo(fullPath);
                        var finishedWrite = DateTime.UtcNow;

                        return
                        (
                            "Succeeded",
                            0,
                            $"Wrote file: {fullPath}\nEncoding: {encodingName}\nBytes: {fileInfo.Length}\nOverwrite: {overwrite}",
                            "",
                            started,
                            finishedWrite
                        );
                    }
                
                case "RunProcess":
                    {
                        if (!_executionOptions.AllowRunProcess)
                        {
                            var finishedRunProcessDisabled = DateTime.UtcNow;
                            return
                            (
                                "Failed",
                                904,
                                "",
                                "RunProcess is disabled by policy.",
                                started,
                                finishedRunProcessDisabled
                            );
                        }

                        var doc = JsonDocument.Parse(payloadJson ?? "{}");
                        var root = doc.RootElement;

                        var fileName = root.GetProperty("fileName").GetString()
                                       ?? throw new Exception("fileName required");

                        var args = root.TryGetProperty("arguments", out var a)
                            ? a.GetString() ?? ""
                            : "";

                        var wd = root.TryGetProperty("workingDirectory", out var w)
                            ? w.GetString()
                            : null;

                        var timeoutSec = root.TryGetProperty("timeoutSeconds", out var t)
                            ? t.GetInt32()
                            : _executionOptions.DefaultTimeoutSeconds;

                        if (timeoutSec <= 0)
                            timeoutSec = _executionOptions.DefaultTimeoutSeconds;

                        if (timeoutSec > _executionOptions.MaxTimeoutSeconds)
                            timeoutSec = _executionOptions.MaxTimeoutSeconds;

                        _policy.ValidateExecutablePath(fileName);

                        if (!string.IsNullOrWhiteSpace(wd))
                        {
                            ValidateWorkingDirectory(wd);
                        }

                        var result = await ExecuteProcessAsync(
                            fileName,
                            args,
                            wd,
                            timeoutSec,
                            ct);

                        var finishedRunProcess = DateTime.UtcNow;
                        return
                        (
                            result.ExitCode == 0 ? "Succeeded" : "Failed",
                            result.ExitCode,
                            result.StdOut,
                            result.StdErr,
                            started,
                            finishedRunProcess
                        );
                    }

                // Native-style named application recovery commands.
                // These no longer rely on cmd.exe wrappers.
                case "RestartPOS":
                    {
                        try
                        {
                            var request = new ShellCommandRequest
                            {
                                RequestId = Guid.NewGuid(),
                                Action = ShellCommandActions.RestartPOS,
                                RequestedUtc = DateTime.UtcNow
                            };

                            var response = await _shellClient.SendAsync(request, ct);
                            var finished = DateTime.UtcNow;

                            return
                            (
                                response.Success ? "Succeeded" : "Failed",
                                response.ExitCode,
                                Trunc(response.StdOut ?? "", _executionOptions.MaxStdoutChars),
                                Trunc(response.StdErr ?? "", _executionOptions.MaxStderrChars),
                                started,
                                finished
                            );
                        }
                        catch (Exception ex)
                        {
                            var finished = DateTime.UtcNow;
                            return
                            (
                                "Failed",
                                910,
                                "",
                                Trunc($"RetailShell broker unavailable or failed: {ex.Message}", _executionOptions.MaxStderrChars),
                                started,
                                finished
                            );
                        }
                    }

                case "RestartRetailShell":
                    {
                        return await RestartApplicationByProfileAsync(
                            commandType: "RestartRetailShell",
                            profileName: "RetailShellRestart",
                            processName: _cfg.RetailShellProcessName,
                            started: started,
                            ct: ct);
                    }

                case "RestartAgent":
                    {
                        // Launch the restart helper in detached mode so it can continue running
                        // after this service instance begins shutting down.
                        return await LaunchDetachedRestartHelperAsync(
                            profileName: "AgentRestart",
                            helperArguments: "RetailCentral.Agent 10",
                            started: started);
                    }

                case "RebootDevice":
                    {
                        return await RequestRebootAsync(started, ct);
                    }

                case "CollectSystemInfo":
                    {
                        var store = Environment.GetEnvironmentVariable("Store") ?? "";
                        var regNum = Environment.GetEnvironmentVariable("REG_NUM") ?? "";

                        var info = new
                        {
                            ComputerName = Environment.MachineName,
                            Store = store,
                            RegisterNumber = regNum,
                            UserName = Environment.UserName,
                            Domain = Environment.UserDomainName,
                            OSVersion = Environment.OSVersion.ToString(),
                            CPUArch = RuntimeInformation.OSArchitecture.ToString(),
                            Is64BitOS = Environment.Is64BitOperatingSystem,
                            Is64BitProcess = Environment.Is64BitProcess,
                            ProcessorCount = Environment.ProcessorCount,
                            SystemPageSize = Environment.SystemPageSize,
                            TickCount64 = Environment.TickCount64,
                            Uptime = GetUptimeInfo(),
                            CurrentDirectory = Environment.CurrentDirectory,
                            DotNetVersion = Environment.Version.ToString(),
                            ComputerSystem = GetComputerSystemInfo(),
                            Bios = GetBiosInfo(),
                            OperatingSystem = GetOperatingSystemInfo(),
                            Cpu = GetCpuInfo(),
                            Memory = GetMemoryInfo(),
                            Network = GetNetworkInfo(),
                            Drives = GetDriveInfo(),
                            Scanner = GetScannerInfo()
                        };

                        var json = JsonSerializer.Serialize(info, new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });

                        var finishedSystemInfo = DateTime.UtcNow;
                        return ("Succeeded", 0, Trunc(json, _executionOptions.MaxStdoutChars), "", started, finishedSystemInfo);
                    }

                case "CollectSoftwareInventory":
                    {
                        var software = GetInstalledSoftware();
                        var updates = GetWindowsUpdates();

                        var payload = new
                        {
                            InstalledSoftware = software,
                            WindowsUpdates = updates
                        };

                        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });

                        var finishedSoftwareInventory = DateTime.UtcNow;
                        return ("Succeeded", 0, Trunc(json, _executionOptions.MaxStdoutChars), "", started, finishedSoftwareInventory);
                    }

                case "DownloadFile":
                    {
                        var doc = JsonDocument.Parse(payloadJson ?? "{}");
                        var root = doc.RootElement;

                        var url = root.GetProperty("url").GetString()
                                  ?? throw new Exception("url required");

                        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                            throw new Exception("url is invalid");

                        _policy.ValidateDownload(uri);

                        var destinationFileName = root.TryGetProperty("destinationFileName", out var dest)
                            ? dest.GetString()
                            : null;

                        if (string.IsNullOrWhiteSpace(destinationFileName))
                        {
                            destinationFileName = Path.GetFileName(uri.AbsolutePath);
                        }

                        if (string.IsNullOrWhiteSpace(destinationFileName))
                            throw new Exception("destinationFileName could not be determined.");

                        var expectedSha256 = root.TryGetProperty("sha256", out var hashProp)
                            ? hashProp.GetString()
                            : null;

                        var execute = root.TryGetProperty("execute", out var execProp) && execProp.GetBoolean();

                        var arguments = root.TryGetProperty("arguments", out var argsProp)
                            ? argsProp.GetString() ?? ""
                            : "";

                        var downloadedPath = await _downloader.DownloadAsync(url, destinationFileName, ct);

                        var actualSha256 = FileDownloadService.ComputeSha256Hex(downloadedPath);

                        if (!string.IsNullOrWhiteSpace(expectedSha256) &&
                            !string.Equals(expectedSha256.Trim(), actualSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            var finishedHashFail = DateTime.UtcNow;
                            return
                            (
                                "Failed",
                                902,
                                "",
                                $"SHA256 mismatch. Expected={expectedSha256}, Actual={actualSha256}",
                                started,
                                finishedHashFail
                            );
                        }

                        if (!execute)
                        {
                            var finishedDownloadOnly = DateTime.UtcNow;
                            return
                            (
                                "Succeeded",
                                0,
                                $"Downloaded to: {downloadedPath}\nSHA256: {actualSha256}",
                                "",
                                started,
                                finishedDownloadOnly
                            );
                        }

                        _policy.ValidateExecutionFromPath(downloadedPath);

                        var workingDirectory = Path.GetDirectoryName(downloadedPath) ?? _downloadsOptions.RootFolder;
                        ValidateWorkingDirectory(workingDirectory);

                        var execResult = await ExecuteProcessAsync(
                            downloadedPath,
                            arguments,
                            workingDirectory,
                            _executionOptions.DefaultTimeoutSeconds,
                            ct);

                        var finishedDownloadExec = DateTime.UtcNow;

                        return
                        (
                            execResult.ExitCode == 0 ? "Succeeded" : "Failed",
                            execResult.ExitCode,
                            Trunc($"Downloaded to: {downloadedPath}\nSHA256: {actualSha256}\n{execResult.StdOut}", _executionOptions.MaxStdoutChars),
                            execResult.StdErr,
                            started,
                            finishedDownloadExec
                        );
                    }

                case "CollectProcessStatus":
                    {
                        var status = GetProcessStatus();
                        var json = JsonSerializer.Serialize(status, new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });

                        var finishedProcessStatus = DateTime.UtcNow;
                        return ("Succeeded", 0, json, "", started, finishedProcessStatus);
                    }

                case "ValidateProcess":
                    {
                        var doc = JsonDocument.Parse(payloadJson ?? "{}");
                        var root = doc.RootElement;

                        var processName = root.TryGetProperty("processName", out var processNameProp)
                            ? processNameProp.GetString()
                            : null;

                        if (string.IsNullOrWhiteSpace(processName))
                            throw new Exception("processName required");

                        var normalized = NormalizeProcessName(processName);
                        var matches = Process.GetProcessesByName(normalized);

                        var finishedValidate = DateTime.UtcNow;

                        if (matches.Length > 0)
                        {
                            var stdout = $"Process '{normalized}' is running. Count={matches.Length}";
                            return ("Succeeded", 0, Trunc(stdout, _executionOptions.MaxStdoutChars), "", started, finishedValidate);
                        }

                        var stderr = $"Process '{normalized}' is not running.";
                        return ("Failed", 3, "", Trunc(stderr, _executionOptions.MaxStderrChars), started, finishedValidate);
                    }

                case "InstallPackage":
                    {
                        var payload = JsonSerializer.Deserialize<PackageDeploymentCommand>(
                            payloadJson ?? "{}",
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                            ?? throw new Exception("Invalid InstallPackage payload.");

                        if (string.IsNullOrWhiteSpace(payload.DownloadUrl))
                            throw new Exception("downloadUrl required");

                        if (!Uri.TryCreate(payload.DownloadUrl, UriKind.Absolute, out var packageUri))
                            throw new Exception("downloadUrl is invalid");

                        _policy.ValidateDownload(packageUri, payload.FileName);

                        if (string.IsNullOrWhiteSpace(payload.FileName))
                            throw new Exception("fileName required");

                        if (string.IsNullOrWhiteSpace(payload.InstallCommand))
                            throw new Exception("installCommand required");

                        var packageIdSegment = payload.PackageId == 0
                            ? "adhoc"
                            : payload.PackageId.ToString();

                        var stagingFolder = Path.Combine(_downloadsOptions.StagingRootFolder, packageIdSegment);
                        Directory.CreateDirectory(stagingFolder);

                        var downloadedPath = await _downloader.DownloadAsync(
                            payload.DownloadUrl,
                            stagingFolder,
                            payload.FileName,
                            ct);

                        var actualSha256 = FileDownloadService.ComputeSha256Hex(downloadedPath);

                        if (!string.IsNullOrWhiteSpace(payload.Sha256) &&
                            !string.Equals(payload.Sha256.Trim(), actualSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            var finishedHashFail = DateTime.UtcNow;
                            return
                            (
                                "Failed",
                                902,
                                "",
                                $"SHA256 mismatch. Expected={payload.Sha256}, Actual={actualSha256}",
                                started,
                                finishedHashFail
                            );
                        }

                        if (string.Equals(payload.ExecuteMode, "StagedOnly", StringComparison.OrdinalIgnoreCase))
                        {
                            var finishedStaged = DateTime.UtcNow;
                            return
                            (
                                "Succeeded",
                                0,
                                $"Staged file: {downloadedPath}\nSHA256: {actualSha256}",
                                "",
                                started,
                                finishedStaged
                            );
                        }

                        if (string.Equals(payload.ExecuteMode, "Windowed", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!TimeSpan.TryParse(payload.WindowStartLocal, out var startWindow))
                                throw new Exception("windowStartLocal required for Windowed mode");

                            if (!TimeSpan.TryParse(payload.WindowEndLocal, out var endWindow))
                                throw new Exception("windowEndLocal required for Windowed mode");

                            var nowLocal = DateTime.Now.TimeOfDay;

                            if (!_windowService.IsInWindow(nowLocal, startWindow, endWindow))
                            {
                                var finishedWaiting = DateTime.UtcNow;
                                return
                                (
                                    "Deferred",
                                    903,
                                    $"Downloaded to: {downloadedPath}\nSHA256: {actualSha256}",
                                    $"Outside install window {payload.WindowStartLocal} - {payload.WindowEndLocal}",
                                    started,
                                    finishedWaiting
                                );
                            }
                        }

                        var workingDirectory = string.IsNullOrWhiteSpace(payload.WorkingDirectory)
                            ? Path.GetDirectoryName(downloadedPath)
                            : payload.WorkingDirectory;

                        if (string.IsNullOrWhiteSpace(workingDirectory))
                            throw new Exception("workingDirectory could not be determined.");

                        ValidateWorkingDirectory(workingDirectory);

                        var installArguments = (payload.InstallArguments ?? "")
                            .Replace("{file}", $"\"{downloadedPath}\"");

                        var resolvedInstallCommand = ResolvePackageInstallCommand(
                            payload.InstallCommand,
                            installArguments,
                            downloadedPath);

                        var timeoutSeconds = payload.TimeoutSeconds > 0
                            ? Math.Min(payload.TimeoutSeconds, _executionOptions.MaxTimeoutSeconds)
                            : _executionOptions.DefaultTimeoutSeconds;

                        var execResult = await _packageExecution.ExecuteAsync(
                            resolvedInstallCommand.FileName,
                            resolvedInstallCommand.Arguments,
                            resolvedInstallCommand.WorkingDirectory,
                            timeoutSeconds,
                            _executionOptions.MaxStdoutChars,
                            _executionOptions.MaxStderrChars,
                            ct);

                        var finishedInstallExec = DateTime.UtcNow;

                        if (execResult.TimedOut)
                        {
                            return
                            (
                                "Failed",
                                124,
                                $"Downloaded to: {downloadedPath}\nSHA256: {actualSha256}",
                                Trunc(execResult.StdErr ?? "", _executionOptions.MaxStderrChars),
                                started,
                                finishedInstallExec
                            );
                        }

                        var exitCode = execResult.ExitCode ?? 1;
                        var rebootRequired = exitCode == 3010;
                        var rebootInitiated = exitCode == 1641;
                        var succeeded = exitCode == 0 || rebootRequired || rebootInitiated;

                        var statusMessage = exitCode switch
                        {
                            0 => "Package installed successfully.",
                            3010 => "Package installed successfully. Reboot required to complete installation.",
                            1641 => "Package installed successfully. Installer initiated a reboot.",
                            _ => $"Package installation failed with exit code {exitCode}."
                        };

                        var stdoutBuilder = new StringBuilder();
                        stdoutBuilder.AppendLine($"Downloaded to: {downloadedPath}");
                        stdoutBuilder.AppendLine($"SHA256: {actualSha256}");
                        stdoutBuilder.AppendLine(statusMessage);

                        if (!string.IsNullOrWhiteSpace(payload.RebootBehavior))
                        {
                            stdoutBuilder.AppendLine($"RebootBehavior: {payload.RebootBehavior}");
                        }

                        if (!string.IsNullOrWhiteSpace(execResult.StdOut))
                        {
                            stdoutBuilder.AppendLine(execResult.StdOut);
                        }

                        return
                        (
                            succeeded ? "Succeeded" : "Failed",
                            exitCode,
                            Trunc(stdoutBuilder.ToString(), _executionOptions.MaxStdoutChars),
                            Trunc(execResult.StdErr ?? "", _executionOptions.MaxStderrChars),
                            started,
                            finishedInstallExec
                        );
                    }

                default:
                    {
                        var finishedUnknown = DateTime.UtcNow;
                        return ("Failed", 2, "", $"Unknown command type '{type}'", started, finishedUnknown);
                    }
            }
        }
        catch (Exception ex)
        {
            var finishedException = DateTime.UtcNow;
            return ("Failed", 1, "", Trunc(ex.ToString(), _executionOptions.MaxStderrChars), started, finishedException);
        }
    }

    /// <summary>
    /// Restart an application by:
    /// 1. Looking up an approved executable profile by name
    /// 2. Stopping existing processes by configured process name
    /// 3. Starting the executable from the approved path
    ///
    /// Long term this is better than cmd.exe wrappers because it is easier to
    /// validate, audit, and troubleshoot.
    /// </summary>
    private async Task<(string Status, int ExitCode, string StdOut, string StdErr, DateTime StartedUtc, DateTime FinishedUtc)> RestartApplicationByProfileAsync(
        string commandType,
        string profileName,
        string? processName,
        DateTime started,
        CancellationToken ct)
    {
        var profile = _executionOptions.AllowedExecutables
            .FirstOrDefault(x => string.Equals(x.Name, profileName, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
            throw new Exception($"Execution profile '{profileName}' is not configured.");

        _policy.ValidateExecutablePath(profile.Path);

        if (string.IsNullOrWhiteSpace(processName))
            throw new Exception($"{commandType} requires a configured process name.");

        var normalizedProcessName = NormalizeProcessName(processName);
        var running = Process.GetProcessesByName(normalizedProcessName);

        var stdout = new StringBuilder();
        stdout.AppendLine($"Command: {commandType}");
        stdout.AppendLine($"Profile: {profile.Name}");
        stdout.AppendLine($"Executable: {profile.Path}");
        stdout.AppendLine($"ProcessName: {normalizedProcessName}");
        stdout.AppendLine($"ExistingInstances: {running.Length}");

        foreach (var proc in running)
        {
            try
            {
                stdout.AppendLine($"Stopping PID {proc.Id} ({proc.ProcessName})...");
                proc.Kill(entireProcessTree: true);
                await proc.WaitForExitAsync(ct);
            }
            catch (Exception ex)
            {
                var finishedKillFail = DateTime.UtcNow;
                return
                (
                    "Failed",
                    905,
                    Trunc(stdout.ToString(), _executionOptions.MaxStdoutChars),
                    Trunc($"Failed to stop existing process '{normalizedProcessName}': {ex.Message}", _executionOptions.MaxStderrChars),
                    started,
                    finishedKillFail
                );
            }
        }

        ValidateWorkingDirectory(profile.WorkingDirectory);

        var psi = new ProcessStartInfo(profile.Path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = string.IsNullOrWhiteSpace(profile.WorkingDirectory)
                ? Path.GetDirectoryName(profile.Path) ?? AppContext.BaseDirectory
                : profile.WorkingDirectory
        };

        using var launched = Process.Start(psi) ?? throw new Exception($"Failed to start '{profile.Path}'.");

        stdout.AppendLine($"Started PID {launched.Id}.");
        var finished = DateTime.UtcNow;

        return
        (
            "Succeeded",
            0,
            Trunc(stdout.ToString(), _executionOptions.MaxStdoutChars),
            "",
            started,
            finished
        );
    }

    /// <summary>
    /// Launches the restart helper in detached mode so it can survive
    /// the current agent service shutting down.
    /// </summary>
    private Task<(string Status, int ExitCode, string StdOut, string StdErr, DateTime StartedUtc, DateTime FinishedUtc)> LaunchDetachedRestartHelperAsync(
        string profileName,
        string helperArguments,
        DateTime started)
    {
        var profile = _executionOptions.AllowedExecutables
            .FirstOrDefault(x => string.Equals(x.Name, profileName, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
            throw new Exception($"Execution profile '{profileName}' is not configured.");

        _policy.ValidateExecutablePath(profile.Path);
        _policy.ValidateArguments(profile, helperArguments);
        ValidateWorkingDirectory(profile.WorkingDirectory);

        var workingDirectory = string.IsNullOrWhiteSpace(profile.WorkingDirectory)
            ? Path.GetDirectoryName(profile.Path) ?? AppContext.BaseDirectory
            : profile.WorkingDirectory;

        var psi = new ProcessStartInfo(profile.Path, helperArguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };

        var proc = Process.Start(psi) ?? throw new Exception($"Failed to start restart helper '{profile.Path}'.");

        var stdout = new StringBuilder();
        stdout.AppendLine("Agent restart helper launched successfully.");
        stdout.AppendLine($"Profile: {profile.Name}");
        stdout.AppendLine($"Executable: {profile.Path}");
        stdout.AppendLine($"Arguments: {helperArguments}");
        stdout.AppendLine($"Helper PID: {proc.Id}");

        var finished = DateTime.UtcNow;

        return Task.FromResult((
            "Succeeded",
            0,
            Trunc(stdout.ToString(), _executionOptions.MaxStdoutChars),
            "",
            started,
            finished
        ));
    }

    /// <summary>
    /// Request a machine reboot through the trusted system shutdown executable.
    /// </summary>
    private async Task<(string Status, int ExitCode, string StdOut, string StdErr, DateTime StartedUtc, DateTime FinishedUtc)> RequestRebootAsync(
        DateTime started,
        CancellationToken ct)
    {
        var shutdownPath = Path.Combine(Environment.SystemDirectory, "shutdown.exe");

        var result = await ExecuteProcessAsync(
            shutdownPath,
            "/r /t 5 /f",
            Environment.SystemDirectory,
            15,
            ct);

        var finished = DateTime.UtcNow;

        return
        (
            result.ExitCode == 0 ? "Succeeded" : "Failed",
            result.ExitCode,
            result.ExitCode == 0
                ? "System reboot requested successfully. Device will reboot in approximately 5 seconds."
                : result.StdOut,
            result.StdErr,
            started,
            finished
        );
    }

    private (string FileName, string Arguments, string WorkingDirectory) ResolvePackageInstallCommand(
        string installCommand,
        string installArguments,
        string downloadedPath)
    {
        var stagedDirectory = Path.GetDirectoryName(downloadedPath) ?? _downloadsOptions.StagingRootFolder;

        if (string.Equals(installCommand, "__PACKAGE_EXE__", StringComparison.OrdinalIgnoreCase))
        {
            _policy.ValidateExecutionFromPath(downloadedPath);
            return (downloadedPath, installArguments, stagedDirectory);
        }

        return ResolveInstallCommand(installCommand, installArguments);
    }

    private (string FileName, string Arguments, string WorkingDirectory) ResolveInstallCommand(string installCommand, string installArguments)
    {
        if (Path.IsPathRooted(installCommand))
        {
            _policy.ValidateExecutablePath(installCommand);
            return (installCommand, installArguments, Path.GetDirectoryName(installCommand) ?? _downloadsOptions.StagingRootFolder);
        }

        var profile = _executionOptions.AllowedExecutables
            .FirstOrDefault(x => string.Equals(x.Name, installCommand, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
            throw new Exception($"Install command '{installCommand}' is not an absolute path or allowed execution profile.");

        _policy.ValidateExecutablePath(profile.Path);
        _policy.ValidateArguments(profile, installArguments);

        return
        (
            profile.Path,
            installArguments,
            string.IsNullOrWhiteSpace(profile.WorkingDirectory)
                ? _downloadsOptions.StagingRootFolder
                : profile.WorkingDirectory
        );
    }

    private void ValidateWorkingDirectory(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
            return;

        if (_executionOptions.RequireAbsolutePath && !Path.IsPathRooted(workingDirectory))
            throw new Exception($"Working directory must be an absolute path: '{workingDirectory}'");

        if (_executionOptions.DisallowUNCPaths &&
            workingDirectory.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception($"UNC working directory is not allowed: '{workingDirectory}'");
        }

        var normalized = Path.GetFullPath(workingDirectory);

        foreach (var blocked in _executionOptions.BlockedPathPrefixes.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var blockedNormalized = Path.GetFullPath(blocked);
            if (normalized.StartsWith(blockedNormalized, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Working directory is under a blocked prefix: '{workingDirectory}'");
            }
        }
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> ExecuteProcessAsync(
        string fileName,
        string arguments,
        string? workingDirectory,
        int timeoutSeconds,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName, arguments ?? "")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        using var process = Process.Start(psi) ?? throw new Exception($"Failed to start process '{fileName}'.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch
            {
            }

            return (124, "", $"Timed out after {timeoutSeconds}s");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return
        (
            process.ExitCode,
            Trunc(stdout, _executionOptions.MaxStdoutChars),
            Trunc(stderr, _executionOptions.MaxStderrChars)
        );
    }

    private static object GetCpuInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");

            foreach (ManagementObject obj in searcher.Get())
            {
                return new
                {
                    Name = obj["Name"]?.ToString(),
                    Manufacturer = obj["Manufacturer"]?.ToString(),
                    NumberOfCores = obj["NumberOfCores"] != null ? Convert.ToInt32(obj["NumberOfCores"]) : (int?)null,
                    NumberOfLogicalProcessors = obj["NumberOfLogicalProcessors"] != null ? Convert.ToInt32(obj["NumberOfLogicalProcessors"]) : (int?)null,
                    MaxClockSpeedMHz = obj["MaxClockSpeed"] != null ? Convert.ToInt32(obj["MaxClockSpeed"]) : (int?)null
                };
            }
        }
        catch (Exception ex)
        {
            return new { Error = ex.Message };
        }

        return new { Error = "CPU information not available." };
    }

    private static object GetMemoryInfo()
    {
        try
        {
            ulong totalBytes = 0;
            using (var searcher = new ManagementObjectSearcher(
                "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    totalBytes = obj["TotalPhysicalMemory"] != null
                        ? Convert.ToUInt64(obj["TotalPhysicalMemory"])
                        : 0;
                    break;
                }
            }

            ulong freeKb = 0;
            using (var osSearcher = new ManagementObjectSearcher(
                "SELECT FreePhysicalMemory FROM Win32_OperatingSystem"))
            {
                foreach (ManagementObject obj in osSearcher.Get())
                {
                    freeKb = obj["FreePhysicalMemory"] != null
                        ? Convert.ToUInt64(obj["FreePhysicalMemory"])
                        : 0;
                    break;
                }
            }

            ulong freeBytes = freeKb * 1024;
            ulong usedBytes = totalBytes >= freeBytes ? totalBytes - freeBytes : 0;

            return new
            {
                TotalPhysicalMemoryBytes = totalBytes,
                TotalPhysicalMemoryGB = Math.Round(totalBytes / 1024d / 1024d / 1024d, 2),
                FreePhysicalMemoryBytes = freeBytes,
                FreePhysicalMemoryGB = Math.Round(freeBytes / 1024d / 1024d / 1024d, 2),
                UsedPhysicalMemoryBytes = usedBytes,
                UsedPhysicalMemoryGB = Math.Round(usedBytes / 1024d / 1024d / 1024d, 2)
            };
        }
        catch (Exception ex)
        {
            return new { Error = ex.Message };
        }
    }

    private static object GetComputerSystemInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Manufacturer, Model FROM Win32_ComputerSystem");

            foreach (ManagementObject obj in searcher.Get())
            {
                return new
                {
                    Manufacturer = obj["Manufacturer"]?.ToString(),
                    Model = obj["Model"]?.ToString()
                };
            }
        }
        catch (Exception ex)
        {
            return new { Error = ex.Message };
        }

        return new { Error = "Computer system information not available." };
    }

    private static object GetBiosInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT SerialNumber, SMBIOSBIOSVersion, Manufacturer FROM Win32_BIOS");

            foreach (ManagementObject obj in searcher.Get())
            {
                return new
                {
                    SerialNumber = obj["SerialNumber"]?.ToString(),
                    SMBIOSBIOSVersion = obj["SMBIOSBIOSVersion"]?.ToString(),
                    Manufacturer = obj["Manufacturer"]?.ToString()
                };
            }
        }
        catch (Exception ex)
        {
            return new { Error = ex.Message };
        }

        return new { Error = "BIOS information not available." };
    }

    private static object GetOperatingSystemInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Caption, Version, InstallDate, LastBootUpTime, FreePhysicalMemory FROM Win32_OperatingSystem");

            foreach (ManagementObject obj in searcher.Get())
            {
                var installDate = TryConvertWmiDate(obj["InstallDate"]?.ToString());
                var lastBoot = TryConvertWmiDate(obj["LastBootUpTime"]?.ToString());

                return new
                {
                    Caption = obj["Caption"]?.ToString(),
                    Version = obj["Version"]?.ToString(),
                    InstallDateUtc = installDate,
                    LastBootUpTimeUtc = lastBoot
                };
            }
        }
        catch (Exception ex)
        {
            return new { Error = ex.Message };
        }

        return new { Error = "Operating system information not available." };
    }

    private static object GetUptimeInfo()
    {
        try
        {
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

            return new
            {
                TotalSeconds = Math.Round(uptime.TotalSeconds, 0),
                HumanReadable = FormatTimeSpan(uptime)
            };
        }
        catch (Exception ex)
        {
            return new { Error = ex.Message };
        }
    }

    private static List<object> GetNetworkInfo()
    {
        var adapters = new List<object>();

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                         .OrderBy(n => n.Name))
            {
                var props = nic.GetIPProperties();

                var ipv4 = props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString())
                    .ToList();

                var ipv6 = props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    .Select(a => a.Address.ToString())
                    .ToList();

                adapters.Add(new
                {
                    Name = nic.Name,
                    Description = nic.Description,
                    NetworkInterfaceType = nic.NetworkInterfaceType.ToString(),
                    OperationalStatus = nic.OperationalStatus.ToString(),
                    SpeedMbps = nic.Speed > 0 ? Math.Round(nic.Speed / 1000d / 1000d, 2) : (double?)null,
                    MacAddress = FormatMac(nic.GetPhysicalAddress()),
                    IPv4Addresses = ipv4,
                    IPv6Addresses = ipv6
                });
            }
        }
        catch (Exception ex)
        {
            adapters.Add(new { Error = ex.Message });
        }

        return adapters;
    }

    private static List<object> GetDriveInfo()
    {
        var drives = new List<object>();

        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (!d.IsReady)
                {
                    drives.Add(new
                    {
                        Name = d.Name,
                        DriveType = d.DriveType.ToString(),
                        IsReady = false
                    });

                    continue;
                }

                drives.Add(new
                {
                    Name = d.Name,
                    DriveType = d.DriveType.ToString(),
                    VolumeLabel = d.VolumeLabel,
                    DriveFormat = d.DriveFormat,
                    TotalSizeBytes = d.TotalSize,
                    TotalSizeGB = Math.Round(d.TotalSize / 1024d / 1024d / 1024d, 2),
                    AvailableFreeSpaceBytes = d.AvailableFreeSpace,
                    AvailableFreeSpaceGB = Math.Round(d.AvailableFreeSpace / 1024d / 1024d / 1024d, 2),
                    RootDirectory = d.RootDirectory.FullName,
                    IsReady = true
                });
            }
        }
        catch (Exception ex)
        {
            drives.Add(new { Error = ex.Message });
        }

        return drives;
    }

    private static object GetScannerInfo()
    {
        try
        {
            return new
            {
                ScannerName = (string?)null,
                ScannerSerialNumber = (string?)null
            };
        }
        catch (Exception ex)
        {
            return new { Error = ex.Message };
        }
    }

    private static DateTime? TryConvertWmiDate(string? wmiDate)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(wmiDate))
                return null;

            return ManagementDateTimeConverter.ToDateTime(wmiDate).ToUniversalTime();
        }
        catch
        {
            return null;
        }
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        var parts = new List<string>();

        if (ts.Days > 0) parts.Add($"{ts.Days}d");
        if (ts.Hours > 0) parts.Add($"{ts.Hours}h");
        if (ts.Minutes > 0) parts.Add($"{ts.Minutes}m");
        if (ts.Seconds > 0 || parts.Count == 0) parts.Add($"{ts.Seconds}s");

        return string.Join(" ", parts);
    }

    private static string? FormatMac(PhysicalAddress? mac)
    {
        if (mac == null)
            return null;

        var bytes = mac.GetAddressBytes();
        if (bytes.Length == 0)
            return null;

        return string.Join("-", bytes.Select(b => b.ToString("X2")));
    }

    private static List<object> GetInstalledSoftware()
    {
        var results = new List<object>();

        string[] registryPaths =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        foreach (var path in registryPaths)
        {
            using var baseKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path);
            if (baseKey == null) continue;

            foreach (var subKeyName in baseKey.GetSubKeyNames())
            {
                using var subKey = baseKey.OpenSubKey(subKeyName);
                if (subKey == null) continue;

                var name = subKey.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(name)) continue;

                results.Add(new
                {
                    Name = name,
                    Version = subKey.GetValue("DisplayVersion") as string,
                    Publisher = subKey.GetValue("Publisher") as string,
                    InstallDate = subKey.GetValue("InstallDate") as string
                });
            }
        }

        return results;
    }

    private object GetProcessStatus()
    {
        return new
        {
            PosProcessName = _cfg.PosProcessName,
            RetailShellProcessName = _cfg.RetailShellProcessName,
            AgentProcessName = _cfg.AgentProcessName,

            Pos = GetSingleProcessStatus(_cfg.PosProcessName),
            RetailShell = GetSingleProcessStatus(_cfg.RetailShellProcessName),
            Agent = GetSingleProcessStatus(_cfg.AgentProcessName)
        };
    }

    private static object GetSingleProcessStatus(string? configuredName)
    {
        var processName = NormalizeProcessName(configuredName);

        if (string.IsNullOrWhiteSpace(processName))
        {
            return new
            {
                ConfiguredName = configuredName,
                ProcessName = processName,
                IsConfigured = false,
                IsRunning = false,
                ProcessCount = 0,
                CpuPercent = (decimal?)null,
                WorkingSetMb = (decimal?)null,
                StartedAtLocal = (DateTime?)null
            };
        }

        try
        {
            var matches = Process.GetProcessesByName(processName);

            if (matches.Length == 0)
            {
                return new
                {
                    ConfiguredName = configuredName,
                    ProcessName = processName,
                    IsConfigured = true,
                    IsRunning = false,
                    ProcessCount = 0,
                    CpuPercent = (decimal?)null,
                    WorkingSetMb = (decimal?)null,
                    StartedAtLocal = (DateTime?)null
                };
            }

            var primary = matches
                .OrderByDescending(p => SafeWorkingSet64(p))
                .First();

            var workingSetMb = Math.Round(SafeWorkingSet64(primary) / 1024m / 1024m, 2);

            DateTime? startedAtLocal = null;
            try
            {
                startedAtLocal = primary.StartTime;
            }
            catch
            {
            }

            return new
            {
                ConfiguredName = configuredName,
                ProcessName = processName,
                IsConfigured = true,
                IsRunning = true,
                ProcessCount = matches.Length,
                CpuPercent = (decimal?)null,
                WorkingSetMb = workingSetMb,
                StartedAtLocal = startedAtLocal
            };
        }
        catch (Exception ex)
        {
            return new
            {
                ConfiguredName = configuredName,
                ProcessName = processName,
                IsConfigured = true,
                IsRunning = false,
                ProcessCount = 0,
                CpuPercent = (decimal?)null,
                WorkingSetMb = (decimal?)null,
                StartedAtLocal = (DateTime?)null,
                Error = ex.Message
            };
        }
    }

    private static string NormalizeProcessName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var name = value.Trim();

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        return name;
    }

    private static long SafeWorkingSet64(Process process)
    {
        try
        {
            return process.WorkingSet64;
        }
        catch
        {
            return 0;
        }
    }

    private static List<object> GetWindowsUpdates()
    {
        var updates = new List<object>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT HotFixID, Description, InstalledOn FROM Win32_QuickFixEngineering");

            foreach (ManagementObject obj in searcher.Get())
            {
                updates.Add(new
                {
                    HotFixId = obj["HotFixID"]?.ToString(),
                    Description = obj["Description"]?.ToString(),
                    InstalledOn = obj["InstalledOn"]?.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            updates.Add(new { Error = ex.Message });
        }

        return updates;
    }
    private static Encoding ResolveTextEncoding(string? encodingName)
    {
        if (string.IsNullOrWhiteSpace(encodingName))
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        return encodingName.Trim().ToLowerInvariant() switch
        {
            "utf-8" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            "utf-8-bom" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            "ascii" => Encoding.ASCII,
            "unicode" => Encoding.Unicode,
            _ => throw new Exception($"Unsupported encoding '{encodingName}'.")
        };
    }
    private static string Trunc(string s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return s;

        return s.Length <= max ? s : s.Substring(0, max);
    }
}
