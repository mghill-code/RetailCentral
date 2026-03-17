using System.Diagnostics;

public sealed class PackageExecutionService
{
    public async Task<(bool TimedOut, int? ExitCode, string StdOut, string StdErr)> ExecuteAsync(
        string fileName,
        string? arguments,
        string? workingDirectory,
        int timeoutSeconds,
        int maxStdoutChars,
        int maxStderrChars,
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
            psi.WorkingDirectory = workingDirectory;

        using var p = Process.Start(psi) ?? throw new Exception("Failed to start install process");

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

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

            return (true, null, "", $"Timed out after {timeoutSeconds}s");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (!string.IsNullOrEmpty(stdout) && stdout.Length > maxStdoutChars)
            stdout = stdout.Substring(0, maxStdoutChars);

        if (!string.IsNullOrEmpty(stderr) && stderr.Length > maxStderrChars)
            stderr = stderr.Substring(0, maxStderrChars);

        return (false, p.ExitCode, stdout, stderr);
    }
}