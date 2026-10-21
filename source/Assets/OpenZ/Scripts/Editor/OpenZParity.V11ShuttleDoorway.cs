#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Sim/ZWorld.ShuttleDoorway.cs, on top of DV118; the view: View/BoardingView.ShuttleDoorway.cs). Measured headless: the counted check of
    /// Editor/OzShuttleDoorwayCount.cs (level 16: take-off, normal and crash landings, boarding, unloading, the heroes and an empty ship, full
    /// flight, the repair station, the switch off, determinism).</summary>
    public static partial class OpenZParity
    {
        static void AddShuttleDoorwayDeviation(List<Deviation> list)
        {
            string measured; bool pass;
            bool was = ZWorld.ShuttlePlayableDeviation, wasOn = ZWorld.ShuttleOn, wasD = ZWorld.ShuttleDoorwayDeviation;
            try
            {
                string check;
                using (OzV11Kit.GameSwitches()) check = OzShuttleDoorwayCount.Check(SettingsText, MapText("L16"));
                measured = OzV11Kit.VerdictCell(check, out pass) + " (the whole report: menu OpenZ/V1.1/Shuttle: K418 Death Edge Cases Check)";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            finally { ZWorld.ShuttlePlayableDeviation = was; ZWorld.ShuttleOn = wasOn; ZWorld.ShuttleDoorwayDeviation = wasD; }
            list.Add(new Deviation
            {
                id = "DV126", change = "The shuttle destroyed while a squad walks in or out: the robots drawn outside, on the ramp or in the doorway survive as normal units behind the wreck; only those fully inside are lost",
                zod = "no shuttle in Zod (DV118 is an OpenZ unit); DV118 alone boarded the squad in one tick (a kill in the next seconds took the whole squad, some still drawn walking to the ramp) and let it out in one tick (a kill left every robot alive, some still drawn on their seats)",
                openz = $"walking in: member k reaches the ramp foot at the rules' moment and passes the doorway {ZWorld.ShuttleBoardDoorSec:0.0} s later; destroyed before that, it is made again as a normal robot (its type, hit points, a hero as himself) " +
                        "on the back axis behind the wreck (slots 1, 2 ..), no order, one squad of the saved (the squad's pool and a pilot-to-be's rank go with them when the leader is saved); " +
                        $"walking out: member k leaves its seat {ZWorld.ShuttleRampOutSec:0.0} s before it steps off the foot and reaches the doorway {ZWorld.ShuttleExitDoorSec:0.0} s after; destroyed before that it is lost with the ship (taken off the map, listed for the view's body), after it it lives; " +
                        "everything else unchanged (in the air, before the ramp foot, after the squad sat down: everyone aboard lost, robots outside live). Deterministic: sim time and the rules' foot schedule. " +
                        "View: the boarding and exit walks are timed to the two doorway moments; at the kill a saved figure walks off the ramp to its robot, a lost one is thrown out with the pieces (View/BoardingView.ShuttleDoorway.cs)",
                measured = measured,
                toggle = "ZWorld.ShuttleDoorwayDeviation (code switch, on; off = DV118 as it was); matters only while DV118 makes shuttles (off in 'Zod exact')",
                status = pass ? DevActive : DevCheck,
            });
        }
    }
}
#endif
