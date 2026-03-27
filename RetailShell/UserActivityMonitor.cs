using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;

namespace RetailShell
{
    public sealed class UserActivityMonitor : IDisposable
    {
        private readonly AppConfig _cfg;
        private readonly DispatcherTimer _timer;
        private readonly JsonSerializerOptions _jsonOptions;

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public UserActivityMonitor(AppConfig cfg)
        {
            _cfg = cfg;
            _jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(Math.Max(5, _cfg.UserActivityWriteIntervalSeconds))
            };
            _timer.Tick += (_, _) => WriteSnapshotSafe();
        }

        public void Start()
        {
            if (!_cfg.UserActivityEnabled)
                return;

            WriteSnapshotSafe();
            _timer.Start();
        }

        public void Stop() => _timer.Stop();

        private void WriteSnapshotSafe()
        {
            try
            {
                var snapshot = CaptureSnapshot();
                var directory = Path.GetDirectoryName(_cfg.UserActivitySnapshotPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(_cfg.UserActivitySnapshotPath, JsonSerializer.Serialize(snapshot, _jsonOptions));
            }
            catch (Exception ex)
            {
                Logger.Log($"UserActivityMonitor failed to write snapshot: {ex}");
            }
        }

        private UserActivitySnapshot CaptureSnapshot()
        {
            var nowUtc = DateTime.UtcNow;
            var idleSeconds = GetIdleSeconds();

            return new UserActivitySnapshot
            {
                CapturedUtc = nowUtc,
                LastInputUtc = idleSeconds.HasValue ? nowUtc.AddSeconds(-idleSeconds.Value) : null,
                IdleSeconds = idleSeconds,
                SessionState = "Active",
                ConsoleUserName = Environment.UserName,
                IsUserActive = idleSeconds.HasValue && idleSeconds.Value < _cfg.UserActivityActiveThresholdSeconds,
                IsPosForeground = IsPosForeground()
            };
        }

        private static int? GetIdleSeconds()
        {
            var lastInput = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref lastInput))
                return null;

            var idleMs = unchecked((uint)Environment.TickCount - lastInput.dwTime);
            return (int)(idleMs / 1000);
        }

        private bool IsPosForeground()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                    return false;

                GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == 0)
                    return false;

                using var process = Process.GetProcessById((int)pid);
                return string.Equals(process.ProcessName, _cfg.POSProcessName, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public void Dispose() => _timer.Stop();
    }
}
