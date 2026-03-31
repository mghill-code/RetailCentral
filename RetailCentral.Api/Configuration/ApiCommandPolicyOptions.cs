using System.ComponentModel.DataAnnotations;

namespace RetailCentral.Api.Configuration
{
    public sealed class ApiCommandPolicyOptions
    {
        public const string SectionName = "CommandPolicy";

        public bool AllowRunProcess { get; set; } = false;
        public bool RequirePayloadJsonForDownloadFile { get; set; } = true;
        public bool RequireSha256ForDownloadFileExecution { get; set; } = true;
        public bool RequireSha256ForInstallPackage { get; set; } = true;

        public List<string> AllowedCommandTypes { get; set; } = new();
        public List<string> HelpdeskAllowedCommandTypes { get; set; } = new();

        public List<string> AllowedInstallProfiles { get; set; } = new();
        public List<string> AllowedDownloadHosts { get; set; } = new();
    }
}