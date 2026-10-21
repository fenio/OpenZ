#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV87 "hatch drivers fire their own weapon" (version 1.1; the rule: Sim/ZObject.DriverFire.cs).
    /// Written from the live constants and the rule table, and measured by the kit's own counted check (OzV11DriverFireCount, the
    /// menu OpenZ/V1.1/Count Driver Shots From The Hatch) with 5 fights per row, run with the game's switches when the block is
    /// written; the switches are put back afterwards. Every other row runs without the rule, also after a Play session left its
    /// switch on: OpenZParity.Run (the Zod rows) and CollectDeviations (every probe) turn it off and put it back.</summary>
    public static partial class OpenZParity
    {
        static void AddDriverFireDeviation(List<Deviation> list, Func<string, int, int> getInt)
        {
            string measured; bool pass;
            try
            {
                string report;
                using (OzV11Kit.GameSwitches()) report = OzV11DriverFireCount.Run(SettingsText, 5);
                measured = OzV11Kit.VerdictCell(report, out pass);
            }
            catch (Exception ex) { measured = "the check threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            bool on = getInt("oz_zod_exact", 0) == 0;
            var s = ZSettings.Parse(SettingsText);
            string guns = "";
            foreach (string t in new[] { "grunt", "psycho", "sniper", "tough", "pyro", "laser", ZElectro.Type })
            {
                var u = s.Unit(t); if (u == null) continue;
                int dmg = t == "grunt" ? u.Damage * ZWorld.CrewRifleMul : u.Damage;
                double cycle = t == ZElectro.Type ? ZElectro.Seconds(ZElectro.CycleTicks) : u.attack_speed;
                string how = t == "tough" ? $"rocket, splash {u.attack_damage_radius} px, {ZWorld.RocketSpeed(t, u.attack_missile_speed)} px/s" : t == ZElectro.Type ? "bolt, never misses, stuns" : $"{u.attack_damage_chance * 100:0} % hits";
                guns += (guns.Length > 0 ? "; " : "") + $"{t} {dmg} HP every {cycle:0.###} s, {u.attack_radius} px, {how}";
            }
            list.Add(new Deviation
            {
                id = "DV87", change = "Hatch drivers fire their own weapon (tanks, mech, the APC's lone driver); no driver comes up on a bridge or in a door",
                zod = "the driver of a light, medium or heavy tank is a picture over the open lid and deals no damage (zvehicle.cpp:268-330); the lid opens 4 times in 5 when the tank takes a new target that can hit drivers and closes 0 to 1.4 s after the tank lets go of it, wherever the tank stands (zvehicle.cpp:198-240, zobject.cpp:1574-1588); the APC fires its crew's weapon from inside and never shows anybody (vapc.cpp:266-342)",
                openz = "the lid rule is unchanged; while the lid of a tank is open (crew not resting, not a veteran crew) its driver fires the weapon of the robot type that drives it, that type's own row of the rule table, at the tank's target when his weapon reaches it with a clear line, else at the nearest enemy unit in his range; " +
                        $"the default driver, a grunt, fires the crane driver's rifle (DV31: the grunt's rifle x {ZWorld.CrewRifleMul}); first shot {ZWorld.CraneGunPopSeconds:0.0} s after the lid opened; no driver sniping from a hatch ({guns}). " +
                        $"On a spot of DV82 (a bridge and {ZWorld.SnipeSafeBridgeReach} px before each end, the exit lane of a fort or factory) the lid of a tank stays shut (the roll is drawn as before) and shuts when it is open, a crew does not rest there and the APC driver does not come up; the crane keeps DV31 everywhere. " +
                        $"An owned APC with its one driver and nobody else aboard fires as before (its crew weapon, DV15), but the driver comes up through the roof hatch to do it: the first shot of a fight waits {ZWorld.CraneGunPopSeconds:0.0} s, he drops back in {ZWorld.CraneGunLinger:0.0} s after the APC last had a target; with passengers, as a veteran crew and on a DV82 spot he stays inside " +
                        "(Sim/ZObject.DriverFire.cs; ZObject.cs lid, ProcessAttackDamage; view: View/BoardingView.DriverFire.cs, RoundHatch 'apc')",
                measured = "OpenZ/V1.1/Count Driver Shots From The Hatch, 5 fights per row: " + measured,
                toggle = $"ZWorld.DriverFireDeviation: the game turns it on at every start, off in 'Zod exact' (oz_zod_exact = {(on ? "0" : "1")}); this report turns it off for the Zod rows and for every other row's probe and puts it back (OpenZParity.Run, CollectDeviations), so they run without it. Part B also needs DV82's switch",
                status = !pass ? DevCheck : on ? DevActive : DevActive + " (off by pref)",
            });
        }
    }
}
#endif
