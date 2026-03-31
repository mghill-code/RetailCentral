namespace RetailCentral.ShellContracts;

public sealed class ShellCommandResponse
{
    public Guid RequestId { get; set; }
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string StdOut { get; set; } = "";
    public string StdErr { get; set; } = "";
}