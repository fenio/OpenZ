#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV121 "the jeep runs robots over" (the rule: Sim/ZObject.JeepCharge.cs) and its counted check (menu
    /// OpenZ/V1.1/Jeep: DV121 Charge Check), measured headless with 3 seeds a row (the menu runs 10; part I, fair chances, 20): one jeep and the robots on a plain
    /// map, before (the rule off) and after (on), a Play session's other switches; the switches are put back afterwards.</summary>
    public static partial class OpenZParity
    {
        static void AddJeepChargeDeviation(List<Deviation> list)
        {
            try { list.Add(DevJeepCharge()); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV121", change = "DevJeepCharge", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
        }

        static Deviation DevJeepCharge()
        {
            string measured; bool pass;
            try
            {
                string report = OzV11JeepChargeCount.Check(SettingsText, 3);
                measured = OzV11Kit.VerdictCell(report, out pass);
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            return new Deviation
            {
                id = "DV121", change = "The jeep runs robots over: it drives at a strong robot (and at a weak one that is close) instead of a gun fight it cannot win, then back to its spot",
                zod = "a unit on an attack or a chase stops in range and fires (zobject.cpp:2296-2302, :2356-2362); Zod has no run-over at all (refs/zod_engine/src)",
                openz = $"Sim/ZObject.JeepCharge.cs: a crewed jeep whose target is an enemy robot on foot that is strong (Tough, Laser, Electrobot, Sniper, hero squads) or within {ZWorld.ChargeClosePx} px (Grunt, Psycho, Pyro) " +
                        $"drives at him and on over him at x{ZWorld.ChargeSpeedMul:0.00} speed, firing (the run-over rule DV65 knocks him down and squashes him); its own fight (such a robot in range goes first over a weak one farther off), its own chase or a call for help, an attack ordered on him or one on the way of the computer's order, " +
                        "never on the way of the player's own order; never through water, cliffs, rocks, buildings, a bridge or one-lane passage or its own units, never at a robot under an enemy gun or fort, never farther than its range + 40 px (+ a call for help's reach) from where it began; " +
                        "then the rest of the squad, then back to its spot (an order left in its list: on with it; a new order ends it at once); " +
                        $"the robot it drives at fights back: a Laser or a hero snipes its driver once it is within {ZWorld.ChargeSnipePx} px ({ZWorld.ChargeSnipeLaser:0.00} / {ZWorld.ChargeSnipeHero:0.00} of his hits, the game's own driver-shot roll), " +
                        $"a Tough stands his ground: he lets the jeep come within {ZWorld.ChargeToughHoldPx}-{ZWorld.ChargeToughHoldPx + ZWorld.ChargeToughHoldSpreadPx} px of open ground (one draw of Zod's rand() when it begins to drive at him, the rule's only draw; its way counted tile by tile, a road 1/1.689 as long: on a road he fires farther out), " +
                        $"fires at once (no DV11 aim-up), aimed from where it will be ({ZWorld.ChargeRocketScatter:0.00} of Zod's scatter), his next rocket {ZWorld.ChargeToughReload:0.00} s later (Zod's cycle 1.442 s); " +
                        "only while that jeep drives at that robot; the light tank and every other vehicle: no charge",
                measured = measured,
                toggle = "ZWorld.JeepChargeDeviation: on in the game with the run-over rule DV65 (off in 'Zod exact' and with the run-over option off, View/ZGame.cs); off by default headless, in the parity rows and in the counted checks",
                status = pass ? DevActive : DevCheck,
            };
        }
    }
}
#endif
