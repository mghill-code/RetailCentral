namespace RetailCentral.Api.Dtos
{
    public class CreateDeviceGroupRequest
    {
        public string GroupName { get; set; } = "";
        public string? Description { get; set; }
    }
}