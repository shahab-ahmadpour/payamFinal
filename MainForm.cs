using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Linq;

namespace AutoClickUI
{
    public partial class MainForm : Form
    {
        // -------------------------
        // Win32 / Multimedia timing
        // -------------------------
        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uPeriod);

        // -------------------------
        // SendInput structs
        // -------------------------
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        private const uint INPUT_KEYBOARD = 1;
        private const ushort VK_F12 = 0x7B;
        private const ushort VK_TAB = 0x09;
        private const ushort VK_SPACE = 0x20;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        // -------------------------
        // Program state
        // -------------------------
        private string configFolder = @"\\irn-st10\payamconf\";
        private string logFolder = @"\\irn-st10\payamconf\logs\";
        private const string NTP_CONFIG_FILE = @"\\irn-st10\payamconf\ntp\ntp_config.txt";
        private static readonly string NTP_CONFIG_DIR = Path.GetDirectoryName(NTP_CONFIG_FILE);
        private string targetProcess = "Payam";
        private DateTime targetTime;

        private int clickCount = 1;
        private int clickInterval = 0;

        // NTP configuration
        private string ntpServer = "ntp.iranet.ir";

        // NTP time model: baseTime + elapsedStopwatch
        private volatile bool hasNtpSync = false;
        private DateTime ntpBaseTime;
        private Stopwatch ntpStopwatch = new Stopwatch();

        // Time source (Payam API is default)
        private TimeSourceMode timeSourceMode = TimeSourceMode.PayamApi;
        private PayamTimeConfig payamConfig = new PayamTimeConfig();
        private PayamTimeProvider payamTimeProvider;
        private ShareAuthConfig shareAuthConfig = new ShareAuthConfig();
        private volatile bool shareConnected = false;

        // threading
        private Thread liveTimeThread;
        private Thread waitingThread;
        private volatile bool stopLiveTime = false;
        private volatile bool isWaiting = false;
        private volatile bool hasStarted = false;

        // file watcher
        private FileSystemWatcher ntpWatcher;

        // countdown progress tracking
        private double waitTotalMs = 0;

        // -------------------------
        // UI Controls
        // -------------------------
        private Label lblLiveTime;
        private HeroClockLabel lblHeroClock;
        private Label lblHeroMeta;
        private Label lblCountdown;
        private Label lblTargetTime;
        private Label lblProcessStatus;
        private Label lblConfigStatus;
        private Label lblNtpStatus;
        private Label lblPayamStatus;
        private Label lblClickCount;
        private Label lblSyncDot;
        private Label lblBrand;

        private TextBox txtConfigFolder;
        private TextBox txtLogFolder;
        private TextBox txtTargetProcess;
        private TextBox txtNtpServer;
        private TextBox txtPayamApiUrl;
        private TextBox txtPayamYearCode;
        private TextBox txtPayamContentTypeOptions;
        private TextBox txtShareRoot;
        private TextBox txtShareDomain;
        private TextBox txtShareUsername;
        private TextBox txtSharePassword;

        private NumericUpDown nudMilliseconds;
        private NumericUpDown nudClickCount;
        private NumericUpDown nudClickInterval;
        private NumericUpDown nudSafetyMargin;
        private NumericUpDown nudClockBias;

        private DateTimePicker dtpTargetDate;
        private DateTimePicker dtpTargetTime;

        private Button btnSyncNtp;
        private Button btnSyncPayam;
        private Button btnSavePayamConfig;
        private Button btnSaveShareAuth;
        private Button btnConnectShare;
        private Button btnStart;
        private Button btnStop;
        private Button btnBrowseConfig;
        private Button btnBrowseLog;
        private Button btnManualConfig;
        private Button btnReadConfig;

        private Panel panelHeader;
        private Panel panelContentHost;
        private Panel panelMain;
        private Panel panelSettings;
        private Panel panelLogs;
        private Panel panelAdmin;
        private CardPanel panelHero;
        private CardPanel panelArm;
        private CardPanel panelStatusChips;
        private CardPanel panelSettingsFolders;
        private CardPanel panelSettingsPayam;
        private CardPanel panelSettingsShare;
        private ThinProgressBar progressCountdown;
        private CheckBox chkShareAuthEnabled;
        private Label lblShareAuthStatus;
        private Panel settingsStack;

        private NavButton btnNavConsole;
        private NavButton btnNavSettings;
        private NavButton btnNavAdmin;
        private NavButton btnNavLogs;

        private RichTextBox rtbLogs;
        private ComboBox cmbTimeSource;

        // Admin / Config Distributor
        private DateTimePicker dtpDistDate;
        private DateTimePicker dtpDistBaseTime;
        private NumericUpDown nudDistBaseMs;
        private NumericUpDown nudDistEndMs;
        private NumericUpDown nudDistClickCount;
        private NumericUpDown nudDistClickInterval;
        private TextBox txtDistProcess;
        private TextBox txtDistMachineInput;
        private ListBox lstDistMachines;
        private ListView lvDistPreview;
        private Label lblDistSummary;
        private CheckBox chkDistCleanupOrphans;
        private CheckBox chkDistSaveMachinesFile;
        private Button btnDistScan;
        private Button btnDistLoadList;
        private Button btnDistSaveList;
        private Button btnDistAddMachine;
        private Button btnDistRemoveMachine;
        private Button btnDistPreview;
        private Button btnDistGenerate;
        private DistributePlan lastDistPlan;

        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusLabel;

        // -------------------------
        // Constructor / lifecycle
        // -------------------------
        public MainForm()
        {
            // Better timer resolution (improves Thread.Sleep scheduling)
            timeBeginPeriod(1);

            InitializeComponent();

            // 0) Authenticate to UNC config share (critical when running as Administrator)
            shareAuthConfig = ShareAuthConfig.Load();
            ApplyShareAuthToUi();
            EnsureShareConnected(logResult: true);

            // 1) Payam API time config (default time source)
            payamConfig = PayamTimeConfig.Load();
            ApplyPayamConfigToUi();
            EnsurePayamProviderStarted();

            // 2) Always load NTP server from ntp_config.txt (authoritative for NTP mode)
            LoadNtpServerFromFile(overwriteTextbox: true);

            // 3) Start watcher so any manual edit of ntp_config.txt takes effect automatically
            SetupNtpConfigWatcher();

            // 4) Auto-start sync for the selected mode
            ThreadPool.QueueUserWorkItem(_ => AutoSyncSelectedTimeSource());
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopAllOperations();
            DisposeWatcher();
            DisposePayamProvider();

            // restore timer resolution
            timeEndPeriod(1);
        }

        private void StopAllOperations()
        {
            try
            {
                isWaiting = false;
                stopLiveTime = true;

                if (waitingThread != null && waitingThread.IsAlive)
                    waitingThread.Join(1000);

                if (liveTimeThread != null && liveTimeThread.IsAlive)
                    liveTimeThread.Join(1000);
            }
            catch { /* best effort */ }
        }

        // -------------------------
        // Time model
        // -------------------------
        private DateTime GetCurrentTime()
        {
            switch (timeSourceMode)
            {
                case TimeSourceMode.System:
                    return DateTime.Now;

                case TimeSourceMode.Ntp:
                    if (!hasNtpSync)
                        return DateTime.Now;
                    return ntpBaseTime.AddMilliseconds(ntpStopwatch.Elapsed.TotalMilliseconds);

                case TimeSourceMode.PayamApi:
                default:
                    if (payamTimeProvider != null && payamTimeProvider.HasSync)
                        return payamTimeProvider.GetCurrentTime();
                    return DateTime.Now;
            }
        }

        private DateTime GetFireThreshold()
        {
            if (timeSourceMode == TimeSourceMode.PayamApi && payamTimeProvider != null)
                return payamTimeProvider.GetFireThreshold(targetTime);

            return targetTime;
        }

        private string GetTimeSourceLabel()
        {
            switch (timeSourceMode)
            {
                case TimeSourceMode.System:
                    return "(System)";
                case TimeSourceMode.Ntp:
                    return hasNtpSync ? $"(NTP: {ntpServer})" : "(System/NTP pending)";
                case TimeSourceMode.PayamApi:
                default:
                    if (payamTimeProvider != null && payamTimeProvider.HasPhaseLock)
                        return "(Payam API)";
                    if (payamTimeProvider != null && payamTimeProvider.HasSync)
                        return "(Payam provisional)";
                    return "(System/Payam pending)";
            }
        }

        private void ApplyNtpSync(DateTime ntpTimeLocal)
        {
            ntpBaseTime = ntpTimeLocal;
            ntpStopwatch.Restart();
            hasNtpSync = true;

            LogMessage($"NTP synced. Base NTP time: {ntpBaseTime:yyyy/MM/dd HH:mm:ss.fff}", Color.Green);

            UI(() =>
            {
                lblNtpStatus.Text = $"NTP Status: Synced with {ntpServer}";
                lblNtpStatus.ForeColor = AppTheme.Success;
                txtNtpServer.Text = ntpServer;
            });
        }

        private void EnsurePayamProviderStarted()
        {
            if (payamTimeProvider == null)
            {
                payamTimeProvider = new PayamTimeProvider(payamConfig, (msg, isError) =>
                    LogMessage(msg, isError ? Color.Orange : Color.Blue));
                payamTimeProvider.Start();
            }
            else
            {
                payamTimeProvider.UpdateConfig(payamConfig);
            }
        }

        private void DisposePayamProvider()
        {
            try
            {
                if (payamTimeProvider != null)
                {
                    payamTimeProvider.Dispose();
                    payamTimeProvider = null;
                }
            }
            catch { }
        }

        private void AutoSyncSelectedTimeSource()
        {
            if (timeSourceMode == TimeSourceMode.PayamApi)
            {
                LogMessage("Auto Payam API time sync on startup...", Color.Blue);
                EnsurePayamProviderStarted();
            }
            else if (timeSourceMode == TimeSourceMode.Ntp)
            {
                LogMessage("Auto NTP sync on startup...", Color.Blue);
                SyncWithNtpServer();
            }
            else
            {
                LogMessage("Using system time on startup.", Color.Orange);
            }
        }

        private void ApplyPayamConfigToUi()
        {
            if (IsHandleCreated && InvokeRequired)
                UI(ApplyPayamConfigToUiCore);
            else
                ApplyPayamConfigToUiCore();
        }

        private void ApplyPayamConfigToUiCore()
        {
            if (txtPayamApiUrl != null) txtPayamApiUrl.Text = payamConfig.ApiUrl ?? PayamTimeConfig.DefaultApiUrl;
            if (txtPayamYearCode != null) txtPayamYearCode.Text = payamConfig.YearCode ?? PayamTimeConfig.DefaultYearCode;
            if (txtPayamContentTypeOptions != null)
                txtPayamContentTypeOptions.Text = payamConfig.ContentTypeOptions ?? PayamTimeConfig.DefaultContentTypeOptions;
            if (nudSafetyMargin != null)
            {
                int margin = Math.Max(0, Math.Min(500, payamConfig.SafetyMarginMs));
                nudSafetyMargin.Value = margin;
            }
            if (nudClockBias != null)
            {
                int bias = Math.Max(0, Math.Min(300, payamConfig.ClockBiasMs));
                nudClockBias.Value = bias;
            }
        }

