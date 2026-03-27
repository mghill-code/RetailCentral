using System.Text.Json;
using Microsoft.Extensions.Logging;

public class UserActivitySnapshotReader
{
    private readonly string _filePath;
    private readonly ILogger _logger;

    public UserActivitySnapshotReader(string filePath, ILogger logger)
    {
        _filePath = filePath;
        _logger = logger;
    }

    public UserActivityDto? Read()
    {
        try
        {
            _logger.LogInformation("Checking user activity snapshot file at: {FilePath}", _filePath);

            if (!File.Exists(_filePath))
            {
                _logger.LogWarning("User activity snapshot file does not exist at: {FilePath}", _filePath);
                return null;
            }

            var json = File.ReadAllText(_filePath);

            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogWarning("User activity snapshot file is empty at: {FilePath}", _filePath);
                return null;
            }

            _logger.LogInformation("User activity snapshot raw JSON: {Json}", json);

            var data = JsonSerializer.Deserialize<UserActivityDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read user activity snapshot from {FilePath}", _filePath);
            return null;
        }
    }
}

public class UserActivityDto
{
    public DateTime? CapturedUtc { get; set; }
    public DateTime? LastInputUtc { get; set; }
    public int? IdleSeconds { get; set; }
    public string? SessionState { get; set; }
    public string? ConsoleUserName { get; set; }
    public bool? IsUserActive { get; set; }
    public bool? IsPosForeground { get; set; }
    public DateTime UpdatedUtc { get; set; }
}