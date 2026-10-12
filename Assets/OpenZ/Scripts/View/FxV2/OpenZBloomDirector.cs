using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// URP Bloom in play mode, day and night, plus the blast / nuke flashes.
    /// <list type="bullet">
    /// <item>Why the old bloom barely showed: URP converts the Bloom threshold from gamma to linear
    /// (PostProcessPass.cs:1346 / PostProcessPassRenderGraph.cs:713 <c>Mathf.GammaToLinearSpace(threshold)</c>), so
    /// OpenZ_PostFX's 1.25 is a LINEAR 1.63 with a hard-coded knee of .82 (Bloom.shader:109-112 prefilter on max(r,g,b)). The
    /// fx_look fire body (1.45, .42, .03) sits inside the knee (7 % reaches the bloom), the rim never does, the ring (1.25 x
    /// tone) never does. At night NightGrade's .95 is a linear .89 with intensity 1.25 on lamps whose heads are small.</item>
    /// <item>
    /// LUMINANCE with no threshold subtraction (every hot pixel contributes its full colour, spread over 5 mips up to ~380 px),
    /// then Neutral tone mapping at exposure 1.15. URP has no such pass, so the same read is rebuilt from URP Bloom (threshold
    /// kept high by day so sunlit white walls never glow; the fx_v2 FX are 3-9 x brighter than the threshold) plus a lower
    /// threshold and stronger bloom at night, tied to NightGrade.NightK.</item>
    /// <item>A priority-30 global runtime Volume (above NightGrade's 20 and the scene's global volume) overrides Bloom threshold /
    /// intensity / scatter / tint every frame: lerp(day, night, NightK) + the blast boost. OpenZ_PostFX.asset is not modified
    /// (edit-mode look and scene view unchanged). NightLights' "night bloom" inspector knobs are superseded by NightThreshold /
    /// NightIntensity here.</item>
    /// <item>Blast boost: every OpenZFxPlayer blast on screen with strength s adds BoostPerS x (s - .6) to the bloom intensity
    /// </item>
    /// <item>Flash(): the nuke's blinding flash = an exposure flash (second volume, ColorAdjustments.postExposure +FlashEv at
    /// weight w, decaying) + a big bloom boost. Works day and night.</item>
    /// <item>Safety: the game camera must render HDR with post-processing on (OpenZSetup sets both; logged once if not and fixed
    /// at run time).</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(130)]
    public sealed class OpenZBloomDirector : MonoBehaviour
    {
        public static OpenZBloomDirector I { get; private set; }
        public static bool AutoCreate = true;

        // ---------------------------------------------------------------------------------------------- tunables (gamma thresholds, as the Volume UI)
        [Header("Day (NightK 0)")]
        public float dayThreshold = 1.25f;          // linear 1.63: sunlit white walls (~1.3) stay out, fx_v2 fire (3-9) blooms 50-80 %
        public float dayIntensity = 1.35f;          // was 1.05
        [Range(0, 1)] public float dayScatter = 0.72f;
        public Color dayTint = new Color(1f, 0.96f, 0.9f, 1f);
        [Header("Night (NightK 1)")]
        public float nightThreshold = 0.80f;        // linear .60: lamp heads, beams, headlights, muzzle flashes, blasts bloom
        public float nightIntensity = 2.0f;         // was 1.25
        [Range(0, 1)] public float nightScatter = 0.80f;
        public Color nightTint = new Color(1f, 0.93f, 0.84f, 1f);
        [Header("Blast boost")]
        public float boostPerS = 0.35f, boostMax = 1.2f, boostDecay = 2.6f, boostMinS = 0.6f;
        [Header("Nuke flash")]
        public float flashEv = 2.2f, flashBloom = 2.2f, flashDecay = 6f;

        Volume vol, flashVol; VolumeProfile prof, flashProf; Bloom bloom; ColorAdjustments flashCa;
        float boost, flashW, sustain, sustainDrop, calmFlash; bool loggedCam;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!AutoCreate || I) return;
            if (!ZGame.I && !FindFirstObjectByType<ZGame>()) return;                        // game levels only (menu keeps its own look)
            new GameObject("OpenZBloomDirector").AddComponent<OpenZBloomDirector>();
        }

        void Awake()
        {
            if (I && I != this) { Destroy(gameObject); return; }
            I = this;
            prof = ScriptableObject.CreateInstance<VolumeProfile>(); prof.name = "OZ_BloomDirector (runtime)"; prof.hideFlags = HideFlags.DontSave;
            bloom = prof.Add<Bloom>(false);
            bloom.threshold.Override(dayThreshold); bloom.intensity.Override(dayIntensity); bloom.scatter.Override(dayScatter); bloom.tint.Override(dayTint);
            bloom.highQualityFiltering.Override(true);
            vol = gameObject.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 30f; vol.weight = 1f; vol.sharedProfile = prof;

            var fgo = new GameObject("OZ_NukeFlashVolume"); fgo.transform.SetParent(transform, false);
            flashProf = ScriptableObject.CreateInstance<VolumeProfile>(); flashProf.name = "OZ_NukeFlash (runtime)"; flashProf.hideFlags = HideFlags.DontSave;
            flashCa = flashProf.Add<ColorAdjustments>(false); flashCa.postExposure.Override(flashEv);
            flashVol = fgo.AddComponent<Volume>(); flashVol.isGlobal = true; flashVol.priority = 40f; flashVol.weight = 0f; flashVol.sharedProfile = flashProf;
        }

        void OnEnable() { OpenZFxPlayer.Exploded -= OnBlast; OpenZFxPlayer.Exploded += OnBlast; }
        void OnDisable() { OpenZFxPlayer.Exploded -= OnBlast; }

        void OnDestroy()
        {
            OpenZFxPlayer.Exploded -= OnBlast;
            if (prof) { foreach (var c in prof.components) if (c) Destroy(c); Destroy(prof); }
            if (flashProf) { foreach (var c in flashProf.components) if (c) Destroy(c); Destroy(flashProf); }
            if (I == this) I = null;
        }

        /// <summary>Every OpenZFxPlayer blast: flare the bloom when it is on screen and big enough.</summary>
        void OnBlast(Vector3 at, float s)
        {
            if (s < boostMinS) return;
            var cam = Camera.main; if (!cam) return;
            var vp = cam.WorldToViewportPoint(at);
            if (vp.z < 0f || vp.x < -0.1f || vp.x > 1.1f || vp.y < -0.1f || vp.y > 1.1f) return;
            boost = Mathf.Min(boostMax, boost + boostPerS * (s - boostMinS + 0.4f));
        }

        /// <summary>Nuke / strike flash: exposure flash at weight `strength` (0-1) + bloom flare.</summary>
        public static void Flash(float strength = 1f)
        {
            if (!I) return;
            if (OzMotion.Reduced) { I.calmFlash = Mathf.Max(I.calmFlash, 0.22f * Mathf.Clamp01(strength)); I.boost = Mathf.Max(I.boost, I.flashBloom * 0.25f * Mathf.Clamp01(strength)); OzCalm.Note("exposure flash"); return; }
            I.flashW = Mathf.Max(I.flashW, Mathf.Clamp01(strength));
            I.boost = Mathf.Max(I.boost, I.flashBloom * Mathf.Clamp01(strength));
        }

        /// <summary>nuke_fx_v3: a burning nuke keeps the bloom up for this frame: extra intensity and a threshold drop (gamma).
        /// NukeFx calls it every frame while the fireball glows; consumed (reset) in LateUpdate.</summary>
        public static void Sustain(float extraIntensity, float thresholdDrop = 0f)
        {
            if (!I) return;
            I.sustain = Mathf.Max(I.sustain, extraIntensity); I.sustainDrop = Mathf.Max(I.sustainDrop, thresholdDrop);
        }

        /// <summary>Linear bloom threshold now (diagnostics / preview).</summary>
        public float LinearThreshold => Mathf.GammaToLinearSpace(bloom ? bloom.threshold.value : dayThreshold);

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            float n = NightGrade.NightK;
            boost *= Mathf.Exp(-boostDecay * dt);
            if (calmFlash > 0f) { flashW = Mathf.MoveTowards(flashW, calmFlash, dt * 0.4f); if (flashW >= calmFlash - 0.001f) calmFlash = 0f; }   // calm: rises over ~0.5 s, then a slow decay
            else flashW *= Mathf.Exp(-(OzMotion.Reduced ? 0.6f : flashDecay) * dt);
            if (flashW < 0.002f) flashW = 0f;
            bloom.threshold.value = Mathf.Max(0.3f, Mathf.Lerp(dayThreshold, nightThreshold, n) - sustainDrop);   // nuke_fx_v3: + the nuke sustain
            bloom.intensity.value = Mathf.Lerp(dayIntensity, nightIntensity, n) + boost + sustain;
            sustain = sustainDrop = 0f;
            bloom.scatter.value = Mathf.Lerp(dayScatter, nightScatter, n);
            bloom.tint.value = Color.Lerp(dayTint, nightTint, n);
            flashCa.postExposure.value = flashEv;
            flashVol.weight = flashW;
            EnsureCamera();
        }

        void EnsureCamera()
        {
            if (loggedCam) return;
            var cam = Camera.main; if (!cam) return;
            loggedCam = true;
            var data = cam.GetUniversalAdditionalCameraData();
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            bool hdr = cam.allowHDR && urp && urp.supportsHDR, post = data && data.renderPostProcessing;
            Debug.Log($"[OpenZ] bloom director: camera HDR {hdr}, post-processing {post}, day threshold {dayThreshold:F2} (linear " +
                      $"{Mathf.GammaToLinearSpace(dayThreshold):F2}), night {nightThreshold:F2} (linear {Mathf.GammaToLinearSpace(nightThreshold):F2}), " +
                      $"intensity {dayIntensity:F2} / {nightIntensity:F2}");
            if (!cam.allowHDR) { cam.allowHDR = true; Debug.LogWarning("[OpenZ] bloom director: the game camera had HDR off (no bloom): turned on"); }
            if (data && !data.renderPostProcessing) { data.renderPostProcessing = true; Debug.LogWarning("[OpenZ] bloom director: post-processing was off on the game camera: turned on"); }
            if (urp && !urp.supportsHDR) Debug.LogWarning("[OpenZ] bloom director: the URP asset has HDR off: bloom cannot see values above 1 (PC_RPAsset m_SupportsHDR)");
        }
    }
}
