using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Management;

[SupportedOSPlatform("windows")]
public sealed class Worker : BackgroundService
{
    private readonly AgentConfig _cfg;
    private readonly AgentApiClient _api;
    private readonly CommandExecutor _exec;
    private readonly ILogger<Worker> _logger;
    private readonly UserActivitySnapshotReader _userActivityReader;
    private readonly AgentPollingOptions _pollingOptions;
    private readonly Random _random = new();

    private DateTime _nextHeartbeatUtc = DateTime.MinValue;
    private int _emptyPollStreak;
    private int _errorStreak;
    private DateTime _fastPollUntilUtc = DateTime.MinValue;

    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public Worker(
        AgentConfig cfg,
        AgentApiClient api,
        CommandExecutor exec,
        ILogger<Worker> logger,
        IOptions<AgentPollingOptions> pollingOptions)
    {
        _cfg = cfg;
        _api = api;
        _exec = exec;
        _logger = logger;
        _pollingOptions = pollingOptions.Value;

        var snapshotPath = !string.IsNullOrWhiteSpace(_cfg.UserActivitySnapshotPath)
            ? _cfg.UserActivitySnapshotPath
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "RetailCentral",
                "Shared",
                "UserActivity.json");

        _logger.LogInformation("User activity snapshot path resolved to: {SnapshotPath}", snapshotPath);

