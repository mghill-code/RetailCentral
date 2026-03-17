public sealed class PackageDeploymentCommand
{
    public int DeploymentId { get; set; }
    public int PackageId { get; set; }
    public string PackageName { get; set; } = "";
    public string FileName { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string ExecuteMode { get; set; } = "Immediate";
    public string? WindowStartLocal { get; set; }
    public string? WindowEndLocal { get; set; }
    public bool UseStoreLocalTime { get; set; } = true;
    public string InstallCommand { get; set; } = "";
    public string? InstallArguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public int TimeoutSeconds { get; set; } = 1800;
    public string? RebootBehavior { get; set; }
}