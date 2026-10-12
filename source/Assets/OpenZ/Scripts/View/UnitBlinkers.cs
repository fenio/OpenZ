using System;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    // ------------------------------------------------------------------------------------------------ data (JSON)
    // meshes of the Z_* prefabs (prefab root frame: +y up; vehicles front = -z, buildings door = +z).

    [Serializable]
    public class UBElement
    {
        public string id, kind, tint, node, surface;
        public int ch;
        public float[] rgb, pos, nrm, tan, root, rootNrm, rootTan, p;
        public float nodeScale = 1f, size, halo, half, hh;
    }

    [Serializable]
    public class UBType { public string type, prefab, kind, label; public float[] boundsMin, boundsMax; public UBElement[] elements; }

    [Serializable] public class UBFile { public int version; public string note; public UBType[] types; }

    /// <summary>
    /// View side only; the Sim is never touched.
    /// <list type="bullet">
    /// <item><b>Elements</b> (unit_blinkers.json, 125 of them on 16 types, measured on the meshes 3 cm proud of the surface):
    /// team <b>beacons</b> (short double-phase flashes, A / B alternate), <b>antenna / mast tips</b> (slow team flash), rows of
    /// three <b>status LEDs</b> (status colour: green = fine, pulsing while a factory produces / the repair bay works, amber =
    /// badly damaged, fast red = engaging (units) or just hit (buildings); + an amber data LED flickering; + a steady team
    /// power LED), thin team <b>neon strips</b> on hull / wall panels, and warm <b>window glows</b> (buildings, cab windshields)
    /// that switch on one by one at dusk. They ADD to the lights VehicleLights, FactoryView, BuildingAnimView and NightLights
    /// already draw (checked apart by the measuring tool).</item>
    /// <item><b>Look</b>: HDR in linear space for bloom (NightGrade lowers the bloom threshold at night): soft and small by
    /// day (beacons ~30 %, LEDs ~40 %, neon ~12 %, windows off), full at night (NightGrade.NightK). Team colour =
    /// VehicleLights.TeamGlow (warm red / royal blue); empty vehicles and cannons (neutral) and neutral buildings are dark;
    /// destroyed = off. Every unit has its own blink phase and period (desynced, never in lockstep).</item>
    /// <item><b>Cost</b>: no Light components. One additive draw (OpenZ/UnitBlinkers) per mount node per unit, the mesh
    /// shared by every unit of a type; channels in a MaterialPropertyBlock updated only when they change; off channels
    /// collapse their quads in the vertex shader. No renderer is added inside a unit (TeamPaint, BoardingView, PopOut,
    /// BuildingBreaker never see them).</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(82)]   // after UnitView (60) placed the units, with VehicleLights (80)
    public class UnitBlinkers : MonoBehaviour
    {
        public static UnitBlinkers I { get; private set; }
        public static bool AutoBoot = true;
        public const string ResourceDir = "OpenZ/UnitBlinkers/";

        [Header("Channel intensity at full night (linear HDR)")]
        [Range(0f, 8f)] public float beacon = 3.0f, tip = 2.4f, led = 1.8f, dataLed = 1.5f, neon = 1.25f, window = 0.95f, power = 1.3f;
        [Header("By day (share of the night value)")]
        [Range(0f, 1f)] public float dayBeacon = 0.3f, dayTip = 0.3f, dayLed = 0.42f, dayNeon = 0.12f, dayPower = 0.35f;
        [Range(0f, 3f)] public float gain = 1f;
        public bool vehicles = true, cannons = true, buildings = true;
        public bool debugKeys = true;

        public int RigCount => rigs.Count;
        /// <summary></summary>
        public static float FullShare = 0.34f;
        public int FullUnits { get { int n = 0; foreach (var r in rigs) if (!r.IsBuilding && r.Full) n++; return n; } }
        public int UnitRigs { get { int n = 0; foreach (var r in rigs) if (!r.IsBuilding) n++; return n; } }

        ZGame game; Camera cam;
        Material mat;
        readonly Dictionary<string, UBType> specs = new Dictionary<string, UBType>();
        readonly List<Rig> rigs = new List<Rig>();
        readonly Dictionary<UnitView, Rig> rigOf = new Dictionary<UnitView, Rig>();
        readonly HashSet<string> unknown = new HashSet<string>();
        static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();
        float nextScan;
        int missingNodes;

        static readonly int ChAId = Shader.PropertyToID("_ChA"), ChBId = Shader.PropertyToID("_ChB"),
            TeamId = Shader.PropertyToID("_Team"), StatusId = Shader.PropertyToID("_Status");
        static readonly Color StatusOk = new Color(0.25f, 1f, 0.35f), StatusWarn = new Color(1f, 0.55f, 0.08f), StatusAlarm = new Color(1f, 0.12f, 0.06f);

        // ------------------------------------------------------------------ boot
        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Early() { if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => TryAttach(); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => TryAttach();

        public static void TryAttach()
        {
            if (!AutoBoot) return;
            var g = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (!g || FindFirstObjectByType<UnitBlinkers>()) return;
            new GameObject("UnitBlinkers").AddComponent<UnitBlinkers>();
        }

        void Awake()
        {
            if (I && I != this) { Destroy(this); return; }
            I = this;
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            cam = Camera.main;
            var ta = Resources.Load<TextAsset>(ResourceDir + "unit_blinkers");
            var file = ta ? JsonUtility.FromJson<UBFile>(ta.text) : null;
            if (file?.types != null) foreach (var t in file.types) if (t != null && !string.IsNullOrEmpty(t.type)) specs[t.type] = t;
            if (specs.Count == 0) Debug.LogWarning("[OpenZ] UnitBlinkers: Resources/OpenZ/UnitBlinkers/unit_blinkers.json missing");
            var m = Resources.Load<Material>(ResourceDir + "M_UnitBlinkers");                  // keeps the shader in player builds
            if (m) mat = new Material(m) { name = "UnitBlinkers (runtime)", hideFlags = HideFlags.DontSave };
            else
            {
                var sh = Shader.Find("OpenZ/UnitBlinkers");
                if (sh) mat = new Material(sh) { name = "UnitBlinkers (runtime)", hideFlags = HideFlags.DontSave };
                else Debug.LogWarning("[OpenZ] UnitBlinkers: shader OpenZ/UnitBlinkers missing");
            }
            if (!game || !mat || specs.Count == 0) enabled = false;
        }

        void Start()
        {
            if (!enabled) return;
            BuildBuildings();
            Scan();
            int n = 0; foreach (var r in rigs) n += r.Elements;
            Debug.Log($"[OpenZ] UnitBlinkers {game.level}: {rigs.Count} rigs, {n} blinkers ({specs.Count} types)" +
                      (missingNodes > 0 ? $"; {missingNodes} elements on the unit root (mount node missing: re-run ub_anchors.py)" : ""));
        }

        void OnDestroy() { if (I == this) I = null; if (mat) Destroy(mat); }

        // ------------------------------------------------------------------ rigs
        sealed class Rig
        {
            public ZObject Obj; public UnitView View; public GameObject Building; public UBType Spec; public bool IsBuilding;
            public readonly List<Transform> Mounts = new List<Transform>(); public readonly List<Mesh> Meshes = new List<Mesh>();
            public Matrix4x4 BuildingLocal = Matrix4x4.identity;
            public MaterialPropertyBlock Block = new MaterialPropertyBlock();
            public float Seed, PeriodA, PeriodTip, Power, StartAt = -10f, HitAt = -10f, WinOn;
            public int LastHealth = int.MinValue; public Team Owner; public bool WasOn;
            public Vector4 ChA, ChB, ShownA = new Vector4(-1, 0, 0, 0), ShownB; public Color ShownTeam = Color.clear, ShownStatus = Color.clear, Status;
            public bool Drawn, Full = true; public int Elements;
        }

        static float Hash(int n) { unchecked { uint x = (uint)n * 747796405u + 2891336453u; x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u; return ((x >> 22) ^ x) / 4294967295f; } }
        static Vector3 V3(float[] a, Vector3 f) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : f;

        Rig NewRig(ZObject o, UBType spec)
        {
            float s = Hash(o.Id * 7919 + 101);
            return new Rig { Obj = o, Spec = spec, Owner = o.Owner, Seed = s, PeriodA = 1.7f + 0.6f * Hash(o.Id * 31 + 7), PeriodTip = 2.8f + 0.9f * Hash(o.Id * 53 + 3),
                             WasOn = o.Owner != Team.Neutral && !o.Destroyed, Power = o.Owner != Team.Neutral && !o.Destroyed ? 1f : 0f };
        }

        void Scan()
        {
            for (int i = rigs.Count - 1; i >= 0; i--)
                if (!rigs[i].IsBuilding && !rigs[i].View) { rigOf.Remove(rigs[i].View); rigs.RemoveAt(i); }
            foreach (var kv in game.Views)
            {
                var v = kv.Value;
                if (!v || v.Obj == null || rigOf.ContainsKey(v)) continue;
                var k = v.Obj.Kind;
                if (!((k == ZKind.Vehicle && vehicles) || (k == ZKind.Cannon && cannons))) continue;
                if (!specs.TryGetValue(v.Obj.Type, out var spec))
                {
                    if (unknown.Add(v.Obj.Type)) Debug.LogWarning($"[OpenZ] UnitBlinkers: no blinkers for '{v.Obj.Type}' (unit_blinkers.json)");
                    rigOf[v] = null; continue;
                }
                var r = NewRig(v.Obj, spec); r.View = v;
                BuildUnit(r, v.transform);
                rigOf[v] = r; rigs.Add(r);
            }
        }

        void BuildUnit(Rig r, Transform root)
        {
            var nodes = new Dictionary<string, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (!nodes.ContainsKey(t.name)) nodes[t.name] = t;
            var groups = new Dictionary<Transform, List<UBElement>>();
            // vehicles / cannons (seeded per object) carry the full set; the rest keep only their team neon strips (team readability)
            r.Full = Hash(r.Obj.Id * 131 + 17) < FullShare;
            foreach (var e in r.Spec.elements)
            {
                if (e == null) continue;
                if (!r.Full && e.kind != "neon") continue;
                Transform m = root;
                if (!string.IsNullOrEmpty(e.node))
                {
                    if (nodes.TryGetValue(e.node, out var t)) m = t;
                    else missingNodes++;
                }
                if (!groups.TryGetValue(m, out var l)) groups[m] = l = new List<UBElement>();
                l.Add(e); r.Elements++;
            }
            foreach (var kv in groups)
            {
                bool rootFrame = kv.Key == root;
                string key = $"{r.Spec.type}|{kv.Key.name}|{(rootFrame ? "root" : "node")}|{(r.Full ? "full" : "neon")}";
                if (!meshCache.TryGetValue(key, out var mesh) || !mesh)
                    meshCache[key] = mesh = BuildMesh(key, kv.Value, e => rootFrame ? Frame.Root : Frame.Node, null);   // root: root-frame metres (also the fallback of a missing node)
                r.Mounts.Add(kv.Key); r.Meshes.Add(mesh);
            }
        }

        static Bounds LocalBounds(Transform root)
        {
            bool any = false; var b = new Bounds(); var w2l = root.worldToLocalMatrix;
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

        void BuildBuildings()
        {
            if (!buildings || game.World == null) return;
            foreach (var o in game.World.Objects)
            {
                if (o.Kind != ZKind.Building) continue;
                string key = o.Type == "fort_front" || o.Type == "fort_back" ? "fort" : o.Type;
                if (!specs.TryGetValue(key, out var spec)) continue;
                if (!game.StaticViews.TryGetValue(o.Id, out var go) || !go) continue;
                var mb = LocalBounds(go.transform);
                if (mb.size.x < 0.5f || mb.size.y < 0.5f) continue;
                if (spec.boundsMin != null && spec.boundsMax != null && spec.boundsMin.Length >= 3 && spec.boundsMax.Length >= 3)
                {
                    var want = V3(spec.boundsMax, Vector3.one) - V3(spec.boundsMin, Vector3.zero);
                    var got = mb.size;
                    if (Mathf.Abs(got.x / want.x - 1f) > 0.12f || Mathf.Abs(got.y / want.y - 1f) > 0.12f || Mathf.Abs(got.z / want.z - 1f) > 0.12f)
                        Debug.LogWarning($"[OpenZ] UnitBlinkers: {go.name} model bounds {got} differ from the measured {want}: check the blinkers, re-run ub_anchors.py");
                }
                var r = NewRig(o, spec); r.IsBuilding = true; r.Building = go;
                // element positions = fractions of the building's own bounds (like NightLights' rigs: a rescaled model moves them)
                string cacheKey = $"{spec.type}|bld|{mb.min.x:F2},{mb.min.y:F2},{mb.min.z:F2}|{mb.size.x:F2},{mb.size.y:F2},{mb.size.z:F2}";
                if (!meshCache.TryGetValue(cacheKey, out var mesh) || !mesh)
                    meshCache[cacheKey] = mesh = BuildMesh(cacheKey, new List<UBElement>(spec.elements), e => Frame.Bounds, mb);
                r.Mounts.Add(go.transform); r.Meshes.Add(mesh); r.Elements = spec.elements.Length;
                rigs.Add(r);
            }
        }

        // ------------------------------------------------------------------ meshes
        enum Frame { Node, Root, Bounds }

        static readonly Vector2[] Corners = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };

        /// <summary>The glow mesh of one mount (cached: every unit of a type shares it). Frame.Node = the element's node frame,
        /// Root = the prefab root frame (fallback / buildings in metres), Bounds = fractions of the building bounds.</summary>
        static Mesh BuildMesh(string key, List<UBElement> els, Func<UBElement, Frame> frameOf, Bounds? mb)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tg = new List<Vector4>(); var u0 = new List<Vector4>(); var u1 = new List<Vector4>(); var t = new List<int>();
            var b = new Bounds(); bool any = false; float maxR = 0.3f;
            void Quad(Vector3 c, Vector3 nrm, Vector3 tan, float half, float r, int mode, Color rgb, float ch)
            {
                int i0 = v.Count;
                for (int k = 0; k < 4; k++)
                {
                    v.Add(c); n.Add(nrm); tg.Add(new Vector4(tan.x, tan.y, tan.z, half));
                    u0.Add(new Vector4(Corners[k].x, Corners[k].y, r, mode)); u1.Add(new Vector4(rgb.r, rgb.g, rgb.b, ch));
                }
                t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
                if (!any) { b = new Bounds(c, Vector3.zero); any = true; } else b.Encapsulate(c);
                maxR = Mathf.Max(maxR, Mathf.Max(half, r));
            }
            foreach (var e in els)
            {
                var f = frameOf(e);
                Vector3 pos, nrm, tan; float unit = 1f;
                if (f == Frame.Node) { pos = V3(e.pos, Vector3.zero); nrm = V3(e.nrm, Vector3.up); tan = V3(e.tan, Vector3.right); unit = 1f / Mathf.Max(0.05f, e.nodeScale); }
                else if (f == Frame.Root) { pos = V3(e.root, Vector3.zero); nrm = V3(e.rootNrm, Vector3.up); tan = V3(e.rootTan, Vector3.right); }
                else
                {
                    var box = mb ?? new Bounds(Vector3.zero, Vector3.one);
                    var p = V3(e.p, Vector3.one * 0.5f);
                    pos = box.min + Vector3.Scale(p, box.size); nrm = V3(e.rootNrm, Vector3.up); tan = V3(e.rootTan, Vector3.right);
                }
                nrm = nrm.sqrMagnitude > 1e-6f ? nrm.normalized : Vector3.up; tan = tan.sqrMagnitude > 1e-6f ? tan.normalized : Vector3.right;
                var rgb = e.rgb != null && e.rgb.Length >= 3 ? new Color(e.rgb[0], e.rgb[1], e.rgb[2]) : Color.white;
                float ch = e.ch + (e.tint == "team" ? 10f : e.tint == "status" ? 20f : 0f);
                float size = e.size * unit, halo = e.halo * unit, half = e.half * unit, hh = e.hh * unit;
                switch (e.kind)
                {
                    case "beacon":
                    case "tip":
                        Quad(pos, Vector3.zero, tan, 0, size * 1.45f, 2, rgb, ch);                     // hot core
                        Quad(pos, Vector3.zero, tan, 0, halo, 0, rgb * 0.38f, ch);                     // halo
                        break;
                    case "led":
                        Quad(pos, nrm, tan, 0, size, 1, rgb, ch);                                      // lens on its panel
                        Quad(pos + nrm * (0.01f * unit), nrm, tan, 0, size * 1.3f, 2, rgb * 0.8f, ch); // hot core
                        Quad(pos + nrm * (0.02f * unit), nrm, tan, 0, halo, 0, rgb * 0.3f, ch);        // small halo
                        break;
                    case "neon":
                        Quad(pos, nrm, tan, half, hh, 4, rgb, ch);                                     // the strip on its panel
                        Quad(pos + nrm * (0.02f * unit), nrm, tan, half + halo, hh + halo, 5, rgb * 0.3f, ch);
                        break;
                    case "window":
                        Quad(pos, nrm, tan, half, hh, 4, rgb, ch);                                     // lit glass
                        Quad(pos + nrm * (0.03f * unit), nrm, tan, half + halo, hh + halo, 5, rgb * 0.22f, ch);
                        break;
                }
            }
            var m = new Mesh { name = "UB_" + key, hideFlags = HideFlags.DontSave };
            m.SetVertices(v); m.SetNormals(n); m.SetTangents(tg); m.SetUVs(0, u0); m.SetUVs(1, u1); m.SetTriangles(t, 0);
            b.Expand(maxR * 2f + 1f);                                                          // billboards / panels expand in the shader
            m.bounds = b;
            return m;
        }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            if (!game) return;
            float now = Time.time, dt = Time.deltaTime;
            if (now >= nextScan) { nextScan = now + 0.5f; Scan(); }
            float t = NightGrade.TimeOfDay, night = NightGrade.NightK;
            double simNow = game.World != null ? game.World.Time : 0;
            for (int i = 0; i < rigs.Count; i++) Step(rigs[i], t, night, now, dt, simNow);
            if (debugKeys && !global::OpenZ.OzRelease.IsRelease) DebugKeys();
        }

        static float Flash(float p) => p < 0.07f ? 1f : p < 0.2f ? Mathf.Lerp(1f, 0.06f, (p - 0.07f) / 0.13f) : 0.06f;

        void Step(Rig r, float tod, float night, float now, float dt, double simNow)
        {
            var o = r.Obj;
            bool alive = o != null && !o.Destroyed && (r.IsBuilding ? r.Building && r.Building.activeInHierarchy
                                                                   : r.View && r.View.isActiveAndEnabled && !r.View.Dying);
            var shownOwner = r.IsBuilding ? (o != null ? o.Owner : Team.Neutral) : CaptureHold.Shown(o);   // boarding_nb_v1 NB17
            bool on = alive && shownOwner != Team.Neutral;
            if (on && (!r.WasOn || shownOwner != r.Owner)) r.StartAt = now;                          // boarded / captured: a start-up flicker
            r.WasOn = on; if (o != null) r.Owner = shownOwner;
            r.Power = Mathf.MoveTowards(r.Power, on ? 1f : 0f, dt / (on ? 0.25f : 0.35f));
            float p = r.Power;
            if (now - r.StartAt < 0.9f) p *= Mathf.PerlinNoise(r.Seed * 17f, now * 22f) > 0.45f ? 1f : 0.1f;
            // hit detection from the health (buildings flash the status LEDs red for 3 s after a hit)
            if (o != null)
            {
                if (r.LastHealth != int.MinValue && o.Health < r.LastHealth) r.HitAt = now;
                r.LastHealth = o.Health;
            }
            bool damaged = o != null && o.MaxHealth > 0 && o.Health * 2 < o.MaxHealth;
            bool engaging = o != null && !r.IsBuilding && o.AttackObject != null;
            bool hit = now - r.HitAt < 3f;
            bool working = o != null && r.IsBuilding && ((o.IsProducer && o.Building) || o.RepairingUnit);
            float statusK; Color status;
            if (engaging || (r.IsBuilding && hit)) { status = StatusAlarm; statusK = Mathf.Repeat(now * 2.2f + r.Seed, 1f) < 0.5f ? 1f : 0.12f; }
            else if (damaged) { status = StatusWarn; statusK = 0.75f + 0.25f * Mathf.Sin(now * 3.1f + r.Seed * 6.28f); }
            else if (working) { status = StatusOk; statusK = 0.55f + 0.45f * Mathf.Sin(now * 6.3f + r.Seed * 6.28f); }
            else { status = StatusOk; statusK = 0.85f; }
            r.Status = status;

            float ph = now / r.PeriodA + r.Seed;
            float bA = Flash(Mathf.Repeat(ph, 1f)), bB = Flash(Mathf.Repeat(ph + 0.5f, 1f));
            float tipK = Flash(Mathf.Repeat(now / r.PeriodTip + r.Seed * 3.1f, 1f) * 1.4f);
            float busy = working || engaging ? 3.2f : 1.1f;
            float data = Mathf.PerlinNoise(r.Seed * 11f, now * busy * 2.3f) > 0.52f ? 1f : 0.1f;
            float neonFlick = 0.93f + 0.07f * Mathf.PerlinNoise(r.Seed * 5f, now * 9f);
            if (damaged && Mathf.PerlinNoise(r.Seed * 3f, now * 2.5f) > 0.8f) neonFlick *= Mathf.PerlinNoise(r.Seed * 9f, now * 30f) > 0.5f ? 1f : 0.15f;
            // windows switch on one by one at dusk (per building delay) and off at dawn, like the street lamps
            bool win = NightGrade.LightsOn(tod, 0.33f + 0.025f * r.Seed, 0.9f + 0.01f * r.Seed);
            r.WinOn = Mathf.MoveTowards(r.WinOn, win ? 1f : 0f, dt / 0.6f);
            float winFlick = Mathf.PerlinNoise(r.Seed * 13f, now * 0.7f) > 0.9f ? 0.55f : 1f;

            float dB = Mathf.Lerp(dayBeacon, 1f, night), dT = Mathf.Lerp(dayTip, 1f, night), dL = Mathf.Lerp(dayLed, 1f, night),
                  dN = Mathf.Lerp(dayNeon, 1f, night), dP = Mathf.Lerp(dayPower, 1f, night);
            float g = gain * p;
            r.ChA = new Vector4(beacon * bA * dB, beacon * bB * dB, tip * tipK * dT, led * statusK * dL) * g;
            r.ChB = new Vector4(dataLed * data * dL, neon * neonFlick * dN, window * r.WinOn * winFlick * Mathf.Max(night, 0.35f), power * dP) * g;
        }

        void LateUpdate()
        {
            if (!game) return;
            for (int i = 0; i < rigs.Count; i++) Apply(rigs[i]);
        }

        static bool Changed(Vector4 a, Vector4 b)
        {
            var d = a - b;
            return Mathf.Abs(d.x) + Mathf.Abs(d.y) + Mathf.Abs(d.z) + Mathf.Abs(d.w) > 0.01f + 0.01f * (Mathf.Abs(a.x) + Mathf.Abs(a.y) + Mathf.Abs(a.z) + Mathf.Abs(a.w));
        }

        void Apply(Rig r)
        {
            r.Drawn = false;
            if (r.IsBuilding ? !r.Building || !r.Building.activeInHierarchy : !r.View || !r.View.isActiveAndEnabled) return;
            float mx = Mathf.Max(Mathf.Max(Mathf.Max(r.ChA.x, r.ChA.y), Mathf.Max(r.ChA.z, r.ChA.w)), Mathf.Max(Mathf.Max(r.ChB.x, r.ChB.y), Mathf.Max(r.ChB.z, r.ChB.w)));
            if (mx <= 0.002f) return;
            var team = VehicleLights.TeamGlow(r.Owner);
            if (Changed(r.ChA, r.ShownA) || Changed(r.ChB, r.ShownB) || team != r.ShownTeam || r.Status != r.ShownStatus)
            {
                r.ShownA = r.ChA; r.ShownB = r.ChB; r.ShownTeam = team; r.ShownStatus = r.Status;
                r.Block.SetVector(ChAId, r.ChA); r.Block.SetVector(ChBId, r.ChB); r.Block.SetColor(TeamId, team); r.Block.SetColor(StatusId, r.Status);
            }
            for (int i = 0; i < r.Mounts.Count; i++)
            {
                var m = r.Mounts[i]; if (!m || !r.Meshes[i]) continue;
                Graphics.DrawMesh(r.Meshes[i], m.localToWorldMatrix, mat, m.gameObject.layer, null, 0, r.Block, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
                r.Drawn = true;
            }
        }

        // ------------------------------------------------------------------ debug
        void DebugKeys()
        {
            var kb = Keyboard.current; if (kb == null || !kb.f10Key.wasPressedThisFrame || !kb.altKey.isPressed || !kb.ctrlKey.isPressed) return;
            Debug.Log(Status());
        }

        public string Status()
        {
            int units = 0, blds = 0, drawn = 0, lit = 0, alarm = 0, warn = 0;
            foreach (var r in rigs)
            {
                if (r.IsBuilding) blds++; else units++;
                if (r.Drawn) drawn++;
                if (r.Power > 0.5f) lit++;
                if (r.Status == StatusAlarm) alarm++; else if (r.Status == StatusWarn) warn++;
            }
            return $"[OpenZ] UnitBlinkers {(game ? game.level : "?")}  t {NightGrade.TimeOfDay:F3} night {NightGrade.NightK:F2}: rigs {rigs.Count} " +
                   $"(units {units}, buildings {blds}), lit {lit}, drawn {drawn}, status red {alarm} / amber {warn}" +
                   (unknown.Count > 0 ? $", no spec for: {string.Join(", ", unknown)}" : "") +
                   (missingNodes > 0 ? $", {missingNodes} elements on a unit root (mount node missing)" : "");
        }
    }
}
