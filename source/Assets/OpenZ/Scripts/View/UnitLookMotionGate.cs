using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// The URP motion blur (CameraAndObjects) smeared every unit by 20-40 px with a dithered zipper whenever the RTS camera panned
    /// or zoomed (measured on the T19 take: the camera moves in 19 % of the match; 11 of 34 day close-ups smeared). Fast fire and
    /// flying debris still streak, so the blur is gated by the camera instead of switched off:
    /// <list type="bullet">
    /// <item>the blur is the scene profile's own (OpenZ_PostFX MotionBlur intensity, capped at <see cref="maxIntensity"/>, clamp at
    /// most <see cref="stillClamp"/>): debris, thrown parts, shells and fire streak; a living unit (robot, vehicle, gun) writes no
    /// object motion at all (KeepUnitCrisp) and stays crisp at any speed</item>
    /// <item>the camera never causes blur: every move of it (the player's pan and zoom, a jump of the view, a kick or rumble, the RTS
    /// camera's own jitter, the ending's camera, a lens change) is kept out of the motion vectors (OpenZFxShake.KeepMotion), so the
    /// ground, buildings and everything that stands still stay sharp while flying parts, debris and shells blur, and nothing
    /// holds the blur back</item>
    /// <item>fallback, only when that is switched off or the pipeline data is not found: a moving camera sets the blur to 0 at once
    /// (back over <see cref="recover"/> s after it stops), and above <see cref="shakePx"/> px of shake the camera counts as moving</item>
    /// <item></item>
    /// </list>
    /// A priority-25 global runtime Volume (above the scene volume and NightGrade's 20; OpenZBloomDirector's 30 only drives
    /// Bloom) overrides MotionBlur intensity / clamp every frame. Game levels only (auto-boot like OpenZBloomDirector);
    /// OpenZ_PostFX.asset is not touched at run time. Off: <see cref="AutoCreate"/> = false or disable the component.
    /// </summary>
    [DefaultExecutionOrder(140)]
    public sealed class UnitLookMotionGate : MonoBehaviour
    {
        public static UnitLookMotionGate I { get; private set; }
        public static bool AutoCreate = true;

        /// <summary>
        /// ON, a saved choice wins; main menu + pause menu). On = URP MotionBlur CameraAndObjects at <see cref="OptionIntensity"/> / clamp
        /// <see cref="OptionClamp"/> (fast debris, shells and explosion parts streak; the camera itself never blurs the picture, see the class comment).
        /// Off = no motion blur at all (intensity 0 over the scene profile). Applies on the next frame.</summary>
        public const string PrefMotionBlur = "oz_motion_blur";
        public const float OptionIntensity = 0.6f, OptionClamp = 0.02f;
        static int mbPref = -1;
        public static bool MotionBlurOn
        {
            get { if (mbPref < 0) mbPref = PlayerPrefs.GetInt(PrefMotionBlur, 1) != 0 ? 1 : 0; return mbPref == 1; }
            set
            {
                mbPref = value ? 1 : 0; PlayerPrefs.SetInt(PrefMotionBlur, mbPref); PlayerPrefs.Save();
                Debug.Log($"[OpenZ] motion blur option: {(value ? "ON (CameraAndObjects " + OptionIntensity + " / clamp " + OptionClamp + ")" : "OFF")} (oz_motion_blur {mbPref}, saved)");
            }
        }

        [Tooltip("Upper limit of the scene profile's motion blur intensity while the camera stands still.")]
        [Range(0, 1)] public float maxIntensity = 0.6f;
        [Tooltip("Motion blur clamp (fraction of the screen) while the camera stands still.")]
        [Range(0, 0.2f)] public float stillClamp = 0.02f;
        [Tooltip("Fallback only (camera not kept out of the motion vectors): camera speed (m/s) above which the camera counts as moving.")]
        public float moveSpeed = 0.25f;
        [Tooltip("Fallback only: camera turn rate (deg/s) or zoom rate (FOV deg/s) above which the camera counts as moving.")]
        public float turnSpeed = 1.5f;
        [Tooltip("Fallback only: seconds after the camera stops before the blur is fully back.")]
        public float recover = 0.35f;
        [Tooltip("Only while the shake is not kept out of the motion vectors: camera shake (px at the 1080 px reference) above which the camera counts as moving.")]
        public float shakePx = 1f;
        [Tooltip("Off = the blur of OpenZ_PostFX applies unchanged (for a before / after).")]
        public bool gate = true;

        Volume vol; VolumeProfile prof; MotionBlur mb;
        Camera cam; Vector3 lastPos; Quaternion lastRot; float lastFov; bool haveLast; float stillFor;
        float baseIntensity, baseClamp = 0.04f, nextScan;

        /// <summary>The motion blur the scene's own volumes ask for (the highest-priority global volume with a MotionBlur
        /// override other than this gate), rescanned every 2 s.</summary>
        void ScanScene()
        {
            baseIntensity = 0f;
            float best = float.MinValue;
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (v == vol || !v.isActiveAndEnabled || !v.isGlobal || v.weight <= 0f) continue;
                var p = v.HasInstantiatedProfile() ? v.profile : v.sharedProfile;
                if (!p || !p.TryGet<MotionBlur>(out var m) || !m.active || !m.intensity.overrideState) continue;
                if (v.priority < best) continue;
                best = v.priority; baseIntensity = m.intensity.value; baseClamp = m.clamp.overrideState ? m.clamp.value : 0.05f;
            }
        }

        /// <summary>Skinned figures (robot bodies, crews, a thrown driver) are kept out of the per-object motion vectors: with the
        /// MOTION BLUR option on the engine drew every skinned robot body as a noisy smear of the ground behind it. Their rigid
        /// kit and weapons are kept out by KeepUnitCrisp while the unit lives; every thrown part keeps writing object motion.</summary>
        public static void KeepSkinnedCrisp(GameObject root)
        {
            if (!root) return;
            foreach (var s in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) s.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
        }

        /// <summary>A living unit never blurs. Every renderer under it (hull, turret, wheels, tracks, guns, kit, crew, also parts
        /// added after it was made) is kept out of the per-object motion vectors, so driving, turning, being pushed or towed,
        /// growing out of a factory door and spinning wheels leave it sharp. Normal path: the camera is kept out of the motion
        /// vectors; fallback path: the blur is off while the camera moves. Parts that leave a unit at its death (a blown-off turret, hull
        /// pieces, rolling wheels, a flipped wreck) are switched back to object motion by the code that throws them, so they
        /// still streak. Returns how many renderers were switched (0 = the unit was already clean).</summary>
        public static int KeepUnitCrisp(GameObject root)
        {
            if (!root) return 0;
            root.GetComponentsInChildren<Renderer>(true, crispScratch);
            int n = 0;
            for (int i = 0; i < crispScratch.Count; i++)
            {
                var r = crispScratch[i];
                if (r && r.motionVectorGenerationMode == MotionVectorGenerationMode.Object) { r.motionVectorGenerationMode = MotionVectorGenerationMode.Camera; n++; }
            }
            crispScratch.Clear();
            UnitRenderersSwitched += n;
            return n;
        }
        static readonly System.Collections.Generic.List<Renderer> crispScratch = new System.Collections.Generic.List<Renderer>(64);
        /// <summary>Check: renderers of living units taken out of the object motion so far (bind and later re-checks).</summary>
        public static int UnitRenderersSwitched;

        /// <summary>Current gated intensity (for the Play-mode check).</summary>
        public float Current { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!AutoCreate || I) return;
            if (!ZGame.I && !FindFirstObjectByType<ZGame>()) return;                         // game levels only
            new GameObject("UnitLookMotionGate").AddComponent<UnitLookMotionGate>();
        }

        void Awake()
        {
            if (I && I != this) { Destroy(gameObject); return; }
            I = this;
            prof = ScriptableObject.CreateInstance<VolumeProfile>(); prof.name = "OZ_UnitLookMotionGate (runtime)"; prof.hideFlags = HideFlags.DontSave;
            mb = prof.Add<MotionBlur>(false);
            mb.mode.Override(MotionBlurMode.CameraAndObjects); mb.quality.Override(MotionBlurQuality.Medium);
            mb.intensity.Override(0f); mb.clamp.Override(stillClamp);
            vol = gameObject.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 25f; vol.weight = 1f; vol.sharedProfile = prof;
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            if (prof)
            {
                foreach (var c in prof.components) if (c) Destroy(c);
                Destroy(prof);
            }
        }

        void LateUpdate()
        {
            if (!vol) return;
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 2f; ScanScene(); }
            bool opt = MotionBlurOn;
            if (!opt) { vol.weight = 1f; Current = 0f; mb.intensity.Override(0f); return; }
            if (baseIntensity < OptionIntensity) { baseIntensity = OptionIntensity; baseClamp = OptionClamp; }
            vol.weight = 1f;
            if (!gate) { Current = Mathf.Min(baseIntensity, maxIntensity); mb.intensity.Override(Current); mb.clamp.Override(Mathf.Min(baseClamp, stillClamp)); return; }
            if (!cam || !cam.isActiveAndEnabled) { cam = Camera.main; haveLast = false; }
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            bool moving = false;
            if (cam)
            {
                var t = cam.transform;
                // While the camera is kept out of the motion vectors (OpenZFxShake.KeepMotion: pan, zoom, jump, shake, jitter, the
                // ending's camera, the lens) nothing the camera does can smear the picture, so nothing holds the blur back: only things
                // that move in the world blur. The camera test below is the fallback for when that is switched off or not available.
                bool kept = OpenZFxShake.MotionKept;
                Vector3 pos = t.position;
                if (haveLast && !kept)
                {
                    float v = (pos - lastPos).magnitude / dt;
                    float w = Quaternion.Angle(t.rotation, lastRot) / dt;
                    float z = Mathf.Abs(cam.fieldOfView - lastFov) / dt;
                    moving = v > moveSpeed || w > turnSpeed || z > turnSpeed;
                }
                lastPos = pos; lastRot = t.rotation; lastFov = cam.fieldOfView; haveLast = true;
                if (!kept && OpenZFxShake.I != null && OpenZFxShake.I.AmountPx(cam) > shakePx) moving = true;       // not kept out (switched off, or the pipeline data was not found): no blur until a kick or a rumble has settled
            }
            stillFor = moving ? 0f : stillFor + dt;
            float k = recover <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(recover * 0.3f, recover, stillFor));
            Current = Mathf.Min(baseIntensity, maxIntensity) * k;
            mb.intensity.Override(Current);
            mb.clamp.Override(Mathf.Min(baseClamp, stillClamp));
        }
    }
}
