using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = Host.CreateApplicationBuilder(args);

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
    cfg.AgentVersion = builder.Configuration["Agent:AgentVersion"] ?? "0.0.0";
    cfg.DeviceId = builder.Configuration["Agent:DeviceId"] ?? "";
    cfg.DeviceSecret = builder.Configuration["Agent:DeviceSecret"] ?? "";
    cfg.PollSeconds = int.Parse(builder.Configuration["Agent:PollSeconds"] ?? "10");
    cfg.HeartbeatSeconds = int.Parse(builder.Configuration["Agent:HeartbeatSeconds"] ?? "30");
    cfg.MaxPendingFetch = int.Parse(builder.Configuration["Agent:MaxPendingFetch"] ?? "1");

    cfg.DefaultTimeoutSeconds = int.Parse(builder.Configuration["Execution:DefaultTimeoutSeconds"] ?? "30");
    cfg.MaxStdoutChars = int.Parse(builder.Configuration["Execution:MaxStdoutChars"] ?? "8000");
    cfg.MaxStderrChars = int.Parse(builder.Configuration["Execution:MaxStderrChars"] ?? "8000");

    var allowed = builder.Configuration.GetSection("Execution:AllowedCommands").Get<string[]>() ?? Array.Empty<string>();
    cfg.AllowedCommands = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);

    return cfg;
});

// Our services
builder.Services.AddSingleton<HmacSigner>();
builder.Services.AddSingleton<AgentApiClient>();
builder.Services.AddSingleton<CommandExecutor>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

public sealed class AgentConfig
{
    public string BaseUrl { get; set; } = "";
    public string StoreNumber { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string AgentVersion { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceSecret { get; set; } = "";
    public int PollSeconds { get; set; } = 10;
    public int HeartbeatSeconds { get; set; } = 30;
    public int MaxPendingFetch { get; set; } = 1;

    public int DefaultTimeoutSeconds { get; set; } = 30;
    public int MaxStdoutChars { get; set; } = 8000;
    public int MaxStderrChars { get; set; } = 8000;
    public HashSet<string> AllowedCommands { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
