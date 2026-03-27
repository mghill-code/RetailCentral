using System;
using System.IO;

public static class Logger
{
    private static string _logPath = "logs\\retailshell.log";
    private static bool _enabled = false;

    public static void Initialize(string path, bool enabled)
    {
        _enabled = enabled;
        _logPath = path;

        if (_enabled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        }
    }

    public static void Log(string message)
    {
        if (!_enabled) return;

        string entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
        File.AppendAllText(_logPath, entry + Environment.NewLine);
    }
}