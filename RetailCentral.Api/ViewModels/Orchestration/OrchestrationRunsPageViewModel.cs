using System.Collections.Generic;

namespace RetailCentral.Api.ViewModels.Orchestration
{
    public class OrchestrationRunsPageViewModel
    {
        public string? StatusFilter { get; set; }
        public string? Search { get; set; }

        public int TotalRuns { get; set; }
        public int PendingRuns { get; set; }
        public int RunningRuns { get; set; }
        public int CompletedRuns { get; set; }
        public int FailedRuns { get; set; }

        public List<OrchestrationRunListItemViewModel> Runs { get; set; } = new();
    }
}