using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ
{
    /// <summary>
    /// Factory reminder (k247): a robot or vehicle factory the player CAPTURES during play goes on building what the other side chose
    /// for it (grunts, jeeps) and sends the new units to the other side's rally point. Until the player has set its production
    /// himself AND its rally point, its production badge carries a soft amber ring and a small amber "!" chip that bounce gently
    /// (OzProductionBadge.Reminder, OzFactoryReminder.View.cs), and a mouseover tip says what is still to do
    /// (OpenZUnitOverlay.FactoryReminder.cs). A click on the factory opens its build menu as always.
    ///
    /// View only: this reads the Sim (teams, types) and hears the player's own production and rally commands
    /// (OpenZProductionWindow, OpenZRallyPoints); it never writes the Sim. No engine calls in here, so the rules also run headless.
    /// Rules: Watch sees every producer every frame; the first team seen on a factory is its starting team (starting buildings never
    /// get a mark); a later change to the player's team marks it (the computer's captures never do); a change away from the player
    /// drops the mark (taken back later = a new mark). Any production change by him since the capture (produce, queue, stop, repeat,
    /// remove from the queue) counts as "production set"; any rally change by him (a point, a route, CLEAR) as "rally point set";
    /// once both are done the mark is gone for good. Forts, radar and the repair station are never marked.
    /// (UI/OzFactoryReminder.Captured.cs).
    /// </summary>
    public static partial class OzFactoryReminder
    {
        /// <summary>On in the game (no Options row; a GAMEPLAY row "FACTORY REMINDERS" could switch it later). Off = no mark, no tip.</summary>
        public static bool Enabled = true;
        public const string TipBoth = "CLICK TO SET PRODUCTION & RALLY POINT", TipProduction = "CLICK TO SET PRODUCTION", TipRally = "CLICK TO SET RALLY POINT";
        /// <summary>Checks: marks given (captures by the player), marks finished (both set), production / rally changes heard on a mark.</summary>
        public static int Marked, Finished, ProductionChanges, RallyChanges;

        sealed class Mark { public bool production, rally; }
        static readonly Dictionary<int, Team> seen = new Dictionary<int, Team>();
        static readonly Dictionary<int, Mark> marks = new Dictionary<int, Mark>();
        static System.WeakReference<ZWorld> worldOf;
        static double lastTime = double.MinValue;

        /// <summary>Robot and vehicle factories only (never a fort, the radar or the repair station).</summary>
        public static bool Eligible(ZObject b) => b != null && b.Kind == ZKind.Building && (b.Type == "robot_factory" || b.Type == "vehicle_factory");

        /// <summary>Forget everything (a new level, or the same world started again).</summary>
        public static void Reset(ZWorld w)
        {
            seen.Clear(); marks.Clear();
            worldOf = w != null ? new System.WeakReference<ZWorld>(w) : null;
            lastTime = w != null ? w.Time : double.MinValue;
        }

        /// <summary>Every frame for every producer (OpenZUnitOverlay.DrawBadges): notes captures and returns whether the mark is on.</summary>
        public static bool Watch(ZWorld w, ZObject b, Team player)
        {
            if (w == null || b == null) return false;
            if (worldOf == null || !worldOf.TryGetTarget(out var cur) || !ReferenceEquals(cur, w) || w.Time < lastTime - 1e-6) Reset(w);
            lastTime = w.Time;
            if (!Eligible(b)) return false;
            if (!seen.TryGetValue(b.Id, out var was)) { seen[b.Id] = b.Owner; return false; }       // first sight = its starting team: never a mark
            if (was != b.Owner)
            {
                seen[b.Id] = b.Owner;
                if (b.Owner == player && player != Team.Neutral) { if (TakenOnDefault(b)) { marks[b.Id] = new Mark(); Marked++; } else { marks.Remove(b.Id); KeptChoice++; } }
                else marks.Remove(b.Id);                                                          // lost again (or taken by the computer)
            }
            return Active(b, player);
        }

        /// <summary>The mark is on: his factory, standing, captured by him and not set up yet.</summary>
        public static bool Active(ZObject b, Team player) =>
            Enabled && b != null && !b.Destroyed && b.Owner == player && player != Team.Neutral && marks.ContainsKey(b.Id);

        /// <summary>The mouseover tip of a marked factory (null = no mark): what is still to do.</summary>
        public static string Tip(ZObject b, Team player)
        {
            if (!Active(b, player)) return null;
            var m = marks[b.Id];
            return !m.production && !m.rally ? TipBoth : !m.production ? TipProduction : TipRally;
        }

        /// <summary>OpenZProductionWindow: the player changed this factory's production (produce, queue, stop, repeat, remove).</summary>
        public static void ProductionSet(ZObject b)
        {
            if (b == null || !marks.TryGetValue(b.Id, out var m)) return;
            m.production = true; ProductionChanges++;
            if (m.rally) { marks.Remove(b.Id); Finished++; }
        }

        /// <summary>OpenZRallyPoints: the player set (or cleared) this factory's rally point or route.</summary>
        public static void RallySet(ZObject b)
        {
            if (b == null || !marks.TryGetValue(b.Id, out var m)) return;
            m.rally = true; RallyChanges++;
            if (m.production) { marks.Remove(b.Id); Finished++; }
        }

        /// <summary>Checks: marks on right now.</summary>
        public static int Count => marks.Count;
    }
}
