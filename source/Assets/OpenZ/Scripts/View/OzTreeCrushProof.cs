using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The Play proof of item 360 (rule DV124, View/OzTreeCrush.cs; menu OpenZ/V1.1/Trees: K360 ..., Editor/OzTreeCrushMenu.cs), in the
    /// running level (16 for the shuttle and the fort cannon; 16 or 2 for the turret). Counted PASS / FAIL lines, the take's sound log,
    /// full-screen pictures from the normal RTS camera (70 m, a FIXED camera on the place it comes down), into <see cref="Folder"/>:
    /// (T) a medium tank destroyed beside a tree of the rules, its turret's debris point put on the tree's drawn trunk: the turret lands
    /// on the tree, the tree breaks at that moment (its break, the heavy wood crack and splinters), no standing trunk under the turret;
    /// (S) the level's shuttle flown over the trees and shot down in the air where its hull comes down on a tree: the trees the rules
    /// fell break when a big piece reaches them (none by the time-out), no standing tree with a resting piece through its trunk;
    /// (G) a fort's cannon shot to pieces while the fort stands, on a heading whose landing lies on a tree: the trees under it break
    /// when it lands, no standing tree through a resting piece. Test only (it moves the camera and destroys what it stages).
    /// </summary>
    public sealed class OzTreeCrushProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            const string sub = "k360_crash_fells_trees";
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", sub);
            return System.IO.Path.Combine(Application.persistentDataPath, sub);
        }
        public static void Run(string mode)
        {
            Report = "running " + mode;
            var go = new GameObject("OzTreeCrushProof"); var p = go.AddComponent<OzTreeCrushProof>(); p.mode = mode;
        }

        string mode = "TSG";
        ZGame g; ZWorld W; RtsCamera rts;
        readonly StringBuilder sb = new StringBuilder();
        int fails, passes, frames; string dir; float T0; Vector3? camAt;
        const float CamHeight = 70f;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; rts = FindFirstObjectByType<RtsCamera>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Update() { if (camAt.HasValue && rts) rts.focus = new Vector3(camAt.Value.x, 0f, camAt.Value.z); }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzTreeCrushProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); frames++; Log($"frame {name}.png ({Time.unscaledTime - T0:0.00} s)"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus) { camAt = focus; if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(CamHeight); }
        readonly Dictionary<int, Vector3> feet = new Dictionary<int, Vector3>();
        Vector3 Foot(ZObject o)
        {
            if (feet.Count == 0)
                for (int i = 0; i < W.HeavyTreeCount; i++) { var t = W.HeavyTreeAt(i, out double fx, out double fy); var p = ZGame.PxToWorld(fx, fy); p.y = g.GroundY(p.x, p.z); feet[t.Id] = p; }
            return feet.TryGetValue(o.Id, out var f) ? f : g.CenterWorld(o);
        }
        List<ZObject> Standing() { var l = new List<ZObject>(); for (int i = 0; i < W.HeavyTreeCount; i++) { var t = W.HeavyTreeAt(i, out _, out _); if (W.HeavyTreeStanding(t)) l.Add(t); } return l; }
        bool DrawnDown(ZObject o)
        {
            if (W.IsTreeTile(o)) return OzTreeFall.StateOf(o.LocX >> 4, o.LocY >> 4) >= 2;
            var dp = DetailPropHub.OfSim(o.Id); return dp ? dp.Broken : !W.HeavyTreeStanding(o);
        }
        bool RockNear(Vector3 p, float r) { foreach (var o in W.Objects) if (o.Type == "rock" && !o.Destroyed && Vector3.Distance(Flat(g.CenterWorld(o, false)), Flat(p)) < r) return true; return false; }
        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>The detail sounds heard between t0 and t1 (unscaled), with their count: the event's name, or "clip:" + the clip for a
        /// prop's break with no event of its own (the level 2 palms and cacti).</summary>
        static Dictionary<string, int> Heard(float t0, float t1)
        {
            var d = new Dictionary<string, int>();
            foreach (var e in OzSoundLog.Since(t0))
            {
                if (e.t > t1 || e.src != "DetailsV2") continue;
                string k = string.IsNullOrEmpty(e.key) ? "clip:" + e.clip : e.key;
                d.TryGetValue(k, out int n); d[k] = n + 1;
            }
            return d;
        }
        static bool HeardBreak(Dictionary<string, int> d) => d.Keys.Any(k => k == "dtl_tree_break" || k.StartsWith("clip:"));
        static string Fmt(Dictionary<string, int> d) => d.Count == 0 ? "nothing" : string.Join(", ", d.Select(kv => kv.Key + " x" + kv.Value));

        /// <summary>Standing trees of the rules within r of p whose trunk (foot to 3 m up, 0.3 m round) a resting piece's collider enters by more than 5 cm.</summary>
        int TrunksThrough(Vector3 p, float r, IEnumerable<Collider> cols, out string which)
        {
            int n = 0; which = "";
            var cl = cols.Where(c => c && c.enabled).ToList();
            foreach (var t in Standing())
            {
                var f = Foot(t); if (Vector3.Distance(Flat(f), Flat(p)) > r) continue;
                foreach (var c in cl)
                {
                    bool hit = false;
                    for (int k = 0; k <= 6 && !hit; k++)
                    {
                        var q = f + Vector3.up * (0.3f + 0.45f * k);
                        var cp = c.ClosestPoint(q);
                        if (new Vector2(cp.x - q.x, cp.z - q.z).magnitude < 0.25f && Mathf.Abs(cp.y - q.y) < 0.05f) hit = true;
                    }
                    if (hit) { n++; which += $" #{t.Id}/{c.name}"; break; }
                }
            }
            return n;
        }

        IEnumerator All()
        {
            T0 = Time.unscaledTime;
            Log($"K360 proof, level {(g ? g.level : "-")}, mode {mode}, rule DV124 {(ZWorld.HeavyFallDeviation ? "ON" : "off")}, view {(OzTreeCrush.Enabled ? "ON" : "off")}, turret rock clearance {ZWorld.HeavyTurretRockClearM:0.0} m");
            if (W == null || !OzTreeCrush.I) { Check("a level is running with OzTreeCrush", false); Finish(); yield break; }
            Check($"the rules know {W.HeavyTreeCount} trees with their drawn trunks", W.HeavyTreeCount > 0);
            if (mode.Contains("T")) yield return Turret();
            if (mode.Contains("S")) yield return Shuttle();
            if (mode.Contains("G")) yield return Gun();
            Finish();
        }

        void Finish()
        {
            Log($"counts: held {OzTreeCrush.Held}, by touch {OzTreeCrush.ReleasedByTouch}, by landing {OzTreeCrush.ReleasedByLanding}, early {OzTreeCrush.ReleasedEarly}, at once {OzTreeCrush.ReleasedAtOnce}, by time-out {OzTreeCrush.ReleasedByTimeout}; " +
                $"turret crushes {OzTreeCrush.TurretCrushes}, turned {OzTreeCrush.TurretTurned}, standing under {OzTreeCrush.TurretStandingUnder}, targets missed {OzTreeCrush.TurretTargetsMissed}; drawn trees felled {OzTreeCrush.FloraFelled + OzTreeCrush.PropsFelled}; " +
                $"shuttle carried {OzTreeCrush.ShuttleCarried} (skipped {OzTreeCrush.ShuttleCarrySkipped}), drop carried {OzTreeCrush.DropCarried}, aimed {OzTreeCrush.ShuttleAimed}; cannon tosses of the rules {OzTreeCrush.GunTossesFromRules}; " +
                $"trunks {OzTreeCrush.TrunksMade} (solid {OzTreeCrush.SolidTrunks}, trigger {OzTreeCrush.TriggerTrunks}), solid hits {OzTreeCrush.SolidHits}, in a piece at the start {OzTreeCrush.StartInPiece}; crack sounds {OzTreeCrush.CrushSounds}");
            Log($"RESULT {(fails == 0 ? "PASS" : "FAIL")}: {passes} PASS, {fails} FAIL, {frames} pictures in {dir}");
            Report = $"{(fails == 0 ? "PASS" : "FAIL")} {passes}/{passes + fails}";
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "k360_proof_" + mode + ".txt"), sb.ToString()); } catch (System.Exception ex) { Debug.LogWarning("[OzTreeCrushProof] " + ex.Message); }
            camAt = null;
            Destroy(gameObject, 0.5f);
        }

        // ------------------------------------------------------------------ (T) a tank's turret onto a tree
        IEnumerator Turret()
        {
            Log("T: a medium tank destroyed beside a tree, its turret onto the tree");
            ZObject tree = null; (int x, int y) spot = (-1, -1);
            foreach (var t in Standing().OrderBy(t => W.IsTreeTile(t) || DetailPropHub.OfSim(t.Id) ? 0 : 1))   // a drawn tree of its own first (level 2: a DV67 palm or cactus)
            {
                var f = Foot(t); if (RockNear(f, 9f)) continue;
                if (W.Map.ClassAt(t.LocX >> 4, t.LocY >> 4) == ZClass.Water) continue;
                spot = OpenNear(t.LocX >> 4, t.LocY >> 4, 6);
                if (spot.x >= 0) { tree = t; break; }
            }
            if (tree == null) { Check("T: a tree with open ground beside it, no rock near", false); yield break; }
            var foot = Foot(tree);
            var tank = W.CreateObject("vehicle", "medium", spot.x * 16, spot.y * 16, Team.Blue);
            if (tank == null) { Check("T: the tank was made", false); yield break; }
            g.SpawnUnitView(tank);
            Cam((foot + g.CenterWorld(tank)) * 0.5f);
            yield return Wait(1.5f);
            Shot("T1_before");
            int under0 = OzTreeCrush.TurretStandingUnder, missed0 = OzTreeCrush.TurretTargetsMissed, crush0 = OzTreeCrush.TurretCrushes;
            float s0 = Time.unscaledTime;
            tank.SetHealth(0); W.UpdateObjectHealth(tank);
            ZMissile m = null; foreach (var x in W.QueuedMissiles) if (x.debris && x.attackerId == tank.Id) m = x;
            if (m == null) { Check("T: the tank's turret missile", false); yield break; }
            var wp = ZGame.WorldToPx(foot); m.x = wp.x + 2; m.y = wp.y + 1;                                   // the turret comes down on the tree's drawn trunk
            Log($"T: tank #{tank.Id} destroyed {Vector3.Distance(Flat(foot), Flat(g.CenterWorld(tank))):0.0} m from tree #{tree.Id}; its turret lands on the trunk at rules time {m.explodeTime:0.00} (now {W.Time:0.00})");
            yield return WaitFor(() => W.HeavyFallOf(tank.Id) != null, 12f);
            float landAt = Time.unscaledTime;
            Shot("T2_landing");
            yield return Wait(0.5f); Shot("T3_plus0_5s");
            yield return Wait(1.0f); Shot("T4_plus1_5s");
            yield return Wait(2.5f); Shot("T5_plus4s");
            var f2 = W.HeavyFallOf(tank.Id);
            Check($"T: the rules felled the tree under the turret ({(f2 != null ? string.Join(", ", f2.trees.Select(i => "#" + i)) : "no fall")})", f2 != null && f2.trees.Contains(tree.Id) && f2.leftStanding == 0);
            Check("T: its drawn tree is down", DrawnDown(tree));
            var heard = Heard(landAt - 0.5f, landAt + 2f);
            Check($"T: heard at the landing: {Fmt(heard)} (the break and the heavy crack)", HeardBreak(heard) && heard.ContainsKey("dtl_tree_crush"));
            Check($"T: the turret lies on the trees it fells (missed {OzTreeCrush.TurretTargetsMissed - missed0}) and on no standing trunk ({OzTreeCrush.TurretStandingUnder - under0})", OzTreeCrush.TurretTargetsMissed == missed0 && OzTreeCrush.TurretStandingUnder == under0);
            Check("T: the crush was drawn at the turret's landing", OzTreeCrush.TurretCrushes > crush0);
        }

        (int x, int y) OpenNear(int tx, int ty, int dist)
        {
            for (int r = dist; r < dist + 8; r++)
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI / 8f; int x = tx + Mathf.RoundToInt(Mathf.Cos(a) * r), y = ty + Mathf.RoundToInt(Mathf.Sin(a) * r);
                    if (x < 2 || y < 2 || x >= W.Map.width - 3 || y >= W.Map.height - 3) continue;
                    bool ok = true;
                    for (int dy = 0; dy < 2 && ok; dy++) for (int dx = 0; dx < 2 && ok; dx++) ok = W.Path.TilePassable(x + dx, y + dy, false);
                    if (ok) return (x, y);
                }
            return (-1, -1);
        }

        // ------------------------------------------------------------------ (S) the shuttle shot down over trees
        IEnumerator Shuttle()
        {
            Log("S: the shuttle flown over the trees and shot down in the air where its hull comes down on a tree");
            var ship = W.Objects.FirstOrDefault(o => o.IsShuttle && !o.Destroyed);
            if (ship == null) { Check("S: a shuttle on this level", false); yield break; }
            // a tree with others round it, 30-60 m from the ship, open ground 25 m beyond it
            ZObject tree = null; (int x, int y) goal = (-1, -1);
            var sc = g.CenterWorld(ship);
            var all = Standing();
            foreach (var t in all.OrderByDescending(t => all.Count(u => Vector3.Distance(Flat(Foot(u)), Flat(Foot(t))) < 5f)))
            {
                var f = Foot(t); float d = Vector3.Distance(Flat(f), Flat(sc)); if (d < 30f || d > 90f) continue;
                var dir = Flat(f - sc).normalized; var b = f + dir * 25f; var bp = ZGame.WorldToPx(b);
                int bx = bp.x >> 4, by = bp.y >> 4; if (bx < 4 || by < 4 || bx >= W.Map.width - 4 || by >= W.Map.height - 4) continue;
                tree = t; goal = W.ShuttleClamp(bp.x, bp.y); break;
            }
            if (tree == null) { Check("S: a tree 30-90 m from the shuttle", false); yield break; }
            var foot = Foot(tree);
            Cam(foot);
            W.SendWaypoints(ship.Owner, ship, new[] { new ZWaypoint { mode = WpMode.Move, refId = -1, x = goal.x, y = goal.y, playerGiven = true } });
            Log($"S: shuttle #{ship.Id} ordered over tree #{tree.Id} ({Standing().Count(u => Vector3.Distance(Flat(Foot(u)), Flat(foot)) < 5f)} trees within 5 m) to {goal.x}, {goal.y}");
            bool shotNear = false; double best = double.MaxValue; float tA = Time.unscaledTime;
            while (Time.unscaledTime - tA < 60f && !ship.Destroyed)
            {   // shot down the first time its hull would come down on a standing tree (within 2.5 m of a trunk)
                if (ZWorld.HeavyShuttlePoint(ship, out double px, out double py, out double T) && ship.ShuttleHeightM > 6.0)
                {
                    var ip = ZGame.PxToWorld(px, py); ZObject near = null; double d = double.MaxValue;
                    foreach (var u in all) { if (!W.HeavyTreeStanding(u)) continue; double du = Vector3.Distance(Flat(Foot(u)), Flat(ip)); if (du < d) { d = du; near = u; } }
                    best = System.Math.Min(best, d);
                    if (!shotNear && Vector3.Distance(Flat(ip), Flat(foot)) < 20f) { Shot("S1_approach"); shotNear = true; }
                    if (d <= 2.5 && near != null) { tree = near; break; }
                }
                yield return null;
            }
            if (ship.Destroyed || best > 2.5) { Check($"S: the shuttle's hull would come down on a tree (nearest {best:0.0} m)", false); yield break; }
            int to0 = OzTreeCrush.ReleasedByTimeout, touch0 = OzTreeCrush.ReleasedByTouch + OzTreeCrush.ReleasedEarly + OzTreeCrush.ReleasedByLanding + OzTreeCrush.ReleasedAtOnce;
            var before = Standing();
            float s0 = Time.unscaledTime; double h = ship.ShuttleHeightM;
            ship.DamageHealth(ship.Health); W.UpdateObjectHealth(ship);
            var fall = W.HeavyFallOf(ship.Id);
            Log($"S: shot down {h:0.0} m up: the rules' fall {(fall != null ? $"lands after {fall.fallSec:0.00} s, {fall.targets.Count} trees under its hull ({string.Join(", ", fall.targets.Select(i => "#" + i))})" : "NONE")}");
            Check("S: the rules know where its hull comes down and the trees under it", fall != null && fall.targets.Count >= 1);
            yield return null; Shot("S2_kill");
            yield return Wait(0.5f); Shot("S3_plus0_5s");
            yield return Wait(0.5f); Shot("S4_plus1s");
            yield return Wait(0.6f); Shot("S5_landing_plus1_6s");
            yield return Wait(1.4f); Shot("S6_plus3s");
            yield return WaitFor(() => OzShuttleBreakup.LastAllRested(), 14f);
            yield return Wait(0.5f); Shot("S7_rest");
            if (fall == null) yield break;
            var felled = fall.trees.Select(i => W.GetAny(i)).Where(o => o != null).ToList();
            Check($"S: the trees under its hull fell ({felled.Count}: {string.Join(", ", felled.Select(o => "#" + o.Id))}), none left standing in its cover", felled.Count >= 1 && fall.leftStanding == 0);
            Check($"S: each drawn tree is down ({felled.Count(DrawnDown)} of {felled.Count})", felled.All(DrawnDown));
            Check($"S: every one broke where a piece reached it or the hull lay (by time-out: {OzTreeCrush.ReleasedByTimeout - to0})", OzTreeCrush.ReleasedByTimeout == to0);
            int through = TrunksThrough(ZGame.PxToWorld(fall.x, fall.y), 25f, OzShuttleBreakup.LastPieces.Where(pc => pc).Select(pc => pc.PieceCollider), out string which);
            Check($"S: no standing tree with a resting piece through its trunk ({through}{which})", through == 0);
            var heard = Heard(s0, Time.unscaledTime);
            Check($"S: heard: {Fmt(heard)}", HeardBreak(heard) && heard.ContainsKey("dtl_tree_crush"));
            Log($"S: {OzTreeCrush.LastLog}");
        }

        // ------------------------------------------------------------------ (G) the fort cannon onto a tree
        IEnumerator Gun()
        {
            Log("G: a fort's cannon shot to pieces while its fort stands, its toss onto a tree");
            ZObject fort = null; ZHeavyFall pre = null; int heading = 0;
            foreach (var o in W.Objects.Where(o => o.IsFort && !o.Destroyed && !o.FortGunDestroyed && o.Owner != Team.Neutral))
            {
                for (int hd = 0; hd < 360 && pre == null; hd += 3)
                {
                    var p = W.HeavyGunToss(o, hd, false);
                    if (p == null || p.movedM > 0 || p.movedDeg != 0 || W.HeavyTreesUnder(p).Count == 0) continue;
                    pre = p; heading = hd; fort = o;
                }
                if (pre != null) break;
            }
            if (fort == null) { Check("G: a fort whose cannon can land on a tree", false); yield break; }
            var under = W.HeavyTreesUnder(pre);
            var land = ZGame.PxToWorld(pre.x, pre.y); land.y = g.GroundY(land.x, land.z);
            Cam(land);
            yield return Wait(1f);
            Shot("G1_before");
            int to0 = OzTreeCrush.ReleasedByTimeout, rules0 = OzTreeCrush.GunTossesFromRules;
            float s0 = Time.unscaledTime;
            ZWorld.HeavyGunHeadingTest = heading;
            fort.FortGunHealth = 0; fort.FortGunKilledAt = W.Time;
            W.HeavyFallSync();
            ZWorld.HeavyGunHeadingTest = double.NaN;
            var toss = W.HeavyFallGunToss(fort.Id);
            Log($"G: fort {fort.Type} #{fort.Id}: cannon shot to pieces, aim heading {heading} deg: the rules' toss {(toss != null ? $"to {toss.headingDeg:0.0} deg, {Vector3.Distance(Flat(land), Flat(g.CenterWorld(fort))):0.0} m out, {toss.targets.Count} trees under it ({string.Join(", ", toss.targets.Select(i => "#" + i))})" : "NONE")}");
            Check("G: the rules tossed it onto the trees", toss != null && toss.targets.Count == under.Count && under.All(toss.targets.Contains));
            yield return Wait(1.5f); Shot("G2_in_flight");
            yield return WaitFor(() => OzFortGunBreakup.LastPieces.Count > 0 && Time.unscaledTime - s0 > 2.5f, 6f);
            Shot("G3_landing");
            yield return Wait(0.5f); Shot("G4_plus0_5s");
            yield return Wait(1.5f); Shot("G5_plus2s");
            yield return WaitFor(() => OzFortGunBreakup.LastAllRested(), 14f);
            yield return Wait(0.5f); Shot("G6_rest");
            Check("G: the drawn cannon flew on the rules' toss", OzTreeCrush.GunTossesFromRules > rules0);
            if (toss == null) yield break;
            var felled = toss.trees.Select(i => W.GetAny(i)).Where(o => o != null).ToList();
            Check($"G: the trees under it fell ({felled.Count}: {string.Join(", ", felled.Select(o => "#" + o.Id))}), none left standing under it", felled.Count >= 1 && toss.leftStanding == 0);
            Check($"G: each drawn tree is down ({felled.Count(DrawnDown)} of {felled.Count})", felled.All(DrawnDown));
            Check($"G: every one broke when the cannon landed (by time-out: {OzTreeCrush.ReleasedByTimeout - to0})", OzTreeCrush.ReleasedByTimeout == to0);
            int through = TrunksThrough(land, 25f, OzFortGunBreakup.LastPieces.Where(pc => pc).SelectMany(pc => pc.cols), out string which);
            Check($"G: no standing tree with a resting piece through its trunk ({through}{which})", through == 0);
            var heard = Heard(s0, Time.unscaledTime);
            Check($"G: heard: {Fmt(heard)}", HeardBreak(heard) && heard.ContainsKey("dtl_tree_crush"));
            Log($"G: {OzTreeCrush.LastLog}");
        }
    }
}
