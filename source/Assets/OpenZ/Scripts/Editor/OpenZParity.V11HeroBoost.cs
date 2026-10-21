#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV112 "hero squads stronger: ALAN &amp; BRAD" (the rule: Sim/ZHeroSquad.Kh4.cs). Measured headless: the
    /// counted check of Editor/OzV11HeroKh4Count.cs (the numbers on and off, the strong shot, the self-repair, a destroyed hero, the troop
    /// carrier, the same fight twice, the thrown grenades and their blast) and a short fight table before / after (10 fights a row against two and three grunt squads, a gun
    /// and a light tank).</summary>
    public static partial class OpenZParity
    {
        static void AddHeroBoostDeviation(List<Deviation> list)
        {
            string measured; bool pass;
            bool was = ZWorld.HeroBoostDeviation;
            try
            {
                string check;
                using (OzV11Kit.GameSwitches()) check = OzV11HeroKh4Count.Check(SettingsText);
                string cell = OzV11Kit.VerdictCell(check, out bool checkPass);
                var parts = new List<string>();
                foreach (var opp in OzV11HeroKh4Count.Opponents)
                    using (OzV11Kit.GameSwitches())
                    {
                        OzV11HeroSquadCount.Row a, b;
                        using (ZWorld.HeroBoostOff()) a = OzV11HeroSquadCount.Count(SettingsText, "ALAN & BRAD", opp, 10, 1100003);
                        using (ZWorld.HeroBoostOn()) b = OzV11HeroSquadCount.Count(SettingsText, "ALAN & BRAD", opp, 10, 1100003);
                        parts.Add($"against {opp} {a.red}/{a.blue}/{a.draw} -> {b.red}/{b.blue}/{b.draw}");
                    }
                pass = checkPass;
                measured = cell + "; fights (10 a row, red wins / blue wins / draws, before -> after): " + string.Join(", ", parts) +
                           " (the 100-fight table: menu OpenZ/V1.1/Hero Squad: KH4 Fights)";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            finally { ZWorld.HeroBoostDeviation = was; }
            var k = ZHeroSquads.Get(ZHeroSquads.AlanBrad);
            var r = ZHeroBoost.Kinds[ZHeroSquads.AlanBrad];
            list.Add(new Deviation
            {
                id = "DV112", change = "Hero squads stronger: ALAN & BRAD (hit points, range, strong shot, thrown grenades) and their self-repair",
                zod = "every robot of a type has its type's hit points, range and damage, and every thrown grenade the same damage (no named units); no robot ever repairs himself: health comes back only at a repair station or by a crane",
                openz = $"Sim/ZHeroSquad.Kh4.cs, both members of {k.Name}, on top of DV108 and KH2: hit points x {r.HealthPct / 100.0:0.00} ({k.HealthMul} x a grunt's, then x {r.HealthPct / 100.0:0.00}), " +
                        $"range x {r.RangePct / 100.0:0.00} (each member's own: Brad a grunt's x {r.RangePct / 100.0:0.00}, Alan his KH2 {ZHeroRange.Mul[ZHeroSquads.AlanBrad][1]:0.00} x a grunt's, then x {r.RangePct / 100.0:0.00}), " +
                        $"the strong shot x {r.DamagePct / 100.0:0.00} (a bullet that hits), their thrown grenades x {r.GrenadePct / 100.0:0.00} (Alan's own and any a hero throws from the squad's pool: a direct hit and an area hit, " +
                        "set where the grenade leaves the hand; radius, scatter, range and the pause between throws stay every robot's), each rounded half up; " +
                        $"and their own ability, self-repair: on foot, alive and below full health a member gets back {r.RegenPctPerMin} % of his full health per minute of the game clock, a little every tick " +
                        "(integer maths, the rest carried to the next tick), never above full; inside a vehicle, gun or troop carrier the crew list keeps his hit points (no repair there); a destroyed hero never comes back. " +
                        "Set where the kind's numbers are set (at the start and every time he gets out). View: the unit panel's health label reads SELF-REPAIR for them (green while he repairs, UI/OpenZHud.HeroRegen.cs)",
                measured = measured,
                toggle = "ZWorld.HeroBoostDeviation: set at every game start (off in 'Zod exact', where no hero squad exists); headless default off and off in the version 1.1 counted checks (OzV11Kit.GameSwitches), this probe turns it on",
                status = pass ? DevActive : DevCheck,
            });
        }
    }
}
#endif
