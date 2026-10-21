#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Globalization;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV103 "round a rock when the way round is not much longer" (version 1.1; the rule:
    /// Sim/ZPathing.RockRound.cs, hooks in ZObject.StartPath and ZPathV3's own start). Measured by the movement kit's own check
    /// (OzKC1Count.RockTable): one Tough and one light tank sent past rocks on the three maps, the rule off and on.</summary>
    public static partial class OpenZParity
    {
        static void AddRockRoundDeviation(List<Deviation> list, Func<string, int, int> getInt)
        {
            string measured; bool pass;
            try
            {
                string table = OzKC1Count.RockTable(4, "tough,light", false);
                int blOff = 0, blOn = 0, arrOff = 0, arrOn = 0;
                foreach (var line in table.Split('\n'))
                {
                    int i = line.IndexOf("the game today ", StringComparison.Ordinal); if (i < 0) continue;
                    var parts = line.Substring(i).Split(' '); blOff += int.Parse(parts[3].TrimEnd(',')); blOn += int.Parse(parts[7]);
                    int k = line.IndexOf("| arrived ", StringComparison.Ordinal); var ap = line.Substring(k + 10).Split(' '); arrOff += int.Parse(ap[0]); arrOn += int.Parse(ap[2]);
                }
                pass = blOn <= blOff && arrOn >= arrOff;
                measured = (pass ? "PASS" : "FAIL") + $": a Tough and a light tank sent past rocks, 4 pairs of points per map and kind: rocks blasted {blOff} -> {blOn}, arrived {arrOff} -> {arrOn}";
            }
            catch (Exception ex) { measured = "the check threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            bool exact = getInt("oz_zod_exact", 0) != 0;
            var inv = CultureInfo.InvariantCulture;
            list.Add(new Deviation
            {
                id = "DV103", change = "Round a rock when the way round is not much longer, instead of blasting it",
                zod = "a unit that carries explosives (Toughs, robots with grenades, tanks, the missile launcher) plans its path as if destroyable rocks were open ground and blasts every rock on that line when it bumps into it, also a rock that only clips a corner",
                openz = $"when such a unit's straight line or path runs through a destroyable rock, its way round on the normal grid is measured too: it walks or drives round when that way exists and is at most {ZPathing.RockRatio.ToString(inv)} times as long and at most {ZPathing.RockExtra.ToString(inv)} px ({(ZPathing.RockExtra / 16).ToString("0.#", inv)} tiles) longer; otherwise it blasts as in Zod. " +
                        $"The choice holds for the same goal for {ZPathing.RockKeep.ToString(inv)} s. Every team alike. Why: units wasted grenades and time cutting a small corner of a rock (Sim/ZPathing.RockRound.cs)",
                measured = "OpenZ/V1.1/Count Movement Kit (KC1), C9: " + measured,
                toggle = $"ZWorld.RockRoundDeviation: the game turns it on at every start unless 'Zod exact' (oz_zod_exact = {(exact ? "1" : "0")}); no Options row; the parity report, the Deviations block and every counted check turn it off for their own runs",
                status = !pass ? DevCheck : DevActive,
            });
        }
    }
}
#endif
