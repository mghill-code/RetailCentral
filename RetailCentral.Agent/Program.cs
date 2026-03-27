using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Win32;
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

// IMPORTANT: wire Microsoft ILogger<T> to Serilog
builder.Services.AddSerilog();

// Run as Windows Service when installed; still runs fine as console with `dotnet run`
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "RetailCentral Agent";
});

// HttpClientFactory
builder.Services.AddHttpClient();

// Config object
builder.Services.AddSingleton(sp =>
{
    var cfg = new AgentConfig();

    cfg.BaseUrl = builder.Configuration["Server:BaseUrl"] ?? "";
    cfg.StoreNumber = builder.Configuration["Agent:StoreNumber"] ?? "";
    cfg.Hostname = builder.Configuration["Agent:Hostname"] ?? Environment.MachineName;
    cfg.RegisterNumber = builder.Configuration["Agent:RegisterNumber"] ?? "";
    cfg.RegisterMetadataPath = builder.Configuration["Agent:RegisterMetadataPath"]
        ?? @"C:\RetailCentral\Agent\register-metadata.json";

    cfg.AgentVersion = builder.Configuration["Agent:AgentVersion"] ?? "0.0.0";
    cfg.DeviceId = builder.Configuration["Agent:DeviceId"] ?? "";
    cfg.DeviceSecret = builder.Configuration["Agent:DeviceSecret"] ?? "";
    cfg.DeviceSecretProtected = builder.Configuration["Agent:DeviceSecretProtected"] ?? "";
    cfg.BootstrapKey = builder.Configuration["Agent:BootstrapKey"] ?? "";
    cfg.PollSeconds = int.Parse(builder.Configuration["Agent:PollSeconds"] ?? "10");
    cfg.HeartbeatSeconds = int.Parse(builder.Configuration["Agent:HeartbeatSeconds"] ?? "30");
    cfg.MaxPendingFetch = int.Parse(builder.Configuration["Agent:MaxPendingFetch"] ?? "1");

    cfg.DefaultTimeoutSeconds = int.Parse(builder.Configuration["Execution:DefaultTimeoutSeconds"] ?? "30");
    cfg.MaxStdoutChars = int.Parse(builder.Configuration["Execution:MaxStdoutChars"] ?? "8000");
    cfg.MaxStderrChars = int.Parse(builder.Configuration["Execution:MaxStderrChars"] ?? "8000");
    cfg.InstallCheckSeconds = int.Parse(builder.Configuration["Execution:InstallCheckSeconds"] ?? "30");

    cfg.DownloadRootFolder = builder.Configuration["Downloads:RootFolder"] ?? @"C:\RetailCentral\Agent\downloads";
    Directory.CreateDirectory(cfg.DownloadRootFolder);

    cfg.StagingRootFolder = builder.Configuration["Downloads:StagingRootFolder"] ?? @"C:\RetailCentral\Agent\staging";
    Directory.CreateDirectory(cfg.StagingRootFolder);

    var allowed = builder.Configuration.GetSection("Execution:AllowedCommands").Get<string[]>() ?? Array.Empty<string>();
    cfg.AllowedCommands = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);

    // If protected secret exists, it overrides plaintext DeviceSecret
    if (!string.IsNullOrWhiteSpace(cfg.DeviceSecretProtected))
    {
        cfg.DeviceSecret = DeviceSecretStore.Unprotect(cfg.DeviceSecretProtected);
    }
    cfg.PosProcessName = builder.Configuration["ProcessMonitoring:PosProcessName"] ?? "bncpos";
    cfg.RetailShellProcessName = builder.Configuration["ProcessMonitoring:RetailShellProcessName"] ?? "RetailShell";
    cfg.AgentProcessName = builder.Configuration["ProcessMonitoring:AgentProcessName"] ?? "RetailCentral.Agent";

    cfg.UserActivityEnabled = builder.Configuration.GetValue<bool>("UserActivity:Enabled");
    cfg.UserActivitySnapshotPath = builder.Configuration["UserActivity:SnapshotPath"]
        ?? @"C:\ProgramData\RetailCentral\Shared\UserActivity.json";

    return cfg;
});

// Our services
builder.Services.AddSingleton<HmacSigner>();
builder.Services.AddSingleton<AgentApiClient>();
builder.Services.AddSingleton<FileDownloadService>();
builder.Services.AddSingleton<DeploymentWindowService>();
builder.Services.AddSingleton<PackageExecutionService>();
builder.Services.AddSingleton<CommandExecutor>();
builder.Services.AddHostedService<Worker>();

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

    public int DefaultTimeoutSeconds { get; set; } = 30;
    public int MaxStdoutChars { get; set; } = 8000;
    public int MaxStderrChars { get; set; } = 8000;
    public int InstallCheckSeconds { get; set; } = 30;

    public string DownloadRootFolder { get; set; } = "";
    public string StagingRootFolder { get; set; } = "";
    public string PosProcessName { get; set; } = "bncpos";
    public string RetailShellProcessName { get; set; } = "RetailShell";
    public string AgentProcessName { get; set; } = "RetailCentral.Agent";
    public HashSet<string> AllowedCommands { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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
                Log.Information("Updated registry policy {KeyPath}\\{ValueName} to {Value}.", PolicyKeyPath, ShadowValueName, ShadowValue);
            }
            else
            {
                Log.Information("Registry policy {KeyPath}\\{ValueName} already set to {Value}.", PolicyKeyPath, ShadowValueName, ShadowValue);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set registry policy {KeyPath}\\{ValueName}.", PolicyKeyPath, ShadowValueName);
        }
    }
}