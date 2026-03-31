namespace RetailCentral.ShellContracts;

public sealed class ShellCommandRequest
{
    public Guid RequestId { get; set; }
    public string Action { get; set; } = "";
    public string? PayloadJson { get; set; }
    public DateTime RequestedUtc { get; set; }
}