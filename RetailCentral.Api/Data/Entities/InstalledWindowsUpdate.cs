public class InstalledWindowsUpdate
{
    public int Id { get; set; }
    public Guid DeviceId { get; set; }

    public string? HotFixId { get; set; }
    public string? Description { get; set; }
    public string? InstalledOn { get; set; }

    public DateTime UpdatedUtc { get; set; }
}