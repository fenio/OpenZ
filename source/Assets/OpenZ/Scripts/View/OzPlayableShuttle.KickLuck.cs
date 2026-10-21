using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K411, the kickflip by itself (view only; the draw and its rules: View/OzShuttleKickflip.Luck.cs; the flip: View/OzShuttleKickflip.Core.cs
    /// and View/OzPlayableShuttle.Kickflip.cs). A normal move order this ship takes, a single right-click (resolved where the double click is,
    /// in this frame's LateUpdate) or a Shift queue once it is sent (the rules' goal turns to the queue's first point), draws ONCE whether it
    /// arms the flip by itself (<see cref="OzKickflipLuck.Chance"/>, 5 %), when its flight is far enough by the double click's rule. The
    /// flip it arms is the double click's flip in every way (the steady cruise at 80 % of the top speed, the room, the whole arc on screen,
    /// the curve, trails and sounds). Here: what the view knows and the plain part does not (crashing, a hit, the landing; the tests' switch),
    /// and the sound log line.
    /// View only: the rules never know about any of it.
    /// </summary>
    public sealed partial class OzPlayableShuttle
    {
        OzKickflipQueued kickLuckQ;
        double kickLuckHealth = double.NaN, kickLuckHitAt = -1e9;
        /// <summary>K411 proof: why the last far order did not draw (empty = it drew), the flips armed by luck in this ship's life.</summary>
        public string KickLuckWhy { get; private set; } = "";
        public int KickLuckArms => flip.LuckPicks;

        /// <summary>OzShuttleKickflip.OrderClick: a Shift queue click with this ship in the selection (no order yet: the queue is sent when
        /// Shift is let go, and a shuttle takes its first point).</summary>
        internal void KickflipQueued(int px, int py)
        {
            if (Obj != null) kickLuckQ.Mark(px, py, Obj.ShuttleGoalX, Obj.ShuttleGoalY, Time.unscaledTime);
        }

        /// <summary>KickflipBeforeJets, every frame before this frame's click is resolved: a hit is noted; a sent Shift queue whose first point
        /// (or, where the ship flies the whole queue, its last point) is now the rules' goal is an order (it draws as a single click does).</summary>
        void KickLuckFrame(ZObject o, double remainPx)
        {
            double now = game.World.Time, h = o.Health;
            if (!double.IsNaN(kickLuckHealth) && h < kickLuckHealth - 1e-9) kickLuckHitAt = now;
            kickLuckHealth = h;
            if (kickClickFrame == Time.frameCount) { kickLuckQ.Pending = false; return; }                   // a plain click in this frame: its own way
            if (!kickLuckQ.Pending) return;
            var (cx, cy) = game.World.ShuttleClamp(kickLuckQ.X, kickLuckQ.Y);
            var (lx, ly) = game.World.ShuttleClamp(kickLuckQ.LastX, kickLuckQ.LastY);                 // the queue's last point: the goal where the ship flies the whole queue
            if (kickLuckQ.Resolve(o.ShuttleGoalX, o.ShuttleGoalY, cx, cy, lx, ly, Time.unscaledTime)) KickLuckOrder(true, remainPx / 8.0, 0, true);
        }

        /// <summary>The order of this frame's click (or of a sent queue): took = this ship took it; e0 = what the double click's own rule did
        /// with it (EvArmed: it armed the flip). Returns OzKickflipState.EvArmed | EvLuck when it armed the flip by luck.</summary>
        int KickLuckOrder(bool took, double distM, int e0, bool queued = false)
        {
            var o = Obj;
            if (!took || o == null || game == null || game.World == null) return 0;
            bool busy = KickLuckBusy(out string why), off = !OzKickflipLuck.On || KickLuckTestsOff();
            int far0 = flip.LuckFar, rep0 = flip.LuckRepeatBlocked;
            int ev = flip.LuckOrder(o.ShuttleGoalX, o.ShuttleGoalY, distM, (e0 & OzKickflipState.EvArmed) != 0, busy, off);
            if (flip.LuckFar == far0) return ev;                                                       // the same order again, or a near one: nothing drawn
            KickLuckWhy = (ev & OzKickflipState.EvLuck) != 0 ? "" : (e0 & OzKickflipState.EvArmed) != 0 ? "double click" : off ? "off" : flip.LuckRepeatBlocked > rep0 ? "right after a flip" : busy ? why : "";
            if ((ev & OzKickflipState.EvLuck) != 0)
                OzSoundLog.Add("FLIP", "shuttle_kickflip", null, $"{(queued ? "queue" : "order")} #{o.Id} (order {flip.LuckOrders}): {distM:F1} m, draw {flip.LuckLastDraw:F3} < {OzKickflipLuck.Chance:F2}: ARMED by luck", 0f, transform.position);
            return ev;
        }

        /// <summary>Crashing or breaking up, hit within OzKickflipLuck.HitCalmSec s, or coming down to land (or to let a squad out): no draw,
        /// and a flip armed by luck waits.</summary>
        bool KickLuckBusy(out string why)
        {
            var o = Obj; why = null;
            if (o == null || o.Destroyed || o.KillAt >= 0 || dying || crashing || BrokenUp) why = "crashing";
            else if (game != null && game.World != null && game.World.Time - kickLuckHitAt < OzKickflipLuck.HitCalmSec) why = "just hit";
            else if (o.ShuttleState == ZShuttleState.Descend || o.ShuttleLandTurning || o.ShuttleExitOnLanding) why = "landing";
            return why != null;
        }

        /// <summary>The start test's extra for a flip armed by luck (KickflipBeforeJets' OnScreen): it waits while the ship is busy.</summary>
        bool KickLuckHold() => flip.LuckArmed && flip.Phase == OzKickflipPhase.Armed && KickLuckBusy(out _);

        /// <summary>The Play proofs that click orders and count flips switch the luck off while they run (their own checks expect no flip
        /// without a double click).</summary>
        static bool KickLuckTestsOff()
        {
#if UNITY_EDITOR
            return OzShuttleKickflipProof.Report.StartsWith("running") || OzShuttleTouchesProof.Report == "running";
#else
            return false;
#endif
        }
    }
}
