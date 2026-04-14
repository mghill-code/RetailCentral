using System.ComponentModel.DataAnnotations;

namespace RetailCentral.Api.Configuration
{
    /// <summary>
    /// Central server-side command policy.
    ///
    /// This policy determines:
    /// - what command types the API is allowed to create
    /// - which command types helpdesk users may issue directly
    /// - which command types orchestration may issue
    /// - what additional payload requirements apply to sensitive commands
    /// </summary>
    public sealed class CommandPolicyOptions
    {
        public const string SectionName = "CommandPolicy";

        /// <summary>
        /// Enables generic RunProcess command creation.
        /// This should normally remain false in production.
        /// </summary>
        public bool AllowRunProcess { get; set; } = false;

        /// <summary>
        /// Require payload JSON for DownloadFile requests.
        /// </summary>
        public bool RequirePayloadJsonForDownloadFile { get; set; } = true;

        /// <summary>
        /// Require SHA256 whenever DownloadFile is marked execute=true.
        /// </summary>
        public bool RequireSha256ForDownloadFileExecution { get; set; } = true;

        /// <summary>
        /// Require SHA256 on InstallPackage requests.
        /// </summary>
        public bool RequireSha256ForInstallPackage { get; set; } = true;

        /// <summary>
        /// Require HTTPS for downloadable artifacts.
        /// </summary>
        public bool RequireHttpsDownloads { get; set; } = true;

        /// <summary>
        /// Maximum allowed timeout the API will accept for generated package installs.
        /// </summary>
        [Range(1, 7200)]
        public int MaxExecutionTimeoutSeconds { get; set; } = 600;

        /// <summary>
        /// Maximum expected artifact size in MB.
        /// </summary>
        [Range(1, 102400)]
        public int MaxDownloadSizeMb { get; set; } = 500;

        /// <summary>
        /// Commands generally allowed through the API.
        /// This is the broad server-side allowlist.
        /// </summary>
        public List<string> AllowedCommandTypes { get; set; } = new();

        /// <summary>
        /// Reduced command set for helpdesk/manual workflows.
        /// </summary>
        public List<string> HelpdeskAllowedCommandTypes { get; set; } = new();

        /// <summary>
        /// Command set allowed for orchestration / zero-touch provisioning.
        /// This should typically be broader than helpdesk, but still controlled.
        /// </summary>
        public List<string> OrchestrationAllowedCommandTypes { get; set; } = new();

        /// <summary>
        /// Approved install profiles that the API may reference for InstallPackage.
        /// These should match the agent-side execution profiles.
        /// </summary>
        public List<string> AllowedInstallProfiles { get; set; } = new();

        /// <summary>
        /// Approved artifact hosts for DownloadFile and InstallPackage.
        /// </summary>
        public List<string> AllowedDownloadHosts { get; set; } = new();
    }
}
