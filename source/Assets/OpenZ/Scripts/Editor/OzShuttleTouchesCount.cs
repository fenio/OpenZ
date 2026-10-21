using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K256g, the shuttle's orders only to places where it can land (the option SHUTTLE ORDERS = LANDABLE SPOTS ONLY; Sim/ZWorld.ShuttleOrders.cs),
    /// counted headless (ZWorld.Create + Tick, no scene, nothing saved; public members only). Menu OpenZ/V1.1/Shuttle: K256g Shuttle Orders Check.
    /// Every click is handed to ZWorld.ShuttleOrdersFilter exactly as the view's PlayerInput.Flush does (the unit -> waypoint lists of one
    /// click), then what is left is sent with SendWaypoints, as Flush sends it.
    ///  A. level 16, the parked ship: a move order to water, rocks, a closed tile, a building, the map's edge band and off the map is taken
    ///     out of the click (the reason named); 12 s later the ship stands where it stood: 0 flights, 0 hovers;
    ///  B. the same order with ANYWHERE (the filter off): it flies there and hovers (the rules as they were);
    ///  C. free ground: the order is kept, it flies there and lands (0 hovers);
    ///  D. a spot only units stand on (a grunt squad holding its place on free ground): the order is kept (units walk away), it flies there
    ///     and hovers (the rules' hover over a blocked spot is still there for this); the squad ordered away, the same click lands it there;
    ///  E. a mixed click (the ship + a grunt squad) at a place the ship can never land but robots can walk to: the squad gets its order,
    ///     the ship none; the same with the filter off: both get theirs;
    ///  F. in the air: an order to water while it flies to free ground is taken out; it lands where it was going;
    ///  G. STOP (an empty list) and the repair order are never taken out; a CPU team's click is left alone (its shuttle order is refused by
    ///     the rules anyway);
    ///  H. the test is the landing spot rule without the units: on 600 tile middles round the ship (out of the edge band, no own repair
    ///     station) ZWorld.ShuttleNeverLands on the level agrees with the landing spot rule on a 5-degree lattice of headings
    ///     (ZWorld.ShuttleNeverLattices) on the same level without its units (0 disagreements);
    ///  I. (review) no refusal where the landing could fit: the nearest place that the ship's own 15-degree lattice refuses but another
    ///     lattice fits is kept (it flies there: it lands, or hovers when it arrives on a lattice that does not fit); of the 600 places,
    ///     the refused ones where some 1-degree heading fits are at most 1 % of the landable ones.
    /// </summary>
    public static class OzShuttleTouchesCount
    {
        const double Tick = ZConst.TICK;

        static void Run(ZWorld w, double secs, Func<bool> until = null)
        {
            double end = w.Time + secs;
            while (w.Time < end) { w.Tick(Tick); if (until != null && until()) return; }
        }

        static string P(int x, int y) => x + "," + y;

        static readonly Dictionary<int, (bool never, string why)> memo = new Dictionary<int, (bool, string)>(); static ZWorld memoWorld;

        /// <summary>Review: the base headings of every 1-degree lattice (with the landing spot rule's 15-degree turns: every whole degree).</summary>
        static readonly int[] Deg1 = Enumerable.Range(0, ZWorld.ShuttleLandTurnStepDeg).ToArray();

        /// <summary>Review: some heading of the 15-degree lattices from these base headings fits at (x, y) (the world's own units count: a
        /// world without units for "never").</summary>
        static bool Lattices(ZWorld w, ZObject s, int x, int y, int[] bases)
        {
            foreach (int b in bases) if (w.ShuttleLandSpot(x, y, s, b, out _, out _)) return true;
            return false;
        }

        /// <summary>One click as PlayerInput collects it: each unit its own list with one move to (x, y); through the filter, then sent.
        /// Returns the units taken out; sent = the units whose order was sent and taken (SendWaypoints true).</summary>
        static List<ZObject> Click(ZWorld w, Team team, IEnumerable<ZObject> units, int x, int y, bool landableOnly, out List<ZObject> sent)
        {
            var pending = new Dictionary<ZObject, List<ZWaypoint>>();
            foreach (var u in units) pending[u] = new List<ZWaypoint> { ZWaypoint.Move(x, y, true) };
            var refused = new List<ZObject>();
            w.ShuttleOrdersFilter(team, pending, landableOnly, refused);
            sent = new List<ZObject>();
            foreach (var kv in pending) if (w.SendWaypoints(team, kv.Key, kv.Value)) sent.Add(kv.Key);
            return refused;
        }

        /// <summary>The nearest tile middle (from (cx, cy), out of the edge band) where the ship can never land for the reason `why`
        /// (want(tile x, tile y) also true), the units left out.</summary>
        static bool Never(ZWorld w, ZObject s, string why, int cx, int cy, out int px, out int py, Func<int, int, bool> want = null)
        {
            px = py = 0; int e = ZWorld.ShuttleEdgeTiles;
            // review: nearest first (a stable sort: ties keep the map's row order, so the same place as the old full scan), and the first
            // match ends it (the never test tries three lattices of headings near an obstacle now: the full scan took three times as long)
            var cand = new List<(long d, int tx, int ty)>();
            for (int ty = e; ty < w.Map.height - e; ty++)
                for (int tx = e; tx < w.Map.width - e; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8; long d = (long)(x - cx) * (x - cx) + (long)(y - cy) * (y - cy);
                    if (d >= 64L * 64L) cand.Add((d, tx, ty));
                }
            if (w != memoWorld) { memo.Clear(); memoWorld = w; }                                                  // the searches of one world share the answers (nothing ticks between them)
            foreach (var (d, tx, ty) in cand.OrderBy(c => c.d))
            {
                if (want != null && !want(tx, ty)) continue;
                int x = tx * 16 + 8, y = ty * 16 + 8; int key = ty * 65536 + tx;
                if (!memo.TryGetValue(key, out var m)) { bool nv = w.ShuttleNeverLands(x, y, s, out string wy0); memo[key] = m = (nv, wy0); }
                if (m.never && m.why == why) { px = x; py = y; return true; }
            }
            return false;
        }

        /// <summary>A target where shuttle s can land now, between minR and maxR px from (cx, cy).</summary>
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

        /// <summary>Until it stands on the ground again or hovers (at most max s), then the rough landing's settle. "landed" / "hover" / "air".</summary>
        static string Settle(ZWorld w, ZObject s, double max = 120)
        {
            double t0 = w.Time; bool left = false;
            Run(w, max, () =>
            {
                if (s.ShuttleState != ZShuttleState.Landed) left = true;
                return s.Destroyed || (left && w.Time - t0 > 0.5 && (s.ShuttleState == ZShuttleState.Landed || s.ShuttleState == ZShuttleState.Hover));
            });
            Run(w, ZWorld.ShuttleRoughSettleSec + 0.1, () => s.ShuttleLevelled && w.Time >= s.ShuttleRampHoldUntil);
            return s.ShuttleState == ZShuttleState.Landed ? "landed" : s.ShuttleState == ZShuttleState.Hover ? "hover" : "air";
        }

        public static string Check(string settings, string m16, string m02)
        {
            var sb = new StringBuilder();
            bool was = ZWorld.ShuttlePlayableDeviation, wasOn = ZWorld.ShuttleOn; var team0 = ZWorld.ShuttleTeam; int nl0 = ZWorld.ShuttleOrdersNoLand;
            try
            {
                ZWorld.ShuttlePlayableDeviation = true; ZWorld.ShuttleOn = true; ZWorld.ShuttleTeam = Team.Red;
                var me = Team.Red;
                // ---------------------------------------------------------------- A. the places it can never land: no order at all
                var w = OzV11Kit.World(settings, m16, 1);
                var s = w.PlayableShuttle;
                sb.AppendLine($"level 16: the parked ship #{s.Id} ({s.Owner}) at {P(s.CenterX, s.CenterY)}, heading {s.ShuttleHeadingDeg:0}");
                var places = new List<(string why, int x, int y)>();
                foreach (var why in new[] { "water", "rocks", "blocked", "building", "item", "bridge", "flag" })
                    if (Never(w, s, why, s.CenterX, s.CenterY, out int x, out int y)) places.Add((why, x, y));
                int ey = s.CenterY;
                places.Add(("map edge", 8, ey));                                                                        // a click in the edge band (tile 0)
                places.Add(("map edge", ZWorld.ShuttleEdgeTiles * 16 - 4, ey));                                         // the band's last tile
                places.Add(("map edge", -40, ey));                                                                      // off the map
                bool allA = places.Count >= 6; int f0 = s.ShuttleFlights, h0 = s.ShuttleHovers, cx0 = s.CenterX, cy0 = s.CenterY, n0 = s.ShuttleNoLandOrders;
                foreach (var (why, x, y) in places)
                {
                    var out1 = Click(w, me, new[] { s }, x, y, true, out var sent);
                    bool ok = out1.Count == 1 && out1[0] == s && sent.Count == 0 && ZWorld.ShuttleOrdersNoLandWhy == why;
                    allA &= ok;
                    sb.AppendLine($"   {why,-9} at {P(x, y)}: taken out of the click {out1.Count == 1}, sent {sent.Count}, the reason \"{ZWorld.ShuttleOrdersNoLandWhy}\"{(ok ? "" : "  <- WRONG")}");
                }
                Run(w, 12);
                bool still = s.ShuttleFlights == f0 && s.ShuttleHovers == h0 && s.CenterX == cx0 && s.CenterY == cy0 && s.ShuttleState == ZShuttleState.Landed && s.ShuttleNoLandOrders == n0 + places.Count;
                sb.AppendLine($"   12 s later: flights {s.ShuttleFlights - f0}, hovers {s.ShuttleHovers - h0}, moved {s.CenterX != cx0 || s.CenterY != cy0}, {s.ShuttleState}; its orders taken out: {s.ShuttleNoLandOrders - n0}");
                sb.AppendLine(OzV11Kit.Verdict(allA && still, $"A. LANDABLE SPOTS ONLY: a move order to {string.Join(", ", places.Select(p => p.why).Distinct())} gives the shuttle no order (taken out of the click, the reason named): 0 flights, 0 hovers"));

                // ---------------------------------------------------------------- B. ANYWHERE: it flies there and hovers (the rules as they were)
                var water = places.FirstOrDefault(p => p.why == "water");
                bool bOk = false;
                if (water.why != null)
                {
                    var out2 = Click(w, me, new[] { s }, water.x, water.y, false, out var sent2);
                    string end = Settle(w, s, 90);
                    bOk = out2.Count == 0 && sent2.Count == 1 && end == "hover" && s.ShuttleHovers == h0 + 1 && s.ShuttleNoLand == "water";
                    sb.AppendLine($"   ANYWHERE, the same click on the water: taken out {out2.Count}, sent {sent2.Count}; it {end} ({s.ShuttleNoLand}), hovers {s.ShuttleHovers - h0}");
                }
                sb.AppendLine(OzV11Kit.Verdict(bOk, "B. ANYWHERE: the order to the water is sent as before: it flies there and hovers where it cannot land"));

                // ---------------------------------------------------------------- C. free ground: kept, it lands
                bool cOk = PadSpot(w, s, s.CenterX, s.CenterY, 300, 900, out int gx, out int gy);
                if (cOk)
                {
                    int hc = s.ShuttleHovers;
                    var out3 = Click(w, me, new[] { s }, gx, gy, true, out var sent3);
                    string end = Settle(w, s, 120);
                    cOk = out3.Count == 0 && sent3.Count == 1 && end == "landed" && s.ShuttleHovers == hc && Math.Abs(s.CenterX - gx) <= 24 && Math.Abs(s.CenterY - gy) <= 24;
                    sb.AppendLine($"   free ground at {P(gx, gy)}: taken out {out3.Count}, sent {sent3.Count}; it {end} at {P(s.CenterX, s.CenterY)}, hovers {s.ShuttleHovers - hc}");
                }
                sb.AppendLine(OzV11Kit.Verdict(cOk, "C. free ground: the order is kept, the shuttle flies there and lands (0 hovers)"));

                // ---------------------------------------------------------------- D. only units on the spot: kept; the rules' hover until they go
                bool dOk = PadSpot(w, s, s.CenterX, s.CenterY, 320, 900, out int ux, out int uy);
                ZObject sq = null;
                if (dOk)
                {
                    sq = OzV11Kit.Place(w, "grunt", ux, uy, me);
                    w.SendHold(me, sq);                                                                                // they stay there until ordered away
                    bool blockedNow = !w.ShuttlePadFree(ux, uy, s, out string whyNow), neverD = w.ShuttleNeverLands(ux, uy, s, out _);
                    int hd = s.ShuttleHovers;
                    var out4 = Click(w, me, new[] { s }, ux, uy, true, out var sent4);
                    string end = Settle(w, s, 120);
                    string hoverWhy = s.ShuttleNoLand; bool hovered = end == "hover" && hoverWhy == "units";
                    // the squad is ordered away; once it has gone the same click again: now it comes down there (a hover waits for a new order)
                    if (PadSpot(w, s, ux, uy, 240, 600, out int ax, out int ay)) OzV11Kit.PlayerMove(w, sq, ax, ay, false);
                    Run(w, 20, () => w.ShuttlePadFree(ux, uy, s, out _));
                    bool cleared = w.ShuttlePadFree(ux, uy, s, out _);
                    var out4b = Click(w, me, new[] { s }, ux, uy, true, out var sent4b);
                    string end2 = Settle(w, s, 60);
                    dOk = blockedNow && whyNow == "units" && !neverD && out4.Count == 0 && sent4.Count == 1 && hovered && s.ShuttleHovers == hd + 1 && cleared && out4b.Count == 0 && sent4b.Count == 1 && end2 == "landed";
                    sb.AppendLine($"   a grunt squad #{sq.Id} holding on free ground at {P(ux, uy)}: blocked now ({whyNow}) {blockedNow}, never {neverD}; taken out {out4.Count}, sent {sent4.Count}; it {end} ({hoverWhy}); the squad ordered away, the spot free {cleared}; the same click again: sent {sent4b.Count}, it {end2} at {P(s.CenterX, s.CenterY)}");
                }
                sb.AppendLine(OzV11Kit.Verdict(dOk, "D. a spot only units stand on is not refused (they walk away): it flies there and hovers (the rules' safety hover); once they have gone the same click lands it there"));

                // ---------------------------------------------------------------- E. a mixed click: the squad goes, the ship does not
                var w2 = OzV11Kit.World(settings, m16, 2); var s2 = w2.PlayableShuttle;
                bool eOk = Never(w2, s2, "building", s2.CenterX, s2.CenterY, out int mx, out int my, (tx, ty) => w2.Map.ClassAt(tx, ty) == ZClass.Ground && w2.Path.TilePassable(tx, ty, true) && !w2.Objects.Any(o => !o.Destroyed && o.Contains(tx * 16 + 8, ty * 16 + 8)))
                           || Never(w2, s2, "blocked", s2.CenterX, s2.CenterY, out mx, out my, (tx, ty) => w2.Map.ClassAt(tx, ty) == ZClass.Ground && w2.Path.TilePassable(tx, ty, true));
                if (eOk && PadSpot(w2, s2, s2.CenterX, s2.CenterY, 64, 400, out int qx, out int qy))
                {
                    var g2 = OzV11Kit.Place(w2, "grunt", qx, qy, me);
                    w2.ShuttleNeverLands(mx, my, s2, out string whyM);
                    var out5 = Click(w2, me, new[] { s2, g2 }, mx, my, true, out var sent5);
                    bool squadGot = g2.Waypoints.Count >= 1 && sent5.Contains(g2), shipNone = out5.Count == 1 && out5[0] == s2 && !sent5.Contains(s2);
                    Run(w2, 8);
                    bool shipStayed = s2.ShuttleFlights == 0 && s2.ShuttleState == ZShuttleState.Landed;
                    var w3 = OzV11Kit.World(settings, m16, 2); var s3 = w3.PlayableShuttle; var g3 = OzV11Kit.Place(w3, "grunt", qx, qy, me);
                    var out6 = Click(w3, me, new[] { s3, g3 }, mx, my, false, out var sent6);
                    bool bothOff = out6.Count == 0 && sent6.Contains(g3) && sent6.Contains(s3);
                    eOk = squadGot && shipNone && shipStayed && bothOff;
                    sb.AppendLine($"   level 16 (seed 2): the ship + a grunt squad, one click at {P(mx, my)} (the ship: never, \"{whyM}\"; a robot can walk there): the squad's order sent {squadGot}, the ship taken out {shipNone}, 8 s later the ship flights {s2.ShuttleFlights}; with ANYWHERE both sent {bothOff}");
                }
                else eOk = false;
                sb.AppendLine(OzV11Kit.Verdict(eOk, "E. a mixed click: the other units get their order, the shuttle none (as for any order some of a group cannot take); ANYWHERE: both"));

                // ---------------------------------------------------------------- F. in the air: an order to the water is taken out, it lands where it was going
                bool fOk = false;
                var w4 = OzV11Kit.World(settings, m16, 3); var s4 = w4.PlayableShuttle;
                if (PadSpot(w4, s4, s4.CenterX, s4.CenterY, 500, 1000, out int fx, out int fy) && Never(w4, s4, "water", s4.CenterX, s4.CenterY, out int wx, out int wy))
                {
                    Click(w4, me, new[] { s4 }, fx, fy, true, out _);
                    Run(w4, 30, () => s4.ShuttleState == ZShuttleState.Cruise && s4.ShuttleSpeedPx > 8);
                    bool flying = s4.ShuttleAirborne;
                    int goalX = s4.ShuttleGoalX, goalY = s4.ShuttleGoalY;
                    var out7 = Click(w4, me, new[] { s4 }, wx, wy, true, out var sent7);
                    bool sameGoal = s4.ShuttleGoalX == goalX && s4.ShuttleGoalY == goalY;
                    string end = Settle(w4, s4, 120);
                    fOk = flying && out7.Count == 1 && sent7.Count == 0 && sameGoal && end == "landed" && s4.ShuttleHovers == 0 && Math.Abs(s4.CenterX - fx) <= 24 && Math.Abs(s4.CenterY - fy) <= 24;
                    sb.AppendLine($"   flying to {P(fx, fy)} (in the air {flying}), a click on the water at {P(wx, wy)}: taken out {out7.Count}, its goal kept {sameGoal}; it {end} at {P(s4.CenterX, s4.CenterY)}, hovers {s4.ShuttleHovers}");
                }
                sb.AppendLine(OzV11Kit.Verdict(fOk, "F. in the air: an order to a place it can never land is taken out; it flies on and lands where it was going"));

                // ---------------------------------------------------------------- G. STOP, the repair order, a CPU team: never taken out
                var pend = new Dictionary<ZObject, List<ZWaypoint>> { [s] = new List<ZWaypoint>() };
                int g1 = w.ShuttleOrdersFilter(me, pend, true);
                var pend2 = new Dictionary<ZObject, List<ZWaypoint>> { [s] = new List<ZWaypoint> { new ZWaypoint { mode = WpMode.UnitRepair, refId = -1, x = water.x, y = water.y } } };
                int g2n = w.ShuttleOrdersFilter(me, pend2, true);
                var pend3 = new Dictionary<ZObject, List<ZWaypoint>> { [s] = new List<ZWaypoint> { ZWaypoint.Move(water.x, water.y, true) } };
                int g3n = w.ShuttleOrdersFilter(Team.Blue, pend3, true);                                                   // blue is the CPU here
                bool gOk = g1 == 0 && pend.Count == 1 && g2n == 0 && pend2.Count == 1 && g3n == 0 && pend3.Count == 1;
                sb.AppendLine($"   STOP taken out {g1}, the repair order (to the water's point) taken out {g2n}, a CPU team's click taken out {g3n}");
                sb.AppendLine(OzV11Kit.Verdict(gOk, "G. STOP, the repair order and a CPU team's click are never taken out"));

                // ---------------------------------------------------------------- H. the test = the landing spot rule without the units
                string noUnits16 = OzShuttleFairSpotCount.NoWalkers(m16);                                                // DV128: the guns stay (they never walk away; the click's test counts them)
                var wn = OzV11Kit.World(settings, noUnits16, 1); var sn = wn.PlayableShuttle;
                int n = 0, agree = 0, never = 0, skipped = 0; var bad = new List<string>(); int ed = ZWorld.ShuttleEdgeTiles;
                int refusedFit1 = 0, otherX = 0, otherY = 0; long otherD = long.MaxValue; var fit1List = new List<string>();
                var w5 = OzV11Kit.World(settings, m16, 4); var s5 = w5.PlayableShuttle;                                           // I: a fresh level 16, its ship parked
                for (int k = 0; k < 2400 && n < 600; k++)
                {
                    double ang = k * 2.399963, r = 30 + (k * 37) % 900;                                                       // a golden-angle spiral round the ship
                    int tx = (s.CenterX + (int)(Math.Cos(ang) * r)) >> 4, ty = (s.CenterY + (int)(Math.Sin(ang) * r)) >> 4;
                    if (tx < ed || ty < ed || tx >= w.Map.width - ed || ty >= w.Map.height - ed) { skipped++; continue; }
                    int x = tx * 16 + 8, y = ty * 16 + 8;
                    if (w.Objects.Any(o => o.Kind == ZKind.Building && !o.Destroyed && o.CanRepairUnit(me) && o.Contains(x, y))) { skipped++; continue; }
                    bool a1 = w.ShuttleNeverLands(x, y, s, out string wa), b1 = sn != null && !Lattices(wn, sn, x, y, ZWorld.ShuttleNeverLattices);
                    n++; if (a1 == b1) agree++; else if (bad.Count < 6) bad.Add($"{P(x, y)} never {a1} ({wa}) / without units {b1}");
                    if (a1) never++;
                    // review: a refused place against every heading (1 degree: the landing tries one 15-degree lattice from the heading it arrives
                    // on); a kept place that the ship's own lattice alone refuses (the kit before the review refused it): the flight of I
                    if (sn != null && a1 && Lattices(wn, sn, x, y, Deg1)) { refusedFit1++; if (fit1List.Count < 6) fit1List.Add(P(x, y)); }
                    if (sn != null && !a1)
                    {
                        long d = s5 == null ? long.MaxValue : (long)(x - s5.CenterX) * (x - s5.CenterX) + (long)(y - s5.CenterY) * (y - s5.CenterY);
                        if (d < otherD && d >= 160L * 160L && !wn.ShuttleLandSpot(x, y, sn, sn.ShuttleHeadingDeg, out _, out _)) { otherD = d; otherX = x; otherY = y; }
                    }
                }
                int landable1 = n - never + refusedFit1;                                                                     // a kept place fits on the 5-degree lattice
                bool hOk = sn != null && n >= 500 && agree == n;
                sb.AppendLine($"   {n} tile middles round the ship ({skipped} skipped: the edge band, own repair stations): never-lands {never}, agreeing with the level without its units {agree}{(bad.Count > 0 ? "; " + string.Join("; ", bad) : "")}");
                sb.AppendLine(OzV11Kit.Verdict(hOk, "H. the test is the landing spot rule without the units (a 5-degree lattice of headings, the ship's outline + 0.5 m, its ramp lane): 0 disagreements"));

                // ---------------------------------------------------------------- I (review). never refuses where a heading fits: every heading, and a flight
                // the places the review found on level 16 where the kit before it refused the order and a landing can fit (the first one
                // among the 600 above when there is one), each flown to on a fresh level 16 without its units: kept now, it flies there; it lands when it arrives
                // on a lattice that fits, else the rules' safety hover (the landing tries one 15-degree lattice from its arrival heading)
                bool Other(int x, int y) => sn != null && s5 != null && (ZWorld.ShuttleFairSpotOn || !wn.ShuttleLandSpot(x, y, sn, sn.ShuttleHeadingDeg, out _, out _)) && !w5.ShuttleNeverLands(x, y, s5, out _);   // DV128: the landing tries the click's lattice too: the review's places the click takes are flown to
                var iPlaces = new List<(int x, int y)>();
                if (otherD < long.MaxValue) iPlaces.Add((otherX, otherY));
                foreach (var (kx, ky) in new[] { (968, 104), (168, 760), (1448, 440), (1288, 1288) }) if (iPlaces.Count < 3 && !iPlaces.Contains((kx, ky)) && Other(kx, ky)) iPlaces.Add((kx, ky));
                int iSent = 0, iLanded = 0, iHover = 0, iSeed = 4;
                foreach (var (bx, by) in iPlaces)
                {
                    var wi = OzV11Kit.World(settings, noUnits16, iSeed++); var si = wi.PlayableShuttle;                   // no units: nothing shoots it down on the way (the order and the landing are measured)
                    var outI = Click(wi, me, new[] { si }, bx, by, true, out var sentI);
                    string end = Settle(wi, si, 150); if (si.Destroyed) end = "destroyed";
                    if (outI.Count == 0 && sentI.Count == 1) iSent++;
                    if (end == "landed") iLanded++; else if (end == "hover") iHover++;
                    sb.AppendLine($"   a place only another 15-degree lattice fits (the kit before the review refused it) at {P(bx, by)}: taken out {outI.Count}, sent {sentI.Count}; it {end}{(end == "hover" ? " (" + si.ShuttleNoLand + ": it arrived on a lattice that does not fit, the rules' safety hover)" : "")} at {P(si.CenterX, si.CenterY)}, heading {si.ShuttleHeadingDeg:0}");
                }
                bool iFlight = iPlaces.Count >= 2 && iSent == iPlaces.Count && iLanded + iHover == iPlaces.Count;
                sb.AppendLine($"   of the same {n} tile middles {never} are refused; a heading fits at 1 degree on {refusedFit1} of them{(fit1List.Count > 0 ? " (" + string.Join("; ", fit1List) + ")" : "")}; landable at some 1-degree heading: {landable1}; the flights: kept {iSent} of {iPlaces.Count}, landed {iLanded}, hovered {iHover}");
                bool iOk = iFlight && refusedFit1 * 100 <= Math.Max(1, landable1);
                sb.AppendLine(OzV11Kit.Verdict(iOk, "I. no refusal where the landing could fit (review): places only another 15-degree lattice fits are kept (it flies there); refused although some 1-degree heading fits: at most 1 % of the landable places (a window under 5 degrees wide)"));
                sb.AppendLine($"(shuttle orders taken out in this check: {ZWorld.ShuttleOrdersNoLand - nl0})");
            }
            finally { ZWorld.ShuttlePlayableDeviation = was; ZWorld.ShuttleOn = wasOn; ZWorld.ShuttleTeam = team0; memo.Clear(); memoWorld = null; }
            return sb.ToString();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Shuttle: K256g Shuttle Orders Check")]
        static void Menu() => OzV11Kit.RunMenu("K256g the shuttle's orders only to places where it can land", () => Check(OzV11Kit.SettingsText, OzV11Kit.MapText("L16"), OzV11Kit.MapText("L02")));
#endif
    }
}
