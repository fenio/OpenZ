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
        public const string Tip = "GRAPHICS QUALITY|High (default): the full look. Medium and Low are for older PCs: softer, shorter shadows, less anti-aliasing (2x / off; the edge smoothing filter stays), a lighter fog pass and lighter ambient occlusion (half resolution on Medium, off on Low). The resolution, effects and gameplay never change.|";
        public const string FpsTip = "FPS COUNTER|Shows the frame rate, the frame time and the graphics preset in the top right corner. Hidden in recordings.|";
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
        }

        /// <summary>Never leave a MEDIUM / LOW asset in the editor's quality level after Play (the shared settings stay HIGH).</summary>
        static void Restore()
        {
            if (originalSaved && QualitySettings.renderPipeline != original) QualitySettings.renderPipeline = original;
        }

        public static string Describe() => $"graphics {Name}, fps counter {(FpsOn ? "on" : "off")}";
    }

    /// <summary>The FPS COUNTER chip: fps (0.5 s average), the frame time and the preset, top right, over the HUD; hidden in takes.</summary>
    public sealed class OzFpsChip : MonoBehaviour
    {
        /// <summary>Top-right anchored (canvas units from the right edge / top), under the MENU button.</summary>
        public static float Right = 12f, Y = 64f, W = 168f, H = 22f;
        Canvas canvas; OzText text; float acc; int frames; string shown = "";

        void Build()
        {
            var go = new GameObject("OpenZ_FpsCanvas", typeof(RectTransform)) { layer = 5 };
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 31000;
            var cs = go.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920, 1080); cs.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; cs.matchWidthOrHeight = 1f;
            var root = Hud.Node("Root", go.transform); root.Fill(); root.pivot = Vector2.zero;
            var c = Hud.Chip(root, "FpsChip", new OzRect(OzAnchor.TR, OzLayout.RefW - Right - W, Y, W, H), "", OzUi.A(OzUi.GM900, 0.86f), OzUi.Amber, 10, 0.12f, OzUi.A(OzUi.AmberLo, 0.9f));
            text = c.text;
        }

        static bool Hidden => !OzQuality.FpsOn || OzSettings.TakeArg || CaptureKit.CineActive;

        void LateUpdate()
        {
            bool show = !Hidden;
            if (!show) { if (canvas && canvas.enabled) canvas.enabled = false; acc = 0f; frames = 0; return; }
            if (!canvas) Build();
            if (!canvas.enabled) canvas.enabled = true;
            acc += Time.unscaledDeltaTime; frames++;
            if (acc < 0.5f) return;
            string s = $"{Mathf.RoundToInt(frames / acc)} FPS · {acc * 1000f / frames:0.0} MS · {OzQuality.Name}";
            if (s != shown) { shown = s; text.Set(s); }
            acc = 0f; frames = 0;
        }
    }
}
