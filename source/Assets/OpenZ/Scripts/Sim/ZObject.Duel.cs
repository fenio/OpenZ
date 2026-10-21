using System;

namespace OpenZ.Sim
{
    /// <summary>Committed duel manoeuvre of a vehicle (OpenZ deviation DV9, <see cref="ZDuel"/>).</summary>
    public enum ZDuelKind { None = 0, Arc = 1, Flank = 2, Reverse = 3, Close = 4 }

    /// <summary>
    ///
    /// What Zod already does (kept exactly): vehicles keep their target and fire while they move (only robots disengage on a
    /// movement step, zobject.cpp:3519-3520; CheckPassiveEngage skips only MOVING ROBOTS, :1615), so a tank on a MOVE order
    /// shoots whatever enemy unit is in range on the way; the missile's target dodges (DodgeMissile, :3657-3714, fired from
    /// :2183) and a vehicle keeps shooting while it dodges. What Zod does NOT do: an ATTACK / AGRO waypoint whose target is in
    /// range stops dead (StopMove + Engage, :2356-2362 and :2296-2302) and stands until the target dies or leaves range.
    ///
    /// DV9 replaces only that standing phase, only for jeep / light / medium (mech) / heavy, only against a vehicle or cannon
    /// target, only inside an ATTACK or AGRO waypoint (never on a player / bot MOVE order, never for robots, APC, crane or
    /// Mobile Missile): the unit drives committed manoeuvres (Arc = circle-strafe at the preferred range, Flank = straight
    /// dash across the target's side, Reverse = short back-up when too close, Close = spiral in when near the edge of its
    /// range), each held 1-2 s (the no-jitter rule), while Zod's ProcessAttackDamage keeps firing on Zod's cooldown with Zod's
    /// damage / range / missile lead / scatter. Zod's own DodgeMissile still interrupts (the manoeuvre resumes after it).
    /// Movement is Zod's (ProcessMove: tile speeds, damage slow-down, the OpenZ pathing gate for spacing); every heading is
    /// checked before it is taken: the unit's box swept along it on the vehicle grid (no water, cliffs, rocks, buildings, map
    /// edge), no bridge / lane keep-clear area, the end point still inside attack range with a clear line of fire, not onto
    /// another unit, AGRO within Zod's agro leash. No valid heading = Zod's stop and fire. Deterministic: sim time, pixel
    /// grid, and only the world's own seeded ZWorld.DuelRng (Zod's rand() stream ZWorld.Rng is never drawn, so with the
    /// switch off every roll is Zod's). Switch: ZWorld.DuelDeviation (PlayerPrefs oz_duel_sim, default on; off in 'Zod exact'
    /// oz_zod_exact = 1 and in the parity rows). Listed as DV9 in the parity report's Deviations.
    /// </summary>
    public static class ZDuel
    {
        /// <summary>Commit window of one manoeuvre (s): uniform in [CommitMin, CommitMax] (1/100 s steps).</summary>
        public const double CommitMin = 1.0, CommitMax = 2.0;
        /// <summary>Re-aim of the manoeuvre's steering point (s) inside one commit (the arc follows the target).</summary>
        public const double AimInterval = 0.25;
        /// <summary>Steering point distance (px); a new manoeuvre sweeps max(LookPx, 1 s of travel, at most 64 px).</summary>
        public const int LookPx = 24, ShortLookPx = 8;
        /// <summary>No movement (px) for this long (s) while manoeuvring = held by traffic: end the manoeuvre, turn the other way.</summary>
        public const double HeldWindow = 0.6, HeldPx = 1.5;
        /// <summary>A pause (Zod dodge, one tick out of range) shorter than this (s) resumes the same manoeuvre (for at least
        /// CommitMin more).</summary>
        public const double ResumeGap = 3.0;
        /// <summary>Preferred range = attack radius - Margin (px); Close below AttackRadius - CloseBand; Reverse below preferred - ReverseBand.</summary>
        public const int CloseBand = 10, ReverseBand = 28, EndSlack = 4, AgroLeash = 16;
        /// <summary>A duel starts only this far (px) inside attack range: at the very edge (the target dodging in and out) the unit
        /// keeps driving its approach while it fires, so the start is not an in / out flicker.</summary>
        public const int EntryBand = 8;
        /// <summary>DuelStep results: Zod's stop-and-fire, a manoeuvre step, keep driving the approach (engaged).</summary>
        public const int Stand = 0, Manoeuvre = 1, Approach = 2;
        /// <summary>A reverse is only started with room for ReverseRoom x (own speed x its time) before the edge of the band
        /// (the target may back away or dodge at the same time), so it is not cut short by range.</summary>
        public const double ReverseRoom = 2.5;
        public static int Margin(string type) => type == "heavy" ? 26 : type == "medium" ? 24 : 22;
        /// <summary>Manoeuvre weights in the range band (Arc, Flank, Reverse; sum 100): jeeps race and swerve, heavies circle.</summary>
        public static (int arc, int flank, int rev) Weights(string type) =>
            type == "jeep" ? (35, 45, 20) : type == "light" ? (50, 30, 20) : type == "medium" ? (55, 25, 20) : (60, 20, 20);
        /// <summary>After this many manoeuvres circling the same way, the next one turns the other way with FlipPct % chance.</summary>
        public const int FlipAfter = 2, FlipPct = 40;
        /// <summary>Chord of a flank dash: the tangent turned this far (deg) toward the target.</summary>
        public const double FlankInDeg = 20;
    }

