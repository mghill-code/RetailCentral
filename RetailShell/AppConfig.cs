using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace RetailShell
{
    public class UtilityItem
    {
        public string Name { get; set; } = "";  // Button display text
        public string Exe { get; set; } = "";   // Full command or full path to executable
    }

    public class AppConfig
    {
        private static AppConfig? _instance;
        public static AppConfig Instance => _instance ??= new AppConfig();

        // ===== General Settings =====
        public bool EnableAnimations { get; set; } = true;
        public bool EnableLogging { get; set; } = true;
        public string LogFolder { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        public int LogRetentionDays { get; set; } = 30;

        // ===== POS Settings =====
        public string POSExecutablePath { get; set; } = string.Empty;
        public string POSArguments { get; set; } = string.Empty;
        public string POSWorkingDirectory { get; set; } = string.Empty;
        public bool POSAutoLaunch { get; set; } = true;
        public int POSLaunchDelaySeconds { get; set; } = 2;

        // ===== Screen Calibration Visibility (config-driven) =====
        // Values are "friendly names" from modelMappings.json, e.g. "NCR 70XRT"
        public List<string> ScreenCalibrateAllowedModels { get; private set; } = new List<string>();

        // ===== Utilities =====
        public List<UtilityItem> Utilities { get; private set; } = new List<UtilityItem>();

        // ===== User Activity =====
        public bool UserActivityEnabled { get; set; } = true;
        public string UserActivitySnapshotPath { get; set; } =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shared", "UserActivity.json");
        public int UserActivityWriteIntervalSeconds { get; set; } = 30;
        public int UserActivityActiveThresholdSeconds { get; set; } = 60;
        public string POSProcessName { get; set; } = "bncpos";

        private AppConfig()
        {
            LoadFromJson();

            if (!Directory.Exists(LogFolder))
                Directory.CreateDirectory(LogFolder);

            CleanupOldLogs();
        }

        private void LoadFromJson()
        {
            try
            {
                string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                if (!File.Exists(jsonPath)) return;

                string jsonText = File.ReadAllText(jsonPath);
                var jObject = JObject.Parse(jsonText);

                EnableAnimations = jObject["EnableAnimations"]?.Value<bool>() ?? EnableAnimations;
                EnableLogging = jObject["EnableLogging"]?.Value<bool>() ?? EnableLogging;
                LogFolder = jObject["LogFolder"]?.Value<string>() ?? LogFolder;
                LogRetentionDays = jObject["LogRetentionDays"]?.Value<int>() ?? LogRetentionDays;

                // If LogFolder is relative in JSON (ex: "Logs"), make it relative to the app base dir
                if (!Path.IsPathRooted(LogFolder))
                    LogFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LogFolder);

                // NEW: ScreenCalibrateAllowedModels
                var allowedModels = jObject["ScreenCalibrateAllowedModels"];
                if (allowedModels != null)
                {
                    ScreenCalibrateAllowedModels =
                        allowedModels.ToObject<List<string>>() ?? new List<string>();
                }

                var posSection = jObject["POS"];
                if (posSection != null)
                {
                    POSExecutablePath = posSection["ExecutablePath"]?.Value<string>() ?? POSExecutablePath;
                    POSArguments = posSection["Arguments"]?.Value<string>() ?? POSArguments;
                    POSWorkingDirectory = posSection["WorkingDirectory"]?.Value<string>() ?? POSWorkingDirectory;
                    POSAutoLaunch = posSection["AutoLaunch"]?.Value<bool>() ?? POSAutoLaunch;
                    POSLaunchDelaySeconds = posSection["LaunchDelaySeconds"]?.Value<int>() ?? POSLaunchDelaySeconds;
                }

                var utilsSection = jObject["Utilities"];
                if (utilsSection != null)
                {
                    Utilities = utilsSection.ToObject<List<UtilityItem>>() ?? new List<UtilityItem>();
                }
            }
            catch
            {
                // Ignore JSON errors, use defaults
            }
        }

        public string GetDailyLogFile()
        {
            return Path.Combine(LogFolder, $"retailshell.{DateTime.Now:yyyyMMdd}.log");
        }

        public string LogFilePath => GetDailyLogFile();

        public void CleanupOldLogs()
        {
            try
            {
                if (!Directory.Exists(LogFolder)) return;

                var files = Directory.GetFiles(LogFolder, "retailshell.*.log");
                foreach (var file in files)
                {
                    try
                    {
                        if (File.GetCreationTime(file) < DateTime.Now.AddDays(-LogRetentionDays))
                            File.Delete(file);
                    }
                    catch { }
                }
            }
            catch { }
        }

        public UtilityItem? GetUtility(string name)
        {
            return Utilities.Find(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
