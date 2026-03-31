using Microsoft.Extensions.Options;

namespace RetailCentral.Agent.Configuration;

public sealed class ExecutionOptionsValidator : IValidateOptions<ExecutionOptions>
{
    public ValidateOptionsResult Validate(string? name, ExecutionOptions options)
    {
        var errors = new List<string>();

        if (options.DefaultTimeoutSeconds <= 0)
            errors.Add("Execution:DefaultTimeoutSeconds must be greater than 0.");

        if (options.MaxTimeoutSeconds <= 0)
            errors.Add("Execution:MaxTimeoutSeconds must be greater than 0.");

        if (options.DefaultTimeoutSeconds > options.MaxTimeoutSeconds)
            errors.Add("Execution:DefaultTimeoutSeconds cannot exceed Execution:MaxTimeoutSeconds.");

        var duplicateCommands = options.AllowedCommands
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateCommands.Count > 0)
            errors.Add($"Execution:AllowedCommands contains duplicates: {string.Join(", ", duplicateCommands)}");

        var duplicateProfiles = options.AllowedExecutables
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateProfiles.Count > 0)
            errors.Add($"Execution:AllowedExecutables contains duplicate profile names: {string.Join(", ", duplicateProfiles)}");

        foreach (var profile in options.AllowedExecutables)
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
                errors.Add("Execution:AllowedExecutables contains an entry with no Name.");

            if (string.IsNullOrWhiteSpace(profile.Path))
                errors.Add($"Execution profile '{profile.Name}' is missing Path.");

            if (profile.TimeoutSeconds <= 0)
                errors.Add($"Execution profile '{profile.Name}' must have TimeoutSeconds > 0.");

            if (profile.TimeoutSeconds > options.MaxTimeoutSeconds)
                errors.Add($"Execution profile '{profile.Name}' TimeoutSeconds exceeds Execution:MaxTimeoutSeconds.");
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}