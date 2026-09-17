using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AutoClickUI
{
    /// <summary>
    /// Editable Payam PeriodicData sync settings (session token lives in X-Content-Type-Options).
    /// </summary>
    public sealed class PayamTimeConfig
    {
        public const string DefaultApiUrl = "http://77.36.153.35:84/api/Main/PeriodicData";
        public const string DefaultYearCode = "0";
        public const string DefaultContentTypeOptions = "54I_s";
        public const int DefaultSafetyMarginMs = 10;
        public const int DefaultPollIntervalMs = 25;
        /// <summary>Subtract from live Payam clock so AutoClick never leads the Payam UI.</summary>
        public const int DefaultClockBiasMs = 60;

        public string ApiUrl { get; set; } = DefaultApiUrl;
        public string YearCode { get; set; } = DefaultYearCode;
        public string ContentTypeOptions { get; set; } = DefaultContentTypeOptions;

        /// <summary>Positive delay after target Payam time before F12 (0..50 typical).</summary>
        public int SafetyMarginMs { get; set; } = DefaultSafetyMarginMs;

        public int PollIntervalMs { get; set; } = DefaultPollIntervalMs;

        /// <summary>
        /// Milliseconds to hold our clock behind the raw phase-lock
        /// (fixes AutoClick appearing ahead of the Payam window).
        /// </summary>
        public int ClockBiasMs { get; set; } = DefaultClockBiasMs;

        public static string DefaultConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "payam_time_config.txt");

        public static PayamTimeConfig Load(string path = null)
        {
            var cfg = new PayamTimeConfig();
            path = string.IsNullOrWhiteSpace(path) ? DefaultConfigPath : path;

            try
            {
                if (!File.Exists(path))
                {
                    cfg.Save(path);
                    return cfg;
                }

                foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var line = (raw ?? string.Empty).Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();

                    if (key.Equals("ApiUrl", StringComparison.OrdinalIgnoreCase))
                        cfg.ApiUrl = string.IsNullOrWhiteSpace(value) ? DefaultApiUrl : value;
                    else if (key.Equals("YearCode", StringComparison.OrdinalIgnoreCase))
                        cfg.YearCode = value ?? DefaultYearCode;
                    else if (key.Equals("X-Content-Type-Options", StringComparison.OrdinalIgnoreCase)
                             || key.Equals("ContentTypeOptions", StringComparison.OrdinalIgnoreCase))
                        cfg.ContentTypeOptions = value ?? DefaultContentTypeOptions;
                    else if (key.Equals("SafetyMarginMs", StringComparison.OrdinalIgnoreCase))
                    {
                        int margin;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out margin))
                            cfg.SafetyMarginMs = Clamp(margin, 0, 500);
                    }
                    else if (key.Equals("PollIntervalMs", StringComparison.OrdinalIgnoreCase))
                    {
                        int poll;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out poll))
                            cfg.PollIntervalMs = Clamp(poll, 10, 1000);
                    }
                    else if (key.Equals("ClockBiasMs", StringComparison.OrdinalIgnoreCase)
                             || key.Equals("SyncLagMs", StringComparison.OrdinalIgnoreCase))
                    {
                        int bias;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out bias))
                            cfg.ClockBiasMs = Clamp(bias, 0, 300);
                    }
                }
            }
            catch
            {
                // Keep defaults on read failure.
            }

            return cfg;
        }

        public void Save(string path = null)
        {
            path = string.IsNullOrWhiteSpace(path) ? DefaultConfigPath : path;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("# Payam API time sync (edit YearCode / X-Content-Type-Options per session)");
            sb.AppendLine("ApiUrl=" + (ApiUrl ?? DefaultApiUrl));
            sb.AppendLine("YearCode=" + (YearCode ?? DefaultYearCode));
            sb.AppendLine("X-Content-Type-Options=" + (ContentTypeOptions ?? DefaultContentTypeOptions));
            sb.AppendLine("SafetyMarginMs=" + Clamp(SafetyMarginMs, 0, 500).ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("ClockBiasMs=" + Clamp(ClockBiasMs, 0, 300).ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("PollIntervalMs=" + Clamp(PollIntervalMs, 10, 1000).ToString(CultureInfo.InvariantCulture));
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        public void ApplyFrom(PayamTimeConfig other)
        {
            if (other == null) return;
            ApiUrl = other.ApiUrl;
            YearCode = other.YearCode;
            ContentTypeOptions = other.ContentTypeOptions;
            SafetyMarginMs = other.SafetyMarginMs;
            ClockBiasMs = other.ClockBiasMs;
            PollIntervalMs = other.PollIntervalMs;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
