using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data;
using RetailCentral.Api.Data.Entities.Orchestration;
using RetailCentral.Api.DTOs.Orchestration;
using RetailCentral.Api.Services.Orchestration;

namespace RetailCentral.Api.Controllers
{
    [ApiController]
    [Route("api/orchestration")]
    public class OrchestrationController : ControllerBase
    {
        private readonly RetailCentralDbContext _db;
        private readonly IOrchestrationEngine _engine;
        private readonly ILogger<OrchestrationController> _logger;
        private readonly OrchestrationPolicyService _orchestrationPolicy;

        public OrchestrationController(
            RetailCentralDbContext db,
            IOrchestrationEngine engine,
            OrchestrationPolicyService orchestrationPolicy,
            ILogger<OrchestrationController> logger)
        {
            _db = db;
            _engine = engine;
            _orchestrationPolicy = orchestrationPolicy;
            _logger = logger;
        }

        // ---------------------------------------------------------------------
        // Create a reusable orchestration template.
        // This defines the ordered steps that future runs will execute.
        // ---------------------------------------------------------------------
        [HttpPost("templates")]
        public async Task<IActionResult> CreateTemplate(
            [FromBody] CreateOrchestrationTemplateRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Template name is required.");

            if (request.Steps == null || request.Steps.Count == 0)
                return BadRequest("At least one step is required.");

            if (request.Version <= 0)
                return BadRequest("Template version must be greater than zero.");

            var duplicateStepOrders = request.Steps
                .GroupBy(x => x.StepOrder)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateStepOrders.Count > 0)
                return BadRequest($"Duplicate step orders found: {string.Join(", ", duplicateStepOrders)}");

            // Validate each orchestration step against the orchestration-specific
            // server-side policy before saving anything to the database.
            var stepValidationErrors = new List<string>();

            foreach (var step in request.Steps.OrderBy(x => x.StepOrder))
            {
                var tempStep = new OrchestrationTemplateStep
                {
                    StepOrder = step.StepOrder,
                    Name = step.Name,
                    StepType = step.StepType,
                    CommandType = step.CommandType,
                    ParametersJson = step.ParametersJson,
                    SuccessCriteriaJson = step.SuccessCriteriaJson,
                    TimeoutSeconds = step.TimeoutSeconds,
                    MaxRetries = step.MaxRetries,
                    OnFailureAction = step.OnFailureAction,
                    ContinueOnFailure = step.ContinueOnFailure
                };

                var errors = _orchestrationPolicy.ValidateTemplateStep(tempStep);

                foreach (var error in errors)
                {
                    stepValidationErrors.Add($"Step {step.StepOrder} ({step.Name}): {error}");
                }
            }

            if (stepValidationErrors.Count > 0)
            {
                return BadRequest(new
                {
                    message = "Template validation failed.",
                    errors = stepValidationErrors
                });
            }

            var template = new OrchestrationTemplate
            {
                Name = request.Name,
                Description = request.Description,
                Version = request.Version,
                DeviceType = request.DeviceType,
                Environment = request.Environment,
                TriggerType = request.TriggerType,
                IsActive = true,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow,
                Steps = request.Steps
                    .OrderBy(x => x.StepOrder)
                    .Select(x => new OrchestrationTemplateStep
                    {
                        StepOrder = x.StepOrder,
                        Name = x.Name,
                        StepType = x.StepType,
                        CommandType = x.CommandType,
                        ParametersJson = x.ParametersJson,
                        SuccessCriteriaJson = x.SuccessCriteriaJson,
                        TimeoutSeconds = x.TimeoutSeconds,
                        MaxRetries = x.MaxRetries,
                        OnFailureAction = x.OnFailureAction,
                        ContinueOnFailure = x.ContinueOnFailure
                    })
                    .ToList()
            };

            _db.OrchestrationTemplates.Add(template);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (
                ex.InnerException?.Message.Contains(
                    "IX_OrchestrationTemplates_Name_Version",
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                return BadRequest($"Template '{request.Name}' version {request.Version} already exists.");
            }

            _logger.LogInformation(
                "Created orchestration template {TemplateId} ({TemplateName})",
                template.Id,
                template.Name);

            return Ok(new
            {
                template.Id,
                template.Name,
                template.Version
            });
        }

