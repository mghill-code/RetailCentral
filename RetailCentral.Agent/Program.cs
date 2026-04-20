using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using RetailCentral.Agent.Configuration;
using RetailCentral.Agent.Services;
using Serilog;

var exeDir = AppContext.BaseDirectory;
var logDir = Path.Combine(exeDir, "logs");

Directory.CreateDirectory(logDir);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logDir, "agent-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true)
    .CreateLogger();

Log.Information("RetailCentral Agent starting...");

// Enforce RDP shadow policy on startup
RemoteDesktopShadowPolicy.EnsureEnabled();

// TEMP: useful for manually generating a protected secret
// if (args.Length > 0 && args[0].Equals("protect-secret", StringComparison.OrdinalIgnoreCase))
// {
//     ProtectSecretTool.Run();
//     return;
// }

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Reduce noisy built-in HttpClient request lifecycle logging.
// Keep Warning/Error so real connection failures still show up.
builder.Logging.AddFilter("System.Net.Http", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient.*", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Extensions.Http.Logging", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Extensions.Http.Logging.LoggingHttpMessageHandler", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Extensions.Http.Logging.LoggingScopeHttpMessageHandler", LogLevel.Warning);

// IMPORTANT: wire Microsoft ILogger<T> to Serilog
builder.Services.AddSerilog();

// Run as Windows Service when installed; still runs fine as console with `dotnet run`
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "RetailCentral Agent";
});

// HttpClientFactory
builder.Services.AddHttpClient();

// Register custom validators before options are validated on startup
builder.Services.AddSingleton<IValidateOptions<ExecutionOptions>, ExecutionOptionsValidator>();

