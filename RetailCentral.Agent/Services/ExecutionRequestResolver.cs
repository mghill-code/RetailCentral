using RetailCentral.Agent.Configuration;
using RetailCentral.Agent.Models;

namespace RetailCentral.Agent.Services;

public sealed class ExecutionRequestResolver
{
    private readonly ExecutionPolicyService _policy;

    public ExecutionRequestResolver(ExecutionPolicyService policy)
    {
        _policy = policy;
    }

    public ResolvedExecution Resolve(ExecutionRequest request)
    {
        var profile = _policy.GetRequiredProfile(request.ProfileName);

        _policy.ValidateExecutablePath(profile.Path);
        _policy.ValidateArguments(profile, request.Arguments);

        var workingDirectory = string.IsNullOrWhiteSpace(request.OverrideWorkingDirectory)
            ? profile.WorkingDirectory
            : request.OverrideWorkingDirectory;

        if (!string.IsNullOrWhiteSpace(workingDirectory))
            _policy.ValidateExecutablePath(Path.Combine(workingDirectory, "dummy.exe"));

        return new ResolvedExecution
        {
            ProfileName = profile.Name,
            FileName = profile.Path,
            Arguments = request.Arguments ?? string.Empty,
            WorkingDirectory = workingDirectory,
            TimeoutSeconds = profile.TimeoutSeconds
        };
    }
}

public sealed class ResolvedExecution
{
    public string ProfileName { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
    public string? WorkingDirectory { get; init; }
    public int TimeoutSeconds { get; init; }
}