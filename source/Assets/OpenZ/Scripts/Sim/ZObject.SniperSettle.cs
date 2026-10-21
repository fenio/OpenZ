using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// K248d, the sniper's stance (with K248b's stance rules, View/SniperStanceGate.cs, and the rules' own stance when that is in the
    /// game): a sniper kneels or lies down only where its fight needs no more walking. Before, it went down once a fight as soon as it
    /// had stood still with a target for a moment; when that target died and the next enemy stood just outside its range, or its order
    /// led on to an enemy farther away, it got up again and walked on: down, up, walk, and the rest of the fight standing.
    /// <para><see cref="SniperFightSettled"/> answers "will this fight make it walk again from here?" from the rules' own state, with
    /// the rules that start such a walk:
    /// <list type="bullet">
    /// <item>an order or a chase that leads on: its first waypoint (and its squad leader's) is an attack or a chase whose target is not
    /// in its range, or any other order (a move, an attack-move, an enter, a crate, a flag, a fort, a repair). The spacing layer's own
    /// small moves do not count (they never get it up);</item>
    /// <item>the agro rule (Zod's, zobject.cpp:1603-1853): an enemy it can attack, sees and can reach, outside its range but within
    /// the agro reach (range + agro_distance) plus <see cref="ZWorld.SniperSettleMarginPx"/> px (one that is about to step into it);</item>
    /// <item>the squad's help call (DV53): an enemy outside its range that shoots at its squad, at any distance.</item>
    /// </list>
    /// None of these: the fight is settled and it may go down (once, as before). Any of them: it fights standing (it would have to get
    /// up for that walk). A pure query of the rules' state: no state, no random draw; the view asks it (SniperStanceGate.Look), and
    /// the rules' stance through <see cref="SniperStanceSettled"/> (sim time only, deterministic).</para>
    /// Switch <see cref="ZWorld.SniperDownToStay"/> (false = K248b's stance rules exactly).
    /// </summary>
    public partial class ZObject
    {
        /// <summary>K248d: its fight needs no more walking from where it stands (see the class summary).</summary>
        public bool SniperFightSettled()
        {
            if (World == null || Destroyed || KillAt >= 0) return true;
            if (!SettleOrderHere(this)) return false;
            if (IsMinion && !Leader.Destroyed && Leader.KillAt < 0 && !SettleOrderHere(Leader)) return false;   // the squad follows its leader's order
            var sq = IsMinion ? Leader : this;
            long reach = AttackRadius + World.S.agro_distance + Math.Max(0, ZWorld.SniperSettleMarginPx), r2 = reach * reach;
            foreach (var o in World.Objects)
            {
                if (!o.IsUnit || o.Destroyed || o.KillAt >= 0 || o.Owner == Team.Neutral || o.Owner == Owner) continue;
                long dx = o.CenterX - CenterX, dy = o.CenterY - CenterY;
                bool near = dx * dx + dy * dy <= r2;
                bool atSquad = o.AttackObject != null && (o.AttackObject == sq || o.AttackObject.Leader == sq);   // the help call would send it after that one
                if (!near && !atSquad) continue;
                if (!CanAttackObject(o) || WithinAttackRadius(o)) continue;                    // in its range: it fights it from here
                if (!CanSee(o) || PathUnreachable(o)) continue;                                // the agro and help rules chase only what they see and can reach
                return false;
            }
            return true;
        }

        /// <summary>K248d: u's order keeps this sniper where it stands: no waypoint, or an attack / chase whose target is in this
        /// sniper's range (the spacing layer's own small moves are passed over).</summary>
        bool SettleOrderHere(ZObject u)
        {
            foreach (var wp in u.Waypoints)
            {
                if (wp.mode == WpMode.Move && !wp.playerGiven && wp.refId < 0) continue;       // a nudge, a step aside, a back-off, a regroup, a waiting bay
                if (wp.mode == WpMode.Agro || wp.mode == WpMode.Attack)
                {
                    var t = World.Get(wp.refId);
                    return t != null && !t.Destroyed && t.KillAt < 0 && WithinAttackRadius(t);
                }
                return false;                                                                    // any other order walks it
            }
            return true;
        }

        double snSettleNext = double.NegativeInfinity;

        /// <summary>K248d, for the rules' own stance: <see cref="SniperFightSettled"/>, a "not yet" looked at again only every
        /// <see cref="ZWorld.SniperSettleLookSecs"/> s of game time (a "yes" is always fresh: it goes down on it at once).</summary>
        internal bool SniperStanceSettled()
        {
            if (!ZWorld.SniperDownToStay || World == null) return true;
            if (World.Time < snSettleNext - 1e-9) return false;
            bool ok = SniperFightSettled();
            snSettleNext = ok ? double.NegativeInfinity : World.Time + ZWorld.SniperSettleLookSecs;
            if (!ok) ZWorld.SniperStoodToWalkOn++;
            return ok;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>K248d switch (ZObject.SniperSettle.cs): a sniper goes down only where its fight needs no more walking. false =
        /// K248b's stance rules exactly. A view rule unless the rules' own stance is in the game (then it is part of that rule).</summary>
        public static bool SniperDownToStay = true;
        /// <summary>K248d: an enemy this many px beyond the agro reach (range + agro_distance) still keeps a sniper standing (it is about
        /// to step into the reach). Chosen with the counted check (Editor/OpenZParity.V11SniperStay.cs).</summary>
        public static int SniperSettleMarginPx = 16;
        /// <summary>K248d: a "not yet" is looked at again after this many s.</summary>
        public static double SniperSettleLookSecs = 0.25;
        /// <summary>K248d statistics (checks): the rules' stance kept a sniper standing because its fight leads on (each look).</summary>
        public static int SniperStoodToWalkOn;
    }
}
