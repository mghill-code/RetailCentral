namespace RetailCentral.Api.Models
{
    public class DeviceGroup
    {
        public int DeviceGroupId { get; set; }
        public string GroupName { get; set; } = "";
        public string? Description { get; set; }
        public DateTime CreatedUtc { get; set; }
    }
}