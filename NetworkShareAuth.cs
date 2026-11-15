using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoClickUI
{
    /// <summary>
    /// Local credentials used to authenticate to the Payam config UNC share
    /// (needed when the app runs elevated and Windows does not reuse explorer credentials).
    /// </summary>
    public sealed class ShareAuthConfig
    {
        public const string DefaultShareRoot = @"\\irn-st10\payamconf";

        public string ShareRoot { get; set; } = DefaultShareRoot;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;

        public static string DefaultConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "share_auth_config.txt");

        public string EffectiveUserName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Username))
                    return string.Empty;
                if (string.IsNullOrWhiteSpace(Domain))
                    return Username.Trim();
                if (Username.IndexOf('\\') >= 0 || Username.IndexOf('@') >= 0)
                    return Username.Trim();
                return Domain.Trim() + "\\" + Username.Trim();
            }
        }

        public static ShareAuthConfig Load(string path = null)
        {
            var cfg = new ShareAuthConfig();
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

                    if (key.Equals("ShareRoot", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("RemotePath", StringComparison.OrdinalIgnoreCase))
                        cfg.ShareRoot = string.IsNullOrWhiteSpace(value) ? DefaultShareRoot : value.TrimEnd('\\');
                    else if (key.Equals("Username", StringComparison.OrdinalIgnoreCase)
                             || key.Equals("User", StringComparison.OrdinalIgnoreCase))
                        cfg.Username = value ?? string.Empty;
                    else if (key.Equals("Password", StringComparison.OrdinalIgnoreCase)
                             || key.Equals("Pass", StringComparison.OrdinalIgnoreCase))
                        cfg.Password = DecodePassword(value);
                    else if (key.Equals("Domain", StringComparison.OrdinalIgnoreCase))
                        cfg.Domain = value ?? string.Empty;
                    else if (key.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
                    {
                        bool enabled;
                        if (bool.TryParse(value, out enabled))
                            cfg.Enabled = enabled;
                        else if (value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase))
                            cfg.Enabled = true;
                        else if (value == "0" || value.Equals("no", StringComparison.OrdinalIgnoreCase))
                            cfg.Enabled = false;
                    }
                }
            }
            catch
            {
                // keep defaults
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
            sb.AppendLine("# Network share auth for Payam config (local to this PC)");
            sb.AppendLine("# Used when running as Administrator so UNC access works without manual explorer login.");
            sb.AppendLine("Enabled=" + (Enabled ? "true" : "false"));
            sb.AppendLine("ShareRoot=" + (string.IsNullOrWhiteSpace(ShareRoot) ? DefaultShareRoot : ShareRoot.TrimEnd('\\')));
            sb.AppendLine("Domain=" + (Domain ?? string.Empty));
            sb.AppendLine("Username=" + (Username ?? string.Empty));
            sb.AppendLine("Password=" + EncodePassword(Password ?? string.Empty));
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>Light obfuscation only — not strong crypto; keeps casual shoulder-surfing down.</summary>
        private static string EncodePassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return string.Empty;
            return "b64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(password));
        }

        private static string DecodePassword(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.StartsWith("b64:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return Encoding.UTF8.GetString(Convert.FromBase64String(value.Substring(4)));
                }
                catch
                {
                    return value;
                }
            }
            return value;
        }
    }

    /// <summary>
    /// Authenticates the current process to a UNC share via WNetAddConnection2
    /// so elevated sessions can read \\server\share without a prior explorer login.
    /// </summary>
    public static class NetworkShareAuth
    {
        private const int RESOURCETYPE_DISK = 0x00000001;
        private const int CONNECT_UPDATE = 0x00000001;
        private const int CONNECT_TEMPORARY = 0x00000004;
        private const int ERROR_SUCCESS = 0;
        private const int ERROR_SESSION_CREDENTIAL_CONFLICT = 1219;
        private const int ERROR_ALREADY_ASSIGNED = 85;
        private const int ERROR_DEVICE_ALREADY_REMEMBERED = 1202;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NETRESOURCE
        {
            public int dwScope;
            public int dwType;
            public int dwDisplayType;
            public int dwUsage;
            public string lpLocalName;
            public string lpRemoteName;
            public string lpComment;
            public string lpProvider;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetAddConnection2(ref NETRESOURCE netResource, string password, string username, int flags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetCancelConnection2(string name, int flags, bool force);

        public static string NormalizeShareRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return ShareAuthConfig.DefaultShareRoot;

            path = path.Trim().TrimEnd('\\');
            if (!path.StartsWith(@"\\", StringComparison.Ordinal))
                return path;

            // \\server\share\subdir... -> \\server\share
            string without = path.Substring(2);
            int slash = without.IndexOf('\\');
            if (slash < 0) return @"\\" + without;
            int slash2 = without.IndexOf('\\', slash + 1);
            if (slash2 < 0) return @"\\" + without;
            return @"\\" + without.Substring(0, slash2);
        }

        public static bool EnsureConnected(ShareAuthConfig cfg, out string message)
        {
            message = null;
            if (cfg == null || !cfg.Enabled)
            {
                message = "Share auth disabled.";
                return true;
            }

            if (string.IsNullOrWhiteSpace(cfg.Username))
            {
                message = "Share username is empty — set it in Settings.";
                return false;
            }

            string remote = NormalizeShareRoot(cfg.ShareRoot);
            if (string.IsNullOrWhiteSpace(remote) || !remote.StartsWith(@"\\", StringComparison.Ordinal))
            {
                message = "Share root must be a UNC path like \\\\server\\share.";
                return false;
            }

            int result = Connect(remote, cfg.EffectiveUserName, cfg.Password ?? string.Empty);
            if (result == ERROR_SUCCESS
                || result == ERROR_ALREADY_ASSIGNED
                || result == ERROR_DEVICE_ALREADY_REMEMBERED)
            {
                message = "Connected to " + remote + " as " + cfg.EffectiveUserName;
                return true;
            }

            if (result == ERROR_SESSION_CREDENTIAL_CONFLICT)
            {
                // Drop conflicting session connection, then retry once.
                WNetCancelConnection2(remote, 0, true);
                result = Connect(remote, cfg.EffectiveUserName, cfg.Password ?? string.Empty);
                if (result == ERROR_SUCCESS
                    || result == ERROR_ALREADY_ASSIGNED
                    || result == ERROR_DEVICE_ALREADY_REMEMBERED)
                {
                    message = "Reconnected to " + remote + " as " + cfg.EffectiveUserName;
                    return true;
                }
            }

            message = "Share connect failed (" + result + "): " + FormatError(result) + " → " + remote;
            return false;
        }

        /// <summary>
        /// Ensures auth using config folder path (derives \\server\share automatically).
        /// </summary>
        public static bool EnsureConnectedForPath(ShareAuthConfig cfg, string anyPathUnderShare, out string message)
        {
            if (cfg == null)
            {
                message = "Share auth config missing.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(anyPathUnderShare) && anyPathUnderShare.StartsWith(@"\\", StringComparison.Ordinal))
                cfg.ShareRoot = NormalizeShareRoot(anyPathUnderShare);

            return EnsureConnected(cfg, out message);
        }

        private static int Connect(string remote, string username, string password)
        {
            var nr = new NETRESOURCE
            {
                dwType = RESOURCETYPE_DISK,
                lpLocalName = null,
                lpRemoteName = remote,
                lpProvider = null
            };

            // Temporary: session-only, no persistent drive mapping.
            return WNetAddConnection2(ref nr, password, username, CONNECT_TEMPORARY);
        }

        private static string FormatError(int code)
        {
            switch (code)
            {
                case 5: return "Access denied";
                case 53: return "Network path not found";
                case 67: return "Network name not found";
                case 86: return "Invalid password";
                case 1326: return "Logon failure: unknown user or bad password";
                case 1327: return "Account restriction";
                case 1909: return "Session credential conflict / already connected differently";
                case ERROR_SESSION_CREDENTIAL_CONFLICT: return "Multiple connections to server with different credentials";
                default: return "Win32 error";
            }
        }
    }
}
