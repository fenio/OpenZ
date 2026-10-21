#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Globalization;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV102 "the run into the enemy fort" (version 1.1; the rule: Sim/ZObject.FortRun.cs, a hook in DV34's
    /// FortRushStep). Measured by the movement kit's own check (OzKC1Count.RunFort): eight red squads ordered into the blue fort from
    /// twelve tiles under fire, on the three maps, the rule off and on.</summary>
    public static partial class OpenZParity
    {
        static void AddFortRunDeviation(List<Deviation> list, Func<string, int, int> getInt)
        {
            string measured; bool pass;
            try
            {
                var inv = CultureInfo.InvariantCulture; var parts = new List<string>(); pass = true;
                foreach (var lv in OzKC1Count.Levels)
                {
                    var a = OzKC1Count.SumFort(lv, 1, false); var b = OzKC1Count.SumFort(lv, 1, true);
                    bool p = OzKC1Count.FortPass(a, b); pass &= p;
                    parts.Add($"{lv} {a.Capped.ToString("0.0", inv)} -> {b.Capped.ToString("0.0", inv)} s, near the door {a.RevNear} -> {b.RevNear}, whole run {a.Rev} -> {b.Rev}{(p ? "" : " (worse)")}");
                }
                measured = (pass ? "PASS" : "FAIL") + ": eight squads into the enemy fort from 12 tiles under fire, time to the fall (none in 120 s = 120 s) and heading reversals off -> on: " + string.Join("; ", parts) +
                           " (the verdict: on every map no later, 0 reversals within 6 tiles of the door, no more reversals in the whole run)";
            }
            catch (Exception ex) { measured = "the check threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            bool exact = getInt("oz_zod_exact", 0) != 0;
            list.Add(new Deviation
            {
                id = "DV102", change = "The run into the enemy fort: a human player's robots ordered through the door go straight in, without dodging or giving way to their own team, from 16 tiles along their way",
                zod = "an ENTER_FORT robot walks to the gate's exit point and inside; it dodges shells on the way and has no collision with other units",
                openz = $"DV34 lets a robot on an ENTER_FORT order run at Zod's running speed, through other units (no spacing, no give-way), without stepping aside or turning off to fight, within {ZObject.FortRushRadius} px of the gate's exit point. " +
                        $"With DV102, for every robot of a HUMAN team whose way left along its path to that point is at most {ZObject.FortRunRadius} px, outside DV34's ring: units of its own team pass through it and it through them (no spacing, queue, side-step or give-way between them), it never steps aside from a shell and is never sent out of a vehicle's way; towards enemies everything stays (spacing, being run over); " +
                        "in both rings it runs straight at the farthest point of its way whose straight line is clear (Zod's DirectPathPossible, no water beside or under it, not slower than the path), so it keeps its path round water and walls and does not turn back and forth before the door. The sprint stays DV34's 7 tiles. Computer teams keep DV34 alone. " +
                        "Why: robots ordered into the enemy fort stumbled round who goes first under fire (Sim/ZObject.FortRun.cs)",
                measured = "OpenZ/V1.1/Count Movement Kit (KC1), C1: " + measured,
                toggle = $"ZWorld.FortRunDeviation: the game turns it on at every start unless 'Zod exact' (oz_zod_exact = {(exact ? "1" : "0")}); it acts only with DV34 on; no Options row; the parity report, the Deviations block and every counted check turn it off for their own runs",
                status = !pass ? DevCheck : DevActive,
            });
        }
    }
}
#endif
