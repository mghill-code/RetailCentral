using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data;
using RetailCentral.Api.Security;
using RetailCentral.Api.Services;
using System.IO;
using System.Threading.RateLimiting;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<RetailCentralDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("RetailCentral"));
});

builder.Services.AddRateLimiter(options =>
{
    // Basic per-device limiter (by X-Device-Id). Falls back to IP if header missing.
    options.AddPolicy("agent", context =>
    {
        var key = context.Request.Headers["X-Device-Id"].FirstOrDefault()
                  ?? context.Connection.RemoteIpAddress?.ToString()
                  ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: key,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,              // 60 requests
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddDataProtection()
    // Dev-friendly: persist keys so secrets survive app restarts
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "dp_keys")));

builder.Services.AddSingleton<DeviceSecretProtection>();

builder.Services.Configure<CommandTimeoutOptions>(
    builder.Configuration.GetSection("CommandTimeout"));

builder.Services.AddHostedService<CommandTimeoutWorker>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<HmacAuthMiddleware>();
app.UseRateLimiter();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers().RequireRateLimiting("agent");
app.Run();