builder.Services
    .AddOptions<ExecutionOptions>()
    .Bind(builder.Configuration.GetSection(ExecutionOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<DownloadsOptions>()
    .Bind(builder.Configuration.GetSection(DownloadsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

//FileWrite settings
builder.Services.Configure<FileWriteOptions>(
    builder.Configuration.GetSection("FileWrites"));

// AgentPolling Configs
builder.Services.Configure<AgentPollingOptions>(
    builder.Configuration.GetSection("AgentPolling"));

// Legacy compatibility config object for services that still depend on AgentConfig
builder.Services.AddSingleton(sp =>
{
    var config = builder.Configuration;
    var downloads = sp.GetRequiredService<IOptions<DownloadsOptions>>().Value;

    var cfg = new AgentConfig
    {
        BaseUrl = config["Server:BaseUrl"] ?? "",
        StoreNumber = config["Agent:StoreNumber"] ?? "",
        Hostname = config["Agent:Hostname"] ?? Environment.MachineName,
        RegisterNumber = config["Agent:RegisterNumber"] ?? "",
        RegisterMetadataPath = config["Agent:RegisterMetadataPath"]
        ?? @"C:\RetailCentral\Agent\register-metadata.json",

        AgentVersion = config["Agent:AgentVersion"] ?? "0.0.0",
        DeviceId = config["Agent:DeviceId"] ?? "",
        DeviceSecret = config["Agent:DeviceSecret"] ?? "",
        DeviceSecretProtected = config["Agent:DeviceSecretProtected"] ?? "",
        BootstrapKey = config["Agent:BootstrapKey"] ?? "",
        PollSeconds = config.GetValue<int>("Agent:PollSeconds", 10),
        HeartbeatSeconds = config.GetValue<int>("Agent:HeartbeatSeconds", 30),
        MaxPendingFetch = config.GetValue<int>("Agent:MaxPendingFetch", 1),

        DeviceType = config["Provisioning:DeviceType"] ?? "Register",
        Environment = config["Provisioning:Environment"] ?? "Production",

        // Kept here for compatibility with existing downloader/package services
        DownloadRootFolder = downloads.RootFolder,
        StagingRootFolder = downloads.StagingRootFolder,

        PosProcessName = config["ProcessMonitoring:PosProcessName"] ?? "bncpos",
        RetailShellProcessName = config["ProcessMonitoring:RetailShellProcessName"] ?? "RetailShell",
        AgentProcessName = config["ProcessMonitoring:AgentProcessName"] ?? "RetailCentral.Agent",

        UserActivityEnabled = config.GetValue<bool>("UserActivity:Enabled"),
        UserActivitySnapshotPath = config["UserActivity:SnapshotPath"]
        ?? @"C:\ProgramData\RetailCentral\Shared\UserActivity.json"
    };

    Directory.CreateDirectory(cfg.DownloadRootFolder);
    Directory.CreateDirectory(cfg.StagingRootFolder);

    // If protected secret exists, it overrides plaintext DeviceSecret
    if (!string.IsNullOrWhiteSpace(cfg.DeviceSecretProtected))
    {
        cfg.DeviceSecret = DeviceSecretStore.Unprotect(cfg.DeviceSecretProtected);
    }

    return cfg;
});

// Our services
builder.Services.AddSingleton<HmacSigner>();
builder.Services.AddSingleton<AgentApiClient>();
builder.Services.AddSingleton<FileDownloadService>();
builder.Services.AddSingleton<DeploymentWindowService>();
builder.Services.AddSingleton<PackageExecutionService>();
builder.Services.AddSingleton<ExecutionPolicyService>();
builder.Services.AddSingleton<CommandExecutor>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddSingleton<ShellCommandClient>();

var host = builder.Build();

try
{
    host.Run();
}
finally
{
    Log.CloseAndFlush();
}

public sealed class AgentConfig
{
    public string BaseUrl { get; set; } = "";
    public string StoreNumber { get; set; } = "";
    public string RegisterNumber { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string RegisterMetadataPath { get; set; } = "";
    public string AgentVersion { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceSecret { get; set; } = "";
    public string DeviceSecretProtected { get; set; } = "";
    public string BootstrapKey { get; set; } = "";
    public int PollSeconds { get; set; } = 10;
    public int HeartbeatSeconds { get; set; } = 30;
    public int MaxPendingFetch { get; set; } = 1;

    public string DeviceType { get; set; } = "Register";
    public string Environment { get; set; } = "Production";

    // Kept for compatibility with existing download/package services
    public string DownloadRootFolder { get; set; } = "";
    public string StagingRootFolder { get; set; } = "";

    public string PosProcessName { get; set; } = "bncpos";
    public string RetailShellProcessName { get; set; } = "RetailShell";
    public string AgentProcessName { get; set; } = "RetailCentral.Agent";

    public bool UserActivityEnabled { get; set; } = true;
    public string UserActivitySnapshotPath { get; set; } =
        @"C:\ProgramData\RetailCentral\Shared\UserActivity.json";
}

public static class RemoteDesktopShadowPolicy
{
    private const string PolicyKeyPath = @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";
    private const string ShadowValueName = "Shadow";
    private const int ShadowValue = 2;

    public static void EnsureEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(PolicyKeyPath, writable: true);

            if (key == null)
            {
                Log.Warning("Unable to open or create registry key for RDP shadow policy: {KeyPath}", PolicyKeyPath);
                return;
            }

            var currentValue = key.GetValue(ShadowValueName);
            var currentInt = currentValue != null ? Convert.ToInt32(currentValue) : -1;

            if (currentInt != ShadowValue)
            {
                key.SetValue(ShadowValueName, ShadowValue, RegistryValueKind.DWord);
                Log.Information(
                    "Updated registry policy {KeyPath}\\{ValueName} to {Value}.",
                    PolicyKeyPath,
                    ShadowValueName,
                    ShadowValue);
            }
            else
            {
                Log.Information(
                    "Registry policy {KeyPath}\\{ValueName} already set to {Value}.",
                    PolicyKeyPath,
                    ShadowValueName,
                    ShadowValue);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set registry policy {KeyPath}\\{ValueName}.", PolicyKeyPath, ShadowValueName);
        }
    }
}
