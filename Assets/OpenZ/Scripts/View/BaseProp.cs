using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// The sim is untouched: same tile, same single blocked tile, same hut health; only the look is bigger
    /// (a 3.4 x 3.0 m framed pad with the tall parts on the tile).
    /// <para>
    /// tilt, NRM_ = panel normal: camera-facing tracker, slow sun tracking inside +-18 deg yaw, 22-46 deg tilt, visible eased
    /// steps), SPIN_ (fan, dish), BLINK_ (small bulbs: no halo, no pool), LAMP_ (work light: a real URP spot granted by the night
    /// light budget through NightBudgetShare, else a soft NightPool ground pool; small lens glow below the bloom threshold),
    /// SMOKE_, TOPPLE_ (the comms mast and the light poles fall on the break), FIRE_, OBST_ (robot view obstacles for the tall
    /// parts that overhang walkable tiles), KEEP_RING_* (the frame: stays after the break; one gate side per hut), RUIN.</para>
    /// <para>All sets are driven by one BasePropDirector (no per-prop Update): animation only inside the camera frustum,
    /// RockPillar chunk physics (RockChunkLife, PopOut).</para>
    /// <para>Break(): CombatView calls it when Zod removes the hut (patch CV_BP1); without the patch ZGame.HideStatic hides the
    /// hut, OnDisable notices and the director re-shows it next frame and breaks it. At half health the set shows damage.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class BaseProp : MonoBehaviour
    {
        [Tooltip("<level>_<set>, e.g. L02_Solar")] public string setKey;
        [Tooltip("fuel, solar, generator, comms, supply")] public string kind = "fuel";
        [Tooltip("Debris thrown on the break (the level chunk kit)")] public Mesh[] chunks;
        public Material chunkMaterial, glassMaterial;
        [Tooltip("Sim object id (-1 = resolved from ZGame.StaticViews)")] public int simId = -1;
        [Tooltip("Ring gate side for this hut: S, E or W (the side toward its path, base_props_v2_layout.json)")] public string gate = "S";

        // ---------------------------------------------------------------------------------------------- parsed parts
        internal class Spin { public Transform t; public Quaternion rest; public Vector3 axis; public float speed, angle; }
        internal class Blink { public Transform at; public Renderer bulb; public Color col; public float period, duty, offset, radius; public int state = -1; }
        internal class Panel
        {
            public Transform yaw, tilt; public Quaternion yawRest, tiltRest;
            public Vector3 yawAxis, tiltAxis;                    // parent space (the node rotates about these)
            public Vector3 yawAxisR, tiltAxisR, nrmR;            // prop root space, rest pose
            public float yawCur, tiltCur = RestTilt, yawVel, tiltVel, smooth;
        }
        internal class Lamp
        {
            public Transform at, aim; public Light light; public NightFixture fix; public Renderer pool;
            public readonly List<Renderer> lenses = new List<Renderer>(); public readonly List<float> lensShown = new List<float>();
            public float intensity, range, angle, level, seed, poolShown = -1f; public bool dead;
        }
        internal class Topple { public Transform t; public string id; }

        internal readonly List<Spin> spins = new List<Spin>();
        internal readonly List<Blink> blinks = new List<Blink>();
        internal readonly List<Panel> panels = new List<Panel>();
        internal readonly List<Lamp> lamps = new List<Lamp>();
        internal readonly List<Topple> topples = new List<Topple>();
        internal readonly List<Transform> smokes = new List<Transform>(), fires = new List<Transform>();
        readonly List<Vector3> obstLocal = new List<Vector3>();                 // x, z (prop root space), radius
        readonly Dictionary<string, GameObject> ring = new Dictionary<string, GameObject>();
        internal Transform ruin;
        readonly List<GameObject> intact = new List<GameObject>();
        internal Bounds bounds;
        internal bool visible;
        internal int stage;                                   // 0 intact, 1 damaged (<= 50 % HP), 2 broken
        internal float nextSmoke, seed, nextHealthPoll, nextSpark, nextBurn;
        bool built, quitting;
        internal bool placed;
        MaterialPropertyBlock mpb;
        Vector3 faceR, upR;                                   // solar: rest facing (horizontal) and up, prop root space
        float nextStep = -1f, seekYaw, seekTilt; int step;
        internal readonly List<int> obstIdx = new List<int>(); internal readonly List<Vector3> obstVal = new List<Vector3>();

        public const float RestTilt = 35f, YawRange = 18f, TiltMin = 22f, TiltMax = 46f, NightTilt = 28f;
        static readonly float[] SeekYaw = { 8f, -5f, 4f, -8f, 5f, -4f }, SeekTilt = { 5f, -4f, 3f, -5f };
        static readonly Regex BlinkRx = new Regex(@"^BLINK_(red|green|amber)_p([\d.]+)_d([\d.]+)_r([\d.]+)(?:_o([\d.]+))?", RegexOptions.Compiled);
        static readonly Regex SpinRx = new Regex(@"^SPIN_\w+?_s(-?[\d.]+)", RegexOptions.Compiled);
        static readonly Regex LampRx = new Regex(@"^LAMP_([a-z]+)_i([\d.]+)_r([\d.]+)_a([\d.]+)", RegexOptions.Compiled);
        static readonly Regex LensRx = new Regex(@"^LENS_[a-z0-9]+_r([\d.]+)", RegexOptions.Compiled);
        static readonly Regex ObstRx = new Regex(@"^OBST_r(\d+)", RegexOptions.Compiled);
        internal static readonly Color Red = new Color(1f, 0.16f, 0.08f), Green = new Color(0.25f, 1f, 0.3f), Amber = new Color(1f, 0.56f, 0.12f);
        /// <summary>Warm work-light colour (linear), a touch paler than the street sodium (soft pools, never blobs).</summary>
        internal static readonly Color Warm = new Color(1f, 0.74f, 0.47f);

        static string Clean(string n) { int k = n.IndexOf("__", System.StringComparison.Ordinal); return k >= 0 ? n.Substring(0, k) : n; }
        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        void Awake() { Build(); }
        void OnEnable() { BasePropDirector.Register(this); foreach (var l in lamps) if (l.fix != null && !NightBudgetShare.Fixtures.Contains(l.fix)) NightBudgetShare.Fixtures.Add(l.fix); }
        void OnApplicationQuit() { quitting = true; }
        void OnDestroy() { foreach (var l in lamps) if (l.fix != null) NightBudgetShare.Fixtures.Remove(l.fix); ReleaseObstacles(); }

        /// <summary>Parses the kit nodes once (play mode; the editor prefab builder also calls it for the report).</summary>
        public void Build()
        {
            if (built) return;
            built = true;
            mpb = new MaterialPropertyBlock();
            var all = GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                string n = Clean(t.name);
                if (n == "RUIN") { ruin = t; continue; }
                if (n.StartsWith("KEEP_RING_")) { ring[n.Substring(10)] = t.gameObject; continue; }
                if (n.StartsWith("TOPPLE_")) topples.Add(new Topple { t = t, id = n.Substring(7) });
                else if (n.StartsWith("SMOKE_")) smokes.Add(t);
                else if (n.StartsWith("FIRE_")) fires.Add(t);
                else if (n.StartsWith("SOLY_"))
                {
                    var tilt = Find(t, "SOLP_");
                    if (!tilt || !Find(tilt, "NRM_")) continue;
                    var nrm = Find(tilt, "NRM_");
                    panels.Add(new Panel
                    {
                        yaw = t, tilt = tilt, yawRest = t.localRotation, tiltRest = tilt.localRotation,
                        yawAxis = AxisOf(t), tiltAxis = AxisOf(tilt), yawAxisR = AxisRoot(t), tiltAxisR = AxisRoot(tilt),
                        nrmR = transform.InverseTransformDirection(nrm.position - tilt.position).normalized,
                    });
                }
                else if (n.StartsWith("SPIN_"))
                {
                    var m = SpinRx.Match(n);
                    spins.Add(new Spin { t = t, rest = t.localRotation, axis = AxisOf(t), speed = m.Success ? F(m.Groups[1].Value) : 90f });
                }
                else if (n.StartsWith("BLINK_"))
                {
                    var m = BlinkRx.Match(n);
                    if (!m.Success) continue;
                    blinks.Add(new Blink
                    {
                        at = t, period = Mathf.Max(0.2f, F(m.Groups[2].Value)), duty = Mathf.Clamp01(F(m.Groups[3].Value)),
                        radius = F(m.Groups[4].Value) / 100f, offset = m.Groups[5].Success ? F(m.Groups[5].Value) : 0f,
                        col = m.Groups[1].Value == "red" ? Red : m.Groups[1].Value == "green" ? Green : Amber,
                    });
                }
                else if (n.StartsWith("LAMP_"))
                {
                    var m = LampRx.Match(n);
                    if (!m.Success) continue;
                    lamps.Add(new Lamp { at = t, aim = Find(t, "AIM_"), intensity = F(m.Groups[2].Value), range = F(m.Groups[3].Value), angle = F(m.Groups[4].Value) });
                }
                else if (n.StartsWith("OBST_"))
                {
                    var m = ObstRx.Match(n);
                    if (!m.Success) continue;
                    var lp = transform.InverseTransformPoint(t.position);
                    obstLocal.Add(new Vector3(lp.x, lp.z, int.Parse(m.Groups[1].Value) / 100f));
                }
            }
            // the intact look = every renderer except the ruin and the ring (the low frame stays after the break)
            foreach (var t in all)
            {
                if (t == transform || !t.GetComponent<Renderer>()) continue;
                if (ruin && t.IsChildOf(ruin)) continue;
                if (Clean(t.name).StartsWith("KEEP_")) continue;
                intact.Add(t.gameObject);
            }
            if (ruin) ruin.gameObject.SetActive(false);
            upR = transform.InverseTransformDirection(Vector3.up).normalized;
            if (panels.Count > 0)
            {
                var h = Vector3.ProjectOnPlane(panels[0].nrmR, upR);
                faceR = h.sqrMagnitude > 1e-6f ? h.normalized : transform.InverseTransformDirection(Vector3.back);
            }
            SetGate(gate);
            BuildLights();
        }

        /// <summary>Shows the ring with its gate on `side` (S, E or W; anything else = closed ring). Editor bake and run time.</summary>
        public void SetGate(string side)
        {
            gate = string.IsNullOrEmpty(side) ? "S" : side.Substring(0, 1).ToUpperInvariant();
            if (ring.Count == 0)
                foreach (var t in GetComponentsInChildren<Transform>(true))
                {
                    string n = Clean(t.name);
                    if (n.StartsWith("KEEP_RING_")) ring[n.Substring(10)] = t.gameObject;
                }
            foreach (var kv in ring)
            {
                bool isGate = kv.Key.EndsWith("_GATE");
                string s = isGate ? kv.Key.Substring(0, kv.Key.Length - 5) : kv.Key;
                bool show = isGate ? s == gate : !(s == gate && ring.ContainsKey(s + "_GATE"));
                if (kv.Value.activeSelf != show) kv.Value.SetActive(show);
            }
        }

        /// <summary>First director tick: the set is where it will stay (instantiated sets are moved after Awake).</summary>
        internal void Place()
        {
            placed = true;
            seed = Mathf.Abs((transform.position.x * 13.37f + transform.position.z * 7.91f) % 97f);
            foreach (var s in spins) s.angle = seed * 37f % 360f;
            for (int i = 0; i < panels.Count; i++) panels[i].smooth = 0.9f + 0.1f * i;          // a ripple along the rows
            foreach (var l in lamps) { l.seed = Mathf.Repeat(seed * 0.37f + l.intensity, 1f); PlaceLamp(l); }
            RegisterObstacles();
            RecalcBounds();
        }

        internal void RecalcBounds()
        {
            bool any = false;
            foreach (var g in intact)
            {
                var r = g.GetComponent<Renderer>(); if (!r) continue;
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
            }
            if (!any) bounds = new Bounds(transform.position + Vector3.up, new Vector3(3.5f, 3f, 3.5f));
            bounds.Expand(0.6f);
        }

        static Transform Find(Transform parent, string prefix)
        {
            if (!parent) return null;
            foreach (Transform c in parent) if (Clean(c.name).StartsWith(prefix)) return c;
            return null;
        }

        /// <summary>The AXIS_ child (0.5 m along the axis at rest) -> the axis in the node's parent space.</summary>
        static Vector3 AxisOf(Transform node)
        {
            var a = Find(node, "AXIS_");
            var w = a ? (a.position - node.position) : node.up;
            if (w.sqrMagnitude < 1e-8f) w = Vector3.up;
            return node.parent ? node.parent.InverseTransformDirection(w.normalized).normalized : w.normalized;
        }

        Vector3 AxisRoot(Transform node)
        {
            var a = Find(node, "AXIS_");
            var w = a ? (a.position - node.position) : node.up;
            return transform.InverseTransformDirection(w.normalized).normalized;
        }

        // ---------------------------------------------------------------------------------------------- lights
        void BuildLights()
        {
            foreach (var b in blinks)
            {
                b.bulb = NightMeshes.Part("Bulb", b.at, NightMeshes.Sphere(), NightMeshes.Bulb(), false, Vector3.zero, Quaternion.identity,
                    Vector3.one * b.radius).GetComponent<Renderer>();
            }
            foreach (var l in lamps)
            {
                // the real light (the night budget grants it near the camera focus) + the fixture it is ranked by
                var go = new GameObject("BP_Light");
                go.transform.SetParent(l.at, false);
                var li = go.AddComponent<Light>();
                li.type = LightType.Spot; li.range = l.range; li.spotAngle = Mathf.Clamp(l.angle, 20f, 170f); li.innerSpotAngle = li.spotAngle * 0.45f;
                li.color = Warm.gamma; li.intensity = 0f; li.enabled = false; li.shadows = LightShadows.None; li.renderMode = LightRenderMode.ForcePixel;
                l.light = li;
                l.fix = new NightFixture
                {
                    Kind = NightKind.WorkLight, Id = $"baseprop_{setKey}_{GetInstanceID()}_{lamps.IndexOf(l)}", Light = li, Col = li.color, ColLinear = Warm,
                    Intensity = l.intensity, Range = l.range, Angle = li.spotAngle, Priority = 2.4f, ShadowCandidate = false,
                };
                // the soft ground pool (NightPool impostor, shown while the real light is not granted)
                var pg = NightMeshes.Part("Pool", transform, NightMeshes.Cube(), NightMeshes.Pool(), false, Vector3.zero, Quaternion.identity, Vector3.one);
                l.pool = pg.GetComponent<Renderer>(); l.pool.enabled = false;
            }
            // small lens glows on the flood heads (below the bloom threshold: a warm dot, never a blob)
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string n = Clean(t.name);
                if (!n.StartsWith("LENS_")) continue;
                var m = LensRx.Match(n);
                float r = m.Success ? F(m.Groups[1].Value) / 100f : 0.07f;
                Lamp owner = null; float best = 1e9f;
                foreach (var l in lamps) { float d = (l.at.position - t.position).sqrMagnitude; if (d < best) { best = d; owner = l; } }
                if (owner == null) continue;
                var lens = NightMeshes.Part("LensGlow", t, NightMeshes.Disc(), NightMeshes.Lens(), false, Vector3.zero, Quaternion.identity, new Vector3(r, r, 1f));
                var rr = lens.GetComponent<Renderer>(); rr.enabled = false;
                owner.lenses.Add(rr); owner.lensShown.Add(-1f);
            }
        }

        void PlaceLamp(Lamp l)
        {
            var dir = l.aim ? (l.aim.position - l.at.position).normalized : Vector3.down;
            if (dir.y > -0.2f) dir = (dir + Vector3.down).normalized;
            l.light.transform.rotation = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.forward : Vector3.up);
            foreach (var lens in l.lenses) lens.transform.rotation = Quaternion.LookRotation(dir);
            // pool under the light ray: centre where it meets the pad, a soft ellipse ~4 m (the installation plus a margin)
            float gy = transform.position.y + 0.045f;
            var o = l.at.position;
            float s = Mathf.Max(0.5f, (o.y - gy) / Mathf.Max(0.25f, -dir.y));
            var c = o + dir * s;
            float w = Mathf.Clamp(2f * s * Mathf.Tan(Mathf.Clamp(l.angle * 0.5f, 10f, 65f) * Mathf.Deg2Rad) * 0.55f, 2.8f, 4.4f);
            float len = Mathf.Min(w / Mathf.Max(0.55f, -dir.y), w * 1.45f);
            var hz = new Vector3(dir.x, 0f, dir.z);
            l.pool.transform.SetPositionAndRotation(new Vector3(c.x, gy + 0.3f, c.z), hz.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(hz.normalized, Vector3.up) : Quaternion.identity);
            l.pool.transform.localScale = new Vector3(w, 3.4f, dir.y < -0.9f ? w : len);
        }

        internal void SetColor(Renderer r, Color c)
        {
            if (!r) return;
            mpb.Clear(); mpb.SetColor(NightMeshes.ColorId, c); r.SetPropertyBlock(mpb);
        }

        // ---------------------------------------------------------------------------------------------- robot view obstacles
        /// <summary>Tall parts that overhang walkable tiles: robots sidestep them (UnitView.Obstacles, like the L17 lamp posts).</summary>
        internal void RegisterObstacles()
        {
            ReleaseObstacles();
            if (stage >= 2) return;
            var list = new List<Vector3>(obstLocal);
            if (kind == "supply" && ring.Count > 0)
            {   // the boom barrier post on the gate's end of the ring (build_base_props_v2.py boom_barrier: 0.7 m from the side middle)
                if (gate == "E") list.Add(new Vector3(1.55f, 0.7f, 0.12f));
                else if (gate == "W") list.Add(new Vector3(-1.55f, 0.7f, 0.12f));
                else list.Add(new Vector3(0.7f, -1.35f, 0.12f));
            }
            foreach (var o in list)
            {
                var w = transform.TransformPoint(new Vector3(o.x, 0f, o.y));
                var v = new Vector3(w.x, w.z, o.z);
                obstIdx.Add(UnitView.Obstacles.Count); obstVal.Add(v);
                UnitView.Obstacles.Add(v);
            }
        }

        /// <summary>True when NightLights (or anything else) rebuilt the shared list and our entries are gone.</summary>
        internal bool ObstaclesLost()
        {
            if (stage >= 2 || obstIdx.Count == 0) return false;
            var list = UnitView.Obstacles;
            for (int i = 0; i < obstIdx.Count; i++) { int k = obstIdx[i]; if (k >= list.Count || list[k] != obstVal[i]) return true; }
            return false;
        }

        internal void ReleaseObstacles()
        {
            var list = UnitView.Obstacles;
            for (int i = 0; i < obstIdx.Count; i++) { int k = obstIdx[i]; if (k < list.Count && list[k] == obstVal[i]) list[k] = Vector3.zero; }
            obstIdx.Clear(); obstVal.Clear();
        }

        // ---------------------------------------------------------------------------------------------- per frame (director)
        /// <summary>Animation step for a set inside the view (the director skips the rest).</summary>
        internal void Tick(float t, float dt, float night, Vector3 sunDir, bool sunUp)
        {
            if (stage >= 2) return;
            float spinMul = stage == 1 ? 0.35f : 1f;
            foreach (var s in spins)
            {
                s.angle = (s.angle + s.speed * spinMul * dt) % 360f;
                s.t.localRotation = Quaternion.AngleAxis(s.angle, s.axis) * s.rest;
            }
            if (panels.Count > 0 && stage == 0) Track(t, dt, night, sunDir, sunUp);
        }

        /// <summary>
        /// side (the rest pose = the camera direction, 55 deg); inside that they follow the sun's azimuth (+-18 deg) and elevation
        /// (22-46 deg tilt) and step to a new aim every 7-11 s, eased like a geared drive, the six pedestals a beat apart.</summary>
        void Track(float t, float dt, float night, Vector3 sunDirW, bool sunUp)
        {
            bool day = sunUp && night <= 0.5f;
            if (nextStep < 0f) nextStep = t + 1.5f + seed % 5f;
            if (t >= nextStep)
            {
                nextStep = t + 7f + (seed * 3.1f + step * 1.7f) % 4f;
                step++;
                seekYaw = day ? SeekYaw[step % SeekYaw.Length] : 0f;
                seekTilt = day ? SeekTilt[step % SeekTilt.Length] : 0f;
            }
            float yawT = 0f, tiltT = NightTilt;
            if (day)
            {
                var sun = transform.InverseTransformDirection(sunDirW).normalized;
                var h = Vector3.ProjectOnPlane(sun, upR);
                float az = h.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(faceR, h.normalized, upR) : 0f;
                if (Mathf.Abs(az) > 100f) az = 0f;                                                  // sun behind the array: hold the camera-facing aim
                float el = Mathf.Asin(Mathf.Clamp(Vector3.Dot(sun, upR), -1f, 1f)) * Mathf.Rad2Deg;
                yawT = Mathf.Clamp(Mathf.Clamp(az, -YawRange, YawRange) + seekYaw, -YawRange, YawRange);
                tiltT = Mathf.Clamp(Mathf.Clamp(90f - el, TiltMin, TiltMax) + seekTilt, TiltMin, TiltMax);
            }
            if (dt <= 0f) return;
            foreach (var p in panels)
            {
                p.yawCur = Mathf.Clamp(Mathf.SmoothDamp(p.yawCur, yawT, ref p.yawVel, p.smooth, 8f, dt), -YawRange, YawRange);
                p.tiltCur = Mathf.Clamp(Mathf.SmoothDamp(p.tiltCur, tiltT, ref p.tiltVel, p.smooth, 8f, dt), TiltMin, TiltMax);
                // wanted normal (root space) -> yaw about the pedestal axis, then tilt about the panel axis (v1 solver)
                float tr = p.tiltCur * Mathf.Deg2Rad;
                var d = Quaternion.AngleAxis(p.yawCur, upR) * (faceR * Mathf.Sin(tr) + upR * Mathf.Cos(tr));
                float yawA = Vector3.SignedAngle(Vector3.ProjectOnPlane(p.nrmR, p.yawAxisR), Vector3.ProjectOnPlane(d, p.yawAxisR), p.yawAxisR);
                var q = Quaternion.AngleAxis(yawA, p.yawAxisR);
                var pa = q * p.tiltAxisR; var n1 = q * p.nrmR;
                float tiltA = Vector3.SignedAngle(Vector3.ProjectOnPlane(n1, pa), Vector3.ProjectOnPlane(d, pa), pa);
                p.yaw.localRotation = Quaternion.AngleAxis(yawA, p.yawAxis) * p.yawRest;
                p.tilt.localRotation = Quaternion.AngleAxis(tiltA, p.tiltAxis) * p.tiltRest;
            }
        }

        /// <summary>Bulbs and work lights run on screen and off (MPB writes on a change only).</summary>
        internal void TickLights(float t, float dt, float night)
        {
            bool broken = stage >= 2;
            foreach (var b in blinks)
            {
                int s;
                if (broken) s = 0;
                else
                {
                    float ph = ((t + b.offset + seed * 0.37f) / b.period) % 1f;
                    bool on = ph < b.duty;
                    if (stage == 1 && Mathf.PerlinNoise(seed, t * 7f) < 0.42f) on = !on;           // damaged: flicker
                    s = on ? 2 : 1;
                }
                int key = s * 10 + Mathf.RoundToInt(night * 8f);
                if (key == b.state) continue;
                b.state = key;
                // below the bloom threshold (NightGrade 0.95 at night, 1.25 by day): a small clear dot, never a halo
                float bulbI = s == 2 ? Mathf.Lerp(1.1f, 0.82f, night) : s == 1 ? 0.1f : 0f;
                SetColor(b.bulb, b.col * bulbI);
                if (b.bulb) b.bulb.enabled = s > 0;
            }
            float tod = NightGrade.TimeOfDay, dusk = Mathf.Lerp(0.25f, 1f, night);
            foreach (var l in lamps)
            {
                bool want = !broken && !l.dead && NightGrade.LightsOn(tod, 0.345f + l.seed * 0.02f, 0.885f + l.seed * 0.01f);
                l.level = Mathf.MoveTowards(l.level, want ? 1f : 0f, dt / (want ? 1.1f : 0.5f));
                float lv = l.level;
                if (lv > 0f && lv < 0.97f && want) lv *= 0.55f + 0.45f * Mathf.PerlinNoise(l.seed * 9f, t * 11f);   // ballast warm-up flicker
                if (stage == 1 && Mathf.PerlinNoise(l.seed * 5f, t * 6f) < 0.3f) lv *= 0.25f;              // damaged: dips
                if (l.fix != null) l.fix.LightOut = lv;
                float realK = l.fix != null && NightBudgetShare.HookFrame >= Time.frameCount - 2 ? l.fix.RealK : 0f;
                if (l.light)
                {
                    float k = lv * realK;
                    bool on = k > 0.002f && l.light.gameObject.activeInHierarchy;
                    if (l.light.enabled != on) l.light.enabled = on;
                    if (on) l.light.intensity = l.intensity * k * 0.9f;                                   // ~1.3 at the pool centre (2.3 m): the street lamps' level (22.5 at 4.5 m)
                }
                // impostor pool (soft, ~4 m) cross-fades with the real light; the lens a warm dot below the bloom threshold
                float pk = 0.62f * dusk * lv * (1f - realK);
                Shade(l.pool, Warm * pk, ref l.poolShown);
                float lk = 0.78f * lv;
                for (int i = 0; i < l.lenses.Count; i++) { float sh = l.lensShown[i]; Shade(l.lenses[i], Warm * lk, ref sh); l.lensShown[i] = sh; }
            }
        }

        void Shade(Renderer r, Color c, ref float shown)
        {
            if (!r) return;
            float key = c.r + c.g * 1.7f + c.b * 0.6f;
            if (key <= 0.002f) { if (r.enabled) r.enabled = false; shown = 0f; return; }
            if (!r.enabled) r.enabled = true;
            if (shown > 0f && Mathf.Abs(key - shown) <= Mathf.Max(0.004f, shown * 0.012f)) return;
            shown = key; SetColor(r, c);
        }

        /// <summary></summary>
        internal void TickEffects(float t, bool onScreen)
        {
            if (stage >= 2) return;
            var z = OpenZFxPlayer.I; bool fx = z && z.isActiveAndEnabled;
            if (onScreen && fx && smokes.Count > 0 && t >= nextSmoke)
            {
                nextSmoke = t + (stage == 1 ? 0.16f : 0.3f) * Random.Range(0.8f, 1.25f);
                foreach (var s in smokes)
                {
                    var c = stage == 1 ? new Color(0.2f, 0.19f, 0.18f) : new Color(0.55f, 0.53f, 0.5f);
                    z.EmitChannel("leg_smoke", s.position, new Vector3(-0.35f + Random.Range(-0.1f, 0.1f), 0.95f + Random.Range(0f, 0.35f), Random.Range(-0.1f, 0.1f)),
                        stage == 1 ? 2.2f : 1.5f, stage == 1 ? 0.3f : 0.16f, c, stage == 1 ? 0.85f : 0.5f);
                }
            }
            if (stage == 1 && onScreen && kind != "fuel" && t >= nextSpark)
            {
                nextSpark = t + Random.Range(2.5f, 6f);
                Sparks(fires.Count > 0 ? fires[0].position : transform.position + Vector3.up, 10);
            }
            if (stage == 1 && kind == "fuel" && onScreen && fires.Count > 0 && t >= nextBurn)
            {
                nextBurn = t + 0.25f;
                if (fx) z.WreckSmoke(fires[0].position, 0.45f, true); else FxManager.I?.Burn(fires[0].position, 0.4f);
            }
            if (t >= nextHealthPoll && stage == 0)
            {
                nextHealthPoll = t + 0.25f;
                var g = ZGame.I;
                if (g != null && g.World != null)
                {
                    if (simId < 0) simId = ResolveSimId(g);
                    var o = simId >= 0 ? g.World.Get(simId) : null;
                    if (o != null && o.MaxHealth > 0 && o.Health > 0 && o.Health <= o.MaxHealth / 2) Damage();
                }
            }
        }

        int ResolveSimId(ZGame g)
        {
            var host = transform.parent ? transform.parent.gameObject : gameObject;
            foreach (var kv in g.StaticViews) if (kv.Value == host || kv.Value == gameObject) return kv.Key;
            return -2;
        }

        void Sparks(Vector3 at, int n)
        {
            var z = OpenZFxPlayer.I;
            if (z && z.isActiveAndEnabled)
                for (int i = 0; i < n; i++)
                {
                    var v = new Vector3(Random.Range(-2.2f, 2.2f), Random.Range(2.5f, 5.5f), Random.Range(-2.2f, 2.2f));
                    z.EmitChannel("leg_spark", at, v, Random.Range(0.35f, 0.7f), Random.Range(0.05f, 0.09f), new Color(1f, 0.78f, 0.35f), 1f);
                }
            else FxManager.I?.Pop(at, 0.4f);
            if (AudioDirector.Has("ngt_lamp_spark")) AudioDirector.Fire("ngt_lamp_spark", at, -1, 0.8f, Random.Range(0.9f, 1.15f));
        }

        // ---------------------------------------------------------------------------------------------- damage + break
        /// <summary>Half health: flicker, dark exhaust, slowed fan, jammed tracker, sparks or a small fuel fire.</summary>
        public void Damage()
        {
            if (stage >= 1) return;
            stage = 1;
            var at = fires.Count > 0 ? fires[0].position : transform.position + Vector3.up;
            Sparks(at, 16);
            if (AudioDirector.Has("dst_debris_metal")) AudioDirector.Fire("dst_debris_metal", at, -1, 0.55f, 1.1f);
            if (kind == "fuel") OzSoundV3.Ignite("bp_dmg_" + GetInstanceID(), at, 0.6f);                     // sound_v3: the flare-up
            if (kind == "fuel" && AudioDirector.I && AudioDirector.Has("dst_fire_loop")) AudioDirector.I.StartLoop("bp_dmg_" + GetInstanceID(), "dst_fire_loop", at, null, 0.45f);
            foreach (var b in blinks) b.state = -1;
        }

        /// <summary>Zod removed the hut: blast by kind, the kit breaks (chunks, ruin, fire, sounds). Idempotent.</summary>
        public void Break(Vector3 blastFrom, int id = -1)
        {
            if (stage >= 2) return;
            stage = 2;
            if (id >= 0) simId = id;
            ReleaseObstacles();
            if (!gameObject.activeInHierarchy) { BasePropDirector.Deferred(this, blastFrom); return; }
            StartCoroutine(BreakRoutine(blastFrom));
        }

        void OnDisable()
        {
            BasePropDirector.Unregister(this);
            foreach (var l in lamps) if (l.fix != null) { l.fix.LightOut = 0f; NightBudgetShare.Fixtures.Remove(l.fix); }
            if (quitting || !Application.isPlaying || stage >= 2 || !gameObject.scene.isLoaded) return;
            var g = ZGame.I;
            if (g == null || g.World == null) return;
            if (simId < 0) simId = ResolveSimId(g);
            if (simId >= 0 && g.World.Get(simId) == null) { stage = 2; ReleaseObstacles(); BasePropDirector.Deferred(this, transform.position + Random.insideUnitSphere * 2f); }
        }

        internal IEnumerator BreakRoutine(Vector3 blast)
        {
            var p = transform.position;
            var fxz = OpenZFxPlayer.I; bool fx = fxz && fxz.isActiveAndEnabled;
            var au = CombatAudio.I;
            Vector3 centre = p + Vector3.up * 0.7f;
            // lights out first: sparks and a pop of glass at each flood head
            foreach (var l in lamps)
            {
                l.dead = true; if (l.fix != null) l.fix.LightOut = 0f;
                if (l.light) l.light.enabled = false;
                if (l.pool) l.pool.enabled = false;
                foreach (var le in l.lenses) if (le) le.enabled = false;
                Sparks(l.at.position, 8);
                if (AudioDirector.Has("ngt_lamp_glass")) AudioDirector.Fire("ngt_lamp_glass", l.at.position, -1, 0.7f, Random.Range(0.95f, 1.1f));
            }
            // 1) the blast by kind (visual only: Zod's hut does no splash damage)
            switch (kind)
            {
                case "fuel":
                    Blast(centre + Vector3.up * 0.2f, 1.6f, 5.0f, FxBlastKind.Barrel);
                    au?.PlayPitched("explosion", p, LegacyMatch.GainHeavy, 0f, -1, 0.9f, LegacyMatch.BankHeavy);
                    break;
                case "generator":
                    Sparks(centre + Vector3.up * 0.2f, 24);
                    yield return new WaitForSeconds(0.14f);
                    Blast(centre, 1.15f, 4.2f, FxBlastKind.Vehicle);
                    au?.PlayPitched("explosion", p, LegacyMatch.GainExplosion, 0f, -1, 1f, LegacyMatch.BankExplosion);
                    break;
                case "solar":
                    Blast(centre, 0.6f, 2.8f, FxBlastKind.Crate);
                    Sparks(centre + Vector3.up * 0.4f, 24);
                    if (AudioDirector.Has("dst_glass")) AudioDirector.Fire("dst_glass", centre, -1, 1f, Random.Range(0.95f, 1.05f));
                    if (AudioDirector.Has("ngt_lamp_glass")) AudioDirector.Fire("ngt_lamp_glass", centre, -1, 0.8f, 0.9f);
                    foreach (var pn in panels) GlassBurst(pn.tilt.position, 9);                       // every panel shatters where it stands
                    break;
                case "comms":
                    Sparks(centre + Vector3.up * 0.3f, 22);
                    Blast(centre, 0.8f, 3.2f, FxBlastKind.Cannon);
                    au?.PlayPitched("explosion", p, LegacyMatch.GainExplosion * 0.8f, 0f, -1, 1.08f, LegacyMatch.BankExplosion);
                    if (AudioDirector.Has("dst_glass")) AudioDirector.Fire("dst_glass", centre + Vector3.up * 2f, -1, 0.6f, 1.2f);
                    break;
                default:                                                                             // supply
                    Blast(centre, 0.95f, 3.4f, FxBlastKind.Crate);
                    au?.PlayPitched("explosion", p, LegacyMatch.GainExplosion * 0.85f, 0f, -1, 1.05f, LegacyMatch.BankExplosion);
                    if (AudioDirector.Has("dst_debris_wood")) AudioDirector.Fire("dst_debris_wood", p, -1, 1f, 1f);
                    break;
            }
            ScorchDecals.Add(p, Random.Range(2.8f, 3.4f), ScorchKind.Explosion);
            if (fx) fxz.Dust(p + Vector3.up * 0.3f, 1.1f); else FxManager.I?.DemolitionDust(p, 2.6f, 2.6f, 0.4f);
            // 2) the kit goes, the ruin stays (the ring stays too); masts and light poles topple first
            var falling = new List<Topple>();
            foreach (var tp in topples) if (tp.t && tp.t.gameObject.activeInHierarchy) falling.Add(tp);
            foreach (var g in intact)
            {
                if (!g) continue;
                bool inFall = false;
                foreach (var tp in falling) if (g.transform.IsChildOf(tp.t)) { inFall = true; break; }
                if (!inFall) g.SetActive(false);
            }
            foreach (var b in blinks) if (b.bulb && (falling.Count == 0 || !IsUnderAny(b.at, falling))) b.bulb.enabled = false;
            if (ruin) ruin.gameObject.SetActive(true);
            Throw(p + Vector3.up * 0.6f, blast, kind == "fuel" ? 1.3f : 1f);
            if (kind == "fuel") StartCoroutine(Secondaries(p));
            if (kind == "supply") StartCoroutine(CookOff(p));
            foreach (var tp in falling) StartCoroutine(Fall(tp, blast, tp.id == "mast" ? 0f : Random.Range(0.08f, 0.3f)));
            // 3) flames on the ruin (fire light + crackle loop), the fuel burns longest
            float burn = kind == "fuel" ? 36f : kind == "generator" ? 22f : kind == "solar" ? 8f : kind == "comms" ? 10f : 18f;
            StartCoroutine(Burn(burn));
            if (AudioDirector.Has("dst_debris_metal")) { yield return new WaitForSeconds(1.3f); AudioDirector.Fire("dst_debris_metal", p, -1, 0.9f, 0.95f); }
        }

        static bool IsUnderAny(Transform t, List<Topple> list) { foreach (var x in list) if (t.IsChildOf(x.t)) return true; return false; }

        void Blast(Vector3 at, float s, float legRadius, FxBlastKind k)
        {
            var z = OpenZFxPlayer.I;
            float legacy = s / OpenZFxCatalog.LegacyToM12;
            if (z && z.isActiveAndEnabled) { FxManager.RaiseExploded(at, legacy); z.BlastSized(at, s, legRadius, k, GetInstanceID()); }
            else FxManager.I?.Explosion(at, legacy);
        }

        void GlassBurst(Vector3 at, int n)
        {
            var z = OpenZFxPlayer.I; if (!(z && z.isActiveAndEnabled)) { FxManager.I?.Pop(at, 1f); return; }
            for (int i = 0; i < n; i++)
            {
                var v = new Vector3(Random.Range(-3f, 3f), Random.Range(2f, 6f), Random.Range(-3f, 3f));
                z.EmitChannel(i % 3 == 0 ? "m12_pop" : "leg_spark_light", at + Random.insideUnitSphere * 0.4f, v, Random.Range(0.5f, 1.1f), Random.Range(0.05f, 0.1f),
                    i % 3 == 0 ? new Color(0.75f, 0.9f, 1f) : new Color(0.85f, 0.95f, 1f), 1f);
            }
        }

        IEnumerator Secondaries(Vector3 p)
        {
            // the tank and the drums cook off one after another (the drums are already flying from Throw())
            for (int i = 0; i < 3; i++)
            {
                yield return new WaitForSeconds(Random.Range(0.3f, 0.65f));
                var at = (fires.Count > i + 1 ? fires[i + 1].position : p + Random.insideUnitSphere) + Vector3.up * 0.2f;
                Blast(at, i == 0 ? 1.1f : 0.8f, 3.2f, FxBlastKind.Barrel);
                CombatAudio.I?.PlayPitched("explosion", at, LegacyMatch.GainExplosion * 0.7f, 0f, -1, Random.Range(1.05f, 1.2f), LegacyMatch.BankExplosion);
            }
        }

        IEnumerator CookOff(Vector3 p)
        {
            int n = Random.Range(7, 11);
            for (int i = 0; i < n; i++)
            {
                yield return new WaitForSeconds(Random.Range(0.18f, 0.55f));
                var at = p + new Vector3(Random.Range(-0.9f, 0.9f), Random.Range(0.3f, 0.9f), Random.Range(-0.7f, 0.9f));
                var z = OpenZFxPlayer.I;
                if (z && z.isActiveAndEnabled) z.Pop(at, 0.5f); else FxManager.I?.Pop(at, 0.5f);
                Sparks(at, 6);
                CombatAudio.I?.PlayPitched("cookoff", at, 0.32f, 0.05f, -1, Random.Range(1.1f, 1.4f), LegacyMatch.BankRifleSingle);
            }
        }

        /// <summary>A mast or a light pole falls away from the blast, hits the ground (dust, metal crash, shake), breaks into
        /// segments and is gone; the lamp heads / the dish fly.</summary>
        IEnumerator Fall(Topple tp, Vector3 blast, float delay)
        {
            var fall = tp.t;
            if (delay > 0f) yield return new WaitForSeconds(delay);
            var pivot = fall.position;
            float height = 2f;
            var rs = fall.GetComponentsInChildren<Renderer>(false);
            if (rs.Length > 0) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); height = Mathf.Max(0.8f, b.max.y - pivot.y); }
            var away = pivot - blast; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere; away.y = 0f; away.Normalize();
            away = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * away;
            var axis = Vector3.Cross(Vector3.up, away).normalized;
            var r0 = fall.rotation; var p0 = fall.position;
            bool mast = tp.id == "mast";
            float T = mast ? Random.Range(0.8f, 1.1f) : Random.Range(0.55f, 0.8f), t = 0f, maxA = Random.Range(78f, 88f);
            while (t < T)
            {
                t += Time.deltaTime; float k = Mathf.Clamp01(t / T);
                fall.SetPositionAndRotation(p0, r0); fall.RotateAround(pivot, axis, maxA * k * k);
                yield return null;
            }
            var tip = pivot + away * height * 0.92f;
            var z = OpenZFxPlayer.I;
            int dusts = mast ? 3 : 1;
            for (int i = 1; i <= dusts; i++)
            {
                var at = Vector3.Lerp(pivot, tip, i / (float)dusts);
                if (z && z.isActiveAndEnabled) z.Dust(at, mast ? 0.7f : 0.45f); else FxManager.I?.Dust(at, 0.5f);
            }
            if (mast)
            {
                if (AudioDirector.Has("dst_turret_crash")) AudioDirector.Fire("dst_turret_crash", tip, -1, 1f, 0.85f);
                else CombatAudio.I?.Play("metal", tip, 0.8f, 0.05f, -1, OzSoundV3.BankMetalHeavy);   // sound_v3
                OpenZFxShake.I?.Impact(tip, 0.25f);
                ThrowNamed(Vector3.Lerp(pivot, tip, 0.6f) + Vector3.up * 0.4f, blast, "mast_seg", 2, 0.55f);
                ThrowNamed(tip + Vector3.up * 0.5f, blast, "dish", 1, 0.6f);
            }
            else
            {
                if (AudioDirector.Has("dst_debris_metal")) AudioDirector.Fire("dst_debris_metal", tip, -1, 0.7f, 1.15f);
                ThrowNamed(Vector3.Lerp(pivot, tip, 0.5f) + Vector3.up * 0.3f, blast, "pole_seg", 1, 0.4f);
                ThrowNamed(tip + Vector3.up * 0.3f, blast, "lamp_head", 1, 0.45f);
            }
            fall.gameObject.SetActive(false);
            fall.SetPositionAndRotation(p0, r0);
        }

        IEnumerator Burn(float seconds)
        {
            var ad = AudioDirector.I; string loop = "bp_fire_" + GetInstanceID();
            if (ad) ad.StopLoop("bp_dmg_" + GetInstanceID(), 0.3f);
            if (fires.Count == 0) yield break;
            OzSoundV3.Ignite(loop, fires[0].position, kind == "fuel" ? 1f : 0.7f);                             // sound_v3: the flare-up
            if (ad && AudioDirector.Has("dst_fire_loop")) ad.StartLoop(loop, "dst_fire_loop", fires[0].position, null, kind == "fuel" ? 1f : 0.7f);
            float t0 = Time.time;
            int active = kind == "fuel" ? fires.Count : Mathf.Min(2, fires.Count);
            while (Time.time - t0 < seconds)
            {
                float k = 1f - (Time.time - t0) / seconds;
                var z = OpenZFxPlayer.I;
                for (int i = 0; i < active; i++)
                {
                    if (i > 0 && k < 0.45f) break;
                    var fp = fires[i].position; fp.y = Mathf.Min(fp.y, transform.position.y + 0.55f);
                    float sc = (kind == "fuel" ? 1.2f : 0.8f) * Mathf.Lerp(0.35f, 1f, k);
                    if (z && z.isActiveAndEnabled) z.WreckSmoke(fp, sc, true); else FxManager.I?.Burn(fp, sc);
                }
                yield return new WaitForSeconds(0.25f);
            }
            if (ad) ad.StopLoop(loop, 2f);
            float e0 = Time.time;
            while (Time.time - e0 < 12f)
            {
                var z = OpenZFxPlayer.I;
                if (z && z.isActiveAndEnabled) z.WreckSmoke(fires[0].position, 0.5f, false);
                yield return new WaitForSeconds(0.45f);
            }
        }

        // ---------------------------------------------------------------------------------------------- debris
        const int MaxLive = 72;
        static readonly List<GameObject> live = new List<GameObject>();

        void Throw(Vector3 at, Vector3 blast, float power)
        {
            if (chunks == null) return;
            bool hasMast = false, hasPole = false;
            foreach (var tp in topples) { if (tp.id == "mast") hasMast = true; else hasPole = true; }
            foreach (var m in chunks)
            {
                if (!m) continue;
                if (hasMast && (m.name.Contains("mast_seg") || m.name.Contains("dish"))) continue;      // thrown where the mast lands
                if (hasPole && (m.name.Contains("pole_seg") || m.name.Contains("lamp_head"))) continue;
                Spawn(m, at, blast, power, m.name.Contains("drum") ? 1.5f : m.name.Contains("panel") || m.name.Contains("shard") ? 1.2f : 1f);
            }
        }

        void ThrowNamed(Vector3 at, Vector3 blast, string part, int n, float power)
        {
            if (chunks == null) return;
            foreach (var m in chunks)
                if (m && m.name.Contains(part) && n-- > 0) Spawn(m, at, blast, power, 1f);
        }

        void Spawn(Mesh mesh, Vector3 at, Vector3 blast, float power, float lift)
        {
            live.RemoveAll(g => !g);
            if (live.Count >= MaxLive)
            {
                var old = live[0]; live.RemoveAt(0);
                if (old) { var orb = old.GetComponent<Rigidbody>(); if (orb) orb.isKinematic = true; PopOut.Begin(old.transform, PopOut.Style.Debris, null, true, false); }
            }
            var go = new GameObject("BaseChunk") { layer = BuildingBreaker.DebrisLayer };
            go.transform.SetPositionAndRotation(at + Random.insideUnitSphere * 0.7f + Vector3.up * Random.Range(0f, 0.5f), Random.rotation);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = (mesh.name.Contains("shard") || mesh.name.Contains("panel")) && glassMaterial ? glassMaterial : chunkMaterial;
            mr.shadowCastingMode = ShadowCastingMode.On;
            var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; mc.convex = true;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Lerp(8f, 60f, Mathf.InverseLerp(0.2f, 1.1f, mesh.bounds.size.magnitude));
            rb.linearDamping = 0.05f; rb.angularDamping = 0.35f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var away = go.transform.position - blast; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere; away.y = 0f; away.Normalize();
            away = Quaternion.Euler(0f, Random.Range(-60f, 60f), 0f) * away;
            rb.linearVelocity = (away * Random.Range(1.8f, 4.2f) + Vector3.up * Random.Range(6.5f, 10.5f) * lift) * power;
            rb.angularVelocity = Random.insideUnitSphere * Random.Range(3f, 10f);
            var life = go.AddComponent<RockChunkLife>(); life.lifeMin = Random.Range(9f, 14f);
            if (kind == "fuel" && (mesh.name.Contains("drum") || mesh.name.Contains("tank_shell"))) go.AddComponent<BasePropFlamingDebris>();
            live.Add(go);
        }
    }

    /// <summary></summary>
    public class BasePropFlamingDebris : MonoBehaviour
    {
        float age, next;
        void Update()
        {
            age += Time.deltaTime;
            if (age > 2.2f) { enabled = false; return; }
            if (Time.time < next) return;
            next = Time.time + 0.05f;
            var z = OpenZFxPlayer.I; if (!(z && z.isActiveAndEnabled)) return;
            float k = 1f - age / 2.2f;
            z.EmitChannel("leg_fire", transform.position, Random.insideUnitSphere * 0.4f + Vector3.up * 0.6f, 0.35f, 0.32f * k + 0.08f, OpenZFxCatalog.WreckFlameB, 1f);
            if (Random.value < 0.5f) z.EmitChannel("leg_smoke", transform.position, Vector3.up * 0.6f, 1.1f, 0.2f, OpenZFxCatalog.WreckSmokeDark, 0.8f);
        }
    }

    /// <summary>
    /// base_props_v2: drives every BaseProp from one Update: frustum test per set, animation only on screen, bulbs and work
    /// lights, exhaust puffs, the half-health poll, deferred breaks, and the robot obstacles (re-registered when the shared
    /// list was rebuilt). Created on demand, one per scene.
    /// </summary>
    [DefaultExecutionOrder(120)]
    public class BasePropDirector : MonoBehaviour
    {
        static BasePropDirector inst;
        static readonly List<BaseProp> props = new List<BaseProp>();
        readonly Plane[] planes = new Plane[6];
        Light sun;
        float nextSunLookup, nextObstCheck;
        public static int Count => props.Count;
        public static int VisibleCount { get; private set; }

        public static void Register(BaseProp p)
        {
            if (!props.Contains(p)) props.Add(p);
            if (!inst && Application.isPlaying)
            {
                var go = new GameObject("BasePropDirector"); inst = go.AddComponent<BasePropDirector>();
            }
        }

        public static void Unregister(BaseProp p) { props.Remove(p); }

        public static void Deferred(BaseProp p, Vector3 blast) { if (inst) inst.StartCoroutine(inst.ShowAndBreak(p, blast)); }

        IEnumerator ShowAndBreak(BaseProp p, Vector3 blast)
        {
            yield return null;
            if (!p) yield break;
            var host = p.transform.parent ? p.transform.parent.gameObject : p.gameObject;
            if (!host.activeSelf) host.SetActive(true);
            if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
            p.stage = 1;
            p.Break(blast);
        }

        void OnDestroy() { if (inst == this) inst = null; }

        void Update()
        {
            var cam = Camera.main;
            if (cam) GeometryUtility.CalculateFrustumPlanes(cam, planes);
            float t = Time.time, dt = Time.deltaTime, night = NightGrade.NightK;
            if (t >= nextSunLookup || !sun) { nextSunLookup = t + 2f; sun = RenderSettings.sun; if (!sun) { var dn = FindFirstObjectByType<DayNight>(); if (dn) sun = dn.sun; } }
            Vector3 sunDir = sun ? -sun.transform.forward : new Vector3(0.3f, 0.8f, -0.4f).normalized;
            bool sunUp = sunDir.y > 0.06f && night < 0.5f;
            bool obstCheck = t >= nextObstCheck;
            if (obstCheck) nextObstCheck = t + 2f;
            int vis = 0;
            for (int i = props.Count - 1; i >= 0; i--)
            {
                var p = props[i];
                if (!p) { props.RemoveAt(i); continue; }
                if (!p.placed) p.Place();
                else if (obstCheck && p.ObstaclesLost()) p.RegisterObstacles();
                bool on = !cam || GeometryUtility.TestPlanesAABB(planes, p.bounds);
                p.visible = on;
                if (on) { vis++; p.Tick(t, dt, night, sunDir, sunUp); }
                p.TickLights(t, dt, night);
                p.TickEffects(t, on);
            }
            VisibleCount = vis;
        }
    }
}
