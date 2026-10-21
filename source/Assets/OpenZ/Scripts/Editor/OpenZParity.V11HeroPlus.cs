#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV133 "hero squads stronger again: ALAN &amp; BRAD +15 % hit points and damage" (the rule:
    /// Sim/ZHeroSquad.K442.cs, on top of DV112). Measured headless: the counted check of Editor/OzV11HeroK442Count.cs (the numbers before /
    /// after, the strong shot, the thrown grenades and their blast, the self-repair, the troop carrier, the same duel twice, and the duels
    /// against a grunt squad and a Tough squad at its own 10 seeds a row).</summary>
    public static partial class OpenZParity
    {
        static void AddHeroPlusDeviation(List<Deviation> list)
        {
            string measured; bool pass;
            bool was = ZWorld.HeroPlusDeviation, wasBoost = ZWorld.HeroBoostDeviation;
            try
            {
                string check;
                using (OzV11Kit.GameSwitches()) check = OzV11HeroK442Count.Check(SettingsText);
                measured = OzV11Kit.VerdictCell(check, out pass) + " (the 100-a-row duels and DV112's fight table before / after: menu OpenZ/V1.1/Hero Squad: K442 Duels)";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            finally { ZWorld.HeroPlusDeviation = was; ZWorld.HeroBoostDeviation = wasBoost; }
            var k = ZHeroSquads.Get(ZHeroSquads.AlanBrad);
            var r = ZHeroPlus.Kinds[ZHeroSquads.AlanBrad];
            var b = ZHeroBoost.Kinds[ZHeroSquads.AlanBrad];
            double h = b.HealthPct / 100.0 * r.HealthPct / 100.0, d = b.DamagePct / 100.0 * r.DamagePct / 100.0, gr = b.GrenadePct / 100.0 * r.GrenadePct / 100.0;
            list.Add(new Deviation
            {
                id = "DV133", change = "Hero squads stronger again: ALAN & BRAD +15 % hit points and +15 % damage (strong shot and thrown grenades) on top of DV112",
                zod = "every robot of a type has its type's hit points and damage, and every thrown grenade the same damage (no named units)",
                openz = $"Sim/ZHeroSquad.K442.cs, both members of {k.Name}, applied inside DV112's own numbers (so at the start and every time a member gets out, on every hit and every grenade a hero throws): " +
                        $"hit points x {r.HealthPct / 100.0:0.00}, the strong shot x {r.DamagePct / 100.0:0.00}, their thrown grenades x {r.GrenadePct / 100.0:0.00} (a direct hit and an area hit), each rounded half up on DV112's rounded number; " +
                        $"with DV108's x {k.HealthMul} / x {k.DamageMul} and DV112's x {b.HealthPct / 100.0:0.00}: hit points {k.HealthMul * h:0.00} x a grunt's, the strong shot {k.DamageMul * d:0.00} x a grunt's hit, grenades {gr:0.00} x the game's; " +
                        "range, the pause between shots and throws, the hit chance, the blast radius, speed, the driver hit and the self-repair share (20 % of full health a minute) unchanged; acts only while DV112 is on",
                measured = measured,
                toggle = "ZWorld.HeroPlusDeviation: on (a code switch); it acts only while ZWorld.HeroBoostDeviation (DV112) is on, which the game sets at every start (off in 'Zod exact', where no hero squad exists) and every headless world leaves off; off in the version 1.1 counted checks (OzV11Kit.GameSwitches), this probe sets it",
                status = pass ? DevActive : DevCheck,
            });
        }
    }
}
#endif
