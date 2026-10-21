#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// top of DV118; the view: View/SelectionOverlay.ShuttleQueue.cs, View/OzShuttleQueueInput.cs). Measured headless: the counted check of
    /// Editor/OzShuttleQueueCount.cs with <see cref="OzShuttleQueueCount.RowSeeds"/> seeds a case (routes of 2, 3 and 5 points, one ship and
    /// three crossing, own and enemy; a Shift point added in flight, a route replaced, EXIT in the air, a last point it cannot land on, the
    /// switch off, a CPU team, determinism, level 16 over water).</summary>
    public static partial class OpenZParity
    {
        static void AddShuttleQueueDeviation(List<Deviation> list)
        {
            string measured; bool pass;
            bool was = ZWorld.ShuttlePlayableDeviation, wasOn = ZWorld.ShuttleOn, wasQ = ZWorld.ShuttleQueueDeviation;
            try
            {
                string check;
                using (OzV11Kit.GameSwitches()) check = OzShuttleQueueCount.Check(SettingsText, MapText("L16"), OzShuttleQueueCount.RowSeeds);
                measured = OzV11Kit.VerdictCell(check, out pass) + " (the whole report: menu OpenZ/V1.1/Shuttle: K410 Route Check)";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            finally { ZWorld.ShuttlePlayableDeviation = was; ZWorld.ShuttleOn = wasOn; ZWorld.ShuttleQueueDeviation = wasQ; }
            list.Add(new Deviation
            {
                id = "DV125", change = "The shuttle flies the whole Shift queue: through the middle points in the air, landing only at the last (as every vehicle follows its waypoint list)",
                zod = "no shuttle in Zod (DV118 is an OpenZ unit); DV118 alone gave the ship only the FIRST move of a list, so a queued route was dropped",
                openz = "a list of two or more move points (SEND_WAYPOINTS, as for every unit; the list replaces the old one): the LAST point is the goal (its landing-spot rule, the air traffic's spot, the hover when it cannot land, EXIT given in the air, a repair station: as for one point), " +
                        $"the points before it are middle points (at most {ZWorld.ShuttleViaMax}; one on the point before it is dropped): it climbs and turns toward the first, then flies through each at the cruise height without stopping, coming down or landing: " +
                        $"within its pass radius ({ZWorld.ShuttlePassMinM:0.0}-{ZWorld.ShuttlePassMaxM:0.0} m: the arc of its turn at the pass speed) the next point becomes the target, so it eases its turn through the point; " +
                        $"before a turn it slows to the pass speed (full speed straight on, half at a right angle, at least {ZWorld.ShuttlePassFloorK * 100:0} %) and keeps it through the turn; the planned stop at the goal always fits in the route left; " +
                        "a new order replaces the route; the Shift click while it flies sends the route still to fly plus the new points, so a Shift point is added at the end (View/OzShuttleQueueInput.cs); K256g's order filter tests only the last point; " +
                        "the air traffic (K338) plans each ship along its route; one point, STOP, a repair order and the CPU (refused) unchanged. Deterministic: the order's points and the ship's sim state only. " +
                        "View: the route drawing shows each middle point up in the air at the cruise height over its spot (a small ring on the ground under it, a faint drop between), the legs level between them and only the last leg coming down to the goal (View/SelectionOverlay.ShuttleQueue.cs)",
                measured = measured,
                toggle = "ZWorld.ShuttleQueueDeviation (code switch, on; off = DV118 as it was: the first move of a list only); matters only while DV118 makes shuttles (off in 'Zod exact')",
                status = pass ? DevActive : DevCheck,
            });
        }
    }
}
#endif