    /// <summary>Per-vehicle state of the DV9 duel layer, with counters for the harness / parity row.</summary>
    public sealed class ZDuelState
    {
        public bool active; public ZDuelKind kind, lastKind; public int sign, lastSign, targetId = -1, seq, sameSign;
        public double start, until, lastTime = -100, nextAim, retryAt = -100, fx, fy, movedX, movedY, movedAt, endedAt = -100;
        // counters
        public int Manoeuvres, Expired, AbortBlocked, AbortHeld, AbortRange, AbortTarget, AbortOther, ShortAborts, SignFlips, Stands, Resumes;
        public readonly int[] ByKind = new int[5];
        public double SumCommit, MinExpired = double.MaxValue, MaxExpired;
    }

    public partial class ZObject
    {
        ZDuelState duel;
        enum DuelEnd { Expired, Blocked, Held, Range, Target, Other }

        /// <summary>DV9 state (null until the unit's first duel).</summary>
        public ZDuelState DuelState => duel;
        /// <summary>View: the manoeuvre the sim drives right now (None outside a duel and during a Zod dodge).</summary>
        public ZDuelKind DuelKind => duel != null && duel.active && World != null && World.Time - duel.lastTime < 0.025 && Waypoints.Count > 0 &&
                                     (Waypoints[0].mode == WpMode.Attack || Waypoints[0].mode == WpMode.Agro) ? duel.kind : ZDuelKind.None;
        /// <summary>View: increments on every new manoeuvre (the hull commits to it at once).</summary>
        public int DuelSeq => duel?.seq ?? 0;
        /// <summary>Tanks, mech (medium) and jeep: the vehicles that duel (DV9).</summary>
        public bool DuelCapable => Kind == ZKind.Vehicle && MoveSpeed > 0 && Owner != Team.Neutral &&
                                   (Type == "jeep" || Type == "light" || Type == "medium" || Type == "heavy");

