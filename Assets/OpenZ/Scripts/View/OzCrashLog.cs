using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Keeps the log of every session that ended in a crash or a forced close, so the cause can be read later.
    /// Unity writes Player.log (this session) and Player-prev.log (the session before) and overwrites both at each start, so a
    /// crash log is gone after two more starts. At each start this checks a marker file that is made at the start and removed
    /// on a normal quit: if it is still there, the last session did not end normally, and its log (Player-prev.log) is copied to
    /// CrashLogs/crash_DATE_TIME.log next to Player.log, together with the crash dump Unity made for it (when there is one).
    /// The marker holds the process id of its game, so a second copy of the game started next to a running one leaves it alone.
    /// The newest <see cref="Keep"/> crashes stay. Builds only (the editor keeps its own log); nothing is ever sent anywhere.
    /// </summary>
    public static class OzCrashLog
    {
        public const int Keep = 10;
        public const string Folder = "CrashLogs", Marker = "session_running.flag";
        const long MaxDump = 32L * 1024 * 1024;

        /// <summary>The folder with the kept crash logs (next to Player.log); null in the editor or when there is no log file.</summary>
        public static string Dir { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Boot()
        {
            Dir = null;
            if (Application.isEditor || Application.isBatchMode) return;
            try
            {
                string log = Application.consoleLogPath;
                if (string.IsNullOrEmpty(log)) return;
                string home = Path.GetDirectoryName(log);
                if (string.IsNullOrEmpty(home) || !Directory.Exists(home)) return;
                Dir = Path.Combine(home, Folder);
                string marker = Path.Combine(home, Marker), prev = Path.Combine(home, "Player-prev.log");
                string me = System.Diagnostics.Process.GetCurrentProcess().Id.ToString();
                if (File.Exists(marker))
                {
                    if (OwnerAlive(marker, me)) return;                                     // a second copy of the game: the first one owns the marker
                    if (File.Exists(prev))
                    {
                        try { KeepLast(prev); }
                        catch (Exception e) { Debug.Log("[OpenZ CrashLog] the last log could not be kept (" + e.GetType().Name + ": " + e.Message + ")"); }
                    }
                }
                File.WriteAllText(marker, me);
                Application.quitting += () => { try { if (File.Exists(marker) && File.ReadAllText(marker).Trim() == me) File.Delete(marker); } catch { } };
            }
            catch (Exception e) { Debug.Log("[OpenZ CrashLog] skipped (" + e.GetType().Name + ": " + e.Message + ")"); }
        }

        /// <summary>True when the marker belongs to another copy of the game that is still running.</summary>
        static bool OwnerAlive(string marker, string me)
        {
            try
            {
                string id = File.ReadAllText(marker).Trim();
                int pid;
                if (id == me || !int.TryParse(id, out pid)) return false;
                using (var own = System.Diagnostics.Process.GetCurrentProcess())
                using (var p = System.Diagnostics.Process.GetProcessById(pid))
                    return !p.HasExited && string.Equals(p.ProcessName, own.ProcessName, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }                                                         // no such process: its game is gone
        }

        static void KeepLast(string prev)
        {
            DateTime when = File.GetLastWriteTime(prev);
            string name = "crash_" + when.ToString("yyyyMMdd_HHmmss");
            string to = Path.Combine(Dir, name + ".log");
            Directory.CreateDirectory(Dir);
            if (!File.Exists(to)) File.Copy(prev, to, false);
            bool dump = false;
            try
            {   // Unity's crash handler writes Crash_DATE_TIME/crash.dmp under the temp folder: the one from the same minutes goes along
                string crashes = Path.Combine(Path.GetTempPath(), Application.companyName, Application.productName, "Crashes");
                if (Directory.Exists(crashes))
                    foreach (var d in Directory.GetDirectories(crashes, "Crash_*").OrderByDescending(Directory.GetLastWriteTime).Take(3))
                    {
                        string dmp = Path.Combine(d, "crash.dmp");
                        if (!File.Exists(dmp) || Math.Abs((File.GetLastWriteTime(dmp) - when).TotalMinutes) > 3.0 || new FileInfo(dmp).Length > MaxDump) continue;
                        string dto = Path.Combine(Dir, name + ".dmp");
                        if (!File.Exists(dto)) File.Copy(dmp, dto, false);
                        dump = true; break;
                    }
            }
            catch { }
            // the newest Keep crashes stay
            var logs = Directory.GetFiles(Dir, "crash_*.log").OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).ToArray();
            for (int i = Keep; i < logs.Length; i++)
            {
                try { File.Delete(logs[i]); string dmpOld = Path.ChangeExtension(logs[i], ".dmp"); if (File.Exists(dmpOld)) File.Delete(dmpOld); } catch { }
            }
            Debug.Log("[OpenZ CrashLog] the last session did not end normally: its log is kept as " + Folder + "/" + name + ".log" + (dump ? " (with the crash dump)" : "") +
                      ", " + Math.Min(logs.Length, Keep) + " kept in all");
        }
    }
}
