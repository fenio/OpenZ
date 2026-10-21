using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K426 (rule DV118, Sim/ZWorld.ShuttleExitSooner.cs): EXIT is taken from the touchdown (a crash landing's too) and while the ship comes down
    /// the last metres, and the rules carry it out as soon as it is safe (a normal landing at once, a crash the moment the self-righting
    /// ends); the crash's wait is shorter (0.8 + 0.2 + 0.7 s, was 1.4 + 1.0 + 1.2 s). Counted headless on level 16 (ZWorld.Create + Tick, no
    /// scene, nothing saved; public members only). Each landing is staged from a fresh world: level 16's own ship with a red grunt squad of
    /// SquadSize aboard flown to a free pad, a normal or a crash landing (the rules' proof switch), and EXIT clicked:
    ///  at the touchdown (the first tick on the ground), in the last 3 m of the descent, or as soon as the rules offer it (CanEjectCrew polled
    ///  every tick from the touchdown; a player clicking as fast as the game lets him); "watch" = no click, the offer sampled every tick.
    /// Measured on the rules' clock, from the touchdown: EXIT offered, EXIT taken, the ramp starting down, the squad out (the ramp fully down:
    /// the robots made on their back slots), the first robot through the doorway and off the ramp foot (the rules' foot schedule). The same
    /// with <see cref="ZWorld.ShuttleExitSoonerOn"/> off gives today's numbers. Lines:
    ///  T1 crash, clicked at the touchdown: taken, the ramp shut until it stands level, down from the level moment, the squad out a ramp's
    ///     swing later; T2 normal, clicked at the touchdown: taken, the ramp down at once, the squad out a ramp's swing later; T3 clicked in
    ///     the last 3 m (both landings): taken in the air, out exactly as when clicked at the touchdown; T4 the offer never drops from the
    ///     descent's start through the touchdown, the crash and the righting (both landings); T5 the crash's timing (level start, level);
    ///     T6 the whole wait after a crash is shorter than today's by at least 1.8 s; T7 the switch off = today's rules (EXIT refused until
    ///     level, the old timing); T8 a squad still walking in blocks EXIT until it sits (as before); T9 a take-off ordered after an EXIT
    ///     given while it rights itself waits for the squad to get out; D the same landing twice gives the same numbers.
    /// AIM lines (not counted): the owner's aim, the first robot out about 2 s after a crash's touchdown and 1 s after a normal one's (it
    /// depends on the ramp's and the walk's own times as well, which this kit does not change).
    /// </summary>
    public static class OzShuttleExitSoonerCount
    {
        const double Tick = ZConst.TICK;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public const int SquadSize = 4;

        sealed class Ctx { public ZWorld w; public ZObject s, lead; }

        /// <summary>One staged landing and what the rules did after it (s after the touchdown; -1 = never).</summary>
        sealed class Run1
        {
            public bool ok, rough, clicked, taken, offeredAtTd, airTaken;
            public double td = -1, levelStart = -1, level = -1, offered = -1, takenAt = -1, rampStart = -1, outAt = -1, door = -1, foot = -1;
            public int made, aboard, rampWhileTilted, refusedDescent, refusedAfterTd, descentTicks, afterTdTicks;
            public string why = "";
            public string Line(string what) => $"{what}: rough {rough}, level start +{F(levelStart)} s, level +{F(level)} s; EXIT offered at the touchdown {offeredAtTd}, first offered {N(offered)} s; " +
                                               $"clicked {clicked}{(clicked ? $" at {F(takenAt)} s, taken {taken}{(airTaken ? " (in the air: on landing)" : "")}" : "")}; ramp down from {N(rampStart)} s; squad of {aboard} out {N(outAt)} s ({made} made); " +
                                               $"the first robot through the doorway {N(door)} s, off the ramp foot {N(foot)} s; ramp moved while tilted {rampWhileTilted} ticks; " +
                                               $"EXIT refused in the descent {refusedDescent}/{descentTicks} ticks, after the touchdown before the click or the out {refusedAfterTd}/{afterTdTicks}{(why != "" ? "; " + why : "")}";
        }

        static string settings, m16;
        static StringBuilder sb; static int pass, fail;
        static void V(bool ok, string what) { sb.AppendLine(OzV11Kit.Verdict(ok, what)); if (ok) pass++; else fail++; }
        static void L(string s) => sb.AppendLine("      " + s);
        static string F(double v, string f = "0.00") => v.ToString(f, Inv);
        static string N(double v) => v < 0 ? "never" : "+" + F(v);

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

        static bool FreeSpot(ZWorld w, int x, int y, out int fx, out int fy, ZObject keepFrom, double minFrom)
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
                    if (keepFrom != null && Math.Sqrt((double)(mx - keepFrom.CenterX) * (mx - keepFrom.CenterX) + (double)(my - keepFrom.CenterY) * (my - keepFrom.CenterY)) < minFrom) continue;
                    double d = (double)(mx - x) * (mx - x) + (double)(my - y) * (my - y);
                    if (d < best) { best = d; fx = mx; fy = my; }
                }
            return best < 1e18;
        }

        static bool PadSpot(ZWorld w, ZObject s, int minR, int maxR, out int px, out int py)
        {
            for (int r = minR; r <= maxR; r += 16)
                for (int a = 0; a < 32; a++)
                {
                    double g = a * Math.PI / 16;
                    var (x, y) = w.ShuttleClamp(s.CenterX + (int)(Math.Cos(g) * r), s.CenterY + (int)(Math.Sin(g) * r));
                    if (w.ShuttlePadFree(x, y, s, out _)) { px = x; py = y; return true; }
                }
            px = py = 0; return false;
        }

        /// <summary>Level 16's own ship, the ground round it cleared, a red grunt squad of SquadSize boarded, the ramp shut again.</summary>
        static Ctx Loaded(int seed)
        {
            var c = new Ctx { w = W16(seed) };
            c.s = c.w.PlayableShuttle;
            foreach (var o in c.w.Objects.ToList())
                if ((o.IsRobot || (o.Kind == ZKind.Vehicle && !o.IsShuttle)) && OzV11Kit.Alive(o) && OzV11Kit.Dist(o, c.s) < 260) o.KillAt = c.w.Time;
            Run(c.w, 0.2);
            var (bx, by) = c.s.ShuttleBackPoint(5);
            FreeSpot(c.w, bx, by, out int fx, out int fy, c.s, 70);
            c.lead = c.w.CreateRobotGroup("grunt", fx - 8, fy - 8, Team.Red, SquadSize);
            OzV11Kit.PlayerEnter(c.w, c.lead, c.s);
            Run(c.w, 60, () => c.s.Passengers.Count > 0);
            Run(c.w, 20, () => c.s.ShuttleRampPhase == ZShuttleRamp.Shut && !c.s.ShuttleExitPending && c.w.Time >= c.s.ShuttleRampHoldUntil);
            return c;
        }

        /// <summary>A loaded ship flown to a free pad until its descent begins, the landing normal or rough as wanted.</summary>
        static Ctx Descending(bool rough, int seed0)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var c = Loaded(seed0 + attempt);
                if (c.s.Passengers.Count == 0) continue;
                if (!PadSpot(c.w, c.s, 400 + attempt * 64, 1100, out int gx, out int gy)) continue;
                ZWorld.ShuttleRoughProofNext = rough;
                OzV11Kit.PlayerMove(c.w, c.s, gx, gy);
                Run(c.w, 90, () => c.s.ShuttleState == ZShuttleState.Descend || c.s.ShuttleState == ZShuttleState.Hover);
                ZWorld.ShuttleRoughProofNext = false;
                if (c.s.ShuttleState != ZShuttleState.Descend || c.s.ShuttleRoughLanding != rough) continue;
                return c;
            }
            return null;
        }

        /// <summary>One landing: mode "touchdown" | "last3m" | "asap" | "watch" | "takeoff" (EXIT at the touchdown, then a move order at once).</summary>
        static Run1 Land(bool rough, string mode, int seed0)
        {
            var r = new Run1();
            var c = Descending(rough, seed0);
            if (c == null) { r.why = "no landing staged"; return r; }
            var w = c.w; var s = c.s; r.rough = s.ShuttleRoughLanding; r.aboard = s.Passengers.Count;
            int opens0 = s.ShuttleRampOpens, made = 0; bool clicked = false; double clickAt = -1;
            bool OfferNow() => s.CanEjectCrew();
            void Click()
            {
                clicked = true; clickAt = w.Time;
                bool airborne = s.ShuttleAirborne;
                r.taken = w.Eject(Team.Red, s);
                r.airTaken = r.taken && airborne && s.ShuttleExitOnLanding;
            }
            // the descent (until the touchdown)
            while (s.ShuttleState == ZShuttleState.Descend && w.Time < 200)
            {
                r.descentTicks++; if (!OfferNow()) r.refusedDescent++;
                if (mode == "last3m" && !clicked && s.ShuttleHeightM <= 3.0) Click();
                w.Tick(Tick); w.Events.Clear();
            }
            if (s.ShuttleState != ZShuttleState.Landed) { r.why = "it did not land (state " + s.ShuttleState + ")"; return r; }
            r.td = s.ShuttleTouchdownAt; r.levelStart = s.ShuttleLevelStartAt - r.td; r.level = s.ShuttleLevelledAt - r.td;
            r.offeredAtTd = OfferNow();
            if ((mode == "touchdown" || mode == "takeoff") && !clicked) Click();
            if (mode == "takeoff") OzV11Kit.PlayerMove(w, s, s.CenterX + 240, s.CenterY);
            double end = w.Time + 14.0;
            bool tookOff = false; double offAt = -1, outAbs = -1;
            while (w.Time < end)
            {
                double now = w.Time;
                bool offer = OfferNow();
                if (r.offered < 0 && offer) r.offered = now - r.td;
                if (outAbs < 0 && s.ShuttleState == ZShuttleState.Landed) { r.afterTdTicks++; if (!offer && !clicked) r.refusedAfterTd++; }
                if (mode == "asap" && !clicked && offer) Click();
                if (now < s.ShuttleLevelledAt - 1e-9 && s.ShuttleRamp01 > 0) r.rampWhileTilted++;
                if (r.rampStart < 0 && s.ShuttleRampOpens != opens0) r.rampStart = now - r.td;
                w.Tick(Tick);
                foreach (var e in w.Events) if (e.type == ZEventType.Created && e.otherId == s.Id) made++;
                w.Events.Clear();
                if (r.rampStart < 0 && s.ShuttleRampOpens != opens0) r.rampStart = w.Time - r.td;
                if (outAbs < 0 && clicked && s.Passengers.Count == 0)
                {
                    outAbs = w.Time; r.outAt = outAbs - r.td;
                    if (s.ShuttleFootTimes.Count > 0) { r.foot = s.ShuttleFootTimes[0] - r.td; r.door = s.ShuttleFootTimes[0] - ZWorld.ShuttleRampOutSec + ZWorld.ShuttleExitDoorSec - r.td; }
                }
                if (!tookOff && mode == "takeoff" && s.ShuttleState != ZShuttleState.Landed) { tookOff = true; offAt = w.Time - r.td; }
                if (mode == "watch" && w.Time > s.ShuttleLevelledAt + 0.5) break;
                if (mode != "takeoff" && mode != "watch" && outAbs >= 0 && w.Time > outAbs + 0.2) break;
                if (mode == "takeoff" && tookOff) break;
            }
            r.clicked = clicked; r.takenAt = clicked ? clickAt - r.td : -1; r.made = made;
            if (mode == "takeoff") r.why = $"take-off at +{F(offAt)} s (the ramp shut {s.ShuttleRampPhase == ZShuttleRamp.Shut || tookOff}), the squad out before it {(r.outAt >= 0 && (offAt < 0 || r.outAt <= offAt))}";
            r.ok = true;
            return r;
        }

        static bool Near(double a, double b, double tol) => a >= 0 && b >= 0 && Math.Abs(a - b) <= tol + 1e-9;

        /// <summary>From the ramp starting down to the squad let out (s): the ramp's swing, or a later kit's own value when it lets the squad out
        /// while the ramp still comes down (a public static ZWorld.ShuttleExitAfterSec, read when present).</summary>
        public static double ExitAfterSec()
        {
            var p = typeof(ZWorld).GetProperty("ShuttleExitAfterSec", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (p != null && p.PropertyType == typeof(double)) return (double)p.GetValue(null);
            return ZWorld.ShuttleRampOpenSec;
        }

        /// <summary>The whole check: the PASS / FAIL lines and the numbers (switches as they were afterwards).</summary>
        public static string Check(string settingsText, string level16)
        {
            settings = settingsText; m16 = level16; sb = new StringBuilder(); pass = fail = 0;
            bool was = ZWorld.ShuttleExitSoonerOn;
            sb.AppendLine("K426 EXIT sooner after a landing (rule DV118, Sim/ZWorld.ShuttleExitSooner.cs): level 16's ship, a red grunt squad of " + SquadSize + " aboard, flown to a free pad");
            sb.AppendLine($"the rules' times: the ramp down {F(ZWorld.ShuttleRampOpenSec)} s, the squad let out {F(ExitAfterSec())} s after it starts, the first robot through the doorway {F(ZWorld.ShuttleExitDoorSec)} s and off the ramp foot {F(ZWorld.ShuttleRampOutSec)} s after the squad is out, {F(ZWorld.ShuttleFileGapSec)} s apart");
            var res = new Dictionary<string, Run1>();
            try
            {
                foreach (bool on in new[] { false, true })
                {
                    ZWorld.ShuttleExitSoonerOn = on;
                    string tag = on ? "K426" : "today (switch off)";
                    sb.AppendLine($"{tag}: crash {F(ZWorld.ShuttleRoughCrashSec)} + rest {F(ZWorld.ShuttleRoughRestSec)} + righting {F(ZWorld.ShuttleRoughLevelSec)} s = level {F(ZWorld.ShuttleRoughSettleSec)} s after a crash's touchdown");
                    foreach (bool rough in new[] { false, true })
                        foreach (var mode in new[] { "touchdown", "last3m", "asap", "watch" })
                        {
                            var r = Land(rough, mode, rough ? 20 : 40);
                            string key = (on ? "on" : "off") + "_" + (rough ? "crash" : "normal") + "_" + mode;
                            res[key] = r;
                            L(r.Line($"{tag}, {(rough ? "crash" : "normal")} landing, EXIT {(mode == "touchdown" ? "clicked at the touchdown" : mode == "last3m" ? "clicked in the last 3 m" : mode == "asap" ? "clicked the moment it is offered" : "not clicked (the offer watched)")}"));
                        }
                }
                ZWorld.ShuttleExitSoonerOn = true;
                double ro = ExitAfterSec(), tol = 2 * Tick;   // the ramp starting down -> the squad let out (the ramp's swing; less with a kit that lets them out on the way down)
                Run1 Get(string k) => res.TryGetValue(k, out var x) ? x : new Run1();

                // T1 crash, at the touchdown
                {
                    var r = Get("on_crash_touchdown");
                    V(r.ok && r.rough && r.offeredAtTd && r.taken && r.rampWhileTilted == 0 && Near(r.rampStart, r.level, tol) && Near(r.outAt, r.level + ro, tol) && r.made == r.aboard && r.aboard == SquadSize,
                      $"T1 crash landing, EXIT clicked at the touchdown: offered and TAKEN ({r.taken}); the ramp stays shut while it crashes, rests and rights itself (moved {r.rampWhileTilted} ticks), comes down from the level moment (+{F(r.rampStart)} s, level +{F(r.level)}), the squad out {F(ro)} s later (+{F(r.outAt)} s, {r.made} of {r.aboard} made)");
                }
                // T2 normal, at the touchdown
                {
                    var r = Get("on_normal_touchdown");
                    V(r.ok && !r.rough && r.offeredAtTd && r.taken && Near(r.level, 0, 1e-9) && r.rampStart >= 0 && r.rampStart <= tol && Near(r.outAt, ro, tol) && r.made == r.aboard && r.aboard == SquadSize,
                      $"T2 normal landing, EXIT clicked at the touchdown: taken ({r.taken}), the ramp down at once (+{F(r.rampStart)} s), the squad out {F(ro)} s later (+{F(r.outAt)} s, {r.made} of {r.aboard} made)");
                }
                // T3 the last 3 m
                {
                    var n = Get("on_normal_last3m"); var c3 = Get("on_crash_last3m"); var nt = Get("on_normal_touchdown"); var ct = Get("on_crash_touchdown");
                    V(n.ok && c3.ok && n.taken && n.airTaken && c3.taken && c3.airTaken && Near(n.outAt, nt.outAt, tol) && Near(c3.outAt, ct.outAt, tol) && c3.rampWhileTilted == 0 && n.made == n.aboard && c3.made == c3.aboard,
                      $"T3 EXIT clicked while it comes down the last 3 m: taken in the air (normal {n.airTaken}, crash {c3.airTaken}) and carried out as when clicked at the touchdown (out +{F(n.outAt)} / +{F(c3.outAt)} s; at the touchdown +{F(nt.outAt)} / +{F(ct.outAt)} s), the crash's ramp shut while tilted ({c3.rampWhileTilted} ticks)");
                }
                // T4 the offer never drops
                {
                    var n = Get("on_normal_watch"); var c4 = Get("on_crash_watch");
                    V(n.ok && c4.ok && n.descentTicks > 100 && c4.descentTicks > 100 && n.refusedDescent == 0 && c4.refusedDescent == 0 && n.refusedAfterTd == 0 && c4.refusedAfterTd == 0 && n.offeredAtTd && c4.offeredAtTd,
                      $"T4 EXIT offered on every tick from the descent's start through the touchdown, the crash and the righting (normal: refused {n.refusedDescent}/{n.descentTicks} coming down, {n.refusedAfterTd}/{n.afterTdTicks} on the ground; crash: {c4.refusedDescent}/{c4.descentTicks}, {c4.refusedAfterTd}/{c4.afterTdTicks})");
                }
                // T5 the crash's timing
                {
                    var on = Get("on_crash_watch"); var off = Get("off_crash_watch");
                    V(Near(on.levelStart, ZWorld.ShuttleSoonerCrashSec + ZWorld.ShuttleSoonerRestSec, 1e-9) && Near(on.level, ZWorld.ShuttleSoonerCrashSec + ZWorld.ShuttleSoonerRestSec + ZWorld.ShuttleSoonerLevelSec, 1e-9)
                      && Near(off.levelStart, ZWorld.ShuttleOldCrashSec + ZWorld.ShuttleOldRestSec, 1e-9) && Near(off.level, ZWorld.ShuttleOldCrashSec + ZWorld.ShuttleOldRestSec + ZWorld.ShuttleOldLevelSec, 1e-9),
                      $"T5 the crash's rules timing: it starts righting itself +{F(on.levelStart)} s and stands level +{F(on.level)} s after the touchdown (today +{F(off.levelStart)} / +{F(off.level)} s)");
                }
                // T6 the whole wait after a crash
                {
                    var on = Get("on_crash_asap"); var off = Get("off_crash_asap");
                    V(on.ok && off.ok && on.outAt >= 0 && off.outAt >= 0 && on.outAt <= off.outAt - 1.8 + 1e-9,
                      $"T6 the wait after a crash, EXIT clicked the moment it is offered: the squad out +{F(on.outAt)} s after the touchdown, the first robot through the doorway +{F(on.door)} s (today +{F(off.outAt)} / +{F(off.door)} s: {F(off.outAt - on.outAt)} s sooner, at least 1.8)");
                }
                // T7 the switch off = today's rules
                {
                    var c7 = Get("off_crash_touchdown"); var a7 = Get("off_crash_asap"); var n7 = Get("off_normal_touchdown");
                    V(c7.ok && !c7.offeredAtTd && !c7.taken && a7.ok && Near(a7.offered, ZWorld.ShuttleOldCrashSec + ZWorld.ShuttleOldRestSec + ZWorld.ShuttleOldLevelSec, tol) && Near(a7.outAt, a7.offered + ro, tol)
                      && n7.ok && n7.taken && Near(n7.outAt, ro, tol),
                      $"T7 the switch off is today's rules: a crash's EXIT at the touchdown refused ({!c7.taken}), first offered +{F(a7.offered)} s (level), out +{F(a7.outAt)} s; a normal landing's EXIT at the touchdown taken, out +{F(n7.outAt)} s");
                }
                // T8 a squad still walking in blocks EXIT (the old gate, kept)
                {
                    var c = new Ctx { w = W16(60) }; c.s = c.w.PlayableShuttle;
                    foreach (var o in c.w.Objects.ToList()) if ((o.IsRobot || (o.Kind == ZKind.Vehicle && !o.IsShuttle)) && OzV11Kit.Alive(o) && OzV11Kit.Dist(o, c.s) < 260) o.KillAt = c.w.Time;
                    Run(c.w, 0.2);
                    var (bx, by) = c.s.ShuttleBackPoint(5); FreeSpot(c.w, bx, by, out int fx, out int fy, c.s, 70);
                    c.lead = c.w.CreateRobotGroup("grunt", fx - 8, fy - 8, Team.Red, SquadSize);
                    OzV11Kit.PlayerEnter(c.w, c.lead, c.s);
                    Run(c.w, 60, () => c.s.Passengers.Count > 0);
                    bool walking = c.w.Time < c.s.ShuttleRampHoldUntil, blocked = !c.s.CanEjectCrew() && !c.w.Eject(Team.Red, c.s);
                    double hold = c.s.ShuttleRampHoldUntil - c.w.Time;
                    Run(c.w, hold + 0.05);
                    bool after = c.s.CanEjectCrew();
                    V(walking && blocked && after, $"T8 a squad still walking up the ramp blocks EXIT until it sits ({F(hold)} s after the boarding: blocked {blocked}, offered after {after})");
                }
                // T9 a take-off after an EXIT given while it rights itself
                {
                    var r = Land(true, "takeoff", 20);
                    L(r.Line("K426, crash landing, EXIT at the touchdown and a move order right after it"));
                    V(r.ok && r.taken && r.outAt >= 0 && r.made == r.aboard && r.why.Contains("the squad out before it True") && r.rampWhileTilted == 0,
                      $"T9 a move order given right after an EXIT at a crash's touchdown waits: the squad out first (+{F(r.outAt)} s), then the ramp shuts and it lifts off ({r.why})");
                }
                // D determinism
                {
                    var a = Land(true, "touchdown", 20); var b = Land(true, "touchdown", 20);
                    string la = a.Line("x"), lb = b.Line("x");
                    V(a.ok && la == lb, "D the same crash landing twice: the same numbers (" + (la == lb ? "identical" : "DIFFERENT: " + lb) + ")");
                }
                // AIM (not counted)
                {
                    var c = Get("on_crash_touchdown"); var n = Get("on_normal_touchdown");
                    sb.AppendLine($"AIM (not counted) the aim, the first robot out about 2 s after a crash's touchdown and 1 s after a normal one's: crash +{F(c.door)} s through the doorway (the squad on the ground +{F(c.outAt)} s), normal +{F(n.door)} s (+{F(n.outAt)} s); the rest is the ramp's swing ({F(ZWorld.ShuttleRampOpenSec)} s) and the walk to the doorway ({F(ZWorld.ShuttleExitDoorSec)} s)");
                }
            }
            finally { ZWorld.ShuttleExitSoonerOn = was; ZWorld.ShuttleRoughProofNext = false; }
            sb.AppendLine($"K426 result: {pass} PASS, {fail} FAIL");
            return sb.ToString();
        }
    }
}
