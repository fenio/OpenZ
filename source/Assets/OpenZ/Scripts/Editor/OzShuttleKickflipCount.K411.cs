using System;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K411, the kickflip by itself, counted out of Play (run by Editor/OzShuttleKickflipCount.cs's menu check and the kit's headless runner,
    /// after the first round's and K390's parts). Plain C#: the game's own draw, state and flip (View/OzShuttleKickflip.Luck.cs,
    /// View/OzShuttleKickflip.Core.cs). Many simulated orders, each given the way the view gives it (OzKickflipState.LuckOrder), each lucky
    /// one flown through the steady cruise frame by frame until its flip is over. PASS / FAIL lines R1 .. R10:
    ///  R1 the knobs: the chance 5 %, the switch on, "far" = the double click's own rule;
    ///  R2 10,000 far orders (8 ships, 30 .. 150 m): about 5 % flip by themselves (of the far orders Chance / (1 + Chance); of the draws
    ///     Chance), every lucky order started and finished one whole flip, no other order flipped;
    ///  R3 0 back-to-back: no ship flipped by luck on two far orders in a row (also at 100 %: exactly every other order);
    ///  R4 3,000 short orders (under the far rule): 0 draws, 0 flips;
    ///  R5 one draw per order: the double click's second click, a third click and the destination clicked again draw nothing; a thousand
    ///     frames after an order that did not draw never arm it;
    ///  R6 deterministic: the same orders give the same flips on a second run; another ship id gives other draws;
    ///  R7 never while busy (crashing, just hit, landing), never with the switch off or the chance 0 (each at 100 %);
    ///  R8 the Shift queue: the queue's first click counts once its point is the rules' goal, only then, only once; a goal turned
    ///     elsewhere or a queue never sent draws nothing;
    ///  R9 a lucky flip is the double click's flip: frame for frame the same pose, arc and events;
    ///  R10 a lucky order while a flip is still running waits and arms after it (as a double click there).
    /// Test only.
    /// </summary>
    public static class OzShuttleKickflipCountK411
    {
        const double Dt60 = 1.0 / 60.0;
        static Action<bool, string> V;

        public static void Run(StringBuilder sb, Action<bool, string> v)
        {
            V = v;
            double c0 = OzKickflipLuck.Chance; bool on0 = OzKickflipLuck.On;
            try
            {
                OzKickflipLuck.Chance = OzKickflipLuck.DefaultChance; OzKickflipLuck.On = true;
                sb.AppendLine($"-- R by itself (K411): a far normal order ({OzKickflipState.FarM:F0} m or more) arms the flip with a chance of {OzKickflipLuck.Chance * 100:F0} %, never twice in a row, never while busy");
                Knobs(); Many(); Short(); OnePerOrder(); Same(); Busy(); Queue(); Identical(); WhileFlipping();
            }
            finally { OzKickflipLuck.Chance = c0; OzKickflipLuck.On = on0; }
        }

        static OzKickflipIn Steady(int gx, int gy, double remain = 300.0)
        {
            double v = 42.0 / 8.0, a = ZWorld.ShuttleAccelMax(42.0) / 8.0;
            return new OzKickflipIn
            {
                Alive = true, State = 2, AtHeight = true, OnScreen = true, SpeedMps = v, VmaxMps = v, AccelMps2 = 0, AccMaxMps2 = a, TurnRateDeg = 0,
                RemainM = remain, BrakeM = ZWorld.ShuttleBrakeDistance(42.0, 0.0, 42.0) / 8.0, FeetM = 12, Health = 3000, GoalX = gx, GoalY = gy,
            };
        }

        /// <summary>A plain number stream for the orders' places and lengths (the orders only; the draw is the game's).</summary>
        sealed class Lcg { ulong s; public Lcg(ulong seed) { s = seed * 6364136223846793005ul + 1442695040888963407ul; } public double Next() { s = s * 6364136223846793005ul + 1442695040888963407ul; return (s >> 11) * (1.0 / 9007199254740992.0); } }

        /// <summary>The ship flies the order: the steady cruise until the flip (if armed) is over, at most maxSec. Returns the frames flipped.</summary>
        static int Fly(OzKickflipState s, int gx, int gy, double distM, double maxSec = 12.0)
        {
            var i = Steady(gx, gy, Math.Max(distM, 1.0)); int flipped = 0;
            for (double t = 0; t < maxSec; t += Dt60)
            {
                s.Step(i, Dt60, out var o);
                if (o.Active) flipped++;
                if (s.Phase == OzKickflipPhase.Idle && t > 0.5) break;
            }
            return flipped;
        }

        static void Knobs()
        {
            V(Math.Abs(OzKickflipLuck.DefaultChance - 0.05) < 1e-12 && OzKickflipLuck.On && OzKickflipState.FarM == 30.0,
              $"R1 the knobs: chance {OzKickflipLuck.DefaultChance * 100:F0} % (public OzKickflipLuck.Chance), switch OzKickflipLuck.On = {OzKickflipLuck.On}, far = the double click's own {OzKickflipState.FarM:F0} m; the flip starts by K390's rule ({OzKickflipState.SteadySpeed * 100:F0} % of the top speed, the room, the whole arc on screen)");
        }

        /// <summary>R2 / R3: many far orders over several ships, interleaved.</summary>
        static List<int> ManyRun(int ships, int perShip, int idBase, out int far, out int draws, out int picks, out int starts, out int ends, out int b2b, out int flipsNoPick)
        {
            var st = new OzKickflipState[ships]; var last = new bool[ships]; var rnd = new Lcg(411);
            var list = new List<int>();
            for (int k = 0; k < ships; k++) st[k] = new OzKickflipState(idBase + k);
            far = draws = picks = starts = ends = b2b = flipsNoPick = 0;
            for (int n = 0; n < perShip; n++)
                for (int k = 0; k < ships; k++)
                {
                    var s = st[k];
                    double d = 30.0 + 120.0 * rnd.Next(), a = (n % 2 == 0 ? 0.0 : Math.PI) + rnd.Next() * Math.PI * 0.5;   // each order in the quarter opposite the last: never the same place (>= 42 m apart)
                    int gx = 1600 + (int)(Math.Cos(a) * d * 8), gy = 1600 + (int)(Math.Sin(a) * d * 8);
                    int s0 = s.Starts, e0 = s.Ends, p0 = s.LuckPicks;
                    s.Click(false, true, d, gx, gy);
                    int ev = s.LuckOrder(gx, gy, d, false, false, false);
                    bool picked = (ev & OzKickflipState.EvLuck) != 0;
                    int fl = Fly(s, gx, gy, d);
                    if (picked) { list.Add(k * 100000 + n); if (last[k]) b2b++; }
                    if (!picked && fl > 0) flipsNoPick++;
                    last[k] = picked;
                    starts += s.Starts - s0; ends += s.Ends - e0;
                }
            for (int k = 0; k < ships; k++) { far += st[k].LuckFar; draws += st[k].LuckDraws; picks += st[k].LuckPicks; }
            return list;
        }

        static List<int> manyFirst;

        static void Many()
        {
            manyFirst = ManyRun(8, 1250, 501, out int far, out int draws, out int picks, out int starts, out int ends, out int b2b, out int noPick);
            double share = picks / (double)Math.Max(1, far), ofDraws = picks / (double)Math.Max(1, draws), want = OzKickflipLuck.Chance / (1 + OzKickflipLuck.Chance);
            V(far == 10000 && share >= 0.040 && share <= 0.056 && ofDraws >= 0.043 && ofDraws <= 0.058,
              $"R2 {far} far orders on 8 ships: {picks} flipped by themselves = {share * 100:F2} % of the far orders (expected {want * 100:F2} % = 5 % with the never-twice rule), {ofDraws * 100:F2} % of the {draws} draws (expected 5 %)");
            V(starts == picks && ends == picks && noPick == 0,
              $"R2 every lucky order started ({starts}) and finished ({ends}) one whole flip in the steady cruise; orders that did not draw it flipped {noPick} times");
            V(b2b == 0, $"R3 back-to-back: {b2b} ships flipped by luck on two far orders in a row");
            OzKickflipLuck.Chance = 1.0;
            ManyRun(4, 250, 601, out int far1, out _, out int picks1, out int st1, out _, out int b2b1, out _);
            // a lucky flip that never got to start (a new order came first) does not hold the next one back; a flown one does
            var u = new OzKickflipState(651);
            int e1 = u.LuckOrder(2400, 1600, 60, false, false, false);                                     // armed by luck, never flown
            u.Click(false, true, 60, 1600, 2400);                                                          // a new order first: disarmed
            int e2 = u.LuckOrder(1600, 2400, 60, false, false, false);                                     // draws again: armed
            Fly(u, 1600, 2400, 60);                                                                        // flown
            u.Click(false, true, 60, 2400, 2400);
            int e3 = u.LuckOrder(2400, 2400, 60, false, false, false);                                     // right after a flown flip: no draw
            OzKickflipLuck.Chance = OzKickflipLuck.DefaultChance;
            V(b2b1 == 0 && picks1 == far1 / 2 && st1 == picks1, $"R3 at 100 %: {picks1} of {far1} far orders flipped (exactly every other order: {far1 / 2}), back-to-back {b2b1}, flips started {st1}");
            V((e1 & OzKickflipState.EvLuck) != 0 && (e2 & OzKickflipState.EvLuck) != 0 && (e3 & OzKickflipState.EvLuck) == 0 && u.Starts == 1 && u.LuckRepeatBlocked == 1,
              $"R3 only a flown flip holds the next order back: armed but never started (a new order came first) -> the next order armed again ({(e2 & OzKickflipState.EvLuck) != 0}); that one flown ({u.Starts} start) -> the next not armed ({u.LuckRepeatBlocked} held back)");
        }

        static void Short()
        {
            OzKickflipLuck.Chance = 1.0;
            var rnd = new Lcg(7); int draws = 0, picks = 0, starts = 0, orders = 0;
            for (int k = 0; k < 6; k++)
            {
                var s = new OzKickflipState(700 + k);
                for (int n = 0; n < 500; n++)
                {
                    double d = 0.5 + 29.4 * rnd.Next(); int gx = 1600 + (n % 2 == 0 ? 1 : -1) * (int)(d * 8), gy = 1600 + n * 64;
                    s.Click(false, true, d, gx, gy);
                    int ev = s.LuckOrder(gx, gy, d, false, false, false); if ((ev & OzKickflipState.EvLuck) != 0) picks++;
                    Fly(s, gx, gy, d, 3.0); orders++;
                }
                draws += s.LuckDraws; starts += s.Starts;
            }
            OzKickflipLuck.Chance = OzKickflipLuck.DefaultChance;
            V(orders == 3000 && draws == 0 && picks == 0 && starts == 0, $"R4 {orders} short orders (0.5 .. 29.9 m, under the far rule) at 100 %: {draws} draws, {picks} armed, {starts} flips");
        }

        static void OnePerOrder()
        {
            OzKickflipLuck.Chance = 1.0;
            var s = new OzKickflipState(801);
            s.LuckOrder(2000, 1600, 60, false, true, false);                                               // order 1: busy, no draw (so order 2 may draw)
            int d0 = s.LuckDraws;
            int ev1 = s.LuckOrder(2400, 1600, 60, false, false, false);                                    // order 2: draws (100 %): armed
            int d1 = s.LuckDraws;
            s.LuckOrder(2402, 1601, 60, true, false, false);                                               // the double click's second click on it
            s.LuckOrder(2399, 1603, 60, false, false, false);                                              // a third click
            s.LuckOrder(2400 + 40, 1600, 60, false, false, false);                                         // the same destination clicked again, 5 m off
            int d2 = s.LuckDraws;
            OzKickflipLuck.Chance = 0.0;                                                                   // a new order that does not arm
            var t = new OzKickflipState(802); t.LuckOrder(2000, 1600, 60, false, false, false);
            OzKickflipLuck.Chance = 1.0;
            bool armed = false; var i = Steady(2000, 1600);
            for (int f = 0; f < 1000; f++) { t.Step(i, Dt60, out _); armed |= t.Phase != OzKickflipPhase.Idle; }
            OzKickflipLuck.Chance = OzKickflipLuck.DefaultChance;
            V(d0 == 0 && d1 == 1 && (ev1 & OzKickflipState.EvLuck) != 0 && d2 == 1 && s.LuckOrders == 2,
              $"R5 one draw per order: {d1} draw for the order, {d2 - d1} more for its double click's second click, a third click and the destination clicked again ({s.LuckOrders} orders counted)");
            V(!armed && t.LuckDraws == 0, $"R5 not per frame: an order that did not arm stayed unarmed through 1000 frames at 100 % ({t.LuckDraws} draws later)");
        }

        static void Same()
        {
            var a = ManyRun(8, 1250, 501, out _, out _, out _, out _, out _, out _, out _);
            bool same = a.Count == manyFirst.Count; for (int k = 0; same && k < a.Count; k++) same = a[k] == manyFirst[k];
            var b = ManyRun(8, 1250, 9001, out _, out _, out _, out _, out _, out _, out _);
            int common = 0; var hs = new HashSet<int>(manyFirst); foreach (var x in b) if (hs.Contains(x)) common++;
            double u1 = OzKickflipLuck.Draw(17, 3), u2 = OzKickflipLuck.Draw(17, 3), u3 = OzKickflipLuck.Draw(18, 3), u4 = OzKickflipLuck.Draw(17, 4);
            V(same && common < b.Count / 2 && u1 == u2 && u1 != u3 && u1 != u4 && u1 >= 0 && u1 < 1,
              $"R6 deterministic: a second run of the 10,000 orders gave the same {a.Count} lucky orders ({(same ? "identical" : "DIFFERENT")}); ships with other ids: {b.Count} lucky, {common} at the same places; draw(17, 3) = {u1:F4} twice, draw(18, 3) = {u3:F4}, draw(17, 4) = {u4:F4}");
        }

        static void Busy()
        {
            OzKickflipLuck.Chance = 1.0;
            int busyPicks = 0, offPicks = 0, zeroPicks = 0, busyBlocked = 0;
            var s = new OzKickflipState(901);
            for (int n = 0; n < 500; n++) { int ev = s.LuckOrder(1600 + n * 80, 1600, 60, false, true, false); if ((ev & OzKickflipState.EvLuck) != 0) busyPicks++; }
            busyBlocked = s.LuckBusyBlocked;
            var t = new OzKickflipState(902);
            for (int n = 0; n < 500; n++) { int ev = t.LuckOrder(1600 + n * 80, 1600, 60, false, false, true); if ((ev & OzKickflipState.EvLuck) != 0) offPicks++; }
            OzKickflipLuck.On = false;
            for (int n = 0; n < 500; n++) { int ev = t.LuckOrder(1600 + n * 80, 2400, 60, false, false, false); if ((ev & OzKickflipState.EvLuck) != 0) offPicks++; }
            OzKickflipLuck.On = true; OzKickflipLuck.Chance = 0.0;
            var z = new OzKickflipState(903);
            for (int n = 0; n < 500; n++) { int ev = z.LuckOrder(1600 + n * 80, 1600, 60, false, false, false); if ((ev & OzKickflipState.EvLuck) != 0) zeroPicks++; }
            OzKickflipLuck.Chance = OzKickflipLuck.DefaultChance;
            V(busyPicks == 0 && busyBlocked == 500, $"R7 busy (crashing, hit within {OzKickflipLuck.HitCalmSec:F0} s, coming down to land) at 100 %: {busyPicks} of 500 far orders armed ({busyBlocked} held back)");
            V(offPicks == 0 && zeroPicks == 0, $"R7 the switch off (the tests' flag, OzKickflipLuck.On = false) at 100 %: {offPicks} of 1000 armed; the chance 0: {zeroPicks} of 500");
        }

        static void Queue()
        {
            var q = new OzKickflipQueued();
            q.Mark(2400, 1600, 1600, 1600, 10.0);
            q.Mark(3200, 1600, 1600, 1600, 10.5);                                                          // a second queue click: the first point counts
            bool r0 = q.Resolve(1600, 1600, 2400, 1600, 11.0);                                             // not sent yet: the goal as it was
            bool r1 = q.Resolve(2408, 1608, 2400, 1600, 12.0);                                             // sent: the goal is its first point
            bool r2 = q.Resolve(2408, 1608, 2400, 1600, 12.1);                                             // only once
            var w = new OzKickflipQueued(); w.Mark(2400, 1600, 1600, 1600, 10.0);
            bool r3 = w.Resolve(1600 + 8 * 200, 1600, 2400, 1600, 11.0);                                    // the goal turned elsewhere (200 m away)
            bool r4 = w.Pending;
            var x = new OzKickflipQueued(); x.Mark(2400, 1600, 1600, 1600, 10.0);
            bool r5 = x.Resolve(2400, 1600, 2400, 1600, 10.0 + OzKickflipLuck.QueueWaitSec + 1);          // never sent within the wait
            // a ship that flies the whole queue: its goal turns to the queue's LAST point (the first one is flown through)
            var y = new OzKickflipQueued(); y.Mark(2400, 1600, 1600, 1600, 10.0); y.Mark(3200, 2400, 1600, 1600, 10.4);
            bool r6 = y.Resolve(3204, 2404, 2400, 1600, 3200, 2400, 11.0);
            bool r7 = y.Resolve(3204, 2404, 2400, 1600, 3200, 2400, 11.1);                                 // only once
            V(!r0 && r1 && !r2 && !r3 && !r4 && !r5 && q.X == 2400 && q.LastX == 3200 && r6 && !r7,
              $"R8 the Shift queue: its first click counts (kept: {q.X == 2400}, the last point follows the later clicks: {q.LastX == 3200}); nothing before it is sent ({r0}); taken once when the goal is its first point ({r1}, again {r2}) or, flying the whole queue, its last point ({r6}, again {r7}); a goal turned 200 m elsewhere ({r3}); a queue not sent within {OzKickflipLuck.QueueWaitSec:F0} s ({r5})");
        }

        static void Identical()
        {
            var a = new OzKickflipState(1201); var b = new OzKickflipState(1201);
            OzKickflipLuck.Chance = 1.0;
            int ea = a.LuckOrder(2400, 1600, 100, false, false, false);
            OzKickflipLuck.Chance = OzKickflipLuck.DefaultChance;
            b.Click(true, true, 100, 2400, 1600);
            var i = Steady(2400, 1600); int frames = 0, diff = 0, flipped = 0; double worst = 0;
            for (int f = 0; f < 600; f++)
            {
                a.Step(i, Dt60, out var oa); b.Step(i, Dt60, out var ob); frames++;
                if (oa.Active) flipped++;
                double d = Math.Abs(oa.RollDeg - ob.RollDeg) + Math.Abs(oa.PitchUpDeg - ob.PitchUpDeg) + Math.Abs(oa.HopM - ob.HopM) + Math.Abs(oa.SideM - ob.SideM) + Math.Abs(oa.RiseM - ob.RiseM) + Math.Abs(oa.Flare - ob.Flare);
                worst = Math.Max(worst, d); if (d > 0 || oa.Events != ob.Events || oa.Active != ob.Active) diff++;
            }
            V((ea & OzKickflipState.EvLuck) != 0 && a.Starts == 1 && a.Ends == 1 && b.Starts == 1 && diff == 0 && flipped > 140,
              $"R9 a lucky flip is the double click's flip: {frames} frames side by side, {flipped} flipped, {diff} frames differ (pose, arc, flare, events; largest difference {worst:G3}); one start, one end each");
        }

        static void WhileFlipping()
        {
            var s = new OzKickflipState(1301);
            s.Click(true, true, 100, 2400, 1600);                                                          // a double click's flip
            var i = Steady(2400, 1600);
            for (int f = 0; f < 600 && s.Phase != OzKickflipPhase.Flip; f++) s.Step(i, Dt60, out _);
            for (int f = 0; f < 60; f++) s.Step(i, Dt60, out _);                                          // into the roll (rolls on past a new order)
            s.LuckOrder(2400, 1600, 100, true, false, false);                                               // its own order, armed by the double click
            s.LuckOrder(3200, 1600, 100, false, false, true);                                               // a far order with the switch off (not armed)
            OzKickflipLuck.Chance = 1.0;
            int ev = s.LuckOrder(3200, 2400, 100, false, false, false);                                     // the next far order: lucky while it still flips
            OzKickflipLuck.Chance = OzKickflipLuck.DefaultChance;
            var ph = s.Phase; var j = Steady(3200, 2400); bool armedAfter = false; int starts0 = s.Starts;
            for (int f = 0; f < 900; f++) { s.Step(j, Dt60, out _); if (s.Phase == OzKickflipPhase.Armed || s.Starts > starts0) armedAfter = true; }
            V((ev & OzKickflipState.EvLuck) != 0 && ph == OzKickflipPhase.Flip && armedAfter && s.Starts == starts0 + 1,
              $"R10 a lucky order while a flip still rolls: it rolled on ({ph}), the lucky flip armed after it and started once ({s.Starts - starts0})");
        }
    }
}
