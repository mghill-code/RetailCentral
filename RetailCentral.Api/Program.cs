using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RetailCentral.Api.Data;
using RetailCentral.Api.Security;
using RetailCentral.Api.Services;
using RetailCentral.Api.Services.Deployments;
using Serilog;
using System.IO;
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
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logDir, "api-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
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

    var app = builder.Build();

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