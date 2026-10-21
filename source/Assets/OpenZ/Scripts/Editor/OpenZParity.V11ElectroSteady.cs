#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV83 "the Electrobot stands and fires" (version 1.1; the rule: Sim/ZObject.ElectroSteady.cs).
    /// Written from the live constants and measured by the kit's own counted check (OzV11ElectrobotCount, the menu
    /// OpenZ/V1.1/Count Electrobot Fights) with 5 runs per row, run with the game's switches when the block is written; the
    /// switches are put back afterwards, so the Electrobot rows above (DV45, DV46) still run without the rule.</summary>
    public static partial class OpenZParity
    {
        static void AddElectroSteadyDeviation(List<Deviation> list, Func<string, int, int> getInt)
        {
            string measured; bool pass;
            try
            {
                string report;
                using (OzV11Kit.GameSwitches()) report = OzV11ElectrobotCount.Run(SettingsText, 5);
                measured = OzV11Kit.VerdictCell(report, out pass);
            }
            catch (Exception ex) { measured = "the check threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            list.Add(new Deviation
            {
                id = "DV83", change = "The Electrobot stands and fires (no dodge walks, no walk past enemies in range, no walk back)",
                zod = "(no Electrobot in Z) every robot steps aside for a shell aimed at it unless it holds a target (zobject.cpp:3657-3714), never fires or looks for targets while it moves (zobject.cpp:3519, :1615), and walks back to where an agro began when the target leaves the leash (zobject.cpp:2233-2328)",
                openz = "the Electrobot on foot never dodges; with a charged gun and an enemy he can hit inside his range he stands and fires before any walking step: an agro or help walk ends there, an order (a move, an attack on something else, entering) waits for that one bolt " +
                        $"(his windup) and goes on while he recharges, the order's own target first when it is in range; his agro never walks back; an agro target that moves away is closed to {ZWorld.ElectroStandBand} px inside his range before he stands; " +
                        "never for a fort rush, the walk out of a factory door or a stunned Electrobot; both sides, the CPU's Electrobot too (Sim/ZObject.ElectroSteady.cs)",
                measured = "OpenZ/V1.1/Count Electrobot Fights, 5 runs per row: " + measured,
                toggle = "ZWorld.ElectroSteadyDeviation: the game turns it on whenever the Electrobot is in the game (DV45: oz_electrobot = 1 and not 'Zod exact'); headless default off, so the rows above run without it",
                status = !pass ? DevCheck : getInt("oz_zod_exact", 0) == 0 && getInt("oz_electrobot", 1) == 1 ? DevActive : DevActive + " (off by pref)",
            });
        }
    }
}
#endif
