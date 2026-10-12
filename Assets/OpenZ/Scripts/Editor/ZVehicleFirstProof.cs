using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Vehicles first, Play-mode proof (Sim/ZYield.cs). Runs staged drives in the running level, one after the other: a crewed vehicle
    /// of the red team is ordered from a start point to a goal, alone or through a block of its own squads (on HOLD POSITION), with or
    /// without enemy squads (on HOLD POSITION, health raised so they last the whole drive) that fire at the vehicle whenever it is in their range. Every other unit near
    /// the stage is taken off the map, the CPU commander is removed for the session and one squad per team is kept far away (a team
    /// without units would end the match). Counted per drive, once per frame:
    ///   time to arrive; the longest stop (sim point, and the drawn hull); the furthest the sim point and the drawn hull left the straight
    ///   line; heading changes over 20 deg; frames the gate held / queued / side-stepped it behind a robot of its own team; the view's
    ///   spacing offset on the hull; shells it fired; health it lost; robots asked aside, the fastest step-aside (x move speed), robots
    ///   the hull drove through; "[MechWalk] slide" console lines for this unit.
    /// Call from the editor while the level plays: ZVehicleFirstProof.Run(logFile, cases). Status / Lines show the progress.
    /// </summary>
    public static class ZVehicleFirstProof
    {
        public sealed class Case
        {
            public string name = "case", vehicle = "medium", crowdType = "grunt", enemyType = "grunt";
            public int sx, sy, gx, gy;                                     // start and goal of the vehicle's centre (px)
            public int cx, cy, cols, rows, pitchX = 28, pitchY = 24;       // crowd: squad anchors on a grid from (cx, cy) px; cols 0 = no crowd
            public int[] enemies;                                          // enemy squads: x, y pairs (px); null = none
            public bool ruleOn = true;
            public string shot; public double shotAt = -1;                 // screenshot file and its time (s after the order)
            public float camHeight = 70f; public int camDx, camDy = 56;      // camera height (m) and its focus against the path's middle (px)
        }

        /// <summary>One squad per team that stays on the map, far from the stages (px): a team without any unit ends the match, and
        /// after the end every order is dropped.</summary>
        public static int KeepRedX = 328, KeepRedY = 648, KeepBlueX = 968, KeepBlueY = 168;
        public const int StagePx = 256;                                    // units nearer than this to the path's middle (both axes) leave the map
        static readonly HashSet<ZObject> keep = new HashSet<ZObject>();
        public static string Status = "idle";
        public static readonly List<string> Lines = new List<string>();
        static IEnumerator<object> routine;
        static readonly List<string> slideLines = new List<string>();
        static int watchId = -1;

        public const string Header = "| case | vehicle | rule | arrived s | longest stop s (sim / drawn) | off the line px (sim / drawn) | heading changes | held by own robots (frames) | view offset max m | shells fired | health lost | asked aside | fastest step-aside | driven through | slide lines (drag) | note |";

        public static void Run(string logFile, params Case[] cases)
        {
            if (routine != null) { Status = "busy"; return; }
            Lines.Clear();
            routine = Routine(logFile, cases);
            EditorApplication.update += Step;
        }

        public static void Abort() { Finish("aborted"); }

        static void Step()
        {
            if (routine == null) { EditorApplication.update -= Step; return; }
            if (!Application.isPlaying) { Finish("stopped: not playing"); return; }
            try { if (!routine.MoveNext()) Finish("done"); }
            catch (Exception e) { Finish("EXCEPTION " + e); }
        }

        static void Finish(string status)
        {
            EditorApplication.update -= Step; routine = null; Status = status;
            Application.logMessageReceived -= OnLog;
            ZPathing.VehicleFirst = true;
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (watchId >= 0 && msg != null && msg.StartsWith("[MechWalk] slide") && msg.Contains("unit " + watchId + " ")) slideLines.Add(msg);
        }

        static double PX(ZObject o) => o.LocX + o.XOver + o.WidthPix * 0.5;
        static double PY(ZObject o) => o.LocY + o.YOver + o.HeightPix * 0.5;

        static IEnumerator<object> Routine(string logFile, Case[] cases)
        {
            var game = ZGame.I; var W = game.World;
            var cam = UnityEngine.Object.FindFirstObjectByType<RtsCamera>();
            var sepField = typeof(UnitView).GetField("sepOffset", BindingFlags.NonPublic | BindingFlags.Instance);
            Application.logMessageReceived += OnLog;
            W.Bots.Clear();
            keep.Clear();
            foreach (var k in new[] { W.CreateRobotGroup("grunt", KeepRedX, KeepRedY, Team.Red), W.CreateRobotGroup("grunt", KeepBlueX, KeepBlueY, Team.Blue) })
            {
                W.SendHold(k.Owner, k);
                keep.Add(k); foreach (var m in k.Minions) keep.Add(m);
            }
            foreach (var k in keep) { k.MaxHealth *= 300; k.Health = k.MaxHealth; game.SpawnUnitView(k); }
            var sb = new StringBuilder();
            sb.AppendLine(Header);
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            File.WriteAllText(logFile, sb.ToString());
            for (int ci = 0; ci < cases.Length; ci++)
            {
                var c = cases[ci];
                Status = $"case {ci + 1}/{cases.Length} {c.name}: setup";
                ZPathing.VehicleFirst = c.ruleOn;
                var mine = new HashSet<ZObject>();
                double mx = (c.sx + c.gx) * 0.5, my = (c.sy + c.gy) * 0.5;
                // the stage: nobody else within StagePx
                foreach (var o in W.Objects)
                    if ((o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon) && o.KillAt < 0 && !o.IsFortDefender && !keep.Contains(o) &&
                        Math.Abs(o.CenterX - mx) < StagePx && Math.Abs(o.CenterY - my) < StagePx) o.KillAt = W.Time;
                double until = W.Time + 0.5; while (W.Time < until) yield return null;

                var v = W.CreateObject("vehicle", c.vehicle, c.sx - 16, c.sy - 16, Team.Red);
                game.SpawnUnitView(v); mine.Add(v); W.SendHold(Team.Red, v);
                var crowd = new List<ZObject>(); var leaders = new List<ZObject>();
                for (int j = 0; j < c.rows; j++)
                    for (int i = 0; i < c.cols; i++)
                    {
                        var l = W.CreateRobotGroup(c.crowdType, c.cx + i * c.pitchX - 8, c.cy + j * c.pitchY - 8, Team.Red);
                        leaders.Add(l); crowd.Add(l); crowd.AddRange(l.Minions);
                        W.SendHold(Team.Red, l);
                    }
                foreach (var r in crowd) { game.SpawnUnitView(r); mine.Add(r); }
                var enemies = new List<ZObject>(); var foes = new List<ZObject>();
                for (int e = 0; c.enemies != null && e + 1 < c.enemies.Length; e += 2)
                {
                    var enemy = W.CreateRobotGroup(c.enemyType, c.enemies[e] - 8, c.enemies[e + 1] - 8, Team.Blue);
                    enemies.Add(enemy); foes.Add(enemy); foes.AddRange(enemy.Minions);
                    W.SendHold(Team.Blue, enemy);
                }
                foreach (var f in foes) { f.MaxHealth *= 300; f.Health = f.MaxHealth; game.SpawnUnitView(f); mine.Add(f); }
                if (cam) { cam.follow = null; var fp = ZGame.PxToWorld(mx + c.camDx, my + c.camDy); cam.focus = new Vector3(fp.x, 0, fp.z); cam.SetHeightImmediate(c.camHeight); }
                until = W.Time + 5.0;
                while (W.Time < until) { Sweep(W, mine, mx, my); yield return null; }
                int attackOrders = 0;
                until = W.Time + 1.0;
                while (W.Time < until) { Sweep(W, mine, mx, my); attackOrders += Aim(foes, v); yield return null; }

                // the order
                slideLines.Clear(); watchId = v.Id;
                int yields0 = ZPathing.YieldsAsked, health0 = v.Health, crowd0 = crowd.Count;
                bool sent = W.SendWaypoints(Team.Red, v, new[] { ZWaypoint.Move(c.gx, c.gy) });
                double t0 = W.Time, lastT = W.Time, lx = PX(v), ly = PY(v);
                double dirx = c.gx - c.sx, diry = c.gy - c.sy, dl = Math.Sqrt(dirx * dirx + diry * diry); dirx /= dl; diry /= dl;
                game.Views.TryGetValue(v.Id, out var view);
                Vector3 lastDrawn = view ? view.transform.position : Vector3.zero;
                double stop = 0, maxStop = 0, dStop = 0, maxDStop = 0, offSim = 0, offDrawn = 0, maxSep = 0, lastAng = double.NaN, fastest = 0, arrived = -1;
                int flips = 0, heldFrames = 0, enemyOnVehicle = 0, frames = 0; bool started = false, shotDone = false;
                var shells = new HashSet<ZMissile>(); var through = new HashSet<int>(); var inserted = new List<string>();
                string wpsBefore = Wps(v), early = ""; double endDist = -1; int arr0 = v.PathState.Arrivals, give0 = v.PathState.GiveUps;
                Status = $"case {ci + 1}/{cases.Length} {c.name}: driving";
                while (W.Time - t0 < 120)
                {
                    yield return null;
                    Sweep(W, mine, mx, my);
                    double now = W.Time, dt = now - lastT; frames++;
                    bool mineLeft = false; foreach (var wp in v.Waypoints) if (wp.playerGiven || (wp.mode == WpMode.Move && wp.refId == -1)) mineLeft = true;
                    if (!mineLeft)
                    {                                                                         // its own order is over
                        arrived = now - t0; endDist = Math.Sqrt((PX(v) - c.gx) * (PX(v) - c.gx) + (PY(v) - c.gy) * (PY(v) - c.gy));
                        if (endDist > 4) early = $"ORDER ENDED {endDist:0} px SHORT (list before: {wpsBefore}; now: {Wps(v)}; arrivals +{v.PathState.Arrivals - arr0}, give-ups +{v.PathState.GiveUps - give0}, {v.PathState.LastOrderEnd}) ";
                        break;
                    }
                    wpsBefore = Wps(v);
                    if (!v.Waypoints[0].playerGiven) { var w0 = v.Waypoints[0]; string k = $"{w0.mode} ref {w0.refId}"; if (!inserted.Contains(k)) inserted.Add(k); }
                    if (v.Destroyed || v.Owner != Team.Red) break;
                    foreach (var m in W.PendingMissiles) if (m.attackerId == v.Id) shells.Add(m);
                    foreach (var m in W.QueuedMissiles) if (m.attackerId == v.Id) shells.Add(m);
                    attackOrders += Aim(foes, v);
                    foreach (var f in foes) if (f.AttackObject == v) { enemyOnVehicle++; break; }
                    double x = PX(v), y = PY(v);
                    if (dt > 1e-6)
                    {
                        double sp = Math.Sqrt((x - lx) * (x - lx) + (y - ly) * (y - ly)) / dt, nominal = Math.Max(1.0, v.RealMoveSpeed);
                        if (sp > 0.5 * nominal) started = true;
                        if (started) { if (sp < 0.25 * nominal) { stop += dt; maxStop = Math.Max(maxStop, stop); } else stop = 0; }
                        if (view)
                        {
                            var dp = view.transform.position; double dsp = new Vector2(dp.x - lastDrawn.x, dp.z - lastDrawn.z).magnitude * 8.0 / dt;
                            if (started) { if (dsp < 0.25 * nominal) { dStop += dt; maxDStop = Math.Max(maxDStop, dStop); } else dStop = 0; }
                            lastDrawn = dp;
                            offDrawn = Math.Max(offDrawn, Math.Abs((dp.x * 8.0 - c.sx) * diry - (-dp.z * 8.0 - c.sy) * dirx));
                            if (sepField != null) maxSep = Math.Max(maxSep, ((Vector3)sepField.GetValue(view)).magnitude);
                        }
                        lx = x; ly = y; lastT = now;
                    }
                    offSim = Math.Max(offSim, Math.Abs((x - c.sx) * diry - (y - c.sy) * dirx));
                    if (v.IsMoving)
                    {
                        double ang = Math.Atan2(v.Dy, v.Dx) * 180 / Math.PI;
                        if (!double.IsNaN(lastAng)) { double d = Math.Abs(ang - lastAng); if (d > 180) d = 360 - d; if (d > 20) flips++; }
                        lastAng = ang;
                    }
                    var ps = v.PathState;
                    if ((ps.wait == ZPathWait.Held || ps.wait == ZPathWait.Queue || ps.wait == ZPathWait.Sidestep) && ps.heldBy != null && ps.heldBy.Kind == ZKind.Robot && ps.heldBy.Owner == v.Owner) heldFrames++;
                    foreach (var r in crowd)
                    {
                        if (r.Destroyed || r.KillAt >= 0) continue;
                        double rx = PX(r) - x, ry = PY(r) - y, d = Math.Sqrt(rx * rx + ry * ry);
                        if (d < ZPathing.Radius(v) + ZPathing.Radius(r) - 3) through.Add(r.Id);
                        if (r.Waypoints.Count > 0 && r.Waypoints[0].refId == ZPathing.RefStepAside && r.IsMoving && r.MoveSpeed > 0)
                            fastest = Math.Max(fastest, Math.Sqrt((double)r.Dx * r.Dx + (double)r.Dy * r.Dy) / (r.MoveSpeed * Math.Max(0.1, W.Map.TileWalkSpeed(r.CenterX, r.CenterY))));   // (against its own speed on that ground)
                    }
                    if (!shotDone && c.shot != null && c.shotAt >= 0 && now - t0 >= c.shotAt) { shotDone = true; ScreenCapture.CaptureScreenshot(c.shot); }
                }
                until = W.Time + 1.0; while (W.Time < until) yield return null;     // the last stance of a walker ends
                int drag = 0; foreach (var s in slideLines) if (s.Contains("over-stretch drag")) drag++;
                int lost = 0; foreach (var r in crowd) if (r.Destroyed || r.KillAt >= 0) lost++;
                string note = (sent ? "" : "ORDER REFUSED ") + (W.GameOver ? "MATCH OVER (orders are dropped) " : "") + early + (v.Destroyed ? "VEHICLE DESTROYED " : v.Owner != Team.Red ? "DRIVER LOST " : "") +
                              (W.Pathing != null && W.Pathing.FailSafeError != null ? "PATHING FAIL-SAFE " : "") +
                              $"crowd {crowd0} robots ({lost} lost)" + (enemies.Count > 0 ? $", {enemies.Count} enemy squad(s), aimed at the vehicle {attackOrders} times, firing at it in {enemyOnVehicle} of {frames} frames" : ", no enemy") +
                              (inserted.Count > 0 ? ", waypoints put in front of its order: " + string.Join(" / ", inserted) : "") +
                              $", order ends {endDist:0.0} px from the goal, 1 s later {Math.Sqrt((PX(v) - c.gx) * (PX(v) - c.gx) + (PY(v) - c.gy) * (PY(v) - c.gy)):0.0} px";
                string line = $"| {c.name} | {c.vehicle} | {(c.ruleOn ? "on" : "off")} | {(arrived >= 0 ? arrived.ToString("0.00") : "NOT ARRIVED in 120")} | {maxStop:0.00} / {maxDStop:0.00} | {offSim:0.0} / {offDrawn:0.0} | {flips} | {heldFrames} | {maxSep:0.00} | {shells.Count} | {health0 - v.Health} | {ZPathing.YieldsAsked - yields0} | {(fastest > 0 ? "x" + fastest.ToString("0.00") : "-")} | {through.Count} | {slideLines.Count} ({drag}) | {note} |";
                Lines.Add(line); File.AppendAllText(logFile, line + "\n");
                foreach (var s in slideLines) File.AppendAllText(logFile + ".slides.txt", c.name + ": " + s + "\n");
                watchId = -1;
                foreach (var o in mine) if (o.KillAt < 0) o.KillAt = W.Time;
                until = W.Time + 1.5; while (W.Time < until) yield return null;
            }
        }

        static string Wps(ZObject o)
        {
            var sb = new StringBuilder();
            foreach (var w in o.Waypoints) sb.Append($"[{w.mode} ({w.x},{w.y}) ref {w.refId}{(w.playerGiven ? " player" : "")}]");
            return sb.Length == 0 ? "empty" : sb.ToString();
        }

        /// <summary>The staged enemy robots (HOLD POSITION: they never walk) take the vehicle as their target whenever it is in their
        /// range. Returns how many were turned onto it this frame.</summary>
        static int Aim(List<ZObject> foes, ZObject v)
        {
            int n = 0;
            foreach (var f in foes)
            {
                if (f.Destroyed || f.KillAt >= 0 || f.AttackObject == v || !f.WithinAttackRadius(v)) continue;
                f.Engage(v); n++;
            }
            return n;
        }

        /// <summary>Units that were not staged (built by a factory, walking in) leave the stage.</summary>
        static void Sweep(ZWorld W, HashSet<ZObject> mine, double mx, double my)
        {
            foreach (var o in W.Objects)
                if ((o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle) && o.KillAt < 0 && !mine.Contains(o) && !keep.Contains(o) && !o.IsFortDefender &&
                    Math.Abs(o.CenterX - mx) < StagePx && Math.Abs(o.CenterY - my) < StagePx) o.KillAt = W.Time;
        }
    }
}
