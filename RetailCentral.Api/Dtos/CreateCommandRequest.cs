using System.ComponentModel.DataAnnotations;

namespace RetailCentral.Api.Dtos
{
    /// <summary>
    /// Unified command creation request used by the Admin API
    /// and reusable by dashboard flows if you later route them
    /// through the same validation path.
    /// </summary>
    public class CreateCommandRequest
    {
        public Guid? DeviceId { get; set; }
        public string? StoreNumber { get; set; }
        public string? GroupName { get; set; }

        [Required]
        public string Type { get; set; } = "";

        public string? PayloadJson { get; set; }

        [Range(1, 1000)]
        public int Priority { get; set; } = 100;

        [Range(1, 20)]
        public int MaxAttempts { get; set; } = 3;

        public DateTime? ExpiresUtc { get; set; }

        /// <summary>
        /// Helper used by controllers and validation logic to ensure
        /// the request targets exactly one of device, store, or group.
        /// </summary>
        public bool HasExactlyOneTarget()
        {
            var count = 0;

            if (DeviceId != null) count++;
            if (!string.IsNullOrWhiteSpace(StoreNumber)) count++;
            if (!string.IsNullOrWhiteSpace(GroupName)) count++;

            return count == 1;
        }
    }
}