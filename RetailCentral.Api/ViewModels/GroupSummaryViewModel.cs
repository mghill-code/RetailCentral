namespace RetailCentral.Api.ViewModels
{
    public class GroupSummaryViewModel
    {
        public int DeviceGroupId { get; set; }
        public string GroupName { get; set; } = "";
        public string? Description { get; set; }
        public DateTime CreatedUtc { get; set; }
        public int MemberCount { get; set; }
    }
}