using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using RetailCentral.Api.Data;
using RetailCentral.Api.Security;
using RetailCentral.Api.Services;
using RetailCentral.Api.Services.Deployments;
using Serilog;
using System.IO;
using System.Security.Claims;
using System.Threading.RateLimiting;

/*
===============================================================================
RetailCommand API - Program.cs
-------------------------------------------------------------------------------
This is the main entry point and composition root for the entire application.

Responsibilities:
- Configure logging (Serilog)
- Configure dependency injection
- Configure middleware pipeline
- Configure authentication & authorization
- Configure background workers
- Configure EF Core and database
- Configure routing (API + Dashboard)

IMPORTANT:
EF Core design-time operations (migrations) will trigger host startup.
This file is designed to gracefully handle that scenario.
===============================================================================
*/


// -----------------------------------------------------------------------------
// STEP 1: Preload configuration (for logging BEFORE host is built)
// -----------------------------------------------------------------------------
var preConfig = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

// Resolve log directory early so Serilog can initialize correctly
var logDir = preConfig["Logging:LogDirectory"];
if (string.IsNullOrWhiteSpace(logDir))
{
    logDir = Path.Combine(AppContext.BaseDirectory, "logs");
}

Directory.CreateDirectory(logDir);

Console.WriteLine($"RetailCommand API log directory: {logDir}");


// -----------------------------------------------------------------------------
// STEP 2: Configure Serilog (global logging pipeline)
// -----------------------------------------------------------------------------
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()

    // Reduce noisy framework logs
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)

    .WriteTo.Console()

    // Rolling file logs
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

    builder.WebHost.UseKestrel();         // Use Kestrel web server
    builder.Host.UseSerilog();            // Plug Serilog into ASP.NET pipeline


    // -------------------------------------------------------------------------
    // SERVICES - Dependency Injection
    // -------------------------------------------------------------------------

    // Core services
    builder.Services.AddScoped<IDeploymentService, DeploymentService>();

    // MVC + Razor Views (Dashboard UI)
    builder.Services.AddControllersWithViews();

    builder.Services.AddEndpointsApiExplorer();

    // Swagger / OpenAPI
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
    // These run continuously in the background

    builder.Services.AddHostedService<DataRetentionService>();              // Cleans up old data
    builder.Services.AddHostedService<SoftwareInventoryScheduleWorker>();   // Collects installed software
    builder.Services.AddHostedService<ProcessStatusScheduleWorker>();       // Collects process state
    builder.Services.AddHostedService<CommandTimeoutWorker>();              // Handles expired/stuck commands
    builder.Services.AddHostedService<RegisterInventoryRefreshWorker>();    // Refreshes device inventory


    // -------------------------------------------------------------------------
    // DATABASE (EF Core)
    // -------------------------------------------------------------------------
    builder.Services.AddDbContext<RetailCentralDbContext>(options =>
    {
        options.UseSqlServer(builder.Configuration.GetConnectionString("RetailCentral"));
    });


    // -------------------------------------------------------------------------
    // RATE LIMITING (Agent Protection)
    // -------------------------------------------------------------------------
    builder.Services.AddRateLimiter(options =>
    {
        // Per-device limiter (keyed by DeviceId or IP)
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
    // DATA PROTECTION (for encrypting device secrets)
    // -----------------------------------------------------------------------------
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "dp_keys")));

    builder.Services.AddSingleton<DeviceSecretProtection>();


    // -------------------------------------------------------------------------
    // CONFIGURATION BINDING
    // -----------------------------------------------------------------------------
    builder.Services.Configure<CommandTimeoutOptions>(
        builder.Configuration.GetSection("CommandTimeout"));


    // -------------------------------------------------------------------------
    // AUTHENTICATION (Windows / AD)
    // -----------------------------------------------------------------------------
    builder.Services
        .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate();

    builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, CustomAuthorizationMiddlewareResultHandler>();


    // -------------------------------------------------------------------------
    // AUTHORIZATION (Role-based access using AD groups)
    // -----------------------------------------------------------------------------
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
    builder.Services.AddScoped<AuditService>();
    builder.Services.AddHealthChecks();


    // -------------------------------------------------------------------------
    // BUILD APP
    // -----------------------------------------------------------------------------
    var app = builder.Build();


    // -------------------------------------------------------------------------
    // MIDDLEWARE PIPELINE
    // -----------------------------------------------------------------------------

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseStaticFiles();                // Enables CSS/JS/images for dashboard

    app.UseMiddleware<HmacAuthMiddleware>();  // Protects agent endpoints
    app.UseRateLimiter();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHttpsRedirection();
    }

    app.UseAuthentication();
    app.UseAuthorization();


    // -------------------------------------------------------------------------
    // ROUTING
    // -----------------------------------------------------------------------------

    app.MapControllers();  // API endpoints

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Dashboard}/{action=Index}/{id?}");

    app.MapHealthChecks("/health");


    // -------------------------------------------------------------------------
    // START APPLICATION
    // -----------------------------------------------------------------------------
    app.Run();
}


// -----------------------------------------------------------------------------
// EXCEPTION HANDLING
// -----------------------------------------------------------------------------
catch (HostAbortedException)
{
    // IMPORTANT:
    // This happens during EF Core migrations (design-time execution).
    // It is NOT an actual crash and should NOT be logged as fatal.
}
catch (Exception ex)
{
    // Real startup failure
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