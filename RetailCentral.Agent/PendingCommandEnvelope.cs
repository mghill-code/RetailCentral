public sealed class PendingCommandEnvelope
{
    public Guid CommandId { get; set; }
    public string? Type { get; set; }
    public string? Scope { get; set; }
    public string? PayloadJson { get; set; }
}