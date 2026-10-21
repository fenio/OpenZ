using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K256f, the EMPTY shuttle and EXIT in the air (rule DV118, Sim/ZWorld.ShuttleEmpty.cs), counted headless (ZWorld.Create + Tick, no
    /// scene, nothing saved; public members only, like every check of the editor assembly). Menu OpenZ/V1.1/Shuttle: K256f Empty Shuttle Check.
    ///  A. an empty shuttle dropped by the testing panel's rule (ZWorld.SpawnShuttle with the null team) beside each fort of level 16, 2 and 17
    ///     (17 with its parked empty start vehicles), with idle robot squads of that side next to it and an empty jeep beside them (the
    ///     control: the game's auto-grab is running), the blue CPU playing: in soakSec s nobody walks into it (no ENTER waypoint on it, no
    ///     unordered one dropped), it never flies, its ramp never opens, it stays empty and neutral;
    ///  B. a red grunt squad of 3 ordered into an empty one walks to its back and boards up the ramp: 1 pilot (the leader) + 2 passengers,
    ///     the ship red; C. it flies on an order, lands, EXIT puts out the 2 (one squad), the pilot stays, EXIT again refused, it flies on
    ///     with the pilot alone; D. a lone robot ordered in: pilot only, EXIT refused; a second squad then boards it as its passengers and
    ///     gets out on EXIT; D2. ALAN &amp; BRAD ordered in: Brad the pilot, Alan out as himself on EXIT; E. shot down in the air: all aboard
    ///     lost (nobody gets out);
    ///  F. EXIT in the air follows the landing test (ZWorld.ShuttlePadFree at the spot it would stop: the cursor's LandableAt): sampled
    ///     every tick of a flight; refused while it climbs out, over water and over a unit; taken over open ground: it lands where it
    ///     stopped and the passengers get out there, the pilot stays; a new order calls it off;
    ///  G. the CPU: its order to enter an empty shuttle is refused; the player's order to the empty ship itself is refused (it never flies);
    ///  H. a map's entry "vehicle:shuttle" ("null" = empty, "red" = crewed); the shuttle rule off: nothing made, every other object the same.
    /// </summary>
    public static class OzShuttleEmptyCount
    {
        const double Tick = ZConst.TICK;
        /// <summary>The soak of A in the parity report's DV118 row (the menu and the headless run use the same).</summary>
        public const double RowSoakSec = 300.0;

        static void Run(ZWorld w, double secs, Func<bool> until = null)
        {
            double end = w.Time + secs;
            while (w.Time < end) { w.Tick(Tick); if (until != null && until()) return; }
        }

        static int Count(ZWorld w, Team t, string type) => w.Objects.Count(o => o.Owner == t && o.Type == type && !o.Destroyed && o.KillAt < 0);
        static string P(int x, int y) => x + "," + y;

        /// <summary>A tile middle (ring by ring from (cx, cy), from minR px) where a robot can stand: ground, open, nothing on it.</summary>
        static bool RobotSpot(ZWorld w, int cx, int cy, int minR, out int px, out int py, int maxR = 600)
        {
            for (int r = minR; r < maxR; r += 8)
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

        /// <summary>A 2 x 2 tile place for a vehicle (its middle, px), ring by ring from (cx, cy) between minR and maxR px.</summary>
        static bool VehicleSpot(ZWorld w, int cx, int cy, int minR, int maxR, out int px, out int py)
        {
            for (int r = minR; r <= maxR; r += 8)
                for (int a = 0; a < 32; a++)
                {
                    double g = a * Math.PI / 16;
                    int tx = (cx + (int)(Math.Cos(g) * r)) >> 4, ty = (cy + (int)(Math.Sin(g) * r)) >> 4;
                    if (tx < 3 || ty < 3 || tx >= w.Map.width - 4 || ty >= w.Map.height - 4) continue;
                    bool ok = true;
                    for (int j = 0; j < 2 && ok; j++) for (int i = 0; i < 2 && ok; i++) ok = w.Map.ClassAt(tx + i, ty + j) == ZClass.Ground && w.Path.TilePassable(tx + i, ty + j, false);
                    if (!ok) continue;
                    int mx = tx * 16 + 16, my = ty * 16 + 16;
                    if (w.Objects.Any(o => !o.Destroyed && o.LocX < mx + 24 && mx - 24 < o.LocX + o.WidthPix && o.LocY < my + 24 && my - 24 < o.LocY + o.HeightPix)) continue;
                    px = mx; py = my; return true;
                }
            px = py = 0; return false;
        }

        /// <summary>A target where shuttle s can land, between minR and maxR px from (cx, cy).</summary>
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

        /// <summary>The testing panel's empty drop (ZWorld.SpawnShuttle, the null team) at the first free place ring by ring from (cx, cy).</summary>
        static ZObject DropEmpty(ZWorld w, int cx, int cy, int minR, int maxR)
        {
            for (int r = minR; r <= maxR; r += 32)
                for (int a = 0; a < 16; a++)
                {
                    double g = a * Math.PI / 8;
                    var s = w.SpawnShuttle(cx + (int)(Math.Cos(g) * r), cy + (int)(Math.Sin(g) * r), Team.Neutral, -90f, out _);
                    if (s != null) return s;
                }
            return null;
        }

        /// <summary>The nearest target to (cx, cy) whose pad has water on it.</summary>
        static bool WaterSpot(ZWorld w, ZObject s, int cx, int cy, out int px, out int py)
        {
            px = py = 0; long best = long.MaxValue;
            for (int ty = 3; ty < w.Map.height - 3; ty++)
                for (int tx = 3; tx < w.Map.width - 3; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8;
                    long d = (long)(x - cx) * (x - cx) + (long)(y - cy) * (y - cy);
                    if (d >= best || (w.Map.ClassAt(tx, ty) != ZClass.Water && w.Map.ClassAt(tx, ty) != ZClass.DeepWater)) continue;
                    if (!w.ShuttlePadFree(x, y, s, out string why) && why == "water") { best = d; px = x; py = y; }
                }
            return best < long.MaxValue;
        }

        static void Enter(ZWorld w, ZObject sq, ZObject s) => OzV11Kit.PlayerEnter(w, sq, s);

        /// <summary>Until the ship has taken the squad (pilot or passengers), at most 60 s.</summary>
        static double Board(ZWorld w, ZObject s, int passengersBefore, bool pilotBefore)
        {
            double t0 = w.Time;
            Run(w, 60, () => pilotBefore ? s.Passengers.Count > passengersBefore : s.Drivers.Count > 0);
            return w.Time - t0;
        }

        /// <summary>A flight to (gx, gy): until it stands on the ground again or hovers (at most max s). True = landed.</summary>
        static bool Fly(ZWorld w, ZObject s, int gx, int gy, double max = 120)
        {
            OzV11Kit.PlayerMove(w, s, gx, gy);
            double t0 = w.Time; bool left = false;
            Run(w, max, () =>
            {
                if (s.ShuttleState != ZShuttleState.Landed) left = true;
                return s.Destroyed || (left && w.Time - t0 > 0.5 && (s.ShuttleState == ZShuttleState.Landed || s.ShuttleState == ZShuttleState.Hover));
            });
            Run(w, ZWorld.ShuttleRoughSettleSec + 0.1, () => s.ShuttleLevelled && w.Time >= s.ShuttleRampHoldUntil);
            return s.ShuttleState == ZShuttleState.Landed;
        }

        static void RampShut(ZWorld w, ZObject s) => Run(w, 20, () => s.ShuttleRampPhase == ZShuttleRamp.Shut && !s.ShuttleExitPending && w.Time >= s.ShuttleRampHoldUntil);

        /// <summary>EXIT on the ground: until the passengers are out (at most the ramp's opening + 3 s). The squad made, else null.</summary>
        static ZObject ExitGround(ZWorld w, ZObject s, out bool taken)
        {
            int pax = s.Passengers.Count;
            taken = w.Eject(s.Owner, s);
            Run(w, ZWorld.ShuttleRampOpenSec + 3.0, () => s.Passengers.Count == 0);
            var outs = Outs(w, s, pax);
            return outs.FirstOrDefault(o => !o.IsMinion);
        }

        /// <summary>The robots the last exit made (the ramp's foot list of an exit, ZObject.ShuttleFootIds), alive; empty when the squad of
        /// `pax` did not get out (the factories go on producing robots meanwhile: a new robot elsewhere is not one of them).</summary>
        static List<ZObject> Outs(ZWorld w, ZObject s, int pax)
        {
            if (s.Passengers.Count != 0 || s.ShuttleFootIds.Count != pax) return new List<ZObject>();
            return s.ShuttleFootIds.Select(id => w.Get(id)).Where(o => o != null && o.IsRobot && !o.Destroyed && o.KillAt < 0).ToList();
        }

        public static string Check(string settings, string m16, string m02, string m17, double soakSec = RowSoakSec)
        {
            var sb = new StringBuilder();
            bool was = ZWorld.ShuttlePlayableDeviation, wasOn = ZWorld.ShuttleOn; var team0 = ZWorld.ShuttleTeam;
            try
            {
                ZWorld.ShuttlePlayableDeviation = true; ZWorld.ShuttleOn = true; ZWorld.ShuttleTeam = Team.Red;
                // ---------------------------------------------------------------- A. nobody walks into an empty shuttle by himself
                bool allA = true; int jeepsTaken = 0, jeeps = 0;
                foreach (var (lvl, map) in new[] { ("L16", m16), ("L02", m02), ("L17", m17) })
                {
                    if (map == null) continue;
                    sb.Append(Soak(settings, map, lvl, soakSec, out bool ok, out int jt, out int jn));
                    allA &= ok; jeepsTaken += jt; jeeps += jn;
                }
                sb.AppendLine(OzV11Kit.Verdict(allA, $"A. an empty shuttle beside idle robots of its side on levels 16, 2 and 17 (the blue CPU playing, {soakSec:0} s): nobody walks into it by himself, it never flies, its ramp stays shut, it stays empty and neutral"));
                sb.AppendLine(OzV11Kit.Verdict(jeepsTaken > 0, $"A. the control: the game's auto-grab runs meanwhile (empty jeeps beside the same robots taken: {jeepsTaken} of {jeeps})"));

                // ---------------------------------------------------------------- B. a squad of 3 ordered in: 1 pilot + 2 passengers
                var w = OzV11Kit.World(settings, m16, 1);
                var parked = w.PlayableShuttle;
                var e1 = DropEmpty(w, parked.CenterX, parked.CenterY, 280, 640);
                bool dropOk = e1 != null && e1.ShuttleIsEmpty && e1.Owner == Team.Neutral && e1.Drivers.Count == 0 && !e1.CanBeEntered() && e1.ShuttleState == ZShuttleState.Landed;
                sb.AppendLine($"B. level 16: the parked ship #{parked.Id} ({parked.Owner}, pilots {parked.Drivers.Count}); the testing panel's empty drop: {(e1 != null ? $"#{e1.Id} at {P(e1.CenterX, e1.CenterY)}, {e1.Owner}, pilots {e1.Drivers.Count}, aboard {e1.Passengers.Count}, 'anyone may enter' {e1.CanBeEntered()}, {e1.MaxHealth} HP" : "REFUSED")}");
                sb.AppendLine(OzV11Kit.Verdict(dropOk, "the testing panel drops an EMPTY shuttle (the null team, no pilot, nobody aboard, standing)"));
                if (e1 == null) return sb.ToString();
                RobotSpot(w, e1.CenterX, e1.CenterY, 56, out int rx, out int ry);
                var sq = OzV11Kit.Place(w, "grunt", rx, ry, Team.Red); int squadN = 1 + sq.Minions.Count;
                int g0 = Count(w, Team.Red, "grunt"), foot0 = ZWorld.ShuttleBoardsAtFoot, taken0 = ZObject.ShuttleEmptyTaken; double rampAt = -1;
                Enter(w, sq, e1);
                bool orderTaken = sq.Waypoints.Count == 1 && sq.Waypoints[0].mode == WpMode.Enter;
                double tB0 = w.Time;
                Run(w, 60, () => { if (e1.Drivers.Count > 0 && rampAt < 0) rampAt = e1.ShuttleRamp01; return e1.Drivers.Count > 0; });
                double tB = w.Time - tB0;
                bool b1 = e1.Drivers.Count == 1 && e1.DriverType == "grunt" && e1.Passengers.Count == squadN - 1 && e1.PassengerType == "grunt" && e1.Owner == Team.Red
                          && Count(w, Team.Red, "grunt") == g0 - squadN && ZWorld.ShuttleBoardsAtFoot == foot0 + 1 && Math.Abs(rampAt - 1) < 1e-9 && e1.ShuttleFootIds.Count == squadN
                          && ZObject.ShuttleEmptyTaken == taken0 + 1;
                sb.AppendLine($"   a red grunt squad of {squadN} at {P(rx, ry)} ordered in (order taken {orderTaken}): after {tB:0.0} s pilot {e1.Drivers.Count} ({e1.DriverType}), passengers {e1.Passengers.Count} ({e1.PassengerType}), the ship {e1.Owner}; " +
                              $"boarded at the ramp foot {ZWorld.ShuttleBoardsAtFoot - foot0}x with the ramp at {rampAt:0.00}, {e1.ShuttleFootIds.Count} figures up the ramp; red grunts on foot {g0} -> {Count(w, Team.Red, "grunt")}");
                sb.AppendLine(OzV11Kit.Verdict(orderTaken && b1, "B. a squad of 3 ordered into an empty shuttle boards up its ramp: its leader the pilot, the other 2 its passengers, the ship the squad's team"));

                // ---------------------------------------------------------------- C. it flies, lands, EXIT: the 2 out, the pilot stays
                RampShut(w, e1);
                PadSpot(w, e1, e1.CenterX, e1.CenterY, 320, 640, out int cx1, out int cy1);
                int fl0 = e1.ShuttleFlights;
                bool landed1 = Fly(w, e1, cx1, cy1);
                bool canExit = e1.CanEjectCrew();
                var outSq = ExitGround(w, e1, out bool ej1);
                int outN = outSq != null ? 1 + outSq.Minions.Count : 0;
                bool again = e1.CanEjectCrew() || w.Eject(Team.Red, e1);
                sb.AppendLine($"C. flown to {P(cx1, cy1)}: {(landed1 ? "landed" : "NOT landed")} ({e1.ShuttleFlights - fl0} flight); EXIT allowed {canExit}, taken {ej1}: out {outN} ({(outSq != null ? outSq.Type : "-")}, one squad {(outSq != null && outSq.Leader == null)}), aboard now {e1.Passengers.Count}, " +
                              $"pilot {e1.Drivers.Count}, the ship {e1.Owner}; EXIT again: {(again ? "ALLOWED" : "refused")}");
                RampShut(w, e1);
                PadSpot(w, e1, e1.CenterX, e1.CenterY, 320, 640, out int cx2, out int cy2);
                bool landed2 = Fly(w, e1, cx2, cy2);
                sb.AppendLine($"   then flown by its pilot alone to {P(cx2, cy2)}: {(landed2 ? "landed" : "NOT landed")}, flights {e1.ShuttleFlights - fl0}");
                sb.AppendLine(OzV11Kit.Verdict(landed1 && canExit && ej1 && outN == squadN - 1 && e1.Passengers.Count == 0 && e1.Drivers.Count == 1 && e1.Owner == Team.Red && !again,
                    "C. landed, EXIT puts out only the 2 passengers (one squad); the pilot stays (never gets out, EXIT again refused)"));
                sb.AppendLine(OzV11Kit.Verdict(landed2 && e1.ShuttleFlights - fl0 == 2, "C. the pilot alone flies it on an order"));

                // ---------------------------------------------------------------- D. a lone robot: pilot only; then a squad as passengers
                var e2 = DropEmpty(w, e1.CenterX, e1.CenterY, 280, 700);
                bool d1 = false, d2 = false; string dLine = "no second drop";
                if (e2 != null)
                {
                    RobotSpot(w, e2.CenterX, e2.CenterY, 56, out int lx, out int ly);
                    var lone = OzV11Kit.Place(w, "grunt", lx, ly, Team.Red, 1);
                    Enter(w, lone, e2); double tl = Board(w, e2, 0, false);
                    bool exitLone = e2.CanEjectCrew() || w.Eject(Team.Red, e2);
                    d1 = e2.Drivers.Count == 1 && e2.Passengers.Count == 0 && e2.Owner == Team.Red && !exitLone && lone.KillAt >= 0;
                    RampShut(w, e2);
                    RobotSpot(w, e2.CenterX, e2.CenterY, 56, out int qx, out int qy);
                    var ps = OzV11Kit.Place(w, "psycho", qx, qy, Team.Red); int psN = 1 + ps.Minions.Count;
                    Enter(w, ps, e2); Board(w, e2, 0, true);
                    Run(w, ZWorld.ShuttleRampOpenSec + 6, () => e2.ShuttleCrewSettled && e2.ShuttleRampPhase == ZShuttleRamp.Shut);
                    int aboard = e2.Passengers.Count; string ptype = e2.PassengerType;
                    var psOut = ExitGround(w, e2, out bool ej2);
                    int psOutN = psOut != null ? 1 + psOut.Minions.Count : 0;
                    d2 = aboard == psN && ptype == "psycho" && ej2 && psOutN == psN && e2.Drivers.Count == 1 && e2.DriverType == "grunt" && e2.Owner == Team.Red;
                    dLine = $"D. a lone red grunt ordered into the empty #{e2.Id}: after {tl:0.0} s pilot {e2.Drivers.Count}, passengers 0, the ship {e2.Owner}, EXIT {(exitLone ? "ALLOWED" : "refused")}; " +
                            $"then a red psycho squad of {psN} ordered in: aboard {aboard} {ptype} as its passengers, EXIT taken {ej2}, out {psOutN}, the grunt pilot stays ({e2.Drivers.Count})";
                }
                sb.AppendLine(dLine);
                sb.AppendLine(OzV11Kit.Verdict(d1, "D. a lone robot ordered in becomes its pilot only: no passengers, EXIT refused"));
                sb.AppendLine(OzV11Kit.Verdict(d2, "D. a squad then ordered into that ship boards as its passengers and gets out on EXIT; the pilot stays"));

                // ---------------------------------------------------------------- D2. the hero squad (ALAN & BRAD): Brad its pilot, Alan out as himself
                {
                    bool heroWas = ZWorld.HeroSquadDeviation; ZWorld.HeroSquadDeviation = true;           // level 16's ALAN & BRAD from the level data
                    var wh = OzV11Kit.World(settings, m16, 5);
                    ZWorld.HeroSquadDeviation = heroWas;
                    var brad = wh.Objects.FirstOrDefault(o => o.IsHero && o.HeroMemberName == "brad" && OzV11Kit.Alive(o));
                    var eh = brad != null ? DropEmpty(wh, brad.CenterX, brad.CenterY, 200, 700) : null;
                    bool d3 = false; string hLine = "D2. no hero squad / no place on level 16";
                    if (eh != null)
                    {
                        int hp0 = brad.MaxHealth; var kind = brad.Hero;
                        Enter(wh, brad, eh); Board(wh, eh, 0, false); RampShut(wh, eh);
                        bool seats = eh.Drivers.Count == 1 && eh.Passengers.Count == 1 && eh.HeroCrewKind(false) == kind && eh.HeroCrewKind(true) == kind && eh.Owner == Team.Red;
                        bool ejh = wh.Eject(Team.Red, eh);                                                        // K256f2: a hero pilot gets out too (Sim/ZWorld.ShuttleHeroes.cs)
                        Run(wh, ZWorld.ShuttleRampOpenSec + 3.0, () => eh.Passengers.Count == 0 && eh.Drivers.Count == 0);
                        var outs2 = Outs(wh, eh, 2);
                        var brad2 = outs2.FirstOrDefault(o => !o.IsMinion); var alan = outs2.FirstOrDefault(o => o.IsMinion);
                        bool bothOut = brad2 != null && brad2.IsHero && brad2.HeroMemberName == "brad" && alan != null && alan.IsHero && alan.HeroMemberName == "alan" && alan.Leader == brad2 && alan.MaxHealth > wh.S.Unit("grunt").MaxHealth;
                        d3 = seats && ejh && bothOut && eh.ShuttleIsEmpty && eh.Owner == Team.Neutral && eh.HeroCrewKind(false) == null;
                        hLine = $"D2. ALAN & BRAD ordered into an empty shuttle on level 16: pilot {(seats ? 1 : 0)} (Brad, the hero seat kept), the seats right {seats} (Alan the passenger); " +
                                $"EXIT taken {ejh}: out {outs2.Count} ({string.Join(", ", outs2.Select(o => (o.IsHero ? o.HeroMemberName : o.Type) + (o.IsMinion ? "" : " leads") + " " + o.Health + " of " + o.MaxHealth + " HP"))}; Brad's {hp0}); the ship now {eh.Owner}, empty {eh.ShuttleIsEmpty}";
                    }
                    sb.AppendLine(hLine);
                    sb.AppendLine(OzV11Kit.Verdict(d3, "D2. a hero squad ordered in: its leader (Brad) the pilot, Alan the passenger; EXIT puts BOTH out as the hero squad (Brad leads) and the ship is empty again (K256f2: heroes never stay in the cockpit)"));
                }

                // ---------------------------------------------------------------- E. shot down in the air: all aboard lost
                {
                    var wa = OzV11Kit.World(settings, m16, 2);
                    var ea = DropEmpty(wa, wa.PlayableShuttle.CenterX, wa.PlayableShuttle.CenterY, 280, 640);
                    RobotSpot(wa, ea.CenterX, ea.CenterY, 56, out int ax, out int ay);
                    var qa = OzV11Kit.Place(wa, "grunt", ax, ay, Team.Red); int qaN = 1 + qa.Minions.Count;
                    Enter(wa, qa, ea); Board(wa, ea, 0, false); RampShut(wa, ea);
                    PadSpot(wa, ea, ea.CenterX, ea.CenterY, 400, 700, out int hx, out int hy);
                    OzV11Kit.PlayerMove(wa, ea, hx, hy); Run(wa, 30, () => ea.ShuttleState == ZShuttleState.Cruise);
                    int gA = Count(wa, Team.Red, "grunt"), pilotA = ea.Drivers.Count, paxA = ea.Passengers.Count, lost0 = wa.ShuttlePassengersLost;
                    var before = new HashSet<ZObject>(wa.Objects);
                    ea.DamageHealth(ea.Health); wa.UpdateObjectHealth(ea); Run(wa, 1.0);
                    int made = wa.Objects.Count(o => o.IsRobot && !before.Contains(o));
                    bool crashed = ea.Destroyed && pilotA == 1 && paxA == qaN - 1 && wa.ShuttlePassengersLost - lost0 == paxA && Count(wa, Team.Red, "grunt") == gA && made == 0
                                   && wa.ShuttleNotes.Any(n => n.kind == ZShuttleNoteKind.Crashed && n.id == ea.Id && n.aboard == paxA);
                    sb.AppendLine($"E. a captured ship (pilot {pilotA} + {paxA} passengers of a squad of {qaN}) shot down in the air at {ea.ShuttleHeightM:0.0} m: destroyed {ea.Destroyed}, passengers lost {wa.ShuttlePassengersLost - lost0}, the pilot with it, robots made after it {made}, red grunts {gA} -> {Count(wa, Team.Red, "grunt")}");
                    sb.AppendLine(OzV11Kit.Verdict(crashed, "E. shot down: all aboard are lost (the pilot and the passengers), nobody gets out"));
                }

                // ---------------------------------------------------------------- F. EXIT in the air follows the landing test
                sb.Append(AirExit(settings, m16, out bool fOk));
                sb.AppendLine(OzV11Kit.Verdict(fOk, "F. EXIT in the air is offered only where it may land and let them out (the landing test at the spot it would stop): refused while it climbs out, over water and over a unit; taken over free ground it lands there and the passengers get out, the pilot stays; a new order calls it off"));

                // ---------------------------------------------------------------- G. the CPU, the empty ship itself
                {
                    var wg = OzV11Kit.World(settings, m16, 3);
                    var eg = DropEmpty(wg, wg.PlayableShuttle.CenterX, wg.PlayableShuttle.CenterY, 280, 640);
                    RobotSpot(wg, eg.CenterX, eg.CenterY, 56, out int bx, out int by);
                    var blue = OzV11Kit.Place(wg, "grunt", bx, by, Team.Blue);
                    bool cpuSent = wg.SendWaypoints(Team.Blue, blue, new[] { new ZWaypoint { mode = WpMode.Enter, refId = eg.Id, x = eg.CenterX, y = eg.CenterY } });
                    bool cpuRefused = blue.Waypoints.Count == 0;
                    bool shipOrder = wg.SendWaypoints(Team.Red, eg, new[] { ZWaypoint.Move(eg.CenterX + 320, eg.CenterY) });
                    bool exitEmpty = eg.CanEjectCrew() || wg.Eject(Team.Red, eg) || wg.Eject(Team.Neutral, eg);
                    Run(wg, 30);
                    bool g = cpuRefused && !shipOrder && !exitEmpty && eg.ShuttleFlights == 0 && eg.ShuttleIsEmpty && eg.ShuttleRampOpens == 0 && eg.ShuttleState == ZShuttleState.Landed;
                    sb.AppendLine($"G. a CPU (blue) squad ordered into an empty shuttle: the order {(cpuRefused ? "refused" : "TAKEN")} (sent {cpuSent}); the player's move order to the empty ship itself: {(shipOrder ? "TAKEN" : "refused")}; EXIT on it: {(exitEmpty ? "TAKEN" : "refused")}; 30 s later: flights {eg.ShuttleFlights}, ramp openings {eg.ShuttleRampOpens}, empty {eg.ShuttleIsEmpty}");
                    sb.AppendLine(OzV11Kit.Verdict(g, "G. the CPU never boards an empty shuttle (its order is refused); the empty ship takes no order and never flies"));
                }

                // ---------------------------------------------------------------- H. a map's entry
                sb.Append(MapEntries(settings, m02, out bool hOk));
                sb.AppendLine(OzV11Kit.Verdict(hOk, "H. a map's entry \"vehicle:shuttle\" makes a playable shuttle (\"null\" = empty, \"red\" = crewed); the shuttle rule off: nothing, every other object the same"));
            }
            catch (Exception ex) { sb.AppendLine("FAIL  the check threw " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace); }
            finally { ZWorld.ShuttlePlayableDeviation = was; ZWorld.ShuttleOn = wasOn; ZWorld.ShuttleTeam = team0; }
            return sb.ToString();
        }

        // -------------------------------------------------------------------- A
        static string Soak(string settings, string map, string lvl, double secs, out bool pass, out int jeepsTaken, out int jeeps)
        {
            var sb = new StringBuilder();
            var w = ZWorld.Create(settings, map, 1, lvl == "L17");                               // level 17 starts its map's vehicles parked and empty (DV32)
            w.Bots.Add(new ZBot(w, Team.Blue, 102) { Difficulty = ZBotDifficulty.Easy }); w.Events.Clear();   // the CPU plays blue (its own target lists and car park run), Easy: the idle red side lasts the soak
            var ships = new List<ZObject>(); var jeepList = new List<ZObject>(); var robots = new List<ZObject>();
            int auto0 = ZObject.ShuttleEmptyAutoRefused;
            foreach (var team in new[] { Team.Red, Team.Blue })
            {
                var fort = w.Objects.FirstOrDefault(o => o.IsFort && o.Owner == team && !o.Destroyed);
                if (fort == null) continue;
                var s = DropEmpty(w, fort.CenterX, fort.CenterY, 160, 900);
                if (s == null) { sb.AppendLine($"A. {lvl}: no place for an empty shuttle by the {team} fort"); continue; }
                ships.Add(s);
                foreach (var type in new[] { "grunt", "psycho" })
                    if (RobotSpot(w, s.CenterX, s.CenterY, 72 + robots.Count * 6, out int x, out int y, 240)) robots.Add(OzV11Kit.Place(w, type, x, y, team));
                if (VehicleSpot(w, s.CenterX, s.CenterY, 96, 200, out int jx, out int jy)) jeepList.Add(OzV11Kit.Place(w, "jeep", jx, jy, Team.Neutral));
            }
            var at = ships.Select(s => (s.CenterX, s.CenterY)).ToList();
            int enterTicks = 0, enterWho = 0; double firstJeep = -1; var who = new HashSet<int>();
            double end = w.Time + secs;
            while (w.Time < end && !w.GameOver)
            {
                w.Tick(Tick);
                foreach (var o in w.Objects)
                {
                    if (!o.IsRobot || o.Waypoints.Count == 0) continue;
                    foreach (var wp in o.Waypoints)
                        if (wp.mode == WpMode.Enter && ships.Any(s => s.Id == wp.refId)) { enterTicks++; if (who.Add(o.Id)) enterWho++; break; }
                }
                if (firstJeep < 0 && jeepList.Any(j => j.Owner != Team.Neutral)) firstJeep = w.Time;
                w.Events.Clear();
            }
            jeeps = jeepList.Count; jeepsTaken = jeepList.Count(j => j.Owner != Team.Neutral);
            int autoDrops = ZObject.ShuttleEmptyAutoRefused - auto0;
            bool ok = ships.Count == 2 && enterTicks == 0 && autoDrops == 0;
            for (int k = 0; k < ships.Count; k++)
            {
                var s = ships[k];
                bool still = !s.Destroyed && s.ShuttleIsEmpty && s.Owner == Team.Neutral && s.ShuttleFlights == 0 && s.ShuttleState == ZShuttleState.Landed && s.ShuttleLift == 0
                             && s.CenterX == at[k].CenterX && s.CenterY == at[k].CenterY && s.ShuttleRampOpens == 0;
                ok &= still;
                sb.AppendLine($"A. {lvl}: empty shuttle #{s.Id} at {P(at[k].CenterX, at[k].CenterY)}: after {w.Time:0} s {(s.Destroyed ? "DESTROYED" : s.Owner.ToString())}, pilots {s.Drivers.Count}, aboard {s.Passengers.Count}, flights {s.ShuttleFlights}, state {s.ShuttleState}, ramp openings {s.ShuttleRampOpens}, still at {P(s.CenterX, s.CenterY)}: {(still ? "untouched" : "CHANGED")}");
            }
            sb.AppendLine($"A. {lvl}: {robots.Count} idle squads placed beside them ({string.Join(", ", robots.Select(r => r.Owner + " " + r.Type))}); robot-ticks with an ENTER waypoint on an empty shuttle {enterTicks} ({enterWho} robots), " +
                          $"unordered ones dropped {autoDrops}; the empty jeeps beside them: {jeepsTaken} of {jeeps} taken{(firstJeep >= 0 ? $" (the first at {firstJeep:0} s)" : "")}{(w.GameOver ? "; the match ended at " + w.Time.ToString("0") + " s" : "")}");
            pass = ok;
            return sb.ToString();
        }

        // -------------------------------------------------------------------- F
        static string AirExit(string settings, string m16, out bool pass)
        {
            var sb = new StringBuilder();
            pass = false;
            var w = OzV11Kit.World(settings, m16, 4);
            var s = DropEmpty(w, w.PlayableShuttle.CenterX, w.PlayableShuttle.CenterY, 280, 640);
            RobotSpot(w, s.CenterX, s.CenterY, 56, out int rx, out int ry);
            var sq = OzV11Kit.Place(w, "grunt", rx, ry, Team.Red); int squadN = 1 + sq.Minions.Count;
            Enter(w, sq, s); Board(w, s, 0, false); RampShut(w, s);
            int pax = s.Passengers.Count;
            // F1: the climb, and every tick of a flight over water: offered exactly where the landing test passes at the stop spot
            WaterSpot(w, s, s.CenterX, s.CenterY, out int wx, out int wy);
            OzV11Kit.PlayerMove(w, s, wx, wy);
            int climbSamples = 0, climbOffered = 0, samples = 0, offered = 0, mismatch = 0, refusedWater = 0;
            var whys = new Dictionary<string, int>();
            double t0 = w.Time; bool left = false;
            Run(w, 120, () =>
            {
                if (s.ShuttleState != ZShuttleState.Landed) left = true;
                if (s.ShuttleState == ZShuttleState.Climb) { climbSamples++; if (s.CanEjectCrew()) climbOffered++; }
                else if (s.ShuttleAirborne && (s.ShuttleState == ZShuttleState.Cruise || s.ShuttleState == ZShuttleState.Hover) && !s.ShuttleLandTurning)
                {
                    samples++;
                    var (ex, ey) = s.ShuttleExitSpot();
                    bool land = w.ShuttlePadFree(ex, ey, s, out string why), can = s.CanEjectCrew();
                    if (can) offered++; else { whys.TryGetValue(why, out int n); whys[why] = n + 1; if (why == "water") refusedWater++; }
                    if (can != land) mismatch++;
                }
                return left && w.Time - t0 > 0.5 && (s.ShuttleState == ZShuttleState.Landed || s.ShuttleState == ZShuttleState.Hover);
            });
            bool hoverWater = s.ShuttleState == ZShuttleState.Hover && s.ShuttleNoLand == "water";
            bool waterRefused = hoverWater && !s.CanEjectCrew() && !w.Eject(Team.Red, s) && s.Passengers.Count == pax && !s.ShuttleExitOnLanding;
            sb.AppendLine($"F. a captured ship (pilot + {pax}) sent onto water at {P(wx, wy)}: climb ticks {climbSamples}, EXIT offered in {climbOffered}; flying / hovering ticks {samples}: offered {offered}, refused {samples - offered} " +
                          $"({string.Join(", ", whys.OrderBy(k => k.Key).Select(k => k.Key + " " + k.Value))}); EXIT offered where the landing test at its stop spot fails, or refused where it passes: {mismatch}; " +
                          $"hovering over the water: EXIT {(waterRefused ? "refused" : "NOT refused")}");
            // F2: from the water, a long flight to free ground; EXIT given in the cruise where it is offered: it lands where it stops
            PadSpot(w, s, s.CenterX, s.CenterY, 560, 1400, out int fx, out int fy);
            OzV11Kit.PlayerMove(w, s, fx, fy);
            (int x, int y) stopAt = (-1, -1); bool exitTaken = false; double tExit = -1;
            Run(w, 60, () =>
            {
                if (s.ShuttleState == ZShuttleState.Cruise && !s.ShuttleLandTurning && s.CanEjectCrew())
                {
                    var (gx0, gy0) = (s.ShuttleGoalX, s.ShuttleGoalY);
                    double dGoal = Math.Sqrt((double)(gx0 - s.CenterX) * (gx0 - s.CenterX) + (double)(gy0 - s.CenterY) * (gy0 - s.CenterY));
                    if (dGoal > 320) { stopAt = s.ShuttleExitSpot(); exitTaken = w.Eject(Team.Red, s); tExit = w.Time; return true; }
                }
                return s.ShuttleState == ZShuttleState.Landed && w.Time - t0 > 1;
            });
            Run(w, 40, () => s.Passengers.Count == 0 && s.ShuttleState == ZShuttleState.Landed);
            var outs = Outs(w, s, pax);
            bool landedThere = s.ShuttleState == ZShuttleState.Landed && s.CenterX == stopAt.x && s.CenterY == stopAt.y;
            bool notTarget = !(s.CenterX == fx && s.CenterY == fy);
            bool f2 = exitTaken && landedThere && notTarget && s.Passengers.Count == 0 && outs.Count == pax && outs.Count(o => !o.IsMinion) == 1 && s.Drivers.Count == 1 && s.Owner == Team.Red;
            sb.AppendLine($"   then sent to {P(fx, fy)} and EXIT given in the cruise (offered) {(tExit >= 0 ? (tExit - t0).ToString("0.0") + " s in" : "NEVER")}: taken {exitTaken}, its stop spot {P(stopAt.x, stopAt.y)}; it landed at {P(s.CenterX, s.CenterY)} ({(landedThere ? "the stop spot" : "ELSEWHERE")}, not the order's target {notTarget}); " +
                          $"out {outs.Count} as {outs.Count(o => !o.IsMinion)} squad, aboard now {s.Passengers.Count}, the pilot stays ({s.Drivers.Count}, {s.Owner})");
            // F3: hovering over a unit (cannot land), the unit goes: EXIT is offered, taken: it comes down right there and lets them out
            var back = outs.FirstOrDefault(o => !o.IsMinion);
            bool f3 = false, f4 = false;
            if (back != null)
            {
                RampShut(w, s); Enter(w, back, s); Board(w, s, 0, true); RampShut(w, s);
                PadSpot(w, s, s.CenterX, s.CenterY, 400, 800, out int ux, out int uy);
                var blocker = OzV11Kit.Place(w, "sniper", ux, uy, Team.Red, 1); blocker.MoveSpeed = 0; blocker.Damage = 0;
                bool l3 = Fly(w, s, ux, uy);
                bool hovUnits = !l3 && s.ShuttleState == ZShuttleState.Hover && s.ShuttleNoLand == "units"; string why3 = s.ShuttleNoLand;
                bool refusedUnits = hovUnits && !s.CanEjectCrew() && !w.Eject(Team.Red, s);
                blocker.KillAt = w.Time; Run(w, 0.5);
                var (hx, hy) = s.ShuttleExitSpot();
                bool offeredNow = s.CanEjectCrew() && w.ShuttlePadFree(hx, hy, s, out _);
                bool ej3 = w.Eject(Team.Red, s);
                Run(w, 40, () => s.Passengers.Count == 0 && s.ShuttleState == ZShuttleState.Landed);
                var outs3 = Outs(w, s, pax); int out3 = outs3.Count;
                f3 = refusedUnits && offeredNow && ej3 && s.ShuttleState == ZShuttleState.Landed && s.CenterX == hx && s.CenterY == hy && out3 == pax && s.Drivers.Count == 1;
                sb.AppendLine($"   hovering over a unit at {P(ux, uy)} ('{why3}'{(hovUnits ? "" : ", NOT hovering")}): EXIT {(refusedUnits ? "refused" : "NOT refused")}; the unit gone: EXIT offered {offeredNow}, taken {ej3}: it came down at {P(s.CenterX, s.CenterY)} ({(s.CenterX == hx && s.CenterY == hy ? "right there" : "ELSEWHERE")}), out {out3}, the pilot stays ({s.Drivers.Count})");
                // F4: a new order calls an EXIT given in the air off
                var back4 = outs3.FirstOrDefault(o => !o.IsMinion);
                if (back4 != null)
                {
                    RampShut(w, s); Enter(w, back4, s); Board(w, s, 0, true); RampShut(w, s);
                    PadSpot(w, s, s.CenterX, s.CenterY, 700, 1400, out int gx, out int gy);
                    OzV11Kit.PlayerMove(w, s, gx, gy);
                    Run(w, 30, () => s.ShuttleState == ZShuttleState.Cruise && s.CanEjectCrew());
                    bool ej4 = w.Eject(Team.Red, s); bool pending = s.ShuttleExitOnLanding;
                    PadSpot(w, s, gx, gy, 0, 200, out int g2x, out int g2y);
                    bool l4 = Fly(w, s, g2x, g2y);
                    f4 = ej4 && pending && !s.ShuttleExitOnLanding && l4 && s.Passengers.Count == pax && !s.ShuttleExitPending;
                    sb.AppendLine($"   EXIT given in the air (taken {ej4}, pending {pending}), then a move order to {P(g2x, g2y)}: {(l4 ? "landed there" : "NOT landed")}, the EXIT called off {!s.ShuttleExitOnLanding && !s.ShuttleExitPending}, still aboard {s.Passengers.Count}");
                }
            }
            pass = climbSamples > 0 && climbOffered == 0 && samples > 0 && offered > 0 && offered < samples && mismatch == 0 && refusedWater > 0 && waterRefused && f2 && f3 && f4;
            return sb.ToString();
        }

        // -------------------------------------------------------------------- H
        static string MapEntries(string settings, string m02, out bool pass)
        {
            var sb = new StringBuilder();
            var probe = ZWorld.Create(settings, m02, 1);
            var fort = probe.Objects.First(o => o.IsFort && o.Owner == Team.Red);
            var spots = new List<(int x, int y)>();
            for (int r = 200; r <= 1200 && spots.Count < 2; r += 32)
                for (int a = 0; a < 16 && spots.Count < 2; a++)
                {
                    double g = a * Math.PI / 8;
                    var (x, y) = probe.ShuttleClamp(fort.CenterX + (int)(Math.Cos(g) * r), fort.CenterY + (int)(Math.Sin(g) * r));
                    x -= 8; y -= 8;                                                                       // the 2 x 2 tiles' middle: a tile corner
                    if (spots.Any(q => Math.Abs(q.x - x) < 160 && Math.Abs(q.y - y) < 160)) continue;
                    if (probe.ShuttleLandSpot(x, y, null, ZWorld.ShuttleDropHeadingDeg, out _, out _)) spots.Add((x, y));
                }
            if (spots.Count < 2) { pass = false; sb.AppendLine("H. no two free places on level 2"); return sb.ToString(); }
            var m = ZMapData.FromJson(m02);
            int n0 = m.objects.Count;
            m.objects.Add(new ZMapData.Obj { x = spots[0].x / 16 - 1, y = spots[0].y / 16 - 1, owner = "null", kind = "vehicle:" + ZWorld.ShuttleType, health_percent = 100 });
            m.objects.Add(new ZMapData.Obj { x = spots[1].x / 16 - 1, y = spots[1].y / 16 - 1, owner = "red", kind = "vehicle:" + ZWorld.ShuttleType, health_percent = 100 });
            string json = UnityEngine.JsonUtility.ToJson(m);
            var w = ZWorld.Create(settings, json, 1);
            var sh = w.Objects.Where(o => o.IsShuttle).ToList();
            var empty = sh.FirstOrDefault(o => o.Owner == Team.Neutral); var red = sh.FirstOrDefault(o => o.Owner == Team.Red);
            bool made = sh.Count == 2 && w.ShuttleMapMade == 2 && empty != null && red != null && empty.ShuttleIsEmpty && !empty.CanBeEntered() && red.Drivers.Count == 1
                        && empty.CenterX == spots[0].x && empty.CenterY == spots[0].y && red.CenterX == spots[1].x && red.CenterY == spots[1].y && w.PlayableShuttle == null
                        && empty.MaxHealth == red.MaxHealth && empty.MaxHealth == ZWorld.ShuttleHpFrom(w.S.Unit("light")?.MaxHealth ?? 3378);   // DV131 (K443): the light tank's x 1.2
            string redAt = red != null ? P(red.CenterX, red.CenterY) : "-";
            Run(w, 30);
            bool quiet = empty != null && empty.ShuttleFlights == 0 && empty.ShuttleIsEmpty;
            ZWorld.ShuttlePlayableDeviation = false;
            var off = ZWorld.Create(settings, json, 1); var plain = ZWorld.Create(settings, m02, 1);
            ZWorld.ShuttlePlayableDeviation = true;
            var listOff = off.Objects.Select(o => o.Type + "@" + o.LocX + "," + o.LocY).ToList(); var listPlain = plain.Objects.Select(o => o.Type + "@" + o.LocX + "," + o.LocY).ToList();
            bool offSame = !off.Objects.Any(o => o.IsShuttle) && off.ShuttleMapSkipped == 2 && listOff.SequenceEqual(listPlain);
            sb.AppendLine($"H. level 2's map with two entries added ({n0} -> {m.objects.Count} objects): \"null\" at tile {spots[0].x / 16 - 1},{spots[0].y / 16 - 1} -> {(empty != null ? $"#{empty.Id} {empty.Owner}, pilots {empty.Drivers.Count}, middle {P(empty.CenterX, empty.CenterY)}, facing {empty.ShuttleHeadingDeg:0}" : "NONE")}; " +
                          $"\"red\" at tile {spots[1].x / 16 - 1},{spots[1].y / 16 - 1} -> {(red != null ? $"#{red.Id} {red.Owner}, pilots {red.Drivers.Count}, middle {redAt}, {red.MaxHealth} HP" : "NONE")}; made {w.ShuttleMapMade}, the empty one {empty?.MaxHealth} HP; level 16's own ship: {(w.PlayableShuttle == null ? "none" : "#" + w.PlayableShuttle.Id)}; 30 s later the empty one: flights {empty?.ShuttleFlights}, empty {empty?.ShuttleIsEmpty}; " +
                          $"the shuttle rule off: shuttles {off.Objects.Count(o => o.IsShuttle)}, entries skipped {off.ShuttleMapSkipped}, every other object as without the entries {listOff.SequenceEqual(listPlain)}");
            pass = made && quiet && offSame;
            if (!pass) sb.AppendLine($"   (H: made {made}: shuttles {sh.Count}, entries made {w.ShuttleMapMade}, the empty one at the entry {empty != null && empty.CenterX == spots[0].x && empty.CenterY == spots[0].y} ({P(spots[0].x, spots[0].y)}), the red one {red != null && red.CenterX == spots[1].x && red.CenterY == spots[1].y} ({P(spots[1].x, spots[1].y)}); quiet {quiet}; off the same {offSame})");
            return sb.ToString();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Shuttle: K256f Empty Shuttle Check")]
        static void Menu() => OzV11Kit.RunMenu("K256f the empty shuttle and EXIT in the air", () => Check(OzV11Kit.SettingsText, OzV11Kit.MapText("L16"), OzV11Kit.MapText("L02"), OzV11Kit.MapText("L17")));
#endif
    }
}
