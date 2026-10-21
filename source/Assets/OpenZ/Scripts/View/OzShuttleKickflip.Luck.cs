using System;

namespace OpenZ
{
    /// <summary>
    /// K411, the kickflip by itself (an easter egg, view only): the plain part, shared by the game (View/OzPlayableShuttle.KickLuck.cs), the
    /// counted check (Editor/OzShuttleKickflipCount.K411.cs) and the kit's headless runner. Players who never try the double right-click
    /// would never see the kickflip, so a normal move order (a single right-click, or a Shift queue once it is sent) whose flight is far
    /// enough by the double click's own rule (<see cref="OzKickflipState.FarM"/>) has a small chance (<see cref="Chance"/>, 5 %) to arm
    /// exactly the same flip. From there everything is the double click's flip: it starts only in the steady cruise at 80 % of the top
    /// speed or more, with the room read and the whole arc on screen, the same curve, trails and sounds.
    /// <list type="bullet">
    /// <item>ONE DRAW PER ORDER, decided when the order is taken (never per frame): a new order = a goal more than
    /// <see cref="OzKickflipGesture.DoubleM"/> m from the last one this ship was given (the second click of a double click, a third click,
    /// the same destination clicked again are the same order: no second draw).</item>
    /// <item>DETERMINISTIC: the draw is a hash of the ship's id and its order count (<see cref="Draw"/>), no random generator, no clock: the
    /// same orders give the same flips on every run.</item>
    /// <item>NEVER TWICE IN A ROW: an order right after a far order whose flip was flown (armed by luck or by the double click, and started)
    /// never draws; a lucky flip that never got to start (the ship off screen all the way, a new order first) does not count. So of all far
    /// orders about Chance / (1 + Chance) flip by luck (4.8 % at 5 %).</item>
    /// <item>NEVER while the ship is crashing or breaking up, within <see cref="HitCalmSec"/> s of a hit, or while it is coming down to land
    /// (the view tells: busy); an armed lucky flip also waits (does not start) while that is so.</item>
    /// <item>OFF: <see cref="On"/> = false (tests), or <see cref="Chance"/> = 0.</item>
    /// </list>
    /// No engine type, nothing allocated per frame. The rules never know about it.
    /// </summary>
    public static class OzKickflipLuck
    {
        /// <summary>The chance (0..1) that a far normal order arms the flip by itself.</summary>
        public static double Chance = 0.05;
        /// <summary>The switch: false = no flip by itself (the double click works as always).</summary>
        public static bool On = DefaultOn;
        /// <summary>K436: off by default: the rules' evasive self-flip (DV129, Sim/ZWorld.ShuttleKick.cs) took its place.</summary>
        public const bool DefaultOn = false;
        /// <summary>No draw, and an armed lucky flip waits, this long after a hit (s of game time).</summary>
        public static double HitCalmSec = 3.0;
        /// <summary>A Shift queue's first point counts as the order when the rules' goal turns to it within this long (s of real time).</summary>
        public static double QueueWaitSec = 60.0;
        /// <summary>The knobs' defaults (the Play check sets 100 % and puts these back).</summary>
        public const double DefaultChance = 0.05;

        /// <summary>The draw for a ship's n-th order: 0 .. 1 (never 1), the same for the same (id, n) on every run (a 64-bit mix of both).</summary>
        public static double Draw(int shipId, int orderNo)
        {
            unchecked
            {
                ulong z = ((ulong)(uint)shipId << 32) ^ (uint)orderNo ^ 0x4B1C_F11Bul;
                z += 0x9E3779B97F4A7C15ul;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
                z ^= z >> 31;
                return (z >> 11) * (1.0 / 9007199254740992.0);
            }
        }
    }

    /// <summary>K411: a Shift queue click remembered until the queue is sent and the rules' goal turns to its first point (a shuttle takes
    /// the first move point of a sent queue) or, where the shuttle flies the whole queue (its goal is then the queue's LAST point and the
    /// points before it are flown through), to its last point. Plain: the view keeps one per ship.</summary>
    public struct OzKickflipQueued
    {
        public bool Pending;
        public int X, Y, LastX, LastY, GoalBeforeX, GoalBeforeY;
        public double At;

        /// <summary>A queue click at (px, py) (px), the rules' goal now (gx, gy), now = real time (s). The queue's first click is kept (and its
        /// time); every later click of the same queue only moves its last point.</summary>
        public void Mark(int px, int py, int gx, int gy, double now)
        {
            if (Pending) { LastX = px; LastY = py; return; }
            Pending = true; X = px; Y = py; LastX = px; LastY = py; GoalBeforeX = gx; GoalBeforeY = gy; At = now;
        }

