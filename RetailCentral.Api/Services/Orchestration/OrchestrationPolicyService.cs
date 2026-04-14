using System.Text.Json;
using Microsoft.Extensions.Options;
using RetailCentral.Api.Configuration;
using RetailCentral.Api.Data.Entities.Orchestration;

namespace RetailCentral.Api.Services.Orchestration
{
    /// <summary>
    /// Validates orchestration template steps against the orchestration-specific
    /// server-side command policy.
    /// </summary>
    public sealed class OrchestrationPolicyService
    {
        private readonly CommandPolicyOptions _policy;

        public OrchestrationPolicyService(IOptions<CommandPolicyOptions> policy)
        {
            _policy = policy.Value;
        }

        public List<string> ValidateTemplateStep(OrchestrationTemplateStep step)
        {
            var errors = new List<string>();

            var commandType = !string.IsNullOrWhiteSpace(step.CommandType)
                ? step.CommandType.Trim()
                : ResolveFallbackCommandType(step.StepType);

            var orchestrationAllowed = new HashSet<string>(
                _policy.OrchestrationAllowedCommandTypes ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(commandType) && !orchestrationAllowed.Contains(commandType))
            {
                errors.Add($"Command type '{commandType}' is not allowed for orchestration.");
            }

            if (step.TimeoutSeconds <= 0 || step.TimeoutSeconds > _policy.MaxExecutionTimeoutSeconds)
            {
                errors.Add($"TimeoutSeconds must be between 1 and {_policy.MaxExecutionTimeoutSeconds}.");
            }

            if (string.Equals(commandType, "ValidateProcess", StringComparison.OrdinalIgnoreCase))
            {
                ValidateValidateProcessPayload(step.ParametersJson, errors);
            }

            if (string.Equals(commandType, "InstallPackage", StringComparison.OrdinalIgnoreCase))
            {
                ValidateInstallPackagePayload(step.ParametersJson, errors);
            }

            return errors;
        }

        private void ValidateValidateProcessPayload(string? parametersJson, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(parametersJson))
            {
                errors.Add("ValidateProcess requires parametersJson.");
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(parametersJson);

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    errors.Add("ValidateProcess parametersJson must be a JSON object.");
                    return;
                }

                if (!TryGetRequiredString(doc.RootElement, "processName", out _))
                {
                    errors.Add("ValidateProcess parametersJson requires 'processName'.");
                }
            }
            catch (JsonException ex)
            {
                errors.Add($"ValidateProcess parametersJson is not valid JSON: {ex.Message}");
            }
        }

        private void ValidateInstallPackagePayload(string? parametersJson, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(parametersJson))
            {
                errors.Add("InstallPackage requires parametersJson.");
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(parametersJson);

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    errors.Add("InstallPackage parametersJson must be a JSON object.");
                    return;
                }

                if (!TryGetRequiredString(doc.RootElement, "downloadUrl", out var downloadUrl))
                {
                    errors.Add("InstallPackage parametersJson requires 'downloadUrl'.");
                }
                else if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
                {
                    if (_policy.RequireHttpsDownloads &&
                        !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add("InstallPackage downloadUrl must use HTTPS.");
                    }

                    if (_policy.AllowedDownloadHosts.Count > 0 &&
                        !_policy.AllowedDownloadHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
                    {
                        errors.Add($"InstallPackage host '{uri.Host}' is not allowed.");
                    }
                }
                else
                {
                    errors.Add("InstallPackage downloadUrl is invalid.");
                }

                if (!TryGetRequiredString(doc.RootElement, "fileName", out _))
                {
                    errors.Add("InstallPackage parametersJson requires 'fileName'.");
                }

                if (!TryGetRequiredString(doc.RootElement, "installCommand", out var installCommand))
                {
                    errors.Add("InstallPackage parametersJson requires 'installCommand'.");
                }
                else if (_policy.AllowedInstallProfiles.Count > 0 &&
                         !_policy.AllowedInstallProfiles.Contains(installCommand, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"InstallPackage installCommand '{installCommand}' is not an approved install profile.");
                }

                if (_policy.RequireSha256ForInstallPackage &&
                    !TryGetRequiredString(doc.RootElement, "sha256", out _))
                {
                    errors.Add("InstallPackage parametersJson requires 'sha256'.");
                }
            }
            catch (JsonException ex)
            {
                errors.Add($"InstallPackage parametersJson is not valid JSON: {ex.Message}");
            }
        }

        private static string ResolveFallbackCommandType(OrchestrationStepType stepType)
        {
            return stepType switch
            {
                OrchestrationStepType.CollectInventory => "CollectSystemInfo",
                OrchestrationStepType.RestartPos => "RestartPOS",
                OrchestrationStepType.RebootMachine => "RebootDevice",
                OrchestrationStepType.ValidateProcess => "ValidateProcess",
                _ => "RunCommand"
            };
        }

        private static bool TryGetRequiredString(JsonElement root, string propertyName, out string value)
        {
            value = string.Empty;

            if (!root.TryGetProperty(propertyName, out var prop))
                return false;

            if (prop.ValueKind != JsonValueKind.String)
                return false;

            value = prop.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }
    }
}