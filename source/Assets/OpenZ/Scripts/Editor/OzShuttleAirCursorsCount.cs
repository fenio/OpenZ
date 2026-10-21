using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K430b counted check (UI/OzShuttleAirOffers.cs: no EXIT and no boarding offered while the shuttle is up in the air), headless on level
    /// 16 (ZWorld.Create + Tick, no scene, nothing saved; public members only). Level 16's own ship is flown to a free pad by the player's
    /// move order, a normal and a crash landing (the rules' proof switch), and every tick from the order to a few seconds after the touchdown
    /// the player's side is asked as the game's code asks it:
    ///  * the ship selected, the pointer on it: the cursor (UI/OpenZCursors.cs: "exit" only when OzShuttleAirOffers.Exit, else the move cursor:
    ///    an own ship is no hover target without robots selected, View/PlayerInput.cs HoverAt), the EXIT CREW button / its key / the touch key
    ///    / the gamepad button (UI/OpenZHud.cs CanExit -> PlayerInput.EjectUnit) and the right-click on the ship (PlayerInput.TryEject: EXIT
    ///    when offered, else the click is a move order to the spot under the pointer);
    ///  * robots selected, the pointer on the ship: the cursor (PlayerInput.HoverAt makes an own carrier a target only when it takes the
    ///    squad, ZObject.CanTakePassengers; OpenZCursors then shows "enter", else the ground under it: "move") and the click
    ///    (PlayerInput.BuildWaypoint: ENTER when the ship takes the squad, else a MOVE to the clicked ground).
    /// The twins above repeat the live lines (the Play proof View/OzShuttleAirCursorsProof.cs reads the real cursor, button and clicks). Lines:
    ///  A1 a squad aboard, up in the air (climb, cruise, landing turn, descent): EXIT offered on 0 ticks, though the rules would take it on
    ///     many of them (K256f, kept: the cause was the offer, not the rules);
    ///  A2 from the touchdown tick on, a crash's too while it crashes, rests tilted and rights itself (K426): EXIT offered on every tick;
    ///  A3 the player's EXIT orders up in the air (the button path and the right-click, in the climb, the cruise where the rules would take
    ///     it, the descent and its last 3 m): refused, nothing given (no EXIT on landing, nothing pending, everyone aboard);
    ///  A4 the player's EXIT at the touchdown tick: taken; a normal landing's ramp down at once, a crash's held until it stands level; the
    ///     squad out a ramp's swing later;
    ///  A5 the test switch off (the old offers): up in the air the EXIT cursor shows exactly where the rules take it (the switch is the cause);
    ///  A6 the right-click on the flying ship: no EXIT, a move order to the spot under it (it comes down there); on the ground EXIT works;
    ///  A7 over all the flights the rules took no EXIT up in the air (K256f's path is kept and reached by no player control);
    ///  B1 the ship with its pilot only (room for a squad), robots selected, up in the air: the ENTER cursor on 0 ticks, the click on the ship
    ///     a MOVE on every tick;
    ///  B2 an ENTER order on the airborne ship (climb, cruise, descent) is refused by the rules' order check: no ENTER waypoint, nobody boards;
    ///  B3 from the touchdown tick on (a crash's too): the ENTER cursor on every tick, the click's ENTER order taken, the squad boards;
    ///  D the same flight twice gives the same numbers.
    /// The rules are not changed by K430b (no DV row); the computer never flies a shuttle.
    /// </summary>
    public static class OzShuttleAirCursorsCount
    {
        const double Tick = ZConst.TICK;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public const int SquadSize = 4;
        const Team Me = Team.Red;

        static string settings, m16;
        static StringBuilder sb; static int pass, fail;
        static void V(bool ok, string what) { sb.AppendLine(OzV11Kit.Verdict(ok, what)); if (ok) pass++; else fail++; }
        static void L(string s) => sb.AppendLine("      " + s);
        static string F(double v, string f = "0.00") => v.ToString(f, Inv);

        // ------------------------------------------------------------------ the player's side, as the game's code decides it
        /// <summary>UI/OpenZCursors.cs Decide, the single selected ship with the pointer on it.</summary>
        static string CursorShip(ZObject s) => OzShuttleAirOffers.Exit(s) ? "exit" : "move";
        /// <summary>Robots selected, the pointer on the ship (PlayerInput.HoverAt, then OpenZCursors.Decide).</summary>
        static string CursorRobots(ZObject s) => s.CanTakePassengers(Me) ? "enter" : "move";
        /// <summary>PlayerInput.BuildWaypoint for robot u, the click on the ship (its hover target only when it takes the squad).</summary>
        static WpMode ClickRobots(ZObject u, ZObject s)
        {
            var hover = s.CanTakePassengers(Me) ? s : null;
            if (hover != null && ((hover.Owner == Team.Neutral && (hover.Kind == ZKind.Vehicle || hover.Kind == ZKind.Cannon)) || hover.CanTakePassengers(u))) return WpMode.Enter;
            return WpMode.Move;
        }
        /// <summary>PlayerInput.EjectUnit (the order).</summary>
        static bool EjectUnit(ZWorld w, ZObject s) => OzShuttleAirOffers.ExitOrder(s) && w.Eject(Me, s);
        /// <summary>OpenZHud.ExitSelected (the EXIT CREW button, its key, the touch key, the gamepad button): CanExit, then EjectUnit.</summary>
        static bool HudExit(ZWorld w, ZObject s) => s.Owner == Me && OzShuttleAirOffers.Exit(s) && !w.GameOver && EjectUnit(w, s);
        /// <summary>PlayerInput.TryEject (the right-click on the single selected ship): true = EXIT given; false = the click goes on as a move order.</summary>
        static bool RightClickExit(ZWorld w, ZObject s) { if (!OzShuttleAirOffers.Exit(s)) return false; EjectUnit(w, s); return true; }

        // ------------------------------------------------------------------ staging
        sealed class Ctx { public ZWorld w; public ZObject s, lead, sel; }

        sealed class Phase { public string name; public int ticks, rules, offered, cursorExit, enterCursor, clickEnter, rulesBoard; }

        sealed class Rec
        {
            public bool ok, rough; public string why = "";
            public Ctx c;
            public double orderAt = -1, firstUp = -1, td = -1, level = -1, offeredFirst = -1;
            public readonly List<Phase> phases = new List<Phase>();
            public int airTicks, airRules, airOffered, airCursorExit, airEnter, airClickEnter, airRulesBoard;
            public int gndTicks, gndOffered, gndEnter, gndClickEnter;   // from the touchdown tick on (until the order of A4 / B3)
            public bool stopGround;                                       // A4 / B3 gave their order: the ground counts end there
            public bool retry;                                            // onTick asks for another flight (A6: the spot under the ship takes no landing)
            public Phase Of(string n) { var p = phases.FirstOrDefault(x => x.name == n); if (p == null) { p = new Phase { name = n }; phases.Add(p); } return p; }
            public string Table()
            {
                var t = new StringBuilder();
                foreach (var p in phases)
                    t.AppendLine($"      {p.name,-38} {p.ticks,5} ticks | the rules take EXIT {p.rules,5} | EXIT offered {p.offered,5} | cursor 'exit' {p.cursorExit,5} | rules take a squad {p.rulesBoard,5} | ENTER cursor {p.enterCursor,5} | click = ENTER {p.clickEnter,5}");
                return t.ToString().TrimEnd();
            }
        }

        static void Run(ZWorld w, double secs, Func<bool> until = null)
        {
            double end = w.Time + secs;
            while (w.Time < end - 1e-9) { w.Tick(Tick); w.Events.Clear(); if (until != null && until()) return; }
        }

        static ZWorld W16(int seed)
        {
            ZWorld.ShuttlePlayableDeviation = true; ZWorld.ShuttleOn = true; ZWorld.ShuttleTeam = Me; ZWorld.ShuttleRoughProofNext = false;
            return OzV11Kit.World(settings, m16, seed);
        }

        static bool FreeSpot(ZWorld w, int x, int y, out int fx, out int fy, ZObject keepFrom, double minFrom)
        {
            fx = fy = 0; double best = 1e18;
            for (int ty = (y >> 4) - 8; ty <= (y >> 4) + 8; ty++)
                for (int tx = (x >> 4) - 8; tx <= (x >> 4) + 8; tx++)
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

        /// <summary>Level 16's own ship, the ground round it cleared. aboard: a red grunt squad boards (the ramp shut again); else the ship keeps
        /// its pilot only and a red grunt squad stands well away from it (the robots of the selection).</summary>
        static Ctx Staged(int seed, bool aboard)
        {
            var c = new Ctx { w = W16(seed) };
            c.s = c.w.PlayableShuttle;
            if (c.s == null) return c;
            foreach (var o in c.w.Objects.ToList())
                if ((o.IsRobot || (o.Kind == ZKind.Vehicle && !o.IsShuttle)) && OzV11Kit.Alive(o) && OzV11Kit.Dist(o, c.s) < 260) o.KillAt = c.w.Time;
            Run(c.w, 0.2);
            var (bx, by) = c.s.ShuttleBackPoint(5);
            if (aboard)
            {
                FreeSpot(c.w, bx, by, out int fx, out int fy, c.s, 70);
                c.lead = c.w.CreateRobotGroup("grunt", fx - 8, fy - 8, Me, SquadSize);
                OzV11Kit.PlayerEnter(c.w, c.lead, c.s);
                Run(c.w, 60, () => c.s.Passengers.Count > 0);
                Run(c.w, 20, () => c.s.ShuttleRampPhase == ZShuttleRamp.Shut && !c.s.ShuttleExitPending && c.w.Time >= c.s.ShuttleRampHoldUntil);
            }
            else
            {
                FreeSpot(c.w, bx, by, out int fx, out int fy, c.s, 120);
                c.sel = c.w.CreateRobotGroup("grunt", fx - 8, fy - 8, Me, SquadSize);
                Run(c.w, 0.5);
            }
            return c;
        }

        static string PhaseOf(Ctx c, bool wasUp)
        {
            var s = c.s;
            if (s.ShuttleAirborne)
            {
                switch (s.ShuttleState)
                {
                    case ZShuttleState.Climb: return "air: take-off climb";
                    case ZShuttleState.Descend: return s.ShuttleHeightM <= 3.0 ? "air: descent, last 3 m" : "air: descent";
                    case ZShuttleState.Hover: return "air: hover";
                    case ZShuttleState.Cruise: return s.ShuttleLandTurning ? "air: landing turn" : "air: cruise";
                }
                return "air: lifting";
            }
            if (!wasUp) return s.ShuttleTakeOffHeld ? "ground: take-off held" : "ground: before the take-off";
            if (c.w.Time < s.ShuttleLevelledAt - 1e-9) return "ground: crash, not level yet (K426)";
            return "ground: after the touchdown";
        }

        /// <summary>One flight to a free pad (a normal or a crash landing), sampled every tick until `afterTd` s after the touchdown.
        /// onTick(ctx, rec, phase) may give orders (A3, A4, B2, B3, A6); it returns true to end the flight's sampling early.</summary>
        static Rec Fly(bool rough, int seed0, bool aboard, double afterTd, Func<Ctx, Rec, string, bool> onTick = null)
        {
            Rec last = null;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var c = Staged(seed0 + attempt, aboard);
                var r = new Rec { rough = rough, c = c }; last = r;
                if (c.s == null) { r.why = "no shuttle on level 16"; continue; }
                if (aboard && c.s.Passengers.Count == 0) { r.why = "the squad did not board"; continue; }
                if (!PadSpot(c.w, c.s, 400 + attempt * 64, 1100, out int gx, out int gy)) { r.why = "no free pad"; continue; }
                ZWorld.ShuttleRoughProofNext = rough;
                OzV11Kit.PlayerMove(c.w, c.s, gx, gy);                                                   // the player's right-click with the ship selected (BuildWaypoint: a vehicle's MOVE)
                r.orderAt = c.w.Time;
                bool up = false, early = false; double end = c.w.Time + 150;
                while (c.w.Time < end)
                {
                    c.w.Tick(Tick); c.w.Events.Clear();
                    var s = c.s;
                    if (s.Destroyed) { r.why = "the ship was destroyed"; break; }
                    if (s.ShuttleAirborne && !up) { up = true; r.firstUp = c.w.Time; }
                    if (up && !s.ShuttleAirborne && r.td < 0) { r.td = s.ShuttleTouchdownAt; r.level = s.ShuttleLevelledAt; ZWorld.ShuttleRoughProofNext = false; }
                    string ph = PhaseOf(c, up);
                    Sample(c, r, ph, up);
                    if (onTick != null && onTick(c, r, ph)) { early = true; break; }
                    if (r.td >= 0 && c.w.Time >= r.td + afterTd) break;
                    if (s.ShuttleState == ZShuttleState.Hover && !string.IsNullOrEmpty(s.ShuttleNoLand) && r.td < 0 && onTick == null) { r.why = "it could not land (" + s.ShuttleNoLand + ")"; break; }
                }
                ZWorld.ShuttleRoughProofNext = false;
                if (r.retry) { r.why = "another flight asked"; continue; }
                if (r.td < 0 && !early) { if (r.why == "") r.why = "no touchdown"; continue; }
                if (r.td >= 0 && c.s.ShuttleRoughLanding != rough) { r.why = "the rules made it a " + (c.s.ShuttleRoughLanding ? "crash" : "normal") + " landing"; continue; }
                r.ok = true;
                return r;
            }
            return last ?? new Rec { why = "not staged" };
        }

        static void Sample(Ctx c, Rec r, string ph, bool up)
        {
            var s = c.s; var p = r.Of(ph);
            bool rules = s.CanEjectCrew(), offered = OzShuttleAirOffers.Exit(s), cursorExit = CursorShip(s) == "exit";
            bool rulesBoard = s.CanTakePassengers(Me), enter = CursorRobots(s) == "enter";
            var u = c.sel ?? c.lead;
            bool clickEnter = u != null && OzV11Kit.Alive(u) && ClickRobots(u, s) == WpMode.Enter;
            p.ticks++; if (rules) p.rules++; if (offered) p.offered++; if (cursorExit) p.cursorExit++; if (rulesBoard) p.rulesBoard++; if (enter) p.enterCursor++; if (clickEnter) p.clickEnter++;
            if (s.ShuttleAirborne)
            {
                r.airTicks++; if (rules) r.airRules++; if (offered) r.airOffered++; if (cursorExit) r.airCursorExit++; if (enter) r.airEnter++; if (clickEnter) r.airClickEnter++; if (rulesBoard) r.airRulesBoard++;
            }
            else if (up && r.td >= 0 && !r.stopGround)
            {
                r.gndTicks++; if (offered) r.gndOffered++; if (enter) r.gndEnter++; if (clickEnter) r.gndClickEnter++;
                if (offered && r.offeredFirst < 0) r.offeredFirst = c.w.Time - r.td;
            }
        }

        // ------------------------------------------------------------------ the check
        public static string Check(string settingsText, string level16)
        {
            settings = settingsText; m16 = level16;
            sb = new StringBuilder(); pass = fail = 0;
            bool keep = OzShuttleAirOffers.GroundOnly;
            int held0 = OzShuttleAirOffers.AirExitHeld, refused0 = OzShuttleAirOffers.AirExitOrdersRefused, airExits0 = ZObject.ShuttleAirExits;
            sb.AppendLine("K430b: no EXIT and no boarding offered while the shuttle is up in the air (UI/OzShuttleAirOffers.cs); headless, level 16, the rules' clock");
            sb.AppendLine($"   the rules' tick {F(Tick, "0.000")} s; a crash: level {F(ZWorld.ShuttleRoughCrashSec + ZWorld.ShuttleRoughRestSec + ZWorld.ShuttleRoughLevelSec)} s after the touchdown (K426 switch {ZWorld.ShuttleExitSoonerOn}); the ramp {F(ZWorld.ShuttleRampOpenSec)} s");
            try
            {
                OzShuttleAirOffers.GroundOnly = true;
                PartA();
                PartB();
                Determinism();
            }
            finally
            {
                OzShuttleAirOffers.GroundOnly = keep; ZWorld.ShuttleRoughProofNext = false;
            }
            int airTaken = ZObject.ShuttleAirExits - airExits0;
            V(airTaken == 0 && OzShuttleAirOffers.AirExitOrdersRefused - refused0 >= 8,
              $"A7 over all the flights: EXIT offers held back up in the air {OzShuttleAirOffers.AirExitHeld - held0} times, the player's EXIT orders refused up in the air {OzShuttleAirOffers.AirExitOrdersRefused - refused0}, EXITs the rules took up in the air {airTaken} (K256f's path kept, reached by no player control)");
            sb.AppendLine($"K430b result: {pass} PASS, {fail} FAIL");
            return sb.ToString();
        }

        sealed class OrderA
        {
            public Rec rec; public readonly Dictionary<string, bool> tried = new Dictionary<string, bool>();
            public int ok, bad, rulesWould, opens0 = -1, aboard, rampWhileTilted; public bool given, taken; public double rampStart = -1, outAt = -1;
        }

        sealed class ClickA6 { public Rec rec; public bool clicked, rcExit, down, hovered; public int gx0, gy0, sx, sy; public string log = ""; }

        sealed class OrderB { public Rec rec; public readonly Dictionary<string, bool> forced = new Dictionary<string, bool>(); public int ok, bad; public bool ordered, taken; public double boardedAt = -1; public ZObject sq; }

        static void PartA()
        {
            sb.AppendLine("A. the ship selected, a red grunt squad of 4 aboard, the pointer on the ship");
            foreach (bool rough in new[] { false, true })
            {
                string tag = rough ? "crash" : "normal";
                // A1 / A2: watched, no order (4 s after the touchdown: a crash stands level 1.7 s after it)
                var r = Fly(rough, rough ? 120 : 140, true, 4.0);
                if (!r.ok) { V(false, $"A1/A2 {tag} landing staged ({r.why})"); }
                else
                {
                    L($"{tag} landing, watched: the order at {F(r.orderAt)} s, up at {F(r.firstUp)} s, the touchdown at {F(r.td)} s, level +{F(r.level - r.td)} s");
                    sb.AppendLine(r.Table());
                    V(r.airTicks > 100 && r.airOffered == 0 && r.airCursorExit == 0 && r.airRules > 0,
                      $"A1 {tag}: up in the air ({r.airTicks} ticks: climb, cruise, landing turn, descent) EXIT offered on {r.airOffered} ticks, the cursor 'exit' on {r.airCursorExit}; the rules would take it on {r.airRules} (K256f, kept)");
                    V(r.gndTicks >= 300 && r.gndOffered == r.gndTicks && r.offeredFirst >= 0 && r.offeredFirst <= Tick + 1e-9,
                      $"A2 {tag}: from the touchdown tick on EXIT offered on {r.gndOffered} of {r.gndTicks} ticks (4 s), first at +{F(r.offeredFirst)} s{(rough ? $" (crooked until +{F(r.level - r.td)} s: K426)" : "")}");
                }
                // A3 / A4: EXIT tried in every phase up in the air (refused), then given on the touchdown tick
                var st = new OrderA();
                var ro2 = Fly(rough, rough ? 120 : 140, true, 14.0, (c, rec, ph) =>
                {
                    if (st.rec != rec) st = new OrderA { rec = rec };
                    var s = c.s; var w = c.w;
                    if (st.opens0 < 0) { st.opens0 = s.ShuttleRampOpens; st.aboard = s.Passengers.Count; }
                    if (s.ShuttleAirborne && !st.tried.ContainsKey(ph) && (ph != "air: cruise" || (w.Time - rec.firstUp > ZWorld.ShuttleClimbSec + 1.0 && s.CanEjectCrew())))
                    {   // the player's EXIT in this phase: the button path, the right-click, the order itself
                        st.tried[ph] = true;
                        int pax = s.Passengers.Count; bool rules = s.CanEjectCrew();
                        bool hud = HudExit(w, s), rc = RightClickExit(w, s), sink = EjectUnit(w, s);
                        bool nothing = !hud && !rc && !sink && !s.ShuttleExitOnLanding && !s.ShuttleExitPending && s.Passengers.Count == pax;
                        if (nothing) st.ok++; else st.bad++;
                        if (rules) st.rulesWould++;
                        L($"{tag} {ph}: the EXIT CREW button path {(hud ? "GIVEN" : "refused")}, the right-click {(rc ? "EXIT" : "a move order")}, the order itself {(sink ? "TAKEN" : "refused")}; the rules would take it now {rules}; EXIT on landing {s.ShuttleExitOnLanding}, pending {s.ShuttleExitPending}, aboard {s.Passengers.Count}");
                    }
                    if (!s.ShuttleAirborne && rec.td >= 0 && !st.given)
                    {   // the touchdown tick (the first tick on the ground): the player's EXIT
                        rec.stopGround = true; st.given = true;
                        st.taken = HudExit(w, s);
                        L($"{tag} the touchdown tick (+{F(w.Time - rec.td)} s, {(w.Time < s.ShuttleLevelledAt - 1e-9 ? "not level" : "level")}): EXIT CREW {(st.taken ? "taken" : "REFUSED")}, pending {s.ShuttleExitPending}");
                        return false;
                    }
                    if (st.given)
                    {
                        if (w.Time < s.ShuttleLevelledAt - 1e-6 && s.ShuttleRamp01 > 0) st.rampWhileTilted++;
                        if (st.rampStart < 0 && s.ShuttleRampOpens != st.opens0) st.rampStart = w.Time - rec.td;
                        if (st.outAt < 0 && s.Passengers.Count == 0) { st.outAt = w.Time - rec.td; return true; }
                    }
                    return false;
                });
                if (!ro2.ok || st.rec != ro2) { V(false, $"A3/A4 {tag} landing staged ({ro2.why})"); continue; }
                L($"{tag} landing, EXIT tried: the touchdown at {F(ro2.td)} s, level +{F(ro2.level - ro2.td)} s; up in the air EXIT offered on {ro2.airOffered} of {ro2.airTicks} ticks");
                V(st.ok >= 4 && st.bad == 0 && st.rulesWould >= 3 && ro2.airOffered == 0,
                  $"A3 {tag}: the player's EXIT up in the air refused in {st.ok} phases ({string.Join(", ", st.tried.Keys)}; the rules would have taken it in {st.rulesWould} of them), given in {st.bad}; nothing pending, everyone aboard");
                double ro = ExitAfterSec();
                bool rampOk = rough ? st.rampWhileTilted == 0 && st.rampStart >= 0 && Math.Abs(st.rampStart - (ro2.level - ro2.td)) <= 0.05 : st.rampStart >= 0 && st.rampStart <= 0.05;
                V(st.taken && rampOk && st.outAt >= 0 && Math.Abs(st.outAt - st.rampStart - ro) <= 0.05,
                  $"A4 {tag}: EXIT on the touchdown tick taken {st.taken}; the ramp down from +{F(st.rampStart)} s ({(rough ? $"held while crooked: it moved on {st.rampWhileTilted} ticks; level +{F(ro2.level - ro2.td)} s" : "at once")}), the squad of {st.aboard} out +{F(st.outAt)} s (a ramp's swing, {F(ro)} s, later)");
            }
            // A5: the test switch off: the old offers
            OzShuttleAirOffers.GroundOnly = false;
            Rec old;
            try { old = Fly(false, 160, true, 0.5); }
            finally { OzShuttleAirOffers.GroundOnly = true; }
            if (!old.ok) V(false, $"A5 the old offers' flight staged ({old.why})");
            else V(old.airCursorExit > 0 && old.airCursorExit == old.airRules,
                   $"A5 the test switch off (the old offers): up in the air the cursor 'exit' on {old.airCursorExit} ticks = the ticks the rules take EXIT ({old.airRules}) of {old.airTicks}: the offer alone put the EXIT cursor in the sky");
            // A6: the right-click on the flying ship: a move order to the spot under it
            var a = new ClickA6();
            var r6 = Fly(false, 180, true, 6.0, (c, rec, ph) =>
            {
                if (a.rec != rec) a = new ClickA6 { rec = rec };
                var s = c.s; var w = c.w;
                if (!a.clicked && ph == "air: cruise" && w.Time - rec.firstUp > ZWorld.ShuttleClimbSec + 1.5)
                {
                    a.clicked = true; a.gx0 = s.ShuttleGoalX; a.gy0 = s.ShuttleGoalY; a.sx = s.CenterX; a.sy = s.CenterY;
                    a.rcExit = RightClickExit(w, s);
                    if (!a.rcExit) OzV11Kit.PlayerMove(w, s, a.sx, a.sy);                               // the click goes on: PlayerInput.BuildWaypoint, a move to the ground under the pointer
                    a.log = $"clicked at {F(w.Time)} s over {a.sx},{a.sy} (its pad was {a.gx0},{a.gy0}): EXIT {a.rcExit}, EXIT on landing {s.ShuttleExitOnLanding}, aboard {s.Passengers.Count}; its goal now {s.ShuttleGoalX},{s.ShuttleGoalY}";
                }
                if (a.clicked && s.ShuttleState == ZShuttleState.Hover && !string.IsNullOrEmpty(s.ShuttleNoLand)) { a.hovered = true; rec.retry = true; return true; }   // the spot under it takes no landing: another flight
                if (a.clicked && !s.ShuttleAirborne && rec.td >= 0 && !a.down)
                {
                    a.down = true; rec.stopGround = true;
                    a.log += $"; down at {s.CenterX},{s.CenterY} ({F(Math.Sqrt((double)(s.CenterX - a.sx) * (s.CenterX - a.sx) + (double)(s.CenterY - a.sy) * (s.CenterY - a.sy)) / 8.0, "0.0")} m from the click), EXIT there {(HudExit(w, s) ? "taken" : "REFUSED")}";
                }
                if (a.down && s.Passengers.Count == 0) { a.log += $", the squad out +{F(w.Time - rec.td)} s"; return true; }
                return false;
            });
            L("A6 " + a.log);
            double far = Math.Sqrt((double)(a.gx0 - a.sx) * (a.gx0 - a.sx) + (double)(a.gy0 - a.sy) * (a.gy0 - a.sy)) / 8.0;
            V(r6.ok && a.rec == r6 && a.clicked && !a.rcExit && a.down && r6.c.s.Passengers.Count == 0 && far > 10,
              $"A6 the right-click on the flying ship ({F(far, "0")} m short of its pad): no EXIT, a move order to the spot under it; it came down there and EXIT on the ground let the squad out");
        }

        static void PartB()
        {
            sb.AppendLine("B. the ship with its pilot only (room for one squad), a red grunt squad of 4 selected, the pointer on the ship");
            foreach (bool rough in new[] { false, true })
            {
                string tag = rough ? "crash" : "normal";
                // B1 / B3 (the cursor): watched, no order
                var r = Fly(rough, rough ? 220 : 240, false, 4.0);
                if (!r.ok) V(false, $"B1 {tag} landing staged ({r.why})");
                else
                {
                    L($"{tag} landing, watched: the order at {F(r.orderAt)} s, up at {F(r.firstUp)} s, the touchdown at {F(r.td)} s, level +{F(r.level - r.td)} s");
                    sb.AppendLine(r.Table());
                    V(r.airTicks > 100 && r.airEnter == 0 && r.airClickEnter == 0 && r.airRulesBoard == 0,
                      $"B1 {tag}: up in the air ({r.airTicks} ticks) the ENTER cursor on {r.airEnter} ticks, the click on the ship ENTER on {r.airClickEnter} (a MOVE to the spot under it on all the others); the rules take a squad on {r.airRulesBoard}");
                    V(r.gndTicks >= 300 && r.gndEnter == r.gndTicks && r.gndClickEnter == r.gndTicks,
                      $"B3 {tag}: from the touchdown tick on the ENTER cursor on {r.gndEnter} of {r.gndTicks} ticks (4 s), the click ENTER on {r.gndClickEnter}{(rough ? " (crooked too: the ramp comes down for them once it stands level)" : "")}");
                }
                // B2 / B3 (the order): an ENTER order on the airborne ship, then the click's ENTER on the touchdown tick
                var st = new OrderB();
                var r2 = Fly(rough, rough ? 220 : 240, false, 95.0, (c, rec, ph) =>
                {
                    if (st.rec != rec) st = new OrderB { rec = rec };
                    var s = c.s; var w = c.w;
                    if (s.ShuttleAirborne && (ph == "air: take-off climb" || ph == "air: cruise" || ph == "air: descent") && !st.forced.ContainsKey(ph)
                        && (ph != "air: cruise" || w.Time - rec.firstUp > ZWorld.ShuttleClimbSec + 1.0))
                    {   // an ENTER order on the airborne ship (what an old click could have sent): the rules' order check
                        st.forced[ph] = true;
                        var u = c.sel; int pax = s.Passengers.Count; var click = ClickRobots(u, s);
                        OzV11Kit.PlayerEnter(w, u, s);
                        bool none = !u.Waypoints.Any(x => x.mode == WpMode.Enter && x.refId == s.Id) && s.Passengers.Count == pax;
                        if (none && click == WpMode.Move) st.ok++; else st.bad++;
                        L($"{tag} {ph}: the click on the ship gives {click}; an ENTER order sent anyway: {(none ? "refused (no ENTER waypoint)" : "TAKEN")}");
                    }
                    if (!s.ShuttleAirborne && rec.td >= 0 && !st.ordered)
                    {   // the touchdown tick: a squad near the pad, the click's order
                        st.ordered = true; rec.stopGround = true;
                        var (bx, by) = s.ShuttleBackPoint(8);
                        if (FreeSpot(w, bx, by, out int fx, out int fy, s, 90)) { st.sq = w.CreateRobotGroup("grunt", fx - 8, fy - 8, Me, SquadSize); c.sel = st.sq; }
                        var mode = st.sq != null ? ClickRobots(st.sq, s) : WpMode.Move;
                        if (mode == WpMode.Enter) OzV11Kit.PlayerEnter(w, st.sq, s);
                        st.taken = mode == WpMode.Enter && st.sq.Waypoints.Count > 0 && st.sq.Waypoints[0].mode == WpMode.Enter;
                        L($"{tag} the touchdown tick (+{F(w.Time - rec.td)} s, {(w.Time < s.ShuttleLevelledAt - 1e-9 ? "not level" : "level")}): the cursor '{CursorRobots(s)}', the click {mode}, the ENTER order taken {st.taken}");
                        return false;
                    }
                    if (st.ordered && st.boardedAt < 0 && s.Passengers.Count >= SquadSize) { st.boardedAt = w.Time - rec.td; return true; }
                    if (st.ordered && w.Time - rec.td > 90) return true;
                    return false;
                });
                if (!r2.ok || st.rec != r2) { V(false, $"B2/B3 {tag} landing staged ({r2.why})"); continue; }
                V(st.ok == 3 && st.bad == 0,
                  $"B2 {tag}: up in the air the click on the ship a MOVE, and an ENTER order sent anyway refused by the rules, in {st.ok} phases ({string.Join(", ", st.forced.Keys)}); taken in {st.bad}");
                V(st.taken && st.boardedAt >= 0,
                  $"B3 {tag}: the click's ENTER order on the touchdown tick taken {st.taken}; the squad of {SquadSize} aboard +{F(st.boardedAt)} s after the touchdown");
            }
        }

        static void Determinism()
        {
            string One()
            {
                var r = Fly(true, 120, true, 2.0);
                return r.ok ? $"touchdown {F(r.td, "0.000")} s, level {F(r.level, "0.000")} s, air {r.airTicks} ticks (the rules take EXIT {r.airRules}, offered {r.airOffered}), ground {r.gndTicks} (offered {r.gndOffered})" : "not staged: " + r.why;
            }
            string a = One(), b = One();
            V(a == b && !a.StartsWith("not"), $"D the same crash flight twice: {a} | {b}");
        }

        /// <summary>From the ramp starting down to the squad let out (s): the ramp's swing, or a later kit's own value when it lets the squad out
        /// while the ramp still comes down (a public static ZWorld.ShuttleExitAfterSec, read when present).</summary>
        static double ExitAfterSec()
        {
            var p = typeof(ZWorld).GetProperty("ShuttleExitAfterSec", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (p != null && p.PropertyType == typeof(double)) return (double)p.GetValue(null);
            return ZWorld.ShuttleRampOpenSec;
        }
    }
}
