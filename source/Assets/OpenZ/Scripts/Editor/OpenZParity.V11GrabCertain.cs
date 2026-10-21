#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV84 "one unit takes it, for certain" (version 1.1; the rule: Sim/ZObject.GrabCertain.cs). Written
    /// from the live constants and measured by the kit's own counted check (OzV11CaptureCount, the menu
    /// OpenZ/V1.1/Count Automatic Captures) with 5 runs per row, run with the game's switches when the block is written; the
    /// switches are put back afterwards, so the rows above (DV38, DV55 and the rules rows) still run without the rule.</summary>
    public static partial class OpenZParity
    {
        static void AddGrabCertainDeviation(List<Deviation> list, Func<string, int, int> getInt)
        {
            string measured; bool pass;
            try
            {
                string report;
                using (OzV11Kit.GameSwitches()) report = OzV11CaptureCount.Run(SettingsText, MapText, 5);
                measured = OzV11Kit.VerdictCell(report, out pass);
            }
            catch (Exception ex) { measured = "the check threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            bool on = getInt("oz_zod_exact", 0) == 0;
            list.Add(new Deviation
            {
                id = "DV84", change = "One unit takes it, for certain (automatic capture of a flag / an empty vehicle; human teams)",
                zod = "every idle robot within 220 px walks to an empty vehicle or a flag by itself, each for itself (zobject.cpp:1603-1853, zsettings.cpp:276-277); units with an order never look",
                openz = $"on top of DV38 / DV55: a unit whose own automatic capture ended without the target taken, or whose way to it (the rest of its path) got no {ZWorld.GrabProgressPx} px shorter for {ZWorld.GrabStallSeconds:0.#} s " +
                        $"(not counting the time it fights, waits for its path or waits its turn in traffic), gives it up for {ZWorld.GrabFailMemory:0} s and the next closest unit goes; a unit the player has ordered takes an empty vehicle or gun within Zod's own 220 px (was {ZWorld.CloseGrabPx}); " +
                        $"a squad leader or vehicle on the player's plain move takes a flag, or (robots) an empty vehicle, within {ZWorld.CloseGrabPx} px of its way and goes on with the order: at most one robot of a squad leaves for a vehicle per order, never for a gun, " +
                        "never a hull on a bridge approach, never on a retreat or a direct order; the CPU keeps Zod's idle grabs (Sim/ZObject.GrabCertain.cs)",
                measured = "OpenZ/V1.1/Count Automatic Captures, 5 runs per row: " + measured,
                toggle = $"ZWorld.GrabCertainDeviation: the game turns it on at every start, off in 'Zod exact' (oz_zod_exact = {(on ? "0" : "1")}); headless default off, so the rows above run without it",
                status = !pass ? DevCheck : on ? DevActive : DevActive + " (off by pref)",
            });
        }
    }
}
#endif
