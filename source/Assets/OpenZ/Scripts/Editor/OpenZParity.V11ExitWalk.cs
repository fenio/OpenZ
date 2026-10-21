#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// DV15; the view: View/BoardingView.ExitWalk.cs). Measured headless: the counted check of Editor/OzExitWalkCount.cs (level 16 under
    /// fire: the shuttle, a move order, ALAN &amp; BRAD, an APC, Zod's own crew exit unchanged, the rule off, determinism). K441b: the rule's
    /// numbers unchanged; the row's text names the view's walk from the ramp foot, planned on the ship as drawn at the rule's own pace.</summary>
    public static partial class OpenZParity
    {
        static void AddExitWalkDeviation(List<Deviation> list)
        {
            string measured; bool pass;
            bool was = ZWorld.ShuttlePlayableDeviation, wasOn = ZWorld.ShuttleOn, wasH = ZWorld.ExitWalkHoldOn;
            try
            {
                string check;
                using (OzV11Kit.GameSwitches()) check = OzExitWalkCount.Check(SettingsText, MapText("L16"));
                measured = OzV11Kit.VerdictCell(check, out pass) + " (the whole report: menu OpenZ/V1.1/Boarding: K441 Exit Walk Check)";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            finally { ZWorld.ShuttlePlayableDeviation = was; ZWorld.ShuttleOn = wasOn; ZWorld.ExitWalkHoldOn = wasH; }
            list.Add(new Deviation
            {
                id = "DV132", change = "Out of the carrier first: a robot let out of the shuttle or an APC fights, walks and can be targeted only once its figure has walked out to its spot",
                zod = "no shuttle in Zod (DV118 is an OpenZ unit) and no APC passengers (DV15); DV118 / DV15 alone let the squad out in one tick onto its spots, where each robot fought at once while its figure was still drawn on its bench or on the ramp",
                openz = $"each robot let out gets a getting-out time: the shuttle: its ramp-foot moment + its walk to its back slot at {ZWorld.ExitWalkMps:0.0} m/s (at least {ZWorld.ExitWalkMinSec:0.00} s) + {ZWorld.ExitHandSec:0.0} s; " +
                        $"the APC: {ZWorld.ApcExitOpenSec:0.00} s (its ramp) + {ZWorld.ApcExitGapSec:0.00} s a place in the file + its {ZWorld.ApcExitWalkSec:0.0} s walk + {ZWorld.ExitHandSec:0.0} s; " +
                        "until then it takes no step and fires nothing (orders given meanwhile start then, a fresh start from its spot) and no enemy can pick it as a target (a blast near it still hurts it). " +
                        "Zod's own crew exits (EJECT from a tank, jeep or gun) unchanged. Deterministic: sim time and the rules' foot schedule. " +
                        "View: the exit walk is never squeezed by the fight; its walk on to the robot follows a robot that moved and ends on it, turned to its facing; a shot fired while the figure still walks (Zod's own crews) is drawn when it is out (View/BoardingView.ExitWalk.cs); " +
                        $"K441b: the shuttle's walk from the ramp foot to the slot is planned on the ship as drawn and walked at this rule's own {ZWorld.ExitWalkMps:0.0} m/s (at least {ZWorld.ExitWalkMinSec:0.00} s), the very walk counted above, and any walk that would end later is paced up (at most x1.4) to end the {ZWorld.ExitHandSec:0.0} s hand-over before the robot may act",
                measured = measured,
                toggle = "ZWorld.ExitWalkHoldOn (code switch, on; off = the robots fight at once, as before); matters only while DV118 makes shuttles or DV15 gives APCs passengers (both off in 'Zod exact')",
                status = pass ? DevActive : DevCheck,
            });
        }
    }
}
#endif
