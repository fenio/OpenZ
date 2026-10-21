#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV115 "the CPU builds a mix of units" (the rule: Sim/ZBot.HeavyMix.cs). Measured headless: the counted
    /// check of Editor/OzV11BotHeavyCount.cs (the picks of single producers unit after unit, switch off and on, and one whole level 16
    /// computer match with the counters checked against the units built). The 20-minute tables before / after: menu
    /// OpenZ/V1.1/CPU Mix: DV115 Table.</summary>
    public static partial class OpenZParity
    {
        static void AddBotHeavyDeviation(List<Deviation> list)
        {
            string measured; bool pass;
            bool was = ZWorld.BotHeavyDeviation;
            try
            {
                string check;
                using (OzV11Kit.GameSwitches()) check = OzV11BotHeavyCount.Check(SettingsText, MapText("L16"), MapText("L17"));
                measured = OzV11Kit.VerdictCell(check, out pass) + " (the computer matches before / after: menu OpenZ/V1.1/CPU Mix: DV115 Table)";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            finally { ZWorld.BotHeavyDeviation = was; }
            list.Add(new Deviation
            {
                id = "DV115", change = "The CPU builds a mix of units: heavy tanks and missile launchers later in a game, Tough / Psycho / Sniper squads in turn",
                zod = "the bot builds the first of medium, light, jeep, pyro, tough, sniper, tough, psycho its producer can make (zbot.cpp:20-27, :1372-1432): Medium tanks and Pyros for ever where it can make them, never a heavy tank or a missile launcher",
                openz = $"after Zod's pick and the Lasers of DV71 (which win), before the Electrobot claim (DV46): from {ZBot.HEAVY_FROM / 60:0}:00 on the game clock, of every {ZBot.HEAVY_EVERY} vehicles a producer starts " +
                        "the first is a heavy tank or a mobile missile launcher, in turn (heavy first; only the one it can make; Zod's pick where it can make neither; a vehicle in the making at 8:00 is finished first); " +
                        $"from the start, every {ZBot.MIX_EVERY}nd robot squad a producer turns out (its Lasers and Electrobots not counted) is the next of {string.Join(", ", ZBot.MixRotation)} it can make; " +
                        "two counters per building (vehicles started from 8:00, robot squads), no random draw, the same STOP_BUILDING + START_BUILDING commands; Normal and Easy; no level named",
                measured = measured,
                toggle = "ZWorld.BotHeavyDeviation (code switch; ZGame: on, off in Zod exact; headless default off, this probe turns it on)",
                status = pass ? DevActive : DevCheck,
            });
        }
    }
}
#endif
