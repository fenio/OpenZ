using System.Collections.Generic;
using System.Threading;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, items 331 + 332 for the guns and the buildings: a big falling piece must not sink into a gun or a building, as it
    /// must not sink into a rock (OzRockSolid.All.cs). A gun (its base and its turret), a building (forts, factories, radars, repair
    /// stations, bridges), a hut, and on level 16 the ruins and the pyramids have no collider, so every big piece passed through them:
    /// the shuttle's break-up pieces and its crew, vehicle wreck pieces and flipped hulls, the radar dish, a building's big chunks, the
    /// fort cannon's pieces. Now, the same way the rocks are solid:
    /// <list type="bullet">
    /// <item>every drawn part of such a static (each mesh of it, at least <see cref="MinPartM"/> long) gets a collider of exactly its
    /// drawn shape (its own triangles; its box when a mesh cannot be read), on the rock layer of OzRockSolid, so only the big pieces meet
    /// them (the colliders that include that layer: OzRockBody and the fort cannon's pieces); small chips, sandbags and every other
    /// body keep their contacts as they were, and the units are rules, never physics;</item>
    /// <item>the colliders are switched on only while a big piece is near (from OzRockSolid.ArmAround / ArmNear: every 0.2 s while a
    /// big body moves, for 1.6 s) and off again soon after it rests; a collider is a child of its drawn part, so a turning turret, a
    /// spinning radar dish or a gate door carries it;</item>
    /// <item>the scripted landings (a tossed turret, a robot thrown by a blast, a felled tree, the fort cannon's toss) see the statics'
    /// drawn boxes as they see the rocks (<see cref="Disc"/>, <see cref="Ball"/>, <see cref="Within"/>, <see cref="BoxHit"/>);</item>
    /// <item>a static that dies (the rules destroy it) loses its colliders before its own break-up or wreck is made (its own pieces never
    /// meet it); what rested on it wakes and falls (OzRockBody.SupportGone); a revived building is made solid again.</item>
    /// </list>
    /// Cost: the meshes are baked for the physics once per level on a background thread (each distinct mesh once), the colliders are made
    /// ahead from <see cref="PrebuildAfter"/> s into the level in at most <see cref="BuildBudgetMs"/> a frame (switched off); after that
    /// a static is only switched on and off. Idle (nothing near moving): nothing is done but a look at the frame's death events.
    /// View only, no rule changes. Switch: <see cref="Enabled"/> (off = the statics as before).
    /// </summary>
    [DefaultExecutionOrder(-41)]                                                                   // after ZGame's tick (-50), before OzRockSolid (-40) and CombatView (50)
    public sealed partial class OzStaticSolid : MonoBehaviour                                     // + OzStaticSolid.Made.cs (the colliders made, counted)
    {
        public static bool Enabled = true;
        /// <summary>Make every static's colliders ahead from the level's start (off: when a big piece first comes near).</summary>
        public static bool Prebuild = true;
        public static float BuildBudgetMs = 0.6f, PrebuildAfter = 2f;
        /// <summary>Level 16: the ruins and the pyramids are solid too.</summary>
        public static bool Ruins = true;
        /// <summary>A drawn part shorter than this (m) on its longest side is left out (lamp lenses, small trims).</summary>
        public static float MinPartM = 0.3f;
        /// <summary>A destroyed building's ruin is solid from this long (s) after its collapse (its own chunks rest or go within 17 s and
        /// settle into its rubble as they always did).</summary>
        public static float RuinAfterS = 20f;
        public static bool On => Enabled && OzRockSolid.AllOn;

        // ------------------------------------------------------------------ statistics (Play check, State menu)
        public static int Statics, Guns, Buildings, Huts, Decor, Parts, MeshParts, BoxParts, Built, Prebuilt, BakeMeshes, ArmCalls, Armed, Disarms, GoneN, Revived,
                          Relisted, PartsLost, Woken, DiscHits, BallHits, BoxHits, CollidersOn, BuiltLate, RuinsMade;
        public static float MaxBuildMs, BakeMsTotal;
        static int bakeDone;
        public static int BakeDone => Volatile.Read(ref bakeDone);

        sealed class Part
        {
            public Transform t; public Renderer r; public Mesh mesh; public int bake = -1; public GameObject holder; public Collider col;
            public bool built, on; public Bounds box;
            public bool Shown => t && r && r.enabled && t.gameObject.activeInHierarchy;
        }
        sealed class St
        {
            public int id, stamp; public string type, what; public GameObject root; public UnitView view; public bool inactiveToo;
            public readonly List<Part> parts = new List<Part>(4); public Bounds box; public bool armed, dead, hold, any, ruin; public float until, lostAt, rescanAt;
        }

        static readonly List<St> statics = new List<St>();
        static readonly Dictionary<int, St> bySim = new Dictionary<int, St>();
        static readonly Dictionary<long, List<St>> grid = new Dictionary<long, List<St>>();
        static readonly List<St> armedList = new List<St>(), near = new List<St>(32);
        static readonly List<Part> queue = new List<Part>(); static readonly List<Vector3> queueAt = new List<Vector3>();
        static readonly List<Mesh> bakeList = new List<Mesh>(); static readonly Dictionary<Mesh, int> bakeIdx = new Dictionary<Mesh, int>();
        static readonly List<MeshRenderer> rbuf = new List<MeshRenderer>(64);
        static readonly List<Collider> gone = new List<Collider>(16);
        static int listedScene = -1, listedCount = -1, missing, stampNow, generation, bakeQueued, prebuildCursor, prebuildPart;
        static float listedAt = -100f; static volatile bool bakeRunning; static bool decorListed;
        const float Cell = 8f;

        // ------------------------------------------------------------------ the statics of the level
        static void Clear()
        {
            foreach (var st in statics) DropHolders(st);
            statics.Clear(); bySim.Clear(); grid.Clear(); armedList.Clear(); queue.Clear(); queueAt.Clear();
            bakeList.Clear(); bakeIdx.Clear(); Interlocked.Increment(ref generation); Volatile.Write(ref bakeDone, 0);
            bakeQueued = 0; prebuildCursor = 0; prebuildPart = 0; decorListed = false; listedCount = -1;
            Statics = Guns = Buildings = Huts = Decor = Parts = 0;
            ResetMade();                                                                                 // the counts of the colliders made start again with the level
        }

        static void EnsureListed()
        {
            var g = ZGame.I; if (!g || g.World == null) return;
            int h = SceneManager.GetActiveScene().handle;
            if (h != listedScene) { Clear(); listedScene = h; }
            if (Time.timeSinceLevelLoad < 0.5f) return;                                                  // the views are bound first
            int n = g.World.Objects.Count;
            if (Time.unscaledTime - listedAt < 2f || (n == listedCount && (missing == 0 || Time.timeSinceLevelLoad > 30f))) return;
            listedCount = n; listedAt = Time.unscaledTime; Relisted++; missing = 0;
            var objs = g.World.Objects;
            for (int i = 0; i < objs.Count; i++)
            {
                var o = objs[i];
                if (o == null || bySim.ContainsKey(o.Id)) continue;
                int before = statics.Count;
                if (o.Kind == ZKind.Cannon)
                {
                    if (g.Views.TryGetValue(o.Id, out var v) && v) { Add(o.Id, o.Type, v.gameObject, v, "gun", false); Guns++; }
                    else if (!o.Destroyed) missing++;                                                    // its view is made a little later (a tower gun): listed again
                }
                else if (o.Kind == ZKind.Building || (o.Kind == ZKind.MapItem && o.Type == "hut"))
                {
                    if (g.StaticViews.TryGetValue(o.Id, out var go) && go) { bool hut = o.Kind == ZKind.MapItem; Add(o.Id, o.Type, go, null, hut ? "hut" : "building", false); if (hut) Huts++; else Buildings++; }
                    else if (!o.Destroyed) missing++;
                }
                if (statics.Count > before && o.Destroyed) statics[statics.Count - 1].dead = true;          // listed already dead (a ruin): solid again only when revived
            }
            if (!decorListed)
            {
                decorListed = true;
                if (Ruins)
                {
                    var ruins = GameObject.Find("Environment/L16_Structures/Ruins");
                    if (ruins) foreach (Transform t in ruins.transform) { Add(0, "ruin", t.gameObject, null, "ruin", false); Decor++; }
                    foreach (var p in L16Pyramid.All) if (p) { Add(0, "pyramid", p.gameObject, null, "pyramid", true); Decor++; }
                }
            }
            StartBake();
        }

        static void Add(int id, string type, GameObject root, UnitView view, string what, bool inactiveToo)
        {
            var st = new St { id = id, type = type, root = root, view = view, what = what, inactiveToo = inactiveToo };
            Scan(st);
            statics.Add(st); Statics++;
            if (id != 0) bySim[id] = st;
            if (st.any) GridAdd(st);
        }

        static void Scan(St st)
        {
            st.parts.Clear(); st.any = false;
            Rewind();                                                                                    // a static scanned again is made ahead again
            if (!st.root) return;
            st.root.GetComponentsInChildren(st.inactiveToo, rbuf);
            for (int i = 0; i < rbuf.Count; i++)
            {
                var mr = rbuf[i];
                if (!mr || (!st.inactiveToo && (!mr.enabled || !mr.gameObject.activeInHierarchy)) || !Wanted(st, mr)) continue;
                var mf = mr.GetComponent<MeshFilter>(); var m = mf ? mf.sharedMesh : null;
                if (!m) continue;
                var wb = WorldBox(mr.transform, m.bounds);
                if (Mathf.Max(wb.size.x, Mathf.Max(wb.size.y, wb.size.z)) < MinPartM) continue;
                var p = new Part { t = mr.transform, r = mr, mesh = m, box = wb };
                if (m.isReadable) p.bake = BakeIndex(m);
                st.parts.Add(p); Parts++;
                if (st.any) st.box.Encapsulate(wb); else { st.box = wb; st.any = true; }
            }
            rbuf.Clear();
        }

        /// <summary>A drawn part of this static (not a glow, a decal, a lens, a light cone, a debris body, a unit seated in or on it, or a
        /// farther level of detail).</summary>
        static bool Wanted(St st, MeshRenderer mr)
        {
            var n = mr.name.ToLowerInvariant();
            if (n.Contains("shadow") || n.Contains("label") || n.Contains("glow") || n.Contains("decal") || n.Contains("lens") || n.Contains("beam") || n.StartsWith("frag_") || n.StartsWith("oz_")) return false;
            var mat = mr.sharedMaterial; if (mat && mat.renderQueue >= 2450) return false;
            if (mr.GetComponentInParent<Rigidbody>(true)) return false;
            if (mr.GetComponentInParent<UnitView>(true) != st.view) return false;                     // a gunner on a gun, a tower gun on a fort: not part of it
            var lg = mr.GetComponentInParent<LODGroup>(true);
            if (lg && lg.transform.IsChildOf(st.root.transform))
            {
                var lods = lg.GetLODs();
                if (lods.Length > 0 && System.Array.IndexOf(lods[0].renderers, mr) < 0) return false;
            }
            return true;
        }

        static Bounds WorldBox(Transform t, Bounds lb)
        {
            var m = t.localToWorldMatrix; var b = new Bounds(m.MultiplyPoint3x4(lb.min), Vector3.zero);
            for (int i = 1; i < 8; i++) b.Encapsulate(m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? lb.min.x : lb.max.x, (i & 2) == 0 ? lb.min.y : lb.max.y, (i & 4) == 0 ? lb.min.z : lb.max.z)));
            return b;
        }

        static long Key(int i, int j) => ((long)i << 32) ^ (uint)j;

        static void GridAdd(St st)
        {
            var b = st.box;
            for (int i = Mathf.FloorToInt(b.min.x / Cell); i <= Mathf.FloorToInt(b.max.x / Cell); i++)
                for (int j = Mathf.FloorToInt(b.min.z / Cell); j <= Mathf.FloorToInt(b.max.z / Cell); j++)
                {
                    long k = Key(i, j);
                    if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<St>(2);
                    if (!l.Contains(st)) l.Add(st);
                }
        }

        /// <summary>The statics whose drawn box lies within `radius` (flat) of `at` (a shared list: read it before the next call).</summary>
        static List<St> Near(Vector3 at, float radius)
        {
            near.Clear(); stampNow++;
            int i0 = Mathf.FloorToInt((at.x - radius) / Cell), i1 = Mathf.FloorToInt((at.x + radius) / Cell);
            int j0 = Mathf.FloorToInt((at.z - radius) / Cell), j1 = Mathf.FloorToInt((at.z + radius) / Cell);
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    if (!grid.TryGetValue(Key(i, j), out var l)) continue;
                    for (int n = 0; n < l.Count; n++)
                    {
                        var st = l[n]; if (st.stamp == stampNow) continue;
                        st.stamp = stampNow;
                        if (FlatGap(at, st.box) <= radius) near.Add(st);
                    }
                }
            return near;
        }

        static float FlatGap(Vector3 p, Bounds b)
        {
            float dx = Mathf.Max(0f, Mathf.Max(b.min.x - p.x, p.x - b.max.x)), dz = Mathf.Max(0f, Mathf.Max(b.min.z - p.z, p.z - b.max.z));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>The rules removed it (or its view is gone).</summary>
        static bool SimDead(St st)
        {
            if (!st.root) return true;
            if (st.id == 0) return false;
            if (st.ruin) return !st.root.activeInHierarchy;
            var g = ZGame.I; if (!g || g.World == null) return false;
            var o = g.World.Get(st.id);
            return o == null || o.Destroyed;
        }

        // ------------------------------------------------------------------ the physics bake (a background thread, each mesh once)
        static int BakeIndex(Mesh m)
        {
            if (!bakeIdx.TryGetValue(m, out int i)) { i = bakeList.Count; bakeList.Add(m); bakeIdx[m] = i; BakeMeshes = bakeList.Count; }
            return i;
        }

        static void StartBake()
        {
            if (bakeRunning || bakeQueued >= bakeList.Count) return;
            int from = bakeQueued, n = bakeList.Count - from; var ids = new EntityId[n]; var have = new bool[n];
            for (int i = 0; i < n; i++) { var m = bakeList[from + i]; have[i] = m; if (m) ids[i] = m.GetEntityId(); }
            bakeQueued = bakeList.Count; bakeRunning = true; int gen = Volatile.Read(ref generation);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < ids.Length; i++)
                {
                    if (Volatile.Read(ref generation) != gen) break;
                    try { if (have[i]) Physics.BakeMesh(ids[i], false); } catch (System.Exception) { }
                    if (Volatile.Read(ref generation) == gen) Interlocked.Increment(ref bakeDone);
                }
                BakeMsTotal += (float)sw.Elapsed.TotalMilliseconds;
                bakeRunning = false;
            });
        }

        /// <summary>Its mesh is baked (or it is made as a box): its collider can be made now without cooking on the main thread.</summary>
        static bool Ready(Part p) => p.bake < 0 || p.bake < Volatile.Read(ref bakeDone);

        // ------------------------------------------------------------------ a part's collider (its drawn shape)
        static PhysicsMaterial mat;
        static PhysicsMaterial Mat() => mat ? mat : (mat = new PhysicsMaterial("OZ_StaticSolid") { bounciness = 0.05f, dynamicFriction = 0.7f, staticFriction = 0.85f });
        static readonly System.Diagnostics.Stopwatch buildWatch = new System.Diagnostics.Stopwatch(), oneWatch = new System.Diagnostics.Stopwatch();

        static void Build(Part p)
        {
            if (p == null || p.built) return;
            p.built = true;
            if (!p.t || !p.mesh || OzRockSolid.Layer < 0) return;
            oneWatch.Restart();
            var h = new GameObject("OZ_StaticSolid") { layer = OzRockSolid.Layer };
            h.SetActive(false);                                                                          // no physics shape until it is switched on
            h.transform.SetParent(p.t, false);
            Collider c;
            if (p.mesh.isReadable) { var mc = h.AddComponent<MeshCollider>(); mc.convex = false; mc.sharedMesh = p.mesh; c = mc; MeshParts++; }
            else { var bc = h.AddComponent<BoxCollider>(); bc.center = p.mesh.bounds.center; bc.size = p.mesh.bounds.size; c = bc; BoxParts++; }
            c.sharedMaterial = Mat(); c.enabled = false;
            h.SetActive(true);
            p.holder = h; p.col = c; Built++;
            float ms = (float)oneWatch.Elapsed.TotalMilliseconds; if (ms > MaxBuildMs) MaxBuildMs = ms; Made(ms);
        }

        static void Switch(Part p, bool on)
        {
            if (p == null || !p.col) return;
            on = on && p.Shown;
            if (p.on == on) return;
            p.on = on; p.col.enabled = on; CollidersOn += on ? 1 : -1;
        }

        static void Enqueue(Part p, Vector3 at)
        {
            if (queue.Contains(p)) return;
            queue.Add(p); queueAt.Add(at);
        }

        static void Dequeue(Part p)
        {
            for (int i = queue.Count - 1; i >= 0; i--) if (queue[i] == p) { queue.RemoveAt(i); queueAt.RemoveAt(i); }
        }

        static void DropHolders(St st)
        {
            foreach (var p in st.parts)
            {
                if (p.on) { p.on = false; CollidersOn--; }
                Dequeue(p);
                if (p.holder) { p.holder.SetActive(false); p.holder.transform.SetParent(null, false); Destroy(p.holder); }
                p.holder = null; p.col = null; p.built = false;
            }
        }

        // ------------------------------------------------------------------ switching them on (OzRockSolid.ArmAround / ArmNear)
        /// <summary>The statics whose drawn box lies within `radius` of `at` are solid for `seconds` (`hold`: also while the fort cannon's
        /// pieces live); their parts within `buildNow` that are not made yet are made at once (when baked), the others queued.</summary>
        public static void ArmAround(Vector3 at, float radius, float seconds, float buildNow, bool hold = false)
        {
            if (!On || OzRockSolid.Layer < 0) return;
            Boot(); EnsureListed(); ArmCalls++;
            if (statics.Count == 0) return;
            float until = Time.time + seconds;
            var l = Near(at, radius);
            for (int i = 0; i < l.Count; i++)
            {
                var st = l[i];
                if (st.dead || st.parts.Count == 0) continue;
                st.until = Mathf.Max(st.until, until); st.hold |= hold;
                if (!st.armed) { st.armed = true; Armed++; armedList.Add(st); }
                foreach (var p in st.parts)
                {
                    if (p.built) { Switch(p, true); continue; }
                    if (Ready(p) && FlatGap(at, p.box) <= buildNow) { Dequeue(p); Build(p); BuiltLate++; Switch(p, true); }
                    else Enqueue(p, at);
                }
            }
        }

        // ------------------------------------------------------------------ the manager
        static OzStaticSolid inst;
        float nextPoll, nextLife;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (inst) return;
            var go = new GameObject("OZ_StaticSolid"); DontDestroyOnLoad(go);
            inst = go.AddComponent<OzStaticSolid>();
        }

        void Update()
        {
            if (!On) return;
            var g = ZGame.I; if (!g || g.World == null) return;
            EnsureListed();
            var ev = g.World.Events;
            for (int i = 0; i < ev.Count; i++)
            {   // a static that dies loses its colliders before its own break-up or wreck is made (CombatView, later this frame)
                var e = ev[i];
                if (e.type == ZEventType.Death && bySim.TryGetValue(e.objId, out var st) && !st.dead) Lost(st);
            }
            BuildStep();
            if (Time.time >= nextPoll && armedList.Count > 0) { nextPoll = Time.time + 0.1f; Poll(); }
            if (Time.time >= nextLife) { nextLife = Time.time + 0.5f; Life(g); }
            OzPieceGiveWay.Step(g);
        }

        /// <summary>Queued parts first (nearest to where they were asked for), then (with <see cref="Prebuild"/>) every part of the level
        /// ahead, while the frame's budget lasts; only parts whose mesh the background bake has finished. Nothing to do: no cost.</summary>
        static void BuildStep()
        {
            StartBake();
            bool ahead = Prebuild && Time.timeSinceLevelLoad >= PrebuildAfter && prebuildCursor < statics.Count;
            if (queue.Count == 0 && !ahead) return;
            buildWatch.Restart(); madeNow = 0;
            while (queue.Count > 0 && Room())
            {
                int best = -1; float bd = float.MaxValue;
                for (int i = 0; i < queue.Count; i++) { if (!Ready(queue[i])) continue; float d = FlatGap(queueAt[i], queue[i].box); if (d < bd) { bd = d; best = i; } }
                if (best < 0) break;
                var p = queue[best]; queue.RemoveAt(best); queueAt.RemoveAt(best);
                Build(p);
                foreach (var st in armedList) if (st.parts.Contains(p)) { Switch(p, true); break; }
            }
            while (ahead && prebuildCursor < statics.Count && Room())
            {
                var st = statics[prebuildCursor];
                if (st.dead || prebuildPart >= st.parts.Count) { prebuildCursor++; prebuildPart = 0; continue; }
                var p = st.parts[prebuildPart];
                if (!p.built && !Ready(p)) break;                                                        // wait for the background bake
                prebuildPart++;
                if (p.built) continue;
                Build(p); Prebuilt++;
                if (st.armed) Switch(p, true);
            }
            NoteFrame(buildWatch.Elapsed.TotalMilliseconds);                                             // the heaviest frame; when all were made
        }

        /// <summary>Every 0.1 s while any static is switched on: a part that hides (its renderer off, a state change) loses its contact; a
        /// part taken away from its static (a wreck's piece) loses its collider; a static whose time is up is switched off.</summary>
        static void Poll()
        {
            bool piecesLive = OzFortGunBreakup.LiveCount > 0;
            for (int i = armedList.Count - 1; i >= 0; i--)
            {
                var st = armedList[i];
                if (st.dead) { armedList.RemoveAt(i); continue; }
                bool keep = Time.time <= st.until || (st.hold && piecesLive);
                foreach (var p in st.parts)
                {
                    if (!p.built) continue;
                    if (p.holder && (!p.t || !st.root || !p.t.IsChildOf(st.root.transform))) { LosePart(p); continue; }
                    Switch(p, keep);
                }
                if (keep) continue;
                st.armed = false; st.hold = false; Disarms++; armedList.RemoveAt(i);
            }
        }

        static void LosePart(Part p)
        {
            gone.Clear(); if (p.col) gone.Add(p.col);
            if (p.on) { p.on = false; CollidersOn--; }
            if (p.holder) { p.holder.SetActive(false); p.holder.transform.SetParent(null, false); Destroy(p.holder); }
            p.holder = null; p.col = null; p.t = null; PartsLost++;
            Woken += OzRockBody.SupportGone(gone) + OzFortGunBreakup.SupportGone(gone);
        }

        /// <summary>Every 0.5 s: a static the rules removed (no death event seen) is lost; a lost building the rules revived, or a static
        /// whose view was replaced (a bridge's state), is listed again.</summary>
        static void Life(ZGame g)
        {
            foreach (var st in statics)
            {
                if (st.id == 0) continue;
                var o = g.World.Get(st.id);
                if (st.rescanAt > 0f && Time.time >= st.rescanAt)
                {   // a revived or swapped static once more, a moment later: its view has put back its own shape and size by now (a ruin's
                    // low stump grows back to the whole building after the rules revive it, its hidden parts show again)
                    st.rescanAt = 0f;
                    if (!st.dead && !st.ruin && st.root) { DropHolders(st); Scan(st); if (st.any) GridAdd(st); }
                }
                if (st.ruin)
                {   // a building's ruin: intact again when the rules revive it; gone when it is hidden or removed
                    if (o != null && !o.Destroyed && st.root) { DropHolders(st); st.ruin = false; st.dead = false; Scan(st); if (st.any) GridAdd(st); Revived++; st.rescanAt = Time.time + 1.5f; }
                    else if (!st.root || !st.root.activeInHierarchy) { Lost(st); st.ruin = false; }
                    continue;
                }
                bool dead = SimDead(st);
                if (dead && !st.dead) { Lost(st); continue; }
                if (dead && Ruins && st.what == "building" && st.root && st.root.activeInHierarchy && Time.time - st.lostAt > RuinAfterS && (o == null || !o.IsBridge))
                {   // its collapse is over (its own chunks have rested or gone): the ruin it leaves is solid for the pieces of later deaths
                    st.ruin = true; st.dead = false; Scan(st);
                    if (st.any) { GridAdd(st); RuinsMade++; } else { st.ruin = false; st.dead = true; st.lostAt = float.MaxValue; }
                    continue;
                }
                GameObject now = null;
                if (st.view == null && g.StaticViews.TryGetValue(st.id, out var go)) now = go;
                bool swapped = now && now != st.root;
                if ((st.dead && !dead) || swapped)
                {
                    DropHolders(st);
                    if (swapped) st.root = now;
                    st.dead = false; Scan(st); if (st.any) GridAdd(st); Revived++; st.rescanAt = Time.time + 1.5f;
                }
            }
        }

        /// <summary>The static is gone (died, or removed): its colliders go at once; what rested on it or leaned against it wakes.</summary>
        static void Lost(St st)
        {
            st.dead = true; st.armed = false; st.hold = false; st.lostAt = Time.time; GoneN++;
            armedList.Remove(st);
            gone.Clear();
            foreach (var p in st.parts) if (p.col) gone.Add(p.col);
            if (gone.Count > 0) Woken += OzRockBody.SupportGone(gone) + OzFortGunBreakup.SupportGone(gone);
            DropHolders(st);
        }

        // ------------------------------------------------------------------ the drawn statics against a scripted landing (no collider needed)
        /// <summary>What a flat disc (middle `c`, radius `r`, its lowest point at `c.y`) touches: a living static's drawn part box (+ `margin`),
        /// or null (as OzRockSolid.DiscRock for the rocks).</summary>
        public static string Disc(Vector3 c, float r, float margin)
        {
            if (!On) return null;
            EnsureListed();
            float rr = r + margin;
            var l = Near(c, rr + 0.5f);
            for (int i = 0; i < l.Count; i++)
            {
                var st = l[i]; if (st.dead || SimDead(st)) continue;
                foreach (var p in st.parts)
                {
                    if (!p.Shown) continue;
                    var b = p.box;
                    if (c.y - rr > b.max.y - 0.05f) continue;                                               // lower than the disc can reach
                    if (FlatGap(c, b) < rr) { DiscHits++; return Name(st); }
                }
            }
            return null;
        }

        /// <summary>What a ball of radius `r` at `at` touches: a living static's drawn part box (+ `margin`), or null.</summary>
        public static string Ball(Vector3 at, float r, float margin)
        {
            if (!On) return null;
            EnsureListed();
            float rr = r + margin;
            var l = Near(at, rr + 0.5f);
            for (int i = 0; i < l.Count; i++)
            {
                var st = l[i]; if (st.dead || SimDead(st)) continue;
                foreach (var p in st.parts)
                {
                    if (!p.Shown) continue;
                    var b = p.box;
                    if (at.y - r > b.max.y - 0.05f || at.y + r < b.min.y) continue;
                    if (FlatGap(at, b) < rr) { BallHits++; return Name(st); }
                }
            }
            return null;
        }

        /// <summary>A living static's drawn box lies within `radius` (flat) of `at`.</summary>
        public static bool Within(Vector3 at, float radius)
        {
            if (!On) return false;
            EnsureListed();
            var l = Near(at, radius);
            for (int i = 0; i < l.Count; i++) if (!l[i].dead && !SimDead(l[i]) && l[i].parts.Count > 0) return true;
            return false;
        }

        /// <summary>OzRockSolid.Blocked (the fort cannon's landing): the landed cannon's box (+ `margin`) overlaps a living static's drawn
        /// part box below its top.</summary>
        public static string BoxHit(Bounds box, float margin)
        {
            if (!On) return null;
            EnsureListed();
            var l = Near(box.center, Mathf.Max(box.extents.x, box.extents.z) + margin + 0.5f);
            for (int i = 0; i < l.Count; i++)
            {
                var st = l[i]; if (st.dead || SimDead(st)) continue;
                foreach (var p in st.parts)
                {
                    if (!p.Shown) continue;
                    var b = p.box;
                    if (box.max.x + margin > b.min.x && box.min.x - margin < b.max.x && box.max.z + margin > b.min.z && box.min.z - margin < b.max.z && box.min.y < b.max.y)
                    { BoxHits++; return Name(st); }
                }
            }
            return null;
        }

        static string Name(St st) => st.what + " " + st.type + (st.id != 0 ? " #" + st.id : "");

        // ------------------------------------------------------------------ checks
        /// <summary>Play check: a collider of a static (made here).</summary>
        public static bool IsStatic(Collider c) => c && c.gameObject.name == "OZ_StaticSolid" && OzRockSolid.IsRock(c);

        /// <summary>Play check: the point lies inside the drawn shape of a switched-on static collider (rays from above and four sides all
        /// meet that collider before the point), and which static.</summary>
        public static bool InsideStatic(Vector3 p, out string which)
        {
            which = "-";
            if (OzRockSolid.Layer < 0) return false;
            int n = Physics.OverlapSphereNonAlloc(p, 6f, hits, OzRockSolid.Mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = hits[i]; if (!IsStatic(c)) continue;
                var b = c.bounds; if (!b.Contains(p)) continue;
                bool all = true;
                for (int k = 0; k < 5 && all; k++)
                {
                    var dir = k == 0 ? Vector3.down : k == 1 ? Vector3.right : k == 2 ? Vector3.left : k == 3 ? Vector3.forward : Vector3.back;
                    all = c.Raycast(new Ray(p - dir * 12f, dir), out var hit, 12f) && hit.distance < 12f - 0.001f;
                }
                if (all) { which = StaticOf(c); return true; }
            }
            return false;
        }
        static readonly Collider[] hits = new Collider[64];

        /// <summary>The static a collider made here belongs to.</summary>
        public static string StaticOf(Collider c)
        {
            if (!c) return "-";
            foreach (var st in statics) foreach (var p in st.parts) if (p.col == c) return Name(st);
            return c.transform.parent ? c.transform.parent.name : c.name;
        }

        /// <summary>Play check: the living static (`guns`: guns only, else buildings and huts) whose drawn box lies nearest `c`.</summary>
        public static bool Nearest(Vector3 c, bool guns, out Bounds box, out int simId, out string what, int skipId = 0)
        {
            EnsureListed(); box = default; simId = 0; what = "-"; float bd = float.MaxValue;
            foreach (var st in statics)
            {
                if (st.dead || st.ruin || !st.any || SimDead(st) || st.id == skipId) continue;
                if (guns != (st.what == "gun")) continue;
                if (st.what == "ruin" || st.what == "pyramid") continue;
                float d = FlatGap(c, st.box);
                if (d < bd) { bd = d; box = st.box; simId = st.id; what = Name(st); }
            }
            return bd < float.MaxValue;
        }

        /// <summary>Play check: the statics' parts made so far / listed.</summary>
        public static string PrebuildState => MadeState();                                              // the living statics' parts (OzStaticSolid.Made.cs)

        /// <summary>One line for the Play check and the log.</summary>
        public static string State => $"static solid {(Enabled ? "on" : "OFF")} (rock layer {OzRockSolid.Layer}): statics {Statics} (guns {Guns}, buildings {Buildings}, huts {Huts}, ruins / pyramids {Decor}), " +
            $"parts {Parts}, meshes baked {BakeDone} of {BakeMeshes} ({BakeMsTotal:0} ms on a background thread), colliders made {PrebuildState} (exact shapes {MeshParts}, boxes {BoxParts}; made when asked {BuiltLate}; slowest {MaxBuildMs:0.00} ms), " +
            $"switched on {Armed} (colliders on now {CollidersOn}), switched off {Disarms}, statics gone {GoneN}, ruins made solid {RuinsMade}, listed again {Revived}, parts taken away {PartsLost}, bodies woken {Woken}, " +
            $"scripted landings that met a static: discs {DiscHits}, flights {BallHits}, the fort cannon's box {BoxHits}";
    }
}
