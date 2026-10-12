using System;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// Pure view-side decor: no Zod object, no sim change, units never target them.
    ///   L02 desert: three eagles soar in slow thermal circles high over the cliffs and the open desert (glide, a few flaps now
    ///   and then, bank into the turn, a bird-shaped soft shadow on the ground) and now and then swoop down over the sand
    ///   (after a worm that is out, which dives in time); five sandworm holes on Z's streaked dune tiles / open sand: a rumble
    ///   and dust trickle, the worm bursts out of a sand mound, sways, looks around (at nearby units too), dives back, again
    ///   at random intervals; it never comes up next to a unit and dives when one gets close.
    ///   L17 car park: five seagulls circle over the harbour and canal, land on the quay edges, look around, stretch, call,
    ///   and fly off when a unit comes close, a shot passes or something explodes nearby.
    /// Destructible: an explosion (FxManager.Exploded) or a shot (sim Hitscan segment) that reaches a critter kills it: the bird
    /// tumbles down trailing feathers (thud and feathers on land, a splash on water), the worm bursts in goo, sand clods and
    /// dust. It stays gone for minutes, then a new one comes (birds fly in from the map edge).
    /// Zod parity for the behaviour (refs/zod_engine/src/abird.cpp, ahutanimal.cpp): birds wander in slow turns with height
    /// changes and call every 15 + rand 5 s with a 1-in-3 chance; hut animals come out, walk / look and go home.
    /// Deterministic: every critter has its own seeded System.Random, so the same map looks the same every run.
    /// Perf: 13 critters max, one manual PlayableGraph each (evaluated only when on screen), no per-frame allocations.
    /// Boots itself in every level scene (ZGame present), like FactoryViews.
    /// </summary>
    [DefaultExecutionOrder(72)]
    public class AmbientLife : MonoBehaviour
    {
        public static AmbientLife I { get; private set; }
        /// <summary>Set false (e.g. in ZGame.Awake) to add AmbientLife by hand instead of the auto boot.</summary>
        public static bool AutoBoot = true;
        /// <summary>Audio hook for the audio manager: (sound event id, position, volume 0-1). While anything is subscribed,
        /// AmbientLife plays none of its default CombatAudio sounds. Ids: the Snd* constants below.</summary>
        public static event Action<string, Vector3, float> AudioEvent;

        [Header("Critters")]
        public bool eagles = true, worms = true, gulls = true;
        [Tooltip("size multipliers on top of the FBX size (eagle 2.4 m and gull 1.4 m wingspan, worm 2.6 m out of the sand at 1)")]
        public float eagleScale = 1f, gullScale = 1f, wormScale = 0.85f;                        // worm 2.2 m: just under a 2.3 m robot
        [Tooltip("extra yaw for the worm model if its face does not point along the root's +Z after import")]
        public float wormYawOffset = 0f;
        [Header("Life")]
        [Tooltip("seconds until a killed critter comes back (random in range)")]
        public Vector2 eagleRespawn = new Vector2(180f, 300f), wormRespawn = new Vector2(150f, 240f), gullRespawn = new Vector2(120f, 200f);
        [Tooltip("seconds between an eagle's swoops (random in range)")] public Vector2 swoopEvery = new Vector2(35f, 80f);
        /// <summary>
        /// per wormEvery s of play (game time, random in range), the first after wormFirst s; never while a fight (a blast within
        /// wormFightR m of the hole or on screen in the last wormFightCalm s) is going on, except a rare wormFightChance roll.</summary>
        [Tooltip("seconds of play between two sandworm appearances map-wide (random in range) / until the first one")]
        public Vector2 wormEvery = new Vector2(480f, 600f), wormFirst = new Vector2(240f, 420f);
        public float wormFightR = 40f, wormFightCalm = 20f, wormFightChance = 0.1f;
        float nextWormAt = -1f, fightT = -99f; Vector3 fightPos; bool fightOnScreen;
        /// <summary>Proof: worm appearances this session and the game time of the last one.</summary>
        public static int WormAppearances, WormBursts; public static float LastWormAt = -1f, NextWormIn = -1f;
        public bool groundShadows = true, playSounds = true, verbose;
        [Tooltip("same behaviour every run")] public int seed = 1996;

        // ------------------------------------------------------------------ sound event ids (README_IMPORT.md)
        /// <summary>Distant eagle cry. Zod plays CROW_SND for its desert birds (abird.cpp), so the bible's crow event is used.</summary>
        public const string SndEagleCry = "amb_l02_crow";
        public const string SndGullCall = "amb_l17_gulls";                    // bible event (existing)
        public const string SndWormRumble = "amb_l02_worm_rumble";            // NEW: low rumble + trickle before the worm bursts out
        public const string SndWormEmerge = "amb_l02_worm_emerge";            // NEW: sand burst + worm roar
        public const string SndWormDive = "amb_l02_worm_dive";                // NEW: worm sucks back into the sand
        public const string SndWormSplat = "critter_worm_splat";              // NEW: worm killed (goo splat)
        public const string SndGullTakeoff = "critter_gull_takeoff";          // NEW: wing flutter when a gull takes off
        public const string SndBirdHit = "critter_bird_hit";                  // NEW: bird hit (feather poof + squawk)
        public const string SndBirdThud = "critter_bird_thud";                // NEW: dead bird lands on the ground
        public const string SndBirdSplash = "critter_bird_splash";            // NEW: dead bird falls into the water
        // default clips through CombatAudio (Resources/OpenZ/Sfx, copied from Materials/atom/critters/sfx) until the audio
        // manager subscribes to AudioEvent; a missing clip is simply silent
        static readonly string[] ClipEagle = { "critter_eagle_cry" }, ClipGull = { "critter_seagull" }, ClipRumble = { "critter_worm_rumble" },
            ClipWorm = { "critter_worm" }, ClipWormDive = { "critter_worm_dive" }, ClipSplat = { "critter_splat" };

        // OpenZ frame: tile (tx, ty) centre = (tx*2+1, -(ty*2+1)); y from ZGame.GroundY at run time.
        /// <summary>L02 worm holes: Z's streaked dune tiles (tileset 0-4, 20-24, 40-44) and open sand, >= 6 m from roads,
        /// water, cliffs and every Zod object, >= 6 m from the level dressing; W1 sits on the quiet east sand below the rock
        /// walls (>= 40 m from any building, 21 m from a flag), not next to the NE robot factory.</summary>
        static readonly Vector2[] L02WormSpots = { new Vector2(95f, -63f), new Vector2(115f, -103f), new Vector2(99f, -35f), new Vector2(49f, -73f), new Vector2(81f, -113f) };
        /// <summary>L02 eagle routes: thermal circles (x, z, radius) over the cliff masses and the open desert, visited in turn.</summary>
        static readonly Vector3[][] L02EagleRoutes =
        {
            new[] { new Vector3(116f, -36f, 18f), new Vector3(80f, -14f, 16f), new Vector3(134f, -72f, 15f) },
            new[] { new Vector3(16f, -82f, 16f), new Vector3(48f, -68f, 20f), new Vector3(28f, -124f, 18f) },
            new[] { new Vector3(106f, -166f, 18f), new Vector3(92f, -112f, 22f), new Vector3(18f, -154f, 16f) },
        };
        static readonly float[] L02EagleAlt = { 27f, 24f, 29f };
        /// <summary>L17 quay-edge perches: (x, z, facing x, facing z) facing the water; straight quay edges with open water
        /// in front (the 9 m landing approach point over water or flat quay, never over the south bluffs), >= 2 tiles from
        /// every Zod object, >= 1.6 m from the marina moorings.</summary>
        static readonly Vector4[] L17Perches =
        {
            new Vector4(11f, -161.55f, 0f, -1f), new Vector4(41f, -161.55f, 0f, -1f), new Vector4(143f, -161.55f, 0f, -1f), new Vector4(159f, -161.55f, 0f, -1f),
            new Vector4(57f, -172.45f, 0f, 1f), new Vector4(77f, -172.45f, 0f, 1f), new Vector4(49f, -170.45f, 0f, 1f), new Vector4(165f, -172.45f, 0f, 1f),
            new Vector4(42.45f, -187f, -1f, 0f), new Vector4(177.55f, -187f, 1f, 0f), new Vector4(57f, -239.55f, 0f, -1f), new Vector4(69.5f, -241.55f, 0f, -1f),
        };
        /// <summary>L17 gull flock circles (x, z, radius): west harbour, east harbour, the canal through the pier.</summary>
        static readonly Vector3[] L17GullCircles = { new Vector3(25f, -200f, 13f), new Vector3(196f, -200f, 13f), new Vector3(110f, -214f, 9f) };
        static readonly int[] L17GullHome = { 0, 0, 1, 1, 2 };
        static readonly bool[] L17GullStartPerched = { true, false, true, false, false };

        // ------------------------------------------------------------------ clip layout (build_critters.py, 24 fps, frame 1 = 0 s)
        const float Fps = 24f;
        const int FlapA = 1, FlapB = 15, TuckF = 2, LimpF = 6, StretchF = 1, GlideF = 18, PerchF = 20, BirdLast = 20;
        const int WRise = 4, WUp = 16, WIdleA = 18, WIdleB = 84, WDive = 86, WUnder = 96, WLast = 100;
        const float EmergeLen = (WUp - WRise) / Fps, IdleLen = (WIdleB - WIdleA) / Fps, DiveLen = (WLast - WDive) / Fps, DiveHidden = (WUnder - WDive) / Fps;
        const float WormTall = 2.6f;

        // ------------------------------------------------------------------ types
        class Rig
        {
            public Transform root, pivot, orient, model; public Renderer[] rends; public bool shown = true; int cast = -1;
            public PlayableGraph graph; public AnimationClipPlayable pa, pb; public AnimationMixerPlayable mix; public bool graphOk;
            public Animation legacy; public AnimationState la, lb;
            public bool hasClip; public float len, shift, loopLen, lift, scale = 1f;
            public float T(int frame) => Mathf.Clamp((frame - 1 + shift) / Fps, 0f, len);

            public void Pose(float ta, float tb, float wb)
            {
                if (graphOk)
                {
                    pa.SetTime(ta); pb.SetTime(tb);
                    mix.SetInputWeight(0, 1f - wb); mix.SetInputWeight(1, wb);
                    graph.Evaluate(0f);
                }
                else if (legacy)
                {
                    la.time = ta; lb.time = tb; la.weight = 1f - wb; lb.weight = wb;
                    legacy.Sample();
                }
            }

            public bool Visible()
            {
                for (int i = 0; i < rends.Length; i++) if (rends[i] && rends[i].isVisible) return true;
                return false;
            }

            public void Show(bool on)
            {
                if (on == shown) return;
                shown = on;
                for (int i = 0; i < rends.Length; i++) if (rends[i]) rends[i].enabled = on;
            }

            public void Shadows(bool on)
            {
                int c = on ? 1 : 0; if (c == cast) return; cast = c;
                for (int i = 0; i < rends.Length; i++) if (rends[i]) rends[i].shadowCastingMode = on ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }

            public void Dispose() { if (graphOk && graph.IsValid()) graph.Destroy(); graphOk = false; }
        }

        enum BS { Soar, Swoop, Land, Perch, TakeOff, Fall, Down, Gone }

        class Bird
        {
            public bool eagle; public int id, home; public Rig rig; public System.Random rng; public BS st; public float stT;
            public Vector3 pos, vel; public float yaw, pitch, pitchExtra, roll, vy, speed, cruise, alt, clear, transitAlt;
            public Vector3[] route; public int wp; public Vector2 centre; public float radius, dir = 1f, hold;
            public float flapW, flapT, flapRate, burstT, burstLeft, w1, tuckW; public int pose0 = GlideF, hold1;
            public Vector3 h0, m0, h1, m1, h2, m2; public float t1, t2, pt; public bool flapFromTuck;
            public int perch = -1; public float perchLeft, landT, swoopT, headYaw, headAim, headT, stretchT, stretchLeft, callT, turnYaw, takeYaw, checkT;
            public Quaternion rot = Quaternion.identity; public Vector3 spinAxis; public float spinRate, popScale = 1f, respawn, featherT;
            public float span, hitR, groundY, groundT, avoidT, avoidYaw; public Transform head, wingL, wingR; public Quaternion wingLRest, wingRRest;
            public bool Alive => st != BS.Fall && st != BS.Down && st != BS.Gone;
            public bool Perched => st == BS.Perch || st == BS.Land;
        }

        enum WS { Under, Rumble, Emerge, Up, Dive, Dead }

        class Worm
        {
            public int id; public Rig rig; public System.Random rng; public WS st; public float t, timer, upLeft;
            public Vector3 pos; public float yaw, baseYaw, yawVel, yawAim, lookT, idleT, idleDir = 1f, dustT, respawn, fromT;
            public Transform body; public Quaternion bodyRest; public float height, radius;
            public bool Out => st == WS.Emerge || st == WS.Up || st == WS.Dive;
            public bool Vulnerable => (st == WS.Emerge && t > EmergeLen * 0.35f) || st == WS.Up || (st == WS.Dive && t < DiveHidden * 0.8f);
        }

        struct Bit
        {
            public Vector3 p, v, spin; public Quaternion r; public float age, life, size, gy, gyT; public byte kind, mat; public bool rest;
        }

        // ------------------------------------------------------------------ state
        ZGame game; Camera cam; DayNight dayNight; Light sunLight; FxManager fx;
        string level; float clock, waterY = float.NegativeInfinity, mapW, mapH; bool night, subscribed;
        readonly List<Bird> birds = new List<Bird>();
        readonly List<Worm> wormList = new List<Worm>();
        readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        Shader litShader; Material paletteMat, wormMat, shadowMat; Mesh shadowMesh; MaterialPropertyBlock shadowMpb;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        int walkMask;
        // perches
        Vector3[] perchPos; Vector3[] perchFace; bool[] perchOk; int[] perchTaken; float[] perchCool;
        // obstacles (buildings, rocks, huts) as world XZ rects + height, for swoop / landing path checks
        readonly List<Vector4> obstRect = new List<Vector4>(); readonly List<float> obstTop = new List<float>();
        // units (refreshed 5x per second)
        Vector3[] unitPos = new Vector3[256]; int unitCount; float unitsT;
        // particles: feathers and goo
        readonly Bit[] bits = new Bit[192]; int bitNext;
        const byte KFeather = 0, KGoo = 1;
        const byte MFeatherBrown = 0, MFeatherWhite = 1, MFeatherGrey = 2, MGoo = 3, MGooDark = 4, MatCount = 5;
        Material[] bitMats; Mesh bitMesh; Matrix4x4[][] bitBatch; readonly int[] bitN = new int[MatCount];

        // ------------------------------------------------------------------ boot
        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => TryAttach(); }
            TryAttach();
        }

        /// <summary>Level scenes only (ZGame present): add AmbientLife once.</summary>
        public static void TryAttach()
        {
            if (!AutoBoot || !ZGame.I || FindFirstObjectByType<AmbientLife>()) return;
            new GameObject("AmbientLife").AddComponent<AmbientLife>();
        }

        void Awake() { I = this; }

        void Start()
        {
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (!game || game.World == null) { enabled = false; return; }
            level = game.level; cam = Camera.main; dayNight = FindFirstObjectByType<DayNight>(); fx = FxManager.I;
            mapW = game.World.Map.width * 2f; mapH = game.World.Map.height * 2f;
            walkMask = ZGame.WalkLayer >= 0 ? 1 << ZGame.WalkLayer : 0;
            FindWater();
            BuildObstacles();
            BuildShared();
            if (level == "L02") { if (eagles) SpawnEagles(); if (worms) SpawnWorms(); }
            else if (level == "L17") { if (gulls) SpawnGulls(); }
            FxManager.Exploded += OnExplosion; subscribed = true;
            int e = 0, g = 0; foreach (var b in birds) if (b.eagle) e++; else g++;
            Debug.Log($"[OpenZ] AmbientLife {level}: {e} eagles, {wormList.Count} sandworm holes, {g} seagulls (water y {waterY:F2}, walk mask {walkMask})");
        }

        void OnDestroy()
        {
            if (subscribed) FxManager.Exploded -= OnExplosion;
            foreach (var b in birds) b.rig?.Dispose();
            foreach (var w in wormList) w.rig?.Dispose();
            foreach (var o in created) if (o) Destroy(o);
            if (I == this) I = null;
        }

        // ------------------------------------------------------------------ setup
        void FindWater()
        {
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.name.StartsWith("WAT_") && r.gameObject.activeInHierarchy) waterY = Mathf.Max(waterY, r.bounds.min.y + 0.02f);   // as ZoneMarkers
            if (float.IsNegativeInfinity(waterY)) waterY = level == "L17" ? -0.8f : -0.35f;                                           // level_terrain_data.py
        }

        void BuildObstacles()
        {
            foreach (var o in game.World.Objects)
            {
                float top;
                if (o.Kind == ZKind.Building && !o.IsBridge) top = 13f;
                else if (o.Kind == ZKind.MapItem && o.Type == "rock") top = 7f;
                else if (o.Kind == ZKind.MapItem && o.Type == "hut") top = 5f;
                else continue;
                float x0 = o.LocX / 8f, x1 = (o.LocX + o.WidthPix) / 8f, z1 = -o.LocY / 8f, z0 = -(o.LocY + o.HeightPix) / 8f;
                obstRect.Add(new Vector4(x0, z0, x1, z1)); obstTop.Add(top);
            }
        }

        void BuildShared()
        {
            litShader = Shader.Find("OpenZ/TeamLit");
            if (!litShader) litShader = Shader.Find("Universal Render Pipeline/Lit");
            var pal = Resources.Load<Texture2D>("OpenZ/Critters/critter_palette");
            var wtex = Resources.Load<Texture2D>("OpenZ/Critters/worm_tex");
            paletteMat = CritterMat(pal, true, "Critter_Palette");
            wormMat = CritterMat(wtex, false, "Critter_Worm");
            // bird-shaped soft ground shadow (OpenZ/Overlay: vertex colour x _Color, no depth write, pulled to the camera)
            var ov = Shader.Find("OpenZ/Overlay");
            if (ov) { shadowMat = new Material(ov) { name = "CritterShadow" }; created.Add(shadowMat); shadowMesh = BirdShadowMesh(); created.Add(shadowMesh); shadowMpb = new MaterialPropertyBlock(); }
            // feathers + goo: one small low-poly lozenge, instanced per colour
            bitMesh = Lozenge(); created.Add(bitMesh);
            bitMats = new Material[MatCount];
            bitMats[MFeatherBrown] = ColorMat(new Color(0.36f, 0.2f, 0.06f), "Feather_Brown");
            bitMats[MFeatherWhite] = ColorMat(new Color(0.96f, 0.95f, 0.92f), "Feather_White");
            bitMats[MFeatherGrey] = ColorMat(new Color(0.7f, 0.72f, 0.76f), "Feather_Grey");
            bitMats[MGoo] = ColorMat(new Color(0.98f, 0.7f, 0.16f), "Worm_Goo");
            bitMats[MGooDark] = ColorMat(new Color(0.55f, 0.24f, 0.08f), "Worm_Skin");
            bitBatch = new Matrix4x4[MatCount][];
            for (int i = 0; i < MatCount; i++) bitBatch[i] = new Matrix4x4[bits.Length];
        }

        Material CritterMat(Texture tex, bool flat, string name)
        {
            var m = new Material(litShader) { name = name };
            if (tex) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex); else m.mainTexture = tex; }
            Style(m, flat);
            created.Add(m);
            return m;
        }

        Material ColorMat(Color c, string name)
        {
            var m = new Material(litShader) { name = name };
            Style(m, true);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
            m.enableInstancing = true;
            created.Add(m);
            return m;
        }

        /// <summary>Clean stylized critter look (OpenZStyle unit look, no team paint, no grime).</summary>
        static void Style(Material m, bool flat)
        {
            F(m, "_TeamMode", 0f); F(m, "_Degreen", 0f); F(m, "_Stylize", 1f); F(m, "_Degrime", 0f); F(m, "_FlatShade", flat ? 1f : 0f);
            // The critters used the unit-readability path (soft fill
            // 0.75, ambient x1.35, auto night class = unit: night lift + rim) -> environment night class, no fill / ambient boost
            F(m, "_SoftFill", 0.2f); F(m, "_AmbientBoost", 1f); F(m, "_Smoothness", 0.18f); F(m, "_Metallic", 0f); F(m, "_RimPower", 3.4f);
            F(m, "_NightClass", 0f);
            C(m, "_KeyWarm", new Color(1.06f, 1f, 0.92f, 1f)); C(m, "_RimColor", new Color(0.5f, 0.46f, 0.38f, 1f));
            C(m, "_BaseColor", Color.white); C(m, "_EmissionColor", Color.black);
        }
        static void F(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }
        static void C(Material m, string p, Color v) { if (m.HasProperty(p)) m.SetColor(p, v); }

        static AnimationClip ClipOf(string res)
        {
            AnimationClip best = null;
            foreach (var c in Resources.LoadAll<AnimationClip>(res))
                if (c && !c.name.StartsWith("__preview__") && (best == null || c.length > best.length)) best = c;
            return best;
        }

        static Transform Find(Transform t, string n)
        {
            if (t.name == n) return t;
            for (int i = 0; i < t.childCount; i++) { var r = Find(t.GetChild(i), n); if (r) return r; }
            return null;
        }

        /// <summary>Instance a critter model under root / pivot: TeamLit material, bird / worm orientation normalised from its
        /// bones (whatever the FBX axis import did: bird head +Z, right wing +X, up +Y; worm spine +Y), manual PlayableGraph with
        /// two inputs of the one take (pose / flap), or the legacy Animation fallback.</summary>
        Rig MakeRig(string res, Material mat, float scale, bool bird, int lastFrame, string name)
        {
            var prefab = Resources.Load<GameObject>(res);
            if (!prefab) { Debug.LogWarning($"[OpenZ] AmbientLife: Resources/{res} missing (critter assets not imported)"); return null; }
            var root = new GameObject(name).transform; root.SetParent(transform, false);
            var pivot = new GameObject("Pivot").transform; pivot.SetParent(root, false);
            // the axis / body-centre correction lives on its own node: the FBX take keys the model root too (constant
            // object curves baked by the exporter), so a correction written on the model itself would be undone by
            // every graph.Evaluate
            var orient = new GameObject("Orient").transform; orient.SetParent(pivot, false);
            var go = Instantiate(prefab, orient, false); go.name = "Model";
            var rig = new Rig { root = root, pivot = pivot, orient = orient, model = go.transform };
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);          // decor: nothing to click or hit
            rig.rends = go.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rig.rends)
            {
                var n = Mathf.Max(1, r.sharedMaterials.Length); var arr = new Material[n];
                if (mat) { for (int i = 0; i < n; i++) arr[i] = mat; r.sharedMaterials = arr; }
                r.receiveShadows = true;
                if (r is SkinnedMeshRenderer sk) sk.updateWhenOffscreen = true;               // the worm rises 2.6 m out of its import bounds
            }
            Orient(rig, bird);
            pivot.localScale = Vector3.one * scale; rig.scale = scale; rig.lift *= scale;

            var clip = ClipOf(res);
            if (clip)
            {
                rig.len = clip.length;
                float expect = (lastFrame - 1) / Fps;
                rig.shift = Mathf.Max(0f, Mathf.Round((clip.length - expect) * Fps));         // the take starts a frame early on some imports
                rig.loopLen = (FlapB - FlapA) / Fps;
                if (clip.legacy)
                {
                    var an = go.GetComponent<Animation>(); if (!an) an = go.AddComponent<Animation>();
                    an.playAutomatically = false; an.cullingType = AnimationCullingType.AlwaysAnimate;
                    an.AddClip(clip, "A"); an.AddClip(clip, "B");
                    rig.legacy = an; rig.la = an["A"]; rig.lb = an["B"];
                    rig.la.enabled = rig.lb.enabled = true; rig.la.speed = rig.lb.speed = 0f; rig.la.weight = 1f; rig.lb.weight = 0f;
                    rig.hasClip = true;
                }
                else
                {
                    var an = go.GetComponent<Animator>(); if (!an) an = go.AddComponent<Animator>();
                    an.applyRootMotion = false; an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    rig.graph = PlayableGraph.Create("OpenZ." + name);
                    rig.graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var output = AnimationPlayableOutput.Create(rig.graph, "Critter", an);
                    rig.mix = AnimationMixerPlayable.Create(rig.graph, 2);
                    rig.pa = AnimationClipPlayable.Create(rig.graph, clip); rig.pb = AnimationClipPlayable.Create(rig.graph, clip);
                    rig.pa.SetApplyFootIK(false); rig.pb.SetApplyFootIK(false);
                    rig.pa.SetSpeed(0.0); rig.pb.SetSpeed(0.0);
                    rig.graph.Connect(rig.pa, 0, rig.mix, 0); rig.graph.Connect(rig.pb, 0, rig.mix, 1);
                    rig.mix.SetInputWeight(0, 1f); rig.mix.SetInputWeight(1, 0f);
                    output.SetSourcePlayable(rig.mix);
                    rig.graph.Play();
                    rig.graphOk = true; rig.hasClip = true;
                }
            }
            else Debug.LogWarning($"[OpenZ] AmbientLife: no animation clip in Resources/{res} (static pose, see README_IMPORT.md)");
            return rig;
        }

        void Orient(Rig rig, bool bird)
        {
            var p = rig.pivot; var m = rig.model; var o = rig.orient;                                // o: identity, between pivot and model
            if (bird)
            {
                Transform head = Find(m, "head"), tail = Find(m, "tail_2"), wl = Find(m, "wing_tip_L"), wr = Find(m, "wing_tip_R"), hips = Find(m, "hips");
                if (!head || !tail || !wl || !wr) { Debug.LogWarning("[OpenZ] AmbientLife: bird bones missing, orientation not checked"); return; }
                Vector3 f = p.InverseTransformPoint(head.position) - p.InverseTransformPoint(tail.position);
                Vector3 r = p.InverseTransformPoint(wr.position) - p.InverseTransformPoint(wl.position);
                if (f.sqrMagnitude < 1e-6f || r.sqrMagnitude < 1e-6f) return;
                // snap to the model's axes: import conversions are 90 / 180 degree turns, the bird's own small tilts stay
                var fs = Snap(f); var u = Vector3.Cross(f.normalized, r.normalized); u -= fs * Vector3.Dot(u, fs); var us = Snap(u);
                if (Mathf.Abs(Vector3.Dot(fs, us)) > 0.5f) return;
                o.localRotation = Quaternion.Inverse(Quaternion.LookRotation(fs, us));
                if (hips)
                {   // body centre on the pivot (banking / tumbling turn about the body); lift = hips above the feet
                    var hl = p.InverseTransformPoint(hips.position);
                    o.localPosition -= hl; rig.lift = Mathf.Max(0f, hl.y);
                }
            }
            else
            {
                var wr = Find(m, "WormRig");
                Transform b0 = wr ? Find(wr, "Bone") : null, b2 = wr ? Find(wr, "Bone.002") : null;
                if (!b0 || !b2) return;
                var up = p.InverseTransformPoint(b2.position) - p.InverseTransformPoint(b0.position);
                if (up.sqrMagnitude < 1e-6f) return;
                o.localRotation = Quaternion.FromToRotation(Snap(up), Vector3.up);                     // the spine leans ~7 deg by design
                // face: the spine leans toward the snout (Worm.fbx: 0.135 m over 1.16 m toward the mouth), so the lean,
                // snapped to 90 degree steps, gives the facing whatever the import axes; turn it onto the root's +Z
                // (wormYawOffset stays as a manual extra)
                var lean = p.InverseTransformPoint(b2.position) - p.InverseTransformPoint(b0.position); lean.y = 0f;
                if (lean.sqrMagnitude > 0.05f * 0.05f)
                {
                    var ls = Snap(lean);
                    o.localRotation = Quaternion.Euler(0f, -Mathf.Atan2(ls.x, ls.z) * Mathf.Rad2Deg, 0f) * o.localRotation;
                }
            }
        }

        /// <summary>Nearest signed model axis (+-X, +-Y, +-Z).</summary>
        static Vector3 Snap(Vector3 v)
        {
            float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
            if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
            if (ay >= az) return new Vector3(0f, Mathf.Sign(v.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        static float R(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
        static Vector3 RandUnit(System.Random r)
        {
            float z = R(r, -1f, 1f), a = R(r, 0f, Mathf.PI * 2f), s = Mathf.Sqrt(1f - z * z);
            return new Vector3(Mathf.Cos(a) * s, z, Mathf.Sin(a) * s);
        }

        Bird NewBird(bool eagle, int i, string res, Material mat, float scale)
        {
            var rig = MakeRig(res, mat, scale, true, BirdLast, (eagle ? "Eagle" : "Seagull") + i);
            if (rig == null) return null;
            var b = new Bird { eagle = eagle, id = i, rig = rig, rng = new System.Random(seed * 31 + i * 7919 + (eagle ? 2 : 17)) };
            b.head = Find(rig.model, "head"); b.wingL = Find(rig.model, "wing_1_L"); b.wingR = Find(rig.model, "wing_1_R");
            if (b.wingL) b.wingLRest = b.wingL.localRotation;
            if (b.wingR) b.wingRRest = b.wingR.localRotation;
            rig.Shadows(false);                                                                  // flying: the soft bird shadow instead
            return b;
        }

        void SpawnEagles()
        {
            for (int i = 0; i < L02EagleRoutes.Length; i++)
            {
                var b = NewBird(true, i, "OpenZ/Critters/Eagle", paletteMat, eagleScale);
                if (b == null) return;
                b.route = L02EagleRoutes[i]; b.alt = L02EagleAlt[i]; b.clear = 9f; b.transitAlt = b.alt;
                b.span = 2.4f * eagleScale; b.hitR = 1.0f * eagleScale; b.flapRate = 1.3f;
                b.cruise = 7.2f + R(b.rng, -0.6f, 0.6f); b.speed = b.cruise; b.dir = i % 2 == 0 ? 1f : -1f;
                var w = b.route[0]; b.centre = new Vector2(w.x, w.y); b.radius = w.z; b.hold = R(b.rng, 25f, 60f);
                float a = R(b.rng, 0f, Mathf.PI * 2f);
                b.pos = new Vector3(b.centre.x + Mathf.Cos(a) * b.radius, b.alt, b.centre.y + Mathf.Sin(a) * b.radius);
                b.yaw = Mathf.Atan2(-Mathf.Sin(a) * b.dir, Mathf.Cos(a) * b.dir) * Mathf.Rad2Deg;
                b.st = BS.Soar; b.burstT = R(b.rng, 2f, 9f); b.callT = 3f + R(b.rng, 0f, 5f);    // Zod: first call 3-8 s after the reset
                b.swoopT = R(b.rng, swoopEvery.x, swoopEvery.y) * 0.6f + i * 9f;
                birds.Add(b);
            }
        }

        void SpawnWorms()
        {
            var map = game.World.Map;
            for (int i = 0; i < L02WormSpots.Length; i++)
            {
                var s = L02WormSpots[i];
                int tx = Mathf.FloorToInt(s.x / 2f), ty = Mathf.FloorToInt(-s.y / 2f);
                if (map.ClassAt(tx, ty) != ZClass.Ground) { Debug.LogWarning($"[OpenZ] AmbientLife: worm hole {i} ({tx},{ty}) is not ground, skipped"); continue; }
                var rig = MakeRig("OpenZ/Critters/Worm", wormMat, wormScale, false, WLast, "Sandworm" + i);
                if (rig == null) return;
                var w = new Worm { id = i, rig = rig, rng = new System.Random(seed * 131 + i * 104729 + 5) };
                w.pos = new Vector3(s.x, game.GroundY(s.x, s.y), s.y);
                w.yaw = R(w.rng, 0f, 360f); w.yawAim = w.baseYaw = w.yaw;
                w.body = Find(rig.model, "WormRig"); if (w.body) w.bodyRest = w.body.localRotation;
                w.height = WormTall * wormScale; w.radius = 0.55f * wormScale;
                w.st = WS.Under; w.timer = R(w.rng, 4f, 26f) + i * 3f;
                rig.root.SetPositionAndRotation(w.pos, Quaternion.Euler(0f, w.yaw + wormYawOffset, 0f));
                rig.Shadows(true); rig.Show(false);
                wormList.Add(w);
            }
        }

        void SpawnGulls()
        {
            int n = L17Perches.Length;
            perchPos = new Vector3[n]; perchFace = new Vector3[n]; perchOk = new bool[n]; perchTaken = new int[n]; perchCool = new float[n];
            for (int i = 0; i < n; i++)
            {
                var q = L17Perches[i]; var face = new Vector3(q.z, 0f, q.w); var pt = new Vector3(q.x, 0f, q.y);
                float y = game.GroundY(pt.x, pt.z);
                for (int k = 0; k < 8 && y < waterY + 0.25f; k++) { pt -= face * 0.25f; y = game.GroundY(pt.x, pt.z); }   // step back onto the quay
                perchPos[i] = new Vector3(pt.x, y, pt.z); perchFace[i] = face; perchOk[i] = y >= waterY + 0.25f; perchTaken[i] = -1;
                if (!perchOk[i]) Debug.LogWarning($"[OpenZ] AmbientLife: perch {i} at ({q.x},{q.y}) has no quay under it, skipped");
            }
            for (int i = 0; i < L17GullHome.Length; i++)
            {
                var b = NewBird(false, i, "OpenZ/Critters/Seagull", paletteMat, gullScale);
                if (b == null) return;
                b.home = L17GullHome[i]; var c = L17GullCircles[b.home];
                b.route = new[] { c, new Vector3(c.x + R(b.rng, -7f, 7f), c.y + R(b.rng, -6f, 6f), c.z * 0.85f), new Vector3(c.x + R(b.rng, -7f, 7f), c.y + R(b.rng, -6f, 6f), c.z * 1.1f) };
                b.alt = waterY + 10.5f + R(b.rng, -1.5f, 1.5f); b.clear = 6f; b.transitAlt = 15f;
                b.span = 1.4f * gullScale; b.hitR = 0.6f * gullScale; b.flapRate = 2.1f;
                b.cruise = 6.8f + R(b.rng, -0.5f, 0.5f); b.speed = b.cruise; b.dir = i % 2 == 0 ? -1f : 1f;
                b.centre = new Vector2(c.x, c.y); b.radius = c.z; b.hold = R(b.rng, 10f, 25f);
                b.callT = 3f + R(b.rng, 0f, 5f); b.burstT = R(b.rng, 1f, 4f); b.landT = R(b.rng, 12f, 40f);
                float a = R(b.rng, 0f, Mathf.PI * 2f);
                b.pos = new Vector3(c.x + Mathf.Cos(a) * c.z, b.alt, c.y + Mathf.Sin(a) * c.z);
                b.yaw = Mathf.Atan2(-Mathf.Sin(a) * b.dir, Mathf.Cos(a) * b.dir) * Mathf.Rad2Deg;
                b.st = BS.Soar;
                birds.Add(b);
                if (L17GullStartPerched[i])
                {
                    int p = PickPerch(b, false);
                    if (p >= 0)
                    {   // already sitting there when the level starts: no landing flaps, already facing the water
                        perchTaken[p] = b.id; b.perch = p; SettleOnPerch(b); b.perchLeft = R(b.rng, 5f, 30f);
                        b.yaw = b.turnYaw; b.flapW = 0f; b.w1 = 0f; b.hold1 = StretchF;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            if (game == null || game.World == null || (birds.Count == 0 && wormList.Count == 0)) return;
            float dt = game.paused ? 0f : Mathf.Min(Time.deltaTime, 0.05f) * Mathf.Clamp(game.gameSpeed, 0.25f, 2f);
            clock += dt;
            if (!cam) cam = Camera.main;
            if (!fx) fx = FxManager.I;
            night = dayNight && dayNight.timeOfDay > 0.46f && dayNight.timeOfDay < 0.86f;
            unitsT -= dt; if (unitsT <= 0f) { unitsT = 0.2f; RefreshUnits(); }
            ReadShots();                                                                        // before CombatView (LateUpdate) clears the events
            if (dt > 0f)
            {
                for (int i = 0; i < wormList.Count; i++) StepWorm(wormList[i], dt);
                for (int i = 0; i < birds.Count; i++) StepBird(birds[i], dt);
                if (perchCool != null) for (int i = 0; i < perchCool.Length; i++) if (perchCool[i] > 0f) perchCool[i] -= dt;
                StepBits(dt);
            }
            for (int i = 0; i < wormList.Count; i++) PoseWorm(wormList[i]);
            for (int i = 0; i < birds.Count; i++) PoseBird(birds[i]);
            DrawBits();
            if (groundShadows && shadowMat) for (int i = 0; i < birds.Count; i++) DrawBirdShadow(birds[i]);
        }

        void RefreshUnits()
        {
            unitCount = 0;
            foreach (var v in game.Views.Values)
            {
                if (!v || v.Dying || !v.gameObject.activeInHierarchy) continue;
                if (unitCount == unitPos.Length) Array.Resize(ref unitPos, unitPos.Length * 2);
                unitPos[unitCount++] = v.transform.position;
            }
        }

        float NearestUnitSq(Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < unitCount; i++) { float dx = unitPos[i].x - p.x, dz = unitPos[i].z - p.z, d = dx * dx + dz * dz; if (d < best) best = d; }
            return best;
        }

        int NearestUnit(Vector3 p, float within)
        {
            int best = -1; float bd = within * within;
            for (int i = 0; i < unitCount; i++) { float dx = unitPos[i].x - p.x, dz = unitPos[i].z - p.z, d = dx * dx + dz * dz; if (d < bd) { bd = d; best = i; } }
            return best;
        }

        /// <summary>Walkable surface or water surface (dead birds splash, shadows lie on the water).</summary>
        float Surface(float x, float z, out bool water)
        {
            float g = game.GroundY(x, z);
            water = false;
            if (g < waterY - 0.05f)
            {
                int tx = Mathf.FloorToInt(x / 2f), ty = Mathf.FloorToInt(-z / 2f); var map = game.World.Map;
                bool inside = tx >= 0 && ty >= 0 && tx < map.width && ty < map.height;
                var c = inside ? map.ClassAt(tx, ty) : ZClass.Water;                           // outside: the sea skirt
                if (c == ZClass.Water || c == ZClass.DeepWater) { water = true; return waterY; }
            }
            return g;
        }

        float Surface(float x, float z) => Surface(x, z, out _);

        bool InMap(Vector3 p, float margin) => p.x > margin && p.x < mapW - margin && p.z < -margin && p.z > -mapH + margin;

        /// <summary>Would a path point at p (height y) fly into a building, rock pillar or hut? (rects grown by `grow` m)</summary>
        bool Blocked(Vector3 p, float grow)
        {
            for (int i = 0; i < obstRect.Count; i++)
            {
                var r = obstRect[i];
                if (p.x > r.x - grow && p.x < r.z + grow && p.z > r.y - grow && p.z < r.w + grow && p.y < obstTop[i] + 1f) return true;
            }
            return false;
        }

        /// <summary>Samples a two-piece Hermite path (h0 -> h1 -> h2) against the ground and the obstacles.</summary>
        bool PathClear(Bird b, float groundClear, bool lastPieceToGround)
        {
            const int N = 24;
            for (int k = 1; k <= N; k++)
            {
                float u = k / (float)N;
                var p = Hermite(b.h0, b.m0 * b.t1, b.h1, b.m1 * b.t1, u);
                if (p.y < Surface(p.x, p.z) + groundClear || Blocked(p, 1.5f)) return false;
                if (lastPieceToGround && k == N) break;
                var q = Hermite(b.h1, b.m1 * b.t2, b.h2, b.m2 * b.t2, u);
                if (!(lastPieceToGround && u > 0.7f) && (q.y < Surface(q.x, q.z) + groundClear || Blocked(q, 1.5f))) return false;
            }
            return true;
        }

        static Vector3 Hermite(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * p0 + (t3 - 2f * t2 + t) * m0 + (-2f * t3 + 3f * t2) * p1 + (t3 - t2) * m1;
        }

        // ------------------------------------------------------------------ birds
        void StepBird(Bird b, float dt)
        {
            b.stT += dt;
            switch (b.st)
            {
                case BS.Soar: Soar(b, dt); break;
                case BS.Swoop: Swoop(b, dt); break;
                case BS.Land: Land(b, dt); break;
                case BS.Perch: Perch(b, dt); break;
                case BS.TakeOff: TakeOff(b, dt); break;
                case BS.Fall: Fall(b, dt); break;
                case BS.Down:
                    if (b.stT > 7f) { b.popScale = Mathf.MoveTowards(b.popScale, 0f, dt / 0.35f); if (b.popScale <= 0f) Gone(b); }   // pops out like the corpses
                    break;
                case BS.Gone: b.respawn -= dt; if (b.respawn <= 0f) Respawn(b); break;
            }
            // Zod abird.cpp: a call every 15 + rand(0-5) s, 1 in 3 of them heard; birds keep quiet at night
            if (b.Alive)
            {
                b.callT -= dt;
                if (b.callT <= 0f)
                {
                    b.callT = 15f + R(b.rng, 0f, 5f);
                    if (b.rng.Next(3) == 0 && !night) { Sound(b.eagle ? SndEagleCry : SndGullCall, b.pos, b.eagle ? 0.8f : 0.7f, 100 + b.id); if (b.st == BS.Perch) b.headAim = R(b.rng, -20f, 20f); }
                }
            }
        }

        void Wings(Bird b, float dt, bool climb)
        {
            b.burstT -= dt;
            if (b.burstT <= 0f)
            {
                b.burstLeft = b.eagle ? R(b.rng, 1.2f, 2.4f) : R(b.rng, 0.9f, 3f);
                b.burstT = b.burstLeft + (b.eagle ? R(b.rng, 5f, 14f) : R(b.rng, 1.5f, 6f));
            }
            b.burstLeft -= dt;
            float target = climb || b.burstLeft > 0f ? 1f : 0f;
            b.flapW = Mathf.MoveTowards(b.flapW, target, dt * 3f);
            b.flapT += dt * b.flapRate * (0.3f + 0.7f * b.flapW);
            b.w1 = Mathf.Max(b.flapW, 0.08f);                                                   // gliding: a little wing life
            b.hold1 = 0; b.pose0 = GlideF;
        }

        void Soar(Bird b, float dt)
        {
            // the thermal drifts to the route's next circle; the bird keeps circling while it moves
            var w = b.route[b.wp]; var aim = new Vector2(w.x, w.y);
            b.centre = Vector2.MoveTowards(b.centre, aim, 1.7f * dt);
            b.radius = Mathf.MoveTowards(b.radius, w.z, 0.8f * dt);
            if ((b.centre - aim).sqrMagnitude < 1f)
            {
                b.hold -= dt;
                if (b.hold <= 0f) { b.wp = (b.wp + 1) % b.route.Length; b.hold = b.eagle ? R(b.rng, 25f, 60f) : R(b.rng, 10f, 25f); }
            }
            float r = b.radius * (1f + 0.14f * Mathf.Sin(clock * 0.07f + b.id * 1.7f));
            var d = new Vector2(b.pos.x - b.centre.x, b.pos.z - b.centre.y); float dist = Mathf.Max(0.01f, d.magnitude); var rad = d / dist;
            var tan = b.dir * new Vector2(-rad.y, rad.x);
            var want = tan + rad * Mathf.Clamp((r - dist) / r * 1.6f, -1.2f, 1.2f);
            float alt = dist > r + 6f ? Mathf.Max(b.alt, b.transitAlt) : b.alt;                  // gulls cross the pier high
            Fly(b, dt, Mathf.Atan2(want.x, want.y) * Mathf.Rad2Deg, r, alt);
            if (b.eagle)
            {
                b.swoopT -= dt;
                if (b.swoopT <= 0f) { b.swoopT = R(b.rng, swoopEvery.x, swoopEvery.y); TryStartSwoop(b); }
            }
            else
            {
                b.landT -= dt;
                if (b.landT <= 0f && !TryStartLanding(b)) b.landT = R(b.rng, 4f, 9f);
            }
        }

        /// <summary>Fly forward along `wantYaw` (turn rate from speed / radius), bank into the turn, hold `alt` but never less
        /// than `clear` over the ground ahead (cliffs), flap when climbing or in a burst.</summary>
        void Fly(Bird b, float dt, float wantYaw, float turnRadius, float alt)
        {
            if (b.pos.y < b.groundY + b.clear - 3f)
            {   // terrain ahead is higher than we can clear: turn to the lower side while climbing hard
                b.avoidT -= dt;
                if (b.avoidT <= 0f)
                {
                    b.avoidT = 0.5f;
                    var l = b.pos + Quaternion.Euler(0f, b.yaw - 70f, 0f) * Vector3.forward * 16f;
                    var r = b.pos + Quaternion.Euler(0f, b.yaw + 70f, 0f) * Vector3.forward * 16f;
                    b.avoidYaw = b.yaw + (Surface(l.x, l.z) < Surface(r.x, r.z) ? -110f : 110f);
                }
                wantYaw = b.avoidYaw; turnRadius = 6f;
            }
            float maxRate = Mathf.Rad2Deg * b.speed / Mathf.Max(5f, turnRadius * 0.75f);
            float turn = Mathf.Clamp(Mathf.DeltaAngle(b.yaw, wantYaw), -maxRate * dt, maxRate * dt);
            b.yaw = Mathf.Repeat(b.yaw + turn, 360f);
            float rate = turn / Mathf.Max(dt, 1e-4f);
            b.roll = Mathf.Lerp(b.roll, Mathf.Clamp(-rate * 0.6f, -38f, 38f), 1f - Mathf.Exp(-3f * dt));
            var fwd = Quaternion.Euler(0f, b.yaw, 0f) * Vector3.forward;
            b.groundT -= dt;
            if (b.groundT <= 0f)
            {
                b.groundT = 0.25f;
                Vector3 a1 = b.pos + fwd * 8f, a2 = b.pos + fwd * 20f;                           // look ahead for cliffs / the map apron
                b.groundY = Mathf.Max(Surface(b.pos.x, b.pos.z), Mathf.Max(Surface(a1.x, a1.z), Surface(a2.x, a2.z)));
            }
            float floor = b.groundY + b.clear;
            float want = Mathf.Max(alt + 1.4f * Mathf.Sin(clock * 0.11f + b.id), floor);             // Zod: slow height changes
            float climbMax = b.pos.y < floor - 1f ? 4.5f : 2.2f;                                     // below the safe height: climb hard
            float vyWant = Mathf.Clamp((want - b.pos.y) * 0.45f, -1.3f, climbMax);
            b.vy = Mathf.MoveTowards(b.vy, vyWant, (climbMax > 3f ? 4f : 1.6f) * dt);
            b.speed = Mathf.MoveTowards(b.speed, b.cruise * (climbMax > 3f ? 0.55f : 1f - 0.12f * Mathf.Clamp01(b.vy / 2f)), 2.5f * dt);
            Wings(b, dt, b.vy > 0.7f);
            b.vel = fwd * b.speed + Vector3.up * b.vy;
            b.pos += b.vel * dt;
            b.pitch = Mathf.Lerp(b.pitch, -Mathf.Atan2(b.vy, b.speed) * Mathf.Rad2Deg, 1f - Mathf.Exp(-4f * dt));
            b.pitchExtra = Mathf.MoveTowards(b.pitchExtra, 0f, 40f * dt);
        }

        /// <summary>Follow the current Hermite piece; true while on the path. Sets velocity, yaw, bank and pitch from the path.</summary>
        bool FollowPath(Bird b, float dt, out float u, out int piece)
        {
            b.pt += dt; Vector3 p;
            if (b.pt < b.t1) { u = b.pt / b.t1; piece = 1; p = Hermite(b.h0, b.m0 * b.t1, b.h1, b.m1 * b.t1, u); }
            else if (b.pt < b.t1 + b.t2) { u = (b.pt - b.t1) / b.t2; piece = 2; p = Hermite(b.h1, b.m1 * b.t2, b.h2, b.m2 * b.t2, u); }
            else { u = 1f; piece = 3; return false; }
            b.vel = (p - b.pos) / Mathf.Max(dt, 1e-4f); b.pos = p;
            float h = new Vector2(b.vel.x, b.vel.z).magnitude;
            if (h > 0.3f)
            {
                float ny = Mathf.Atan2(b.vel.x, b.vel.z) * Mathf.Rad2Deg;
                float rate = Mathf.DeltaAngle(b.yaw, ny) / Mathf.Max(dt, 1e-4f);
                b.yaw = Mathf.MoveTowardsAngle(b.yaw, ny, 400f * dt);
                b.roll = Mathf.Lerp(b.roll, Mathf.Clamp(-rate * 0.6f, -40f, 40f), 1f - Mathf.Exp(-4f * dt));
            }
            b.pitch = Mathf.Lerp(b.pitch, Mathf.Clamp(-Mathf.Atan2(b.vel.y, Mathf.Max(h, 0.5f)) * Mathf.Rad2Deg, -45f, 45f), 1f - Mathf.Exp(-6f * dt));
            return true;
        }

        void TryStartSwoop(Bird b)
        {
            // prey first: a worm that is out within 70 m (it will dive in time), else open low desert under the circle
            Worm prey = null; float best = 70f * 70f;
            for (int i = 0; i < wormList.Count; i++)
            {
                var w = wormList[i]; if (w.st != WS.Up && w.st != WS.Emerge) continue;
                float dx = w.pos.x - b.pos.x, dz = w.pos.z - b.pos.z, dd = dx * dx + dz * dz;
                if (dd < best) { best = dd; prey = w; }
            }
            Vector3 g = default; bool ok = prey != null;
            if (ok) g = prey.pos;
            for (int k = 0; k < 6 && !ok; k++)
            {
                float a = R(b.rng, 0f, Mathf.PI * 2f), rr = R(b.rng, 0.2f, 0.9f) * b.radius;
                g = new Vector3(b.centre.x + Mathf.Cos(a) * rr, 0f, b.centre.y + Mathf.Sin(a) * rr);
                if (!InMap(g, 8f)) continue;
                g.y = Surface(g.x, g.z, out bool water);
                ok = !water && g.y < 2.5f && NearestUnitSq(g) > 36f && !Blocked(g + Vector3.up * 2f, 3f);   // desert floor, not a cliff top
            }
            if (!ok) return;
            var dirH = new Vector3(g.x - b.pos.x, 0f, g.z - b.pos.z); float dist = dirH.magnitude;
            if (dist < 12f) return;
            dirH /= dist;
            b.h0 = b.pos; b.m0 = b.vel;
            b.h1 = g + Vector3.up * 2.8f; b.m1 = dirH * 13f;
            b.t1 = Mathf.Clamp(Vector3.Distance(b.h0, b.h1) / 11f, 2.4f, 5.5f); b.t2 = 3.4f; b.pt = 0f;
            // climb out straight on, or veer left / right of a cliff, rock or building ahead
            bool clear = false;
            for (int k = 0; k < 3 && !clear; k++)
            {
                var d = Quaternion.Euler(0f, k == 0 ? 0f : k == 1 ? -40f : 40f, 0f) * dirH;
                b.h2 = b.h1 + d * 30f + Vector3.up * Mathf.Max(6f, (b.alt - b.h1.y) * 0.5f); b.m2 = d * 8f + Vector3.up * 3.5f;
                clear = PathClear(b, 2f, false);
                for (int s = 1; s <= 4 && clear; s++)
                {   // and room to climb back to the soaring height after the path ends (no cliff straight ahead)
                    var q = b.h2 + d * (8f * s);
                    clear = Surface(q.x, q.z) + b.clear < b.h2.y + 2.2f * (8f * s) / b.cruise;
                }
            }
            if (!clear) { if (verbose) Debug.Log($"[OpenZ] AmbientLife: eagle {b.id} swoop blocked"); return; }
            b.st = BS.Swoop; b.stT = 0f; b.tuckW = 0f; b.flapFromTuck = false;
            if (verbose) Debug.Log($"[OpenZ] AmbientLife: eagle {b.id} swoops at {(prey != null ? "worm " + prey.id : "the sand")} ({g.x:F0}, {g.z:F0})");
        }

        void Swoop(Bird b, float dt)
        {
            if (!FollowPath(b, dt, out float u, out int piece)) { EndPath(b); return; }
            bool flap = piece == 1 ? u > 0.8f : u < 0.75f;
            if (flap && !b.flapFromTuck) { b.flapFromTuck = true; b.flapT = (TuckF - FlapA) / Fps / Mathf.Max(0.01f, b.rig.loopLen); }   // flap on from the tucked pose
            b.tuckW = Mathf.MoveTowards(b.tuckW, piece == 1 && u < 0.75f ? 0.55f : 0f, dt * 2f);
            b.flapW = Mathf.MoveTowards(b.flapW, flap ? 1f : 0f, dt * 3.5f);
            b.flapT += dt * b.flapRate * 1.35f * (0.3f + 0.7f * b.flapW);
            b.pose0 = GlideF;
            if (b.flapW >= b.tuckW) { b.hold1 = 0; b.w1 = Mathf.Max(b.flapW, 0.08f); } else { b.hold1 = TuckF; b.w1 = b.tuckW; }
        }

        void EndPath(Bird b)
        {
            b.st = BS.Soar; b.stT = 0f; b.vy = b.vel.y; b.speed = Mathf.Max(3f, new Vector2(b.vel.x, b.vel.z).magnitude);
        }

        int PickPerch(Bird b, bool checkPath)
        {
            int best = -1; float bestS = float.MaxValue; var home = L17GullCircles[b.home];
            for (int i = 0; i < perchPos.Length; i++)
            {
                if (!perchOk[i] || perchTaken[i] >= 0 || perchCool[i] > 0f) continue;
                var p = perchPos[i];
                float hx = p.x - home.x, hz = p.z - home.y; if (hx * hx + hz * hz > 75f * 75f) continue;
                if (NearestUnitSq(p) < 12f * 12f) continue;
                float bx = p.x - b.pos.x, bz = p.z - b.pos.z;
                float s = Mathf.Sqrt(bx * bx + bz * bz) + R(b.rng, 0f, 25f);
                if (s < bestS)
                {
                    if (checkPath && !LandingPath(b, i)) continue;
                    bestS = s; best = i;
                }
            }
            return best;
        }

        /// <summary>Landing path: from here to a point 9 m out over the water, 3.2 m up, then in to the quay edge.</summary>
        bool LandingPath(Bird b, int i)
        {
            var face = perchFace[i]; var P = perchPos[i] + Vector3.up * b.rig.lift;
            b.h0 = b.pos; b.m0 = b.vel;
            b.h1 = P + face * 9f + Vector3.up * 3.2f; b.m1 = -face * 6f + Vector3.down * 1.2f;
            b.h2 = P; b.m2 = -face * 1.5f + Vector3.down * 0.4f;
            b.t1 = Mathf.Clamp(Vector3.Distance(b.h0, b.h1) / 7f, 2f, 7f); b.t2 = 1.8f; b.pt = 0f;
            return PathClear(b, 0.8f, true);
        }

        bool TryStartLanding(Bird b)
        {
            int p = PickPerch(b, true);
            if (p < 0) return false;
            LandingPath(b, p);
            perchTaken[p] = b.id; b.perch = p;
            b.st = BS.Land; b.stT = 0f; b.checkT = 0f;
            return true;
        }

        void Land(Bird b, float dt)
        {
            b.checkT -= dt;
            if (b.checkT <= 0f)
            {
                b.checkT = 0.2f;
                int u = NearestUnit(perchPos[b.perch], 8f);
                if (u >= 0) { StartTakeOff(b, unitPos[u], true); return; }                       // someone is on the quay: abort
            }
            if (!FollowPath(b, dt, out float s, out int piece)) { SettleOnPerch(b); return; }
            bool flare = piece == 2 || (piece == 1 && s < 0.2f);
            b.flapW = Mathf.MoveTowards(b.flapW, flare ? 1f : 0f, dt * 4f);
            b.flapT += dt * b.flapRate * (piece == 2 ? 1.5f : 1f) * (0.3f + 0.7f * b.flapW);
            b.pitchExtra = piece == 2 ? -28f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 1f, s)) : 0f;   // flare: chest up
            b.pose0 = GlideF; b.hold1 = 0; b.w1 = Mathf.Max(b.flapW, 0.08f);
        }

        void SettleOnPerch(Bird b)
        {
            var face = perchFace[b.perch];
            b.pos = perchPos[b.perch] + Vector3.up * b.rig.lift; b.vel = Vector3.zero; b.vy = 0f; b.speed = 0f;
            b.st = BS.Perch; b.stT = 0f; b.flapW = 1f; b.w1 = 1f; b.hold1 = 0; b.pose0 = PerchF;   // flap weight 1 hides the glide -> perch switch
            b.turnYaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg + R(b.rng, -45f, 45f);       // hop round to look out over the water
            b.perchLeft = night ? R(b.rng, 60f, 140f) : R(b.rng, 10f, 35f);
            b.headT = R(b.rng, 0.8f, 2f); b.stretchT = R(b.rng, 6f, 18f); b.checkT = 0f;
            b.rig.Shadows(true);
        }

        void Perch(Bird b, float dt)
        {
            b.yaw = Mathf.MoveTowardsAngle(b.yaw, b.turnYaw, 260f * dt);
            b.flapT += dt * b.flapRate;
            b.stretchT -= dt;
            if (b.stretchT <= 0f) { b.stretchT = R(b.rng, 12f, 26f); b.stretchLeft = 1.3f; }
            b.stretchLeft -= dt;
            if (b.flapW > 0.01f) { b.flapW = Mathf.MoveTowards(b.flapW, 0f, dt * 3f); b.hold1 = 0; b.w1 = b.flapW; }    // landing flaps settle
            else { b.hold1 = StretchF; b.w1 = Mathf.MoveTowards(b.w1, b.stretchLeft > 0f ? 0.65f : 0f, dt * 2.5f); }   // wing stretch (wings up)
            b.pitch = Mathf.Lerp(b.pitch, 0f, 1f - Mathf.Exp(-8f * dt)); b.roll = Mathf.Lerp(b.roll, 0f, 1f - Mathf.Exp(-8f * dt));
            b.pitchExtra = Mathf.MoveTowards(b.pitchExtra, 0f, 90f * dt);                       // the landing flare settles
            b.headT -= dt;
            if (b.headT <= 0f) { b.headT = R(b.rng, 1.2f, 3.5f); b.headAim = b.rng.Next(4) == 0 ? 0f : R(b.rng, -55f, 55f); }
            b.headYaw = Mathf.MoveTowards(b.headYaw, b.headAim, 220f * dt);
            b.checkT -= dt;
            if (b.checkT <= 0f)
            {
                b.checkT = 0.2f;
                int u = NearestUnit(b.pos, 9f);
                if (u >= 0) { StartTakeOff(b, unitPos[u], true); return; }
            }
            b.perchLeft -= dt;
            if (b.perchLeft <= 0f) StartTakeOff(b, b.pos - perchFace[b.perch], false);
        }

        void StartTakeOff(Bird b, Vector3 from, bool scared)
        {
            var face = b.perch >= 0 ? perchFace[b.perch] : Quaternion.Euler(0f, b.yaw, 0f) * Vector3.forward;
            if (b.perch >= 0) { perchTaken[b.perch] = -1; if (scared) perchCool[b.perch] = 25f; b.perch = -1; }
            var away = b.pos - from; away.y = 0f;
            var dir = face + (away.sqrMagnitude > 0.01f ? away.normalized * 0.6f : Vector3.zero);    // out over the water, away from the threat
            b.takeYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            bool airborne = b.st != BS.Perch;                                                      // a landing that is broken off keeps its speed
            b.speed = airborne ? new Vector2(b.vel.x, b.vel.z).magnitude : 0f; b.vy = airborne ? b.vel.y : 0f;
            b.st = BS.TakeOff; b.stT = 0f; b.headYaw = b.headAim = 0f;
            b.landT = night ? R(b.rng, 3f, 8f) : R(b.rng, 15f, 40f);
            b.rig.Shadows(false);
            Sound(SndGullTakeoff, b.pos, 0.6f, 200 + b.id);
            if (scared && b.rng.Next(2) == 0 && !night) Sound(SndGullCall, b.pos, 0.75f, 100 + b.id);
        }

        void TakeOff(Bird b, float dt)
        {
            b.flapW = Mathf.MoveTowards(b.flapW, 1f, dt * 5f);
            if (b.flapW > 0.99f) b.pose0 = GlideF;
            b.hold1 = 0; b.w1 = b.flapW;
            b.flapT += dt * b.flapRate * 1.4f;
            b.yaw = Mathf.MoveTowardsAngle(b.yaw, b.takeYaw, 240f * dt);
            b.speed = Mathf.MoveTowards(b.speed, b.cruise, 5f * dt);
            b.vy = Mathf.MoveTowards(b.vy, 3.2f, 8f * dt);
            var fwd = Quaternion.Euler(0f, b.yaw, 0f) * Vector3.forward;
            b.vel = fwd * b.speed + Vector3.up * b.vy; b.pos += b.vel * dt;
            b.pitch = Mathf.Lerp(b.pitch, -22f, 1f - Mathf.Exp(-5f * dt)); b.roll = Mathf.Lerp(b.roll, 0f, 1f - Mathf.Exp(-5f * dt));
            if (b.pos.y > Surface(b.pos.x, b.pos.z) + 6.5f || b.stT > 4f) { b.st = BS.Soar; b.stT = 0f; b.groundT = 0f; }
        }

        void KillBird(Bird b, Vector3 from, bool blast)
        {
            if (!b.Alive) return;
            if (b.perch >= 0) { perchTaken[b.perch] = -1; b.perch = -1; }
            var push = b.pos - from; push = push.sqrMagnitude > 0.01f ? push.normalized : Vector3.up;
            b.vel = b.vel * 0.5f + push * (blast ? 5f : 2.5f) + Vector3.up * (blast ? 4f : 1.5f);
            b.rot = b.rig.root.rotation; b.spinAxis = RandUnit(b.rng); b.spinRate = R(b.rng, 280f, 560f);
            b.st = BS.Fall; b.stT = 0f; b.pose0 = GlideF; b.hold1 = LimpF; b.w1 = 0.7f; b.featherT = 0f; b.headYaw = 0f;
            Feathers(b, b.pos, b.eagle ? 16 : 12, 2.2f);
            Sound(SndBirdHit, b.pos, 0.9f, 300 + b.id);
            if (!b.eagle) Sound(SndGullCall, b.pos, 0.8f, 100 + b.id);
            b.rig.Shadows(true);
            if (verbose) Debug.Log($"[OpenZ] AmbientLife: {(b.eagle ? "eagle" : "gull")} {b.id} hit at ({b.pos.x:F0}, {b.pos.y:F0}, {b.pos.z:F0})");
        }

        void Fall(Bird b, float dt)
        {
            b.vel += Vector3.down * 9.81f * dt; b.vel *= 1f - 0.4f * dt;
            b.pos += b.vel * dt;
            b.rot = Quaternion.AngleAxis(b.spinRate * dt, b.spinAxis) * b.rot;
            b.featherT -= dt; if (b.featherT <= 0f) { b.featherT = 0.12f; Feathers(b, b.pos, 1, 0.4f); }
            float s = Surface(b.pos.x, b.pos.z, out bool water);
            if (b.pos.y > s + 0.25f) return;
            var at = new Vector3(b.pos.x, s, b.pos.z);
            if (water)
            {   // splash: light blue pop sparks + dust read as spray; the bird is gone
                if (fx) fx.Pop(at + Vector3.up * 0.1f, 1f);
                Feathers(b, at + Vector3.up * 0.2f, 5, 0.6f);
                Sound(SndBirdSplash, at, 0.8f, 400 + b.id);
                Gone(b);
                return;
            }
            if (fx) fx.Dust(at, 0.7f);
            Feathers(b, at + Vector3.up * 0.3f, 6, 1f);
            Sound(SndBirdThud, at, 0.7f, 400 + b.id);
            b.st = BS.Down; b.stT = 0f; b.popScale = 1f;
            b.pos = at + Vector3.up * 0.1f * b.span;                                               // lies on its side
            b.rot = Quaternion.Euler(0f, R(b.rng, 0f, 360f), R(b.rng, 70f, 100f));
            b.pose0 = PerchF; b.hold1 = LimpF; b.w1 = 0.45f;
        }

        void Gone(Bird b)
        {
            b.st = BS.Gone; b.stT = 0f; b.rig.Show(false);
            var range = b.eagle ? eagleRespawn : gullRespawn;
            b.respawn = R(b.rng, range.x, range.y);
        }

        /// <summary>A new bird flies in from outside the map (eagles: any edge, gulls: the sea to the south).</summary>
        void Respawn(Bird b)
        {
            Vector3 p;
            if (b.eagle)
            {
                switch (b.rng.Next(4))
                {
                    case 0: p = new Vector3(R(b.rng, 0f, mapW), 0f, 25f); break;
                    case 1: p = new Vector3(R(b.rng, 0f, mapW), 0f, -mapH - 25f); break;
                    case 2: p = new Vector3(-25f, 0f, R(b.rng, -mapH, 0f)); break;
                    default: p = new Vector3(mapW + 25f, 0f, R(b.rng, -mapH, 0f)); break;
                }
                p.y = b.alt + 4f;
            }
            else { var c = L17GullCircles[b.home]; p = new Vector3(c.x + R(b.rng, -25f, 25f), b.transitAlt, -mapH - 20f); }
            b.wp = 0; var w = b.route[0]; b.centre = new Vector2(w.x, w.y); b.radius = w.z;
            b.pos = p; b.yaw = Mathf.Atan2(b.centre.x - p.x, b.centre.y - p.z) * Mathf.Rad2Deg;
            b.speed = b.cruise; b.vy = 0f; b.roll = b.pitch = b.pitchExtra = 0f; b.flapW = 1f; b.popScale = 1f; b.groundT = 0f;
            b.pose0 = GlideF; b.hold1 = 0; b.w1 = 1f;
            b.rig.pivot.localScale = Vector3.one * b.rig.scale; b.rig.Shadows(false);
            b.st = BS.Soar; b.stT = 0f; b.landT = R(b.rng, 20f, 45f); b.swoopT = R(b.rng, swoopEvery.x, swoopEvery.y);
            if (verbose) Debug.Log($"[OpenZ] AmbientLife: a new {(b.eagle ? "eagle" : "gull")} ({b.id}) flies in");
        }

        void PoseBird(Bird b)
        {
            var rig = b.rig;
            if (b.st == BS.Gone) { rig.Show(false); return; }
            if (b.st == BS.Fall || b.st == BS.Down) rig.root.SetPositionAndRotation(b.pos, b.rot);
            else rig.root.SetPositionAndRotation(b.pos, Quaternion.Euler(b.pitch + b.pitchExtra, b.yaw, b.roll));
            // never fill the screen: a bird passing close to the lens (camera zoomed to 30-45 m, eagles soar at 24-29 m)
            // shrinks away smoothly between 6 and 4 wingspans (eagle 14.4 -> 9.6 m) instead of popping out at 7 m, where a
            // 2.4 m eagle already covered ~60 % of the screen height (FOV 32)
            float camK = 1f;
            if (cam) { float cd = Vector3.Distance(cam.transform.position, b.pos); camK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f * b.span, 6f * b.span, cd)); }
            bool wasHidden = !rig.shown;
            rig.Show(camK > 0.001f);
            if (!rig.shown) return;
            rig.pivot.localScale = Vector3.one * (rig.scale * camK * (b.st == BS.Down ? Mathf.Max(0.001f, b.popScale) : 1f));
            if (!rig.hasClip) { ProceduralWings(b); return; }
            if (!wasHidden && !rig.Visible()) return;                                           // off screen: logic only
            float ta = rig.T(b.pose0);
            float tb = b.hold1 > 0 ? rig.T(b.hold1) : rig.T(FlapA) + Mathf.Repeat(b.flapT, 1f) * rig.loopLen;
            rig.Pose(ta, tb, Mathf.Clamp01(b.w1));
            if (b.head && Mathf.Abs(b.headYaw) > 0.1f) b.head.rotation = Quaternion.AngleAxis(b.headYaw, rig.root.up) * b.head.rotation;
        }

        /// <summary>No clip imported: flap the wing roots about the body axis (world-space deltas, independent of bone axes).</summary>
        void ProceduralWings(Bird b)
        {
            if (!b.wingL || !b.wingR || b.st == BS.Perch || b.st == BS.Down) return;
            float a = 32f * b.w1 * Mathf.Sin(b.flapT * Mathf.PI * 2f);
            var f = b.rig.root.forward;
            b.wingL.localRotation = b.wingLRest; b.wingR.localRotation = b.wingRRest;             // nothing else resets the bones
            b.wingL.rotation = Quaternion.AngleAxis(a, f) * b.wingL.rotation;
            b.wingR.rotation = Quaternion.AngleAxis(-a, f) * b.wingR.rotation;
        }

        // ------------------------------------------------------------------ worms
        void StepWorm(Worm w, float dt)
        {
            w.t += dt;
            switch (w.st)
            {
                case WS.Under:
                    w.timer -= dt;
                    if (w.timer > 0f) break;
                    if (nextWormAt < 0f) nextWormAt = clock + R(w.rng, wormFirst.x, wormFirst.y);
                    if (clock < nextWormAt || AnyWormOut()) { w.timer = R(w.rng, 3f, 6f); break; }
                    if (clock - fightT < wormFightCalm && (fightOnScreen || new Vector2(fightPos.x - w.pos.x, fightPos.z - w.pos.z).sqrMagnitude < wormFightR * wormFightR) && w.rng.NextDouble() > wormFightChance)
                    { w.timer = R(w.rng, 8f, 16f); break; }                                                     // not during a fight (rarely yes)
                    if (NearestUnitSq(w.pos) > 9f * 9f && !EagleNear(w.pos, 25f))
                    {   // rumble first: a trickle of dust, the ground grumbles, then it bursts out
                        w.st = WS.Rumble; w.t = 0f; w.dustT = 0f;
                        Sound(SndWormRumble, w.pos, 0.55f, 500 + w.id);
                    }
                    else w.timer = R(w.rng, 3f, 6f);
                    break;
                case WS.Rumble:
                    w.dustT -= dt;
                    if (w.dustT <= 0f && fx) { w.dustT = 0.3f; fx.Dust(w.pos + new Vector3(R(w.rng, -0.6f, 0.6f), 0.1f, R(w.rng, -0.6f, 0.6f)), 0.22f); }
                    if (NearestUnitSq(w.pos) < 7f * 7f) { w.st = WS.Under; w.timer = R(w.rng, 6f, 12f); break; }
                    if (w.t >= 1.1f) Emerge(w);
                    break;
                case WS.Emerge:
                    if (w.t >= EmergeLen) { w.st = WS.Up; w.t = 0f; w.upLeft = R(w.rng, 4f, 9f); w.idleT = 0f; w.idleDir = 1f; w.lookT = 0.3f; }
                    break;
                case WS.Up:
                    w.upLeft -= dt;
                    w.idleT += dt * w.idleDir;                                                      // idle clip ping-pong: sway + jaw
                    if (w.idleT > IdleLen) { w.idleT = IdleLen; w.idleDir = -1f; } else if (w.idleT < 0f) { w.idleT = 0f; w.idleDir = 1f; }
                    w.lookT -= dt;
                    if (w.lookT <= 0f)
                    {   // look around; keep an eye on a unit that comes near
                        w.lookT = R(w.rng, 1.1f, 2.6f);
                        int u = NearestUnit(w.pos, 26f);
                        w.yawAim = u >= 0 ? Mathf.Atan2(unitPos[u].x - w.pos.x, unitPos[u].z - w.pos.z) * Mathf.Rad2Deg + R(w.rng, -15f, 15f)
                                          : w.yaw + R(w.rng, -110f, 110f);
                    }
                    w.yaw = Mathf.SmoothDampAngle(w.yaw, w.yawAim, ref w.yawVel, 0.45f, 240f, dt);
                    if (w.upLeft <= 0f || NearestUnitSq(w.pos) < 6.5f * 6.5f || EagleSwoopNear(w.pos, 16f)) Dive(w);
                    break;
                case WS.Dive:
                    if (w.t >= DiveLen) { w.st = WS.Under; w.t = 0f; w.timer = R(w.rng, 9f, 28f); w.rig.Show(false); }
                    break;
                case WS.Dead:
                    w.respawn -= dt;
                    if (w.respawn <= 0f) { w.st = WS.Under; w.t = 0f; w.timer = R(w.rng, 5f, 15f); }
                    break;
            }
        }

        bool AnyWormOut() { for (int i = 0; i < wormList.Count; i++) { var o = wormList[i].st; if (o != WS.Under && o != WS.Dead) return true; } return false; }

        void Emerge(Worm w)
        {
            w.st = WS.Emerge; w.t = 0f; w.yawVel = 0f;
            nextWormAt = clock + R(w.rng, wormEvery.x, wormEvery.y);
            WormAppearances++; LastWormAt = clock; NextWormIn = nextWormAt - clock;
            Debug.Log($"[OpenZ] AmbientLife: sandworm {w.id} up at {clock:F0} s of play (appearance {WormAppearances}), next one not before {NextWormIn:F0} s");
            if (fx)
            {
                fx.Dust(w.pos + Vector3.up * 0.2f, 1.1f);
                fx.DemolitionDust(w.pos, 2.2f * wormScale, 2.2f * wormScale, 0.35f);
                fx.Breakup(w.pos + Vector3.up * 0.3f, "stone", 1.2f * wormScale, 8, 0.45f);
            }
            ScorchDecals.AddSand(w.pos, 1.5f * wormScale, 75f, 0.42f);
            WormBursts++;
            Sound(SndWormEmerge, w.pos, 0.8f, 500 + w.id);
        }

        void Dive(Worm w)
        {
            if (w.st != WS.Up && w.st != WS.Emerge) return;
            w.fromT = w.st == WS.Up ? w.rig.T(WIdleA) + w.idleT : w.rig.T(WRise) + w.t;              // crossfade from this pose
            w.st = WS.Dive; w.t = 0f;
            if (fx)
            {
                fx.Dust(w.pos + Vector3.up * 0.15f, 0.9f);
                fx.DemolitionDust(w.pos, 1.6f * wormScale, 1.6f * wormScale, 0.3f);
            }
            ScorchDecals.AddSand(w.pos, 1.3f * wormScale, 60f, 0.36f);                                   // refreshes the patch if the emerge one is gone
            WormBursts++;
            Sound(SndWormDive, w.pos, 0.6f, 500 + w.id);
        }

        bool EagleNear(Vector3 p, float r)
        {
            for (int i = 0; i < birds.Count; i++) { var b = birds[i]; if (b.eagle && b.Alive && (b.pos - p).sqrMagnitude < r * r) return true; }
            return false;
        }

        bool EagleSwoopNear(Vector3 p, float r)
        {
            for (int i = 0; i < birds.Count; i++) { var b = birds[i]; if (b.eagle && b.st == BS.Swoop && (b.pos - p).sqrMagnitude < r * r) return true; }
            return false;
        }

        float Exposed(Worm w)
        {
            switch (w.st)
            {
                case WS.Emerge: return w.height * Mathf.Clamp01((w.t - 2f / Fps) / (EmergeLen * 0.8f));
                case WS.Up: return w.height;
                case WS.Dive: return w.height * (1f - Mathf.Clamp01(w.t / DiveHidden));
                default: return 0f;
            }
        }

        void PoseWorm(Worm w)
        {
            var rig = w.rig;
            bool visibleState = w.st == WS.Emerge || w.st == WS.Up || (w.st == WS.Dive && w.t < DiveHidden);
            if (!visibleState) { rig.Show(false); return; }
            // the sand mound (irregular crater ring) keeps its spawn yaw; looking around turns the worm body only, so the
            // mound never spins on the sand (whole-model turn only if the WormRig node is missing)
            rig.root.SetPositionAndRotation(w.pos, Quaternion.Euler(0f, (w.body ? w.baseYaw : w.yaw) + wormYawOffset, 0f));
            if (!rig.shown || rig.Visible())
            {
                if (rig.hasClip)
                {
                    float t = w.st == WS.Emerge ? rig.T(WRise) + w.t : w.st == WS.Up ? rig.T(WIdleA) + w.idleT : rig.T(WDive) + w.t;
                    if (w.st == WS.Dive && w.t < 0.15f) rig.Pose(t, w.fromT, 1f - w.t / 0.15f);     // blend out of the idle sway
                    else rig.Pose(t, t, 0f);
                }
                if (w.body)
                {   // body only: turn to the look direction + extra sway (the mound stays flat and still on the ground)
                    float k = w.st == WS.Up ? 1f : w.st == WS.Emerge ? Mathf.Clamp01(w.t / EmergeLen) : 1f - Mathf.Clamp01(w.t / 0.3f);
                    float sx = Mathf.Sin(clock * 5.6f + w.id) * 5f * k, sz = Mathf.Sin(clock * 3.9f + w.id * 2.1f) * 3.5f * k;
                    var up = rig.root.up;
                    w.body.localRotation = w.bodyRest;
                    w.body.rotation = Quaternion.AngleAxis(Mathf.DeltaAngle(w.baseYaw, w.yaw), up)
                                    * Quaternion.AngleAxis(sx, rig.root.right) * Quaternion.AngleAxis(sz, rig.root.forward) * w.body.rotation;
                }
            }
            rig.Show(true);
        }

        void KillWorm(Worm w, Vector3 from)
        {
            if (!w.Vulnerable) return;
            var c = w.pos + Vector3.up * Mathf.Min(1.3f, Exposed(w) * 0.55f);
            Goo(w, c, from, 22);
            if (fx)
            {
                fx.Breakup(w.pos + Vector3.up * 0.4f, "stone", 1.8f * wormScale, 12, 0.55f);
                fx.Dust(w.pos + Vector3.up * 0.2f, 1.3f);
                fx.DemolitionDust(w.pos, 2.6f * wormScale, 2.6f * wormScale, 0.4f);
            }
            Sound(SndWormSplat, c, 1f, 600 + w.id);
            w.st = WS.Dead; w.t = 0f; w.respawn = R(w.rng, wormRespawn.x, wormRespawn.y);
            w.rig.Show(false);
            if (verbose) Debug.Log($"[OpenZ] AmbientLife: sandworm {w.id} killed at ({w.pos.x:F0}, {w.pos.z:F0}), back in {w.respawn:F0} s");
        }

        // ------------------------------------------------------------------ damage (explosions, shots)
        void OnExplosion(Vector3 at, float strength)
        {
            if (!this || !enabled) return;
            fightT = clock; fightPos = at;
            if (cam) { var vp = cam.WorldToViewportPoint(at); fightOnScreen = vp.z > 0f && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.1f && vp.y < 1.1f; }
            float reach = 1.2f + 2.4f * strength;                                                  // PropBreakables reach
            for (int i = 0; i < birds.Count; i++)
            {
                var b = birds[i]; if (!b.Alive) continue;
                float r = reach + b.hitR, d2 = (b.pos - at).sqrMagnitude;
                if (d2 < r * r) KillBird(b, at, true);
                else if (!b.eagle && b.Perched && d2 < 28f * 28f) StartTakeOff(b, at, true);
            }
            for (int i = 0; i < wormList.Count; i++)
            {
                var w = wormList[i];
                float d = DistPointSegment(at, w.pos, w.pos + Vector3.up * Mathf.Max(0.3f, Exposed(w)));
                if (w.Vulnerable && d < reach + w.radius) KillWorm(w, at);
                else if (w.Out && d < 15f) Dive(w);
                else if (w.st == WS.Rumble && d < 12f) { w.st = WS.Under; w.timer = R(w.rng, 8f, 15f); }
            }
        }

        void ReadShots()
        {
            var ev = game.World.Events;
            for (int i = 0; i < ev.Count; i++)
            {
                var e = ev[i];
                if (e.type != ZEventType.Hitscan) continue;
                if (!ShotSegment(e, out var a, out var b)) continue;
                OnShot(a, b);
            }
        }

        bool ShotSegment(ZEvent e, out Vector3 from, out Vector3 to)
        {
            from = to = default;
            if (game.Views.TryGetValue(e.objId, out var av) && av) from = av.MuzzlePos();
            else { var a = game.World.GetAny(e.objId); if (a == null) return false; from = game.CenterWorld(a) + Vector3.up; }
            if (game.Views.TryGetValue(e.otherId, out var tv) && tv) to = tv.AimPos();
            else { var t = game.World.GetAny(e.otherId); if (t == null) return false; to = game.CenterWorld(t) + Vector3.up * 0.9f; }
            if (!e.flag) to = from + (to - from) * 1.3f;                                          // a miss flies on past the target
            return true;
        }

        void OnShot(Vector3 a, Vector3 b)
        {
            for (int i = 0; i < birds.Count; i++)
            {
                var bd = birds[i]; if (!bd.Alive) continue;
                float d = DistPointSegment(bd.pos, a, b);
                if (d < bd.hitR) KillBird(bd, a, false);
                else if (!bd.eagle && bd.Perched && d < 9f) StartTakeOff(bd, a, true);
            }
            for (int i = 0; i < wormList.Count; i++)
            {
                var w = wormList[i]; if (!w.Vulnerable) continue;
                if (DistSegmentSegment(a, b, w.pos, w.pos + Vector3.up * Exposed(w)) < w.radius) KillWorm(w, a);
            }
        }

        static float DistPointSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a; float l = ab.sqrMagnitude;
            float t = l > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / l) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }

        /// <summary>Closest distance between segments p1-q1 and p2-q2 (Ericson, Real-Time Collision Detection 5.1.9).</summary>
        static float DistSegmentSegment(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r), s, t;
            const float Eps = 1e-6f;
            if (a <= Eps && e <= Eps) return r.magnitude;
            if (a <= Eps) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= Eps) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float bb = Vector3.Dot(d1, d2), den = a * e - bb * bb;
                    s = den > Eps ? Mathf.Clamp01((bb * f - c * e) / den) : 0f;
                    t = (bb * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((bb - c) / a); }
                }
            }
            return Vector3.Distance(p1 + d1 * s, p2 + d2 * t);
        }

        // ------------------------------------------------------------------ feathers and goo
        void AddBit(byte kind, byte mat, Vector3 p, Vector3 v, float life, float size, System.Random rng)
        {
            ref var x = ref bits[bitNext]; bitNext = (bitNext + 1) % bits.Length;                 // ring: the oldest bit makes room
            x.kind = kind; x.mat = mat; x.p = p; x.v = v; x.life = life; x.age = 0f; x.size = size; x.rest = false;
            x.r = Quaternion.Euler(R(rng, 0f, 360f), R(rng, 0f, 360f), R(rng, 0f, 360f));
            x.spin = new Vector3(R(rng, -300f, 300f), R(rng, -200f, 200f), R(rng, -300f, 300f));
            x.gy = Surface(p.x, p.z); x.gyT = 0.4f;
        }

        void Feathers(Bird b, Vector3 at, int n, float burst)
        {
            for (int i = 0; i < n; i++)
            {
                byte m = b.eagle ? (b.rng.Next(3) == 0 ? MFeatherWhite : MFeatherBrown) : (b.rng.Next(3) == 0 ? MFeatherGrey : MFeatherWhite);
                var v = RandUnit(b.rng) * R(b.rng, 0.4f, 1f) * burst; v.y = Mathf.Abs(v.y) * 0.8f + 0.3f * burst;
                AddBit(KFeather, m, at + RandUnit(b.rng) * 0.2f * b.span, b.vel * 0.3f + v, R(b.rng, 5f, 8f), R(b.rng, 0.7f, 1.2f) * (b.eagle ? 1.4f : 1f), b.rng);
            }
        }

        void Goo(Worm w, Vector3 at, Vector3 from, int n)
        {
            var away = at - from; away.y = 0f; away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                var d = RandUnit(w.rng); d.y = Mathf.Abs(d.y);
                var v = d * R(w.rng, 2f, 5f) + Vector3.up * R(w.rng, 2f, 5f) + away * 1.5f;
                AddBit(KGoo, i % 3 == 0 ? MGooDark : MGoo, at + d * 0.3f, v, R(w.rng, 3f, 4.5f), R(w.rng, 0.6f, 1.4f) * wormScale, w.rng);
            }
        }

        void StepBits(float dt)
        {
            for (int i = 0; i < bits.Length; i++)
            {
                ref var x = ref bits[i];
                if (x.life <= 0f) continue;
                x.age += dt;
                if (x.age >= x.life) { x.life = 0f; continue; }
                if (x.rest) continue;
                Vector3 p;
                if (x.kind == KFeather)
                {   // feathers: drag down to a slow drifting fall, flutter sideways, spin
                    x.v += Vector3.down * (9.81f * 0.35f * dt); x.v *= 1f - Mathf.Min(1f, 2.6f * dt);
                    var flutter = new Vector3(Mathf.Sin(x.age * 6f + x.size * 17f) * 0.6f, 0f, Mathf.Cos(x.age * 4.3f + x.size * 9f) * 0.4f);
                    p = x.p + (x.v + flutter) * dt;
                    x.r = Quaternion.Euler(x.spin * dt) * x.r;
                }
                else
                {   // goo: ballistic, splats flat on the sand
                    x.v += Vector3.down * (9.81f * dt);
                    p = x.p + x.v * dt;
                    x.r = Quaternion.Euler(x.spin * dt) * x.r;
                }
                x.gyT -= dt;
                if (x.gyT <= 0f) { x.gyT = 0.4f; x.gy = Surface(p.x, p.z); }
                if (p.y <= x.gy + 0.02f)
                {
                    p.y = x.gy + 0.02f; x.rest = true; x.v = Vector3.zero;
                    x.r = x.kind == KGoo ? Quaternion.Euler(0f, x.spin.y, 0f) : Quaternion.Euler(90f, x.spin.y, 0f);
                    x.life = Mathf.Min(x.life, x.age + (x.kind == KGoo ? 2.5f : 4f));
                }
                x.p = p;
            }
        }

        void DrawBits()
        {
            if (bitMats == null) return;
            for (int m = 0; m < MatCount; m++) bitN[m] = 0;
            for (int i = 0; i < bits.Length; i++)
            {
                ref var x = ref bits[i];
                if (x.life <= 0f) continue;
                float shrink = Mathf.Clamp01((x.life - x.age) / 0.8f);
                Vector3 s = x.kind == KFeather ? new Vector3(0.05f, 0.012f, 0.17f) : x.rest ? new Vector3(0.22f, 0.04f, 0.22f) : new Vector3(0.13f, 0.13f, 0.13f);
                s *= x.size * shrink;
                bitBatch[x.mat][bitN[x.mat]++] = Matrix4x4.TRS(x.p, x.r, s);
            }
            for (int m = 0; m < MatCount; m++)
                if (bitN[m] > 0 && bitMats[m]) Graphics.DrawMeshInstanced(bitMesh, 0, bitMats[m], bitBatch[m], bitN[m], null, ShadowCastingMode.Off, true);
        }

        /// <summary>Unit octahedron stretched along Z: a feather (flat) or a goo drop (round) depending on the scale.</summary>
        static Mesh Lozenge()
        {
            var v = new[] { new Vector3(0, 0, 0.5f), new Vector3(0, 0, -0.5f), new Vector3(0.5f, 0, 0), new Vector3(-0.5f, 0, 0), new Vector3(0, 0.5f, 0), new Vector3(0, -0.5f, 0) };
            var t = new[] { 0, 2, 4, 0, 4, 3, 0, 3, 5, 0, 5, 2, 1, 4, 2, 1, 3, 4, 1, 5, 3, 1, 2, 5 };
            var m = new Mesh { name = "CritterBit" }; m.vertices = v; m.triangles = t; m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ bird shadows
        /// <summary>Flat bird silhouette (1 m span, +Z = head), solid inside with a soft vertex-alpha rim (OpenZ/Overlay).</summary>
        static Mesh BirdShadowMesh()
        {
            // right half outline (x, z) from the head round the wing tip to the tail; mirrored for the left half
            var half = new[] { new Vector2(0f, 0.3f), new Vector2(0.05f, 0.22f), new Vector2(0.18f, 0.12f), new Vector2(0.35f, 0.1f), new Vector2(0.5f, 0.04f),
                               new Vector2(0.38f, -0.02f), new Vector2(0.2f, -0.06f), new Vector2(0.07f, -0.12f), new Vector2(0.1f, -0.28f), new Vector2(0f, -0.3f) };
            var ring = new List<Vector2>();
            for (int i = 0; i < half.Length; i++) ring.Add(half[i]);
            for (int i = half.Length - 2; i >= 1; i--) ring.Add(new Vector2(-half[i].x, half[i].y));
            int n = ring.Count;
            var v = new List<Vector3> { Vector3.zero }; var c = new List<Color> { Color.white }; var t = new List<int>();
            for (int i = 0; i < n; i++) { v.Add(new Vector3(ring[i].x, 0f, ring[i].y)); c.Add(Color.white); }
            for (int i = 0; i < n; i++) { var o = ring[i] * 1.18f + ring[i].normalized * 0.02f; v.Add(new Vector3(o.x, 0f, o.y)); c.Add(new Color(1f, 1f, 1f, 0f)); }
            for (int i = 0; i < n; i++)
            {
                int a = 1 + i, b = 1 + (i + 1) % n, oa = 1 + n + i, ob = 1 + n + (i + 1) % n;
                t.Add(0); t.Add(b); t.Add(a);
                t.Add(a); t.Add(b); t.Add(ob); t.Add(a); t.Add(ob); t.Add(oa);
            }
            var m = new Mesh { name = "BirdShadow" }; m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        Light Sun()
        {
            if (sunLight) return sunLight;
            if (dayNight && dayNight.sun) sunLight = dayNight.sun; else sunLight = RenderSettings.sun;
            return sunLight;
        }

        void DrawBirdShadow(Bird b)
        {
            if (b.st == BS.Gone || b.st == BS.Down || b.st == BS.Perch || !b.rig.shown) return;   // perched / down: the real shadow
            var sun = Sun(); if (!sun) return;
            var L = sun.transform.forward; if (L.y > -0.2f) return;                              // sun too low: shadows fall off the map
            float elev = Mathf.Asin(Mathf.Clamp(-L.y, -1f, 1f)) * Mathf.Rad2Deg;
            float k = Mathf.Clamp01(sun.intensity / 1.2f) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(12f, 30f, elev));
            if (k < 0.02f) return;
            Vector3 hitP, n = Vector3.up;
            if (walkMask != 0 && Physics.Raycast(b.pos, L, out var hit, 250f, walkMask, QueryTriggerInteraction.Ignore)) { hitP = hit.point; n = hit.normal; }
            else { float g = game.GroundY(b.pos.x, b.pos.z); hitP = b.pos + L * ((b.pos.y - g) / -L.y); hitP.y = game.GroundY(hitP.x, hitP.z); }
            if (hitP.y < waterY) { hitP = b.pos + L * ((b.pos.y - waterY) / -L.y); hitP.y = waterY; n = Vector3.up; }   // on the water surface
            float h = Mathf.Max(0f, b.pos.y - hitP.y);
            float a = 0.34f * k * Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(h / 35f));
            float open = b.pose0 == PerchF ? 0.45f : 1f - 0.4f * b.w1 * Mathf.Abs(Mathf.Sin(b.flapT * Mathf.PI * 2f));
            float sc = b.span * (1f + h * 0.008f);
            float yaw = b.st == BS.Fall ? b.rot.eulerAngles.y : b.yaw;
            var rot = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(0f, yaw, 0f);
            var m = Matrix4x4.TRS(hitP + n * 0.06f, rot, new Vector3(sc * open, 1f, sc));
            shadowMpb.SetColor(ColorId, new Color(0.05f, 0.04f, 0.03f, a));
            Graphics.DrawMesh(shadowMesh, m, shadowMat, 0, null, 0, shadowMpb, ShadowCastingMode.Off, false);
        }

        // ------------------------------------------------------------------ sound
        void Sound(string id, Vector3 p, float vol, int emitter)
        {
            if (!playSounds) return;
            if (AudioEvent != null) { AudioEvent(id, p, vol); return; }
            var au = CombatAudio.I; if (!au) return;
            string[] names;
            switch (id)
            {
                case SndEagleCry: names = ClipEagle; break;
                case SndGullCall: case SndBirdHit: names = id == SndBirdHit ? ClipSplat : ClipGull; break;
                case SndWormRumble: names = ClipRumble; break;
                case SndWormEmerge: names = ClipWorm; break;
                case SndWormDive: names = ClipWormDive; break;
                case SndWormSplat: names = ClipSplat; break;
                default: return;                                                                   // no default clip (takeoff, thud, splash)
            }
            au.Play(id, p, vol, 0.25f, emitter, names);
        }

        // ------------------------------------------------------------------ tests / debug (README_IMPORT.md)
        /// <summary>One line per critter: kind, state, position. For the README test steps and editor-script checks.</summary>
        public string DebugState()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"AmbientLife {level} t={clock:F0}s night={night}\n");
            foreach (var b in birds) sb.Append($"  {(b.eagle ? "eagle" : "gull")}{b.id} {b.st} ({b.pos.x:F0},{b.pos.y:F1},{b.pos.z:F0}) flap {b.w1:F2} perch {b.perch}\n");
            foreach (var w in wormList) sb.Append($"  worm{w.id} {w.st} ({w.pos.x:F0},{w.pos.y:F1},{w.pos.z:F0}) timer {w.timer:F1} respawn {w.respawn:F0}\n");
            return sb.ToString();
        }

        public static string Status() => I ? I.DebugState() : "AmbientLife not running (no level scene or AutoBoot off)";

        [ContextMenu("Test: worms up now")]
        public void TestWormsUp() { nextWormAt = clock; fightT = -99f; foreach (var w in wormList) if (w.st == WS.Under) w.timer = 0f; }

        [ContextMenu("Test: eagles swoop now")]
        public void TestSwoop() { foreach (var b in birds) if (b.eagle && b.st == BS.Soar) TryStartSwoop(b); }

        [ContextMenu("Test: gulls land now")]
        public void TestLand() { foreach (var b in birds) if (!b.eagle && b.st == BS.Soar) TryStartLanding(b); }

        [ContextMenu("Test: explode at every critter")]
        public void TestExplodeAll()
        {
            var at = new List<Vector3>();
            foreach (var b in birds) if (b.Alive) at.Add(b.pos + Vector3.down * 0.5f);
            foreach (var w in wormList) if (w.Vulnerable) at.Add(w.pos + Vector3.up * 0.2f);
            foreach (var p in at) { if (fx) fx.Explosion(p, 0.6f); else OnExplosion(p, 0.6f); }
        }

        [ContextMenu("Test: respawn everything now")]
        public void TestRespawn()
        {
            foreach (var b in birds) if (b.st == BS.Gone) b.respawn = 0f;
            foreach (var w in wormList) if (w.st == WS.Dead) w.respawn = 0f;
        }
    }
}
