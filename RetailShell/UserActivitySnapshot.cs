namespace RetailShell
{
    public sealed class UserActivitySnapshot
    {
        public DateTime CapturedUtc { get; set; }
        public DateTime? LastInputUtc { get; set; }
        public int? IdleSeconds { get; set; }
        public string SessionState { get; set; } = "Unknown";
        public string? ConsoleUserName { get; set; }
        public bool IsUserActive { get; set; }
        public bool IsPosForeground { get; set; }
    }
}