        // ---------------------------------------------------------------------
        // Create a run manually from an existing template.
        // This is the easiest way to test orchestration before relying on
        // enrollment-triggered zero-touch provisioning.
        // ---------------------------------------------------------------------
        [HttpPost("runs")]
        public async Task<IActionResult> CreateRun(
            [FromBody] CreateOrchestrationRunRequest request,
            CancellationToken cancellationToken)
        {
            if (request.TemplateId <= 0)
                return BadRequest("A valid templateId is required.");

            var templateExists = await _db.OrchestrationTemplates
                .AnyAsync(x => x.Id == request.TemplateId && x.IsActive, cancellationToken);

            if (!templateExists)
                return BadRequest($"Template {request.TemplateId} was not found or is inactive.");

            if (request.DeviceId == null)
                return BadRequest("deviceId is required for a manual orchestration run.");

            var run = await _engine.CreateRunFromTemplateAsync(
                request.TemplateId,
                request.DeviceId,
                request.AgentId,
                request.StoreId,
                request.RegisterId,
                OrchestrationTriggerSource.User,
                request.RequestedBy,
                request.ParametersJson,
                cancellationToken);

            return Ok(new
            {
                run.Id,
                run.CorrelationId,
                run.Status
            });
        }

        // ---------------------------------------------------------------------
        // List orchestration runs.
        // Optional querystring: ?status=Running / Pending / Completed / Failed
        // ---------------------------------------------------------------------
        [HttpGet("runs")]
        public async Task<IActionResult> GetRuns(
            [FromQuery] string? status,
            CancellationToken cancellationToken)
        {
            var query = _db.OrchestrationRuns.AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) &&
                Enum.TryParse<OrchestrationRunStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(x => x.Status == parsedStatus);
            }

            var runs = await query
                .OrderByDescending(x => x.StartedUtc)
                .Select(x => new
                {
                    x.Id,
                    x.TemplateId,
                    x.DeviceId,
                    x.AgentId,
                    x.StoreId,
                    x.RegisterId,
                    x.Status,
                    x.CurrentStepOrder,
                    x.CorrelationId,
                    x.RequestedBy,
                    x.StartedUtc,
                    x.CompletedUtc
                })
                .ToListAsync(cancellationToken);

            return Ok(runs);
        }

        // ---------------------------------------------------------------------
        // Get a single run with its step-by-step execution detail.
        // Includes template-step metadata so the UI / debugging output is easier
        // to understand than raw step status alone.
        // ---------------------------------------------------------------------
        [HttpGet("runs/{id:long}")]
        public async Task<ActionResult<OrchestrationRunDto>> GetRun(
            long id,
            CancellationToken cancellationToken)
        {
            var run = await _db.OrchestrationRuns
                .Include(x => x.Steps)
                    .ThenInclude(x => x.TemplateStep)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (run == null)
                return NotFound();

            var dto = new OrchestrationRunDto
            {
                Id = run.Id,
                TemplateId = run.TemplateId,
                Status = run.Status,
                CurrentStepOrder = run.CurrentStepOrder,
                CorrelationId = run.CorrelationId,
                StartedUtc = run.StartedUtc,
                CompletedUtc = run.CompletedUtc,
                Steps = run.Steps
                    .OrderBy(x => x.StepOrder)
                    .Select(x => new OrchestrationStepDto
                    {
                        Id = x.Id,
                        StepOrder = x.StepOrder,
                        Name = x.TemplateStep != null ? x.TemplateStep.Name : null,
                        CommandType = x.TemplateStep != null ? x.TemplateStep.CommandType : null,
                        StepType = x.TemplateStep != null ? x.TemplateStep.StepType : null,
                        Status = x.Status,
                        AttemptCount = x.AttemptCount,
                        CommandId = x.CommandId,
                        ErrorMessage = x.ErrorMessage,
                        StartedUtc = x.StartedUtc,
                        CompletedUtc = x.CompletedUtc
                    })
                    .ToList()
            };

            return Ok(dto);
        }

        // ---------------------------------------------------------------------
        // List available orchestration templates.
        // ---------------------------------------------------------------------
        [HttpGet("templates")]
        public async Task<IActionResult> GetTemplates(CancellationToken cancellationToken)
        {
            var templates = await _db.OrchestrationTemplates
                .OrderBy(x => x.Name)
                .ThenBy(x => x.Version)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.Version,
                    x.DeviceType,
                    x.Environment,
                    x.IsActive,
                    x.CreatedUtc
                })
                .ToListAsync(cancellationToken);

            return Ok(templates);
        }
    }
}
