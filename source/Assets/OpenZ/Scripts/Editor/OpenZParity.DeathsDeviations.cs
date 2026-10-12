#if !OPENZ_HARNESS
using System.Collections.Generic;

namespace OpenZ.EditorTools
{
    /// <summary>deaths_mech_v1: the Deviations rows of the death / destruction presentation. All of them are view only (the sim, its
    /// damage, debris missiles and timings stay Zod's); they are listed because they draw something Zod does not.</summary>
    public static partial class OpenZParity
    {
        static void AddDeathDeviations(List<Deviation> list)
        {
            list.Add(new Deviation
            {
                id = "DV17", change = "Light vehicles flip on death (view only)",
                zod = "jeep / APC / crane leave a wasted sprite for 5-7 s, then sparks (EDeath, edeath.cpp:22, DoSparks)",
                openz = "one in three (seeded per unit) is kicked up spinning with the legacy destruction launch (up 6.5-8.5 m/s, spin (sin a 4.3, 1.4 cos a, -cos a 4.3), restitution .12, damping .28 / .95), crashes with a clang, dust and a scorch, then burns like the others",
                measured = "Play: OpenZ/Deaths/Test: kill vehicles + cannons (outcome mode 'always') + slow-motion frame dump",
                toggle = $"WreckFx.Flips = {WreckFx.Flips}",
                status = WreckFx.Flips ? DevActive : DevCheck,
            });
            list.Add(new Deviation
            {
                id = "DV18", change = "Vehicles and cannons break into physical parts (view only)",
                zod = "wasted sprite + little fires / smoke 5-7 s then a spark fountain (vehicles); wasted gun 2-4 s then the turret flies (cannons); no parts",
                openz = "Zod's timings kept (burn 5-7 s + spark fountain, barrel off at 2-4 s, turret lands on the sim's debris point); on top, seeded: 1 in 3 vehicles explode into their parts at death, 1 in 3 cook off into parts when burnt out, 1 in 2 cannon bases fall apart when the barrel flies; the rest smoulder and sink away (no black clone, no pop)",
                measured = "Play: OpenZ/Deaths/Test: kill vehicles + cannons; OpenZ/Deaths/Test: blow up buildings (debris audit)",
                toggle = $"WreckFx.BreakAtDeath = {WreckFx.BreakAtDeath}, CookOffs = {WreckFx.CookOffs}, CannonBreak = {WreckFx.CannonBreak}",
                status = DevActive,
            });
            list.Add(new Deviation
            {
                id = "DV19", change = "Robots react to blasts they survive (the look; who is down and the fire hold: DV51)",
                zod = "no reaction: a robot hit by splash keeps its sprite and fires on",
                openz = "knockdown (blown up to 2 m, lies, scrambles up, blends back to the sim position) only for a strong direct heavy hit that wins the DV51 roll; every other hit (splash, grenade, debris, a lost roll) is a very small torso flinch (3-5.5 deg, 0.04-0.09 m, ~0.25 s) that keeps it walking, aiming and firing; shrapnel chips off every unit hit. With DV51 on (default) the SIM marks who is down and holds its fire until it is up, and the view stretches the fall / lie / get-up to end on that time (a friendly or a robot the sim did not knock flinches); with DV51 off the view alone decides and draws a downed robot's shots when it is up (<= 1.9 s; a kill shot or a rocket makes it scramble up at once). Every knockdown and death throw points away from the shooter",
                measured = "Play: OpenZ/Deaths/Test: heavy blast (death trace lists the reactions)",
                toggle = $"HitReactions.Enabled = {HitReactions.Enabled}, Shrapnel = {HitReactions.Shrapnel}",
                status = HitReactions.Enabled ? DevActive : DevCheck,
            });
            if (RobotDeathRule.Mix != RobotDeathRule.DeathMix.Zod)
                list.Add(new Deviation
                {
                    id = "DV20", change = "Robot death mix",
                    zod = "missile -> body flies (ERobotTurrent), flame -> melt, every other kill -> one of 4 pop deaths (zrobot.cpp:369-378)",
                    openz = $"pop shares: bullets {RobotDeathRule.ShareBullet:0.00}, laser {RobotDeathRule.ShareLaser:0.00}, splash {RobotDeathRule.ShareSplash:0.00}, flame {RobotDeathRule.ShareFlame:0.00}; the rest ragdoll / melt",
                    measured = "Play: OpenZ/Deaths/Trace deaths",
                    toggle = "RobotDeathRule.Mix (OpenZ/Deaths menu)",
                    status = DevActive,
                });
        }
    }
}
#endif
