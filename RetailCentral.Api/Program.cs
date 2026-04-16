using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using RetailCentral.Api.BackgroundServices;
using RetailCentral.Api.Configuration;
using RetailCentral.Api.Data;
using RetailCentral.Api.Models;
using RetailCentral.Api.Security;
using RetailCentral.Api.Services;
using RetailCentral.Api.Services.Deployments;
using RetailCentral.Api.Services.Orchestration;
using Serilog;
using System.Security.Claims;
using System.Threading.RateLimiting;
using static RetailCentral.Api.Services.CommandTimeoutWorker;


/*
===============================================================================
RetailCommand API - Program.cs
-------------------------------------------------------------------------------
This is the main entry point and composition root for the application.

Responsibilities:
- Configure logging (Serilog)
- Configure dependency injection
- Configure middleware pipeline
- Configure authentication and authorization
- Configure background workers
- Configure EF Core
- Bind configuration objects
- Register validation and policy services
- Register orchestration services and zero-touch provisioning support
===============================================================================
*/

// -----------------------------------------------------------------------------
// STEP 1: Preload configuration so logging can be configured before host build
// -----------------------------------------------------------------------------
var preConfig = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile(
        $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json",
        optional: true,
        reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

var logDir = preConfig["Logging:LogDirectory"];
if (string.IsNullOrWhiteSpace(logDir))
{
    logDir = Path.Combine(AppContext.BaseDirectory, "logs");
}

Directory.CreateDirectory(logDir);

Console.WriteLine($"RetailCommand API log directory: {logDir}");

// -----------------------------------------------------------------------------
// STEP 2: Configure Serilog logging
// -----------------------------------------------------------------------------
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logDir, "api-.log"),
        rollingInterval: RollingInterval.Day,
        fileSizeLimitBytes: 50_000_000,
        rollOnFileSizeLimit: true,
        retainedFileCountLimit: 7,
        shared: true)
    .CreateLogger();