        /// <summary>ATTACK / AGRO waypoint, target in range (where Zod stops and fires): drive a committed manoeuvre and fire.
        /// Returns ZDuel.Manoeuvre (engaged, moved this tick), ZDuel.Approach (engaged, the waypoint keeps driving its approach:
        /// the target is at the very edge of range) or ZDuel.Stand (Zod's StopMove + Engage).</summary>
        internal int DuelStep(ZObject t, double dt, bool agro)
        {
            if (ChargeStep(t, dt, agro, out int chargeResult)) return chargeResult;     // DV121: a jeep drives at a robot it runs over, where Zod stops and fires (Sim/ZObject.JeepCharge.cs)
            if (!ZWorld.DuelDeviation || !DuelCapable || t == null || !(t.Kind == ZKind.Vehicle || t.Kind == ZKind.Cannon)
                || (ZWorld.NoTankDuelVsJeep && Type != "jeep" && t.Kind == ZKind.Vehicle && t.Type == "jeep"))   // combat_nb_v1 NB12: tanks do not manoeuvre against a jeep
            {
                if (duel != null && duel.active) EndDuel(duel, DuelEnd.Other);
                return ZDuel.Stand;
            }
            var d = duel ??= new ZDuelState();
            double now = World.Time;
            bool resumed = false;
            if (d.active)
            {
                if (d.targetId != t.Id) EndDuel(d, DuelEnd.Target);
                else if (now - d.lastTime > ZDuel.ResumeGap) EndDuel(d, DuelEnd.Other);
                else if (now - d.lastTime > 0.015)
                {                                                                            // back from a Zod dodge: the same manoeuvre goes on, for at
                    resumed = true; d.Resumes++; d.movedAt = now; d.movedX = PosX; d.movedY = PosY;   // least another CommitMin (never a 0.1 s
                    d.until = Math.Max(d.until, now + ZDuel.CommitMin);                      // stub between a dodge and the next manoeuvre)
                }
            }
            d.lastTime = now;
            Engage(t);                                                                       // fire (Zod's ProcessAttackDamage, own cooldown)
            if (d.active && now >= d.until) EndDuel(d, DuelEnd.Expired);
            if (d.active && (resumed || now >= d.nextAim || NearCurTarget()))
            {
                var why = DuelAim(d, t, agro, ZDuel.LookPx);
                if (why == DuelEnd.Blocked && now - d.start < ZDuel.CommitMin)
                    why = DuelAim(d, t, agro, ZDuel.ShortLookPx);                            // inside its first second: creep on (a shorter look) rather than flick
                if (why != DuelEnd.Expired) EndDuel(d, why);                                 // (Expired = aimed fine)
            }
            if (!d.active)
            {
                bool chained = now - d.endedAt <= 0.02;                                      // the last manoeuvre ended this tick: go straight on
                if (!chained && Dist(CenterX, CenterY, t.CenterX, t.CenterY) > AttackRadius - ZDuel.EntryBand) return ZDuel.Approach;
                if (now < d.retryAt) return ZDuel.Stand;
                if (!DuelChoose(d, t, agro)) { d.retryAt = now + 0.5; d.Stands++; return ZDuel.Stand; }
            }
            if (!ProcessMove(dt, true, out _, out _))
            {                                                                                // terrain (the sweep missed a corner): stop, choose anew
                EndDuel(d, DuelEnd.Blocked); StopMove(); d.retryAt = now + 0.1;
                return ZDuel.Manoeuvre;
            }
            if (Math.Abs(PosX - d.movedX) + Math.Abs(PosY - d.movedY) >= ZDuel.HeldPx) { d.movedX = PosX; d.movedY = PosY; d.movedAt = now; }
            else if (now - d.movedAt > ZDuel.HeldWindow) { EndDuel(d, DuelEnd.Held); d.sign = -d.sign; d.sameSign = 0; }
            return ZDuel.Manoeuvre;
        }

        /// <summary>The waypoint's target left attack range: a running manoeuvre ends; an ATTACK waypoint re-plans its chase from
        /// here (Zod's own re-plan, cur cleared), an AGRO one chases in a straight line anyway.</summary>
        internal void DuelLeftRange(bool replan)
        {
            if (duel == null || !duel.active) return;
            EndDuel(duel, DuelEnd.Range);
            if (replan) { StopMove(); ClearCur(); }
        }

        bool NearCurTarget() => Math.Abs(CenterX - cur.x) + Math.Abs(CenterY - cur.y) <= 3;

