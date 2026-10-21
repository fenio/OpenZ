// OpenZ (GPL-3.0)
// Graphics quality presets: lighter settings for older PCs, HIGH keeps the full look. Two Options rows:
//  * GRAPHICS QUALITY: HIGH (default) / MEDIUM / LOW (oz_gfx_quality, saved, live). HIGH = Assets/Settings/PC_RPAsset exactly as before
//    (nothing changes). MEDIUM / LOW are their own URP asset copies (Resources/OpenZ/Quality, made by OpenZ > Perf > Make quality presets;
//    they share PC_Renderer, so Forward+, decals, depth + opaque textures for the water, SSAO and the post stack all stay) switched in
//    with QualitySettings.renderPipeline; the shared PC_RPAsset is never written. What they lower: MSAA (2x / off; SMAA on the camera
//    stays), main-light shadows (2048 / 1024, 2 cascades, 180 / 120 m), the additional-light shadow atlas (2048 / 1024), soft-shadow
//    quality (medium / low) and the atmosphere ray-march (12 / 9 steps instead of 16; the per-pixel jitter hides the step count).
//    Render scale stays 1.0 on every preset (full quality, never downscaled by a preset).
//    Gameplay never changes. NIGHT LIGHT SHADOWS stays its own row (the one knob for night-light shadows).
//    is on, OzLitePresets.cs lowers numbers and switches the game already had (sun shadow reach and steps, night lights,
//    night-light shadows, rain with its depth layers; on LOW also the lamps' glow and beams; the table is OzLiteTable.cs) and
//    puts them back on HIGH. On HIGH none of it runs: Apply below reads one flag.
//  * FPS COUNTER: OFF (default) / ON (oz_fps_counter): a small HUD chip (top right) with fps, ms and the preset; hidden in recorded takes
//    (-take, CaptureKit cine). A real uGUI element, not OnGUI.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace OpenZ
{
    public static class OzQuality
    {
        public const string Pref = "oz_gfx_quality";
        public const string FpsPref = "oz_fps_counter";
        public const string Label = "GRAPHICS QUALITY", FpsLabel = "FPS COUNTER";
        // version 1.1: one clause per thing MEDIUM / LOW give up (a cut that is taken out of OzLiteTable.cs takes its clause with it)
        public const string Tip = "GRAPHICS QUALITY|High (default): the full look. Medium and Low are for slower PCs: softer shadows that end sooner when zoomed far out, less anti-aliasing (2x / off; the edge smoothing filter stays), lighter fog and ambient occlusion (half / off), fewer night lights that light the ground or cast shadows, thinner rain. Low gives up the most: its lamps also lose their glow and beams. Resolution, units, shots, sounds and the rules never change.|";
        public const string FpsTip = "FPS COUNTER|Shows the frame rate, the time of one frame for the processor and for the graphics chip, and the graphics preset in the top right corner. Hidden in recordings.|";
        public static readonly string[] Names = { "HIGH", "MEDIUM", "LOW" };
        /// <summary>Atmosphere ray-march steps per preset (AtmosphereFullscreen _AtmoSteps; HIGH = the shader's 16).</summary>
        public static readonly float[] AtmoSteps = { 16f, 12f, 9f };
        public const string ResDir = "OpenZ/Quality/", MediumAsset = "OZ_URP_Medium", LowAsset = "OZ_URP_Low";
        static readonly int IdSteps = Shader.PropertyToID("_AtmoSteps");
        static int level = -1;
        static RenderPipelineAsset original; static bool originalSaved;

        public static int Level
        {
            get
            {
                if (level < 0)
                {
                    level = Mathf.Clamp(PlayerPrefs.GetInt(Pref, 0), 0, 2);
                    int dev = DevQuality(); if (dev >= 0) level = dev;
                }
                return level;
            }
            set
            {
                level = Mathf.Clamp(value, 0, 2); PlayerPrefs.SetInt(Pref, level); PlayerPrefs.Save();
                Debug.Log($"[OpenZ Options] graphics quality: {Names[level]} ({Pref} {level}, saved, live)");
                Apply();
            }
        }
        public static string Name => Names[Level];

        // (or 0 / 1 / 2) picks the preset for this run; -ozrenderscale 0.5..1 measures a lower render scale (the shipped presets stay 1.0).
        static string DevVal(string a)
        {
            if (Application.isEditor || OzRelease.IsRelease) return null;
            var v = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < v.Length - 1; i++) if (v[i].Equals(a, System.StringComparison.OrdinalIgnoreCase)) return v[i + 1];
            return null;
        }
        static int DevQuality()
        {
            var q = DevVal("-ozquality"); if (string.IsNullOrEmpty(q)) return -1;
            int i = System.Array.IndexOf(Names, q.ToUpperInvariant()); if (i >= 0) return i;
            return int.TryParse(q, out i) && i >= 0 && i <= 2 ? i : -1;
        }
        static float DevRenderScale()
        {
            var r = DevVal("-ozrenderscale");
            return !string.IsNullOrEmpty(r) && float.TryParse(r, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? Mathf.Clamp(f, 0.5f, 1f) : -1f;
        }
        public static void Step(int dir) => Level = (Level + (dir < 0 ? 2 : 1)) % 3;

        public static bool FpsOn
        {
            get => PlayerPrefs.GetInt(FpsPref, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(FpsPref, value ? 1 : 0); PlayerPrefs.Save();
                Debug.Log($"[OpenZ Options] fps counter: {(value ? "ON" : "OFF")} ({FpsPref} {(value ? 1 : 0)}, saved, live)");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            Apply();
            Application.quitting -= Restore; Application.quitting += Restore;                               // the editor: Play stop puts the project asset back
            var go = new GameObject("OzFpsChip"); Object.DontDestroyOnLoad(go); go.AddComponent<OzFpsChip>();
        }

        /// <summary>Switches the active URP asset for the preset (HIGH = the project's own asset, untouched) and the fog step count.</summary>
        public static void Apply()
        {
            if (!originalSaved)
            {
                original = QualitySettings.renderPipeline; originalSaved = true;
                if (original && original.name.StartsWith("OZ_URP_")) original = GraphicsSettings.defaultRenderPipeline;   // a killed editor session left a preset in
            }
            int l = Level;
            RenderPipelineAsset want = original;
            if (l > 0)
            {
                var a = Resources.Load<UniversalRenderPipelineAsset>(ResDir + (l == 1 ? MediumAsset : LowAsset));
                if (a) want = a;
                else Debug.LogWarning($"[OpenZ Quality] {Names[l]}: Resources/{ResDir}{(l == 1 ? MediumAsset : LowAsset)} missing (run OpenZ > Perf > Make quality presets): HIGH stays");
            }
            if (QualitySettings.renderPipeline != want) QualitySettings.renderPipeline = want;
            Shader.SetGlobalFloat(IdSteps, AtmoSteps[l]);
            var cur = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float drs = DevRenderScale();
            if (cur && drs > 0f && Mathf.Abs(cur.renderScale - drs) > 0.001f) { cur.renderScale = drs; Debug.Log($"[OpenZ Quality] dev run: render scale {drs:0.##} (session only, -ozrenderscale)"); }
            Debug.Log($"[OpenZ Quality] {Names[l]}: {(cur ? cur.name : "?")}, MSAA {(cur ? cur.msaaSampleCount : 0)}x, shadows {(cur ? cur.mainLightShadowmapResolution : 0)} / {(cur ? cur.shadowCascadeCount : 0)} cascades / {(cur ? cur.shadowDistance : 0):0} m, fog steps {AtmoSteps[l]:0}, render scale {(cur ? cur.renderScale : 1f):0.##}");
            if (l > 0 || OzLite.Active) OzLite.Apply(l);                                                 // version 1.1, the lighter presets (OzLitePresets.cs): on HIGH this reads one flag and calls nothing
        }

        /// <summary>Never leave a MEDIUM / LOW asset in the editor's quality level after Play (the shared settings stay HIGH).</summary>
        static void Restore()
        {
            if (originalSaved && QualitySettings.renderPipeline != original) QualitySettings.renderPipeline = original;
        }

        public static string Describe() => $"graphics {Name}, fps counter {(FpsOn ? "on" : "off")}";
    }

    /// <summary>FRAME RATE LIMIT (Options, GRAPHICS tab): 30 or 60 frames a second, 60 unless the player chose 30 (oz_fps_cap, saved,
    /// applied at once). It is the game's own cap (GameBoot.CapFps): the heat guard and a recording session come back to it.
    /// Vsync stays off and the rules do not change with it.</summary>
    public static class OzFpsCap
    {
        public const string CapPref = "oz_fps_cap", Label = "FRAME RATE LIMIT";                     // was Pref: OzQuality above has its own Pref (the defaults check reads one key constant per name in a file)
        /// <summary>The old name of the key (the editor checks read it).</summary>
        public static string Pref => CapPref;
        public const string Tip = "FRAME RATE LIMIT|30 runs evenly on a slower computer and keeps it cooler. 60 is the default.|";
        /// <summary>The player's limit: 60 with no saved value.</summary>
        public static int Saved => PlayerPrefs.GetInt(CapPref, 60) == 30 ? 30 : 60;
        public static string Name => Saved == 30 ? "30" : "60";
        /// <summary>The row has two values: either arrow goes to the other one.</summary>
        public static void Step(int dir) => Set(Saved == 30 ? 60 : 30);
        public static void Set(int v)
        {
            v = v == 30 ? 30 : 60;
            PlayerPrefs.SetInt(CapPref, v); PlayerPrefs.Save();
            if (GameBoot.CapFps > 60) { Debug.Log($"[OpenZ Options] frame rate limit: {v} ({CapPref}, saved; this capture session keeps its own cap of {GameBoot.CapFps})"); return; }
            GameBoot.CapFps = v;
            int held = HeatGuardRuntime.HeldFps;                                                             // the heat guard holds a lower cap right now: it gives the player's value back when it lets go
            if (!GameBoot.TestMode) Application.targetFrameRate = held > 0 ? Mathf.Min(held, v) : v;
            Debug.Log($"[OpenZ Options] frame rate limit: {v} ({CapPref} {v}, saved, live{(held > 0 ? ", the heat guard holds " + held + " for now" : "")})");
        }
    }

    /// <summary>The FPS COUNTER chip: fps (0.5 s average), the frame's time split into the processor's and the graphics chip's part
    /// (the engine's frame timing figures; a figure the machine does not give is left out) and the preset, top right, over the HUD;
    /// hidden in takes. With the counter off nothing is measured and nothing is drawn.</summary>
    public sealed class OzFpsChip : MonoBehaviour
    {
        /// <summary>Top-right anchored (canvas units from the right edge / top), under the MENU button.</summary>
        public static float Right = 12f, Y = 64f, W = 268f, H = 22f;
        Canvas canvas; OzText text; float acc; int frames; string shown = "";
        readonly FrameTiming[] timing = new FrameTiming[1]; double cpuAcc, gpuAcc; int cpuN, gpuN;
        /// <summary>The last half second's figures (checks): frames a second, the processor's and the graphics chip's milliseconds (0 = not given).</summary>
        public static float ShownFps, ShownCpuMs, ShownGpuMs; public static string ShownText = "";

        void Build()
        {
            var go = new GameObject("OpenZ_FpsCanvas", typeof(RectTransform)) { layer = 5 };
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 31000;
            var cs = go.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920, 1080); cs.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; cs.matchWidthOrHeight = 1f;
            var root = Hud.Node("Root", go.transform); root.Fill(); root.pivot = Vector2.zero;
            var c = Hud.Chip(root, "FpsChip", new OzRect(OzAnchor.TR, OzLayout.RefW - Right - W, Y, W, H), "", OzUi.A(OzUi.GM900, 0.86f), OzUi.Amber, 10, 0.12f, OzUi.A(OzUi.AmberLo, 0.9f));
            text = c.text; chipRt = root.Find("FpsChip") as RectTransform; placedY = Y;
        }

        RectTransform chipRt; float placedY; int placedW, placedH; float placedScale = -1f;
        /// <summary>Checks: canvas units from the top at which the chip stands now (Y unless a panel lay there).</summary>
        public static float PlacedY;
        /// <summary>The chip's place: under the MENU button (Y), and from there down in small steps until none of its five points
        /// lies on a panel of the interface (a large interface scale, panels the player arranged). Looked at twice a second.</summary>
        void Place()
        {
            var hud = OpenZHud.I;
            if (!chipRt || !hud || !hud.Canvas || !hud.hudVisible) return;
            float k = Screen.height / 1080f, right = Screen.width - Right * k, left = right - W * k, y = Y;
            for (int i = 0; i < 60; i++, y += 6f)
            {
                float top = Screen.height - y * k, bottom = Screen.height - (y + H) * k;
                if (bottom < 0f) { y = Y; break; }
                if (!hud.IsOverUi(new Vector2(left + 1f, top - 1f)) && !hud.IsOverUi(new Vector2(right - 1f, top - 1f)) && !hud.IsOverUi(new Vector2(left + 1f, bottom + 1f))
                    && !hud.IsOverUi(new Vector2(right - 1f, bottom + 1f)) && !hud.IsOverUi(new Vector2((left + right) * 0.5f, (top + bottom) * 0.5f))
                    && !hud.IsOverUi(new Vector2(left + 1f, top + 5f * k)) && !hud.IsOverUi(new Vector2(right - 1f, top + 5f * k))) break;   // and 5 units of air above it
            }
            if (Mathf.Abs(y - placedY) > 0.01f) { placedY = y; chipRt.Place(new OzRect(OzAnchor.TR, OzLayout.RefW - Right - W, y, W, H)); }
            PlacedY = placedY;
        }

        static bool Hidden => !OzQuality.FpsOn || OzSettings.TakeArg || CaptureKit.CineActive;

        void LateUpdate()
        {
            bool show = !Hidden;
            if (!show) { if (canvas && canvas.enabled) canvas.enabled = false; acc = 0f; frames = 0; cpuAcc = gpuAcc = 0.0; cpuN = gpuN = 0; ShownText = ""; return; }
            if (!canvas) Build();
            if (!canvas.enabled) canvas.enabled = true;
            acc += Time.unscaledDeltaTime; frames++;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
            {   // the processor's part: the longer of the main and the render thread, without the wait for the frame limit
                double c = System.Math.Max(timing[0].cpuMainThreadFrameTime, timing[0].cpuRenderThreadFrameTime), g = timing[0].gpuFrameTime;
                if (c > 0.0) { cpuAcc += c; cpuN++; }
                if (g > 0.0) { gpuAcc += g; gpuN++; }
            }
            if (acc < 0.5f) return;
            Place();
            float fps = frames / acc, cpu = cpuN > 0 ? (float)(cpuAcc / cpuN) : 0f, gpu = gpuN > 0 ? (float)(gpuAcc / gpuN) : 0f;
            string s = cpu > 0f
                ? $"{Mathf.RoundToInt(fps)} FPS · CPU {Mathf.RoundToInt(cpu)} MS{(gpu > 0f ? " · GPU " + Mathf.RoundToInt(gpu) + " MS" : "")} · {OzQuality.Name}"
                : $"{Mathf.RoundToInt(fps)} FPS · {acc * 1000f / frames:0.0} MS · {OzQuality.Name}";
            if (s != shown) { shown = s; text.Set(s); }
            ShownFps = fps; ShownCpuMs = cpu; ShownGpuMs = gpu; ShownText = s;
            acc = 0f; frames = 0; cpuAcc = gpuAcc = 0.0; cpuN = gpuN = 0;
        }
    }
}
