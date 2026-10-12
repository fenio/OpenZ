using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// <list type="bullet">
    /// <item><c>OpenZ.FogReadCheck.Shot(dir, "canal")</c>: freezes the game clock (Time.timeScale 0) and writes, of the SAME frame,
    /// <c>canal_ON.png</c>, <c>canal_BANKSOFF.png</c> (haze only), <c>canal_ALLOFF.png</c> (atmosphere pass off) and <c>canal_units.json</c>
    /// (screen boxes of the visible units), then restores the clock. tools\fog_ab_check.py reads them.</item>
    /// <item><c>OpenZ.FogReadCheck.Scrub(dir, 0.84f, 0.95f, 0.005f)</c>: ON + BANKSOFF pairs along the time of day (the pop check).</item>
    /// <item><c>OpenZ.FogReadCheck.Perf(12)</c>: 12 s alternating 2 s windows banks on / off, logs the mean GPU (FrameTimingManager) and CPU ms.</item>
    /// </list>
    /// Uses Atmosphere.DebugFog (A/B reference only, reset to 0 at the end). Busy flag: <c>FogReadCheck.Busy</c>; result line: <c>FogReadCheck.Last</c>.
    /// </summary>
    public class FogReadCheck : MonoBehaviour
    {
        public static bool Busy { get; private set; }
        public static string Last { get; private set; } = "";
        static FogReadCheck host;

        static FogReadCheck Host()
        {
            if (host) return host;
            host = new GameObject("OZ_FogReadCheck") { hideFlags = HideFlags.DontSave }.AddComponent<FogReadCheck>();
            return host;
        }

        public static void Shot(string dir, string tag) { if (!Busy) Host().StartCoroutine(Host().ShotCo(dir, tag)); }
        public static void Scrub(string dir, float t0, float t1, float dt) { if (!Busy) Host().StartCoroutine(Host().ScrubCo(dir, t0, t1, dt)); }
        public static void Perf(float seconds) { if (!Busy) Host().StartCoroutine(Host().PerfCo(seconds)); }

        static IEnumerator Settle()
        {
            for (int i = 0; i < 3; i++) yield return null;                                   // Atmosphere.Update + OzWeather.LateUpdate run with the new DebugFog
            yield return new WaitForEndOfFrame();
        }

        static void Save(string dir, string name)
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(dir, name), tex.EncodeToPNG());
            Destroy(tex);
        }

        IEnumerator ShotCo(string dir, string tag)
        {
            Busy = true; Directory.CreateDirectory(dir);
            float ts = Time.timeScale; Time.timeScale = 0f;
            Atmosphere.DebugFog = 0; yield return Settle(); Save(dir, tag + "_ON.png");
            File.WriteAllText(Path.Combine(dir, tag + "_units.json"), UnitBoxes());
            Atmosphere.DebugFog = 1; yield return Settle(); Save(dir, tag + "_BANKSOFF.png");
            Atmosphere.DebugFog = 2; yield return Settle(); Save(dir, tag + "_ALLOFF.png");
            Atmosphere.DebugFog = 0; yield return null;
            Time.timeScale = ts;
            var dn = FindFirstObjectByType<DayNight>();
            Last = $"[OpenZ FogReadCheck] {tag}: t {(dn ? dn.timeOfDay : -1f):F3} low {Shader.GetGlobalFloat("_LowFogDensity"):F4} haze {Shader.GetGlobalFloat("_FogDensity"):F5} -> {dir}";
            Debug.Log(Last); Busy = false;
        }

        IEnumerator ScrubCo(string dir, float t0, float t1, float dt)
        {
            Busy = true; Directory.CreateDirectory(dir);
            float ts = Time.timeScale; Time.timeScale = 0f;
            int k = 0;
            for (float t = t0; t <= t1 + 1e-4f; t += dt, k++)
            {
                NightLights.JumpTo(t);
                Atmosphere.DebugFog = 0; yield return Settle(); Save(dir, $"scrub_{k:00}_{t:F3}_ON.png");
                Atmosphere.DebugFog = 1; yield return Settle(); Save(dir, $"scrub_{k:00}_{t:F3}_BANKSOFF.png");
            }
            Atmosphere.DebugFog = 0; Time.timeScale = ts;
            Last = $"[OpenZ FogReadCheck] scrub {t0:F3}-{t1:F3} step {dt:F3}: {k} pairs -> {dir}";
            Debug.Log(Last); Busy = false;
        }

        IEnumerator PerfCo(float seconds)
        {
            Busy = true;
            var timings = new FrameTiming[1];
            double[] gpu = new double[2], cpu = new double[2]; int[] gn = new int[2], cn = new int[2];
            float end = Time.realtimeSinceStartup + seconds, win = 0; int mode = 0;
            Atmosphere.DebugFog = 0;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                win += Time.unscaledDeltaTime;
                if (win > 2f) { win = 0; mode = 1 - mode; Atmosphere.DebugFog = mode; continue; }   // the switching frame is not counted
                if (win < 0.25f) continue;
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0) { gpu[mode] += timings[0].gpuFrameTime; gn[mode]++; }
                cpu[mode] += Time.unscaledDeltaTime * 1000.0; cn[mode]++;
            }
            Atmosphere.DebugFog = 0;
            string G(int m) => gn[m] > 0 ? (gpu[m] / gn[m]).ToString("F2", CultureInfo.InvariantCulture) : "n/a";
            string C(int m) => cn[m] > 0 ? (cpu[m] / cn[m]).ToString("F2", CultureInfo.InvariantCulture) : "n/a";
            Last = $"[OpenZ FogReadCheck] perf {seconds:F0} s at {Screen.width}x{Screen.height}: banks ON gpu {G(0)} ms cpu {C(0)} ms ({cn[0]} fr) | banks OFF gpu {G(1)} ms cpu {C(1)} ms ({cn[1]} fr)";
            Debug.Log(Last); Busy = false;
        }

        /// <summary>Screen boxes (image pixels, y down) of the visible units: the renderer bounds of every active UnitView, projected.</summary>
        static string UnitBoxes()
        {
            var cam = Camera.main; var sb = new StringBuilder();
            sb.Append("{\"w\":").Append(Screen.width).Append(",\"h\":").Append(Screen.height).Append(",\"units\":[");
            bool first = true;
            if (cam)
                foreach (var u in FindObjectsByType<UnitView>(FindObjectsSortMode.None))
                {
                    if (!u || !u.isActiveAndEnabled) continue;
                    bool any = false; Bounds b = default;
                    foreach (var r in u.GetComponentsInChildren<Renderer>(false))
                    {
                        if (!r.enabled || !r.isVisible || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                        if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                    }
                    if (!any) continue;
                    float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue; bool behind = false;
                    for (int c = 0; c < 8; c++)
                    {
                        var w = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z);
                        var s = cam.WorldToScreenPoint(w); if (s.z <= 0) { behind = true; break; }
                        x0 = Mathf.Min(x0, s.x); x1 = Mathf.Max(x1, s.x); y0 = Mathf.Min(y0, Screen.height - s.y); y1 = Mathf.Max(y1, Screen.height - s.y);
                    }
                    if (behind || x1 < 0 || y1 < 0 || x0 > Screen.width || y0 > Screen.height || (x1 - x0) * (y1 - y0) < 64) continue;
                    sb.Append(first ? "" : ",").Append('[').Append(((int)x0).ToString(CultureInfo.InvariantCulture)).Append(',').Append(((int)y0).ToString(CultureInfo.InvariantCulture))
                      .Append(',').Append(((int)x1).ToString(CultureInfo.InvariantCulture)).Append(',').Append(((int)y1).ToString(CultureInfo.InvariantCulture)).Append(']');
                    first = false;
                }
            return sb.Append("]}").ToString();
        }

        void OnDisable() { Atmosphere.DebugFog = 0; Busy = false; }
    }
}
