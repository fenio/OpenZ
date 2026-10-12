using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// The Pyro's weapon, drawn and heard like the flamethrower of our earlier projects (flame jet, flame pool, burn surface, napalm
    /// ground):
    /// <list type="bullet">
    /// <item><b>Jet</b>: while a Pyro fires (every drawn Zod shot re-cues its jet for .4 s, cue()), 300 flipbook
    /// sprites/s leave the flamer's Muzzle (read AFTER WeaponHold placed the gun: this runs at order 97), fly at Zod's flame speed
    /// (300 px/s = 37.5 m/s x game speed, eflame.cpp:10), spread to a 1.77 m end radius, grow 3.45x and burn from yellow to
    /// red (OpenZPyroFlamePool + OpenZ/PyroFlame, HDR additive: blooms day and night). The stream ends on the target like Zod's
    /// EFlame (the sim hits the target only), with a short splash past it.</item>
    /// <item><b>Sticky fire</b>: the stream's arrival sets its target burning (5 s; a 12.5 s structure time is
    /// kept for completeness, but in Zod and in the OpenZ sim a Pyro never flames a building or map item: they are explosive-only
    /// targets, rpyro.cpp:149 / ZObject.CanAttackObject, so in play robots, vehicles and cannons burn):
    /// every .08 s soft blurred licks spawn on the target's surface facing the camera and ride its bones / hull
    /// (OpenZPyroBurnSurface); a flickering light 8 x (.8 + .2 sin 9t); the paint chars +.055 per hit up to .48 (TeamPaint.Charred,
    /// only units / buildings that have a TeamPaint). Visual only: Zod's pyro deals no burn damage (rpyro.cpp, zobject.cpp).</item>
    /// <item><b>Things it touches</b>: decor props and static map objects the drawn stream passes through burn as visual sticky
    /// fire for the 12.5 s structure time (see TouchAlong).</item>
    /// <item><b>Burning ground</b>: where the stream lands on the ground (misses, the feet of burning robots, the base of hit
    /// vehicles and buildings) a patch of ground fire burns ~7 s (OpenZPyroGroundFire + OpenZ/PyroGround +
    /// OpenZ/PyroTongues), smokes every .5 s and leaves a scorch (ScorchDecals).</item>
    /// <item><b>Light</b>: a pool of real point lights (Forward+, no shadows, default rendering layer = the terrain / unit layer,
    /// like every FX light in the project) handed each frame to the brightest fires on screen by priority: jets 22, burning
    /// things 8, ground patches 5 (three.js units, / pi = URP, OpenZFxCatalog.ThreeToUrpLight); a patch without a real
    /// light shows its own soft glow on the ground instead.</item>
    /// <item><b>Heat shimmer</b> (OpenZ/HeatDistort) over the brightest jets and patches; <b>smoke</b> = the M12 dark smoke
    /// (OpenZFxPlayer m12_smoke, #817c73 .54).</item>
    /// <item><b>Sound</b> through AudioDirector, all 3D: wpn_pyro_flame_start when a jet starts, wpn_pyro_flame_loop as a real
    /// loop that follows the Pyro while it fires (nearest 3 jets), wpn_pyro_flame_stop when it ends, imp_flame_ground at the
    /// nearest burning patch and dst_fire_loop (crackle) at the nearest burning thing.</item>
    /// </list>
    /// A/B: <see cref="Enabled"/> false = the pre-v2 look (FxManager.Flame). Menu: OpenZ/FX/Pyro Fire v2.
    /// </summary>
    [DefaultExecutionOrder(97)]
    public sealed class OpenZPyroFire : MonoBehaviour
    {
        public static OpenZPyroFire I { get; private set; }
        /// <summary>A/B switch (false = FxManager.Flame + the CombatAudio one-shot, the look before this package).</summary>
        public static bool Enabled = true;
        public static bool AutoCreate = true;
        /// <summary>True while this system plays the pyro's sounds (AudioDirector skips its own random ground crackle then).</summary>
        public static bool OwnsSound => Enabled && I && I.ready && I.isActiveAndEnabled;

        public const float Range = 15f, EndRadius = (0.25f + 6.8f * 0.19f) * 1.15f;
        public const float BurnSeconds = 5f, StructureBurnSeconds = 12.5f, CharStep = 0.055f, CharMax = 0.48f;
        public const float JetHold = 0.4f, EmitRate = 300f, LegacySpeed = 28f, EmitCapPerFrame = 12f, Overshoot = 0.6f;
        public const float JetLightThree = 22f, BurnLightThree = 8f, PatchLightThree = 7f, LightRange = 10f;
        /// <summary>
        /// on the sand reads red-pink, through URP's ACES the same light reads cream-yellow. tools/fit_layers.py fitted the colour
        /// </summary>
        public static readonly Color FireLight = new Color32(0xff, 0x30, 0x00, 0xff);
        public const float LightMatch = 0.909f;
        public static readonly Color SmokeDark = new Color32(0x81, 0x7c, 0x73, 0xff);                  // M12 'dark' smoke (OpenZFxCatalog.M12Smoke.altColor)

        [Header("Budget")]
        [Range(256, 4096)] public int maxSprites = 2048;
        [Range(0, 16)] public int maxLights = 11;
        [Range(0, 6)] public int maxLoops = 3;
        [Range(0, 12)] public int maxShimmer = 10;
        [Header("Look")]
        [Range(0f, 3f)] public float lightGain = 1f;
        /// <summary>
        /// (x NightGrade.NightK). Measured on the round-2 HDR frames (light contribution is linear before the post, so lit = unlit +
        /// k x (lit - unlit)), each game through its own post, pool zone 7 x 3.5 m: at k 1 the port's pool by day was visible over
        /// </summary>
        [Range(0.5f, 3f)] public float dayLightBoost = 1.6f;
        [Range(0.5f, 3f)] public float nightLightBoost = 1.0f;
        /// <summary>
        /// range shrinks toward this share at full night.</summary>
        [Range(0.3f, 1f)] public float nightRangeScale = 0.75f;
        [Range(0f, 3f)] public float haloGain = 1f;
        /// <summary>HDR boost of the sticky licks on a burning vehicle, cannon or wreck (rigid hulls: the licks are spread over the
        /// hull's faces and hardly overlap) by day and at night (x NightGrade.NightK). At the legacy .25 a single lick adds about
        /// 1.1-1.5 in its dense part, under the day bloom threshold (1.63 linear): pale soft discs without a glow. Robots, small
        /// damage fires and touched props pile their licks on one spot and keep .25.</summary>
        [Range(0.25f, 3f)] public float hullLickBoostDay = 2.2f;
        [Range(0.25f, 3f)] public float hullLickBoostNight = 0.25f;
        /// <summary>
        /// drawn shot (hits +-.5 m, misses +-1.6 m and down to the ground; Zod's pyro misses 30 % of its rolls), so the end used to
        /// snap 4.6 times a second: a forked, twitching hose. Now it sweeps (time constant ~.1 s) and rides the target's root, so a
        /// walking or driving target is not lagged. Visual only: arrivals, ignition points and the sim are unchanged.</summary>
        [Range(2f, 60f)] public float aimGlide = 10f;
        [Range(0.5f, 1.5f)] public float tongueHeight = 1f;
        /// <summary>Gain on the hot core of the burning ground's flame tongues (OpenZ/PyroTongues _CoreGain; the single tongues on a
        /// burning wreck use the same material) by day and at night (x NightGrade.NightK). A tongue is one alpha-blended layer: at
        /// gain 1 its core reaches about 6 on screen in a spot a third of its width, where the stream piles additive sprites to 5-12
        /// over a band metres long, so by day (bloom threshold 1.63 linear) only the stream showed a glow. Night keeps 1.</summary>
        [Range(1f, 6f)] public float tongueCoreDay = 3.5f;
        [Range(1f, 6f)] public float tongueCoreNight = 1f;
        /// <summary>MOTION BLUR option: how far a tongue is drawn out sideways by its own sway (x the effects' shutter; 0 = not at all).</summary>
        [Range(0f, 8f)] public float tongueSmear = OzFxSmooth.SmearFire;
        [Tooltip("review: how fast a jet sprite burns out after it splashes on the target (x its normal ageing)")]
        [Range(1f, 4f)] public float splashAgeRate = OpenZPyroFlamePool.SplashAgeRate;
        [Tooltip("Share of the flame speed kept through the splash (higher = the fire washes further past the target, like the legacy plume)")]
        [Range(0.05f, 1f)] public float splashSpeedKeep = OpenZPyroFlamePool.SplashKeep;
        public bool stickyFire = true, groundFire = true, charring = true, smoke = true, shimmer = true;
        [Tooltip("Decor props and static map objects the stream passes through burn (visual only, 12.5 s)")]
        public bool touchFire = true;
        [Range(0f, 0.02f)] public float shimmerStrength = 0.0105f;
        /// <summary>
        /// Each shimmer is an OpenZ/HeatDistort mode-2 plume standing on the fire, plumeHeight x its width tall.</summary>
        [Range(1f, 3f)] public float plumeHeight = 1.8f;
        /// <summary>Proof: plumes drawn last frame / peak.</summary>
        public static int PlumesDrawn, PeakPlumes;
        // Sound levels (multipliers on the AudioDirector events, through CombatAudio's buses / leveller). Reference = a grunt bolt:
        // GainBolt .6 x .8 x Weapons .26 x trim(bolt2 -7.1 LUFS) = .075 -> -29.6 LUFS-equivalent. Flame loop (flame_loop1 -17.9,
        // -9 dB) x 3.2 = .236 -> -30.4: as present as a bolt, continuous (the old one-shot sat ~.1-.15 among the bolts: "missing
        // sound"); ignite (v3-napalm-ignite-a -9.2, -10 dB, trim .68) x 2 -> -30.2; stop (sd_flame_stop -15.2, -14 dB) x 2.4 -> -35;
        // burning things (sd_fire_crackle_loop -18.8, World .7, -9.5 dB) x 1 -> -33.3.
        [Header("Sound (volume multipliers on the AudioDirector events)")]
        public float loopVol = 3.2f;
        public float igniteVol = 2.0f, stopVol = 2.4f, groundVol = 0.7f, burnVol = 1.0f;

        // ---- diagnostics (PyroFireShow / README verify)
        public int Jets => jets.Count;
        public int Burning => burns.Count;
        public int Sprites => pool != null ? pool.Count : 0;
        public int Patches => ground != null ? ground.ActiveCount : 0;
        public int LightsOn { get; private set; }
        public int LoopsOn { get; private set; }
        public int Shots { get; private set; }
        public int LoopsStarted { get; private set; }
        public int Ignites { get; private set; }
        public int PeakSprites { get; private set; }
        public int SpritesDropped => pool != null ? pool.Dropped : 0;
        public string Status { get; private set; } = "not started";

        bool ready;
        Material flameMat, tongueMat, groundMat, shimmerMat;
        OpenZPyroFlamePool pool;
        OpenZPyroGroundFire ground;
        Camera cam; RtsCamera rts; float nextRtsSearch;
        Mesh quad;
        // created on first use: Unity 6 refuses a MaterialPropertyBlock built in a field initializer (it broke the whole component)
        MaterialPropertyBlock shimmerBlockF;
        MaterialPropertyBlock shimmerBlock => shimmerBlockF ??= new MaterialPropertyBlock();
        static readonly int AlphaId = Shader.PropertyToID("_Alpha"), TimeId = Shader.PropertyToID("_OZPyroTime");
        static readonly int CoreGainId = Shader.PropertyToID("_CoreGain"), TongueSmearId = Shader.PropertyToID("_Smear");
        uint rngState = 0x6A09E667u;
        float Rand() { rngState ^= rngState << 13; rngState ^= rngState >> 17; rngState ^= rngState << 5; return (rngState & 0xFFFFFF) / 16777216f; }

        // ------------------------------------------------------------------------------------------------ entry (CombatView)
        /// <summary>CombatView.DrawShot, case "pyro": one drawn Zod flame shot. False = not handled (disabled / assets missing):
        /// CombatView then draws the old FxManager.Flame.</summary>
        public static bool Shot(ZEvent e, ZObject a, ZObject t, UnitView av, Vector3 from, Vector3 to)
        {
            if (!Enabled || a == null || t == null) return false;
            if (!I)
            {
                if (!AutoCreate || !ZGame.I) return false;
                new GameObject("OpenZPyroFire").AddComponent<OpenZPyroFire>();
            }
            if (!I || !I.ready || !I.isActiveAndEnabled) return false;
            I.Cue(e, a, t, av, from, to);
            return true;
        }

        // ------------------------------------------------------------------------------------------------ lifecycle
        /// <summary>critic round 2: made at level load, not on the first pyro shot. The materials, the sprite mesh, the 16 lights and the
        /// touch registry are ready before the first flame, and every fire shader is drawn once (Prewarm), so the first flame neither
        /// hitches nor shows the editor's cyan placeholder while an async shader variant compiles.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!AutoCreate || !Enabled || I) return;
            if (!ZGame.I && !FindFirstObjectByType<ZGame>()) return;                                    // game levels only
            new GameObject("OpenZPyroFire").AddComponent<OpenZPyroFire>();
        }

        System.Collections.IEnumerator Start()
        {
            for (int i = 0; i < 12; i++) yield return null;                                             // after ZGame bound its static views
            if (ready && !touchBuilt) BuildTouchables();
        }

        int warmFrames = 4;
        /// <summary>One 2 mm sprite 3 m in front of the camera and the tongue / ground materials on a 1 mm quad for the first frames:
        /// every fire pass is drawn once with the scene's fog variant (the tongue shader culls a quad with fade 0 anyway).</summary>
        void Prewarm()
        {
            var ct = cam.transform; var p = ct.position + ct.forward * 3f;
            pool.Spawn(p, Vector3.zero, 0.05f, 0.002f, p.y - 5f, p.y - 5f);
            var m = Matrix4x4.TRS(p, ct.rotation, Vector3.one * 0.001f);
            if (tongueMat) Graphics.RenderMesh(new RenderParams(tongueMat) { layer = 0, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = new Bounds(p, Vector3.one) }, quad, 0, m);
            if (groundMat) Graphics.RenderMesh(new RenderParams(groundMat) { layer = 0, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = new Bounds(p, Vector3.one) }, quad, 0, m);
        }

        void Awake()
        {
            if (I && I != this) { Destroy(gameObject); return; }
            I = this;
            flameMat = Load("OpenZ/FX/Pyro/M_PyroFlame", "OpenZ/PyroFlame");
            tongueMat = Load("OpenZ/FX/Pyro/M_PyroTongues", "OpenZ/PyroTongues");
            if (tongueMat && tongueMat.HasProperty("_Books") && !tongueMat.GetTexture("_Books")) tongueMat.SetTexture("_Books", Resources.Load<Texture2D>("OpenZ/FX/Pyro/ground-flames"));   // the flame flipbooks of the ground tongues
            groundMat = Load("OpenZ/FX/Pyro/M_PyroGround", "OpenZ/PyroGround");
            var hd = Resources.Load<Material>("OpenZ/FX/M_HeatDistort");
            if (hd) { shimmerMat = new Material(hd) { name = "PyroShimmer (runtime)" }; shimmerMat.SetFloat("_Mode", 2f); shimmerMat.SetFloat("_Inner", 0.1f); shimmerMat.SetFloat("_NoiseScale", 8f); shimmerMat.SetFloat("_Speed", 3.0f); }
            if (!flameMat || !flameMat.GetTexture("_FlameTex"))
            {
                Status = "flame material / flipbook missing: Resources/OpenZ/FX/Pyro (see README_IMPORT.md); the old FxManager.Flame stays";
                Debug.LogWarning("[OpenZPyroFire] " + Status);
                enabled = false; return;
            }
            if (Broken(flameMat))
            {
                Status = "OpenZ/PyroFlame did not compile in this Unity (see the console); the old FxManager.Flame stays";
                Debug.LogError("[OpenZPyroFire] " + Status);
                enabled = false; return;
            }
            if (Broken(tongueMat)) { Debug.LogError("[OpenZPyroFire] OpenZ/PyroTongues did not compile: burning ground drawn without tongues"); Destroy(tongueMat); tongueMat = null; }
            if (Broken(groundMat)) { Debug.LogError("[OpenZPyroFire] OpenZ/PyroGround did not compile: burning ground drawn without its char surface"); Destroy(groundMat); groundMat = null; }
            pool = new OpenZPyroFlamePool(maxSprites);
            ground = new OpenZPyroGroundFire();
            quad = BuildQuad();
            BuildLights();
            ready = true; Status = "ok";
        }

        static bool Broken(Material m)
        {
            if (!m) return false;
            var s = m.shader;
            if (!s || !s.isSupported) return true;
#if UNITY_EDITOR
            if (UnityEditor.ShaderUtil.ShaderHasError(s)) return true;
#endif
            return false;
        }

        static Material Load(string res, string shader)
        {
            var m = Resources.Load<Material>(res);
            if (m) return new Material(m) { name = m.name + " (runtime)" };
            var s = Shader.Find(shader);
            return s ? new Material(s) { name = shader + " (runtime)" } : null;
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            var ad = AudioDirector.I;
            if (ad) { foreach (var j in jets.Values) if (j.loopOn) ad.StopLoop(j.loopKey, 0.2f); ad.StopLoop(GroundLoopKey, 0.3f); ad.StopLoop(BurnLoopKey, 0.3f); }
            pool?.Dispose(); ground?.Dispose();
            foreach (var m in new[] { flameMat, tongueMat, groundMat, shimmerMat }) if (m) Destroy(m);
            if (quad) Destroy(quad);
        }

        static Mesh BuildQuad()
        {
            var m = new Mesh { name = "OZ_PyroShimmerQuad" };
            m.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            m.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            // neither pass meets a missing vertex stream, the tongue is culled (fade 0) and the ground stays empty (dry mask 0);
            // the shimmer (OpenZ/HeatDistort) reads only POSITION / NORMAL / TEXCOORD0
            var z4 = new List<Vector4> { Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero };
            m.SetUVs(1, z4); m.SetUVs(2, z4); m.SetColors(new List<Color> { Color.clear, Color.clear, Color.clear, Color.clear });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0); m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------------------------------------ helpers
        static float G(float x, float z) => ZGame.I ? ZGame.I.GroundY(x, z) : 0f;
        static bool Wet(Vector3 p)
        {
            var fx = OpenZFxPlayer.I;
            if (fx) return fx.WetAt(p, p.y);
            return OpenZFxPlayer.IsWaterTile(p);
        }
        // C# 9 allocate two new delegates per active patch per frame (and per lick): steady GC churn in every fire.
        static readonly System.Func<float, float, float> GroundF = G;
        static readonly System.Func<Vector3, bool> WetF = Wet;
        // ---- a burning wreck's few ground-fire flames (UnitView.UpdateWreckFlames)
        /// <summary>True when single ground-fire tongues can be drawn (the system runs and OpenZ/PyroTongues compiled).</summary>
        public static bool TonguesReady => I && I.ready && I.isActiveAndEnabled && I.tongueMat && I.ground != null;
        /// <summary>The ground point under (x, z). False over open water: nothing burns there (a bridge deck is ground).</summary>
        public static bool DrySpot(float x, float z, out Vector3 p) { p = new Vector3(x, G(x, z), z); return !Wet(p); }
        /// <summary>One of the burning ground's upright flame tongues at `root` for this frame (OpenZPyroGroundFire.Loose). The owner
        /// calls it every frame while it burns, so nothing is left behind when the owner goes.</summary>
        public static void Tongue(Vector3 root, float seed, float size, float speed, float fade, float hs, float ss) { if (TonguesReady) I.ground.Loose(root, seed, size, speed, fade, hs, ss); }
        /// <summary>Proof: loose tongues drawn in the last frame.</summary>
        public static int LooseTongues => I && I.ground != null ? I.ground.LooseDrawn : 0;
        /// <summary>
        /// rest wait a frame (a patch grows 12 cm per refresh, so every refresh used to rebuild at once: frame spikes in big fights).</summary>
        public const int MaxGridRebuildsPerFrame = 2;
        static float GameSpeed => ZGame.I ? Mathf.Max(0.25f, ZGame.I.gameSpeed) : 1f;
        /// <summary>Zod's flame effect speed: 300 px/s = 37.5 m/s of game time (eflame.cpp:10), x the game speed.</summary>
        static float Speed => CombatView.ZodBulletMps * GameSpeed;

        bool OnScreen(Vector3 p, float margin)
        {
            if (!cam) return true;
            var v = cam.WorldToViewportPoint(p);
            return v.z > 0f && v.x > -margin && v.x < 1f + margin && v.y > -margin && v.y < 1f + margin;
        }
        Vector3 Focus => rts ? rts.focus : cam ? cam.transform.position : Vector3.zero;

        // ================================================================================================ jets
        sealed class Jet
        {
            public int id; public UnitView shooter; public ZObject target; public int targetId;
            public Transform follow; public Vector3 toLocal, toWorld; public bool hit;
            public float lastCue, endAt, acc, lastGround, phase;
            public bool loopOn, audible, fallbackAudio; public string loopKey;
            public Vector3 at, dir, end; public float length, sortKey;
            public Transform targetRoot, aimRootT; public Vector3 aimOff, aimRootPos; public bool aimSet, aimRootOk;
            public float arcH, arcAt = -1f;
        }
        readonly Dictionary<int, Jet> jets = new Dictionary<int, Jet>();
        readonly List<int> jetKeys = new List<int>();

        struct Pending { public float at; public int jetId; public ZObject target; public bool hit; public Vector3 point, from; public Team team; }
        readonly List<Pending> pending = new List<Pending>(64);

        void Cue(ZEvent e, ZObject a, ZObject t, UnitView av, Vector3 from, Vector3 to)
        {
            float now = Time.time; Shots++;
            if (!jets.TryGetValue(a.Id, out var j)) { j = new Jet { id = a.Id, loopKey = "pyro_jet:" + a.Id, phase = Rand() * 10f }; jets[a.Id] = j; j.endAt = -1f; }
            bool fresh = now >= j.endAt || now - j.lastCue > 0.6f;
            j.shooter = av; j.target = t; j.targetId = t.Id; j.hit = e.flag; j.lastCue = now; j.endAt = now + JetHold;
            var game = ZGame.I;
            // where the stream ends: on the body it hits (and moves with it), on the wall it hits, or on the ground for a miss
            j.follow = null;
            UnitView tv = null;
            if (game) game.Views.TryGetValue(t.Id, out tv);
            j.targetRoot = tv && t.Kind != ZKind.Building ? tv.transform : null;
            if (e.flag)
            {
                if (tv && t.Kind != ZKind.Building) { j.follow = tv.transform; j.toLocal = tv.transform.InverseTransformPoint(to); }
                j.toWorld = to;
            }
            else
            {   // a miss: the flame goes past and lands on the ground a little further on (downward fire licks along the ground)
                var h = to - from; h.y = 0f; var hn = h.sqrMagnitude > 0.01f ? h.normalized : Vector3.forward;
                // the ground: for a nearly flat stream that is 20-40 m past the target, so misses set the road, the far sand and the cliffs
                // alight 6-20 tiles behind the target. Zod's flame (eflame.cpp) goes to the target: the miss now lands along the aim line
                // between 1.5 m short of the target and 0.8 m past it, never beyond the stream's reach.
                var tp = tv ? tv.transform.position : game ? game.CenterWorld(t) : to; tp.y = 0f; var f0 = from; f0.y = 0f;
                float tgt = Vector3.Distance(f0, tp), along = Mathf.Clamp(h.magnitude + 0.8f, Mathf.Max(0.5f, tgt - 1.5f), Mathf.Min(Range, tgt + 0.8f));
                var g = from + hn * along; g.y = G(g.x, g.z) + 0.15f;
                j.toWorld = g;
            }
            if (fresh) { j.acc = 0f; j.at = from; j.aimSet = false; StartJetSound(j, from); }
            if (!(av && av.isActiveAndEnabled)) j.at = from;
            // the flame arrives after its Zod flight (distance / 37.5 m/s): then the target catches fire (EFlame -> EPyroFire)
            float travel = Vector3.Distance(from, j.toWorld) / Speed;
            if (t.Destroyed) BlastWave.NoteDrawn(a.Id, t, travel);                                          // H0 round 3: the killing flame's arrival (GP3 proof)
            var wv = WeaponsV2Runner.I; if (wv) wv.NoteMps("pyro flame", Speed);
            pending.Add(new Pending { at = now + travel, jetId = a.Id, target = t, hit = e.flag, point = j.toWorld, from = from, team = a.Owner });
            if (j.fallbackAudio || !AudioDirector.I) CombatAudio.I?.Play("flame", from, 0.9f, 0.9f, a.Id, "flame_loop1", "flame_loop2");   // no director: the old one-shot
        }

        Vector3 JetEnd(Jet j) => j.follow && j.follow.gameObject.activeInHierarchy ? j.follow.TransformPoint(j.toLocal) : j.toWorld;

        void StepJets(float dt, float now)
        {
            jetKeys.Clear(); foreach (var k in jets.Keys) jetKeys.Add(k);
            float fill = pool.Fill, rateK = fill > 0.85f ? 0.35f : fill > 0.7f ? 0.6f : 1f, speed = Speed;
            foreach (var id in jetKeys)
            {
                var j = jets[id];
                bool shooterOk = j.shooter && j.shooter.isActiveAndEnabled && !j.shooter.Dying;
                if (now >= j.endAt || !shooterOk) { EndJet(j); jets.Remove(id); continue; }
                j.at = j.shooter.MuzzlePos();                                                  // after WeaponHold (order 80)
                if (j.follow) j.toWorld = JetEnd(j);
                // kept relative to the target's root, so it never lags a moving target; a new target (or a root that vanished) keeps
                // the end where it was and sweeps from there, like a hose.
                bool rootOk = j.targetRoot && j.targetRoot.gameObject.activeInHierarchy;
                var root = rootOk ? j.targetRoot.position : Vector3.zero;
                var want = j.toWorld - root;
                if (!j.aimSet) { j.aimOff = want; j.aimSet = true; }
                else
                {
                    if (rootOk != j.aimRootOk || j.targetRoot != j.aimRootT) j.aimOff += j.aimRootPos - root;
                    j.aimOff = Vector3.Lerp(j.aimOff, want, 1f - Mathf.Exp(-dt * Mathf.Max(0.5f, aimGlide)));
                }
                j.aimRootOk = rootOk; j.aimRootT = j.targetRoot; j.aimRootPos = root;
                var d = root + j.aimOff - j.at; float dist = d.magnitude;
                if (dist < 0.2f) continue;
                j.dir = d / dist;
                j.length = Mathf.Min(dist + Overshoot, Range + 1.5f);                      // what is drawn: to the target (+ the splash)
                if (now - j.arcAt > 0.14f || j.arcAt < 0f) { j.arcAt = now; j.arcH = ShotClearance.ArcHeight(j.at, root + j.aimOff); }
                j.end = j.at + j.dir * j.length;
                float life = (Range - 0.5f) / speed;
                float splashAt = Mathf.Min(dist + 0.3f, Range + 1f);
                j.audible = OnScreen(j.at, 0.15f);
                bool near = OnScreen(j.at, 0.6f) || OnScreen(j.end, 0.6f);
                // step(): acc += 300 dt (max 12 a frame), sprites spread along this frame's travel
                j.acc = Mathf.Min(EmitCapPerFrame, j.acc + EmitRate * (speed / LegacySpeed) * rateK * dt);
                int emit = Mathf.FloorToInt(j.acc); j.acc -= emit;
                if (!near) emit = 0;
                if (emit > 0)
                {
                    float fa = G(j.at.x, j.at.z), fe = G(j.end.x, j.end.z);
                    var perp = new Vector3(j.dir.z, 0f, -j.dir.x); if (perp.sqrMagnitude < 1e-4f) perp = Vector3.right; else perp.Normalize();
                    float sideMax = (EndRadius - 0.7f) / life;
                    for (int i = 0; i < emit; i++)
                    {
                        var p = j.at + j.dir * (0.05f + (i / (float)Mathf.Max(1, emit)) * speed * Mathf.Min(dt, 1f / 30f));
                        float side = (Rand() - 0.5f) * 2f * sideMax;
                        var v = j.dir * speed + perp * side + Vector3.up * ((Rand() - 0.5f) * 1.1f);
                        pool.Spawn(p, v, life, 0.44f + Rand() * 0.14f, fa, fe, 0f, -1f, 0f, null, Rand(), Rand(), Rand(), splashAt, j.arcH);
                    }
                }
                // the jet lights its surroundings: light at .55 of the stream, 22 x (.82 + .1 sin 8.3t + .08 sin 23.7t)
                float fl = 0.82f + 0.1f * Mathf.Sin((now + j.phase) * 8.3f) + 0.08f * Mathf.Sin((now + j.phase) * 23.7f);
                float lAt = Mathf.Min((Range - 0.5f) * 0.55f, Mathf.Max(0.5f, dist - 0.4f)), lu = Mathf.Clamp01(lAt / Mathf.Max(0.1f, dist));
                AddLightCand(1_000_000 + id, j.at + j.dir * lAt + Vector3.up * (4f * j.arcH * lu * (1f - lu)), JetLightThree * fl, LightRange, 3f);
                if (shimmer && near) AddShimmer(j.at + j.dir * (j.length * 0.6f) + Vector3.up * (0.55f + 0.96f * j.arcH), 1.4f + j.length * 0.14f, 1f, Vector3.SqrMagnitude(j.at - Focus));   // plume base on top of the stream
            }
        }

        void EndJet(Jet j)
        {
            var ad = AudioDirector.I;
            if (ad && j.loopOn) ad.StopLoop(j.loopKey, 0.25f);
            if (ad && j.audible && !j.fallbackAudio) AudioDirector.Fire("wpn_pyro_flame_stop", j.at, j.id, stopVol);
            j.loopOn = false;
        }

        void StartJetSound(Jet j, Vector3 at)
        {
            if (!AudioDirector.I) return;
            if (OnScreen(at, 0.15f)) AudioDirector.Fire("wpn_pyro_flame_start", at, j.id, igniteVol);
        }

        // ================================================================================================ arrivals: sticky fire + ground
        void StepPending(float now)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i];
                if (p.at > now) continue;
                pending.RemoveAt(i);
                Arrive(p, now);
            }
        }

        void Arrive(Pending p, float now)
        {
            var game = ZGame.I; if (!game) return;
            jets.TryGetValue(p.jetId, out var j);
            TouchAlong(p.from, p.point, p.team, now);                                             // critic round 2: what the stream passes through
            var t = p.target;
            if (!p.hit || t == null)
            {   // a miss lands on the ground
                if (groundFire) IgniteGround(new Vector3(p.point.x, G(p.point.x, p.point.z), p.point.z), 1.3f + Rand() * 0.3f, now);
                return;
            }
            UnitView tv = null; GameObject go = null;
            if (game.Views.TryGetValue(t.Id, out var v) && v) { tv = v; go = v.gameObject; }
            else if (game.StaticViews.TryGetValue(t.Id, out var sgo) && sgo) go = sgo;
            bool structure = t.Kind == ZKind.Building || t.Kind == ZKind.MapItem || (tv == null && go != null);
            if (go && go.activeInHierarchy && stickyFire) IgniteThing(t, tv, go, structure, p.point, now);
            if (!groundFire || j == null) return;
            // the stream spills onto the ground around what it hits (feet of a robot, the base of a vehicle / building)
            float gap = t.Kind == ZKind.Robot ? 0.9f : structure ? 1.6f : 1.4f;
            if (now - j.lastGround < gap) return;
            j.lastGround = now;
            Vector3 gpos;
            if (t.Kind == ZKind.Robot && tv) { var q = Random.insideUnitCircle * 0.5f; gpos = tv.transform.position + new Vector3(q.x, 0f, q.y); }
            else
            {   // the ground in front of the hit point, toward the Pyro
                var toward = j.at - p.point; toward.y = 0f; toward = toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.zero;
                gpos = p.point + toward * (structure ? 1.2f : 0.8f);
            }
            gpos.y = G(gpos.x, gpos.z);
            IgniteGround(gpos, t.Kind == ZKind.Robot ? 1.3f : 1.5f, now);
        }

        void IgniteGround(Vector3 at, float radius, float now)
        {
            if (Wet(at))
            {
                if (smoke && OpenZFxPlayer.I) OpenZFxPlayer.I.EmitChannel("m12_smoke", at + Vector3.up * 0.3f, new Vector3(0.1f, 1.4f, 0.05f), 1.1f, 0.9f, new Color(0.78f, 0.78f, 0.76f), 0.35f);
                return;
            }
            var patch = ground.Ignite(at, radius, now, GroundF, WetF, out bool created);
            if (created) { ScorchDecals.Add(at, patch.r * 1.05f, ScorchKind.Contact); Ignites++; }
        }

        // ================================================================================================ burning things (sticky fire)
        sealed class Burn
        {
            public int key; public ZObject obj; public UnitView view; public GameObject go; public OpenZPyroBurnSurface surf; public TeamPaint paint;
            public float until, charAmt, nextSmoke, phase; public bool structure, touched, small; public int licks, simId = -1; public Vector3 at, local;
        }
        readonly Dictionary<int, Burn> burns = new Dictionary<int, Burn>();
        readonly List<int> burnKeys = new List<int>();
        float stickyAcc;

        /// <summary>
        /// keeps a quarter-health vehicle / building alight with this sticky fire (licks, light, smoke, shimmer, the nearest burn loop),
        /// re-lit every ~2 s while it stays that low. False when the pyro fire is off (the caller then keeps its particle fire).</summary>
        public static bool KeepBurning(ZObject t, UnitView tv, GameObject go, bool structure, Vector3 point)
        {
            if (!I || !I.isActiveAndEnabled || t == null || !go || !go.activeInHierarchy || !I.stickyFire) return false;
            I.IgniteThing(t, tv, go, structure, point, Time.time);
            return true;
        }
        /// <summary></summary>
        public static void StopBurning(int id) { if (I) I.burns.Remove(id); }
        /// <summary>
        /// a quarter-health LIVING unit / building gets ONE small fire at `point` (engine deck / roof spot): 1-2 small licks, a small dim
        /// light, thin dark smoke, its heat plume, NO charring. The full body fire stays the pyro's: a real pyro hit turns it into the
        /// full sticky fire, which ends after its 5 s / 12.5 s and then falls back to this small one. Re-lit by the caller (~2 s).</summary>
        public static bool KeepBurningSmall(ZObject t, UnitView tv, GameObject go, bool structure, Vector3 point)
        {
            if (!I || !I.isActiveAndEnabled || t == null || !go || !go.activeInHierarchy || !I.stickyFire) return false;
            float now = Time.time;
            if (I.burns.TryGetValue(t.Id, out var b) && b.go == go)
            {
                if (!b.small) return true;                                                          // the pyro's full fire burns it: keep that until it ends
                b.until = now + BurnSeconds; return true;
            }
            var surf = OpenZPyroBurnSurface.Build(go, structure);
            if (surf == null) return false;
            I.burns[t.Id] = new Burn { key = t.Id, obj = t, view = tv, go = go, surf = surf, structure = structure, small = true, phase = I.Rand() * 10f,
                                       licks = structure ? 2 : 1, local = go.transform.InverseTransformPoint(point), until = now + BurnSeconds };
            SmallFires++;
            return true;
        }
        public static int SmallFires;
        /// <summary>True while a real pyro (full) sticky fire burns this object.</summary>
        public static bool FullBurning(int id) => I && I.burns.TryGetValue(id, out var b) && !b.small;
        public static float SmallLight = 0.3f, SmallRange = 0.4f;

        void IgniteThing(ZObject t, UnitView tv, GameObject go, bool structure, Vector3 point, float now)
        {
            if (!burns.TryGetValue(t.Id, out var b) || !b.go || b.go != go)
            {
                var surf = OpenZPyroBurnSurface.Build(go, structure);
                if (surf == null) return;
                b = new Burn { key = t.Id, obj = t, view = tv, go = go, surf = surf, paint = go.GetComponent<TeamPaint>(), structure = structure, phase = Rand() * 10f,
                               licks = t.Kind == ZKind.Robot ? 2 : structure ? 2 : 3 };
                if (b.paint) b.charAmt = Mathf.Clamp01(b.paint.Charred * 0.78f);
                burns[t.Id] = b;
                Ignites++;
            }
            else if (b.small)
            {
                b.small = false; b.licks = t.Kind == ZKind.Robot ? 2 : structure ? 2 : 3; b.paint = go.GetComponent<TeamPaint>();
                if (b.paint) b.charAmt = Mathf.Clamp01(b.paint.Charred * 0.78f);
            }
            b.until = now + (structure ? StructureBurnSeconds : BurnSeconds);
            if (structure) b.surf.AddSpot(point, b.until);
            // igniteFlameTarget: burnAmount = min(.48, burnAmount + .055) -> the model's diffuse x (1 - amount)
            if (charring && b.paint && !(tv && tv.Dying))
            {
                b.charAmt = Mathf.Min(CharMax, b.charAmt + CharStep);
                float want = b.charAmt / 0.78f;                                                // TeamPaint: k = 1 - .78 x Charred
                if (want > b.paint.Charred + 0.01f) b.paint.Charred = want;
            }
        }

        void StepBurns(float dt, float now, Vector3 camPos)
        {
            // a shared accumulator decorrelates the licks without per-frame spawning
            stickyAcc += dt; bool emitSticky = stickyAcc >= 0.08f; if (emitSticky) stickyAcc %= 0.08f;
            burnKeys.Clear(); foreach (var k in burns.Keys) burnKeys.Add(k);
            float fill = pool.Fill;
            float hullBoost = Mathf.Lerp(hullLickBoostDay, hullLickBoostNight, Mathf.Clamp01(NightGrade.NightK));   // by day the licks on a hull pass the bloom threshold
            foreach (var key in burnKeys)
            {
                var b = burns[key];
                if (!b.go || !b.go.activeInHierarchy || now >= b.until) { burns.Remove(key); continue; }
                if (b.touched && b.simId >= 0) { var so = ZGame.I && ZGame.I.World != null ? ZGame.I.World.Get(b.simId) : null; if (so == null || so.Destroyed) { burns.Remove(key); continue; } }
                if (b.small)
                {
                    var sc = b.go.transform.TransformPoint(b.local); b.at = sc;
                    if (!OnScreen(sc, 0.08f)) continue;
                    float spread = b.structure ? 0.6f : 0.3f;
                    if (emitSticky && fill <= 0.9f)
                        for (int i = 0; i < b.licks; i++)
                        {
                            var p = sc + new Vector3((Rand() - 0.5f) * spread, 0f, (Rand() - 0.5f) * spread);
                            pool.Spawn(p, new Vector3((Rand() - 0.5f) * 0.12f, 0.5f, (Rand() - 0.5f) * 0.12f), 0.7f, b.structure ? 0.45f : 0.4f, G(p.x, p.z)   /* Play check: 0.32 read as a spark by day */, G(p.x, p.z), 0.25f, 0.5f, 1f, b.go.transform);
                        }
                    AddLightCand(2_000_000 + key, sc + Vector3.up * 0.3f, BurnLightThree * SmallLight * (0.8f + 0.2f * Mathf.Sin((now + b.phase) * 9f)), LightRange * SmallRange, 1.5f);
                    if (shimmer) AddShimmer(sc + Vector3.up * 0.2f, b.structure ? 2.2f : 1.8f, 1f,   /* Play check: 1.3 m hid in the hull at h 70 */ Vector3.SqrMagnitude(sc - Focus) * 1.2f);
                    if (smoke && now >= b.nextSmoke && OpenZFxPlayer.I)
                    {
                        b.nextSmoke = now + 0.45f * (0.8f + 0.4f * Rand());
                        OpenZFxPlayer.I.EmitChannel("m12_smoke", sc + Vector3.up * 0.45f, new Vector3(0.1f, 1.3f + Rand() * 0.3f, 0.05f), 1.4f, b.structure ? 1.3f : 0.9f, SmokeDark, 0.45f);
                    }
                    continue;
                }
                var c = b.surf.CenterWorld;                                                     // robots / vehicles: the body; buildings: the burning spot
                Vector3 spotW = default, spotN = default;
                bool atSpot = b.structure && b.surf.LatestSpot(now, out spotW, out spotN);
                if (atSpot) c = spotW + spotN * 0.6f;
                b.at = c;
                if (!OnScreen(c, 0.08f)) continue;
                int n = b.structure && !b.surf.Whole ? Mathf.Min(6, b.licks * Mathf.Max(1, b.surf.SpotCount(now))) : b.licks;
                if (fill > 0.9f) n = 1;
                if (emitSticky)
                    for (int i = 0; i < n; i++)
                    {
                        if (!b.surf.Sample(camPos, GroundF, now, out var p, out var nrm, out var anchor)) continue;
                        // spawn(p, n.x .16, .6 + max(0, n.y) .12, n.z .16, .85 s, .52, ground, boost .25, spin .5, soft 1, anchor)
                        pool.Spawn(p, new Vector3(nrm.x * 0.16f, 0.6f + Mathf.Max(0f, nrm.y) * 0.12f, nrm.z * 0.16f), 0.85f, 0.52f, G(p.x, p.z), G(p.x, p.z), b.structure || b.surf.Skinned ? 0.25f : hullBoost, 0.5f, 1f, anchor);
                    }
                // the burning actor's light: 8 x (.8 + .2 sin 9t)
                AddLightCand(2_000_000 + key, c + Vector3.up * 0.3f, BurnLightThree * (0.8f + 0.2f * Mathf.Sin((now + b.phase) * 9f)), LightRange, 2f);
                if (shimmer) { float bs = atSpot ? 1.6f : Mathf.Clamp(b.surf.Size, 0.8f, 2.8f); AddShimmer(c + Vector3.up * (atSpot ? 0.7f : bs * 0.45f), bs * 1.1f, Mathf.Clamp01((b.until - now) / 1.5f) * 0.9f, Vector3.SqrMagnitude(c - Focus) * 1.2f); }
                if (smoke && now >= b.nextSmoke && OpenZFxPlayer.I)
                {
                    b.nextSmoke = now + (b.structure ? 0.35f : 0.5f) * (0.8f + 0.4f * Rand());
                    float s = atSpot ? 1.6f : Mathf.Clamp(b.surf.Size, 0.6f, 2.5f);
                    var sp = c + Vector3.up * (atSpot ? 0.8f : s * 0.6f) + new Vector3((Rand() - 0.5f) * s * 0.6f, 0f, (Rand() - 0.5f) * s * 0.6f);
                    OpenZFxPlayer.I.EmitChannel("m12_smoke", sp, new Vector3(0.12f, 1.4f + Rand() * 0.4f, 0.06f), 1.5f, Mathf.Clamp(s * 0.9f, 0.7f, 2.2f), SmokeDark, 0.5f);
                }
            }
        }

        // ================================================================================================ things the stream touches
        /// <summary>
        /// flamer set everything in its cone alight that can burn: players, vehicles, crates and walls (fireFlamethrower:
        /// cone radius flameRadiusAt(along) .25 -> 1.77 m over 15 m; igniteFlameTarget OBSTACLE = 12.5 s; never the shooter's own
        /// walls). Zod's pyro hits only its target and the sim stays Zod's, so the rest burns as VISUAL sticky fire only: decor props
        /// (DEC_* under Environment: cacti, crates, barrels, sandbags, supply / solar / service / settlement sets) and static map
        /// objects (the other sides' buildings, huts / base prop sets, grenade and rocket crates: ZGame.StaticViews) that the DRAWN
        /// crackle. No damage, no char. Never units (the sim did not hit them), rocks, cliffs, bridges, flags, lamps or water.</summary>
        sealed class Touchable { public int key, simId; public GameObject go; public Bounds b; public OpenZPyroBurnSurface surf; }   // surf: built once (triangle cache)
        readonly List<Touchable> touchables = new List<Touchable>(256);
        bool touchBuilt; float touchRetryAt;
        public const int DecorKeyBase = 600_000, MaxTouched = 12;
        /// <summary>How far past the target the drawn splash washes: splashSpeedKeep x what is left of the stream's 14.5 m
        /// (OpenZPyroFlamePool splash), at least MinWash.</summary>
        public const float MinWash = 1.0f;
        float Wash(float len) => Mathf.Clamp((Range - 0.5f - len) * splashSpeedKeep, MinWash, Range);
        public int TouchIgnites { get; private set; }
        public int TouchRegistered => touchables.Count;
        static readonly string[] NotFlammable = { "rock", "cliff", "stone", "boulder", "canyon", "island", "pocket", "interior", "mesa", "pillar",
            "ridge", "dune", "distant", "water", "wat_", "sea", "river", "road", "bridge", "brg_", "rubble", "crater", "flag", "lamp", "bollard", "light", "ground", "terrain", "skirt" };
        static bool Flammable(string name)
        {
            var n = name.ToLowerInvariant();
            foreach (var k in NotFlammable) if (n.Contains(k)) return false;
            return true;
        }

        static bool WorldBox(GameObject go, out Bounds b)
        {
            b = default; bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(false))
            {
                if (!r || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || r is SpriteRenderer || r is BillboardRenderer) continue;
                if (OpenZPyroBurnSurface.NotBody(r)) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any && b.size.y > 0.15f;
        }

        static bool HasDecAncestor(Transform t, Transform stop) { for (; t && t != stop; t = t.parent) if (t.name.StartsWith("DEC_")) return true; return false; }

        /// <summary>The things that can catch fire: built once a level (12 frames after load, or at the first flame).</summary>
        void BuildTouchables()
        {
            touchBuilt = true; touchables.Clear();
            var game = ZGame.I;
            if (!game || game.World == null) { touchBuilt = false; touchRetryAt = Time.time + 5f; return; }   // the level is not bound yet: try again later
            foreach (var kv in game.StaticViews)
            {
                var go = kv.Value; if (!go || !go.activeInHierarchy) continue;
                var o = game.World != null ? game.World.Get(kv.Key) : null;
                if (o == null || o.Destroyed || o.Type == "rock" || o.Type == "flag" || o.Type.Contains("bridge")) continue;
                if (!Flammable(go.name) || go.GetComponentInChildren<RockPillar>(true)) continue;
                if (WorldBox(go, out var b)) touchables.Add(new Touchable { key = kv.Key, simId = kv.Key, go = go, b = b });
            }
            var env = GameObject.Find("Environment");
            if (env)
            {
                int i = 0;
                foreach (var t in env.GetComponentsInChildren<Transform>(false))
                {
                    if (!t.name.StartsWith("DEC_") || !Flammable(t.name)) continue;
                    if (t.parent && t.parent != env.transform && HasDecAncestor(t.parent, env.transform)) continue;   // the top DEC_ of a set only
                    if (!WorldBox(t.gameObject, out var b)) continue;
                    if (b.size.x > 7f || b.size.z > 7f || b.size.y > 12f) continue;                              // ridges, islands, big decor (PropBreakables' rule)
                    touchables.Add(new Touchable { key = DecorKeyBase + i++, simId = -1, go = t.gameObject, b = b });
                }
            }
            if (game.StaticViews.Count == 0) { touchBuilt = false; touchRetryAt = Time.time + 5f; }          // static views not bound yet: rebuild later
            Debug.Log($"[OpenZPyroFire] things that can catch fire: {touchables.Count}");
        }

        /// <summary>Walks the drawn stream from the muzzle to the arrival point and on through the splash in .5 m steps; anything
        /// at its closest point.</summary>
        void TouchAlong(Vector3 from, Vector3 end, Team shooter, float now)
        {
            if (!stickyFire || !touchFire) return;
            if (!touchBuilt && Time.time >= touchRetryAt) BuildTouchables();
            if (touchables.Count == 0) return;
            var d = end - from; d.y = 0f; float len = d.magnitude; if (len < 0.3f) return;
            float wash = Wash(len); var dn = d / len; var tip = end + dn * wash; float total = len + wash;
            var seg = new Bounds(); seg.SetMinMax(Vector3.Min(from, tip) - new Vector3(EndRadius, 1.5f, EndRadius), Vector3.Max(from, tip) + new Vector3(EndRadius, 2.5f, EndRadius));
            var game = ZGame.I;
            foreach (var tc in touchables)
            {
                if (!tc.b.Intersects(seg) || !tc.go || !tc.go.activeInHierarchy) continue;
                if (tc.simId >= 0)
                {
                    var o = game && game.World != null ? game.World.Get(tc.simId) : null;
                    if (o == null || o.Destroyed || (o.Kind == ZKind.Building && o.Owner == shooter)) continue;
                }
                int n = Mathf.CeilToInt(total / 0.5f);
                for (int k = 1; k <= n; k++)
                {
                    float u = Mathf.Min(total, k * 0.5f);
                    var s = u <= len ? Vector3.Lerp(from, end, u / len) : end + dn * (u - len);
                    if (u > len) s.y = end.y + (u - len) * 0.35f;                                                 // the splash rises
                    float rad = 0.25f + (EndRadius - 0.25f) * Mathf.Clamp01(u / Range);                           // flameRadiusAt(along)
                    var c = tc.b.ClosestPoint(s);
                    float dx = c.x - s.x, dz = c.z - s.z;
                    if (dx * dx + dz * dz > rad * rad) continue;
                    if (s.y < tc.b.min.y - 0.4f || s.y > tc.b.max.y + 0.9f) continue;                              // passes under / over it
                    IgniteTouched(tc, c, now);
                    break;
                }
            }
        }

        /// <summary>Stage Fire Show: the nearest registered thing that can catch fire (decor first) within maxD of p (xz).</summary>
        public bool NearestTouchable(Vector3 p, float maxD, out Vector3 at)
        {
            if (!touchBuilt) BuildTouchables();
            at = default; float best = maxD * maxD; bool found = false;
            foreach (var tc in touchables)
            {
                if (!tc.go || !tc.go.activeInHierarchy) continue;
                var c = tc.b.center; float d = (c.x - p.x) * (c.x - p.x) + (c.z - p.z) * (c.z - p.z);
                if (tc.simId >= 0) d *= 1.5f;                                                                    // prefer decor
                if (d < best) { best = d; at = c; found = true; }
            }
            return found;
        }

        void IgniteTouched(Touchable tc, Vector3 point, float now)
        {
            if (!burns.TryGetValue(tc.key, out var b) || !b.go || b.go != tc.go)
            {
                int live = 0; foreach (var x in burns.Values) if (x.touched) live++;
                if (live >= MaxTouched) return;                                                                   // capped: the oldest burn out first
                var surf = tc.surf ??= OpenZPyroBurnSurface.Build(tc.go, true, tc.simId < 0);                      // decor: real triangles; kept for the next time
                if (surf == null) return;
                b = new Burn { key = tc.key, go = tc.go, surf = surf, structure = true, touched = true, simId = tc.simId, phase = Rand() * 10f, licks = 2 };
                burns[tc.key] = b; Ignites++; TouchIgnites++;
            }
            float until = now + StructureBurnSeconds;
            b.until = Mathf.Max(b.until, until);
            b.surf.AddSpot(point, until);
        }

        // ================================================================================================ burning ground
        void StepGround(float dt, float now)
        {
            ground.Step(dt, now, tongueHeight);
            int rebuilds = 0;
            foreach (var p in ground.patches)
            {
                if (!p.active) continue;
                // patch's grid, and under the per-frame budget it was drawn for a frame or more at the OLD place
                if (OpenZPyroGroundFire.NeedsRebuild(p) && (p.surf == null || p.builtR < 0f || rebuilds < MaxGridRebuildsPerFrame))
                {   // grew / drifted: new tongue roots + ground grid (a brand-new patch always builds at once; the rest queue a frame)
                    ground.Resample(p, GroundF, WetF);
                    ground.BuildSurface(p, 2.3f, GroundF, WetF);
                    rebuilds++;
                }
                bool vis = OnScreen(p.c, 0.1f);
                if (smoke && vis && now - p.lastSmoke >= 0.5f && OpenZFxPlayer.I && p.Remaining(now) > 0.5f)
                {   // every .5 s, golden-angle ring 1.1 (x patch scale), .6 m up, 'dark' (.12, 1.6, .06) 1.6 s 1.1
                    p.lastSmoke = now;
                    float a = (now - p.start) * 2.399f, k = Mathf.Sqrt(p.r / OpenZPyroGroundFire.NapalmRadius);
                    var sp = p.c + new Vector3(Mathf.Cos(a) * 1.1f * k * 1.6f, 0f, Mathf.Sin(a) * 1.1f * k * 1.6f);
                    sp.y = G(sp.x, sp.z) + 0.6f;
                    OpenZFxPlayer.I.EmitChannel("m12_smoke", sp, new Vector3(0.12f, 1.6f, 0.06f), 1.6f, Mathf.Clamp(2.2f * k * 1.4f, 0.8f, 2.2f), SmokeDark, 0.54f);
                }
                p.halo = 0.6f;                                                                  // its own ground glow unless it gets a real light (StepLights)
                AddLightCand(3_000_000 + p.id, p.c + Vector3.up * 0.9f, PatchLightThree * p.fadeX * (0.85f + 0.15f * Mathf.Sin((now + p.seed) * 7.3f)), LightRange * 0.8f, 2.2f);
                if (shimmer && vis) AddShimmer(p.c + Vector3.up * (0.45f + p.r * 0.12f), 1.0f * p.r, p.fadeX * 0.85f, Vector3.SqrMagnitude(p.c - Focus) * 1.5f);
            }
        }

        // ================================================================================================ lights (priority pool)
        struct LightCand { public int key; public Vector3 pos; public float three, range, prio, score; }
        readonly List<LightCand> cands = new List<LightCand>(64);
        sealed class LightSlot { public Light l; public int key = -1; public float k; public bool wanted; public Vector3 pos; public float three, range; }
        readonly List<LightSlot> slots = new List<LightSlot>();

        void BuildLights()
        {
            for (int i = 0; i < 16; i++)
            {
                var go = new GameObject("PyroLight" + i); go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>(); l.type = LightType.Point; l.shadows = LightShadows.None; l.enabled = false;
                l.renderMode = LightRenderMode.ForcePixel; l.color = FireLight;
                slots.Add(new LightSlot { l = l });
            }
        }

        void AddLightCand(int key, Vector3 pos, float three, float range, float prio)
        {
            if (maxLights <= 0) return;
            float vis = OnScreen(pos, 0.1f) ? 1f : OnScreen(pos, 0.45f) ? 0.35f : 0f;
            if (vis <= 0f) return;
            float d = Vector3.Distance(pos, Focus);
            cands.Add(new LightCand { key = key, pos = pos, three = three, range = range, prio = prio, score = three * prio * vis / (1f + d / 35f) });
        }

        // IComparer instances: List.Sort(Comparison) wraps the delegate in a new comparer on every call under Mono (NightLightBudget)
        sealed class ScoreDesc : IComparer<LightCand> { public int Compare(LightCand a, LightCand b) => b.score.CompareTo(a.score); }
        static readonly ScoreDesc ScoreCmp = new ScoreDesc();

        void StepLights(float dt)
        {
            foreach (var s in slots) s.wanted = false;
            cands.Sort(ScoreCmp);
            int n = Mathf.Min(Mathf.Min(maxLights, slots.Count), cands.Count);
            for (int i = 0; i < n; i++)
            {
                var c = cands[i];
                LightSlot slot = null;
                foreach (var s in slots) if (s.key == c.key) { slot = s; break; }
                if (slot == null) foreach (var s in slots) if (s.key < 0 || (!s.wanted && s.k <= 0.001f)) { slot = s; slot.key = c.key; slot.k = 0f; break; }
                if (slot == null) continue;
                slot.wanted = true; slot.pos = c.pos; slot.three = c.three; slot.range = c.range;
                if (c.key >= 3_000_000) foreach (var p in ground.patches) if (p.id == c.key - 3_000_000) { p.halo = 0f; break; }   // a real light lights the ground now
            }
            int on = 0;
            float gain = OpenZFxCatalog.ThreeToUrpLight * OpenZFxCatalog.LightGain * lightGain * LightMatch
                         * Mathf.Lerp(dayLightBoost, nightLightBoost, Mathf.Clamp01(NightGrade.NightK));
            foreach (var s in slots)
            {
                s.k = Mathf.MoveTowards(s.k, s.wanted ? 1f : 0f, dt / (s.wanted ? 0.12f : 0.18f));
                if (s.k <= 0.001f && !s.wanted) { if (s.l.enabled) s.l.enabled = false; s.key = -1; continue; }
                s.l.transform.position = s.pos; s.l.range = s.range * Mathf.Lerp(1f, nightRangeScale, Mathf.Clamp01(NightGrade.NightK)); s.l.intensity = s.three * gain * s.k;
                if (!s.l.enabled) s.l.enabled = true;
                on++;
            }
            LightsOn = on;
            cands.Clear();
        }

        // ================================================================================================ heat shimmer
        struct Shim { public Vector3 pos; public float size, alpha, d2; }
        readonly List<Shim> shims = new List<Shim>(16);
        void AddShimmer(Vector3 pos, float size, float alpha, float d2) { if (shims.Count < 32) shims.Add(new Shim { pos = pos, size = size, alpha = alpha, d2 = d2 }); }
        sealed class NearFirst : IComparer<Shim> { public int Compare(Shim a, Shim b) => a.d2.CompareTo(b.d2); }
        static readonly NearFirst DistCmp = new NearFirst();

        void DrawShimmer()
        {
            if (!shimmerMat || !cam || shims.Count == 0 || maxShimmer <= 0) { shims.Clear(); PlumesDrawn = 0; return; }
            shims.Sort(DistCmp);
            shimmerMat.SetFloat("_Strength", shimmerStrength);
            int n = Mathf.Min(maxShimmer, shims.Count);
            var cp = cam.transform.position;
            for (int i = 0; i < n; i++)
            {
                var s = shims[i];
                shimmerBlock.SetFloat(AlphaId, Mathf.Clamp01(s.alpha));
                var rot = Quaternion.LookRotation(s.pos - cp, Vector3.up);
                float h = s.size * plumeHeight;
                var centre = s.pos + rot * Vector3.up * (h * 0.5f);
                var rp = new RenderParams(shimmerMat) { layer = 0, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, matProps = shimmerBlock };
                Graphics.RenderMesh(rp, quad, 0, Matrix4x4.TRS(centre, rot, new Vector3(s.size, h, 1f)));
            }
            PlumesDrawn = n; PeakPlumes = Mathf.Max(PeakPlumes, n);
            shims.Clear();
        }

        // ================================================================================================ sound (AudioDirector, 3D)
        const string GroundLoopKey = "pyro_ground", BurnLoopKey = "pyro_burn";
        readonly List<Jet> audibleJets = new List<Jet>(8);
        sealed class JetNearFirst : IComparer<Jet> { public int Compare(Jet a, Jet b) => a.sortKey.CompareTo(b.sortKey); }
        static readonly JetNearFirst ByListener = new JetNearFirst();
        Vector3 groundLoopAt, burnLoopAt; bool groundLoopOn, burnLoopOn;

        void StepAudio(float now)
        {
            var ad = AudioDirector.I; int loops = 0;
            if (!ad) { LoopsOn = 0; return; }
            audibleJets.Clear();
            var f = Focus;
            foreach (var j in jets.Values)
            {
                if (j.fallbackAudio) continue;
                if (j.audible) { audibleJets.Add(j); j.sortKey = Vector3.SqrMagnitude(j.at - f); }
                else if (j.loopOn) { ad.StopLoop(j.loopKey, 0.3f); j.loopOn = false; }
            }
            audibleJets.Sort(ByListener);
            int n = Mathf.Min(maxLoops, audibleJets.Count);
            float norm = Mathf.Pow(Mathf.Max(1, n), -0.35f);                                   // three loops: +1.7 dB over one, not +4.8
            for (int i = 0; i < audibleJets.Count; i++)
            {
                var j = audibleJets[i];
                if (i >= n) { if (j.loopOn) { ad.StopLoop(j.loopKey, 0.3f); j.loopOn = false; } continue; }
                if (j.loopOn && !ad.LoopActive(j.loopKey)) j.loopOn = false;
                if (!j.loopOn)
                {
                    int ok = ad.StartLoop(j.loopKey, "wpn_pyro_flame_loop", j.at, j.shooter ? j.shooter.transform : null, loopVol * norm, 1f);
                    if (ok > 0) { j.loopOn = true; LoopsStarted++; OzSoundLog.Add("PY", "wpn_pyro_flame_loop", "flame_loop", "PLAY loop " + j.loopKey, loopVol * norm, j.at); }
                    else { j.fallbackAudio = true; OzSoundLog.Add("PY", "wpn_pyro_flame_loop", null, "DROP loop (no clips: AudioDirector patch missing?) -> CombatAudio one-shots", 0f, j.at); }
                }
                else ad.SetLoop(j.loopKey, loopVol * norm, 1f);
                if (j.loopOn) loops++;
            }
            bool gFound = false; float gBest = float.MaxValue;
            foreach (var p in ground.patches)
            {
                if (!p.active || !OnScreen(p.c, 0.1f)) continue;
                float d = Vector3.SqrMagnitude(p.c - f); if (d < gBest) { gBest = d; groundLoopAt = p.c + Vector3.up * 0.4f; gFound = true; }
            }
            if (gFound)
            {
                if (!groundLoopOn || !ad.LoopActive(GroundLoopKey)) groundLoopOn = ad.StartLoop(GroundLoopKey, "imp_flame_ground", groundLoopAt, null, groundVol, 1f) > 0;
                else ad.SetLoop(GroundLoopKey, groundVol, 1f, groundLoopAt);
                if (groundLoopOn) loops++;
            }
            else if (groundLoopOn) { ad.StopLoop(GroundLoopKey, 0.8f); groundLoopOn = false; }
            // the nearest burning thing on screen: the fire crackle
            bool bFound = false; float bBest = float.MaxValue;
            foreach (var b in burns.Values)
            {
                if (!b.go || b.small) continue; var c = b.at; if (!OnScreen(c, 0.1f)) continue;   // small damage fires: OpenZDamageStates runs their crackle
                float d = Vector3.SqrMagnitude(c - f); if (d < bBest) { bBest = d; burnLoopAt = c; bFound = true; }
            }
            if (bFound)
            {
                if (!burnLoopOn || !ad.LoopActive(BurnLoopKey)) burnLoopOn = ad.StartLoop(BurnLoopKey, "dst_fire_loop", burnLoopAt, null, burnVol, 1f) > 0;
                else ad.SetLoop(BurnLoopKey, burnVol, 1f, burnLoopAt);
                if (burnLoopOn) loops++;
            }
            else if (burnLoopOn) { ad.StopLoop(BurnLoopKey, 0.8f); burnLoopOn = false; }
            LoopsOn = loops;
        }

        // ================================================================================================ frame
        void LateUpdate()
        {
            if (!ready) return;
            float dt = Time.deltaTime, now = Time.time;
            if (!cam) cam = Camera.main;
            if (warmFrames > 0 && cam) { warmFrames--; Prewarm(); }
            if (!rts && Time.unscaledTime >= nextRtsSearch) { rts = FindFirstObjectByType<RtsCamera>(); nextRtsSearch = Time.unscaledTime + 1f; }   // review: no full-scene search every frame when a scene has no RtsCamera
            var camPos = cam ? cam.transform.position : Vector3.up * 50f;
            StepPending(now);
            StepJets(dt, now);
            StepBurns(dt, now, camPos);
            StepGround(dt, now);
            pool.splashAgeRate = splashAgeRate; pool.splashKeep = splashSpeedKeep;
            pool.Step(dt);
            PeakSprites = Mathf.Max(PeakSprites, pool.Count);
            StepLights(dt);
            StepAudio(now);
            Shader.SetGlobalFloat(TimeId, ground.Time);
            if (tongueMat)
            {   // the tongues' hot core passes the day bloom threshold (night keeps the legacy value); the option's sideways stretch
                tongueMat.SetFloat(CoreGainId, Mathf.Lerp(tongueCoreDay, tongueCoreNight, Mathf.Clamp01(NightGrade.NightK)));
                tongueMat.SetFloat(TongueSmearId, tongueSmear);
            }
            ground.Draw(groundMat, tongueMat);
            pool.Draw(flameMat);
            DrawShimmer();
        }

        /// <summary>Stops everything at once (level reset / A-B switch).</summary>
        public void ClearAll()
        {
            var ad = AudioDirector.I;
            foreach (var j in jets.Values) if (ad && j.loopOn) ad.StopLoop(j.loopKey, 0.1f);
            if (ad) { ad.StopLoop(GroundLoopKey, 0.2f); ad.StopLoop(BurnLoopKey, 0.2f); }
            jets.Clear(); burns.Clear(); pending.Clear(); pool?.Clear(); ground?.Clear(); groundLoopOn = burnLoopOn = false;
            foreach (var s in slots) { s.l.enabled = false; s.key = -1; s.k = 0f; }
        }

        void OnDisable() { if (ready) ClearAll(); }

        /// <summary>One line for logs / the show report.</summary>
        public string Diagnostics() =>
            $"jets {Jets}, burning {Burning}, sprites {Sprites}/{(pool != null ? pool.Capacity : 0)} (peak {PeakSprites}, dropped {SpritesDropped}), ground patches {Patches}, lights {LightsOn}/{maxLights}, loops {LoopsOn}, shots {Shots}, ignites {Ignites} (touched things {TouchIgnites} of {touchables.Count} registered), loops started {LoopsStarted}";
    }
}
