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
        private volatile bool useSystemTime = false;
        private DateTime ntpBaseTime;
        private Stopwatch ntpStopwatch = new Stopwatch();

        // threading
        private Thread liveTimeThread;
        private Thread waitingThread;
        private volatile bool stopLiveTime = false;
        private volatile bool isWaiting = false;
        private volatile bool hasStarted = false;

        // file watcher
        private FileSystemWatcher ntpWatcher;

        // -------------------------
        // UI Controls
        // -------------------------
        private Label lblLiveTime;
        private Label lblTargetTime;
        private Label lblProcessStatus;
        private Label lblConfigStatus;
        private Label lblNtpStatus;
        private Label lblClickCount;

        private TextBox txtConfigFolder;
        private TextBox txtLogFolder;
        private TextBox txtTargetProcess;
        private TextBox txtNtpServer;

        private NumericUpDown nudMilliseconds;
        private NumericUpDown nudClickCount;
        private NumericUpDown nudClickInterval;

        private DateTimePicker dtpTargetDate;
        private DateTimePicker dtpTargetTime;

        private Button btnSyncNtp;
        private Button btnStart;
        private Button btnStop;
        private Button btnBrowseConfig;
        private Button btnBrowseLog;
        private Button btnManualConfig;
        private Button btnReadConfig;

        private TabControl tabControl;
        private TabPage tabMain;
        private TabPage tabSettings;
        private TabPage tabLogs;

        private RichTextBox rtbLogs;
        private CheckBox chkUseSystemTime;

        private GroupBox gbStatus;
        private GroupBox gbSettings;
        private GroupBox gbActions;

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

            // 1) Always load NTP server from ntp_config.txt (authoritative)
            LoadNtpServerFromFile(overwriteTextbox: true);

            // 2) Start watcher so any manual edit of ntp_config.txt takes effect automatically
            SetupNtpConfigWatcher();

            // 3) Auto sync NTP on startup (unless user chose system time)
            ThreadPool.QueueUserWorkItem(_ =>
            {
                LogMessage("Auto NTP sync on startup...", Color.Blue);
                SyncWithNtpServer();
            });
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopAllOperations();
            DisposeWatcher();

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
            if (useSystemTime || !hasNtpSync)
                return DateTime.Now;

            // ntpBaseTime is in local Iran time (as you already used)
            return ntpBaseTime.AddMilliseconds(ntpStopwatch.Elapsed.TotalMilliseconds);
        }

        private void ApplyNtpSync(DateTime ntpTimeLocal)
        {
            ntpBaseTime = ntpTimeLocal;
            ntpStopwatch.Restart();
            hasNtpSync = true;
            useSystemTime = false;

            LogMessage($"NTP synced. Base NTP time: {ntpBaseTime:yyyy/MM/dd HH:mm:ss.fff}", Color.Green);

            UI(() =>
            {
                lblNtpStatus.Text = $"NTP Status: Synced with {ntpServer}";
                lblNtpStatus.ForeColor = Color.Green;
                txtNtpServer.Text = ntpServer;
            });
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
                        if (!useSystemTime)
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
                if (useSystemTime)
                {
                    LogMessage("Skipping NTP sync because 'Use System Time' is enabled.", Color.Orange);
                    UI(() =>
                    {
                        lblNtpStatus.Text = "NTP Status: Disabled";
                        lblNtpStatus.ForeColor = Color.Orange;
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
                        lblNtpStatus.ForeColor = Color.Red;
                    });
                    return;
                }

                UI(() =>
                {
                    lblNtpStatus.Text = $"NTP Status: Syncing with {ntpServer} ...";
                    lblNtpStatus.ForeColor = Color.Blue;
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
            // - else use system time
            if (hasNtpSync)
            {
                LogMessage("NTP unavailable; keeping last NTP base time.", Color.Orange);
                return ntpBaseTime.AddMilliseconds(ntpStopwatch.Elapsed.TotalMilliseconds);
            }

            LogMessage("NTP unavailable; falling back to system time.", Color.Red);
            useSystemTime = true;
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
                    double remainingMs = (targetTime - now).TotalMilliseconds;

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
                sb.AppendLine($"NTP Server: {(useSystemTime ? "Disabled(System)" : ntpServer)}");
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
                    rtbLogs.SelectionColor = color ?? Color.Black;
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

                return task.Wait(TimeSpan.FromSeconds(1)) && task.Result;
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
            // Form Settings
            this.Text = "PayamAutoClick v2.2";
            this.Icon = PayamAutoClick.Properties.Resources.Icon1;

            this.Size = new Size(600, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.FormClosing += MainForm_FormClosing;

            tabControl = new TabControl();
            tabControl.Dock = DockStyle.Fill;

            tabMain = new TabPage("Main");
            tabSettings = new TabPage("Settings");
            tabLogs = new TabPage("Logs");

            // Status Group
            gbStatus = new GroupBox();
            gbStatus.Text = "Status";
            gbStatus.Location = new Point(10, 10);
            gbStatus.Size = new Size(550, 150);

            lblLiveTime = new Label { Location = new Point(10, 25), Size = new Size(530, 20), Text = "Live Time: Starting..." };
            lblTargetTime = new Label { Location = new Point(10, 50), Size = new Size(530, 20), Text = "Target Time: Not set" };
            lblProcessStatus = new Label { Location = new Point(10, 75), Size = new Size(530, 20), Text = "Target Process: Not set" };
            lblClickCount = new Label { Location = new Point(10, 100), Size = new Size(530, 20), Text = "Click Count: 1" };
            lblConfigStatus = new Label { Location = new Point(10, 125), Size = new Size(530, 20), Text = "Config Status: Not Set" };

            gbStatus.Controls.Add(lblLiveTime);
            gbStatus.Controls.Add(lblTargetTime);
            gbStatus.Controls.Add(lblProcessStatus);
            gbStatus.Controls.Add(lblClickCount);
            gbStatus.Controls.Add(lblConfigStatus);

            tabMain.Controls.Add(gbStatus);

            // Actions Group
            gbActions = new GroupBox();
            gbActions.Text = "Actions";
            gbActions.Location = new Point(10, 170);
            gbActions.Size = new Size(550, 250);

            Label lblSetDate = new Label { Text = "Target Date:", Location = new Point(10, 25), Size = new Size(100, 20) };
            dtpTargetDate = new DateTimePicker { Location = new Point(110, 25), Size = new Size(150, 20), Format = DateTimePickerFormat.Short, Value = DateTime.Today };

            Label lblSetTime = new Label { Text = "Target Time:", Location = new Point(270, 25), Size = new Size(100, 20) };
            dtpTargetTime = new DateTimePicker { Location = new Point(370, 25), Size = new Size(150, 20), Format = DateTimePickerFormat.Time, ShowUpDown = true, Value = DateTime.Now.AddMinutes(1) };

            Label lblProc = new Label { Text = "Target Process:", Location = new Point(10, 55), Size = new Size(100, 20) };
            txtTargetProcess = new TextBox { Location = new Point(110, 55), Size = new Size(150, 20), Text = "Payam" };

            Label lblMs = new Label { Text = "Milliseconds:", Location = new Point(270, 55), Size = new Size(100, 20) };
            nudMilliseconds = new NumericUpDown { Location = new Point(370, 55), Size = new Size(150, 20), Minimum = 0, Maximum = 999, Value = 0 };

            Label lblCount = new Label { Text = "Click Count:", Location = new Point(10, 85), Size = new Size(100, 20) };
            nudClickCount = new NumericUpDown { Location = new Point(110, 85), Size = new Size(150, 20), Minimum = 1, Maximum = 500, Value = 1 };
            nudClickCount.ValueChanged += (s, e) =>
            {
                nudClickInterval.Enabled = nudClickCount.Value > 1;
                if (nudClickCount.Value <= 1) nudClickInterval.Value = 0;
            };

            Label lblInterval = new Label { Text = "Click Interval (ms):", Location = new Point(270, 85), Size = new Size(110, 20) };
            nudClickInterval = new NumericUpDown { Location = new Point(370, 85), Size = new Size(150, 20), Minimum = 0, Maximum = 60000, Value = 0, Enabled = false };

            chkUseSystemTime = new CheckBox { Text = "Use System Time (No NTP)", Location = new Point(10, 115), Size = new Size(220, 20) };
            chkUseSystemTime.CheckedChanged += (s, e) =>
            {
                useSystemTime = chkUseSystemTime.Checked;
                txtNtpServer.Enabled = !useSystemTime;
                btnSyncNtp.Enabled = !useSystemTime;

                if (useSystemTime)
                {
                    LogMessage("Using system time (NTP disabled).", Color.Orange);
                    UI(() =>
                    {
                        lblNtpStatus.Text = "NTP Status: Disabled";
                        lblNtpStatus.ForeColor = Color.Orange;
                    });
                }
                else
                {
                    LogMessage("Using NTP time.", Color.Blue);
                    ThreadPool.QueueUserWorkItem(_ => SyncWithNtpServer());
                }
            };

            btnReadConfig = new Button { Text = "Read Config", Location = new Point(10, 145), Size = new Size(260, 30) };
            btnReadConfig.Click += BtnReadConfig_Click;

            btnManualConfig = new Button { Text = "Use Manual Settings", Location = new Point(280, 145), Size = new Size(260, 30) };
            btnManualConfig.Click += BtnManualConfig_Click;

            btnStart = new Button { Text = "Start", Location = new Point(10, 185), Size = new Size(260, 30) };
            btnStart.Click += BtnStart_Click;

            btnStop = new Button { Text = "Stop", Location = new Point(280, 185), Size = new Size(260, 30), Enabled = false };
            btnStop.Click += BtnStop_Click;

            gbActions.Controls.Add(lblSetDate);
            gbActions.Controls.Add(dtpTargetDate);
            gbActions.Controls.Add(lblSetTime);
            gbActions.Controls.Add(dtpTargetTime);
            gbActions.Controls.Add(lblProc);
            gbActions.Controls.Add(txtTargetProcess);
            gbActions.Controls.Add(lblMs);
            gbActions.Controls.Add(nudMilliseconds);
            gbActions.Controls.Add(lblCount);
            gbActions.Controls.Add(nudClickCount);
            gbActions.Controls.Add(lblInterval);
            gbActions.Controls.Add(nudClickInterval);
            gbActions.Controls.Add(chkUseSystemTime);
            gbActions.Controls.Add(btnReadConfig);
            gbActions.Controls.Add(btnManualConfig);
            gbActions.Controls.Add(btnStart);
            gbActions.Controls.Add(btnStop);

            tabMain.Controls.Add(gbActions);

            // Settings tab
            gbSettings = new GroupBox { Text = "Settings", Location = new Point(10, 10), Size = new Size(550, 150) };

            Label lblConfigFolder = new Label { Text = "Config Folder:", Location = new Point(10, 25), Size = new Size(100, 20) };
            txtConfigFolder = new TextBox { Location = new Point(110, 25), Size = new Size(350, 20), Text = configFolder };
            btnBrowseConfig = new Button { Text = "Browse", Location = new Point(470, 25), Size = new Size(70, 20) };
            btnBrowseConfig.Click += (s, e) =>
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

            Label lblLogFolder = new Label { Text = "Log Folder:", Location = new Point(10, 55), Size = new Size(100, 20) };
            txtLogFolder = new TextBox { Location = new Point(110, 55), Size = new Size(350, 20), Text = logFolder };
            btnBrowseLog = new Button { Text = "Browse", Location = new Point(470, 55), Size = new Size(70, 20) };
            btnBrowseLog.Click += (s, e) =>
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

            Label lblNtpSrv = new Label { Text = "NTP Server:", Location = new Point(10, 85), Size = new Size(100, 20) };
            txtNtpServer = new TextBox { Location = new Point(110, 85), Size = new Size(430, 20), Text = ntpServer };

            btnSyncNtp = new Button { Text = "Sync NTP", Location = new Point(400, 110), Size = new Size(140, 25) };
            btnSyncNtp.Click += (s, e) =>
            {
                var server = SafeGetText(txtNtpServer);
                if (!IsValidNtpServerAddress(server))
                {
                    LogMessage($"Invalid NTP server address: {server}", Color.Red);
                    UI(() =>
                    {
                        lblNtpStatus.Text = "NTP Status: Invalid server address";
                        lblNtpStatus.ForeColor = Color.Red;
                    });
                    return;
                }

                // User override: save to authoritative file, then sync using file
                ntpServer = server;
                SaveNtpServerToFile(ntpServer);

                ThreadPool.QueueUserWorkItem(_ => SyncWithNtpServer());
            };

            lblNtpStatus = new Label { Location = new Point(10, 115), Size = new Size(530, 20), Text = "NTP Status: (not synced)" };

            gbSettings.Controls.Add(lblConfigFolder);
            gbSettings.Controls.Add(txtConfigFolder);
            gbSettings.Controls.Add(btnBrowseConfig);
            gbSettings.Controls.Add(lblLogFolder);
            gbSettings.Controls.Add(txtLogFolder);
            gbSettings.Controls.Add(btnBrowseLog);
            gbSettings.Controls.Add(lblNtpSrv);
            gbSettings.Controls.Add(txtNtpServer);
            gbSettings.Controls.Add(btnSyncNtp);
            gbSettings.Controls.Add(lblNtpStatus);

            tabSettings.Controls.Add(gbSettings);

            // Logs tab
            rtbLogs = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, Font = new Font("Consolas", 9F) };
            tabLogs.Controls.Add(rtbLogs);

            tabControl.TabPages.Add(tabMain);
            tabControl.TabPages.Add(tabSettings);
            tabControl.TabPages.Add(tabLogs);

            this.Controls.Add(tabControl);

            statusStrip = new StatusStrip();
            statusLabel = new ToolStripStatusLabel { Text = "Ready" };
            statusStrip.Items.Add(statusLabel);
            this.Controls.Add(statusStrip);

            // Raise priority for better scheduling
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High; } catch { }

            // start live time thread
            liveTimeThread = new Thread(DisplayLiveTime) { IsBackground = true };
            liveTimeThread.Start();
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
                        lblConfigStatus.Text = "Not Set";
                        statusLabel.Text = "Invalid configuration path";
                    });
                    return;
                }

                string machineName = Environment.MachineName;
                string configPath = Path.Combine(configFolder, $"{machineName}_config.txt");

                LogMessage($"Checking access to config folder: {configFolder}", Color.Blue);
                if (!IsDirectoryAccessible(configFolder))
                {
                    LogMessage($"Cannot access configuration folder: {configFolder}", Color.Red);
                    UI(() =>
                    {
                        MessageBox.Show("Cannot access the configuration folder.", "Access Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        lblConfigStatus.Text = "Not Set";
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
                        lblConfigStatus.Text = "Not Set";
                        statusLabel.Text = "Configuration file not found";
                    });
                    return;
                }

                UI(() =>
                {
                    lblConfigStatus.Text = Path.GetFileNameWithoutExtension(configPath) + " Found";
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
                                lblTargetTime.Text = $"Target Time: {targetTime:yyyy/MM/dd HH:mm:ss.fff}";
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
                                lblProcessStatus.Text = $"Target Process: {targetProcess}";
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
                                lblClickCount.Text = $"Click Count: {clickCount}";
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
                var baseTime = dtpTargetDate.Value.Date + dtpTargetTime.Value.TimeOfDay;
                targetTime = baseTime.AddMilliseconds((double)nudMilliseconds.Value);

                targetProcess = SafeGetText(txtTargetProcess) ?? "Payam";
                clickCount = (int)nudClickCount.Value;
                clickInterval = (int)nudClickInterval.Value;

                UI(() =>
                {
                    lblTargetTime.Text = $"Target Time: {targetTime:yyyy/MM/dd HH:mm:ss.fff}";
                    lblProcessStatus.Text = $"Target Process: {targetProcess}";
                    lblClickCount.Text = $"Click Count: {clickCount}";
                    lblConfigStatus.Text = "Using Manual Settings";
                    lblConfigStatus.ForeColor = Color.Green;
                });

                LogMessage($"Manual settings applied. Target: {targetTime:yyyy/MM/dd HH:mm:ss.fff}", Color.Blue);

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
                var now = GetCurrentTime();
                if (targetTime <= now)
                {
                    MessageBox.Show("Target time must be in the future!", "Invalid Time", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    LogMessage("Cannot start: Target time must be in the future!", Color.Red);
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

                waitingThread = new Thread(WaitForTargetTime) { IsBackground = true };
                waitingThread.Start();

                UI(() =>
                {
                    btnStart.Enabled = false;
                    btnStop.Enabled = true;
                    btnReadConfig.Enabled = false;
                    btnManualConfig.Enabled = false;
                    statusLabel.Text = "Waiting for target time...";
                });

                LogMessage($"Waiting for target time: {targetTime:yyyy/MM/dd HH:mm:ss.fff}", Color.Blue);
            }
            catch (Exception ex)
            {
                LogMessage($"Error starting: {ex.Message}", Color.Red);
            }
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
                        Color pc = running ? Color.Green : Color.Red;

                        UI(() =>
                        {
                            lblProcessStatus.Text = $"Target Process: {targetProcess} ({ps})";
                            lblProcessStatus.ForeColor = pc;
                        });

                        elapsedSinceProcCheck = 0;
                    }

                    var now = GetCurrentTime();
                    UI(() =>
                    {
                        string source = useSystemTime || !hasNtpSync ? "(System)" : $"(NTP: {ntpServer})";
                        lblLiveTime.Text = $"Live Time: {now:yyyy/MM/dd HH:mm:ss.fff} {source}";

                        if (hasStarted && isWaiting)
                        {
                            var rem = targetTime - now;
                            if (rem.TotalMilliseconds > 0)
                            {
                                string fmt = rem.TotalHours >= 1
                                    ? $"{rem.Hours:D2}:{rem.Minutes:D2}:{rem.Seconds:D2}.{rem.Milliseconds:D3}"
                                    : $"{rem.Minutes:D2}:{rem.Seconds:D2}.{rem.Milliseconds:D3}";

                                statusLabel.Text = $"Waiting... Remaining: {fmt}";
                            }
                        }
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
    }
}
