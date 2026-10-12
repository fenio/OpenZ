using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// "modern RTS orders" (Z had none of these; Shift-queue, Ctrl+0-9, double-click and attack-move already exist):
    ///  * PATROL: a looping attack-move between where the unit stood (A) and the clicked point (B). Each leg is Zod's own attack-move
    ///    waypoint (MOVE with attackTo, zobject.cpp:3285-3339 CheckAttackTo: an enemy in agro range gets an ATTACK waypoint in front, the
    ///    leg resumes after the fight); when a leg ends the next one is issued toward the other end, for ever, until any new order.
    ///    Kept as a patrol record on the unit (not a new WpMode: WpMode mirrors zobject.h's modes and every Zod move path stays the same).
    ///  * STOP: Zod's empty SEND_WAYPOINTS (the unit stops where it is; it still fights what comes into range).
    ///  * HOLD POSITION: stop, and no automatic walking: passive engagement only fires at enemies already in range (no AGRO chase, no
    ///    auto-grab of flags / vehicles / crates, no squad help call), until any new order.
    /// Pure sim, deterministic (no random draw of its own, fixed object order; the leg is issued inside the unit's own tick), commands
    /// through ZWorld like every player and CPU order (the bot may use them; multiplayer-ready). Off (PatrolDeviation) = the commands
    /// are refused and nothing changes.
    /// </summary>
    public partial class ZObject
    {
        bool patrolOn, patrolToB; int patrolAx, patrolAy, patrolBx, patrolBy;
        /// <summary>DV68: patrolling (the two ends in map px), holding position.</summary>
        public bool Patrolling => patrolOn;
        public int PatrolAx => patrolAx; public int PatrolAy => patrolAy; public int PatrolBx => patrolBx; public int PatrolBy => patrolBy;
        public bool HoldPosition { get; internal set; }
        /// <summary>Checks: patrol legs issued by this unit.</summary>
        public int PatrolLegs { get; private set; }

        internal void SetPatrol(int ax, int ay, int bx, int by) { patrolOn = true; patrolToB = true; patrolAx = ax; patrolAy = ay; patrolBx = bx; patrolBy = by; }
        internal void ClearOrderModes() { patrolOn = false; HoldPosition = false; }

        /// <summary>ProcessServer, before the waypoint step: a patrolling leader whose leg ended (no waypoint, no target) turns round.</summary>
        void PatrolStep()
        {
            if (!patrolOn || !ZWorld.PatrolDeviation) return;
            if (IsMinion || Destroyed || !CanMove) { patrolOn = false; return; }
            if (Waypoints.Count > 0 || AttackObject != null) return;
            patrolToB = !patrolToB;
            int x = patrolToB ? patrolBx : patrolAx, y = patrolToB ? patrolBy : patrolAy;
            var w = ZWaypoint.Move(x, y, CanAttack()); w.playerGiven = true;
            Waypoints.Add(w);
            PatrolLegs++;
            World.Pathing?.OnOrder(this);
            CloneMinionWayPoints();
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV68 (CTL1): patrol / stop / hold-position orders. Off = refused (Zod has none).</summary>
        public static bool PatrolDeviation = true;

        /// <summary>PATROL from where the unit stands to (bx, by) and back, for ever (until any new order).</summary>
        public bool SendPatrol(Team team, ZObject o, int bx, int by)
        {
            if (!PatrolDeviation || o == null || !o.CanMove) return false;
            int ax = o.CenterX, ay = o.CenterY;
            if (!SendWaypoints(team, o, new[] { ZWaypoint.Move(bx, by, true) })) return false;
            if (o.Waypoints.Count == 0) return false;
            o.SetPatrol(ax, ay, bx, by);
            return true;
        }

        /// <summary>STOP: Zod's empty waypoint list.</summary>
        public bool SendStop(Team team, ZObject o) => SendWaypoints(team, o, new List<ZWaypoint>());

        /// <summary>HOLD POSITION: stop, then fire only at enemies in range (no chase, no auto-grab, no help call).</summary>
        public bool SendHold(Team team, ZObject o)
        {
            if (!PatrolDeviation || !SendWaypoints(team, o, new List<ZWaypoint>())) return false;
            o.HoldPosition = true; foreach (var m in o.Minions) if (m != null) m.HoldPosition = true;
            return true;
        }
    }
}
