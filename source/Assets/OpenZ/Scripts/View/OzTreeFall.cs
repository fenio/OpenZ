using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Level 16 (DV88): the trees that close a tile fall when the rules destroy them. View only: the rules decide
    /// (Sim/ZWorld.TreeTiles.cs: a tree is a destructible detail like a Desert palm, with a rock tile's rules); this draws it with
    /// the palms' own parts:
    ///  * at level start every tree of the rules gets a DetailProp on its drawn plant (found by the scene object name the rules
    ///    carry), so DetailPropHub binds it like a palm: shots and blasts near it knock it (wobble, a knock, a few leaves), a
    ///    right-click with explosives orders an attack on it, and the rules' Death event breaks it (CombatView -> SimDied -> Break:
    ///    the wood break sound, bark and leaf bits, dust, a small scorch);
    ///  * at half its hit points it leans a little and sheds leaves (the rocks' damaged stage, polled like RockPillar);
    ///  * broken: the lowest part of its own mesh stays as the stump (cut off flat, the cut closed); the rest tips over away from
    ///    the hit about the edge of the snap (PalmFall's hinge fall, th'' = 3g / 2L sin th) with a creak, lands on its crown with a
    ///    thud, a leaf rustle and dust (a splash in the swamp), lies 8 to 12 s and sinks away. Other tall plants on the same tile
    ///    go down with it;
    ///  * the far version of its 32 m cell stops showing it: the plant's own run of points in the cell's joined far mesh is folded
    ///    onto one point (the runs: Resources/OpenZ/Maps/L16_tree_far, written by the editor menu OpenZ/L16 Jungle/8b). Without a
    ///    run for a plant its cell stays on its near objects from then on, so a felled tree is never drawn again either way.
    /// Sounds: the palm's own clips through DetailProp.Play (one line each in the sound log, keys dtl_tree_break, dtl_tree_creak,
    /// dtl_tree_crash, dtl_tree_leaves, dtl_tree_splash). Created by ZGame on level 16 in Awake, before the first
    /// DetailPropHub.Update binds the props.
    /// </summary>
    public partial class OzTreeFall : MonoBehaviour                                               // round 4: + OzTreeFall.Blast.cs
    {
        public static OzTreeFall I { get; private set; }
        [Tooltip("s: a fallen tree lies as a log, then sinks away, between these times after it landed")] public Vector2 life = new Vector2(40f, 60f);
        [Tooltip("degrees: the lean of the damaged stage (it must read from the default camera height)")] public Vector2 lean = new Vector2(10f, 15f);
        [Tooltip("the stump's width against the trunk's own (it must read from the default camera height)")] public float stumpWide = 1.5f;
        [Tooltip("first tilt speed (rad/s) of the falling tree, a close blast pushes harder")] public float kick = 0.45f;

        /// <summary>For the checks: trees bound to the rules / trees of the rules without a drawn plant, leaning stages shown, falls
        /// begun, falls landed, stumps made, plants folded out of a far mesh, plants whose cell had to stay near, runs loaded.</summary>
        public static int Bound, Unbound, Leans, Falls, Landed, Stumps, FarCuts, FarForced, FarRuns, Floated;
        public static string LastLog = "";

        public const float PickRadius = 0.6f;                                                       // m: the column a shot must pass and a click must hit
        static readonly Color[] BarkAndLeaves = { new Color(0.33f, 0.24f, 0.15f), new Color(0.24f, 0.17f, 0.11f), new Color(0.2f, 0.42f, 0.16f), new Color(0.36f, 0.55f, 0.2f) };
        static readonly string[] Crack = { "sd_debris_wood_a", "sd_debris_wood_b", "sd_debris_wood_c" };   // the palm's break clips
        static readonly string[] Knock = { "sd_debris_wood_c" };                                             // the palm's knock clip
        static readonly string[] Creak = { "s3_creak_c", "s3_creak_d" };                                     // PalmFall's lists from here on
        static readonly string[] CrownThud = { "Foley/sd_dive_land_sand_a", "Foley/sd_dive_land_sand_b", "Foley/sd_dive_land_sand_c" };
        static readonly string[] LeafRustle = { "DetailsV2/dv2_tumbleweed_rustle_a", "DetailsV2/dv2_tumbleweed_rustle_b" };
        static readonly string[] Splash = { "sd_debris_splash_a", "sd_debris_splash_b" };

        sealed class Stand
        {
            public int tx, ty; public ZObject sim; public Transform holder, main; public LODGroup cell;
            public readonly List<Transform> plants = new List<Transform>(); public readonly List<Bounds> boxes = new List<Bounds>();
            public float height, radius, blastAt = -10f; public Vector3 blast; public bool leaned, fallen, landed, stump, flora;
        }
        [System.Serializable] sealed class FarDoc { public string[] names; public int[] start, count; }

        readonly List<Stand> trees = new List<Stand>();
        readonly Dictionary<DetailProp, Stand> byProp = new Dictionary<DetailProp, Stand>();
        readonly Dictionary<int, Stand> byTile = new Dictionary<int, Stand>();
        readonly Dictionary<string, Vector2Int> farRun = new Dictionary<string, Vector2Int>();
        readonly HashSet<LODGroup> nearOnly = new HashSet<LODGroup>();
        readonly List<Mesh> made = new List<Mesh>();
        DetailKit.Bits bits;                                                                        // one pool of bark and leaf bits for all the trees
        float nextPoll;

        static int Key(int tx, int ty) => ty * 1000 + tx;

        /// <summary>For the checks: 0 standing, 1 leaning, 2 falling, 3 down; -1 = no drawn tree is bound to this tile.</summary>
        public static int StateOf(int tx, int ty) => I && I.byTile.TryGetValue(Key(tx, ty), out var t) ? (t.landed ? 3 : t.fallen ? 2 : t.leaned ? 1 : 0) : -1;
        public static bool HasStump(int tx, int ty) => I && I.byTile.TryGetValue(Key(tx, ty), out var t) && t.stump;
        /// <summary>For proof pictures: where the drawn tree of a tile stands (false = none bound).</summary>
        public static bool FootOf(int tx, int ty, out Vector3 foot)
        {
            foot = Vector3.zero;
            if (!I || !I.byTile.TryGetValue(Key(tx, ty), out var t) || !t.holder) return false;
            foot = t.holder.position; return true;
        }

        void Awake()
        {
            I = this; Bound = Unbound = Leans = Falls = Landed = Stumps = FarCuts = FarForced = FarRuns = Floated = 0; LastLog = "";
            var game = ZGame.I; var w = game ? game.World : null;
            if (w == null || w.TreeTiles.Count == 0) return;
            var env = GameObject.Find("Environment"); var root = env ? env.transform.Find("L16_Flora") : null;
            if (!root) { Unbound = w.TreeTiles.Count; Debug.LogWarning("[OpenZ] DV88 trees: no L16_Flora in the scene, " + Unbound + " trees of the rules have no drawn tree"); return; }
            var runs = Resources.Load<TextAsset>("OpenZ/Maps/L16_tree_far");
            var fd = runs ? JsonUtility.FromJson<FarDoc>(runs.text) : null;
            if (fd != null && fd.names != null && fd.start != null && fd.count != null)
                for (int i = 0; i < fd.names.Length && i < fd.start.Length && i < fd.count.Length; i++) farRun[fd.names[i]] = new Vector2Int(fd.start[i], fd.count[i]);
            FarRuns = farRun.Count;
            // the tree-family plants that stand on a tree tile (a tile lies inside one 32 m cell)
            var want = new HashSet<int>(); foreach (var o in w.TreeTiles) want.Add(Key(o.LocX >> 4, o.LocY >> 4));
            var onTile = new Dictionary<int, List<Transform>>();
            foreach (Transform cell in root)
            {
                if (!cell.name.StartsWith("T_")) continue;
                foreach (Transform p in cell)
                {
                    if (p.name == "FAR") continue;
                    int k = Key(Mathf.FloorToInt(p.position.x * 0.5f), Mathf.FloorToInt(-p.position.z * 0.5f));
                    if (!want.Contains(k)) continue;
                    if (!onTile.TryGetValue(k, out var l)) onTile[k] = l = new List<Transform>();
                    l.Add(p);
                }
            }
            bits = new DetailKit.Bits(BarkAndLeaves); DetailPropHub.Track(bits);
            var missing = new List<string>();
            foreach (var o in w.TreeTiles)
            {
                int tx = o.LocX >> 4, ty = o.LocY >> 4, k = Key(tx, ty);
                w.DetailPropUid.TryGetValue(o.Id, out var uid);
                Transform main = null;
                if (onTile.TryGetValue(k, out var list)) foreach (var p in list) if (p.name == uid) { main = p; break; }
                var mr = main ? main.GetComponent<Renderer>() : null;
                if (!mr) { Unbound++; missing.Add(uid ?? "?"); continue; }
                var cellT = main.parent;
                var t = new Stand { tx = tx, ty = ty, sim = o, main = main, cell = cellT ? cellT.GetComponent<LODGroup>() : null };
                var box = mr.bounds; var foot = main.position;
                if (foot.y - box.min.y > 1f) foot.y = box.min.y;                                    // a hanging vine: its own origin is at its top
                t.height = Mathf.Max(1f, box.max.y - foot.y); t.radius = Mathf.Max(0.5f, Mathf.Max(box.extents.x, box.extents.z));
                // a holder at the foot: the lean and the fall turn it, DetailProp's knock rocks the plant inside it
                var h = new GameObject("TreeTile_" + tx + "_" + ty).transform; h.SetParent(cellT, false); h.position = foot; t.holder = h;
                foreach (var p in list) { var pr = p.GetComponent<Renderer>(); t.plants.Add(p); t.boxes.Add(pr ? pr.bounds : box); p.SetParent(h, true); }
                var dp = main.gameObject.AddComponent<DetailProp>();
                var ls = main.lossyScale;                                                           // DetailProp.Start scales the two sizes by it
                dp.id = uid; dp.kind = "tree"; dp.mat = "wood"; dp.HpOwned = true;                  // the rules own its hit points: shots and blasts only knock it
                dp.hitRadius = PickRadius / Mathf.Max(0.01f, Mathf.Max(ls.x, ls.z));
                dp.hitHeight = Mathf.Clamp(t.height * 0.6f, 2.5f, 4.5f) / Mathf.Max(0.01f, ls.y);
                dp.debris = BarkAndLeaves; dp.SharedBits = bits; dp.debrisCount = 22; dp.scorch = 0.55f; dp.knockWobble = 0.6f; dp.glowAtNight = false;
                dp.breakEvent = "dtl_tree_break"; dp.breakClips = Crack; dp.hitClips = Knock;
                dp.BrokenBy += OnBroken; dp.Damaged += OnDamaged;
                trees.Add(t); byProp[dp] = t; byTile[k] = t; Bound++;
            }
            CollectBlastFlora(root);                                                                // round 4: the level's other trees for the fort's blast, and the cut warmed up (OzTreeFall.Blast.cs)
            Debug.Log($"[OpenZ] DV88 trees: {Bound} of {w.TreeTiles.Count} trees of the rules have their drawn tree ({w.TreeTilesClosed} close a tile), far runs known for {FarRuns} plants" +
                      (missing.Count > 0 ? "; NOT FOUND in the scene: " + string.Join(", ", missing.GetRange(0, Mathf.Min(8, missing.Count)).ToArray()) : ""));
        }

        void OnDestroy()
        {
            foreach (var m in made) if (m) Destroy(m);
            made.Clear();
            if (I == this) I = null;
        }

        void Update()
        {
            BlastTick();                                                                            // round 4: the fort's blast fells the trees as its wave reaches them (OzTreeFall.Blast.cs)
            if (Time.time < nextPoll) return;
            nextPoll = Time.time + 0.2f;                                                            // the rock pillars' pace (RockPillar.Update)
            for (int i = 0; i < trees.Count; i++)
            {
                var t = trees[i];
                if (!t.leaned && !t.fallen && t.sim.Health > 0 && t.sim.Health <= t.sim.MaxHealth / 2) Lean(t);
            }
        }

        void OnDamaged(DetailProp p, float amount, Vector3 from, bool blast)
        {
            if (blast && byProp.TryGetValue(p, out var t)) { t.blast = from; t.blastAt = Time.time; }
        }

        /// <summary>Flat direction away from where it was hit: the killer, else the last blast near it, else a way of its own.</summary>
        static Vector3 Away(Stand t, Vector3 foot, Vector3 from)
        {
            var d = foot - from; d.y = 0f;
            if (d.sqrMagnitude < 0.04f && Time.time - t.blastAt < 1.5f) { d = foot - t.blast; d.y = 0f; }
            if (d.sqrMagnitude < 0.04f) { float a = Random.Range(0f, Mathf.PI * 2f); d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); }
            return d.normalized;
        }

        // ------------------------------------------------------------------ the damaged stage
        void Lean(Stand t)
        {
            t.leaned = true; Leans++;
            var foot = t.holder.position; var away = Away(t, foot, foot);
            var top = foot + Vector3.up * (t.height * 0.7f);
            DetailProp.Play("dtl_tree_crack", Crack, foot + Vector3.up * (t.height * 0.3f), 0.7f, 1f, -1, 0f);   // the trunk cracks as it takes its lean
            DetailProp.Play("dtl_tree_creak", Creak, foot + Vector3.up * (t.height * 0.45f), 0.5f, Random.Range(0.62f, 0.75f), -1, 0f);
            var rng = new System.Random(t.tx * 73 + t.ty * 7 + Time.frameCount);
            // leaves come down, from the whole crown and large enough to be seen from the default camera height
            for (int k = 0; k < 4; k++)
            {
                var from = top + new Vector3(((float)rng.NextDouble() - 0.5f) * t.radius, ((float)rng.NextDouble() - 0.3f) * t.height * 0.25f, ((float)rng.NextDouble() - 0.5f) * t.radius);
                bits.Burst(from, 9, 2, 2.6f, 0.22f, rng); bits.Burst(from, 8, 3, 2.6f, 0.22f, rng);
            }
            StartCoroutine(LeanRoutine(t, Vector3.Cross(Vector3.up, away).normalized, Random.Range(lean.x, lean.y)));
            Log(t, "leans");
        }

        IEnumerator LeanRoutine(Stand t, Vector3 axis, float deg)
        {
            var r0 = t.holder.rotation; float k = 0f;
            while (k < 1f && !t.fallen)
            {
                k = Mathf.Min(1f, k + Time.deltaTime / 0.6f);
                t.holder.rotation = Quaternion.AngleAxis(deg * k * (2f - k), axis) * r0;            // eases out
                yield return null;
            }
        }

        // ------------------------------------------------------------------ the fall
        void OnBroken(DetailProp p, Vector3 from)
        {
            if (!byProp.TryGetValue(p, out var t) || t.fallen) return;
            t.fallen = true; Falls++;
            var foot = t.holder.position; var away = RockFreeAway(t, foot, Away(t, foot, from));                  // items 331 + 332: never into a rock (OzTreeFall.Rocks.cs)
            float push = Mathf.Clamp01(1.5f - (foot - from).magnitude / 8f);                        // PalmFall: a close blast tips it faster
            for (int i = 0; i < t.plants.Count; i++) if (t.plants[i]) HideInFar(t, t.plants[i], t.boxes[i]);
            OzFloraWind.Still(t.holder.gameObject);                                                 // level 16 plant wind: the tree and the plants that go down with it are back on their own still materials before the cut, so the stump takes those (no manager, another level: nothing happens)
            float cut = Mathf.Clamp(t.height * 0.08f, 0.35f, 0.6f);
            var top = CutStump(t, foot.y + cut);
            foreach (var r in t.main.GetComponentsInChildren<Renderer>(true)) r.enabled = true;     // DetailProp.Break hid the body: it is the part that falls
            StartCoroutine(Fall(t, away, push, top ? cut : 0f, top));
            Log(t, "falls");
        }

        /// <summary>The plant's own lowest part becomes the stump: its mesh cut flat at the world height y, the cut closed, the same
        /// materials, drawn with the near objects of its cell. Returns the rest, which the plant draws from now on (null = no cut:
        /// the mesh cannot be read or nothing of it lies below y; the whole plant falls then and no stump stays).</summary>
        Mesh CutStump(Stand t, float y)
        {
            var mf = t.main.GetComponent<MeshFilter>(); var mr = t.main.GetComponent<MeshRenderer>(); var src = mf ? mf.sharedMesh : null;
            if (!src || !mr || !src.isReadable) return null;
            var vs = src.vertices; var side = new float[vs.Length]; int below = 0;
            for (int i = 0; i < vs.Length; i++) { side[i] = t.main.TransformPoint(vs[i]).y - y; if (side[i] < 0f) below++; }
            if (below == 0 || below == vs.Length) return null;
            OzMeshCut.Split(src, vs, side, t.main.InverseTransformDirection(Vector3.up), out var low, out var high);
            if (low.vertexCount == 0 || high.vertexCount == 0) { Destroy(low); Destroy(high); return null; }
            OzMeshCut.Widen(low, t.main.InverseTransformDirection(Vector3.up), stumpWide);
            made.Add(low); made.Add(high);
            var go = new GameObject("TreeStump_" + t.tx + "_" + t.ty) { layer = t.main.gameObject.layer };
            var st = go.transform; st.SetParent(t.holder.parent, false);
            st.SetPositionAndRotation(t.main.position, t.main.rotation); st.localScale = t.main.localScale;           // the holder and the cell have no scale of their own
            go.AddComponent<MeshFilter>().sharedMesh = low;
            var sr = go.AddComponent<MeshRenderer>(); sr.sharedMaterials = mr.sharedMaterials; sr.shadowCastingMode = mr.shadowCastingMode;
            if (t.cell)
            {   // with the near objects of its cell, so it costs nothing in the far views
                var lods = t.cell.GetLODs();
                if (lods.Length > 0)
                {
                    var rs = new List<Renderer>(lods[0].renderers) { sr }; lods[0].renderers = rs.ToArray(); t.cell.SetLODs(lods);
                    if (nearOnly.Contains(t.cell)) t.cell.ForceLOD(0);
                }
            }
            // the cut face: pale fresh wood with a year ring or two, so the stump reads on dark soil from the default camera height
            var cut = OzMeshCut.CapMesh(low, OzMeshCut.LowCap, t.main.InverseTransformDirection(Vector3.up).normalized);
            if (cut)
            {
                made.Add(cut);
                var cg = new GameObject("TreeStumpCut_" + t.tx + "_" + t.ty) { layer = go.layer };
                cg.transform.SetParent(st.parent, false);
                cg.transform.SetPositionAndRotation(st.position + Vector3.up * 0.02f, st.rotation); cg.transform.localScale = st.localScale;
                cg.AddComponent<MeshFilter>().sharedMesh = cut;
                var cr = cg.AddComponent<MeshRenderer>(); cr.sharedMaterial = CutWood(); cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (t.cell)
                {
                    var lods = t.cell.GetLODs();
                    if (lods.Length > 0) { var rs = new List<Renderer>(lods[0].renderers) { cr }; lods[0].renderers = rs.ToArray(); t.cell.SetLODs(lods); }
                }
                StumpCuts++;
            }
            mf.sharedMesh = high; t.stump = true; Stumps++;
            return high;
        }

        /// <summary>Checks: stumps that got their cut-wood face.</summary>
        public static int StumpCuts;
        static Material cutWood;
        /// <summary>Fresh cut wood: pale, two year rings, a thin bark rim (one small picture made here, read from the middle outwards).</summary>
        static Material CutWood()
        {
            if (cutWood) return cutWood;
            var tex = new Texture2D(64, 2, TextureFormat.RGBA32, false) { name = "OzCutWood", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            Color wood = new Color(0.86f, 0.72f, 0.47f), ring = new Color(0.66f, 0.50f, 0.29f), heart = new Color(0.78f, 0.62f, 0.38f), bark = new Color(0.36f, 0.26f, 0.16f);
            for (int x = 0; x < 64; x++)
            {
                float r = x / 63f;
                var c = r < 0.10f ? heart : r > 0.93f ? bark : (Mathf.Abs(r - 0.42f) < 0.035f || Mathf.Abs(r - 0.72f) < 0.035f) ? ring : wood;
                tex.SetPixel(x, 0, c); tex.SetPixel(x, 1, c);
            }
            tex.Apply(false, true);
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            cutWood = new Material(sh) { name = "OzCutWood" };
            cutWood.SetTexture("_BaseMap", tex); cutWood.SetColor("_BaseColor", Color.white); cutWood.SetFloat("_Smoothness", 0.08f);
            return cutWood;
        }

        /// <summary>The far version of the cell stops showing this plant.</summary>
        void HideInFar(Stand t, Transform plant, Bounds box)
        {
            if (!t.cell) return;
            var ft = t.cell.transform.Find("FAR"); var mf = ft ? ft.GetComponent<MeshFilter>() : null;
            if (mf && mf.sharedMesh && farRun.TryGetValue(plant.name, out var run) && FoldRun(mf, run, box)) { FarCuts++; return; }
            if (nearOnly.Add(t.cell)) t.cell.ForceLOD(0);                                           // no run known, or it is not this plant's: the cell stays on its near objects
            FarForced++;
        }

        static bool FoldRun(MeshFilter mf, Vector2Int run, Bounds box)
        {
            var sm = mf.sharedMesh; int a = run.x, n = run.y;
            if (!sm.isReadable || a < 0 || n <= 0 || a + n > sm.vertexCount) return false;
            var vs = sm.vertices; var ft = mf.transform; box.Expand(1.5f);
            for (int i = a; i < a + n; i++) if (!box.Contains(ft.TransformPoint(vs[i]))) return false;                // not this plant's points: the mesh is left alone
            var p = vs[a]; for (int i = a + 1; i < a + n; i++) vs[i] = p;
            mf.mesh.vertices = vs;                                                                  // the cell's own copy of its far mesh, from its first felled tree on
            return true;
        }

        static void Pose(Transform h, Vector3 pivot, Vector3 axis, float th, Vector3 p0, Quaternion r0)
        {
            var q = Quaternion.AngleAxis(th * Mathf.Rad2Deg, axis);
            h.SetPositionAndRotation(pivot + q * (p0 - pivot), q * r0);
        }

        IEnumerator Fall(Stand t, Vector3 away, float push, float cut, Mesh top)
        {
            var game = ZGame.I; var fx = OpenZFxPlayer.I; var h = t.holder;
            var foot = h.position; float L = Mathf.Max(2f, t.height);
            var pivot = foot + Vector3.up * cut + away * Mathf.Clamp(t.radius * 0.12f, 0.15f, 0.45f);                 // PalmFall's hinge: the far edge of the snap, at the top of the stump
            var axis = Vector3.Cross(Vector3.up, away).normalized;                                  // + angle tips the top toward `away`
            var mid = foot + Vector3.up * (L * 0.45f);
            // where it comes to rest: the middle of the crown (0.72 of the height) 0.6 of the crown's radius over the ground there
            float hc = Mathf.Max(1f, L * 0.72f - cut), rc = Mathf.Min(t.radius, hc * 0.9f);
            var at = pivot + away * hc; float gy = game ? game.GroundY(at.x, at.z) : foot.y;
            // over water it does not go down to the bed: wood floats, the middle of its crown at the surface (half under)
            float pool = fx && OpenZFxPlayer.IsWaterTile(at) ? fx.WaterYAt(at) : float.NegativeInfinity;
            bool floats = !float.IsNegativeInfinity(pool) && pool > gy + 0.1f;
            float land = Mathf.Acos(Mathf.Clamp(((floats ? pool : gy + 0.6f * rc) - pivot.y) / hc, -0.25f, 0.9f));
            float th = 0f, w = kick + 0.5f * push, k3 = 3f * 9.81f / (2f * L), clock = 0f; bool creaked = false;
            var p0 = h.position; var r0 = h.rotation;
            while (true)
            {
                float dt = game && game.paused ? 0f : Mathf.Min(Time.deltaTime, 0.05f);
                clock += dt; w += k3 * Mathf.Sin(th) * dt; th += w * dt;
                if (!creaked && clock > 0.12f) { creaked = true; DetailProp.Play("dtl_tree_creak", Creak, mid, 0.55f, Random.Range(0.62f, 0.75f), -1, 0f); }
                bool down = th >= land || clock > 8f;
                if (down) th = land;
                Pose(h, pivot, axis, th, p0, r0);
                if (down) break;
                yield return null;
            }
            // the crown is down: thud, leaves, dust (PalmFall's crown contact); a splash where it lands in water
            t.landed = true; Landed++; var landSw = System.Diagnostics.Stopwatch.StartNew();
            var crown = pivot + Quaternion.AngleAxis(land * Mathf.Rad2Deg, axis) * (Vector3.up * hc);
            float vol = Mathf.Clamp(w * hc / 7f, 0.35f, 1f);                                        // by the crown's speed
            float wy = fx && OpenZFxPlayer.IsWaterTile(crown) ? fx.WaterYAt(crown) : float.NegativeInfinity;
            bool wet = !float.IsNegativeInfinity(wy);
            crown.y = wet ? wy : (game ? game.GroundY(crown.x, crown.z) : foot.y) + 0.2f;
            DetailProp.Play("dtl_tree_crash", CrownThud, crown, 0.75f * vol + 0.15f, Random.Range(0.72f, 0.85f), -1, 0f);
            DetailProp.Play("dtl_tree_leaves", LeafRustle, crown, 0.55f * vol + 0.1f, Random.Range(0.9f, 1.05f), -1, 0f);
            if (wet) { DetailProp.Play("dtl_tree_splash", Splash, crown, 0.5f, Random.Range(0.85f, 1.1f), -1, 0f); fx.Splash(crown, 0.8f); fx.Ripple(crown, 0.8f); }
            else if (fx) { fx.Dust(crown, 0.9f); fx.Dust(Vector3.Lerp(pivot, crown, 0.5f), 0.6f); }
            else if (FxManager.I) FxManager.I.Dust(crown, 0.6f);
            var rng = new System.Random(t.tx * 73 + t.ty * 7 + Time.frameCount);
            bits.Burst(crown + Vector3.up * 0.4f, 12, 2, 2.4f, 0.1f, rng); bits.Burst(crown + Vector3.up * 0.4f, 12, 3, 2.4f, 0.1f, rng);
            Log(t, "down"); LandTime((float)landSw.Elapsed.TotalMilliseconds);                       // the blast count's split (OzTreeFall.Blast.cs)
            float amp = Mathf.Clamp(w * 2f, 1f, 3.5f) * Mathf.Deg2Rad, s = 0f;                      // a short soft bounce on the crown
            while (s < 0.7f)
            {
                s += game && game.paused ? 0f : Time.deltaTime;
                Pose(h, pivot, axis, land - amp * Mathf.Exp(-7f * s) * Mathf.Abs(Mathf.Sin(10f * s)), p0, r0);
                yield return null;
            }
            Pose(h, pivot, axis, land, p0, r0);
            // it lies as a log, then sinks away (as the palm's parts do); on water it floats that long, rocking a little, with a
            // faint ring at its crown now and then where the camera looks
            float lie = Random.Range(life.x, life.y);
            if (floats) { Floated++; Log(t, "floats"); }
            float ringAt = 1.5f;
            for (s = 0f; s < lie; s += game && game.paused ? 0f : Time.deltaTime)
            {
                if (floats)
                {
                    Pose(h, pivot, axis, land + 0.012f * Mathf.Sin(s * 1.7f) * Mathf.Min(1f, s), p0, r0);
                    if (s >= ringAt) { ringAt = s + Random.Range(2.2f, 3.4f); if (fx && WreckFx.OnScreen(crown, 0.1f)) fx.RippleCalm(new Vector3(crown.x, pool, crown.z), 0.45f, 2f); }
                }
                yield return null;
            }
            var rest = h.position; float depth = t.radius * 1.7f + 0.6f;
            for (s = 0f; s < 1f; s += (game && game.paused ? 0f : Time.deltaTime) / 3f) { h.position = rest + Vector3.down * (depth * s * s); yield return null; }
            foreach (var p in t.plants) if (p) p.gameObject.SetActive(false);
            if (top) { var mf = t.main.GetComponent<MeshFilter>(); if (mf) mf.sharedMesh = null; made.Remove(top); Destroy(top); }
        }

        static void Log(Stand t, string what)
        {
            LastLog = "tree " + t.tx + "," + t.ty + " " + what;
            if (Debug.isDebugBuild) Debug.Log("[OpenZ TreeFall] " + LastLog);
        }
    }

    /// <summary>Cuts a readable mesh in two along a plane that is given as one signed distance per point (below 0 = the low part).
    /// Both parts keep the sub-meshes, the normals and the first uv set. Every closed cut line gets a flat face (the low part's
    /// faces `up`, the high part's the other way); open cut lines (single leaf faces) stay open.</summary>
    public static class OzMeshCut
    {
        internal sealed class Half
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int>[] tris; public readonly List<int> seg = new List<int>();     // seg: per cut piece its two points and its sub-mesh
            public readonly List<int> cap = new List<int>();                                         // the flat faces on the cut: three points each, the first is the face's middle
            public readonly Dictionary<int, int> own = new Dictionary<int, int>(); public readonly Dictionary<long, int> cut = new Dictionary<long, int>();
            public Half(int subs) { tris = new List<int>[subs]; for (int i = 0; i < subs; i++) tris[i] = new List<int>(); }
        }

        public static void Split(Mesh src, Vector3[] vs, float[] side, Vector3 up, out Mesh low, out Mesh high)
        {
            var ns = src.normals; var us = src.uv; bool hasN = ns != null && ns.Length == vs.Length, hasU = us != null && us.Length == vs.Length;
            int subs = src.subMeshCount; var tris = new int[subs][];
            for (int s = 0; s < subs; s++) tris[s] = src.GetTriangles(s);
            Cut(vs, hasN ? ns : null, hasU ? us : null, tris, side, up, out var lo, out var hi);
            low = Build(lo, src.name + "_stump", subs, hasN, hasU); high = Build(hi, src.name + "_top", subs, hasN, hasU);
            LowCap = lo.cap.ToArray();
        }

        /// <summary>The flat faces that closed the low part of the last Split: three of its points each, the first the face's middle.</summary>
        public static int[] LowCap = new int[0];

        /// <summary>The cut faces of a low part as a mesh of their own (read from the part as it is now, so after a Widen), facing
        /// `up`. Its uv runs from 0 at each face's middle to 1 at its rim: a ring picture draws year rings.</summary>
        public static Mesh CapMesh(Mesh low, int[] cap, Vector3 up)
        {
            if (!low || cap == null || cap.Length < 3) return null;
            var src = low.vertices; int n = cap.Length;
            var v = new Vector3[n]; var nr = new Vector3[n]; var uv = new Vector2[n]; var tr = new int[n];
            for (int i = 0; i < n; i++) { v[i] = src[cap[i]]; nr[i] = up; uv[i] = new Vector2(i % 3 == 0 ? 0f : 1f, 0.5f); tr[i] = i; }
            var m = new Mesh { name = low.name + "_cut" };
            if (n > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = v; m.normals = nr; m.uv = uv; m.triangles = tr; m.RecalculateBounds();
            return m;
        }

        /// <summary>The cut itself, on plain arrays (ns and us may be null; tris: one index list per sub-mesh).</summary>
        internal static void Cut(Vector3[] vs, Vector3[] ns, Vector2[] us, int[][] tris, float[] side, Vector3 up, out Half lo, out Half hi)
        {
            bool hasN = ns != null, hasU = us != null; int subs = tris.Length;
            var low = new Half(subs); var high = new Half(subs);
            int Own(Half g, int i)
            {
                if (g.own.TryGetValue(i, out int k)) return k;
                k = g.v.Count; g.v.Add(vs[i]); if (hasN) g.n.Add(ns[i]); if (hasU) g.uv.Add(us[i]);
                g.own[i] = k; return k;
            }
            int Cross(Half g, int a, int b)                                                         // the point where the edge a-b crosses the plane
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (g.cut.TryGetValue(key, out int k)) return k;
                float f = side[a] / (side[a] - side[b]);
                k = g.v.Count; g.v.Add(Vector3.LerpUnclamped(vs[a], vs[b], f));
                if (hasN) g.n.Add(Vector3.LerpUnclamped(ns[a], ns[b], f).normalized);
                if (hasU) g.uv.Add(Vector2.LerpUnclamped(us[a], us[b], f));
                g.cut[key] = k; return k;
            }
            for (int s = 0; s < subs; s++)
            {
                var tr = tris[s];
                for (int i = 0; i + 2 < tr.Length; i += 3)
                {
                    int a = tr[i], b = tr[i + 1], c = tr[i + 2];
                    bool la = side[a] < 0f, lb = side[b] < 0f, lc = side[c] < 0f;
                    if (la == lb && lb == lc) { var whole = la ? low : high; whole.tris[s].Add(Own(whole, a)); whole.tris[s].Add(Own(whole, b)); whole.tris[s].Add(Own(whole, c)); continue; }
                    // turn the triangle so that `a` is the point alone on its side (the turning sense stays)
                    if (lb != la && lb != lc) { int x = a; a = b; b = c; c = x; la = lb; }
                    else if (lc != la && lc != lb) { int x = c; c = b; b = a; a = x; la = lc; }
                    var one = la ? low : high; var two = la ? high : low;
                    int a1 = Own(one, a), ab1 = Cross(one, a, b), ac1 = Cross(one, a, c);
                    one.tris[s].Add(a1); one.tris[s].Add(ab1); one.tris[s].Add(ac1);
                    int ab2 = Cross(two, a, b), b2 = Own(two, b), c2 = Own(two, c), ac2 = Cross(two, a, c);
                    two.tris[s].Add(ab2); two.tris[s].Add(b2); two.tris[s].Add(c2);
                    two.tris[s].Add(ab2); two.tris[s].Add(c2); two.tris[s].Add(ac2);
                    one.seg.Add(ab1); one.seg.Add(ac1); one.seg.Add(s);
                    two.seg.Add(ab2); two.seg.Add(ac2); two.seg.Add(s);
                }
            }
            Cap(low, up, hasN, hasU, true); Cap(high, -up, hasN, hasU, false);
            lo = low; hi = high;
        }

        /// <summary>A flat face on every closed cut line: a fan round its middle, turned to face `dir`.</summary>
        /// <remarks>roundOpen (the stump's side): a cut line that is NOT closed (a trunk built of shells that only touch, a seam left
        /// open) but runs round its middle (six pieces or more, three quarters of the way round) still gets its face in h.cap, for the
        /// cut-wood picture on top of the stump; the mesh itself gets no face there, as before.</remarks>
        static void Cap(Half h, Vector3 dir, bool hasN, bool hasU, bool roundOpen)
        {
            int m = h.seg.Count / 3; if (m < 3) return;
            var node = new Dictionary<Vector3Int, int>(); var first = new List<int>(); var a = new int[m]; var b = new int[m];   // points at one place are one node (uv seams split them)
            int Node(int p)
            {
                var q = h.v[p]; var key = new Vector3Int(Mathf.RoundToInt(q.x * 1e4f), Mathf.RoundToInt(q.y * 1e4f), Mathf.RoundToInt(q.z * 1e4f));
                if (!node.TryGetValue(key, out int id)) { id = first.Count; node[key] = id; first.Add(p); }
                return id;
            }
            for (int i = 0; i < m; i++) { a[i] = Node(h.seg[3 * i]); b[i] = Node(h.seg[3 * i + 1]); }
            int nn = first.Count; var root = new int[nn]; var deg = new int[nn];
            for (int i = 0; i < nn; i++) root[i] = i;
            int Find(int x) { while (root[x] != x) { root[x] = root[root[x]]; x = root[x]; } return x; }
            for (int i = 0; i < m; i++)
            {
                if (a[i] == b[i]) continue;
                deg[a[i]]++; deg[b[i]]++;
                int ra = Find(a[i]), rb = Find(b[i]); if (ra != rb) root[ra] = rb;
            }
            // a cut line is closed when each of its points joins an even number of pieces (two on a plain ring)
            var open = new bool[nn]; var sum = new Vector3[nn]; var uvs = new Vector2[nn]; var len = new float[nn]; var count = new int[nn];
            for (int i = 0; i < nn; i++) if (deg[i] % 2 != 0) open[Find(i)] = true;
            for (int i = 0; i < m; i++)
            {
                if (a[i] == b[i]) continue;
                int r = Find(a[i]); Vector3 p = h.v[first[a[i]]], q = h.v[first[b[i]]]; float l = (q - p).magnitude;
                sum[r] += (p + q) * (0.5f * l); len[r] += l; count[r]++;
                if (hasU) uvs[r] += (h.uv[h.seg[3 * i]] + h.uv[h.seg[3 * i + 1]]) * (0.5f * l);
            }
            var round = new float[nn];
            if (roundOpen)
                for (int i = 0; i < m; i++)
                {
                    if (a[i] == b[i]) continue;
                    int r = Find(a[i]); if (!open[r] || len[r] <= 1e-6f) continue;
                    Vector3 c0 = sum[r] / len[r], vp = h.v[first[a[i]]] - c0, vq = h.v[first[b[i]]] - c0;
                    vp -= dir * Vector3.Dot(vp, dir); vq -= dir * Vector3.Dot(vq, dir);
                    if (vp.sqrMagnitude > 1e-10f && vq.sqrMagnitude > 1e-10f) round[r] += Vector3.Angle(vp, vq) * Mathf.Deg2Rad;
                }
            var centre = new int[nn]; var ring = new int[nn];
            for (int i = 0; i < nn; i++) { centre[i] = -1; ring[i] = -1; }
            int Ring(int id, int p)                                                                 // the face's own point at a ring point (it has the face's normal)
            {
                if (ring[id] >= 0) return ring[id];
                int k = h.v.Count; h.v.Add(h.v[p]); if (hasN) h.n.Add(dir); if (hasU) h.uv.Add(h.uv[p]);
                ring[id] = k; return k;
            }
            for (int i = 0; i < m; i++)
            {
                if (a[i] == b[i]) continue;
                int r = Find(a[i]); if (count[r] < 3 || len[r] <= 1e-6f) continue;
                bool faceOnly = open[r];
                if (faceOnly && !(roundOpen && count[r] >= 6 && round[r] >= 4.7f)) continue;
                if (centre[r] < 0) { centre[r] = h.v.Count; h.v.Add(sum[r] / len[r]); if (hasN) h.n.Add(dir); if (hasU) h.uv.Add(uvs[r] / len[r]); }
                int c = centre[r], p = Ring(a[i], h.seg[3 * i]), q = Ring(b[i], h.seg[3 * i + 1]);
                var tl = h.tris[h.seg[3 * i + 2]];
                bool fwd = Vector3.Dot(Vector3.Cross(h.v[p] - h.v[c], h.v[q] - h.v[c]), dir) >= 0f;
                if (!faceOnly) { tl.Add(c); tl.Add(fwd ? p : q); tl.Add(fwd ? q : p); }
                h.cap.Add(c); h.cap.Add(fwd ? p : q); h.cap.Add(fwd ? q : p);
            }
        }

        /// <summary>Makes every separate piece of a mesh (a trunk, a root, a leaf) `k` times as wide about its own middle line; heights
        /// along `up` stay. Points at one place are one point (uv seams split them); pieces = points joined by faces.</summary>
        public static void Widen(Mesh m, Vector3 up, float k)
        {
            if (!m || Mathf.Abs(k - 1f) < 0.001f) return;
            var v = m.vertices; int n = v.Length; if (n == 0) return;
            up = up.sqrMagnitude > 1e-8f ? up.normalized : Vector3.up;
            var node = new Dictionary<Vector3Int, int>(); var id = new int[n]; int nodes = 0;
            for (int i = 0; i < n; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(v[i].x * 1e4f), Mathf.RoundToInt(v[i].y * 1e4f), Mathf.RoundToInt(v[i].z * 1e4f));
                if (!node.TryGetValue(key, out int q)) { q = nodes++; node[key] = q; }
                id[i] = q;
            }
            var root = new int[nodes]; for (int i = 0; i < nodes; i++) root[i] = i;
            int Find(int x) { while (root[x] != x) { root[x] = root[root[x]]; x = root[x]; } return x; }
            for (int s = 0; s < m.subMeshCount; s++)
            {
                var tr = m.GetTriangles(s);
                for (int i = 0; i + 2 < tr.Length; i += 3)
                {
                    int a = Find(id[tr[i]]), b = Find(id[tr[i + 1]]), c = Find(id[tr[i + 2]]);
                    if (a != b) root[a] = b;
                    c = Find(c); b = Find(b); if (c != b) root[c] = b;
                }
            }
            var sum = new Vector3[nodes]; var cnt = new int[nodes];
            for (int i = 0; i < n; i++) { int r = Find(id[i]); sum[r] += v[i]; cnt[r]++; }
            for (int i = 0; i < n; i++)
            {
                int r = Find(id[i]); var c = sum[r] / cnt[r]; var d = v[i] - c; float along = Vector3.Dot(d, up);
                v[i] = c + up * along + (d - up * along) * k;
            }
            m.vertices = v; m.RecalculateBounds();
        }

        static Mesh Build(Half h, string name, int subs, bool hasN, bool hasU)
        {
            var m = new Mesh { name = name };
            if (h.v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(h.v); if (hasN) m.SetNormals(h.n); if (hasU) m.SetUVs(0, h.uv);
            m.subMeshCount = subs;
            for (int s = 0; s < subs; s++) m.SetTriangles(h.tris[s], s);
            if (!hasN) m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
