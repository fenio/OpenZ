#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV130 "a robot of the player's team under fire runs to no flag by itself" (K438; the rule: Sim/ZObject.NoAutoFlag.cs).
    /// Measured headless by the kit's counted check (Editor/OzNoAutoFlagCount.cs) with one run per row, rule off and on, the switches of a
    /// normal game; the switch is put back afterwards (the whole check, 5 runs per row: menu OpenZ/V1.1/Robots: K438 No Flag Run Under Fire Check).</summary>
    public static partial class OpenZParity
    {
        static void AddNoAutoFlagDeviation(List<Deviation> list, Func<string, int, int> getInt)
        {
            string measured; bool pass;
            try
            {
                string check = OzNoAutoFlagCount.Check(SettingsText, MapText, 1);
                measured = OzV11Kit.VerdictCell(check, out pass) + " (the whole report: menu OpenZ/V1.1/Robots: K438 No Flag Run Under Fire Check)";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            bool on = getInt("oz_zod_exact", 0) == 0;
            list.Add(new Deviation
            {
                id = "DV130", change = "A robot of the player's team under fire runs to no flag by itself; a flag the player clicked always",
                zod = "an idle robot with no enemy in its range + 40 px that it sees walks to a flag that is not its team's within 220 px by itself (zobject.cpp:1603-1853, auto_grab_flag_distance); a shooter beyond that, one it cannot see or reach, or a fight of its squad mate does not stop it",
                openz = $"a robot of a player's team whose squad is in a fight it can take (a robot of it holds an enemy as its target or walks to attack or chase one, or held one in the last {ZWorld.NoAutoFlagCalmSec:0.#} s that is still near; an enemy stands where one of them can hit it; " +
                        $"or one of them was hit by an enemy in the last {ZWorld.NoAutoFlagCalmSec:0.#} s or is aimed at while an enemy it would go and fight is near) starts no flag run of its own (the idle grab; the flag beside the way of the player's move, DV84), " +
                        "a flag run of its own under way ends at the next half-second look and the squad fights (the game's own targeting, the squad's help call DV53) or goes on with the player's order; it is no closer taker for a flag (DV55). " +
                        "Fire it cannot answer (a howitzer beyond its reach, an enemy it cannot see or reach) does not hold it. After the fight: as before. The player's orders, vehicles, empty vehicles / guns / crates and the computer's teams unchanged. Deterministic",
                measured = measured,
                toggle = $"ZWorld.NoAutoFlagUnderFireDeviation: the game turns it on at every start, off in 'Zod exact' (oz_zod_exact = {(on ? "0" : "1")}); headless default off, so the rows above run without it",
                status = !pass ? DevCheck : on ? DevActive : DevActive + " (off by pref)",
            });
        }
    }
}
#endif
