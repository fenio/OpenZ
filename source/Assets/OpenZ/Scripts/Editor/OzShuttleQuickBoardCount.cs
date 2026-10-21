using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// the rules, headless on level 16 (ZWorld.Create + Tick, no scene, nothing saved; public members only). Level 16's own ship (its pilot
    /// aboard), the ground round it cleared, a red grunt squad of 1 .. 6 standing behind it:
    ///  A boarding (ENTER): the ramp's opening and closing take ShuttleRampOpenSec / ShuttleRampCloseSec; the squad boards with the ramp
    ///    fully down; the file at the foot ShuttleFileGapSec apart or more; the ramp starts up ShuttleBoardUpSec after the last one reached
    ///    the foot (after he is through the doorway + 0.15 s) and shuts at least 0.15 s after the hold (he sat); EXIT refused until he sat;
    ///    a move order meanwhile takes off the moment the ramp has shut; from the boarding to the latch at least 25 % shorter than run 28's
    ///    rules (1.5 s swings, 3.2 s foot -> seat, 0.55 s file and 3.2 m/s; at least 30 % for a squad of up to 4);
    ///  B unloading (EXIT on the ground, the ramp shut): the squad is let out while the ramp comes down, ShuttleExitLeadSec before it is down
    ///    (the first one at the doorway as it lands), stands in its line on the back slots as one squad; the ramp starts up as the last one
    ///    steps off its tip and shuts ShuttleRampCloseSec later; from EXIT to the latch at least 25 % shorter than run 28 (30 % up to 4);
    ///  C the switch ZWorld.ShuttleQuickRamp off: the old order of the waits (the ramp up only after the last one sat, the squad out only with
    ///    the ramp fully down), with the new times;
    ///  E DV126 in the new in-between states: destroyed while the boarding ramp swings up (everyone past the doorway: all lost, nobody made)
    ///    and right after an EXIT let the squad out while the ramp still comes down (all still inside: all lost, nobody made);
    ///  D A and B again from fresh worlds: the same moments to the tick (deterministic).
    /// </summary>
    public static class OzShuttleQuickBoardCount
    {
        const double Tick = ZConst.TICK;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        /// <summary>Run 28's rules, the reference the owner played : swings, foot -> seat, seat -> off the foot, file.</summary>
        public const double OldSwingSec = 1.5, OldRampInSec = 3.2, OldRampOutSec = 3.2, OldFileGapSec = 0.55, OldWalkMps = 3.2;

        static string settings, m16;
        static StringBuilder sb; static int pass, fail;
        static readonly List<string> digest = new List<string>();
        static void V(bool ok, string what) { sb.AppendLine(OzV11Kit.Verdict(ok, what)); if (ok) pass++; else fail++; }
        static void L(string s) => sb.AppendLine("      " + s);
        static string F(double v, string f = "0.00") => v.ToString(f, Inv);
        static bool Alive(ZObject o) => OzV11Kit.Alive(o);
        static bool Near(double a, double b, double tol = 1.5 * ZConst.TICK) => Math.Abs(a - b) <= tol + 1e-9;

        static void Run(ZWorld w, double secs, Func<bool> until = null)
        {
            double end = w.Time + secs;
            while (w.Time < end - 1e-9) { w.Tick(Tick); w.Events.Clear(); if (until != null && until()) return; }
        }

        static ZWorld W16(int seed)
        {
            ZWorld.ShuttlePlayableDeviation = true; ZWorld.ShuttleOn = true; ZWorld.ShuttleTeam = Team.Red; ZWorld.ShuttleRoughProofNext = false;
            return OzV11Kit.World(settings, m16, seed);
        }

        static bool FreeSpot(ZWorld w, int x, int y, ZObject s, double minFrom, out int fx, out int fy)
        {
            fx = fy = 0; double best = 1e18;
            for (int ty = (y >> 4) - 6; ty <= (y >> 4) + 6; ty++)
                for (int tx = (x >> 4) - 6; tx <= (x >> 4) + 6; tx++)
                {
                    if (tx < 2 || ty < 2 || tx >= w.Map.width - 2 || ty >= w.Map.height - 2) continue;
                    var c = w.Map.ClassAt(tx, ty);
                    if ((c != ZClass.Ground && c != ZClass.Road) || !w.Path.TilePassable(tx, ty, true) || !w.Path.TilePassable(tx, ty, false)) continue;
                    int mx = tx * 16 + 8, my = ty * 16 + 8;
                    if (w.Objects.Any(o => !o.Destroyed && o.Contains(mx, my))) continue;
                    if (Math.Sqrt((double)(mx - s.CenterX) * (mx - s.CenterX) + (double)(my - s.CenterY) * (my - s.CenterY)) < minFrom) continue;
                    double d = (double)(mx - x) * (mx - x) + (double)(my - y) * (my - y);
                    if (d < best) { best = d; fx = mx; fy = my; }
                }
            return best < 1e18;
        }

        /// <summary>Level 16's own ship, the ground round it cleared, a red grunt squad of n behind it (beyond back slot 5).</summary>
        static (ZWorld w, ZObject s, ZObject lead) Ship(int seed, int n)
        {
            var w = W16(seed); var s = w.PlayableShuttle;
            foreach (var o in w.Objects.ToList())
                if ((o.IsRobot || (o.Kind == ZKind.Vehicle && !o.IsShuttle)) && Alive(o) && OzV11Kit.Dist(o, s) < 260) o.KillAt = w.Time;
            Run(w, 0.2);
            var (bx, by) = s.ShuttleBackPoint(5);
            FreeSpot(w, bx, by, s, 70, out int fx, out int fy);
            var lead = w.CreateRobotGroup("grunt", fx - 8, fy - 8, Team.Red, n);
            return (w, s, lead);
        }

        /// <summary>The ramp's moments seen tick by tick.</summary>
        sealed class Ramp
        {
            public double openAt = -1, openEnd = -1, upAt = -1, shutAt = -1;
            int opens0, shuts0; ZShuttleRamp last;
            public Ramp(ZObject s) { opens0 = s.ShuttleRampOpens; shuts0 = s.ShuttleRampShuts; last = s.ShuttleRampPhase; }
            public void See(ZWorld w, ZObject s)
            {
                if (openAt < 0 && s.ShuttleRampOpens > opens0) openAt = w.Time - Tick;                   // the swing began in this tick
                if (openAt >= 0 && openEnd < 0 && s.ShuttleRampPhase == ZShuttleRamp.Open) openEnd = w.Time;
                if (openEnd >= 0 && upAt < 0 && s.ShuttleRampPhase == ZShuttleRamp.Closing && last != ZShuttleRamp.Closing) upAt = w.Time - Tick;
                if (upAt >= 0 && shutAt < 0 && s.ShuttleRampShuts > shuts0) shutAt = w.Time;
                last = s.ShuttleRampPhase;
            }
        }

        /// <summary>Run 28's boarding of n (from the boarding tick to the latch): the leader's step to the foot at its walk (he stands within
        /// ShuttleBoardPx of it: the same distance, run 28's 3.2 m/s), the file, foot -> seat, the swing up (measured on run 28's rules: the same).</summary>
        static double OldBoardSec(int n, double leaderStep) => leaderStep * ZWorld.ShuttleWalkMps / OldWalkMps + (n - 1) * Math.Max(OldFileGapSec, ZWorld.ShuttleFileM / OldWalkMps) + OldRampInSec + OldSwingSec;
        static double OldUnloadSec(int n) => OldSwingSec + (n - 1) * OldFileGapSec + OldRampOutSec + OldSwingSec;

        // ------------------------------------------------------------------ A: boarding
        sealed class BoardRes { public double tFoot = -1, tBoard = -1, hold, lastFoot, total, swingOpen, swingClose, upAfterFoot; public List<double> feet; public bool rampFull, gaps, exitEarly, exitLate, heldTakeOff, offAtShut; public string digest; public Ramp ramp; }

        static BoardRes Board(int n, int seed, bool takeOff)
        {
            var (w, s, lead) = Ship(seed, n);
            var r = new BoardRes(); var ramp = r.ramp = new Ramp(s);
            OzV11Kit.PlayerEnter(w, lead, s);
            var (fx, fy) = s.ShuttleBackPoint(0);
            Run(w, 60, () =>
            {
                ramp.See(w, s);
                if (r.tFoot < 0 && Alive(lead) && Math.Abs(lead.CenterX - fx) <= ZWorld.ShuttleBoardPx && Math.Abs(lead.CenterY - fy) <= ZWorld.ShuttleBoardPx) r.tFoot = w.Time;
                return s.Passengers.Count > 0;
            });
            r.tBoard = w.Time; r.rampFull = Math.Abs(s.ShuttleRamp01 - 1) < 1e-9;
            if (r.tFoot < 0) r.tFoot = r.tBoard;
            r.feet = s.ShuttleFootTimes.ToList(); r.hold = s.ShuttleRampHoldUntil; r.lastFoot = r.feet.Count > 0 ? r.feet[r.feet.Count - 1] : r.tBoard;
            r.gaps = r.feet.Count == n; for (int k = 1; k < r.feet.Count; k++) r.gaps &= r.feet[k] - r.feet[k - 1] >= ZWorld.ShuttleFileGapSec - 1e-9;
            int notes0 = w.ShuttleNoteSeq; double tOff = -1;
            if (takeOff)
            {
                Run(w, 0.3, () => { ramp.See(w, s); return false; });
                var (gx, gy) = (s.CenterX + 400, s.CenterY);
                for (int a = 0; a < 32; a++) { var c = w.ShuttleClamp(s.CenterX + (int)(Math.Cos(a * Math.PI / 16) * 480), s.CenterY + (int)(Math.Sin(a * Math.PI / 16) * 480)); if (w.ShuttlePadFree(c.x, c.y, s, out _)) { (gx, gy) = c; break; } }
                OzV11Kit.PlayerMove(w, s, gx, gy);
            }
            Run(w, 20, () =>
            {
                ramp.See(w, s);
                if (r.hold - w.Time > 0.04 && r.hold - w.Time < 0.06) r.exitEarly = s.CanEjectCrew();
                if (takeOff && s.ShuttleTakeOffHeld) r.heldTakeOff = true;
                if (takeOff && tOff < 0 && w.ShuttleNotes.Any(x => x.seq > notes0 && x.kind == ZShuttleNoteKind.TakeOff)) tOff = w.Time;
                return ramp.shutAt >= 0 && (!takeOff || tOff >= 0) && w.Time > r.hold + 0.1;
            });
            if (!takeOff) r.exitLate = s.CanEjectCrew();
            r.offAtShut = takeOff && tOff >= 0 && tOff >= ramp.shutAt - 1e-9 && tOff <= ramp.shutAt + 0.2;
            r.swingOpen = ramp.openEnd - ramp.openAt; r.swingClose = ramp.shutAt - ramp.upAt; r.upAfterFoot = ramp.upAt - r.lastFoot;
            r.total = ramp.shutAt - r.tBoard;
            r.digest = $"A{n}|{F(r.tBoard, "0.000")}|{string.Join(",", r.feet.Select(f => F(f, "0.000")))}|{F(ramp.openAt, "0.000")}|{F(ramp.upAt, "0.000")}|{F(ramp.shutAt, "0.000")}|{s.Passengers.Count}";
            return r;
        }

        static void A()
        {
            sb.AppendLine($"A. boarding a landed ship (level 16's own, its pilot aboard), red grunt squads of 1 .. 6 ordered in from behind it; K422: swings {F(ZWorld.ShuttleRampOpenSec, "0.0#")} / {F(ZWorld.ShuttleRampCloseSec, "0.0#")} s, " +
                          $"file {F(ZWorld.ShuttleFileGapSec)} s at {F(ZWorld.ShuttleWalkMps, "0.0")} m/s, foot -> doorway {F(ZWorld.ShuttleBoardDoorSec)} s, foot -> seat {F(ZWorld.ShuttleRampInSec)} s, the ramp up {F(ZWorld.ShuttleBoardUpSec)} s after the last foot");
            bool swings = true, full = true, gaps = true, up = true, door = true, seat = true, gate = true, shorter = true, shorter4 = true;
            for (int n = 1; n <= 6; n++)
            {
                var r = Board(n, 40 + n, false);
                double old = OldBoardSec(n, r.feet.Count > 0 ? r.feet[0] - r.tBoard : 0), pct = 100 * (old - r.total) / old;
                double lastDoor = r.lastFoot + ZWorld.ShuttleBoardDoorSec, shut = r.ramp.shutAt;
                L($"squad of {n}: at the foot {F(r.tFoot)}, boarded {F(r.tBoard)} (waited for the ramp {F(r.tBoard - r.tFoot)} s; its swing began {F(r.ramp.openAt)}); feet +{string.Join(" +", r.feet.Select(f => F(f - r.tBoard)))}; the last through the doorway +{F(lastDoor - r.tBoard)}, " +
                  $"the ramp up +{F(r.ramp.upAt - r.tBoard)}, shut +{F(shut - r.tBoard)} (sat by +{F(r.hold - r.tBoard)}); boarding -> latch {F(r.total)} s (run 28: {F(old)} s, {F(pct, "0")} % shorter); swings {F(r.swingOpen)} / {F(r.swingClose)} s; EXIT just before the hold {(r.exitEarly ? "ALLOWED" : "refused")}, after it {(r.exitLate ? "allowed" : "REFUSED")}");
                swings &= (r.ramp.openAt < 0 || Near(r.swingOpen, ZWorld.ShuttleRampOpenSec)) && Near(r.swingClose, ZWorld.ShuttleRampCloseSec);
                full &= r.rampFull; gaps &= r.gaps;
                up &= Near(r.upAfterFoot, ZWorld.ShuttleBoardUpSec);
                door &= r.ramp.upAt >= lastDoor + ZWorld.ShuttleBoardUpMarginSec - Tick - 1e-9;
                seat &= shut >= r.hold + ZWorld.ShuttleSeatShutMarginSec - Tick - 1e-9 && r.hold >= r.ramp.upAt;
                gate &= !r.exitEarly && r.exitLate;
                shorter &= pct >= 25; if (n <= 4) shorter4 &= pct >= 30;
                digest.Add(r.digest);
            }
            V(swings, $"A the ramp swings down in {F(ZWorld.ShuttleRampOpenSec, "0.0#")} s and up in {F(ZWorld.ShuttleRampCloseSec, "0.0#")} s (run 28: 1.5 / 1.5 s)");
            V(full && gaps, "A the squad boards with the ramp fully down, the file at the foot one after the other (ShuttleFileGapSec apart or more)");
            V(up && door, $"A the ramp starts up {F(ZWorld.ShuttleBoardUpSec)} s after the last one reached the foot: never before he is through the doorway + {F(ZWorld.ShuttleBoardUpMarginSec)} s (no figure on a moving ramp)");
            V(seat && gate, $"A it shuts at least {F(ZWorld.ShuttleSeatShutMarginSec)} s after the last one sat (the hold), and EXIT stays refused until he sat");
            var t = Board(3, 47, true);
            L($"a move order 0.3 s after a boarding of 3: held {t.heldTakeOff}, the ramp shut at +{F(t.ramp.shutAt - t.tBoard)}, the take-off at the shut {t.offAtShut}");
            V(t.heldTakeOff && t.offAtShut, "A a move order given meanwhile waits and takes off the moment the ramp has shut");
            V(shorter && shorter4, "A from the boarding to the latch: at least 25 % shorter than run 28 for every squad, at least 30 % for squads of up to 4");
        }

        // ------------------------------------------------------------------ B: unloading
        sealed class ExitRes { public double tEject, tExit = -1, rampAtExit, lastFoot, total; public List<double> feet; public int outs, line; public bool oneSquad, behind, downFirst; public string digest; public Ramp ramp; }

        static ExitRes Unload(int n, int seed)
        {
            var (w, s, lead) = Ship(seed, n);
            OzV11Kit.PlayerEnter(w, lead, s); Run(w, 60, () => s.Passengers.Count > 0);
            Run(w, 20, () => s.ShuttleRampPhase == ZShuttleRamp.Shut && w.Time >= s.ShuttleRampHoldUntil);
            Run(w, 0.5);
            var r = new ExitRes(); var ramp = r.ramp = new Ramp(s);
            var before = new HashSet<ZObject>(w.Objects);
            r.tEject = w.Time; w.Eject(Team.Red, s); r.downFirst = s.Passengers.Count == n && s.ShuttleExitPending;
            Run(w, 10, () => { ramp.See(w, s); if (r.tExit < 0 && s.Passengers.Count == 0) { r.tExit = w.Time; r.rampAtExit = s.ShuttleRamp01; } return r.tExit >= 0; });
            r.feet = s.ShuttleFootTimes.ToList(); r.lastFoot = r.feet.Count > 0 ? r.feet[r.feet.Count - 1] : r.tExit;
            var outs = w.Objects.Where(o => o.IsRobot && o.Owner == Team.Red && Alive(o) && !before.Contains(o)).ToList();
            r.outs = outs.Count;
            var ld = outs.FirstOrDefault(o => !o.IsMinion);
            r.oneSquad = ld != null && ld.Minions.Count + 1 == n;
            r.behind = outs.All(o => OzV11Kit.Dist(o, s) / 8.0 >= ZWorld.ShuttleBackM + ZWorld.ShuttleFileM - 0.3 && o.Waypoints.Count == 0);
            Run(w, 20, () => { ramp.See(w, s); return ramp.shutAt >= 0; });
            r.total = ramp.shutAt - r.tEject;
            r.digest = $"B{n}|{F(r.tExit - r.tEject, "0.000")}|{F(r.rampAtExit, "0.000")}|{string.Join(",", r.feet.Select(f => F(f - r.tEject, "0.000")))}|{F(ramp.upAt - r.tEject, "0.000")}|{F(ramp.shutAt - r.tEject, "0.000")}|{string.Join(",", outs.Select(o => o.CenterX + ":" + o.CenterY))}";
            return r;
        }

        static void B()
        {
            sb.AppendLine($"B. unloading on the ground (EXIT, the ramp shut), the same squads; K422: let out {F(ZWorld.ShuttleExitLeadSec)} s before the ramp is down (at {F(100 * ZWorld.ShuttleExitRamp01, "0")} % of its swing), seat -> doorway {F(ZWorld.ShuttleExitDoorSec)} s, seat -> off the foot {F(ZWorld.ShuttleRampOutSec)} s, the ramp up {F(ZWorld.ShuttleExitTipSec)} s before the last is off the foot");
            bool let = true, door = true, line = true, up = true, swings = true, shorter = true, shorter4 = true;
            for (int n = 1; n <= 6; n++)
            {
                var r = Unload(n, 60 + n);
                double old = OldUnloadSec(n), pct = 100 * (old - r.total) / old;
                double firstDoor = r.feet.Count > 0 ? r.feet[0] - ZWorld.ShuttleRampOutSec + ZWorld.ShuttleExitDoorSec : -1;
                L($"squad of {n}: EXIT at 0, the ramp's swing began +{F(r.ramp.openAt - r.tEject)}, down +{F(r.ramp.openEnd - r.tEject)}; let out +{F(r.tExit - r.tEject)} with the ramp at {F(r.rampAtExit)}; the first at the doorway +{F(firstDoor - r.tEject)}; " +
                  $"off the foot +{string.Join(" +", r.feet.Select(f => F(f - r.tEject)))}; the ramp up +{F(r.ramp.upAt - r.tEject)}, shut +{F(r.ramp.shutAt - r.tEject)}; EXIT -> latch {F(r.total)} s (run 28: {F(old)} s, {F(pct, "0")} % shorter); out {r.outs}, one squad {r.oneSquad}, behind the ship on their slots {r.behind}");
                let &= r.downFirst && Near(r.tExit - r.tEject, ZWorld.ShuttleExitAfterSec + Tick, 2 * Tick) && Math.Abs(r.rampAtExit - ZWorld.ShuttleExitRamp01) <= 2 * Tick / ZWorld.ShuttleRampOpenSec + 1e-9;
                door &= Near(firstDoor, r.ramp.openEnd, 2 * Tick);
                line &= r.outs == n && r.oneSquad && r.behind;
                up &= Near(r.ramp.upAt, r.lastFoot - ZWorld.ShuttleExitTipSec, 2 * Tick);
                swings &= Near(r.ramp.openEnd - r.ramp.openAt, ZWorld.ShuttleRampOpenSec) && Near(r.ramp.shutAt - r.ramp.upAt, ZWorld.ShuttleRampCloseSec);
                shorter &= pct >= 25; if (n <= 4) shorter4 &= pct >= 30;
                digest.Add(r.digest);
            }
            V(let && door, $"B EXIT brings the ramp down and lets the squad get up while it comes down ({F(ZWorld.ShuttleExitLeadSec)} s before it is down): the first one reaches the doorway as it lands");
            V(line, "B they stand in their line on the back slots behind the ship as one squad, no order");
            V(up && swings, $"B the ramp starts up as the last one steps off its tip ({F(ZWorld.ShuttleExitTipSec)} s before he is off the foot) and swings in {F(ZWorld.ShuttleRampOpenSec, "0.0#")} / {F(ZWorld.ShuttleRampCloseSec, "0.0#")} s");
            V(shorter && shorter4, "B from EXIT to the latch: at least 25 % shorter than run 28 for every squad, at least 30 % for squads of up to 4");
        }

        // ------------------------------------------------------------------ C: the switch off
        static void C()
        {
            bool was = ZWorld.ShuttleQuickRamp;
            try
            {
                ZWorld.ShuttleQuickRamp = false;
                var b = Board(3, 81, false); var u = Unload(3, 82);
                L($"switch off: boarding of 3: the ramp up {F(b.ramp.upAt - b.hold)} s after the hold (the last one sat); unloading: let out with the ramp at {F(u.rampAtExit)}, the ramp up {F(u.ramp.upAt - u.lastFoot)} s after the last foot");
                V(Near(b.ramp.upAt, b.hold) && Math.Abs(u.rampAtExit - 1) < 1e-9 && Near(u.ramp.upAt, u.lastFoot), "C ZWorld.ShuttleQuickRamp off: the ramp waits for the last one to sit, the squad gets up only with the ramp fully down (the old order, the new times)");
            }
            finally { ZWorld.ShuttleQuickRamp = was; }
        }

        // ------------------------------------------------------------------ E: DV126 in the new in-between states
        static void E()
        {
            bool doorWas = ZWorld.ShuttleDoorwayDeviation; ZWorld.ShuttleDoorwayDeviation = true;
            try
            {
                {   // the boarding ramp swinging up
                    var (w, s, lead) = Ship(91, 4);
                    OzV11Kit.PlayerEnter(w, lead, s); Run(w, 60, () => s.Passengers.Count > 0);
                    Run(w, 20, () => s.ShuttleRampPhase == ZShuttleRamp.Closing && s.ShuttleRamp01 <= 0.6);       // mid-swing up (the last one walks the aisle)
                    int aboard = s.Passengers.Count, sid = s.Id, seq0 = w.ShuttleLostSeq, d0 = w.ShuttleDoorwaySeq; var before = new HashSet<ZObject>(w.Objects);
                    string state = $"{s.ShuttleRampPhase} {F(s.ShuttleRamp01)}";
                    s.DamageHealth(s.Health); w.UpdateObjectHealth(s); Run(w, 1.0);
                    int lost = w.ShuttleLostCrew.Count(x => x.shipId == sid && x.seq > seq0), rows = w.ShuttleDoorwayRows.Count(x => x.shipId == sid && x.seq > d0);
                    int made = w.Objects.Count(o => o.IsRobot && Alive(o) && !before.Contains(o));
                    L($"destroyed while the boarding ramp swings up (ramp {state}): aboard {aboard}, lost {lost}, the file's rows {rows}, robots made {made}");
                    V(aboard == 4 && lost == 4 && made == 0 && state.StartsWith("Closing"), "E destroyed while the boarding ramp swings up (everyone past the doorway): all aboard lost, nobody made");
                }
                {   // the squad let out while the ramp still comes down
                    var (w, s, lead) = Ship(92, 4);
                    OzV11Kit.PlayerEnter(w, lead, s); Run(w, 60, () => s.Passengers.Count > 0);
                    Run(w, 20, () => s.ShuttleRampPhase == ZShuttleRamp.Shut && w.Time >= s.ShuttleRampHoldUntil);
                    w.Eject(Team.Red, s); var outsBefore = new HashSet<ZObject>(w.Objects);
                    Run(w, 10, () => s.Passengers.Count == 0);
                    Run(w, 0.05);
                    var outs = w.Objects.Where(o => o.IsRobot && o.Owner == Team.Red && Alive(o) && !outsBefore.Contains(o)).ToList();
                    int sid = s.Id, seq0 = w.ShuttleLostSeq, d0 = w.ShuttleDoorwaySeq; var before = new HashSet<ZObject>(w.Objects);
                    string state = $"{s.ShuttleRampPhase} {F(s.ShuttleRamp01)}";
                    s.DamageHealth(s.Health); w.UpdateObjectHealth(s); Run(w, 1.0);
                    int lost = w.ShuttleLostCrew.Count(x => x.shipId == sid && x.seq > seq0), doorLost = w.ShuttleDoorwayRows.Count(x => x.shipId == sid && x.seq > d0 && !x.saved);
                    int alive = outs.Count(Alive), made = w.Objects.Count(o => o.IsRobot && Alive(o) && !before.Contains(o));
                    L($"destroyed 0.05 s after an EXIT let the squad out while the ramp came down (ramp {state}): let out {outs.Count}, lost {lost} (the file's lost {doorLost}), alive {alive}, made {made}");
                    V(outs.Count == 4 && lost == 4 && doorLost == 4 && alive == 0 && made == 0, "E destroyed right after the squad was let out while the ramp comes down (all still inside): all lost with the ship, nobody walks out of the wreck");
                }
            }
            finally { ZWorld.ShuttleDoorwayDeviation = doorWas; }
        }

        static string Hash(string s) { unchecked { uint h = 2166136261; foreach (char ch in s) { h ^= ch; h *= 16777619; } return h.ToString("x8"); } }

        /// <summary>The whole check: A, B, C, E, then D (A and B again: the same moments).</summary>
        public static string Check(string settingsText, string m16Text)
        {
            settings = settingsText; m16 = m16Text;
            sb = new StringBuilder(); pass = fail = 0; digest.Clear();
            bool was = ZWorld.ShuttlePlayableDeviation, wasOn = ZWorld.ShuttleOn, quick = ZWorld.ShuttleQuickRamp; var team0 = ZWorld.ShuttleTeam;
            try
            {
                ZWorld.ShuttleQuickRamp = true;
                sb.AppendLine("K422: the shuttle's boarding and unloading phase by phase (level 16, headless; times in sim seconds)");
                A(); B(); C(); E();
                var first = digest.ToList(); digest.Clear();
                var keep = sb; sb = new StringBuilder(); int p0 = pass, f0 = fail;
                for (int n = 1; n <= 6; n++) digest.Add(Board(n, 40 + n, false).digest);
                for (int n = 1; n <= 6; n++) digest.Add(Unload(n, 60 + n).digest);
                sb = keep; pass = p0; fail = f0;
                bool same = string.Join("\n", first) == string.Join("\n", digest);
                sb.AppendLine($"D. A and B again from fresh worlds: {digest.Count} digests (ticks of every moment, places), the same {same}; hash {Hash(string.Join("\n", first))}");
                V(same && digest.Count == 12, "D the same orders twice: the same moments to the tick and the same places (deterministic)");
            }
            catch (Exception ex) { sb.AppendLine("FAIL  the check threw " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace); fail++; }
            finally { ZWorld.ShuttlePlayableDeviation = was; ZWorld.ShuttleOn = wasOn; ZWorld.ShuttleQuickRamp = quick; ZWorld.ShuttleTeam = team0; ZWorld.ShuttleRoughProofNext = false; }
            sb.AppendLine($"K422: {pass} PASS, {fail} FAIL");
            return sb.ToString();
        }
    }
}
