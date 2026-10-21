using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine.InputSystem;

namespace OpenZ
{
    /// <summary>
    /// K410 (rule DV125): the Shift click's points for a shuttle that is already flying go AFTER its route. A unit's SEND_WAYPOINTS replaces its
    /// whole list (the rules, as for every unit), so when the queue is sent (PlayerInput.Flush, first) every shuttle in it that still flies
    /// toward its goal gets the route it still has to fly put in front of the new points: its middle points left, then its goal. The command
    /// carries the whole list, so every peer and a replay see the same order (nothing here is read by the rules).
    ///  * A Shift click while it flies: added at the end (it flies on through its goal, now a middle point, to the new points).
    ///  * A plain click: replaces the route (nothing is put in front).
    ///  * Standing (nothing to fly), hovering over its goal, coming down or turning over its landing spot: the Shift points are the route.
    /// The route drawing (View/SelectionOverlay.ShuttleQueue.cs) shows the same: the route still to fly, then the Shift points.
    /// </summary>
    public static class OzShuttleQueueInput
    {
        static bool shiftSet;
        static readonly List<ZWaypoint> tmp = new List<ZWaypoint>();
        /// <summary>Checks: queues sent with the route still to fly put in front, and their points in all.</summary>
        public static int Appended, AppendedPoints;

        /// <summary>PlayerInput, the right button's order click: first = the pending queue was empty (a new set of clicks begins), queueClick
        /// = the click belongs to a Shift queue.</summary>
        public static void Click(bool first, bool queueClick) { if (first) shiftSet = queueClick; else shiftSet |= queueClick; }

        /// <summary>The ship flies toward its goal now (a Shift point goes after its route): a take-off held, climbing, or cruising toward its
        /// goal (or through middle points).</summary>
        public static bool Flying(ZObject o)
        {
            if (o == null || !o.IsShuttle || o.Destroyed || o.KillAt >= 0 || o.ShuttleGoalX < 0) return false;
            if (o.ShuttleState == ZShuttleState.Landed) return o.ShuttleTakeOffHeld;
            if (o.ShuttleState == ZShuttleState.Climb) return true;
            if (o.ShuttleState != ZShuttleState.Cruise || o.ShuttleLandTurning || o.ShuttleDeckTurning) return false;
            if (o.ShuttleViaCount > 0) return true;
            double dx = o.ShuttleGoalX - o.ShuttleBaseX, dy = o.ShuttleGoalY - o.ShuttleBaseY;
            return dx * dx + dy * dy > 16.0 * 16.0;
        }

        /// <summary>The route the ship still has to fly as move waypoints (its middle points left, then its goal).</summary>
        public static void Remaining(ZObject o, List<ZWaypoint> into)
        {
            for (int i = 0; i < o.ShuttleViaCount; i++) into.Add(new ZWaypoint { mode = WpMode.Move, x = o.ShuttleViaX(i), y = o.ShuttleViaY(i), playerGiven = true });
            into.Add(new ZWaypoint { mode = WpMode.Move, x = o.ShuttleGoalX, y = o.ShuttleGoalY, playerGiven = true });
        }

        /// <summary>PlayerInput.Flush, first: after a Shift queue, every flying shuttle's list gets its route still to fly in front.</summary>
        public static void Prefix(ZWorld w, Dictionary<ZObject, List<ZWaypoint>> pending)
        {
            bool shift = shiftSet; shiftSet = false;
            if (!shift || w == null || pending == null || !ZWorld.ShuttleQueueDeviation) return;
            foreach (var kv in pending)
            {
                if (kv.Value == null || kv.Value.Count == 0 || !Flying(kv.Key)) continue;
                tmp.Clear(); Remaining(kv.Key, tmp);
                kv.Value.InsertRange(0, tmp);
                Appended++; AppendedPoints += kv.Value.Count;
            }
        }

        /// <summary>OzShuttleOrderAim.Cursor: while Shift is held a click may be a middle point (flown over, anywhere): no NO cursor then
        /// (the queue's landing, its last point, is tested when it is sent).</summary>
        public static bool CursorFree()
        {
            var kb = Keyboard.current;
            return ZWorld.ShuttleQueueDeviation && kb != null && kb.shiftKey.isPressed;
        }
    }
}
