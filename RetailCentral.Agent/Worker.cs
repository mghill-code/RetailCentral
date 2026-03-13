using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

public sealed class Worker : BackgroundService
{
    private readonly AgentConfig _cfg;
    private readonly AgentApiClient _api;
    private readonly CommandExecutor _exec;
    private readonly ILogger<Worker> _logger;

    private DateTime _nextHeartbeatUtc = DateTime.MinValue;

    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
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

                    var appsettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                    AgentConfigWriter.SaveProtectedSecret(appsettingsPath, _cfg.DeviceId, protectedSecret);

                    _logger.LogInformation("ENROLLED DeviceId={DeviceId}", _cfg.DeviceId);
                    _logger.LogInformation("Device secret was received, protected, and written to appsettings.json.");
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

    private static object BuildHeartbeatInventory()
    {
        var store = Environment.GetEnvironmentVariable("Store") ?? "";
        var regNum = Environment.GetEnvironmentVariable("REG_NUM") ?? "";

        var primaryNic = NetworkInterface
            .GetAllNetworkInterfaces()
            .Where(n =>
                n.OperationalStatus == OperationalStatus.Up &&
                n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n => new
            {
                Nic = n,
                Props = n.GetIPProperties()
            })
            .FirstOrDefault(x => x.Props.UnicastAddresses.Any(a =>
                a.Address.AddressFamily == AddressFamily.InterNetwork));

        string? ip = primaryNic?.Props.UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            ?.Address.ToString();

        string? mac = null;
        if (primaryNic != null)
        {
            var bytes = primaryNic.Nic.GetPhysicalAddress().GetAddressBytes();
            if (bytes.Length > 0)
                mac = string.Join("-", bytes.Select(b => b.ToString("X2")));
        }

        return new
        {
            computerName = Environment.MachineName,
            store = store,
            registerNumber = regNum,
            ipAddress = ip,
            macAddress = mac,
            domain = Environment.UserDomainName,
            osVersion = Environment.OSVersion.ToString(),
            cpuArch = RuntimeInformation.OSArchitecture.ToString()
        };
    }

    private sealed class PendingCommand
    {
        public Guid CommandId { get; set; }
        public string? Type { get; set; }
        public string? Scope { get; set; }
        public string? PayloadJson { get; set; }
    }
}