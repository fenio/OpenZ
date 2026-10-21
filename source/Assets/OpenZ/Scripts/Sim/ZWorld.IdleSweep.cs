using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// scanned once. A squad / vehicle is FORGOTTEN when it has no order and no target, is not fighting, guarding (hold / patrol) or a
    /// fort defender, has moved less than IdleSweepStillPx (3 m) in the last IdleSweepStillSec (90) s, and stands within IdleSweepNearPx
    /// (20 m) of one of its team's factories or its fort. Only when at least IdleSweepMinCount (2) are forgotten does each of them get ONE
    /// attack-move order toward the enemy fort; nothing else changes until the next check. Human teams are never touched.
    /// Deterministic: sim time, integer positions, the object list order; no RNG draw.
    /// </summary>
    public sealed partial class ZWorld
    {
        public static bool IdleSweepDeviation = true;
        public static double IdleSweepEvery = 420.0, IdleSweepStillSec = 90.0;
        public static int IdleSweepStillPx = 24, IdleSweepNearPx = 160, IdleSweepMinCount = 2;
        /// <summary>Statistics and one line per check ("CPU idle sweep: N forgotten, M ordered") for checks / proofs.</summary>
        public int IdleSweepChecks, IdleSweepOrdered;
        public readonly List<string> IdleSweepLog = new List<string>();
        double nextIdleSweep = -1, nextStillSample;
        readonly Dictionary<int, (int x, int y, double t)> stillSince = new Dictionary<int, (int x, int y, double t)>();

        void IdleSweepTick()
        {
            if (!IdleSweepDeviation || Bots.Count == 0) return;
            if (Time >= nextStillSample)
            {   // once a second: where each CPU squad / vehicle stood still since (an anchor that moves when the unit moves > 3 m)
                nextStillSample = Time + 1.0;
                foreach (var o in Objects)
                {
                    if (!o.IsMobile || o.IsMinion || o.Destroyed || o.Owner == Team.Neutral || IsHumanTeam(o.Owner)) continue;
                    if (!stillSince.TryGetValue(o.Id, out var a) || Math.Abs(o.CenterX - a.x) > IdleSweepStillPx || Math.Abs(o.CenterY - a.y) > IdleSweepStillPx)
                        stillSince[o.Id] = (o.CenterX, o.CenterY, Time);
                }
            }
            if (nextIdleSweep < 0) nextIdleSweep = IdleSweepEvery * IdleSweepSlow();       // K434: x 1.3 while an Easy computer plays (Sim/ZBot.EasySlow.cs)
            if (Time < nextIdleSweep) return;
            nextIdleSweep += IdleSweepEvery * IdleSweepSlow();
            foreach (var bot in Bots) if (!bot.Ignored) IdleSweep(bot.Team);
        }

        void IdleSweep(Team team)
        {
            IdleSweepChecks++;
            ZObject target = null;
            foreach (var f in Objects) if (f.IsFort && !f.Destroyed && f.Owner != team && f.Owner != Team.Neutral) { target = f; break; }
            var forgotten = new List<ZObject>();
            foreach (var o in Objects)
            {
                if (o.Owner != team || !o.IsMobile || o.IsMinion || o.Destroyed || o.KillAt >= 0 || o.MoveSpeed <= 0) continue;
                if (o.Waypoints.Count > 0 || o.AttackObject != null || o.HoldPosition || o.Patrolling || o.IsFortDefender) continue;
                if (o.StrikeHeld) continue;                                                     // DV107: a member of a strike group is not forgotten, it waits for its group
                if (!stillSince.TryGetValue(o.Id, out var a) || Time - a.t < IdleSweepStillSec * IdleSweepSlow(team)) continue;   // K434: 117 s on Easy
                bool nearHome = false;
                foreach (var b in Objects)
                {
                    if (b.Owner != team || b.Destroyed || b.Kind != ZKind.Building || !(b.IsProducer || b.IsFort)) continue;
                    long ex = Math.Max(0, Math.Max(b.LocX - o.CenterX, o.CenterX - (b.LocX + b.WidthPix)));
                    long ey = Math.Max(0, Math.Max(b.LocY - o.CenterY, o.CenterY - (b.LocY + b.HeightPix)));
                    if (ex * ex + ey * ey <= (long)IdleSweepNearPx * IdleSweepNearPx) { nearHome = true; break; }
                }
                if (nearHome) forgotten.Add(o);
            }
            int ordered = 0;
            if (target != null && forgotten.Count >= IdleSweepMinCount)
                foreach (var o in forgotten)
                    if (SendWaypoints(team, o, new List<ZWaypoint> { ZWaypoint.Move(target.CenterX, target.CenterY, true) })) { ordered++; stillSince.Remove(o.Id); }
            IdleSweepOrdered += ordered;
            string line = $"{Time:F0} s {team}: CPU idle sweep: {forgotten.Count} forgotten, {ordered} ordered";
            if (IdleSweepLog.Count >= 50) IdleSweepLog.RemoveAt(0);
            IdleSweepLog.Add(line);
        }
    }
}
