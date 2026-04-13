using RetailCentral.Api.Data.Entities.Orchestration;
using RetailCentral.Api.Models;

namespace RetailCentral.Api.Services.Orchestration
{
    public interface IOrchestrationCommandFactory
    {
        Command BuildCommandForStep(
            OrchestrationRun run,
            OrchestrationRunStep runStep,
            OrchestrationTemplateStep templateStep);
    }
}