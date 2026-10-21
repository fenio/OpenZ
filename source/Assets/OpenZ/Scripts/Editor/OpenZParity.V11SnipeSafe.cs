#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV82 "no driver sniping on a bridge or in a door" (version 1.1; the rule: Sim/ZWorld.SnipeSafe.cs).
    /// Written from the live constants and measured by the kit's own counted check (OzV11SnipeCount, the menu
    /// OpenZ/V1.1/Count Snipes On Bridges And In Doors) with 5 runs per spot, run with the game's switches when the block is
    /// written; the switches are put back afterwards, so the rows above still run without the rule.</summary>
    public static partial class OpenZParity
    {
        static void AddSnipeSafeDeviation(List<Deviation> list, Func<string, int, int> getInt)
        {
            string measured; bool pass;
            try
            {
                string report;
                using (OzV11Kit.GameSwitches()) report = OzV11SnipeCount.Run(SettingsText, 5);
                measured = OzV11Kit.VerdictCell(report, out pass);
            }
            catch (Exception ex) { measured = "the check threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            bool on = getInt("oz_zod_exact", 0) == 0;
            list.Add(new Deviation
            {
                id = "DV82", change = "No driver sniping on a bridge or in the door of a fort or factory",
                zod = "a hit of a sniping weapon on a vehicle whose driver can be hit is a driver hit when one roll passes attack_snipe_chance, wherever the vehicle stands (zobject.cpp:2187-2199, zvehicle.cpp:178-186); the emptied vehicle stays where it is (zobject.cpp:334-359), which blocks nobody because units have no collision",
                openz = $"a vehicle on a standing bridge (the deck and {ZWorld.SnipeSafeBridgeReach} px before each end, the bridge's own width: the area the pathing layer keeps clear for the deck's lane) or in the exit lane of a fort, a robot factory or a vehicle factory " +
                        $"(DV66's lane: from the point a new unit appears on through the exit point and {ZWorld.ExitLaneReach} px on, {ZWorld.ExitLaneHalf} px + the chassis radius either side) cannot lose its driver: " +
                        "the snipe roll is drawn as before and a passed roll hits the hull instead, with the hull damage and the sound of any hit; everywhere else, and for guns, sniping is unchanged (Sim/ZWorld.SnipeSafe.cs, Sim/ZObject.cs ProcessAttackDamage)",
                measured = "OpenZ/V1.1/Count Snipes On Bridges And In Doors, 5 runs per spot: " + measured,
                toggle = $"ZWorld.SnipeSafeSpotDeviation: the game turns it on at every start, off in 'Zod exact' (oz_zod_exact = {(on ? "0" : "1")}); headless default off, so the rows above run without it",
                status = !pass ? DevCheck : on ? DevActive : DevActive + " (off by pref)",
            });
        }
    }
}
#endif
