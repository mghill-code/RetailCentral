public class InstalledSoftware
{
    public int Id { get; set; }
    public Guid DeviceId { get; set; }

    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Publisher { get; set; }
    public string? InstallDate { get; set; }

    public DateTime UpdatedUtc { get; set; }
}