using System;

namespace RetailCentral.Api.Dtos
{
    public class CommandDto
    {
        public Guid CommandId { get; set; }
        public string Type { get; set; } = "";
        public string Scope { get; set; } = "";
        public string? PayloadJson { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? ExpiresUtc { get; set; }
    }
}