// -----------------------------------------------------------------------------
// STEP 3: Build application host
// -----------------------------------------------------------------------------
try
{
    Log.Information("RetailCommand API starting...");
    Log.Information("Log directory: {LogDir}", logDir);
    Log.Information("Base directory: {BaseDir}", AppContext.BaseDirectory);

    var builder = WebApplication.CreateBuilder(args);

    builder.WebHost.UseKestrel();
    builder.Host.UseSerilog();

    // -------------------------------------------------------------------------
    // CORE SERVICES (Business logic / domain services)
    // -------------------------------------------------------------------------
    builder.Services.AddScoped<IDeploymentService, DeploymentService>();
    builder.Services.AddScoped<AuditService>();
    builder.Services.AddScoped<CommandValidationService>();

    // -------------------------------------------------------------------------
    // ORCHESTRATION SERVICES
    // -------------------------------------------------------------------------
    // These services turn the platform from simple command dispatch into a
    // workflow/orchestration engine capable of:
    // - multi-step task sequencing
    // - run/step state tracking
    // - zero-touch provisioning after enrollment
    // - reusable templates and provisioning profiles
    builder.Services.AddScoped<IOrchestrationEngine, OrchestrationEngine>();
    builder.Services.AddScoped<IOrchestrationCommandFactory, OrchestrationCommandFactory>();
    builder.Services.AddScoped<IProvisioningProfileResolver, ProvisioningProfileResolver>();
    builder.Services.AddScoped<IEnrollmentOrchestrationService, EnrollmentOrchestrationService>();
    builder.Services.AddScoped<OrchestrationPolicyService>();
    // -------------------------------------------------------------------------
    // MVC + RAZOR (Dashboard UI + API controllers)
    // -------------------------------------------------------------------------
    builder.Services.AddControllersWithViews();
    builder.Services.AddEndpointsApiExplorer();

    // -------------------------------------------------------------------------
    // SWAGGER (API documentation)
    // -------------------------------------------------------------------------
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "RetailCommand API - Device Control Center",
            Version = "v1",
            Description = "Centralized device control, monitoring, command execution, deployment management, and orchestration."
        });
    });

    // -------------------------------------------------------------------------
    // DATABASE (EF Core - SQL Server)
    // -------------------------------------------------------------------------
    builder.Services.AddDbContext<RetailCentralDbContext>(options =>
    {
        options.UseSqlServer(builder.Configuration.GetConnectionString("RetailCentral"));
    });

    // -------------------------------------------------------------------------
    // RATE LIMITING (Agent protection - prevents abuse / flooding)
    // -------------------------------------------------------------------------
    builder.Services.AddRateLimiter(options =>
    {
        options.AddPolicy("agent", context =>
        {
            var key = context.Request.Headers["X-Device-Id"].FirstOrDefault()
                      ?? context.Connection.RemoteIpAddress?.ToString()
                      ?? "unknown";

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: key,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 60,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        });

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    });

    // -------------------------------------------------------------------------
    // CONFIGURATION BINDING (Strongly-typed settings)
    // -------------------------------------------------------------------------

    // Controls command timeout + stale recovery behavior
    builder.Services.Configure<CommandTimeoutOptions>(
        builder.Configuration.GetSection("CommandTimeout"));

    // Controls retry delays (exponential backoff, etc.)
    builder.Services.Configure<RetryBackoffOptions>(
        builder.Configuration.GetSection("RetryBackoff"));

    // Controls how often agents poll the server
    builder.Services.Configure<AgentPollingHintsOptions>(
        builder.Configuration.GetSection("AgentPollingHints"));

    // Controls which commands are allowed and how they are validated
    builder.Services.Configure<CommandPolicyOptions>(
        builder.Configuration.GetSection(CommandPolicyOptions.SectionName));

    // -------------------------------------------------------------------------
    // RETRY BACKOFF SERVICE (used by timeout worker + retry logic)
    // -------------------------------------------------------------------------
    builder.Services.AddSingleton<RetryBackoffService>();

    // -------------------------------------------------------------------------
    // BACKGROUND WORKERS (critical system automation)
    // -------------------------------------------------------------------------
    builder.Services.AddHostedService<DataRetentionService>();              // DB cleanup (logs, heartbeats)
    builder.Services.AddHostedService<SoftwareInventoryScheduleWorker>();   // Software inventory refresh
    builder.Services.AddHostedService<ProcessStatusScheduleWorker>();       // Process monitoring
    builder.Services.AddHostedService<CommandTimeoutWorker>();              // Retry + timeout recovery
    builder.Services.AddHostedService<RegisterInventoryRefreshWorker>();    // Hardware inventory refresh

    // Orchestration worker:
    // - scans active orchestration runs
    // - dispatches next eligible steps into the existing command pipeline
    // - correlates command results back into orchestration run/step state
    builder.Services.AddHostedService<OrchestrationWorker>();

    // -------------------------------------------------------------------------
    // DATA PROTECTION (used for securing secrets like DeviceSecret)
    // -------------------------------------------------------------------------
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "dp_keys")));

    builder.Services.AddSingleton<DeviceSecretProtection>();

    // -------------------------------------------------------------------------
    // AUTHENTICATION (Windows / Active Directory)
    // -------------------------------------------------------------------------
    builder.Services
        .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate();

    builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, CustomAuthorizationMiddlewareResultHandler>();

    // -------------------------------------------------------------------------
    // AUTHORIZATION (RBAC via AD groups)
    // -------------------------------------------------------------------------
    var authSection = builder.Configuration.GetSection("Security:Authorization");

    var viewer = authSection.GetSection("DashboardViewerGroups").Get<string[]>() ?? Array.Empty<string>();
    var helpdesk = authSection.GetSection("DashboardHelpdeskGroups").Get<string[]>() ?? Array.Empty<string>();
    var engineer = authSection.GetSection("DashboardEngineerGroups").Get<string[]>() ?? Array.Empty<string>();
    var admin = authSection.GetSection("DashboardAdminGroups").Get<string[]>() ?? Array.Empty<string>();

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("DashboardViewer", p =>
        {
            p.RequireAuthenticatedUser();
            p.RequireAssertion(ctx => IsUserInAnyGroup(ctx.User, viewer));
        });

        options.AddPolicy("DashboardHelpdesk", p =>
        {
            p.RequireAuthenticatedUser();
            p.RequireAssertion(ctx => IsUserInAnyGroup(ctx.User, helpdesk));
        });

        options.AddPolicy("DashboardEngineer", p =>
        {
            p.RequireAuthenticatedUser();
            p.RequireAssertion(ctx => IsUserInAnyGroup(ctx.User, engineer));
        });

        options.AddPolicy("DashboardAdmin", p =>
        {
            p.RequireAuthenticatedUser();
            p.RequireAssertion(ctx => IsUserInAnyGroup(ctx.User, admin));
        });
    });

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddHealthChecks();

    // -------------------------------------------------------------------------
    // BUILD APPLICATION
    // -------------------------------------------------------------------------
    var app = builder.Build();

    // -------------------------------------------------------------------------
    // MIDDLEWARE PIPELINE
    // -------------------------------------------------------------------------
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    // Serves static files (CSS, JS, images for dashboard)
    //app.UseStaticFiles();
    

    var contentTypeProvider = new FileExtensionContentTypeProvider();
    contentTypeProvider.Mappings[".reg"] = "text/plain";

    app.UseStaticFiles(new StaticFileOptions
    {
        ContentTypeProvider = contentTypeProvider
    });

    // HMAC auth for agent endpoints ONLY
    app.UseMiddleware<HmacAuthMiddleware>();

    // Apply rate limiting
    app.UseRateLimiter();

    // Force HTTPS in production
    if (!app.Environment.IsDevelopment())
    {
        app.UseHttpsRedirection();
    }

    // Authentication + Authorization
    app.UseAuthentication();
    app.UseAuthorization();

    // -------------------------------------------------------------------------
    // ROUTING
    // -------------------------------------------------------------------------
    app.MapControllers();

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Dashboard}/{action=Index}/{id?}");

    app.MapHealthChecks("/health");

    // -------------------------------------------------------------------------
    // START APPLICATION
    // -------------------------------------------------------------------------
    app.Run();
}
catch (HostAbortedException)
{
    // Expected during EF migrations / design-time
}
catch (Exception ex)
{
    Log.Fatal(ex, "RetailCommand API terminated unexpectedly.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// -----------------------------------------------------------------------------
// HELPER METHODS
// -----------------------------------------------------------------------------
static bool IsUserInAnyGroup(ClaimsPrincipal user, IEnumerable<string> groups)
{
    foreach (var group in groups)
    {
        if (user.IsInRole(group))
            return true;
    }

    return false;
}