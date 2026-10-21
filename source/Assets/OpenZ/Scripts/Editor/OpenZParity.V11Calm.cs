#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Globalization;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV101 "calm movement" (version 1.1; the rules: Sim/ZCalm.cs and Sim/ZObject.Calm.cs). Written from the
    /// live constants and measured by the kit's own counted check (OzV11SwerveCount, the menu OpenZ/V1.1/Count Calm Movement (b49)),
    /// run with the game's switches when the block is written; the check sets the rule's switch itself and puts it back, and the
    /// block itself is collected with the rule off (CalmOff round CollectDeviations), so the other rows run without it.</summary>
    public static partial class OpenZParity
    {
        /// <summary>Runs the body with calm movement off (a Play session leaves the static switch on) and puts the switch back.</summary>
        static T CalmOff<T>(Func<T> body)
        {
            bool was = ZWorld.CalmMoveDeviation, fort = ZWorld.FortRunDeviation, rock = ZWorld.RockRoundDeviation, overlap = ZWorld.FriendsOverlap;
            ZWorld.CalmMoveDeviation = ZWorld.FortRunDeviation = ZWorld.RockRoundDeviation = ZWorld.FriendsOverlap = false;   // the movement kit's rules (DV101 to DV103, the overlap option)
            try { return body(); }
            finally { ZWorld.CalmMoveDeviation = was; ZWorld.FortRunDeviation = fort; ZWorld.RockRoundDeviation = rock; ZWorld.FriendsOverlap = overlap; }
        }

        /// <summary>The same as CalmOff for a whole block: the movement kit's rules off until the scope is disposed, then back.</summary>
        static IDisposable CalmOffScope()
        {
            bool was = ZWorld.CalmMoveDeviation, fort = ZWorld.FortRunDeviation, rock = ZWorld.RockRoundDeviation, overlap = ZWorld.FriendsOverlap;
            ZWorld.CalmMoveDeviation = ZWorld.FortRunDeviation = ZWorld.RockRoundDeviation = ZWorld.FriendsOverlap = false;
            return new Restore(() => { ZWorld.CalmMoveDeviation = was; ZWorld.FortRunDeviation = fort; ZWorld.RockRoundDeviation = rock; ZWorld.FriendsOverlap = overlap; });
        }
        sealed class Restore : IDisposable { readonly Action a; public Restore(Action a) { this.a = a; } public void Dispose() => a(); }

        static void AddCalmDeviation(List<Deviation> list, Func<string, int, int> getInt)
        {
            string measured; bool pass;
            try
            {
                string report;
                using (OzV11Kit.GameSwitches()) report = OzV11SwerveCount.Run(SettingsText, MapText);
                measured = OzV11Kit.VerdictCell(report, out pass);
            }
            catch (Exception ex) { measured = "the check threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            bool exact = getInt("oz_zod_exact", 0) != 0, on = !exact;
            var inv = CultureInfo.InvariantCulture;
            string S(double v) => v.ToString(inv);
            list.Add(new Deviation
            {
                id = "DV101", change = "Calm movement: straight ways, the same team closer in tight places, the player's order first, fewer steps aside",
                zod = "units have no collision and drive through each other; a path runs from tile to tile in eight directions with the clicked point added last; every unit a shell or a grenade is aimed at steps aside to a random point unless it is a robot that holds a target at that moment, and the next shell re-aims a step that is under way; " +
                      "a unit on an attack-move turns to a random enemy within its range + 40 px, walks up to it and follows it until it is dead",
                openz = "under the spacing layer (DV6) the keeping apart showed as swerving. With the rule on: " +
                        $"(1) a planned path loses the jogs of the tile grid: a path point one of whose two legs is at most {S(ZPathing.JogMax)} px long is left out when the straight leg past it is clear for the unit's own box (a rock counts as in the way; never a point in a bridge or lane keep-clear area and never a leg across one, except for a vehicle with no other unit within {S(ZPathing.AloneRadius)} px when its path is planned); " +
                        $"(2) an order that goes on after something put in front of it keeps its route (the stuck guard measured its first progress one tick too early and re-planned), a unit with nobody within {S(ZPathing.AloneRadius)} px gets no way round unless it has made no way for {S(ZPathing.AloneBlocked)} s, with company a new way round at most every {S(ZPathing.RouteKeep)} s; " +
                        $"(3) two units of ONE team that are both on an order (robots, vehicles, or one of each) keep less room in a bridge, gate or passage zone, or after {S(ZPathing.PressAfter)} s of slowing each other: the spacing shrinks by {S(ZPathing.CloseStep)} every {S(ZPathing.CloseStepTime)} s down to {S(ZPathing.CloseMin)} of the normal one and grows back in the open; so close they queue instead of weaving; " +
                        $"a robot on an order is sent out of the way of a vehicle of its team at most every {S(ZPathing.YieldRest)} s (the next vehicles drive through it, as 'vehicles first' already allowed); within {S(ZPathing.NearGoal)} px of the end of its way, and the moment its order ends, a unit is back at the normal spacing; nothing changes towards enemies, standing units, crushing or what can be hit; " +
                        $"(4) on the PLAYER's order a unit fights only an enemy it can hit from where it stands on its way: it stops and fires, and goes on the moment that enemy is dead or out of range, never more than {S(ZWorld.CalmOrderFightFar)} px from its way (the click with Ctrl held = the attack-move as before); steps aside that follow one another end after {S(ZWorld.CalmOrderAsideTime)} s or {S(ZWorld.CalmOrderAsideFar)} px; " +
                        $"farther than {S(ZPathing.KeepGoalFar)} px from the ordered point his move is never ended early: a way round that stops short of it is not taken and the stuck guard plans again where it gave the order up; the flag or empty vehicle taken beside its way stays as before; " +
                        $"(5) Toughs and Snipers never step aside, they stand and fire (like the Electrobot, DV83); grunts, psychos, pyros and lasers still do, but after a step aside by any body of a squad no body of that squad steps aside again for {S(ZWorld.CalmRobotDodgeRest)} s" +
                        (ZWorld.CalmShootFirst > 0 ? ", nor, while an enemy is in its range, before the squad has fired once since (this part seldom acts: a robot that holds a target never stepped aside)" : "") +
                        (ZWorld.CalmDodgeEnds ? $", and a step aside ends {S(ZWorld.CalmDodgeEndWait)} s after the shell or grenade it was for has gone off" : "; a step aside that was begun is walked to its point as before") +
                        "; vehicles step aside as before. " +
                        "Why: units zigzagged and made needless turns, a lone vehicle before a bridge most of all; under fire a unit got one small move after the other; robots stepped aside instead of firing (Sim/ZCalm.cs, Sim/ZObject.Calm.cs). " +
                        "The option FRIENDLY UNITS OVERLAP (Options, GAME tab, oz_overlap, default OFF) switches the same-team part of the spacing layer off: with it ON two units of one team of which one moves pass through each other as in Zod (no spacing, queue, side-step, push or step aside between them); paths round obstacles, everything towards enemies and units that both stand keep apart as before (Sim/ZPathing.Overlap.cs)",
                measured = "OpenZ/V1.1/Count Calm Movement (b49): " + measured,
                toggle = $"ZWorld.CalmMoveDeviation: the game turns it on at every start unless 'Zod exact' (oz_zod_exact = {(exact ? "1" : "0")}); no Options row; the parity report, this block and every counted check turn it off for their own runs, so the other rows run without it. ZWorld.FriendsOverlap: the Options row FRIENDLY UNITS OVERLAP (oz_overlap = {getInt("oz_overlap", 0)}), never in 'Zod exact'",
                status = !pass ? DevCheck : on ? DevActive : DevActive + " (off by pref)",
            });
        }
    }
}
#endif
