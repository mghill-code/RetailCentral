using System;

namespace RetailCentral.Api.Dtos
{
    public class EnrollResponse
    {
        public Guid DeviceId { get; set; }

        // Day 2: add DeviceSecret return
        public int HeartbeatSeconds { get; set; } = 300;
        public int PollSeconds { get; set; } = 30;

        public DateTime ServerUtc { get; set; }
        public string? DeviceSecret { get; set; } // only returned on first enroll (or rotation)
    }
}