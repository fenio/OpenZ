// OpenZ (GPL-3.0) - GRAPHICS QUALITY MEDIUM / LOW: the sun's (at night the moon's) shadows fade out smoothly while the camera zooms
// far out, and fade back in on the way down, instead of their far end sweeping down the picture until the last one is gone.
// Those presets let the shadows reach only so far (the reach follows the view up to OzLiteTable.ReachMax; OzLiteDriver writes it to
// the active settings file). At the camera height where even the ground at the screen's lower edge lies beyond that longest reach no
// shadow is left; over the last FadeShare of the zoom range (closest to widest) below that height the shadows' strength eases to
// nothing. The strength is lowered only while the game camera draws (put on in beginCameraRendering, taken off in
// endCameraRendering, as the camera shake does), so the day and night, the rain and every other writer of the light always read
// their own value. HIGH: its shadows reach the whole view at every zoom (ShadowDistanceByHeight), so nothing is faded and no
// drawing hook is set; nor while the shadow reach cut of the lighter presets is switched off. Drawn only: no rule reads it.
// Play check: return OpenZ.OzShadowZoomFade.Status();
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    [DefaultExecutionOrder(950)]
    public sealed class OzShadowZoomFade : MonoBehaviour
    {
        /// <summary>Off = the shadows end where the reach ends, as before (for a before / after look).</summary>
        public static bool On = true;
        /// <summary>Share of the zoom range (RtsCamera.ZoomClosest to ZoomWidest) over which the shadows fade out below the height
        /// where none are left.</summary>
        public static float FadeShare = 0.25f;
        /// <summary>For the checks: the strength factor in force (1 = full shadows), the reach (m), the camera height above the ground
        /// plane (m) and the height (m) at which no shadow is left at the screen's lower edge (0 = not measured: HIGH, or no view).</summary>
        public static float Factor = 1f, ReachNow, HeightNow, GoneAt;

        Camera target; Light lit; DayNight dn; float before = -1f, written = -1f, nextLook; bool hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { if (!FindFirstObjectByType<OzShadowZoomFade>()) { var go = new GameObject("OZ_ShadowZoomFade"); DontDestroyOnLoad(go); go.AddComponent<OzShadowZoomFade>(); } }

        void OnDisable() { Hook(false); Factor = 1f; }
        void OnApplicationQuit() => OnDisable();

        /// <summary>The share of the shadows' strength left at camera height h (above the ground plane), when no shadow is left from
        /// the height gone up and the fade takes band metres below it.</summary>
        public static float FactorAt(float h, float gone, float band) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(gone - band, gone, h));

        void LateUpdate()
        {
            float k = 1f; var cam = Camera.main;
            ReachNow = HeightNow = GoneAt = 0f;
            if (On && OzLite.Active && OzLite.Cut(OzLiteTable.CutSunReach) && cam && ZGame.I != null && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                // the ground at the screen's lower edge, on the ground plane the reach rule uses (ShadowDistanceByHeight / OzLiteDriver)
                var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0f, 0f));
                int l = OzLite.Level;
                if (ray.direction.y < -0.02f && ray.origin.y > 0f && l > 0 && l < OzLiteTable.ReachMax.Length)
                {
                    // below its limit the reach grows with the view, so the shadows end for good only where the lower edge's ground lies
                    // as far as the preset's longest reach
                    ReachNow = urp.shadowDistance; HeightNow = ray.origin.y;
                    GoneAt = OzLiteTable.ReachMax[l] * -ray.direction.y;
                    k = FactorAt(HeightNow, GoneAt, FadeShare * (RtsCamera.ZoomWidest - RtsCamera.ZoomClosest));
                }
            }
            Factor = k; target = cam;
            if (k < 0.999f) { if (!hooked) Hook(true); }
            else if (hooked) Hook(false);
        }

        void Hook(bool on)
        {
            if (on == hooked) return;
            hooked = on;
            if (on) { RenderPipelineManager.beginCameraRendering += Begin; RenderPipelineManager.endCameraRendering += End; }
            else { RenderPipelineManager.beginCameraRendering -= Begin; RenderPipelineManager.endCameraRendering -= End; Restore(); }
        }

        Light Sun()
        {
            if (RenderSettings.sun) return RenderSettings.sun;
            if (!dn && Time.unscaledTime >= nextLook) { nextLook = Time.unscaledTime + 2f; dn = FindFirstObjectByType<DayNight>(); }
            return dn ? dn.sun : null;
        }

        void Begin(ScriptableRenderContext ctx, Camera c)
        {
            if (c != target || Factor >= 0.999f) return;
            var l = Sun(); if (!l) return;
            if (written >= 0f && lit == l && Mathf.Approximately(l.shadowStrength, written)) l.shadowStrength = before;   // a frame whose end never came: its own value first
            lit = l; before = l.shadowStrength; written = before * Factor; l.shadowStrength = written;
        }

        void End(ScriptableRenderContext ctx, Camera c) { if (c == target) Restore(); }

        /// <summary>The light's own value back, unless something else wrote it meanwhile.</summary>
        void Restore()
        {
            if (lit && written >= 0f && Mathf.Approximately(lit.shadowStrength, written)) lit.shadowStrength = before;
            written = -1f;
        }

        /// <summary>One-line state for the Play check.</summary>
        public static string Status() => !OzLite.Active ? $"OzShadowZoomFade: HIGH, nothing faded (factor {Factor:0.00})"
            : $"OzShadowZoomFade: {(On ? "on" : "off")}, {OzQuality.Name}, reach {ReachNow:0} m, camera {HeightNow:0} m, no shadow left from {GoneAt:0} m, " +
              $"fade from {GoneAt - FadeShare * (RtsCamera.ZoomWidest - RtsCamera.ZoomClosest):0} m, strength factor {Factor:0.00}";
    }
}