        _userActivityReader = new UserActivitySnapshotReader(snapshotPath, _logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_cfg.DeviceId) || string.IsNullOrWhiteSpace(_cfg.DeviceSecret))
        {
            var enrolled = await TryEnrollAsync(stoppingToken);
            if (!enrolled)
            {
                return;
            }
        }

        _logger.LogInformation("Agent started. DeviceId={DeviceId}", _cfg.DeviceId);

        _nextHeartbeatUtc = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            bool gotCommands = false;
            bool hadTransientError = false;
            int? serverSuggestedSeconds = null;

            try
            {
                if (DateTime.UtcNow >= _nextHeartbeatUtc)
                {
                    var heartbeat = BuildHeartbeatPayload();
                    await _api.HeartbeatAsync(heartbeat, stoppingToken);

                    _nextHeartbeatUtc = DateTime.UtcNow.AddSeconds(_cfg.HeartbeatSeconds);
                    _logger.LogInformation("Heartbeat OK at {UtcNow}", DateTime.UtcNow);
                }

                var pollResult = await _api.GetPendingAsync(_cfg.MaxPendingFetch, stoppingToken);

                var commands = pollResult.Commands ?? new List<PendingCommandEnvelope>();
                serverSuggestedSeconds = pollResult.PollAfterSeconds;
                gotCommands = commands.Count > 0;

                _logger.LogInformation(
                    "Pending command poll complete. CommandCount={CommandCount}, ServerPollAfter={ServerPollAfter}",
                    commands.Count,
                    serverSuggestedSeconds);

                foreach (var cmd in commands)
                {
                    if (cmd.CommandId == Guid.Empty || string.IsNullOrWhiteSpace(cmd.Type))
                    {
                        _logger.LogWarning("Skipping invalid command payload: {@Command}", cmd);
                        continue;
                    }

                    _logger.LogInformation("Executing CommandId={CommandId} Type={Type}", cmd.CommandId, cmd.Type);

                    (string Status, int ExitCode, string StdOut, string StdErr, DateTime StartedUtc, DateTime FinishedUtc) result =
                        await _exec.ExecuteAsync(cmd.Type, cmd.PayloadJson, stoppingToken);

                    var resultBody = new
                    {
                        commandType = cmd.Type,
                        status = result.Status,
                        exitCode = result.ExitCode,
                        stdOut = result.StdOut,
                        stdErr = result.StdErr,
                        startedUtc = result.StartedUtc,
                        finishedUtc = result.FinishedUtc
                    };

                    await _api.PostResultAsync(cmd.CommandId, resultBody, stoppingToken);

                    _logger.LogInformation(
                        "Posted result CommandId={CommandId} Status={Status} ExitCode={ExitCode}",
                        cmd.CommandId,
                        result.Status,
                        result.ExitCode);
                }

                // Successful heartbeat + pending poll cycle.
                // Clear any stale error state so the agent recovers immediately
                // after the API becomes reachable again.
                _errorStreak = 0;

                // If the server is explicitly directing cadence, let that fully control
                // the next delay instead of retaining a stale fast-poll window.
                if (serverSuggestedSeconds.HasValue && serverSuggestedSeconds.Value > 0)
                {
                    _fastPollUntilUtc = DateTime.MinValue;
                }
            }
            catch (HttpRequestException ex)
            {
                hadTransientError = true;
                _logger.LogWarning(ex, "Transient agent polling error.");
            }
            catch (TaskCanceledException ex) when (!stoppingToken.IsCancellationRequested)
            {
                hadTransientError = true;
                _logger.LogWarning(ex, "Agent polling timed out.");
            }
            catch (Exception ex)
            {
                hadTransientError = true;
                _logger.LogError(ex, "Agent loop error");
            }

            var delay = GetNextPollDelay(gotCommands, hadTransientError, serverSuggestedSeconds);

            _logger.LogInformation(
                "Next poll in {DelaySeconds:n1}s. GotCommands={GotCommands}, Error={HadTransientError}, EmptyPollStreak={EmptyPollStreak}, ErrorStreak={ErrorStreak}, FastPollUntilUtc={FastPollUntilUtc:o}",
                delay.TotalSeconds,
                gotCommands,
                hadTransientError,
                _emptyPollStreak,
                _errorStreak,
                _fastPollUntilUtc);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<bool> TryEnrollAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DeviceId/DeviceSecret missing. Enrolling...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var enroll = await _api.EnrollAsync(stoppingToken);
                if (enroll == null)
                {
                    _logger.LogWarning("Enroll returned no secret. This usually means device already exists. Use a new hostname or rotate the secret.");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    return false;
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

                return false;
            }
            catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Enroll failed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        return false;
    }

    private object BuildHeartbeatPayload()
    {
        RefreshRegisterMetadataWithDetectedValues();

        var userActivity = _cfg.UserActivityEnabled
            ? _userActivityReader.Read()
            : null;

        _logger.LogInformation("User activity snapshot read: {@UserActivity}", userActivity);

        if (userActivity == null && _cfg.UserActivityEnabled)
        {
            _logger.LogDebug("User activity snapshot not available for this heartbeat.");
        }

        return new
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
            userActivity = userActivity,
            extra = new { note = "Agent heartbeat" }
        };
    }

    private TimeSpan GetNextPollDelay(
        bool gotCommands,
        bool hadTransientError,
        int? serverSuggestedSeconds = null)
    {
        if (serverSuggestedSeconds.HasValue && serverSuggestedSeconds.Value > 0)
        {
            return AddJitter(TimeSpan.FromSeconds(serverSuggestedSeconds.Value));
        }

        if (hadTransientError)
        {
            _errorStreak++;
            _emptyPollStreak = 0;

            var errorDelaySeconds = Math.Min(
                _pollingOptions.MaxErrorBackoffSeconds,
                _pollingOptions.InitialErrorBackoffSeconds * (int)Math.Pow(2, Math.Max(0, _errorStreak - 1)));

            return AddJitter(TimeSpan.FromSeconds(errorDelaySeconds));
        }

        _errorStreak = 0;

        if (gotCommands)
        {
            _emptyPollStreak = 0;
            _fastPollUntilUtc = DateTime.UtcNow.AddSeconds(_pollingOptions.FastPollWindowSeconds);
            return AddJitter(TimeSpan.FromSeconds(_pollingOptions.MinPollSeconds));
        }

        if (DateTime.UtcNow < _fastPollUntilUtc)
        {
            return AddJitter(TimeSpan.FromSeconds(_pollingOptions.MinPollSeconds));
        }

        _emptyPollStreak++;

        var idleDelaySeconds = _emptyPollStreak switch
        {
            1 => _pollingOptions.BasePollSeconds,
            2 => Math.Min(_pollingOptions.MaxIdlePollSeconds, 20),
            3 => Math.Min(_pollingOptions.MaxIdlePollSeconds, 30),
            4 => Math.Min(_pollingOptions.MaxIdlePollSeconds, 45),
            _ => _pollingOptions.MaxIdlePollSeconds
        };

        return AddJitter(TimeSpan.FromSeconds(idleDelaySeconds));
    }

    private TimeSpan AddJitter(TimeSpan baseDelay)
    {
        var jitterPercent = Math.Max(0, _pollingOptions.JitterPercent);
        var spread = jitterPercent / 100.0;

        var factor = 1.0;

        if (spread > 0)
        {
            var min = 1.0 - spread;
            var max = 1.0 + spread;
            factor = min + (_random.NextDouble() * (max - min));
        }

        var jitteredMs = Math.Max(1000, baseDelay.TotalMilliseconds * factor);
        return TimeSpan.FromMilliseconds(jitteredMs);
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
