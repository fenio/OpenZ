using System;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// ZWorld.Rules.cs CheckFlagCaptures / AwardZone).
    /// <list type="bullet">
    /// <item>Every zone flag and both fort flags are waving cloth (OpenZ/FlagCloth: GPU vertex wave from the pole to the fly
    /// end, wind from the Atmosphere's global _FogWind, per-flag phase, two-sided soft light) in the owner's colour (Z's brick
    /// red / royal blue / warm grey-white), on a pole with a brass finial. The cloth flies downwind (all flags agree).</item>
    /// <item>
    /// out from the flag in ~1 s, and each linked building flashes in the team colour when the sweep reaches it.</item>
    /// <item>Fort flags wave too (the static flag baked into the imported fort mesh is cut out at run time, the cloth flag
    /// stands in its roof socket) and dip toward half-mast while the fort is in danger (Zod's rule: an enemy unit within
    /// 250 px of the fort centre, zmusic_engine.cpp:213-313, or the fort took damage), rising again 3 s after it is calm.</item>
    /// <item><see cref="Sound"/>
    /// staged AudioDirector subscribes by reflection and sets <see cref="BuiltInSounds"/> = false (README: its OnFlagSoundObj
    /// Resources/OpenZ/Sfx through CombatAudio.</item>
    /// </list>
    /// View side only: polls the sim every frame (no events consumed, nothing written). No per-frame allocations.
    /// </summary>
    [DefaultExecutionOrder(120)]   // after ZGame (-50, sim tick), ZoneMarkers (0) and CombatView (50): same-frame reaction
    public class FlagView : MonoBehaviour
    {
        int fortSeen;
        // ================================================================== public hooks
        public static FlagView I { get; private set; }
        /// <summary>True while a FlagView runs: RulesView skips its own capture ring and flag sounds.</summary>
        public static bool Active => I != null && I.isActiveAndEnabled;

        /// <summary></summary>
        public const string SndCapture = "ter_flag_capture", SndRaise = "ter_flag_raise", SndGained = "ter_zone_gained",
                            SndZoneLost = "ter_zone_lost", SndBuilding = "bld_building_captured",
                            SndCaptured = "ann_territory_captured", SndLost = "ann_territory_lost", SndRadar = "ann_radar_online",
                            SndFortAttack = "ann_fort_under_attack";

        public struct SoundEvent
        {
            public string id; public Vector3 position; public bool is2D; public float volume;
            public Team team, oldTeam; public int objectId;
        }
        public struct CaptureInfo { public ZObject flag; public ZZone zone; public Team from, to; public Vector3 position; }

        /// <summary>Every flag sound moment (AudioDirector subscribes and sets <see cref="BuiltInSounds"/> = false).</summary>
        public static event Action<SoundEvent> Sound;
        /// <summary>The raise moment of every capture (HUD toast, minimap ping).</summary>
        public static event Action<CaptureInfo> Captured;
        /// <summary><see cref="Sound"/></summary>
        public static bool BuiltInSounds = true;

        // ================================================================== settings
        [Header("Materials (else Resources OpenZ/FX/M_FlagCloth + M_FlagCaptureFx, else Shader.Find)")]
        public Material clothMaterial;
        public Material fxMaterial;

        [Header("Zone flag (metres)")]
        public float poleHeight = 3.4f;
        public float poleRadius = 0.045f;
        public Vector2 clothSize = new Vector2(1.45f, 0.92f);
        [Tooltip("Hide the renderers of the Z_flag prefab (placeholder pole + banner cube)")] public bool hidePrefabFlag = true;

        [Header("Fort flag (metres)")]
        public float fortPoleHeight = 4.3f;
        public float fortPoleRadius = 0.2f;
        public Vector2 fortClothSize = new Vector2(3.0f, 1.9f);
        [Tooltip("Cut the static flag baked into the imported fort mesh (m01_fort.glb) at run time")] public bool cutBakedFortFlag = true;
        [Range(0, 1), Tooltip("How far the fort cloth dips (fraction of its hoist travel) while the fort is in danger")] public float fortDipDepth = 0.75f;

        [Header("Wind")]
        [Range(0, 2)] public float windStrength = 1f;
        [Tooltip("Used when the Atmosphere sets no _FogWind (test mode turns it off); m/s, = Atmosphere.cs:100 desert / :114 city")]
        public Vector3 fallbackWind = new Vector3(3f, 0, 1.5f);
        public Vector3 fallbackWindCity = new Vector3(1.2f, 0, 0.6f);
        [Range(0, 30)] public float swayDegrees = 9f;

        [Header("Capture sequence (seconds / metres)")]
        public float dropTime = 0.34f;
        public float raiseTime = 0.95f;
        public float celebrateTime = 2.25f;
        public float sweepTime = 1.0f;
        public float captureRingRadius = 1.6f;
        public float pulseRadius = 7.5f;
        [Range(0, 0.3f), Tooltip("Faint zone tint kept after the sweep (0 = none: Z's team colour stays subtle)")] public float groundTintHold = 0f;
        [Range(0, 3)] public float fxGlow = 0.45f;
        [Range(0, 80)] public int confettiCount = 34;
        [Tooltip("Minimum gap between announcer lines (s)")] public float voiceGap = 1.6f;
        [Tooltip("Height samples per frame for the draped ring / zone meshes built after start")] public int drapeBudget = 400;

        // ================================================================== state
        sealed class Flag
        {
            public ZObject obj; public bool fort; public ZZone zone; public int index;
            public Transform root, pivot, cloth; public Mesh mesh; public Color[] colors; public Renderer clothR, poleR;
            public Vector2 size; public float hoistTop, hoistLow, y, seed;
            public Team shown;                       // owner the cloth shows (or is animating towards)
            public Color paint; public float energy; public Color uploadedPaint; public float uploadedEnergy = -1;
            public float capT0 = -1, dropFrom; public Team from, to; public bool swapped;
            public Draped ring; public float ringT0 = -1;
            public Vector3 blockDir; public float blockCos = 2f;          // fort: never fly into the central turret
            public GameObject fortView; public int lastHealth; public float nextCheck, hitUntil, calmSince = -1, dip;
            public bool nearEnemy; public float nearSince = -1, nextAnnounce, deadT = -1; public Vector2Int fortCentre;
        }

        sealed class ZoneFx
        {
            public ZZone zone; public Draped wash; public Renderer marker; public MeshRenderer border; public bool markerLooked, markerWasOn;
            public float t0 = -1; public Vector3 centre; public float maxR; public Team from, to;
        }

        /// <summary>Terrain-draped grid (rect or polar); heights are sampled over several frames (raycast budget).</summary>
        sealed class Draped
        {
            public int nx, nz; public bool polar, zoneRules; public Vector3[] v; public Color[] c; public int filled;
            public Mesh mesh; public MeshRenderer r; public bool queued;
        }

        struct Pending { public float at; public int kind; public string id; public Vector3 pos; public Team team, old; public int objId; }
        const int KindBuilding = 0, KindVoice = 1;
        readonly List<(float delay, int id, Vector3 pos)> links = new List<(float, int, Vector3)>(8);

        ZGame game; Transform root;
        readonly List<Flag> flags = new List<Flag>();
        readonly List<ZoneFx> zones = new List<ZoneFx>();
        readonly List<Draped> buildQueue = new List<Draped>();
        readonly List<Pending> pending = new List<Pending>(48);
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        Material cloth, fx, fxGround, sparkMat, confettiMat;
        /// <summary>Ring, zone wash and border sweep: scene surfaces up to this far (m of eye depth) in front of the draped mesh
        /// still receive the effect (full up to 0.6 x, fading out to 1 x), so small rises and low props no longer cut it.</summary>
        public const float GroundBand = 1.6f;
        static readonly int GroundBandId = Shader.PropertyToID("_GroundBand"), ZTestId = Shader.PropertyToID("_ZTest");

        /// <summary>The ground layers draw without the depth buffer test and the shader tests the camera depth texture with this
        /// band instead (OpenZ/FlagCaptureFx _GroundBand). 0, or a pipeline without the depth texture = the plain depth buffer test.</summary>
        public void SetGroundBand(float band)
        {
            if (!fxGround) return;
            bool sceneDepth = true;
            var urp = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp) sceneDepth = urp.supportsCameraDepthTexture;
            bool on = band > 0f && sceneDepth;
            fxGround.SetFloat(GroundBandId, on ? band : 0f);
            fxGround.SetFloat(ZTestId, (float)(on ? CompareFunction.Always : CompareFunction.LessEqual));
        }
        Mesh zonePole, fortPole;
        MaterialPropertyBlock mpb;
        ParticleSystem sparks, confetti;
        ZoneMarkers markers; bool markersLooked;
        Vector3 windDir = Vector3.right; float windYaw, windPhase, nextVoice;
        string fortCutLog = "none";

        static readonly int FogWindId = Shader.PropertyToID("_FogWind"), FlagWindId = Shader.PropertyToID("_OZFlagWind"),
                            ModeId = Shader.PropertyToID("_Mode"), NewColorId = Shader.PropertyToID("_NewColor"),
                            OldColorId = Shader.PropertyToID("_OldColor"), CenterId = Shader.PropertyToID("_Center"),
                            AnimId = Shader.PropertyToID("_Anim"), GlowId = Shader.PropertyToID("_Glow");

        // ZoneMarkers band alpha (ZoneMarkers.cs:18 Alpha = 0.5, neutral x0.55 at :54)
        const float LineAlpha = 0.5f, NeutralLineK = 0.55f;
        // Zod: "fort in danger" = an enemy non-building, non-item object within 250 px of the fort centre, checked every
        // 0.25 s; the danger level changes 3 s later (zmusic_engine.cpp:213-313); the warning repeats every 10 s (zplayer.cpp:1087-1096)
        const int FortDangerPx = 250; const float FortCheck = 0.25f, FortLevelDelay = 3f, FortRepeat = 10f;
        // Cloth energy (vertex alpha) 0..1 boosts the flutter; OpenZ/FlagCloth adds the capture glow only above 0.3, so the old
        // cloth dropping and the fort flag flinching / dipping flutter harder without glowing (team colour stays subtle).
        const float FlutterOnly = 0.3f;

        // at glTF (x 0.0024, z -0.6263), roof socket y 0.2125, pole top 0.5755, cloth x 0.03..0.31. Unity mesh space = glTF
        // with X negated (glTFast). Cut = triangles fully inside the box, plus the pole down to the roof socket.
        static readonly Vector3 BakedBoxMin = new Vector3(-0.325f, 0.30f, -0.705f), BakedBoxMax = new Vector3(0.045f, 0.585f, -0.58f);
        static readonly Vector3 BakedPoleBase = new Vector3(-0.0024f, 0.2125f, -0.6263f);
        const float BakedPoleR = 0.036f, BakedPoleY0 = 0.205f, BakedPoleY1 = 0.585f;
        static readonly Dictionary<Mesh, (Mesh cut, float sign)> fortCutCache = new Dictionary<Mesh, (Mesh, float)>();

        // the audio helper under these names; a missing clip is skipped silently)
        static readonly string[] ClipCapture = { "flag-capture-1", "flag-capture-2", "s3_flag_raise_tight", "s3_flag_capture_d" }, ClipRaise = { "v3-capture-raise" }, ClipGained = { "v3-flag-success", "sd_zone_flip_sweep", "s3_zone_gained_c" },   // sound_v3
            ClipCaptured = { "rachel_territory_captured_1", "rachel_territory_captured_2" }, ClipLost = { "rachel_territory_lost_1", "rachel_territory_lost_2" },
            ClipRadar = { "rachel_radar_online_1", "rachel_radar_online_2" }, ClipFort = { "rachel_fort_under_attack_1", "rachel_fort_under_attack_2" };

        // ================================================================== bootstrap
        /// <summary>Adds a FlagView to every game scene (a scene with a ZGame) unless one is already there.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInstall()
        {
            Ensure();
            SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
        }
        static void OnSceneLoaded(Scene s, LoadSceneMode m) => Ensure();
        static void Ensure()
        {
            if (PlayerPrefs.GetInt("oz_flagview", 1) == 0) return;
            var g = ZGame.I; if (!g || FindFirstObjectByType<FlagView>()) return;
            g.gameObject.AddComponent<FlagView>();
        }

        void Awake() { if (I && I != this) { Destroy(this); return; } I = this; mpb = new MaterialPropertyBlock(); }

        void OnDestroy()
        {
            if (I != this) return;
            I = null;
            foreach (var o in owned) if (o) Destroy(o);
            if (root) Destroy(root.gameObject);
            Shader.SetGlobalVector(FlagWindId, Vector4.zero);
        }

        static FxManager Fxm() => FxManager.I ? FxManager.I : null;   // Unity-null safe (?. skips the destroyed check)

        T Own<T>(T o) where T : UnityEngine.Object { if (o) owned.Add(o); return o; }

        Material LoadMat(Material assigned, string res, string shader)
        {
            if (assigned) return assigned;
            var m = Resources.Load<Material>(res); if (m) return m;
            var s = Shader.Find(shader); if (!s) return null;
            return Own(new Material(s) { name = shader + " (runtime)" });
        }

        void Start()
        {
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (!game || game.World == null) { enabled = false; return; }
            cloth = LoadMat(clothMaterial, "OpenZ/FX/M_FlagCloth", "OpenZ/FlagCloth");
            fx = LoadMat(fxMaterial, "OpenZ/FX/M_FlagCaptureFx", "OpenZ/FlagCaptureFx");
            if (!cloth) { Debug.LogError("[OpenZ] FlagView: shader OpenZ/FlagCloth missing (import OpenZFlagCloth.shader)"); enabled = false; return; }
            if (!fx) Debug.LogWarning("[OpenZ] FlagView: shader OpenZ/FlagCaptureFx missing: flags wave, but no ring / sweep / confetti");

            root = new GameObject("OZ_Flags").transform;
            zonePole = Own(BuildPoleMesh(poleHeight, poleRadius, 0, 0));
            fortPole = Own(BuildPoleMesh(fortPoleHeight, fortPoleRadius, fortPoleRadius * 1.7f, 0.35f));

            int nZone = 0, nFort = 0;
            foreach (var o in game.World.Flags) if (AddZoneFlag(o)) nZone++;
            foreach (var o in game.World.Objects) if (o.IsFort && AddFortFlag(o)) nFort++;
            if (fx)
            {
                fxGround = Own(new Material(fx) { name = "FlagGroundFx" }); SetGroundBand(GroundBand);   // ring / wash / border sweep
                sparkMat = Own(new Material(fx) { name = "FlagSparks" }); sparkMat.SetFloat(ModeId, 3); sparkMat.SetFloat(GlowId, fxGlow);
                confettiMat = Own(new Material(fx) { name = "FlagConfetti" }); confettiMat.SetFloat(ModeId, 4);
                sparks = MakeParticles("FlagSparks", sparkMat, false);
                confetti = MakeParticles("FlagConfetti", confettiMat, true);
                foreach (var f in flags) if (!f.fort) { f.ring = NewRing(f); EnqueueDrape(f.ring); }
                foreach (var z in game.World.Zones) if (z.Flag != null) { var zf = GetZone(z); if (zf != null) EnqueueDrape(zf.wash); }
            }
            UpdateWind(0f, Time.time);
            foreach (var f in flags) PoseCloth(f, Time.time);
            Debug.Log($"[OpenZ] FlagView: {nZone} zone flags, {nFort} fort flags (fort flag cut: {fortCutLog}), fx {(fx ? "on" : "off")}");
        }

        // ================================================================== flag construction
        bool AddZoneFlag(ZObject o)
        {
            if (o == null) return false;
            var basePos = game.CenterWorld(o);
            if (game.StaticViews.TryGetValue(o.Id, out var go) && go && hidePrefabFlag)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            var f = NewFlag(o, basePos - Vector3.up * 0.05f, zonePole, poleHeight, poleRadius, clothSize, false);
            f.zone = game.World.ZoneOf(o);
            return true;
        }

        bool AddFortFlag(ZObject o)
        {
            if (!game.StaticViews.TryGetValue(o.Id, out var go) || !go) return false;
            Vector3 basePos;
            MeshFilter mf = null; float sign = 0f;
            foreach (var m in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = m.sharedMesh; if (!mesh || !mesh.isReadable || mesh.vertexCount < 5000) continue;
                float sg = FindBakedFlag(mesh); if (sg == 0) continue;
                mf = m; sign = sg; break;
            }
            if (mf)
            {
                var M = mf.transform.localToWorldMatrix;
                basePos = M.MultiplyPoint3x4(new Vector3(BakedPoleBase.x * sign, BakedPoleBase.y, BakedPoleBase.z));
                if (cutBakedFortFlag) CutBakedFlag(mf, sign);
                else fortCutLog = "off";
            }
            else
            {   // no readable imported fort mesh: Zod's flag sprite spot (bfort.cpp:316-334, local px 85,29) on top of the model
                var rs = go.GetComponentsInChildren<Renderer>(true); if (rs.Length == 0) return false;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                basePos = ZGame.PxToWorld(o.LocX + 85, o.LocY + 29); basePos.y = b.max.y - 0.4f;
                fortCutLog = "baked flag not found (fallback position)";
            }
            var f = NewFlag(o, basePos, fortPole, fortPoleHeight, fortPoleRadius, fortClothSize, true);
            f.fortView = go; f.lastHealth = o.Health;
            f.fortCentre = new Vector2Int(o.CenterX, o.CenterY);
            var c = game.CenterWorld(o, false); var toTurret = new Vector3(c.x - basePos.x, 0, c.z - basePos.z);
            if (toTurret.sqrMagnitude > 0.01f) { f.blockDir = toTurret.normalized; f.blockCos = Mathf.Cos(62f * Mathf.Deg2Rad); }
            return true;
        }

        Flag NewFlag(ZObject o, Vector3 basePos, Mesh pole, float height, float radius, Vector2 size, bool fort)
        {
            var f = new Flag { obj = o, fort = fort, index = flags.Count, size = size, shown = o.Owner, seed = (o.Id * 0.6180339f) % 1f * 6.2831853f };
            f.root = new GameObject((fort ? "FortFlag_" : "Flag_") + o.Id).transform; f.root.SetParent(root, false);
            f.root.position = basePos;
            var pg = new GameObject("Pole"); pg.transform.SetParent(f.root, false);
            pg.AddComponent<MeshFilter>().sharedMesh = pole;
            var pr = pg.AddComponent<MeshRenderer>(); pr.sharedMaterial = cloth; pr.shadowCastingMode = ShadowCastingMode.On; f.poleR = pr;
            f.pivot = new GameObject("Pivot").transform; f.pivot.SetParent(f.root, false);
            f.cloth = new GameObject("Cloth").transform; f.cloth.SetParent(f.pivot, false);
            f.mesh = Own(BuildClothMesh(size, f.seed, out f.colors));
            f.cloth.gameObject.AddComponent<MeshFilter>().sharedMesh = f.mesh;
            var cr = f.cloth.gameObject.AddComponent<MeshRenderer>(); cr.sharedMaterial = cloth; cr.shadowCastingMode = ShadowCastingMode.On; f.clothR = cr;
            f.hoistTop = height - (fort ? 0.3f : 0.16f);
            f.hoistLow = Mathf.Min(f.hoistTop, size.y + 0.15f + size.x * 0.25f);
            f.y = f.hoistTop;
            f.cloth.localPosition = new Vector3(radius * 0.6f, f.y, 0);
            f.paint = ClothColor(o.Owner);
            flags.Add(f);
            Upload(f);
            return f;
        }

        /// <summary>Owner colour of the cloth (linear): TeamColors brick red / royal blue; neutral = a mid warm grey cloth.</summary>
        static Color ClothColor(Team t)
        {
            var c = TeamColors.Of(t).linear;
            if (t == Team.Neutral) { c = new Color(0.19f, 0.18f, 0.16f, 1f); }   // neutral cloth = mid warm grey (sRGB ~120,118,112), never white
            c.a = 1; return c;
        }

        /// <summary>Light colour for flashes (brighter than the paint so it reads as light on the ground and walls).</summary>
        static Color LightColor(Team t) => (t = TeamColors.Shown(t)) == Team.Red ? new Color(1f, 0.34f, 0.22f) : t == Team.Blue ? new Color(0.36f, 0.52f, 1f) : new Color(0.95f, 0.92f, 0.85f);

        // ================================================================== meshes
        const int ClothCols = 16, ClothRows = 8;

        /// <summary>UV1 = (cloth width, height, wave phase seed in radians, 0): the phase lives in the mesh, so it never jumps when
        /// the sway moves the cloth (a phase from the world position wrapped through frac() and could pop).</summary>
        static Mesh BuildClothMesh(Vector2 size, float seed, out Color[] colors)
        {
            int nx = ClothCols + 1, ny = ClothRows + 1, n = nx * ny;
            var v = new Vector3[n]; var nr = new Vector3[n]; var uv = new Vector2[n]; var uv1 = new Vector4[n]; colors = new Color[n];
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i; float u = i / (float)ClothCols, vv = j / (float)ClothRows;
                    v[k] = new Vector3(u * size.x, -size.y + vv * size.y, 0); nr[k] = Vector3.forward;
                    uv[k] = new Vector2(u, vv); uv1[k] = new Vector4(size.x, size.y, seed, 0); colors[k] = Color.white;
                }
            var t = new int[ClothCols * ClothRows * 6]; int q = 0;
            for (int j = 0; j < ClothRows; j++)
                for (int i = 0; i < ClothCols; i++)
                {
                    int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                    t[q++] = a; t[q++] = c; t[q++] = b; t[q++] = b; t[q++] = c; t[q++] = d;
                }
            var m = new Mesh { name = "FlagCloth" };
            m.vertices = v; m.normals = nr; m.uv = uv; m.SetUVs(1, uv1); m.colors = colors; m.triangles = t;
            // the vertex wave moves the cloth out of its plane and the droop swings it down: generous bounds, no pop-out culling
            m.bounds = new Bounds(new Vector3(size.x * 0.5f, -size.y * 0.7f, 0), new Vector3(size.x * 1.3f, size.y * 2.2f, size.x * 1.0f));
            return m;
        }

        static readonly Color PolePaint = new Color(0.40f, 0.41f, 0.43f, 0f), PoleDark = new Color(0.22f, 0.22f, 0.23f, 0f),
                              Brass = new Color(0.72f, 0.46f, 0.12f, 0f);

        /// <summary>Pole (tapering shaft, sunk 15 cm), hoist collar, brass ball finial, optional base collar (fort roof socket).
        /// uv.x = -1 marks rigid vertices for OpenZ/FlagCloth, uv.y = 1 marks brass; colours are linear.</summary>
        static Mesh BuildPoleMesh(float height, float radius, float baseR, float baseH)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var c = new List<Color>(); var t = new List<int>();
            const int S = 10;
            void Ring(float y0, float y1, float r0, float r1, Color col, float brass, bool capTop)
            {
                int start = v.Count; float slope = (r0 - r1) / Mathf.Max(y1 - y0, 1e-4f);
                for (int i = 0; i <= S; i++)
                {
                    float a = i * Mathf.PI * 2f / S, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    var nn = new Vector3(ca, slope, sa).normalized;
                    v.Add(new Vector3(ca * r0, y0, sa * r0)); n.Add(nn); uv.Add(new Vector2(-1, brass)); c.Add(col);
                    v.Add(new Vector3(ca * r1, y1, sa * r1)); n.Add(nn); uv.Add(new Vector2(-1, brass)); c.Add(col);
                }
                for (int i = 0; i < S; i++) { int a = start + i * 2; t.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 }); }
                if (!capTop) return;
                int ctr = v.Count; v.Add(new Vector3(0, y1, 0)); n.Add(Vector3.up); uv.Add(new Vector2(-1, brass)); c.Add(col);
                int rs = v.Count;
                for (int i = 0; i <= S; i++) { float a = i * Mathf.PI * 2f / S; v.Add(new Vector3(Mathf.Cos(a) * r1, y1, Mathf.Sin(a) * r1)); n.Add(Vector3.up); uv.Add(new Vector2(-1, brass)); c.Add(col); }
                for (int i = 0; i < S; i++) t.AddRange(new[] { ctr, rs + i + 1, rs + i });
            }
            void Ball(Vector3 ctr, float r, Color col)
            {
                const int LA = 6; int start = v.Count;
                for (int j = 0; j <= LA; j++)
                {
                    float th = j * Mathf.PI / LA, y = Mathf.Cos(th), s = Mathf.Sin(th);
                    for (int i = 0; i <= S; i++)
                    {
                        float a = i * Mathf.PI * 2f / S; var d = new Vector3(Mathf.Cos(a) * s, y, Mathf.Sin(a) * s);
                        v.Add(ctr + d * r); n.Add(d); uv.Add(new Vector2(-1, 1)); c.Add(col);
                    }
                }
                for (int j = 0; j < LA; j++)
                    for (int i = 0; i < S; i++)
                    {
                        int a = start + j * (S + 1) + i, b = a + S + 1;
                        t.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                    }
            }
            Ring(-0.15f, height, radius, radius * 0.8f, PolePaint, 0, true);                         // shaft
            Ring(height - 0.03f, height + 0.05f, radius * 1.5f, radius * 1.45f, Brass, 1, true);       // hoist truck
            float fr = Mathf.Max(radius * 2.1f, 0.075f);
            Ball(new Vector3(0, height + 0.05f + fr * 0.92f, 0), fr, Brass);                           // finial
            if (baseR > 0) Ring(-0.1f, baseH, baseR, baseR * 0.85f, PoleDark, 0, true);                // roof collar
            var m = new Mesh { name = "FlagPole" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        // ================================================================== baked fort flag
        /// <summary>Returns the X sign (+1 glTFast, -1 unflipped) whose baked-flag box holds the flag's vertices, else 0.</summary>
        static float FindBakedFlag(Mesh mesh)
        {
            if (fortCutCache.TryGetValue(mesh, out var hit)) return hit.sign;
            var b = mesh.bounds;
            if (b.max.y < 0.5f || b.max.y > 0.65f || b.extents.x < 0.8f) return 0;                       // not m01_fort
            var vs = mesh.vertices;
            foreach (var sign in new[] { 1f, -1f })
            {
                int n = 0;
                foreach (var p in vs) if (InBox(p, sign)) n++;
                if (n >= 150) return sign;
            }
            return 0;
        }

        static bool InBox(Vector3 p, float sign)
        {
            float x = p.x * sign;
            return x >= BakedBoxMin.x && x <= BakedBoxMax.x && p.y >= BakedBoxMin.y && p.y <= BakedBoxMax.y && p.z >= BakedBoxMin.z && p.z <= BakedBoxMax.z;
        }
        static bool InPole(Vector3 p, float sign)
        {
            float dx = p.x * sign - BakedPoleBase.x, dz = p.z - BakedPoleBase.z;
            return dx * dx + dz * dz < BakedPoleR * BakedPoleR && p.y >= BakedPoleY0 && p.y <= BakedPoleY1;
        }

        /// <summary>Swaps the fort's mesh for a copy without the baked flag (shared by both forts; original bounds kept, so
        /// TeamPaint's accent band, which reads mesh.bounds, is unchanged).</summary>
        void CutBakedFlag(MeshFilter mf, float sign)
        {
            var src = mf.sharedMesh;
            if (!fortCutCache.TryGetValue(src, out var e) || !e.cut)
            {
                var vs = src.vertices; var cut = Instantiate(src); cut.name = src.name + "_noflag";
                int removed = 0; var keep = new List<int>();
                for (int s = 0; s < src.subMeshCount; s++)
                {
                    var tris = src.GetTriangles(s); keep.Clear();
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        Vector3 a = vs[tris[i]], b = vs[tris[i + 1]], c = vs[tris[i + 2]];
                        bool flag = (InBox(a, sign) && InBox(b, sign) && InBox(c, sign)) || (InPole(a, sign) && InPole(b, sign) && InPole(c, sign));
                        if (flag) { removed++; continue; }
                        keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]);
                    }
                    cut.SetTriangles(keep, s, false);
                }
                cut.bounds = src.bounds;
                e = (cut, sign); fortCutCache[src] = e; fortCutCache[cut] = e;
                fortCutLog = $"{removed} triangles removed";
            }
            else fortCutLog = "cached cut mesh reused";                  // scene reload: the static cache already holds the copy
            mf.sharedMesh = e.cut;
        }

        // ================================================================== draped FX meshes
        Draped NewRing(Flag f)
        {
            const int seg = 56, rings = 14; float R = pulseRadius + 1f;
            var d = new Draped { nx = seg, nz = rings + 1, polar = true, v = new Vector3[seg * (rings + 1)], c = new Color[seg * (rings + 1)] };
            var o = f.root.position;
            for (int j = 0; j <= rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg, r = R * j / rings;
                    d.v[j * seg + i] = new Vector3(o.x + Mathf.Cos(a) * r, 0, o.z + Mathf.Sin(a) * r);
                    d.c[j * seg + i] = new Color(1, 1, 1, 1);
                }
            return d;
        }

        ZoneFx GetZone(ZZone z)
        {
            foreach (var e in zones) if (e.zone == z) return e;
            if (!fx) return null;
            float x0 = z.X / 8f, x1 = (z.X + z.W) / 8f, zt = -z.Y / 8f, zb = -(z.Y + z.H) / 8f;
            const float step = 1f;
            int nx = Mathf.Max(2, Mathf.CeilToInt((x1 - x0) / step) + 1), nz = Mathf.Max(2, Mathf.CeilToInt((zt - zb) / step) + 1);
            var d = new Draped { nx = nx, nz = nz, zoneRules = true, v = new Vector3[nx * nz], c = new Color[nx * nz] };
            var map = game.World.Map;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = Mathf.Lerp(x0, x1, i / (float)(nx - 1)), zz = Mathf.Lerp(zt, zb, j / (float)(nz - 1));
                    var px = ZGame.WorldToPx(new Vector3(x, 0, zz));
                    var cls = map.ClassAt(Mathf.Clamp(px.x, z.X, z.X + z.W - 1) >> 4, Mathf.Clamp(px.y, z.Y, z.Y + z.H - 1) >> 4);
                    bool ok = cls == ZClass.Ground || cls == ZClass.Road;                                  // no river, cliffs, rock walls
                    float edge = Mathf.Min(Mathf.Min(x - x0, x1 - x), Mathf.Min(zt - zz, zz - zb));
                    d.v[j * nx + i] = new Vector3(x, 0, zz);
                    d.c[j * nx + i] = new Color(1, 1, 1, ok ? Mathf.Clamp01((edge - 0.25f) / 1.2f) : 0f);
                }
            var e2 = new ZoneFx { zone = z, wash = d };
            zones.Add(e2);
            return e2;
        }

        void EnqueueDrape(Draped d) { if (d != null && !d.queued && d.mesh == null) { d.queued = true; buildQueue.Add(d); } }

        /// <summary>Samples heights (GroundY raycasts) within the budget; finishes the mesh when all are in.</summary>
        bool Fill(Draped d, int budget)
        {
            int n = d.v.Length;
            while (d.filled < n && budget-- > 0)
            {
                var p = d.v[d.filled]; float y = Mathf.Min(game.GroundY(p.x, p.z), game.TerrainY(p.x, p.z) + 0.3f);   // never above the terrain (no domes over units)
                // ZoneMarkers.cs:89: a surface well above the play floor is a cliff top or rock wall, not zone ground
                if (d.zoneRules && y > 1.5f) d.c[d.filled].a = 0;
                d.v[d.filled] = new Vector3(p.x, y + 0.07f, p.z);
                d.filled++;
            }
            if (d.filled < n) return false;
            if (d.polar) { float R = pulseRadius + 1f; var o = d.v[0]; for (int k = 0; k < n; k++) { float r = Vector2.Distance(new Vector2(d.v[k].x, d.v[k].z), new Vector2(o.x, o.z)); d.c[k].a = 1f - Mathf.Clamp01((r - R * 0.85f) / (R * 0.15f)); } }
            var tris = new List<int>(n * 6);
            int cols = d.polar ? d.nx : d.nx - 1;
            for (int j = 0; j < d.nz - 1; j++)
                for (int i = 0; i < cols; i++)
                {
                    int a = j * d.nx + i, b = j * d.nx + (i + 1) % d.nx, c = a + d.nx, e = b + d.nx;
                    if (d.c[a].a + d.c[b].a + d.c[c].a + d.c[e].a <= 0f) continue;
                    tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(e);
                }
            var m = Own(new Mesh { name = d.polar ? "FlagRing" : "ZoneWash" });
            if (n > 65000) m.indexFormat = IndexFormat.UInt32;
            m.vertices = d.v; m.colors = d.c; m.SetTriangles(tris, 0); m.RecalculateBounds();
            var go = new GameObject(m.name); go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            d.r = go.AddComponent<MeshRenderer>(); d.r.sharedMaterial = fxGround ? fxGround : fx;
            d.r.shadowCastingMode = ShadowCastingMode.Off; d.r.receiveShadows = false; d.r.enabled = false;
            d.mesh = m; d.v = null; d.c = null;
            return true;
        }

        void Ready(Draped d) { if (d != null && d.mesh == null) { buildQueue.Remove(d); d.queued = false; Fill(d, int.MaxValue); } }

        // ================================================================== particles
        ParticleSystem MakeParticles(string name, Material mat, bool isConfetti)
        {
            var go = new GameObject(name); go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 10f; main.loop = true; main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = isConfetti ? 700 : 300;
            main.gravityModifier = isConfetti ? 0.32f : 0.45f;
            main.startSpeed = 0f; main.startLifetime = 1f; main.startSize = 0.1f;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            if (isConfetti)
            {
                r.renderMode = ParticleSystemRenderMode.Billboard;
                var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.limit = 50f; lv.drag = 1.15f;
                var nz = ps.noise; nz.enabled = true; nz.strength = 0.7f; nz.frequency = 0.9f; nz.scrollSpeed = 0.6f; nz.quality = ParticleSystemNoiseQuality.Low;
                var sol = ps.sizeOverLifetime; sol.enabled = true; sol.separateAxes = true;
                var flip = new AnimationCurve(); for (int i = 0; i <= 16; i++) flip.AddKey(i / 16f, i % 2 == 0 ? 1f : 0.18f);   // cards tumbling
                var fade = new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.82f, 1), new Keyframe(1, 0));
                sol.x = new ParticleSystem.MinMaxCurve(1f, flip); sol.y = new ParticleSystem.MinMaxCurve(1f, fade); sol.z = new ParticleSystem.MinMaxCurve(1f, fade);
            }
            else
            {
                r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = 1.6f; r.velocityScale = 0.06f;
                var sol = ps.sizeOverLifetime; sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0)));
            }
            ps.Play();
            return ps;
        }

        /// <summary>
        /// plus team-colour confetti thrown up out of the pole.</summary>
        void Burst(Vector3 at, Team team)
        {
            Color tc = ClothColor(team).gamma, hot = Color.Lerp(tc, Color.white, 0.55f), light = Color.Lerp(tc, Color.white, 0.45f);
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            if (sparks)
                for (int j = 0; j < 10; j++)
                {
                    float a = j * Mathf.PI * 2f / 10f, h = 1.4f + j % 2 * 0.5f;
                    ep.position = at; ep.velocity = new Vector3(Mathf.Cos(a) * h, 0.9f + j % 3 * 0.4f, Mathf.Sin(a) * h) * 1.6f;
                    ep.startLifetime = 0.48f + j % 3 * 0.07f; ep.startSize = 0.14f + j % 2 * 0.04f; ep.startColor = hot;
                    sparks.Emit(ep, 1);
                }
            if (confetti)
                for (int j = 0; j < confettiCount; j++)
                {
                    float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f), spread = UnityEngine.Random.Range(0.6f, 2.4f);
                    ep.position = at + new Vector3(UnityEngine.Random.Range(-0.2f, 0.2f), UnityEngine.Random.Range(-0.1f, 0.4f), UnityEngine.Random.Range(-0.2f, 0.2f));
                    ep.velocity = new Vector3(Mathf.Cos(a) * spread, UnityEngine.Random.Range(4.2f, 6.8f), Mathf.Sin(a) * spread);
                    ep.startLifetime = UnityEngine.Random.Range(1.8f, 2.7f); ep.startSize = UnityEngine.Random.Range(0.09f, 0.15f);
                    ep.rotation = UnityEngine.Random.Range(0f, 360f); ep.angularVelocity = UnityEngine.Random.Range(-540f, 540f);
                    float pick = UnityEngine.Random.value;
                    Color col = pick < 0.68f ? tc * UnityEngine.Random.Range(0.85f, 1.12f) : pick < 0.88f ? light : new Color(1f, 0.83f, 0.38f);
                    col.a = 1; ep.startColor = col;
                    confetti.Emit(ep, 1);
                }
        }

        // ================================================================== frame
        void LateUpdate()
        {
            if (game == null || game.World == null) return;
            float now = Time.time, dt = Time.deltaTime;
            UpdateWind(dt, now);
            if (buildQueue.Count > 0) { int budget = drapeBudget; while (buildQueue.Count > 0 && budget > 0) { var d = buildQueue[0]; int before = d.filled; if (Fill(d, budget)) { buildQueue.RemoveAt(0); d.queued = false; } budget -= Mathf.Max(1, d.filled - before); } }
            for (int i = 0; i < flags.Count; i++)
            {
                var f = flags[i];
                if (f.fort) UpdateFort(f, now, dt); else UpdateZoneFlag(f, now);
                PoseCloth(f, now);
                if (f.ring != null && f.ringT0 >= 0) UpdateRing(f, now);
            }
            for (int i = 0; i < zones.Count; i++) if (zones[i].t0 >= 0) UpdateZoneFx(zones[i], now);
            for (int i = 0; i < pending.Count; i++)
            {
                var p = pending[i]; if (now < p.at) continue;
                pending.RemoveAt(i--);
                if (p.kind == KindBuilding)
                {
                    Fxm()?.AddLight(p.pos + Vector3.up * 3.5f, LightColor(p.team), 9f, 13f, 0.9f, 0.1f, true);
                    RaiseSound(SndBuilding, p.pos, false, 0.7f, p.team, p.old, p.objId, null);
                }
                else Voice(p.id, p.team, p.old, p.objId, now);
            }
        }

        void UpdateWind(float dt, float now)
        {
            Vector4 fw = Shader.GetGlobalVector(FogWindId);
            var wv = new Vector3(fw.x, 0, fw.z);
            if (wv.sqrMagnitude < 0.01f) { var fb = game.level == "L17" ? fallbackWindCity : fallbackWind; wv = new Vector3(fb.x, 0, fb.z); }
            float speed = Mathf.Max(wv.magnitude, 0.01f); windDir = wv / speed;
            windYaw = Mathf.Atan2(-windDir.z, windDir.x) * Mathf.Rad2Deg;                          // local +x (fly end) points downwind
            float s = Mathf.Clamp(speed / 3.35f, 0.25f, 1.6f) * windStrength;                        // desert (3, 1.5) = 1, city (1.2, 0.6) = 0.4
            float gust = Mathf.Clamp01(0.45f + 0.4f * Mathf.Sin(now * 0.23f) * Mathf.Sin(now * 0.61f + 1.3f) + 0.3f * (Mathf.PerlinNoise(now * 0.37f, 7.1f) - 0.5f));
            windPhase += dt * (3.8f + 3.4f * s + 1.8f * gust);
            if (windPhase > Mathf.PI * 2f) windPhase -= Mathf.PI * 2f;                              // integer multiples in the shader: seamless
            Shader.SetGlobalVector(FlagWindId, new Vector4(s, gust, windPhase, now));
        }

        void PoseCloth(Flag f, float now)
        {
            float yaw = windYaw + swayDegrees * (0.7f * Mathf.Sin(now * 0.31f + f.seed) + 0.3f * Mathf.Sin(now * 0.83f + f.seed * 2.1f));
            if (f.blockCos < 1f)
            {   // fort: keep the fly end away from the central turret (62 deg cone)
                float r = yaw * Mathf.Deg2Rad; var dir = new Vector3(Mathf.Cos(r), 0, -Mathf.Sin(r));
                if (Vector3.Dot(dir, f.blockDir) > f.blockCos)
                {
                    float side = Vector3.Cross(f.blockDir, dir).y >= 0 ? 1f : -1f;
                    var edge = Quaternion.Euler(0, side * 62f, 0) * f.blockDir;
                    yaw = Mathf.Atan2(-edge.z, edge.x) * Mathf.Rad2Deg;
                }
            }
            f.pivot.localRotation = Quaternion.Euler(0, yaw, 0);
            var lp = f.cloth.localPosition; if (lp.y != f.y) { lp.y = f.y; f.cloth.localPosition = lp; }
            Upload(f);
        }

        void Upload(Flag f)
        {
            if (f.paint == f.uploadedPaint && Mathf.Abs(f.energy - f.uploadedEnergy) < 0.004f) return;
            var c = f.paint; c.a = Mathf.Clamp01(f.energy);
            for (int i = 0; i < f.colors.Length; i++) f.colors[i] = c;
            f.mesh.colors = f.colors;
            f.uploadedPaint = f.paint; f.uploadedEnergy = f.energy;
        }

        // ================================================================== zone flag capture
        void UpdateZoneFlag(Flag f, float now)
        {
            var owner = f.obj.Owner;
            if (owner != f.shown) BeginCapture(f, owner, now);
            if (f.capT0 < 0) return;
            float t = now - f.capT0;
            if (t < dropTime)
            {   // the old cloth drops down the pole (gravity-like ease in), crumpling
                float k = t / dropTime;
                f.y = Mathf.Lerp(f.dropFrom, f.hoistLow, k * k);
                f.energy = FlutterOnly;                                   // flutters, no glow: the old colour must not light up
                return;
            }
            if (!f.swapped) Raise(f, now);
            float r = t - dropTime;
            // the new cloth springs up (small overshoot, then settles) and snaps open in the wind
            f.y = Mathf.Lerp(f.hoistLow, f.hoistTop, EaseOutBack(Mathf.Clamp01(r / raiseTime)));
            f.energy = Mathf.Exp(-2.9f * r);
            if (r >= celebrateTime) { f.capT0 = -1; f.energy = 0; f.y = f.hoistTop; }
        }

        static float EaseOutBack(float x) { const float c1 = 1.2f, c3 = c1 + 1f; float k = x - 1f; return 1f + c3 * k * k * k + c1 * k * k; }

        void BeginCapture(Flag f, Team to, float now)
        {
            f.from = f.shown; f.to = to; f.shown = to;
            f.capT0 = now; f.dropFrom = f.y; f.swapped = false;
            var top = f.root.position + Vector3.up * f.y;
            Fxm()?.Dust(f.root.position, 0.35f);
            RaiseSound(SndCapture, top, false, 0.8f, to, f.from, f.obj.Id, ClipCapture);              // Zod: touch = capture, same frame
        }

        /// <summary>The bottom of the drop: new colour, burst, ring, zone sweep, building flashes, sounds and voices.</summary>
        void Raise(Flag f, float now)
        {
            f.swapped = true;
            f.paint = ClothColor(f.to);
            var basePos = f.root.position;
            var clothMid = basePos + Vector3.up * (f.hoistLow - f.size.y * 0.5f);
            Burst(clothMid, f.to);
            Fxm()?.AddLight(clothMid + Vector3.up * 0.4f, LightColor(f.to), 7f, 9f, 0.75f, 0.15f, true);
            if (f.ring != null) { Ready(f.ring); f.ringT0 = now; if (f.ring.r) f.ring.r.enabled = true; UpdateRing(f, now); }

            bool gained = f.to == game.player, lost = f.from == game.player && f.to != game.player;
            RaiseSound(SndRaise, basePos + Vector3.up * f.hoistTop, false, 0.72f, f.to, f.from, f.obj.Id, AudioDirector.Has(SndRaise) ? null : ClipRaise);
            if (gained) RaiseSound(SndGained, basePos, true, 0.55f, f.to, f.from, f.obj.Id, ClipGained);                 // RulesView.cs:52 2D .55
            if (lost && f.to != Team.Neutral) RaiseSound(SndZoneLost, basePos, true, 0.6f, f.to, f.from, f.obj.Id, null);   // new sd_zone_lost_sweep
            // (elimination awards every flag of the dead team to neutral at once, zserver.cpp:1675-1729: no 2D spam then)

            ZoneFx zf = null;
            if (f.zone != null && fx)
            {
                zf = GetZone(f.zone);
                if (zf != null) StartSweep(zf, f, now);
            }
            // linked buildings flash (light + bld_building_captured) when the sweep front reaches them, at least 80 ms apart
            // (sound bible); bridges never change owner (ZWorld.Rules.cs SetOwnerZod) and are skipped
            bool radar = false; links.Clear();
            foreach (var b in f.obj.LinkedObjects)
            {
                if (b == null || b.IsBridge) continue;
                if (b.Type == "radar") radar = true;
                var p = game.CenterWorld(b);
                float dist = new Vector2(p.x - basePos.x, p.z - basePos.z).magnitude;
                float maxR = zf != null ? zf.maxR : Mathf.Max(dist, 1f);
                float k = 1f - Mathf.Sqrt(Mathf.Clamp01(1f - dist / Mathf.Max(maxR, 0.01f)));           // inverse of the ease-out front
                int at = links.Count; links.Add((sweepTime * k, b.Id, p));
                while (at > 0 && links[at - 1].delay > links[at].delay) { var tmp = links[at - 1]; links[at - 1] = links[at]; links[at] = tmp; at--; }
            }
            float lastAt = -1f;
            foreach (var l in links)
            {
                float d = Mathf.Max(l.delay, lastAt + 0.08f); lastAt = d;
                Schedule(new Pending { at = now + d, kind = KindBuilding, pos = l.pos, team = f.to, old = f.from, objId = l.id });
            }
            // Rachel (Zod: capturer hears "territory taken", old owner "territory lost", new owner "radar activated")
            if (f.to != Team.Neutral)
            {
                if (gained) Schedule(new Pending { at = now + 0.6f, kind = KindVoice, id = SndCaptured, team = f.to, old = f.from, objId = f.obj.Id });
                if (lost) Schedule(new Pending { at = now + 0.35f, kind = KindVoice, id = SndLost, team = f.to, old = f.from, objId = f.obj.Id });
                if (gained && radar) Schedule(new Pending { at = now + 2.2f, kind = KindVoice, id = SndRadar, team = f.to, old = f.from, objId = f.obj.Id });
            }
            Captured?.Invoke(new CaptureInfo { flag = f.obj, zone = f.zone, from = f.from, to = f.to, position = basePos });
        }

        void Schedule(Pending p) { if (pending.Count < 64) pending.Add(p); }

        void UpdateRing(Flag f, float now)
        {
            var d = f.ring; if (d.r == null) return;
            float t = now - f.ringT0;
            if (t > 2.3f) { d.r.enabled = false; f.ringT0 = -1; return; }
            var o = f.root.position;
            mpb.Clear();
            mpb.SetFloat(ModeId, 0); mpb.SetColor(NewColorId, LightColor(f.to)); mpb.SetFloat(GlowId, fxGlow);   // light, not paint: reads on sand
            mpb.SetVector(CenterId, new Vector4(o.x, o.y, o.z, pulseRadius + 1f));
            mpb.SetVector(AnimId, new Vector4(t, sweepTime, captureRingRadius, 0));
            d.r.SetPropertyBlock(mpb);
        }

        void StartSweep(ZoneFx z, Flag f, float now)
        {
            Ready(z.wash);
            z.from = z.t0 >= 0 ? z.to : f.from; z.to = f.to; z.t0 = now;
            z.centre = f.root.position;
            float x0 = z.zone.X / 8f, x1 = (z.zone.X + z.zone.W) / 8f, zt = -z.zone.Y / 8f, zb = -(z.zone.Y + z.zone.H) / 8f;
            float dx = Mathf.Max(Mathf.Abs(z.centre.x - x0), Mathf.Abs(z.centre.x - x1)), dz = Mathf.Max(Mathf.Abs(z.centre.z - zt), Mathf.Abs(z.centre.z - zb));
            z.maxR = Mathf.Sqrt(dx * dx + dz * dz) + 0.5f;
            if (z.wash.r) z.wash.r.enabled = true;
            if (!z.markerLooked) FindBorder(z);
            if (z.border && z.marker)
            {   // borders are off by default (ZoneMarkers.ShowBorders): then the band shows only for the sweep and fades out
                if (!z.border.enabled) z.markerWasOn = z.marker.enabled;
                z.marker.enabled = false; z.border.enabled = true;
            }
            UpdateZoneFx(z, now);
        }

        /// <summary>The zone's ZoneMarkers band ("ZoneBorder" + id, ZoneMarkers.cs:36): a copy shares its mesh and sweeps the
        /// colour; the original is hidden during the sweep and restored to its own state after (ZoneMarkers.ShowBorders is off by
        /// default, so usually the band only exists for the sweep; when shown it already has the new colour, ZoneMarkers.cs:46-58).</summary>
        void FindBorder(ZoneFx z)
        {
            z.markerLooked = true;
            if (!markersLooked) { markers = FindFirstObjectByType<ZoneMarkers>(); markersLooked = true; }
            if (!markers) return;
            var t = markers.transform.Find("ZoneBorder" + z.zone.Id);
            var src = t ? t.GetComponent<MeshRenderer>() : null; var mf = t ? t.GetComponent<MeshFilter>() : null;
            if (!src || !mf || !mf.sharedMesh) return;
            var go = new GameObject("BorderSweep"); go.transform.SetParent(t, false);
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = fxGround ? fxGround : fx; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.enabled = false;
            z.marker = src; z.border = r;
        }

        void UpdateZoneFx(ZoneFx z, float now)
        {
            float t = now - z.t0;
            bool washOn = t <= sweepTime + 1.05f || groundTintHold > 0f;
            bool borderOn = z.markerWasOn ? t <= sweepTime : t <= sweepTime + 0.9f;
            float lineFade = z.markerWasOn ? 1f : Mathf.Clamp01(t / 0.15f) * (1f - Mathf.SmoothStep(0f, 1f, (t - sweepTime) / 0.9f));
            var c = z.centre;
            if (z.wash.r)
            {
                z.wash.r.enabled = washOn;
                if (washOn)
                {
                    mpb.Clear();
                    mpb.SetFloat(ModeId, 1); mpb.SetColor(NewColorId, TeamColors.Of(z.to)); mpb.SetFloat(GlowId, fxGlow);
                    mpb.SetVector(CenterId, new Vector4(c.x, c.y, c.z, z.maxR));
                    mpb.SetVector(AnimId, new Vector4(t, sweepTime, captureRingRadius, groundTintHold));
                    z.wash.r.SetPropertyBlock(mpb);
                }
            }
            if (z.border)
            {
                z.border.enabled = borderOn;
                if (z.marker) z.marker.enabled = !borderOn && z.markerWasOn;
                if (borderOn)
                {
                    mpb.Clear();
                    mpb.SetFloat(ModeId, 2); mpb.SetFloat(GlowId, fxGlow);
                    mpb.SetColor(NewColorId, LineColor(z.to)); mpb.SetColor(OldColorId, LineColor(z.from));
                    mpb.SetVector(CenterId, new Vector4(c.x, c.y, c.z, z.maxR));
                    mpb.SetVector(AnimId, new Vector4(t, sweepTime, captureRingRadius, lineFade));
                    z.border.SetPropertyBlock(mpb);
                }
            }
            if (!washOn && !borderOn) z.t0 = -1;
        }

        static Color LineColor(Team t) { var c = TeamColors.Paint(t); c.a = t == Team.Neutral ? LineAlpha * NeutralLineK : LineAlpha; return c; }

        // ================================================================== fort flag
        void UpdateFort(Flag f, float now, float dt)
        {
            var o = f.obj;
            bool gone = o.Destroyed || !f.fortView || !f.fortView.activeInHierarchy;
            if (gone)
            {   // the fort dies (nuke / collapse): the cloth drops and the flag is gone with the building
                if (f.deadT < 0) f.deadT = now;
                float k = Mathf.Clamp01((now - f.deadT) / 0.3f);
                f.y = Mathf.Lerp(f.y, f.hoistLow, k); f.energy = FlutterOnly;
                if (k >= 1f && f.root.gameObject.activeSelf) f.root.gameObject.SetActive(false);
                return;
            }
            if (f.deadT >= 0) { f.deadT = -1; f.root.gameObject.SetActive(true); }
            if (o.Owner != f.shown) { f.shown = o.Owner; f.paint = ClothColor(o.Owner); }

            if (o.Health < f.lastHealth) { f.hitUntil = now + 4f; f.energy = Mathf.Max(f.energy, FlutterOnly); }   // hit: flinch (no glow)
            f.lastHealth = o.Health;
            if (now >= f.nextCheck) { f.nextCheck = now + FortCheck; f.nearEnemy = EnemyNearFort(f); }
            bool danger = f.nearEnemy || now < f.hitUntil;
            if (danger) f.calmSince = -1; else if (f.calmSince < 0) f.calmSince = now;
            bool dipping = danger || (f.calmSince >= 0 && now - f.calmSince < FortLevelDelay);
            f.dip = Mathf.MoveTowards(f.dip, dipping ? 1f : 0f, dt * (dipping ? 1.4f : 0.6f));   // lowers briskly, rises slowly
            float ease = f.dip * f.dip * (3f - 2f * f.dip);
            float tug = dipping ? 0.05f * Mathf.Sin(now * 5.3f + f.seed) : 0f;                    // rope tugs while in danger
            f.y = f.hoistTop - ease * fortDipDepth * (f.hoistTop - f.hoistLow) + tug * ease;
            f.energy = Mathf.MoveTowards(f.energy, FlutterOnly * ease, dt * 1.2f);           // nervous flutter while dipped, never a glow

            // Zod's warning for the player's own fort: after 3 s of danger, then every 10 s (zplayer.cpp:1087-1096)
            // Zod: after 3 s of danger, then every 10 s (zplayer.cpp:1087-1096)
            if (o.Owner == game.player && (f.nearEnemy || FortAlert.HitRecently))                        // combat_nb_v1 NB31: FortAlert decides (real hits only)
            {
                if (f.nearSince < 0) f.nearSince = now;
                int fired = FortAlert.Report(true);
                if (fired != fortSeen)
                {
                    fortSeen = fired;
                    Schedule(new Pending { at = now, kind = KindVoice, id = SndFortAttack, team = o.Owner, old = o.Owner, objId = o.Id });
                }
            }
            else f.nearSince = -1;
        }

        bool EnemyNearFort(Flag f)
        {
            var fort = f.obj; const int r2 = FortDangerPx * FortDangerPx;
            foreach (var o in game.World.Objects)
            {
                if (o.Owner == Team.Neutral || o.Owner == fort.Owner) continue;
                if (o.Kind == ZKind.Building || o.Kind == ZKind.MapItem || o.Destroyed) continue;
                int dx = o.CenterX - f.fortCentre.x, dy = o.CenterY - f.fortCentre.y;
                if (dx * dx + dy * dy <= r2) return true;
            }
            return false;
        }

        // ================================================================== sound
        /// <summary>audio_v2: the same cue for the same flag / building at most every CueDebounce s (a contested flag ping-pongs every
        /// 0.2 s: T19 zone 1 flipped 14 times in 2.7 s). The visuals keep every flip.</summary>
        public static float CueDebounce = 1.5f;
        static readonly Dictionary<(string, int), float> cueLast = new Dictionary<(string, int), float>();
        void RaiseSound(string id, Vector3 pos, bool is2D, float vol, Team team, Team old, int objId, string[] clips)
        {
            float nowCue = Time.unscaledTime;
            if (cueLast.TryGetValue((id, objId), out var lastCue) && nowCue - lastCue < CueDebounce) return;
            cueLast[(id, objId)] = nowCue;
            Sound?.Invoke(new SoundEvent { id = id, position = pos, is2D = is2D, volume = vol, team = team, oldTeam = old, objectId = objId });
            if (!BuiltInSounds || clips == null) return;
            var au = CombatAudio.I; if (!au) return;
            if (is2D) au.PlayWide(id, pos, vol, clips);                        // world cue: positional, never culled (was 2D)
            else au.Play(id, pos, vol, 0.15f, objId, clips);
        }

        void Voice(string id, Team team, Team old, int objId, float now)
        {
            if (now < nextVoice) { Schedule(new Pending { at = nextVoice, kind = KindVoice, id = id, team = team, old = old, objId = objId }); return; }
            nextVoice = now + voiceGap;
            Sound?.Invoke(new SoundEvent { id = id, position = Vector3.zero, is2D = true, volume = 1f, team = team, oldTeam = old, objectId = objId });
            if (!BuiltInSounds) return;
            var au = CombatAudio.I; if (!au) return;
            // Rachel's own filtered source (CombatAudio.Voice); sound bible cooldowns: captured 6 s, lost 3 s; the fort warning
            // repeats every 10 s (UpdateFort)
            if (id == SndCaptured) au.Voice(id, ClipCaptured, 1f, 6f, false);
            else if (id == SndLost) au.Voice(id, ClipLost, 1f, 3f, false);
            else if (id == SndRadar) au.Voice(id, ClipRadar, 1f, 0f, false);
            else if (id == SndFortAttack) au.Voice(id, ClipFort, 1f, 0f, false);
        }

        // ================================================================== queries (AudioDirector: bld_flag_flap loops near flags)
        public int FlagCount => flags.Count;
        public bool IsFortFlag(int i) => flags[i].fort;
        /// <summary>World centre of flag i's cloth; false when that flag is hidden (destroyed fort).</summary>
        public bool ClothCentre(int i, out Vector3 p)
        {
            var f = flags[i]; p = f.cloth.position + f.cloth.rotation * new Vector3(f.size.x * 0.5f, -f.size.y * 0.5f, 0);
            return f.root.gameObject.activeInHierarchy;
        }

        // ================================================================== test hooks (Inspector context menu, or an editor script)
        /// <summary>Visual + sound replay of a capture on zone flag `index` (0-based), ending in the sim's real owner.</summary>
        public void ReplayCapture(int index)
        {
            int n = 0;
            foreach (var f in flags)
            {
                if (f.fort) continue;
                if (n++ != index) continue;
                var real = f.obj.Owner;
                f.shown = real == Team.Red ? Team.Blue : Team.Red;                  // pretend it was the other team's
                f.paint = ClothColor(f.shown); f.uploadedEnergy = -1;
                BeginCapture(f, real, Time.time);
                return;
            }
        }

        [ContextMenu("Replay capture on zone flag 0")] void ReplayCapture0() => ReplayCapture(0);
        [ContextMenu("Fort flags: 6 s of danger (dip)")] void TestFortDip() => ForceFortDanger(6f);

        /// <summary>Both fort flags dip as if the forts were hit for `seconds`.</summary>
        public void ForceFortDanger(float seconds) { foreach (var f in flags) if (f.fort) f.hitUntil = Time.time + seconds; }

        /// <summary>One-line state for tests and the build log.</summary>
        public string Diagnostics()
        {
            int zf = 0, ff = 0, busy = 0, built = 0;
            foreach (var f in flags) { if (f.fort) ff++; else zf++; if (f.capT0 >= 0) busy++; if (f.ring != null && f.ring.mesh) built++; }
            int zb = 0; foreach (var z in zones) if (z.wash.mesh) zb++;
            return $"flags {zf} zone + {ff} fort, capturing {busy}, rings built {built}, zone washes {zb}/{zones.Count}, queue {buildQueue.Count}, fort cut: {fortCutLog}, wind {Shader.GetGlobalVector(FlagWindId)}";
        }
    }
}
