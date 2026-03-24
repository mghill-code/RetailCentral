using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RetailCentral.Api.Data;
using RetailCentral.Api.Security;
using RetailCentral.Api.Services;
using RetailCentral.Api.Services.Deployments;
using Serilog;
using System.IO;
using System.Security.Claims;
using System.Threading.RateLimiting;


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

Console.WriteLine($"RetailCentral API log directory: {logDir}");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()

    // 🔥 Reduce EF Core noise
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)

    // 🔥 Reduce ASP.NET noise
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

try
{
    Log.Information("RetailCentral API starting...");
    Log.Information("RetailCentral API log directory resolved to {LogDir}", logDir);
    Log.Information("RetailCentral API base directory is {BaseDir}", AppContext.BaseDirectory);

    var builder = WebApplication.CreateBuilder(args);

    builder.WebHost.UseKestrel();
    builder.Host.UseSerilog();

    builder.Services.AddScoped<IDeploymentService, DeploymentService>();
    builder.Services.AddControllersWithViews();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    builder.Services.AddHostedService<DataRetentionService>();
    builder.Services.AddHostedService<SoftwareInventoryScheduleWorker>();
    builder.Services.AddHostedService<ProcessStatusScheduleWorker>();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<AuditService>();
    builder.Services.AddHealthChecks();
    builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, CustomAuthorizationMiddlewareResultHandler>();
    builder.Services.AddDbContext<RetailCentralDbContext>(options =>
    {
        options.UseSqlServer(builder.Configuration.GetConnectionString("RetailCentral"));
    });

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

    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "dp_keys")));

    builder.Services.AddSingleton<DeviceSecretProtection>();

    builder.Services.Configure<CommandTimeoutOptions>(
        builder.Configuration.GetSection("CommandTimeout"));

    builder.Services.AddHostedService<CommandTimeoutWorker>();
    builder.Services.AddHostedService<RegisterInventoryRefreshWorker>();

    builder.Services.AddHealthChecks();

    // =========================
    // Active Directory / Windows Authentication
    // =========================
    builder.Services
        .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate();

    var authSection = builder.Configuration.GetSection("Security:Authorization");

    var dashboardViewerGroups =
        authSection.GetSection("DashboardViewerGroups").Get<string[]>()
        ?? throw new InvalidOperationException("Missing Security:Authorization:DashboardViewerGroups configuration.");

    var dashboardHelpdeskGroups =
        authSection.GetSection("DashboardHelpdeskGroups").Get<string[]>()
        ?? throw new InvalidOperationException("Missing Security:Authorization:DashboardHelpdeskGroups configuration.");

    var dashboardEngineerGroups =
        authSection.GetSection("DashboardEngineerGroups").Get<string[]>()
        ?? throw new InvalidOperationException("Missing Security:Authorization:DashboardEngineerGroups configuration.");

    var dashboardAdminGroups =
        authSection.GetSection("DashboardAdminGroups").Get<string[]>()
        ?? throw new InvalidOperationException("Missing Security:Authorization:DashboardAdminGroups configuration.");

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

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseStaticFiles();

    // HMAC remains for agent endpoints
    app.UseMiddleware<HmacAuthMiddleware>();

    app.UseRateLimiter();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHttpsRedirection();
    }

    app.UseAuthentication();
    app.UseAuthorization();

    
    app.MapControllers();

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Dashboard}/{action=Index}/{id?}");

    app.MapHealthChecks("/health");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "RetailCentral API terminated unexpectedly.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

static bool IsUserInAnyGroup(ClaimsPrincipal user, IEnumerable<string> groups)
{
    foreach (var group in groups)
    {
        if (user.IsInRole(group))
            return true;
    }

    return false;
}