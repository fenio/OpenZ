using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K256b (the polish round): B (boarding at the ramp foot), B2 (the ramp's gates), C (the eased flight's time), D (the exit onto the
    /// back slots) and H (everyone aboard lost) take the polish round's rules.
    /// DV118 the level 16 shuttle as the player's transport (Sim/ZWorld.Shuttle.cs), counted headless (ZWorld.Create + Tick, no scene,
    /// nothing saved). The level 16 map with red as the player (blue a CPU team that gives no orders of its own), and a plain map for
    /// the weapons:
    ///  A. map load: switch off = the decoration of DV95 as before; switch on = the playable shuttle in its place (a red vehicle, the
    ///     light tank's hit points x 1.2 (DV131), one pilot, no weapon, never snipable, closes no tile); the option SHUTTLE off = no shuttle at all,
    ///     the ship's 16 tiles open, every other object the same; other levels: no shuttle.
    ///  B. boarding: a squad ordered into it walks to its back, boards at the ramp foot with the ramp fully down (within its walk + the
    ///     ramp's opening + 1 s) as the APC's passengers; C. a move order (after the ramp shut): take-off, climb, eased flight, landing
    ///     at the target, the time 2 + d / v + 2.3 + 2 s from the constants (the S-curve), nobody gets out by himself, boarding refused
    ///     while it is up;
    ///     D. EXIT on the ground: the ramp comes down, then the squad stands on its back slots behind the ship as one squad;
    ///     E. targets where it cannot land (units, water): it hovers, EXIT refused, then it lands elsewhere;
    ///     F. it hovers over a flag and the flag stays as it was; G. targets far outside the map: it stays inside the map;
    ///     H. destroyed up in the air or on the ground: everyone aboard is lost, nobody gets out;
    ///  I. every weapon reaches it up in the air (one attacker of each type on a plain map, and the blue fort's own gun on level 16),
    ///     and the blasts aimed at it never hurt the unit standing under it;
    ///  J. the CPU never uses it: an order from a CPU team is refused, and in a whole computer match it never moves or takes anyone.
    /// Menu OpenZ/V1.1/Shuttle: DV118 Check.
    /// </summary>
    public static class OzV11ShuttleCount
    {
        const double Tick = ZConst.TICK;

        static void Run(ZWorld w, double secs, Func<bool> until = null)
        {
            double end = w.Time + secs;
            while (w.Time < end) { w.Tick(Tick); if (until != null && until()) return; }
        }

        static int Count(ZWorld w, Team t, string type) => w.Objects.Count(o => o.Owner == t && o.Type == type && !o.Destroyed && o.KillAt < 0);

        /// <summary>The nearest tile middle (ring by ring from (cx, cy)) where a robot can stand: ground, open for robots, nothing on it.</summary>
        static bool RobotSpot(ZWorld w, int cx, int cy, int minR, out int px, out int py)
        {
            for (int r = minR; r < 600; r += 8)
                for (int a = 0; a < 24; a++)
                {
                    double g = a * Math.PI / 12;
                    int x = cx + (int)(Math.Cos(g) * r), y = cy + (int)(Math.Sin(g) * r), tx = x >> 4, ty = y >> 4;
                    if (tx < 2 || ty < 2 || tx >= w.Map.width - 2 || ty >= w.Map.height - 2) continue;
                    if (w.Map.ClassAt(tx, ty) != ZClass.Ground || !w.Path.TilePassable(tx, ty, true) || !w.Path.TilePassable(tx, ty, false)) continue;
                    int mx = tx * 16 + 8, my = ty * 16 + 8;
                    if (w.Objects.Any(o => !o.Destroyed && o.Contains(mx, my))) continue;
                    px = mx; py = my; return true;
                }
            px = py = 0; return false;
        }

        /// <summary>A target where the shuttle can land, between minR and maxR px from (cx, cy).</summary>
        static bool PadSpot(ZWorld w, ZObject s, int cx, int cy, int minR, int maxR, out int px, out int py)
        {
            for (int r = minR; r <= maxR; r += 16)
                for (int a = 0; a < 32; a++)
                {
                    double g = a * Math.PI / 16;
                    var (x, y) = w.ShuttleClamp(cx + (int)(Math.Cos(g) * r), cy + (int)(Math.Sin(g) * r));
                    if (w.ShuttlePadFree(x, y, s, out _)) { px = x; py = y; return true; }
                }
            px = py = 0; return false;
        }

        /// <summary>K256b: a landing target between minR and maxR px straight ahead of the ship (then a little to either side).</summary>
        static bool PadAhead(ZWorld w, ZObject s, int minR, int maxR, out int px, out int py)
        {
            double hd = s.ShuttleHeadingDeg * Math.PI / 180.0;
            foreach (int off in new[] { 0, 6, -6, 12, -12, 18, -18, 24, -24, 30, -30, 36, -36, 42, -42, 48, -48 })   // within 48 deg the climb has turned it
                for (int r = minR; r <= maxR; r += 16)
                {
                    double g = hd + off * Math.PI / 180.0;
                    var (x, y) = w.ShuttleClamp(s.CenterX + (int)Math.Round(Math.Cos(g) * r), s.CenterY + (int)Math.Round(Math.Sin(g) * r));
                    if (w.ShuttlePadFree(x, y, s, out _)) { px = x; py = y; return true; }
                }
            px = py = 0; return false;
        }

        /// <summary>K256b: until the ramp is shut and nobody is on it (a take-off is held until then).</summary>
        static void RampShut(ZWorld w, ZObject s) => Run(w, 20, () => s.ShuttleRampPhase == ZShuttleRamp.Shut && !s.ShuttleExitPending && w.Time >= s.ShuttleRampHoldUntil);

        /// <summary>The target nearest to (cx, cy) whose pad has water on it (and nothing else in the way first).</summary>
        static bool WaterSpot(ZWorld w, ZObject s, int cx, int cy, out int px, out int py)
        {
            px = py = 0; long best = long.MaxValue;
            for (int ty = 3; ty < w.Map.height - 3; ty++)
                for (int tx = 3; tx < w.Map.width - 3; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8;
                    long d = (long)(x - cx) * (x - cx) + (long)(y - cy) * (y - cy);
                    if (d >= best) continue;
                    if (!w.ShuttlePadFree(x, y, s, out string why) && why == "water") { best = d; px = x; py = y; }
                }
            return best < long.MaxValue;
        }

        static string Flight(ZWorld w, ZObject s, int gx, int gy, out bool landed, out double secs, out bool inside, out double topM, double max = 400)
        {
            OzV11Kit.PlayerMove(w, s, gx, gy);
            double t0 = w.Time, top = 0; bool inMap = true; int wpx = w.Map.WidthPix, hpx = w.Map.HeightPix;
            var states = new List<ZShuttleState> { s.ShuttleState }; bool left = false;
            Run(w, max, () =>
            {
                if (s.ShuttleState != states[states.Count - 1]) states.Add(s.ShuttleState);
                if (s.ShuttleState != ZShuttleState.Landed) left = true;
                top = Math.Max(top, s.ShuttleHeightM);
                if (s.LocX < 0 || s.LocY < 0 || s.LocX + s.WidthPix > wpx || s.LocY + s.HeightPix > hpx) inMap = false;
                return s.Destroyed || (left && w.Time - t0 > 0.5 && (s.ShuttleState == ZShuttleState.Landed || s.ShuttleState == ZShuttleState.Hover));   // K256b: a take-off held for the ramp comes first
            });
            secs = w.Time - t0; landed = s.ShuttleState == ZShuttleState.Landed; topM = top; inside = inMap;
            return string.Join(" > ", states);
        }

        public static string Check(string settings, string m16, string m02 = null)
        {
            var sb = new StringBuilder();
            bool was = ZWorld.ShuttlePlayableDeviation, wasOn = ZWorld.ShuttleOn; var team0 = ZWorld.ShuttleTeam;
            try
            {
                ZWorld.ShuttleTeam = Team.Red;
                // ---------------------------------------------------------------- A. map load
                ZWorld.ShuttleOn = true; ZWorld.ShuttlePlayableDeviation = false; var w0 = ZWorld.Create(settings, m16, 1);
                ZWorld.ShuttlePlayableDeviation = true; var w1 = ZWorld.Create(settings, m16, 1);
                ZWorld.ShuttleOn = false; var w2 = ZWorld.Create(settings, m16, 1);
                ZWorld.ShuttleOn = true;
                var s1 = w1.PlayableShuttle; var lt = w1.S.Unit("light");
                var deco = w0.L16Shuttle;
                bool same1 = deco != null && w0.Objects.Count == w1.Objects.Count && w0.Objects.Zip(w1.Objects, (a, b) => a == deco ? b == s1 && a.Id == b.Id : a.Id == b.Id && a.Type == b.Type && a.LocX == b.LocX && a.LocY == b.LocY).All(x => x);
                var w0rest = w0.Objects.Where(o => o != deco).Select(o => o.Type + "@" + o.LocX + "," + o.LocY).ToList();
                var w2all = w2.Objects.Select(o => o.Type + "@" + o.LocX + "," + o.LocY).ToList();
                sb.AppendLine($"A. switch off: the decoration {(deco != null ? "stands" : "MISSING")} ({w0.Objects.Count} objects, its square {w0.L16ShuttleOpenTiles()} of 16 tiles open); " +
                              $"switch on: {(s1 != null ? $"the playable shuttle #{s1.Id} {s1.Type} of {s1.Owner} at {s1.CenterX},{s1.CenterY}, {s1.MaxHealth} HP (light tank {lt?.MaxHealth}), pilots {s1.Drivers.Count} ({s1.DriverType}), damage {s1.Damage}, snipable {s1.CanBeSniped()}, speed {s1.RealMoveSpeed:0.#} px/s" : "NO SHUTTLE")}, " +
                              $"decoration {(w1.L16Shuttle == null ? "none" : "STILL THERE")}, square {w1.L16ShuttleOpenTiles()} of 16 tiles open; option off: shuttle {(w2.PlayableShuttle == null ? "none" : "THERE")}, decoration {(w2.L16Shuttle == null ? "none" : "THERE")}, square {w2.L16ShuttleOpenTiles()} of 16 open, {w2.Objects.Count} objects");
                sb.AppendLine(OzV11Kit.Verdict(deco != null && w0.PlayableShuttle == null && w0.L16ShuttleOpenTiles() == 0, "switch off: the level 16 decoration of DV95 as before, no playable shuttle"));
                sb.AppendLine(OzV11Kit.Verdict(s1 != null && w1.L16Shuttle == null && s1.Kind == ZKind.Vehicle && s1.Owner == Team.Red && lt != null && s1.MaxHealth == ZWorld.ShuttleHpFrom(lt.MaxHealth) /* DV131 (K443) */ && s1.Health == s1.MaxHealth
                    && s1.Drivers.Count == 1 && s1.Damage == 0 && !s1.CanAttack() && !s1.CanBeSniped() && s1.ShuttleState == ZShuttleState.Landed && same1 && w1.L16ShuttleOpenTiles() == 16 && w1.L16ShuttleHutRemoved,
                    "switch on: the playable shuttle in the decoration's place (same id, every other object the same), red, the light tank's hit points x 1.2 (DV131), one pilot, no weapon, never snipable, on the ground, its 16 tiles open"));
                sb.AppendLine(OzV11Kit.Verdict(w2.PlayableShuttle == null && w2.L16Shuttle == null && w2.L16ShuttleOptionOff && w2.L16ShuttleHutRemoved && w2.L16CampHutRemoved && w2.L16ShuttleOpenTiles() == 16 && w0rest.SequenceEqual(w2all),
                    "option SHUTTLE off: no shuttle at all on level 16 (nor the decoration), its 16 tiles open, every other object as with the decoration"));
                if (m02 != null)
                {
                    var wl = ZWorld.Create(settings, m02, 1);
                    sb.AppendLine(OzV11Kit.Verdict(wl.PlayableShuttle == null && !wl.Objects.Any(o => o.IsShuttle), "another level (L02) with the switch on: no shuttle"));
                }

                // ---------------------------------------------------------------- B. boarding
                var w = OzV11Kit.World(settings, m16, 1);
                var s = w.PlayableShuttle;
                RobotSpot(w, s.CenterX, s.CenterY, 56, out int rx, out int ry);
                var sq = OzV11Kit.Place(w, "grunt", rx, ry, Team.Red); int squadN = 1 + sq.Minions.Count;
                int grunts0 = Count(w, Team.Red, "grunt"), atFoot0 = ZWorld.ShuttleBoardsAtFoot;
                var walk = new OzV11Kit.Motion(sq); double tb0 = w.Time, rampAt = -1, nearHull = 99;
                OzV11Kit.PlayerEnter(w, sq, s);
                Run(w, 60, () =>
                {
                    if (sq.KillAt < 0) { walk.Step(sq, w.Time); foreach (var m in OzV11Kit.Squad(sq)) if (m.KillAt < 0) nearHull = Math.Min(nearHull, OzV11Kit.Dist(m, s) / 8.0); }
                    if (s.Passengers.Count > 0) rampAt = s.ShuttleRamp01;
                    return s.Passengers.Count > 0;
                });
                double tAboard = w.Time - tb0, walkSecs = walk.MovingTicks * Tick;
                bool boarded = s.Passengers.Count == squadN && s.PassengerType == "grunt" && Count(w, Team.Red, "grunt") == grunts0 - squadN && s.Drivers.Count == 1;
                bool atBack = ZWorld.ShuttleBoardsAtFoot == atFoot0 + 1 && Math.Abs(rampAt - 1) < 1e-9 && tAboard <= walkSecs + ZWorld.ShuttleRampOpenSec + 1.0 && nearHull >= (ZWorld.ShuttleRadiusPx + 6) / 8.0 - 0.01;
                sb.AppendLine($"B. a red grunt squad of {squadN} at {rx},{ry} ordered into it: aboard {s.Passengers.Count} {s.PassengerType} after {tAboard:0.0} s (the leader walked {walkSecs:0.0} s; the ramp's opening {ZWorld.ShuttleRampOpenSec} s), pilot {s.Drivers.Count}; " +
                              $"boarded at the ramp foot {ZWorld.ShuttleBoardsAtFoot - atFoot0}x with the ramp at {rampAt:0.00}; nearest any member came to the ship's middle {nearHull:0.00} m (the spacing ring {(ZWorld.ShuttleRadiusPx + 6) / 8.0:0.00} m)");
                sb.AppendLine(OzV11Kit.Verdict(boarded, "a squad ordered into the shuttle boards it as its passengers (the APC's rule), the pilot stays"));
                sb.AppendLine(OzV11Kit.Verdict(atBack, "it boards at the ramp foot behind the ship with the ramp fully down, within its walk + the ramp's opening + 1 s, never inside the hull's spacing ring"));

                // ---------------------------------------------------------------- B2. the ramp's gates (K256b)
                {
                    var wb = OzV11Kit.World(settings, m16, 5); var sbs = wb.PlayableShuttle;
                    RobotSpot(wb, sbs.CenterX, sbs.CenterY, 56, out int qx2, out int qy2);
                    var q2 = OzV11Kit.Place(wb, "grunt", qx2, qy2, Team.Red); OzV11Kit.PlayerEnter(wb, q2, sbs); Run(wb, 60, () => sbs.Passengers.Count > 0);
                    bool exitWhileIn = sbs.CanEjectCrew() || wb.Eject(Team.Red, sbs);
                    Run(wb, 0.5);
                    PadSpot(wb, sbs, sbs.CenterX, sbs.CenterY, 320, 640, out int mx2, out int my2);
                    int shuts0 = sbs.ShuttleRampShuts, notesB = wb.ShuttleNoteSeq; double tShut = -1, tOff = -1; bool held = false;
                    OzV11Kit.PlayerMove(wb, sbs, mx2, my2);
                    Run(wb, 20, () =>
                    {
                        if (sbs.ShuttleTakeOffHeld) held = true;
                        if (tShut < 0 && sbs.ShuttleRampShuts > shuts0) tShut = wb.Time;
                        if (tOff < 0 && wb.ShuttleNotes.Any(n => n.seq > notesB && n.kind == ZShuttleNoteKind.TakeOff)) tOff = wb.Time;
                        return tOff >= 0;
                    });
                    sb.AppendLine($"B2. right after a boarding: EXIT {(exitWhileIn ? "ALLOWED" : "refused")} while the squad walks up the ramp; a move order 0.5 s after the boarding: held {held}, the ramp shut at {tShut:0.00} s, take-off at {tOff:0.00} s");
                    sb.AppendLine(OzV11Kit.Verdict(!exitWhileIn && held && tShut > 0 && tOff >= tShut - 1e-9, "the ramp's gates: no EXIT while the squad walks in; a move order meanwhile waits and takes off the moment the ramp has shut"));
                }

                // ---------------------------------------------------------------- C. flight and landing
                RampShut(w, s);                                                                                       // K256b: the ramp shuts after the boarding
                PadAhead(w, s, 320, 640, out int gx, out int gy);
                double dist = Math.Sqrt((double)(gx - s.CenterX) * (gx - s.CenterX) + (double)(gy - s.CenterY) * (gy - s.CenterY));
                int g0 = Count(w, Team.Red, "grunt"), notes0 = w.ShuttleNoteSeq;
                OzV11Kit.PlayerMove(w, s, gx, gy);
                Run(w, 0.5);
                RobotSpot(w, s.CenterX, s.CenterY, 72, out int r2x, out int r2y);
                var sq2 = OzV11Kit.Place(w, "psycho", r2x, r2y, Team.Red);
                OzV11Kit.PlayerEnter(w, sq2, s);
                bool refusedUp = sq2.Waypoints.Count == 0 && !s.CanTakePassengers(Team.Red) && !s.CanEjectCrew() && !w.Eject(Team.Red, s);
                string path = Flight(w, s, gx, gy, out bool landed, out double secs, out bool inside, out double top);
                double want = ZWorld.ShuttleClimbSec + ZWorld.ShuttleStraightSec(dist, s.RealMoveSpeed) + ZWorld.ShuttleDescendSec;   // K256b: the S-curve speeding up and the planned stop cost 2.3 s
                var kinds = w.ShuttleNotes.Where(n => n.seq > notes0).Select(n => n.kind.ToString()).ToList();
                sb.AppendLine($"C. move order to {gx},{gy} ({dist:0} px): {path}; {(landed ? "landed" : "NOT landed")} after {secs + 0.5:0.00} s (2 + d / v + 2.3 + 2 = {want:0.00} s + its landing turn {s.ShuttleLastLandTurnSec:0.00} s), top {top:0.0} m, at {s.CenterX},{s.CenterY}; " +
                              $"notes {string.Join(", ", kinds)}; aboard {s.Passengers.Count}, red grunts on foot {g0} -> {Count(w, Team.Red, "grunt")}; a psycho squad ordered in while it was up: {(refusedUp ? "refused" : "NOT refused")}, EXIT up: {(refusedUp ? "refused" : "?")}");
                sb.AppendLine(OzV11Kit.Verdict(landed && Math.Abs(secs + 0.5 - want - s.ShuttleLastLandTurnSec) < 0.25 && Math.Abs(top - ZWorld.ShuttleCruiseM) < 0.01 && s.CenterX == gx && s.CenterY == gy && inside
                    && s.Passengers.Count == squadN && Count(w, Team.Red, "grunt") == g0 && kinds.Count >= 3 && kinds[0] == "TakeOff" && kinds.Contains("Arrived") && kinds[kinds.Count - 1] == "Landed",
                    "a move order: take-off, climb to the cruise height, eased flight (2 + d / v + 2.3 + 2 s), landing on the target; nobody gets out by himself"));
                sb.AppendLine(OzV11Kit.Verdict(refusedUp, "while it is up no squad can board it and EXIT is refused"));

                // ---------------------------------------------------------------- D. unload on the ground
                bool roughC = s.ShuttleRoughLanding; Run(w, ZWorld.ShuttleRoughSettleSec + 0.1, () => s.ShuttleLevelled);   // a rough landing: EXIT once it stands level (2.5 s)
                bool canExit = s.CanEjectCrew();
                var redBefore = new HashSet<ZObject>(w.Objects.Where(o => o.Owner == Team.Red && o.Type == "grunt"));
                bool ej = w.Eject(Team.Red, s); double te0 = w.Time; bool downFirst = s.Passengers.Count == squadN && s.ShuttleExitPending;
                Run(w, ZWorld.ShuttleRampOpenSec + 1.0, () => s.Passengers.Count == 0);
                double tOut = w.Time - te0, rampOut = s.ShuttleRamp01;
                var outs = w.Objects.Where(o => o.Owner == Team.Red && o.Type == "grunt" && !o.Destroyed && o.KillAt < 0 && !redBefore.Contains(o)).ToList();
                var lead = outs.FirstOrDefault(o => !o.IsMinion);
                var line = lead == null ? new List<ZObject>() : OzV11Kit.Squad(lead).ToList();
                double worstSlot = 0;
                for (int k = 0; k < line.Count; k++) { var (bx0, by0) = s.ShuttleBackPoint(line.Count - k); worstSlot = Math.Max(worstSlot, Math.Sqrt((double)(line[k].CenterX - bx0) * (line[k].CenterX - bx0) + (double)(line[k].CenterY - by0) * (line[k].CenterY - by0))); }
                bool behind = line.Count > 0 && line.All(o => OzV11Kit.Dist(o, s) / 8.0 >= ZWorld.ShuttleBackM + ZWorld.ShuttleFileM - 0.3 && o.Waypoints.Count == 0);
                sb.AppendLine($"D. EXIT on the ground{(roughC ? " (after a rough landing, once it stood level)" : "")}: allowed {canExit}, done {ej} (the ramp first: {downFirst}); out after {tOut:0.00} s with the ramp at {rampOut:0.00}; aboard now {s.Passengers.Count}; red grunts out {outs.Count} (one squad: {(lead != null && lead.Minions.Count + 1 == squadN ? "yes" : "no")}), " +
                              $"standing on their back slots (worst {worstSlot:0.0} px off), all behind the ship, no waypoint: {behind}");
                sb.AppendLine(OzV11Kit.Verdict(canExit && ej && downFirst && s.Passengers.Count == 0 && Math.Abs(rampOut - ZWorld.ShuttleExitRamp01) <= 0.02 && tOut <= ZWorld.ShuttleExitAfterSec + 0.05 && outs.Count == squadN && lead != null && lead.Minions.Count + 1 == squadN && worstSlot <= 2 && behind,
                    "landed: EXIT brings the ramp down (K422: the squad gets up while it comes down, let out at the rules' moment), then the squad stands in a line on its back slots behind the ship as one squad"));

                // ---------------------------------------------------------------- E. where it cannot land: units, water
                OzV11Kit.PlayerEnter(w, lead, s); Run(w, 60, () => s.Passengers.Count > 0);
                PadSpot(w, s, s.CenterX, s.CenterY, 300, 600, out int ux, out int uy);
                var blocker = OzV11Kit.Place(w, "sniper", ux, uy, Team.Red, 1); blocker.MoveSpeed = 0; blocker.Damage = 0;
                string p1 = Flight(w, s, ux, uy, out bool l1, out _, out _, out _);
                bool hov1 = !l1 && s.ShuttleState == ZShuttleState.Hover && s.ShuttleNoLand == "units" && !s.CanEjectCrew() && !w.Eject(Team.Red, s) && s.Passengers.Count == squadN;
                WaterSpot(w, s, s.CenterX, s.CenterY, out int wx, out int wy);
                string p2 = Flight(w, s, wx, wy, out bool l2, out _, out _, out _);
                bool hov2 = !l2 && s.ShuttleState == ZShuttleState.Hover && s.ShuttleNoLand == "water" && !s.CanEjectCrew() && !w.Eject(Team.Red, s) && s.Passengers.Count == squadN;
                w.SendWaypoints(Team.Red, s, new ZWaypoint[0]); Run(w, 5);
                bool stopHover = s.ShuttleState == ZShuttleState.Hover;
                PadSpot(w, s, s.CenterX, s.CenterY, 64, 900, out int bx, out int by);
                string p3 = Flight(w, s, bx, by, out bool l3, out _, out _, out _);
                Run(w, ZWorld.ShuttleRoughSettleSec + 0.1, () => s.ShuttleLevelled);                                    // a rough landing: EXIT once it stands level
                sb.AppendLine($"E. onto a standing unit at {ux},{uy}: {p1}, why '{(hov1 ? "units" : s.ShuttleNoLand)}'; onto water at {wx},{wy}: {p2}; STOP there: {(stopHover ? "still hovering" : "NOT hovering")}; then a free place {bx},{by}: {p3}, EXIT allowed {s.CanEjectCrew()}");
                sb.AppendLine(OzV11Kit.Verdict(hov1 && hov2 && stopHover && l3 && s.CanEjectCrew(), "a target with a unit or water on it: it hovers there, EXIT refused, until a move to a free place lands it"));

                // ---------------------------------------------------------------- F. flags
                var flag = w.Flags.Where(f => f.Owner != Team.Red).OrderBy(f => Math.Abs(f.CenterX - s.CenterX) + Math.Abs(f.CenterY - s.CenterY)).FirstOrDefault();
                var fOwner = flag?.Owner ?? Team.Neutral;
                string pf = flag != null ? Flight(w, s, flag.CenterX, flag.CenterY, out _, out _, out _, out _) : "no flag";
                Run(w, 5);
                sb.AppendLine($"F. over the {fOwner} flag at {flag?.CenterX},{flag?.CenterY}: {pf}, why '{s.ShuttleNoLand}', 5 s later the flag is {flag?.Owner}");
                sb.AppendLine(OzV11Kit.Verdict(flag != null && s.ShuttleState == ZShuttleState.Hover && flag.Owner == fOwner && s.IntersectsObject(flag), "it never takes a flag (it hovers over one and the flag stays as it was)"));

                // ---------------------------------------------------------------- G. map edge
                int hpG = s.Health, blueG = 0;                                                                       // K256e: the map edge is the point here, not the fire: the flights to the
                foreach (var o in w.Objects.ToList()) if (o.Owner == Team.Blue && o.IsUnit && !o.Destroyed && o.KillAt < 0) { o.KillAt = w.Time; blueG++; }   // corners cross the blue base
                Run(w, 0.2);
                var gA = w.ShuttleClamp(-5000, -5000); var gB = w.ShuttleClamp(500000, 500000);
                string pa = Flight(w, s, -5000, -5000, out _, out double ta, out bool inA, out _, 600);
                var atA = (s.CenterX, s.CenterY);
                string pb = Flight(w, s, 500000, 500000, out _, out double tb, out bool inB, out _, 600);
                var atB = (s.CenterX, s.CenterY);
                sb.AppendLine($"G. ordered far outside the map: to {gA} ({pa}, {ta:0} s, now {atA}) and to {gB} ({pb}, {tb:0} s, now {atB}); inside the map all the way: {inA && inB}; (blue units and guns taken away first: {blueG}; hit points before {hpG}, destroyed on the way {s.Destroyed})");
                sb.AppendLine(OzV11Kit.Verdict(inA && inB && atA == gA && atB == gB && gA.x == ZWorld.ShuttleEdgeTiles * 16 + 8 && gB.x == (w.Map.width - 1 - ZWorld.ShuttleEdgeTiles) * 16 + 8,
                    "a target outside the map is moved inside it; the shuttle never leaves the map"));

                // ---------------------------------------------------------------- H. destroyed in the air / on the ground
                var wa = OzV11Kit.World(settings, m16, 2); var sa = wa.PlayableShuttle;
                RobotSpot(wa, sa.CenterX, sa.CenterY, 56, out int ax, out int ay);
                var qa = OzV11Kit.Place(wa, "grunt", ax, ay, Team.Red); OzV11Kit.PlayerEnter(wa, qa, sa); Run(wa, 60, () => sa.Passengers.Count > 0);
                PadSpot(wa, sa, sa.CenterX, sa.CenterY, 400, 700, out int hx, out int hy);
                OzV11Kit.PlayerMove(wa, sa, hx, hy); Run(wa, 30, () => sa.ShuttleState == ZShuttleState.Cruise);
                int ga0 = Count(wa, Team.Red, "grunt"), aboardA = sa.Passengers.Count;
                sa.DamageHealth(sa.Health); wa.UpdateObjectHealth(sa); Run(wa, 0.5);
                bool crashed = sa.Destroyed && wa.ShuttlePassengersLost == aboardA && Count(wa, Team.Red, "grunt") == ga0 && wa.ShuttleNotes.Any(n => n.kind == ZShuttleNoteKind.Crashed && n.aboard == aboardA);
                var wg = OzV11Kit.World(settings, m16, 3); var sg = wg.PlayableShuttle;
                RobotSpot(wg, sg.CenterX, sg.CenterY, 56, out int qx, out int qy);
                var qg = OzV11Kit.Place(wg, "grunt", qx, qy, Team.Red); OzV11Kit.PlayerEnter(wg, qg, sg); Run(wg, 60, () => sg.Passengers.Count > 0); Run(wg, 20, () => wg.Time >= sg.ShuttleRampHoldUntil);   // K418 (DV126): every one past the doorway first (one still on the ramp survives)
                int gg0 = Count(wg, Team.Red, "grunt"), aboardG = sg.Passengers.Count;
                sg.DamageHealth(sg.Health); wg.UpdateObjectHealth(sg); Run(wg, 0.5);
                bool wrecked = sg.Destroyed && Count(wg, Team.Red, "grunt") == gg0 && aboardG == squadN && wg.ShuttlePassengersLost == aboardG && wg.ShuttleNotes.Any(n => n.kind == ZShuttleNoteKind.Wrecked && n.aboard == aboardG);   // K256b: all lost on the ground too
                sb.AppendLine($"H. shot down in the air with {aboardA} aboard: destroyed {sa.Destroyed}, lost {wa.ShuttlePassengersLost}, red grunts {ga0} -> {Count(wa, Team.Red, "grunt")}; destroyed on the ground with {aboardG} aboard: lost {wg.ShuttlePassengersLost}, red grunts {gg0} -> {Count(wg, Team.Red, "grunt")}");
                sb.AppendLine(OzV11Kit.Verdict(crashed, "shot down in the air: it crashes, the passengers are lost"));
                sb.AppendLine(OzV11Kit.Verdict(wrecked, "destroyed on the ground: the passengers are lost too, nobody gets out"));

                // ---------------------------------------------------------------- I. every weapon reaches it up there
                bool strafeWas = ZWorld.ShuttleStrafeOn; ZWorld.ShuttleStrafeOn = false;   // what reaches it and what leaks below, without the strafe (with it the attackers may lose the ship's range and shoot the robot under it itself; the strafe's own counts: the kit's A2)
                bool allHit, nobodyBelow;
                try { sb.Append(Weapons(settings, m16, out allHit, out nobodyBelow)); } finally { ZWorld.ShuttleStrafeOn = strafeWas; }
                sb.AppendLine(OzV11Kit.Verdict(allHit, "every weapon hits the shuttle up in the air"));
                sb.AppendLine(OzV11Kit.Verdict(nobodyBelow, "a blast aimed at the shuttle up in the air never hurts the unit under it"));

                // ---------------------------------------------------------------- J. the CPU never uses it
                var wc = ZWorld.Create(settings, m16, 4); wc.Bots.Add(new ZBot(wc, Team.Blue, 105)); wc.Bots.Add(new ZBot(wc, Team.Red, 4)); wc.Events.Clear();
                var sc = wc.PlayableShuttle; int cx0 = sc.CenterX, cy0 = sc.CenterY;
                bool refused = !wc.SendWaypoints(Team.Red, sc, new[] { ZWaypoint.Move(cx0 + 300, cy0) });
                Run(wc, 600, () => wc.GameOver);
                bool unused = refused && sc.ShuttleFlights == 0 && sc.CenterX == cx0 && sc.CenterY == cy0 && sc.Passengers.Count == 0 && sc.ShuttleState == ZShuttleState.Landed;
                sb.AppendLine($"J. a computer match on level 16 (both teams CPU, {wc.Time:0} s{(wc.GameOver ? ", over" : "")}): the red CPU's orders to the shuttle refused {wc.ShuttleCpuOrdersRefused} (the first one by the check), flights {sc.ShuttleFlights}, aboard {sc.Passengers.Count}, still at {sc.CenterX},{sc.CenterY}{(sc.Destroyed ? ", destroyed by the enemy" : "")}");
                sb.AppendLine(OzV11Kit.Verdict(unused, "the CPU never uses the shuttle (its orders are refused, it never flies and never takes a squad)"));
            }
            catch (Exception ex) { sb.AppendLine("FAIL  the check threw " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace); }
            finally { ZWorld.ShuttlePlayableDeviation = was; ZWorld.ShuttleOn = wasOn; ZWorld.ShuttleTeam = team0; }
            return sb.ToString();
        }

        /// <summary>The attacker types of the table (robots, vehicles, guns) and the grunt's grenades; the fort gun on level 16.</summary>
        public static readonly string[] Attackers = { "grunt", "psycho", "sniper", "tough", "pyro", "laser", ZElectro.Type, "jeep", "light", "medium", "heavy", "missile_launcher", "apc", "gatling", "gun", "howitzer", "missile_cannon" };

        /// <summary>I: one blue attacker of each type on a plain map attacks the red shuttle hovering at 12 m over a red robot that
        /// cannot move or shoot (the shuttle cannot land on it); the grunts' grenades; the blue fort's gun on level 16.</summary>
        public static string Weapons(string settings, string m16, out bool allHit, out bool nobodyBelow)
        {
            var sb = new StringBuilder("I. every weapon against the shuttle hovering at 12 m (60 s, one attacker at 0.7 of its range):\n");
            allHit = true; nobodyBelow = true;
            var map = OzV11Kit.Plain(80, 80);
            var kinds = Attackers.Concat(new[] { "grenades" }).ToList();
            foreach (var k in kinds)
            {
                ZWorld.ShuttlePlayableDeviation = true;
                var w = OzV11Kit.World(settings, map, 1);
                OzV11Kit.Dummy(w, 70 * 16, 70 * 16);
                var s = w.CreatePlayableShuttle(400, 640, Team.Red);
                s.ShuttleState = ZShuttleState.Hover; s.ShuttleLift = 1;
                var under = OzV11Kit.Place(w, "tough", 400, 640, Team.Red, 1); under.MoveSpeed = 0; under.Damage = 0; under.MaxHealth = under.Health = 1000000;
                string type = k == "grenades" ? "grunt" : k;
                var u = w.S.Unit(type);
                int range = Math.Max(40, u?.attack_radius ?? 100);
                var a = OzV11Kit.Place(w, type, 400 + (int)(range * 0.7), 640, Team.Blue);
                if (k == "grenades") a.Grenades = 3; else if (a.IsRobot) { a.Grenades = 0; foreach (var m in a.Minions) m.Grenades = 0; }
                OzV11Kit.CpuAttack(w, a, s);
                int hp0 = s.Health, hits = 0, last = s.Health; double first = -1;
                double t0 = w.Time;
                Run(w, 60, () =>
                {
                    if (s.Health < last) { hits++; last = s.Health; if (first < 0) first = w.Time - t0; }
                    return s.Destroyed;
                });
                bool hit = hits > 0, below = under.Health == under.MaxHealth;
                allHit &= hit; nobodyBelow &= below;
                sb.AppendLine($"   {k,-17} {(hit ? "HIT" : "NO HIT")}: {hits} hits, {hp0 - Math.Max(0, s.Health)} HP of {hp0}{(s.Destroyed ? " (shot down)" : "")}, first after {(first < 0 ? "-" : first.ToString("0.0"))} s; the robot under it lost {under.MaxHealth - under.Health} HP; the attacker's weapon: {(a.DamageIsMissile || k == "grenades" ? "rocket / shell / grenade (flies at it, the game's lead)" : "direct hit")}");
            }
            // the blue fort's own gun (DV7), level 16
            {
                var w = OzV11Kit.World(settings, m16, 1);
                var fort = w.Objects.First(o => o.IsFort && o.Owner == Team.Blue);
                var s = w.PlayableShuttle;
                bool hit = false; int dist = 0, hits = 0, hp0 = 0;
                foreach (int d in new[] { 200, 280, 360 })
                {
                    s.ShuttlePlace(fort.CenterX, fort.CenterY + fort.HeightPix / 2 + d); s.ShuttleState = ZShuttleState.Hover; s.ShuttleLift = 1;
                    s.SetHealth(s.MaxHealth); hp0 = s.Health; int last = s.Health; hits = 0;
                    Run(w, 60, () => { if (s.Health < last) { hits++; last = s.Health; } return s.Destroyed; });
                    if (hits > 0) { hit = true; dist = d; break; }
                }
                sb.AppendLine($"   {"fort gun",-17} {(hit ? "HIT" : "NO HIT")}: {hits} hits at {dist} px south of the blue fort's wall, {hp0 - Math.Max(0, s.Health)} HP");
                allHit &= hit;
            }
            return sb.ToString();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Shuttle: DV118 Check")]
        static void Menu() => OzV11Kit.RunMenu("DV118 the level 16 shuttle", () => Check(OzV11Kit.SettingsText, OzV11Kit.MapText("L16"), OzV11Kit.MapText("L02")));
#endif
    }
}
