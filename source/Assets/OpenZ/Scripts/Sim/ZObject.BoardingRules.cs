using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// (ZGame sets them at every start from PlayerPrefs; both off in 'Zod exact' oz_zod_exact = 1 and in every parity Zod row). Pure sim:
    /// sim ticks (ZWorld.TickIndex), the pixel grid, fixed object order, no rand() draw, so with both off the sim is the previous build bit
    /// for bit.
    /// <list type="bullet">
    /// <item>
    /// Zod blocks only the ejected crew from cannons (just_left_cannon, until its next order);
    /// any other idle robot within auto_grab_vehicle_distance walks straight back in (zobject.cpp:1603-1853). With DV58 a cannon whose
    /// crew a HUMAN player ejected (EJECT_VEHICLE, ZWorld.Eject) is locked for EjectLockTicks (15 s of sim time): no unit of any team
    /// auto-enters it (the idle auto-grab, DV55's closest-grabber pick, an auto ENTER waypoint already on its way); a player click or a
    /// CPU bot order (SEND_WAYPOINTS ENTER, playerGiven) still enters at once. The CPU never ejects (zbot.cpp has no EJECT_VEHICLE).</item>
    /// <item>
    /// Zod ends an ENTER waypoint the moment its target can no longer be entered (zobject.cpp:2584-2667:
    /// KillWP), so a squad sent after a vehicle that another squad got into first, or that was destroyed or captured, freezes wherever it
    /// stands mid-path. With DV59 a human player's ENTER order on such a target turns into a plain MOVE to the free spot next to the
    /// target on the squad's side (the target's last spot when it is gone): the squad walks on in formation and stops there; the first
    /// to arrive still gets in (one driver; an APC takes one passenger squad). CPU squads keep Zod's KillWP (bot-vs-bot balance, like
    /// DV35).</item>
    /// </list>
    /// </summary>
    public partial class ZObject
    {
        // ================================================================== DV58 eject lock
        /// <summary>DV58: sim tick until which no unit auto-enters this cannon (-1 = none).</summary>
        public long NoAutoEnterUntilTick = -1;
        /// <summary>DV58: 15 s at Zod's 100 Hz server tick.</summary>
        public const long EjectLockTicks = 1500;
        /// <summary>DV58: this cannon is locked against auto entries right now (explicit orders still enter).</summary>
        public bool AutoEnterLocked => ZWorld.EjectLockDeviation && NoAutoEnterUntilTick >= 0 && World != null && World.TickIndex < NoAutoEnterUntilTick;

        // ================================================================== DV59 enter falls back to a walk
        /// <summary>DV59, ProcessEnterWP: the target can no longer be entered. True = handled (turned into a MOVE, or a minion waits for
        /// its leader to do it this tick); false = Zod's KillWP.</summary>
        bool EnterFallback(ZObject t, ZWaypoint wp)
        {
            if (!ZWorld.EnterFallbackDeviation || !wp.playerGiven || !World.IsHumanTeam(Owner)) return false;
            if (IsMinion)
            {   // the leader turns the squad's order this tick (cloned to the minions at the side effects): wait for it
                var l = Leader;
                return l != null && !l.Destroyed && (l.UpdatedWaypoints || (l.Waypoints.Count > 0 && l.Waypoints[0].mode == WpMode.Enter && l.Waypoints[0].refId == wp.refId));
            }
            FallbackGoal(t, wp, out int gx, out int gy);
            var mv = new ZWaypoint { mode = WpMode.Move, refId = -1, x = gx, y = gy, attackTo = false, playerGiven = true };
            Waypoints[0] = mv;
            UpdatedWaypoints = true;                                                        // ZWorld.Tick side effects clone it to the minions
            PS.ClearPending(); foreach (var m in Minions) m?.PS.ClearPending();               // a fresh order starts clean (as SendWaypoints)
            World.EnterFallbacks++;
            return true;
        }

        /// <summary>DV59: where the squad walks instead. The target still there (captured, entered, a wreck): the reachable spot next to
        /// it nearest to this unit (8 directions, half its size + 12 px out); gone: the order's last spot.</summary>
        void FallbackGoal(ZObject t, ZWaypoint wp, out int gx, out int gy)
        {
            gx = wp.x; gy = wp.y;
            if (t == null) return;
            int cx = t.CenterX, cy = t.CenterY, r = Math.Max(t.WidthPix, t.HeightPix) / 2 + 12, d = (r * 181) >> 8;   // 181/256 = 1/sqrt 2
            long best = long.MaxValue;
            for (int k = 0; k < 8; k++)
            {
                int px = cx, py = cy;
                switch (k)
                {
                    case 0: px += r; break; case 1: px += d; py += d; break; case 2: py += r; break; case 3: px -= d; py += d; break;
                    case 4: px -= r; break; case 5: px -= d; py -= d; break; case 6: py -= r; break; default: px += d; py -= d; break;
                }
                if (px < 8 || py < 8 || px >= World.Path.WidthPix - 8 || py >= World.Path.HeightPix - 8) continue;
                if (!World.Path.ShouldBeAbleToMoveTo(CenterX, CenterY, px, py, IsRobot)) continue;
                long dd = (long)(px - CenterX) * (px - CenterX) + (long)(py - CenterY) * (py - CenterY);
                if (dd < best) { best = dd; gx = px; gy = py; }
            }
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary></summary>
        public static bool EjectLockDeviation = true;
        /// <summary>DV59 a player's enter order on a vehicle that was taken / destroyed on the way becomes a walk there. PlayerPrefs
        /// oz_enter_fallback.</summary>
        public static bool EnterFallbackDeviation = true;
        /// <summary>DV59 statistics (checks): enter orders turned into a walk.</summary>
        public int EnterFallbacks;
        /// <summary>DV58 statistics (checks): cannons locked after a player's eject.</summary>
        public int EjectLocks;

        /// <summary>DV58, EJECT_VEHICLE on a cannon by a human player: nobody auto-enters it for EjectLockTicks.</summary>
        void LockAutoEnter(Team team, ZObject v)
        {
            if (!EjectLockDeviation || v == null || v.Kind != ZKind.Cannon || !IsHumanTeam(team)) return;
            v.NoAutoEnterUntilTick = TickIndex + ZObject.EjectLockTicks;
            EjectLocks++;
        }
    }
}
