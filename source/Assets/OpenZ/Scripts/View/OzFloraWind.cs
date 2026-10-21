using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// Level 16: the trees, palms and plants move in the level wind all the time (a quiet loop of leaves, as the desert palms
    /// have), and in a rain spell they lean the way the wind goes and sway harder and faster. View only: the rules are never
    /// read or written.
    /// <list type="bullet">
    /// <item>The plants are scenery drawn in numbers: every plant is its own object that shares its shape's mesh and materials.
    /// The movement is computed in the vertex stage of OpenZ/TeamLit (OZFloraWind in Shaders/OpenZFloraWind.hlsl, called by
    /// the colour, shadow and depth passes alike), from the object's own place, so nothing is computed per plant per frame,
    /// a plant off screen costs nothing and the shadow moves with the plant.</item>
    /// <item>This manager runs once on level 16 (on the ZGame object). At the start it finds every plant by the name of its
    /// mesh (the plant kit's FL_ shapes; logs, rocks, mounds, lily pads and the joined flowers stand still by the table in
    /// OzFloraWindMath), measures each shape once from its own points (crown pivot, crown radius, height: the preparation's
    /// own measure), and puts the plant's renderer on a run-time copy of its material that carries those numbers (one copy
    /// per material and shape, so equal plants are still drawn together). Per frame it sets a few globals, nothing else.</item>
    /// <item>Far away each 32 m cell of plants is one joined mesh that cannot move. So the movement fades out with the
    /// distance from the camera to the cell's middle and is zero before the far mesh takes over: nothing jumps at the
    /// change. For that the cells' change-over is measured from the cell's middle and with one size per family (the middle
    /// one of the sizes they had), so every cell changes at the same distance.</item>
    /// <item>The wind is the flags' wind: direction from the level's air (_FogWind), strength and gust from FlagView
    /// (_OZFlagWind). The rain wind (OzJungleAir.RainWindK) adds the lean and the faster tempo.</item>
    /// <item>A plant that is tipped over later (felled) is put back on its own still material; <see cref="Still"/> does the
    /// same at once for whoever fells it (OzTreeFall calls it before it cuts the tree: the stump and the falling top are
    /// still things). A tree that can be felled stands in a holder inside its cell: the cell is looked for above the
    /// holder.</item>
    /// <item>Whatever sits on a plant (a bird, a monkey) asks <see cref="Moved"/> once a frame for where its seat is now.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OzFloraWind : MonoBehaviour
    {
        public static OzFloraWind I { get; private set; }
        /// <summary>Tests can stop the automatic install.</summary>
        public static bool AutoBoot = true;

        [Tooltip("x the level wind's strength (0 = the plants stand still)")] [Range(0f, 2f)] public float strength = 1f;
        [Tooltip("The plant cells of the level (m)")] public float cellSize = 32f;
        [Tooltip("The movement is full up to this share of the distance at which a cell changes to its far mesh, and zero from the second share")]
        public Vector2 fadeShare = new Vector2(0.80f, 0.96f);
        [Tooltip("A plant tipped further than this (degrees) is not moved by the wind")] public float maxTilt = 35f;
        Vector3 kitUp = Vector3.up; bool kitUpKnown;
        /// <summary>The plant's own axis (one of its six) that points up as it stands.</summary>
        static Vector3 UprightAxis(Transform t)
        {
            var q = t.rotation; Vector3 best = Vector3.up; float bd = -2f;
            for (int k = 0; k < 6; k++)
            {
                var a = k == 0 ? Vector3.up : k == 1 ? Vector3.down : k == 2 ? Vector3.forward : k == 3 ? Vector3.back : k == 4 ? Vector3.right : Vector3.left;
                float d = (q * a).y; if (d > bd) { bd = d; best = a; }
            }
            return best;
        }
        [Tooltip("The drawing box of a plant is widened by this much (m) for trees and palms / for small plants: what the rain wind can move it, so a leaning crown is not cut off at the screen edge")]
        public Vector2 boundsPad = new Vector2(5.0f, 2.4f);   // DV118 + K256e: room for the shuttle's downwash bend (a 13 m kapok leans 2.6 m, with the rain wind and its flutter 4.7 m)

        public sealed class Plant
        {
            public MeshRenderer R; public Material[] Original; public int Kind, Family;
            public Vector3 PivotMesh; public float Radius, Height;                 // mesh units
            public bool Gone;                                                      // released (felled, tipped) or destroyed: it no longer moves and nothing drips from it
        }

        /// <summary>Play-check counters.</summary>
        public static int Plants, Shapes, Copies, StillByTable, Unknown, Tilted, Batched, NoWindMaterial, Released, Cells, OutOfCell;
        /// <summary>Plants under the level's plant root for which no cell was found: their movement would never fade out and
        /// would jump when the cell changes to its far mesh. Must be 0.</summary>
        public static int NoCell;
        public static string LastLog = "";
        public static readonly List<string> UnknownNames = new List<string>();

        readonly List<Plant> plants = new List<Plant>();
        readonly Dictionary<Renderer, Plant> byRenderer = new Dictionary<Renderer, Plant>();
        readonly HashSet<MeshFilter> seen = new HashSet<MeshFilter>();
        readonly Dictionary<Mesh, OzFloraWindMath.Shape> shapes = new Dictionary<Mesh, OzFloraWindMath.Shape>();
        readonly Dictionary<(Material, Mesh, int), Material> copies = new Dictionary<(Material, Mesh, int), Material>();
        readonly List<Vector4> crowns = new List<Vector4>(), palms = new List<Vector4>();
        readonly List<Plant> crownPlants = new List<Plant>();                     // the plant of each crown (same order)
        readonly List<MeshFilter> scratch = new List<MeshFilter>();
        readonly Vector4[] kK = new Vector4[OzFloraWindMath.Kinds], kL = new Vector4[OzFloraWindMath.Kinds];
        public IReadOnlyList<Plant> All => plants;
        /// <summary>Trees and palms for the drips after rain: x, foot height, z, crown radius (world).</summary>
        public List<Vector4> Crowns => crowns;
        public float Gust { get; private set; }
        public float Strength { get; private set; }
        public float Storm { get; private set; }
        public float WindClock { get; private set; }
        public Vector2 WindDir { get; private set; } = new Vector2(1f, 0f);
        /// <summary>Full / none distance of the movement for tree cells (x, y) and small plant cells (z, w), m.</summary>
        public Vector4 Fade { get; private set; }
        public float TreeChangeOver { get; private set; }
        public float PlantChangeOver { get; private set; }
        /// <summary>The sizes the cells of each family had before they were made even (x smallest, y largest, m), and the one
        /// size they have now (the middle one).</summary>
        public Vector2 TreeSizesBefore { get; private set; }
        public Vector2 PlantSizesBefore { get; private set; }
        public float TreeSize => treeSize;
        public float PlantSize => plantSize;

        static readonly int FogWindId = Shader.PropertyToID("_FogWind"), FlagWindId = Shader.PropertyToID("_OZFlagWind"),
            WindId = Shader.PropertyToID("_OZFloraWind"), Wind2Id = Shader.PropertyToID("_OZFloraWind2"), KId = Shader.PropertyToID("_OZFloraK"),
            LId = Shader.PropertyToID("_OZFloraL"), CellId = Shader.PropertyToID("_OZFloraCell"), FadeId = Shader.PropertyToID("_OZFloraFade"),
            PivotId = Shader.PropertyToID("_OzWindPalm"), CrownId = Shader.PropertyToID("_OzWindPalm2");

        ZGame game; Camera cam; Transform floraRoot, zodRoot;
        float nextScan, treeSize, plantSize, treeTransition = 0.9f, plantTransition = 0.95f; int checkAt;

        // ------------------------------------------------------------------ boot (as PalmWind)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Ensure();
            SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
        }
        static void OnSceneLoaded(Scene s, LoadSceneMode m) => Ensure();

        /// <summary>Adds the manager on level 16 unless one is already there.</summary>
        public static void Ensure()
        {
            if (!AutoBoot) return;
            var g = ZGame.I; if (!g || g.level != "L16" || FindFirstObjectByType<OzFloraWind>()) return;
            g.gameObject.AddComponent<OzFloraWind>();
        }

        void Awake()
        {
            if (I && I != this) { Destroy(this); return; }
            I = this; Plants = Shapes = Copies = StillByTable = Unknown = Tilted = Batched = NoWindMaterial = Released = Cells = OutOfCell = NoCell = 0; LastLog = ""; UnknownNames.Clear();
        }

        void Start()
        {
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            cam = Camera.main;
            var env = GameObject.Find("Environment"); floraRoot = env ? env.transform.Find("L16_Flora") : null;
            var zo = GameObject.Find("ZodObjects"); zodRoot = zo ? zo.transform : null;
            SetCells();
            Scan();
            UpdateWind(0f);
            Log(Status());
        }

        void OnDisable() { Shader.SetGlobalVector(WindId, Vector4.zero); Shader.SetGlobalVector(Bend3Id, Vector4.zero); }       // switched off: the plants stand still (rest pose); K256e: no bend

        void OnDestroy()
        {
            if (I != this) return;
            I = null;
            Shader.SetGlobalVector(WindId, Vector4.zero); Shader.SetGlobalVector(Wind2Id, Vector4.zero); Shader.SetGlobalVector(Bend3Id, Vector4.zero);   // K256e: no bend left
            foreach (var p in plants) if (p.R) { p.R.sharedMaterials = p.Original; p.R.ResetLocalBounds(); }
            foreach (var c in copies.Values) if (c) Destroy(c);
            copies.Clear(); plants.Clear(); byRenderer.Clear(); seen.Clear(); shapes.Clear(); crowns.Clear(); crownPlants.Clear(); palms.Clear();
        }

        // ------------------------------------------------------------------ frame: a few globals, nothing per plant
        void LateUpdate()
        {
            UpdateWind(Time.deltaTime);
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 3f; Scan(); UpdateFade(); }
            CheckSome(128);
        }

        void UpdateWind(float dt)
        {
            Vector4 fw = Shader.GetGlobalVector(FogWindId);                            // the level's air (OzJungleAir: 2.5 m/s, in rain the spell's 5 m/s)
            var wv = new Vector2(fw.x, fw.z);
            if (wv.sqrMagnitude < 0.01f) wv = new Vector2(OzJungleAir.CalmWind.x, OzJungleAir.CalmWind.z);
            float speed = Mathf.Max(wv.magnitude, 0.01f); WindDir = wv / speed;
            Vector4 fl = Shader.GetGlobalVector(FlagWindId);                           // FlagView: (strength, gust, phase, time)
            float s, g;
            if (fl.w > 0.01f) { s = fl.x; g = fl.y; }
            else { s = Mathf.Clamp(speed / 3.35f, 0.25f, 1.6f); g = OzFloraWindMath.CalmGust(Time.time); }
            Storm = Mathf.Clamp01(OzJungleAir.RainWindK);
            Gust = Mathf.Lerp(g, OzFloraWindMath.RainGust(Time.time), Storm);
            Strength = s * strength;
            WindClock += dt * (1f + OzFloraWindMath.RainTempo * Storm);
            for (int i = 0; i < OzFloraWindMath.Kinds; i++)
            {
                kK[i] = new Vector4(OzFloraWindMath.SwayDeg[i] * Mathf.Deg2Rad, OzFloraWindMath.BendDeg[i] * Mathf.Deg2Rad, OzFloraWindMath.FlutterM[i], 0f);
                kL[i] = new Vector4(OzFloraWindMath.LeanTrunkDeg[i] * Mathf.Deg2Rad, OzFloraWindMath.LeanCrownDeg[i] * Mathf.Deg2Rad, OzFloraWindMath.PushM[i], 0f);
            }
            Shader.SetGlobalVectorArray(KId, kK); Shader.SetGlobalVectorArray(LId, kL);
            Shader.SetGlobalVector(WindId, new Vector4(WindDir.x, WindDir.y, Strength, Gust));
            Shader.SetGlobalVector(Wind2Id, new Vector4(WindClock, Storm, OzFloraWindMath.GroundGuard, 0f));
            OzFloraDownwash.Current(out var dwA, out var dwB); Shader.SetGlobalVector("_OZDownwash", dwA); Shader.SetGlobalVector("_OZDownwash2", dwB);   // DV118: the shuttle's downwash (View/OzFloraDownwash.cs)
            OzFloraWindMath.BendSpring(ref bendE, ref bendV, ref bendF, ref bendMode, ref bendLastT, OzFloraWindMath.BendTarget(OzFloraDownwash.K), dt);   // K256e its absolute bend, eased
            BendE = bendE; BendF = bendF; BendAt = OzFloraDownwash.Centre;
            Shader.SetGlobalVector(Bend3Id, new Vector4(BendAt.x, BendAt.z, bendE, bendF));
            Shader.SetGlobalVector(CellId, new Vector4(0f, 0f, cellSize, 0f));
            Shader.SetGlobalVector(FadeId, Fade);
        }

        /// <summary>The wind the shader draws with in this frame, for the counted checks.</summary>
        public OzFloraWindMath.Wind CurrentWind()
        {   // DV118: with the shuttle's downwash; K256e: and its absolute bend
            var w = OzFloraDownwash.With(new OzFloraWindMath.Wind { Wx = WindDir.x, Wz = WindDir.y, S = Strength, Gust = Gust, T = WindClock, Storm = Storm });
            w.DbX = BendAt.x; w.DbZ = BendAt.z; w.DbE = bendE; w.DbF = bendF;
            return w;
        }
        /// <summary>K256e the downwash's absolute bend now: its envelope (eased in, springing back), the flutter's, the jet axis' ground point.</summary>
        public static float BendE { get; private set; }
        public static float BendF { get; private set; }
        public static Vector3 BendAt { get; private set; }
        float bendE, bendV, bendF, bendLastT; int bendMode;
        static readonly int Bend3Id = Shader.PropertyToID("_OZDownwash3");

        // ------------------------------------------------------------------ the cells and the distance the movement ends at
        /// <summary>Every plant cell changes to its far mesh by the distance from the camera to the cell's own middle, with one
        /// size per family (the middle one of the sizes the cells had): the same distance for every cell.</summary>
        void SetCells()
        {
            if (!floraRoot) return;
            var ts = new List<float>(); var ps = new List<float>(); var groups = new List<(LODGroup lg, bool tree, int cx, int cz)>();
            foreach (Transform c in floraRoot)
            {
                var lg = c.GetComponent<LODGroup>(); if (!lg) continue;
                var parts = c.name.Split('_'); if (parts.Length != 3 || !int.TryParse(parts[1], out int cx) || !int.TryParse(parts[2], out int cz)) continue;
                bool tree = parts[0] == "T";
                (tree ? ts : ps).Add(lg.size * Mathf.Abs(c.lossyScale.y));
                groups.Add((lg, tree, cx, cz));
                var lods = lg.GetLODs();
                if (lods.Length > 0) { if (tree) treeTransition = lods[0].screenRelativeTransitionHeight; else plantTransition = lods[0].screenRelativeTransitionHeight; }
            }
            ts.Sort(); ps.Sort();
            treeSize = ts.Count > 0 ? ts[ts.Count / 2] : 40f; plantSize = ps.Count > 0 ? ps[ps.Count / 2] : 33f;
            TreeSizesBefore = ts.Count > 0 ? new Vector2(ts[0], ts[ts.Count - 1]) : Vector2.zero; PlantSizesBefore = ps.Count > 0 ? new Vector2(ps[0], ps[ps.Count - 1]) : Vector2.zero;
            foreach (var (lg, tree, cx, cz) in groups)
            {
                var mid = new Vector3((cx + 0.5f) * cellSize, 0f, (cz + 0.5f) * cellSize);
                lg.localReferencePoint = lg.transform.InverseTransformPoint(mid);
                lg.size = (tree ? treeSize : plantSize) / Mathf.Max(1e-4f, Mathf.Abs(lg.transform.lossyScale.y));
                Cells++;
            }
            UpdateFade();
        }

        /// <summary>The distance (m, camera to the cell's point) at which a cell of this size changes to its far mesh.</summary>
        public float ChangeOverOf(float size, bool tree)
        {
            if (!cam) cam = Camera.main;
            float tan = Mathf.Tan(0.5f * Mathf.Deg2Rad * (cam ? cam.fieldOfView : 32f)), bias = Mathf.Max(0.01f, QualitySettings.lodBias);
            return size > 0f ? size * bias / (2f * tan * Mathf.Max(0.01f, tree ? treeTransition : plantTransition)) : 0f;
        }

        void UpdateFade()
        {
            TreeChangeOver = ChangeOverOf(treeSize, true);
            PlantChangeOver = ChangeOverOf(plantSize, false);
            Fade = new Vector4(fadeShare.x * TreeChangeOver, fadeShare.y * TreeChangeOver, fadeShare.x * PlantChangeOver, fadeShare.y * PlantChangeOver);
        }

        /// <summary>1 near the camera .. 0 where the far mesh takes over, for a plant standing at foot: the shader's own rule.</summary>
        public float FadeAt(Vector3 foot, int family)
        {
            if (family <= 0 || !cam) return 1f;
            var mid = new Vector3((Mathf.Floor(foot.x / cellSize) + 0.5f) * cellSize, 0f, (Mathf.Floor(foot.z / cellSize) + 0.5f) * cellSize);
            float d = Vector3.Distance(cam.transform.position, mid);
            float a = family > 1 ? Fade.z : Fade.x, b = family > 1 ? Fade.w : Fade.y;
            float u = Mathf.Clamp01((d - a) / Mathf.Max(0.01f, b - a));
            return 1f - u * u * (3f - 2f * u);
        }

        // ------------------------------------------------------------------ the plants
        /// <summary>Looks for plants that are not hooked yet (the start, and every 3 s: plants that another step put in later).</summary>
        public void Scan()
        {
            int before = plants.Count;
            // a root is walked again only when something was added to or taken from its tree (nothing is allocated otherwise)
            if (floraRoot && floraRoot.hierarchyCount != floraCount) { floraCount = floraRoot.hierarchyCount; floraRoot.GetComponentsInChildren(true, scratch); foreach (var mf in scratch) Hook(mf); }
            if (zodRoot && zodRoot.hierarchyCount != zodCount) { zodCount = zodRoot.hierarchyCount; zodRoot.GetComponentsInChildren(true, scratch); foreach (var mf in scratch) Hook(mf); }
            scratch.Clear();
            if (plants.Count != before) Log("+" + (plants.Count - before) + " plant(s): " + Status());
        }

        /// <summary>Looks again at everything now (for whoever put plants in or changed them just now).</summary>
        public void Rescan() { floraCount = zodCount = -1; Scan(); }

        int floraCount = -1, zodCount = -1;
        readonly Dictionary<Mesh, string> plantMeshes = new Dictionary<Mesh, string>();   // a mesh's name is asked for once (null = not a plant shape)

        void Hook(MeshFilter mf)
        {
            if (!mf) return;
            var mesh = mf.sharedMesh; if (!mesh) return;
            if (!plantMeshes.TryGetValue(mesh, out string name))
            {
                name = mesh.name;
                if (name.Length < 3 || name[0] != 'F' || name[1] != 'L' || name[2] != '_' || name.EndsWith("_far", System.StringComparison.Ordinal)) name = null;   // the plant kit's near shapes only
                plantMeshes[mesh] = name;
            }
            if (name == null || !seen.Add(mf)) return;                                               // each plant once
            int kind = OzFloraWindMath.KindOf(name, out float natural, out bool known);
            if (!known) { Unknown++; if (!UnknownNames.Contains(name)) UnknownNames.Add(name); return; }
            if (kind == OzFloraWindMath.Still) { StillByTable++; return; }
            var r = mf.GetComponent<MeshRenderer>(); if (!r) return;
            if (r.isPartOfStaticBatch) { Batched++; return; }                                        // the shader needs the plant's own matrix
            var t = r.transform;
            if (!kitUpKnown) { kitUp = UprightAxis(t); kitUpKnown = true; }                          // the plant kit's shapes share one up axis: their height runs along -Z of the mesh and a plant stands turned 90 degrees, so transform.up lies flat. Read once, from the first plant of the level (it stands)
            if (Vector3.Angle(t.rotation * kitUp, Vector3.up) > maxTilt) { Tilted++; return; }
            float sc = Mathf.Abs(t.lossyScale.y); if (sc < 1e-4f) return;
            if (!shapes.TryGetValue(mesh, out var sh))
            {
                if (!mesh.isReadable) { NoWindMaterial++; Log(name + ": mesh not readable: stays still"); return; }
                var v = mesh.vertices; var xyz = new float[v.Length * 3];
                // the plant's own space: its points through its own turn and scale, without its place, at its natural size
                for (int i = 0; i < v.Length; i++) { var q = t.TransformVector(v[i]) / sc; xyz[3 * i] = q.x; xyz[3 * i + 1] = q.y; xyz[3 * i + 2] = q.z; }
                float top = float.MinValue, low = float.MaxValue; for (int i = 0; i < v.Length; i++) { top = Mathf.Max(top, xyz[3 * i + 1]); low = Mathf.Min(low, xyz[3 * i + 1]); }
                float len = kind == OzFloraWindMath.Hang ? -low : top;
                float unit = natural > 0f && len > 1e-3f && Mathf.Abs(len - natural) > 0.03f * natural ? natural / len : 1f;   // a mesh that is not in metres
                if (unit != 1f) for (int i = 0; i < xyz.Length; i++) xyz[i] *= unit;
                sh = OzFloraWindMath.Measure(xyz, v.Length, kind);
                var pivotWorldSized = new Vector3(sh.Px, sh.Py, sh.Pz) / unit * sc;
                var pm = t.InverseTransformVector(pivotWorldSized);
                sh.Px = pm.x; sh.Py = pm.y; sh.Pz = pm.z; sh.R /= unit; sh.H /= unit;                // from here on: mesh units
                shapes[mesh] = sh; Shapes++;
                Log(name + ": " + OzFloraWindMath.KindName(kind) + ", height " + (sh.H * unit).ToString("F2") + " m, crown from " + sh.CrownBase.ToString("F2") + " m, pivot (mesh) " + pm.ToString("F2") + ", crown radius " + (sh.R * unit).ToString("F2") + " m" + (unit != 1f ? ", mesh unit " + unit.ToString("F3") + " m" : ""));
            }
            // the plant's cell: its parent, or (a tree that can be felled stands in a holder inside its cell, and its neighbours on
            // the same tile with it) the one above that: the child of the plant root this plant hangs under
            var par = t.parent; int family = 0;
            while (par && par.parent != floraRoot) par = par.parent;
            if (par && floraRoot && par.GetComponent<LODGroup>()) family = par.name.StartsWith("P_", System.StringComparison.Ordinal) ? 2 : 1;
            if (family == 0 && floraRoot && t.IsChildOf(floraRoot)) NoCell++;
            var mats = r.sharedMaterials; Material[] orig = null;
            for (int k = 0; k < mats.Length; k++)
            {
                var m = mats[k];
                if (!m || !m.HasProperty(PivotId)) continue;
                if (!copies.TryGetValue((m, mesh, family), out var c))
                {
                    c = new Material(m) { name = m.name + " (wind)", hideFlags = HideFlags.DontSave };
                    c.SetVector(PivotId, new Vector4(sh.Px, sh.Py, sh.Pz, -kind));                      // the kind, negative: the palm sway's own code lets it go
                    c.SetVector(CrownId, new Vector4(sh.R, sh.H, family, 0f));
                    copies[(m, mesh, family)] = c; Copies++;
                }
                if (orig == null) orig = (Material[])mats.Clone();
                mats[k] = c;
            }
            if (orig == null) { NoWindMaterial++; return; }
            r.sharedMaterials = mats;
            var lb = r.localBounds; lb.Expand(2f * (kind == OzFloraWindMath.Small ? boundsPad.y : boundsPad.x) / sc); r.localBounds = lb;   // the moved plant stays inside its drawing box
            var p = new Plant { R = r, Original = orig, Kind = kind, Family = family, PivotMesh = new Vector3(sh.Px, sh.Py, sh.Pz), Radius = sh.R, Height = sh.H };
            plants.Add(p); byRenderer[r] = p; Plants++;
            if (family > 0)
            {   // the plant must stand in the square its cell is named after (the far mesh and the fade go by it)
                var parts = par.name.Split('_');
                if (parts.Length == 3 && int.TryParse(parts[1], out int cx) && int.TryParse(parts[2], out int cz)
                    && (Mathf.FloorToInt(t.position.x / cellSize) != cx || Mathf.FloorToInt(t.position.z / cellSize) != cz)) OutOfCell++;
            }
            var pos = t.position;
            if (kind == OzFloraWindMath.Palm || kind == OzFloraWindMath.Clump) palms.Add(new Vector4(pos.x, pos.y, pos.z, sh.R * sc));
            if (kind == OzFloraWindMath.Palm || kind == OzFloraWindMath.Clump || kind == OzFloraWindMath.Tree) { crowns.Add(new Vector4(pos.x, pos.y, pos.z, sh.R * sc)); crownPlants.Add(p); }
        }

        /// <summary>A few plants a frame: one that is gone is forgotten, one that was tipped over (felled) goes back on its own
        /// still material.</summary>
        void CheckSome(int n)
        {
            if (plants.Count == 0) return;
            for (int k = 0; k < n; k++)
            {
                if (plants.Count == 0) return;
                if (checkAt >= plants.Count) checkAt = 0;
                var p = plants[checkAt];
                if (!p.R) { p.Gone = true; plants[checkAt] = plants[plants.Count - 1]; plants.RemoveAt(plants.Count - 1); continue; }
                if (Vector3.Angle(p.R.transform.rotation * kitUp, Vector3.up) > maxTilt + 5f) { Release(p); plants[checkAt] = plants[plants.Count - 1]; plants.RemoveAt(plants.Count - 1); continue; }
                checkAt++;
            }
        }

        void Release(Plant p)
        {
            if (p.R) { p.R.sharedMaterials = p.Original; p.R.ResetLocalBounds(); byRenderer.Remove(p.R); }
            p.Gone = true; Released++;
        }

        /// <summary>For whoever fells or moves a plant: everything under root stands still from now on (its own materials).</summary>
        public static void Still(GameObject root)
        {
            var w = I; if (!w || !root) return;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                if (w.byRenderer.TryGetValue(r, out var p)) { w.Release(p); w.plants.Remove(p); }
        }

        public bool TryGet(Renderer r, out Plant p) => byRenderer.TryGetValue(r, out p);

        /// <summary>For whatever sits on a plant (a bird on a branch, a monkey in a crown): where the wind has moved the world
        /// point restPoint of this plant in this frame: the point plus the sway, the rain lean and the fade the picture is
        /// drawn with (the shader's plain number twin, OzFloraWindMath.Displace, with this frame's wind). Give the seat's
        /// REST place every time (where it is on the standing tree), not the last answer. A renderer that is no plant in the
        /// wind (never hooked, felled, a still thing) gives the point back unchanged.</summary>
        public Vector3 Moved(Renderer tree, Vector3 restPoint)
        {
            if (!enabled || !tree || !byRenderer.TryGetValue(tree, out var p) || !p.R) return restPoint;
            var m = p.R.transform.localToWorldMatrix;
            var foot = new Vector3(m.m03, m.m13, m.m23); var top = m.MultiplyPoint3x4(p.PivotMesh);
            float sc = new Vector3(m.m00, m.m10, m.m20).magnitude;                                   // the placement scale, as the shader takes it
            float ax = m.m00, az = m.m20, al = Mathf.Sqrt(ax * ax + az * az); if (al > 1e-4f) { ax /= al; az /= al; } else { ax = 1f; az = 0f; }
            OzFloraWindMath.Displace(p.Kind, CurrentWind(), foot.x, foot.y, foot.z, top.x, top.y, top.z, sc, p.Radius * sc, p.Height * sc, FadeAt(foot, p.Family),
                restPoint.x, restPoint.y, restPoint.z, out float dx, out float dy, out float dz, ax, az);
            return restPoint + new Vector3(dx, dy, dz);
        }

        /// <summary>The plant in the wind whose foot stands nearest to the point (flat distance, at most `within` m), for a seat
        /// that knows its place but not its tree: ask once, keep the renderer, hand it to <see cref="Moved"/>. Null = none.</summary>
        public MeshRenderer NearestPlant(Vector3 point, float within)
        {
            MeshRenderer best = null; float bd = within * within;
            for (int i = 0; i < plants.Count; i++)
            {
                var p = plants[i]; if (p.Gone || !p.R) continue;
                var q = p.R.transform.position; float dx = q.x - point.x, dz = q.z - point.z, d = dx * dx + dz * dz;
                if (d <= bd) { bd = d; best = p.R; }
            }
            return best;
        }

        /// <summary>The crowns (trees and palms) whose foot stands inside the x / z box widened by pad (m), for the drips after
        /// rain: x, foot height, z, crown radius (world). Left out: a plant that was released (felled, tipped), is gone or
        /// is switched off.</summary>
        public void CrownsIn(Vector2 min, Vector2 max, float pad, List<Vector4> into)
        {
            into.Clear();
            for (int i = 0; i < crowns.Count; i++)
            {
                var c = crowns[i];
                if (c.x < min.x - pad || c.x > max.x + pad || c.z < min.y - pad || c.z > max.y + pad) continue;
                var p = crownPlants[i];
                if (p.Gone || !p.R || !p.R.enabled || !p.R.gameObject.activeInHierarchy) continue;
                into.Add(c);
            }
        }

        /// <summary>Is a palm on screen within maxDistance of the camera (the gust's frond rattle).</summary>
        public bool PalmInView(Camera c, float maxDistance)
        {
            if (!c) return false;
            var cp = c.transform.position; float md2 = maxDistance * maxDistance;
            for (int i = 0; i < palms.Count; i++)
            {
                var p = (Vector3)palms[i];
                if ((p - cp).sqrMagnitude > md2) continue;
                var vp = c.WorldToViewportPoint(p + Vector3.up * 3f);
                if (vp.z > 0f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ status / log
        public static string Status()
        {
            var w = I;
            if (!w) return "OzFloraWind not running";
            return "plants in the wind " + Plants + " (" + Shapes + " shapes, " + Copies + " wind materials), still by the table " + StillByTable + ", unknown names " + Unknown
                   + ", tipped " + Tilted + ", in a static batch " + Batched + ", no wind material " + NoWindMaterial + ", released " + Released
                   + "; cells " + Cells + " (plants outside their cell's square " + OutOfCell + ", plants under the plant root without a cell " + NoCell + "), change to the far mesh at " + w.TreeChangeOver.ToString("F0") + " m (trees) / " + w.PlantChangeOver.ToString("F0")
                   + " m (small plants), movement full to " + w.Fade.x.ToString("F0") + " / " + w.Fade.z.ToString("F0") + " m; wind to (" + w.WindDir.x.ToString("F2") + ", " + w.WindDir.y.ToString("F2")
                   + ") strength " + w.Strength.ToString("F2") + " gust " + w.Gust.ToString("F2") + " rain wind " + w.Storm.ToString("F2") + (w.enabled ? "" : " (manager OFF: still)");
        }

        static void Log(string s)
        {
            LastLog = s;
            if (Debug.isDebugBuild) Debug.Log("[OpenZ FloraWind] " + s);
        }
    }
}
