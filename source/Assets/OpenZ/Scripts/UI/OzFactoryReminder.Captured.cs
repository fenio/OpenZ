using OpenZ.Sim;

namespace OpenZ
{
    /// <summary>
    /// Factory reminder (k247), the production a captured factory goes on with (K429b, item 435).
    /// A capture keeps a factory's production: ZWorld.AwardZone gives the zone's buildings the new team and starts the DEFAULT production
    /// (entry 0 of the build list: grunts in a robot factory, jeeps in a vehicle factory, the same at every level, ZBuildLists) only on an
    /// ZBot.ChooseBuildOrders, STOP then START) or queued units goes on building that for the new owner, and it is not "unset" any more.
    /// The mark (the amber ring, the "!" chip, "CLICK TO SET PRODUCTION &amp; RALLY POINT") is for a factory that still runs its default
    /// production: a factory he takes while it runs a chosen production gets no mark. Decided once, on the frame Watch sees the capture.
    /// Unchanged: a factory he takes on its default production (a neutral one, an enemy one nobody set) is marked as before, a stopped
    /// one too (nothing runs: he has to choose); his starting buildings, forts, radar and the repair station never; what clears a mark;
    /// the tip texts; rally points. View only: it reads the Sim, never writes it, no engine calls (the rules also run headless).
    /// Switch CapturedChoiceCounts (on); off = every factory he takes is marked (as before).
    /// </summary>
    public static partial class OzFactoryReminder
    {
        /// <summary></summary>
        public static bool CapturedChoiceCounts = true;
        /// <summary>Checks: factories he took that got no mark because they go on with a production chosen before.</summary>
        public static int KeptChoice;

        /// <summary>The factory's default production at its level now: entry 0 of its build list (null = it builds nothing).</summary>
        public static string DefaultUnit(ZObject b)
        {
            if (b == null) return null;
            var list = ZBuildLists.For(b.Type, b.BuildLevel);
            return list.Count > 0 ? list[0] : null;
        }

        /// <summary>It runs its default production: the default unit with nothing queued (Zod's own queue rule keeps one repeat copy of
        /// it), or nothing at all (stopped: he has to choose).</summary>
        public static bool OnDefaultProduction(ZObject b)
        {
            if (b == null || b.CurUnit == null) return true;
            if (b.CurUnit != DefaultUnit(b)) return false;
            int q = b.Queue.Count;
            return q == 0 || (q == 1 && !ZWorld.FifoQueueDeviation && b.Queue[0] == b.CurUnit);
        }

        /// <summary>Watch, on a capture by him: does it get the mark?</summary>
        static bool TakenOnDefault(ZObject b) => !CapturedChoiceCounts || OnDefaultProduction(b);
    }
}
