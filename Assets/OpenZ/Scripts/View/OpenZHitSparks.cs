using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// Replaces, for bullets hitting robots, vehicles, cannons and buildings, the old hit look (M12 bolt impact = 5 fixed
    /// 1 m streaks on a golden-angle star + 8 round LEG glow dots + a ball core, all at constant length and no fade).
    ///
    /// One burst = 12-30 tiny stretched-billboard streaks (length grows with speed: motion-blur look), HDR white-hot -> orange -> red
    /// over a 0.14-0.5 s life (gain in linear space on OpenZ/FxAdditive shape 1, so the peak sits ~6-9 over the ~1.3 bloom
    /// threshold and the orange keeps its hue under ACES), gravity, world collision with the walkable ground (bounce + dampen
    /// 0.3-0.5, life lost per bounce), a few slow glowing embers that hop on the ground, a 0.05 s hot core flash with a soft halo,
    /// a small pooled URP point light (range ~2.5 m, 0.08 s flicker, capped), a faint smoke puff on armour, and a varied metal
    /// Spray = the shot reflected about a jittered surface normal facing the shooter, in a cone (+ a few wide ones).
    /// Scale per weapon: rifle / bolt small, gatling medium, sniper and driver hits big. Lasers keep their own green impact,
    /// ground hits keep their dust (hard ground: a few dim chips instead of the old star).
    /// Pooled: two ParticleSystems + a light pool built once, fixed pending ring, no per-hit allocation; bursts per frame / second
    /// capped (starved bursts shrink instead of vanishing), off-screen hits skipped, fewer streaks with distance / a full pool.
    /// Hooks: OpenZFxPlayer.Shot (Take), OpenZFxPlayer.Impact bolt block (Chips), CombatView.DriverHit / Sniped (Driver).
    /// </summary>
    [DefaultExecutionOrder(112)]
    public class OpenZHitSparks : MonoBehaviour
    {
        public static OpenZHitSparks I { get; private set; }
        /// <summary>Creates the component in every scene that has a ZGame (like OpenZFxPlayer).</summary>
        public static bool AutoCreate = true;
        public static bool Active => I != null && I.ready && I.isActiveAndEnabled;
        /// <summary></summary>
        public const string SoundKey = "hit_ricochet";
        /// <summary></summary>
        public static readonly string[] Whine = { "v3-armor-ricochet-a", "hs_ricochet_a", "hs_ricochet_b", "hs_ricochet_c", "hs_ricochet_d" };
        public static readonly string[] Tink = { "hs_tink_a", "hs_tink_b", "hs_tink_c" };
        public static readonly string[] Bank = { "v3-armor-ricochet-a", "hs_ricochet_a", "hs_ricochet_b", "hs_ricochet_c", "hs_ricochet_d", "hs_tink_a", "hs_tink_b", "hs_tink_c" };
        public const float BankGain = 0.42f;

        [Header("Streaks (linear HDR gain; URP bloom threshold ~1.25-1.45)")]
        [Tooltip("Peak HDR of a white-hot streak (x the white-hot vertex colour)")] public float streakGain = 7.5f;
        [Tooltip("Stretched billboard: extra length per m/s of speed (s)")] public float velocityScale = 0.032f;
        [Tooltip("m/s^2 down")] public float gravity = 11.5f;
        [Range(0f, 1f)] public float bounce = 0.4f;
        [Range(0f, 1f), Tooltip("Speed lost per ground contact")] public float dampen = 0.4f;
        [Range(0f, 1f), Tooltip("Life lost per ground contact")] public float lifetimeLoss = 0.2f;
        [Tooltip("Linear air drag (1/s)")] public float drag = 1.4f;
        public float countScale = 1f, sizeScale = 1f;
        [Tooltip("RTS readability floor: min on-screen size (fraction of the viewport)")] public float minScreenSize = 0.0008f;
        [Header("Flash")]
        public float flashGain = 9f;
        public float flashScale = 1f;
        [Header("Light")]
        public bool lights = true;
        [Range(0, 8)] public int maxLights = 4;
        public float lightGain = 1f, lightRange = 2.5f, lightLife = 0.08f;
        [Tooltip("No light for bursts farther than this from the camera (m)")] public float lightMaxDistance = 170f;
        [Header("Sound")]
        public bool sounds = true;
        public float soundGain = 1f;
        public int maxSoundsPerSecond = 8;
        public float minSoundGap = 0.06f;
        [Header("Budget")]
        public int maxBurstsPerFrame = 8;
        public int maxBurstsPerSecond = 60;
        public bool smoke = true;

        public int Bursts { get; private set; }
        public int Streaks { get; private set; }
        public int Culled { get; private set; }
        public int Dropped { get; private set; }
        public int Starved { get; private set; }
        public int SoundsPlayed { get; private set; }
        public int LiveLights { get; private set; }
        public int LiveStreaks => streakPs ? streakPs.particleCount : 0;

        // ------------------------------------------------------------------------------------------------ profiles
        enum Kind : byte { Rifle, Bolt, MachineGun, Sniper, Driver, Chips }
        struct Profile
        {
            public int nMin, nMax, embers;
            public float vMin, vMax, lifeMin, lifeMax, wMin, wMax, cone, flash, light, snd, vol;
        }
        static Profile Pf(int nMin, int nMax, int embers, float vMin, float vMax, float lifeMin, float lifeMax, float wMin, float wMax,
            float cone, float flash, float light, float snd, float vol) => new Profile
        {
            nMin = nMin, nMax = nMax, embers = embers, vMin = vMin, vMax = vMax, lifeMin = lifeMin, lifeMax = lifeMax, wMin = wMin, wMax = wMax,
            cone = cone, flash = flash, light = light, snd = snd, vol = vol,
        };
        //                                      count   emb  speed m/s    life s        width m        cone  flash  light  sound  vol
        static readonly Profile[] P =
        {
            Pf(14, 18, 2, 4.5f, 10f,   0.14f, 0.34f, 0.035f, 0.055f, 50f, 0.45f, 2.3f, 0.50f, 0.40f),   // Rifle (APC crew, fallback)
            Pf(12, 18, 2, 4.0f, 9.5f,  0.14f, 0.34f, 0.035f, 0.055f, 55f, 0.50f, 2.4f, 0.45f, 0.40f),   // Bolt (grunt, psycho, jeep)
            Pf(18, 24, 3, 5.0f, 11.5f, 0.15f, 0.40f, 0.040f, 0.060f, 50f, 0.60f, 2.6f, 0.30f, 0.40f),   // MachineGun (gatling)
            Pf(24, 30, 4, 6.0f, 14f,   0.20f, 0.50f, 0.045f, 0.070f, 40f, 0.85f, 2.9f, 1.00f, 0.55f),   // Sniper
            Pf(24, 30, 4, 5.0f, 12f,   0.20f, 0.50f, 0.045f, 0.070f, 60f, 0.90f, 3.0f, 0.00f, 0.00f),   // Driver hit / snipe (CombatView plays its sound)
            Pf(5, 7, 0,   3.0f, 7f,    0.10f, 0.24f, 0.030f, 0.045f, 60f, 0.25f, 0.0f, 0.00f, 0.00f),   // Chips: bolt on road / rock
        };
        static Kind KindOf(FxWeapon w) => w == FxWeapon.Bolt ? Kind.Bolt : w == FxWeapon.MachineGun ? Kind.MachineGun : w == FxWeapon.Sniper ? Kind.Sniper : Kind.Rifle;

        // sRGB particle colours (the renderer converts to linear; the gain is the material's linear _Intensity)
        static readonly Color HotA = new Color(1f, 0.97f, 0.9f), HotB = new Color(1f, 0.9f, 0.7f), EmberC = new Color(1f, 0.72f, 0.36f);
        static readonly Color FlashCore = new Color(1f, 0.93f, 0.8f), FlashHalo = new Color(1f, 0.55f, 0.2f), SmokeC = new Color(0.42f, 0.42f, 0.4f);
        static readonly Color LightHot = new Color(1f, 0.9f, 0.75f), LightWarm = new Color(1f, 0.55f, 0.22f);

        // ------------------------------------------------------------------------------------------------ bootstrap
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!AutoCreate || I) return;
            if (!ZGame.I && !FindFirstObjectByType<ZGame>()) return;
            new GameObject("OpenZHitSparks").AddComponent<OpenZHitSparks>();
        }

        // ------------------------------------------------------------------------------------------------ state
        ParticleSystem streakPs, flashPs;
        Material streakMat, flashMat;
        ParticleSystem.EmitParams ep;
        Camera cam;
        bool ready, maskSet;
        uint rng = 0x2545F491u;
        float tokens;
        int sparkSmooth = -1;                                                                              // the MOTION BLUR option's state last applied to the streaks

        struct Pending { public float at; public Vector3 pos, dir; public Kind kind; public FxSurface surf; public bool anchored; public HitSurface.Anchor anc; }   // IM1: anchored on the target's surface
        readonly Pending[] pend = new Pending[256];
        int pendCount;

        sealed class FxL { public Light l; public float age = 1f, life = 0f, i0, seed; }
        readonly FxL[] lightPool = new FxL[8];
        int newLightsThisFrame;

        readonly string[] one = new string[1];
        readonly float[] soundTimes = new float[16];
        int soundHead, lastClipA = -1, lastClipB = -1;
        float lastSound = -10f;

        const int StreakCapacity = 1400, FlashCapacity = 160;
        static readonly int IdShape = Shader.PropertyToID("_Shape"), IdOpq = Shader.PropertyToID("_Opaqueness"), IdColor = Shader.PropertyToID("_Color"),
            IdIntensity = Shader.PropertyToID("_Intensity"), IdCore = Shader.PropertyToID("_CoreColor"), IdCoreMix = Shader.PropertyToID("_CoreMix"),
            IdUseVc = Shader.PropertyToID("_UseVertexColor"), IdSquare = Shader.PropertyToID("_Squareness"), IdLit = Shader.PropertyToID("_Lit"),
            IdEmission = Shader.PropertyToID("_EmissionColor"), IdSoft = Shader.PropertyToID("_SoftFade"), IdFireOpacity = Shader.PropertyToID("_FireOpacity"),
            IdFireRamp = Shader.PropertyToID("_FireRamp");

        // ------------------------------------------------------------------------------------------------ public API
        /// <summary>OpenZFxPlayer.Shot: a bullet / bolt that will reach a robot (Unit) or a vehicle, cannon or building (Metal) at time
        /// `at`. False = not handled (other surface or no spark system): the caller keeps its own impact.</summary>
        public static bool Take(float at, Vector3 pos, Vector3 dir, FxWeapon w, FxSurface s)
        {
            if (!Active || (s != FxSurface.Unit && s != FxSurface.Metal)) return false;
            int c0 = I.pendCount; I.Enqueue(at, pos, dir, KindOf(w), s);
            if (I.pendCount > c0 && HitSurface.Claim(pos, out var anc)) { I.pend[I.pendCount - 1].anchored = true; I.pend[I.pendCount - 1].anc = anc; }   // IM1: follows the body during the flight
            return true;
        }

        /// <summary>An immediate burst (tests, other views). dir = the travel direction of the round.</summary>
        public static bool Burst(Vector3 pos, Vector3 dir, FxWeapon w, FxSurface s, float scale = 1f, bool sound = true)
        {
            if (!Active) return false;
            I.DoBurst(pos, dir, KindOf(w), s, scale, sound);
            return true;
        }

        /// <summary>CombatView driver hit / snipe at the seat: a big burst, no sound (CombatView plays driverhit / sniped).</summary>
        public static bool Driver(Vector3 seat, Vector3 dir, bool killed)
        {
            if (!Active) return false;
            I.DoBurst(seat, dir, Kind.Driver, FxSurface.Metal, killed ? 1.35f : 1f, false);
            return true;
        }

        /// <summary>OpenZFxPlayer.Impact bolt block (misses): replaces the 5-point M12 star. Road / rock: a few dim chips; soft ground:
        /// nothing extra (its dust and sand glow stay). False = no spark system (the caller draws the old star).</summary>
        public static bool Chips(Vector3 pos, FxSurface s)
        {
            if (!Active) return false;
            if (s == FxSurface.Road || s == FxSurface.Rock) I.DoBurst(pos, Vector3.down, Kind.Chips, s, 1f, false);
            return true;
        }

        public string Stats() => $"bursts {Bursts} streaks {Streaks} live {LiveStreaks} culled {Culled} dropped {Dropped} starved {Starved} sounds {SoundsPlayed} lights {LiveLights}";

        // ------------------------------------------------------------------------------------------------ lifecycle
        void Awake()
        {
            if (I && I != this) { Destroy(gameObject); return; }
            I = this;
            cam = Camera.main;
            var lib = Resources.Load<Material>("OpenZ/FX/M_FxAdditive");                           // keeps the shader in player builds
            var shader = lib && lib.shader ? lib.shader : Shader.Find("OpenZ/FxAdditive");
            if (!shader) { Debug.LogWarning("OpenZHitSparks: shader OpenZ/FxAdditive missing: old hit sparks stay"); enabled = false; return; }
            streakMat = new Material(shader) { name = "FX_HitSparkStreak", renderQueue = 3051 };
            flashMat = new Material(shader) { name = "FX_HitSparkFlash", renderQueue = 3052 };
            ApplyLook();
            streakPs = BuildSystem("HitSparks_Streaks", streakMat, StreakCapacity, true);
            flashPs = BuildSystem("HitSparks_Flash", flashMat, FlashCapacity, false);
            for (int i = 0; i < lightPool.Length; i++)
            {
                var go = new GameObject("HitSparkLight" + i); go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>(); l.type = LightType.Point; l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel; l.enabled = false;
                lightPool[i] = new FxL { l = l, seed = i * 2.17f };
            }
            ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            tokens = maxBurstsPerSecond * 0.25f;
            ready = true;
        }

        void OnDestroy() { if (I == this) I = null; }

        void OnValidate() { if (streakMat) ApplyLook(); }

        static void SetLin(Material m, int id, Color linear) => m.SetVector(id, new Vector4(linear.r, linear.g, linear.b, linear.a));

        /// <summary>Material gains (linear). Called on Awake and when a value changes in the inspector (live tuning in Play).</summary>
        public void ApplyLook()
        {
            // streaks: OpenZ/FxAdditive shape 1 with squareness 0 = a soft ellipse, stretched along the velocity into a needle
            streakMat.SetFloat(IdShape, 1f); streakMat.SetFloat(IdOpq, 0f); streakMat.SetFloat(IdUseVc, 1f); streakMat.SetFloat(IdSquare, 0f);
            streakMat.SetFloat(IdLit, 0f); streakMat.SetFloat(IdSoft, 0f); streakMat.SetFloat(IdFireRamp, 0f);
            streakMat.SetVector(IdFireOpacity, new Vector4(-1f, 0f, 0f, 0f));
            SetLin(streakMat, IdColor, Color.white); SetLin(streakMat, IdEmission, Color.black);
            SetLin(streakMat, IdCore, new Color(1f, 0.85f, 0.6f).linear); streakMat.SetFloat(IdCoreMix, 0.4f);
            streakMat.SetFloat(IdIntensity, streakGain);
            // flash: shape 0 (LEG point sprite core + halo), soft against the body it hits
            flashMat.SetFloat(IdShape, 0f); flashMat.SetFloat(IdOpq, 0f); flashMat.SetFloat(IdUseVc, 1f); flashMat.SetFloat(IdLit, 0f);
            flashMat.SetFloat(IdSoft, 0.2f); flashMat.SetFloat(IdFireRamp, 0f); flashMat.SetVector(IdFireOpacity, new Vector4(-1f, 0f, 0f, 0f));
            SetLin(flashMat, IdColor, Color.white); SetLin(flashMat, IdEmission, Color.black);
            SetLin(flashMat, IdCore, new Color(1f, 0.96f, 0.88f).linear); flashMat.SetFloat(IdCoreMix, 0.85f);
            flashMat.SetFloat(IdIntensity, flashGain);
            if (streakPs)
            {
                var main = streakPs.main; main.gravityModifier = gravity / Mathf.Max(0.01f, Mathf.Abs(Physics.gravity.y));
                var c = streakPs.collision; c.bounce = bounce; c.dampen = dampen; c.lifetimeLoss = lifetimeLoss;
                var lv = streakPs.limitVelocityOverLifetime; lv.drag = drag;
                var r = streakPs.GetComponent<ParticleSystemRenderer>(); r.velocityScale = velocityScale; r.minParticleSize = minScreenSize; sparkSmooth = -1;   // the option's streak values are applied again on the next frame
            }
        }

        static readonly System.Collections.Generic.List<ParticleSystemVertexStream> Streams = new System.Collections.Generic.List<ParticleSystemVertexStream>
        {   // same streams as OpenZFxPlayer (the shader reads TEXCOORD0.zw = age, stable random)
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.AgePercent, ParticleSystemVertexStream.StableRandomX,
        };

        ParticleSystem BuildSystem(string name, Material mat, int capacity, bool streaks)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();
            var main = ps.main;
            main.playOnAwake = false; main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.maxParticles = capacity;
            main.startSpeed = 0f; main.startLifetime = 1f; main.startSize = 1f;
            main.gravityModifier = streaks ? gravity / Mathf.Max(0.01f, Mathf.Abs(Physics.gravity.y)) : 0f;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;

            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, streaks
                ? new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 0.8f), new Keyframe(1f, 0.4f))          // thins as it cools
                : new AnimationCurve(new Keyframe(0f, 0.75f, 0f, 3f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.55f)));   // flash pops, then shrinks

            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            if (streaks)
                g.SetKeys(
                    new[] { new GradientColorKey(new Color(1f, 1f, 1f), 0f), new GradientColorKey(new Color(1f, 0.86f, 0.55f), 0.12f),
                            new GradientColorKey(new Color(1f, 0.62f, 0.22f), 0.35f), new GradientColorKey(new Color(1f, 0.4f, 0.09f), 0.7f),
                            new GradientColorKey(new Color(0.8f, 0.2f, 0.04f), 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0.6f, 0.8f), new GradientAlphaKey(0f, 1f) });
            else
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.75f, 0.45f), 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.35f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            if (streaks)
            {
                var lv = ps.limitVelocityOverLifetime; lv.enabled = true;
                lv.limit = 1000f; lv.dampen = 0f; lv.drag = drag;
                lv.multiplyDragByParticleSize = false; lv.multiplyDragByParticleVelocity = false;
                var c = ps.collision; c.enabled = true;
                c.type = ParticleSystemCollisionType.World; c.mode = ParticleSystemCollisionMode.Collision3D;
                c.quality = ParticleSystemCollisionQuality.Medium; c.enableDynamicColliders = false; c.maxCollisionShapes = 64;
                c.collidesWith = ZGame.WalkLayer >= 0 ? (1 << ZGame.WalkLayer) : Physics.DefaultRaycastLayers;
                c.bounce = bounce; c.dampen = dampen; c.lifetimeLoss = lifetimeLoss; c.minKillSpeed = 0.15f; c.radiusScale = 0.3f;
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.lengthScale = 1f;                      // at rest a dot as long as it is wide ...
                r.velocityScale = velocityScale;         // ... plus 3.2 cm per m/s: a 10 m/s spark is a ~0.35 m streak
                r.cameraVelocityScale = 0f;
            }
            else r.renderMode = ParticleSystemRenderMode.Billboard;
            r.alignment = ParticleSystemRenderSpace.View;
            r.sortMode = ParticleSystemSortMode.None;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.minParticleSize = streaks ? minScreenSize : 0f; r.maxParticleSize = 0.5f;
            r.applyActiveColorSpace = true;
            r.sharedMaterial = mat;
            r.SetActiveVertexStreams(Streams);
            go.SetActive(true);
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------------------------------------ queue
        Vector3 surfN; bool hasSurfN;                                                                      // IM1: the burst's real surface normal

        void Enqueue(float at, Vector3 pos, Vector3 dir, Kind kind, FxSurface s)
        {
            if (pendCount >= pend.Length) { Dropped++; return; }
            pend[pendCount++] = new Pending { at = at, pos = pos, dir = dir, kind = kind, surf = s };
        }

        void LateUpdate()
        {
            if (!ready) return;
            if (!cam) cam = Camera.main;
            if (sparkSmooth != (OzFxSmooth.On ? 1 : 0))
            {   // MOTION BLUR option: each streak is longer with its speed, with a round head and a tail that fades to nothing
                sparkSmooth = OzFxSmooth.On ? 1 : 0;
                var sr = streakPs.GetComponent<ParticleSystemRenderer>();
                sr.velocityScale = sparkSmooth == 1 ? Mathf.Max(velocityScale, OzFxSmooth.SparkSeconds) : velocityScale;
                streakMat.SetFloat("_StreakBlur", sparkSmooth == 1 ? Mathf.Max(0.1f, OzFxSmooth.SparkFade) : 0f);
            }
            if (!maskSet && ZGame.WalkLayer >= 0)
            {   // ZGame picks its walkable layer at start-up: collide with terrain + bridge decks only (not units, props, debris)
                var c = streakPs.collision; c.collidesWith = 1 << ZGame.WalkLayer; maskSet = true;
            }
            float dt = Time.deltaTime;
            tokens = Mathf.Min(tokens + dt * maxBurstsPerSecond, Mathf.Max(1f, maxBurstsPerSecond * 0.25f));
            newLightsThisFrame = 0;
            float now = Time.time; int budget = maxBurstsPerFrame;
            for (int i = pendCount - 1; i >= 0; i--)
            {   // backwards with swap-remove: the element moved into slot i was already looked at
                if (pend[i].at > now) continue;
                var p = pend[i]; pend[i] = pend[--pendCount];
                if (budget-- <= 0) { Dropped++; continue; }
                if (p.anchored && HitSurface.Now(p.anc, out var sp, out var sn)) { surfN = sn; hasSurfN = true; DoBurst(sp, p.dir, p.kind, p.surf, 1f, true); hasSurfN = false; }   // IM1
                else DoBurst(p.pos, p.dir, p.kind, p.surf, 1f, true);
            }
            StepLights(dt);
        }

        // ------------------------------------------------------------------------------------------------ burst
        float R() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }

        /// <summary>Random unit vector inside a cone of half-angle deg around the unit axis (uniform over the cap).</summary>
        Vector3 Cone(Vector3 axis, float deg)
        {
            float z = Mathf.Lerp(Mathf.Cos(deg * Mathf.Deg2Rad), 1f, R()), phi = R() * 6.2831853f, s = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            var t1 = Mathf.Abs(axis.y) < 0.95f ? Vector3.Cross(axis, Vector3.up).normalized : Vector3.Cross(axis, Vector3.right).normalized;
            var t2 = Vector3.Cross(axis, t1);
            return axis * z + t1 * (s * Mathf.Cos(phi)) + t2 * (s * Mathf.Sin(phi));
        }

        void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float life, float size, Color c, float alpha)
        {
            ep.position = pos; ep.velocity = vel; ep.startLifetime = Mathf.Max(0.02f, life); ep.startSize = size;
            c.a = Mathf.Clamp01(alpha); ep.startColor = c;
            ps.Emit(ep, 1);
        }

        void DoBurst(Vector3 pos, Vector3 dir, Kind kind, FxSurface surf, float scale, bool sound)
        {
            float lod = 1f, dist = 60f;
            if (cam)
            {
                var vp = cam.WorldToViewportPoint(pos);
                if (vp.z <= 0f || vp.x < -0.1f || vp.x > 1.1f || vp.y < -0.1f || vp.y > 1.1f) { Culled++; return; }   // off screen: nothing to see or hear
                dist = vp.z;
                lod = dist <= 60f ? 1f : Mathf.Lerp(1f, 0.45f, (dist - 60f) / 120f);
            }
            bool starved = tokens < 1f;
            if (starved) { lod *= 0.4f; Starved++; } else tokens -= 1f;
            if (streakPs.particleCount > StreakCapacity * 0.7f) lod *= 0.5f;
            bool far = dist > lightMaxDistance;
            var pr = P[(int)kind];

            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.down;
            dir.Normalize();
            // surface normal: faces the shooter, tilted up, jittered sideways (armour plates face every way); spray = the round
            // reflected about it, pulled a little toward the normal and up so the burst reads from the RTS camera
            var h = new Vector3(-dir.x, 0f, -dir.z);
            if (h.sqrMagnitude < 1e-4f) h = Vector3.forward;
            h.Normalize();
            var side = Vector3.Cross(Vector3.up, h);
            var n = (h * 0.85f + Vector3.up * (0.3f + 0.3f * R()) + side * ((R() - 0.5f) * 0.9f)).normalized;
            if (hasSurfN) n = (surfN * 0.8f + Vector3.up * 0.15f + side * ((R() - 0.5f) * 0.25f)).normalized;   // IM1: the real surface normal (a little up for the RTS camera)
            if (kind == Kind.Chips) n = (Vector3.up + h * 0.4f).normalized;
            var refl = dir - 2f * Vector3.Dot(dir, n) * n;
            var spray = (refl * 0.7f + n * 0.3f + Vector3.up * 0.15f).normalized;
            // a robot's aim point sits inside its body: start the burst on the shooter's side of it
            var at = hasSurfN ? pos + surfN * 0.03f : pos - dir * (surf == FxSurface.Unit ? 0.28f : 0.06f);   // IM1: on the surface, not inset

            float sq = Mathf.Sqrt(scale);
            int count = Mathf.Max(3, Mathf.RoundToInt(Mathf.Lerp(pr.nMin, pr.nMax, R()) * countScale * scale * lod * (surf == FxSurface.Unit ? 0.85f : 1f)));
            float wMul = sizeScale * (far ? 1.3f : 1f);
            float alpha = kind == Kind.Chips ? 0.5f : 1f;
            for (int i = 0; i < count; i++)
            {
                bool wide = R() < 0.22f;
                var d = Cone(wide ? n : spray, wide ? 80f : pr.cone);
                if (d.y < -0.25f) d.y = -d.y * 0.5f;                                                // never straight into the ground
                float v = Mathf.Lerp(pr.vMin, pr.vMax, Mathf.Pow(R(), 0.8f)) * sq;
                Emit(streakPs, at + d * 0.03f, d * v, Mathf.Lerp(pr.lifeMin, pr.lifeMax, R()), Mathf.Lerp(pr.wMin, pr.wMax, R()) * wMul,
                    R() < 0.4f ? HotA : HotB, alpha);
            }
            int embers = Mathf.CeilToInt(pr.embers * lod);
            for (int i = 0; i < embers; i++)
            {   // slow glowing bits that drop, bounce and hop on the ground
                var d = Cone(n, 70f); d.y = Mathf.Abs(d.y) + 0.3f;
                Emit(streakPs, at, d.normalized * Mathf.Lerp(1.2f, 3.2f, R()) * sq, Mathf.Lerp(0.35f, 0.6f, R()), Mathf.Lerp(0.06f, 0.08f, R()) * wMul, EmberC, 1f);
            }
            Streaks += count + embers;

            // 0.05 s hot core + a soft short halo (reads at the full overview where the streaks are a few pixels)
            float fs = pr.flash * flashScale * sq * (far ? 1.4f : 1f);
            var fp = at + n * 0.05f;
            Emit(flashPs, fp, Vector3.zero, 0.055f, fs, FlashCore, kind == Kind.Chips ? 0.5f : 1f);
            if (kind != Kind.Chips) Emit(flashPs, fp, Vector3.zero, 0.1f, fs * 2.6f, FlashHalo, 0.3f);

            if (smoke && kind != Kind.Chips && (surf == FxSurface.Metal || kind == Kind.Sniper) && OpenZFxPlayer.I)
                OpenZFxPlayer.I.EmitChannel("leg_smoke", at + n * 0.1f, n * 0.4f + Vector3.up * 0.3f, 0.45f, 0.28f * sq, SmokeC, surf == FxSurface.Unit ? 0.22f : 0.35f);

            if (lights && !far && !starved && pr.light > 0f) AddLight(at + n * 0.25f, pr.light * sq, lightRange * Mathf.Lerp(0.85f, 1.15f, R()) * sq);
            if (sound && sounds && pr.snd > 0f) PlayRicochet(at, kind, surf);
            Bursts++;
        }

        // ------------------------------------------------------------------------------------------------ light pool
        void AddLight(Vector3 at, float intensity, float range)
        {
            if (maxLights <= 0 || newLightsThisFrame >= 2) return;
            FxL best = null;
            int n = Mathf.Min(maxLights, lightPool.Length);
            for (int i = 0; i < n; i++)
            {
                var x = lightPool[i];
                if (x.age >= x.life) { best = x; break; }
                if (x.age > 0.03f && (best == null || x.age > best.age)) best = x;   // steal the oldest (past its peak) only
            }
            if (best == null) return;
            newLightsThisFrame++;
            best.age = 0f; best.life = Mathf.Max(0.03f, lightLife); best.i0 = intensity * lightGain;
            best.l.transform.position = at; best.l.range = Mathf.Max(0.5f, range); best.l.color = LightHot; best.l.intensity = best.i0; best.l.enabled = true;
        }

        void StepLights(float dt)
        {
            int live = 0;
            for (int i = 0; i < lightPool.Length; i++)
            {
                var x = lightPool[i];
                if (!x.l.enabled) continue;
                x.age += dt;
                if (x.age >= x.life) { x.l.enabled = false; continue; }
                live++;
                float k = x.age / x.life;
                float flick = 0.72f + 0.28f * Mathf.Sin(x.age * 190f + x.seed);
                x.l.intensity = x.i0 * Mathf.Pow(1f - k, 1.6f) * flick;
                x.l.color = Color.Lerp(LightHot, LightWarm, k);
            }
            LiveLights = live;
        }

        // ------------------------------------------------------------------------------------------------ ricochet sound
        void PlayRicochet(Vector3 at, Kind kind, FxSurface surf)
        {
            var au = CombatAudio.I; if (!au) return;
            var pr = P[(int)kind];
            if (R() > pr.snd * (surf == FxSurface.Unit ? 0.85f : 1f)) return;
            float now = Time.time;
            if (now - lastSound < minSoundGap) return;
            int inWindow = 0;
            for (int j = 0; j < soundTimes.Length; j++) if (now - soundTimes[j] < 1f) inWindow++;
            if (inWindow >= maxSoundsPerSecond) return;
            // robots: more pings off small plates; vehicles: more whining ricochets; snipers: always the long whine
            bool whine = kind == Kind.Sniper || R() < (surf == FxSurface.Unit ? 0.45f : 0.65f);
            var bank = whine ? Whine : Tink;
            int off = whine ? 0 : Whine.Length, idx = 0;
            for (int guard = 0; guard < 6; guard++)
            {   // never one of the last two clips again
                idx = Mathf.Min(bank.Length - 1, (int)(R() * bank.Length));
                if (off + idx != lastClipA && off + idx != lastClipB && au.HasClip(bank[idx])) break;
            }
            if (!au.HasClip(bank[idx])) { idx = 0; bank = Whine; off = 0; }
            lastClipB = lastClipA; lastClipA = off + idx;
            one[0] = bank[idx];
            float pitch = Mathf.Lerp(0.92f, 1.12f, R());                                              // x CombatAudio's +-3.5 %: ~0.89-1.16
            au.PlayPitched(SoundKey, at, pr.vol * soundGain * Mathf.Lerp(0.8f, 1f, R()), 0f, -1, pitch, one);
            lastSound = now; soundTimes[soundHead] = now; soundHead = (soundHead + 1) % soundTimes.Length;
            SoundsPlayed++;
        }

        // ------------------------------------------------------------------------------------------------ verification helper
        /// <summary>Play check: 16 bursts at the camera focus, every weapon on armour and on a robot, 0.35 s apart.</summary>
        [ContextMenu("Test: 16 bursts at the camera focus")]
        public void TestBursts() { if (ready) StartCoroutine(TestRoutine()); }

        IEnumerator TestRoutine()
        {
            var rts = FindFirstObjectByType<RtsCamera>();
            var c = rts ? rts.focus : (cam ? cam.transform.position + cam.transform.forward * 60f : Vector3.zero);
            if (ZGame.I) c.y = ZGame.I.GroundY(c.x, c.z);
            var ws = new[] { FxWeapon.Bolt, FxWeapon.Rifle, FxWeapon.MachineGun, FxWeapon.Sniper };
            for (int i = 0; i < 16; i++)
            {
                float a = i * 2.399f;
                var from = c + new Vector3(Mathf.Cos(a) * 12f, 1.2f, Mathf.Sin(a) * 12f);
                var to = c + new Vector3(0f, i % 2 == 0 ? 1.1f : 1.6f, 0f);
                DoBurst(to, to - from, KindOf(ws[i % 4]), i % 2 == 0 ? FxSurface.Unit : FxSurface.Metal, 1f, true);
                yield return new WaitForSeconds(0.35f);
            }
            Debug.Log("[OpenZHitSparks] test done: " + Stats());
        }
    }
}