        void EndDuel(ZDuelState d, DuelEnd why)
        {
            if (!d.active) return;
            double commit = World.Time - d.start;
            d.SumCommit += commit;
            switch (why)
            {
                case DuelEnd.Expired: d.Expired++; d.MinExpired = Math.Min(d.MinExpired, commit); d.MaxExpired = Math.Max(d.MaxExpired, commit); break;
                case DuelEnd.Blocked: d.AbortBlocked++; break;
                case DuelEnd.Held: d.AbortHeld++; break;
                case DuelEnd.Range: d.AbortRange++; break;
                case DuelEnd.Target: d.AbortTarget++; break;
                default: d.AbortOther++; break;
            }
            if (why != DuelEnd.Expired && commit < ZDuel.CommitMin - 1e-9) d.ShortAborts++;
            d.active = false; d.lastKind = d.kind; d.endedAt = World.Time;
        }

        /// <summary>Pick the next manoeuvre (deterministic: sim state + ZWorld.DuelRng) and start it if a heading is valid.</summary>
        bool DuelChoose(ZDuelState d, ZObject t, bool agro)
        {
            var rng = World.DuelRandom;
            double now = World.Time, dist = Dist(CenterX, CenterY, t.CenterX, t.CenterY), v = Math.Max(1.0, RealMoveSpeed);
            int R = AttackRadius;
            double P = R - ZDuel.Margin(Type), dMin = P - ZDuel.ReverseBand, dMax = R - ZDuel.CloseBand;
            int r1 = rng.Next(2), r2 = rng.Next(100), r3 = rng.Next(100), r4 = rng.Next(101);   // fixed draw count per choice
            if (d.sign == 0) d.sign = r1 == 0 ? 1 : -1;
            int s0 = d.sign;
            if (d.sameSign >= ZDuel.FlipAfter && r2 < ZDuel.FlipPct) s0 = -s0;
            double dur = ZDuel.CommitMin + (ZDuel.CommitMax - ZDuel.CommitMin) * r4 / 100.0;
            bool roomBack = (dMax - dist) / (ZDuel.ReverseRoom * v) >= ZDuel.CommitMin;   // room to back up 1 s even if the target backs away too
            var list = new (ZDuelKind k, int s)[5];
            int n = 0;
            if (dist > dMax) { list[n++] = (ZDuelKind.Close, s0); list[n++] = (ZDuelKind.Close, -s0); list[n++] = (ZDuelKind.Arc, s0); list[n++] = (ZDuelKind.Arc, -s0); }
            else if (dist < dMin) { if (roomBack) list[n++] = (ZDuelKind.Reverse, s0); list[n++] = (ZDuelKind.Arc, s0); list[n++] = (ZDuelKind.Arc, -s0); list[n++] = (ZDuelKind.Flank, s0); }
            else
            {
                var w = ZDuel.Weights(Type);
                var k = r3 < w.arc ? ZDuelKind.Arc : r3 < w.arc + w.flank ? ZDuelKind.Flank : ZDuelKind.Reverse;
                if (k == ZDuelKind.Reverse && (d.lastKind == ZDuelKind.Reverse || dist > P + 6 || !roomBack)) k = ZDuelKind.Arc;
                list[n++] = (k, s0);
                if (k != ZDuelKind.Arc) list[n++] = (ZDuelKind.Arc, s0);
                list[n++] = (ZDuelKind.Arc, -s0); list[n++] = (ZDuelKind.Flank, -s0);
            }
            for (int i = 0; i < n; i++)
            {
                var (k, s) = list[i];
                d.kind = k; d.sign = s;
                if (k == ZDuelKind.Flank)
                {                                                                            // chord: the tangent turned FlankInDeg toward the target
                    Radial(t, out double rx, out double ry);
                    double tx = -ry * s, ty = rx * s, a = ZDuel.FlankInDeg * Math.PI / 180;
                    d.fx = tx * Math.Cos(a) - rx * Math.Sin(a); d.fy = ty * Math.Cos(a) - ry * Math.Sin(a);
                }
                if (DuelAim(d, t, agro, Math.Max(ZDuel.LookPx, (int)Math.Min(v * ZDuel.CommitMin, 64))) != DuelEnd.Expired) continue;
                double len = k == ZDuelKind.Reverse ? Math.Max(ZDuel.CommitMin, Math.Min(dur, (dMax - dist) / (ZDuel.ReverseRoom * v))) : dur;
                if (d.lastSign != 0 && s != d.lastSign) { d.SignFlips++; d.sameSign = 1; } else d.sameSign++;
                d.lastSign = s;
                d.active = true; d.targetId = t.Id; d.start = now; d.until = now + len; d.seq++; d.Manoeuvres++; d.ByKind[(int)k]++;
                d.movedX = PosX; d.movedY = PosY; d.movedAt = now;
                cur.pfPoints = null; cur.pathFindingId = 0; cur.gotPfResponse = true;       // the approach path is spent: a later chase re-plans from here
                DuelAim(d, t, agro, ZDuel.LookPx);                                           // steer at the normal look distance
                return true;
            }
            d.kind = ZDuelKind.None;
            return false;
        }

