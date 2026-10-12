using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Fort gate, Play-mode proof (the gate is view only: FactoryView). Both forts of the running level at the same time, one case
    /// after the other per fort:
    ///   exits: a squad of each robot type and one long vehicle, made by the fort's own production (the build time left is set to
    ///   1.6 s, so the gate gets its normal 1 s lead); a rally point sends each new unit straight out along the road;
    ///   entries: an enemy squad and an enemy jeep on an ENTER_FORT order; each member is taken off the map just before it reaches
    ///   the point where the fort would go up, so the fort stands for the next case.
    /// The CPU commanders are removed, every unit and gun near a fort is taken off the map, the fort gun and the roof defender are
    /// held off for the session. Counted once per drawn frame against the plane of the gate leaves, with the DRAWN bodies (centre
    /// +- half a body length along the gate axis): the gate's opening (0 shut .. 1 open) when the first body reaches the leaves,
    /// when the last body is clear of them, and the smallest opening in between. Call while the level plays:
    /// ZFortGateProof.Run(logFile, shotDir, shotPrefix). Status / Lines show the progress.
    /// </summary>
    public static class ZFortGateProof
    {
        public static string Status = "idle";
        public static readonly List<string> Lines = new List<string>();
        public static string[] RobotTypes = { "grunt", "psycho", "sniper", "tough", "pyro", "laser" };
        public static string LongVehicle = "missile_launcher";
        public static float Speed = 2f, CamHeight = 70f;
        /// <summary>Pictures of the squads in the gate of the fort the camera looks at. Writing a picture stalls the game for about a
        /// quarter of a second and the next frame then jumps up to 0.4 sim-seconds, so a counted run is made WITHOUT them.</summary>
        public static bool SquadShots = false, RoadShots = true;
        /// <summary>Only this fort type (fort_front / fort_back); null = both.</summary>
        public static string OnlyFortType = null;
        public static string[] EntryTypes = { "grunt", "jeep" };
        public const float RobotHalf = 0.45f, VehicleHalf = 2.6f, JeepHalf = 2.3f;
        const int StagePx = 18 * 16, RallyTiles = 14;

        sealed class FortRun
        {
            public ZObject fort; public FactoryView fv; public Vector3 leaf, dir; public string name; public int cx, cy, ex, ey;
            public IEnumerator<object> it; public bool done; public List<ZWaypoint> rallyWas;
        }

        static readonly List<FortRun> runs = new List<FortRun>();
        static bool fortGunWas, defenderWas; static float speedWas; static int lastFrame = -1;
        static string logFile, shotDir, shotPrefix; static FortRun camFort; static IEnumerator<object> lead;

        public static void Run(string log, string shots, string prefix)
        {
            if (runs.Count > 0 || lead != null) { Status = "busy"; return; }
            if (!Application.isPlaying || ZGame.I == null || ZGame.I.World == null || FactoryViews.I == null) { Status = "not playing"; return; }
            logFile = log; shotDir = shots; shotPrefix = prefix; Lines.Clear();
            lead = Lead();
            EditorApplication.update += Step;
        }

        public static void Abort() { Finish("aborted"); }

        static void Step()
        {
            if (!Application.isPlaying) { Finish("stopped: not playing"); return; }
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            try
            {
                if (lead != null) { if (!lead.MoveNext()) lead = null; return; }
                bool any = false;
                foreach (var r in runs) if (!r.done) { if (!r.it.MoveNext()) r.done = true; else any = true; }
                if (!any) Finish("done");
            }
            catch (Exception e) { Finish("EXCEPTION " + e); }
        }

        static void Finish(string status)
        {
            EditorApplication.update -= Step;
            if (runs.Count > 0 || lead != null)
            {
                ZWorld.FortGunDeviation = fortGunWas; ZWorld.FortDefenderDeviation = defenderWas;
                foreach (var r in runs) if (r.fort != null && r.rallyWas != null) { r.fort.RallyPoints.Clear(); r.fort.RallyPoints.AddRange(r.rallyWas); }
                if (ZGame.I) ZGame.I.gameSpeed = speedWas;
                try { if (logFile != null) File.WriteAllText(logFile, string.Join("\n", Lines) + "\n"); } catch (Exception) { }
            }
            runs.Clear(); lead = null; Status = status;
        }

        static float Along(FortRun f, Vector3 world) { var lp = f.fv.transform.InverseTransformPoint(world) - f.leaf; lp.y = 0f; return Vector3.Dot(lp, f.dir); }

        static bool Drawn(ZObject u, out Vector3 p)
        {
            p = default;
            if (u == null || u.Destroyed || u.KillAt >= 0 || !ZGame.I.Views.TryGetValue(u.Id, out var v) || !v || !v.isActiveAndEnabled || v.Dying) return false;
            p = v.transform.position; return true;
        }

        static IEnumerator<object> Wait(ZWorld W, double seconds) { double until = W.Time + seconds; while (W.Time < until) yield return null; }

        static IEnumerator<object> Lead()
        {
            var game = ZGame.I; var W = game.World;
            Status = "setup";
            W.Bots.Clear();
            fortGunWas = ZWorld.FortGunDeviation; defenderWas = ZWorld.FortDefenderDeviation; speedWas = game.gameSpeed;
            ZWorld.FortGunDeviation = false; ZWorld.FortDefenderDeviation = false;
            var dn = UnityEngine.Object.FindFirstObjectByType<DayNight>();
            if (dn) { dn.timeOfDay = 0.03f; dn.running = false; }
            foreach (var o in W.Objects)
            {
                if (o.Kind != ZKind.Building || !o.IsFort || o.Destroyed || (OnlyFortType != null && o.Type != OnlyFortType)) continue;
                var fv = FactoryViews.I.For(o.Id); if (!fv) continue;
                var r = new FortRun { fort = o, fv = fv, name = game.level + " " + o.Owner.ToString().ToLowerInvariant() + " fort" };
                o.GetCreationPoint(out r.cx, out r.cy); o.GetCreationMovePoint(out r.ex, out r.ey);
                var a = fv.transform.InverseTransformPoint(ZGame.PxToWorld(r.cx, r.cy)); var e = fv.transform.InverseTransformPoint(ZGame.PxToWorld(r.ex, r.ey));
                var d = e - a; d.y = 0f; r.dir = d.normalized;
                r.leaf = fv.Gate ? fv.transform.InverseTransformPoint(fv.Building.TransformPoint(fv.Gate.DoorPoint)) : new Vector3((fv.DoorMin.x + fv.DoorMax.x) * 0.5f, 0f, fv.DoorMin.z);
                r.rallyWas = new List<ZWaypoint>(o.RallyPoints);
                int ux = Math.Sign(r.ex - r.cx), uy = Math.Sign(r.ey - r.cy);
                o.RallyPoints.Clear(); o.RallyPoints.Add(ZWaypoint.Move(r.ex + ux * RallyTiles * 16, r.ey + uy * RallyTiles * 16));
                runs.Add(r);
                Lines.Add($"{r.name} #{o.Id} ({o.Type}): the door plane lies {Along(r, fv.transform.TransformPoint(new Vector3((fv.DoorMin.x + fv.DoorMax.x) * 0.5f, 0f, fv.DoorMin.z))):0.00} m outside the gate leaves, creation point {Along(r, ZGame.PxToWorld(r.cx, r.cy)):0.00} m, exit point {Along(r, ZGame.PxToWorld(r.ex, r.ey)):0.00} m from the leaves (+ = outside); gate kit {(fv.Gate ? "yes" : "no")}");
            }
            // the stage: nobody and no gun near a fort
            int removed = 0;
            foreach (var o in W.Objects)
            {
                if (!(o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon) || o.KillAt >= 0 || o.IsFortDefender) continue;
                foreach (var r in runs)
                    if (Math.Abs(o.CenterX - r.fort.CenterX) < StagePx && Math.Abs(o.CenterY - r.fort.CenterY) < StagePx + 8 * 16) { o.KillAt = W.Time; removed++; break; }
            }
            // one squad per team stays on the map far from both forts (a team without any unit ends the match, and after the end nothing is built)
            (int x, int y) keepRed = FarTile(W, null), keepBlue = FarTile(W, keepRed);
            foreach (var (team, at) in new[] { (Team.Red, keepRed), (Team.Blue, keepBlue) })
            {
                var k = W.CreateRobotGroup("grunt", at.x * 16, at.y * 16, team);
                W.SendHold(team, k);
                var all = new List<ZObject> { k }; all.AddRange(k.Minions);
                foreach (var m in all) { m.MaxHealth *= 300; m.Health = m.MaxHealth; game.SpawnUnitView(m); }
            }
            Lines.Add($"stage: {removed} units and guns near the forts taken off the map, one squad per team parked far off (tiles {keepRed.x},{keepRed.y} and {keepBlue.x},{keepBlue.y}), CPU commanders removed, fort gun and roof defender held off, game speed {Speed}, time of day held at noon");
            var w0 = Wait(W, 1.0); while (w0.MoveNext()) yield return null;
            var cam = UnityEngine.Object.FindFirstObjectByType<RtsCamera>();
            // the road in front of each gate, normal camera
            if (cam && shotDir != null && RoadShots)
                foreach (var r in runs)
                {
                    var gp = r.fv.transform.TransformPoint(r.leaf + r.dir * 7f);
                    cam.follow = null; cam.focus = new Vector3(gp.x, 0f, gp.z); cam.SetHeightImmediate(CamHeight);
                    for (int i = 0; i < 30; i++) yield return null;
                    ScreenCapture.CaptureScreenshot($"{shotDir}/{shotPrefix}_road_{r.name.Replace(' ', '_')}_normal70.png");
                    for (int i = 0; i < 10; i++) yield return null;
                }
            camFort = runs.Find(x => x.fort.Type == "fort_front") ?? (runs.Count > 0 ? runs[0] : null);
            if (cam && camFort != null) { var gp = camFort.fv.transform.TransformPoint(camFort.leaf + camFort.dir * 5f); cam.focus = new Vector3(gp.x, 0f, gp.z); cam.SetHeightImmediate(CamHeight); }
            game.gameSpeed = Speed;
            foreach (var r in runs) r.it = FortRoutine(r);
            Lines.Add("| fort | case | gate at the build | first body at the leaves: s after the build, gate | last body clear of the leaves: s, gate | smallest opening in between | gate starts to close s after the last body is clear | shut s after | 16 m of road beyond the ramp, s | note |");
            Lines.Add("|---|---|---|---|---|---|---|---|---|---|");
            Status = "running";
        }

        static IEnumerator<object> FortRoutine(FortRun r)
        {
            var game = ZGame.I; var W = game.World;
            var types = new List<string>(RobotTypes) { LongVehicle };
            foreach (var type in types)
            {
                Status = $"{r.name}: exit {type}";
                int maxId = 0; foreach (var o in W.Objects) if (o.Id > maxId) maxId = o.Id;
                r.fort.StopBuildingProduction();
                r.fort.CurUnit = type; r.fort.Building = true; r.fort.BInit = W.Time; r.fort.BFinal = W.Time + 1.6;
                var group = new List<ZObject>(); double tOrder = W.Time;
                while (group.Count == 0 && W.Time - tOrder < 6.0)
                {
                    foreach (var o in W.Objects)
                        if (o.Id > maxId && o.Owner == r.fort.Owner && (o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle) && !o.IsFortDefender
                            && Math.Abs(o.CenterX - r.cx) < 64 && Math.Abs(o.CenterY - r.cy) < 64) group.Add(o);
                    if (group.Count == 0) yield return null;
                }
                r.fort.StopBuildingProduction();
                if (group.Count == 0) { Lines.Add($"| {r.name} | {type} out | - | - | - | - | - | - | - | NOT BUILT within 6 s |"); continue; }
                double tBuilt = W.Time; float openBuilt = r.fv.Open;
                bool vehicle = group[0].Kind == ZKind.Vehicle; float half = vehicle ? VehicleHalf : RobotHalf;
                float openFirst = -1f, openLast = -1f, minOpen = 2f; double tFirst = -1, tLast = -1, tCloseStart = -1, tShut = -1, tA = -1, tB = -1;
                bool shot1 = false, shot2 = false; string note = ""; double prevT = W.Time, maxStep = 0;
                while (W.Time - tBuilt < 50.0)
                {
                    float maxFront = float.NegativeInfinity, minTail = float.PositiveInfinity, minA = float.PositiveInfinity; int alive = 0, centresIn = 0;
                    foreach (var u in group)
                    {
                        if (!Drawn(u, out var p)) continue;
                        float a = Along(r, p); alive++;
                        if (a + half > maxFront) maxFront = a + half;
                        if (a - half < minTail) minTail = a - half;
                        if (a < minA) minA = a;
                        if (a < 0f) centresIn++;
                    }
                    if (alive == 0) { if (W.Time - tBuilt > 3.0) { note += "no drawn member; "; break; } yield return null; continue; }
                    float open = r.fv.Open;
                    if (tLast < 0 && W.Time - prevT > maxStep) maxStep = W.Time - prevT;
                    prevT = W.Time;
                    if (tFirst < 0 && maxFront >= 0f) { tFirst = W.Time; openFirst = open; }
                    if (tFirst >= 0 && tLast < 0 && open < minOpen) minOpen = open;
                    if (tFirst >= 0 && tLast < 0 && minTail >= 0f) { tLast = W.Time; openLast = open; }
                    if (tLast >= 0 && tCloseStart < 0 && open < 0.999f) tCloseStart = W.Time;
                    if (tLast >= 0 && tShut < 0 && open <= 0.001f) tShut = W.Time;
                    if (tA < 0 && minA >= 4f) tA = W.Time;
                    if (tB < 0 && minA >= 20f) tB = W.Time;
                    if (SquadShots && shotDir != null && r == camFort)
                    {   // the squad in the gateway: the first member's centre on the leaf plane; then the last member's
                        if (!shot1 && maxFront - half >= 0f) { shot1 = true; ScreenCapture.CaptureScreenshot($"{shotDir}/{shotPrefix}_{r.name.Replace(' ', '_')}_{type}_1_first_in_the_gate_normal70.png"); }
                        else if (shot1 && !shot2 && centresIn <= 1 && minTail + half >= -0.3f) { shot2 = true; ScreenCapture.CaptureScreenshot($"{shotDir}/{shotPrefix}_{r.name.Replace(' ', '_')}_{type}_2_last_in_the_gate_normal70.png"); }
                    }
                    if (tB >= 0 && tShut >= 0) break;
                    if (tShut >= 0 && tLast >= 0 && W.Time - tLast > 18.0) break;                 // the rally point may lie nearer than 20 m on this map
                    yield return null;
                }
                if (tFirst < 0) note += "no body reached the leaves; ";
                if (tLast < 0) note += "the last body never cleared the leaves; ";
                if (tB < 0) note += "did not reach 20 m out; ";
                note += $"largest step between two samples {maxStep:0.00} s";
                Lines.Add($"| {r.name} | {type} out ({group.Count}) | {openBuilt:0.00} | {(tFirst < 0 ? "-" : (tFirst - tBuilt).ToString("0.00"))}, {Fmt(openFirst)} | {(tLast < 0 ? "-" : (tLast - tBuilt).ToString("0.00"))}, {Fmt(openLast)} | {(minOpen > 1.5f ? "-" : minOpen.ToString("0.00"))} | {(tCloseStart < 0 || tLast < 0 ? "-" : (tCloseStart - tLast).ToString("0.00"))} | {(tShut < 0 || tLast < 0 ? "-" : (tShut - tLast).ToString("0.00"))} | {(tA < 0 || tB < 0 ? "-" : (tB - tA).ToString("0.00"))} | {note} |");
                foreach (var u in group) if (u.KillAt < 0) u.KillAt = W.Time;
                var w1 = Wait(W, 1.5); while (w1.MoveNext()) yield return null;
            }
            // entries
            var enemy = r.fort.Owner == Team.Red ? Team.Blue : Team.Red;
            int ux = Math.Sign(r.ex - r.cx), uy = Math.Sign(r.ey - r.cy);
            foreach (var type in EntryTypes)
            {
                Status = $"{r.name}: enter {type}";
                while (r.fv.Open > 0.001f) yield return null;                                   // from a shut gate
                int sx = r.ex + ux * 6 * 16, sy = r.ey + uy * 6 * 16;
                ZObject lead0 = ZBuildLists.IsRobot(type) ? W.CreateRobotGroup(type, sx - 8, sy - 8, enemy) : W.CreateObject("vehicle", type, sx - 16, sy - 16, enemy);
                var group = new List<ZObject> { lead0 }; group.AddRange(lead0.Minions);
                foreach (var u in group) { u.MaxHealth *= 300; u.Health = u.MaxHealth; game.SpawnUnitView(u); }
                bool ok = W.SendWaypoints(enemy, lead0, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.EnterFort, refId = r.fort.Id, x = r.ex, y = r.ey } });
                bool vehicle = lead0.Kind == ZKind.Vehicle; float half = vehicle ? JeepHalf : RobotHalf; int killPx = vehicle ? 10 : 4;
                double t0 = W.Time; float openRamp = -1f, openLeaf = -1f, openDeep = -1f, minOpen = 2f, deepest = float.PositiveInfinity; double tRamp = -1, tLeaf = -1, tDeep = -1;
                int arrived = 0; string note = ok ? "" : "ORDER REFUSED; ";
                while (W.Time - t0 < 45.0 && arrived < group.Count && !r.fort.Destroyed)
                {
                    float minFront = float.PositiveInfinity; int alive = 0; float open = r.fv.Open;
                    foreach (var u in group)
                    {
                        if (!Drawn(u, out var p)) continue;
                        alive++; float a = Along(r, p);
                        if (a - half < minFront) minFront = a - half;
                        long dx = u.CenterX - r.cx, dy = u.CenterY - r.cy;
                        if (dx * dx + dy * dy <= (long)killPx * killPx)
                        {   // one step before the fort would go up: this member is in as deep as it gets
                            arrived++; openDeep = open; tDeep = W.Time; if (a < deepest) deepest = a;
                            u.KillAt = W.Time;
                        }
                    }
                    if (alive == 0) { yield return null; continue; }
                    if (tRamp < 0 && minFront <= 3f) { tRamp = W.Time; openRamp = open; }
                    if (tRamp >= 0 && open < minOpen) minOpen = open;
                    if (tLeaf < 0 && minFront <= 0f) { tLeaf = W.Time; openLeaf = open; }
                    yield return null;
                }
                if (r.fort.Destroyed) note += "THE FORT WENT UP; ";
                if (arrived < group.Count) note += $"only {arrived} of {group.Count} got in within 45 s; ";
                foreach (var u in group) if (u.KillAt < 0) u.KillAt = W.Time;
                Lines.Add($"| {r.name} | {type} IN ({group.Count}) | (shut) | nose at the foot of the ramp (3 m before the leaves): {(tRamp < 0 ? "-" : (tRamp - t0).ToString("0.0"))} s, {Fmt(openRamp)}; nose at the leaves: {(tLeaf < 0 ? "not reached" : Fmt(openLeaf))} | last member at its deepest point ({(deepest > 1e6f ? "-" : deepest.ToString("0.0"))} m from the leaves): {Fmt(openDeep)} | {(minOpen > 1.5f ? "-" : minOpen.ToString("0.00"))} | - | - | - | {note} |");
                var w2 = Wait(W, 2.5); while (w2.MoveNext()) yield return null;
            }
            Status = $"{r.name}: finished";
        }

        static string Fmt(float open) => open < 0f ? "-" : open.ToString("0.00");

        /// <summary>The robot-passable tile farthest from every fort (and from `other`, when given).</summary>
        static (int x, int y) FarTile(ZWorld W, (int x, int y)? other)
        {
            (int x, int y) best = (2, 2); int bestD = -1;
            for (int y = 3; y < 400; y += 3)
                for (int x = 3; x < 400; x += 3)
                {
                    if (!W.Path.TileOnMap(x, y) || !W.Path.TilePassable(x, y, true) || !W.Path.TilePassable(x + 1, y, true) || !W.Path.TilePassable(x, y + 1, true)) continue;
                    int d = int.MaxValue;
                    foreach (var r in runs) d = Math.Min(d, Math.Max(Math.Abs(x - r.fort.CenterX / 16), Math.Abs(y - r.fort.CenterY / 16)));
                    if (other.HasValue) d = Math.Min(d, Math.Max(Math.Abs(x - other.Value.x), Math.Abs(y - other.Value.y)));
                    if (d > bestD) { bestD = d; best = (x, y); }
                }
            return best;
        }
    }
}
