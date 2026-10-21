using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, item 331: the rocks are solid for the fort's big cannon when it is thrown off a falling fort, for its break-up pieces
    /// (OzFortGunBreakup) and for the falling fort's heavy concrete chunks. A rock is a drawn pillar (RockPillar) without a collider, so
    /// the thrown cannon could land in a rock and its pieces lay half inside it. Now:
    /// <list type="bullet">
    /// <item>the toss lands the cannon on free ground (<see cref="FreeLanding"/>, from FortGunView.StartToss): a landing place where the
    /// landed cannon's box (+ <see cref="Margin"/>) would touch a rock (its drawn box), or where the last part of the flight (from
    /// <see cref="ArcFrom"/> of it) would pass through a rock, moves to the nearest free place round the fort: the heading in 10 deg steps
    /// (up to +-120 deg) and 1.5 m steps farther out (up to 6 m), the cheapest first; the place it moves to also clears every building's
    /// and gun's map footprint. A landing that touches no rock stays exactly where it was drawn (as before), and every other number of the
    /// toss stays as it was (no number of the fort's own sequence is drawn more);</item>
    /// <item>around the landing (<see cref="ArmRadius"/>) and around a falling fort (<see cref="FortArmRadius"/>) every rock's standing
    /// part (the pillar, or its stump) gets a collider of exactly its drawn shape (a static triangle mesh: the RockZ meshes from the stored
    /// shapes in OzRockSolid.Meshes.cs because their meshes cannot be read at run time, readable rock meshes such as level 16's from their
    /// own triangles, anything else its box), built one rock part per frame while the cannon flies. The colliders lie on a layer that the
    /// layer table keeps out of every contact; only colliders that include that layer meet them (the cannon's pieces, the fallen fort's
    /// heavy concrete chunks), so ragdolls, rock chunks, wreck parts, sandbags and every other body keep their contacts exactly as they
    /// were. They are switched on only while those pieces need them (no ray of any other system meets a rock elsewhere or later);</item>
    /// <item>a rock that the rules remove loses its collider at once; when a rock's standing part goes (the stump replaces the pillar, or
    /// the rock is removed), a piece or chunk resting on it is woken and falls.</item>
    /// </list>
    /// The colliders stay with their rock part (children of it): hidden with it, shown with the stump. View only, no rule changes.
    /// Switch: <see cref="Enabled"/> (off = the landing place and every contact as before).
    /// </summary>
    [DefaultExecutionOrder(-40)]                                                                   // after ZGame's tick (-50): reads the frame's sim events before CombatView (50) clears them
    public sealed partial class OzRockSolid : MonoBehaviour
    {
        public static bool Enabled = true;
        /// <summary>Clear air (m) between the landed cannon's box and a rock's drawn box (and, for a moved place, a building's or a gun's map
        /// footprint).</summary>
        public static float Margin = 0.5f;
        /// <summary>The last part of the toss flight that must miss the rocks (share of the flight) and its clear air (m).</summary>
        public static float ArcFrom = 0.8f, ArcMargin = 0.4f;
        const int ArcSamples = 6;
        /// <summary>Rocks whose middle lies within this distance (m) of the cannon's landing place are solid while its pieces exist (the
        /// farthest rolling piece rests under 18 m from the landing place; a rock's drawn side lies about 1.3 m from its middle).</summary>
        public static float ArmRadius = 20f;
        /// <summary>Rocks within this distance (m) of a falling fort's middle are solid for this long (s) for its heavy concrete chunks.</summary>
        public static float FortArmRadius = 26f, FortArmSeconds = 25f;
        /// <summary>The cannon's landing place: rocks are switched on for at least this long (s) from the toss (its flight and landing).</summary>
        public static float TossArmSeconds = 8f;
        /// <summary>A real mesh's bounds must match a stored shape's bounds within this (m), else the mesh's box is used.</summary>
        public const float BoundsTolerance = 0.05f;
        /// <summary>Off (the default): only colliders that include the rock layer meet the rocks (the cannon's pieces, a fallen fort's heavy
        /// concrete chunks). On: the layer table lets every heavy debris body (layer DebrisHeavy) meet them while they are switched on.</summary>
        public static bool AllHeavyDebris = false;

        // ------------------------------------------------------------------ statistics (Play check)
        public static int Landings, LandingsMoved, LandingsKept, LandingsOnWater, RocksListed, PartsBuilt, MeshColliders, StoredShapes, OwnMeshes, Boxes, BadShapes,
                          ArmedRocks, Disarms, RocksGone, PartSwaps, Woken, FortFalls, ChunksIncluded, CollidersOn;
        public static float LastBuildMs, MaxBuildMs;
        public static string LastLanding = "-";
        public static Vector3 LastLandingFrom, LastLandingTo;

        // ------------------------------------------------------------------ the layer
        static int layer = -2;
        /// <summary>The rocks' collider layer: the first unnamed one of 3, 6 and 7 (the project's own searches for a free layer look at 8..31);
        /// -1 = none free (the rocks then stay as they were).</summary>
        public static int Layer { get { if (layer == -2) layer = PickLayer(); return layer; } }
        public static int Mask => Layer >= 0 ? 1 << Layer : 0;
        static int PickLayer()
        {
            if (string.IsNullOrEmpty(LayerMask.LayerToName(3))) return 3;
            if (string.IsNullOrEmpty(LayerMask.LayerToName(6))) return 6;
            if (string.IsNullOrEmpty(LayerMask.LayerToName(7))) return 7;
            return -1;
        }
        static int HeavyLayer { get { int l = LayerMask.NameToLayer("DebrisHeavy"); return l >= 0 ? l : OzFortGunBreakup.HeavyLayerFallback; } }
        static bool tableSet;
        static void LayerTable()
        {
            if (tableSet || Layer < 0) return;
            tableSet = true;
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(Layer, i, true);                    // nothing meets the rocks by the table ...
            if (AllHeavyDebris) Physics.IgnoreLayerCollision(Layer, HeavyLayer, false);
        }

        /// <summary>A collider of a rock (made here).</summary>
        public static bool IsRock(Collider c) => c && Layer >= 0 && c.gameObject.layer == Layer;
        /// <summary>... but a collider that includes the rock layer: the cannon's pieces and the fallen fort's heavy chunks. Its layer override
        /// priority is raised above the rock collider's (0), so its "include" decides the pair although the layer table of the rock's side
        /// says no, whatever rule the engine has for two equal priorities (no other collider of the game sets layer overrides, so no other
        /// pair changes).</summary>
        public static void Include(Collider c)
        {
            if (!c || !Enabled || Layer < 0) return;
            LayerTable();
            c.includeLayers = c.includeLayers.value | Mask;
            if (c.layerOverridePriority < 1) c.layerOverridePriority = 1;
        }
        public static bool Includes(Collider c) => c && Layer >= 0 && (c.includeLayers.value & Mask) != 0;

        static PhysicsMaterial stone;
        static PhysicsMaterial Stone() => stone ? stone : (stone = new PhysicsMaterial("OZ_RockStone") { bounciness = 0.05f, dynamicFriction = 0.75f, staticFriction = 0.9f });

        // ------------------------------------------------------------------ the rocks of the level
        sealed class Part
        {
            public GameObject go; public Renderer r; public Mesh mesh; public Bounds box;
            public readonly List<Collider> cols = new List<Collider>(2);
            public bool built, on, wasShown;
            public bool Shown => go && go.activeInHierarchy;
        }
        sealed class Rock
        {
            public RockPillar rp; public Part pillar, damaged; public Vector3 centre;
            public bool gone, armed, listed; public float until;
            public Part Standing => pillar != null && pillar.Shown ? pillar : damaged != null && damaged.Shown ? damaged : null;
        }
        static readonly List<Rock> rocks = new List<Rock>();
        static readonly List<Rock> watched = new List<Rock>();                                       // ever switched on: polled for their state
        static readonly List<Part> queue = new List<Part>();
        static readonly List<Vector3> queueAt = new List<Vector3>();
        static int listedScene = -1; static float listedAt = -10f;

        static Part MakePart(GameObject go)
        {
            if (!go) return null;
            var mf = go.GetComponent<MeshFilter>(); var r = go.GetComponent<MeshRenderer>();
            var p = new Part { go = go, r = r, mesh = mf ? mf.sharedMesh : null, wasShown = go.activeInHierarchy };
            p.box = p.mesh ? WorldBox(go.transform, p.mesh.bounds) : new Bounds(go.transform.position, Vector3.zero);   // also for a hidden part (its renderer has no bounds then)
            return p;
        }

        static Bounds WorldBox(Transform t, Bounds lb)
        {
            var m = t.localToWorldMatrix; var b = new Bounds(m.MultiplyPoint3x4(lb.min), Vector3.zero);
            for (int i = 1; i < 8; i++) b.Encapsulate(m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? lb.min.x : lb.max.x, (i & 2) == 0 ? lb.min.y : lb.max.y, (i & 4) == 0 ? lb.min.z : lb.max.z)));
            return b;
        }

        static void EnsureRocks()
        {
            int h = SceneManager.GetActiveScene().handle;
            if (h == listedScene && (rocks.Count > 0 || Time.unscaledTime - listedAt < 5f)) return;
            if (h != listedScene) { rocks.Clear(); watched.Clear(); queue.Clear(); queueAt.Clear(); }
            listedScene = h; listedAt = Time.unscaledTime;
            if (rocks.Count > 0) return;
            foreach (var rp in FindObjectsByType<RockPillar>(FindObjectsSortMode.InstanceID))
            {
                if (!rp) continue;
                var r = new Rock { rp = rp, pillar = MakePart(rp.pillar), damaged = MakePart(rp.damaged), centre = rp.transform.position };
                rocks.Add(r);
            }
            RocksListed = rocks.Count;
        }

        /// <summary>The rules removed the rock (its sim object is gone or destroyed); a rock not yet bound to the rules counts as standing.</summary>
        static bool Gone(Rock r)
        {
            if (r.gone) return true;
            if (!r.rp) return r.gone = true;
            var g = ZGame.I;
            if (g == null || g.World == null || r.rp.simId < 0) return false;
            var o = g.World.Get(r.rp.simId);
            return r.gone = o == null || o.Destroyed;
        }

        // ------------------------------------------------------------------ the landing place (FortGunView.StartToss)
        /// <summary>FortGunView.StartToss, after it measured the landed cannon (`b`, with its pivot at (land.x, gy, land.z)): moves the landing
        /// place to free ground when the drawn one touches a rock or the flight's end passes through a rock (the place it moves to also clears
        /// every building's and gun's footprint; a drawn place that touches no rock is never moved); then switches the rocks round the landing
        /// on for its pieces. `c` = the fort's middle, `half` = half its long side (m), `hdg` = the drawn landing heading (deg), `from` /
        /// `apex` = the toss arc. True = moved (land, gy and b changed; the caller's tossTo follows).</summary>
        public static bool FreeLanding(ZObject fort, Vector3 c, float half, float hdg, Vector3 from, float apex, ref Vector3 land, ref float gy, ref Bounds b)
        {
            var g = ZGame.I;
            if (!Enabled || g == null || g.World == null || fort == null) return false;
            Landings++; Boot(); EnsureRocks();
            var pivot = new Vector3(land.x, gy, land.z);
            Vector3 rel = b.center - pivot, ext = b.extents;
            LastLandingFrom = pivot;
            string why = Blocked(g.World, b, rel, ext, from, apex, false, true);
            if (why == null) { LastLanding = "free where it was drawn"; LastLandingTo = pivot; Arm(pivot); return false; }
            if (OzWaterLanding.OpenWaterAt(pivot, out _)) { LandingsOnWater++; LastLanding = "over open water, kept (" + why + ")"; LastLandingTo = pivot; return false; }
            float dist0 = new Vector2(land.x - c.x, land.z - c.z).magnitude;
            bool landClear = why.StartsWith(ArcWhy);                                             // only its flight's end passes a rock
            for (int pass = 0; pass < 2; pass++)
            {   // pass 0: landing and the flight's end clear; pass 1 (none found): the landing clear (a drawn landing that is clear stays)
                if (pass == 1 && landClear) break;
                for (int k = 1; k <= 16; k++)
                    for (int i = 0; i <= 4 && i <= k; i++)
                    {
                        int j = k - i; if (j > 12) continue;
                        for (int s = 0; s < (j == 0 ? 1 : 2); s++)
                        {
                            float dh = (s == 0 ? 10f : -10f) * j, dd = 1.5f * i;
                            var q = c + Quaternion.Euler(0f, hdg + dh, 0f) * Vector3.forward * (dist0 + dd);
                            float y = g.GroundY(q.x, q.z); var pv = new Vector3(q.x, y, q.z);
                            if (OzWaterLanding.OpenWaterAt(pv, out _)) continue;                       // never moved into open water
                            var bb = new Bounds(pv + rel, ext * 2f);
                            if (Blocked(g.World, bb, rel, ext, from, apex, true, pass == 0) != null) continue;
                            land = new Vector3(q.x, land.y, q.z); gy = y; b = bb;
                            LandingsMoved++; LastLandingTo = pv;
                            LastLanding = $"moved {(dh >= 0f ? "+" : "")}{dh:0} deg, {dd:0.0} m farther out{(pass == 1 ? " (its flight's end still passes a rock)" : "")}; drawn place: {why}";
                            Debug.Log($"[OpenZ] fort {fort.Type} #{fort.Id} cannon lands on free ground: {LastLanding} ({pivot.x:0.0}, {pivot.z:0.0} -> {pv.x:0.0}, {pv.z:0.0})");
                            Arm(pv);
                            return true;
                        }
                    }
            }
            LandingsKept++; LastLanding = "no free place round the fort, kept (" + why + ")"; LastLandingTo = pivot;
            Debug.Log($"[OpenZ] fort {fort.Type} #{fort.Id} cannon: {LastLanding}");
            Arm(pivot);
            return false;
        }

        const string ArcWhy = "its flight's end passes rock #";

        static bool Overlap(Bounds a, Bounds o, float m) => a.max.x + m > o.min.x && a.min.x - m < o.max.x && a.max.z + m > o.min.z && a.min.z - m < o.max.z;

        /// <summary>What the landed cannon's box would touch (null = nothing): a rock's drawn box; for a `moved` place also a building's or a
        /// gun's map footprint (the fort itself included); with `arc` a rock its flight's end passes. A drawn place is tested for rocks only,
        /// so a landing that touches no rock is never moved (away from rocks every toss lands where it always did).</summary>
        static string Blocked(ZWorld w, Bounds box, Vector3 rel, Vector3 ext, Vector3 from, float apex, bool moved, bool arc)
        {
            for (int i = 0; i < rocks.Count; i++)
            {
                var r = rocks[i]; if (Gone(r)) continue;
                var p = r.Standing; if (p == null) continue;
                if (Overlap(box, p.box, Margin) && box.min.y < p.box.max.y) return "rock #" + r.rp.simId;
            }
            { var sw = OzStaticSolid.BoxHit(box, Margin); if (sw != null) return sw; }                            // items 331 + 332: nor a gun's or a building's drawn shape
            if (moved)
            {   // the place it moves to must not be inside a building or a gun either
                var objs = w.Objects;
                for (int i = 0; i < objs.Count; i++)
                {
                    var o = objs[i];
                    if (o == null || (o.Kind != ZKind.Building && o.Kind != ZKind.Cannon) || o.IsBridge) continue;
                    float x0 = o.LocX / 8f, x1 = (o.LocX + o.WidthPix) / 8f, z0 = -(o.LocY + o.HeightPix) / 8f, z1 = -o.LocY / 8f;
                    if (box.max.x + Margin > x0 && box.min.x - Margin < x1 && box.max.z + Margin > z0 && box.min.z - Margin < z1) return o.Type + " #" + o.Id;
                }
            }
            if (!arc) return null;
            var pivot = box.center - rel;
            var to = new Vector3(pivot.x, pivot.y + (pivot.y - box.min.y) - 0.2f, pivot.z);              // FortGunView: its lowest point 0.2 m into the ground
            for (int s = 0; s < ArcSamples; s++)
            {
                float k = Mathf.Lerp(ArcFrom, 0.97f, s / (ArcSamples - 1f));
                var at = Vector3.Lerp(from, to, k); at.y += 4f * apex * k * (1f - k);
                var sb = new Bounds(at + rel, ext * 2f);
                for (int i = 0; i < rocks.Count; i++)
                {
                    var r = rocks[i]; if (Gone(r)) continue;
                    var p = r.Standing; if (p == null) continue;
                    if (Overlap(sb, p.box, ArcMargin) && sb.min.y < p.box.max.y - 0.05f) return ArcWhy + r.rp.simId;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ switching the rocks on
        static void Arm(Vector3 at)
        {
            if (!OzFortGunBreakup.Enabled) return;
            ArmNear(at, ArmRadius, TossArmSeconds);
        }

        /// <summary>The standing parts of the rocks within `radius` of `at` become solid now (each one's collider is made over the next
        /// frames, nearest first, or at once by <see cref="BuildNear"/>) for `seconds`, and as long as a cannon's pieces exist.</summary>
        public static void ArmNear(Vector3 at, float radius, float seconds)
        {
            if (!Enabled || Layer < 0) return;
            Boot(); LayerTable(); EnsureRocks();
            OzStaticSolid.ArmAround(at, radius, seconds, 0f, true);                                                // items 331 + 332: the guns and buildings round it too, while the cannon's pieces live
            float until = Time.time + seconds, r2 = radius * radius;
            for (int i = 0; i < rocks.Count; i++)
            {
                var r = rocks[i];
                float dx = r.centre.x - at.x, dz = r.centre.z - at.z;
                if (dx * dx + dz * dz > r2 || Gone(r)) continue;
                r.until = Mathf.Max(r.until, until);
                if (!r.armed) { r.armed = true; ArmedRocks++; }
                if (!r.listed) { r.listed = true; watched.Add(r); }
                var p = r.Standing;
                if (p == null) continue;
                if (p.built) On(p, true);
                else if (!queue.Contains(p)) { queue.Add(p); queueAt.Add(at); }
            }
        }

        /// <summary>Every queued rock part within `radius` of `at` is made now (the landing frame).</summary>
        public static void BuildNear(Vector3 at, float radius)
        {
            float r2 = radius * radius;
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                var p = queue[i];
                var d = p.box.center - at; d.y = 0f;
                if (d.sqrMagnitude > r2) continue;
                queue.RemoveAt(i); queueAt.RemoveAt(i);
                BuildAndSwitch(p);
            }
        }

        static void Dequeue(Part p)
        {
            for (int i = queue.Count - 1; i >= 0; i--) if (queue[i] == p) { queue.RemoveAt(i); queueAt.RemoveAt(i); }
        }

        static void BuildAndSwitch(Part p)
        {
            Build(p);
            var r = RockOf(p);
            if (r != null && r.armed && !Gone(r) && p.Shown) On(p, true);
        }

        static Rock RockOf(Part p)
        {
            for (int i = 0; i < watched.Count; i++) if (watched[i].pillar == p || watched[i].damaged == p) return watched[i];
            return null;
        }

        static void On(Part p, bool on)
        {
            if (p == null || p.on == on) return;
            p.on = on;
            for (int i = 0; i < p.cols.Count; i++) if (p.cols[i]) p.cols[i].enabled = on;
            CollidersOn += on ? p.cols.Count : -p.cols.Count;
        }

        // ------------------------------------------------------------------ a rock part's collider (its drawn shape)
        static readonly Dictionary<string, Mesh> shapeMeshes = new Dictionary<string, Mesh>();

        /// <summary>The stored shape of a RockZ mesh as a mesh the physics can read, or null (no shape stored, or the real mesh's bounds differ).</summary>
        static Mesh Shape(Mesh src)
        {
            if (!src) return null;
            if (shapeMeshes.TryGetValue(src.name, out var have)) return have;
            Mesh m = null;
            if (ShapeDefs().TryGetValue(src.name, out var d))
            {
                var b = src.bounds;
                if (Vector3.Distance(b.min, d.min) <= BoundsTolerance && Vector3.Distance(b.max, d.max) <= BoundsTolerance)
                {
                    var v = new Vector3[d.v.Length / 3];
                    for (int i = 0; i < v.Length; i++) v[i] = new Vector3(d.v[3 * i], d.v[3 * i + 1], d.v[3 * i + 2]);
                    m = new Mesh { name = src.name + "_solid" };
                    m.SetVertices(v); m.SetTriangles(d.t, 0); m.RecalculateBounds();
                }
                else
                {
                    BadShapes++;
                    Debug.LogWarning($"[OpenZ] rock solid: the stored shape of {src.name} does not match the mesh (bounds {b.min} .. {b.max}, stored {d.min} .. {d.max}): its box is used");
                }
            }
            shapeMeshes[src.name] = m;
            return m;
        }

        static void Build(Part p)
        {
            if (p == null || p.built) return;
            p.built = true;
            if (!p.go || !p.mesh) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var holder = new GameObject("OZ_RockSolid") { layer = Layer };
            holder.transform.SetParent(p.go.transform, false);
            Collider col;
            var shape = Shape(p.mesh);
            if (shape) { var mc = holder.AddComponent<MeshCollider>(); mc.convex = false; mc.sharedMesh = shape; col = mc; StoredShapes++; MeshColliders++; }
            else if (p.mesh.isReadable) { var mc = holder.AddComponent<MeshCollider>(); mc.convex = false; mc.sharedMesh = p.mesh; col = mc; OwnMeshes++; MeshColliders++; }
            else { var bc = holder.AddComponent<BoxCollider>(); bc.center = p.mesh.bounds.center; bc.size = p.mesh.bounds.size; col = bc; Boxes++; }
            col.sharedMaterial = Stone();
            col.enabled = false;
            p.cols.Add(col);
            PartsBuilt++;
            LastBuildMs = (float)sw.Elapsed.TotalMilliseconds; if (LastBuildMs > MaxBuildMs) MaxBuildMs = LastBuildMs;
        }

        // ------------------------------------------------------------------ the manager: builds, the rocks' state, the fort's fall
        static OzRockSolid inst;
        float nextPoll;
        readonly List<int> fortsPending = new List<int>(); readonly List<float> fortsUntil = new List<float>(); float nextChunkScan;
        static readonly Collider[] buf = new Collider[64];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (inst) return;
            var go = new GameObject("OZ_RockSolid"); DontDestroyOnLoad(go);
            inst = go.AddComponent<OzRockSolid>();
            LayerTable();
        }

        void Update()
        {
            if (!Enabled || Layer < 0) return;
            var g = ZGame.I;
            if (g != null && g.World != null)
            {
                var ev = g.World.Events;
                for (int i = 0; i < ev.Count; i++)
                {
                    var e = ev[i];
                    if (e.type != ZEventType.Death) continue;
                    var o = g.World.GetAny(e.objId);
                    if (o == null) continue;
                    if (o.IsFort) FortFell(g, o);
                    else if (o.Type == "rock") RockDied(o.Id);
                }
            }
            if (queue.Count > 0)
            {   // one rock part per frame, the nearest to its landing first
                int best = 0; float bd = float.MaxValue;
                for (int i = 0; i < queue.Count; i++) { var d = queue[i].box.center - queueAt[i]; d.y = 0f; if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = i; } }
                var p = queue[best]; queue.RemoveAt(best); queueAt.RemoveAt(best);
                BuildAndSwitch(p);
            }
            if (fortsPending.Count > 0 && Time.time >= nextChunkScan) ScanChunks(g);
            if (Time.time < nextPoll || watched.Count == 0) return;
            nextPoll = Time.time + 0.1f;
            bool piecesLive = OzFortGunBreakup.LiveCount > 0;
            for (int i = 0; i < watched.Count; i++)
            {
                var r = watched[i];
                if (r.gone) continue;
                if (Gone(r)) { Lost(r, true); continue; }
                bool pillarGone = r.pillar != null && r.pillar.wasShown && !r.pillar.Shown;
                bool stumpGone = r.damaged != null && r.damaged.wasShown && !r.damaged.Shown;
                if (r.pillar != null) r.pillar.wasShown = r.pillar.Shown;
                if (r.damaged != null) r.damaged.wasShown = r.damaged.Shown;
                if (pillarGone || stumpGone)
                {   // its standing part changed (the stump replaced the pillar): what rested on the old one falls, onto the new one
                    PartSwaps++;
                    var now = r.Standing;
                    if (now != null)
                    {
                        r.until = Mathf.Max(r.until, Time.time + 6f);
                        if (!r.armed) { r.armed = true; ArmedRocks++; }
                        Dequeue(now); BuildAndSwitch(now);
                    }
                    if (pillarGone) { SupportGone(r.pillar); On(r.pillar, false); }
                    if (stumpGone) { SupportGone(r.damaged); On(r.damaged, false); }
                }
                if (r.armed && Time.time > r.until && !piecesLive)
                {
                    r.armed = false; Disarms++;
                    On(r.pillar, false); On(r.damaged, false);
                }
            }
        }

        static void RockDied(int id)
        {
            for (int i = 0; i < watched.Count; i++) if (watched[i].rp && watched[i].rp.simId == id && !watched[i].gone) { watched[i].gone = true; Lost(watched[i], true); return; }
        }

        /// <summary>The rules removed the rock: its colliders off for good; what rested on it falls.</summary>
        static void Lost(Rock r, bool count)
        {
            r.gone = true; r.armed = false;
            if (count) RocksGone++;
            SupportGone(r.pillar); SupportGone(r.damaged);
            On(r.pillar, false); On(r.damaged, false);
            Dequeue(r.pillar); Dequeue(r.damaged);
        }

        /// <summary>A rock part goes: the cannon's pieces resting on or leaning against it are woken, and the heavy chunks lying on it
        /// (kinematic, including the rock layer) fall again.</summary>
        static void SupportGone(Part p)
        {
            if (p == null || !p.built) return;
            Woken += OzFortGunBreakup.SupportGone(p.cols); Woken += OzRockBody.SupportGone(p.cols);           // items 331 + 332: every big body too
            var b = p.box; var c = b.center + Vector3.up * 0.5f; var e = b.extents + new Vector3(0.3f, 0.6f, 0.3f);
            int n = Physics.OverlapBoxNonAlloc(c, e, buf, Quaternion.identity, 1 << HeavyLayer, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = buf[i]; if (!col || !Includes(col)) continue;
                var rb = col.attachedRigidbody;
                if (!rb || !rb.isKinematic || rb.GetComponent<OzFortGunBreakup>()) continue;
                rb.isKinematic = false; rb.WakeUp(); Woken++;
            }
        }

        // ------------------------------------------------------------------ a fort falls: its heavy concrete chunks meet the rocks
        void FortFell(ZGame g, ZObject o)
        {
            FortFalls++;
            var c = g.CenterWorld(o);
            ArmNear(c, FortArmRadius, FortArmSeconds);
            if (!fortsPending.Contains(o.Id)) { fortsPending.Add(o.Id); fortsUntil.Add(Time.time + 2f); }
            nextChunkScan = 0f;
        }

        /// <summary>For 2 s after a fort's fall (its chunks are made and thrown by the collapse): its heavy chunks include the rock layer.</summary>
        void ScanChunks(ZGame g)
        {
            nextChunkScan = Time.time + 0.25f;
            if (g == null) { fortsPending.Clear(); fortsUntil.Clear(); return; }
            BuildingBreaker.BigPart[] parts = null;
            int heavy = HeavyLayer;
            for (int i = fortsPending.Count - 1; i >= 0; i--)
            {
                if (g.StaticViews.TryGetValue(fortsPending[i], out var fgo) && fgo && fgo.GetComponent<BuildingBreaker>() is BuildingBreaker br)
                {
                    parts ??= FindObjectsByType<BuildingBreaker.BigPart>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    foreach (var bp in parts)
                    {
                        if (!bp || bp.owner != br || !bp.rb || bp.gameObject.layer != heavy) continue;
                        foreach (var col in bp.GetComponents<Collider>()) if (col && !Includes(col)) { Include(col); ChunksIncluded++; }
                    }
                }
                if (Time.time >= fortsUntil[i]) { fortsPending.RemoveAt(i); fortsUntil.RemoveAt(i); }
            }
        }

        /// <summary>How deep (m) the collider `a` lies in the switched-on rock colliders near it (0 = clear), and which rock.</summary>
        public static float DepthInRocks(Collider a, out string which)
        {
            which = "-";
            if (!a || !a.enabled || Layer < 0) return 0f;
            var b = a.bounds;
            int n = Physics.OverlapBoxNonAlloc(b.center, b.extents + Vector3.one * 0.05f, buf, Quaternion.identity, Mask, QueryTriggerInteraction.Ignore);
            float deep = 0f;
            for (int i = 0; i < n; i++)
            {
                var rc = buf[i]; if (!IsRock(rc)) continue;
                if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, rc, rc.transform.position, rc.transform.rotation, out _, out float d) && d > deep)
                { deep = d; which = rc.transform.parent && rc.transform.parent.parent ? rc.transform.parent.parent.name : rc.name; }
            }
            return deep;
        }

        /// <summary>Play check: the point lies inside the drawn shape of a switched-on rock collider (rays from above and from four sides all
        /// meet that rock before the point; a triangle-mesh collider is a surface, so a body wholly inside it touches nothing).</summary>
        public static bool InsideRock(Vector3 p, out string which)
        {
            which = "-";
            if (Layer < 0) return false;
            int n = Physics.OverlapSphereNonAlloc(p, 4f, buf, Mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var rc = buf[i]; if (!IsRock(rc)) continue;
                var b = rc.bounds; if (!b.Contains(p)) continue;
                bool all = true;
                for (int k = 0; k < 5 && all; k++)
                {
                    var dir = k == 0 ? Vector3.down : k == 1 ? Vector3.right : k == 2 ? Vector3.left : k == 3 ? Vector3.forward : Vector3.back;
                    var from = p - dir * 8f;
                    all = rc.Raycast(new Ray(from, dir), out var hit, 8f) && hit.distance < 8f - 0.001f;
                }
                if (all) { which = rc.transform.parent && rc.transform.parent.parent ? rc.transform.parent.parent.name : rc.name; return true; }
            }
            return false;
        }

        /// <summary>One line for the Play check and the log.</summary>
        public static string State => $"rock solid {(Enabled ? "on" : "OFF")}, layer {Layer}{(AllHeavyDebris ? " (all heavy debris)" : "")}: rocks {RocksListed}, landings {Landings} " +
            $"(moved {LandingsMoved}, kept {LandingsKept}, water {LandingsOnWater}; last: {LastLanding}), rocks switched on {ArmedRocks} (colliders on now {CollidersOn}), parts built {PartsBuilt} " +
            $"(stored shapes {StoredShapes}, own meshes {OwnMeshes}, boxes {Boxes}, shapes that did not match {BadShapes}; slowest {MaxBuildMs:0.00} ms), switched off {Disarms}, " +
            $"rocks gone {RocksGone}, stumps / parts changed {PartSwaps}, bodies woken {Woken}, fort falls {FortFalls}, fort chunks meeting the rocks {ChunksIncluded}";
    }
}
