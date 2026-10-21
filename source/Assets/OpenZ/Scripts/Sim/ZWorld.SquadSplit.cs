using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV98 (a squad member left far behind becomes a squad of his own). A robot squad whose member stands farther
    /// than <see cref="SquadSplitPx"/> from every other member of his squad (about one screen at the standard camera) is split:
    /// that member leaves the squad and is a single robot from then on, with his own orders, health and grenades; the others stay one
    /// squad. When the one left alone is the leader, the first member of the others leads them (the hand-over the rules make when a
    /// leader dies). Nothing is created or removed: every count of robots stays the same; the number of squads grows by one.
    /// Checked once a second of game time; at most one split per squad and check. Zod has no such rule (a squad stays one squad
    /// until its members die), so a member that was held back could stretch the squad's frame across the whole map.
    /// Static switch like the other deviations (off = Zod).
    /// </summary>
    public sealed partial class ZWorld
    {
        /// <summary>DV98 switch (off = Zod: a squad stays together whatever the distance).</summary>
        public static bool SquadSplitDeviation = true;
        /// <summary>The distance (px, 8 px a metre) to the nearest other member over which a member becomes a squad of his own: 640 px
        /// = 80 m = 40 tiles, about the width of the screen at the standard camera.</summary>
        public static int SquadSplitPx = 640;
        /// <summary>Counter for the checks: squads split in this world.</summary>
        public int SquadSplits;
        double nextSquadSplitCheck;

        void SquadSplitTick()
        {
            if (!SquadSplitDeviation || Time < nextSquadSplitCheck) return;
            nextSquadSplitCheck = Time + 1.0;
            for (int i = 0; i < Objects.Count; i++)
            {
                var l = Objects[i];
                if (l.Kind != ZKind.Robot || l.Leader != null || l.Minions.Count == 0 || l.Destroyed || l.KillAt >= 0) continue;
                var alone = FarMember(l);
                if (alone != null) SplitFromSquad(l, alone);
            }
        }

        /// <summary>The member of the squad led by l (l included) whose nearest live squad mate is farther than SquadSplitPx; null = none.
        /// Members that are dead or being removed do not count.</summary>
        ZObject FarMember(ZObject l)
        {
            long lim = (long)SquadSplitPx * SquadSplitPx;
            int live = (l.Destroyed || l.KillAt >= 0) ? 0 : 1;
            foreach (var m in l.Minions) if (m != null && !m.Destroyed && m.KillAt < 0) live++;
            if (live < 2) return null;
            for (int a = -1; a < l.Minions.Count; a++)
            {
                var m = a < 0 ? l : l.Minions[a];
                if (m == null || m.Destroyed || m.KillAt >= 0) continue;
                long best = long.MaxValue;
                for (int b = -1; b < l.Minions.Count; b++)
                {
                    var n = b < 0 ? l : l.Minions[b];
                    if (n == null || n == m || n.Destroyed || n.KillAt >= 0) continue;
                    long dx = m.CenterX - n.CenterX, dy = m.CenterY - n.CenterY, d = dx * dx + dy * dy;
                    if (d < best) best = d;
                }
                if (best != long.MaxValue && best > lim) return m;
            }
            return null;
        }

        /// <summary>m leaves the squad led by l. A minion simply leaves; the leader leaves his squad to its first other member (the
        /// rules' own hand-over on a leader's death, ZWorld.RemoveObjectFromGroup), keeping his own grenades.</summary>
        void SplitFromSquad(ZObject l, ZObject m)
        {
            if (m != l)
            {
                l.Minions.Remove(m); m.Leader = null; m.MemberIndex = 0;
            }
            else
            {
                ZObject nl = null;
                foreach (var x in l.Minions) if (x != null && !x.Destroyed && x.KillAt < 0) { nl = x; break; }
                if (nl == null) return;
                nl.Leader = null;
                foreach (var x in l.Minions) if (x != null && x != nl) { x.Leader = nl; nl.Minions.Add(x); }
                l.Minions.Clear();
            }
            SquadSplits++;
        }
    }
}
