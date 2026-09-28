using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AutoClickUI
{
    /// <summary>One PC slot in a staggered click schedule.</summary>
    internal sealed class ConfigSlot
    {
        public string MachineName { get; set; }
        public int SlotIndex { get; set; }
        public DateTime TargetTime { get; set; }
        public string ExistingConfigPath { get; set; }
        public bool HadExistingFile { get; set; }
    }

    internal sealed class DistributePlan
    {
        public DateTime BaseTime { get; set; }
        public DateTime EndTime { get; set; }
        public int WindowMs { get; set; }
        public int StepMs { get; set; }
        public string TargetProcess { get; set; }
        public int ClickCount { get; set; }
        public int ClickInterval { get; set; }
        public List<ConfigSlot> Slots { get; set; }
        public List<string> Warnings { get; set; }

        public DistributePlan()
        {
            Slots = new List<ConfigSlot>();
            Warnings = new List<string>();
            TargetProcess = "Payam";
            ClickCount = 1;
        }
    }

    internal sealed class DistributeWriteResult
    {
        public int Written { get; set; }
        public int Failed { get; set; }
        public List<string> Errors { get; set; }
        public List<string> WrittenPaths { get; set; }

        public DistributeWriteResult()
        {
            Errors = new List<string>();
            WrittenPaths = new List<string>();
        }
    }

    internal sealed class CleanupResult
    {
        public int Deleted { get; set; }
        public int Failed { get; set; }
        public List<string> DeletedFiles { get; set; }
        public List<string> Errors { get; set; }

        public CleanupResult()
        {
            DeletedFiles = new List<string>();
            Errors = new List<string>();
        }
    }

    /// <summary>
    /// Builds unique staggered TargetTime configs for many PCs on the share.
    /// Keeps existing per-machine file format: IRN-PC0002_config.txt
    /// </summary>
    internal static class ConfigDistributor
    {
        public const string MachinesFileName = "machines.txt";
        public const string ConfigSuffix = "_config.txt";
        private static readonly string TimeFormat = "yyyy/MM/dd HH:mm:ss.fff";

        public static string MachinesFilePath(string configFolder)
        {
            return Path.Combine(configFolder ?? string.Empty, MachinesFileName);
        }

        public static string ConfigPathFor(string configFolder, string machineName)
        {
            return Path.Combine(configFolder ?? string.Empty, (machineName ?? string.Empty).Trim() + ConfigSuffix);
        }

        /// <summary>Discover machine names from existing *_config.txt files (excluding machines.txt).</summary>
        public static List<string> DiscoverMachinesFromConfigs(string configFolder)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(configFolder) || !Directory.Exists(configFolder))
                return result;

            foreach (var path in Directory.GetFiles(configFolder, "*" + ConfigSuffix))
            {
                string name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name) || name.Length <= ConfigSuffix.Length)
                    continue;
                if (!name.EndsWith(ConfigSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;
                string machine = name.Substring(0, name.Length - ConfigSuffix.Length).Trim();
                if (machine.Length == 0) continue;
                if (string.Equals(machine, "ntp", StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(machine);
            }

            return NormalizeMachineList(result);
        }

        public static List<string> LoadMachinesFile(string path)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return result;

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                string t = (line ?? string.Empty).Trim();
                if (t.Length == 0 || t.StartsWith("#") || t.StartsWith(";")) continue;
                // allow "IRN-PC0002" or "IRN-PC0002_config.txt"
                if (t.EndsWith(ConfigSuffix, StringComparison.OrdinalIgnoreCase))
                    t = t.Substring(0, t.Length - ConfigSuffix.Length).Trim();
                if (t.Length > 0) result.Add(t);
            }
            return NormalizeMachineList(result);
        }

        public static void SaveMachinesFile(string path, IEnumerable<string> machines)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("machines.txt path is empty.");

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var list = NormalizeMachineList(machines);
            var sb = new StringBuilder();
            sb.AppendLine("# AutoClick machine list — one PC name per line");
            sb.AppendLine("# Used by Admin → Config Distributor");
            sb.AppendLine("# Example: IRN-PC0002");
            foreach (var m in list)
                sb.AppendLine(m);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public static List<string> NormalizeMachineList(IEnumerable<string> machines)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            if (machines == null) return list;
            foreach (var raw in machines)
            {
                string m = (raw ?? string.Empty).Trim();
                if (m.Length == 0) continue;
                if (m.EndsWith(ConfigSuffix, StringComparison.OrdinalIgnoreCase))
                    m = m.Substring(0, m.Length - ConfigSuffix.Length).Trim();
                if (m.Length == 0) continue;
                if (set.Add(m))
                    list.Add(m);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        /// <summary>
        /// Evenly space N machines from baseTime through endTime (inclusive).
        /// Guarantees unique millisecond timestamps when window allows.
        /// </summary>
        public static DistributePlan BuildPlan(
            IEnumerable<string> machines,
            DateTime baseTime,
            DateTime endTime,
            string targetProcess,
            int clickCount,
            int clickInterval,
            string configFolder)
        {
            var plan = new DistributePlan
            {
                BaseTime = baseTime,
                EndTime = endTime,
                TargetProcess = string.IsNullOrWhiteSpace(targetProcess) ? "Payam" : targetProcess.Trim(),
                ClickCount = clickCount < 1 ? 1 : clickCount,
                ClickInterval = clickInterval < 0 ? 0 : clickInterval
            };

            if (endTime < baseTime)
            {
                plan.Warnings.Add("End time is before base time — swapped automatically.");
                var tmp = baseTime;
                baseTime = endTime;
                endTime = tmp;
                plan.BaseTime = baseTime;
                plan.EndTime = endTime;
            }

            long windowTicks = (endTime - baseTime).Ticks;
            plan.WindowMs = (int)Math.Max(0, windowTicks / TimeSpan.TicksPerMillisecond);

            var list = NormalizeMachineList(machines);
            if (list.Count == 0)
            {
                plan.Warnings.Add("No machines in the list.");
                return plan;
            }

            int n = list.Count;
            int step = (n <= 1 || plan.WindowMs <= 0) ? 0 : plan.WindowMs / (n - 1);
            plan.StepMs = step;

            if (n > 1 && plan.WindowMs > 0 && step == 0)
                plan.Warnings.Add("Window is smaller than machine count — some times may collide; uniqueness pass will nudge duplicates.");

            if (n > plan.WindowMs + 1 && plan.WindowMs >= 0)
                plan.Warnings.Add("More machines than available milliseconds in the window (" + (plan.WindowMs + 1) + "). Duplicates will be nudged forward.");

            var used = new HashSet<long>();
            for (int i = 0; i < n; i++)
            {
                int offsetMs = (n == 1 || plan.WindowMs == 0) ? 0 : (int)Math.Round(i * (plan.WindowMs / (double)(n - 1)));
                if (offsetMs < 0) offsetMs = 0;
                if (offsetMs > plan.WindowMs) offsetMs = plan.WindowMs;

                DateTime t = baseTime.AddMilliseconds(offsetMs);
                // uniqueness: nudge +1ms while colliding
                int guard = 0;
                while (used.Contains(t.Ticks) && guard < 100000)
                {
                    t = t.AddMilliseconds(1);
                    guard++;
                }
                if (guard > 0)
                    plan.Warnings.Add(list[i] + ": nudged +" + guard + " ms to avoid duplicate time.");
                used.Add(t.Ticks);

                string path = string.IsNullOrWhiteSpace(configFolder)
                    ? null
                    : ConfigPathFor(configFolder, list[i]);

                plan.Slots.Add(new ConfigSlot
                {
                    MachineName = list[i],
                    SlotIndex = i,
                    TargetTime = t,
                    ExistingConfigPath = path,
                    HadExistingFile = path != null && File.Exists(path)
                });
            }

            return plan;
        }

        public static string FormatConfigContent(DateTime targetTime, string process, int clickCount, int clickInterval)
        {
            var sb = new StringBuilder();
            sb.Append("TargetTime=").Append(targetTime.ToString(TimeFormat, CultureInfo.InvariantCulture)).AppendLine();
            sb.Append("TargetProcess=").Append(process ?? "Payam").AppendLine();
            sb.Append("clickCount=").Append(clickCount).AppendLine();
            if (clickInterval > 0)
                sb.Append("clickInterval=").Append(clickInterval).AppendLine();
            return sb.ToString();
        }

        public static DistributeWriteResult WriteConfigs(string configFolder, DistributePlan plan)
        {
            var result = new DistributeWriteResult();
            if (plan == null || plan.Slots == null || plan.Slots.Count == 0)
            {
                result.Errors.Add("Nothing to write — build a preview first.");
                return result;
            }
            if (string.IsNullOrWhiteSpace(configFolder))
            {
                result.Errors.Add("Config folder is empty.");
                result.Failed = plan.Slots.Count;
                return result;
            }
            if (!Directory.Exists(configFolder))
            {
                result.Errors.Add("Config folder not accessible: " + configFolder);
                result.Failed = plan.Slots.Count;
                return result;
            }

            foreach (var slot in plan.Slots)
            {
                try
                {
                    string path = ConfigPathFor(configFolder, slot.MachineName);
                    string content = FormatConfigContent(slot.TargetTime, plan.TargetProcess, plan.ClickCount, plan.ClickInterval);
                    File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    result.Written++;
                    result.WrittenPaths.Add(path);
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add(slot.MachineName + ": " + ex.Message);
                }
            }
            return result;
        }

        /// <summary>Delete *_config.txt files whose machine is not in keepMachines.</summary>
        public static CleanupResult CleanupOrphanConfigs(string configFolder, IEnumerable<string> keepMachines)
        {
            var result = new CleanupResult();
            if (string.IsNullOrWhiteSpace(configFolder) || !Directory.Exists(configFolder))
            {
                result.Errors.Add("Config folder not accessible.");
                return result;
            }

            var keep = new HashSet<string>(NormalizeMachineList(keepMachines), StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.GetFiles(configFolder, "*" + ConfigSuffix))
            {
                string name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name) || name.Length <= ConfigSuffix.Length) continue;
                string machine = name.Substring(0, name.Length - ConfigSuffix.Length).Trim();
                if (keep.Contains(machine)) continue;
                try
                {
                    File.Delete(path);
                    result.Deleted++;
                    result.DeletedFiles.Add(name);
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add(name + ": " + ex.Message);
                }
            }
            return result;
        }

        public static string DescribePlan(DistributePlan plan)
        {
            if (plan == null) return "No plan.";
            int n = plan.Slots != null ? plan.Slots.Count : 0;
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} PC(s)  ·  {1} → {2}  ·  window {3} ms  ·  step ~{4} ms",
                n,
                plan.BaseTime.ToString(TimeFormat, CultureInfo.InvariantCulture),
                plan.EndTime.ToString(TimeFormat, CultureInfo.InvariantCulture),
                plan.WindowMs,
                plan.StepMs);
        }
    }
}
