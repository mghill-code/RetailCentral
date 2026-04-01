using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using RetailCentral.Api.Configuration;
using RetailCentral.Api.Data;
using RetailCentral.Api.Models;
using RetailCentral.Api.Security;
using RetailCentral.Api.Services;
using RetailCentral.Api.Services.Deployments;
using Serilog;
using System.Security.Claims;
using System.Threading.RateLimiting;


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
===============================================================================
*/

// -----------------------------------------------------------------------------
// STEP 1: Preload configuration so logging can be configured before host build
// -----------------------------------------------------------------------------
var preConfig = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true, reloadOnChange: true)
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
// STEP 2: Configure Serilog
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
    // CORE SERVICES
    // -------------------------------------------------------------------------
    builder.Services.AddScoped<IDeploymentService, DeploymentService>();
    builder.Services.AddScoped<AuditService>();
    builder.Services.AddScoped<CommandValidationService>();

    // -------------------------------------------------------------------------
    // MVC + RAZOR
    // -------------------------------------------------------------------------
    builder.Services.AddControllersWithViews();
    builder.Services.AddEndpointsApiExplorer();

    // -------------------------------------------------------------------------
    // SWAGGER
    // -------------------------------------------------------------------------
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "RetailCommand API - Device Control Center",
            Version = "v1",
            Description = "Centralized device control, monitoring, command execution, and deployment management."
        });
    });

    // -------------------------------------------------------------------------
    // BACKGROUND WORKERS
    // -------------------------------------------------------------------------
    builder.Services.AddHostedService<DataRetentionService>();
    builder.Services.AddHostedService<SoftwareInventoryScheduleWorker>();
    builder.Services.AddHostedService<ProcessStatusScheduleWorker>();
    builder.Services.AddHostedService<CommandTimeoutWorker>();
    builder.Services.AddHostedService<RegisterInventoryRefreshWorker>();

    // -------------------------------------------------------------------------
    // DATABASE
    // -------------------------------------------------------------------------
    builder.Services.AddDbContext<RetailCentralDbContext>(options =>
    {
        options.UseSqlServer(builder.Configuration.GetConnectionString("RetailCentral"));
    });

    // -------------------------------------------------------------------------
    // RATE LIMITING
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

    // AGENT POLLING HINTS
    builder.Services.Configure<AgentPollingHintsOptions>(
    builder.Configuration.GetSection("AgentPollingHints"));

    // -------------------------------------------------------------------------
    // DATA PROTECTION
    // -------------------------------------------------------------------------
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "dp_keys")));

    builder.Services.AddSingleton<DeviceSecretProtection>();

    // -------------------------------------------------------------------------
    // CONFIGURATION BINDING
    // -------------------------------------------------------------------------
    builder.Services.Configure<CommandTimeoutOptions>(
        builder.Configuration.GetSection("CommandTimeout"));

    // Server-side command creation policy.
    // IMPORTANT: CommandPolicy must be a TOP-LEVEL section in appsettings.json.
    builder.Services.Configure<CommandPolicyOptions>(
        builder.Configuration.GetSection(CommandPolicyOptions.SectionName));

    // -------------------------------------------------------------------------
    // AUTHENTICATION
    // -------------------------------------------------------------------------
    builder.Services
        .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate();

    builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, CustomAuthorizationMiddlewareResultHandler>();

    // -------------------------------------------------------------------------
    // AUTHORIZATION
    // -------------------------------------------------------------------------
    var authSection = builder.Configuration.GetSection("Security:Authorization");

    var dashboardViewerGroups = authSection.GetSection("DashboardViewerGroups").Get<string[]>() ?? throw new InvalidOperationException("Missing DashboardViewerGroups");
    var dashboardHelpdeskGroups = authSection.GetSection("DashboardHelpdeskGroups").Get<string[]>() ?? throw new InvalidOperationException("Missing DashboardHelpdeskGroups");
    var dashboardEngineerGroups = authSection.GetSection("DashboardEngineerGroups").Get<string[]>() ?? throw new InvalidOperationException("Missing DashboardEngineerGroups");
    var dashboardAdminGroups = authSection.GetSection("DashboardAdminGroups").Get<string[]>() ?? throw new InvalidOperationException("Missing DashboardAdminGroups");

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("DashboardViewer", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(ctx => IsUserInAnyGroup(ctx.User, dashboardViewerGroups));
        });

        options.AddPolicy("DashboardHelpdesk", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(ctx => IsUserInAnyGroup(ctx.User, dashboardHelpdeskGroups));
        });

        options.AddPolicy("DashboardEngineer", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(ctx => IsUserInAnyGroup(ctx.User, dashboardEngineerGroups));
        });

        options.AddPolicy("DashboardAdmin", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(ctx => IsUserInAnyGroup(ctx.User, dashboardAdminGroups));
        });
    });

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddHealthChecks();

    // -------------------------------------------------------------------------
    // BUILD APP
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

    app.UseStaticFiles();
    app.UseMiddleware<HmacAuthMiddleware>();
    app.UseRateLimiter();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHttpsRedirection();
    }

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
    // START APP
    // -------------------------------------------------------------------------
    app.Run();
}
catch (HostAbortedException)
{
    // Expected during some EF design-time operations.
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