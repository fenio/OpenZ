#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV104 "a thrown grenade is judged by where it lands" (version 1.1; the rule:
    /// Sim/ZWorld.GrenadeBoost.cs). Written from the live constants and the settings, and measured by the kit's own counted check
    /// (OzV11GrenadeCount, the menu OpenZ/V1.1/Count Grenades (b31)) with RowRuns fights per row, run with the game's switches
    /// when the block is written; the check turns the rule on and off itself and puts the switches back. Every other row runs
    /// with Zod's grenade, also after a Play session left the switch on: OpenZParity.Run and CollectDeviations hold it off
    /// (ZWorld.GrenadeBoostOff) and put it back.</summary>
    public static partial class OpenZParity
    {
        /// <summary>Fights per row when the row is written (the menu's full count uses OzV11GrenadeCount.Runs).</summary>
        public const int GrenadeBoostRowRuns = 10;

        static void AddGrenadeBoostDeviation(List<Deviation> list, Func<string, int, int> getInt)
        {
            string measured; bool pass;
            try
            {
                string report;
                using (OzV11Kit.GameSwitches()) report = OzV11GrenadeCount.Run(SettingsText, GrenadeBoostRowRuns);
                measured = OzV11Kit.VerdictCell(report, out pass);
            }
            catch (Exception ex) { measured = "the check threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            bool exact = getInt("oz_zod_exact", 0) != 0, pref = getInt("oz_grenade_boost", 1) == 1, on = !exact && pref;
            var s = ZSettings.Parse(SettingsText);
            int zd = (int)(s.grenade_damage * ZConst.MAX_UNIT_HEALTH), zr = s.grenade_damage_radius;
            int nd = (zd * ZWorld.GrenadeBoostDamagePct + 50) / 100, nr = (zr * ZWorld.GrenadeBoostRadiusPct + 50) / 100, na = (zd * ZWorld.GrenadeAreaDamagePct + 50) / 100;
            list.Add(new Deviation
            {
                id = "DV104", change = "A thrown grenade is judged by where it lands: blast radius x 1.2; a direct hit x 1.15, an area hit x 0.9",
                zod = $"a thrown grenade takes grenade_damage x MAX_UNIT_HEALTH = {zd} HP at the centre of its blast, falling in a straight line to nothing at grenade_damage_radius = {zr} px " +
                      "(zsettings.cpp:269-272, zobject.cpp:2126-2157, zserver.cpp:1076-1098)",
                openz = $"a grenade thrown by a robot of a squad or by a fort's roof defender reaches {nr} px ({ZWorld.GrenadeBoostRadiusPct} %); a target it lands ON (the blast point inside the target's own box) takes {nd} HP at the centre ({ZWorld.GrenadeBoostDamagePct} % of Zod's, rounded), " +
                        $"a target only caught in the blast takes {na} HP at the centre ({ZWorld.GrenadeAreaDamagePct} %), both with Zod's straight-line fall-off to {nr} px: " +
                        "ZWorld.ThrownGrenadeDamage, ThrownGrenadeAreaDamage, ThrownGrenadeRadius, read where the grenade leaves the hand (Sim/ZObject.cs ProcessAttackDamage, Sim/ZObject.FortDefender.cs FdLob), and ZWorld.BlastHit in ProcessMissileDamage (Sim/ZWorld.GrenadeBoost.cs). " +
                        "Unchanged: the throw range, the +-24 px scatter, the 40 px/s flight, the time between throws, 20 a box, the straight-line fall-off, never the thrower's own side, and the step aside of the unit that is thrown at " +
                        $"(a vehicle that is free to move still leaves most blasts); the blasts of a destroyed grenade crate ({zd} HP, 40 px), of rocks and of every shell and rocket keep their numbers. " +
                        "View, with the rule on and off: the blast of a thrown grenade is drawn by its real radius (View/OzGrenadeBlast.cs)",
                measured = $"OpenZ/V1.1/Count Grenades (b31), {GrenadeBoostRowRuns} fights per row: " + measured,
                toggle = $"PlayerPrefs oz_grenade_boost = {(pref ? "1 (on, default)" : "0 (off)")}; off in 'Zod exact' (oz_zod_exact = {(exact ? "1" : "0")}). ZWorld.GrenadeBoostDeviation: the game sets it at every start, headless default off; " +
                         "this report holds it off for the Zod rows and for every other row's probe and puts it back (ZWorld.GrenadeBoostOff in OpenZParity.Run and CollectDeviations)",
                status = !pass ? DevCheck : on ? DevActive : DevActive + " (off by pref)",
            });
        }
    }
}
#endif
