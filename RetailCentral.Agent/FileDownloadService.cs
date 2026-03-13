using System.Security.Cryptography;

public sealed class FileDownloadService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AgentConfig _cfg;

    public FileDownloadService(IHttpClientFactory httpClientFactory, AgentConfig cfg)
    {
        _httpClientFactory = httpClientFactory;
        _cfg = cfg;
    }

    public async Task<string> DownloadAsync(string url, string destinationFileName, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();

        var destinationPath = Path.Combine(_cfg.DownloadRootFolder, destinationFileName);

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = File.Create(destinationPath);
        await source.CopyToAsync(target, ct);

        return destinationPath;
    }

    public static string ComputeSha256Hex(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}