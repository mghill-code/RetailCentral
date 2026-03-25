namespace RetailCentral.Api.ViewModels
{
    public class AboutPageConfig
    {
        public string ProductName { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Acronym { get; set; } = "";
        public string AcronymExpansion { get; set; } = "";
        public string Mission { get; set; } = "";
        public string PlatformSummary { get; set; } = "";

        public List<string> Capabilities { get; set; } = new();

        public AboutEnvironmentConfig Environment { get; set; } = new();
        public AboutSupportConfig Support { get; set; } = new();
    }

    public class AboutEnvironmentConfig
    {
        public string DisplayName { get; set; } = "";
        public bool ShowRuntimeDetails { get; set; }
    }

    public class AboutSupportConfig
    {
        public string PrimaryOwner { get; set; } = "";
        public string SupportTeam { get; set; } = "";
        public string Escalation { get; set; } = "";
    }
}