        /// <summary>As <see cref="Resolve(int, int, int, int, int, int, double)"/> with the first point only.</summary>
        public bool Resolve(int gx, int gy, int cx, int cy, double now) => Resolve(gx, gy, cx, cy, cx, cy, now);

        /// <summary>Each frame: true once, on the frame the rules' goal (gx, gy) turned away from the goal at the click to (near) the queue's
        /// first point (cx, cy) or its last point (lx, ly) (each as the rules clamp it; the rules may move it to a free spot within
        /// ArmedRetargetM m). A goal that turned elsewhere, or no change within QueueWaitSec, forgets the click.</summary>
        public bool Resolve(int gx, int gy, int cx, int cy, int lx, int ly, double now)
        {
            if (!Pending) return false;
            if (now - At > OzKickflipLuck.QueueWaitSec || now < At) { Pending = false; return false; }
            if (gx == GoalBeforeX && gy == GoalBeforeY) return false;
            Pending = false;
            double dx = gx - cx, dy = gy - cy, ex = gx - lx, ey = gy - ly;
            return Math.Sqrt(dx * dx + dy * dy) / 8.0 <= OzKickflipState.ArmedRetargetM || Math.Sqrt(ex * ex + ey * ey) / 8.0 <= OzKickflipState.ArmedRetargetM;
        }
    }

    public sealed partial class OzKickflipState
    {
        /// <summary>K411: the event bit of a flip armed by luck (with EvArmed).</summary>
        public const int EvLuck = 2048;
        /// <summary>K411 counts: new orders seen, far ones, near ones (no draw), draws made, flips armed by luck, far orders right after a
        /// flown flip (no draw), far orders while busy (no draw), far orders with the switch off, far orders armed by the double click.</summary>
        public int LuckOrders, LuckFar, LuckShort, LuckDraws, LuckPicks, LuckRepeatBlocked, LuckBusyBlocked, LuckOffSkipped, LuckDoubleArmed;
        /// <summary>K411: the flip armed (or waiting, or flipping) now was armed by luck; the last draw (-1 = none).</summary>
        public bool LuckArmed { get; private set; }
        public double LuckLastDraw { get; private set; } = -1;
        bool luckSeen, luckPrevFarArmed; int luckLastX, luckLastY, luckStartsAtArm;

        /// <summary>The last far order armed a flip and that flip was flown (started): the next far order does not draw.</summary>
        bool LuckLastFlown => luckPrevFarArmed && Starts > luckStartsAtArm;
        void LuckMarkArmed(bool lucky) { luckPrevFarArmed = true; luckStartsAtArm = Starts; LuckArmed = lucky; }

        /// <summary>K411: an order this ship took (the rules' goal is now (gx, gy); distM = the ship's line point to it). armedNow = the double
        /// click armed the flip for it in this frame; busy = crashing, just hit or coming down to land; off = the switch off for now.
        /// Returns EvArmed | EvLuck when the order armed the flip by luck, else 0.</summary>
        public int LuckOrder(int gx, int gy, double distM, bool armedNow, bool busy, bool off)
        {
            if (luckSeen && Near(gx, gy, luckLastX, luckLastY))
            {   // the same order (the double click's second click, a third click, the destination clicked again): no second draw
                if (armedNow) { LuckMarkArmed(false); LuckDoubleArmed++; }
                return 0;
            }
            luckSeen = true; luckLastX = gx; luckLastY = gy; LuckOrders++;
            if (distM < FarM) { LuckShort++; return 0; }
            LuckFar++;
            if (armedNow) { LuckMarkArmed(false); LuckDoubleArmed++; return 0; }
            if (off || !OzKickflipLuck.On || !(OzKickflipLuck.Chance > 0)) { LuckOffSkipped++; luckPrevFarArmed = false; return 0; }
            if (LuckLastFlown) { LuckRepeatBlocked++; luckPrevFarArmed = false; return 0; }
            if (busy) { LuckBusyBlocked++; luckPrevFarArmed = false; return 0; }
            double u = OzKickflipLuck.Draw(Id, LuckOrders); LuckDraws++; LuckLastDraw = u;
            if (u >= OzKickflipLuck.Chance) { luckPrevFarArmed = false; return 0; }
            LuckPicks++; LuckMarkArmed(true);
            if (Phase == OzKickflipPhase.Flip || Phase == OzKickflipPhase.Settle) { pending = true; pendX = gx; pendY = gy; }   // after the flip on (as a double click there)
            else Arm(gx, gy);
            return EvArmed | EvLuck;
        }
    }
}
