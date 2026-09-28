using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AutoClickUI
{
    /// <summary>
    /// Reads Payam PeriodicData NowTime (second precision) over raw TCP/HTTP
    /// with a persistent keep-alive connection and phase-locks a Stopwatch on
    /// each second-edge change for DelayAfterSecondMs scheduling.
    /// </summary>
    public sealed class PayamTimeProvider : IDisposable
    {
        private static readonly Regex NowTimeRegex = new Regex(
            "\"NowTime\"\\s*:\\s*\"(?<t>\\d{1,2}:\\d{2}:\\d{2})\"",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly object _gate = new object();
        private readonly object _ioGate = new object();
        private readonly Stopwatch _stopwatch = new Stopwatch();

        private PayamTimeConfig _config;
        private Thread _worker;
        private volatile bool _running;
        private volatile bool _hasPhaseLock;
        private volatile bool _hasAnyReading;
        private volatile bool _armedFastPoll;
        private DateTime _basePayamLocal;
        private string _lastNowTimeText = string.Empty;
        private string _status = "Not synced";
        private DateTime _lastSuccessUtc = DateTime.MinValue;
        private DateTime _lastPhaseLockUtc = DateTime.MinValue;

        private int _consecutiveFailures;
        private Action<string, bool> _log;
        private int _lastRttMs;
        private int _phaseLockVersion;

        private TcpClient _client;
        private NetworkStream _stream;
        private string _endpointKey = string.Empty;

        public PayamTimeProvider(PayamTimeConfig config, Action<string, bool> log = null)
        {
            _config = config ?? new PayamTimeConfig();
            _log = log;
        }

        public bool HasSync { get { return _hasPhaseLock || _hasAnyReading; } }
        public bool HasPhaseLock { get { return _hasPhaseLock; } }
        public string Status { get { return _status; } }
        public string LastNowTimeText { get { return _lastNowTimeText; } }
        public DateTime LastSuccessUtc { get { return _lastSuccessUtc; } }
        public DateTime LastPhaseLockUtc { get { return _lastPhaseLockUtc; } }
        public int LastRttMs { get { return _lastRttMs; } }
        public int PhaseLockVersion { get { return _phaseLockVersion; } }
        public bool ArmedFastPoll { get { return _armedFastPoll; } }

        public int SafetyMarginMs
        {
            get { lock (_gate) return _config.SafetyMarginMs; }
        }

        public int DelayAfterSecondMs
        {
            get { lock (_gate) return _config.DelayAfterSecondMs; }
        }

        public void UpdateConfig(PayamTimeConfig config)
        {
            if (config == null) return;
            bool endpointChanged;
            lock (_gate)
            {
                endpointChanged = _config == null
                    || !string.Equals(_config.ApiUrl, config.ApiUrl, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(_config.YearCode, config.YearCode, StringComparison.Ordinal)
                    || !string.Equals(_config.ApiKey, config.ApiKey, StringComparison.Ordinal);
                _config = config;
            }
            if (endpointChanged)
            {
                lock (_ioGate)
                    CloseConnection_NoLock();
            }
        }

        /// <summary>When armed, poll at ArmedPollIntervalMs for tighter second-edge detection.</summary>
        public void SetArmedFastPoll(bool armed)
        {
            _armedFastPoll = armed;
            if (armed)
                Log("Payam fast-poll ARMED (" + GetArmedPollMs() + "ms).", false);
            else
                Log("Payam fast-poll idle.", false);
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _worker = new Thread(SyncLoop)
            {
                IsBackground = true,
                Name = "PayamTimeProvider",
                Priority = ThreadPriority.AboveNormal
            };
            _worker.Start();
        }

        public void Stop()
        {
            _running = false;
            _armedFastPoll = false;
            var t = _worker;
            if (t != null && t.IsAlive)
                t.Join(1500);
            _worker = null;
            lock (_ioGate) CloseConnection_NoLock();
        }

        public DateTime GetCurrentTime()
        {
            return GetCurrentTimeCore(applyBias: true);
        }

        /// <summary>Phase-locked Payam clock without ClockBias (for arming / fire scheduling).</summary>
        public DateTime GetCurrentTimeUnbiased()
        {
            return GetCurrentTimeCore(applyBias: false);
        }

        private DateTime GetCurrentTimeCore(bool applyBias)
        {
            lock (_gate)
            {
                if (!_hasPhaseLock && !_hasAnyReading)
                    return DateTime.Now;

                double ms = _stopwatch.Elapsed.TotalMilliseconds;
                if (applyBias)
                    ms -= Math.Max(0, _config.ClockBiasMs);
                return _basePayamLocal.AddMilliseconds(ms);
            }
        }

        public bool TryGetApiSecondTime(out DateTime secondTime, out string nowTimeText)
        {
            lock (_gate)
            {
                nowTimeText = _lastNowTimeText;
                if (!_hasAnyReading || string.IsNullOrEmpty(_lastNowTimeText))
                {
                    secondTime = DateTime.Now;
                    return false;
                }

                secondTime = CombineWithToday(_lastNowTimeText);
                if (_hasPhaseLock || _hasAnyReading)
                {
                    DateTime running = _basePayamLocal.AddMilliseconds(_stopwatch.Elapsed.TotalMilliseconds);
                    if (secondTime.Date != running.Date
                        && Math.Abs((secondTime - running).TotalHours) > 12)
                    {
                        secondTime = new DateTime(running.Year, running.Month, running.Day,
                            secondTime.Hour, secondTime.Minute, secondTime.Second, 0, DateTimeKind.Local);
                    }
                }
                return true;
            }
        }

        public DateTime GetFireThreshold(DateTime targetPayamTime)
        {
            int margin;
            int delay;
            lock (_gate)
            {
                margin = Math.Max(0, _config.SafetyMarginMs);
                delay = Math.Max(0, _config.DelayAfterSecondMs);
            }
            // Prefer explicit delay-after-second when set; else fall back to target ms.
            var second = new DateTime(
                targetPayamTime.Year, targetPayamTime.Month, targetPayamTime.Day,
                targetPayamTime.Hour, targetPayamTime.Minute, targetPayamTime.Second, 0, targetPayamTime.Kind);
            int offset = delay > 0 ? delay : targetPayamTime.Millisecond;
            return second.AddMilliseconds(offset + margin);
        }

        public bool TryGetPhaseLockSnapshot(
            out DateTime lockedSecond,
            out double msSinceLock,
            out string nowTimeText,
            out int lastRttMs,
            out int lockVersion)
        {
            lock (_gate)
            {
                nowTimeText = _lastNowTimeText;
                lastRttMs = _lastRttMs;
                lockVersion = _phaseLockVersion;
                msSinceLock = _stopwatch.IsRunning ? _stopwatch.Elapsed.TotalMilliseconds : 0;
                if (!_hasPhaseLock || string.IsNullOrEmpty(_lastNowTimeText))
                {
                    lockedSecond = DateTime.MinValue;
                    return false;
                }
                lockedSecond = _basePayamLocal;
                lockedSecond = new DateTime(
                    lockedSecond.Year, lockedSecond.Month, lockedSecond.Day,
                    lockedSecond.Hour, lockedSecond.Minute, lockedSecond.Second, 0, lockedSecond.Kind);
                return true;
            }
        }

        public int EstimateEdgeDetectionLagMs()
        {
            int lag = _lastRttMs / 2;
            if (lag < 0) lag = 0;
            if (lag > 60) lag = 60;
            return lag;
        }

        public void Dispose()
        {
            Stop();
        }

        private int GetArmedPollMs()
        {
            lock (_gate)
            {
                int v = _config.ArmedPollIntervalMs;
                if (v < 5) v = 5;
                if (v > 50) v = 50;
                return v;
            }
        }

        private void SyncLoop()
        {
            Log("Payam time sync loop started.", false);

            while (_running)
            {
                int sleepMs = 40;
                try
                {
                    PayamTimeConfig cfg;
                    lock (_gate) cfg = CloneConfig(_config);
                    sleepMs = _armedFastPoll
                        ? GetArmedPollMs()
                        : Math.Max(10, cfg.PollIntervalMs);

                    string json;
                    var rttSw = Stopwatch.StartNew();
                    // When armed, fail fast so a slow socket cannot skip an entire second.
                    int ioTimeoutMs = _armedFastPoll ? 280 : 1500;
                    json = FetchPeriodicDataRaw(cfg, ioTimeoutMs);
                    rttSw.Stop();
                    int rttMs = (int)Math.Max(0, Math.Min(250, rttSw.ElapsedMilliseconds));

                    string nowText;
                    if (!TryExtractNowTime(json, out nowText))
                    {
                        _consecutiveFailures++;
                        SetStatus("Payam sync: NowTime missing in response");
                        Thread.Sleep(sleepMs);
                        continue;
                    }

                    _consecutiveFailures = 0;
                    _lastSuccessUtc = DateTime.UtcNow;
                    _lastRttMs = rttMs;
                    ApplyNowTimeReading(nowText, rttMs);
                }
                catch (Exception ex)
                {
                    _consecutiveFailures++;
                    lock (_ioGate) CloseConnection_NoLock();
                    SetStatus("Payam sync error: " + Truncate(ex.Message, 80));
                    if (_consecutiveFailures == 1 || _consecutiveFailures % 25 == 0)
                        Log("Payam PeriodicData fetch failed: " + ex.Message, true);
                    Thread.Sleep(Math.Max(sleepMs, 150));
                    continue;
                }

                Thread.Sleep(sleepMs);
            }

            lock (_ioGate) CloseConnection_NoLock();
            Log("Payam time sync loop stopped.", false);
        }

        private void ApplyNowTimeReading(string nowText, int rttMs)
        {
            if (!_hasAnyReading)
            {
                DateTime provisional = CombineWithToday(nowText);
                lock (_gate)
                {
                    _basePayamLocal = provisional;
                    _stopwatch.Restart();
                    _lastNowTimeText = nowText;
                    _hasAnyReading = true;
                    _hasPhaseLock = false;
                }
                SetStatus("Payam sync: waiting for second edge...");
                Log("Payam provisional time: " + provisional.ToString("HH:mm:ss", CultureInfo.InvariantCulture), false);
                return;
            }

            if (!string.Equals(nowText, _lastNowTimeText, StringComparison.Ordinal))
            {
                DateTime edge = CombineWithToday(nowText);
                DateTime previous;
                lock (_gate) previous = _basePayamLocal;
                if (edge < previous.AddMinutes(-30))
                    edge = edge.AddDays(1);

                lock (_gate)
                {
                    _basePayamLocal = edge;
                    _stopwatch.Restart();
                    _lastNowTimeText = nowText;
                    _hasPhaseLock = true;
                    _lastPhaseLockUtc = DateTime.UtcNow;
                    _lastRttMs = rttMs;
                    Interlocked.Increment(ref _phaseLockVersion);
                }

                SetStatus("Payam synced (phase-locked @" + nowText + ")");
                Log("Payam phase lock @" + edge.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                    + " (rtt≈" + rttMs + "ms, armed=" + _armedFastPoll + ")", false);
            }
        }

        private static DateTime CombineWithToday(string hhmmss)
        {
            TimeSpan tod;
            if (!TimeSpan.TryParseExact(hhmmss, @"h\:mm\:ss", CultureInfo.InvariantCulture, out tod)
                && !TimeSpan.TryParseExact(hhmmss, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out tod)
                && !TimeSpan.TryParse(hhmmss, CultureInfo.InvariantCulture, out tod))
            {
                throw new FormatException("Invalid NowTime: " + hhmmss);
            }

            DateTime today = DateTime.Today;
            return new DateTime(today.Year, today.Month, today.Day, tod.Hours, tod.Minutes, tod.Seconds, 0, DateTimeKind.Local);
        }

        private void CloseConnection_NoLock()
        {
            try { if (_stream != null) _stream.Close(); } catch { }
            try { if (_client != null) _client.Close(); } catch { }
            _stream = null;
            _client = null;
            _endpointKey = string.Empty;
        }

        /// <summary>
        /// Minimal raw HTTP/1.1 GET — only Host / YearCode / X-API-KEY / Connection: close.
        /// Extra headers (User-Agent, Accept, keep-alive, …) cause Payam HTTP 400.
        /// </summary>
        internal static string FetchPeriodicDataRaw(PayamTimeConfig cfg, int timeoutMs = 1500)
        {
            if (cfg == null) throw new ArgumentNullException("cfg");
            if (string.IsNullOrWhiteSpace(cfg.ApiUrl))
                throw new InvalidOperationException("Payam ApiUrl is empty.");
            if (timeoutMs < 100) timeoutMs = 100;
            if (timeoutMs > 5000) timeoutMs = 5000;

            Uri uri = new Uri(cfg.ApiUrl);
            if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Only http:// Payam API URLs are supported for raw TcpClient sync.");

            string host = uri.Host;
            int port = uri.IsDefaultPort ? 80 : uri.Port;
            string path = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;

            var req = new StringBuilder(256);
            req.Append("GET ").Append(path).Append(" HTTP/1.1\r\n");
            req.Append("Host: ").Append(host).Append(':').Append(port).Append("\r\n");
            req.Append("YearCode: ").Append(cfg.YearCode ?? string.Empty).Append("\r\n");
            req.Append("X-API-KEY: ").Append(cfg.ApiKey ?? string.Empty).Append("\r\n");
            req.Append("Connection: close\r\n");
            req.Append("\r\n");

            byte[] requestBytes = Encoding.ASCII.GetBytes(req.ToString());

            using (var client = new TcpClient())
            {
                client.NoDelay = true;
                client.ReceiveBufferSize = 4096;
                client.SendBufferSize = 1024;
                client.ReceiveTimeout = timeoutMs;
                client.SendTimeout = timeoutMs;

                var connectResult = client.BeginConnect(host, port, null, null);
                if (!connectResult.AsyncWaitHandle.WaitOne(timeoutMs))
                    throw new TimeoutException("Connect timeout to " + host + ":" + port);
                client.EndConnect(connectResult);

                using (NetworkStream stream = client.GetStream())
                {
                    stream.Write(requestBytes, 0, requestBytes.Length);
                    stream.Flush();

                    using (var ms = new MemoryStream())
                    {
                        var buffer = new byte[4096];
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                            ms.Write(buffer, 0, read);

                        string responseText = Encoding.UTF8.GetString(ms.ToArray());
                        return ExtractHttpBody(responseText);
                    }
                }
            }
        }

        private static string ExtractHttpBody(string responseText)
        {
            if (string.IsNullOrEmpty(responseText))
                throw new InvalidOperationException("Empty HTTP response from Payam.");

            int statusEnd = responseText.IndexOf("\r\n", StringComparison.Ordinal);
            string statusLine = statusEnd > 0 ? responseText.Substring(0, statusEnd) : responseText;
            if (statusLine.IndexOf(" 200 ", StringComparison.Ordinal) < 0
                && !statusLine.EndsWith(" 200", StringComparison.Ordinal))
            {
                string hint = string.Empty;
                if (statusLine.IndexOf("400", StringComparison.Ordinal) >= 0)
                    hint = " — check Settings → X-API-KEY / YearCode";
                throw new InvalidOperationException("Payam HTTP status: " + statusLine + hint);
            }

            int sep = responseText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (sep < 0)
                throw new InvalidOperationException("Malformed HTTP response (no header separator).");

            return responseText.Substring(sep + 4).Trim();
        }

        internal static bool TryExtractNowTime(string json, out string nowTime)
        {
            nowTime = null;
            if (string.IsNullOrEmpty(json)) return false;
            Match m = NowTimeRegex.Match(json);
            if (!m.Success) return false;
            nowTime = m.Groups["t"].Value;
            return !string.IsNullOrEmpty(nowTime);
        }

        private void SetStatus(string status)
        {
            _status = status ?? string.Empty;
        }

        private void Log(string message, bool isError)
        {
            var handler = _log;
            if (handler != null)
                handler(message, isError);
        }

        private static PayamTimeConfig CloneConfig(PayamTimeConfig src)
        {
            return new PayamTimeConfig
            {
                ApiUrl = src.ApiUrl,
                YearCode = src.YearCode,
                ApiKey = src.ApiKey,
                SafetyMarginMs = src.SafetyMarginMs,
                ClockBiasMs = src.ClockBiasMs,
                PollIntervalMs = src.PollIntervalMs,
                DelayAfterSecondMs = src.DelayAfterSecondMs,
                ArmedPollIntervalMs = src.ArmedPollIntervalMs
            };
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max) + "...";
        }
    }
}
