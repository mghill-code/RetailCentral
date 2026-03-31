using System.ComponentModel.DataAnnotations;

namespace RetailCentral.Agent.Configuration;

public sealed class ExecutionOptions
{
    public const string SectionName = "Execution";

    [Range(1, 3600)]
    public int DefaultTimeoutSeconds { get; set; } = 30;

    [Range(1, 7200)]
    public int MaxTimeoutSeconds { get; set; } = 300;

    [Range(0, 5_000_000)]
    public int MaxStdoutChars { get; set; } = 100_000;

    [Range(0, 5_000_000)]
    public int MaxStderrChars { get; set; } = 100_000;

    [Range(1, 3600)]
    public int InstallCheckSeconds { get; set; } = 30;

    public bool AllowRunProcess { get; set; } = false;
    public bool RequireAbsolutePath { get; set; } = true;
    public bool DisallowRelativePaths { get; set; } = true;
    public bool DisallowUNCPaths { get; set; } = true;
    public bool DisallowCmdExe { get; set; } = true;
    public bool DisallowPowershell { get; set; } = true;

    public List<string> AllowedCommands { get; set; } = new();
    public List<string> TrustedExecutionRoots { get; set; } = new();
    public List<string> BlockedPathPrefixes { get; set; } = new();
    public List<AllowedExecutableOptions> AllowedExecutables { get; set; } = new();
}

public sealed class AllowedExecutableOptions
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Path { get; set; } = string.Empty;

    public string? WorkingDirectory { get; set; }

    [Range(1, 7200)]
    public int TimeoutSeconds { get; set; } = 60;

    public bool AllowFromCommandPayload { get; set; } = false;

    public List<string> AllowedArguments { get; set; } = new();
    public List<string> AllowedArgumentsRegex { get; set; } = new();
}

public sealed class DownloadsOptions
{
    public const string SectionName = "Downloads";

    [Required]
    public string RootFolder { get; set; } = string.Empty;

    [Required]
    public string StagingRootFolder { get; set; } = string.Empty;

    public List<string> AllowedHosts { get; set; } = new();
    public bool RequireHttps { get; set; } = true;
    public bool RequireHashValidation { get; set; } = true;
    public bool DisallowExecuteFromDownloadsRoot { get; set; } = true;
    public List<string> AllowedExtensions { get; set; } = new();
}