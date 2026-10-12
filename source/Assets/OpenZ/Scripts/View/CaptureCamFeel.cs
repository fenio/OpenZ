using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// The flyby / menu-clip / level-start camera feel. Off unless <see cref="On"/> is set (the capture
    /// recipes, ZodFlyover while it flies, or the exe argument <c>-oz-camfeel</c>).
    ///  - Handheld: low-frequency Perlin on pitch / yaw / roll, <see cref="HandheldDeg"/> (0.15-0.3 deg; 0.22 default) at
    ///    0.18-0.35 Hz, never per-frame jitter (the row bans random per-frame jitter).
    ///  - Impulse: every blast (OpenZFxPlayer.Exploded, FxManager.Exploded) adds a kick by tier and distance: amplitude
    ///    = TierDeg(s) / (1 + distance / 45 m), a damped 7-9 Hz wobble that is gone in 0.5 s; blasts behind the camera count half.
    /// Applied like OpenZFxShake (beginCameraRendering, removed in endCameraRendering), so RtsCamera / CaptureKit / ZodFlyover, picking
    /// and the UI never see it and nothing accumulates. While On, the gameplay OpenZFxShake is muted (one shake, not two).
    /// The clock is Time.captureFramerate-aware (fixed-step recordings stay deterministic frame to frame).
    /// </summary>
    public static class CaptureCamFeel
    {
        public static float HandheldDeg = 0.22f, HandheldHz = 0.26f, ImpulseDecay = 0.5f, MaxKickDeg = 1.1f;
        static bool on; static float clock, kickAt = -10f, kickDeg, kickSeed; static float savedShake = 1f;
        static Quaternion applied = Quaternion.identity; static Camera appliedCam; static int kicks;
        public static int Kicks => kicks;

        public static bool On
        {
            get => on;
            set
            {
                if (on == value) return;
                on = value;
                if (on)
                {
                    RenderPipelineManager.beginCameraRendering += Begin; RenderPipelineManager.endCameraRendering += End;
                    OpenZFxPlayer.Exploded += OnBlast; FxManager.Exploded += OnBlast;
                    if (OpenZFxShake.I != null) { savedShake = OpenZFxShake.I.scale; OpenZFxShake.I.scale = 0f; }
                }
                else
                {
                    RenderPipelineManager.beginCameraRendering -= Begin; RenderPipelineManager.endCameraRendering -= End;
                    OpenZFxPlayer.Exploded -= OnBlast; FxManager.Exploded -= OnBlast;
                    if (OpenZFxShake.I != null) OpenZFxShake.I.scale = savedShake;
                }
                Debug.Log($"[OpenZ CamFeel] {(on ? "ON" : "OFF")} (handheld {HandheldDeg:0.00} deg at {HandheldHz:0.00} Hz, impulse decay {ImpulseDecay:0.0} s, max kick {MaxKickDeg:0.0} deg; kicks so far {kicks})");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            foreach (var a in Environment.GetCommandLineArgs()) if (a.Equals("-oz-camfeel", StringComparison.OrdinalIgnoreCase)) { On = true; break; }
        }

        /// <summary>Tier by M12 strength: shell 0.85 -> 0.16 deg, rocket 1.2 -> 0.3, vehicle 1.7 -> 0.5, building 2.15 -> 0.7, nuke 4.5 -> 1.1.</summary>
        public static float TierDeg(float s) => Mathf.Clamp(0.42f * (s - 0.45f), 0f, MaxKickDeg);

        static void OnBlast(Vector3 at, float s)
        {
            var c = Camera.main; if (!c) return;
            float d = Vector3.Distance(c.transform.position, at);
            float k = TierDeg(s) / (1f + d / 45f);
            if (Vector3.Dot(c.transform.forward, at - c.transform.position) < 0f) k *= 0.5f;
            float now = clock, left = Remaining(now);
            if (k < 0.02f || k <= left) return;                                                                // a weaker blast inside a stronger kick adds nothing
            kickDeg = k; kickAt = now; kickSeed = UnityEngine.Random.Range(0f, 100f); kicks++;
        }

        static float Remaining(float now) { float u = (now - kickAt) / Mathf.Max(0.05f, ImpulseDecay); return u >= 1f ? 0f : kickDeg * (1f - u) * (1f - u); }

        static int lastFrame = -1;
        static void Begin(ScriptableRenderContext ctx, Camera cam)
        {
            if (!on || cam != Camera.main) return;
            if (Time.frameCount != lastFrame) { lastFrame = Time.frameCount; clock += Time.captureFramerate > 0 ? 1f / Time.captureFramerate : Time.unscaledDeltaTime; }
            float t = clock * HandheldHz;
            float hp = (Mathf.PerlinNoise(t, 0.37f) - 0.5f) * 2f, hy = (Mathf.PerlinNoise(0.71f, t * 0.83f) - 0.5f) * 2f, hr = (Mathf.PerlinNoise(t * 0.61f, 5.3f) - 0.5f) * 2f;
            float u = clock - kickAt, amp = Remaining(clock);
            float wob = amp > 0f ? Mathf.Sin(u * 2f * Mathf.PI * 8f + kickSeed) : 0f, wob2 = amp > 0f ? Mathf.Sin(u * 2f * Mathf.PI * 7.1f + kickSeed * 1.7f) : 0f;
            var e = new Vector3(hp * HandheldDeg + wob * amp, hy * HandheldDeg * 0.8f + wob2 * amp * 0.6f, hr * HandheldDeg * 0.5f + wob * amp * 0.3f);
            applied = Quaternion.Euler(e); appliedCam = cam;
            cam.transform.rotation = cam.transform.rotation * applied;
        }

        static void End(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != appliedCam || !cam) return;
            cam.transform.rotation = cam.transform.rotation * Quaternion.Inverse(applied);
            applied = Quaternion.identity; appliedCam = null;
        }
    }
}
