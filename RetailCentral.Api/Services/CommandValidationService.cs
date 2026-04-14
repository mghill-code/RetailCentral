using System.Text.Json;
using Microsoft.Extensions.Options;
using RetailCentral.Api.Configuration;
using RetailCentral.Api.Dtos;

namespace RetailCentral.Api.Services
{
    /// <summary>
    /// Performs API-side validation for direct/manual command creation requests.
    ///
    /// This validator is intended for:
    /// - helpdesk/manual API usage
    /// - dashboard/admin command creation
    ///
    /// Orchestration validation is handled separately so we can apply a different
    /// allowed-command set for zero-touch provisioning and workflow automation.
    /// </summary>
    public sealed class CommandValidationService
    {
        private readonly CommandPolicyOptions _policy;

        public CommandValidationService(IOptions<CommandPolicyOptions> policy)
        {
            _policy = policy.Value;
        }

        /// <summary>
        /// Validate a direct/manual command request.
        ///
        /// helpdeskMode=true applies the reduced HelpdeskAllowedCommandTypes policy.
        /// helpdeskMode=false applies the broader AllowedCommandTypes policy.
        /// </summary>
        public CommandValidationResult Validate(CreateCommandRequest request, bool helpdeskMode = false)
        {
            var errors = new List<string>();

            if (request == null)
            {
                errors.Add("Request body is required.");
                return CommandValidationResult.Fail(errors);
            }

            if (string.IsNullOrWhiteSpace(request.Type))
            {
                errors.Add("Type is required.");
                return CommandValidationResult.Fail(errors);
            }

            // Preserve the caller's intended exact command name after trimming.
            var normalizedType = request.Type.Trim();

            var allowedTypes = helpdeskMode
                ? _policy.HelpdeskAllowedCommandTypes
                : _policy.AllowedCommandTypes;

            var allowedTypeSet = new HashSet<string>(
                allowedTypes ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            if (!allowedTypeSet.Contains(normalizedType))
            {
                errors.Add($"Command type '{normalizedType}' is not allowed.");
            }

            if (!request.HasExactlyOneTarget())
            {
                errors.Add("Provide exactly one target: DeviceId, StoreNumber, or GroupName.");
            }

            if (request.Priority < 1 || request.Priority > 1000)
            {
                errors.Add("Priority must be between 1 and 1000.");
            }

            if (request.MaxAttempts < 1 || request.MaxAttempts > 20)
            {
                errors.Add("MaxAttempts must be between 1 and 20.");
            }

            switch (normalizedType)
            {
                case "Echo":
                    ValidateEcho(request.PayloadJson, errors);
                    break;

                case "CollectSystemInfo":
                case "CollectProcessStatus":
                case "CollectSoftwareInventory":
                case "RestartPOS":
                case "RestartRetailShell":
                case "RestartAgent":
                case "RebootDevice":
                case "ValidateProcess":
                    ValidateOptionalObjectPayload(request.PayloadJson, errors, normalizedType);
                    break;

                case "DownloadFile":
                    ValidateDownloadFile(request.PayloadJson, errors);
                    break;

                case "InstallPackage":
                    ValidateInstallPackage(request.PayloadJson, errors);
                    break;

                case "RunProcess":
                    ValidateRunProcess(request.PayloadJson, errors);
                    break;
            }

            return errors.Count == 0
                ? CommandValidationResult.Success(normalizedType)
                : CommandValidationResult.Fail(errors);
        }

        private void ValidateEcho(string? payloadJson, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
                return;

            try
            {
                using var doc = JsonDocument.Parse(payloadJson);

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    errors.Add("Echo payload must be a JSON object.");
                    return;
                }

                if (doc.RootElement.TryGetProperty("message", out var messageProp) &&
                    messageProp.ValueKind != JsonValueKind.String)
                {
                    errors.Add("Echo payload 'message' must be a string.");
                }
            }
            catch (JsonException ex)
            {
                errors.Add($"Echo payload is not valid JSON: {ex.Message}");
            }
        }

        private void ValidateOptionalObjectPayload(string? payloadJson, List<string> errors, string commandType)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
                return;

            try
            {
                using var doc = JsonDocument.Parse(payloadJson);

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    errors.Add($"{commandType} payload must be a JSON object.");
                    return;
                }

