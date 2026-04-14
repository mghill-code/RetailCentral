namespace RetailCentral.Api.DTOs.Orchestration
{
    public class CreateOrchestrationRunRequest
    {
        public int TemplateId { get; set; }
        public Guid? DeviceId { get; set; }
        public int? AgentId { get; set; }
        public int? StoreId { get; set; }
        public int? RegisterId { get; set; }
        public string? RequestedBy { get; set; }
        public string? ParametersJson { get; set; }
    }
}