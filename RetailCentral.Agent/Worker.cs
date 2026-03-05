using System.Text.Json;

public sealed class Worker : BackgroundService
{
    private readonly AgentConfig _cfg;
    private readonly AgentApiClient _api;
    private readonly CommandExecutor _exec;

    private DateTime _nextHeartbeatUtc = DateTime.MinValue;

    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    public Worker(AgentConfig cfg, AgentApiClient api, CommandExecutor exec)
    {
        _cfg = cfg;
        _api = api;
        _exec = exec;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // If DeviceId/Secret missing, enroll (new hostname recommended)
        if (string.IsNullOrWhiteSpace(_cfg.DeviceId) || string.IsNullOrWhiteSpace(_cfg.DeviceSecret))
        {
            Console.WriteLine("DeviceId/DeviceSecret missing. Enrolling...");

            // Retry enroll instead of crashing/exiting if server is temporarily down
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var enroll = await _api.EnrollAsync(stoppingToken);
                    if (enroll == null)
                    {
                        Console.WriteLine("Enroll returned no secret. This usually means device already exists. Use a NEW hostname or rotate secret.");
                        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                        return;
                    }

                    _cfg.DeviceId = enroll.Value.DeviceId.ToString();
                    _cfg.DeviceSecret = enroll.Value.DeviceSecret;

                    Console.WriteLine($"ENROLLED DeviceId={_cfg.DeviceId}");
                    Console.WriteLine($"DeviceSecret={_cfg.DeviceSecret}");
                    Console.WriteLine("Copy these into appsettings.json and restart the agent.");
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Enroll failed: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }

            return;
        }

        Console.WriteLine($"Agent started. DeviceId={_cfg.DeviceId}");

        _nextHeartbeatUtc = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Heartbeat
                if (DateTime.UtcNow >= _nextHeartbeatUtc)
                {
                    var hb = new
                    {
                        timestampUtc = DateTime.UtcNow,
                        storeNumber = _cfg.StoreNumber,
                        hostname = _cfg.Hostname,
                        agentVersion = _cfg.AgentVersion,
                        osVersion = Environment.OSVersion.VersionString,
                        metrics = new { cpu = Environment.ProcessorCount },
                        extra = new { note = "Day5 agent heartbeat" }
                    };

                    await _api.HeartbeatAsync(hb, stoppingToken);
                    _nextHeartbeatUtc = DateTime.UtcNow.AddSeconds(_cfg.HeartbeatSeconds);

                    Console.WriteLine($"Heartbeat OK @ {DateTime.UtcNow:o}");
                }

                // Poll pending
                var json = await _api.GetPendingAsync(_cfg.MaxPendingFetch, stoppingToken);

                var commands = JsonSerializer.Deserialize<List<PendingCommand>>(json, JsonOpts) ?? new();

                foreach (var cmd in commands)
                {
                    // Guard against bad JSON mapping or unexpected payloads
                    if (cmd.CommandId == Guid.Empty || string.IsNullOrWhiteSpace(cmd.Type))
                    {
                        Console.WriteLine($"WARNING: Skipping invalid command payload: {JsonSerializer.Serialize(cmd)}");
                        continue;
                    }

                    Console.WriteLine($"Executing CommandId={cmd.CommandId} Type={cmd.Type}");

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

                    Console.WriteLine($"Posted result CommandId={cmd.CommandId} Status={status} ExitCode={exit}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Agent loop error: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(_cfg.PollSeconds), stoppingToken);
        }
    }

    private sealed class PendingCommand
    {
        public Guid CommandId { get; set; }
        public string? Type { get; set; }
        public string? Scope { get; set; }
        public string? PayloadJson { get; set; }
    }
}