        void Radial(ZObject t, out double rx, out double ry)
        {
            rx = CenterX - t.CenterX; ry = CenterY - t.CenterY;
            double l = Math.Sqrt(rx * rx + ry * ry);
            if (l < 1e-6) { rx = 1; ry = 0; } else { rx /= l; ry /= l; }
        }

        /// <summary>Steer the current manoeuvre: heading from its kind, the first valid of 0 / +-15 ... +-60 deg, then SetTarget +
        /// SetVelocity (Zod's movement does the rest). Returns Expired when steering is fine, else why the manoeuvre must end.</summary>
        DuelEnd DuelAim(ZDuelState d, ZObject t, bool agro, int look)
        {
            double dist = Dist(CenterX, CenterY, t.CenterX, t.CenterY);
            int R = AttackRadius;
            double P = R - ZDuel.Margin(Type), dMax = R - ZDuel.CloseBand;
            if (d.kind == ZDuelKind.Reverse)
            {                                                                                // backs up to the edge of the band, never out of range
                if (dist >= dMax - 2) return DuelEnd.Range;
                look = Math.Max(6, Math.Min(look, (int)(dMax - dist)));
            }
            Radial(t, out double rx, out double ry);
            double tx = -ry * d.sign, ty = rx * d.sign, hx, hy;
            switch (d.kind)
            {
                case ZDuelKind.Reverse: hx = rx; hy = ry; break;
                case ZDuelKind.Flank: hx = d.fx; hy = d.fy; break;
                case ZDuelKind.Close when dist > P: hx = tx * 0.55 - rx * 0.85; hy = ty * 0.55 - ry * 0.85; break;
                default:
                {
                    double k = Math.Max(-0.7, Math.Min(0.7, -(dist - P) / 24.0));          // spiral toward the preferred range
                    hx = tx + rx * k; hy = ty + ry * k; break;
                }
            }
            double hl = Math.Sqrt(hx * hx + hy * hy); hx /= hl; hy /= hl;
            foreach (double deg in RotTry)
            {
                double a = deg * Math.PI / 180 * d.sign, c = Math.Cos(a), s = Math.Sin(a);
                double ux = hx * c - hy * s, uy = hx * s + hy * c;
                if (!DuelHeadingOk(t, ux, uy, look, agro, out int ex, out int ey)) continue;
                if (look > ZDuel.LookPx)
                {                                                                            // (choice test: steer at the normal distance)
                    double cx = PosX + (WidthPix >> 1), cy = PosY + (HeightPix >> 1);
                    ex = (int)Math.Round(cx + ux * ZDuel.LookPx); ey = (int)Math.Round(cy + uy * ZDuel.LookPx);
                }
                SetTarget(ex, ey); SetVelocity();
                d.nextAim = World.Time + ZDuel.AimInterval;
                return DuelEnd.Expired;
            }
            return DuelEnd.Blocked;
        }

        /// <summary>Headings tried around the manoeuvre's own (deg, x the arc sign): a wall or a unit bends the manoeuvre
        /// (it slides along) before it ends it.</summary>
        static readonly double[] RotTry = { 0, 15, -15, 30, -30, 45, -45, 60, -60 };

