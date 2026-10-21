#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Sim/ZWorld.ShuttleHp.cs, on top of DV118). Measured headless: the counted check of Editor/OzShuttleHpCount.cs with one seed a weapon
    /// (the whole check: menu OpenZ/V1.1/Shuttle: K443 More HP Check).</summary>
    public static partial class OpenZParity
    {
        /// <summary>DV118's words for the ship's hit points (its row, OpenZParity.V11Shuttle.cs).</summary>
        static string ShuttleHpWords => ZWorld.ShuttleMoreHpOn
            ? $"the light tank's hit points x {ZWorld.ShuttleHpFactor:0.0#} ({ZWorld.ShuttleHpFrom(ZWorld.ShuttleBaseHp)}, DV131)"
            : "the light tank's hit points";

        static void AddShuttleHpDeviation(List<Deviation> list)
        {
            string measured; bool pass;
            try
            {
                string check;
                using (OzV11Kit.GameSwitches()) check = OzShuttleHpCount.Check(SettingsText, MapText("L16"), MapText("L02"), 1);
                measured = OzV11Kit.VerdictCell(check, out pass) + " (the whole report: menu OpenZ/V1.1/Shuttle: K443 More HP Check)";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            int hp = ZWorld.ShuttleHpFrom(ZWorld.ShuttleBaseHp);
            list.Add(new Deviation
            {
                id = "DV131", change = "Every shuttle starts with 20 % more hit points: the light tank's x 1.2",
                zod = "no shuttle in Zod (DV118 is an OpenZ unit); DV118 gave it the light tank's hit points (3378)",
                openz = $"every shuttle the rules make starts with the light tank's hit points x {ZWorld.ShuttleHpFactor:0.0#}, " +
                        $"rounded to the nearest point: {hp} (was 3378), the same for every team; everything that reads its health reads a share of the maximum and scales with it (the damage smoke and fire 50 / 25 / 40 %, the damaged look 40 / 70 %, " +
                        "the health bars and the panel's percent, the repair station's full repair in its 5 s pad); a crash landing or break-up only at 0, as before; weapons, the light tank and every other unit unchanged. Deterministic",
                measured = measured,
                toggle = "ZWorld.ShuttleMoreHpOn (code switch, on; off = the light tank's hit points as before); matters only while DV118 makes shuttles (off in 'Zod exact')",
                status = !pass ? DevCheck : ZWorld.ShuttleMoreHpOn ? DevActive : DevActive + " (off)",   // ACTIVE only while the rule is on
            });
        }
    }
}
#endif
