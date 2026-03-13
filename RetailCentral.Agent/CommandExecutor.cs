using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Management;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

public sealed class CommandExecutor
{
    private readonly AgentConfig _cfg;
    private readonly FileDownloadService _downloader;

    public CommandExecutor(AgentConfig cfg, FileDownloadService downloader)
    {
        _cfg = cfg;
        _downloader = downloader;
    }

    public async Task<(string Status, int ExitCode, string StdOut, string StdErr, DateTime StartedUtc, DateTime FinishedUtc)> ExecuteAsync(
        string type,
        string? payloadJson,
        CancellationToken ct)
    {
        var started = DateTime.UtcNow;

        if (!_cfg.AllowedCommands.Contains(type))
        {
            var finished = DateTime.UtcNow;
            return
            (
                "Failed",
                901,
                "",
                $"Command type '{type}' is not allowed by policy.",
                started,
                finished
            );
        }

        try
        {
            switch (type)
            {
                case "Echo":
                    {
                        var msg = payloadJson ?? "";
                        var finished = DateTime.UtcNow;
                        return ("Succeeded", 0, $"Echo: {msg}", "", started, finished);
                    }

                case "RunProcess":
                    {
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
                            : _cfg.DefaultTimeoutSeconds;

                        var psi = new ProcessStartInfo(fileName, args)
                        {
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        if (!string.IsNullOrWhiteSpace(wd))
                            psi.WorkingDirectory = wd;

                        using var p = Process.Start(psi) ?? throw new Exception("Failed to start process");

                        var stdoutTask = p.StandardOutput.ReadToEndAsync();
                        var stderrTask = p.StandardError.ReadToEndAsync();

                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

                        try
                        {
                            await p.WaitForExitAsync(timeoutCts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            try
                            {
                                if (!p.HasExited)
                                    p.Kill(true);
                            }
                            catch
                            {
                            }

                            var finishedTimeout = DateTime.UtcNow;
                            return ("Failed", 124, "", $"Timed out after {timeoutSec}s", started, finishedTimeout);
                        }

                        var stdout = await stdoutTask;
                        var stderr = await stderrTask;

                        stdout = Trunc(stdout, _cfg.MaxStdoutChars);
                        stderr = Trunc(stderr, _cfg.MaxStderrChars);

                        var exit = p.ExitCode;
                        var finished = DateTime.UtcNow;

                        return (exit == 0 ? "Succeeded" : "Failed", exit, stdout, stderr, started, finished);
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

                        var finished = DateTime.UtcNow;
                        return ("Succeeded", 0, Trunc(json, _cfg.MaxStdoutChars), "", started, finished);
                    }

                case "DownloadFile":
                    {
                        var doc = JsonDocument.Parse(payloadJson ?? "{}");
                        var root = doc.RootElement;

                        var url = root.GetProperty("url").GetString()
                                  ?? throw new Exception("url required");

                        var destinationFileName = root.TryGetProperty("destinationFileName", out var dest)
                            ? dest.GetString()
                            : null;

                        if (string.IsNullOrWhiteSpace(destinationFileName))
                        {
                            destinationFileName = Path.GetFileName(new Uri(url).AbsolutePath);
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

                        string actualSha256 = FileDownloadService.ComputeSha256Hex(downloadedPath);

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

                        var psi = new ProcessStartInfo(downloadedPath, arguments)
                        {
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WorkingDirectory = Path.GetDirectoryName(downloadedPath) ?? _cfg.DownloadRootFolder
                        };

                        using var p = Process.Start(psi) ?? throw new Exception("Failed to start downloaded file");

                        var stdoutTask = p.StandardOutput.ReadToEndAsync();
                        var stderrTask = p.StandardError.ReadToEndAsync();

                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_cfg.DefaultTimeoutSeconds));

                        try
                        {
                            await p.WaitForExitAsync(timeoutCts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            try
                            {
                                if (!p.HasExited)
                                    p.Kill(true);
                            }
                            catch
                            {
                            }

                            var finishedTimeout = DateTime.UtcNow;
                            return
                            (
                                "Failed",
                                124,
                                "",
                                $"Downloaded file execution timed out after {_cfg.DefaultTimeoutSeconds}s",
                                started,
                                finishedTimeout
                            );
                        }

                        var stdout = await stdoutTask;
                        var stderr = await stderrTask;

                        stdout = Trunc(stdout, _cfg.MaxStdoutChars);
                        stderr = Trunc(stderr, _cfg.MaxStderrChars);

                        var exit = p.ExitCode;
                        var finishedExec = DateTime.UtcNow;

                        return
                        (
                            exit == 0 ? "Succeeded" : "Failed",
                            exit,
                            $"Downloaded to: {downloadedPath}\nSHA256: {actualSha256}\n{stdout}",
                            stderr,
                            started,
                            finishedExec
                        );
                    }

                default:
                    {
                        var finished = DateTime.UtcNow;
                        return ("Failed", 2, "", $"Unknown command type '{type}'", started, finished);
                    }
            }
        }
        catch (Exception ex)
        {
            var finished = DateTime.UtcNow;
            return ("Failed", 1, "", Trunc(ex.ToString(), _cfg.MaxStderrChars), started, finished);
        }
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

    private static string Trunc(string s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return s;

        return s.Length <= max ? s : s.Substring(0, max);
    }
}