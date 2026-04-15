namespace RetailCentral.Agent.Configuration;

public sealed class FileWriteOptions
{
    public bool RequireAbsolutePath { get; set; } = true;
    public bool DisallowUNCPaths { get; set; } = true;
    public List<string> TrustedWriteRoots { get; set; } = new();
    public List<string> BlockedWritePathPrefixes { get; set; } = new();
}