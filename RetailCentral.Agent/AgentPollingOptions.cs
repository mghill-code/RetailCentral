public sealed class AgentPollingOptions
{
    public int BasePollSeconds { get; set; } = 15;
    public int MinPollSeconds { get; set; } = 5;
    public int MaxIdlePollSeconds { get; set; } = 60;
    public int InitialErrorBackoffSeconds { get; set; } = 30;
    public int MaxErrorBackoffSeconds { get; set; } = 300;
    public int FastPollWindowSeconds { get; set; } = 45;
    public int JitterPercent { get; set; } = 20;
}