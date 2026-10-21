using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// DV113 "Ctrl: Pyros burn buildings and bridges". Zod: a building (forts, factories, the radar, the repair station, bridges) takes
    /// explosives only (SetClassFlags: AttackedByExplosives, zbuilding.cpp:17-19), and a unit without explosives cannot attack it
    /// (CanAttackObject, zobject.cpp:4767-4777): a Pyro's flame never touched one.
    /// <para>With DV113 the player's attack order given with Ctrl held (ZWaypoint.forceFire, set by View/PlayerInput.cs BuildWaypoint) on a
    /// standing building or bridge that is not the player's own is taken by the Pyros of the selection: they walk into range and flame it
    /// with their own weapon, unchanged (Zod's pyro: 104 HP a hit at 70 %, every 0.1 s, 120 px range: about 730 HP a second for one Pyro,
    /// a squad of four brings a factory or a bridge of 83333 HP down in about half a minute), until it falls (0 HP, as when explosives
    /// bring it down: a factory stops and rebuilds itself later, a bridge collapses, a fort's team is out) or the player gives another order.
    /// The flame, the sticky fire on the building, the burning ground, the light, the smoke and the sounds are the Pyro's existing ones
    /// (View/Pyro/OpenZPyroFire.cs: a hit on a building burns as a structure).</para>
    /// <para>Unchanged: an order without Ctrl (refused, as in Zod); every unit that is not a Pyro; a Pyro squad with grenades (it has
    /// explosives and throws them, as before); map items (rocks, trees, huts, props, wrecks: explosives only); CPU teams (the CPU never
    /// gives the mark, and the rule asks for a human team). Units never pick a building by themselves (the passive engage and the
    /// attack-move picks look at units only), so the player's order is the only way in.</para>
    /// </summary>
    public partial class ZObject
    {
        /// <summary>DV113 in CanAttackObject (asked only when <paramref name="o"/> takes explosives only and this unit has none): this
        /// unit may flame o now. A Pyro of a human team (the rule on) and a building or bridge that a waypoint of the unit's own list
        /// attacks with the player's Ctrl mark. Any waypoint of the list counts, not only the first, so a short step aside the
        /// pathing puts in front of the order does not drop the target; the list is the squad leader's copy (CloneMinionWayPoints),
        /// so every Pyro of the squad flames the same building.</summary>
        bool PyroBurnsNow(ZObject o)
        {
            if (!PyroBurnUnit(o)) return false;
            foreach (var w in Waypoints) if (IsPyroBurnOrder(w, o)) return true;
            return false;
        }

        /// <summary>DV113 in ZWorld.CheckWaypoint (SEND_WAYPOINTS): the attack waypoint <paramref name="wp"/> on <paramref name="t"/>
        /// is valid for this unit although CanAttackObject says no (the waypoint is not in the unit's list yet). The other checks of
        /// CanAttackObject stay: a weapon, a standing target, not of the unit's own team.</summary>
        internal bool PyroBurnOrderOk(ZObject t, ZWaypoint wp) =>
            t != null && CanAttack() && !t.Destroyed && Owner != t.Owner && PyroBurnUnit(t) && IsPyroBurnOrder(wp, t);

        bool PyroBurnUnit(ZObject o) =>
            ZWorld.PyroForceBurnDeviation && IsRobot && Type == "pyro" && o != null && o.Kind == ZKind.Building && World != null && World.IsHumanTeam(Owner);

        static bool IsPyroBurnOrder(ZWaypoint w, ZObject o) => w.mode == WpMode.Attack && w.refId == o.Id && w.forceFire && w.playerGiven;
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV113 switch. Static like the other rule switches: on by default; View/ZGame.cs sets it at every start (off in
        /// 'Zod exact', oz_zod_exact = 1). The parity report's other rows never give the Ctrl mark, so they are the same either way.</summary>
        public static bool PyroForceBurnDeviation = true;

        /// <summary>DV113, the cursor (UI/OpenZCursors.cs Decide): with Ctrl held over a standing building or bridge that is not the
        /// player's own, the ATTACK cursor when the selection holds a Pyro of the player's (it will flame it), where the explosives-only
        /// rule showed NO.</summary>
        public static bool PyroBurnCursor(ZObject h, IEnumerable<ZObject> selection, Team player, bool ctrl)
        {
            if (!PyroForceBurnDeviation || !ctrl || h == null || h.Destroyed || h.Kind != ZKind.Building || h.Owner == player || selection == null) return false;
            foreach (var u in selection)
                if (u != null && !u.Destroyed && u.Owner == player && u.IsRobot && u.Type == "pyro" && u.CanAttack()) return true;
            return false;
        }
    }
}
