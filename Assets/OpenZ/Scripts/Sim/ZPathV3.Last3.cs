using System;

namespace OpenZ.Sim
{
    /// <summary>
    ///  1 a following minion in single file whose turn has not come yet (closer than FileGap behind the mate ahead ALONG the leader's
    ///    trail) but that is well clear of that mate on the ground (FileGap + StartMargin) walks on at AheadFloor x the leader's pace
    ///    instead of standing. Round a corner the trail folds: the projections of two minions on its two arms come close while the
    ///    minions stand well apart on the ground. The file's car-following (gap pace) and the gate still keep it behind that mate.
    ///    Measured (headless A/B vs the previous A/B, round 2): L17 wreck gap reversals 27 -> 15 (OFF 18), stand 2.4 -> 1.9 s; small shifts in
    ///    3 L02 rows (backforth stand 0.7 -> 0.9 s = OFF), none past OFF. It does NOT change the 3.8 s stand of L17 route building
    ///    squad6 (that grunt stands in the trail pursuit further down FollowTarget: still open, see the package README).
    /// Runs only with ZWorld.PathV3Deviation on (CommitOn): off = the live pathing bit for bit. No RNG, no clock: deterministic.
    /// </summary>
    public sealed partial class ZPathing
    {
        /// <summary>Last-round fixes, one bit each (headless A/B only; the game runs the default): 1 the file minion clear of its mate walks on.</summary>
        internal static int TestLast3 = 0x1;
        internal static bool Last3Fix(int bit) => (TestLast3 & bit) != 0;
        /// <summary>Follow steps where fix 1 let a file minion walk on instead of standing.</summary>
        public int FileCornerWalks;

        /// <summary>Fix 1 (FollowTarget, single file, v3 only): the pace of a minion whose turn has not come (along &lt; 0) - at least
        /// AheadFloor x the leader's pace when the mate before it in the file (by the trail) is at least FileGap + StartMargin away on the
        /// ground; closer, it waits as before. The file's car-following and the gate (MateStepOk) still keep it off that mate. Otherwise
        /// vdes unchanged. (In the traced case that mate stood 36 px off: 28 px aside and 23 px BEHIND it along the heading.)</summary>
        internal double FileCornerPace(ZObject m, ZObject l, ZObject pred, double s, double along, double vl, double vdes)
        {
            if (!CommitOn || !Last3Fix(1) || pred == null || along >= 0 || s < 1 || vl <= GoSpeed) return vdes;
            double floor = AheadFloor * vl;
            if (vdes >= floor) return vdes;
            double ex = CX(pred) - CX(m), ey = CY(pred) - CY(m);
            double gap = FileGap(l) + StartMargin;
            if (ex * ex + ey * ey < gap * gap) return vdes;                              // (close behind it on the ground: waits as before)
            FileCornerWalks++;
            return floor;
        }
    }
}
