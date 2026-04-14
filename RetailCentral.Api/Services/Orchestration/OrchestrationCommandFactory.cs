using System.Text.Json;
using System.Text.Json.Nodes;
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
            var commandType = ResolveCommandType(templateStep);

            // Build a payload that preserves orchestration metadata but also allows
            // agent-side command handlers to read expected top-level properties
            // like "processName" without needing orchestration-specific parsing.
            var payload = BuildPayload(templateStep, run, runStep);

            return new Command
            {
                CommandId = Guid.NewGuid(),
                DeviceId = run.DeviceId,
                Scope = "Device",
                Type = commandType,
                Status = "Pending",
                IssuedBy = OrchestrationConstants.CommandSourceOrchestration,
                StoreNumber = run.StoreId?.ToString(),
                PayloadJson = payload.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = false
                }),
                Priority = 100,
                CreatedUtc = DateTime.UtcNow,
                IssuedUtc = DateTime.UtcNow,
                AttemptCount = 0,
                MaxAttempts = Math.Max(1, templateStep.MaxRetries + 1),
                NextAttemptUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.AddMinutes(30)
            };
        }

        private static JsonObject BuildPayload(
            OrchestrationTemplateStep templateStep,
            OrchestrationRun run,
            OrchestrationRunStep runStep)
        {
            JsonObject payloadRoot;

            // If ParametersJson is a JSON object, flatten it into the root payload.
            // That keeps agent command handlers simple.
            if (!string.IsNullOrWhiteSpace(templateStep.ParametersJson))
            {
                try
                {
                    var parsed = JsonNode.Parse(templateStep.ParametersJson);

                    if (parsed is JsonObject obj)
                    {
                        payloadRoot = obj;
                    }
                    else
                    {
                        payloadRoot = new JsonObject
                        {
                            ["rawParameters"] = templateStep.ParametersJson
                        };
                    }
                }
                catch
                {
                    payloadRoot = new JsonObject
                    {
                        ["rawParameters"] = templateStep.ParametersJson
                    };
                }
            }
            else
            {
                payloadRoot = new JsonObject();
            }

            payloadRoot["_orchestration"] = new JsonObject
            {
                ["orchestrationRunId"] = run.Id,
                ["orchestrationRunStepId"] = runStep.Id,
                ["templateStepId"] = templateStep.Id,
                ["stepType"] = templateStep.StepType.ToString()
            };

            return payloadRoot;
        }

        private static string ResolveCommandType(OrchestrationTemplateStep templateStep)
        {
            if (!string.IsNullOrWhiteSpace(templateStep.CommandType))
                return templateStep.CommandType;

            return templateStep.StepType switch
            {
                OrchestrationStepType.CollectInventory => "CollectSystemInfo",
                OrchestrationStepType.RestartPos => "RestartPOS",
                OrchestrationStepType.RebootMachine => "RebootDevice",
                OrchestrationStepType.ValidateProcess => "ValidateProcess",

                // These should only be used after confirming the agent supports them
                OrchestrationStepType.RunScript => "RunCommand",
                OrchestrationStepType.RestartProcess => "RestartProcess",
                OrchestrationStepType.RestartService => "RestartService",
                OrchestrationStepType.WriteFile => "WriteFile",
                OrchestrationStepType.ApplyConfiguration => "ApplyConfiguration",
                OrchestrationStepType.LaunchPos => "LaunchPOS",
                OrchestrationStepType.SendNamedPipeCommand => "NamedPipeCommand",
                OrchestrationStepType.Wait => "Wait",

                _ => "RunCommand"
            };
        }
    }
}