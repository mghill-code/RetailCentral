using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data;
using RetailCentral.Api.Data.Entities.Orchestration;

namespace RetailCentral.Api.Services.Orchestration
{
    public class OrchestrationEngine : IOrchestrationEngine
    {
        private readonly RetailCentralDbContext _db;
        private readonly IOrchestrationCommandFactory _commandFactory;
        private readonly ILogger<OrchestrationEngine> _logger;

        public OrchestrationEngine(
            RetailCentralDbContext db,
            IOrchestrationCommandFactory commandFactory,
            ILogger<OrchestrationEngine> logger)
        {
            _db = db;
            _commandFactory = commandFactory;
            _logger = logger;
        }

        public async Task<OrchestrationRun> CreateRunFromTemplateAsync(
            int templateId,
            Guid? deviceId,
            int? agentId,
            int? storeId,
            int? registerId,
            OrchestrationTriggerSource triggerSource,
            string? requestedBy,
            string? parametersJson,
            CancellationToken cancellationToken)
        {
            var template = await _db.OrchestrationTemplates
                .Include(x => x.Steps)
                .FirstOrDefaultAsync(x => x.Id == templateId && x.IsActive, cancellationToken);

            if (template == null)
                throw new InvalidOperationException($"Orchestration template {templateId} was not found or is inactive.");

            var run = new OrchestrationRun
            {
                TemplateId = template.Id,
                DeviceId = deviceId,
                AgentId = agentId,
                StoreId = storeId,
                RegisterId = registerId,
                Status = OrchestrationRunStatus.Pending,
                TriggerSource = triggerSource,
                RequestedBy = requestedBy,
                ParametersJson = parametersJson,
                StartedUtc = DateTime.UtcNow
            };

            _db.OrchestrationRuns.Add(run);
            await _db.SaveChangesAsync(cancellationToken);

            var runSteps = template.Steps
                .OrderBy(x => x.StepOrder)
                .Select(x => new OrchestrationRunStep
                {
                    RunId = run.Id,
                    TemplateStepId = x.Id,
                    StepOrder = x.StepOrder,
                    Status = OrchestrationRunStepStatus.Pending,
                    AttemptCount = 0
                })
                .ToList();

            _db.OrchestrationRunSteps.AddRange(runSteps);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created orchestration run {RunId} from template {TemplateId}", run.Id, template.Id);

            return run;
        }

        public async Task<int> AdvancePendingRunsAsync(CancellationToken cancellationToken)
        {
            var runs = await _db.OrchestrationRuns
                .Include(x => x.Template)
                .Include(x => x.Steps)
                .Where(x =>
                    x.Status == OrchestrationRunStatus.Pending ||
                    x.Status == OrchestrationRunStatus.Running ||
                    x.Status == OrchestrationRunStatus.WaitingForRetry)
                .OrderBy(x => x.Id)
                .ToListAsync(cancellationToken);

            var advancedCount = 0;

            foreach (var run in runs)
            {
                var templateSteps = await _db.OrchestrationTemplateSteps
                    .Where(x => x.TemplateId == run.TemplateId)
                    .OrderBy(x => x.StepOrder)
                    .ToListAsync(cancellationToken);

                var hasBlockingFailure = run.Steps.Any(x =>
                    x.Status == OrchestrationRunStepStatus.Failed ||
                    x.Status == OrchestrationRunStepStatus.TimedOut);

                if (hasBlockingFailure)
                {
                    run.Status = OrchestrationRunStatus.Failed;
                    if (!run.CompletedUtc.HasValue)
                    {
                        run.CompletedUtc = DateTime.UtcNow;
                    }

                    continue;
                }

                var activeRunStep = run.Steps
                    .Where(x =>
                        x.Status == OrchestrationRunStepStatus.Dispatched ||
                        x.Status == OrchestrationRunStepStatus.Running)
                    .OrderBy(x => x.StepOrder)
                    .FirstOrDefault();

                if (activeRunStep != null)
                {
                    continue;
                }

                var nextRunStep = run.Steps
                                .Where(x => x.Status == OrchestrationRunStepStatus.Pending || x.Status == OrchestrationRunStepStatus.Ready)
                    .OrderBy(x => x.StepOrder)
                    .FirstOrDefault();

                if (nextRunStep == null)
                {
                    var hasFailed = run.Steps.Any(x =>
                        x.Status == OrchestrationRunStepStatus.Failed ||
                        x.Status == OrchestrationRunStepStatus.TimedOut);

                    if (hasFailed)
                    {
                        run.Status = OrchestrationRunStatus.Failed;
                    }
                    else if (run.Steps.All(x =>
                        x.Status == OrchestrationRunStepStatus.Succeeded ||
                        x.Status == OrchestrationRunStepStatus.Skipped))
                    {
                        run.Status = OrchestrationRunStatus.Completed;
                        run.CompletedUtc = DateTime.UtcNow;
                    }

                    continue;
                }

                var templateStep = templateSteps.FirstOrDefault(x => x.Id == nextRunStep.TemplateStepId);
                if (templateStep == null)
                {
                    nextRunStep.Status = OrchestrationRunStepStatus.Failed;
                    nextRunStep.ErrorMessage = "Template step definition could not be found.";
                    nextRunStep.CompletedUtc = DateTime.UtcNow;
                    run.Status = OrchestrationRunStatus.Failed;
                    run.CompletedUtc = DateTime.UtcNow;
                    continue;
                }

                var alreadyDispatched = nextRunStep.CommandId.HasValue &&
                                        await _db.Commands.AnyAsync(x => x.CommandId == nextRunStep.CommandId.Value, cancellationToken);

                if (alreadyDispatched)
                    continue;

                var command = _commandFactory.BuildCommandForStep(run, nextRunStep, templateStep);

                _db.Commands.Add(command);
                await _db.SaveChangesAsync(cancellationToken);

                nextRunStep.CommandId = command.CommandId;
                nextRunStep.Status = OrchestrationRunStepStatus.Dispatched;
                nextRunStep.AttemptCount += 1;
                nextRunStep.StartedUtc = DateTime.UtcNow;

                run.Status = OrchestrationRunStatus.Running;
                run.CurrentStepOrder = nextRunStep.StepOrder;

                advancedCount++;

                _logger.LogInformation(
                    "Dispatched orchestration run {RunId} step {StepOrder} as command {CommandId}",
                    run.Id,
                    nextRunStep.StepOrder,
                    command.CommandId);
            }

            await _db.SaveChangesAsync(cancellationToken);
            return advancedCount;
        }

