using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// factories, L17 street lights). View side only, the Sim is never touched. Auto-boots in every level scene (ZGame).
    /// <list type="bullet">
    /// <item><b>Building rigs</b> (Resources/OpenZ/Night/night_rigs.json, placed per building type in fractions of the
    /// model bounds, the FactoryView frame): sweeping searchlights (fort towers on wall brackets, factory roofs; real spot +
    /// beam cone + pool, sweep / figure-8 / scan patterns, they catch and follow enemy units for a few seconds), gate /
    /// door / wall floodlights, uplights on the fort gun and the radar dish, amber rotating work beacons while a factory
    /// produces or the repair bay repairs, the repair arc flicker, a team LED beacon on every searchlight bracket.
    /// FactoryView (door lamps, windows, strip) and BuildingAnimView (radar / repair beacons) keep theirs: no duplicates.
    /// Team-owned: lights burn only for an owner, neutral = dark, capture = a short blackout then the new team's tint,
    /// destroyed = sparks + out (hidden), revived = back on.</item>
    /// <item><b>Lamps</b>: every DEC_*lamp* prop of the environment (L17 round13 street lamps on the Zod lamp tiles, L02
    /// service lamps) plus the new posts of Resources/OpenZ/Night/&lt;level&gt;_night.json (L17 streets, quays, car parks;
    /// bridge ends on both levels), cloned from the level's own lamp prop. Warm spot light, fog glow cone, halo, emissive
    /// glass, sodium warm-up flicker at dusk, some faulty flickering lamps. Destructible: explosions bend or topple them,
    /// vehicles driving into a post knock it over (sparks, glass, light out, the pole bends / falls, dust, clang).</item>
    /// <item><b>Timing</b>: lights switch on at dusk with a stagger (lamps sweep west to east from t 0.335, floods 0.345,
    /// searchlights 0.372 tower after tower) and off at dawn (0.885-0.905).</item>
    /// <item><b>Budget</b> (NightLightBudget): max real lights near the camera, shadows for a few searchlights, heat and
    /// frame-time aware; the rest render emissive heads, beams and projected pool impostors.</item>
    /// <item><b>Hooks</b>: Atmosphere.FogLampSource (lit lamps glow in the volumetric fog), PropBreakables.ClaimBreak
    /// (lamps break here, not as generic props), DayNight.ExternalLamps (DayNight makes no lamp lights of its own),
    /// AudioEvent (sound track), NightGrade tuning knobs.</item>
    /// </list>
    /// Debug: F9 cycles day / dusk / blue hour / night / dawn, Shift+F9 freezes the clock, Ctrl+F9 logs Status();
    /// </summary>
    [DefaultExecutionOrder(90)]
    public class NightLights : MonoBehaviour
    {
        public static NightLights I { get; private set; }
        /// <summary>Set false (e.g. in ZGame.Awake) to add NightLights by hand.</summary>
        public static bool AutoBoot = true;
        /// <summary>Sound hook (key, position): lamp_spark, lamp_glass, lamp_fall, lamp_land, light_fizzle, searchlight_on.
        /// While anything is subscribed no default sound plays.</summary>
        public static event System.Action<string, Vector3> AudioEvent;

        [Header("Night grade (live; pushed into NightGrade)")]
        [Range(0.3f, 2f)] public float moonScale = 1.65f;
        [Range(0.3f, 2f)] public float ambientScale = 1.6f;
        [Range(-1.5f, 1f)] public float nightExposure = 0.2f;
        [Range(-30f, 40f)] public float nightContrast = 14f;
        [Range(-60f, 20f)] public float nightSaturation = -12f;
        [Range(-40f, 20f)] public float nightTemperature = -9f;
        [Range(0.5f, 1.5f)] public float nightBloomThreshold = 0.95f;
        [Range(0f, 3f)] public float nightBloomIntensity = 1.25f;
        [Range(0f, 0.5f)] public float nightVignette = 0.24f;
        [Range(0f, 1f)] public float moonShadowStrength = 0.62f;
        [Range(0f, 3f)] public float rimStrength = 2.4f;
        [Range(0f, 2f), Tooltip("Moonlit fill on units in their own colour (TeamLit _OZNightFill)")] public float unitFill = 1f;

        [Header("Budget")]
        [Range(4, 64)] public int maxRealLights = 40;
        [Range(0, 8)] public int maxShadowed = 3;
        public bool beams = true, pools = true, halos = true;
        [Range(0f, 3f)] public float lightGain = 0.6f, beamGain = 1f, poolGain = 0.22f, haloGain = 0.35f;
        [Range(0f, 6f)] public float lampPoolBoost = 1.0f;   // street lamps: soft warm pool (tuned in the L17 Game view; 2.4 blew out to discs)   // 1/1/1 blew the floods out to white blobs
        // ground_lights_fix: extra REAL-light gain per kind on top of lightGain (impostor pools, beams and halos unchanged). URP falls
        // off as 1/d^2 x N.L: at lightGain 0.6 the fort searchlight pool (7.9 m up, 17.7 m out) got 0.4x the moonlit sand, a 4.5 m
        // street lamp 0.8x under itself, so a granted real light showed no pool at all. Floods (2.6-6.5x already) stay at 1.
        [Tooltip("Real-light gain x lightGain for street / service lamps (0.6 x 2.5 = 1.5)")]
        [Range(0f, 10f)] public float lampLightGain = 2.5f;
        [Tooltip("Real-light gain x lightGain for searchlights (0.6 x 5 = 3.0: fort pool 2.2x, factory 5-7x the moonlit ground)")]
        [Range(0f, 10f)] public float searchLightGain = 5f;

        [Header("Lamps")]
        public bool knockLamps = true;
        [Range(0.5f, 2.5f)] public float knockRadius = 1.25f;

        [Header("Debug")]
        public bool debugKeys = true;

        public int RigCount => rigs.Count;
        public int FixtureCount => fixtures.Count;
        public NightLightBudget Budget => budget;
        public IReadOnlyList<NightFixture> Fixtures => fixtures;

        ZGame game; DayNight dayNight; Camera cam; RtsCamera rts; Transform env;
        string level = "L02"; float mapW = 144f;
        NightRigsFile specs; NightLevelSpec levelSpec; NightLevelFile levelFile;
        readonly List<NightRig> rigs = new List<NightRig>();
        readonly List<NightFixture> fixtures = new List<NightFixture>();
        readonly List<NightFixture> lamps = new List<NightFixture>();
        readonly Dictionary<GameObject, NightFixture> lampByGO = new Dictionary<GameObject, NightFixture>();
        readonly Dictionary<long, List<NightFixture>> lampCells = new Dictionary<long, List<NightFixture>>();
        readonly NightLightBudget budget = new NightLightBudget();
        MaterialPropertyBlock mpb;
        float nextKnock; int lastBlastFrame = -1; Vector3 lastBlastPos;
        int newLamps, clonedLamps;
        readonly System.Random rnd = new System.Random(4242);

        static readonly float[] Presets = { 0.08f, 0.33f, 0.43f, 0.62f, 0.895f };
        static readonly string[] PresetNames = { "day", "dusk", "blue hour", "night", "dawn" };
        const int MaxFog = 12;

        // ------------------------------------------------------------------ boot
        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Early()
        {
            DayNight.ExternalLamps = AutoBoot;                                                   // DayNight.OnEnable runs after this
            if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => TryAttach(); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => TryAttach();

        /// <summary>Level scenes only (ZGame present). Elsewhere (menu previews) DayNight gets its own lamp lights back.</summary>
        public static void TryAttach()
        {
            if (!AutoBoot) return;
            var g = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (!g)
            {
                // Leaving ExternalLamps false (the old code) made a level loaded after a menu build DayNight point lights on
                // its lamp props, and every cloned street post copied one of them as an unmanaged light.
                foreach (var dn in FindObjectsByType<DayNight>(FindObjectsSortMode.None))
                {
                    if (!dn.isActiveAndEnabled || !relit.Add(dn.GetInstanceID())) continue;
                    DayNight.ExternalLamps = false;
                    dn.enabled = false; dn.enabled = true;
                }
                DayNight.ExternalLamps = true;
                return;
            }
            DayNight.ExternalLamps = true;
            if (!FindFirstObjectByType<NightLights>()) new GameObject("NightLights").AddComponent<NightLights>();
        }
        static readonly HashSet<int> relit = new HashSet<int>();

        // Built in Awake (added from sceneLoaded, after every scene Awake, before any Start): the cloned lamp posts exist
        // before PropBreakables.Start and AudioDirector collect the environment's lamps.
        void Awake()
        {
            if (I && I != this) { Destroy(gameObject); return; }
            I = this;
            mpb = new MaterialPropertyBlock();
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            dayNight = FindFirstObjectByType<DayNight>();
            cam = Camera.main; rts = cam ? cam.GetComponent<RtsCamera>() : null; if (!rts) rts = FindFirstObjectByType<RtsCamera>();
            if (rts) mapW = Mathf.Max(20f, rts.mapSizeMeters.x);
            var e = GameObject.Find("Environment"); env = e ? e.transform : dayNight ? dayNight.lampRoot : null;
            if (game == null || game.World == null) { Debug.LogWarning("[OpenZ] NightLights: no ZGame world"); enabled = false; return; }
            level = game.level;
            LoadData();
            Physics.SyncTransforms();
            BuildRigs();
            BuildLamps();
            Atmosphere.FogLampSource = FillFogLamps;
            PropBreakables.ClaimBreak = ClaimProp;
            FxManager.Exploded += OnBlastLegacy;
            OpenZFxPlayer.Exploded += OnBlastM12;
            int search = 0; foreach (var f in fixtures) if (f.Kind == NightKind.Searchlight) search++;
            Debug.Log($"[OpenZ] NightLights {level}: {rigs.Count} building rigs, {fixtures.Count - lamps.Count} building lights ({search} searchlights), " +
                      $"{lamps.Count} lamps ({lamps.Count - newLamps} props + {newLamps} new posts, {clonedLamps} cloned), budget {maxRealLights} real / {maxShadowed} shadowed");
        }

        void OnDestroy()
        {
            FxManager.Exploded -= OnBlastLegacy;
            OpenZFxPlayer.Exploded -= OnBlastM12;
            if (I != this) return;
            I = null;
            Atmosphere.FogLampSource = null;
            PropBreakables.ClaimBreak = null;
        }

        void LoadData()
        {
            var ta = Resources.Load<TextAsset>("OpenZ/Night/night_rigs");
            specs = ta ? JsonUtility.FromJson<NightRigsFile>(ta.text) : null;
            if (specs == null) Debug.LogWarning("[OpenZ] NightLights: Resources/OpenZ/Night/night_rigs.json missing (README_IMPORT.md step 1)");
            var lt = Resources.Load<TextAsset>($"OpenZ/Night/{level}_night");
            levelFile = lt ? JsonUtility.FromJson<NightLevelFile>(lt.text) : null;
            levelSpec = null;
            if (specs?.levels != null) foreach (var l in specs.levels) if (l != null && l.level == level) levelSpec = l;
            if (levelSpec == null) levelSpec = new NightLevelSpec { level = level, lampTemplate = level == "L17" ? "round13_warm_street_lamp" : "Service_lamp" };
            NightGrade.HazeBoost = levelSpec.haze - 1f;
        }

        NightRigSpec RigSpec(string key)
        {
            if (specs?.rigs == null) return null;
            foreach (var r in specs.rigs) if (r != null && r.key == key) return r;
            return null;
        }

        NightLampType LampTypeFor(string name)
        {
            NightLampType fallback = null;
            if (specs?.lampTypes != null)
                foreach (var t in specs.lampTypes)
                {
                    if (t == null) continue;
                    if (t.key != "procedural" && name.IndexOf(t.key, System.StringComparison.OrdinalIgnoreCase) >= 0) return t;
                    if (t.key == levelSpec.lampTemplate) fallback = t;
                    if (fallback == null && t.key == "procedural") fallback = t;
                }
            return fallback ?? new NightLampType { key = "procedural", color = new[] { 1f, 0.7f, 0.4f }, intensity = 15, range = 12, angle = 118, pitch = 90,
                cone = 1, coneLen = 2.6f, coneAngle = 52, pool = 1, priority = 2, glowNames = new[] { "glow", "diffuser" }, flickerShare = 0.1f };
        }

        // ------------------------------------------------------------------ helpers
        static Color ToColor(float[] c) => c != null && c.Length >= 3 ? new Color(c[0], c[1], c[2], 1f) : Color.white;
        static float Hash(int n) { unchecked { uint x = (uint)n * 747796405u + 2891336453u; x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u; return ((x >> 22) ^ x) / 4294967295f; } }
        float GroundY(float x, float z) => game ? game.GroundY(x, z) : 0f;
        Vector3 Focus() => rts ? rts.focus : cam ? cam.transform.position : Vector3.zero;

        /// <summary>Model bounds in the building root's local space (MeshFilter bounds through their transforms) = FactoryView's frame.</summary>
        static Bounds LocalBounds(Transform root)
        {
            bool any = false; var b = new Bounds();
            var w2l = root.worldToLocalMatrix;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh; if (!m) continue;
                var mbb = m.bounds; var M = w2l * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mbb.center + Vector3.Scale(mbb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = M.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        static readonly System.Reflection.FieldInfo TierField = typeof(UniversalAdditionalLightData).GetField("m_AdditionalLightsShadowResolutionTier",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Light MakeLight(Transform parent, string name, LightType type, Vector3 localPos, Quaternion localRot, NightFixture f)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = localPos; go.transform.localRotation = localRot;
            var l = go.AddComponent<Light>();
            l.type = type; l.range = Mathf.Max(0.5f, f.Range); l.color = f.Col; l.intensity = 0f; l.enabled = false;
            l.shadows = LightShadows.None; l.shadowStrength = 0.88f; l.renderMode = LightRenderMode.ForcePixel;
            if (type == LightType.Spot) { l.spotAngle = Mathf.Clamp(f.Angle, 1f, 179f); l.innerSpotAngle = l.spotAngle * (f.Kind == NightKind.Searchlight ? 0.35f : 0.45f); }
            // cone, was 55 %) and no shadows from the fixture's own mount / tower parapet right in front of the lens
            l.shadowNearPlane = f.Kind == NightKind.Searchlight ? 1.5f : f.Kind == NightKind.Lamp ? 0.5f : f.Kind == NightKind.Flood || f.Kind == NightKind.WorkLight ? 0.6f : 0.4f;
            var ald = l.GetUniversalAdditionalLightData();
            // the searchlights' shadow maps at URP's "medium" tier (512 px) instead of the default "high" (1024): the setter is
            // not public in URP 17, the serialized field is (same as picking Medium in the light's inspector)
            try { if (ald && TierField != null) TierField.SetValue(ald, UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium); } catch { }
            return l;
        }

        float GroundHit(Vector3 origin, Vector3 dir, float range)
        {
            if (dir.y > -0.03f) return range;
            float s = (origin.y - GroundY(origin.x, origin.z)) / -dir.y;
            for (int i = 0; i < 2; i++) { var p = origin + dir * s; s = (origin.y - GroundY(p.x, p.z)) / -dir.y; }
            return Mathf.Clamp(s, 0.5f, range);
        }

        /// <summary>Place a pool impostor under a light ray (centre where it meets the ground; ellipse stretched by the slant).</summary>
        void PlacePool(NightFixture f, Vector3 origin, Vector3 dir, float s)
        {
            if (!f.PoolT) return;
            var c = origin + dir * s; float gy = GroundY(c.x, c.z);
            float half = Mathf.Tan(Mathf.Clamp(f.Angle * 0.5f, 5f, 65f) * Mathf.Deg2Rad);
            float w = Mathf.Clamp(2f * s * half * (f.Angle > 90f ? 0.7f : 1.2f), 1.2f, f.Range * 1.3f);   // wide lamps: the bright core, not the cut-off
            float slant = Mathf.Max(0.3f, -dir.y);
            float len = Mathf.Min(w / slant, f.Range * 1.6f);
            var hz = new Vector3(dir.x, 0, dir.z);
            var rot = hz.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(hz.normalized, Vector3.up) : Quaternion.identity;
            f.PoolT.SetPositionAndRotation(new Vector3(c.x, gy + 0.35f, c.z), rot);
            f.PoolT.localScale = new Vector3(w, 3.8f, dir.y < -0.9f ? w : len);
            float d = Mathf.Max(1f, (c - origin).magnitude);
            f.StaticPoolE = f.Intensity / (d * d);
        }

        Renderer PoolRenderer(NightFixture f)
        {
            if (f.PoolK <= 0f) return null;
            var go = NightMeshes.Part("Pool_" + f.Id, transform, NightMeshes.Cube(), NightMeshes.Pool(), false, Vector3.zero, Quaternion.identity, Vector3.one);
            f.PoolT = go.transform;
            var r = go.GetComponent<Renderer>(); r.enabled = false;
            return r;
        }

        // ------------------------------------------------------------------ building rigs
        void BuildRigs()
        {
            foreach (var o in game.World.Objects)
            {
                if (o.Kind != ZKind.Building) continue;
                string key = o.Type == "fort_front" || o.Type == "fort_back" ? "fort" : o.Type;
                var spec = RigSpec(key); if (spec == null || spec.elements == null) continue;
                if (!game.StaticViews.TryGetValue(o.Id, out var go) || !go) continue;
                var rig = new NightRig { B = o, Key = key, LastOwner = o.Owner };
                var root = new GameObject($"NightRig_{o.Type}_{o.Id}").transform; root.SetParent(transform, false);
                root.SetPositionAndRotation(go.transform.position, Quaternion.Euler(0, go.transform.eulerAngles.y, 0));
                rig.Root = root; rig.MB = LocalBounds(go.transform);
                if (rig.MB.size.x < 0.5f || rig.MB.size.y < 0.5f) { Debug.LogWarning($"[OpenZ] NightLights: no model bounds on {go.name}"); Destroy(root.gameObject); continue; }
                if (spec.boundsMin != null && spec.boundsMax != null && spec.boundsMin.Length >= 3 && spec.boundsMax.Length >= 3)
                {   // the mount points are fractions of the measured GLB bounds: a model swap / rescale moves every light
                    var want = new Vector3(spec.boundsMax[0] - spec.boundsMin[0], spec.boundsMax[1] - spec.boundsMin[1], spec.boundsMax[2] - spec.boundsMin[2]);
                    var got = rig.MB.size;
                    if (Mathf.Abs(got.x / want.x - 1f) > 0.12f || Mathf.Abs(got.y / want.y - 1f) > 0.12f || Mathf.Abs(got.z / want.z - 1f) > 0.12f)
                        Debug.LogWarning($"[OpenZ] NightLights: {go.name} model bounds {got} differ from the measured {want} (night_rigs.json): check the light mounts, rerun plan_night.py");
                }
                int idx = 0;
                foreach (var e in spec.elements)
                {
                    if (e == null || e.p == null || e.p.Length < 3) continue;
                    var f = BuildElement(rig, e, idx++);
                    if (f == null) continue;
                    rig.Fixtures.Add(f); fixtures.Add(f);
                }
                ApplyTeamTint(rig);
                rigs.Add(rig);
            }
        }

        static NightKind ParseKind(string k) => k switch
        {
            "searchlight" => NightKind.Searchlight, "uplight" => NightKind.Uplight, "work_beacon" => NightKind.WorkBeacon,
            "work_light" => NightKind.WorkLight, "weld" => NightKind.Weld, _ => NightKind.Flood,
        };

        NightFixture BuildElement(NightRig rig, NightElementSpec e, int idx)
        {
            var local = rig.MB.min + Vector3.Scale(new Vector3(e.p[0], e.p[1], e.p[2]), rig.MB.size);
            var f = new NightFixture
            {
                Kind = ParseKind(e.kind), Id = $"{rig.B.Type}_{rig.B.Id}_{e.id}", Rig = rig, Spec = e,
                Col = ToColor(e.color), Intensity = e.intensity, Range = e.range, Angle = e.angle, Priority = e.priority,
                ShadowCandidate = e.shadow, BeamK = e.beam, PoolK = e.pool, When = string.IsNullOrEmpty(e.when) ? "owned" : e.when,
                Seed = Hash(rig.B.Id * 97 + idx * 13 + 7),
            };
            f.ColLinear = f.Col.linear;
            var root = new GameObject(e.id).transform; root.SetParent(rig.Root, false);
            root.localPosition = local; root.localRotation = Quaternion.Euler(0, e.yaw, 0);
            f.Root = root;
            switch (f.Kind)
            {
                case NightKind.Searchlight: BuildSearchlight(f, e); break;
                case NightKind.WorkBeacon: BuildWorkBeacon(f, e); break;
                case NightKind.Weld: BuildWeld(f, e); break;
                default: BuildFlood(f, e); break;
            }
            float j = Hash(rig.B.Id * 131 + idx);
            if (f.Kind == NightKind.Searchlight) { f.OnAt = 0.372f + e.onDelay + 0.004f * j; f.OffAt = 0.885f + 0.006f * j; }
            else { f.OnAt = 0.345f + e.onDelay + 0.008f * j; f.OffAt = 0.895f + 0.008f * j; }
            return f;
        }

        void BuildSearchlight(NightFixture f, NightElementSpec e)
        {
            var H = NightMeshes.Housing();
            NightMeshes.Part("Mount", f.Root, NightMeshes.SearchMount(Mathf.Round(e.arm * 100f) / 100f), H, true, Vector3.zero, Quaternion.identity, Vector3.one);
            f.Yaw = new GameObject("Yaw").transform; f.Yaw.SetParent(f.Root, false);
            NightMeshes.Part("Yoke", f.Yaw, NightMeshes.SearchYoke(), H, true, Vector3.zero, Quaternion.identity, Vector3.one);
            f.Pitch = new GameObject("Pitch").transform; f.Pitch.SetParent(f.Yaw, false); f.Pitch.localPosition = new Vector3(0, 0.02f, 0);
            NightMeshes.Part("Drum", f.Pitch, NightMeshes.SearchDrum(), H, true, Vector3.zero, Quaternion.identity, Vector3.one);
            NightMeshes.Part("Glass", f.Pitch, NightMeshes.Disc(), NightMeshes.DarkGlass(), false, new Vector3(0, 0, 0.335f), Quaternion.identity, new Vector3(0.31f, 0.31f, 1f));
            f.Lens = NightMeshes.Part("Lens", f.Pitch, NightMeshes.Disc(), NightMeshes.Lens(), false, new Vector3(0, 0, 0.345f), Quaternion.identity, new Vector3(0.3f, 0.3f, 1f)).GetComponent<Renderer>();
            f.Halo = NightMeshes.Part("Halo", f.Pitch, NightMeshes.Quad(), NightMeshes.Halo(), false, new Vector3(0, 0, 0.5f), Quaternion.identity, Vector3.one * 1.3f).GetComponent<Renderer>();
            f.HaloR = 1.3f;
            f.Light = MakeLight(f.Pitch, "Light", LightType.Spot, new Vector3(0, 0, 0.38f), Quaternion.identity, f);
            if (f.BeamK > 0f)
            {
                var b = NightMeshes.Part("Beam", f.Pitch, NightMeshes.Cone(), NightMeshes.Beam(), false, new Vector3(0, 0, 0.36f), Quaternion.identity, Vector3.one);
                f.Beam = b.GetComponent<Renderer>(); f.BeamT = b.transform;
            }
            f.Pool = PoolRenderer(f);
            f.Led = NightMeshes.Part("Beacon", f.Root, NightMeshes.Sphere(), NightMeshes.Bulb(), false, new Vector3(0.22f, -0.2f, -0.12f), Quaternion.identity, Vector3.one * 0.05f).GetComponent<Renderer>();
            f.PitchRest = e.pitch; f.PitchNow = 62f; f.YawNow = 0f;
            f.Sweep = Mathf.Max(0f, e.sweep); f.Period = Mathf.Max(4f, e.period);
            f.Pattern = e.pattern == "figure8" ? 1 : e.pattern == "scan" ? 2 : e.pattern == "circle" ? 3 : 0;
            f.Track = true;
            f.Pitch.localRotation = Quaternion.Euler(f.PitchNow, 0, 0);
        }

        void BuildFlood(NightFixture f, NightElementSpec e)
        {
            f.Root.localRotation = Quaternion.Euler(0, e.yaw, 0) * Quaternion.Euler(e.pitch, 0, 0);
            f.Head = f.Root;
            NightMeshes.Part("Head", f.Root, NightMeshes.FloodHead(), NightMeshes.Housing(), true, Vector3.zero, Quaternion.identity, Vector3.one);
            f.Lens = NightMeshes.Part("Lens", f.Root, NightMeshes.Quad(), NightMeshes.Lens(), false, new Vector3(0, 0, 0.026f), Quaternion.identity, new Vector3(0.21f, 0.13f, 1f)).GetComponent<Renderer>();
            f.HaloR = f.Kind == NightKind.Uplight ? 0.55f : 0.8f;
            f.Halo = NightMeshes.Part("Halo", f.Root, NightMeshes.Quad(), NightMeshes.Halo(), false, new Vector3(0, 0, 0.14f), Quaternion.identity, Vector3.one * f.HaloR).GetComponent<Renderer>();
            f.Light = MakeLight(f.Root, "Light", LightType.Spot, new Vector3(0, 0, 0.05f), Quaternion.identity, f);
            var origin = f.Root.TransformPoint(new Vector3(0, 0, 0.05f)); var dir = f.Root.forward;
            float s = GroundHit(origin, dir, f.Range);
            if (f.BeamK > 0f)
            {
                var b = NightMeshes.Part("Beam", f.Root, NightMeshes.Cone(), NightMeshes.Beam(), false, new Vector3(0, 0, 0.04f), Quaternion.identity, Vector3.one);
                f.Beam = b.GetComponent<Renderer>(); f.BeamT = b.transform;
                float a = Mathf.Tan(Mathf.Clamp(f.Angle * 0.5f, 3f, 70f) * Mathf.Deg2Rad) * 0.85f;
                float L = Mathf.Min(s + 0.6f, f.Kind == NightKind.Uplight ? 5f : f.Range * 0.7f);
                f.BeamT.localScale = new Vector3(L * a, L * a, L);
                f.BeamLen = L;
            }
            f.Pool = PoolRenderer(f);
            if (f.Pool) PlacePool(f, origin, dir, s);
        }

        void BuildWorkBeacon(NightFixture f, NightElementSpec e)
        {
            NightMeshes.Part("Base", f.Root, NightMeshes.BeaconBase(), NightMeshes.Housing(), true, Vector3.zero, Quaternion.identity, Vector3.one);
            f.Bulb = NightMeshes.Part("Dome", f.Root, NightMeshes.Sphere(), NightMeshes.Bulb(), false, new Vector3(0, 0.24f, 0), Quaternion.identity, new Vector3(0.09f, 0.11f, 0.09f)).GetComponent<Renderer>();
            f.HaloR = 0.55f;
            f.Halo = NightMeshes.Part("Halo", f.Root, NightMeshes.Quad(), NightMeshes.Halo(), false, new Vector3(0, 0.24f, 0), Quaternion.identity, Vector3.one * f.HaloR).GetComponent<Renderer>();
            f.Yaw = new GameObject("Spin").transform; f.Yaw.SetParent(f.Root, false); f.Yaw.localPosition = new Vector3(0, 0.24f, 0);
            f.Light = MakeLight(f.Yaw, "Light", LightType.Spot, new Vector3(0, 0, 0.1f), Quaternion.Euler(e.pitch, 0, 0), f);
            f.Period = Mathf.Max(0.3f, e.period);
        }

        void BuildWeld(NightFixture f, NightElementSpec e)
        {
            f.HaloR = 0.9f;
            f.Halo = NightMeshes.Part("Halo", f.Root, NightMeshes.Quad(), NightMeshes.Halo(), false, Vector3.zero, Quaternion.identity, Vector3.one * f.HaloR).GetComponent<Renderer>();
            f.Light = MakeLight(f.Root, "Light", LightType.Point, Vector3.zero, Quaternion.identity, f);
        }

        void ApplyTeamTint(NightRig rig)
        {
            var team = TeamColors.Of(rig.B.Owner);
            foreach (var f in rig.Fixtures)
            {
                var baseCol = ToColor(f.Spec.color);
                f.Col = rig.B.Owner == Team.Neutral ? baseCol : Color.Lerp(baseCol, team, f.Spec.teamTint);
                f.Col.a = 1f; f.ColLinear = f.Col.linear;
                if (f.Light) f.Light.color = f.Col;
                f.OutShown = f.HaloShown = f.BeamShown = f.PoolShown = f.LedShown = f.LensShown = f.BulbShown = -1f;
            }
        }

        // ------------------------------------------------------------------ lamps
        void BuildLamps()
        {
            if (!env) return;
            UnitView.Obstacles.Clear();
            var existing = new List<GameObject>();
            foreach (Transform t in env)
                if (t.gameObject.activeInHierarchy && t.name.IndexOf("lamp", System.StringComparison.OrdinalIgnoreCase) >= 0) existing.Add(t.gameObject);
            GameObject template = null;
            foreach (var go in existing)
            {
                AddLamp(go, LampTypeFor(go.name), false);
                if (!template && !string.IsNullOrEmpty(levelSpec.lampTemplate) && go.name.IndexOf(levelSpec.lampTemplate, System.StringComparison.OrdinalIgnoreCase) >= 0) template = go;
            }
            if (levelFile?.lamps == null) return;
            foreach (var p in levelFile.lamps)
            {
                if (p == null) continue;
                var go = CloneLamp(template, p);
                if (go) clonedLamps++; else go = ProceduralLamp(p);
                if (template && go != null && go.name.StartsWith("DEC_night_lamp_") && !go.name.Contains("proc")) go.transform.localScale *= 0.85f;   // gate L17: heads read oversized
                var nf = AddLamp(go, template ? LampTypeFor(template.name) : LampTypeFor("procedural"), true);
                if (nf != null) { newLamps++; nf.ObstacleIndex = UnitView.Obstacles.Count; UnitView.Obstacles.Add(new Vector3(nf.Foot.x, nf.Foot.z, 0.35f)); }
            }
        }

        NightFixture AddLamp(GameObject go, NightLampType type, bool isNew)
        {
            if (!go || lampByGO.ContainsKey(go)) return null;
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return null;
            // ran without ExternalLamps, or a light imported with the prop) is removed, so it is neither doubled nor copied
            // into the cloned posts as an unmanaged light (Instantiate runs before Destroy takes effect: clones are cleaned
            // by their own AddLamp).
            foreach (var foreign in go.GetComponentsInChildren<Light>(true))
            {
                foreign.enabled = false;
                if (foreign.gameObject != go && foreign.transform.childCount == 0 && foreign.GetComponents<Component>().Length == 2) Destroy(foreign.gameObject);
                else Destroy(foreign);
            }
            Bounds all = rs[0].bounds; foreach (var r in rs) all.Encapsulate(r.bounds);
            bool gotHead = false; Bounds head = default; Renderer tallest = rs[0];
            var emissive = new List<Renderer>(); var emissiveBase = new List<Color>(); Renderer bulb = null;
            foreach (var r in rs)
            {
                if (r.bounds.size.y > tallest.bounds.size.y) tallest = r;
                string n = r.name.ToLowerInvariant();
                bool glow = false;
                if (type.glowNames != null) foreach (var g in type.glowNames) if (!string.IsNullOrEmpty(g) && n.Contains(g)) glow = true;
                if (!glow) continue;
                if (!gotHead) { head = r.bounds; gotHead = true; } else head.Encapsulate(r.bounds);
                var m = r.sharedMaterial;
                if (m && m.HasProperty(NightMeshes.EmissionId)) { emissive.Add(r); emissiveBase.Add(m.GetColor(NightMeshes.EmissionId)); }
                else bulb = r;
            }
            var foot = new Vector3(tallest.bounds.center.x, all.min.y, tallest.bounds.center.z);
            var headPos = gotHead ? head.center : new Vector3(foot.x, all.max.y - 0.25f, foot.z);
            var f = new NightFixture
            {
                Kind = NightKind.Lamp, Id = go.name, LampGO = go, LampType = type, Col = ToColor(type.color), Intensity = type.intensity,
                Range = type.range, Angle = type.angle, Priority = type.priority, PoolK = type.pool, BeamK = type.cone, When = "night",
                Seed = Hash(go.name.GetHashCode()), Foot = foot, Pos0 = go.transform.position, Rot0 = go.transform.rotation,
            };
            f.ColLinear = f.Col.linear;
            f.Faulty = Hash(go.name.GetHashCode() * 7 + 3) < type.flickerShare;
            f.Emissive = emissive.ToArray(); f.EmissiveBase = emissiveBase.ToArray(); f.Bulb = bulb;
            var anchor = new GameObject("LampFx_" + go.name).transform; anchor.SetParent(transform, false);
            anchor.SetPositionAndRotation(headPos, Quaternion.identity);
            f.Head = anchor; f.Root = anchor;
            f.HeadLocal = go.transform.InverseTransformPoint(headPos); f.HeadLocalRot = Quaternion.Inverse(go.transform.rotation);
            var down = Quaternion.Euler(Mathf.Clamp(type.pitch, 0f, 90f), 0, 0);
            f.Light = MakeLight(anchor, "Light", LightType.Spot, new Vector3(0, -0.08f, 0), down, f);
            float gy = GroundY(headPos.x, headPos.z), h = Mathf.Max(0.8f, headPos.y - gy);
            if (f.BeamK > 0f)
            {
                var b = NightMeshes.Part("Cone", anchor, NightMeshes.Cone(), NightMeshes.Beam(), false, new Vector3(0, -0.06f, 0), down, Vector3.one);
                f.Beam = b.GetComponent<Renderer>(); f.BeamT = b.transform;
                float L = Mathf.Min(type.coneLen, h - 0.1f), a = Mathf.Tan(Mathf.Clamp(type.coneAngle, 5f, 75f) * Mathf.Deg2Rad);
                f.BeamT.localScale = new Vector3(L * a, L * a, L); f.BeamLen = L;
            }
            f.HaloR = 0.7f;
            f.Halo = NightMeshes.Part("Halo", anchor, NightMeshes.Quad(), NightMeshes.Halo(), false, new Vector3(0, -0.05f, 0), Quaternion.identity, Vector3.one * f.HaloR).GetComponent<Renderer>();
            f.Pool = PoolRenderer(f);
            if (f.Pool) PlacePool(f, headPos, Vector3.down, h);
            float jx = Mathf.Clamp01(headPos.x / mapW);
            f.OnAt = 0.335f + 0.022f * jx + 0.01f * Hash(go.name.GetHashCode() + 11);
            f.OffAt = 0.893f + 0.012f * Hash(go.name.GetHashCode() + 23);
            lampByGO[go] = f; lamps.Add(f); fixtures.Add(f);
            long key = Cell(foot);
            if (!lampCells.TryGetValue(key, out var list)) lampCells[key] = list = new List<NightFixture>();
            list.Add(f);
            return f;
        }

        static long Cell(Vector3 p) => ((long)Mathf.FloorToInt(p.x / 8f) << 32) ^ (uint)Mathf.FloorToInt(p.z / 8f);

        GameObject CloneLamp(GameObject template, NightLampPlacement p)
        {
            if (!template || !lampByGO.TryGetValue(template, out var tf)) return null;
            var arm = tf.Head.position - tf.Foot; arm.y = 0;
            float yawT = arm.sqrMagnitude > 0.01f ? Mathf.Atan2(arm.x, arm.z) * Mathf.Rad2Deg : template.transform.eulerAngles.y;
            var dq = Quaternion.Euler(0, p.yaw - yawT, 0);
            var go = Instantiate(template, template.transform.parent);
            go.name = $"DEC_night_lamp_{p.id}";
            var footOff = tf.Foot - template.transform.position;
            float rootAbove = template.transform.position.y - GroundY(tf.Foot.x, tf.Foot.z);
            float gy = GroundY(p.x, p.z);
            var offR = dq * footOff;
            go.transform.rotation = dq * template.transform.rotation;
            go.transform.position = new Vector3(p.x - offR.x, gy + rootAbove, p.z - offR.z);
            return go;
        }

        GameObject ProceduralLamp(NightLampPlacement p)
        {
            var go = new GameObject($"DEC_night_lamp_proc_{p.id}");
            go.transform.SetParent(env, false);
            go.transform.SetPositionAndRotation(new Vector3(p.x, GroundY(p.x, p.z) - 0.02f, p.z), Quaternion.Euler(0, p.yaw, 0));
            NightMeshes.Part("Post", go.transform, NightMeshes.LampPost(), NightMeshes.LampPostMaterial(), true, Vector3.zero, Quaternion.identity, Vector3.one);
            NightMeshes.Part("Glow", go.transform, NightMeshes.Sphere(), NightMeshes.Bulb(), false, new Vector3(0, 3.02f, 0.72f), Quaternion.identity, new Vector3(0.17f, 0.05f, 0.12f));
            return go;
        }

        // ------------------------------------------------------------------ frame
        void PushGrade()
        {
            NightGrade.MoonScale = moonScale; NightGrade.AmbientScale = ambientScale;
            NightGrade.Exposure = nightExposure; NightGrade.Contrast = nightContrast; NightGrade.Saturation = nightSaturation;
            NightGrade.Temperature = nightTemperature; NightGrade.BloomThreshold = nightBloomThreshold; NightGrade.BloomIntensity = nightBloomIntensity;
            NightGrade.Vignette = nightVignette; NightGrade.ShadowStrengthNight = moonShadowStrength;
            NightGrade.Rim = new Color(0.10f, 0.14f, 0.26f, 1f) * rimStrength;
            NightGrade.Fill = new Color(0.30f, 0.33f, 0.40f, 1f) * unitFill;
            NightGrade.HazeBoost = levelSpec != null ? levelSpec.haze - 1f : 0f;
        }

        void Update()
        {
            if (game == null) return;
            PushGrade();
            float t = dayNight ? dayNight.timeOfDay : NightGrade.TimeOfDay, night = NightGrade.Night(t);
            float dt = Time.deltaTime, now = Time.time;
            if (debugKeys && !global::OpenZ.OzRelease.IsRelease) DebugKeys(t);
            UpdateRigs(now);
            for (int i = 0; i < fixtures.Count; i++) UpdateFixture(fixtures[i], t, night, dt, now);
            if (knockLamps && now >= nextKnock) { nextKnock = now + 0.03f; KnockCheck(); }   // NB49: every ~2 frames (was 0.1 s: a vehicle drove 0.5 m into the pole first)
            budget.MaxLights = maxRealLights; budget.MaxShadowed = maxShadowed;
            budget.Tick(fixtures, cam, Focus(), now, dt);
            for (int i = 0; i < fixtures.Count; i++) ApplyLight(fixtures[i]);
        }

        void UpdateRigs(float now)
        {
            foreach (var rig in rigs)
            {
                var B = rig.B;
                bool dead = B.Destroyed;
                if (dead && !rig.WasDestroyed)
                {
                    rig.WasDestroyed = true;
                    foreach (var f in rig.Fixtures) { if (f.Out > 0.15f) Fizzle(f.LightPos, 0.8f); f.Level = 0f; f.Target = null; }
                    SetShown(rig, false);
                }
                else if (!dead && rig.WasDestroyed)
                {
                    rig.WasDestroyed = false; rig.Blackout = now + 1.5f; SetShown(rig, true);
                }
                if (B.Owner != rig.LastOwner)
                {
                    rig.LastOwner = B.Owner; rig.Blackout = now + 0.9f;
                    foreach (var f in rig.Fixtures) { if (f.Out > 0.3f && f.Kind == NightKind.Searchlight) Fizzle(f.LightPos, 0.4f); f.Level = Mathf.Min(f.Level, 0.02f); f.Target = null; }
                    ApplyTeamTint(rig);
                }
            }
        }

        void SetShown(NightRig rig, bool on)
        {
            if (rig.Shown == on) return;
            rig.Shown = on;
            rig.Root.gameObject.SetActive(on);
            foreach (var f in rig.Fixtures) if (f.Pool) f.Pool.enabled = false;
        }

        bool Producing(ZObject b)
        {
            var fv = FactoryViews.I ? FactoryViews.I.For(b.Id) : null;
            if (fv) return fv.Producing;
            return b.Building && b.CurUnit != null && !game.World.UnitLimitReached(b.Owner);
        }

        void UpdateFixture(NightFixture f, float t, float night, float dt, float now)
        {
            bool want;
            if (f.IsLamp)
                want = f.LampGO && f.LampGO.activeInHierarchy && !f.GlassBroken && f.Damage < 2 && NightGrade.LightsOn(t, f.OnAt, f.OffAt);
            else
            {
                var B = f.Rig.B;
                bool owned = !B.Destroyed && B.Owner != Team.Neutral && now >= f.Rig.Blackout && f.Rig.Shown;
                want = f.When == "producing" ? owned && Producing(B)
                     : f.When == "repairing" ? owned && B.RepairingUnit
                     : owned && NightGrade.LightsOn(t, f.OnAt, f.OffAt);
            }
            float upT = f.Kind switch { NightKind.Lamp => 3.2f, NightKind.Searchlight => 0.35f, NightKind.WorkBeacon => 0.3f, NightKind.Weld => 0.05f, _ => 0.8f };
            float prev = f.Level;
            f.Level = want ? Mathf.MoveTowards(f.Level, 1f, dt / upT) : Mathf.MoveTowards(f.Level, 0f, dt / (f.Kind == NightKind.Weld ? 0.05f : 0.6f));
            if (prev <= 0f && f.Level > 0f && f.Kind == NightKind.Searchlight) Audio("searchlight_on", f.LightPos);

            float o = f.Level;
            if (f.IsLamp)
            {
                if (f.Level < 1f && want)
                {   // sodium warm-up: dim, a couple of blinks in the first half, then full
                    o = Mathf.Pow(f.Level, 1.6f);
                    if (f.Level < 0.5f && Mathf.PerlinNoise(f.Seed * 9.1f, now * 7f) > 0.72f) o *= 0.25f;
                }
                o *= Flicker(f, now);
            }
            else if (f.Kind == NightKind.Weld && o > 0f)
            {   // arc welding: fast irregular strobes and sparks
                float n = Mathf.PerlinNoise(f.Seed * 3f, now * 19f);
                o *= n > 0.5f ? 0.55f + 0.9f * Mathf.PerlinNoise(f.Seed * 5f, now * 43f) : 0.06f;
                if (want && now >= f.WeldNext && FxManager.I && n > 0.5f) { f.WeldNext = now + 0.25f + 0.35f * Mathf.PerlinNoise(f.Seed, now); FxManager.I.Pop(f.LightPos, 0.3f); }
            }
            f.Out = o;
            bool conditional = f.When == "producing" || f.When == "repairing";
            f.LightOut = o * (conditional ? night : 1f);   // work beacons / weld: real light only when it is dark (the bulb and halo show by day)

            if (f.Kind == NightKind.Searchlight) Aim(f, dt, now, want, night);
            else if (f.Kind == NightKind.WorkBeacon && f.Yaw && o > 0f) f.Yaw.localRotation = Quaternion.Euler(0, Mathf.Repeat(now * 360f / f.Period + f.Seed * 360f, 360f), 0);
            else if (f.IsLamp && (f.Falling || f.TiltVel != 0f || f.Tilt != f.TiltTarget)) AnimateLamp(f, dt);
            Visuals(f, night, now);
        }

        float Flicker(NightFixture f, float now)
        {
            if (OzMotion.Reduced) return 0.96f;
            if (f.Faulty || now < f.FlickerUntil)
            {
                float p = Mathf.PerlinNoise(f.Seed * 17.3f, now * (now < f.FlickerUntil ? 6f : 1.7f));
                if (p > 0.6f) return Mathf.PerlinNoise(f.Seed * 5.1f, now * 31f) > 0.45f ? 1f : 0.06f;
                return 0.96f;
            }
            return 0.975f + 0.025f * Mathf.PerlinNoise(f.Seed * 3.3f, now * 8f);
        }

        // ------------------------------------------------------------------ searchlights
        void Aim(NightFixture f, float dt, float now, bool on, float night)
        {
            float tYaw, tPitch;
            if (!on && f.Level <= 0.01f) { tYaw = 0f; tPitch = 62f; f.Target = null; }
            else
            {
                float ph = now * Mathf.PI * 2f / f.Period + f.Seed * 6.283f;
                switch (f.Pattern)
                {
                    case 1: tYaw = f.Sweep * Mathf.Sin(ph); tPitch = f.PitchRest + 6f * Mathf.Sin(ph * 2f); break;
                    case 2:
                        if (now >= f.ScanNext)
                        {
                            f.ScanFrom = new Vector2(f.YawNow, f.PitchNow);
                            f.ScanTo = new Vector2((float)(rnd.NextDouble() * 2 - 1) * f.Sweep, f.PitchRest + (float)(rnd.NextDouble() * 13 - 5));
                            f.ScanT0 = now; f.ScanNext = now + 2.2f + 1.8f * (float)rnd.NextDouble();
                        }
                        float k = Mathf.SmoothStep(0f, 1f, (now - f.ScanT0) / 1.4f);
                        tYaw = Mathf.LerpAngle(f.ScanFrom.x, f.ScanTo.x, k); tPitch = Mathf.Lerp(f.ScanFrom.y, f.ScanTo.y, k); break;
                    case 3: tYaw = Mathf.Repeat(now * 360f / f.Period + f.Seed * 360f, 360f); tPitch = f.PitchRest; break;
                    default: tYaw = f.Sweep * Mathf.Sin(ph); tPitch = f.PitchRest + 3f * Mathf.Sin(ph * 2.3f); break;
                }
                if (f.Track && f.Level > 0.5f) TrackTarget(f, now, ref tYaw, ref tPitch);
            }
            float rate = f.Target != null ? 4f : 2.4f, a = 1f - Mathf.Exp(-rate * dt);
            f.YawNow = Mathf.LerpAngle(f.YawNow, tYaw, a);
            f.PitchNow = Mathf.Lerp(f.PitchNow, Mathf.Clamp(tPitch, -10f, 75f), a);
            f.Yaw.localRotation = Quaternion.Euler(0, f.YawNow, 0);
            f.Pitch.localRotation = Quaternion.Euler(f.PitchNow, 0, 0);
            if (f.Out <= 0.001f || !f.Light) return;
            // beam length and pool follow the ground
            var origin = f.Light.transform.position; var dir = f.Pitch.forward;
            float s = GroundHit(origin, dir, f.Range);
            if (f.BeamT)
            {
                float ta = Mathf.Tan(Mathf.Clamp(f.Angle * 0.5f, 2f, 40f) * Mathf.Deg2Rad) * 1.2f;
                f.BeamT.localScale = new Vector3(s * ta, s * ta, s + 0.8f); f.BeamLen = s;
            }
            if (f.Pool) PlacePool(f, origin, dir, s);
        }

        void TrackTarget(NightFixture f, float now, ref float tYaw, ref float tPitch)
        {
            if (now >= f.NextTrackScan)
            {
                f.NextTrackScan = now + 0.5f + 0.1f * f.Seed;
                // A target is followed as long as it stays in range (was dropped after 6 s + a 4.5 s pause even with the
                // enemy still there); when it leaves / dies the light picks the next intruder, else returns to its sweep
                if (f.Target != null && (f.Target.Destroyed || f.Target.Owner == f.Rig.B.Owner || f.Target.Owner == Team.Neutral || !InSector(f, f.Target, 1.05f, out _, out _)))
                { f.Target = null; f.TrackCooldown = now + 1.2f; }
                if (f.Target == null && now >= f.TrackCooldown) f.Target = FindIntruder(f, now);
            }
            if (f.Target != null && InSector(f, f.Target, 1.1f, out float y, out float p)) { tYaw = y; tPitch = p; }
        }

        Vector3 UnitWorld(ZObject o)
        {
            var w = ZGame.PxToWorld(o.CenterX, o.CenterY);
            w.y = GroundY(w.x, w.z) + (o.Kind == ZKind.Vehicle ? 0.9f : 0.7f);
            return w;
        }

        bool InSector(NightFixture f, ZObject o, float reachMul, out float yaw, out float pitch)
        {
            var d = f.Root.InverseTransformDirection(UnitWorld(o) - f.Pitch.position);
            yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float h = Mathf.Sqrt(d.x * d.x + d.z * d.z);
            pitch = Mathf.Atan2(-d.y, h) * Mathf.Rad2Deg;
            return h <= f.Range * 0.85f * reachMul && Mathf.Abs(yaw) <= f.Sweep + 35f && pitch > 4f;
        }

        ZObject FindIntruder(NightFixture f, float now)
        {
            var team = f.Rig.B.Owner; if (team == Team.Neutral) return null;
            ZObject best = null; float bestD = float.MaxValue;
            var objs = game.World.Objects;
            for (int i = 0; i < objs.Count; i++)
            {
                var o = objs[i];
                if (o.Destroyed || o.Owner == team || o.Owner == Team.Neutral) continue;
                if (!(o.Kind == ZKind.Vehicle || (o.Kind == ZKind.Robot && !o.IsMinion))) continue;
                if (!InSector(f, o, 1f, out _, out _)) continue;
                var w = ZGame.PxToWorld(o.CenterX, o.CenterY); var p = f.Pitch.position;
                float d = (w.x - p.x) * (w.x - p.x) + (w.z - p.z) * (w.z - p.z);
                if (d < bestD) { bestD = d; best = o; }
            }
            if (best != null) f.TargetUntil = now + 6f;
            return best;
        }

        // ------------------------------------------------------------------ visuals
        void SetColor(Renderer r, Color c, ref float shown)
        {
            if (!r) return;
            float key = c.r + c.g * 1.7f + c.b * 0.6f;
            if (shown >= 0f && Mathf.Abs(key - shown) <= Mathf.Max(0.004f, shown * 0.012f)) return;
            shown = key;
            bool on = key > 0.0015f;
            if (r.enabled != on) r.enabled = on;
            if (!on) return;
            mpb.Clear(); mpb.SetColor(NightMeshes.ColorId, c); r.SetPropertyBlock(mpb);
        }

        void Visuals(NightFixture f, float night, float now)
        {
            float o = f.Out, dusk = Mathf.Lerp(0.25f, 1f, night);
            Color c = f.ColLinear;
            if (f.IsLamp && f.Level < 1f) c = new Color(c.r, c.g * Mathf.Lerp(0.55f, 1f, f.Level), c.b * Mathf.Lerp(0.3f, 1f, f.Level), 1f);   // sodium warms up red
            float lensGain = f.Kind == NightKind.Searchlight ? 7f : 5f;
            SetColor(f.Lens, c * (lensGain * o), ref f.LensShown);
            if (f.Emissive != null)
            {
                float k = o, key = k + (f.GlassBroken ? 10f : 0f);
                if (Mathf.Abs(key - f.OutShown) > 0.008f)
                {
                    f.OutShown = key;
                    for (int i = 0; i < f.Emissive.Length; i++)
                    {
                        var r = f.Emissive[i]; if (!r) continue;
                        if (f.GlassBroken) { r.enabled = false; continue; }
                        mpb.Clear(); mpb.SetColor(NightMeshes.EmissionId, f.EmissiveBase[i] * (0.12f + 4.4f * k)); r.SetPropertyBlock(mpb);
                    }
                }
            }
            if (f.Bulb)
            {
                float bk = f.Kind == NightKind.WorkBeacon ? 5f * o : f.GlassBroken ? 0f : 4f * o;
                SetColor(f.Bulb, c * bk, ref f.BulbShown);
            }
            float haloK = halos ? haloGain * dusk * o * (f.Kind == NightKind.Searchlight ? 2.6f : f.Kind == NightKind.Weld ? 2.2f : f.IsLamp ? 1.8f : 1.5f) : 0f;
            if (f.Kind == NightKind.Searchlight && f.Target != null) haloK *= 1.3f;
            SetColor(f.Halo, c * haloK, ref f.HaloShown);
            float beamK = beams ? beamGain * f.BeamK * o * Mathf.Pow(night, 0.8f) * (f.Kind == NightKind.Searchlight ? (f.Target != null ? 0.55f : 0.42f) : f.IsLamp ? 0.11f : 0.22f) : 0f;
            SetColor(f.Beam, c * beamK, ref f.BeamShown);
            if (f.Pool)
            {
                // impostor ~ the real light: dst * (1 + 4 E) matches albedo * (moon + ambient + E) for a moonlit surface (~0.3)
                float pk = pools ? poolGain * (f.IsLamp ? lampPoolBoost : 1f) * f.PoolK * o * dusk * (1f - f.RealK) * Mathf.Min(10f, f.StaticPoolE * 4f) : 0f;
                SetColor(f.Pool, c * pk, ref f.PoolShown);
            }
            if (f.Led)
            {
                bool owned = f.Rig != null && f.Rig.B.Owner != Team.Neutral && !f.Rig.B.Destroyed;
                float blink = Mathf.Repeat(now / 1.6f + f.Seed, 1f) < 0.22f ? 1f : 0.08f;
                var tc = f.Rig != null ? TeamColors.Of(f.Rig.B.Owner).linear : Color.white;
                SetColor(f.Led, owned ? tc * (5f * blink * Mathf.Lerp(0.35f, 1f, night)) : Color.black, ref f.LedShown);
            }
        }

        void ApplyLight(NightFixture f)
        {
            var l = f.Light; if (!l) return;
            float k = f.LightOut * f.RealK;
            bool on = k > 0.002f && l.gameObject.activeInHierarchy;
            if (l.enabled != on) l.enabled = on;
            if (!on) return;
            float kindGain = f.IsLamp ? lampLightGain : f.Kind == NightKind.Searchlight ? searchLightGain : 1f;   // ground_lights_fix
            l.intensity = f.Intensity * k * lightGain * kindGain;
            var sh = f.ShadowK > 0.001f ? LightShadows.Soft : LightShadows.None;
            if (l.shadows != sh) l.shadows = sh;
            if (sh != LightShadows.None)
            {
                float st = NightShadows.Strength * f.ShadowK; if (Mathf.Abs(l.shadowStrength - st) > 0.002f) l.shadowStrength = st;
                if (!f.ShadowCandidate) NightShadows.Configure(l, f.ShadowHigh);               // NEARBY lights: soft MEDIUM, tier, bias (searchlights keep theirs)
            }
        }

        // ------------------------------------------------------------------ lamp damage
        bool ClaimProp(GameObject go) => go && lampByGO.ContainsKey(go);

        void OnBlastLegacy(Vector3 at, float strength) => OnBlast(at, strength, 1.2f + 2.4f * strength);
        void OnBlastM12(Vector3 at, float s) => OnBlast(at, s, 1.0f + 1.8f * s);

        void OnBlast(Vector3 at, float strength, float reach)
        {
            if (Time.frameCount == lastBlastFrame && (at - lastBlastPos).sqrMagnitude < 1f) return;   // FxManager + fx port report one blast twice
            lastBlastFrame = Time.frameCount; lastBlastPos = at;
            int cr = Mathf.CeilToInt(reach / 8f) + 1, cx = Mathf.FloorToInt(at.x / 8f), cz = Mathf.FloorToInt(at.z / 8f);
            for (int ix = cx - cr; ix <= cx + cr; ix++)
                for (int iz = cz - cr; iz <= cz + cr; iz++)
                {
                    if (!lampCells.TryGetValue(((long)ix << 32) ^ (uint)iz, out var list)) continue;
                    foreach (var f in list)
                    {
                        if (f.Damage >= 2 || !f.LampGO || !f.LampGO.activeInHierarchy) continue;
                        var hp = f.Head.position;
                        float d = Mathf.Min(Flat(f.Foot - at).magnitude, Flat(hp - at).magnitude);
                        if (d > reach) continue;
                        float sev = 1f - d / reach;
                        var away = Flat(f.Foot - at); if (away.sqrMagnitude < 1e-3f) away = Vector3.forward; away.Normalize();
                        if (sev > 0.45f || (strength >= 1.5f && sev > 0.2f) || f.Damage == 1) Topple(f, away, sev);
                        else Bend(f, away, sev);
                    }
                }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        void KnockCheck()
        {
            if (lamps.Count == 0) return;
            var objs = game.World.Objects;
            for (int i = 0; i < objs.Count; i++)
            {
                var o = objs[i];
                if (o.Kind != ZKind.Vehicle || o.Destroyed || !o.IsMoving) continue;
                // the hull's front edge knocks the pole (radius = the vehicle's half length + a short look-ahead along its motion,
                // was a 1.25 m circle round its centre), and the pole falls away from the hull along its motion
                var vel = new Vector3(o.Dx, 0, -o.Dy) / 8f;                                  // m/s (pixel y points south)
                var w = ZGame.PxToWorld(o.CenterX, o.CenterY) + vel * 0.12f;
                float reach = (o.Type == "jeep" ? 1.5f : o.Type == "heavy" || o.Type == "apc" || o.Type == "missile_launcher" ? 2.1f : 1.8f) + knockRadius * 0.2f;
                int cx = Mathf.FloorToInt(w.x / 8f), cz = Mathf.FloorToInt(w.z / 8f);
                for (int ix = cx - 1; ix <= cx + 1; ix++)
                    for (int iz = cz - 1; iz <= cz + 1; iz++)
                    {
                        if (!lampCells.TryGetValue(((long)ix << 32) ^ (uint)iz, out var list)) continue;
                        foreach (var f in list)
                        {
                            if (f.Damage >= 2 || !f.LampGO || !f.LampGO.activeInHierarchy) continue;
                            var away = Flat(f.Foot - w);
                            if (away.sqrMagnitude > reach * reach) continue;
                            var mv = vel.sqrMagnitude > 1e-6f ? vel.normalized : Vector3.zero;
                            var dir = mv + (away.sqrMagnitude > 1e-4f ? away.normalized * 0.8f : Vector3.zero);
                            if (dir.sqrMagnitude < 1e-6f) dir = mv.sqrMagnitude > 0f ? mv : Vector3.forward;
                            Topple(f, dir.normalized, 0.7f);
                        }
                    }
            }
        }

        void Bend(NightFixture f, Vector3 dir, float sev)
        {
            f.Damage = 1;
            f.TiltAxis = Vector3.Cross(Vector3.up, dir).normalized;
            f.TiltTarget = 12f + 26f * sev; f.TiltVel = 120f;
            f.FlickerUntil = Time.time + 2.5f;
            if (rnd.NextDouble() < 0.5 + 0.4 * sev) GlassBreak(f); else { f.Faulty = true; Sparks(f.Head.position, 0.45f); }
        }

        void Topple(NightFixture f, Vector3 dir, float sev)
        {
            if (f.Damage >= 2) return;
            // NB49: a bent pole falls the way it is pushed now (the first hit's axis made it fall the wrong way); its current bend
            // is carried over onto the new axis so it never pops
            var axis = Vector3.Cross(Vector3.up, dir).normalized;
            if (f.Damage == 1 && f.TiltAxis.sqrMagnitude > 0.5f) f.Tilt *= Mathf.Max(0f, Vector3.Dot(f.TiltAxis, axis));
            f.TiltAxis = axis;
            f.Damage = 2; f.Falling = true; f.Landed = false;
            if (f.ObstacleIndex >= 0 && f.ObstacleIndex < UnitView.Obstacles.Count) UnitView.Obstacles[f.ObstacleIndex] = Vector3.zero;   // fallen pole: no longer in the way
            f.TiltTarget = 84f + 4f * (float)rnd.NextDouble(); f.TiltVel = Mathf.Max(f.TiltVel, 25f + 55f * sev);
            GlassBreak(f);
            Audio("lamp_fall", f.Foot);
        }

        void GlassBreak(NightFixture f)
        {
            if (f.GlassBroken) return;
            f.GlassBroken = true;
            var hp = f.Head.position;
            Sparks(hp, 0.8f);
            FxManager.I?.Breakup(hp, "metal", 0.35f, 5, 0.16f);
            FxManager.I?.AddLight(hp, new Color(1f, 0.85f, 0.6f), 3f * Mathf.Max(0.2f, f.Out), 5f, 0.2f, 0.6f, true);   // the bulb pops
            Audio("lamp_glass", hp);
        }

        void Sparks(Vector3 at, float k)
        {
            FxManager.I?.Pop(at, 0.5f * k + 0.2f);
            Audio("lamp_spark", at);
        }

        void Fizzle(Vector3 at, float k)
        {
            FxManager.I?.Pop(at, 0.3f + 0.4f * k);
            FxManager.I?.AddLight(at, new Color(0.8f, 0.9f, 1f), 2.4f * k, 4f, 0.15f, 0.8f, true);
            Audio("light_fizzle", at);
        }

        void AnimateLamp(NightFixture f, float dt)
        {
            if (f.Falling)
            {
                f.TiltVel += (280f * Mathf.Sin(Mathf.Max(3f, f.Tilt) * Mathf.Deg2Rad) + 20f) * dt;
                f.Tilt += f.TiltVel * dt;
                if (f.Tilt >= f.TiltTarget)
                {
                    f.Tilt = f.TiltTarget;
                    if (!f.Landed) { f.Landed = true; LandFx(f); f.TiltVel = -Mathf.Abs(f.TiltVel) * 0.22f; }
                    else if (Mathf.Abs(f.TiltVel) < 12f) { f.TiltVel = 0f; f.Falling = false; }
                    else f.TiltVel = -Mathf.Abs(f.TiltVel) * 0.3f;
                }
            }
            else
            {
                float acc = (f.TiltTarget - f.Tilt) * 90f - f.TiltVel * 11f;
                f.TiltVel += acc * dt; f.Tilt += f.TiltVel * dt;
                if (Mathf.Abs(f.TiltTarget - f.Tilt) < 0.05f && Mathf.Abs(f.TiltVel) < 0.5f) { f.Tilt = f.TiltTarget; f.TiltVel = 0f; }
            }
            if (!f.LampGO) { f.Falling = false; f.TiltVel = 0f; f.Tilt = f.TiltTarget; return; }   // prop removed by another system
            var q = Quaternion.AngleAxis(f.Tilt, f.TiltAxis);
            var t = f.LampGO.transform;
            t.SetPositionAndRotation(f.Foot + q * (f.Pos0 - f.Foot), q * f.Rot0);
            f.Head.SetPositionAndRotation(t.TransformPoint(f.HeadLocal), t.rotation * f.HeadLocalRot);
            // a bent lamp that still burns: its impostor pool follows the tilted head once the pole has settled
            if (f.Pool && f.Light && !f.Falling && f.TiltVel == 0f && !f.GlassBroken)
            {
                var o = f.Light.transform.position; var d = f.Light.transform.forward;
                PlacePool(f, o, d, GroundHit(o, d, f.Range));
            }
        }

        void LandFx(NightFixture f)
        {
            var hp = f.Head.position;
            FxManager.I?.Dust(new Vector3(hp.x, GroundY(hp.x, hp.z) + 0.2f, hp.z), 0.6f);
            if (FxManager.I && rnd.NextDouble() < 0.7) FxManager.I.Pop(hp, 0.4f);
            Audio("lamp_land", hp);
        }

        void Audio(string key, Vector3 pos)
        {
            if (AudioEvent != null) { AudioEvent(key, pos); return; }
            var au = CombatAudio.I; if (!au) return;
            switch (key)
            {
                case "lamp_spark": case "light_fizzle": au.Play("night_spark", pos, 0.45f, 0.08f, -1, "laser-impact-electric-a", "laser-impact-electric-b"); break;
                case "lamp_land": au.Play("night_land", pos, 0.6f, 0.05f, -1, OzSoundV3.BankMetalMedium);   /* sound_v3 */ break;
                // glass: the sound package's shatter bank once it is in Resources/OpenZ/Sfx (silent until then: the spark plays)
                case "lamp_glass": au.Play("night_glass", pos, 0.5f, 0.05f, -1, "sd_glass_shatter_a", "sd_glass_shatter_b"); break;
            }
        }

        // ------------------------------------------------------------------ fog lamps (Atmosphere hook)
        readonly float[] fogD = new float[MaxFog]; readonly NightFixture[] fogF = new NightFixture[MaxFog];

        /// <summary>Atmosphere.FogLampSource: the lit lights nearest the camera focus light the volumetric fog.</summary>
        int FillFogLamps(Vector3 camPos, Vector4[] pos, Vector4[] col)
        {
            var c = rts ? rts.focus : camPos;
            int n = 0, max = Mathf.Min(MaxFog, Mathf.Min(pos.Length, col.Length));
            for (int i = 0; i < fixtures.Count; i++)
            {
                var f = fixtures[i];
                if (f.Out <= 0.05f || f.Kind == NightKind.Uplight || f.Kind == NightKind.Weld || !f.Head && !f.Light) continue;
                var p = f.IsLamp ? f.Head.position : f.LightPos;
                float d = (p.x - c.x) * (p.x - c.x) + (p.z - c.z) * (p.z - c.z);
                if (n < max) { fogD[n] = d; fogF[n] = f; n++; continue; }
                int worst = 0; for (int k = 1; k < n; k++) if (fogD[k] > fogD[worst]) worst = k;
                if (d < fogD[worst]) { fogD[worst] = d; fogF[worst] = f; }
            }
            float range = levelSpec != null ? levelSpec.fogLampRange : 10f;
            for (int i = 0; i < n; i++)
            {
                var f = fogF[i]; var p = f.IsLamp ? f.Head.position : f.LightPos;
                float g = f.IsLamp ? 0.22f : f.Kind == NightKind.Searchlight ? 0.26f : f.Kind == NightKind.WorkBeacon ? 0.1f : 0.17f;
                pos[i] = new Vector4(p.x, p.y, p.z, f.Kind == NightKind.Searchlight ? range * 0.8f : range);
                var lc = f.ColLinear * (g * f.Out);
                col[i] = new Vector4(lc.r, lc.g, lc.b, 0f);
            }
            return n;
        }

        // ------------------------------------------------------------------ debug
        void DebugKeys(float t)
        {
            var kb = Keyboard.current; if (kb == null || !kb.f9Key.wasPressedThisFrame) return;
            if (kb.ctrlKey.isPressed) { Debug.Log(Status()); return; }
            if (!dayNight) return;
            if (kb.shiftKey.isPressed) { dayNight.running = !dayNight.running; Debug.Log($"[OpenZ] NightLights: day/night clock {(dayNight.running ? "running" : "frozen")} at t {dayNight.timeOfDay:F3}"); return; }
            int next = 0;
            for (int i = 0; i < Presets.Length; i++) if (Presets[i] > t + 0.01f) { next = i; break; }
            JumpTo(Presets[next]);
            Debug.Log($"[OpenZ] NightLights: time of day -> {PresetNames[next]} ({Presets[next]:F3})");
        }

        /// <summary>Set the DayNight clock (debug key, OpenZ/Night menu).</summary>
        public static void JumpTo(float t)
        {
            var dn = FindFirstObjectByType<DayNight>(); if (!dn) return;
            dn.timeOfDay = Mathf.Repeat(t, 1f); dn.Apply(dn.timeOfDay);
        }

        public string Status()
        {
            float t = dayNight ? dayNight.timeOfDay : NightGrade.TimeOfDay;
            int on = 0, lampsDown = 0, lampsBroken = 0, lampsOn = 0, search = 0, searchOn = 0, tracking = 0, pooled = 0;
            foreach (var f in fixtures)
            {
                if (f.Out > 0.02f) on++;
                if (f.Pool && f.Pool.enabled) pooled++;
                if (f.IsLamp) { if (f.Damage >= 2) lampsDown++; else if (f.GlassBroken) lampsBroken++; if (f.Out > 0.02f) lampsOn++; }
                if (f.Kind == NightKind.Searchlight) { search++; if (f.Out > 0.02f) searchOn++; if (f.Target != null) tracking++; }
            }
            var sb = new System.Text.StringBuilder();
            sb.Append($"[OpenZ] NightLights {level}  t {t:F3}  night {NightGrade.Night(t):F2}  clock {(dayNight && dayNight.running ? "running" : "frozen")}\n");
            sb.Append($"  rigs {rigs.Count}, fixtures {fixtures.Count}, burning {on}; searchlights {searchOn}/{search} (tracking {tracking}); lamps {lampsOn}/{lamps.Count} lit, {lampsBroken} broken, {lampsDown} down; pool impostors drawn {pooled}\n");
            sb.Append($"  budget: real {budget.Granted}/{budget.EffectiveMax} (max {maxRealLights}), shadows {budget.ShadowsGranted}/{budget.EffectiveShadows}, " +
                      $"GPU {(budget.Temperature >= 0 ? budget.Temperature + " C" : "n/a")} heat {budget.HeatLevel}, frame {budget.FrameMs:F1} ms, fx lights {budget.FxLights}\n");
            sb.Append($"  grade: exposure {nightExposure:F2}, contrast {nightContrast:F0}, saturation {nightSaturation:F0}, temperature {nightTemperature:F0}, bloom {nightBloomThreshold:F2}/{nightBloomIntensity:F2}, moon x{moonScale:F2}, ambient x{ambientScale:F2}\n");
            sb.Append($"  hooks: fog lamps {(Atmosphere.FogLampSource != null ? "on" : "OFF")}, prop claim {(PropBreakables.ClaimBreak != null ? "on" : "OFF")}, atmosphere pass {AtmospherePassState()}, DayNight external lamps {DayNight.ExternalLamps}\n");
            foreach (var rig in rigs)
            {
                int lit = 0; foreach (var f in rig.Fixtures) if (f.Out > 0.02f) lit++;
                sb.Append($"  {rig.B.Type}#{rig.B.Id} {rig.B.Owner}{(rig.B.Destroyed ? " DESTROYED" : "")}: {lit}/{rig.Fixtures.Count} lit\n");
            }
            return sb.ToString();
        }

        static string AtmospherePassState()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var fi = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (!urp || fi == null) return "unknown";
            foreach (var rd in (ScriptableRendererData[])fi.GetValue(urp))
                if (rd) foreach (var feat in rd.rendererFeatures) if (feat && feat.name.Contains("Atmosphere")) return feat.isActive ? "active" : "INACTIVE (renderer feature off)";
            return "missing";
        }
    }
}
