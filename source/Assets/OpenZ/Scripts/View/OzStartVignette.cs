using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// A soft cinematic vignette (URP Vignette override, 0.33, smooth, black) on a runtime global volume of its own
    /// (the OpenZ_PostFX asset is never modified) during the level-start fly-by (Zod's briefing) / mission banner; the moment the
    /// player gets control (ZodBriefing hands over, else OpenZHud.ControlAt = the HUD entrance) it eases to 0 over 1.5 s and the
    /// volume is removed. Never on during normal play.
    /// </summary>
    public class OzStartVignette : MonoBehaviour
    {
        public static float Intensity = 0.33f, FadeSeconds = 1.5f;
        /// <summary>Play check: the live weight (1 = full vignette, 0 = gone) and when it reached 0.</summary>
        public static float Weight { get; private set; }
        public static float GoneAt { get; private set; } = -1f;
        Volume vol; VolumeProfile profile; float bornAt, fadeFrom = -1f; bool sawBriefing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded += (s, m) => Spawn(); Spawn(); }

        static void Spawn()
        {
            if (!FindFirstObjectByType<ZGame>() || FindFirstObjectByType<OzStartVignette>()) return;
            new GameObject("OZ_StartVignette").AddComponent<OzStartVignette>();
        }

        void Awake()
        {
            bornAt = OzMotion.Now; GoneAt = -1f;
            profile = ScriptableObject.CreateInstance<VolumeProfile>(); profile.name = "OZ_StartVignette (runtime)";
            var v = profile.Add<Vignette>(true);
            v.intensity.Override(Intensity); v.smoothness.Override(0.85f); v.color.Override(Color.black); v.rounded.Override(false);
            vol = gameObject.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 100f; vol.weight = 1f; vol.sharedProfile = profile;
            Weight = 1f;
            Debug.Log($"[OpenZ] level-start vignette on ({Intensity:0.00}), fades {FadeSeconds:0.0} s after the player gets control");
        }

        void Update()
        {
            float now = OzMotion.Now;
            if (fadeFrom < 0f)
            {
                // the player gets control when Zod's level-start fly-by briefing hands over (sim, input + HUD back); without a
                // briefing (skipped / off / bot match) when the HUD entrance starts after the mission banner
                if (ZodBriefing.BriefingActive) { sawBriefing = true; return; }
                bool control = sawBriefing || (now - bornAt > 1.2f && OpenZHud.ControlAt >= bornAt - 0.5f && now >= OpenZHud.ControlAt);
                if (control || now - bornAt > 40f) { fadeFrom = now; Debug.Log($"[OpenZ] level-start vignette: player has control {now - bornAt:0.0} s after the level start ({(sawBriefing ? "briefing handed over" : "HUD entrance")}), easing out over {FadeSeconds:0.0} s"); }
                return;
            }
            float k = Mathf.Clamp01((now - fadeFrom) / FadeSeconds);
            Weight = 1f - (0.5f - 0.5f * Mathf.Cos(k * Mathf.PI));
            vol.weight = Weight;
            if (k >= 1f)
            {
                GoneAt = now; Weight = 0f;
                Debug.Log($"[OpenZ] level-start vignette gone {now - bornAt:0.0} s after the level start");
                Destroy(gameObject);
            }
        }

        void OnDestroy() { if (profile) Destroy(profile); }
    }
}
