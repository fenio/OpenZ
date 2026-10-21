#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV108 "hero squads placed by the level data: ALAN &amp; BRAD" (the rule: Sim/ZHeroSquad.cs). Measured
    /// headless: the rule check of Editor/OzV11HeroSquadCount.cs (two robots, Brad leads, hit points, the strong shot on a hull and
    /// on a grunt, the shot pause, vehicles / guns / troop carriers as grunts and out again as themselves, a normal loss, the level 16
    /// extra squad and its campfire tile with the switch on and off) and a short fight table
    /// (20 fights a row against two and three grunt squads).</summary>
    public static partial class OpenZParity
    {
        static void AddHeroSquadDeviation(List<Deviation> list)
        {
            string measured; bool pass;
            bool was = ZWorld.HeroSquadDeviation;
            try
            {
                string rules;
                using (OzV11Kit.GameSwitches()) rules = OzV11HeroSquadCount.Rules(SettingsText, MapText("L16"));
                OzV11HeroSquadCount.Row two, three;
                using (OzV11Kit.GameSwitches())
                {
                    two = OzV11HeroSquadCount.Count(SettingsText, "ALAN & BRAD", "2 grunt squads", 20, 1100003);
                    three = OzV11HeroSquadCount.Count(SettingsText, "ALAN & BRAD", "3 grunt squads", 20, 1200006);
                }
                string cell = OzV11Kit.VerdictCell(rules, out bool rulesPass);
                string kh; using (OzV11Kit.GameSwitches()) kh = OzV11HeroKhCount.Check(SettingsText);   // KH (items 186, 187)
                cell += "; KH: " + OzV11Kit.VerdictCell(kh, out bool khPass);
                string kh2; using (OzV11Kit.GameSwitches()) kh2 = OzV11HeroKh2Count.Check(SettingsText);  // KH2 (item 199)
                cell += "; KH2: " + OzV11Kit.VerdictCell(kh2, out bool kh2Pass);
                pass = rulesPass && khPass && kh2Pass && three.red < 10;
                measured = cell + $"; fights (20 a row, red wins / blue wins / draws): against two grunt squads {two.red}/{two.blue}/{two.draw}, against three {three.red}/{three.blue}/{three.draw}" +
                           " (the 100-fight table: menu OpenZ/V1.1/Hero Squad: Count Fights)";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            finally { ZWorld.HeroSquadDeviation = was; }
            var k = ZHeroSquads.Get(ZHeroSquads.AlanBrad);
            list.Add(new Deviation
            {
                id = "DV108", change = "Hero squads placed by the level data: ALAN & BRAD",
                zod = "every squad of the map is a squad of one robot type with that type's numbers (zserver.cpp:623-657); no named squads",
                openz = $"Sim/ZHeroSquad.cs: an entry of the level data's own list \"heroes\" (\"squad\": \"{ZHeroSquads.AlanBrad}\") places {k.Name} as an EXTRA squad on top of the map's units " +
                        $"(nothing replaced): two grunts, Brad the leader (he walks in front, Alan in the V slot behind him), each with {k.HealthMul} x a grunt's hit points, the strong shot " +
                        $"({k.DamageMul} x a grunt's hit on any target), {k.CycleMul:0.00} x a grunt's pause between shots (chosen by counted fights), every other grunt rule: they board " +
                        "vehicles, guns and troop carriers as grunts do, but ONLY on the player's order (never by themselves: the idle grab, the grab on the way and the CPU's car park skip them), and come out again as Alan and Brad with their own hit points. " +
                        $"Alan reaches {ZHeroRange.MulOf(k, 1):0.00} x a grunt's range (his rifle and his grenades; KH2, switch ZWorld.HeroRangeDeviation), Brad a grunt's; a member plans his attack approach (DV28) with his leader's range when it is shorter, and fires once in his own range. Alan starts with {ZHeroGrenades.StartOf(k, 1)} grenades of his own (Brad none), thrown by the sniper's grenade rule (DV40: at vehicles, guns and buildings, never at robots; with DV40 off at anything); they ride with him through a crew list. Their loss is a normal loss. " +
                        "Level 16: an extra red start squad (the psycho squad and every unit of the map stay), on two sun loungers east of the shuttle with a burning campfire beside them. " +
                        "View: View/OzHeroSquadView.cs, View/OzHeroCampfire.cs (the real Alan and Brad on the grunt's animations, their own gun sounds, picture and name everywhere a unit's name shows; KH2: their torso and face turn to the target, their own lying legs on the start loungers, their feet held still through the drink and the other antics whose clips keep the feet still)",
                measured = measured,
                toggle = "ZWorld.HeroSquadDeviation: set at every game start (off in 'Zod exact': no heroes, no campfire, the map alone); headless default off, this probe turns it on",
                status = pass ? DevActive : DevCheck,
            });
        }
    }
}
#endif
