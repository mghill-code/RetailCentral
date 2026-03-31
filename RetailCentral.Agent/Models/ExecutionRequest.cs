namespace RetailCentral.Agent.Models;

public sealed class ExecutionRequest
{
    public string ProfileName { get; init; } = string.Empty;
    public string? Arguments { get; init; }
    public string? OverrideWorkingDirectory { get; init; }
}