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
    /// and phase-locks a Stopwatch on each second-edge change for ~ms resolution.
    /// </summary>
    public sealed class PayamTimeProvider : IDisposable
    {
        private static readonly Regex NowTimeRegex = new Regex(
            "\"NowTime\"\\s*:\\s*\"(?<t>\\d{1,2}:\\d{2}:\\d{2})\"",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly object _gate = new object();
        private readonly Stopwatch _stopwatch = new Stopwatch();

        private PayamTimeConfig _config;
        private Thread _worker;
        private volatile bool _running;
        private volatile bool _hasPhaseLock;
        private volatile bool _hasAnyReading;
        private DateTime _basePayamLocal;
        private string _lastNowTimeText = string.Empty;
        private string _status = "Not synced";
        private DateTime _lastSuccessUtc = DateTime.MinValue;
        private DateTime _lastPhaseLockUtc = DateTime.MinValue;

        private int _consecutiveFailures;
        private Action<string, bool> _log;

        public PayamTimeProvider(PayamTimeConfig config, Action<string, bool> log = null)
        {
            _config = config ?? new PayamTimeConfig();
            _log = log;
        }

        public bool HasSync => _hasPhaseLock || _hasAnyReading;

        public bool HasPhaseLock => _hasPhaseLock;

        public string Status
        {
            get { return _status; }
        }

        public string LastNowTimeText
        {
            get { return _lastNowTimeText; }
        }

        public DateTime LastSuccessUtc
        {
            get { return _lastSuccessUtc; }
        }

        public DateTime LastPhaseLockUtc
        {
            get { return _lastPhaseLockUtc; }
        }

        public int SafetyMarginMs
        {
            get
            {
                lock (_gate) return _config.SafetyMarginMs;
            }
        }

        public void UpdateConfig(PayamTimeConfig config)
        {
            if (config == null) return;
            lock (_gate)
            {
                _config = config;
            }
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
            var t = _worker;
            if (t != null && t.IsAlive)
                t.Join(1500);
            _worker = null;
        }

        public DateTime GetCurrentTime()
        {
            lock (_gate)
            {
                if (!_hasPhaseLock && !_hasAnyReading)
                    return DateTime.Now;

                // ClockBiasMs pulls our clock behind Payam so Live Time / F12 never lead the Payam UI.
                int bias = Math.Max(0, _config.ClockBiasMs);
                return _basePayamLocal.AddMilliseconds(_stopwatch.Elapsed.TotalMilliseconds - bias);
            }
        }

        /// <summary>
        /// Exact second currently reported by Payam PeriodicData (no invented milliseconds).
        /// This matches the NowTime string exchanged by Payam.
        /// </summary>
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
                // Keep calendar day consistent with running model near midnight.
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

        /// <summary>
        /// Effective fire threshold: target Payam time + positive safety margin.
        /// </summary>
        public DateTime GetFireThreshold(DateTime targetPayamTime)
        {
            int margin;
            lock (_gate) margin = Math.Max(0, _config.SafetyMarginMs);
            return targetPayamTime.AddMilliseconds(margin);
        }

        public void Dispose()
        {
            Stop();
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
                    sleepMs = Math.Max(10, cfg.PollIntervalMs);

                    string json;
                    var rttSw = Stopwatch.StartNew();
                    json = FetchPeriodicDataRaw(cfg);
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
                    ApplyNowTimeReading(nowText, rttMs);
                }
                catch (Exception ex)
                {
                    _consecutiveFailures++;
                    SetStatus("Payam sync error: " + Truncate(ex.Message, 80));
                    if (_consecutiveFailures == 1 || _consecutiveFailures % 25 == 0)
                        Log("Payam PeriodicData fetch failed: " + ex.Message, true);
                    Thread.Sleep(Math.Max(sleepMs, 200));
                    continue;
                }

                Thread.Sleep(sleepMs);
            }

            Log("Payam time sync loop stopped.", false);
        }

        private void ApplyNowTimeReading(string nowText, int rttMs)
        {
            // First reading: provisional lock at second boundary until edge arrives.
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

            // Second-edge phase lock (and continuous re-lock to correct drift).
            if (!string.Equals(nowText, _lastNowTimeText, StringComparison.Ordinal))
            {
                DateTime edge = CombineWithToday(nowText);
                // Handle midnight wrap relative to previous reading.
                DateTime previous;
                lock (_gate) previous = _basePayamLocal;
                if (edge < previous.AddMinutes(-30))
                    edge = edge.AddDays(1);

                lock (_gate)
                {
                    // Lock at the second edge; ClockBiasMs in GetCurrentTime keeps us behind Payam UI.
                    _basePayamLocal = edge;
                    _stopwatch.Restart();
                    _lastNowTimeText = nowText;
                    _hasPhaseLock = true;
                    _lastPhaseLockUtc = DateTime.UtcNow;
                }

                SetStatus("Payam synced (phase-locked @" + nowText + ")");
                Log("Payam phase lock at " + edge.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)
                    + " (rtt≈" + rttMs + "ms, bias=" + _config.ClockBiasMs + "ms)", false);
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

        /// <summary>
        /// Minimal raw HTTP/1.1 GET — no User-Agent/Accept (Payam returns 400 if extras are present).
        /// Uses a short-lived connection with TCP_NODELAY for lower second-edge latency.
        /// </summary>
        internal static string FetchPeriodicDataRaw(PayamTimeConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException("cfg");
            if (string.IsNullOrWhiteSpace(cfg.ApiUrl))
                throw new InvalidOperationException("Payam ApiUrl is empty.");

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
            req.Append("X-Content-Type-Options: ").Append(cfg.ContentTypeOptions ?? string.Empty).Append("\r\n");
            // close is required for simple framing; reconnect cost is still lower than HttpClient overhead.
            req.Append("Connection: close\r\n");
            req.Append("\r\n");

            byte[] requestBytes = Encoding.ASCII.GetBytes(req.ToString());

            using (var client = new TcpClient())
            {
                // Faster connect + tiny buffers for a small JSON payload.
                client.NoDelay = true;
                client.ReceiveBufferSize = 4096;
                client.SendBufferSize = 1024;
                client.ReceiveTimeout = 1500;
                client.SendTimeout = 1500;

                var connectResult = client.BeginConnect(host, port, null, null);
                if (!connectResult.AsyncWaitHandle.WaitOne(1500))
                    throw new TimeoutException("Connect timeout to " + host + ":" + port);
                client.EndConnect(connectResult);

                using (NetworkStream stream = client.GetStream())
                {
                    stream.Write(requestBytes, 0, requestBytes.Length);
                    stream.Flush();

                    string responseText = ReadAsciiResponse(stream);
                    return ExtractHttpBody(responseText);
                }
            }
        }

        private static string ReadAsciiResponse(NetworkStream stream)
        {
            using (var ms = new MemoryStream())
            {
                var buffer = new byte[4096];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    ms.Write(buffer, 0, read);

                return Encoding.UTF8.GetString(ms.ToArray());
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
                throw new InvalidOperationException("Payam HTTP status: " + statusLine);
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
                ContentTypeOptions = src.ContentTypeOptions,
                SafetyMarginMs = src.SafetyMarginMs,
                ClockBiasMs = src.ClockBiasMs,
                PollIntervalMs = src.PollIntervalMs
            };
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max) + "...";
        }
    }
}
