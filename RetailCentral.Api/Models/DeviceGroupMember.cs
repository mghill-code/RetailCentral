namespace RetailCentral.Api.Models
{
    public class DeviceGroupMember
    {
        public int DeviceGroupMemberId { get; set; }
        public int DeviceGroupId { get; set; }
        public Guid DeviceId { get; set; }
        public DateTime AddedUtc { get; set; }

        public DeviceGroup? DeviceGroup { get; set; }
        public Device? Device { get; set; }
    }
}