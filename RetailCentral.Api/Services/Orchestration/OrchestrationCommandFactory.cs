using System.Text.Json;
using RetailCentral.Api.Data.Entities.Orchestration;
using RetailCentral.Api.Models;

namespace RetailCentral.Api.Services.Orchestration
{
    public class OrchestrationCommandFactory : IOrchestrationCommandFactory
    {
        public Command BuildCommandForStep(
            OrchestrationRun run,
            OrchestrationRunStep runStep,
            OrchestrationTemplateStep templateStep)
        {
            var payload = new
            {
                orchestrationRunId = run.Id,
                orchestrationRunStepId = runStep.Id,
                templateStepId = templateStep.Id,
                stepType = templateStep.StepType.ToString(),
                parameters = templateStep.ParametersJson
            };

            var commandType = ResolveCommandType(templateStep);

            return new Command
            {
                CommandId = Guid.NewGuid(),
                DeviceId = run.DeviceId,
                Scope = "Device",
                Type = commandType,
                Status = "Pending",
                IssuedBy = OrchestrationConstants.CommandSourceOrchestration,
                StoreNumber = run.StoreId?.ToString(),
                PayloadJson = JsonSerializer.Serialize(payload),
                Priority = 100,
                CreatedUtc = DateTime.UtcNow,
                IssuedUtc = DateTime.UtcNow,
                AttemptCount = 0,
                MaxAttempts = Math.Max(1, templateStep.MaxRetries + 1),
                NextAttemptUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.AddMinutes(30)
            };
        }

        private static string ResolveCommandType(OrchestrationTemplateStep templateStep)
        {
            if (!string.IsNullOrWhiteSpace(templateStep.CommandType))
                return templateStep.CommandType;

            return templateStep.StepType switch
            {
                OrchestrationStepType.CollectInventory => "CollectInventory",
                OrchestrationStepType.WriteFile => "WriteFile",
                OrchestrationStepType.ApplyConfiguration => "ApplyConfiguration",
                OrchestrationStepType.ValidateProcess => "ValidateProcess",
                OrchestrationStepType.RestartProcess => "RestartProcess",
                OrchestrationStepType.RestartPos => "RestartPos",
                OrchestrationStepType.LaunchPos => "LaunchPos",
                OrchestrationStepType.SendNamedPipeCommand => "NamedPipeCommand",
                OrchestrationStepType.RunScript => "RunScript",
                OrchestrationStepType.RebootMachine => "RebootMachine",
                OrchestrationStepType.Wait => "Wait",
                _ => "RunCommand"
            };
        }
    }
}