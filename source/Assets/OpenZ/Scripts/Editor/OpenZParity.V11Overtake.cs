#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Globalization;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV105 "faster units pass slower ones" (version 1.1; the rule: Sim/ZOvertake.cs). Written from the
    /// live constants and measured by the kit's own counted check (OzV11OvertakeCount, the menu OpenZ/V1.1/Count Faster Units
    /// Passing (f40)), run with the game's switches when the block is written; the check sets the rule's switch itself and puts
    /// it back, and the block itself is collected with the rule off (CollectDeviations), so the other rows run without it.</summary>
    public static partial class OpenZParity
    {
        static void AddOvertakeDeviation(List<Deviation> list, Func<string, int, int> getInt)
        {
            string measured; bool pass;
            try
            {
                string report;
                using (OzV11Kit.GameSwitches()) report = OzV11OvertakeCount.Run(SettingsText, MapText);
                measured = OzV11Kit.VerdictCell(report, out pass);
            }
            catch (Exception ex) { measured = "the check threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            bool exact = getInt("oz_zod_exact", 0) != 0, row = getInt("oz_overtake", 0) == 1, on = !exact && row;
            var inv = CultureInfo.InvariantCulture;
            list.Add(new Deviation
            {
                id = "DV105", change = "Faster units pass slower ones (the Options row FAST UNITS PASS SLOW ONES)",
                zod = "units have no collision and drive through each other, so every unit of a mixed group travels at its own speed and the group arrives in the order of its speeds; nothing waits for anything",
                openz = "under the spacing layer (DV6) a unit queued behind any unit going its way, so a column moved at the pace of its slowest vehicle in front. With the rule on: " +
                        $"(1) a unit on a plain move that is queued behind a unit of its team going the same way goes round it, with the gate's own side-step (until now only for standing units), when its own speed on the ground beside is at least {ZPathing.PassRatio.ToString(inv)} times " +
                        $"what the slow unit does and the strip beside it (its own box, {ZPathing.PassMargin.ToString(inv)} px clear of the spacing) is free ground with no bridge, passage, building margin or door way, no unit standing or coming the other way: for the whole pass where that strip is slower ground " +
                        $"than its own line (the side of a road; at most {ZPathing.PassLookMax.ToString(inv)} px), else {ZPathing.PassLookOpen.ToString(inv)} px ahead. The spacing is kept all the time, nobody is pushed, nobody is faster than its own speed; a spacing ahead of the slow unit it drives back onto the line it left; " +
                        "a squad's members go round with their leader, on his side; a pass that gains nothing for " + ZPathing.PassStall.ToString(inv) + " s ends and the unit falls in behind the slow one: it stands for that, " + ZPathing.FallInMax.ToString(inv) +
                        " s at most, never while a unit of its team is held up by it, and is not taken for a jam by the layer's last resort meanwhile. Nobody is made to stand for somebody else. " +
                        $"(2) Never: in or at a bridge, a water crossing, a fort gate or a passage (their queues are untouched); within {ZPathing.PassMinLeft.ToString(inv)} px of the end of the way; under an attack order; on a group order whose clicked point has an enemy unit, gun or building within " +
                        $"{ZPathing.ContactRange.ToString(inv)} px; while an enemy is that near; a unit marked KeepTogether (the mark for units that another rule marches as one body). Why: a player found armies much slower than in the original; counted, jeeps behind a heavy tank arrived about 2 times later than alone (Sim/ZOvertake.cs)",
                measured = "OpenZ/V1.1/Count Faster Units Passing (f40): " + measured,
                toggle = $"ZWorld.OvertakeDeviation: OFF BY DEFAULT; the game turns it on at a match start only when the Options row FAST UNITS PASS SLOW ONES (ADVANCED tab) is on (oz_overtake = {(row ? "1" : "0 (default)")}) and not in 'Zod exact' (oz_zod_exact = {(exact ? "1" : "0")}); the parity report, this block and every counted check turn it off for their own runs, so the other rows run without it",
                status = !pass ? DevCheck : on ? DevActive : DevActive + " (off by pref)",
            });
        }
    }
}
#endif
