using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// </summary>
    public static partial class OpenZParity
    {
        static void AddAiCombatDeviations(List<Deviation> list, Func<string, int, int> getInt)
        {
            string St(string pref) => getInt(pref, 1) == 1 ? DevActive : DevActive + " (off by pref)";
            list.Add(new Deviation
            {
                id = "DV53", change = "Squad help: idle squad mates and nearby idle units engage the enemy shooting one of them",
                zod = "no retaliation: a robot agros only on an enemy within its attack radius + agro_distance 40 px (zobject.cpp:1603-1853); an attacker that out-ranges the squad (sniper 144, laser 136, heavy 144 vs grunt 120) picks it off",
                openz = "a hit (hitscan / missile / grenade) by an enemy unit gives every IDLE unit of that side that can attack and sees the shooter (its squad mates always, others when the victim is inside their agro radius) Zod's AGRO waypoint on it, leash = the distance + 32 px; " +
                        "at most one call per victim per second; orders, fights and moves are never touched, cannons stay put. A robot being shuffled by the pathing layer / a scuffle nudge (not an order) still engages in range",
                measured = "harness help: 2nd idle squad 80 px behind the hit squad reacted 0/48 -> 48/48 per map (L02 + L17), the hit squad's mates as before (38/48, 30-40/48: the victim dies first)",
                toggle = "oz_squad_help (off in Zod exact)", status = St("oz_squad_help"),
            });
            list.Add(new Deviation
            {
                id = "DV54", change = "Captures run: a move onto a flag and an enter order always start the run",
                zod = "AttemptStartRun only when the target is within move_speed x stamina and not on the 1-in-5 roll (zobject.cpp:2058-2079, :2729-2747): a grunt runs only inside ~156 px",
                openz = "with stamina >= 0.3 s the unit runs (run_unit_speed 1.8x) for its max_run_time, then walks; recharge unchanged; Zod's roll is still drawn where Zod draws it",
                measured = "harness run: running at 0.3 s flag 200-260 px 0/40 -> 40/40, enter jeep 200 px 0/48 -> 48/48; mean arrival 11.2 -> 6.2 s (L02 jeep 200 px)",
                toggle = "oz_capture_run (off in Zod exact)", status = St("oz_capture_run"),
            });
            list.Add(new Deviation
            {
                id = "DV55", change = "Close grab: a flag within Zod's flag distance / an empty vehicle within 5 tiles is taken by the single closest idle unit, also a player-held one (with DV84 on: the vehicle within 220 px)",
                zod = "every idle leader within auto_grab 220 px grabs (zobject.cpp:1603-1853); OpenZ DV38 holds player-ordered units (no auto-grab) and sends one unit per target (the first to check)",
                openz = $"a held unit still takes an empty vehicle within {ZWorld.CloseGrabPx} px and a flag within {ZWorld.CloseGrabFlagPx} px (Zod's auto_grab_flag_distance) that it sees and can reach; on a human team only the closest eligible idle unit (squad = one) goes for any auto-grab; sight rule of ai paths unchanged (never through rocks); with DV84 on a held unit takes an empty vehicle or gun within Zod's auto_grab_vehicle_distance (220 px) like a flag, not only within {ZWorld.CloseGrabPx} px (OpenZParity.V11GrabCertain.cs)",
                measured = "harness grab: held squad 3-5 tiles away went 0/18 -> 17/18 L02, 0/18 -> 18/18 L17 (running), held squad 9-12 tiles from an empty vehicle 0 (flags: taken up to ZWorld.CloseGrabFlagPx)",
                toggle = "oz_close_grab (off in Zod exact)", status = St("oz_close_grab"),
            });
            list.Add(new Deviation
            {
                id = "DV56", change = "The CPU sends its idle groups (keeps a small defence)",
                zod = "ReduceUnitsToPercent 15-35 % + 1 per round and one unit per target (mutual nearest, zbot.cpp:176-414): with few targets a big group stands by the fort",
                openz = $"Normal CPU: every {ZBot.COMMIT_INTERVAL:0} s, when {ZBot.COMMIT_MIN}+ units (leaders / vehicles) stood idle {ZBot.COMMIT_IDLE:0} s, it keeps the {ZBot.COMMIT_KEEP} nearest its fort and sends the rest (Zod's bot waypoint, attack-move) to the foreign flag nearest them, else into the enemy fort (robots) / at the nearest enemy (vehicles), same bot regions; Easy unchanged",
                measured = "harness cpu (9 units spawned by the CPU fort, CPU ahead on flags): L02 idle at +60 s 5 -> 1, +90 s 3 -> 0 (Zod's dribble empties it at +180 s); bot vs bot 3 seeds x 2 maps end normally",
                toggle = "oz_bot_commit (off in Zod exact)", status = St("oz_bot_commit"),
            });
        }
    }
}
