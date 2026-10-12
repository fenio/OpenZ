using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    /// <summary>
    /// When the ending blast starts (the nuke strike's impact, or the fort death blast without a strike; win AND loss) a
    /// cinematic URP Vignette eases in over FadeIn s (smoothstep) to WinIntensity / LossIntensity (a loss slightly darker, a touch
    /// of deep red-black in its colour), holds through the ending and eases out over FadeOut s when the end screen takes over (it
    /// brings its own OzPost grade). A runtime global volume of its own (priority 90, the OpenZ_PostFX asset is never touched), like
    /// OzStartVignette. CALM SCREEN never removes it (only flashes are calmed). Log lines for the Play proof.
    /// </summary>
    public class OzEndingVignette : MonoBehaviour
    {
        public static float WinIntensity = 0.38f, LossIntensity = 0.44f, FadeIn = 1.5f, FadeOut = 1.2f;
        /// <summary>Play check: live intensity, the peak reached, when it began (unscaled s) and whether it was a loss.</summary>
        public static float Live { get; private set; }
        public static float Peak { get; private set; }
        public static float BeganAt { get; private set; } = -1f;
        public static bool WasLoss { get; private set; }

        static OzEndingVignette I;
        Volume vol; VolumeProfile profile; Vignette vig; float start, outFrom = -1f, outFromValue; int lossHint;

        /// <summary>Start (or keep) the ending vignette. lossHint 1 = the player's fort fell, 0 = the enemy's, -1 = unknown (from the sim).</summary>
        public static void Begin(int lossHint)
        {
            if (!Application.isPlaying) return;
            if (I) { if (lossHint >= 0) I.lossHint = lossHint; return; }
            var go = new GameObject("OZ_EndingVignette");
            I = go.AddComponent<OzEndingVignette>(); I.lossHint = lossHint;
        }

        /// <summary>The end screen / menu takes over: ease out.</summary>
        public static void Release()
        {
            if (!I || I.outFrom >= 0f) return;
            I.outFrom = Time.unscaledTime; I.outFromValue = Live;
            Debug.Log($"[OpenZ] ending vignette: end screen takes over {Time.unscaledTime - BeganAt:0.0} s after the blast, easing out over {FadeOut:0.0} s (peak {Peak:0.00})");
        }

        bool Loss()
        {
            if (lossHint >= 0) return lossHint == 1;
            var g = ZGame.I; var w = g ? g.World : null;
            return w != null && w.GameOver && w.Winner != g.player;
        }

        void Awake()
        {
            start = BeganAt = Time.unscaledTime; Live = 0f; Peak = 0f;
            profile = ScriptableObject.CreateInstance<VolumeProfile>(); profile.name = "OZ_EndingVignette (runtime)";
            vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0f); vig.smoothness.Override(0.6f); vig.color.Override(Color.black); vig.rounded.Override(false);
            vol = gameObject.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 90f; vol.weight = 1f; vol.sharedProfile = profile;
        }

        void Start()
        {
            WasLoss = Loss();
            Debug.Log($"[OpenZ] ending vignette: the blast starts ({(WasLoss ? "loss" : "win")}), easing in over {FadeIn:0.0} s to {(WasLoss ? LossIntensity : WinIntensity):0.00}");
        }

        void Update()
        {
            float now = Time.unscaledTime;
            bool loss = Loss(); WasLoss = loss;
            float target = loss ? LossIntensity : WinIntensity;
            float v;
            if (outFrom < 0f)
            {
                float k = Mathf.Clamp01((now - start) / FadeIn);
                v = target * k * k * (3f - 2f * k);
            }
            else
            {
                float k = Mathf.Clamp01((now - outFrom) / FadeOut);
                v = outFromValue * (1f - k * k * (3f - 2f * k));
                if (k >= 1f) { Live = 0f; Debug.Log($"[OpenZ] ending vignette gone (peak {Peak:0.00})"); Destroy(gameObject); return; }
            }
            Live = v; if (v > Peak) Peak = v;
            vig.intensity.Override(v);
            vig.color.Override(loss ? new Color(0.06f, 0.0f, 0.0f) : Color.black);
            // the end screen took over (or the level was left) without calling Release: ease out anyway
            if (outFrom < 0f && OpenZEndScreen.AnyShown) Release();
        }

        void OnDestroy() { if (profile) Destroy(profile); if (I == this) I = null; }
    }
}
