using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using RetailCentral.Agent.Configuration;

namespace RetailCentral.Agent.Services;

public sealed class ExecutionPolicyService
{
    private static readonly string[] DangerousFileNames =
    [
        "cmd.exe",
        "powershell.exe",
        "pwsh.exe",
        "wscript.exe",
        "cscript.exe",
        "mshta.exe",
        "rundll32.exe",
        "regsvr32.exe"
    ];

    private readonly ExecutionOptions _execution;
    private readonly DownloadsOptions _downloads;

    public ExecutionPolicyService(
        IOptions<ExecutionOptions> execution,
        IOptions<DownloadsOptions> downloads)
    {
        _execution = execution.Value;
        _downloads = downloads.Value;
    }

    public bool IsCommandAllowed(string commandType) =>
        _execution.AllowedCommands.Contains(commandType, StringComparer.OrdinalIgnoreCase);

    public AllowedExecutableOptions GetRequiredProfile(string profileName)
    {
        var profile = _execution.AllowedExecutables
            .FirstOrDefault(x => string.Equals(x.Name, profileName, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
            throw new InvalidOperationException($"Execution profile '{profileName}' is not allowed.");

        return profile;
    }

    public void ValidateExecutablePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Executable path is required.");

        if (_execution.RequireAbsolutePath && !Path.IsPathRooted(path))
            throw new InvalidOperationException($"Executable path must be absolute: '{path}'");

        if (_execution.DisallowRelativePaths && !Path.IsPathRooted(path))
            throw new InvalidOperationException($"Relative executable path is not allowed: '{path}'");

        if (_execution.DisallowUNCPaths && path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"UNC executable path is not allowed: '{path}'");

        var normalized = Normalize(path);

        foreach (var blocked in _execution.BlockedPathPrefixes.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (normalized.StartsWith(Normalize(blocked), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Executable path is under a blocked prefix: '{path}'");
        }

        if (_execution.TrustedExecutionRoots.Count > 0 &&
            !_execution.TrustedExecutionRoots.Any(root =>
                normalized.StartsWith(Normalize(root), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Executable path is not under a trusted root: '{path}'");
        }

        var fileName = Path.GetFileName(path);

        if (_execution.DisallowCmdExe &&
            string.Equals(fileName, "cmd.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("cmd.exe is not allowed.");
        }

        if (_execution.DisallowPowershell &&
            (string.Equals(fileName, "powershell.exe", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(fileName, "pwsh.exe", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("PowerShell is not allowed.");
        }

        if (DangerousFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase) &&
            !_execution.AllowedExecutables.Any(x => string.Equals(Path.GetFileName(x.Path), fileName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Dangerous interpreter or utility is not allowed: '{fileName}'");
        }
    }

    public void ValidateArguments(AllowedExecutableOptions profile, string? arguments)
    {
        arguments ??= string.Empty;

        if (profile.AllowedArguments.Count == 0 && profile.AllowedArgumentsRegex.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(arguments))
                throw new InvalidOperationException($"Execution profile '{profile.Name}' does not allow arguments.");
            return;
        }

        if (profile.AllowedArguments.Any(a => string.Equals(a, arguments, StringComparison.Ordinal)))
            return;

        foreach (var pattern in profile.AllowedArgumentsRegex)
        {
            if (Regex.IsMatch(arguments, pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
                return;
        }

        throw new InvalidOperationException(
            $"Arguments are not allowed for execution profile '{profile.Name}'. Value: '{arguments}'");
    }

    public void ValidateDownload(Uri uri, string? expectedFileName = null)
    {
        if (_downloads.RequireHttps && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Only HTTPS downloads are allowed: '{uri}'");

        if (_downloads.AllowedHosts.Count > 0 &&
            !_downloads.AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Download host is not allowed: '{uri.Host}'");
        }

        if (!string.IsNullOrWhiteSpace(expectedFileName))
        {
            var ext = Path.GetExtension(expectedFileName);
            if (_downloads.AllowedExtensions.Count > 0 &&
                !_downloads.AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Downloaded file extension is not allowed: '{ext}'");
            }
        }
    }

    public void ValidateExecutionFromPath(string path)
    {
        var normalized = Normalize(path);
        var downloadsRoot = Normalize(_downloads.RootFolder);

        if (_downloads.DisallowExecuteFromDownloadsRoot &&
            normalized.StartsWith(downloadsRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Execution directly from downloads root is not allowed: '{path}'");
        }

        ValidateExecutablePath(path);
    }

    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }
}