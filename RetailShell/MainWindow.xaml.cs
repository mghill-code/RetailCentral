using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace RetailShell
{
    public partial class MainWindow : Window
    {
        // ===== Win32 interop =====
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        private const int SW_RESTORE = 9;

        private Dictionary<string, string> _modelMappings = new Dictionary<string, string>();

        // Keeps Launch button state correct even if user closes POS via X
        private System.Windows.Threading.DispatcherTimer? _posWatchdog;

        // User activity monitor writes last input / idle state for the agent to upload
        private UserActivityMonitor? _userActivityMonitor;

        public MainWindow()
        {
            InitializeComponent();

            // Load brand image based on Store environment variable
            LoadBrandImage();

            // ===== Fullscreen kiosk =====
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            ResizeMode = ResizeMode.NoResize;

            // IMPORTANT: Do NOT keep TopMost on.
            Topmost = false;
            ShowInTaskbar = false;

            // Block Alt+F4
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F4 && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                    e.Handled = true;
            };

            // Wire main buttons
            BtnLaunchPOS.Click += async (s, e) => await LaunchOrBringPOSAsync();
            BtnExitPOS.Click += BtnExitPOS_Click;
            BtnUtilities.Click += BtnUtilities_Click;
            BtnBack.Click += BtnBack_Click;

            LoadModelMappings();
            SetSystemInfo();
            SetupUtilityButtons();
            UpdatePOSButtonState();

            // Watchdog: keeps Launch POS enabled/disabled correctly if POS is closed via X
            _posWatchdog = new System.Windows.Threading.DispatcherTimer();
            _posWatchdog.Interval = TimeSpan.FromSeconds(1);
            _posWatchdog.Tick += (s, e) => UpdatePOSButtonState();
            _posWatchdog.Start();

            // Start user activity monitor after window initialization
            StartUserActivityMonitor();

            if (AppConfig.Instance.EnableAnimations)
                FadeInWindow();

            if (AppConfig.Instance.POSAutoLaunch)
            {
                Task.Delay(AppConfig.Instance.POSLaunchDelaySeconds * 1000)
                    .ContinueWith(_ => Dispatcher.Invoke(async () => await LaunchOrBringPOSAsync()));
            }
        }

        // ===== Prevent closing RetailShell =====
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // RetailShell is intended to remain open as the replacement shell.
            e.Cancel = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _posWatchdog?.Stop();
                _userActivityMonitor?.Dispose();
            }
            catch
            {
                // Swallow any shutdown cleanup issues
            }

            base.OnClosed(e);
        }

        // ================= USER ACTIVITY =================

        private void StartUserActivityMonitor()
        {
            try
            {
                if (!AppConfig.Instance.UserActivityEnabled)
                {
                    Log("User activity monitor disabled by configuration.");
                    return;
                }

                _userActivityMonitor?.Dispose();
                _userActivityMonitor = new UserActivityMonitor(AppConfig.Instance);
                _userActivityMonitor.Start();

                Log($"User activity monitor started. SnapshotPath='{AppConfig.Instance.UserActivitySnapshotPath}', IntervalSeconds={AppConfig.Instance.UserActivityWriteIntervalSeconds}.");
            }
            catch (Exception ex)
            {
                Log("Failed to start user activity monitor: " + ex.Message);
            }
        }

        // ================= BRANDING =================

        private void LoadBrandImage()
        {
            try
            {
                // Store env var
                string store = Environment.GetEnvironmentVariable("Store") ?? string.Empty;
                store = store.Trim();

                // Optional normalization: if numeric, pad to 5 digits (e.g., "340" -> "00340")
                if (!string.IsNullOrWhiteSpace(store) && store.All(char.IsDigit) && store.Length < 5)
                    store = store.PadLeft(5, '0');

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string brandingDir = Path.Combine(baseDir, "Branding");

                string storePath = Path.Combine(brandingDir, $"{store}.png");
                string brandPath = Path.Combine(brandingDir, "Brand.png");
                string defaultPath = Path.Combine(brandingDir, "Default.png");

                string? chosen = null;

                if (!string.IsNullOrWhiteSpace(store) && File.Exists(storePath))
                    chosen = storePath;
                else if (File.Exists(brandPath))
                    chosen = brandPath;
                else if (File.Exists(defaultPath))
                    chosen = defaultPath;

                if (chosen == null)
                {
                    Log($"Brand image not found. Store='{store}'. Looked for '{storePath}', '{brandPath}', '{defaultPath}'.");
                    if (BrandImage != null) BrandImage.Source = null;
                    return;
                }

                // Load without locking the file
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(chosen, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();

                if (BrandImage != null)
                    BrandImage.Source = bmp;

                Log($"Loaded brand image: {chosen}");
            }
            catch (Exception ex)
            {
                Log("Brand image load error: " + ex.Message);
            }
        }

        // ================= POS =================

        /// <summary>
        /// If POS is running, brings it to front (even if it fell behind).
        /// If POS is not running, launches it and brings it to front.
        /// </summary>
        private async Task LaunchOrBringPOSAsync()
        {
            string exePath = AppConfig.Instance.POSExecutablePath;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                Log("POS executable path invalid or not found.");
                return;
            }

            string exeName = Path.GetFileNameWithoutExtension(exePath);

            // Always refresh state first
            UpdatePOSButtonState();

            // 1) If running, bring to front
            var running = Process.GetProcessesByName(exeName);
            if (running.Length > 0)
            {
                Log("POS already running - bringing to front.");

                foreach (var proc in running)
                    await BringProcessMainWindowToFrontAsync(proc);

                UpdatePOSButtonState();
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = AppConfig.Instance.POSArguments ?? "",
                    UseShellExecute = true,
                    WorkingDirectory = AppConfig.Instance.POSWorkingDirectory
                };

                var process = Process.Start(startInfo);

                if (process != null)
                {
                    Log($"POS launched: {exePath}");

                    // Wait for UI to initialize (some POS apps take a bit)
                    await BringProcessMainWindowToFrontAsync(process);

                    UpdatePOSButtonState();

                    // If POS exits, refresh UI state
                    _ = Task.Run(() =>
                    {
                        process.WaitForExit();
                        Dispatcher.Invoke(() =>
                        {
                            UpdatePOSButtonState();
                            Log("POS exited.");
                        });
                    });
                }
                else
                {
                    UpdatePOSButtonState();
                }
            }
            catch (Exception ex)
            {
                Log("Error launching POS: " + ex.Message);
                UpdatePOSButtonState();
            }
        }

        private void BtnExitPOS_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string exePath = AppConfig.Instance.POSExecutablePath;
                if (string.IsNullOrWhiteSpace(exePath)) return;

                string exeName = Path.GetFileNameWithoutExtension(exePath);
                var running = Process.GetProcessesByName(exeName);

                foreach (var proc in running)
                {
                    try
                    {
                        proc.Kill();
                        proc.WaitForExit();
                    }
                    catch { }
                }

                UpdatePOSButtonState();
                Log("POS exited successfully.");
            }
            catch (Exception ex)
            {
                Log("Error exiting POS: " + ex.Message);
            }
        }

        /// <summary>
        /// Robustly waits for a process to have a main window, then forces it to the foreground.
        /// Handles cases where the process is running but the window handle is not ready yet.
        /// </summary>
        private async Task BringProcessMainWindowToFrontAsync(Process proc)
        {
            if (proc == null) return;

            // Give the process a moment to create its window handle
            for (int i = 0; i < 30; i++) // ~3 seconds
            {
                try
                {
                    proc.Refresh();

                    if (proc.MainWindowHandle != IntPtr.Zero)
                    {
                        BringExternalWindowToFront(proc.MainWindowHandle);
                        return;
                    }
                }
                catch { }

                await Task.Delay(100);
            }

            // Last attempt: enumerate by name again (some POS spawns a child process for UI)
            try
            {
                var sameName = Process.GetProcessesByName(proc.ProcessName);
                foreach (var p in sameName)
                {
                    try
                    {
                        p.Refresh();
                        if (p.MainWindowHandle != IntPtr.Zero)
                        {
                            BringExternalWindowToFront(p.MainWindowHandle);
                            return;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void BringExternalWindowToFront(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;

            try
            {
                if (IsIconic(hWnd))
                    ShowWindow(hWnd, SW_RESTORE);

                // Attach thread input to bypass focus restrictions
                IntPtr fg = GetForegroundWindow();
                uint fgThread = GetWindowThreadProcessId(fg, out _);

                uint thisThread = GetWindowThreadProcessId(new WindowInteropHelper(this).Handle, out _);

                AttachThreadInput(thisThread, fgThread, true);

                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);

                AttachThreadInput(thisThread, fgThread, false);
            }
            catch
            {
                try { SetForegroundWindow(hWnd); } catch { }
            }
        }

        // ================= UTILITIES PANEL =================

        private void BtnUtilities_Click(object sender, RoutedEventArgs e)
        {
            MainButtonsPanel.Visibility = Visibility.Collapsed;
            UtilitiesPanel.Visibility = Visibility.Visible;
            Log("Utilities menu opened.");
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            UtilitiesPanel.Visibility = Visibility.Collapsed;
            MainButtonsPanel.Visibility = Visibility.Visible;
            Log("Returned to main menu from Utilities.");
        }

        // ================= MODEL-BASED UTILITY VISIBILITY =================

        /// <summary>
        /// Only show "Screen Calibrate" on specific models defined in modelMappings.json and AppSettings.json.
        /// This reads the raw WMI model, maps it via _modelMappings, then checks the friendly name.
        /// </summary>
        private bool ShouldShowScreenCalibrate()
        {
            try
            {
                // Read allowed models from appsettings.json
                var allowed = AppConfig.Instance.ScreenCalibrateAllowedModels;

                // If nothing configured, do not show
                if (allowed == null || allowed.Count == 0)
                    return false;

                // Raw hardware model from WMI
                string rawModel = GetComputerModel();
                if (string.IsNullOrWhiteSpace(rawModel))
                    return false;

                // Map raw model to friendly name using modelMappings.json
                foreach (var kvp in _modelMappings)
                {
                    if (rawModel.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        string friendly = (kvp.Value ?? string.Empty).Trim();

                        // Check if friendly name is in allowed list
                        return allowed.Any(a =>
                            string.Equals(a?.Trim(), friendly, StringComparison.OrdinalIgnoreCase));
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private void SetupUtilityButtons()
        {
            var buttonMap = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase)
            {
                { "Screen Calibrate", BtnScreenCalibrate },
                { "Configure PinPad", BtnPinPad },
                { "Scanner Program", BtnScannerProgram },
                { "Reboot", BtnReboot },
                { "Shutdown", BtnShutdown }
            };

            bool allowScreenCalibrate = ShouldShowScreenCalibrate();

            foreach (var btn in buttonMap.Values)
            {
                btn.Visibility = Visibility.Collapsed;
                btn.Click -= BtnUtility_Click;
                btn.Click -= BtnScannerProgram_Click;
                btn.Content = string.Empty;
                btn.ToolTip = null;
            }

            foreach (var util in AppConfig.Instance.Utilities)
            {
                if (!buttonMap.TryGetValue(util.Name, out var btn))
                    continue;

                // Only show Screen Calibrate on allowed models (from modelMappings.json)
                if (util.Name.Equals("Screen Calibrate", StringComparison.OrdinalIgnoreCase) && !allowScreenCalibrate)
                    continue;

                btn.Visibility = Visibility.Visible;
                btn.ToolTip = util.Exe;

                if (util.Name.Equals("Scanner Program", StringComparison.OrdinalIgnoreCase))
                {
                    btn.Content = new TextBlock
                    {
                        Text = "Scanner\nProgramming",
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    };
                    btn.Click += BtnScannerProgram_Click;
                }
                else
                {
                    btn.Content = util.Name;
                    btn.Click += BtnUtility_Click;
                }
            }

            BtnBack.Visibility = Visibility.Visible;
        }

        private void BtnUtility_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ToolTip is string cmd && !string.IsNullOrWhiteSpace(cmd))
            {
                try
                {
                    var (fileName, args) = SplitCommand(cmd);

                    bool isShutdown = fileName.Equals("shutdown", StringComparison.OrdinalIgnoreCase) ||
                                      fileName.EndsWith("shutdown.exe", StringComparison.OrdinalIgnoreCase);

                    if (isShutdown && fileName.Equals("shutdown", StringComparison.OrdinalIgnoreCase))
                        fileName = Path.Combine(Environment.SystemDirectory, "shutdown.exe");

                    var psi = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = args,
                        UseShellExecute = !isShutdown,
                        CreateNoWindow = isShutdown
                    };

                    var proc = Process.Start(psi);
                    Log($"Utility launched: {cmd}");

                    // Bring to front if it has a window
                    if (proc != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            await BringProcessMainWindowToFrontAsync(proc);
                        });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to launch utility:\n" + ex.Message);
                    Log("Failed to launch utility: " + ex.Message);
                }
            }
            else
            {
                MessageBox.Show("Utility executable not found or not configured.");
                Log("Utility launch blocked: executable not configured.");
            }
        }

        // ===== Scanner Programming =====

        private void BtnScannerProgram_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string model = DetectScannerModel();

                string imagePath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "ScannerImages",
                    $"{model}.jpg");

                if (!File.Exists(imagePath))
                {
                    imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScannerImages", "Unknown.jpg");
                }

                if (File.Exists(imagePath))
                {
                    var scannerWindow = new ScannerImageWindow { Owner = this };
                    scannerWindow.LoadImage(imagePath);
                    scannerWindow.FadeIn();
                    scannerWindow.ShowDialog();
                }
                else
                {
                    MessageBox.Show($"No programming image found for scanner model: {model}");
                    Log($"Scanner image missing. Model={model}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error opening scanner programming:\n" + ex.Message);
                Log("Scanner program error: " + ex.Message);
            }
        }

        private string DetectScannerModel()
        {
            try
            {
                string scannerName = string.Empty;

                using (var searcher = new ManagementObjectSearcher(@"root\CIMV2", "SELECT * FROM Symbol_BarcodeScanner"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        scannerName = mo["Name"]?.ToString() ?? scannerName;
                    }
                }

                if (!string.IsNullOrWhiteSpace(scannerName))
                {
                    if (scannerName.Contains("4308", StringComparison.OrdinalIgnoreCase)) return "DS4308";
                    if (scannerName.Contains("9208", StringComparison.OrdinalIgnoreCase)) return "DS9208";
                    if (scannerName.Contains("DS4208-HD", StringComparison.OrdinalIgnoreCase)) return "DS4208-HD";
                    if (scannerName.Contains("DS4208-SR", StringComparison.OrdinalIgnoreCase)) return "DS4208-SR";
                    if (scannerName.Contains("DS4208-DL", StringComparison.OrdinalIgnoreCase)) return "DS4208-DL";
                    if (scannerName.Contains("LS4208", StringComparison.OrdinalIgnoreCase)) return "LS4208";
                }

                using (var pnp = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%Barcode%' OR Name LIKE '%Scanner%'"))
                {
                    foreach (ManagementObject mo in pnp.Get())
                    {
                        string name = mo["Name"]?.ToString() ?? "";
                        if (name.IndexOf("HP ElitePOS 2D Barcode Scanner", StringComparison.OrdinalIgnoreCase) >= 0)
                            return "HP ElitePOS 2D Barcode Scanner";
                    }
                }
            }
            catch { }

            return "UnknownScanner";
        }

        // ================= SYSTEM INFO =================

        private void LoadModelMappings()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "modelMappings.json");
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var mappings = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    if (mappings != null)
                        _modelMappings = mappings;
                }
            }
            catch { }
        }

        private void SetSystemInfo()
        {
            try
            {
                TxtComputerModel.Text = ConvertModelToFriendly(GetComputerModel());
                TxtComputerName.Text = Environment.MachineName;
                TxtBootTime.Text = (DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount)).ToString("G");
                TxtStoreNumber.Text = Environment.GetEnvironmentVariable("Store") ?? "UnknownStore";
                TxtRegisterNumber.Text = Environment.GetEnvironmentVariable("REG_NUM") ?? "UnknownRegister";
                TxtIPAddress.Text = GetLocalIPAddress();
                TxtMacAddress.Text = GetMacAddress();
                TxtPinPadIP.Text = "192.168.1.50";
                Log("System info populated for " + Environment.MachineName);
            }
            catch (Exception ex)
            {
                Log("System info error: " + ex.Message);
            }
        }

        private string ConvertModelToFriendly(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return model;

            foreach (var key in _modelMappings.Keys)
            {
                if (model.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    return _modelMappings[key];
            }

            return model;
        }

        private string GetComputerModel()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Model FROM Win32_ComputerSystem");
                foreach (ManagementObject mo in searcher.Get())
                    return mo["Model"]?.ToString() ?? "Unknown";
            }
            catch { }

            return "Unknown";
        }

        private string GetLocalIPAddress()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var ip = ni.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                    if (ip != null)
                        return ip.Address.ToString();
                }
            }
            return "Unknown";
        }

        private string GetMacAddress()
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up &&
                    (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                     nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                {
                    var mac = nic.GetPhysicalAddress();
                    if (mac != null && mac.ToString() != "")
                        return string.Join(":", mac.GetAddressBytes().Select(b => b.ToString("X2")));
                }
            }
            return "Unknown";
        }

        // ================= helpers =================

        private void UpdatePOSButtonState()
        {
            try
            {
                string exePath = AppConfig.Instance.POSExecutablePath;

                // If not configured, disable both buttons
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    BtnLaunchPOS.IsEnabled = false;
                    BtnExitPOS.IsEnabled = false;
                    return;
                }

                // Launch should always be clickable (it acts as "Launch OR Bring To Front")
                BtnLaunchPOS.IsEnabled = true;

                // Exit only makes sense when running
                string exeName = Path.GetFileNameWithoutExtension(exePath);
                bool running = Process.GetProcessesByName(exeName).Length > 0;
                BtnExitPOS.IsEnabled = running;
            }
            catch
            {
                // Safe default
                BtnLaunchPOS.IsEnabled = true;
                BtnExitPOS.IsEnabled = true;
            }
        }

        private static (string FileName, string Arguments) SplitCommand(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return (string.Empty, string.Empty);
            commandLine = commandLine.Trim();

            if (commandLine.StartsWith("\""))
            {
                int end = commandLine.IndexOf('"', 1);
                if (end > 1)
                {
                    string file = commandLine.Substring(1, end - 1);
                    string args = commandLine.Substring(end + 1).Trim();
                    return (file, args);
                }
            }

            int firstSpace = commandLine.IndexOf(' ');
            if (firstSpace < 0) return (commandLine, string.Empty);
            return (commandLine.Substring(0, firstSpace), commandLine.Substring(firstSpace + 1).Trim());
        }

        private void Log(string message)
        {
            if (!AppConfig.Instance.EnableLogging) return;

            try
            {
                string logFile = AppConfig.Instance.GetDailyLogFile();
                File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}");
            }
            catch { }
        }

        private void FadeInWindow()
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)));
        }
    }
}