        public async Task ProcessCommandResultAsync(Guid commandId, CancellationToken cancellationToken)
        {
            var runStep = await _db.OrchestrationRunSteps
                .Include(x => x.Run)
                .Include(x => x.TemplateStep)
                .FirstOrDefaultAsync(x => x.CommandId == commandId, cancellationToken);

            if (runStep == null)
                return;

            var command = await _db.Commands.FirstOrDefaultAsync(x => x.CommandId == commandId, cancellationToken);
            if (command == null)
                return;

            var result = await _db.CommandResults.FirstOrDefaultAsync(x => x.CommandId == commandId, cancellationToken);

            if (string.Equals(command.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(command.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                runStep.Status = OrchestrationRunStepStatus.Succeeded;
                runStep.CompletedUtc = DateTime.UtcNow;
                runStep.ResultJson = result?.StdOut;
            }
            else if (string.Equals(command.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                var failureAction = runStep.TemplateStep.OnFailureAction;
                var continueOnFailure = runStep.TemplateStep.ContinueOnFailure;

                if (continueOnFailure || failureAction == OrchestrationFailureAction.Continue)
                {
                    runStep.Status = OrchestrationRunStepStatus.Skipped;
                    runStep.ErrorMessage = command.LastError ?? result?.StdErr ?? "Command failed, but step was allowed to continue.";
                    runStep.CompletedUtc = DateTime.UtcNow;
                }
                else if (failureAction == OrchestrationFailureAction.RetryStep)
                {
                    if (runStep.AttemptCount <= runStep.TemplateStep.MaxRetries)
                    {
                        runStep.Status = OrchestrationRunStepStatus.Ready;
                        runStep.CommandId = null;
                        runStep.ErrorMessage = command.LastError ?? result?.StdErr ?? "Command failed. Step reset for retry.";
                        runStep.CompletedUtc = null;
                        runStep.StartedUtc = null;
                    }
                    else
                    {
                        runStep.Status = OrchestrationRunStepStatus.Failed;
                        runStep.ErrorMessage = command.LastError ?? result?.StdErr ?? "Command failed and max retries were reached.";
                        runStep.CompletedUtc = DateTime.UtcNow;
                    }
                }
                else
                {
                    // FailRun and any not-yet-special-cased actions end the step as Failed.
                    runStep.Status = OrchestrationRunStepStatus.Failed;
                    runStep.ErrorMessage = command.LastError ?? result?.StdErr ?? "Command failed.";
                    runStep.CompletedUtc = DateTime.UtcNow;
                }
            }

            var run = runStep.Run;
            if (run.Status == OrchestrationRunStatus.Failed ||
                run.Status == OrchestrationRunStatus.Completed ||
                run.Status == OrchestrationRunStatus.Cancelled ||
                run.Status == OrchestrationRunStatus.RolledBack)
            {
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }

            var hasRemaining = await _db.OrchestrationRunSteps
                .AnyAsync(x =>
                    x.RunId == run.Id &&
                    (x.Status == OrchestrationRunStepStatus.Pending || x.Status == OrchestrationRunStepStatus.Ready),
                    cancellationToken);

            var hasFailures = await _db.OrchestrationRunSteps
                .AnyAsync(x =>
                    x.RunId == run.Id &&
                    (x.Status == OrchestrationRunStepStatus.Failed || x.Status == OrchestrationRunStepStatus.TimedOut),
                    cancellationToken);

            if (hasFailures)
            {
                run.Status = OrchestrationRunStatus.Failed;
                run.CompletedUtc = DateTime.UtcNow;

                await MarkRemainingStepsSkippedAsync(
                    run.Id,
                    runStep.StepOrder,
                    "Not executed because an earlier step failed and the run was stopped.",
                    cancellationToken);
            }
            else if (hasRemaining)
            {
                run.Status = OrchestrationRunStatus.Pending;
            }
            else
            {
                run.Status = OrchestrationRunStatus.Completed;
                run.CompletedUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        private async Task MarkRemainingStepsSkippedAsync(
            long runId,
            int failedStepOrder,
            string reason,
            CancellationToken cancellationToken)
        {
            var remainingSteps = await _db.OrchestrationRunSteps
                .Where(x =>
                    x.RunId == runId &&
                    x.StepOrder > failedStepOrder &&
                    (x.Status == OrchestrationRunStepStatus.Pending ||
                     x.Status == OrchestrationRunStepStatus.Ready))
                .ToListAsync(cancellationToken);

            foreach (var step in remainingSteps)
            {
                step.Status = OrchestrationRunStepStatus.Skipped;
                step.ErrorMessage = reason;
                step.CompletedUtc = DateTime.UtcNow;
            }
        }

    }
}