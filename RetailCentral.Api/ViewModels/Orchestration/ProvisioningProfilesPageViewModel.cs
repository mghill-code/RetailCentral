using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;

namespace RetailCentral.Api.ViewModels.Orchestration
{
    public class ProvisioningProfilesPageViewModel
    {
        public string? Search { get; set; }
        public string? DeviceTypeFilter { get; set; }
        public string? EnvironmentFilter { get; set; }
        public string? StoreGroupFilter { get; set; }
        public bool? ActiveFilter { get; set; }

        public int TotalProfiles { get; set; }
        public int ActiveProfiles { get; set; }
        public int DefaultProfiles { get; set; }
        public string? FilterMode { get; set; }

        public List<SelectListItem> DeviceTypeOptions { get; set; } = new();
        public List<SelectListItem> EnvironmentOptions { get; set; } = new();
        public List<SelectListItem> StoreGroupOptions { get; set; } = new();

        public List<ProvisioningProfileListItemViewModel> Profiles { get; set; } = new();
    }
}