        /// <summary>A heading is valid when the unit's box swept `look` px along it stays on free vehicle tiles (water, cliffs,
        /// rocks, buildings and the map edge are walls) and out of bridge / lane keep-clear areas, and the end point keeps the
        /// target in range with a clear line of fire, does not close on another unit and (AGRO) stays in Zod's agro leash.</summary>
        bool DuelHeadingOk(ZObject t, double ux, double uy, int look, bool agro, out int ex, out int ey)
        {
            double cx = PosX + (WidthPix >> 1), cy = PosY + (HeightPix >> 1);
            ex = (int)Math.Round(cx + ux * look); ey = (int)Math.Round(cy + uy * look);
            var lanes = World.Pathing != null && World.Pathing.Enabled ? World.Pathing : null;
            for (int s = 4; ; s += 4)
            {
                if (s > look) s = look;
                double px = cx + ux * s, py = cy + uy * s;
                if (!DuelBoxFree((int)Math.Floor(px) - (WidthPix >> 1), (int)Math.Floor(py) - (HeightPix >> 1))) return false;
                if (lanes != null && lanes.DuelLaneAt((int)px, (int)py)) return false;
                if (s >= look) break;
            }
            double de = Dist(ex, ey, t.CenterX, t.CenterY);
            if (de > AttackRadius - ZDuel.EndSlack) return false;
            if (de < ZPathing.MinSpacing(this, t) + 8) return false;
            if (!t.IsDestroyableImpass && !t.ShuttleAirborne && World.EngageBarrierBetween(ex, ey, t.CenterX, t.CenterY)) return false;   // DV118: not for the flying shuttle
            if (LosActive && !World.Sight.ClearFrom(ex, ey, 0, t)) return false;          // DV23: the end point keeps a clear line
            if (agro)
            {
                double lim = World.S.agro_distance + ZDuel.AgroLeash, dn = Dist(CenterX, CenterY, cur.agroX, cur.agroY), dl = Dist(ex, ey, cur.agroX, cur.agroY);
                if (dl > lim && dl > dn) return false;
            }
            foreach (var o in World.Objects)
            {
                if (o == this || o == t || !o.IsUnit || o.Destroyed || o.KillAt >= 0) continue;
                if (Math.Abs(o.CenterX - ex) > 64 || Math.Abs(o.CenterY - ey) > 64) continue;
                double need = ZPathing.MinSpacing(this, o) + 4, dnow = Dist(CenterX, CenterY, o.CenterX, o.CenterY), dend = Dist(ex, ey, o.CenterX, o.CenterY);
                if (dend < need && dend < dnow) return false;
            }
            return true;
        }

        /// <summary>ZReach.BoxFree on the vehicle grid with rocks (box shrunk by 1 px like Zod's WithinImpassable, on the map).</summary>
        bool DuelBoxFree(int x, int y)
        {
            var pf = World.Path;
            int w = WidthPix - 2, h = HeightPix - 2; x += 1; y += 1;
            if (x < 0 || y < 0 || x + w >= pf.WidthPix || y + h >= pf.HeightPix) return false;
            for (int j = y >> 4; j <= (y + h - 1) >> 4; j++)
                for (int i = x >> 4; i <= (x + w - 1) >> 4; i++)
                    if (!pf.TilePassableGrid(i, j, false, false)) return false;
            return true;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>OpenZ deviation DV9 (vehicle duel manoeuvres, ZObject.Duel.cs). OFF = Zod exact (the attacker stops and fires).
        /// Static like SnipeDeviation / FortGunDeviation; ZGame sets it at every start (PlayerPrefs oz_duel_sim, oz_zod_exact).</summary>
        public static bool DuelDeviation = true;
        /// <summary>The duel layer's own seeded stream (ZWorld.Create: from the world seed), so Zod's rand() stream (Rng) is never
        /// drawn by it.</summary>
        public ZRng DuelRng;
        internal ZRng DuelRandom => DuelRng ??= new ZRng(104729);
    }

    public sealed partial class ZPathing
    {
        /// <summary>DV9: the point lies in a bridge / one-lane passage keep-clear area (no duel manoeuvres there).</summary>
        internal bool DuelLaneAt(int x, int y) => Lanes.Count > 0 && KeepClearAt(x, y) != null;
    }
}
