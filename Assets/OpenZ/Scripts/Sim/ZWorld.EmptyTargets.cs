namespace OpenZ.Sim
{
    /// <summary>
    /// DV81 "Ctrl to attack an empty vehicle or gun". A vehicle or gun with nobody inside belongs to the null team. Zod's order
    /// table (zplayer.cpp:2746-2881) gives every selected vehicle and gun an ATTACK order on anything that is not of its team, the
    /// null team included (:2797-2826), while robots get ENTER (:2837-2843); the cursor shows ENTER as
    /// soon as one robot is selected (:2235), also when the vehicles of the same selection will attack. So one right-click on an
    /// empty gun the player wanted to man could shell it.
    /// <para>With DV81 a right-click on an empty vehicle or gun is an attack order only while Ctrl is held, the same key that
    /// attacks a neutral building. Without Ctrl: robots get the ENTER order as before, vehicles a plain move to the clicked point
    /// (they stop next to it), guns no order. With Ctrl: ATTACK for every selected unit, robots included.</para>
    /// <para>EmptyClickWaypoint and EmptyClickCursor are the two decisions of the player's input (View/PlayerInput.cs BuildWaypoint,
    /// UI/OpenZCursors.cs Decide) and of the DV81 probe (Editor/OpenZParity.CtrlAttackEmpty.cs); the simulation's tick never calls
    /// them. What units choose by themselves is unchanged (nothing picks the null team: ZObject.CheckPassiveEngage, CheckAttackTo),
    /// and so are the CPU's orders (ZBot.Command: ENTER on an empty vehicle or gun, never ATTACK).</para>
    /// <para>One rule of the simulation belongs to DV81: DropQueuedAttacksOnEmptied. When a crew is shot out, Zod drops only the
    /// FIRST waypoint of every attacker (zserver.cpp:1849-1870); an attack order that waits further back in a unit's list would
    /// later shell the empty vehicle without Ctrl. For human teams those waiting orders are dropped as well; CPU teams keep Zod's
    /// rule, so the CPU's game is the same.</para>
    /// </summary>
    public sealed partial class ZWorld
    {
        /// <summary>DV81 switch (code; on by default). Off = Zod's order table, cursor and waypoint clean-up. The parity report
        /// switches it off for its Zod rows (Editor/OpenZParity.cs), like every other rule of the simulation that differs from Zod.</summary>
        public static bool CtrlAttackEmptyDeviation = true;

        /// <summary>Checks: waiting attack orders DropQueuedAttacksOnEmptied has dropped since the program started (all worlds).
        /// Only counted, never read by a rule.</summary>
        public static int StaleEmptyOrdersDropped;

        /// <summary>DV81 in the rules: <paramref name="v"/> has just lost its crew (called by ZWorld.UpdateObjectDriverHealth, in
        /// front of Zod's own loop). Zod's loop drops the attack on v only when it is a unit's FIRST waypoint. An ATTACK or AGRO
        /// waypoint on v that waits further back (a Shift queue, or an order pushed back by an inserted step aside, back-off, dodge
        /// or rock blast) stayed in the list, and the unit attacked the empty vehicle or gun later, without Ctrl. For the units of
        /// a human team every such waiting waypoint is dropped here; the first waypoint is left to Zod's loop, which runs next.
        /// CPU teams are not touched.</summary>
        void DropQueuedAttacksOnEmptied(ZObject v)
        {
            if (!CtrlAttackEmptyDeviation || v == null) return;
            foreach (var o in Objects)
            {
                if (o.Waypoints.Count < 2 || !IsHumanTeam(o.Owner)) continue;
                for (int i = o.Waypoints.Count - 1; i >= 1; i--)
                {
                    var q = o.Waypoints[i];
                    if ((q.mode == WpMode.Attack || q.mode == WpMode.Agro) && q.refId == v.Id) { o.Waypoints.RemoveAt(i); StaleEmptyOrdersDropped++; }
                }
            }
        }

        /// <summary>A vehicle or gun with nobody inside (the null team) that still stands.</summary>
        public static bool IsEmptyUnit(ZObject h) =>
            h != null && !h.Destroyed && h.Owner == Team.Neutral && (h.Kind == ZKind.Vehicle || h.Kind == ZKind.Cannon);

        /// <summary>DV81, the order of a right-click on <paramref name="h"/> for the selected unit <paramref name="u"/>.
        /// <paramref name="wp"/> comes in as the click's base waypoint (a move to the clicked point that refers to h).
        /// True = h is an empty vehicle or gun and wp is final: with Ctrl the ATTACK order on h; without Ctrl, for a vehicle or a
        /// gun, a plain move to the clicked point (a gun cannot move: the rules refuse it, ZWorld.CheckWaypoint).
        /// False = not an empty vehicle or gun, the rule is off, or a robot without Ctrl: the caller's own table goes on
        /// (a robot gets ENTER there, as before).</summary>
        public static bool EmptyClickWaypoint(ZObject u, ZObject h, bool ctrl, ref ZWaypoint wp)
        {
            if (!CtrlAttackEmptyDeviation || u == null || !IsEmptyUnit(h)) return false;
            if (ctrl) { wp.mode = WpMode.Attack; wp.refId = h.Id; return true; }
            if (u.Kind == ZKind.Robot) return false;
            wp.mode = WpMode.Move; wp.refId = -1;
            return true;
        }

        /// <summary>DV81, the cursor over <paramref name="h"/> for the current selection (robots: a robot is selected; canMove,
        /// canAttack, explosives: any selected unit can). Null = not an empty vehicle or gun, or the rule is off: the caller's own
        /// table decides. Without Ctrl never "attack": "enter" with a robot, else "move", guns only "cannon". With Ctrl "attack",
        /// or "nono" when nothing selected can attack it.</summary>
        public static string EmptyClickCursor(ZObject h, bool ctrl, bool robots, bool canMove, bool canAttack, bool explosives)
        {
            if (!CtrlAttackEmptyDeviation || !IsEmptyUnit(h)) return null;
            if (ctrl) return !canAttack || (!explosives && h.AttackedByExplosives) ? "nono" : "attack";
            if (robots && canMove) return "enter";
            return canMove ? "move" : "cannon";
        }
    }
}
