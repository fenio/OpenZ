using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// the Sim is not touched. One rig per radar, vehicle factory and repair station that has a static view:
    /// <list type="bullet">
    /// <item>Radar: the dish (mast, yoke, dish, feed) turns slowly on its mast axis (8 deg/s, eased) while a team owns the
    /// radar (Z: the radar works for its owner); neutral or destroyed it coasts to a stop. Destroyed: the dish is blown off
    /// whole like a tank turret (physics, tumbling, lands with dust + metal sound) and its fracture cells are removed so it
    /// is not there twice. The two roof antennas sway in the level wind; a beacon on each tip blinks in the owner colour.</item>
    /// <item>Vehicle factory: the roof fan spins up while the factory produces (Zod BUILDING and below the unit cap, same test
    /// as FactoryView), idles slowly when owned, windmills when neutral, stops when destroyed; motion-blurred.</item>
    /// <item>Repair station: the hoist hook hangs on its two cables and swings like a pendulum in the wind; while the station
    /// repairs a vehicle (Zod's 5 s, ZObject.RepairingUnit) it lowers into the bay, works, and rises again; the trolley
    /// lamps blink in the owner colour (fast while repairing).</item>
    /// </list>
    /// Model swap at run time: Resources/OpenZ/BuildingAnims/&lt;type&gt;_Split (the Blender split of the same imported GLB,
    /// transform) and the anim_* parts (children of the Model with their pivots). Every split mesh keeps the original's
    /// mesh bounds height (bounds keeper triangles), so TeamPaint's accent band is exactly where it was. If the prefab was
    /// already rebuilt from the split FBX, the anim_* nodes are used as they are. No split model: beacons only (logged).
    /// Execution order 75: Update sees the Sim state after ZGame (-50) ticked and before CombatView (50) handles this
    /// frame's deaths in LateUpdate (so the dish leaves before BuildingBreaker collapses the rest); LateUpdate (after 50)
    /// trims the fracture cells and places the beacons. Boots itself in level scenes (ZGame present); see README_IMPORT.md.
    /// </summary>
    [DefaultExecutionOrder(75)]
    public class BuildingAnimView : MonoBehaviour
    {
        public static BuildingAnimView I { get; private set; }
        /// <summary>Set false (e.g. in ZGame.Awake) to add BuildingAnimView by hand instead of the auto boot.</summary>
        public static bool AutoBoot = true;
        public const string ResourceDir = "OpenZ/BuildingAnims/";
        /// <summary>Audio hook: (key, building, position). Keys: radar_dish_start, radar_dish_stop, radar_dish_toss,
        /// radar_dish_land, repair_hoist_down, repair_hoist_up. While anything is subscribed no default sound plays
        /// (the only default is the metal landing of a tossed dish).</summary>
        public static event System.Action<string, ZObject, Vector3> AudioEvent;

        [Header("Radar dish")]
        [Range(0, 30)] public float dishDegPerSec = 8f;
        [Tooltip("seconds to reach full speed after a capture / to coast to a stop")] public float dishSpinUp = 3f, dishSpinDown = 4.5f;
        [Tooltip("destroyed radar: the dish is blown off whole (physics) instead of breaking with the building")] public bool tossDish = true;
        [Header("Vehicle factory roof fan (deg/s)")]
        public float fanProducing = 330f, fanIdle = 45f, fanNeutral = 12f;
        [Tooltip("seconds to reach ~95 % of the new speed")] public float fanSpinUp = 1.4f, fanSpinDown = 3f;
        [Header("Wind sway (antennas, hoist); wind = the Atmosphere's _FogWind")]
        [Range(0, 3)] public float swayStrength = 1f;
        [Tooltip("used when no Atmosphere sets _FogWind (m/s), = Atmosphere.cs desert / city")]
        public Vector3 fallbackWind = new Vector3(3f, 0, 1.5f), fallbackWindCity = new Vector3(1.2f, 0, 0.6f);
        [Header("Repair station hoist")]
        [Tooltip("metres the hook drops into the bay while a vehicle is repaired")] public float hookDrop = 0.9f;
        public float hookDownTime = 1.1f, hookUpTime = 1.6f;
        [Header("Beacons (owner colour)")]
        public bool beacons = true;
        [Range(0.5f, 6f)] public float beaconGain = 2.8f;

        ZGame game;
        readonly List<Rig> rigs = new List<Rig>();
        Vector3 windDir = Vector3.right; float windS = 1f, gust = 0.5f;
        static readonly int FogWindId = Shader.PropertyToID("_FogWind"), FlagWindId = Shader.PropertyToID("_OZFlagWind"), ColorId = Shader.PropertyToID("_Color");
        static Mesh sphereMesh, quadMesh;
        static readonly Dictionary<Mesh, Mesh> hulls = new Dictionary<Mesh, Mesh>();

        // ------------------------------------------------------------------ boot (same pattern as FactoryViews)
        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => TryAttach(); }
            TryAttach();
        }

        public static void TryAttach()
        {
            if (!AutoBoot || !ZGame.I) return;
            if (!FindFirstObjectByType<BuildingAnimView>()) new GameObject("BuildingAnimView").AddComponent<BuildingAnimView>();
        }

        void Awake() { I = this; }

        void Start()
        {
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            Build();
        }

        void OnDestroy()
        {
            foreach (var r in rigs) r.Dispose();
            rigs.Clear();
            if (I == this) I = null;
        }

        public void Build()
        {
            if (game == null || game.World == null) return;
            int n = 0, split = 0, lights = 0; var notes = new List<string>();
            foreach (var o in game.World.Objects)
            {
                if (o.Kind != ZKind.Building) continue;
                var spec = BuildingAnimSpec.For(o.Type); if (spec == null) continue;
                if (!game.StaticViews.TryGetValue(o.Id, out var go) || !go) continue;
                var r = new Rig(this, o, go, spec);
                if (!r.Init(notes)) { r.Dispose(); continue; }
                rigs.Add(r); n++; if (r.Split) split++; lights += r.BeaconCount;
            }
            Debug.Log($"[OpenZ] BuildingAnimView: {n} rigs ({split} split models swapped in, {lights} beacons)" + (notes.Count > 0 ? "; " + string.Join("; ", notes) : ""));
        }

        [ContextMenu("Rebuild rigs")]
        public void Rebuild() { foreach (var r in rigs) r.Dispose(); rigs.Clear(); Build(); }

        public string Status()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in rigs) sb.Append(r.Status()).Append('\n');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            if (game == null || game.World == null) return;
            float dtFx = game.paused ? 0f : Time.deltaTime * Mathf.Clamp(game.gameSpeed, 0.25f, 4f);   // machines follow game time
            float dtAmb = Mathf.Min(Time.deltaTime, 0.05f);                                                // wind is ambient (flags too)
            UpdateWind(Time.time);
            for (int i = 0; i < rigs.Count; i++) rigs[i].Tick(dtFx, dtAmb, Time.time);
        }

        void LateUpdate()
        {
            if (game == null || game.World == null) return;
            for (int i = 0; i < rigs.Count; i++) rigs[i].Late(Time.time);
        }

        void UpdateWind(float now)
        {
            Vector4 fw = Shader.GetGlobalVector(FogWindId);                        // Atmosphere.cs: desert (3, 0, 1.5), city (1.2, 0, 0.6)
            var wv = new Vector3(fw.x, 0, fw.z);
            if (wv.sqrMagnitude < 0.01f) { var fb = game.level == "L17" ? fallbackWindCity : fallbackWind; wv = new Vector3(fb.x, 0, fb.z); }
            float speed = Mathf.Max(wv.magnitude, 0.01f); windDir = wv / speed;
            windS = Mathf.Clamp(speed / 3.35f, 0.25f, 1.6f) * swayStrength;        // same scale as FlagView: desert 1, city 0.4
            Vector4 fl = Shader.GetGlobalVector(FlagWindId);                        // FlagView: (strength, gust, phase, time)
            if (fl.w > 0.01f) gust = fl.y;
            else gust = Mathf.Clamp01(0.45f + 0.4f * Mathf.Sin(now * 0.23f) * Mathf.Sin(now * 0.61f + 1.3f) + 0.3f * (Mathf.PerlinNoise(now * 0.37f, 7.1f) - 0.5f));
        }

        internal static void Sound(string key, ZObject b, Vector3 pos, params string[] defaults)
        {
            if (AudioEvent != null) { AudioEvent(key, b, pos); return; }
            if (defaults.Length > 0) CombatAudio.I?.Play(key, pos, 1f, 0.2f, b != null ? b.Id : -1, defaults);
        }

        // ------------------------------------------------------------------ shared meshes / material
        /// <summary>The factory team-light material (FactoryView.cs FactoryMeshes.LightMaterial: OpenZ/Overlay, HDR, unlit,
        /// depth-tested, queue 2460), shared; each beacon sets its own _Color through a MaterialPropertyBlock.</summary>
        static Material BeaconMaterial() => FactoryMeshes.LightMaterial();
        /// <summary>ZF23 proof counters: repair work bulb switched on / smoke-stack puffs.</summary>
        public static int RepairBulbOns, RepairStackPuffs;
        static Material stackMat;
        static Material StackMaterial()
        {
            if (stackMat) return stackMat;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            stackMat = new Material(sh) { name = "RepairStack_Gunmetal" };
            stackMat.SetColor("_BaseColor", new Color(0.27f, 0.26f, 0.25f)); stackMat.SetFloat("_Metallic", 0.35f); stackMat.SetFloat("_Smoothness", 0.5f);
            return stackMat;
        }

        static Mesh Sphere()
        {
            if (sphereMesh) return sphereMesh;
            const int U = 12, V = 8; var v = new List<Vector3>(); var t = new List<int>(); var c = new List<Color>();
            for (int j = 0; j <= V; j++)
                for (int i = 0; i <= U; i++)
                {
                    float th = Mathf.PI * j / V, ph = 2 * Mathf.PI * i / U;
                    v.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)) * 0.5f); c.Add(Color.white);
                }
            for (int j = 0; j < V; j++)
                for (int i = 0; i < U; i++)
                {
                    int a = j * (U + 1) + i, b = a + U + 1;
                    t.Add(a); t.Add(b); t.Add(a + 1); t.Add(a + 1); t.Add(b); t.Add(b + 1);
                }
            sphereMesh = new Mesh { name = "BeaconSphere" }; sphereMesh.SetVertices(v); sphereMesh.SetColors(c); sphereMesh.SetTriangles(t, 0); sphereMesh.RecalculateBounds();
            return sphereMesh;
        }

        static Mesh Quad()
        {
            if (quadMesh) return quadMesh;
            quadMesh = new Mesh { name = "BeaconQuad" };
            quadMesh.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) });
            quadMesh.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            quadMesh.SetTriangles(new List<int> { 0, 2, 1, 0, 3, 2 }, 0); quadMesh.RecalculateBounds();
            return quadMesh;
        }

        /// <summary>Collision hull source of a split part: its real triangles only (the two bounds keeper micro triangles,
        /// far below / above the part, would give the convex hull an invisible spike). Null if the mesh is not readable.</summary>
        static Mesh HullOf(Mesh m)
        {
            if (!m) return null;
            if (hulls.TryGetValue(m, out var h)) return h;
            if (!m.isReadable) { hulls[m] = null; return null; }
            var v = m.vertices; var t = m.triangles;
            var map = new Dictionary<int, int>(); var nv = new List<Vector3>(); var nt = new List<int>();
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-14f) continue;
                for (int k = 0; k < 3; k++)
                {
                    int s = t[i + k];
                    if (!map.TryGetValue(s, out int d)) { d = nv.Count; map[s] = d; nv.Add(v[s]); }
                    nt.Add(d);
                }
            }
            h = new Mesh { name = m.name + "_hull", indexFormat = nv.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            h.SetVertices(nv); h.SetTriangles(nt, 0); h.RecalculateBounds();
            hulls[m] = h; return h;
        }

        // ================================================================== one building
        class Beacon
        {
            public Transform anchor; public Vector3 local, normal; public Vector2 size; public bool sphere; public int phase;
            public Transform tr; public MeshRenderer mr;
        }

        class Rig
        {
            readonly BuildingAnimView m; readonly ZObject o; readonly GameObject go; readonly BuildingAnimSpec spec;
            public bool Split { get; private set; }
            public int BeaconCount => beacons.Count;
            string mode = "-";
            MeshFilter mf; MeshRenderer mr; Mesh origMesh; readonly List<GameObject> made = new List<GameObject>();
            Transform dish, fan, cable, hook, swing, scaler; readonly Transform[] ants = new Transform[2];
            Quaternion dishRest, fanRest, swingRest; readonly Quaternion[] antRest = new Quaternion[2];
            Vector3 dishAxis = Vector3.up, fanAxis = Vector3.up, hookRest; float cableLen = 1f, unitM = 1f;
            float dishAngle, dishSpeed, fanAngle, fanSpeed, drop, dropVel, kickAt;
            Vector2 swingA, swingV;
            bool wasDestroyed, wasActive, wasWorking; Team lastOwner;
            readonly float seed;
            readonly List<Beacon> beacons = new List<Beacon>();
            MaterialPropertyBlock mpb;
            GameObject wreck; Vector3 tossPivotLocal; int trimFrames;
            // machine housing; the bulb flashes and the stack puffs while the station repairs (the countdown: BridgeRepairBadges)
            GameObject extras; Transform bulbTr; MeshRenderer bulbMr; Light bulbLight; Vector3 stackTopW; bool bulbOn; float stackNext;

            public Rig(BuildingAnimView mgr, ZObject obj, GameObject g, BuildingAnimSpec s)
            {
                m = mgr; o = obj; go = g; spec = s;
                seed = Mathf.Repeat(obj.Id * 0.618034f, 1f);
            }

            public bool Init(List<string> notes)
            {
                // the building's model mesh: the biggest mesh with a real material (the Model child of the Z_* prefab)
                int best = 0;
                foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!f.sharedMesh || f.name.StartsWith("anim_")) continue;
                    var r = f.GetComponent<MeshRenderer>(); if (!r || !r.sharedMaterial) continue;
                    if (f.sharedMesh.vertexCount > best) { best = f.sharedMesh.vertexCount; mf = f; mr = r; }
                }
                if (!mf) { notes.Add($"{go.name}: no model mesh"); return false; }
                unitM = mf.transform.lossyScale.y;                                              // metres per model unit
                Split = AttachSplit(notes);
                if (dish) { dishRest = dish.localRotation; dishAxis = LocalUp(dish); dishAngle = seed * 360f; }
                if (fan) { fanRest = fan.localRotation; fanAxis = LocalUp(fan); fanAngle = seed * 360f; }
                for (int i = 0; i < 2; i++) if (ants[i]) antRest[i] = ants[i].localRotation;
                if (cable && hook) BuildHoist();
                if (m.beacons) BuildBeacons();
                if (spec.key == "repair") BuildRepairExtras();
                wasDestroyed = o.Destroyed; lastOwner = o.Owner; wasActive = !o.Destroyed && o.Owner != Team.Neutral;
                if (wasDestroyed && dish) dish.gameObject.SetActive(false);
                if (dish && wasActive) dishSpeed = m.dishDegPerSec;
                if (Split) { var tp = go.GetComponent<TeamPaint>(); if (tp) tp.Apply(); }            // new renderers get the owner colour + band
                return dish || fan || hook || ants[0] || ants[1] || beacons.Count > 0;
            }

            Vector3 LocalUp(Transform t) => t.parent ? t.parent.InverseTransformDirection(go.transform.up).normalized : Vector3.up;

            static Transform FindDeep(Transform root, string name)
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
                return null;
            }

            bool AttachSplit(List<string> notes)
            {
                // 1) prefab already rebuilt from the split FBX (README "prefab swap"): bind the anim_* nodes as they are
                bool inPrefab = true;
                foreach (var p in spec.parts) if (!FindDeep(go.transform, p.node)) { inPrefab = false; break; }
                if (inPrefab)
                {
                    foreach (var p in spec.parts) Bind(p.node, FindDeep(go.transform, p.node));
                    mode = "prefab split"; return true;
                }
                // 2) run-time swap from Resources
                var asset = Resources.Load<GameObject>(ResourceDir + spec.key + "_Split");
                if (!asset) { mode = "no split model"; notes.Add($"{spec.key}: no Resources/{ResourceDir}{spec.key}_Split (lights only)"); return false; }
                var body = FindDeep(asset.transform, spec.key + "_Body");
                var bodyMf = body ? body.GetComponent<MeshFilter>() : null;
                if (!bodyMf || !bodyMf.sharedMesh) { notes.Add($"{spec.key}_Split: no {spec.key}_Body mesh"); return false; }
                var toAsset = asset.transform.worldToLocalMatrix;
                var br = toAsset * body.localToWorldMatrix;
                if (!NearIdentity(br)) { notes.Add($"{spec.key}_Split: body node is not at the origin (import settings changed?), not swapped"); return false; }
                var ob = mf.sharedMesh.bounds; var nb = bodyMf.sharedMesh.bounds; float tol = 0.01f * ob.size.magnitude;
                if (Vector3.Distance(ob.min, nb.min) > tol || Vector3.Distance(ob.max, nb.max) > tol)
                { notes.Add($"{spec.key}_Split: bounds {nb.min}..{nb.max} do not match the model {ob.min}..{ob.max} (another imported model?), not swapped"); return false; }
                var found = new List<(BuildingAnimSpec.Part p, Mesh mesh, Matrix4x4 rel)>();
                foreach (var p in spec.parts)
                {
                    var t = FindDeep(asset.transform, p.node); var pf = t ? t.GetComponent<MeshFilter>() : null;
                    if (!pf || !pf.sharedMesh) { notes.Add($"{spec.key}_Split: node {p.node} missing, not swapped"); return false; }
                    var rel = toAsset * t.localToWorldMatrix;
                    Vector3 at = rel.GetColumn(3);
                    if (Vector3.Distance(at, p.pivot) > 0.01f) { notes.Add($"{spec.key}_Split: {p.node} pivot {at} is not {p.pivot} (mirrored / rescaled import?), not swapped"); return false; }
                    found.Add((p, pf.sharedMesh, rel));
                }
                origMesh = mf.sharedMesh;
                mf.sharedMesh = bodyMf.sharedMesh;                                         // same object, material, transform: the body
                var mats = mr.sharedMaterials;
                foreach (var (p, mesh, rel) in found)
                {
                    var g = new GameObject(p.node); g.layer = mf.gameObject.layer;
                    g.transform.SetParent(mf.transform, false);
                    g.transform.localPosition = rel.GetColumn(3); g.transform.localRotation = rel.rotation; g.transform.localScale = rel.lossyScale;
                    g.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = g.AddComponent<MeshRenderer>(); r.sharedMaterials = mats;
                    r.shadowCastingMode = mr.shadowCastingMode; r.receiveShadows = mr.receiveShadows;
                    r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;           // spinning blades / tossed dish blur
                    made.Add(g);
                    Bind(p.node, g.transform);
                }
                mode = "run-time split";
                return true;
            }

            static bool NearIdentity(Matrix4x4 a)
            {
                Vector3 pos = a.GetColumn(3); var s = a.lossyScale;
                return pos.magnitude < 1e-3f && Quaternion.Angle(a.rotation, Quaternion.identity) < 0.05f && Mathf.Abs(s.x - 1) < 1e-3f && Mathf.Abs(s.y - 1) < 1e-3f && Mathf.Abs(s.z - 1) < 1e-3f;
            }

            void Bind(string node, Transform t)
            {
                if (!t) return;
                switch (node)
                {
                    case "anim_dish": dish = t; break;
                    case "anim_fan": fan = t; break;
                    case "anim_antenna_1": ants[0] = t; break;
                    case "anim_antenna_2": ants[1] = t; break;
                    case "anim_hook_cable": cable = t; break;
                    case "anim_hook": hook = t; break;
                }
            }

            /// <summary>Hoist rig: Swing (at the cable top, building-aligned) -> CableScale (stretches the cable downwards) ->
            /// cable; the hook hangs from Swing and drops by the cable stretch.</summary>
            void BuildHoist()
            {
                var parent = cable.parent;
                swing = new GameObject("HoistSwing").transform; swing.SetParent(parent, false);
                swing.position = cable.position; swing.rotation = go.transform.rotation;
                scaler = new GameObject("CableScale").transform; scaler.SetParent(swing, false);
                cable.SetParent(scaler, true); hook.SetParent(swing, true);
                made.Add(swing.gameObject);
                swingRest = swing.localRotation; hookRest = hook.localPosition;
                cableLen = Mathf.Max(0.01f, -hookRest.y);                                       // swing-local units, cable top -> hook top
                unitM = swing.lossyScale.y;
            }

            void BuildBeacons()
            {
                mpb ??= new MaterialPropertyBlock();
                foreach (var b in spec.beacons)
                {
                    Transform anchor = null; Vector3 local = b.local;
                    if (!string.IsNullOrEmpty(b.node))
                    {
                        anchor = b.node == "anim_antenna_1" ? ants[0] : b.node == "anim_antenna_2" ? ants[1] : null;
                        if (!anchor) { anchor = mf.transform; local = b.fallback; }                 // no split: the original antenna spot
                    }
                    else anchor = mf.transform;
                    var g = new GameObject($"Beacon_{go.name}_{beacons.Count}"); g.transform.SetParent(m.transform, false);
                    g.AddComponent<MeshFilter>().sharedMesh = b.sphere ? Sphere() : Quad();
                    var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = BeaconMaterial();
                    r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off;
                    beacons.Add(new Beacon { anchor = anchor, local = local, normal = b.normal, size = b.size, sphere = b.sphere, phase = b.phase, tr = g.transform, mr = r });
                }
            }

            /// <summary>ZF23: exhaust stack (flange, pipe, rain cap) and a work bulb on a socket, standing on the housing roof found in the
            /// body mesh (highest vertex near the spot), in the model's own units; view only, hidden when the station is destroyed.</summary>
            void BuildRepairExtras()
            {
                var mt = mf.transform; var mesh = mf.sharedMesh; if (!mesh) return;
                var vs = mesh.vertices;
                float TopAt(float x, float z)
                {
                    float best = float.MinValue;
                    foreach (var v in vs) if ((v.x - x) * (v.x - x) + (v.z - z) * (v.z - z) < 0.03f * 0.03f && v.y > best) best = v.y;
                    return best == float.MinValue ? 0.4f : best;
                }
                extras = new GameObject($"RepairExtras_{go.name}"); extras.transform.SetParent(m.transform, false);
                extras.transform.SetPositionAndRotation(mt.position, mt.rotation); extras.transform.localScale = mt.lossyScale;
                var cyl = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx"); var sph = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
                GameObject Part(string n, Mesh pm, Material mat, Vector3 at, Vector3 sc, bool shadows)
                {
                    var g = new GameObject(n); g.layer = mf.gameObject.layer; g.transform.SetParent(extras.transform, false);
                    g.transform.localPosition = at; g.transform.localScale = sc;
                    g.AddComponent<MeshFilter>().sharedMesh = pm;
                    var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
                    r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    return g;
                }
                // stack: 0.36 m pipe, 1.3 m tall, on a flange, with a wider rain cap (u = model units per metre)
                float sx = 0.82f, sz = 0.25f, sy = TopAt(sx, sz);
                float u = 1f / Mathf.Max(0.01f, mt.lossyScale.y);
                var sm = StackMaterial();
                Part("Stack_Flange", cyl, sm, new Vector3(sx, sy + 0.04f * u, sz), new Vector3(0.5f * u, 0.04f * u, 0.5f * u), true);
                Part("Stack_Pipe", cyl, sm, new Vector3(sx, sy + 0.65f * u, sz), new Vector3(0.36f * u, 0.65f * u, 0.36f * u), true);
                Part("Stack_Cap", cyl, sm, new Vector3(sx, sy + 1.36f * u, sz), new Vector3(0.52f * u, 0.05f * u, 0.52f * u), true);
                stackTopW = extras.transform.TransformPoint(new Vector3(sx, sy + 1.45f * u, sz));
                // bulb: a short dark socket and a 0.26 m glass bulb (the beacon light material, driven per frame)
                float bx = 0.70f, bz = 0.36f, by = TopAt(bx, bz);
                Part("Bulb_Socket", cyl, sm, new Vector3(bx, by + 0.06f * u, bz), new Vector3(0.22f * u, 0.06f * u, 0.22f * u), true);
                var bulb = Part("Bulb_Glass", sph, BeaconMaterial(), new Vector3(bx, by + 0.24f * u, bz), Vector3.one * 0.26f * u, false);
                bulbTr = bulb.transform; bulbMr = bulb.GetComponent<MeshRenderer>(); bulbMr.lightProbeUsage = LightProbeUsage.Off;
                var lg = new GameObject("Bulb_Light"); lg.transform.SetParent(bulbTr, false);
                bulbLight = lg.AddComponent<Light>(); bulbLight.type = LightType.Point; bulbLight.color = new Color(1f, 0.64f, 0.3f);
                bulbLight.range = 7f; bulbLight.intensity = 0f; bulbLight.shadows = LightShadows.None; bulbLight.enabled = false;
                mpb ??= new MaterialPropertyBlock();
            }

            void RepairExtras(float dt, float now, bool own)
            {
                bool working = own && o.RepairingUnit;
                if (working != bulbOn)
                {
                    bulbOn = working;
                    if (working)
                    {   // the relay clunks, the stack coughs (a vent hiss follows with the puffs, rate limited)
                        RepairBulbOns++; stackNext = 0f;
                        AudioDirector.Fire("ngt_searchlight_on", bulbTr.position, o.Id, 0.45f, 1.3f);
                        AudioDirector.Fire("bld_factory_exhaust", stackTopW, o.Id, 0.5f, 1.12f);
                    }
                }
                // Z's work lamp: flashes while repairing (~1.6 Hz, soft edges), a faint ember when owned and idle, dark when neutral
                float k = working ? 0.3f + 0.7f * Mathf.SmoothStep(0f, 1f, Mathf.PingPong(now * 3.2f + seed, 1f)) : own ? 0.05f : 0f;
                var c = new Color(1f, 0.62f, 0.24f) * (0.04f + 3.4f * k); c.a = 1f;
                bulbMr.GetPropertyBlock(mpb); mpb.SetColor(ColorId, c); bulbMr.SetPropertyBlock(mpb);
                bulbLight.enabled = working; bulbLight.intensity = 3.2f * k;
                if (working && dt > 0f)
                {
                    stackNext -= dt;
                    if (stackNext <= 0f)
                    {
                        stackNext = Random.Range(0.28f, 0.45f); RepairStackPuffs++;
                        var fv = FactoryViews.I;
                        if (fv) fv.EmitPuff("repair", stackTopW, 0.22f, 1.05f, Random.Range(2.2f, 3f), Random.Range(1f, 1.4f));
                        OzSoundV3.VentPuff(stackTopW, o);
                    }
                }
            }

            // -------------------------------------------------------------- per frame
            public void Tick(float dt, float dtAmb, float now)
            {
                bool destroyed = o.Destroyed;
                if (destroyed != wasDestroyed) { if (destroyed) OnDestroyed(); else OnRevived(); wasDestroyed = destroyed; }
                lastOwner = o.Owner;
                if (destroyed) return;
                bool own = o.Owner != Team.Neutral;
                if (dish)
                {
                    bool active = own;                                                        // Z: the radar serves its owner
                    if (active != wasActive) { Sound(active ? "radar_dish_start" : "radar_dish_stop", o, dish.position); wasActive = active; }
                    float target = active ? m.dishDegPerSec : 0f;
                    float accel = m.dishDegPerSec / Mathf.Max(0.05f, active ? m.dishSpinUp : m.dishSpinDown);
                    dishSpeed = Mathf.MoveTowards(dishSpeed, target, accel * dt);            // constant acceleration: smooth start / coast
                    dishAngle = Mathf.Repeat(dishAngle + dishSpeed * dt, 360f);
                    dish.localRotation = Quaternion.AngleAxis(dishAngle, dishAxis) * dishRest;
                }
                if (fan)
                {
                    bool producing = Producing(own);
                    float target = producing ? m.fanProducing : own ? m.fanIdle : m.fanNeutral * Mathf.Lerp(0.6f, 1.4f, m.gust);
                    float tau = Mathf.Max(0.05f, (target > fanSpeed ? m.fanSpinUp : m.fanSpinDown) / 3f);
                    fanSpeed += (target - fanSpeed) * (1f - Mathf.Exp(-dt / tau));
                    fanAngle = Mathf.Repeat(fanAngle + fanSpeed * dt, 360f);
                    fan.localRotation = Quaternion.AngleAxis(fanAngle, fanAxis) * fanRest;
                }
                for (int i = 0; i < 2; i++)
                    if (ants[i]) ants[i].localRotation = Tilt(ants[i].parent, WindSway(now, seed + i * 0.37f, i == 0 ? 2.4f : 1.8f)) * antRest[i];
                if (swing) Hoist(dt, dtAmb, now, own);
                if (extras) RepairExtras(dt, now, own);
            }

            ZGame game => m.game;

            /// <summary>The factory's own FactoryView.Producing (FactoryView.cs:306, ticked by FactoryViews at order 70, before
            /// this Update); the same Zod test (owned, BUILDING, unit type set, below the unit cap) if FactoryViews is absent.</summary>
            bool Producing(bool own)
            {
                var fvs = FactoryViews.I;
                if (fvs && fvs.Views.TryGetValue(o.Id, out var fv) && fv) return fv.Producing;
                return own && o.Building && o.CurUnit != null && !game.World.UnitLimitReached(o.Owner);
            }

            /// <summary>Lean (x = downwind, y = cross wind, degrees) of a thin mast: steady lean with the gusts plus a ~0.55 Hz
            /// sway, all scaled by the level wind (desert 1, city 0.4).</summary>
            Vector2 WindSway(float t, float s, float amp)
            {
                float w = 2f * Mathf.PI * 0.55f, k = m.windS * amp;
                float lean = k * (0.35f + 0.45f * m.gust) + k * (0.6f + 0.6f * m.gust) * (0.65f * Mathf.Sin(w * t + s * 6.28f) + 0.35f * Mathf.Sin(1.9f * w * t + s * 11f));
                float cross = k * 0.4f * Mathf.Sin(1.3f * w * t + s * 17f);
                return new Vector2(lean, cross);
            }

            /// <summary>Rotation (in parent space) that tips the building's up axis by a (downwind, cross wind) degrees.</summary>
            Quaternion Tilt(Transform parent, Vector2 a)
            {
                Vector3 up = parent ? parent.InverseTransformDirection(go.transform.up).normalized : Vector3.up;
                Vector3 wd = parent ? parent.InverseTransformDirection(m.windDir) : m.windDir;
                wd = Vector3.ProjectOnPlane(wd, up); if (wd.sqrMagnitude < 1e-6f) wd = Vector3.Cross(up, Vector3.forward);
                wd.Normalize(); var cr = Vector3.Cross(up, wd);
                var dir = up + Mathf.Tan(a.x * Mathf.Deg2Rad) * wd + Mathf.Tan(a.y * Mathf.Deg2Rad) * cr;
                return Quaternion.FromToRotation(up, dir.normalized);
            }

            void Hoist(float dt, float dtAmb, float now, bool own)
            {
                bool working = own && o.RepairingUnit;                                          // Zod's flat 5 s repair (brepair.cpp)
                if (working != wasWorking)
                {
                    Sound(working ? "repair_hoist_down" : "repair_hoist_up", o, hook.position);
                    swingV += Random.insideUnitCircle * 4f;                                     // the start / stop jerks the hook
                    wasWorking = working;
                }
                float want = working ? m.hookDrop + 0.05f * Mathf.Sin(now * 2f * Mathf.PI * 1.3f + seed * 6f) : 0f;
                if (dt > 0) drop = Mathf.SmoothDamp(drop, want, ref dropVel, (working ? m.hookDownTime : m.hookUpTime) * 0.45f, Mathf.Infinity, dt);
                if (working && now > kickAt) { swingV += Random.insideUnitCircle * 5f; kickAt = now + Random.Range(0.5f, 0.9f); }   // work jolts
                // damped pendulum (degrees), natural period of the real length (cable + half the hook, + the drop)
                float L = Mathf.Max(0.5f, cableLen * unitM + 0.7f + drop);
                float w0 = Mathf.Sqrt(9.81f / L), zeta = 0.12f;
                var target = new Vector2(m.windS * 1.3f * (0.4f + 1.2f * m.gust), m.windS * 0.4f * Mathf.Sin(now * 0.7f + seed * 9f));
                var acc = -w0 * w0 * (swingA - target) - 2f * zeta * w0 * swingV;
                swingV += acc * dtAmb; swingA += swingV * dtAmb;
                swingA = Vector2.ClampMagnitude(swingA, 9f);
                swing.localRotation = Tilt(swing.parent, swingA) * swingRest;
                float d = drop / Mathf.Max(1e-4f, unitM);                                        // metres -> swing-local units
                scaler.localScale = new Vector3(1f, (cableLen + d) / cableLen, 1f);
                hook.localPosition = hookRest - new Vector3(0, d, 0);
            }

            public void Late(float now)
            {
                if (trimFrames > 0) TrimFracture();
                if (extras)
                {
                    bool showX = !o.Destroyed && go.activeInHierarchy;
                    if (extras.activeSelf != showX) { extras.SetActive(showX); if (!showX) bulbOn = false; }
                }
                if (beacons.Count == 0) return;
                bool show = !o.Destroyed && go.activeInHierarchy && m.beacons;
                bool own = o.Owner != Team.Neutral;
                var team = TeamColors.Of(o.Owner).linear;
                for (int i = 0; i < beacons.Count; i++)
                {
                    var b = beacons[i];
                    if (!b.tr) continue;
                    if (b.tr.gameObject.activeSelf != show) b.tr.gameObject.SetActive(show);
                    if (!show || !b.anchor) continue;
                    float us = b.anchor.lossyScale.x;
                    b.tr.position = b.anchor.TransformPoint(b.local);
                    if (b.sphere) { b.tr.rotation = Quaternion.identity; b.tr.localScale = Vector3.one * b.size.x * us; }
                    else
                    {
                        b.tr.rotation = b.anchor.rotation * Quaternion.LookRotation(-b.normal, Vector3.up);   // quad front (-z) faces out
                        b.tr.localScale = new Vector3(b.size.x * us, b.size.y * us, 1f);
                    }
                    float k = BlinkLevel(now, b.phase, own);
                    var c = own ? team * (m.beaconGain * k) : new Color(0.03f, 0.03f, 0.035f);
                    if (own && k < 0.25f) c = Color.Lerp(new Color(0.03f, 0.03f, 0.035f), team * 0.35f, k * 4f);
                    c.a = 1f;
                    b.mr.GetPropertyBlock(mpb); mpb.SetColor(ColorId, c); b.mr.SetPropertyBlock(mpb);
                }
            }

            /// <summary>Radar: short bright flashes, the two tips alternating; repair lamps: slow alternate blink, fast while repairing.</summary>
            float BlinkLevel(float now, int phase, bool own)
            {
                if (!own) return 0f;
                float t = now + seed * 3f;
                if (spec.key == "radar")
                {
                    float p = Mathf.Repeat(t / 1.8f + phase * 0.5f, 1f);
                    return p < 0.12f ? 1f : p < 0.2f ? Mathf.Lerp(1f, 0.15f, (p - 0.12f) / 0.08f) : 0.15f;
                }
                float period = o.RepairingUnit ? 0.5f : 2.2f;
                float q = Mathf.Repeat(t / period + phase * 0.5f, 1f);
                return q < 0.45f ? 1f : 0.12f;
            }

            // -------------------------------------------------------------- destruction / revival
            void OnDestroyed()
            {
                wasActive = false; dishSpeed = 0f; fanSpeed = 0f; drop = 0f; swingA = swingV = Vector2.zero;
                if (swing) { swing.localRotation = swingRest; scaler.localScale = Vector3.one; hook.localPosition = hookRest; }
                if (dish && dish.gameObject.activeSelf)
                {
                    // BuildingBreaker.Collapse (CombatView LateUpdate, this frame) hides and fractures what is still active
                    if (m.tossDish) TossDish();
                    dish.gameObject.SetActive(false);
                }
            }

            void OnRevived()
            {
                if (dish) { dish.gameObject.SetActive(true); dishSpeed = 0f; }
                if (wreck) { var w = wreck.GetComponent<DishWreck>(); if (w) w.PopNow(); wreck = null; }
                trimFrames = 0;
            }

            /// <summary>The whole dish is blown off like a Z tank turret (UnitView.TossTurret), but with physics: it tumbles,
            /// bounces and settles on the terrain, charred, then pops out like the other debris.</summary>
            void TossDish()
            {
                var src = dish; var smf = src.GetComponent<MeshFilter>(); var smr = src.GetComponent<MeshRenderer>();
                if (!smf || !smr) return;
                var w = new GameObject(go.name + "_dish_wreck");
                w.transform.SetPositionAndRotation(src.position, src.rotation); w.transform.localScale = src.lossyScale;
                w.layer = BuildingBreaker.DebrisLayer;                                          // hits the terrain, not the building cells
                w.AddComponent<MeshFilter>().sharedMesh = smf.sharedMesh;
                var r = w.AddComponent<MeshRenderer>(); r.sharedMaterials = smr.sharedMaterials; r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                var tp = w.AddComponent<TeamPaint>(); tp.Team = lastOwner; tp.Charred = 0.55f;
                var hull = HullOf(smf.sharedMesh);
                if (hull) { var mc = w.AddComponent<MeshCollider>(); mc.sharedMesh = hull; mc.convex = true; }
                else { var bc = w.AddComponent<BoxCollider>(); bc.center = (spec.dishMin + spec.dishMax) * 0.5f; bc.size = spec.dishMax - spec.dishMin; }
                var rb = w.AddComponent<Rigidbody>();
                rb.mass = 160f; rb.linearDamping = 0.25f; rb.angularDamping = 0.8f; rb.maxAngularVelocity = 12f;
                rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                var root = go.transform;
                var away = src.position - root.position; away.y = 0;
                var rnd = Quaternion.Euler(0, (seed - 0.5f) * 140f + Random.Range(-30f, 30f), 0);
                away = away.sqrMagnitude > 0.04f ? rnd * away.normalized : rnd * root.forward;
                rb.linearVelocity = away * Random.Range(2.6f, 4.2f) + Vector3.up * Random.Range(7.5f, 9.5f);
                rb.angularVelocity = Random.onUnitSphere * Random.Range(1.6f, 3.2f);
                var dw = w.AddComponent<DishWreck>(); dw.b = o; dw.life = 22f;
                wreck = w;
                tossPivotLocal = root.InverseTransformPoint(src.position);
                trimFrames = 4;
                FxManager.I?.Explosion(src.position + Vector3.up * 0.4f, 1.15f);
                Sound("radar_dish_toss", o, src.position);
            }

            /// <summary>BuildingBreaker.Collapse instantiates the fracture cells as "&lt;building&gt;_fracture" (BuildingBreaker.cs:56);
            /// the cells cut from the dish are removed (the dish flies whole).</summary>
            void TrimFracture()
            {
                trimFrames--;
                var fr = GameObject.Find(go.name + "_fracture");
                if (!fr) return;
                trimFrames = 0;
                var root = go.transform; int n = 0;
                foreach (Transform c in fr.transform)
                {
                    var rr = c.GetComponent<Renderer>(); if (!rr) continue;
                    var p = root.InverseTransformPoint(rr.bounds.center);
                    float d = new Vector2(p.x - tossPivotLocal.x, p.z - tossPivotLocal.z).magnitude;
                    if (p.y > tossPivotLocal.y + 0.3f && d < spec.dishReach) { Object.Destroy(c.gameObject); n++; }
                }
                if (n > 0) Debug.Log($"[OpenZ] BuildingAnimView: {go.name} dish tossed whole, {n} dish fracture cells removed");
            }

            public void Dispose()
            {
                foreach (var b in beacons) if (b.tr) Object.Destroy(b.tr.gameObject);
                beacons.Clear();
                if (origMesh && mf) mf.sharedMesh = origMesh;
                if (swing && cable && hook) { cable.SetParent(swing.parent, true); hook.SetParent(swing.parent, true); }
                bool ownDish = dish && !made.Contains(dish.gameObject);                        // prefab-split dish: show it again
                foreach (var g in made)
                    if (g) { g.name = "_disposed"; g.SetActive(false); g.transform.SetParent(null, false); Object.Destroy(g); }   // Destroy is deferred: never found again by a rebuild this frame
                made.Clear();
                if (ownDish) dish.gameObject.SetActive(true);
                if (wreck) Object.Destroy(wreck);
            }

            public string Status() =>
                $"{go.name} [{mode}] owner {o.Owner}{(o.Destroyed ? " DESTROYED" : "")}" +
                (dish ? $" dish {dishSpeed:F1} deg/s" : "") + (fan ? $" fan {fanSpeed:F0} deg/s" : "") +
                (swing ? $" hook drop {drop:F2} m swing {swingA.magnitude:F1} deg" : "") + $" beacons {beacons.Count}";
        }

        /// <summary></summary>
        public class DishWreck : MonoBehaviour
        {
            public ZObject b; public float life = 22f;
            float age, lastHit = -10f, popAt = -1f; int hits; Vector3 baseScale;
            void Start() { baseScale = transform.localScale; }
            public void PopNow() { if (popAt < 0) popAt = age; }
            void OnCollisionEnter(Collision c)
            {
                float speed = c.relativeVelocity.magnitude;
                if (age < 0.25f || speed < 1.5f || age - lastHit < 0.3f || hits >= 5) return;
                lastHit = age; hits++;
                var p = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
                FxManager.I?.Dust(p, Mathf.Min(1.4f, 0.35f + speed * 0.06f));
                if (hits == 1)
                {
                    BuildingAnimView.Sound("radar_dish_land", b, p, OzSoundV3.BankMetalHeavy);   // sound_v3
                }
            }
            void Update()
            {
                age += Time.deltaTime;
                if (popAt < 0 && age >= life) popAt = age;
                if (popAt < 0) return;
                float t = age - popAt;                                                          // debrisPopScale
                float s = t < 0.14f ? 1f + 0.12f * t / 0.14f : 1.12f * (1f - Mathf.Clamp01((t - 0.14f) / 0.36f));
                transform.localScale = baseScale * Mathf.Max(0.001f, s);
                if (t >= 0.5f) Destroy(gameObject);
            }
        }
    }

    /// <summary>
    /// Per building type: the split parts with their pivots in the Model's local space (Unity axes = the GLB's x flipped by
    /// rescaled import is refused instead of drawn wrong) and the beacons (model units; fallback = the spot on the original,
    /// unsplit model).
    /// </summary>
    public class BuildingAnimSpec
    {
        public struct Part { public string node; public Vector3 pivot; }
        public struct BeaconDef { public string node; public Vector3 local, fallback, normal; public Vector2 size; public bool sphere; public int phase; }
        public string key; public Part[] parts; public BeaconDef[] beacons = new BeaconDef[0];
        public Vector3 dishMin, dishMax; public float dishReach = 2.7f;                          // dish extents (collider fallback), metres swept

        public static BuildingAnimSpec For(string type)
        {
            switch (type)
            {
                case "radar":
                    return new BuildingAnimSpec
                    {
                        key = "radar",
                        parts = new[]
                        {
                            new Part { node = "anim_dish", pivot = new Vector3(0.00107f, 0.14066f, -0.06845f) },
                            new Part { node = "anim_antenna_1", pivot = new Vector3(0.42175f, 0.11052f, 0.48143f) },   // moved 3.5 m to the front-right corner (clear of the dish)
                            new Part { node = "anim_antenna_2", pivot = new Vector3(-0.39458f, -0.00126f, 0.43362f) },
                        },
                        beacons = new[]
                        {
                            new BeaconDef { node = "anim_antenna_1", local = new Vector3(-0.00493f, 0.43129f, -0.00187f), fallback = new Vector3(0.35402f, 0.54558f, -0.39714f), size = new Vector2(0.05f, 0.05f), sphere = true, phase = 0 },
                            new BeaconDef { node = "anim_antenna_2", local = new Vector3(-0.00067f, 0.17030f, 0.00352f), fallback = new Vector3(-0.39525f, 0.16904f, 0.43714f), size = new Vector2(0.045f, 0.045f), sphere = true, phase = 1 },
                        },
                        dishMin = new Vector3(-0.4805f, 0f, -0.1425f), dishMax = new Vector3(0.3536f, 0.8078f, 0.6177f), dishReach = 2.7f,
                    };
                case "vehicle_factory":
                    return new BuildingAnimSpec
                    {
                        key = "vehicle_factory",
                        parts = new[] { new Part { node = "anim_fan", pivot = new Vector3(0.00169f, 0.22735f, -0.14046f) } },
                    };
                case "repair":
                    return new BuildingAnimSpec
                    {
                        key = "repair",
                        parts = new[]
                        {
                            new Part { node = "anim_hook_cable", pivot = new Vector3(0.03511f, 0.35897f, -0.24934f) },
                            new Part { node = "anim_hook", pivot = new Vector3(0.03530f, 0.26495f, -0.24972f) },
                        },
                        beacons = new[]
                        {   // the red lamps on the hoist trolley, front and back face, 2.5 cm off the face
                            new BeaconDef { local = new Vector3(0.00684f, 0.47229f, -0.17569f), normal = Vector3.forward, size = new Vector2(0.1002f, 0.0330f), phase = 0 },
                            new BeaconDef { local = new Vector3(0.00584f, 0.47329f, -0.49714f), normal = Vector3.back, size = new Vector2(0.1030f, 0.0350f), phase = 1 },
                        },
                    };
            }
            return null;
        }
    }
}
