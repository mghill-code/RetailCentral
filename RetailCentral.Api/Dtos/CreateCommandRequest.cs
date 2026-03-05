using System;

namespace RetailCentral.Api.Dtos
{
    public class CreateCommandRequest
    {
        public TargetDto Target { get; set; } = new();
        public string Type { get; set; } = "";
        public string? PayloadJson { get; set; }
        public DateTime? ExpiresUtc { get; set; }
        public int Priority { get; set; } = 100;

        public class TargetDto
        {
            public Guid? DeviceId { get; set; }       // set for per-device
            public string? StoreNumber { get; set; }  // set for store-wide
            public string Scope { get; set; } = "Device"; // Device | StoreAllDevices | StoreOnce
        }
    }
}