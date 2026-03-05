using System.Diagnostics;
using System.Text;
using System.Text.Json;

public sealed class CommandExecutor
{
    private readonly AgentConfig _cfg;

    public CommandExecutor(AgentConfig cfg)
    {
        _cfg = cfg;
    }

    public async Task<(string Status, int ExitCode, string StdOut, string StdErr, DateTime StartedUtc, DateTime FinishedUtc)> ExecuteAsync(
        string type,
        string? payloadJson,
        CancellationToken ct)
    {
        var started = DateTime.UtcNow;

        // Allowlist enforcement
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
                        var sb = new StringBuilder();
                        sb.AppendLine($"MachineName={Environment.MachineName}");
                        sb.AppendLine($"OS={Environment.OSVersion}");
                        sb.AppendLine($"64BitOS={Environment.Is64BitOperatingSystem}");
                        sb.AppendLine($"ProcessorCount={Environment.ProcessorCount}");
                        sb.AppendLine($"UserName={Environment.UserName}");

                        var finished = DateTime.UtcNow;
                        return ("Succeeded", 0, Trunc(sb.ToString(), _cfg.MaxStdoutChars), "", started, finished);
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

    private static string Trunc(string s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return s;

        return s.Length <= max ? s : s.Substring(0, max);
    }
}