        private bool TryReadPayamConfigFromUi(out string error)
        {
            error = null;
            string apiUrl = SafeGetText(txtPayamApiUrl);
            string yearCode = SafeGetText(txtPayamYearCode);
            string token = SafeGetText(txtPayamContentTypeOptions);

            if (string.IsNullOrWhiteSpace(apiUrl))
            {
                error = "Payam API URL is empty.";
                return false;
            }

            Uri uri;
            if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out uri)
                || !string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase))
            {
                error = "Payam API URL must be an absolute http:// address.";
                return false;
            }

            payamConfig.ApiUrl = apiUrl;
            payamConfig.YearCode = yearCode ?? string.Empty;
            payamConfig.ContentTypeOptions = token ?? string.Empty;
            payamConfig.SafetyMarginMs = (int)nudSafetyMargin.Value;
            if (nudClockBias != null)
                payamConfig.ClockBiasMs = (int)nudClockBias.Value;
            return true;
        }

        private void SavePayamConfigFromUi()
        {
            string error;
            if (!TryReadPayamConfigFromUi(out error))
            {
                LogMessage(error, Color.Red);
                return;
            }

            try
            {
                payamConfig.Save();
                EnsurePayamProviderStarted();
                LogMessage($"Payam time config saved: {PayamTimeConfig.DefaultConfigPath}", Color.Green);
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to save Payam time config: {ex.Message}", Color.Red);
            }
        }

        /// <summary>Push Bias/Margin from UI into the live provider immediately.</summary>
        private void ApplyLivePayamTimingFromUi()
        {
            if (payamConfig == null) return;
            if (nudSafetyMargin != null)
                payamConfig.SafetyMarginMs = (int)nudSafetyMargin.Value;
            if (nudClockBias != null)
                payamConfig.ClockBiasMs = (int)nudClockBias.Value;
            if (payamTimeProvider != null)
                payamTimeProvider.UpdateConfig(payamConfig);
        }

        private void SetTimeSourceMode(TimeSourceMode mode, bool syncNow)
        {
            timeSourceMode = mode;
            UpdateTimeSourceUiEnabled();

            if (mode == TimeSourceMode.PayamApi)
            {
                LogMessage("Time source: Payam API Time.", Color.Blue);
                EnsurePayamProviderStarted();
                if (syncNow)
                    LogMessage("Payam sync is continuous (phase-lock on second edges).", Color.Blue);
            }
            else if (mode == TimeSourceMode.Ntp)
            {
                LogMessage("Time source: NTP.", Color.Blue);
                if (syncNow)
                    ThreadPool.QueueUserWorkItem(_ => SyncWithNtpServer());
            }
            else
            {
                LogMessage("Time source: System time.", Color.Orange);
                UI(() =>
                {
                    lblNtpStatus.Text = "NTP Status: Disabled (System time)";
                    lblNtpStatus.ForeColor = AppTheme.Warning;
                });
            }
        }

        private void UpdateTimeSourceUiEnabled()
        {
            if (IsHandleCreated && InvokeRequired)
                UI(UpdateTimeSourceUiEnabledCore);
            else
                UpdateTimeSourceUiEnabledCore();
        }

        private void UpdateTimeSourceUiEnabledCore()
        {
            bool ntp = timeSourceMode == TimeSourceMode.Ntp;
            bool payam = timeSourceMode == TimeSourceMode.PayamApi;

            if (txtNtpServer != null) txtNtpServer.Enabled = ntp;
            if (btnSyncNtp != null) btnSyncNtp.Enabled = ntp;

            if (txtPayamApiUrl != null) txtPayamApiUrl.Enabled = payam;
            if (txtPayamYearCode != null) txtPayamYearCode.Enabled = payam;
            if (txtPayamContentTypeOptions != null) txtPayamContentTypeOptions.Enabled = payam;
            if (nudSafetyMargin != null) nudSafetyMargin.Enabled = payam;
            if (nudClockBias != null) nudClockBias.Enabled = payam;
            if (btnSyncPayam != null) btnSyncPayam.Enabled = payam;
            if (btnSavePayamConfig != null) btnSavePayamConfig.Enabled = payam;
        }

        // -------------------------
        // Config: NTP file is authoritative
        // -------------------------
        private string NtpConfigPath => NTP_CONFIG_FILE;

        private void SetupNtpConfigWatcher()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(NTP_CONFIG_DIR) || !Directory.Exists(NTP_CONFIG_DIR))
                {
                    LogMessage($"NTP watcher not started. Directory not accessible: {NTP_CONFIG_DIR}", Color.Orange);
                    return;
                }

                ntpWatcher = new FileSystemWatcher(NTP_CONFIG_DIR, Path.GetFileName(NTP_CONFIG_FILE));
                ntpWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
                ntpWatcher.Changed += (_, __) => OnNtpConfigFileChanged();
                ntpWatcher.Created += (_, __) => OnNtpConfigFileChanged();
                ntpWatcher.Renamed += (_, __) => OnNtpConfigFileChanged();
                ntpWatcher.EnableRaisingEvents = true;

                LogMessage($"NTP config watcher started: {NTP_CONFIG_FILE}", Color.Blue);
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to start NTP config watcher: {ex.Message}", Color.Orange);
            }
        }


        private void DisposeWatcher()
        {
            try
            {
                if (ntpWatcher != null)
                {
                    ntpWatcher.EnableRaisingEvents = false;
                    ntpWatcher.Dispose();
                    ntpWatcher = null;
                }
            }
            catch { }
        }

        // Debounce to avoid multiple events
        private int _ntpReloadGate = 0;

        private void OnNtpConfigFileChanged()
        {
            if (Interlocked.Exchange(ref _ntpReloadGate, 1) == 1)
                return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // small delay so editor flushes file
                    Thread.Sleep(150);

                    var oldServer = ntpServer;
                    var loaded = LoadNtpServerFromFile(overwriteTextbox: true);

                    if (loaded && !string.Equals(oldServer, ntpServer, StringComparison.OrdinalIgnoreCase))
                    {
                        LogMessage($"NTP server changed via file: {oldServer} -> {ntpServer}", Color.Green);

                        // Re-sync automatically if NTP mode is active
                        if (timeSourceMode == TimeSourceMode.Ntp)
                        {
                            SyncWithNtpServer();
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _ntpReloadGate, 0);
                }
            });
        }

        private bool LoadNtpServerFromFile(bool overwriteTextbox)
        {
            try
            {
                // keep configFolder aligned with UI
                var uiPath = SafeGetText(txtConfigFolder);
                if (!string.IsNullOrWhiteSpace(uiPath))
                    configFolder = uiPath;

                if (string.IsNullOrWhiteSpace(configFolder))
                    return false;

                if (!IsDirectoryAccessible(configFolder))
                {
                    LogMessage("NTP config load skipped: config folder not accessible.", Color.Orange);
                    return false;
                }

                var path = NtpConfigPath;
                if (!File.Exists(path))
                {
                    LogMessage($"NTP config not found: {path}", Color.Orange);
                    return false;
                }

                string serverFromFile = null;

                foreach (var line in File.ReadAllLines(path))
                {
                    var t = (line ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#"))
                        continue;

                    if (t.StartsWith("NTPServer=", StringComparison.OrdinalIgnoreCase))
                    {
                        serverFromFile = t.Substring("NTPServer=".Length).Trim();
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(serverFromFile))
                {
                    LogMessage("NTP config file present but no NTPServer= line found.", Color.Orange);
                    return false;
                }

                if (!IsValidNtpServerAddress(serverFromFile))
                {
                    LogMessage($"Invalid NTP server in file: {serverFromFile}", Color.Red);
                    return false;
                }

                ntpServer = serverFromFile;

                if (overwriteTextbox)
                {
                    UI(() => { txtNtpServer.Text = ntpServer; });
                }

                LogMessage($"NTP server loaded from file: {ntpServer}", Color.Blue);
                return true;
            }
            catch (Exception ex)
            {
                LogMessage($"Error loading ntp_config.txt: {ex.Message}", Color.Red);
                return false;
            }
        }

        private void SaveNtpServerToFile(string server)
        {
            try
            {
                EnsureShareConnected(logResult: false);

                if (string.IsNullOrWhiteSpace(NTP_CONFIG_DIR) || !Directory.Exists(NTP_CONFIG_DIR))
                {
                    LogMessage($"Cannot save NTP config. Directory not accessible: {NTP_CONFIG_DIR}", Color.Red);
                    return;
                }

                File.WriteAllText(NTP_CONFIG_FILE, $"NTPServer={server}{Environment.NewLine}", Encoding.UTF8);
                LogMessage($"NTP config saved: {NTP_CONFIG_FILE}", Color.Green);
            }
            catch (Exception ex)
            {
                LogMessage($"Error saving ntp_config.txt: {ex.Message}", Color.Red);
            }
        }


        // -------------------------
        // NTP
        // -------------------------
        private void SyncWithNtpServer()
        {
            try
            {
                if (timeSourceMode != TimeSourceMode.Ntp)
                {
                    LogMessage("Skipping NTP sync because NTP mode is not selected.", Color.Orange);
                    UI(() =>
                    {
                        lblNtpStatus.Text = timeSourceMode == TimeSourceMode.System
                            ? "NTP Status: Disabled (System time)"
                            : "NTP Status: Idle (Payam mode)";
                        lblNtpStatus.ForeColor = AppTheme.Warning;
                    });
                    return;
                }

                // IMPORTANT: Always re-load authoritative server from file before syncing
                // so ntp_config.txt always wins.
                LoadNtpServerFromFile(overwriteTextbox: true);

                if (!IsValidNtpServerAddress(ntpServer))
                {
                    LogMessage($"Invalid NTP server address: {ntpServer}", Color.Red);
                    UI(() =>
                    {
                        lblNtpStatus.Text = "NTP Status: Invalid server address";
                        lblNtpStatus.ForeColor = AppTheme.Danger;
                    });
                    return;
                }

                UI(() =>
                {
                    lblNtpStatus.Text = $"NTP Status: Syncing with {ntpServer} ...";
                    lblNtpStatus.ForeColor = AppTheme.LogInfo;
                });

                var ntpTime = GetNtpTimeWithRetry(ntpServer, maxAttempts: 3);
                ApplyNtpSync(ntpTime);
            }
            catch (Exception ex)
            {
                LogMessage($"Error in SyncWithNtpServer: {ex.Message}", Color.Red);
                UI(() =>
                {
                    lblNtpStatus.Text = "NTP Status: Error";
                    lblNtpStatus.ForeColor = Color.Red;
                });
            }
        }

        private DateTime GetNtpTimeWithRetry(string server, int maxAttempts)
        {
            Exception last = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var t = GetNtpTime(server);
                    LogMessage($"NTP time received from {server} (attempt {attempt}/{maxAttempts})", Color.Green);
                    return t;
                }
                catch (Exception ex)
                {
                    last = ex;
                    LogMessage($"NTP attempt {attempt}/{maxAttempts} failed: {ex.Message}", Color.Orange);
                    Thread.Sleep(500);
                }
            }

            // fallback policy:
            // - if we have previous sync, keep it (do not jump to system time silently)
            // - else use system time reading for this call only
            if (hasNtpSync)
            {
                LogMessage("NTP unavailable; keeping last NTP base time.", Color.Orange);
                return ntpBaseTime.AddMilliseconds(ntpStopwatch.Elapsed.TotalMilliseconds);
            }

            LogMessage("NTP unavailable; falling back to system time reading.", Color.Red);
            return DateTime.Now;
        }

        private DateTime GetNtpTime(string server)
        {
            LogMessage($"Connecting to NTP server: {server}...");

            using (var client = new UdpClient())
            {
                client.Connect(server, 123);
                client.Client.ReceiveTimeout = 3000;
                client.Client.SendTimeout = 3000;

                byte[] ntpData = new byte[48];
                ntpData[0] = 0x1B;

                client.Send(ntpData, ntpData.Length);

                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                ntpData = client.Receive(ref remoteEndPoint);

                ulong intPart = BitConverter.ToUInt32(ntpData, 40);
                ulong fractPart = BitConverter.ToUInt32(ntpData, 44);
                intPart = SwapEndianness(intPart);
                fractPart = SwapEndianness(fractPart);

                var milliseconds = (intPart * 1000) + ((fractPart * 1000) / 0x100000000L);

                var networkDateTimeUtc = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddMilliseconds((long)milliseconds);

                // Convert to Iran Standard Time (as in your code)
                TimeZoneInfo iranTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(networkDateTimeUtc, iranTimeZone);
            }
        }

        private static uint SwapEndianness(ulong x)
        {
            return (uint)(((x & 0x000000ff) << 24) +
                          ((x & 0x0000ff00) << 8) +
                          ((x & 0x00ff0000) >> 8) +
                          ((x & 0xff000000) >> 24));
        }

        private bool IsValidNtpServerAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return false;

            address = address.Trim();

            // Valid IP?
            if (IPAddress.TryParse(address, out _))
                return true;

            // Basic hostname check
            if (address.Contains(" ")) return false;
            if (!address.Contains(".")) return false;

            // allow common hostnames
            return address.Length >= 3;
        }

        // -------------------------
        // Precise waiting
        // -------------------------
        private void WaitForTargetTime()
        {
            LogMessage("Starting wait for target time...");

            try
            {
                // Try to foreground target process
                var processes = Process.GetProcessesByName(targetProcess);
                if (processes.Length > 0)
                {
                    SetForegroundWindow(processes[0].MainWindowHandle);
                    LogMessage($"Set {targetProcess} as foreground window");
                }
                else
                {
                    LogMessage($"Warning: Process {targetProcess} is not running", Color.Orange);
                }

                while (isWaiting)
                {
                    var now = GetCurrentTime();
                    // Payam mode: never fire early — wait until target + positive safety margin.
                    var fireAt = GetFireThreshold();
                    double remainingMs = (fireAt - now).TotalMilliseconds;

                    // Near the target second, also accept the exact API NowTime edge
                    // so F12 aligns with the second Payam actually exchanges.
                    if (timeSourceMode == TimeSourceMode.PayamApi
                        && payamTimeProvider != null
                        && remainingMs <= 1200)
                    {
                        DateTime apiSecond;
                        string apiText;
                        if (payamTimeProvider.TryGetApiSecondTime(out apiSecond, out apiText))
                        {
                            // Target second reached on API → wait only the ms part + safety margin.
                            var targetSecond = new DateTime(
                                fireAt.Year, fireAt.Month, fireAt.Day,
                                fireAt.Hour, fireAt.Minute, fireAt.Second, 0, fireAt.Kind);
                            if (apiSecond >= targetSecond)
                            {
                                // fireAt may include ms + safety margin beyond the whole second.
                                double afterSecondMs = (fireAt - targetSecond).TotalMilliseconds;
                                if (afterSecondMs > 0)
                                    PreciseDelayMs(afterSecondMs);
                                PressF12Multiple();
                                break;
                            }
                        }
                    }

                    if (remainingMs <= 0)
                    {
                        PressF12Multiple();
                        break;
                    }

                    // coarse sleep then fine spin
                    if (remainingMs > 25)
                    {
                        int sleepMs = (int)Math.Min(10, Math.Max(1, remainingMs - 15));
                        Thread.Sleep(sleepMs);
                    }
                    else
                    {
                        // last ~25ms: spin for accuracy
                        Thread.SpinWait(800);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error in wait thread: {ex.Message}", Color.Red);
            }
            finally
            {
                UI(() =>
                {
                    btnStart.Enabled = true;
                    btnStop.Enabled = false;
                    btnReadConfig.Enabled = true;
                    btnManualConfig.Enabled = true;

                    hasStarted = false;
                    isWaiting = false;
                    statusLabel.Text = "Ready";
                });
            }
        }

        // High precision wait for intervals (for key pressing sequence)
        private void PreciseDelayMs(double ms)
        {
            if (ms <= 0) return;

            var sw = Stopwatch.StartNew();

            // Sleep most of it, spin last few ms
            if (ms > 6)
            {
                int sleepMs = (int)(ms - 3);
                if (sleepMs > 0) Thread.Sleep(sleepMs);
            }

            while (sw.Elapsed.TotalMilliseconds < ms)
            {
                Thread.SpinWait(200);
            }
        }

        // -------------------------
        // Key pressing logic
        // -------------------------
        private void PressF12Multiple()
        {
            List<DateTime> pressTimes = new List<DateTime>();
            List<string> pressKeys = new List<string>();

            var start = GetCurrentTime();

            int totalPresses = (clickCount <= 1) ? 1 : (3 * clickCount - 2);
            LogMessage($"Target time reached! Performing {totalPresses} key presses for {clickCount} F12 presses...");

            double delayBetween = 0;

            if (clickCount > 1 && clickInterval > 0)
            {
                delayBetween = (double)clickInterval / (totalPresses - 1);
                LogMessage($"Click interval: {clickInterval} ms, per-press delay: {delayBetween:F3} ms");
            }
            else if (clickCount > 1)
            {
                delayBetween = 50;
                LogMessage($"No click interval specified, default per-press delay: {delayBetween:F3} ms");
            }

            for (int i = 0; i < totalPresses; i++)
            {
                // decide key
                ushort keyCode;
                string keyName;

                if (i == 0 || (i % 3 == 0 && (i / 3) < clickCount))
                {
                    keyCode = VK_F12;
                    keyName = "F12";
                }
                else if (i % 3 == 1)
                {
                    keyCode = VK_TAB;
                    keyName = "Tab";
                }
                else
                {
                    keyCode = VK_SPACE;
                    keyName = "Space";
                }

                // time stamp (use same time model as program)
                var pressTime = GetCurrentTime();
                pressTimes.Add(pressTime);
                pressKeys.Add(keyName);

                // send key down + up
                INPUT[] keyInputs = new INPUT[2];

                keyInputs[0].type = INPUT_KEYBOARD;
                keyInputs[0].u.ki = new KEYBDINPUT
                {
                    wVk = keyCode,
                    wScan = (ushort)MapVirtualKey(keyCode, 0),
                    dwFlags = 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                };

                keyInputs[1].type = INPUT_KEYBOARD;
                keyInputs[1].u.ki = new KEYBDINPUT
                {
                    wVk = keyCode,
                    wScan = (ushort)MapVirtualKey(keyCode, 0),
                    dwFlags = KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                };

                SendInput(2, keyInputs, Marshal.SizeOf(typeof(INPUT)));

                // very short settle
                PreciseDelayMs(1);

                LogMessage($"Pressed key: {keyName} at {pressTime:yyyy/MM/dd HH:mm:ss.fff}");

                if (i < totalPresses - 1 && delayBetween > 0)
                {
                    PreciseDelayMs(delayBetween);
                }
            }

            var end = GetCurrentTime();
            var totalDuration = (end - start).TotalMilliseconds;

            var firstDelay = (pressTimes[0] - targetTime).TotalMilliseconds;
            LogMessage($"First key press ({pressKeys[0]}) Delay from Target: {firstDelay:F3} ms", Color.Blue);

            if (totalPresses > 1)
            {
                for (int i = 1; i < pressTimes.Count; i++)
                {
                    var interval = (pressTimes[i] - pressTimes[i - 1]).TotalMilliseconds;
                    LogMessage($"Interval {i} ({pressKeys[i - 1]} -> {pressKeys[i]}): {interval:F3} ms");
                }
            }

            var cps = totalPresses / (Math.Max(1, totalDuration) / 1000.0);
            LogMessage($"Total duration: {totalDuration:F3} ms | Speed: {cps:F2} CPS");

            LogClick(pressTimes, totalDuration, cps, pressKeys);

            UI(() =>
            {
                DialogResult result = MessageBox.Show(
                    $"Operation completed successfully with {totalPresses} key presses ({clickCount} F12 presses). Log has been saved.\nClick OK to exit the program.",
                    "Operation Completed",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Information);

                if (result == DialogResult.OK)
                {
                    StopAllOperations();
                    Application.Exit();
                }
                else
                {
                    btnStart.Enabled = true;
                    btnStop.Enabled = false;
                    btnReadConfig.Enabled = true;
                    btnManualConfig.Enabled = true;
                    hasStarted = false;
                    isWaiting = false;
                    statusLabel.Text = "Ready";
                }
            });
        }

        // -------------------------
        // Logging
        // -------------------------
        private void LogClick(List<DateTime> clickTimes, double totalDuration, double cps, List<string> clickedKeys)
        {
            try
            {
                logFolder = SafeGetText(txtLogFolder) ?? logFolder;

                if (string.IsNullOrWhiteSpace(logFolder))
                    logFolder = Application.StartupPath;

                if (!Directory.Exists(logFolder))
                {
                    try { Directory.CreateDirectory(logFolder); }
                    catch
                    {
                        logFolder = Application.StartupPath;
                    }
                }

                string machineName = Environment.MachineName;
                string timeStamp = clickTimes[0].ToString("MM.dd-HH.mm");
                string logFileName = $"{machineName}_log{timeStamp}.txt";
                string logPath = Path.Combine(logFolder, logFileName);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"ComputerName: {machineName}");
                sb.AppendLine($"Target Time: {targetTime:yyyy/MM/dd HH:mm:ss.fff}");
                sb.AppendLine($"Time Source: {DescribeTimeSourceForLog()}");
                if (timeSourceMode == TimeSourceMode.PayamApi)
                {
                    sb.AppendLine($"Payam Safety Margin: {payamConfig.SafetyMarginMs} ms");
                    sb.AppendLine($"Payam Clock Bias: {payamConfig.ClockBiasMs} ms");
                }
                sb.AppendLine($"Click Interval: {clickInterval} ms");
                sb.AppendLine($"Key Pattern: F12, Tab, Space between each F12");
                sb.AppendLine($"Total Duration for {clickTimes.Count} key presses: {totalDuration:F3} ms");
                sb.AppendLine($"Click Speed: {cps:F2} CPS");

                for (int i = 0; i < clickTimes.Count; i++)
                {
                    sb.Append($"Key press {i + 1} ({clickedKeys[i]}): {clickTimes[i]:yyyy/MM/dd HH:mm:ss.fff}");

                    if (i == 0)
                    {
                        var delay = (clickTimes[0] - targetTime).TotalMilliseconds;
                        sb.Append($", Delay from Target: {delay:F3} ms");
                    }
                    else
                    {
                        var interval = (clickTimes[i] - clickTimes[i - 1]).TotalMilliseconds;
                        sb.Append($", Interval from previous: {interval:F3} ms");
                    }

                    sb.AppendLine();
                }

                File.WriteAllText(logPath, sb.ToString(), Encoding.UTF8);
                LogMessage($"Log saved to {logPath}", Color.Green);
            }
            catch (Exception ex)
            {
                LogMessage($"Error saving log: {ex.Message}", Color.Red);
            }
        }

        private void LogMessage(string message, Color? color = null)
        {
            try
            {
                if (!this.IsHandleCreated) return;

                // Use BeginInvoke to avoid blocking worker threads (less timing distortion)
                this.BeginInvoke(new Action(() =>
                {
                    string timestamped = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
                    rtbLogs.SelectionStart = rtbLogs.TextLength;
                    rtbLogs.SelectionLength = 0;
                    rtbLogs.SelectionColor = AppTheme.MapLogColor(color);
                    rtbLogs.AppendText(timestamped + Environment.NewLine);
                    rtbLogs.SelectionStart = rtbLogs.Text.Length;
                    rtbLogs.ScrollToCaret();
                }));
            }
            catch
            {
                // ignore close-time issues
            }
        }

        private void UI(Action action)
        {
            try
            {
                if (!IsHandleCreated) return;
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch { }
        }

        private static string SafeGetText(TextBox tb)
        {
            try { return tb?.Text?.Trim(); } catch { return null; }
        }

        // -------------------------
        // Network share authentication
        // -------------------------
        private bool EnsureShareConnected(bool logResult)
        {
            try
            {
                if (shareAuthConfig == null)
                    shareAuthConfig = ShareAuthConfig.Load();

                // Prefer live config-folder UNC if present.
                string folder = SafeGetText(txtConfigFolder) ?? configFolder;
                string message;
                bool ok = NetworkShareAuth.EnsureConnectedForPath(shareAuthConfig, folder, out message);
                shareConnected = ok;

                if (logResult)
                {
                    if (ok)
                        LogMessage(message ?? "Share connected.", Color.Green);
                    else
                        LogMessage(message ?? "Share connect failed.", Color.Orange);
                }

                UI(UpdateShareAuthStatusLabel);
                return ok;
            }
            catch (Exception ex)
            {
                shareConnected = false;
                if (logResult)
                    LogMessage("Share connect error: " + ex.Message, Color.Red);
                UI(UpdateShareAuthStatusLabel);
                return false;
            }
        }

        private void UpdateShareAuthStatusLabel()
        {
            if (lblShareAuthStatus == null) return;
            if (shareAuthConfig == null || !shareAuthConfig.Enabled)
            {
                lblShareAuthStatus.Text = "Share auth  ·  disabled";
                lblShareAuthStatus.ForeColor = AppTheme.TextMuted;
            }
            else if (shareConnected)
            {
                lblShareAuthStatus.Text = "Share auth  ·  connected as " + shareAuthConfig.EffectiveUserName;
                lblShareAuthStatus.ForeColor = AppTheme.Success;
            }
            else if (string.IsNullOrWhiteSpace(shareAuthConfig.Username))
            {
                lblShareAuthStatus.Text = "Share auth  ·  set username/password";
                lblShareAuthStatus.ForeColor = AppTheme.Warning;
            }
            else
            {
                lblShareAuthStatus.Text = "Share auth  ·  not connected";
                lblShareAuthStatus.ForeColor = AppTheme.Danger;
            }
        }

        private void ApplyShareAuthToUi()
        {
            Action apply = () =>
            {
                if (shareAuthConfig == null) return;
                if (chkShareAuthEnabled != null) chkShareAuthEnabled.Checked = shareAuthConfig.Enabled;
                if (txtShareRoot != null)
                    txtShareRoot.Text = string.IsNullOrWhiteSpace(shareAuthConfig.ShareRoot)
                        ? ShareAuthConfig.DefaultShareRoot
                        : shareAuthConfig.ShareRoot;
                if (txtShareDomain != null) txtShareDomain.Text = shareAuthConfig.Domain ?? string.Empty;
                if (txtShareUsername != null) txtShareUsername.Text = shareAuthConfig.Username ?? string.Empty;
                if (txtSharePassword != null) txtSharePassword.Text = shareAuthConfig.Password ?? string.Empty;
                UpdateShareAuthStatusLabel();
            };

            if (IsHandleCreated && InvokeRequired) UI(apply);
            else apply();
        }

        private bool TryReadShareAuthFromUi(out string error)
        {
            error = null;
            if (shareAuthConfig == null) shareAuthConfig = new ShareAuthConfig();

            shareAuthConfig.Enabled = chkShareAuthEnabled == null || chkShareAuthEnabled.Checked;
            shareAuthConfig.ShareRoot = SafeGetText(txtShareRoot) ?? ShareAuthConfig.DefaultShareRoot;
            shareAuthConfig.Domain = SafeGetText(txtShareDomain) ?? string.Empty;
            shareAuthConfig.Username = SafeGetText(txtShareUsername) ?? string.Empty;
            shareAuthConfig.Password = txtSharePassword != null ? (txtSharePassword.Text ?? string.Empty) : string.Empty;

            if (shareAuthConfig.Enabled && string.IsNullOrWhiteSpace(shareAuthConfig.Username))
            {
                error = "Share username is required when auth is enabled.";
                return false;
            }

            if (shareAuthConfig.Enabled)
            {
                string root = NetworkShareAuth.NormalizeShareRoot(shareAuthConfig.ShareRoot);
                if (!root.StartsWith(@"\\", StringComparison.Ordinal))
                {
                    error = "Share root must be a UNC path like \\\\irn-st10\\payamconf.";
                    return false;
                }
                shareAuthConfig.ShareRoot = root;
            }

            return true;
        }

        private void SaveShareAuthFromUi(bool connectAfterSave)
        {
            string error;
            if (!TryReadShareAuthFromUi(out error))
            {
                LogMessage(error, Color.Red);
                MessageBox.Show(error, "Share Auth", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                shareAuthConfig.Save();
                LogMessage("Share auth config saved: " + ShareAuthConfig.DefaultConfigPath, Color.Green);
                if (connectAfterSave)
                    EnsureShareConnected(logResult: true);
                else
                    UI(UpdateShareAuthStatusLabel);
            }
            catch (Exception ex)
            {
                LogMessage("Failed to save share auth config: " + ex.Message, Color.Red);
            }
        }

        // -------------------------
        // Process / directory helpers
        // -------------------------
        private bool IsProcessRunning(string processName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(processName)) return false;
                return Process.GetProcessesByName(processName).Length > 0;
            }
            catch { return false; }
        }

        // FIXED: do not require subdirectories; just check we can enumerate at least one entry
        private bool IsDirectoryAccessible(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return false;

                // Authenticate UNC before probing (elevated admin sessions need this).
                if (path.StartsWith(@"\\", StringComparison.Ordinal))
                    EnsureShareConnected(logResult: false);

                var task = Task.Run(() =>
                {
                    try
                    {
                        if (!Directory.Exists(path)) return false;
                        Directory.EnumerateFileSystemEntries(path).Take(1).ToList();
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                });

                bool ok = task.Wait(TimeSpan.FromSeconds(3)) && task.Result;
                if (!ok && path.StartsWith(@"\\", StringComparison.Ordinal))
                {
                    // One retry after forced reconnect.
                    EnsureShareConnected(logResult: true);
                    var retry = Task.Run(() =>
                    {
                        try
                        {
                            if (!Directory.Exists(path)) return false;
                            Directory.EnumerateFileSystemEntries(path).Take(1).ToList();
                            return true;
                        }
                        catch { return false; }
                    });
                    ok = retry.Wait(TimeSpan.FromSeconds(3)) && retry.Result;
                }
                return ok;
            }
            catch
            {
                return false;
            }
        }

        // -------------------------
        // UI initialization
        // -------------------------
        private void InitializeComponent()
        {
            this.Text = "Payam AutoClick";
            this.Icon = PayamAutoClick.Properties.Resources.Icon1;
            this.ClientSize = new Size(960, 780);
            this.MinimumSize = new Size(920, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.FormClosing += MainForm_FormClosing;
            AppTheme.StyleForm(this);

            // Header (must stay outside content host so Dock never covers it)
            panelHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
                BackColor = AppTheme.Surface,
                Padding = new Padding(16, 0, 16, 0)
            };
            panelHeader.Paint += (s, e) =>
            {
                using (var pen = new Pen(AppTheme.Border))
                    e.Graphics.DrawLine(pen, 0, panelHeader.Height - 1, panelHeader.Width, panelHeader.Height - 1);
            };

            var headerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = AppTheme.Surface
            };
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 420F));

            var brandPanel = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface };
            lblBrand = new Label
            {
                Text = "PAYAM AUTOCLICK",
                Location = new Point(0, 10),
                Size = new Size(320, 22),
                Font = AppTheme.BrandFont,
                ForeColor = AppTheme.TextPrimary,
                BackColor = AppTheme.Surface
            };
            var lblSubtitle = new Label
            {
                Text = "Clean console · config distributor · v2.7",
                Location = new Point(2, 34),
                Size = new Size(360, 16),
                Font = AppTheme.CaptionFont,
                ForeColor = AppTheme.TextMuted,
                BackColor = AppTheme.Surface
            };
            brandPanel.Controls.Add(lblBrand);
            brandPanel.Controls.Add(lblSubtitle);

            var navPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = AppTheme.Surface,
                Padding = new Padding(0, 12, 0, 0)
            };
            btnNavConsole = new NavButton { Text = "Console", Size = new Size(88, 32), Active = true, Margin = new Padding(2, 0, 2, 0) };
            btnNavSettings = new NavButton { Text = "Settings", Size = new Size(88, 32), Margin = new Padding(2, 0, 2, 0) };
            btnNavAdmin = new NavButton { Text = "Admin", Size = new Size(88, 32), Margin = new Padding(2, 0, 2, 0) };
            btnNavLogs = new NavButton { Text = "Logs", Size = new Size(88, 32), Margin = new Padding(2, 0, 2, 0) };
            btnNavConsole.Click += (s, e) => ShowSection(0);
            btnNavSettings.Click += (s, e) => ShowSection(1);
            btnNavAdmin.Click += (s, e) => ShowSection(2);
            btnNavLogs.Click += (s, e) => ShowSection(3);
            navPanel.Controls.Add(btnNavConsole);
            navPanel.Controls.Add(btnNavSettings);
            navPanel.Controls.Add(btnNavAdmin);
            navPanel.Controls.Add(btnNavLogs);

            headerLayout.Controls.Add(brandPanel, 0, 0);
            headerLayout.Controls.Add(navPanel, 1, 0);
            panelHeader.Controls.Add(headerLayout);

            // Content host: all tabs live HERE so BringToFront never steals space from the header
            panelContentHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.Bg,
                Padding = new Padding(0)
            };

            panelMain = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Bg, Padding = new Padding(16), Visible = true, AutoScroll = true };
            panelSettings = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Bg, Padding = new Padding(16), Visible = false, AutoScroll = true };
            panelAdmin = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Bg, Padding = new Padding(16), Visible = false, AutoScroll = true };
            panelLogs = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Bg, Padding = new Padding(16), Visible = false, AutoScroll = true };

            BuildConsoleSection();
            BuildSettingsSection();
            BuildAdminSection();
            BuildLogsSection();

            statusStrip = new StatusStrip();
            statusLabel = new ToolStripStatusLabel { Text = "Ready" };
            statusStrip.Items.Add(statusLabel);
            AppTheme.StyleStatusStrip(statusStrip, statusLabel);

            // Sections only inside content host
            panelContentHost.Controls.Add(panelLogs);
            panelContentHost.Controls.Add(panelAdmin);
            panelContentHost.Controls.Add(panelSettings);
            panelContentHost.Controls.Add(panelMain);

            // Form dock order: Fill host first, then Bottom strip, then Top header (last = docks first)
            this.Controls.Add(panelContentHost);
            this.Controls.Add(statusStrip);
            this.Controls.Add(panelHeader);

            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High; } catch { }

            liveTimeThread = new Thread(DisplayLiveTime) { IsBackground = true };
            liveTimeThread.Start();

            UpdateTimeSourceUiEnabled();
            ShowSection(0);
        }

        private void ShowSection(int index)
        {
            panelMain.Visible = index == 0;
            panelSettings.Visible = index == 1;
            panelAdmin.Visible = index == 2;
            panelLogs.Visible = index == 3;
            btnNavConsole.Active = index == 0;
            btnNavSettings.Active = index == 1;
            btnNavAdmin.Active = index == 2;
            btnNavLogs.Active = index == 3;

            // Only reorder inside content host — never BringToFront against the form header
            Panel active = panelMain;
            if (index == 1) active = panelSettings;
            else if (index == 2) active = panelAdmin;
            else if (index == 3) active = panelLogs;
            if (active != null && panelContentHost != null && active.Parent == panelContentHost)
                active.BringToFront();
        }

        private Label MakeCaption(string text)
        {
            var lbl = new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.BottomLeft,
                Margin = new Padding(2, 0, 2, 0),
                AutoSize = false
            };
            AppTheme.StyleLabel(lbl, muted: true);
            lbl.Font = AppTheme.CaptionFont;
            return lbl;
        }

        private TableLayoutPanel MakeFieldGrid(int columns)
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = columns,
                RowCount = 2,
                BackColor = AppTheme.Surface,
                Height = 52,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(0)
            };
            float pct = 100f / columns;
            for (int i = 0; i < columns; i++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, pct));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            return grid;
        }

        private void AddLabeledField(TableLayoutPanel grid, int col, string caption, Control field)
        {
            grid.Controls.Add(MakeCaption(caption), col, 0);
            field.Dock = DockStyle.Fill;
            field.Margin = new Padding(2, 2, 2, 2);
            grid.Controls.Add(field, col, 1);
        }

        private void BuildConsoleSection()
        {
            panelHero = new CardPanel("Live time")
            {
                Dock = DockStyle.Top,
                Height = 156,
                Margin = new Padding(0, 0, 0, 12)
            };

            var heroInner = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                BackColor = AppTheme.Surface,
                Padding = new Padding(2, 0, 2, 0)
            };
            heroInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            heroInner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44F));
            heroInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
            heroInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            heroInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            heroInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 10F));

            lblHeroClock = new HeroClockLabel
            {
                Text = "--:--:--",
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };

            lblSyncDot = new Label
            {
                Text = "●",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = AppTheme.TextMuted,
                BackColor = AppTheme.Surface,
                TextAlign = ContentAlignment.MiddleCenter
            };

            lblHeroMeta = new Label
            {
                Text = "Source: starting…",
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 0, 2, 0)
            };
            AppTheme.StyleLabel(lblHeroMeta, muted: true, mono: true);

            lblCountdown = new Label
            {
                Text = "Remaining  —",
                Dock = DockStyle.Fill,
                Font = AppTheme.UiFontBold,
                ForeColor = AppTheme.TextPrimary,
                BackColor = AppTheme.Surface,
                Margin = new Padding(2, 0, 2, 0)
            };

            progressCountdown = new ThinProgressBar
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 2, 2, 0),
                Progress = 0
            };

            lblLiveTime = new Label { Visible = false, Size = new Size(1, 1) };

            heroInner.Controls.Add(lblHeroClock, 0, 0);
            heroInner.Controls.Add(lblSyncDot, 1, 0);
            heroInner.Controls.Add(lblHeroMeta, 0, 1);
            heroInner.SetColumnSpan(lblHeroMeta, 2);
            heroInner.Controls.Add(lblCountdown, 0, 2);
            heroInner.SetColumnSpan(lblCountdown, 2);
            heroInner.Controls.Add(progressCountdown, 0, 3);
            heroInner.SetColumnSpan(progressCountdown, 2);
            panelHero.Body.Controls.Add(heroInner);
            panelHero.Body.Controls.Add(lblLiveTime);

            panelStatusChips = new CardPanel("Status")
            {
                Dock = DockStyle.Top,
                Height = 96,
                Margin = new Padding(0, 0, 0, 12)
            };

            var statusGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                BackColor = AppTheme.Surface,
                Padding = new Padding(0)
            };
            statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            statusGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            statusGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            lblTargetTime = new Label { Text = "Target  ·  Not set", Dock = DockStyle.Fill, Margin = new Padding(2) };
            lblProcessStatus = new Label { Text = "Process  ·  —", Dock = DockStyle.Fill, Margin = new Padding(2) };
            lblClickCount = new Label { Text = "Clicks  ·  1", Dock = DockStyle.Fill, Margin = new Padding(2) };
            lblConfigStatus = new Label { Text = "Config  ·  Not set", Dock = DockStyle.Fill, Margin = new Padding(2) };
            lblPayamStatus = new Label { Text = "Sync  ·  starting…", Dock = DockStyle.Fill, Margin = new Padding(2) };
            AppTheme.StyleLabel(lblTargetTime, mono: true);
            AppTheme.StyleLabel(lblProcessStatus, muted: true);
            AppTheme.StyleLabel(lblClickCount, muted: true);
            AppTheme.StyleLabel(lblConfigStatus, muted: true);
            AppTheme.StyleLabel(lblPayamStatus, muted: true);

            statusGrid.Controls.Add(lblTargetTime, 0, 0);
            statusGrid.Controls.Add(lblClickCount, 1, 0);
            statusGrid.Controls.Add(lblProcessStatus, 2, 0);
            statusGrid.Controls.Add(lblConfigStatus, 0, 1);
            statusGrid.Controls.Add(lblPayamStatus, 1, 1);
            statusGrid.SetColumnSpan(lblPayamStatus, 2);
            panelStatusChips.Body.Controls.Add(statusGrid);

            panelArm = new CardPanel("Setup")
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };

            var armRoot = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = AppTheme.Surface,
                Padding = new Padding(0)
            };
            armRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            armRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            armRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            armRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            armRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 74F));

            var row1 = MakeFieldGrid(4);
            row1.Dock = DockStyle.Fill;
            row1.Margin = new Padding(0);
            dtpTargetDate = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = DateTime.Today };
            AppTheme.StyleDateTimePicker(dtpTargetDate);
            dtpTargetTime = new DateTimePicker { Format = DateTimePickerFormat.Time, ShowUpDown = true, Value = DateTime.Now.AddMinutes(1) };
            AppTheme.StyleDateTimePicker(dtpTargetTime);
            nudMilliseconds = new NumericUpDown { Minimum = 0, Maximum = 999, Value = 0 };
            AppTheme.StyleNumeric(nudMilliseconds);
            txtTargetProcess = new TextBox { Text = "Payam" };
            AppTheme.StyleTextBox(txtTargetProcess);
            AddLabeledField(row1, 0, "TARGET DATE", dtpTargetDate);
            AddLabeledField(row1, 1, "TARGET TIME", dtpTargetTime);
            AddLabeledField(row1, 2, "MILLISECONDS", nudMilliseconds);
            AddLabeledField(row1, 3, "PROCESS", txtTargetProcess);

            var row2 = MakeFieldGrid(3);
            row2.Dock = DockStyle.Fill;
            row2.Margin = new Padding(0);
            nudClickCount = new NumericUpDown { Minimum = 1, Maximum = 500, Value = 1 };
            AppTheme.StyleNumeric(nudClickCount);
            nudClickCount.ValueChanged += (s, e) =>
            {
                nudClickInterval.Enabled = nudClickCount.Value > 1;
                if (nudClickCount.Value <= 1) nudClickInterval.Value = 0;
                lblClickCount.Text = "Clicks  ·  " + ((int)nudClickCount.Value).ToString();
            };
            nudClickInterval = new NumericUpDown { Minimum = 0, Maximum = 60000, Value = 0, Enabled = false };
            AppTheme.StyleNumeric(nudClickInterval);
            cmbTimeSource = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            AppTheme.StyleCombo(cmbTimeSource);
            cmbTimeSource.Items.Add("Payam API Time");
            cmbTimeSource.Items.Add("NTP");
            cmbTimeSource.Items.Add("System Time");
            cmbTimeSource.SelectedIndex = 0;
            cmbTimeSource.SelectedIndexChanged += (s, e) =>
            {
                TimeSourceMode mode;
                switch (cmbTimeSource.SelectedIndex)
                {
                    case 1: mode = TimeSourceMode.Ntp; break;
                    case 2: mode = TimeSourceMode.System; break;
                    default: mode = TimeSourceMode.PayamApi; break;
                }
                SetTimeSourceMode(mode, syncNow: true);
            };
            AddLabeledField(row2, 0, "CLICK COUNT", nudClickCount);
            AddLabeledField(row2, 1, "CLICK INTERVAL (MS)", nudClickInterval);
            AddLabeledField(row2, 2, "TIME SOURCE", cmbTimeSource);

            var rowActions = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = AppTheme.Surface,
                Padding = new Padding(0, 2, 0, 0)
            };
            rowActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            rowActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            var btnRead = new AccentButton { Text = "Read Config", Dock = DockStyle.Fill, Margin = new Padding(0, 2, 6, 2) };
            btnRead.SetSecondary();
            btnRead.Click += BtnReadConfig_Click;
            btnReadConfig = btnRead;

            var btnManual = new AccentButton { Text = "Use Manual Settings", Dock = DockStyle.Fill, Margin = new Padding(6, 2, 0, 2) };
            btnManual.SetSecondary();
            btnManual.Click += BtnManualConfig_Click;
            btnManualConfig = btnManual;

            rowActions.Controls.Add(btnReadConfig, 0, 0);
            rowActions.Controls.Add(btnManualConfig, 1, 0);

            var spacer = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface };

            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = AppTheme.Surface
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));

            var start = new AccentButton { Text = "START", Dock = DockStyle.Fill, Margin = new Padding(0, 2, 6, 2) };
            start.SetAccent(AppTheme.Start, AppTheme.StartHover);
            start.Font = new Font("Segoe UI Semibold", 11.5F, FontStyle.Bold);
            start.Click += BtnStart_Click;
            btnStart = start;

            var stop = new AccentButton { Text = "STOP", Dock = DockStyle.Fill, Margin = new Padding(6, 2, 0, 2), Enabled = false };
            stop.SetAccent(AppTheme.StopEnabled, AppTheme.Danger);
            stop.Font = new Font("Segoe UI Semibold", 11.5F, FontStyle.Bold);
            stop.Click += BtnStop_Click;
            btnStop = stop;

            var hint = new Label
            {
                Text = "F12 fires on Payam clock + safety margin — never early.",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };
            AppTheme.StyleLabel(hint, muted: true);
            hint.Font = AppTheme.CaptionFont;

            bottom.Controls.Add(btnStart, 0, 0);
            bottom.Controls.Add(btnStop, 1, 0);
            bottom.Controls.Add(hint, 0, 1);
            bottom.SetColumnSpan(hint, 2);

            armRoot.Controls.Add(row1, 0, 0);
            armRoot.Controls.Add(row2, 0, 1);
            armRoot.Controls.Add(rowActions, 0, 2);
            armRoot.Controls.Add(spacer, 0, 3);
            armRoot.Controls.Add(bottom, 0, 4);
            panelArm.Body.Controls.Add(armRoot);

            var gap1 = new Panel { Dock = DockStyle.Top, Height = 12, BackColor = AppTheme.Bg };
            var gap2 = new Panel { Dock = DockStyle.Top, Height = 12, BackColor = AppTheme.Bg };
            panelMain.Controls.Add(panelArm);
            panelMain.Controls.Add(gap2);
            panelMain.Controls.Add(panelStatusChips);
            panelMain.Controls.Add(gap1);
            panelMain.Controls.Add(panelHero);
        }

        private void BuildSettingsSection()
        {
            const int foldersH = 210;
            const int payamH = 278;
            const int shareH = 278;
            const int gap = 12;

            settingsStack = new Panel
            {
                Dock = DockStyle.Top,
                Height = foldersH + gap + payamH + gap + shareH + 8,
                BackColor = AppTheme.Bg,
                Padding = new Padding(0, 0, 4, 8)
            };

            panelSettingsFolders = new CardPanel("Folders / NTP")
            {
                Dock = DockStyle.Top,
                Height = foldersH
            };
            BuildFoldersCard();

            panelSettingsPayam = new CardPanel("Payam API Time")
            {
                Dock = DockStyle.Top,
                Height = payamH
            };
            BuildPayamCard();

            panelSettingsShare = new CardPanel("Network Share Auth")
            {
                Dock = DockStyle.Top,
                Height = shareH
            };
            BuildShareCard();

            // Dock Top: last added is visually highest
            settingsStack.Controls.Add(panelSettingsShare);
            settingsStack.Controls.Add(new Panel { Dock = DockStyle.Top, Height = gap, BackColor = AppTheme.Bg });
            settingsStack.Controls.Add(panelSettingsPayam);
            settingsStack.Controls.Add(new Panel { Dock = DockStyle.Top, Height = gap, BackColor = AppTheme.Bg });
            settingsStack.Controls.Add(panelSettingsFolders);

            panelSettings.Controls.Add(settingsStack);
            Action syncWidths = () =>
            {
                int w = Math.Max(320, panelSettings.ClientSize.Width - panelSettings.Padding.Horizontal - 24);
                settingsStack.Width = w;
                panelSettingsFolders.Width = w;
                panelSettingsPayam.Width = w;
                panelSettingsShare.Width = w;
            };
            panelSettings.Resize += (s, e) => syncWidths();
            syncWidths();
        }

        private TableLayoutPanel MakePathRow(string caption, out TextBox textBox, string initialText, out AccentButton browseBtn, string browseText, int browseWidth)
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 52,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = AppTheme.Surface,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(0)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, browseWidth));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

            var cap = MakeCaption(caption);
            row.Controls.Add(cap, 0, 0);
            row.SetColumnSpan(cap, 2);

            textBox = new TextBox { Text = initialText, Dock = DockStyle.Fill, Margin = new Padding(2, 2, 2, 2) };
            AppTheme.StyleTextBox(textBox);
            browseBtn = new AccentButton { Text = browseText, Dock = DockStyle.Fill, Margin = new Padding(6, 2, 0, 2) };
            browseBtn.SetSecondary();
            row.Controls.Add(textBox, 0, 1);
            row.Controls.Add(browseBtn, 1, 1);
            return row;
        }

        private void BuildFoldersCard()
        {
            var body = panelSettingsFolders.Body;

            AccentButton browseCfg;
            var pathCfg = MakePathRow("CONFIG FOLDER", out txtConfigFolder, configFolder, out browseCfg, "Browse", 110);
            browseCfg.Click += (s, e) =>
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        txtConfigFolder.Text = dialog.SelectedPath;
                        configFolder = dialog.SelectedPath;
                        DisposeWatcher();
                        SetupNtpConfigWatcher();
                        LoadNtpServerFromFile(overwriteTextbox: true);
                    }
                }
            };
            btnBrowseConfig = browseCfg;

            AccentButton browseLog;
            var pathLog = MakePathRow("LOG FOLDER", out txtLogFolder, logFolder, out browseLog, "Browse", 110);
            browseLog.Click += (s, e) =>
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        txtLogFolder.Text = dialog.SelectedPath;
                        logFolder = dialog.SelectedPath;
                    }
                }
            };
            btnBrowseLog = browseLog;

            AccentButton syncNtp;
            var ntpRow = MakePathRow("NTP SERVER", out txtNtpServer, ntpServer, out syncNtp, "Sync NTP", 130);
            txtNtpServer.Enabled = false;
            syncNtp.Enabled = false;
            syncNtp.Click += (s, e) =>
            {
                var server = SafeGetText(txtNtpServer);
                if (!IsValidNtpServerAddress(server))
                {
                    LogMessage("Invalid NTP server address: " + server, Color.Red);
                    UI(() =>
                    {
                        lblNtpStatus.Text = "NTP Status: Invalid server address";
                        lblNtpStatus.ForeColor = AppTheme.Danger;
                    });
                    return;
                }
                ntpServer = server;
                SaveNtpServerToFile(ntpServer);
                ThreadPool.QueueUserWorkItem(_ => SyncWithNtpServer());
            };
            btnSyncNtp = syncNtp;

            lblNtpStatus = new Label
            {
                Visible = false,
                Size = new Size(1, 1),
                Text = "NTP Status: Idle (Payam mode)"
            };

            // Dock Top: last added is visually highest
            body.Controls.Add(ntpRow);
            body.Controls.Add(pathLog);
            body.Controls.Add(pathCfg);
            body.Controls.Add(lblNtpStatus);
        }

        private void BuildPayamCard()
        {
            var body = panelSettingsPayam.Body;

            var urlRow = MakeFieldGrid(1);
            urlRow.Controls.Add(MakeCaption("API URL"), 0, 0);
            txtPayamApiUrl = new TextBox { Text = PayamTimeConfig.DefaultApiUrl };
            AppTheme.StyleTextBox(txtPayamApiUrl);
            txtPayamApiUrl.Dock = DockStyle.Fill;
            txtPayamApiUrl.Margin = new Padding(2, 2, 2, 2);
            urlRow.Controls.Add(txtPayamApiUrl, 0, 1);

            var mid = MakeFieldGrid(3);
            txtPayamYearCode = new TextBox { Text = PayamTimeConfig.DefaultYearCode };
            AppTheme.StyleTextBox(txtPayamYearCode);
            txtPayamContentTypeOptions = new TextBox { Text = PayamTimeConfig.DefaultContentTypeOptions };
            AppTheme.StyleTextBox(txtPayamContentTypeOptions);
            nudSafetyMargin = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 500,
                Value = PayamTimeConfig.DefaultSafetyMarginMs
            };
            AppTheme.StyleNumeric(nudSafetyMargin);
            AddLabeledField(mid, 0, "YEARCODE", txtPayamYearCode);
            AddLabeledField(mid, 1, "X-CONTENT-TYPE-OPTIONS", txtPayamContentTypeOptions);
            AddLabeledField(mid, 2, "SAFETY MARGIN (MS)", nudSafetyMargin);

            var bias = MakeFieldGrid(2);
            bias.ColumnStyles.Clear();
            bias.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140F));
            bias.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            nudClockBias = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 300,
                Value = PayamTimeConfig.DefaultClockBiasMs
            };
            AppTheme.StyleNumeric(nudClockBias);
            nudClockBias.ValueChanged += (s, e) => ApplyLivePayamTimingFromUi();
            nudSafetyMargin.ValueChanged += (s, e) => ApplyLivePayamTimingFromUi();
            AddLabeledField(bias, 0, "CLOCK BIAS (MS)", nudClockBias);
            var marginHint = new Label
            {
                Text = "Bias applies instantly. Raise it if AutoClick is still ahead of Payam UI.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(8, 2, 2, 2)
            };
            AppTheme.StyleLabel(marginHint, muted: true);
            marginHint.Font = AppTheme.CaptionFont;
            bias.Controls.Add(MakeCaption(" "), 1, 0);
            bias.Controls.Add(marginHint, 1, 1);

            var actions = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 44,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = AppTheme.Surface,
                Margin = new Padding(0, 8, 0, 0)
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            var savePayam = new AccentButton { Text = "Save Payam Config", Dock = DockStyle.Fill, Margin = new Padding(0, 2, 6, 2) };
            savePayam.SetSecondary();
            savePayam.Click += (s, e) => SavePayamConfigFromUi();
            btnSavePayamConfig = savePayam;

            var syncPayam = new AccentButton { Text = "Apply / Resync Payam", Dock = DockStyle.Fill, Margin = new Padding(6, 2, 0, 2) };
            syncPayam.SetAccent(AppTheme.Accent, AppTheme.AccentDim);
            syncPayam.Click += (s, e) =>
            {
                SavePayamConfigFromUi();
                if (timeSourceMode != TimeSourceMode.PayamApi)
                    SetTimeSourceMode(TimeSourceMode.PayamApi, syncNow: true);
                else
                    EnsurePayamProviderStarted();
                LogMessage("Payam config applied; waiting for next second-edge phase lock.", Color.Blue);
            };
            btnSyncPayam = syncPayam;
            actions.Controls.Add(btnSavePayamConfig, 0, 0);
            actions.Controls.Add(btnSyncPayam, 1, 0);

            body.Controls.Add(actions);
            body.Controls.Add(bias);
            body.Controls.Add(mid);
            body.Controls.Add(urlRow);
        }

        private void BuildShareCard()
        {
            var body = panelSettingsShare.Body;

            chkShareAuthEnabled = new CheckBox
            {
                Text = "Auto-login to config share (recommended when running as Administrator)",
                Dock = DockStyle.Top,
                Height = 24,
                Checked = true,
                ForeColor = AppTheme.TextPrimary,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 0, 0, 6)
            };

            var rootRow = MakeFieldGrid(1);
            rootRow.Controls.Add(MakeCaption("SHARE ROOT (UNC)"), 0, 0);
            txtShareRoot = new TextBox { Text = ShareAuthConfig.DefaultShareRoot };
            AppTheme.StyleTextBox(txtShareRoot);
            txtShareRoot.Dock = DockStyle.Fill;
            txtShareRoot.Margin = new Padding(2, 2, 2, 2);
            rootRow.Controls.Add(txtShareRoot, 0, 1);

            var creds = MakeFieldGrid(3);
            txtShareDomain = new TextBox();
            AppTheme.StyleTextBox(txtShareDomain);
            txtShareUsername = new TextBox();
            AppTheme.StyleTextBox(txtShareUsername);
            txtSharePassword = new TextBox { UseSystemPasswordChar = true };
            AppTheme.StyleTextBox(txtSharePassword);
            AddLabeledField(creds, 0, "DOMAIN", txtShareDomain);
            AddLabeledField(creds, 1, "USERNAME", txtShareUsername);
            AddLabeledField(creds, 2, "PASSWORD", txtSharePassword);

            lblShareAuthStatus = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                Text = "Share auth  ·  not configured",
                Margin = new Padding(2, 4, 2, 4)
            };
            AppTheme.StyleLabel(lblShareAuthStatus, muted: true);
            lblShareAuthStatus.Font = AppTheme.CaptionFont;

            var actions = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 44,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = AppTheme.Surface,
                Margin = new Padding(0, 4, 0, 0)
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));

            var saveShare = new AccentButton { Text = "Save Share Auth", Dock = DockStyle.Fill, Margin = new Padding(0, 2, 6, 2) };
            saveShare.SetSecondary();
            saveShare.Click += (s, e) => SaveShareAuthFromUi(connectAfterSave: true);
            btnSaveShareAuth = saveShare;

            var connectShare = new AccentButton { Text = "Connect Now", Dock = DockStyle.Fill, Margin = new Padding(6, 2, 6, 2) };
            connectShare.SetAccent(AppTheme.AccentDim, AppTheme.Accent);
            connectShare.Click += (s, e) =>
            {
                string err;
                if (!TryReadShareAuthFromUi(out err))
                {
                    LogMessage(err, Color.Red);
                    return;
                }
                EnsureShareConnected(logResult: true);
            };
            btnConnectShare = connectShare;

            var shareHint = new Label
            {
                Text = "Saved next to the exe. Avoids manual Explorer login.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(6, 2, 0, 2)
            };
            AppTheme.StyleLabel(shareHint, muted: true);
            shareHint.Font = AppTheme.CaptionFont;

            actions.Controls.Add(btnSaveShareAuth, 0, 0);
            actions.Controls.Add(btnConnectShare, 1, 0);
            actions.Controls.Add(shareHint, 2, 0);

            body.Controls.Add(actions);
            body.Controls.Add(lblShareAuthStatus);
            body.Controls.Add(creds);
            body.Controls.Add(rootRow);
            body.Controls.Add(chkShareAuthEnabled);
        }

        private void BuildAdminSection()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 680,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = AppTheme.Bg,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 250F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var scheduleCard = new CardPanel("Schedule window");
            scheduleCard.Dock = DockStyle.Fill;
            scheduleCard.Margin = new Padding(0, 0, 8, 8);
            BuildDistScheduleCard(scheduleCard.Body);

            var machinesCard = new CardPanel("Machines");
            machinesCard.Dock = DockStyle.Fill;
            machinesCard.Margin = new Padding(8, 0, 0, 8);
            BuildDistMachinesCard(machinesCard.Body);

            var previewCard = new CardPanel("Preview & generate");
            previewCard.Dock = DockStyle.Fill;
            previewCard.Margin = new Padding(0, 8, 0, 0);
            BuildDistPreviewCard(previewCard.Body);

            root.Controls.Add(scheduleCard, 0, 0);
            root.Controls.Add(machinesCard, 1, 0);
            root.SetColumnSpan(previewCard, 2);
            root.Controls.Add(previewCard, 0, 1);

            panelAdmin.Controls.Add(root);
            panelAdmin.Resize += (s, e) =>
            {
                int w = Math.Max(400, panelAdmin.ClientSize.Width - panelAdmin.Padding.Horizontal - 8);
                int h = Math.Max(640, panelAdmin.ClientSize.Height - panelAdmin.Padding.Vertical - 8);
                root.Width = w;
                root.Height = h;
            };

            // Seed defaults from console controls when available
            try
            {
                dtpDistDate.Value = dtpTargetDate != null ? dtpTargetDate.Value.Date : DateTime.Today;
                var baseTod = dtpTargetTime != null ? dtpTargetTime.Value.TimeOfDay : DateTime.Now.AddMinutes(2).TimeOfDay;
                dtpDistBaseTime.Value = DateTime.Today.Add(new TimeSpan(baseTod.Hours, baseTod.Minutes, baseTod.Seconds));
                nudDistBaseMs.Value = 700;
                nudDistEndMs.Value = 200; // crosses next second when End < Start
                txtDistProcess.Text = SafeGetText(txtTargetProcess) ?? "Payam";
                if (nudClickCount != null) nudDistClickCount.Value = Math.Max(1, nudClickCount.Value);
            }
            catch { }
        }

        private void BuildDistScheduleCard(Panel body)
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 4,
                BackColor = AppTheme.Surface,
                Padding = new Padding(0)
            };
            for (int i = 0; i < 4; i++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

            dtpDistDate = new DateTimePicker { Format = DateTimePickerFormat.Short, Dock = DockStyle.Fill, Margin = new Padding(2) };
            AppTheme.StyleDateTimePicker(dtpDistDate);
            dtpDistBaseTime = new DateTimePicker { Format = DateTimePickerFormat.Time, ShowUpDown = true, Dock = DockStyle.Fill, Margin = new Padding(2) };
            AppTheme.StyleDateTimePicker(dtpDistBaseTime);
            nudDistBaseMs = new NumericUpDown { Minimum = 0, Maximum = 999, Value = 700, Dock = DockStyle.Fill, Margin = new Padding(2) };
            AppTheme.StyleNumeric(nudDistBaseMs);
            nudDistEndMs = new NumericUpDown { Minimum = 0, Maximum = 999, Value = 200, Dock = DockStyle.Fill, Margin = new Padding(2) };
            AppTheme.StyleNumeric(nudDistEndMs);

            grid.Controls.Add(MakeCaption("DATE"), 0, 0);
            grid.Controls.Add(MakeCaption("BASE TIME"), 1, 0);
            grid.Controls.Add(MakeCaption("START MS"), 2, 0);
            grid.Controls.Add(MakeCaption("END MS"), 3, 0);
            grid.Controls.Add(dtpDistDate, 0, 1);
            grid.Controls.Add(dtpDistBaseTime, 1, 1);
            grid.Controls.Add(nudDistBaseMs, 2, 1);
            grid.Controls.Add(nudDistEndMs, 3, 1);

            txtDistProcess = new TextBox { Text = "Payam", Dock = DockStyle.Fill, Margin = new Padding(2) };
            AppTheme.StyleTextBox(txtDistProcess);
            nudDistClickCount = new NumericUpDown { Minimum = 1, Maximum = 500, Value = 3, Dock = DockStyle.Fill, Margin = new Padding(2) };
            AppTheme.StyleNumeric(nudDistClickCount);
            nudDistClickInterval = new NumericUpDown { Minimum = 0, Maximum = 60000, Value = 0, Dock = DockStyle.Fill, Margin = new Padding(2) };
            AppTheme.StyleNumeric(nudDistClickInterval);

            var hint = new Label
            {
                Text = "If End MS < Start MS → crosses into next second (e.g. .700 → .200).",
                Dock = DockStyle.Fill,
                Margin = new Padding(2),
                TextAlign = ContentAlignment.MiddleLeft
            };
            AppTheme.StyleLabel(hint, muted: true);
            hint.Font = AppTheme.CaptionFont;

            grid.Controls.Add(MakeCaption("PROCESS"), 0, 2);
            grid.Controls.Add(MakeCaption("CLICK COUNT"), 1, 2);
            grid.Controls.Add(MakeCaption("CLICK INTERVAL"), 2, 2);
            grid.Controls.Add(MakeCaption("NOTE"), 3, 2);
            grid.Controls.Add(txtDistProcess, 0, 3);
            grid.Controls.Add(nudDistClickCount, 1, 3);
            grid.Controls.Add(nudDistClickInterval, 2, 3);
            grid.Controls.Add(hint, 3, 3);

            body.Controls.Add(grid);
        }

        private void BuildDistMachinesCard(Panel body)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = AppTheme.Surface
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));

            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = AppTheme.Surface
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78F));

            txtDistMachineInput = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(2), Text = "" };
            AppTheme.StyleTextBox(txtDistMachineInput);
            txtDistMachineInput.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    DistAddMachineFromInput();
                }
            };

            var addBtn = new AccentButton { Text = "Add", Dock = DockStyle.Fill, Margin = new Padding(4, 2, 2, 2) };
            addBtn.SetSecondary();
            addBtn.Click += (s, e) => DistAddMachineFromInput();
            btnDistAddMachine = addBtn;

            var remBtn = new AccentButton { Text = "Remove", Dock = DockStyle.Fill, Margin = new Padding(2) };
            remBtn.SetSecondary();
            remBtn.Click += (s, e) => DistRemoveSelectedMachines();
            btnDistRemoveMachine = remBtn;

            var scanBtn = new AccentButton { Text = "Scan share", Dock = DockStyle.Fill, Margin = new Padding(2, 2, 0, 2) };
            scanBtn.SetAccent(AppTheme.Accent, AppTheme.AccentDim);
            scanBtn.Click += (s, e) => DistScanShareMachines();
            btnDistScan = scanBtn;

            top.Controls.Add(txtDistMachineInput, 0, 0);
            top.Controls.Add(btnDistAddMachine, 1, 0);
            top.Controls.Add(btnDistRemoveMachine, 2, 0);
            top.Controls.Add(btnDistScan, 3, 0);

            lstDistMachines = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                SelectionMode = SelectionMode.MultiExtended,
                Font = AppTheme.MonoFont,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                ForeColor = AppTheme.TextPrimary,
                Margin = new Padding(2, 6, 2, 4)
            };

            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = AppTheme.Surface
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            var loadBtn = new AccentButton { Text = "Load machines.txt", Dock = DockStyle.Fill, Margin = new Padding(2, 2, 6, 2) };
            loadBtn.SetSecondary();
            loadBtn.Click += (s, e) => DistLoadMachinesFile();
            btnDistLoadList = loadBtn;

            var saveBtn = new AccentButton { Text = "Save machines.txt", Dock = DockStyle.Fill, Margin = new Padding(6, 2, 2, 2) };
            saveBtn.SetSecondary();
            saveBtn.Click += (s, e) => DistSaveMachinesFile();
            btnDistSaveList = saveBtn;

            bottom.Controls.Add(btnDistLoadList, 0, 0);
            bottom.Controls.Add(btnDistSaveList, 1, 0);

            layout.Controls.Add(top, 0, 0);
            layout.Controls.Add(lstDistMachines, 0, 1);
            layout.Controls.Add(bottom, 0, 2);
            body.Controls.Add(layout);
        }

        private void BuildDistPreviewCard(Panel body)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = AppTheme.Surface
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));

            lblDistSummary = new Label
            {
                Text = "Load or scan machines, set the window, then Preview.",
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 0, 2, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };
            AppTheme.StyleLabel(lblDistSummary, muted: true);

            lvDistPreview = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BorderStyle = BorderStyle.FixedSingle,
                Font = AppTheme.MonoFont,
                BackColor = Color.White,
                ForeColor = AppTheme.TextPrimary,
                Margin = new Padding(2, 4, 2, 4)
            };
            lvDistPreview.Columns.Add("#", 44);
            lvDistPreview.Columns.Add("Machine", 160);
            lvDistPreview.Columns.Add("TargetTime", 210);
            lvDistPreview.Columns.Add("File", 220);
            lvDistPreview.Columns.Add("Status", 100);

            var actions = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = AppTheme.Surface
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22F));

            chkDistSaveMachinesFile = new CheckBox
            {
                Text = "Also save machines.txt",
                Checked = true,
                Dock = DockStyle.Fill,
                ForeColor = AppTheme.TextPrimary,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(4, 8, 4, 4)
            };
            chkDistCleanupOrphans = new CheckBox
            {
                Text = "Delete configs not in list",
                Checked = false,
                Dock = DockStyle.Fill,
                ForeColor = AppTheme.TextPrimary,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(4, 8, 4, 4)
            };

            var previewBtn = new AccentButton { Text = "Preview", Dock = DockStyle.Fill, Margin = new Padding(2, 4, 6, 4) };
            previewBtn.SetSecondary();
            previewBtn.Click += (s, e) => DistBuildPreview();
            btnDistPreview = previewBtn;

            var genBtn = new AccentButton { Text = "Generate configs", Dock = DockStyle.Fill, Margin = new Padding(6, 4, 2, 4) };
            genBtn.SetAccent(AppTheme.Start, AppTheme.StartHover);
            genBtn.Click += (s, e) => DistGenerateConfigs();
            btnDistGenerate = genBtn;

            actions.Controls.Add(chkDistSaveMachinesFile, 0, 0);
            actions.Controls.Add(chkDistCleanupOrphans, 1, 0);
            actions.Controls.Add(btnDistPreview, 2, 0);
            actions.Controls.Add(btnDistGenerate, 3, 0);

            layout.Controls.Add(lblDistSummary, 0, 0);
            layout.Controls.Add(lvDistPreview, 0, 1);
            layout.Controls.Add(actions, 0, 2);
            body.Controls.Add(layout);
        }

        // -------------------------
        // Admin / Config Distributor
        // -------------------------
        private string DistConfigFolder()
        {
            string folder = SafeGetText(txtConfigFolder);
            if (string.IsNullOrWhiteSpace(folder)) folder = configFolder;
            return folder;
        }

        private List<string> DistGetMachineListFromUi()
        {
            var list = new List<string>();
            if (lstDistMachines == null) return list;
            foreach (var item in lstDistMachines.Items)
            {
                if (item != null) list.Add(item.ToString());
            }
            return ConfigDistributor.NormalizeMachineList(list);
        }

        private void DistSetMachineList(IEnumerable<string> machines)
        {
            if (lstDistMachines == null) return;
            lstDistMachines.BeginUpdate();
            try
            {
                lstDistMachines.Items.Clear();
                foreach (var m in ConfigDistributor.NormalizeMachineList(machines))
                    lstDistMachines.Items.Add(m);
            }
            finally
            {
                lstDistMachines.EndUpdate();
            }
        }

        private void DistAddMachineFromInput()
        {
            string name = SafeGetText(txtDistMachineInput);
            if (string.IsNullOrWhiteSpace(name)) return;
            var list = DistGetMachineListFromUi();
            list.Add(name.Trim());
            DistSetMachineList(list);
            txtDistMachineInput.Text = string.Empty;
            lastDistPlan = null;
            if (lblDistSummary != null)
                lblDistSummary.Text = list.Count + " machine(s) — click Preview to recalculate slots.";
        }

        private void DistRemoveSelectedMachines()
        {
            if (lstDistMachines == null || lstDistMachines.SelectedItems.Count == 0) return;
            var remove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in lstDistMachines.SelectedItems)
                remove.Add(item.ToString());
            var kept = DistGetMachineListFromUi().Where(m => !remove.Contains(m)).ToList();
            DistSetMachineList(kept);
            lastDistPlan = null;
        }

        private void DistScanShareMachines()
        {
            try
            {
                EnsureShareConnected(logResult: false);
                string folder = DistConfigFolder();
                if (!IsDirectoryAccessible(folder))
                {
                    LogMessage("Admin: cannot access config folder for scan.", Color.Red);
                    MessageBox.Show("Cannot access config folder.\nCheck Settings → Network Share Auth.", "Scan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var found = ConfigDistributor.DiscoverMachinesFromConfigs(folder);
                DistSetMachineList(found);
                LogMessage("Admin: scanned " + found.Count + " machine config(s) from share.", Color.Blue);
                statusLabel.Text = "Scanned " + found.Count + " machines from share";
                lastDistPlan = null;
                if (lblDistSummary != null)
                    lblDistSummary.Text = found.Count + " machine(s) from share — click Preview.";
            }
            catch (Exception ex)
            {
                LogMessage("Admin scan failed: " + ex.Message, Color.Red);
            }
        }

        private void DistLoadMachinesFile()
        {
            try
            {
                EnsureShareConnected(logResult: false);
                string folder = DistConfigFolder();
                string path = ConfigDistributor.MachinesFilePath(folder);
                if (!File.Exists(path))
                {
                    MessageBox.Show("machines.txt not found:\n" + path + "\n\nScan share first, or Add machines manually, then Save machines.txt.", "Load list", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var list = ConfigDistributor.LoadMachinesFile(path);
                DistSetMachineList(list);
                LogMessage("Admin: loaded " + list.Count + " machine(s) from machines.txt", Color.Blue);
                statusLabel.Text = "Loaded machines.txt (" + list.Count + ")";
                lastDistPlan = null;
            }
            catch (Exception ex)
            {
                LogMessage("Admin load machines.txt failed: " + ex.Message, Color.Red);
            }
        }

        private void DistSaveMachinesFile()
        {
            try
            {
                EnsureShareConnected(logResult: true);
                string folder = DistConfigFolder();
                if (!IsDirectoryAccessible(folder))
                {
                    MessageBox.Show("Cannot access config folder.", "Save list", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var list = DistGetMachineListFromUi();
                string path = ConfigDistributor.MachinesFilePath(folder);
                ConfigDistributor.SaveMachinesFile(path, list);
                LogMessage("Admin: saved machines.txt (" + list.Count + ") → " + path, Color.Green);
                statusLabel.Text = "Saved machines.txt";
            }
            catch (Exception ex)
            {
                LogMessage("Admin save machines.txt failed: " + ex.Message, Color.Red);
                MessageBox.Show(ex.Message, "Save list", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool DistTryGetBaseAndEnd(out DateTime baseTime, out DateTime endTime, out string error)
        {
            baseTime = DateTime.MinValue;
            endTime = DateTime.MinValue;
            error = null;
            try
            {
                DateTime date = dtpDistDate.Value.Date;
                TimeSpan tod = dtpDistBaseTime.Value.TimeOfDay;
                int startMs = (int)nudDistBaseMs.Value;
                int endMs = (int)nudDistEndMs.Value;
                baseTime = date.Add(new TimeSpan(tod.Hours, tod.Minutes, tod.Seconds)).AddMilliseconds(startMs);

                if (endMs >= startMs)
                {
                    endTime = date.Add(new TimeSpan(tod.Hours, tod.Minutes, tod.Seconds)).AddMilliseconds(endMs);
                }
                else
                {
                    // Crosses into next second (e.g. 59.700 → 00.200)
                    endTime = date.Add(new TimeSpan(tod.Hours, tod.Minutes, tod.Seconds)).AddSeconds(1).AddMilliseconds(endMs);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private DistributePlan DistBuildPreview()
        {
            string err;
            DateTime baseTime, endTime;
            if (!DistTryGetBaseAndEnd(out baseTime, out endTime, out err))
            {
                MessageBox.Show(err ?? "Invalid schedule.", "Preview", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            var machines = DistGetMachineListFromUi();
            if (machines.Count == 0)
            {
                MessageBox.Show("Machine list is empty.\nScan share, load machines.txt, or Add PCs.", "Preview", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            string folder = DistConfigFolder();
            var plan = ConfigDistributor.BuildPlan(
                machines,
                baseTime,
                endTime,
                SafeGetText(txtDistProcess) ?? "Payam",
                (int)nudDistClickCount.Value,
                (int)nudDistClickInterval.Value,
                folder);

            lastDistPlan = plan;
            DistFillPreviewList(plan);
            lblDistSummary.Text = ConfigDistributor.DescribePlan(plan);
            if (plan.Warnings != null)
            {
                foreach (var w in plan.Warnings)
                    LogMessage("Admin preview: " + w, Color.Orange);
            }
            LogMessage("Admin preview ready: " + ConfigDistributor.DescribePlan(plan), Color.Blue);
            statusLabel.Text = "Preview ready — " + plan.Slots.Count + " slots";
            return plan;
        }

        private void DistFillPreviewList(DistributePlan plan)
        {
            lvDistPreview.BeginUpdate();
            try
            {
                lvDistPreview.Items.Clear();
                if (plan == null || plan.Slots == null) return;
                foreach (var slot in plan.Slots)
                {
                    var item = new ListViewItem(slot.SlotIndex.ToString());
                    item.SubItems.Add(slot.MachineName);
                    item.SubItems.Add(slot.TargetTime.ToString("yyyy/MM/dd HH:mm:ss.fff"));
                    item.SubItems.Add(slot.MachineName + ConfigDistributor.ConfigSuffix);
                    item.SubItems.Add(slot.HadExistingFile ? "overwrite" : "new");
                    lvDistPreview.Items.Add(item);
                }
            }
            finally
            {
                lvDistPreview.EndUpdate();
            }
        }

        private void DistGenerateConfigs()
        {
            try
            {
                EnsureShareConnected(logResult: true);
                string folder = DistConfigFolder();
                if (!IsDirectoryAccessible(folder))
                {
                    MessageBox.Show("Cannot access config folder.\nCheck Settings → Network Share Auth / Config Folder.", "Generate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var plan = lastDistPlan ?? DistBuildPreview();
                if (plan == null || plan.Slots == null || plan.Slots.Count == 0)
                    return;

                // Rebuild with latest UI values in case schedule changed after last preview
                plan = DistBuildPreview();
                if (plan == null) return;

                string msg = "Write " + plan.Slots.Count + " config file(s) to:\n" + folder +
                             "\n\n" + ConfigDistributor.DescribePlan(plan) +
                             "\n\nExisting matching files will be overwritten.";
                if (chkDistCleanupOrphans != null && chkDistCleanupOrphans.Checked)
                    msg += "\n\nOrphan *_config.txt files NOT in this list will be DELETED.";

                if (MessageBox.Show(msg, "Generate configs", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                    return;

                if (chkDistSaveMachinesFile != null && chkDistSaveMachinesFile.Checked)
                {
                    ConfigDistributor.SaveMachinesFile(ConfigDistributor.MachinesFilePath(folder), DistGetMachineListFromUi());
                    LogMessage("Admin: machines.txt updated.", Color.Blue);
                }

                var write = ConfigDistributor.WriteConfigs(folder, plan);
                foreach (var e in write.Errors)
                    LogMessage("Admin write error: " + e, Color.Red);

                int deleted = 0;
                if (chkDistCleanupOrphans != null && chkDistCleanupOrphans.Checked)
                {
                    var clean = ConfigDistributor.CleanupOrphanConfigs(folder, DistGetMachineListFromUi());
                    deleted = clean.Deleted;
                    foreach (var e in clean.Errors)
                        LogMessage("Admin cleanup error: " + e, Color.Orange);
                    if (deleted > 0)
                        LogMessage("Admin: deleted " + deleted + " orphan config(s).", Color.Orange);
                }

                DistFillPreviewList(plan);
                string done = "Generated " + write.Written + " config(s)";
                if (write.Failed > 0) done += ", " + write.Failed + " failed";
                if (deleted > 0) done += ", " + deleted + " orphan(s) removed";
                LogMessage("Admin: " + done, write.Failed > 0 ? Color.Orange : Color.Green);
                statusLabel.Text = done;
                lblDistSummary.Text = done + "  ·  " + ConfigDistributor.DescribePlan(plan);
                MessageBox.Show(done, "Generate configs", MessageBoxButtons.OK,
                    write.Failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LogMessage("Admin generate failed: " + ex.Message, Color.Red);
                MessageBox.Show(ex.Message, "Generate", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BuildLogsSection()
        {
            var logSurface = new CardPanel("Event Log")
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };

            rtbLogs = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true
            };
            AppTheme.StyleRichText(rtbLogs);
            logSurface.Body.Controls.Add(rtbLogs);
            panelLogs.Controls.Add(logSurface);
        }

        // -------------------------
        // Config reading (main config)
        // -------------------------
        private void BtnReadConfig_Click(object sender, EventArgs e)
        {
            try
            {
                configFolder = SafeGetText(txtConfigFolder) ?? configFolder;
                if (string.IsNullOrWhiteSpace(configFolder))
                {
                    LogMessage("Configuration folder path is empty", Color.Red);
                    UI(() =>
                    {
                        MessageBox.Show("Configuration folder path is empty.", "Invalid Path", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        lblConfigStatus.Text = "Config  ·  Not set";
                        statusLabel.Text = "Invalid configuration path";
                    });
                    return;
                }

                string machineName = Environment.MachineName;
                string configPath = Path.Combine(configFolder, $"{machineName}_config.txt");

                LogMessage($"Checking access to config folder: {configFolder}", Color.Blue);
                if (!EnsureShareConnected(logResult: true) && shareAuthConfig != null && shareAuthConfig.Enabled)
                {
                    LogMessage("Share auth failed — config folder may still be unreachable.", Color.Orange);
                }
                if (!IsDirectoryAccessible(configFolder))
                {
                    LogMessage($"Cannot access configuration folder: {configFolder}", Color.Red);
                    UI(() =>
                    {
                        MessageBox.Show(
                            "Cannot access the configuration folder.\n\nIf you run as Administrator, set Share Username/Password in Settings → Network Share Auth, then click Connect Now.",
                            "Access Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        lblConfigStatus.Text = "Config  ·  Not set";
                        statusLabel.Text = "Cannot access configuration folder";
                    });
                    return;
                }

                if (!File.Exists(configPath))
                {
                    LogMessage($"Configuration file not found: {configPath}", Color.Red);
                    UI(() =>
                    {
                        MessageBox.Show("Configuration file not found.", "File Not Found", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        lblConfigStatus.Text = "Config  ·  Not set";
                        statusLabel.Text = "Configuration file not found";
                    });
                    return;
                }

                UI(() =>
                {
                    lblConfigStatus.Text = "Config  ·  " + Path.GetFileNameWithoutExtension(configPath);
                    lblConfigStatus.ForeColor = Color.Green;
                });

                bool timeParsed = false;
                bool processSet = false;
                bool countSet = false;
                bool intervalSet = false;

                foreach (var line in File.ReadAllLines(configPath))
                {
                    string trimmed = (line ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;

                    if (trimmed.StartsWith("TargetTime=", StringComparison.OrdinalIgnoreCase))
                    {
                        string timeStr = trimmed.Substring("TargetTime=".Length).Trim();
                        if (DateTime.TryParseExact(timeStr, "yyyy/MM/dd HH:mm:ss.fff", null,
                            System.Globalization.DateTimeStyles.None, out DateTime parsedTime))
                        {
                            targetTime = parsedTime;
                            timeParsed = true;
                            UI(() =>
                            {
                                dtpTargetDate.Value = parsedTime.Date;
                                dtpTargetTime.Value = DateTime.Today.Add(parsedTime.TimeOfDay);
                                nudMilliseconds.Value = parsedTime.Millisecond;
                                lblTargetTime.Text = $"Target  ·  {targetTime:yyyy/MM/dd HH:mm:ss.fff}";
                            });
                            LogMessage($"Target time set: {targetTime:yyyy/MM/dd HH:mm:ss.fff}", Color.Green);
                        }
                        else
                        {
                            LogMessage($"Invalid TargetTime format: {timeStr}", Color.Red);
                        }
                    }
                    else if (trimmed.StartsWith("TargetProcess=", StringComparison.OrdinalIgnoreCase))
                    {
                        targetProcess = trimmed.Substring("TargetProcess=".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(targetProcess))
                        {
                            processSet = true;
                            UI(() =>
                            {
                                txtTargetProcess.Text = targetProcess;
                                lblProcessStatus.Text = $"Process  ·  {targetProcess}";
                            });
                            LogMessage($"Target process set: {targetProcess}", Color.Green);
                        }
                    }
                    else if (trimmed.StartsWith("clickCount=", StringComparison.OrdinalIgnoreCase))
                    {
                        string countStr = trimmed.Substring("clickCount=".Length).Trim();
                        if (int.TryParse(countStr, out int parsedCount) && parsedCount > 0)
                        {
                            clickCount = parsedCount;
                            countSet = true;
                            UI(() =>
                            {
                                if (parsedCount > nudClickCount.Maximum) nudClickCount.Maximum = parsedCount;
                                nudClickCount.Value = parsedCount;
                                lblClickCount.Text = $"Clicks  ·  {clickCount}";
                            });
                            LogMessage($"Click count set: {clickCount}", Color.Green);
                        }
                    }
                    else if (trimmed.StartsWith("clickInterval=", StringComparison.OrdinalIgnoreCase))
                    {
                        string intervalStr = trimmed.Substring("clickInterval=".Length).Trim();
                        if (int.TryParse(intervalStr, out int parsedInterval) && parsedInterval >= 0)
                        {
                            clickInterval = parsedInterval;
                            intervalSet = true;
                            UI(() =>
                            {
                                if (parsedInterval > nudClickInterval.Maximum) nudClickInterval.Maximum = parsedInterval;
                                nudClickInterval.Value = parsedInterval;
                            });
                            LogMessage($"Click interval set: {clickInterval} ms", Color.Green);
                        }
                    }
                }

                // IMPORTANT: NTP is always loaded from ntp_config.txt (authoritative)
                LoadNtpServerFromFile(overwriteTextbox: true);

                if (!timeParsed || !processSet || !countSet)
                {
                    LogMessage("Configuration incomplete (TargetTime / TargetProcess / clickCount).", Color.Red);
                    UI(() => statusLabel.Text = "Configuration incomplete");
                    return;
                }

                UI(() => statusLabel.Text = "Configuration loaded successfully");
            }
            catch (Exception ex)
            {
                LogMessage($"Error reading configuration: {ex.Message}", Color.Red);
            }
        }

        private void BtnManualConfig_Click(object sender, EventArgs e)
        {
            try
            {
                ApplyConsoleTargetFromUi(logApplied: true);

                UI(() =>
                {
                    lblConfigStatus.Text = "Config  ·  Manual";
                    lblConfigStatus.ForeColor = Color.Green;
                });

                var now = GetCurrentTime();
                if (targetTime <= now)
                {
                    LogMessage("Warning: Target time is in the past!", Color.Orange);
                    UI(() => statusLabel.Text = "Warning: Target time is in the past");
                }
                else
                {
                    UI(() => statusLabel.Text = "Manual settings applied");
                }

                // NTP server still comes from file unless user explicitly changes it and syncs in Settings tab
                LoadNtpServerFromFile(overwriteTextbox: true);
            }
            catch (Exception ex)
            {
                LogMessage($"Error applying manual settings: {ex.Message}", Color.Red);
            }
        }

        // -------------------------
        // Start/Stop
        // -------------------------
        private void BtnStart_Click(object sender, EventArgs e)
        {
            try
            {
                // Always arm from what the operator currently sees on Console.
                ApplyConsoleTargetFromUi(logApplied: false);

                if (timeSourceMode == TimeSourceMode.PayamApi)
                {
                    // Keep UI values applied for margin/token before arming.
                    string cfgError;
                    if (!TryReadPayamConfigFromUi(out cfgError))
                    {
                        MessageBox.Show(cfgError, "Payam Config", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    EnsurePayamProviderStarted();

                    if (payamTimeProvider == null || !payamTimeProvider.HasPhaseLock)
                    {
                        var proceed = MessageBox.Show(
                            "Payam time is not phase-locked yet (still waiting for a second-edge sync).\n\nContinue anyway?",
                            "Payam Sync",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);
                        if (proceed != DialogResult.Yes)
                            return;
                    }
                }

                var now = GetCurrentTime();
                var fireAt = GetFireThreshold();
                if (fireAt <= now)
                {
                    string source = GetTimeSourceLabel();
                    string detail =
                        "Target time must be after the current synced clock.\n\n" +
                        "Now (" + source + "):  " + now.ToString("yyyy/MM/dd HH:mm:ss.fff") + "\n" +
                        "Target:                 " + targetTime.ToString("yyyy/MM/dd HH:mm:ss.fff") + "\n" +
                        "Fire at (+safety):      " + fireAt.ToString("yyyy/MM/dd HH:mm:ss.fff") + "\n\n" +
                        "What to do:\n" +
                        "1) Admin → set TODAY's date/time → Preview → Generate configs\n" +
                        "2) On this PC → Read Config (or set Console date/time + Use Manual Settings)\n" +
                        "3) Wait until Live Time is synced, then START a bit before the target.";
                    MessageBox.Show(detail, "Target time is not in the future", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    LogMessage(
                        "Cannot start: fireAt=" + fireAt.ToString("yyyy/MM/dd HH:mm:ss.fff")
                        + " <= now=" + now.ToString("yyyy/MM/dd HH:mm:ss.fff") + " " + source,
                        Color.Red);
                    return;
                }

                if (string.IsNullOrWhiteSpace(targetProcess))
                {
                    MessageBox.Show("Target process name cannot be empty!", "Invalid Process", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    LogMessage("Cannot start: Target process name cannot be empty!", Color.Red);
                    return;
                }

                hasStarted = true;
                isWaiting = true;

                waitTotalMs = Math.Max(1, (GetFireThreshold() - GetCurrentTime()).TotalMilliseconds);

                waitingThread = new Thread(WaitForTargetTime) { IsBackground = true };
                waitingThread.Start();

                UI(() =>
                {
                    btnStart.Enabled = false;
                    btnStop.Enabled = true;
                    btnReadConfig.Enabled = false;
                    btnManualConfig.Enabled = false;
                    statusLabel.Text = "Armed · waiting for target";
                    if (progressCountdown != null) progressCountdown.Progress = 0;
                });

                LogMessage($"Waiting for target time: {targetTime:yyyy/MM/dd HH:mm:ss.fff} via {DescribeTimeSourceForLog()}", Color.Blue);
                if (timeSourceMode == TimeSourceMode.PayamApi)
                    LogMessage($"Payam fire threshold: {fireAt:yyyy/MM/dd HH:mm:ss.fff} (safety +{payamConfig.SafetyMarginMs} ms)", Color.Blue);
            }
            catch (Exception ex)
            {
                LogMessage($"Error starting: {ex.Message}", Color.Red);
            }
        }

        /// <summary>
        /// Syncs targetTime / process / clicks from the Console controls currently on screen.
        /// </summary>
        private void ApplyConsoleTargetFromUi(bool logApplied)
        {
            var baseTime = dtpTargetDate.Value.Date + dtpTargetTime.Value.TimeOfDay;
            // DateTimePicker TimeOfDay may already include milliseconds on some cultures; force from nud.
            baseTime = new DateTime(
                baseTime.Year, baseTime.Month, baseTime.Day,
                baseTime.Hour, baseTime.Minute, baseTime.Second, 0, baseTime.Kind);
            targetTime = baseTime.AddMilliseconds((double)nudMilliseconds.Value);

            targetProcess = SafeGetText(txtTargetProcess) ?? "Payam";
            clickCount = (int)nudClickCount.Value;
            clickInterval = (int)nudClickInterval.Value;

            UI(() =>
            {
                lblTargetTime.Text = "Target  ·  " + targetTime.ToString("yyyy/MM/dd HH:mm:ss.fff");
                lblProcessStatus.Text = "Process  ·  " + targetProcess;
                lblClickCount.Text = "Clicks  ·  " + clickCount.ToString();
            });

            if (logApplied)
                LogMessage("Console target applied: " + targetTime.ToString("yyyy/MM/dd HH:mm:ss.fff"), Color.Blue);
        }

        private void BtnStop_Click(object sender, EventArgs e)
        {
            isWaiting = false;
            hasStarted = false;

            try
            {
                if (waitingThread != null && waitingThread.IsAlive)
                    waitingThread.Join(1000);
            }
            catch { }

            UI(() =>
            {
                btnStart.Enabled = true;
                btnStop.Enabled = false;
                btnReadConfig.Enabled = true;
                btnManualConfig.Enabled = true;
                statusLabel.Text = "Operation stopped";
            });

            LogMessage("Operation stopped by user", Color.Orange);
        }

        // -------------------------
        // Live time display
        // -------------------------
        private void DisplayLiveTime()
        {
            int processCheckIntervalMs = 2000;
            int elapsedSinceProcCheck = processCheckIntervalMs;

            while (!stopLiveTime)
            {
                try
                {
                    if (elapsedSinceProcCheck >= processCheckIntervalMs)
                    {
                        bool running = IsProcessRunning(targetProcess);
                        string ps = running ? "Running" : "Not Running";
                        Color pc = running ? AppTheme.Success : AppTheme.Danger;

                        UI(() =>
                        {
                            lblProcessStatus.Text = $"Process  ·  {targetProcess} ({ps})";
                            lblProcessStatus.ForeColor = pc;
                        });

                        elapsedSinceProcCheck = 0;
                    }

                    var now = GetCurrentTime();
                    string source = GetTimeSourceLabel();
                    string payamStatus = payamTimeProvider != null ? payamTimeProvider.Status : "Sync  ·  off";
                    bool payamOk = payamTimeProvider != null && payamTimeProvider.HasPhaseLock;
                    bool payamProv = payamTimeProvider != null && payamTimeProvider.HasSync && !payamOk;

                    // Payam mode: show the exact API NowTime second (no invented .fff).
                    string clockText;
                    if (timeSourceMode == TimeSourceMode.PayamApi && payamTimeProvider != null)
                    {
                        DateTime apiSecond;
                        string apiText;
                        if (payamTimeProvider.TryGetApiSecondTime(out apiSecond, out apiText)
                            && !string.IsNullOrEmpty(apiText))
                            clockText = apiText; // HH:mm:ss from PeriodicData
                        else
                            clockText = now.ToString("HH:mm:ss");
                    }
                    else
                    {
                        clockText = now.ToString("HH:mm:ss.fff");
                    }

                    string remText = "Remaining  —";
                    double progress = 0;
                    string statusText = null;

                    if (hasStarted && isWaiting)
                    {
                        var rem = GetFireThreshold() - now;
                        if (rem.TotalMilliseconds > 0)
                        {
                            string fmt = rem.TotalHours >= 1
                                ? $"{rem.Hours:D2}:{rem.Minutes:D2}:{rem.Seconds:D2}.{rem.Milliseconds:D3}"
                                : $"{rem.Minutes:D2}:{rem.Seconds:D2}.{rem.Milliseconds:D3}";
                            remText = "Remaining  " + fmt;
                            statusText = "Armed · " + fmt;

                            if (waitTotalMs > 1)
                            {
                                double left = rem.TotalMilliseconds;
                                progress = 1.0 - (left / waitTotalMs);
                                if (progress < 0) progress = 0;
                                if (progress > 1) progress = 1;
                            }
                        }
                        else
                        {
                            remText = "Remaining  00:00.000";
                            progress = 1;
                            statusText = "Firing…";
                        }
                    }
                    else if (!hasStarted)
                    {
                        remText = "Remaining  —";
                        progress = 0;
                    }

                    string syncChip = payamStatus;
                    if (!syncChip.StartsWith("Sync", StringComparison.OrdinalIgnoreCase)
                        && !syncChip.StartsWith("Payam", StringComparison.OrdinalIgnoreCase))
                        syncChip = "Sync  ·  " + syncChip;
                    else if (syncChip.StartsWith("Payam", StringComparison.OrdinalIgnoreCase))
                        syncChip = syncChip.Replace("Payam Sync:", "Sync  ·").Replace("Payam synced", "Sync  · locked");

                    UI(() =>
                    {
                        if (lblHeroClock != null)
                            lblHeroClock.Text = clockText;
                        if (lblLiveTime != null)
                            lblLiveTime.Text = $"Live Time: {now:yyyy/MM/dd HH:mm:ss.fff} {source}";
                        if (lblHeroMeta != null)
                        {
                            if (timeSourceMode == TimeSourceMode.PayamApi)
                                lblHeroMeta.Text = now.ToString("yyyy/MM/dd") + "  " + source + "  (second = Payam NowTime)";
                            else
                                lblHeroMeta.Text = now.ToString("yyyy/MM/dd") + "  " + source;
                        }

                        if (lblSyncDot != null)
                        {
                            if (timeSourceMode == TimeSourceMode.PayamApi)
                                lblSyncDot.ForeColor = payamOk ? AppTheme.Success : (payamProv ? AppTheme.Warning : AppTheme.TextMuted);
                            else if (timeSourceMode == TimeSourceMode.Ntp)
                                lblSyncDot.ForeColor = hasNtpSync ? AppTheme.Success : AppTheme.Warning;
                            else
                                lblSyncDot.ForeColor = AppTheme.LogInfo;
                        }

                        if (lblPayamStatus != null)
                        {
                            lblPayamStatus.Text = syncChip;
                            lblPayamStatus.ForeColor = payamOk
                                ? AppTheme.Success
                                : (payamProv ? AppTheme.Warning : AppTheme.TextMuted);
                        }

                        if (lblCountdown != null)
                            lblCountdown.Text = remText;
                        if (progressCountdown != null)
                            progressCountdown.Progress = progress;

                        if (statusText != null)
                            statusLabel.Text = statusText;
                    });

                    Thread.Sleep(50);
                    elapsedSinceProcCheck += 50;
                }
                catch
                {
                    break;
                }
            }
        }

        private string DescribeTimeSourceForLog()
        {
            switch (timeSourceMode)
            {
                case TimeSourceMode.System:
                    return "System";
                case TimeSourceMode.Ntp:
                    return "NTP (" + ntpServer + ")";
                case TimeSourceMode.PayamApi:
                default:
                    string url = payamConfig != null ? payamConfig.ApiUrl : PayamTimeConfig.DefaultApiUrl;
                    return "Payam API (" + url + ")";
            }
        }
    }
}
