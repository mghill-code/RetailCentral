using System;

namespace RetailCentral.Api.Data.Entities.Orchestration
{
    public class EnrollmentAction
    {
        public long Id { get; set; }

        public int? DeviceId { get; set; }
        public int? AgentId { get; set; }

        public int? AssignedProfileId { get; set; }
        public ProvisioningProfile? AssignedProfile { get; set; }

        public long? InitialRunId { get; set; }
        public OrchestrationRun? InitialRun { get; set; }

        public EnrollmentActionStatus Status { get; set; }

        public DateTime CreatedUtc { get; set; }
        public DateTime? CompletedUtc { get; set; }
    }
}