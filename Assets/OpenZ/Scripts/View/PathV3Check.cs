#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// on the level that is loaded, in the full look, CPU bots paused (they are put back at the end). Editor / development builds only.
    /// Every stage runs twice with the same camera: BEFORE (ZWorld.PathV3Deviation off = the live pathing, the rollback) and AFTER (on):
    ///   bridge    two groups cross a bridge towards each other at once (L02: the N-S bridge, squad vs squad and vehicles vs grunts;
    ///             L17: the E-W bridge, a squad vs three vehicles)
    ///   canal     L17: a squad, then three vehicles, cross the canal (robots wade, vehicles by land); L02: a squad into the river
    ///   ruin      L17: a squad, then three vehicles, to the far side of the destroyed bridge (52,114): the vehicles go the long way round
    ///   mass      30 units (squads + vehicles) ordered at once on open ground
    ///   building  a 6-grunt squad, then three vehicles, round a building (open ground: path commit)
    ///   wreck     a wreck (a neutral "wreck" map item, drawn as a dark block) closes one tile of the level's 2-tile gap; a 6-grunt squad and
    ///             two 3-grunt squads meet in it head-on: the jam clears within ~6 s (DV47: over the wreck, never through rock or a building)
    /// Steps: 1) open the OpenZ scene and start Play in the full look; 2) start L02 from the level menu, wait until the level is up;
    /// L02, loads L17 (OzScenes.Next) and runs L17 in the same Play; 4) PathV3Check.Report says "done L02 ...; done L17 ..." at the end.
    /// The staged area is cleared of units first (gone, not killed: no death events; cannons and parked map vehicles stay).
    /// Bars (AFTER): open stages - no orders left, reversals (drawn) no more than BEFORE, settle <= 1.5 s (building); bridge / canal /
    /// ruin - arrived no later than BEFORE + 15 % + 5 s, orders left no more than BEFORE, the longest stand with an order no more than
    /// max(BEFORE + 0.5, 3) s; wreck - the longest stand with an order <= 6.0 s (review round 2), DV47 fired, no order left; every stage - nobody on a hard
    /// blocked tile (building, rock, wall, water for a vehicle).
    /// </summary>
    public class PathV3Check : MonoBehaviour
    {
        public static string Report = "idle", Only;
        /// <summary>Game speed during the check (ZGame.gameSpeed, 0.25-4): every time in the report is sim time.</summary>
        public static float CheckSpeed = 3f;
        /// <summary>chain (default): started on L02, the check loads L17 next in the same Play (OzScenes.Next, the level order of the
        /// build) and runs there too - ONE short Play for both reports. Started on L17: L17 only.</summary>
        public static void Run(string only = null, bool chain = true)
        {
            Only = only; Chain = chain; Report = "running";
            var go = new GameObject("PathV3Check"); DontDestroyOnLoad(go); go.AddComponent<PathV3Check>();
        }
        public static bool Chain = true;
        static string Records => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records"));

        struct Site { public string name; public int sx, sy, gx, gy; }
        /// <summary>The headless harness's sites (tiles, pathing_v3/tools/PcScenarios.cs + PcReview.cs + the stress suite).</summary>
        static readonly Dictionary<string, Site[]> Sites = new Dictionary<string, Site[]>
        {
            { "L02", new[] {
                new Site { name = "gap", sx = 9, sy = 53, gx = 10, gy = 63 },
                new Site { name = "building", sx = 40, sy = 24, gx = 53, gy = 24 },
                new Site { name = "bridgeNS", sx = 52, sy = 54, gx = 53, gy = 67 },
                new Site { name = "river", sx = 26, sy = 20, gx = 35, gy = 20 },
                new Site { name = "mass", sx = 26, sy = 80, gx = 12, gy = 63 } } },
            { "L17", new[] {
                new Site { name = "gap", sx = 62, sy = 38, gx = 76, gy = 38 },
                new Site { name = "building", sx = 75, sy = 58, gx = 78, gy = 66 },
                new Site { name = "bridge", sx = 44, sy = 95, gx = 66, gy = 96 },
                new Site { name = "canal", sx = 36, sy = 40, gx = 78, gy = 36 },
                new Site { name = "ruin", sx = 38, sy = 112, gx = 72, gy = 112 },
                new Site { name = "mass", sx = 64, sy = 49, gx = 92, gy = 52 } } },
        };
        /// <summary>The test wreck (tiles x, y, w, h): one tile of the 2-tile gap (L02 row 59 cols 6-7; L17 the rock-wall opening rows 37-38).</summary>
        static readonly Dictionary<string, (int x, int y, int w, int h)> WreckAt = new Dictionary<string, (int, int, int, int)> { { "L02", (6, 59, 1, 1) }, { "L17", (68, 37, 3, 1) } };

        ZGame g; ZWorld W; RtsCamera cam; string lv;
        readonly StringBuilder md = new StringBuilder();
        readonly List<ZObject> staged = new List<ZObject>();
        int pass, runs;
        float bArrived, bStuck; int bLeft, bRevs;                                                // the BEFORE run of the stage (the AFTER bars compare with it)

        static bool Do(string k) => Only == null || Only.Contains(k);
        /// <summary>
        /// </summary>
        void Mark(string what)
        {
            try
            {
                System.IO.Directory.CreateDirectory($"{Records}/path_v3");
                System.IO.File.AppendAllText($"{Records}/path_v3/timeline_{lv}.txt", $"{System.DateTime.Now:HH:mm:ss.fff}\t{Time.unscaledTime:0.000}\t{what}\n");
            }
            catch { }
        }
        bool Has(string n) { foreach (var s in Sites[lv]) if (s.name == n) return true; return false; }
        Site S(string n) { foreach (var s in Sites[lv]) if (s.name == n) return s; return default; }

        struct StageDef { public string kind, site, a, b, cls; }

        IEnumerator Start()
        {
            string done = "";
            if (ZGame.I == null) { Report = "failed: start a level (L02, then the check loads L17) first"; Destroy(gameObject); yield break; }
            yield return RunLevel();
            done = Report;
            if (Chain && lv == "L02" && !Report.StartsWith("failed"))
            {                                                                                   // the same Play: the next level of the build (L17)
                Report = "loading L17"; OzScenes.Next();
                float w = 0f;
                while (w < 240f && (ZGame.I == null || ZGame.I.level != "L17" || ZGame.I.World == null || ZGame.I.World.Time < 2.0)) { w += Time.unscaledDeltaTime; yield return null; }
                if (ZGame.I == null || ZGame.I.level != "L17") { Report = done + $"; L17 not loaded after {w:0} s (run it there by hand)"; Destroy(gameObject); yield break; }
                md.Clear(); staged.Clear(); pass = runs = 0;
                yield return RunLevel();
                Report = done + "; " + Report;
            }
            Destroy(gameObject);
        }

        IEnumerator RunLevel()
        {
            g = ZGame.I; W = g.World; lv = g.level; cam = FindFirstObjectByType<RtsCamera>();
            if (W == null || lv == null || !Sites.ContainsKey(lv)) { Report = $"failed: load L02 or L17 first (level '{lv}')"; yield break; }
            var bots = new List<ZBot>(W.Bots); W.Bots.Clear();                                    // no CPU orders meanwhile (put back at the end)
            bool saved = ZWorld.PathV3Deviation; float speed0 = g.gameSpeed; g.gameSpeed = CheckSpeed;   // a short Play: the sim runs CheckSpeed x (Zod's own speed setting)
            md.AppendLine($"# PathV3Check {System.DateTime.Now:yyyy-MM-dd HH:mm} {lv} (pathing v3: before = oz_path_v3 off = the live pathing, after = on)\n");
            md.AppendLine("| stage | group | mode | units | reversals > 135 deg (drawn) | heading flips > 90 deg (drawn) | body yaw flips > 90 deg | longest stand with an order s | settle max s | micro-moves after arrival (drawn / sim) | orders left | closest members walking m | all arrived s | DV47 unsticks / units on soft tiles / unit-frames on hard tiles | zoned unit-frames % | bars (after) |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            var stages = new List<StageDef>();
            void Add(string kind, string site, string cls, string a, string b = null) { if (Has(site)) stages.Add(new StageDef { kind = kind, site = site, a = a, b = b, cls = cls }); }
            if (Do("bridge")) { Add("cross", "bridgeNS", "zone", "squad6", "squad6"); Add("cross", "bridgeNS", "zone", "veh3", "grunts2x3"); Add("cross", "bridge", "zone", "squad6", "veh3"); }
            if (Do("canal")) { Add("route", "canal", "zone", "squad6"); Add("route", "canal", "zone", "veh3"); Add("route", "river", "zone", "squad6"); }
            if (Do("ruin")) { Add("route", "ruin", "zone", "squad6"); Add("route", "ruin", "zone", "veh3"); }
            if (Do("mass")) Add("route", "mass", "open", "mix30");
            if (Do("building")) { Add("route", "building", "open", "squad6"); Add("route", "building", "open", "veh3"); }
            if (Do("wreck")) Add("wreck", "gap", "wreck", "squad6", "grunts2x3");
            if (Do("regroup")) Add("backforth", "building", "open", "squad6");
            foreach (var st in stages)
                foreach (var after in new[] { false, true })
                {
                    ZWorld.PathV3Deviation = after;
                    Report = $"running {st.kind} {st.site} {st.a}{(st.b != null ? " vs " + st.b : "")} {(after ? "after" : "before")}";
                    yield return Stage(S(st.site), st, after);
                }
            ZWorld.PathV3Deviation = saved; g.gameSpeed = speed0;
            W.Bots.AddRange(bots);
            md.AppendLine($"\n**{pass}/{runs} after-runs meet every bar: {(pass == runs ? "PASS" : "FAIL")}**");
            System.IO.File.WriteAllText($"{Records}/path_v3_check_{lv}.md", md.ToString());
            Report = $"done {lv}: {pass}/{runs} PASS, {Records}/path_v3_check_{lv}.md";
        }

        /// <summary>Units in the staged area are taken off the map (gone, not killed: no death event, no wreck); cannons and parked (neutral)
        /// map vehicles stay, like in the headless harness.</summary>
        void Clear(Site s, int margin)
        {
            int x0 = Mathf.Min(s.sx, s.gx) - margin, x1 = Mathf.Max(s.sx, s.gx) + margin, y0 = Mathf.Min(s.sy, s.gy) - margin, y1 = Mathf.Max(s.sy, s.gy) + margin;
            foreach (var o in new List<ZObject>(W.Objects))
            {
                if (!o.IsUnit || o.Destroyed || o.KillAt >= 0 || o.Kind == ZKind.Cannon) continue;
                if (o.Kind == ZKind.Vehicle && o.Owner == Team.Neutral) continue;
                int tx = o.CenterX >> 4, ty = o.CenterY >> 4;
                if (tx < x0 || tx > x1 || ty < y0 || ty > y1) continue;
                o.KillAt = W.Time;
            }
        }

        bool Free(int tx, int ty, bool robot, List<(int x, int y, int r)> taken, int r)
        {
            int n = robot ? 1 : 2;
            for (int dy = -1; dy <= n; dy++) for (int dx = -1; dx <= n; dx++)
                {
                    int x = tx + dx, y = ty + dy;
                    if (!W.Path.TilePassable(x, y, robot) || !W.Path.TilePassable(x, y, false)) return false;
                    var c = W.Map.ClassAt(x, y); if (c == ZClass.Water || c == ZClass.DeepWater) return false;
                }
            foreach (var o in W.Objects)
                if (!o.Destroyed && o.KillAt < 0 && (o.IsUnit || o.Kind == ZKind.Building || o.Type == "rock") &&
                    o.LocX < (tx + n + 1) * 16 && (tx - 1) * 16 < o.LocX + o.WidthPix && o.LocY < (ty + n + 1) * 16 && (ty - 1) * 16 < o.LocY + o.HeightPix) return false;
            foreach (var t in taken) if (Mathf.Max(Mathf.Abs(t.x - tx), Mathf.Abs(t.y - ty)) < t.r + r) return false;
            return true;
        }
        (int x, int y) Spot(int tx, int ty, bool robot, List<(int x, int y, int r)> taken, int r)
        {
            for (int ring = 0; ring < 40; ring++)
                for (int y = ty - ring; y <= ty + ring; y++)
                    for (int x = tx - ring; x <= tx + ring; x++)
                    {
                        if (Mathf.Max(Mathf.Abs(x - tx), Mathf.Abs(y - ty)) != ring || !Free(x, y, robot, taken, r)) continue;
                        taken.Add((x, y, r)); return (x, y);
                    }
            return (tx, ty);
        }

        List<ZObject> Spawn(string group, int tx, int ty)
        {
            var ordered = new List<ZObject>(); var taken = new List<(int, int, int)>();
            int rr = group.StartsWith("mix") ? 2 : 3;
            void Sq(string type, int amount) { var (x, y) = Spot(tx, ty, true, taken, rr); var o = W.CreateRobotGroup(type, x * 16, y * 16, Team.Red, amount); g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m); ordered.Add(o); }
            void Ve(string type) { var (x, y) = Spot(tx, ty, false, taken, rr); var o = W.CreateObject("vehicle", type, x * 16, y * 16, Team.Red); g.SpawnUnitView(o); ordered.Add(o); }
            switch (group)
            {
                case "squad6": Sq("grunt", 6); break;
                case "grunts2x3": Sq("grunt", 3); Sq("grunt", 3); break;
                case "veh3": Ve("light"); Ve("medium"); Ve("jeep"); break;
                default:
                {
                    string[] rt = { "grunt", "psycho", "sniper" }; string[] vt = { "jeep", "light", "medium", "heavy", "apc", "missile_launcher" };
                    for (int i = 0; i < 3; i++) Sq(rt[i], 3);
                    for (int i = 0; i < 21; i++) Ve(vt[i % vt.Length]);
                    break;
                }
            }
            foreach (var o in ordered) o.PlayerHold = true;                                   // DV38: never auto-grabs the parked vehicles meanwhile
            return ordered;
        }

        sealed class Track
        {
            public ZObject o; public int squad;
            public readonly List<float> t = new List<float>();
            public readonly List<Vector2> drawn = new List<Vector2>(), sim = new List<Vector2>();
            public readonly List<float> yaw = new List<float>();
            public readonly List<bool> order = new List<bool>();
        }

        IEnumerator Stage(Site s, StageDef st, bool after)
        {
            string mode = after ? "after" : "before", grp = st.b != null ? $"{st.a}_vs_{st.b}" : st.a, name = $"{lv}_{st.kind}_{s.name}_{grp}_{mode}";
            Mark($"{name} clear");
            Clear(s, 10);
            yield return new WaitForSeconds(3f);
            ZObject wreck = null; GameObject marker = null;
            if (st.kind == "wreck" && WreckAt.TryGetValue(lv, out var wk))
            {                                                                                     // the jam's wreck: a sim map item + a dark block to see it
                wreck = W.AddTestWreck(wk.x, wk.y, wk.w, wk.h);
                if (wreck != null)
                {
                    marker = GameObject.CreatePrimitive(PrimitiveType.Cube); marker.name = "PathV3Check_wreck";
                    var c = ZGame.PxToWorld(wk.x * 16 + wk.w * 8, wk.y * 16 + wk.h * 8);
                    marker.transform.position = new Vector3(c.x, 0.35f, c.z); marker.transform.localScale = new Vector3(wk.w * 2f * 0.9f, 0.7f, wk.h * 2f * 0.9f);
                    var col = marker.GetComponent<Collider>(); if (col) Destroy(col);
                    var rd = marker.GetComponent<Renderer>(); if (rd) rd.material.color = new Color(0.16f, 0.13f, 0.11f);
                }
            }
            bool cross = st.kind == "cross" || st.kind == "wreck";
            var orders = new List<(List<ZObject> units, int gx, int gy)>();
            var ordered = Spawn(st.a, s.sx, s.sy);
            orders.Add((ordered, s.gx, s.gy));
            if (cross) { var ob = Spawn(st.b, s.gx, s.gy); orders.Add((ob, s.sx, s.sy)); }
            staged.Clear(); foreach (var (units, _, _) in orders) foreach (var o in units) { staged.Add(o); staged.AddRange(o.Minions); }
            var tracks = new List<Track>(); int sq = 0;
            foreach (var (units, _, _) in orders) foreach (var o in units) { tracks.Add(new Track { o = o, squad = sq }); foreach (var m in o.Minions) tracks.Add(new Track { o = m, squad = sq }); sq++; }
            var c0 = ZGame.PxToWorld((s.sx + s.gx) * 8 + 8, (s.sy + s.gy) * 8 + 8);
            float span = Mathf.Sqrt((s.sx - s.gx) * (s.sx - s.gx) + (s.sy - s.gy) * (s.sy - s.gy)) * 2f;
            if (cam) { cam.focus = new Vector3(c0.x, 0, c0.z); cam.SetHeightImmediate(Mathf.Clamp(span * 1.3f + 14f, 28f, 90f)); }
            yield return new WaitForSeconds(3f);
            int unst0 = W.Pathing != null ? W.Pathing.Unsticks : 0;
            foreach (var (units, gx, gy) in orders)
                foreach (var o in units) W.SendWaypoints(Team.Red, o, new List<ZWaypoint> { ZWaypoint.Move(gx * 16 + 8, gy * 16 + 8, true) });
            Mark($"{name} orders");
            double start = W.Time; float idleSince = -1, arrived = -1; int shot = 0; float nextShot = 1f;   // (sim seconds: the check runs at CheckSpeed x)
            bool back = st.kind == "backforth"; int legs = 0;                                    // backforth: goal, back to the start, goal again (orders 3 s apart while walking)
            float nextLeg = 3f;
            string frames = $"{Records}/milestones/path_v3"; System.IO.Directory.CreateDirectory(frames);
            float limit = s.name == "ruin" ? 240f : s.name == "canal" ? 180f : 120f, tail = 4f;
            int hard = 0, zonedFrames = 0, frames0 = 0; var onSoft = new HashSet<int>();
            while (W.Time - start < limit)
            {
                float now = (float)(W.Time - start);
                foreach (var tr in tracks)
                {
                    if (tr.o.Destroyed || !g.Views.TryGetValue(tr.o.Id, out var v) || !v) continue;
                    var p = v.transform.position; var sm = g.CenterWorldSmooth(tr.o);
                    tr.t.Add(now); tr.drawn.Add(new Vector2(p.x, p.z)); tr.sim.Add(new Vector2(sm.x, sm.z)); tr.yaw.Add(v.transform.eulerAngles.y); tr.order.Add(tr.o.Waypoints.Count > 0);
                    frames0++; if (tr.o.PathZoned) zonedFrames++;
                    if (W.Pathing != null && W.Pathing.SoftPassOn(tr.o)) onSoft.Add(tr.o.Id);
                    if (OnHardTile(tr.o, wreck)) hard++;
                }
                if (now >= nextShot && shot < 12) { ScreenCapture.CaptureScreenshot($"{frames}/{name}_{shot:00}.png"); shot++; nextShot += 6f; }
                if (back && legs < 2 && now >= nextLeg)
                {                                                                                 // orders back and forth: reverse while walking
                    legs++; nextLeg = now + (legs == 1 ? 4f : 1e9f);
                    int bx = legs == 1 ? s.sx : s.gx, by = legs == 1 ? s.sy : s.gy;
                    foreach (var (units, _, _) in orders) foreach (var o in units) W.SendWaypoints(Team.Red, o, new List<ZWaypoint> { ZWaypoint.Move(bx * 16 + 8, by * 16 + 8, true) });
                    Mark($"{name} order leg {legs + 1}");
                }
                bool busy = false; foreach (var o in staged) if (!o.Destroyed && o.Waypoints.Count > 0) busy = true;
                if (!busy) { if (idleSince < 0) { idleSince = now; arrived = now; } if (now - idleSince >= tail) break; } else idleSince = -1;
                yield return null;
            }
            Mark($"{name} end (all arrived {(arrived < 0 ? -1 : arrived):0.0} s sim)");
            int left = 0; foreach (var o in staged) if (!o.Destroyed && o.Waypoints.Count > 0) left++;
            int unst = (W.Pathing != null ? W.Pathing.Unsticks : 0) - unst0;
            int revs = 0, flips = 0, yawFlips = 0, micro = 0, microSim = 0; float stuck = 0, settle = 0, closest = 99f;
            var csv = new StringBuilder("t,id,type,role,drawn_x,drawn_z,sim_x,sim_z,yaw,order\n");
            foreach (var tr in tracks)
            {
                revs += Turns(tr, tr.drawn, 0.25f, 0.125f, 135f); flips += Turns(tr, tr.drawn, 0.1f, 0.0625f, 90f);
                yawFlips += YawFlips(tr);
                stuck = Mathf.Max(stuck, Stuck(tr)); settle = Mathf.Max(settle, Settle(tr));
                micro += Micro(tr, tr.drawn); microSim += Micro(tr, tr.sim);
                for (int k = 0; k < tr.t.Count; k++)
                    csv.AppendLine($"{tr.t[k]:0.000},{tr.o.Id},{tr.o.Type},{(tr.o.Kind == ZKind.Vehicle ? "vehicle" : tr.o.IsMinion ? "minion" : "leader")},{tr.drawn[k].x:0.000},{tr.drawn[k].y:0.000},{tr.sim[k].x:0.000},{tr.sim[k].y:0.000},{tr.yaw[k]:0.0},{(tr.order[k] ? 1 : 0)}");
            }
            for (int a = 0; a < tracks.Count; a++)
                for (int b = a + 1; b < tracks.Count; b++)
                {
                    if (tracks[a].squad != tracks[b].squad || !tracks[a].o.IsRobot || !tracks[b].o.IsRobot) continue;
                    int n = Mathf.Min(tracks[a].t.Count, tracks[b].t.Count);
                    for (int k = 0; k < n; k++) if (tracks[a].t[k] > 1.5f && (tracks[a].order[k] || tracks[b].order[k])) closest = Mathf.Min(closest, (tracks[a].drawn[k] - tracks[b].drawn[k]).magnitude);
                }
            System.IO.Directory.CreateDirectory($"{Records}/path_v3");
            System.IO.File.WriteAllText($"{Records}/path_v3/{name}.csv", csv.ToString());
            string bars = "-";
            float arr = arrived < 0 ? limit : arrived;
            if (!after) { bArrived = arr; bStuck = stuck; bLeft = left; bRevs = revs; }
            else
            {
                var bad = new List<string>();
                if (st.cls == "open")
                {
                    if (left > 0) bad.Add($"{left} orders left");
                    if (revs > bRevs) bad.Add($"{revs} reversals (before {bRevs})");
                    if (s.name == "building" && settle > 1.5f) bad.Add($"settle {settle:0.00} s");
                }
                else if (st.cls == "zone")
                {
                    if (arr > bArrived * 1.15f + 5f) bad.Add($"arrived {arr:0.0} s (before {bArrived:0.0} s)");
                    if (left > bLeft) bad.Add($"{left} orders left (before {bLeft})");
                    if (stuck > Mathf.Max(bStuck + 0.5f, 3f)) bad.Add($"stood {stuck:0.0} s with an order (before {bStuck:0.0} s)");
                }
                else
                {
                    if (stuck > 6.0f) bad.Add($"stood {stuck:0.0} s with an order (bar 6.0 s)");
                    if (unst <= 0) bad.Add("DV47 never fired");
                    if (left > 0) bad.Add($"{left} orders left");
                }
                if (hard > 0) bad.Add($"{hard} unit-frames on a hard blocked tile");
                runs++; if (bad.Count == 0) pass++;
                bars = bad.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", bad);
            }
            if (back) RegroupSteps(tracks, name);
            md.AppendLine($"| {st.kind} {s.name} | {grp} | {mode} | {tracks.Count} | {revs} | {flips} | {yawFlips} | {stuck:0.0} | {settle:0.00} | {micro} / {microSim} | {left} | {(closest < 98 ? closest.ToString("0.00") : "-")} | {(arrived < 0 ? "-" : arrived.ToString("0.0"))} | {unst} / {onSoft.Count} / {hard} | {(frames0 > 0 ? 100.0 * zonedFrames / frames0 : 0):0} | {bars} |");
            foreach (var o in staged) if (!o.Destroyed && W.Get(o.Id) == o && o.KillAt < 0) o.KillAt = W.Time;   // gone (not killed) before the next stage
            if (wreck != null && wreck.KillAt < 0) wreck.KillAt = W.Time;                        // (the map item goes, its tiles open again)
            if (marker) Destroy(marker);
            yield return new WaitForSeconds(3f);
        }

        /// <summary>G14 (3) regroup proof: per frame, each robot's drawn step vs the sim's own (interpolated) step in a +-2 frame window. A snap =
        /// a drawn step > 1.5 x the largest sim step around it and > 5 cm. Written to path_v3/[name]_steps.txt and the report.</summary>
        void RegroupSteps(List<Track> tracks, string name)
        {
            float maxDrawn = 0, maxSim = 0, worstRatio = 0; int snaps = 0, frames = 0;
            var log = new StringBuilder("t,id,drawn_step_m,sim_step_window_max_m\n");
            foreach (var tr in tracks)
            {
                if (!tr.o.IsRobot) continue;
                for (int k = 3; k < tr.t.Count - 2; k++)
                {
                    float d = (tr.drawn[k] - tr.drawn[k - 1]).magnitude, sm = 0;
                    for (int j = k - 2; j <= k + 2; j++) sm = Mathf.Max(sm, (tr.sim[j] - tr.sim[j - 1]).magnitude);
                    maxDrawn = Mathf.Max(maxDrawn, d); maxSim = Mathf.Max(maxSim, sm); frames++;
                    if (sm > 0.005f) worstRatio = Mathf.Max(worstRatio, d / sm);
                    bool snap = d > 1.5f * sm && d > 0.05f; if (snap) snaps++;
                    if (snap || d > 0.02f) log.AppendLine($"{tr.t[k]:0.000},{tr.o.Id},{d:0.0000},{sm:0.0000}{(snap ? ",SNAP" : "")}");
                }
            }
            System.IO.File.WriteAllText($"{Records}/path_v3/{name}_steps.txt", log.ToString());
            md.AppendLine($"\n> regroup steps {name}: {frames} robot-frames, max drawn step {maxDrawn:0.000} m / frame, max sim step {maxSim:0.000} m / frame, worst drawn / sim ratio {worstRatio:0.00}, snaps {snaps} ({(snaps == 0 && maxDrawn <= maxSim * 1.05f + 0.005f ? "PASS: no snap, drawn <= sim step" : "FAIL")})\n");
            Debug.Log($"[PathV3Check] regroup steps {name}: max drawn {maxDrawn:0.000} m, max sim {maxSim:0.000} m, ratio {worstRatio:0.00}, snaps {snaps}");
        }

        /// <summary>The unit's box (shrunk 2 px, like the stress suite) on a tile its own grid blocks that is not a soft blocker / the test wreck.</summary>
        bool OnHardTile(ZObject u, ZObject wreck)
        {
            if (u.PathInBuildingStage) return false;                                              // (Zod's scripted building moves drive through walls)
            int x0 = u.LocX + 2, y0 = u.LocY + 2, x1 = u.LocX + u.WidthPix - 3, y1 = u.LocY + u.HeightPix - 3;
            for (int ty = y0 >> 4; ty <= y1 >> 4; ty++)
                for (int tx = x0 >> 4; tx <= x1 >> 4; tx++)
                {
                    if (W.Path.TilePassable(tx, ty, u.IsRobot)) continue;
                    if (W.Pathing != null && W.Pathing.SoftTileAt(tx, ty)) continue;
                    if (wreck != null && tx >= wreck.LocX >> 4 && tx < (wreck.LocX + wreck.WidthPix) >> 4 && ty >= wreck.LocY >> 4 && ty < (wreck.LocY + wreck.HeightPix) >> 4) continue;
                    return true;
                }
            return false;
        }

        static int Turns(Track tr, List<Vector2> p, float win, float minLen, float deg)
        {
            int n = 0, k0 = 0; Vector2? prev = null; float still = 0;
            for (int k = 1; k < tr.t.Count; k++)
            {
                if (tr.t[k] - tr.t[k0] < win) continue;
                var d = p[k] - p[k0]; float dt = tr.t[k] - tr.t[k0]; k0 = k;
                if (d.magnitude < minLen) { still += dt; if (still >= 1f) prev = null; continue; }
                still = 0;
                if (prev.HasValue && Vector2.Angle(prev.Value, d) > deg) n++;
                prev = d;
            }
            return n;
        }
        static int YawFlips(Track tr)
        {
            int n = 0, k0 = 0;
            for (int k = 1; k < tr.t.Count; k++)
            {
                if (tr.t[k] - tr.t[k0] < 0.1f) continue;
                if (Mathf.Abs(Mathf.DeltaAngle(tr.yaw[k0], tr.yaw[k])) > 90f) n++;
                k0 = k;
            }
            return n;
        }
        /// <summary>Longest time with an order and under 0.25 m (2 px) of sim movement (waits included: a jam is a jam).</summary>
        static float Stuck(Track tr)
        {
            float best = 0; int a = 0;
            for (int k = 0; k < tr.t.Count; k++)
            {
                if (!tr.order[k] || (tr.sim[k] - tr.sim[a]).magnitude >= 0.25f) { a = k; continue; }
                best = Mathf.Max(best, tr.t[k] - tr.t[a]);
            }
            return best;
        }
        static float Settle(Track tr)
        {
            int n = tr.t.Count; if (n < 2) return 0;
            var fin = tr.sim[n - 1]; int last = 0, near = -1;
            for (int k = 1; k < n; k++) if ((tr.sim[k] - tr.sim[k - 1]).sqrMagnitude > 1e-10f) last = k;
            for (int k = 0; k < n; k++) if ((tr.sim[k] - fin).magnitude <= 1f) { near = k; break; }
            return near < 0 ? 0 : Mathf.Max(0, tr.t[last] - tr.t[near]);
        }
        static int Micro(Track tr, List<Vector2> p)
        {
            int end = -1, micro = 0;
            for (int k = 1; k < tr.t.Count; k++) if (tr.order[k - 1] && !tr.order[k]) { end = k; break; }
            if (end < 0) return 0;
            int i = end;
            while (i < tr.t.Count - 1)
            {
                while (i + 1 < tr.t.Count && (p[i + 1] - p[i]).sqrMagnitude <= 1e-8f) i++;
                if (i + 1 >= tr.t.Count) break;
                int s0 = i; float mx = 0, restT = 0; i++;
                while (i < tr.t.Count && restT < 0.2f)
                {
                    if ((p[i] - p[i - 1]).sqrMagnitude <= 1e-8f) restT += tr.t[i] - tr.t[i - 1]; else restT = 0;
                    mx = Mathf.Max(mx, (p[i] - p[s0]).magnitude); i++;
                }
                if (mx < 0.1f) micro++;
            }
            return micro;
        }
    }
}
#endif
