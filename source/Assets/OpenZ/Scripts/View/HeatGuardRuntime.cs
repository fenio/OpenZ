using System.Threading;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// the build reads the NVIDIA GPU temperature every 2 s (nvidia-smi, background thread, silently off when it is
    /// missing) and backs off: at 78 C 45 fps + render scale 0.8, at 82 C 30 fps + render scale 0.65, back to normal
    /// below 74 C. Editor: inactive (GpuGuard does the job there).
    /// </summary>
    public class HeatGuardRuntime : MonoBehaviour
    {
        /// <summary></summary>
        public static bool TakeMode;
        static volatile int temp = -1;
        /// <summary></summary>
        public static int GpuTempC => temp;
        /// <summary>The lower frame cap the guard holds right now (0 = none): the FRAME RATE LIMIT row does not lift it.</summary>
        public static int HeldFps;
        static Thread worker;
        int level, lastLogged = -100;   // 0 normal, 1 warm, 2 hot
        float baseScale = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor || FindFirstObjectByType<HeatGuardRuntime>()) return;
            var go = new GameObject("HeatGuard"); DontDestroyOnLoad(go); go.AddComponent<HeatGuardRuntime>();
            worker = new Thread(Poll) { IsBackground = true, Name = "OpenZ heat guard" }; worker.Start();
        }

        static void Poll()
        {
            while (true)
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("nvidia-smi", "--query-gpu=temperature.gpu --format=csv,noheader")
                    { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                    using (var p = System.Diagnostics.Process.Start(psi))
                    {
                        string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(3000);
                        if (int.TryParse(o.Trim().Split('\n')[0].Trim(), out var t)) temp = t;
                    }
                }
                catch { return; }                                                                   // no NVIDIA tools: guard off
                Thread.Sleep(2000);
            }
        }

        void Start()
        {
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp) baseScale = urp.renderScale;
        }

        void Update()
        {
            int t = temp; if (t < 0) return;
            if (TakeMode)
            {   // log every 5 C step; only a real emergency (88 C) costs frames
                if (t / 5 != lastLogged / 5) { lastLogged = t; Debug.Log($"[OpenZ] heat guard (take, log only): GPU {t} C"); }
                int w2 = t >= 88 ? 1 : t < 84 ? 0 : level;
                if (w2 == level) return;
                level = w2; HeldFps = level == 1 ? 45 : 0; Application.targetFrameRate = level == 1 ? Mathf.Min(45, GameBoot.CapFps) : GameBoot.CapFps;
                Debug.Log($"[OpenZ] heat guard (take): GPU {t} C -> {(level == 1 ? "45 fps" : "60 fps")}");
                return;
            }
            // 60 fps cap, the heat is only logged; one emergency cap at 90 C (30 fps, back to 60 below 85 C)
            int want = t >= 90 ? 2 : t < 85 ? 0 : level;
            if (want == level) return;
            level = want;
            HeldFps = level == 2 ? 30 : 0;
            Application.targetFrameRate = level == 2 ? 30 : GameBoot.CapFps;
            Debug.Log($"[OpenZ] heat guard: GPU {t} C -> {(level == 2 ? "EMERGENCY 30 fps" : GameBoot.CapFps + " fps")} (render scale stays {baseScale})");
        }

        void OnApplicationQuit()
        {
            TakeFlag.Restore();
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp) urp.renderScale = baseScale;
        }
    }

    /// <summary>
    /// and the L02 head start PRESELECTED in the menu (the player still chooses there), test / bot-match / gate flags off,
    /// exactly 60 fps (GameBoot, VSync off), heat guard log-only until 88 C.
    /// </summary>
    public static class TakeFlag
    {
        public static bool On { get; private set; }
        // menu_exe_v1: no musicBefore: the music switch is a session override now (AudioDirector.SessionMusicOff), never written

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            foreach (var a in System.Environment.GetCommandLineArgs()) if (!global::OpenZ.OzRelease.IsRelease && a.Equals("-take", System.StringComparison.OrdinalIgnoreCase)) On = true;
            if (!On) return;
            AudioDirector.SessionMusicOff = true;                                                          // menu_exe_v1: ROOT CAUSE fix (a killed take left oz_music 0 for good)
            OzSettings.TakeSnapshot();                                                                      // the player's own difficulty / head start, put back at quit or at the next start
            PlayerPrefs.SetInt("oz_difficulty", 1); PlayerPrefs.SetInt("oz_headstart", 1);
            PlayerPrefs.SetInt("oz_testmode", 0); PlayerPrefs.SetInt("oz_botmatch", 0); PlayerPrefs.SetInt("oz_gate", 0);
            PlayerPrefs.Save();
            HeatGuardRuntime.TakeMode = true;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = GameBoot.CapFps;
            Debug.Log("[OpenZ] -take: music off, Easy + L02 head start preselected, 60 fps, heat guard log-only until 88 C");
        }

        public static void Restore()
        {
            if (!On) return;
            OzSettings.TakeRestore("take quit");                                                            // menu_exe_v1
        }
    }
}
