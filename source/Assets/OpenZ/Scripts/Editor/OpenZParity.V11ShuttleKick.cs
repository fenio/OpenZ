#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV129 "the shuttle's kickflip is a real manoeuvre" (items 436 + 438; the rule: Sim/ZWorld.ShuttleKick.cs,
    /// on top of DV118; the view: View/OzPlayableShuttle.KickEvade.cs, View/OzShuttleKickflip.Evade.cs). Measured headless: the counted check of
    /// Editor/OzShuttleKickCount.cs with <see cref="OzShuttleKickCount.RowSeeds"/> seeds a case (the dash on a plain map, a new order during the
    /// flip at five angles and to near points ahead, guns and Toughs firing at a flipping ship, the evasive self-flip on long Level 16 flights under fire, the switch off).</summary>
    public static partial class OpenZParity
    {
        static void AddShuttleKickDeviation(List<Deviation> list)
        {
            string measured; bool pass;
            bool was = ZWorld.ShuttlePlayableDeviation, wasOn = ZWorld.ShuttleOn, wasK = ZWorld.ShuttleKickDeviation;
            try
            {
                string check;
                using (OzV11Kit.GameSwitches()) check = OzShuttleKickCount.Check(SettingsText, MapText("L16"), OzShuttleKickCount.RowSeeds);
                measured = OzV11Kit.VerdictCell(check, out pass) + " (the whole report: menu OpenZ/V1.1/Shuttle: K436 Kickflip Evade Check)";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            finally { ZWorld.ShuttlePlayableDeviation = was; ZWorld.ShuttleOn = wasOn; ZWorld.ShuttleKickDeviation = wasK; }
            list.Add(new Deviation
            {
                id = "DV129", change = "The shuttle's kickflip is a real manoeuvre: a dash forward through the roll, a harder target while it rolls, and the ship flips by itself to dodge fire on a long flight",
                zod = "no shuttle in Zod (DV118 is an OpenZ unit); before DV129 the flip was a picture only (the rules never knew it) and the ship flipped by itself on 5 % of far orders",
                openz = $"the flip's window ({ZWorld.ShuttleKickSec:0.0} s) opens with the player's double click (the view asks the rules on the frame it pops) or by the ship itself; never two at once. " +
                        $"The dash: extra speed (x{ZWorld.ShuttleKickPeak:0.0} at its peak, 64 u^3 (1 - u)^3 over the window: smooth at both ends), about 1.6 s of cruise of extra way, the start test keeps the whole flip and the planned stop before the goal or a route's next point; " +
                        $"a new order eases the extra speed out over {ZWorld.ShuttleKickEaseSec:0.0} s (in the pop it also closes the window; a near point ahead: the planned stop takes that way out, {ZWorld.ShuttleKickTieMinSec:0.00} .. {ZWorld.ShuttleKickTieMaxSec:0.0} s, the stop planned that much shorter). A harder target while the window is open: a gun's hit chance x{ZWorld.ShuttleKickHitMul:0.00} (the same roll of the world's generator), " +
                        "a rocket or shell fired at it keeps its aim with that share (a hashed roll; the others are aimed past the hull), a blast in flight that would reach it does so with that share. " +
                        $"The evasive self-flip (instead of the 5 %): on a long flight (>= {ZWorld.ShuttleKickSelfMinRemainM:0} m left, >= {ZWorld.ShuttleKickSelfFlownM:0} m flown), in the steady cruise, under enemy fire for its reaction time ({ZWorld.ShuttleKickReactMinSec:0.0} .. {ZWorld.ShuttleKickReactMaxSec:0.0} s, hashed), at least {ZWorld.ShuttleKickCooldownSec:0} s after any flip. " +
                        "Deterministic: the rules' clock, the ship's state and hashed rolls only. View: the same flip pops; the frame follows the drawn hull unturned; the pilot's evasive lines when on screen and selected",
                measured = measured,
                toggle = "ZWorld.ShuttleKickDeviation (code switch, on; off = DV118 as it was: the flip a picture only); matters only while DV118 makes shuttles (off in 'Zod exact')",
                status = pass ? DevActive : DevCheck,
            });
        }
    }
}
#endif
