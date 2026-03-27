using System;
using System.Collections.Generic;

namespace RetailCentral.Api.ViewModels
{
    public class CommandCenterViewModel
    {
        public List<CommandCenterRowViewModel> Commands { get; set; } = new();
        public string? Status { get; set; }
        public string? Search { get; set; }
        public bool ShowExpiredPendingOnly { get; set; }
        public int TotalCount { get; set; }
        public int PendingCount { get; set; }
        public int InProgressCount { get; set; }
        public int FailedCount { get; set; }
        public int SucceededCount { get; set; }
        public int ExpiredPendingCount { get; set; }
    }

    public class CommandCenterRowViewModel
    {
        public Guid CommandId { get; set; }
        public Guid? DeviceId { get; set; }
        public string? StoreNumber { get; set; }
        public string? GroupName { get; set; }
        public string Scope { get; set; } = "";
        public string Type { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public DateTime? ExpiresUtc { get; set; }
        public int AttemptCount { get; set; }
        public int MaxAttempts { get; set; }
        public string? LastError { get; set; }
        public string? IssuedBy { get; set; }

        public bool IsExpiredPending =>
            Status == "Pending" &&
            ExpiresUtc.HasValue &&
            ExpiresUtc.Value <= DateTime.UtcNow;

        public string PendingReason
        {
            get
            {
                if (Status != "Pending")
                    return string.Empty;

                if (IsExpiredPending)
                    return "Expired before pickup by agent.";

                if (DeviceId != null)
                    return "Waiting for target device to poll and claim the command.";

                if (!string.IsNullOrWhiteSpace(StoreNumber))
                    return "Waiting for matching device in the target store.";

                if (!string.IsNullOrWhiteSpace(GroupName))
                    return "Waiting for matching device in the target group.";

                return "Waiting to be picked up by an agent.";
            }
        }
    }
}