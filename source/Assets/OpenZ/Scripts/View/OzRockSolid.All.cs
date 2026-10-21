using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, items 331 + 332: the rocks are solid for EVERY big falling piece, not only for the fort cannon's (OzRockSolid.cs):
    /// <list type="bullet">
    /// <item>bodies of physics (vehicle wreck pieces and gun bases, the flipped hull, robot bodies, the heavy chunks of a building, the
    /// shuttle's pieces and its crew, the radar dish) meet the rock colliders of OzRockSolid (the drawn shapes, layer <see cref="Layer"/>)
    /// through <see cref="OzRockBody"/>: only bodies whose longest side is at least <see cref="OzRockBody.BigM"/> and only when a rock lies
    /// within their reach; the rocks round a moving body are switched on (<see cref="ArmAround"/>) and off again when it rests;</item>
    /// <item>scripted landings move off the rocks: a tank's or a gun's turret (<see cref="TurretLanding"/>), a robot thrown by a blast when
    /// the ragdolls are used up (<see cref="BodyLanding"/>), a felled level 16 tree (<see cref="TreeAway"/>); a landing that touches no
    /// rock stays exactly where it was drawn;</item>
    /// <item>every rock part is built once per level, one at a time in a small time budget from the level's start
    /// (<see cref="Prebuild"/>), so a break never waits for a build; after that a rock is only switched on and off.</item>
    /// </list>
    /// Small chips (rock bits, sandbags, small concrete chunks, tyres, shards) stay as they are. View only, no rule changes.
    /// Switch: <see cref="AllBigDebris"/> (off = only the fort cannon's pieces, as before).
    /// </summary>
    public sealed partial class OzRockSolid
    {
        /// <summary>Off = only the fort cannon's pieces and the fallen fort's chunks meet the rocks (the turret, body and tree landings as
        /// drawn): everything else as before.</summary>
        public static bool AllBigDebris = true;
        /// <summary>Build every rock part's collider from the level's start, in at most <see cref="BuildBudgetMs"/> a frame (off: built when
        /// a body comes near, the nearest at once).</summary>
        public static bool Prebuild = true;
        public static float BuildBudgetMs = 0.6f, PrebuildAfter = 2f;
        /// <summary>Fallback switch (see the kit's notes): the layer table lets every body of the light debris layer meet the switched-on
        /// rocks (small chips too), for an engine where a collider's include does not decide the pair.</summary>
        public static bool AllLightDebris = false;
        /// <summary>A turret lands at most this far (m) from the place the rules drew, inside the rules' debris blast (5 m radius); the
        /// drawn landing blast (its fireball, dust and scorch) follows the moved turret (<see cref="TurretBlastAt"/>).</summary>
        public static float TurretMaxMove = 4f;
        /// <summary>Only when no free place lies within <see cref="TurretMaxMove"/> (a wide rock block): the farthest a turret lands from the
        /// rules' place (m).</summary>
        public static float TurretMaxMoveFar = 10f;
        /// <summary>A turret's arc may rise to this share of its drawn height (at most <see cref="TurretApexMax"/> m) to clear a rock it would
        /// pass through.</summary>
        public static float TurretApexMore = 2f, TurretApexMax = 12f;
        /// <summary>The ball (radius, m) a flying turret's body is tested as along its arc.</summary>
        public static float TurretArcBallM = 1.5f;
        /// <summary>The scripted blast fall: a body moved off a rock lands at most this far (m) from where it was thrown (RagdollFlight.MaxLandM
        /// is 6 m; nearer first).</summary>
        public static float BodyMaxLandM = 7.5f;
        /// <summary>A felled tree turns at most this much (deg) off its way to fall beside a rock (the least turn that clears the rocks).</summary>
        public static float TreeMaxTurn = 90f;

        /// <summary>Statistics (Play check): turret landings tested / moved / kept in a rock (no free place) / their arcs raised; scripted body
        /// landings tested / moved / kept; tree falls tested / turned / kept; drops that met a rock; rock parts built ahead; arm calls.</summary>
        public static int TurretLandings, TurretMoved, TurretKept, TurretRaised, BodyLandings, BodyMoved, BodyKept, TreeFalls, TreeTurned, TreeKept, DropTouches, Prebuilt, ArmCalls;
        public static string LastTurret = "-", LastBody = "-", LastTree = "-";

        /// <summary>The rocks meet every big body now.</summary>
        public static bool AllOn => Enabled && AllBigDebris && Layer >= 0;

        // ------------------------------------------------------------------ the rocks on a grid (8 m cells, by their middle)
        const float GridCell = 8f;
        static readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
        static int gridScene = -1, gridCount = -1;
        static readonly List<int> nearRocks = new List<int>(64);

        static long GridKey(int i, int j) => ((long)i << 32) ^ (uint)j;

        static void EnsureGrid()
        {
            EnsureRocks();
            if (gridScene == listedScene && gridCount == rocks.Count) return;
            grid.Clear(); gridScene = listedScene; gridCount = rocks.Count; prebuildCursor = 0; prebuildPart = 0;
            for (int i = 0; i < rocks.Count; i++)
            {
                var c = rocks[i].centre; long k = GridKey(Mathf.FloorToInt(c.x / GridCell), Mathf.FloorToInt(c.z / GridCell));
                if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>(4);
                l.Add(i);
            }
        }

        /// <summary>The rocks whose middle lies within `radius` (flat) of `at` (a shared list: read it before the next call).</summary>
        static List<int> Near(Vector3 at, float radius)
        {
            nearRocks.Clear(); EnsureGrid();
            int i0 = Mathf.FloorToInt((at.x - radius) / GridCell), i1 = Mathf.FloorToInt((at.x + radius) / GridCell);
            int j0 = Mathf.FloorToInt((at.z - radius) / GridCell), j1 = Mathf.FloorToInt((at.z + radius) / GridCell);
            float r2 = radius * radius;
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    if (!grid.TryGetValue(GridKey(i, j), out var l)) continue;
                    for (int n = 0; n < l.Count; n++)
                    {
                        var c = rocks[l[n]].centre; float dx = c.x - at.x, dz = c.z - at.z;
                        if (dx * dx + dz * dz <= r2) nearRocks.Add(l[n]);
                    }
                }
            return nearRocks;
        }

        /// <summary>A standing rock (not removed by the rules) has its middle within `radius` of `at`.</summary>
        public static bool RockWithin(Vector3 at, float radius)
        {
            if (!AllOn) return false;
            foreach (int i in Near(at, radius)) { var r = rocks[i]; if (!Gone(r) && r.Standing != null) return true; }
            return OzStaticSolid.Within(at, radius);                                                              // items 331 + 332: a gun or a building near counts too
        }

        // ------------------------------------------------------------------ switching the rocks on round a body
        /// <summary>The standing parts of the rocks whose middle lies within `radius` of `at` are solid for `seconds` (OzRockSolid's own
        /// poll switches them off afterwards); those within `buildNow` that are not built yet are built at once, the others are queued
        /// (nearest first, in a small time budget a frame).</summary>
        public static void ArmAround(Vector3 at, float radius, float seconds, float buildNow)
        {
            if (!AllOn) return;
            Boot(); LayerTable(); BootAll(); ArmCalls++;
            OzStaticSolid.ArmAround(at, radius, seconds, buildNow);                                              // items 331 + 332: the guns and buildings round it too (OzStaticSolid.cs)
            float until = Time.time + seconds, b2 = buildNow * buildNow;
            foreach (int i in Near(at, radius))
            {
                var r = rocks[i];
                if (Gone(r)) continue;
                r.until = Mathf.Max(r.until, until);
                if (!r.armed) { r.armed = true; ArmedRocks++; }
                if (!r.listed) { r.listed = true; watched.Add(r); }
                var p = r.Standing;
                if (p == null) continue;
                if (p.built) { On(p, true); continue; }
                var d = p.box.center - at; d.y = 0f;
                if (d.sqrMagnitude <= b2) { Dequeue(p); BuildAndSwitch(p); }
                else if (!queue.Contains(p)) { queue.Add(p); queueAt.Add(at); }
            }
        }

        /// <summary>OzRockDebris, every frame: queued rock parts are built, nearest first, while the frame's budget lasts; then (with
        /// <see cref="Prebuild"/>, from <see cref="PrebuildAfter"/> s into the level) the parts not built yet, one rock after the other.</summary>
        internal static void BuildStep()
        {
            if (!AllOn) return;
            EnsureGrid();
            bool ahead = Prebuild && Time.timeSinceLevelLoad >= PrebuildAfter && prebuildCursor < rocks.Count;
            if (queue.Count == 0 && !ahead) return;                                                         // nothing to build: no cost
            var sw = buildWatch; sw.Restart();
            while (queue.Count > 0 && sw.Elapsed.TotalMilliseconds < BuildBudgetMs)
            {
                int best = 0; float bd = float.MaxValue;
                for (int i = 0; i < queue.Count; i++) { var d = queue[i].box.center - queueAt[i]; d.y = 0f; if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = i; } }
                var p = queue[best]; queue.RemoveAt(best); queueAt.RemoveAt(best);
                BuildAndSwitch(p);
            }
            if (!ahead) return;
            while (prebuildCursor < rocks.Count && sw.Elapsed.TotalMilliseconds < BuildBudgetMs)
            {
                var r = rocks[prebuildCursor];
                var p = prebuildPart == 0 ? r.pillar : r.damaged;
                if (prebuildPart == 0) prebuildPart = 1; else { prebuildPart = 0; prebuildCursor++; }
                if (p == null || p.built) continue;
                Build(p); Prebuilt++;
            }
        }
        static int prebuildCursor, prebuildPart;
        static readonly System.Diagnostics.Stopwatch buildWatch = new System.Diagnostics.Stopwatch();
        /// <summary>The scripted landings so far (Play check, State menu).</summary>
        public static string LandingState => $"turret landings {TurretLandings}: moved {TurretMoved} (core only {TurretCoreMoved}), arc raised {TurretRaised}, arc only {TurretArcOnly}, kept {TurretKept}; " +
                                             $"body landings {BodyLandings}: moved {BodyMoved} (lying place only {BodyLyingOnly}), kept {BodyKept}; tree falls {TreeFalls}: turned {TreeTurned}, kept {TreeKept}";
        /// <summary>Rock parts built ahead so far / rock parts of the level.</summary>
        public static string PrebuildState { get { int n = 0, b = 0; foreach (var r in rocks) { if (r.pillar != null) { n++; if (r.pillar.built) b++; } if (r.damaged != null) { n++; if (r.damaged.built) b++; } } return b + " of " + n; } }

        // ------------------------------------------------------------------ the manager of the big bodies
        static OzRockDebris allInst;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BootAll()
        {
            if (allInst) return;
            var go = new GameObject("OZ_RockDebris"); DontDestroyOnLoad(go);
            allInst = go.AddComponent<OzRockDebris>();
            if (Layer >= 0)
            {
                LayerTable();
                Physics.IgnoreLayerCollision(Layer, BuildingBreaker.DebrisLayer, !(AllBigDebris && AllLightDebris));
            }
        }

        // ------------------------------------------------------------------ the drawn rocks against a scripted landing (no collider needed)
        /// <summary>What a flat disc (middle `c`, radius `r`, its lowest point at `c.y`) touches: the drawn box of a standing rock part
        /// (+ `margin`), or null.</summary>
        static string DiscRock(Vector3 c, float r, float margin)
        {
            float reach = r + margin + 4f;
            foreach (int i in Near(c, reach))
            {
                var rk = rocks[i]; if (Gone(rk)) continue;
                var p = rk.Standing; if (p == null) continue;
                var b = p.box;
                float rr = r + margin;
                if (c.y - rr > b.max.y - 0.05f) continue;                                                       // a rock lower than the disc can reach down a slope
                float dx = Mathf.Max(0f, Mathf.Max(b.min.x - c.x, c.x - b.max.x)), dz = Mathf.Max(0f, Mathf.Max(b.min.z - c.z, c.z - b.max.z));
                if (dx * dx + dz * dz < rr * rr) return "rock #" + (rk.rp ? rk.rp.simId : -1);
            }
            return OzStaticSolid.Disc(c, r, margin);                                                              // items 331 + 332: nor on a gun or a building
        }

        /// <summary>What a ball of radius `r` flying the arc from → to (apex `apex` over the straight line) touches between `k0` and `k1` of
        /// its flight (`n` samples): a standing rock part's drawn box (+ `margin`), or null.</summary>
        static string ArcRock(Vector3 from, Vector3 to, float apex, float k0, float k1, int n, float r, float margin)
        {
            for (int s = 0; s < n; s++)
            {
                float k = Mathf.Lerp(k0, k1, n <= 1 ? 1f : s / (n - 1f));
                var at = Vector3.Lerp(from, to, k); at.y += 4f * apex * k * (1f - k);
                foreach (int i in Near(at, r + margin + 4f))
                {
                    var rk = rocks[i]; if (Gone(rk)) continue;
                    var p = rk.Standing; if (p == null) continue;
                    var b = p.box;
                    if (at.y - r > b.max.y - 0.05f || at.y + r < b.min.y) continue;
                    float dx = Mathf.Max(0f, Mathf.Max(b.min.x - at.x, at.x - b.max.x)), dz = Mathf.Max(0f, Mathf.Max(b.min.z - at.z, at.z - b.max.z));
                    float rr = r + margin;
                    if (dx * dx + dz * dz < rr * rr) return "its flight passes rock #" + (rk.rp ? rk.rp.simId : -1);
                }
                { var sw = OzStaticSolid.Ball(at, r, margin); if (sw != null) return "its flight passes " + sw; }    // items 331 + 332: nor through a gun or a building
            }
            return null;
        }

        /// <summary>A moved place must not lie in a building's or a gun's map footprint either (+ `margin`), nor in open water.</summary>
        static bool PlaceFree(ZWorld w, Vector3 c, float r, float margin)
        {
            if (OzWaterLanding.OpenWaterAt(c, out _)) return false;
            if (w == null) return true;
            var objs = w.Objects;
            for (int i = 0; i < objs.Count; i++)
            {
                var o = objs[i];
                if (o == null || o.Destroyed || (o.Kind != ZKind.Building && o.Kind != ZKind.Cannon) || o.IsBridge) continue;
                float x0 = o.LocX / 8f, x1 = (o.LocX + o.WidthPix) / 8f, z0 = -(o.LocY + o.HeightPix) / 8f, z1 = -o.LocY / 8f;
                float dx = Mathf.Max(0f, Mathf.Max(x0 - c.x, c.x - x1)), dz = Mathf.Max(0f, Mathf.Max(z0 - c.z, c.z - z1));
                float rr = r + margin;
                if (dx * dx + dz * dz < rr * rr) return false;
            }
            return true;
        }

        static float Ground(Vector3 p) { var g = ZGame.I; return g ? g.GroundY(p.x, p.z) : p.y; }

        /// <summary>How far (m) the drawn turret reaches from its pivot (its meshes' own boxes, turned with them): it lies on its side in any
        /// turn about the pivot it lands on, so its barrel may point any way.</summary>
        static float ReachFromPivot(Transform t, float fallback)
        {
            float far = 0f; bool any = false; var pv = t.position;
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf ? mf.GetComponent<MeshRenderer>() : null;
                if (!mr || !mr.enabled || !mf.sharedMesh) continue;
                var mb = mf.sharedMesh.bounds; var m = mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f)));
                    far = Mathf.Max(far, (c - pv).magnitude); any = true;
                }
            }
            return any ? far : fallback;
        }

        // ------------------------------------------------------------------ the tank / gun turret (UnitView.TossTurret)
        /// <summary>UnitView.TossTurret, after the arc's numbers are set: a turret whose landed body (a disc of its reach from its pivot, along
        /// its short skid) would touch a rock's drawn shape, or whose arc would pass through a rock, lands on the nearest free place within
        /// <see cref="TurretMaxMove"/> m of the rules' debris point (rings of 1 m, 30 deg apart; never into water, a building or a gun; only
        /// when none lies that near, up to <see cref="TurretMaxMoveFar"/> m); an arc that only clips a rock with the landing free rises
        /// instead. The rules' own debris blast stays where they put it; its drawn blast follows the turret (<see cref="TurretBlastAt"/>).</summary>
        public static void TurretLanding(int unitId, Transform turret, Vector3 from, ref Vector3 to, ref float apex)
        {
            if (!AllOn || !turret || OzWaterLanding.OpenWaterAt(to, out _)) return;
            var drawnAt = to;
            TurretLandingCore(turret, from, ref to, ref apex);
            var moved = to - drawnAt; moved.y = 0f;
            if (moved.sqrMagnitude <= 0.01f) return;
            if (movedTurrets.Count > 32)
            {   // landings whose blast never came (the unit's debris was drawn elsewhere): forgotten
                stale.Clear(); foreach (var kv in movedTurrets) if (Time.time > kv.Value.until) stale.Add(kv.Key);
                foreach (int k in stale) movedTurrets.Remove(k);
            }
            movedTurrets[unitId] = (drawnAt, to, Time.time + 12f); TurretBlastsMoved++;
        }
        static readonly List<int> stale = new List<int>(8);

        /// <summary>Drawn turret landings moved off the rocks, by the dead unit's id: the rules' point, the turret's, until when (s).</summary>
        static readonly Dictionary<int, (Vector3 drawn, Vector3 moved, float until)> movedTurrets = new Dictionary<int, (Vector3, Vector3, float)>();
        /// <summary>Turret landings moved / their drawn blasts that followed the turret.</summary>
        public static int TurretBlastsMoved, TurretBlastsFollowed;

        /// <summary>LegacyMatch.MissileBlast, a turret's landing blast (the rules' debris missile of the unit `unitId` at `at`): where it
        /// is drawn: on the turret when its landing was moved off a rock, else where the rules put it. The rules' blast itself (its damage)
        /// stays at its own point.</summary>
        public static Vector3 TurretBlastAt(int unitId, Vector3 at)
        {
            if (!movedTurrets.TryGetValue(unitId, out var m)) return at;
            movedTurrets.Remove(unitId);
            if (Time.time > m.until || new Vector2(m.drawn.x - at.x, m.drawn.z - at.z).magnitude > 1.5f) return at;
            TurretBlastsFollowed++;
            return new Vector3(m.moved.x, Ground(m.moved), m.moved.z);
        }

        static void TurretLandingCore(Transform turret, Vector3 from, ref Vector3 to, ref float apex)
        {
            var g = ZGame.I; if (!g || g.World == null) return;
            EnsureRocks();
            if (!RockWithin(to, TurretMaxMove + 12f) && !RockWithin(Vector3.Lerp(from, to, 0.5f), Vector3.Distance(from, to) * 0.5f + 8f)) return;   // no rock anywhere near: as drawn
            TurretLandings++;
            float r = Mathf.Clamp(ReachFromPivot(turret, 1.6f), 0.5f, 4f);
            var flat = to - from; flat.y = 0f; var dir = flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.forward;
            string why = TurretBlocked(from, to, apex, r, dir, out bool landClear);
            if (why == null) { LastTurret = "free where it was drawn"; return; }
            if (landClear && RaiseArc(from, to, r, dir, ref apex)) { TurretRaised++; LastTurret = $"its arc raised to {apex:0.0} m ({why})"; return; }
            var drawn = to;
            for (int ring = 0; ring < 2; ring++)
            for (int pass = 0; pass < 2; pass++)
            {   // pass 0: landing and arc free (the arc may rise); pass 1: the landing free (a drawn landing that is free stays); the near
                // rings first (up to TurretMaxMove), the far ones only when none of those is free
                if (pass == 1 && landClear) break;
                float d0 = ring == 0 ? 1f : TurretMaxMove + 1f, d1 = ring == 0 ? TurretMaxMove : TurretMaxMoveFar;
                for (float d = d0; d <= d1 + 1e-3f; d += 1f)
                    for (int s = 0; s < 12; s++)
                    {
                        int turn = (s + 1) / 2 * ((s & 1) == 0 ? 1 : -1);                                            // 0, +30, -30, +60, ... (from the side it came from)
                        var q = drawn + Quaternion.Euler(0f, turn * 30f, 0f) * -dir * d;
                        q.y = g.GroundY(q.x, q.z) + 0.35f;
                        if (!PlaceFree(g.World, q, r, 0.3f)) continue;
                        var f2 = q - from; f2.y = 0f; var dir2 = f2.sqrMagnitude > 0.01f ? f2.normalized : dir;
                        float a2 = apex;
                        string w2 = TurretBlocked(from, q, a2, r, dir2, out bool lc2);
                        if (!lc2) continue;
                        if (w2 != null && pass == 0 && !RaiseArc(from, q, r, dir2, ref a2)) continue;
                        to = q; if (a2 > apex) TurretRaised++; apex = a2; TurretMoved++;
                        LastTurret = $"moved {d:0} m{(pass == 1 ? " (its arc still passes a rock)" : "")} ({why})";
                        return;
                    }
            }
            if (landClear) { TurretArcOnly++; LastTurret = "lands free, its arc passes a rock (" + why + ")"; return; }
            for (int k = 1; k <= 2; k++)
            {   // last: the turret's core only (its reach x 0.75, then x 0.5): a long barrel lies along the ground beside the rock
                float r2 = Mathf.Max(0.5f, r * (1f - 0.25f * k));
                for (float d = 1f; d <= TurretMaxMoveFar + 1e-3f; d += 1f)
                    for (int s = 0; s < 12; s++)
                    {
                        int turn = (s + 1) / 2 * ((s & 1) == 0 ? 1 : -1);
                        var q = drawn + Quaternion.Euler(0f, turn * 30f, 0f) * -dir * d;
                        q.y = g.GroundY(q.x, q.z) + 0.35f;
                        if (!PlaceFree(g.World, q, r2, 0.3f)) continue;
                        var f2 = q - from; f2.y = 0f; var dir2 = f2.sqrMagnitude > 0.01f ? f2.normalized : dir;
                        float a2 = apex;
                        string w2 = TurretBlocked(from, q, a2, r2, dir2, out bool lc2);
                        if (!lc2) continue;
                        if (w2 != null) RaiseArc(from, q, r2, dir2, ref a2);
                        to = q; if (a2 > apex) TurretRaised++; apex = a2; TurretMoved++; TurretCoreMoved++;
                        LastTurret = $"moved {d:0} m, its core only ({why})";
                        return;
                    }
            }
            if (TurretLandingFar(from, ref to, ref apex, r, dir, drawn, why)) return;                               // items 331 + 332: a gun or a building in the way: farther out
            TurretKept++; LastTurret = "no free place within " + TurretMaxMoveFar.ToString("0") + " m, kept (" + why + ")";
        }

        /// <summary>Turrets that land free while their arc still passes a rock's drawn box (no higher arc or near place cleared it).</summary>
        public static int TurretArcOnly;
        /// <summary>Turrets moved by the last search (no place within reach of the whole turret: its core, the barrel beside the rock).</summary>
        public static int TurretCoreMoved;

        static bool RaiseArc(Vector3 from, Vector3 to, float r, Vector3 dir, ref float apex)
        {
            for (float k = 1.25f; k <= TurretApexMore + 1e-3f; k += 0.25f)
            {
                float a = Mathf.Min(TurretApexMax, apex * k);
                if (TurretBlocked(from, to, a, r, dir, out _) != null) continue;
                apex = a; return true;
            }
            return false;
        }

        /// <summary>The turret's landing (its body and its skid) and its arc against the rocks; landClear = the landing alone is free.</summary>
        static string TurretBlocked(Vector3 from, Vector3 to, float apex, float r, Vector3 dir, out bool landClear)
        {
            var land = new Vector3(to.x, Ground(to), to.z);
            string w = DiscRock(land, r, 0.3f) ?? DiscRock(land + dir * 0.6f, r, 0.3f);                         // OzHeavyLanding's skid: up to 0.6 m along its way
            landClear = w == null;
            if (w != null) return w;
            return ArcRock(from, to, apex, 0.15f, 0.97f, 10, Mathf.Min(TurretArcBallM, r * 0.6f), 0.2f);   // its body along the arc (the barrel's sweep left out)
        }

        // ------------------------------------------------------------------ a robot thrown by a blast, scripted (UnitView, Death.Airborne)
        /// <summary>UnitView.Die, the scripted blast fall (the ragdolls are used up): a body whose lying place would touch a rock's drawn
        /// shape, or whose flight would pass through a rock, lands nearer along its way (0.5 m steps, down to 0.4 m), else turned off its way
        /// (15 deg steps, then as far as <see cref="BodyMaxLandM"/>); `away` follows a turn. The lying place: the body along its way from half its height behind the landing to its
        /// full height past it (0.5 m of clear air each side), and round the landing as far as half its height (it may lie turned).
        /// A landing that touches no rock is kept.</summary>
        public static Vector3 BodyLanding(Vector3 from, Vector3 to, float airH, float height, ref Vector3 away)
        {
            if (!AllOn) return to;
            var g = ZGame.I; if (!g || g.World == null) return to;
            EnsureRocks();
            float H = Mathf.Clamp(height, 1.2f, 4.5f);
            if (!RockWithin(to, H + 6f) && !RockWithin(from, H + 6f)) return to;
            BodyLandings++;
            var flat = to - from; flat.y = 0f; float d0 = flat.magnitude;
            var dir = d0 > 0.01f ? flat / d0 : (away.sqrMagnitude > 0.001f ? new Vector3(away.x, 0f, away.z).normalized : Vector3.forward);
            string why = BodyBlocked(from, to, airH, H, dir);
            if (why == null) { LastBody = "free where it was drawn"; return to; }
            float dMax = Mathf.Max(d0, BodyMaxLandM);
            for (int s = 0; s < 25; s++)
            {
                int turn = (s + 1) / 2 * ((s & 1) == 0 ? 1 : -1);                                                    // 0, +15, -15, ... +-180
                var dv = Quaternion.Euler(0f, turn * 15f, 0f) * dir;
                for (float d = s == 0 ? d0 - 0.5f : dMax; d >= 0.4f - 1e-3f; d -= 0.5f)
                {
                    var q = from + dv * d; q.y = g.GroundY(q.x, q.z);
                    if (!PlaceFree(g.World, q, 0.5f * H, 0.2f)) continue;
                    if (BodyBlocked(from, q, airH, H, dv) != null) continue;
                    if (turn != 0) away = dv * Mathf.Max(0.01f, away.magnitude);
                    BodyMoved++; LastBody = $"moved to {d:0.0} m from {d0:0.0} m{(turn != 0 ? ", turned " + (turn * 15) + " deg" : "")} ({why})";
                    return new Vector3(q.x, to.y, q.z);
                }
            }
            for (int s = 0; s < 25; s++)
            {   // last: its lying place only (in a pocket of rocks its short flight may graze a rock's edge; it never lies in one)
                int turn = (s + 1) / 2 * ((s & 1) == 0 ? 1 : -1);
                var dv = Quaternion.Euler(0f, turn * 15f, 0f) * dir;
                for (float d = dMax; d >= 0.4f - 1e-3f; d -= 0.5f)
                {
                    var q = from + dv * d; q.y = g.GroundY(q.x, q.z);
                    if (!PlaceFree(g.World, q, 0.5f * H, 0.2f)) continue;
                    if (BodyBlocked(from, q, airH, H, dv, false) != null) continue;
                    if (turn != 0) away = dv * Mathf.Max(0.01f, away.magnitude);
                    BodyMoved++; BodyLyingOnly++; LastBody = $"moved to {d:0.0} m from {d0:0.0} m, its lying place only ({why})";
                    return new Vector3(q.x, to.y, q.z);
                }
            }
            BodyKept++; LastBody = "no free place, kept (" + why + ")";
            return to;
        }

        /// <summary>Scripted body landings moved by the last search (its lying place free, its flight not).</summary>
        public static int BodyLyingOnly;

        static string BodyBlocked(Vector3 from, Vector3 to, float airH, float H, Vector3 dir, bool flight = true)
        {
            var land = new Vector3(to.x, Ground(to), to.z);
            string w = DiscRock(land, 0.45f * H + 0.3f, 0.1f);
            for (float s = -0.5f * H; w == null && s <= H + 1e-3f; s += 0.5f)
            {
                var p = land + dir * s; p.y = Ground(p);
                w = DiscRock(p, 0.35f, 0.15f);
            }
            if (w != null || !flight) return w;
            return ArcRock(from + Vector3.up * 0.9f, land + Vector3.up * 0.4f, airH, 0.1f, 0.9f, 8, 0.45f, 0.1f);
        }

        // ------------------------------------------------------------------ a felled level 16 tree (OzTreeFall)
        /// <summary>OzTreeFall.OnBroken: a tree whose lying trunk (from its stump to its tip, 0.4 m thick) or crown (radius `crown` round
        /// 0.72 of its height) would lie in a rock's drawn shape falls turned by the least turn that clears every rock (10 deg steps, at most
        /// <see cref="TreeMaxTurn"/>, never back toward the hit); none clear: as drawn. A fall that touches no rock keeps its way.</summary>
        public static Vector3 TreeAway(Vector3 foot, Vector3 away, float height, float crown, float cut)
        {
            if (!AllOn || away.sqrMagnitude < 1e-4f) return away;
            EnsureRocks();
            float L = Mathf.Max(2f, height);
            if (!RockWithin(foot, L + crown + 4f)) return away;
            TreeFalls++;
            string why = TreeBlocked(foot, away, L, crown, cut);
            if (why == null) { LastTree = "free where it was drawn"; return away; }
            for (int s = 1; s <= 2 * Mathf.RoundToInt(TreeMaxTurn / 10f); s++)
            {
                int turn = (s + 1) / 2 * ((s & 1) == 1 ? 1 : -1);
                var a = Quaternion.Euler(0f, turn * 10f, 0f) * away;
                if (TreeBlocked(foot, a, L, crown, cut) != null) continue;
                TreeTurned++; LastTree = $"turned {turn * 10} deg ({why})";
                return a.normalized;
            }
            TreeKept++; LastTree = "no free way within " + TreeMaxTurn.ToString("0") + " deg, kept (" + why + ")";
            return away;
        }

        static string TreeBlocked(Vector3 foot, Vector3 away, float L, float crown, float cut)
        {
            var dir = new Vector3(away.x, 0f, away.z).normalized;
            float hc = Mathf.Max(1f, L * 0.72f - cut), rc = Mathf.Min(crown, hc * 0.9f), tip = Mathf.Max(hc, L - cut);
            for (float s = 1f; s <= tip + 1e-3f; s += 0.8f)
            {
                var p = foot + dir * s; p.y = Ground(p);
                var w = DiscRock(p, 0.4f, 0.1f); if (w != null) return w;
            }
            var c = foot + dir * hc; c.y = Ground(c);
            return DiscRock(c, rc, 0.1f);
        }
    }
}