                if (string.Equals(commandType, "ValidateProcess", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryGetRequiredString(doc.RootElement, "processName", out _))
                    {
                        errors.Add("ValidateProcess payload requires 'processName'.");
                    }
                }
            }
            catch (JsonException ex)
            {
                errors.Add($"{commandType} payload is not valid JSON: {ex.Message}");
            }
        }

        private void ValidateRunProcess(string? payloadJson, List<string> errors)
        {
            if (!_policy.AllowRunProcess)
            {
                errors.Add("RunProcess is disabled by server policy.");
                return;
            }

            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                errors.Add("RunProcess requires payloadJson.");
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(payloadJson);

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    errors.Add("RunProcess payload must be a JSON object.");
                    return;
                }

                if (!TryGetRequiredString(doc.RootElement, "fileName", out _))
                {
                    errors.Add("RunProcess payload requires 'fileName'.");
                }
            }
            catch (JsonException ex)
            {
                errors.Add($"RunProcess payload is not valid JSON: {ex.Message}");
            }
        }

        private void ValidateDownloadFile(string? payloadJson, List<string> errors)
        {
            if (_policy.RequirePayloadJsonForDownloadFile && string.IsNullOrWhiteSpace(payloadJson))
            {
                errors.Add("DownloadFile requires payloadJson.");
                return;
            }

            if (string.IsNullOrWhiteSpace(payloadJson))
                return;

            try
            {
                using var doc = JsonDocument.Parse(payloadJson);

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    errors.Add("DownloadFile payload must be a JSON object.");
                    return;
                }

                if (!TryGetRequiredString(doc.RootElement, "url", out var url))
                {
                    errors.Add("DownloadFile payload requires 'url'.");
                    return;
                }

                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    errors.Add("DownloadFile 'url' is invalid.");
                    return;
                }

                if (_policy.RequireHttpsDownloads &&
                    !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("DownloadFile URL must use HTTPS.");
                }

                if (_policy.AllowedDownloadHosts.Count > 0 &&
                    !_policy.AllowedDownloadHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"DownloadFile host '{uri.Host}' is not allowed.");
                }

                var execute = doc.RootElement.TryGetProperty("execute", out var executeProp) &&
                              executeProp.ValueKind == JsonValueKind.True;

                if (execute &&
                    _policy.RequireSha256ForDownloadFileExecution &&
                    !TryGetRequiredString(doc.RootElement, "sha256", out _))
                {
                    errors.Add("DownloadFile with execute=true requires 'sha256'.");
                }
            }
            catch (JsonException ex)
            {
                errors.Add($"DownloadFile payload is not valid JSON: {ex.Message}");
            }
        }

        private void ValidateInstallPackage(string? payloadJson, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                errors.Add("InstallPackage requires payloadJson.");
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(payloadJson);

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    errors.Add("InstallPackage payload must be a JSON object.");
                    return;
                }

                if (!TryGetRequiredString(doc.RootElement, "downloadUrl", out var downloadUrl))
                {
                    errors.Add("InstallPackage payload requires 'downloadUrl'.");
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
                    errors.Add("InstallPackage payload requires 'fileName'.");
                }

                if (!TryGetRequiredString(doc.RootElement, "installCommand", out var installCommand))
                {
                    errors.Add("InstallPackage payload requires 'installCommand'.");
                }
                else if (_policy.AllowedInstallProfiles.Count > 0 &&
                         !_policy.AllowedInstallProfiles.Contains(installCommand, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"InstallPackage installCommand '{installCommand}' is not an approved install profile.");
                }

                if (_policy.RequireSha256ForInstallPackage &&
                    !TryGetRequiredString(doc.RootElement, "sha256", out _))
                {
                    errors.Add("InstallPackage payload requires 'sha256'.");
                }

                if (doc.RootElement.TryGetProperty("timeoutSeconds", out var timeoutProp) &&
                    timeoutProp.ValueKind == JsonValueKind.Number &&
                    timeoutProp.TryGetInt32(out var timeoutSeconds))
                {
                    if (timeoutSeconds <= 0 || timeoutSeconds > _policy.MaxExecutionTimeoutSeconds)
                    {
                        errors.Add($"InstallPackage timeoutSeconds must be between 1 and {_policy.MaxExecutionTimeoutSeconds}.");
                    }
                }
            }
            catch (JsonException ex)
            {
                errors.Add($"InstallPackage payload is not valid JSON: {ex.Message}");
            }
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

    public sealed class CommandValidationResult
    {
        public bool IsValid { get; init; }
        public string NormalizedType { get; init; } = "";
        public List<string> Errors { get; init; } = new();

        public static CommandValidationResult Success(string normalizedType) =>
            new()
            {
                IsValid = true,
                NormalizedType = normalizedType
            };

        public static CommandValidationResult Fail(List<string> errors) =>
            new()
            {
                IsValid = false,
                Errors = errors
            };
    }
}
