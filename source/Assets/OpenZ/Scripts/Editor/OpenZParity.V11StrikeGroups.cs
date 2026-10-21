#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Globalization;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV107 "enemy strike groups" (the rule: Sim/ZBot.StrikeGroup.cs). Written from the rule's live
    /// constants and measured by the short probe of its counted check (OzV11StrikeCount.Probe: one match on the game's first
    /// switch itself and puts every switch back, so every other row runs without the rule.</summary>
    public static partial class OpenZParity
    {
        static void AddStrikeGroupDeviation(List<Deviation> list, Func<string, int, int> getInt)
        {
            string measured; bool pass;
            try { measured = OzV11Kit.VerdictCell(OzV11StrikeCount.Probe(SettingsText, MapText), out pass); }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            var inv = CultureInfo.InvariantCulture;
            string T(double s) => ((int)(s / 60)).ToString(inv) + ":" + ((int)(s % 60)).ToString("00", inv);
            bool game = getInt("oz_zod_exact", 0) == 0; int pref = getInt("oz_strike_groups", 1);
            list.Add(new Deviation
            {
                id = "DV107", change = "Enemy strike groups: the computer gathers a group in front of its fort and sends it as one body at the other side's biggest gathering",
                zod = "the engine's computer never gathers, waits or sends units together: every 4 to 12 s it orders a random 15 to 35 % (+1) of its idle units, one unit per nearest target (zbot.cpp:127-201, :642-687)",
                openz = $"ZBot.StrikeGroup.cs. Never before {T(ZWorld.StrikeStartSec)} on the game clock; the next window no sooner than {T(ZWorld.StrikeGapSec)} after the last group formed and left, {ZWorld.StrikeRetrySec.ToString("0", inv)} s after a window given up; every difficulty (the Easy limits are not asked). " +
                        $"Fed by the computer's fort and its factories nearest to it: 1 on a map under {ZWorld.StrikeSmallMapTiles} tiles, 2 under {ZWorld.StrikeLargeMapTiles}, 3 from there on (all it owns when it owns fewer; none without a factory). " +
                        $"The window opens when one of them turns out a unit and lasts one production cycle: the game's own build time of the slowest of them, at most {ZWorld.StrikeCycleLimitSec.ToString("0", inv)} s. " +
                        $"Every armed unit they turn out in it (at most {ZWorld.StrikeMaxMembers}) gets a place of its own in front of the gate ({ZWorld.StrikeFrontMinPx} to {ZWorld.StrikeFrontMaxPx} px out, up to {ZWorld.StrikeSideMaxPx} px to each side, {ZWorld.StrikePlacePitchPx} px apart, the gate's way out and every exit lane left free, open ground a vehicle reaches from the gate without a bridge), walks there and holds position; a member that stops beside its place is sent to it again, {ZWorld.StrikeResendTries} times without coming nearer, then the spot it stands on is its place when that spot is on the forming ground (out of the gate's way, of every exit lane, not too near the gate), else it gets the next free place once, else it is released alone. " +
                        $"Formed = every feeding building has delivered, at least {ZWorld.StrikeMinMembers} members, all standing; or the cycle is over with at least {ZWorld.StrikeMinMembers}. " +
                        "Given up (every member released at once with an order of the computer's own kind): a territory that holds a feeding building is lost (no other territory), a robot or vehicle of the group is destroyed by fire (a squad whose leader fell gets its order under its next robot, one tick later; a robot a crocodile takes does not end it), the cycle is over with too few. " +
                        $"The target: the unit of the other side with the most of its side's squads and vehicles within {ZWorld.StrikeGatherPx} px (the middle of those); looked at again every {ZWorld.StrikeRetargetSec.ToString("0", inv)} s. " +
                        $"The march: one common route for the group (the vehicles' shortest way when it has a vehicle, else the robots'), a point every {ZWorld.StrikeRouteStepPx} px, each member sent from point to point and then to its own spot at the target; everybody leaves in the same tick with the computer's attack-move order; a member more than {ZWorld.StrikeAheadFreePx} px of way ahead of the group's rear slows down, to {ZWorld.StrikeMinPace.ToString("0.##", inv)} of its speed at {ZWorld.StrikeAheadMaxPx} px, so the group moves at the pace of its slowest member. " +
                        $"Released (all at once, running orders kept): everybody in the target area ({ZWorld.StrikeArrivePx} px), or {ZWorld.StrikeArriveGraceSec.ToString("0", inv)} s after half of them; fewer than two left; {ZWorld.StrikeMarchLimitSec.ToString("0", inv)} s of march; the switch turned off. A member on its way at its own speed that does not move {ZWorld.StrikeStallPx} px in {ZWorld.StrikeStallSec.ToString("0", inv)} s is released alone. " +
                        "While forming and marching a member is out of the computer's order rounds and of DV37, DV56, DV60, DV70, DV72, and its crew does not rest in the hatch (DV75). Orders only through ZWorld.SendWaypoints / SendHold and the planner's own Command. Deterministic: game time, object order, no random draw",
                measured = "OzV11StrikeCount.Probe (one match against the stand-in player to 12:00): " + measured,
                toggle = $"ZWorld.StrikeGroupDeviation: the game sets it at every start from the Options row ENEMY STRIKE GROUPS (PlayerPrefs oz_strike_groups = {pref}, default 1) and when the row is switched during a match; off in 'Zod exact' (oz_zod_exact = {(game ? "0" : "1")}); headless default off, and this report, the Deviations block, the computer's own checks and the counted checks of version 1.1 turn it off for their run (ZWorld.StrikeGroupsOff, OzV11Kit.GameSwitches), so every other row runs without it",
                status = !pass ? DevCheck : game && pref == 1 ? DevActive : DevActive + " (off by pref)",
            });
        }
    }
}
#endif
