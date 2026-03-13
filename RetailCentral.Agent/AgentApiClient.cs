using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using System.Runtime.Versioning;

public sealed class AgentApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AgentConfig _cfg;
    private readonly HmacSigner _signer;

    public AgentApiClient(IHttpClientFactory httpClientFactory, AgentConfig cfg, HmacSigner signer)
    {
        _httpClientFactory = httpClientFactory;
        _cfg = cfg;
        _signer = signer;
    }

    private HttpClient CreateClient()
    {
        var c = _httpClientFactory.CreateClient();
        c.BaseAddress = new Uri(_cfg.BaseUrl.TrimEnd('/'));
        return c;
    }

    public async Task<(Guid DeviceId, string DeviceSecret)?> EnrollAsync(CancellationToken ct)
    {
        var client = CreateClient();

        var body = new
        {
            storeNumber = _cfg.StoreNumber,
            hostname = _cfg.Hostname,
            agentVersion = _cfg.AgentVersion,
            osVersion = Environment.OSVersion.VersionString,

            bootstrapKey = _cfg.BootstrapKey,
            machineName = Environment.MachineName,
            machineGuid = GetMachineGuid()
        };

        var resp = await client.PostAsJsonAsync("/api/agent/v1/enroll", body, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var txt = await resp.Content.ReadAsStringAsync(ct);
            throw new Exception($"Enroll failed: {(int)resp.StatusCode} {txt}");
        }

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

        var deviceId = Guid.Parse(json.GetProperty("deviceId").GetString()!);
        var secretProp = json.TryGetProperty("deviceSecret", out var s) ? s.GetString() : null;

        // only new device enroll returns a secret (existing device returns null)
        if (string.IsNullOrWhiteSpace(secretProp))
            return null;

        return (deviceId, secretProp!);
    }

    public async Task<string> GetPendingAsync(int max, CancellationToken ct)
    {
        var path = $"/api/agent/v1/commands/pending?max={max}";
        var method = "GET";
        var bodyBytes = Array.Empty<byte>();

        var (ts, sig) = _signer.Sign(_cfg.DeviceSecret, method, path, bodyBytes);

        var client = CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Add("X-Device-Id", _cfg.DeviceId);
        req.Headers.Add("X-Device-Timestamp", ts);
        req.Headers.Add("X-Device-Signature", sig);

        var resp = await client.SendAsync(req, ct);
        var txt = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"GetPending failed: {(int)resp.StatusCode} {txt}");

        return txt;
    }

    public async Task HeartbeatAsync(object heartbeatBody, CancellationToken ct)
    {
        var path = "/api/agent/v1/heartbeat";
        var method = "POST";

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(heartbeatBody);

        var (ts, sig) = _signer.Sign(_cfg.DeviceSecret, method, path, jsonBytes);

        var client = CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Content = new ByteArrayContent(jsonBytes);
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        req.Headers.Add("X-Device-Id", _cfg.DeviceId);
        req.Headers.Add("X-Device-Timestamp", ts);
        req.Headers.Add("X-Device-Signature", sig);

        var resp = await client.SendAsync(req, ct);
        var txt = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Heartbeat failed: {(int)resp.StatusCode} {txt}");
    }

    public async Task PostResultAsync(Guid commandId, object resultBody, CancellationToken ct)
    {
        var path = $"/api/agent/v1/commands/{commandId}/result";
        var method = "POST";

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(resultBody);

        var (ts, sig) = _signer.Sign(_cfg.DeviceSecret, method, path, jsonBytes);

        var client = CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Content = new ByteArrayContent(jsonBytes);
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        req.Headers.Add("X-Device-Id", _cfg.DeviceId);
        req.Headers.Add("X-Device-Timestamp", ts);
        req.Headers.Add("X-Device-Signature", sig);

        var resp = await client.SendAsync(req, ct);
        var txt = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"PostResult failed: {(int)resp.StatusCode} {txt}");
    }
    [SupportedOSPlatform("windows")]
    private static string? GetMachineGuid()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid")?.ToString();
        }
        catch
        {
            return null;
        }
    }
}