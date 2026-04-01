public sealed class PendingCommandsPollResult
{
    public List<PendingCommandEnvelope> Commands { get; set; } = new();
    public int? PollAfterSeconds { get; set; }
}