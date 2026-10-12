using System.Diagnostics;
using System.Threading;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// pauses the editor at 76 C (polled every 0.5 s; a paused Game view stops rendering, so the GPU cools) and resumes below 72 C.
    /// Only resumes a pause it caused. Logs every pause/resume to the console for the build log.
    /// </summary>
    [InitializeOnLoad]
    public static class GpuGuard
    {
        public const int PauseAt = 76, ResumeBelow = 75;
        static volatile int lastTemp = -1;
        static double nextPoll;
        static bool polling, pausedByUs;
        public static int LastTemp => lastTemp;
        public static int Pauses { get; private set; }
        /// <summary>
        /// 30 s and every new peak). Stored in EditorPrefs so it survives the domain reload on entering Play.</summary>
        public static bool LogOnly { get => EditorPrefs.GetBool("OpenZ.GpuGuard.LogOnly", true); set => EditorPrefs.SetBool("OpenZ.GpuGuard.LogOnly", value); }
        /// <summary></summary>
        public const int EmergencyAt = 90, EmergencyResume = 85;
        static double nextLog; static int peak;

        static GpuGuard() { EditorApplication.update += Tick; }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) { pausedByUs = false; return; }
            if (!polling && EditorApplication.timeSinceStartup >= nextPoll)
            {
                nextPoll = EditorApplication.timeSinceStartup + 0.5; polling = true;
                ThreadPool.QueueUserWorkItem(_ => { lastTemp = Query(); polling = false; });
            }
            int t = lastTemp; if (t < 0) return;
            if (LogOnly)
            {
                if (!EditorApplication.isPaused && t >= EmergencyAt) { EditorApplication.isPaused = true; pausedByUs = true; Pauses++; Debug.LogError($"[GpuGuard] EMERGENCY: GPU {t} C >= {EmergencyAt} C, Play paused until < {EmergencyResume} C"); }
                else if (EditorApplication.isPaused && pausedByUs && t < EmergencyResume) { EditorApplication.isPaused = false; pausedByUs = false; Debug.Log($"[GpuGuard] GPU {t} C: resumed after the emergency pause"); }
                if (EditorApplication.timeSinceStartup >= nextLog || t > peak)
                {
                    nextLog = EditorApplication.timeSinceStartup + 30; peak = System.Math.Max(peak, t);
                    // H0 round 3: the old suffix "not paused (take mode)" was printed on every reading >= 76 C, even while the 90 C
                    string state = t >= EmergencyAt ? (EditorApplication.isPaused && pausedByUs ? " EMERGENCY PAUSE (>= 90 C)" : " >= 90 C, emergency pause pending")
                                 : t >= PauseAt ? " above 76 C: log-only mode, Play keeps running (emergency pause at 90 C)" : "";
                    string line = $"{System.DateTime.Now:HH:mm:ss} GPU {t} C (peak {peak} C){state}";
                    if (t >= PauseAt) Debug.LogWarning("[GpuGuard] " + line); else Debug.Log("[GpuGuard] " + line);
                    try { System.IO.File.AppendAllText(System.IO.Path.Combine(OpenZPaths.Records, "gpu_take_log.md"), "- " + line + System.Environment.NewLine); } catch { }
                }
                return;
            }
            if (!EditorApplication.isPaused && t >= PauseAt)
            {
                EditorApplication.isPaused = true; pausedByUs = true; Pauses++;
                Debug.LogWarning($"[GpuGuard] GPU {t} C >= {PauseAt} C: Play mode paused until < {ResumeBelow} C");
            }
            else if (EditorApplication.isPaused && pausedByUs && t < ResumeBelow)
            {
                EditorApplication.isPaused = false; pausedByUs = false;
                Debug.Log($"[GpuGuard] GPU {t} C: resumed");
            }
        }

        static int Query()
        {
            try
            {
                var p = new Process { StartInfo = new ProcessStartInfo("nvidia-smi", "--query-gpu=temperature.gpu --format=csv,noheader")
                    { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true } };
                p.Start(); string s = p.StandardOutput.ReadToEnd(); p.WaitForExit(3000);
                return int.TryParse(s.Trim(), out int v) ? v : -1;
            }
            catch { return -1; }
        }
    }
}
