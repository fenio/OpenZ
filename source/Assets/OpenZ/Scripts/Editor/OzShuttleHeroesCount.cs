using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K256f2 (rule DV118, Sim/ZWorld.ShuttleHeroes.cs): heroes never stay stuck in a shuttle, and no vehicle or gun ever boards one. Counted
    /// headless (ZWorld.Create + Tick, no scene, nothing saved; public members only, like every check of the editor assembly). Menu
    /// OpenZ/V1.1/Shuttle: K256f2 Heroes Exit Check.
    ///  H1 ALAN &amp; BRAD ordered into an empty shuttle: Brad the pilot, Alan the passenger; flown, landed, EXIT: both out as the hero squad
    ///     (Brad leads) with their hit points, rank and grenades; the ship EMPTY again, field by field as a fresh empty one, and it stays so
    ///     (no flight, the ramp shut, nobody walks in by himself, its own move order refused); then a grunt squad boards it again;
    ///  H2 a lone hero (Brad alone, Alan alone) takes an empty ship: its pilot; EXIT on the ground lets him out with his grenades, the ship
    ///     empty; again, EXIT in the air: offered exactly where the landing test at the stop spot passes, given: it lands there, he gets out;
    ///  H3 heroes with others: the hero squad as the passengers of a lone grunt pilot (EXIT: the heroes out, the grunt pilot stays); a grunt
    ///     squad as the passengers of a lone hero pilot (EXIT: everyone out, the grunts as their squad and Brad as himself in one line, the
    ///     ship empty); level 16's own ship with the hero squad aboard (EXIT: the heroes out, its pilot stays); ALAN &amp; BRAD split apart
    ///     (DV98: two squads), Brad the pilot and Alan his passenger (EXIT: both out as they went in, every grenade once, the ship empty);
    ///  H4 shot down in the air with Brad the pilot and Alan aboard: both lost, nobody made after it, both listed for the view's bodies;
    ///  V  every vehicle and gun (jeep, light, medium and heavy tank, APC, MLRS, crane, the four guns) and another shuttle, beside an empty
    ///     and a crewed shuttle: the player's and the CPU's ENTER order refused for each one, an ENTER waypoint put in by hand dropped without
    ///     boarding, and a soak with the CPU playing: no vehicle or gun ever holds an ENTER waypoint on a shuttle, the ships never change.
    /// </summary>
    public static class OzShuttleHeroesCount
    {
        const double Tick = ZConst.TICK;
        /// <summary>The vehicles and guns of V (the testing panel's lists) and the soak of V in seconds.</summary>
        public static readonly string[] VehicleTypes = { "jeep", "light", "medium", "heavy", "apc", "missile_launcher", "crane", "gatling", "gun", "howitzer", "missile_cannon" };
        public const double VehicleSoakSec = 120.0;

        static void Run(ZWorld w, double secs, Func<bool> until = null)
        {
            double end = w.Time + secs;
            while (w.Time < end) { w.Tick(Tick); if (until != null && until()) return; }
        }

        static string P(int x, int y) => x + "," + y;
        static bool Alive(ZObject o) => OzV11Kit.Alive(o);

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

        /// <summary>The testing panel's drop (ZWorld.SpawnShuttle) for `team` (the null team = an empty one) at the first free place ring by ring.</summary>
        static ZObject Drop(ZWorld w, int cx, int cy, int minR, int maxR, Team team = Team.Neutral)
        {
            for (int r = minR; r <= maxR; r += 32)
                for (int a = 0; a < 16; a++)
                {
                    double g = a * Math.PI / 8;
                    var s = w.SpawnShuttle(cx + (int)(Math.Cos(g) * r), cy + (int)(Math.Sin(g) * r), team, -90f, out _);
                    if (s != null) return s;
                }
            return null;
        }

        static void Enter(ZWorld w, ZObject sq, ZObject s) => OzV11Kit.PlayerEnter(w, sq, s);

        /// <summary>Until the ship has taken the squad: pilot (an empty ship) or passengers (a crewed one), at most 60 s; then the ramp shut.</summary>
        static void Board(ZWorld w, ZObject s, bool asPassengers)
        {
            int p0 = s.Passengers.Count;
            Run(w, 60, () => asPassengers ? s.Passengers.Count > p0 : s.Drivers.Count > 0);
            RampShut(w, s);
        }

        /// <summary>Board, with robot `who` holding `grenades` every tick until it is in (on its way it may blast a prop or a rock in its path
        /// with them: what goes aboard is that number), and its hit points the last tick before it got in.</summary>
        static void BoardWatch(ZWorld w, ZObject s, bool asPassengers, ZObject who, ref int grenades, ref int hp)
        {
            int p0 = s.Passengers.Count, g = grenades, h = hp;
            Run(w, 60, () => { if (Alive(who)) { who.Grenades = g; h = who.Health; } return asPassengers ? s.Passengers.Count > p0 : s.Drivers.Count > 0; });
            hp = h;
            RampShut(w, s);
        }

        static void RampShut(ZWorld w, ZObject s) => Run(w, 20, () => s.ShuttleRampPhase == ZShuttleRamp.Shut && !s.ShuttleExitPending && w.Time >= s.ShuttleRampHoldUntil);

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

        /// <summary>EXIT, then until nobody who gets out is aboard (at most the ramp's opening + 4 s; in the air until it landed, 60 s). The
        /// robots the exit made (the ramp's foot list, alive), in the line's order.</summary>
        static List<ZObject> Exit(ZWorld w, ZObject s, out bool taken)
        {
            taken = w.Eject(s.Owner, s);
            double max = s.ShuttleAirborne ? 60 : ZWorld.ShuttleRampOpenSec + 4.0;
            Run(w, max, () => !s.ShuttleHasLeavers && s.ShuttleState == ZShuttleState.Landed && !s.ShuttleExitPending);
            if (s.ShuttleHasLeavers) return new List<ZObject>();
            return s.ShuttleFootIds.Select(id => w.Get(id)).Where(o => o != null && o.IsRobot && Alive(o)).ToList();
        }

        /// <summary>The ship's own fields that make it "empty" (compared with a fresh empty drop).</summary>
        static string Print(ZObject s) =>
            $"team {s.Owner}, pilots {s.Drivers.Count}, aboard {s.Passengers.Count}, crew types {s.DriverType}/{s.PassengerType}, pool {s.PassengerGrenades}+{s.ShuttlePilotPool}, own squad aboard {s.ShuttlePilotSquadIn}, " +
            $"hero seats {(s.HeroCrewKind(false) != null ? "pilot" : "-")}/{(s.HeroCrewKind(true) != null ? "passengers" : "-")} ({s.HeroesAboard}), rank {s.Kills}/{s.Veteran}/{s.PromotedAt}, " +
            $"empty {s.ShuttleIsEmpty}, anyone may enter {s.CanBeEntered()}, EXIT {s.CanEjectCrew()}, hero pilot {s.ShuttleHeroPilot}, exit pending {s.ShuttleExitPending}, take-off held {s.ShuttleTakeOffHeld}, " +
            $"air exit {s.ShuttleExitOnLanding}, repair trip {s.ShuttleRepairTarget}/{s.ShuttleOnRepairPad}, {s.ShuttleState} lift {s.ShuttleLift}, weapon {s.Damage}/{s.AttackRadius}";

        /// <summary>The level-16 world with its own ALAN &amp; BRAD (the level data's hero squad, as in the game); Brad (the leader).</summary>
        static ZWorld HeroWorld(string settings, string m16, int seed, out ZObject brad)
        {
            bool was = ZWorld.HeroSquadDeviation; ZWorld.HeroSquadDeviation = true;
            var w = OzV11Kit.World(settings, m16, seed);
            ZWorld.HeroSquadDeviation = was;
            brad = w.Objects.FirstOrDefault(o => o.IsHero && o.HeroMemberName == "brad" && Alive(o) && o.Owner == Team.Red);
            return w;
        }

        static ZObject Member(ZObject lead, string name) => lead == null ? null : OzV11Kit.Squad(lead).FirstOrDefault(o => o.IsHero && o.HeroMemberName == name);

        public static string Check(string settings, string m16, string m02)
        {
            var sb = new StringBuilder();
            bool was = ZWorld.ShuttlePlayableDeviation, wasOn = ZWorld.ShuttleOn, heroWas = ZWorld.HeroSquadDeviation; var team0 = ZWorld.ShuttleTeam;
            try
            {
                ZWorld.ShuttlePlayableDeviation = true; ZWorld.ShuttleOn = true; ZWorld.ShuttleTeam = Team.Red;
                sb.Append(H1(settings, m16));
                sb.Append(H2(settings, m16));
                sb.Append(H3(settings, m16));
                sb.Append(H4(settings, m16));
                sb.Append(V(settings, m16));
            }
            catch (Exception ex) { sb.AppendLine("FAIL  the check threw " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace); }
            finally { ZWorld.ShuttlePlayableDeviation = was; ZWorld.ShuttleOn = wasOn; ZWorld.HeroSquadDeviation = heroWas; ZWorld.ShuttleTeam = team0; }
            return sb.ToString();
        }

        // -------------------------------------------------------------------- H1: the hero squad in, flown, EXIT: both out, the ship empty
        static string H1(string settings, string m16)
        {
            var sb = new StringBuilder();
            var w = HeroWorld(settings, m16, 11, out var brad);
            var alan = Member(brad, "alan");
            var e = brad != null ? Drop(w, brad.CenterX, brad.CenterY, 200, 700) : null;
            if (e == null || alan == null) { sb.AppendLine(OzV11Kit.Verdict(false, "H1. the hero squad and an empty drop on level 16")); return sb.ToString(); }
            var fresh = Drop(w, e.CenterX, e.CenterY, 300, 900);                                 // a second, untouched empty ship: what "empty" is
            brad.Grenades = 3;                                                                    // a crate's pool (Brad leads it); Alan's own 5 from the start
            brad.Kills = alan.Kills = 2;                                                          // the squad's rank (DV12)
            int bradHp = brad.Health = brad.MaxHealth - 400, alanHp = alan.Health = alan.MaxHealth - 900, alanOwn = alan.Grenades, bradPool = brad.Grenades;
            int ex0 = ZObject.ShuttleHeroExits, em0 = ZObject.ShuttleHeroesEmptied;
            Enter(w, brad, e);
            Run(w, 60, () => { if (Alive(brad)) { brad.Grenades = bradPool; bradHp = brad.Health; } if (Alive(alan)) { alan.Grenades = alanOwn; alanHp = alan.Health; } return e.Drivers.Count > 0; });   // they hold these until they are in (a prop blasted on the way would cost grenades); the hit points of the last tick before
            RampShut(w, e);
            bool seats = e.Drivers.Count == 1 && e.Passengers.Count == 1 && e.ShuttleHeroPilot && e.HeroCrewKind(false) == brad.Hero && e.HeroCrewKind(true) == brad.Hero && e.ShuttlePilotSquadIn && e.Owner == Team.Red && e.Kills == 2;
            sb.AppendLine($"H1. level 16: ALAN & BRAD ordered into the empty #{e.Id}: pilot {e.Drivers.Count} ({(e.ShuttleHeroPilot ? "Brad, a hero" : "NOT a hero")}), passengers {e.Passengers.Count} ({(e.HeroCrewKind(true) != null ? "Alan" : "plain")}), the ship {e.Owner}, its rank {e.Kills}");
            PadSpot(w, e, e.CenterX, e.CenterY, 320, 640, out int fx, out int fy);
            bool flown = Fly(w, e, fx, fy);
            bool offered = e.CanEjectCrew();
            var outs = Exit(w, e, out bool taken);
            var b2 = outs.FirstOrDefault(o => !o.IsMinion); var a2 = outs.FirstOrDefault(o => o.IsMinion);
            bool both = outs.Count == 2 && b2 != null && b2.IsHero && b2.HeroMemberName == "brad" && a2 != null && a2.IsHero && a2.HeroMemberName == "alan" && a2.Leader == b2
                        && b2.Health == bradHp && a2.Health == alanHp && b2.MaxHealth == brad.MaxHealth && b2.Kills == 2 && a2.Kills == 2 && b2.Grenades == bradPool && a2.Grenades == alanOwn && b2.Owner == Team.Red;
            sb.AppendLine($"   flown to {P(fx, fy)}: {(flown ? "landed" : "NOT landed")}; EXIT offered {offered}, taken {taken}: out {outs.Count} ({string.Join(", ", outs.Select(o => (o.IsHero ? o.HeroMemberName : o.Type) + (o.IsMinion ? "" : " (leads)") + " " + o.Health + " HP, rank " + o.Kills + ", grenades " + o.Grenades))}); " +
                          $"aboard they had: Brad {bradHp} HP and the pool {bradPool}, Alan {alanHp} HP and his own {alanOwn}");
            bool emptyNow = e.ShuttleIsEmpty && Print(e) == Print(fresh);
            sb.AppendLine($"   the ship now: {Print(e)}");
            sb.AppendLine($"   a fresh empty: {Print(fresh)}");
            sb.AppendLine(OzV11Kit.Verdict(seats && flown && offered && taken && both && ZObject.ShuttleHeroExits == ex0 + 1, "H1. ALAN & BRAD took an empty shuttle (Brad the pilot, Alan the passenger); landed, EXIT puts BOTH out as the hero squad (Brad leads) with their hit points, rank and grenades"));
            sb.AppendLine(OzV11Kit.Verdict(emptyNow && ZObject.ShuttleHeroesEmptied == em0 + 1, "H1. the ship is EMPTY again exactly as a fresh empty one (the null team, no pilot, nobody aboard, no seats, no rank, no order kept)"));
            // it stays empty: idle squads beside it, its own order, 60 s
            RobotSpot(w, e.CenterX, e.CenterY, 64, out int gx, out int gy);
            var idle = OzV11Kit.Place(w, "grunt", gx, gy, Team.Red); int n = 1 + idle.Minions.Count;
            bool order = w.SendWaypoints(Team.Red, e, new[] { ZWaypoint.Move(e.CenterX + 320, e.CenterY) }) || w.Eject(Team.Red, e);
            int fl0 = e.ShuttleFlights, enterTicks = 0; double t0 = w.Time;
            Run(w, 60, () => { foreach (var o in w.Objects) if (o.Waypoints.Count > 0 && o.Waypoints.Any(q => q.mode == WpMode.Enter && q.refId == e.Id)) enterTicks++; return false; });
            bool stays = !order && e.ShuttleFlights == fl0 && e.ShuttleIsEmpty && e.ShuttleRampPhase == ZShuttleRamp.Shut && enterTicks == 0 && e.ShuttleState == ZShuttleState.Landed && Print(e) == Print(fresh);
            sb.AppendLine($"   then 60 s beside the heroes and an idle grunt squad: its own move order / EXIT {(order ? "TAKEN" : "refused")}, flights {e.ShuttleFlights - fl0}, ramp {e.ShuttleRampPhase}, robot-ticks with an ENTER waypoint on it {enterTicks}, still empty {e.ShuttleIsEmpty}");
            sb.AppendLine(OzV11Kit.Verdict(stays, "H1. it stays empty: no flight, the ramp shut, nobody walks in by himself, it takes no order"));
            // a grunt squad boards it again
            Enter(w, idle, e); Board(w, e, false);
            int paxAgain = e.Passengers.Count;
            bool again = e.Drivers.Count == 1 && e.DriverType == "grunt" && !e.ShuttleHeroPilot && paxAgain == n - 1 && e.Owner == Team.Red;
            var outG = Exit(w, e, out bool tg);
            bool k256f = again && tg && outG.Count == paxAgain && e.Drivers.Count == 1 && e.Owner == Team.Red && !e.CanEjectCrew();
            sb.AppendLine($"   a red grunt squad of {n} ordered in: pilot {e.Drivers.Count} ({e.DriverType}{(e.ShuttleHeroPilot ? ", a hero" : "")}), passengers {paxAgain}, the ship {e.Owner}; EXIT taken {tg}: out {outG.Count}, the grunt pilot stays ({e.Drivers.Count}), EXIT again {(e.CanEjectCrew() ? "ALLOWED" : "refused")}");
            sb.AppendLine(OzV11Kit.Verdict(again && k256f, "H1. a grunt squad then boards the emptied ship as before (its leader the pilot who stays, the rest its passengers)"));
            return sb.ToString();
        }

        // -------------------------------------------------------------------- H2: a lone hero
        static string H2(string settings, string m16)
        {
            var sb = new StringBuilder();
            bool ok1 = false, ok2 = false, ok3 = false;
            {   // Brad alone: on the ground, then in the air
                var w = HeroWorld(settings, m16, 12, out var brad);
                var alan = Member(brad, "alan");
                if (alan != null) { alan.KillAt = w.Time; Run(w, 0.5); }                       // Alan gone: Brad leads nobody
                var e = brad != null ? Drop(w, brad.CenterX, brad.CenterY, 200, 700) : null;
                if (e != null && brad.Minions.Count == 0)
                {
                    brad.Grenades = 2; int hp0 = brad.Health, pool = brad.Grenades;
                    Enter(w, brad, e); BoardWatch(w, e, false, brad, ref pool, ref hp0);
                    bool lonePilot = e.Drivers.Count == 1 && e.Passengers.Count == 0 && e.ShuttleHeroPilot && e.ShuttlePilotPool == pool && e.Owner == Team.Red;
                    bool offered = e.CanEjectCrew();
                    var outs = Exit(w, e, out bool taken);
                    var b = outs.FirstOrDefault();
                    ok1 = lonePilot && offered && taken && outs.Count == 1 && b.IsHero && b.HeroMemberName == "brad" && b.Minions.Count == 0 && b.Health == hp0 && b.Grenades == pool && e.ShuttleIsEmpty && e.Owner == Team.Neutral;
                    sb.AppendLine($"H2. Brad alone ordered into the empty #{e.Id}: pilot {(lonePilot ? $"Brad, no passengers, his pool {pool} kept" : $"NOT as expected (pool {e.ShuttlePilotPool} of {pool})")}; EXIT on the ground offered {offered}, taken {taken}: out {outs.Count} ({(b != null ? (b.IsHero ? b.HeroMemberName : b.Type) + " " + b.Health + " HP, grenades " + b.Grenades : "nobody")}); the ship {e.Owner}, empty {e.ShuttleIsEmpty}");
                    // in the air
                    if (b != null)
                    {
                        RampShut(w, e); Enter(w, b, e); Board(w, e, false);
                        PadSpot(w, e, e.CenterX, e.CenterY, 700, 1400, out int fx, out int fy);
                        OzV11Kit.PlayerMove(w, e, fx, fy);
                        int climbOffered = 0, samples = 0, offeredAir = 0, mismatch = 0; (int x, int y) stop = (-1, -1); bool airTaken = false; double t0 = w.Time;
                        Run(w, 60, () =>
                        {
                            if (e.ShuttleState == ZShuttleState.Climb && e.CanEjectCrew()) climbOffered++;
                            if (e.ShuttleAirborne && (e.ShuttleState == ZShuttleState.Cruise || e.ShuttleState == ZShuttleState.Hover) && !e.ShuttleLandTurning)
                            {
                                samples++;
                                var (sx, sy) = e.ShuttleExitSpot();
                                bool land = w.ShuttlePadFree(sx, sy, e, out _), can = e.CanEjectCrew();
                                if (can) offeredAir++;
                                if (can != land) mismatch++;
                                double dGoal = Math.Sqrt((double)(e.ShuttleGoalX - e.CenterX) * (e.ShuttleGoalX - e.CenterX) + (double)(e.ShuttleGoalY - e.CenterY) * (e.ShuttleGoalY - e.CenterY));
                                if (can && e.ShuttleState == ZShuttleState.Cruise && dGoal > 320 && w.Time - t0 > 3) { stop = (sx, sy); airTaken = w.Eject(Team.Red, e); return true; }
                            }
                            return e.ShuttleState == ZShuttleState.Landed && w.Time - t0 > 1;
                        });
                        bool pending = e.ShuttleExitOnLanding;
                        Run(w, 60, () => !e.ShuttleHasLeavers && e.ShuttleState == ZShuttleState.Landed && !e.ShuttleExitPending);
                        var outsA = !e.ShuttleHasLeavers ? e.ShuttleFootIds.Select(id => w.Get(id)).Where(o => o != null && Alive(o)).ToList() : new List<ZObject>();
                        var bA = outsA.FirstOrDefault();
                        ok2 = climbOffered == 0 && samples > 0 && mismatch == 0 && airTaken && pending && e.CenterX == stop.x && e.CenterY == stop.y && outsA.Count == 1 && bA != null && bA.HeroMemberName == "brad" && e.ShuttleIsEmpty && e.Owner == Team.Neutral;
                        sb.AppendLine($"   Brad alone again, flown toward {P(fx, fy)}: EXIT offered in {climbOffered} climb ticks, {offeredAir} of {samples} flying ticks (against the landing test at the stop spot: {mismatch} off); " +
                                      $"given in the cruise (taken {airTaken}, pending {pending}): landed at {P(e.CenterX, e.CenterY)} ({(e.CenterX == stop.x && e.CenterY == stop.y ? "the stop spot" : "ELSEWHERE")}), out {outsA.Count} ({(bA != null ? bA.HeroMemberName ?? bA.Type : "nobody")}), the ship {e.Owner}, empty {e.ShuttleIsEmpty}");
                    }
                }
                else sb.AppendLine("H2. no lone Brad / no place for the drop");
            }
            {   // Alan alone: his own grenades come back with him
                var w = HeroWorld(settings, m16, 13, out var brad);
                var alan = Member(brad, "alan");
                if (brad != null) { brad.KillAt = w.Time; Run(w, 0.5); }                       // Brad gone: Alan leads himself (the pool merged onto his own)
                var e = alan != null ? Drop(w, alan.CenterX, alan.CenterY, 200, 700) : null;
                if (e != null && alan.Leader == null && alan.Minions.Count == 0)
                {
                    int g0 = alan.Grenades, hp0 = alan.Health;
                    g0 = 5; Enter(w, alan, e); BoardWatch(w, e, false, alan, ref g0, ref hp0);
                    bool pilot = e.ShuttleHeroPilot && e.Passengers.Count == 0;
                    var outs = Exit(w, e, out bool taken);
                    var a = outs.FirstOrDefault();
                    ok3 = pilot && taken && outs.Count == 1 && a.HeroMemberName == "alan" && a.Grenades == g0 && a.Health == hp0 && e.ShuttleIsEmpty;
                    sb.AppendLine($"   Alan alone ({g0} grenades): pilot {(pilot ? "Alan" : "NOT a hero")}; EXIT taken {taken}: out {outs.Count} ({(a != null ? (a.HeroMemberName ?? a.Type) + " " + a.Health + " HP, grenades " + a.Grenades : "nobody")}); the ship empty {e.ShuttleIsEmpty}");
                }
                else sb.AppendLine("   no lone Alan / no place for the drop");
            }
            sb.AppendLine(OzV11Kit.Verdict(ok1, "H2. a lone hero is the pilot; EXIT on the ground lets him out as himself (his hit points, the squad's pool), the ship empty"));
            sb.AppendLine(OzV11Kit.Verdict(ok2, "H2. a lone hero pilot in the air: EXIT never in the climb, offered exactly where the landing test at the stop spot passes; given, it lands there, he gets out, the ship empty"));
            sb.AppendLine(OzV11Kit.Verdict(ok3, "H2. Alan alone as the pilot: out on EXIT with his own grenades"));
            return sb.ToString();
        }

        // -------------------------------------------------------------------- H3: heroes with others
        static string H3(string settings, string m16)
        {
            var sb = new StringBuilder();
            bool okA = false, okB = false, okC = false, okD = false;
            {   // (a) a lone grunt pilot, the hero squad his passengers
                var w = HeroWorld(settings, m16, 14, out var brad);
                var e = brad != null ? Drop(w, brad.CenterX, brad.CenterY, 200, 700) : null;
                if (e != null)
                {
                    RobotSpot(w, e.CenterX, e.CenterY, 56, out int lx, out int ly);
                    var lone = OzV11Kit.Place(w, "grunt", lx, ly, Team.Red, 1);
                    Enter(w, lone, e); Board(w, e, false);
                    Enter(w, brad, e); Board(w, e, true);
                    bool aboard = e.Drivers.Count == 1 && !e.ShuttleHeroPilot && e.Passengers.Count == 2 && e.HeroCrewKind(true) != null;
                    var outs = Exit(w, e, out bool taken);
                    var lead = outs.FirstOrDefault(o => !o.IsMinion);
                    okA = aboard && taken && outs.Count == 2 && lead != null && lead.HeroMemberName == "brad" && outs.Any(o => o.HeroMemberName == "alan" && o.Leader == lead) && e.Drivers.Count == 1 && e.DriverType == "grunt" && e.Owner == Team.Red;
                    sb.AppendLine($"H3. a lone grunt the pilot of #{e.Id}, ALAN & BRAD then its passengers ({(aboard ? "as expected" : "NOT as expected")}); EXIT taken {taken}: out {outs.Count} ({string.Join(", ", outs.Select(o => o.HeroMemberName ?? o.Type))}), the grunt pilot stays ({e.Drivers.Count}, {e.Owner})");
                }
            }
            {   // (b) Brad alone the pilot, a grunt squad his passengers
                var w = HeroWorld(settings, m16, 15, out var brad);
                var alan = Member(brad, "alan");
                if (alan != null) { alan.KillAt = w.Time; Run(w, 0.5); }
                var e = brad != null ? Drop(w, brad.CenterX, brad.CenterY, 200, 700) : null;
                if (e != null)
                {
                    brad.Grenades = 1; int bp = brad.Grenades, bh = brad.Health;
                    Enter(w, brad, e); BoardWatch(w, e, false, brad, ref bp, ref bh);
                    RobotSpot(w, e.CenterX, e.CenterY, 56, out int gx, out int gy);
                    var gs = OzV11Kit.Place(w, "grunt", gx, gy, Team.Red); gs.Grenades = 4; int gn = 1 + gs.Minions.Count, gp = gs.Grenades, gh = gs.Health;
                    Enter(w, gs, e); BoardWatch(w, e, true, gs, ref gp, ref gh);
                    bool aboard = e.ShuttleHeroPilot && e.Passengers.Count == gn && e.HeroCrewKind(true) == null;
                    var outs = Exit(w, e, out bool taken);
                    var heroes = outs.Where(o => o.IsHero).ToList(); var grunts = outs.Where(o => !o.IsHero).ToList();
                    var gl = grunts.FirstOrDefault(o => !o.IsMinion);
                    var spots = outs.Select(o => o.CenterX + "," + o.CenterY).Distinct().Count();
                    okB = aboard && taken && outs.Count == gn + 1 && heroes.Count == 1 && heroes[0].HeroMemberName == "brad" && heroes[0].Minions.Count == 0 && heroes[0].Leader == null && heroes[0].Grenades == bp
                          && grunts.Count == gn && gl != null && grunts.All(o => o == gl || o.Leader == gl) && gl.Grenades == gp && spots == outs.Count && e.ShuttleFootIds.Count == gn + 1
                          && e.ShuttleIsEmpty && e.Owner == Team.Neutral;
                    sb.AppendLine($"   Brad alone the pilot of #{e.Id}, a grunt squad of {gn} then its passengers ({(aboard ? "as expected" : "NOT as expected")}); EXIT taken {taken}: out {outs.Count}: {heroes.Count} hero ({string.Join(", ", heroes.Select(o => o.HeroMemberName + (o.Minions.Count == 0 && o.Leader == null ? " as himself" : " IN A SQUAD") + ", grenades " + o.Grenades + " of " + bp))}), " +
                                  $"{grunts.Count} grunts as {(gl != null && grunts.All(o => o == gl || o.Leader == gl) ? "one squad" : "NOT one squad")} (pool {gl?.Grenades} of {gp}); {spots} places in one line of {e.ShuttleFootIds.Count}; the ship {e.Owner}, empty {e.ShuttleIsEmpty}");
                }
            }
            {   // (c) level 16's own ship, the hero squad its passengers
                var w = HeroWorld(settings, m16, 16, out var brad);
                var ship = w.PlayableShuttle;
                if (ship != null && brad != null)
                {
                    int d0 = ship.Drivers.Count;
                    Enter(w, brad, ship); Board(w, ship, true);
                    bool aboard = ship.Passengers.Count == 2 && !ship.ShuttleHeroPilot && ship.HeroCrewKind(true) != null;
                    var outs = Exit(w, ship, out bool taken);
                    okC = aboard && taken && outs.Count == 2 && outs.All(o => o.IsHero) && ship.Drivers.Count == d0 && d0 == 1 && ship.Owner == Team.Red && !ship.ShuttleIsEmpty;
                    sb.AppendLine($"   level 16's own ship #{ship.Id} (its pilot {d0}), ALAN & BRAD its passengers ({(aboard ? "as expected" : "NOT as expected")}); EXIT taken {taken}: out {outs.Count} ({string.Join(", ", outs.Select(o => o.HeroMemberName ?? o.Type))}), its own pilot stays ({ship.Drivers.Count}, {ship.Owner})");
                }
            }
            {   // (d) ALAN & BRAD split apart (DV98: a member left far behind becomes a squad of his own): Brad alone the pilot, Alan alone his passenger
                var w = HeroWorld(settings, m16, 18, out var brad);
                var alan = Member(brad, "alan");
                var e = brad != null ? Drop(w, brad.CenterX, brad.CenterY, 200, 700) : null;
                bool splitWas = ZWorld.SquadSplitDeviation;
                if (e != null && alan != null && RobotSpot(w, brad.CenterX, brad.CenterY, ZWorld.SquadSplitPx + 96, out int fx, out int fy, ZWorld.SquadSplitPx + 900))
                {
                    int ax0 = alan.CenterX, ay0 = alan.CenterY;
                    ZWorld.SquadSplitDeviation = true;
                    alan.SetCords(fx - (alan.WidthPix >> 1), fy - (alan.HeightPix >> 1));          // far from Brad: the game's own split (DV98) makes them two squads
                    Run(w, 2.5, () => alan.Leader == null && brad.Minions.Count == 0);
                    ZWorld.SquadSplitDeviation = splitWas;
                    bool split = alan.Leader == null && brad.Leader == null && brad.Minions.Count == 0 && alan.Minions.Count == 0;
                    alan.SetCords(ax0 - (alan.WidthPix >> 1), ay0 - (alan.HeightPix >> 1));        // back where he stood (a squad of his own now)
                    brad.Grenades = 2; int bp = brad.Grenades, bh = brad.Health;
                    Enter(w, brad, e); BoardWatch(w, e, false, brad, ref bp, ref bh);
                    int ag = alan.Grenades, ah = alan.Health;
                    Enter(w, alan, e); BoardWatch(w, e, true, alan, ref ag, ref ah);
                    bool aboard = e.ShuttleHeroPilot && e.Passengers.Count == 1 && e.HeroCrewKind(true) == brad.Hero && !e.ShuttlePilotSquadIn;
                    int gIn = bp + ag;
                    var outs = Exit(w, e, out bool taken);
                    var b = outs.FirstOrDefault(o => o.HeroMemberName == "brad"); var a = outs.FirstOrDefault(o => o.HeroMemberName == "alan");
                    int gOut = outs.Sum(o => o.Grenades);
                    okD = split && aboard && taken && outs.Count == 2 && b != null && a != null && b.Leader == null && b.Minions.Count == 0 && a.Leader == null && a.Minions.Count == 0
                          && b.Grenades == bp && a.Grenades == ag && gOut == gIn && b.Health == bh && a.Health == ah && e.ShuttleIsEmpty && e.Owner == Team.Neutral;
                    sb.AppendLine($"   ALAN & BRAD split apart ({(split ? "two squads" : "NOT split")}): Brad alone the pilot of #{e.Id} (pool {bp}), Alan alone then its passenger (his own {ag}) ({(aboard ? "as expected" : "NOT as expected")}); " +
                                  $"EXIT taken {taken}: out {outs.Count} ({string.Join(", ", outs.Select(o => (o.HeroMemberName ?? o.Type) + (o.Leader == null && o.Minions.Count == 0 ? " as himself" : o.Leader == null ? " leads " + o.Minions.Count : " in a squad") + ", grenades " + o.Grenades))}); " +
                                  $"grenades in {gIn}, out {gOut}; the ship {e.Owner}, empty {e.ShuttleIsEmpty}");
                }
                else { ZWorld.SquadSplitDeviation = splitWas; sb.AppendLine("   no place to split ALAN & BRAD / no drop"); }
            }
            sb.AppendLine(OzV11Kit.Verdict(okA, "H3. the hero squad as the passengers of a lone grunt pilot: EXIT puts the heroes out (Brad leads), the grunt pilot stays"));
            sb.AppendLine(OzV11Kit.Verdict(okB, "H3. a grunt squad as the passengers of a lone hero pilot: EXIT puts everyone out (the grunts as their squad, Brad as himself, one line on the back slots), the ship empty"));
            sb.AppendLine(OzV11Kit.Verdict(okC, "H3. level 16's own ship keeps its own pilot: the hero squad aboard gets out on EXIT, the pilot stays"));
            sb.AppendLine(OzV11Kit.Verdict(okD, "H3. ALAN & BRAD split apart (each a squad of his own), Brad the pilot and Alan his passenger: EXIT puts both out as they went in, every grenade once (none doubled), the ship empty"));
            return sb.ToString();
        }

        // -------------------------------------------------------------------- H4: shot down with the heroes aboard
        static string H4(string settings, string m16)
        {
            var sb = new StringBuilder();
            var w = HeroWorld(settings, m16, 17, out var brad);
            var e = brad != null ? Drop(w, brad.CenterX, brad.CenterY, 200, 700) : null;
            bool ok = false;
            if (e != null)
            {
                Enter(w, brad, e); Board(w, e, false);
                PadSpot(w, e, e.CenterX, e.CenterY, 400, 700, out int hx, out int hy);
                OzV11Kit.PlayerMove(w, e, hx, hy); Run(w, 30, () => e.ShuttleState == ZShuttleState.Cruise);
                bool heroPilot = e.ShuttleHeroPilot && e.Passengers.Count == 1;
                var before = new HashSet<ZObject>(w.Objects);
                int seq0 = w.ShuttleLostSeq, lost0 = ZObject.ShuttleHeroPilotsLost;
                e.DamageHealth(e.Health); w.UpdateObjectHealth(e); Run(w, 1.0);
                int made = w.Objects.Count(o => o.IsRobot && !before.Contains(o));
                int heroesLeft = w.Objects.Count(o => o.IsHero && Alive(o));
                var rows = w.ShuttleLostCrew.Where(r => r.shipId == e.Id && r.seq > seq0).ToList();
                ok = heroPilot && e.Destroyed && made == 0 && heroesLeft == 0 && rows.Count == 2 && rows[0].member == "alan" && rows[1].member == "brad" && rows.All(r => r.hero == ZHeroSquads.AlanBrad && r.airborne)
                     && ZObject.ShuttleHeroPilotsLost == lost0 + 1;
                sb.AppendLine($"H4. Brad the pilot and Alan aboard #{e.Id}, shot down in the air at {e.ShuttleHeightM:0.0} m: destroyed {e.Destroyed}, robots made after it {made}, heroes alive {heroesLeft}; " +
                              $"rows for the view's bodies {rows.Count} ({string.Join(", ", rows.Select(r => r.member + " " + r.index + "/" + r.count))})");
            }
            sb.AppendLine(OzV11Kit.Verdict(ok, "H4. shot down with the heroes aboard: both lost with everyone aboard (nobody gets out), both listed for the view's bodies"));
            return sb.ToString();
        }

        // -------------------------------------------------------------------- V: no vehicle or gun ever boards a shuttle
        static string V(string settings, string m16)
        {
            var sb = new StringBuilder();
            var w = ZWorld.Create(settings, m16, 21);
            w.Bots.Add(new ZBot(w, Team.Blue, 121) { Difficulty = ZBotDifficulty.Easy }); w.Events.Clear();   // the CPU plays blue (its target lists, car park and orders run)
            var fort = w.Objects.First(o => o.IsFort && o.Owner == Team.Red && !o.Destroyed);
            var empty = Drop(w, fort.CenterX, fort.CenterY, 220, 900);
            var crewed = empty != null ? Drop(w, empty.CenterX, empty.CenterY, 220, 900, Team.Red) : null;
            if (empty == null || crewed == null) { sb.AppendLine(OzV11Kit.Verdict(false, "V. an empty and a crewed shuttle on level 16")); return sb.ToString(); }
            var other = Drop(w, crewed.CenterX, crewed.CenterY, 220, 900, Team.Red);                // another shuttle (it is a vehicle too)
            var units = new List<ZObject>();
            foreach (var ship in new[] { empty, crewed })
                foreach (var t in VehicleTypes)
                    if (VehicleSpot(w, ship.CenterX, ship.CenterY, 64, 260, out int x, out int y)) units.Add(OzV11Kit.Place(w, t, x, y, Team.Red));
            var blue = new List<ZObject>();
            foreach (var t in VehicleTypes)
                if (VehicleSpot(w, empty.CenterX, empty.CenterY, 64, 320, out int x, out int y)) blue.Add(OzV11Kit.Place(w, t, x, y, Team.Blue));
            if (other != null) units.Add(other);
            string Crew(ZObject s) => $"{s.Owner} {s.Drivers.Count}+{s.Passengers.Count}";
            string e0 = Crew(empty), c0 = Crew(crewed);
            int taken0 = ZObject.ShuttleEmptyTaken;
            // 1. the player's ENTER order on each ship, every unit; the CPU's for its units on the empty one
            int sent = 0, kept = 0;
            foreach (var u in units)
                foreach (var ship in new[] { empty, crewed })
                {
                    if (u == ship) continue;
                    w.SendWaypoints(Team.Red, u, new[] { new ZWaypoint { mode = WpMode.Enter, refId = ship.Id, x = ship.CenterX, y = ship.CenterY } }); sent++;
                    if (u.Waypoints.Any(q => q.mode == WpMode.Enter)) kept++;
                }
            foreach (var u in blue)
            {
                w.SendWaypoints(Team.Blue, u, new[] { new ZWaypoint { mode = WpMode.Enter, refId = empty.Id, x = empty.CenterX, y = empty.CenterY, playerGiven = true } }); sent++;
                if (u.Waypoints.Any(q => q.mode == WpMode.Enter)) kept++;
            }
            sb.AppendLine($"V. level 16: an empty shuttle #{empty.Id} ({e0}) and a crewed one #{crewed.Id} ({c0}); {units.Count} red vehicles, guns and a shuttle beside them ({string.Join(", ", units.Select(u => u.Type).Distinct())}), {blue.Count} blue (the CPU's)");
            sb.AppendLine($"   ENTER orders on a shuttle sent for them: {sent}, kept: {kept}");
            // 2. an ENTER waypoint put in by hand (no order check): the unit's own step must drop it without boarding
            int put = 0;
            foreach (var u in units.Concat(blue))
            {
                if (u.IsShuttle) { u.Waypoints.Clear(); u.Waypoints.Add(new ZWaypoint { mode = WpMode.Enter, refId = empty.Id, x = empty.CenterX, y = empty.CenterY, playerGiven = true }); put++; continue; }
                var ship = u.Owner == Team.Red && put % 2 == 1 ? crewed : empty;
                u.Waypoints.Clear(); u.Waypoints.Add(new ZWaypoint { mode = WpMode.Enter, refId = ship.Id, x = ship.CenterX, y = ship.CenterY, playerGiven = true }); put++;
            }
            int holdTicks = 0;
            Run(w, 3.0, () => { holdTicks += units.Concat(blue).Count(u => Alive(u) && u.Waypoints.Any(q => q.mode == WpMode.Enter)); return false; });
            int stillPut = units.Concat(blue).Count(u => Alive(u) && u.Waypoints.Any(q => q.mode == WpMode.Enter));   // (a unit destroyed meanwhile never steps again)
            sb.AppendLine($"   ENTER waypoints put in by hand: {put}; 3 s later still held {stillPut} (unit-ticks while held {holdTicks}); the ships {Crew(empty)} / {Crew(crewed)}");
            // 3. the soak: the CPU playing, every unit idle beside the ships
            int enterTicks = 0, gone = 0; var who = new HashSet<string>();
            double end = w.Time + VehicleSoakSec;
            while (w.Time < end && !w.GameOver)
            {
                w.Tick(Tick);
                foreach (var o in w.Objects)
                {
                    if (o.IsRobot || o.Waypoints.Count == 0) continue;
                    foreach (var q in o.Waypoints)
                        if (q.mode == WpMode.Enter) { var t = w.Get(q.refId); if (t != null && t.IsShuttle) { enterTicks++; who.Add(o.Type); } break; }
                }
                w.Events.Clear();
            }
            foreach (var u in units.Concat(blue)) if (!Alive(u)) gone++;
            bool shipsSame = Crew(empty) == e0 && Crew(crewed) == c0 && empty.ShuttleIsEmpty && ZObject.ShuttleEmptyTaken == taken0;
            sb.AppendLine($"   {VehicleSoakSec:0} s with the CPU playing: vehicle- / gun-ticks with an ENTER waypoint on a shuttle {enterTicks}{(who.Count > 0 ? " (" + string.Join(", ", who) + ")" : "")}; the ships {Crew(empty)} / {Crew(crewed)} (before {e0} / {c0}), empty ships taken {ZObject.ShuttleEmptyTaken - taken0}; units lost meanwhile {gone}");
            sb.AppendLine(OzV11Kit.Verdict(sent > 0 && kept == 0, "V. the player's and the CPU's ENTER order on a shuttle is refused for every vehicle, gun and shuttle (crewed and empty ship)"));
            sb.AppendLine(OzV11Kit.Verdict(put > 0 && stillPut == 0 && Crew(empty) == e0 && Crew(crewed) == c0, "V. an ENTER waypoint on a shuttle put in by hand is dropped by the unit's own step: nobody boards"));
            sb.AppendLine(OzV11Kit.Verdict(enterTicks == 0 && shipsSame, "V. with the CPU playing, no vehicle or gun ever holds an ENTER waypoint on a shuttle; the ships' crews never change"));
            return sb.ToString();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Shuttle: K256f2 Heroes Exit Check")]
        static void Menu() => OzV11Kit.RunMenu("K256f2 the heroes get out, no vehicle boards a shuttle", () => Check(OzV11Kit.SettingsText, OzV11Kit.MapText("L16"), OzV11Kit.MapText("L02")));
#endif
    }
}
