#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Globalization;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV90 "a group order at or through a zone to 8 or more selected units: the members' first path
    /// searches are spread over ticks" (the rule: Sim/ZPathV3.cs SpreadGroupV3 and Sim/ZCommit.cs DeferPath). Measured headless on
    /// the Desert with the mass-order check's own group (OzV11MassOrderCount): 40 units ordered to the near end of a bridge with
    /// the rule on (twice: the replay) and off, and the same order to a group one unit under the limit with the rule on and off
    /// (the end states must be identical: a small order behaves exactly as before). Only counted numbers are written, no times.</summary>
    public static partial class OpenZParity
    {
        static void AddOrderSpreadDeviation(List<Deviation> list)
        {
            string measured; bool pass; bool was = ZPathing.LiveOrderSpread;
            try
            {
                var inv = CultureInfo.InvariantCulture;
                var lv = OzV11MassOrderCount.Levels[1]; var goal = OzV11MassOrderCount.ZoneOrders[1].at; string map = MapText(lv.level);
                OzV11MassOrderCount.Row Run(bool spread, int n) { ZPathing.LiveOrderSpread = spread; return OzV11MassOrderCount.One(SettingsText, map, lv.level, lv.name, n, "at a zone", lv.ax, lv.ay, goal, 290); }
                var on = Run(true, 40); var again = Run(true, 40); var off = Run(false, 40);
                int few = ZPathing.LiveOrderSpreadMin - 1;
                var smallOn = Run(true, few); var smallOff = Run(false, few);
                bool replay = on.checksum == again.checksum, small = smallOn.checksum == smallOff.checksum && smallOn.live && smallOn.firstTickSearches == smallOff.firstTickSearches;
                pass = on.live && off.live && on.maxStarts <= ZPathing.SearchCap && on.firstTickSearches < off.firstTickSearches && replay && small && on.notStarted <= off.notStarted;   // a body whose spot is where it stands never leaves it, with the rule on or off
                measured = $"{lv.name}, {on.units} units ({on.bodies} bodies) ordered to the near end of a bridge, live rules {(on.live ? "yes" : "NO")}: path searches in the tick after the order {on.firstTickSearches} (rule off: {off.firstTickSearches}); " +
                           $"most path requests started in one tick {on.maxStarts} (cap {ZPathing.SearchCap}); longest wait in the queue {on.queueWait.ToString("0.00", inv)} s; the last body under way {on.lastStart.ToString("0.00", inv)} s after the order " +
                           $"(rule off: {off.lastStart.ToString("0.00", inv)} s); bodies that never left their place {on.notStarted} (rule off: {off.notStarted}); replay {(replay ? "identical" : "DIFFERENT")}; " +
                           $"a group of {smallOn.units} units ({few} is under the limit), the same order with the rule on and off: {smallOn.firstTickSearches} and {smallOff.firstTickSearches} searches in the first tick, end state after 3 s {(smallOn.checksum == smallOff.checksum ? "identical" : "DIFFERENT")}";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            finally { ZPathing.LiveOrderSpread = was; }
            list.Add(new Deviation
            {
                id = "DV90", change = "A group order at or through a zone to 8 or more selected units: the members' first path searches are spread over ticks",
                zod = "no such rule: every unit of a group order asks for its path when its order arrives (the searches run beside the server loop, in the path thread)",
                openz = $"ZPathV3.cs SpreadGroupV3 and ZCommit.cs DeferPath: when one move order to {ZPathing.LiveOrderSpreadMin} or more selected units (ZPathing.LiveOrderSpreadMin) runs the live rules (the clicked point lies at a bridge, a water " +
                        "crossing, a gate or a passage, or the way goes through one), the FIRST path request of every member waits its turn in the path queue that the orders with own spots already use (DV44): at most " +
                        $"{ZPathing.SearchCap} requests (ZPathing.SearchCap) and {ZPathing.WorkBudget} work units are started per 10 ms tick, oldest first, in the fixed order in which the members asked (the rules' own update order; no random " +
                        "draw, no clock). A waiting unit stands, as a unit waiting for its path does. Every later request of the same units, every order to fewer selected units and every order with own spots are untouched. " +
                        "Why: one order to a huge selection ran 60 to 140 path searches in one tick, a visible stall",
                measured = measured,
                toggle = "ZPathing.LiveOrderSpread: on in every game; it acts only inside DV44's group orders, so 'Zod exact' and the DV44 switch turn it off with them",
                status = pass ? DevActive : DevCheck,
            });
        }
    }
}
#endif
