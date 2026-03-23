using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Management;

public sealed class Worker : BackgroundService
{
    private readonly AgentConfig _cfg;
    private readonly AgentApiClient _api;
    private readonly CommandExecutor _exec;
    private readonly ILogger<Worker> _logger;

    private DateTime _nextHeartbeatUtc = DateTime.MinValue;

    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public Worker(AgentConfig cfg, AgentApiClient api, CommandExecutor exec, ILogger<Worker> logger)
    {
        _cfg = cfg;
        _api = api;
        _exec = exec;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_cfg.DeviceId) || string.IsNullOrWhiteSpace(_cfg.DeviceSecret))
        {
            _logger.LogInformation("DeviceId/DeviceSecret missing. Enrolling...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var enroll = await _api.EnrollAsync(stoppingToken);
                    if (enroll == null)
                    {
                        _logger.LogWarning("Enroll returned no secret. This usually means device already exists. Use a NEW hostname or rotate secret.");
                        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                        return;
                    }

                    _cfg.DeviceId = enroll.Value.DeviceId.ToString();

                    var protectedSecret = DeviceSecretStore.Protect(enroll.Value.DeviceSecret);

                    _cfg.DeviceSecret = enroll.Value.DeviceSecret;
                    _cfg.DeviceSecretProtected = protectedSecret;

                    var appsettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json");
                    AgentConfigWriter.SaveProtectedSecret(appsettingsPath, _cfg.DeviceId, protectedSecret);

                    _logger.LogInformation("ENROLLED DeviceId={DeviceId}", _cfg.DeviceId);
                    _logger.LogInformation("Device secret was received, protected, and written to appsettings.Local.json.");
                    _logger.LogInformation("Restart the agent/service so it continues using protected credentials.");

                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Enroll failed");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }

            return;
        }

        _logger.LogInformation("Agent started. DeviceId={DeviceId}", _cfg.DeviceId);

        _nextHeartbeatUtc = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow >= _nextHeartbeatUtc)
                {
                    RefreshRegisterMetadataWithDetectedValues();

                    var hb = new
                    {
                        timestampUtc = DateTime.UtcNow,
                        storeNumber = _cfg.StoreNumber,
                        hostname = _cfg.Hostname,
                        agentVersion = _cfg.AgentVersion,
                        osVersion = Environment.OSVersion.VersionString,
                        metrics = new
                        {
                            processorCount = Environment.ProcessorCount,
                            workingSetMb = Math.Round(Environment.WorkingSet / 1024d / 1024d, 2),
                            machineName = Environment.MachineName,
                            is64Bit = Environment.Is64BitOperatingSystem
                        },
                        inventory = BuildHeartbeatInventory(),
                        extra = new { note = "Agent heartbeat" }
                    };

                    await _api.HeartbeatAsync(hb, stoppingToken);
                    _nextHeartbeatUtc = DateTime.UtcNow.AddSeconds(_cfg.HeartbeatSeconds);

                    _logger.LogInformation("Heartbeat OK at {UtcNow}", DateTime.UtcNow);
                }

                var json = await _api.GetPendingAsync(_cfg.MaxPendingFetch, stoppingToken);

                var commands = JsonSerializer.Deserialize<List<PendingCommand>>(json, JsonOpts) ?? new();

                foreach (var cmd in commands)
                {
                    if (cmd.CommandId == Guid.Empty || string.IsNullOrWhiteSpace(cmd.Type))
                    {
                        _logger.LogWarning("Skipping invalid command payload: {Payload}", JsonSerializer.Serialize(cmd));
                        continue;
                    }

                    _logger.LogInformation("Executing CommandId={CommandId} Type={Type}", cmd.CommandId, cmd.Type);

                    var (status, exit, stdout, stderr, started, finished) =
                        await _exec.ExecuteAsync(cmd.Type, cmd.PayloadJson, stoppingToken);

                    var resultBody = new
                    {
                        commandType = cmd.Type,
                        status,
                        exitCode = exit,
                        stdOut = stdout,
                        stdErr = stderr,
                        startedUtc = started,
                        finishedUtc = finished
                    };

                    await _api.PostResultAsync(cmd.CommandId, resultBody, stoppingToken);

                    _logger.LogInformation(
                        "Posted result CommandId={CommandId} Status={Status} ExitCode={ExitCode}",
                        cmd.CommandId,
                        status,
                        exit);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Agent loop error");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(_cfg.PollSeconds), stoppingToken);
        }
    }

    private object BuildHeartbeatInventory()
    {
        var store = _cfg.StoreNumber ?? "";

        var hostnameForRegister = !string.IsNullOrWhiteSpace(_cfg.Hostname)
            ? _cfg.Hostname
            : Environment.MachineName;

        var regNum = !string.IsNullOrWhiteSpace(_cfg.RegisterNumber)
            ? _cfg.RegisterNumber
            : ParseRegisterFromHostname(hostnameForRegister);

        var adapters = NetworkInterface
            .GetAllNetworkInterfaces()
            .Where(n =>
                n.OperationalStatus == OperationalStatus.Up &&
                n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .Select(n => new
            {
                Nic = n,
                Props = n.GetIPProperties(),
                MacBytes = n.GetPhysicalAddress().GetAddressBytes()
            })
            .Where(x =>
                x.Props.UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork) &&
                x.MacBytes.Length > 0)
            .OrderByDescending(x =>
                x.Nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                x.Nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            .ToList();

        var primaryNic = adapters.FirstOrDefault();

        string? ip = primaryNic?.Props.UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            ?.Address.ToString();

        string? mac = null;
        if (primaryNic != null && primaryNic.MacBytes.Length > 0)
        {
            mac = string.Join("-", primaryNic.MacBytes.Select(b => b.ToString("X2")));
        }

        var metadata = ReadRegisterMetadata();

        _logger.LogInformation(
            "Inventory details: Host={Host} Store={Store} Register={Register} IP={IP} MAC={MAC}",
            hostnameForRegister,
            store,
            regNum,
            ip ?? "(null)",
            mac ?? "(null)");

        _logger.LogInformation(
            "Metadata loaded: StoreName={StoreName}, StoreAddress={StoreAddress}, StoreCity={StoreCity}, StoreState={StoreState}, StoreZipCode={StoreZipCode}, ReleaseLevel={ReleaseLevel}, ReleaseApplied={ReleaseApplied}, VerifoneModel={VerifoneModel}, VerifoneIP={VerifoneIP}, ScannerName={ScannerName}, ScannerSerialNumber={ScannerSerialNumber}",
            metadata.StoreName ?? "(null)",
            metadata.StoreAddress ?? "(null)",
            metadata.StoreCity ?? "(null)",
            metadata.StoreState ?? "(null)",
            metadata.StoreZipCode ?? "(null)",
            metadata.ReleaseLevel ?? "(null)",
            metadata.ReleaseApplied ?? "(null)",
            metadata.VerifoneModel ?? "(null)",
            metadata.VerifoneIP ?? "(null)",
            metadata.ScannerName ?? "(null)",
            metadata.ScannerSerialNumber ?? "(null)");

        return new
        {
            computerName = hostnameForRegister,
            store = store,
            registerNumber = regNum,
            ipAddress = ip,
            macAddress = mac,
            domain = Environment.UserDomainName,
            osVersion = Environment.OSVersion.ToString(),
            cpuArch = RuntimeInformation.OSArchitecture.ToString(),

            storeName = metadata.StoreName,
            storeAddress = metadata.StoreAddress,
            storeCity = metadata.StoreCity,
            storeState = metadata.StoreState,
            storeZipCode = metadata.StoreZipCode,
            releaseLevel = metadata.ReleaseLevel,
            releaseApplied = metadata.ReleaseApplied,
            verifoneModel = metadata.VerifoneModel,
            verifoneIP = metadata.VerifoneIP,
            scannerName = metadata.ScannerName,
            scannerSerialNumber = metadata.ScannerSerialNumber
        };
    }

    private void RefreshRegisterMetadataWithDetectedValues()
    {
        var path = _cfg.RegisterMetadataPath;
        _logger.LogInformation("Refreshing register metadata at {Path}", path);

        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning("RegisterMetadataPath is blank. Skipping metadata refresh.");
            return;
        }

        try
        {
            JsonObject root;

            if (!File.Exists(path))
            {
                root = new JsonObject();
            }
            else
            {
                var existingJson = File.ReadAllText(path);
                root = string.IsNullOrWhiteSpace(existingJson)
                    ? new JsonObject()
                    : (JsonNode.Parse(existingJson)?.AsObject() ?? new JsonObject());
            }

            var scannerInfo = DetectScannerInfo();

            if (!string.IsNullOrWhiteSpace(scannerInfo.ScannerName))
            {
                var currentScannerName = root["scannerName"]?.ToString();
                if (!string.Equals(currentScannerName, scannerInfo.ScannerName, StringComparison.Ordinal))
                {
                    root["scannerName"] = scannerInfo.ScannerName;
                }
            }

            if (!string.IsNullOrWhiteSpace(scannerInfo.ScannerSerialNumber))
            {
                var currentScannerSerial = root["scannerSerialNumber"]?.ToString();
                if (!string.Equals(currentScannerSerial, scannerInfo.ScannerSerialNumber, StringComparison.Ordinal))
                {
                    root["scannerSerialNumber"] = scannerInfo.ScannerSerialNumber;
                }
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, root.ToJsonString(JsonOpts));

            _logger.LogInformation(
                "Register metadata refresh complete. ScannerName={ScannerName}, ScannerSerialNumber={ScannerSerialNumber}",
                root["scannerName"]?.ToString() ?? "(null)",
                root["scannerSerialNumber"]?.ToString() ?? "(null)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh register metadata at {Path}", path);
        }
    }

    private RegisterMetadata ReadRegisterMetadata()
    {
        var path = _cfg.RegisterMetadataPath;
        _logger.LogInformation("Reading register metadata from {Path}", path);

        if (string.IsNullOrWhiteSpace(path))
            return new RegisterMetadata();

        try
        {
            if (!File.Exists(path))
            {
                _logger.LogInformation("Register metadata file not found at {Path}", path);
                return new RegisterMetadata();
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogInformation("Register metadata file is empty at {Path}", path);
                return new RegisterMetadata();
            }

            var metadata = JsonSerializer.Deserialize<RegisterMetadata>(json, JsonOpts);
            return metadata ?? new RegisterMetadata();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read register metadata from {Path}", path);
            return new RegisterMetadata();
        }
    }

    private (string? ScannerName, string? ScannerSerialNumber) DetectScannerInfo()
    {
        try
        {
            string? scannerName = null;
            string? scannerSerialNumber = null;

            using (var searcher = new ManagementObjectSearcher(@"root\CIMV2", "SELECT * FROM Symbol_BarcodeScanner"))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    scannerName = mo["Name"]?.ToString();
                    scannerSerialNumber = mo["SerialNumber"]?.ToString();

                    if (!string.IsNullOrWhiteSpace(scannerName) || !string.IsNullOrWhiteSpace(scannerSerialNumber))
                        break;
                }
            }

            if (!string.IsNullOrWhiteSpace(scannerName))
            {
                var normalizedName = NormalizeScannerName(scannerName);
                return (normalizedName, scannerSerialNumber);
            }

            using (var pnp = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%Barcode%' OR Name LIKE '%Scanner%'"))
            {
                foreach (ManagementObject mo in pnp.Get())
                {
                    var name = mo["Name"]?.ToString() ?? "";

                    if (name.IndexOf("HP ElitePOS 2D Barcode Scanner", StringComparison.OrdinalIgnoreCase) >= 0)
                        return ("HP ElitePOS 2D Barcode Scanner", null);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Scanner detection failed");
        }

        return (null, null);
    }

    private static string NormalizeScannerName(string scannerName)
    {
        if (string.IsNullOrWhiteSpace(scannerName))
            return "UnknownScanner";

        if (scannerName.Contains("4308", StringComparison.OrdinalIgnoreCase)) return "DS4308";
        if (scannerName.Contains("9208", StringComparison.OrdinalIgnoreCase)) return "DS9208";
        if (scannerName.Contains("DS4208-HD", StringComparison.OrdinalIgnoreCase)) return "DS4208-HD";
        if (scannerName.Contains("DS4208-SR", StringComparison.OrdinalIgnoreCase)) return "DS4208-SR";
        if (scannerName.Contains("DS4208-DL", StringComparison.OrdinalIgnoreCase)) return "DS4208-DL";
        if (scannerName.Contains("LS4208", StringComparison.OrdinalIgnoreCase)) return "LS4208";

        return scannerName;
    }

    private static string ParseRegisterFromHostname(string hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname))
            return "";

        var parts = hostname.Split('-', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
            return "";

        return parts[^1];
    }

    private sealed class PendingCommand
    {
        public Guid CommandId { get; set; }
        public string? Type { get; set; }
        public string? Scope { get; set; }
        public string? PayloadJson { get; set; }
    }

    private sealed class RegisterMetadata
    {
        public string? StoreName { get; set; }
        public string? StoreAddress { get; set; }
        public string? StoreCity { get; set; }
        public string? StoreState { get; set; }
        public string? StoreZipCode { get; set; }
        public string? ReleaseLevel { get; set; }
        public string? ReleaseApplied { get; set; }
        public string? VerifoneModel { get; set; }
        public string? VerifoneIP { get; set; }
        public string? ScannerName { get; set; }
        public string? ScannerSerialNumber { get; set; }
    }
}