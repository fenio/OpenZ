using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// moment. Counted headless on level 16 (ZWorld.Create + Tick, no scene, nothing saved; public members only). Each moment is staged
    /// from a fresh world, the ship is destroyed by the rules' own death (DamageHealth + UpdateObjectHealth) and the world runs on:
    ///  C1 during the take-off (lifting off, the legs folding): everyone aboard lost in the air, nobody made;
    ///  C2 during a normal landing (coming down high / low / just before the touchdown, at the touchdown): everyone aboard lost (in the air,
    ///     then on the ground);
    ///  C3 during a crash landing (the drop, the slam, resting tilted, righting itself; EXIT pending while it rights itself): everyone aboard lost;
    ///  C4 while a squad boards (walking to its back slots; at the ramp foot; some up the ramp, some in; all in, the ramp still down): the
    ///     robots drawn outside, on the ramp or in the doorway SURVIVE as a normal squad on the ground behind the wreck; only those past the
    ///     doorway are lost;
    ///  C5 while a squad unloads (the ramp coming down; all still seated; some on the ramp, some seated; all on the ramp or out; long out):
    ///     the robots still inside are lost, those in the doorway, on the ramp or out live;
    ///  C6 ALAN &amp; BRAD boarding / leaving a ship they took EMPTY (Brad its pilot): the same rule, the heroes as themselves;
    ///  C7 in full flight (the kickflip is drawn in the cruise; the rules fly on unchanged): everyone aboard lost in the air;
    ///  C8 at a repair station (the crane moving away while it turns over the deck, coming down onto the deck, on the deck being repaired,
    ///     lifting off with the crane coming back): everyone aboard lost, the crane called back, the bay free after the wreck's 4 s, a tank
    ///     repaired there after;
    ///  C9 lifting off while another ship waits for its pad (K338): the waiting ship stops waiting and lands;
    ///  S  the switch off (DV118 as it was: a boarding squad all lost, an unloading squad all alive);
    ///  D  the same moments twice: the same rows, places and hit points (deterministic).
    /// Every survivor: alive, its own team, no boarding state left (a saved boarder stands with no order, an exiter keeps its own and never an
    /// ENTER), a squad with one leader, at least 5 m from the
    /// wreck's middle on open ground, apart from the others, and it walks when ordered. Every lost robot is gone from the map; nobody keeps an
    /// ENTER order on the dead ship; the ship is gone a tick after.
    /// </summary>
    public static class OzShuttleDoorwayCount
    {
        const double Tick = ZConst.TICK;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public const int SquadSize = 4;
        /// <summary>A survivor stands at least this far from the wreck's middle (m): the hull's pathing radius is 4.4 m.</summary>
        public const double ClearM = 5.0;

        sealed class Ctx { public ZWorld w; public ZObject s, lead; public List<int> ids = new List<int>(); public string note = ""; }
        sealed class Res
        {
            public string what; public bool ok; public int aboard, lost, saved, made, doorLost, doorSaved; public bool air; public double heightM;
            public string text, digest;
        }

        static string settings, m16;
        static StringBuilder sb; static int pass, fail;
        static void V(bool ok, string what) { sb.AppendLine(OzV11Kit.Verdict(ok, what)); if (ok) pass++; else fail++; }
        static void L(string s) => sb.AppendLine("      " + s);
        static string F(double v, string f = "0.00") => v.ToString(f, Inv);
        static bool Alive(ZObject o) => OzV11Kit.Alive(o);
        static double DistM(int ax, int ay, int bx, int by) => Math.Sqrt((double)(ax - bx) * (ax - bx) + (double)(ay - by) * (ay - by)) / 8.0;

        static void Run(ZWorld w, double secs, Func<bool> until = null)
        {
            double end = w.Time + secs;
            while (w.Time < end - 1e-9) { w.Tick(Tick); w.Events.Clear(); if (until != null && until()) return; }
        }
        static void RunTo(ZWorld w, double t) { while (w.Time < t - 1e-9) { w.Tick(Tick); w.Events.Clear(); } }

        // ------------------------------------------------------------------ worlds
        static ZWorld W16(int seed, bool heroes = false)
        {
            ZWorld.ShuttlePlayableDeviation = true; ZWorld.ShuttleOn = true; ZWorld.ShuttleTeam = Team.Red; ZWorld.ShuttleRoughProofNext = false;
            bool was = ZWorld.HeroSquadDeviation; if (heroes) ZWorld.HeroSquadDeviation = true;
            var w = OzV11Kit.World(settings, m16, seed);
            ZWorld.HeroSquadDeviation = was;
            return w;
        }

        static void ClearAround(ZWorld w, ZObject s, int r, ZObject keep = null)
        {
            foreach (var o in w.Objects.ToList())
                if ((o.IsRobot || (o.Kind == ZKind.Vehicle && !o.IsShuttle)) && Alive(o) && OzV11Kit.Dist(o, s) < r && (keep == null || !OzV11Kit.Squad(keep).Contains(o))) o.KillAt = w.Time;
            Run(w, 0.2);
        }

        static bool FreeSpot(ZWorld w, int x, int y, out int fx, out int fy, ZObject keepFrom = null, double minFrom = 0)
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

        /// <summary>Level 16's own ship (its pilot aboard), the ground round it cleared, a red squad of SquadSize behind it.</summary>
        static Ctx Ship(int seed, bool squadOut)
        {
            var c = new Ctx { w = W16(seed) };
            c.s = c.w.PlayableShuttle;
            ClearAround(c.w, c.s, 260);
            var (bx, by) = c.s.ShuttleBackPoint(5);
            FreeSpot(c.w, bx, by, out int fx, out int fy, c.s, 70);
            c.lead = c.w.CreateRobotGroup("grunt", fx - 8, fy - 8, Team.Red, SquadSize);
            foreach (var m in OzV11Kit.Squad(c.lead)) c.ids.Add(m.Id);
            if (!squadOut) { OzV11Kit.PlayerEnter(c.w, c.lead, c.s); Run(c.w, 60, () => c.s.Passengers.Count > 0); RampShut(c.w, c.s); }
            return c;
        }

        static void RampShut(ZWorld w, ZObject s) => Run(w, 20, () => s.ShuttleRampPhase == ZShuttleRamp.Shut && !s.ShuttleExitPending && w.Time >= s.ShuttleRampHoldUntil);

        // ------------------------------------------------------------------ the kill and what is left
        static Res Kill(Ctx c, string what, Func<Res, bool> expect)
        {
            var w = c.w; var s = c.s;
            var r = new Res { what = what, aboard = s.Passengers.Count, air = s.ShuttleAirborne, heightM = s.ShuttleHeightM };
            var before = new HashSet<ZObject>(w.Objects);
            int seq0 = w.ShuttleLostSeq, dseq0 = w.ShuttleDoorwaySeq;
            int sid = s.Id; double tk = w.Time; int sx = s.CenterX, sy = s.CenterY;
            string state = $"{s.ShuttleState} lift {F(s.ShuttleLift)} ramp {s.ShuttleRampPhase} {F(s.ShuttleRamp01)}";
            s.DamageHealth(s.Health); w.UpdateObjectHealth(s);
            var lostRows = w.ShuttleLostCrew.Where(x => x.shipId == sid && x.seq > seq0).ToList();
            var doorRows = w.ShuttleDoorwayRows.Where(x => x.shipId == sid && x.seq > dseq0).ToList();
            Run(w, 1.0);
            r.lost = lostRows.Count; r.doorLost = doorRows.Count(x => !x.saved); r.doorSaved = doorRows.Count(x => x.saved);
            var made = w.Objects.Where(o => o.IsRobot && !before.Contains(o) && Alive(o)).ToList();
            r.made = made.Count;
            var savedIds = doorRows.Where(x => x.saved).Select(x => x.robotId).ToList();
            var saved = savedIds.Select(id => w.Get(id)).ToList();
            r.saved = saved.Count(Alive);
            bool shipGone = w.Get(sid) == null;
            bool noEnter = !w.Objects.Any(o => o.IsRobot && Alive(o) && o.Waypoints.Count > 0 && o.Waypoints[0].mode == WpMode.Enter && o.Waypoints[0].refId == sid);
            bool lostGone = doorRows.Where(x => !x.saved).All(x => w.Get(x.robotId) == null);
            bool airRows = lostRows.All(x => x.airborne == r.air && Math.Abs(x.heightM - r.heightM) < 1e-6);
            // the survivors: alive, red, standing with no order, a squad with one leader each, clear of the wreck, apart, on open ground
            var probs = new List<string>();
            foreach (var o in saved)
            {
                if (!Alive(o)) { probs.Add("#" + (o?.Id ?? -1) + " not alive"); continue; }
                if (o.Owner != Team.Red) probs.Add("#" + o.Id + " team " + o.Owner);
                bool boarder = doorRows.Any(x => x.saved && x.boarding && x.robotId == o.Id);
                if (o.Waypoints.Count > 0 && (boarder || o.Waypoints[0].mode == WpMode.Enter)) probs.Add("#" + o.Id + " has an order " + o.Waypoints[0].mode);   // a saved boarder stands with no order; an exiter keeps what it was doing, never an ENTER
                if (o.Leader != null && !Alive(o.Leader)) probs.Add("#" + o.Id + " leader dead");
                double d = DistM(o.CenterX, o.CenterY, sx, sy); if (d < ClearM) probs.Add("#" + o.Id + " " + F(d, "0.0") + " m from the wreck");
                int tx = o.CenterX >> 4, ty = o.CenterY >> 4; if (!w.Path.TilePassable(tx, ty, true)) probs.Add("#" + o.Id + " on a closed tile");
                foreach (var q in saved) if (q != o && Alive(q) && DistM(o.CenterX, o.CenterY, q.CenterX, q.CenterY) < 0.9) { probs.Add("#" + o.Id + " on top of #" + q.Id); break; }
            }
            int leaders = saved.Count(o => Alive(o) && !o.IsMinion);
            // they take an order: the leader(s) sent 7 m on; each squad walks
            double moved = 99;
            if (r.saved > 0)
            {
                var start = saved.Where(Alive).ToDictionary(o => o, o => (o.CenterX, o.CenterY));
                foreach (var o in saved.Where(o => Alive(o) && !o.IsMinion))
                {
                    FreeSpot(w, o.CenterX + 56, o.CenterY + 24, out int gx, out int gy);
                    OzV11Kit.PlayerMove(w, o, gx, gy, false);
                }
                Run(w, 5.0);
                moved = start.Where(kv => Alive(kv.Key)).Select(kv => DistM(kv.Key.CenterX, kv.Key.CenterY, kv.Value.Item1, kv.Value.Item2)).DefaultIfEmpty(0).Min();
            }
            int madeWant = doorRows.Count(x => x.saved && x.boarding);
            bool core = shipGone && noEnter && lostGone && airRows && probs.Count == 0 && (r.saved == 0 || (leaders >= 1 && moved >= 2.0)) && r.made == madeWant && r.saved == r.doorSaved;
            r.ok = core && expect(r);
            r.text = $"{what}: at {F(tk)} s ({state}, {(r.air ? "in the air " + F(r.heightM, "0.0") + " m" : "on the ground")}): aboard {r.aboard}; lost {r.lost} (rows), the file's lost {r.doorLost}, saved {r.saved} of {r.doorSaved} (made {r.made}, leaders {leaders}, the least walked after the order {F(moved, "0.0")} m); " +
                     $"ship gone {shipGone}, no ENTER order left {noEnter}, lost robots off the map {lostGone}, rows' height right {airRows}{(probs.Count > 0 ? "; PROBLEMS " + string.Join(", ", probs) : "")}{c.note}";
            r.digest = $"{what}|{F(tk, "0.000")}|{r.aboard}|{r.lost}|{r.doorLost}|{r.doorSaved}|" + string.Join(";", doorRows.Select(x => $"{x.index}{(x.boarding ? "b" : "e")}{(x.saved ? "s" : "l")}{x.x},{x.y}")) + "|" +
                       string.Join(";", lostRows.Select(x => $"{x.type}{x.member}{x.hp}")) + "|" + string.Join(";", saved.Where(Alive).Select(o => $"{o.Type}{o.HeroMemberName}{o.Health}@{o.CenterX},{o.CenterY}"));
            return r;
        }

        static void Line(Res r, string verdict) { L(r.text); V(r.ok, verdict); }

        // ------------------------------------------------------------------ C1: the take-off
        static void C1()
        {
            sb.AppendLine("C1. destroyed during the take-off (lifting off, the legs folding)");
            foreach (double lift in new[] { 0.08, 0.5, 0.92 })
            {
                var c = Ship(1, false);
                PadSpot(c.w, c.s, 480, 900, out int gx, out int gy);
                OzV11Kit.PlayerMove(c.w, c.s, gx, gy);
                Run(c.w, 20, () => c.s.ShuttleState == ZShuttleState.Climb && c.s.ShuttleLift >= lift);
                var r = Kill(c, "C1 lift " + F(lift), x => x.air && x.lost == SquadSize && x.aboard == SquadSize && x.made == 0 && x.doorSaved + x.doorLost == 0);
                Line(r, $"C1 take-off at lift {F(lift)}: everyone aboard lost in the air ({SquadSize} rows at the ship's height), nobody made, nobody left in a boarding state");
                Digest.Add(r.digest);
            }
        }

        // ------------------------------------------------------------------ C2 / C3: the landings
        /// <summary>A loaded ship flown to a pad; `rough` = the next landing is a crash landing (ZWorld.ShuttleRoughProofNext); returns false
        /// when no pad gave the wanted landing.</summary>
        static Ctx Landing(bool rough, out bool okLanding)
        {
            okLanding = false;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var c = Ship(2 + attempt, false);
                if (!PadSpot(c.w, c.s, 400 + attempt * 64, 1000, out int gx, out int gy)) continue;
                ZWorld.ShuttleRoughProofNext = rough;
                OzV11Kit.PlayerMove(c.w, c.s, gx, gy);
                Run(c.w, 90, () => c.s.ShuttleState == ZShuttleState.Descend || c.s.ShuttleState == ZShuttleState.Hover);
                if (c.s.ShuttleState != ZShuttleState.Descend) continue;
                if (c.s.ShuttleRoughLanding != rough) continue;
                okLanding = true; return c;
            }
            return null;
        }

        static void C2()
        {
            sb.AppendLine("C2. destroyed during a normal landing (coming down, the legs unfolding, just before / at the touchdown)");
            foreach (double lift in new[] { 0.7, 0.3, 0.03, 0.0 })
            {
                var c = Landing(false, out bool ok);
                if (!ok) { V(false, "C2 a normal landing staged"); return; }
                if (lift > 0) Run(c.w, 10, () => c.s.ShuttleLift <= lift);
                else Run(c.w, 10, () => c.s.ShuttleState == ZShuttleState.Landed);
                bool wantAir = lift > 0;
                var r = Kill(c, "C2 lift " + F(lift), x => x.air == wantAir && x.lost == SquadSize && x.made == 0 && x.doorSaved + x.doorLost == 0);
                Line(r, lift > 0 ? $"C2 landing at lift {F(lift)}: everyone aboard lost in the air, nobody made" : "C2 at the touchdown: everyone aboard lost on the ground (the ramp still shut), nobody made");
                Digest.Add(r.digest);
            }
        }

        static void C3()
        {
            sb.AppendLine("C3. destroyed during a crash landing (the drop, the slam, resting tilted, righting itself)");
            foreach (var when in new[] { "drop", "slam", "rest", "righting", "exit waiting" })
            {
                var c = Landing(true, out bool ok);
                if (!ok) { V(false, "C3 a crash landing staged"); return; }
                var s = c.s; var w = c.w;
                if (when == "drop") Run(w, 10, () => s.ShuttleLift <= 0.1);
                else
                {
                    Run(w, 10, () => s.ShuttleState == ZShuttleState.Landed);
                    double td = s.ShuttleTouchdownAt;
                    if (when == "slam") RunTo(w, td + 0.06);
                    else if (when == "rest") RunTo(w, td + ZWorld.ShuttleRoughCrashSec + ZWorld.ShuttleRoughRestSec * 0.5);
                    else if (when == "righting") RunTo(w, s.ShuttleLevelStartAt + ZWorld.ShuttleRoughLevelSec * 0.5);
                    else { RunTo(w, td + 0.2); bool taken = w.Eject(Team.Red, s); c.note = $"; EXIT given while tilted: {(taken ? "taken (pending)" : "refused")}"; RunTo(w, s.ShuttleLevelStartAt + 0.1); }
                }
                bool wantAir = when == "drop";
                var r = Kill(c, "C3 " + when, x => x.air == wantAir && x.lost == SquadSize && x.made == 0 && x.doorSaved + x.doorLost == 0);
                Line(r, $"C3 crash landing, {when}: everyone aboard lost, nobody made, nobody out");
                Digest.Add(r.digest);
            }
        }

        // ------------------------------------------------------------------ C4: boarding
        static void C4()
        {
            sb.AppendLine("C4. destroyed while a squad boards (walking to the ship; at the ramp foot; some up the ramp; all in, the ramp still down)");
            // (a) walking to its back slots: nobody aboard yet
            {
                var c = Ship(3, true);
                OzV11Kit.PlayerEnter(c.w, c.lead, c.s);
                var foot0 = c.s.ShuttleBackPoint(0);
                Run(c.w, 20, () => c.s.Passengers.Count > 0 || DistM(c.lead.CenterX, c.lead.CenterY, foot0.x, foot0.y) < 4.0);
                bool before = c.s.Passengers.Count == 0;
                var alive0 = c.ids.Select(id => c.w.Get(id)).ToList();
                var r = Kill(c, "C4a walking", x => x.aboard == 0 && x.lost == 0 && x.made == 0);
                bool free = alive0.All(o => Alive(o) && !(o.Waypoints.Count > 0 && o.Waypoints[0].mode == WpMode.Enter));
                double moved = 0;
                if (free) { var p0 = alive0.ToDictionary(o => o, o => (o.CenterX, o.CenterY)); FreeSpot(c.w, c.lead.CenterX - 64, c.lead.CenterY, out int gx, out int gy); OzV11Kit.PlayerMove(c.w, c.lead, gx, gy, false); Run(c.w, 5); moved = p0.Select(kv => DistM(kv.Key.CenterX, kv.Key.CenterY, kv.Value.Item1, kv.Value.Item2)).Min(); }
                r.text += $"; the squad (still outside): alive and free {free}, the least walked after an order {F(moved, "0.0")} m";
                r.ok &= before && free && moved >= 2.0;
                Line(r, "C4 walking to the ship (nobody aboard yet): nobody lost, the squad stays a normal free squad (no ENTER order left on the wreck, it walks when ordered)");
                Digest.Add(r.digest);
            }
            foreach (var when in new[] { "foot", "half", "all in" })
            {
                var c = Ship(3, true);
                OzV11Kit.PlayerEnter(c.w, c.lead, c.s);
                Run(c.w, 60, () => c.s.Passengers.Count > 0);
                var ft = c.s.ShuttleFootTimes.ToList(); double t0 = c.w.Time; int n = ft.Count;
                double tk = when == "foot" ? t0 + 0.1 : when == "half" ? ft[1] + ZWorld.ShuttleBoardDoorSec + 0.015 : ft[n - 1] + ZWorld.ShuttleBoardDoorSec + 0.015;
                bool rampDown = tk < c.s.ShuttleRampHoldUntil;
                RunTo(c.w, tk);
                int expInside = ft.Count(f => c.w.Time >= f + ZWorld.ShuttleBoardDoorSec - 1e-9);
                int want = when == "foot" ? 0 : when == "half" ? 2 : n;
                c.note = $"; feet at {string.Join(" ", ft.Select(f => F(f - t0)))} s after the boarding, the doorway {F(ZWorld.ShuttleBoardDoorSec, "0.0")} s after each; ramp down at the kill {rampDown}";
                var r = Kill(c, "C4 " + when, x => x.aboard == n && x.lost == want && x.saved == n - want && expInside == want && rampDown);
                string v = when == "foot" ? "C4 at the ramp foot (the file walking in, nobody past the doorway): ALL saved as one free squad behind the wreck, nobody lost"
                         : when == "half" ? "C4 half up the ramp (two past the doorway, two on the ramp / walking in): the two inside lost, the two drawn outside saved"
                         : "C4 all past the doorway (the ramp still down): all lost, nobody saved";
                Line(r, v);
                Digest.Add(r.digest);
            }
        }

        // ------------------------------------------------------------------ C5: unloading
        static void C5()
        {
            sb.AppendLine("C5. destroyed while a squad unloads (the ramp coming down; all seated; some on the ramp; all out of the bay; long out)");
            foreach (var when in new[] { "ramp opening", "all seated", "half", "all out of the bay", "long out" })
            {
                var c = Ship(4, false);
                var w = c.w; var s = c.s;
                w.Eject(Team.Red, s);
                if (when == "ramp opening") { Run(w, 5, () => s.ShuttleRampPhase == ZShuttleRamp.Opening && s.ShuttleRamp01 > 0.5 * ZWorld.ShuttleExitRamp01)   /* K422: before the squad is let out while it comes down */; var r0 = Kill(c, "C5 " + when, x => x.aboard == SquadSize && x.lost == SquadSize && x.made == 0 && x.doorLost + x.doorSaved == 0); Line(r0, "C5 the ramp coming down (EXIT pending): everyone aboard lost, nobody made"); Digest.Add(r0.digest); continue; }
                Run(w, 10, () => s.Passengers.Count == 0 && s.ShuttleLastRampUse == ZShuttleRampUse.Unloaded);
                double te = w.Time; var ft = s.ShuttleFootTimes.ToList(); int n = ft.Count;
                double seat(int k) => ft[k] - ZWorld.ShuttleRampOutSec;
                double tk = when == "all seated" ? te + 0.05 : when == "half" ? seat(1) + ZWorld.ShuttleExitDoorSec + 0.015 : when == "all out of the bay" ? seat(n - 1) + ZWorld.ShuttleExitDoorSec + 0.015 : s.ShuttleRampHoldUntil + 1.0;
                RunTo(w, tk);
                int expInside = ft.Count(f => ZWorld.ShuttleExiterInside(w.Time, f));
                int want = when == "all seated" ? n : when == "half" ? n - 2 : 0;
                c.note = $"; seats left at {string.Join(" ", ft.Select(f => F(f - ZWorld.ShuttleRampOutSec - te)))} s after the exit, the doorway {F(ZWorld.ShuttleExitDoorSec, "0.0")} s after each";
                var r = Kill(c, "C5 " + when, x => x.aboard == 0 && x.lost == want && x.doorLost == want && expInside == want && x.made == 0 && (when == "long out" ? x.doorSaved == 0 : x.saved == n - want));
                string v = when == "all seated" ? "C5 the squad let out but all still seated: all lost with the ship (their robots off the map), nobody walks out of the wreck"
                         : when == "half" ? "C5 two in the doorway / on the ramp, two still inside: the two inside lost, the two outside live (a squad with its leader)"
                         : when == "all out of the bay" ? "C5 all in the doorway, on the ramp or out: all live, nobody lost"
                         : "C5 long out: nobody lost, nothing listed";
                Line(r, v);
                Digest.Add(r.digest);
            }
            {   // the same blast (or a shot of the same tick) also reaches a lost exiter still on its back slot until the next tick: no second death
                var c = Ship(4, false); var w = c.w; var s = c.s;
                w.Eject(Team.Red, s);
                Run(w, 10, () => s.Passengers.Count == 0 && s.ShuttleLastRampUse == ZShuttleRampUse.Unloaded);
                RunTo(w, w.Time + 0.05);
                var ids = s.ShuttleFootIds.ToList(); int dseq0 = w.ShuttleDoorwaySeq; int sid = s.Id;
                w.Events.Clear();
                s.DamageHealth(s.Health); w.UpdateObjectHealth(s);
                var lostIds = w.ShuttleDoorwayRows.Where(x => x.shipId == sid && x.seq > dseq0 && !x.saved).Select(x => x.robotId).ToList();
                foreach (var id in lostIds) { var o = w.Get(id); if (o != null) { o.DamageHealth(o.Health); w.UpdateObjectHealth(o); } }
                int deaths = w.Events.Count(e => e.type == ZEventType.Death && lostIds.Contains(e.objId));
                w.Events.Clear(); Run(w, 0.5);
                bool gone = lostIds.All(id => w.Get(id) == null);
                L($"C5 all seated, then the lost exiters hit to 0 HP in the same tick: lost {lostIds.Count} of {ids.Count}, Death events for them {deaths}, off the map a tick later {gone}");
                Digest.Add($"C5 second death {lostIds.Count} {deaths} {gone}");
                V(lostIds.Count == ids.Count && ids.Count > 0 && deaths == 0 && gone, "C5 a lost exiter hit again in the tick it was lost: no second death (no Death event, no kill credit, no second body)");
            }
        }

        // ------------------------------------------------------------------ C6: ALAN & BRAD and the ship they took empty
        static ZObject Drop(ZWorld w, int cx, int cy, int minR, int maxR)
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

        static Ctx Heroes(int seed)
        {
            var c = new Ctx { w = W16(seed, true) };
            c.lead = c.w.Objects.FirstOrDefault(o => o.IsHero && o.HeroMemberName == "brad" && Alive(o) && o.Owner == Team.Red);
            if (c.lead == null) return null;
            c.s = Drop(c.w, c.lead.CenterX, c.lead.CenterY, 200, 700);
            if (c.s == null) return null;
            foreach (var m in OzV11Kit.Squad(c.lead)) c.ids.Add(m.Id);
            return c;
        }

        static void C6()
        {
            sb.AppendLine("C6. ALAN & BRAD boarding / leaving a ship they took EMPTY (Brad its pilot, Alan the passenger)");
            foreach (var when in new[] { "boarding foot", "boarding half", "exit seated", "exit half" })
            {
                var c = Heroes(11);
                if (c == null) { V(false, "C6 the hero squad and an empty drop on level 16"); return; }
                var w = c.w; var s = c.s;
                OzV11Kit.PlayerEnter(w, c.lead, s);
                Run(w, 60, () => s.Drivers.Count > 0);
                var ft = s.ShuttleFootTimes.ToList(); double t0 = w.Time; int n = ft.Count;
                int want; double tk;
                if (when.StartsWith("boarding"))
                {
                    tk = when == "boarding foot" ? t0 + 0.1 : ft[0] + ZWorld.ShuttleBoardDoorSec + 0.015;
                    want = when == "boarding foot" ? 0 : ft.Count(f => tk >= f + ZWorld.ShuttleBoardDoorSec);
                    RunTo(w, tk);
                }
                else
                {
                    RampShut(w, s);
                    w.Eject(Team.Red, s);
                    Run(w, 10, () => s.Drivers.Count == 0 && s.ShuttleLastRampUse == ZShuttleRampUse.Unloaded);
                    ft = s.ShuttleFootTimes.ToList(); n = ft.Count; double te = w.Time;
                    tk = when == "exit seated" ? te + 0.05 : ft[0] - ZWorld.ShuttleRampOutSec + ZWorld.ShuttleExitDoorSec + 0.015;
                    RunTo(w, tk);
                    want = ft.Count(f => ZWorld.ShuttleExiterInside(w.Time, f));
                }
                int heroes0 = w.Objects.Count(o => o.IsHero && Alive(o));
                var r = Kill(c, "C6 " + when, x => x.lost == want && x.made == (when.StartsWith("boarding") ? n - want : 0) && (when.StartsWith("exit") ? x.doorLost == want : true));
                var heroes = w.Objects.Where(o => o.IsHero && Alive(o) && o.Owner == Team.Red).ToList();
                var rows = w.ShuttleLostCrew.Where(x => x.shipId == s.Id).ToList();
                bool names = rows.All(x => x.member == "alan" || x.member == "brad") && heroes.All(o => o.HeroMemberName == "alan" || o.HeroMemberName == "brad");
                bool bradLeads = !heroes.Any(o => o.HeroMemberName == "brad") || heroes.Any(o => o.HeroMemberName == "brad" && !o.IsMinion);
                bool count = heroes.Count + rows.Count == 2;
                r.text += $"; heroes alive after {heroes.Count} ({string.Join(", ", heroes.Select(o => o.HeroMemberName + (o.IsMinion ? "" : " (leads)") + " " + o.Health + " HP"))}), rows {string.Join(", ", rows.Select(x => x.member))}; feet {string.Join(" ", ft.Select(f => F(f - t0)))}";
                r.ok &= names && bradLeads && count;
                string v = when == "boarding foot" ? "C6 the heroes at the ramp foot: both saved as themselves (Brad leads), nobody lost"
                         : when == "boarding half" ? "C6 Brad past the doorway, Alan still outside: Brad lost (his row), Alan saved as himself"
                         : when == "exit seated" ? "C6 let out of the emptied ship but still seated: both lost (their rows: alan, brad), nobody walks out"
                         : "C6 Brad in the doorway, Alan still inside: Brad lives, Alan lost";
                Line(r, v);
                Digest.Add(r.digest);
            }
        }

        // ------------------------------------------------------------------ C7: in full flight
        static void C7()
        {
            sb.AppendLine("C7. destroyed in full flight (the kickflip is drawn in the cruise; the rules fly on unchanged)");
            var c = Ship(5, false);
            PadSpot(c.w, c.s, 700, 1200, out int gx, out int gy);
            OzV11Kit.PlayerMove(c.w, c.s, gx, gy);
            Run(c.w, 30, () => c.s.ShuttleState == ZShuttleState.Cruise);
            Run(c.w, 2.0, () => c.s.ShuttleState != ZShuttleState.Cruise);
            var r = Kill(c, "C7 cruise", x => x.air && x.lost == SquadSize && x.made == 0 && x.doorLost + x.doorSaved == 0);
            Line(r, "C7 in full flight: everyone aboard lost in the air at the cruise height, nobody made");
            Digest.Add(r.digest);
        }

        // ------------------------------------------------------------------ C8: the repair station
        static void C8()
        {
            sb.AppendLine("C8. destroyed at a repair station (the crane moving away, coming down onto the deck, on the deck, lifting off with the crane coming back)");
            foreach (var when in new[] { "crane away", "onto the deck", "on the deck", "lifting off" })
            {
                var c = Ship(6, false);
                var w = c.w; var s = c.s;
                ZObject st = null;
                foreach (var f in w.Flags) foreach (var b in f.LinkedObjects) if (b.Type == "repair" && st == null) { st = b; if (b.Owner != Team.Red) w.AwardZone(f, Team.Red); }
                if (st == null) { V(false, "C8 a repair station on level 16"); return; }
                s.SetHealth(s.MaxHealth / 2);
                w.SendWaypoints(Team.Red, s, new[] { new ZWaypoint { mode = WpMode.UnitRepair, refId = st.Id, x = st.CenterX, y = st.CenterY, playerGiven = true } });
                bool staged;
                if (when == "crane away") staged = RunUntil(w, 90, () => s.ShuttleDeckTurning && s.ShuttleCraneCallAt >= 0 && w.Time < s.ShuttleCraneClearAt - 0.3);
                else if (when == "onto the deck") staged = RunUntil(w, 90, () => s.ShuttleState == ZShuttleState.Descend && s.ShuttleLift < 0.5);
                else if (when == "on the deck") staged = RunUntil(w, 90, () => s.ShuttleOnRepairPad && s.Health > s.MaxHealth * 0.6);
                else staged = RunUntil(w, 120, () => s.ShuttleState == ZShuttleState.Climb && s.ShuttleLift > 0.3);
                int crane0 = s.ShuttleCraneStation;
                var r = Kill(c, "C8 " + when, x => x.lost == SquadSize && x.made == 0 && x.doorLost + x.doorSaved == 0);
                bool callGone = s.ShuttleCraneStation < 0 && s.ShuttleCraneCallAt < 0;
                Run(w, ZWorld.ShuttleDeckWreckSec + 0.5);
                bool bayFree = st.RepairOccupantId < 0 && !st.ShuttleWreckOnDeck && !w.ShuttleCraneAway(st.Id);
                // a light tank repaired there after
                FreeSpot(w, st.CenterX + 120, st.CenterY + 60, out int vx, out int vy, st, 90);
                var tank = w.CreateObject("vehicle", "light", vx - 16, vy - 16, Team.Red); tank.SetHealth(tank.MaxHealth / 2);
                w.SendWaypoints(Team.Red, tank, new[] { new ZWaypoint { mode = WpMode.UnitRepair, refId = st.Id, x = st.CenterX, y = st.CenterY, playerGiven = true } });
                bool tankIn = RunUntil(w, 60, () => tank.OnRepairPad(st.Id));
                r.text += $"; staged {staged}, the crane's call before {crane0}, after: called back {callGone}; {F(ZWorld.ShuttleDeckWreckSec + 0.5, "0.0")} s on the bay free {bayFree}; a light tank on the pad after {tankIn}";
                r.ok &= staged && callGone && bayFree && tankIn;
                Line(r, $"C8 {when}: everyone aboard lost, the crane called back, the bay free after the wreck's {F(ZWorld.ShuttleDeckWreckSec, "0")} s, the next vehicle repaired there");
                Digest.Add(r.digest);
            }
        }

        static bool RunUntil(ZWorld w, double secs, Func<bool> until) { Run(w, secs, until); return until(); }

        // ------------------------------------------------------------------ C9: another ship waiting for this one (K338's air traffic)
        static void C9()
        {
            sb.AppendLine("C9. destroyed while another ship waits for it (K338: a ship sent onto the pad it is lifting off from holds until it has gone)");
            var c = Ship(7, false); var w = c.w; var a = c.s;
            ZObject b = null;
            for (int r = 300; r <= 900 && b == null; r += 48)
                for (int k = 0; k < 16 && b == null; k++)
                {
                    double g = k * Math.PI / 8;
                    b = w.SpawnShuttle(a.CenterX + (int)(Math.Cos(g) * r), a.CenterY + (int)(Math.Sin(g) * r), Team.Red, -90f, out _);
                }
            if (b == null || !PadSpot(w, a, 500, 1000, out int gx, out int gy)) { V(false, "C9 a second ship and a pad"); return; }
            int ax = a.CenterX, ay = a.CenterY;
            OzV11Kit.PlayerMove(w, a, gx, gy);
            OzV11Kit.PlayerMove(w, b, ax, ay);
            bool held = RunUntil(w, 60, () => a.ShuttleAirborne && a.ShuttleLift >= 0.3 && (b.ShuttleHolding || OzV11Kit.Dist(a, b) < 160));
            var r9 = Kill(c, "C9 lifting off, the other ship waiting", x => x.air && x.lost == SquadSize && x.made == 0);
            bool landed = RunUntil(w, 60, () => b.ShuttleState == ZShuttleState.Landed && b.ShuttleLift <= 0 && b.ShuttleLandings > 0);
            double off = DistM(b.CenterX, b.CenterY, ax, ay);
            r9.text += $"; the other ship near or holding at the kill {held}; then it landed {landed} ({b.ShuttleState}, {F(off, "0.0")} m from the pad it was sent to, holds counted {ZWorld.ShuttleHolds})";
            r9.ok &= held && landed && off <= 12.0;
            Line(r9, "C9 lifting off with another ship waiting for its pad: everyone aboard lost; the waiting ship stops waiting and lands (never held by a wreck)");
            Digest.Add(r9.digest);
        }

        // ------------------------------------------------------------------ S: the switch off
        static void S()
        {
            sb.AppendLine("S. the switch off (ZWorld.ShuttleDoorwayDeviation = false: DV118 as it was)");
            bool was = ZWorld.ShuttleDoorwayDeviation;
            try
            {
                ZWorld.ShuttleDoorwayDeviation = false;
                var c = Ship(3, true);
                OzV11Kit.PlayerEnter(c.w, c.lead, c.s);
                Run(c.w, 60, () => c.s.Passengers.Count > 0);
                RunTo(c.w, c.w.Time + 0.1);
                var r = Kill(c, "S boarding", x => x.lost == SquadSize && x.made == 0 && x.doorLost + x.doorSaved == 0);
                Line(r, "S boarding at the ramp foot with the switch off: the whole squad lost as before");
                var c2 = Ship(4, false);
                c2.w.Eject(Team.Red, c2.s);
                Run(c2.w, 10, () => c2.s.Passengers.Count == 0 && c2.s.ShuttleLastRampUse == ZShuttleRampUse.Unloaded);
                var outs = c2.s.ShuttleFootIds.ToList();
                RunTo(c2.w, c2.w.Time + 0.05);
                var r2 = Kill(c2, "S unloading", x => x.lost == 0 && x.doorLost + x.doorSaved == 0);
                int alive = outs.Count(id => Alive(c2.w.Get(id)));
                r2.text += $"; robots let out alive {alive} of {outs.Count}";
                r2.ok &= alive == outs.Count;
                Line(r2, "S unloading, all still seated, with the switch off: every robot let out lives as before");
            }
            finally { ZWorld.ShuttleDoorwayDeviation = was; }
        }

        static readonly List<string> Digest = new List<string>();

        /// <summary>The whole check: C1-C8, S, then D (C4, C5 and C6 again: the same digest).</summary>
        public static string Check(string settingsText, string m16Text)
        {
            settings = settingsText; m16 = m16Text;
            sb = new StringBuilder(); pass = fail = 0; Digest.Clear();
            bool was = ZWorld.ShuttlePlayableDeviation, wasOn = ZWorld.ShuttleOn, heroWas = ZWorld.HeroSquadDeviation, doorWas = ZWorld.ShuttleDoorwayDeviation; var team0 = ZWorld.ShuttleTeam;
            try
            {
                ZWorld.ShuttleDoorwayDeviation = true;
                sb.AppendLine($"K418 / DV126: the shuttle destroyed at every in-between moment (level 16, squads of {SquadSize}; the doorway {F(ZWorld.ShuttleBoardDoorSec, "0.0")} s after the ramp foot walking in, {F(ZWorld.ShuttleExitDoorSec, "0.0")} s after the seat walking out)");
                C1(); C2(); C3(); C4(); C5(); C6(); C7(); C8(); C9(); S();
                var first = string.Join("\n", Digest);
                int nFirst = Digest.Count;
                Digest.Clear(); var keepSb = sb; sb = new StringBuilder(); int p0 = pass, f0 = fail;
                C4(); C5(); C6();
                var again = string.Join("\n", Digest);
                sb = keepSb; pass = p0; fail = f0;
                var firstPart = first.Split('\n').Where(l => l.StartsWith("C4") || l.StartsWith("C5") || l.StartsWith("C6")).ToList();
                bool same = string.Join("\n", firstPart) == again;
                sb.AppendLine($"D. C4, C5 and C6 run again from fresh worlds: {firstPart.Count} digests (rows, places, hit points), the same {same}; all digests {nFirst}, hash {Hash(first)}");
                V(same && firstPart.Count > 0, "D the same moments twice: the same rows, survivors, places and hit points (deterministic, a replay plays the same)");
            }
            catch (Exception ex) { sb.AppendLine("FAIL  the check threw " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace); fail++; }
            finally { ZWorld.ShuttlePlayableDeviation = was; ZWorld.ShuttleOn = wasOn; ZWorld.HeroSquadDeviation = heroWas; ZWorld.ShuttleDoorwayDeviation = doorWas; ZWorld.ShuttleTeam = team0; ZWorld.ShuttleRoughProofNext = false; }
            sb.AppendLine($"K418: {pass} PASS, {fail} FAIL");
            return sb.ToString();
        }

        static string Hash(string s) { unchecked { uint h = 2166136261; foreach (char ch in s) { h ^= ch; h *= 16777619; } return h.ToString("x8"); } }

    }
}
