using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// Idle rest, deviation DV75. It is part of the driver-sniping deviation DC2 and uses its switch (ZWorld.SnipeDeviation): in
    /// 'Zod exact' nothing in this file ever sets a state.
    /// Zod: a tank's lid opens only when the tank engages a target that can snipe (4 of 5 times, zvehicle.cpp:198-207), closes
    /// 0-1.4 s after it lets go of it (zvehicle.cpp:209-240), and a tank can be sniped only while the lid is open
    /// (zvehicle.cpp:178-186). A hit is a driver hit when one roll passes attack_snipe_chance (zobject.cpp:2187-2199).
    /// OpenZ adds: a crewed vehicle with a roof hatch (ZWorld.IdleRestTypes) that has been calm for ZWorld.IdleRestSeconds, plus its
    /// own 0..IdleRestSpreadSeconds so a parked row does not open in one frame, rests: Resting is true, the view opens the hatch and
    /// shows the driver head and shoulders out. Calm = no waypoint, not moving, no target, not stunned, no health and no driver
    /// health lost. A vehicle Zod lets be sniped with its lid open (light / medium / heavy) can be sniped while its crew rests
    /// (ZObject.CanBeSniped) and the snipe threshold of that hit is x ZWorld.RestSnipeFactor, capped at ZWorld.RestSnipeCap
    /// (RestSnipeChance). The other types only show the driver. An order ends the rest at once. When an enemy wakes the crew (an
    /// agro or dodge waypoint, a target in range, a stun) the rest ends ZWorld.IdleRestWakeSeconds later; once the vehicle is hit
    /// it ends ZWorld.IdleRestHitWakeSeconds after that hit. So the first shots of an ambush find the hatch open, and no more
    /// than those. Veteran crews stay buttoned up (DV12) and never rest. Both teams, CPU vehicles included.
    /// Zod's lid (LidOpen, its pending close, its roll) is never written here. Deterministic: sim time and object ids only, no
    /// rand() draw, no view state.
    /// </summary>
    public partial class ZObject
    {
        double restCalmSince = -1, restWakeAt = -1, restHitSeen = -100;
        int restHp, restDriverHp;

        /// <summary>DV75: the crew rests in the open hatch. View: hatch open, driver out. Rule: CanBeSniped and RestSnipeChance.</summary>
        public bool Resting { get; private set; }
        /// <summary>DV75: sim time this rest began (-1 = not resting). View and checks.</summary>
        public double RestingSince { get; private set; } = -1;

        /// <summary>ProcessServer, every tick, before the stun hold (a stunned vehicle is woken too). Reads what the last tick left:
        /// orders, movement, the target, damage. Hits are resolved in the shooters' own step, so every shot of the tick that wakes
        /// the crew still found the hatch open.</summary>
        void RestStep(double time)
        {
            if (Kind != ZKind.Vehicle) return;
            int drv = Drivers.Count > 0 ? Drivers[0] : 0;
            bool hit = Health < restHp || drv < restDriverHp || LastEnemyHit > restHitSeen;
            restHp = Health; restDriverHp = drv; restHitSeen = LastEnemyHit;
            if (!ZWorld.SnipeDeviation || Drivers.Count == 0 || Owner == Team.Neutral || VeteranActive || World.GameOver || !ZWorld.IdleRestTypes.Contains(Type))
            { RestEnd(-1); return; }
            bool order = Waypoints.Count > 0;
            bool busy = order || IsMoving || AttackObject != null || cgTarget != null || Stunned || hit;
            if (!Resting)
            {
                if (busy || restCalmSince < 0) { restCalmSince = time; return; }                       // the calm starts (again) now
                if (time - restCalmSince < ZWorld.IdleRestSeconds + ZWorld.IdleRestSpreadSeconds * Math.Abs(Id * 37 % 100) / 100.0) return;
                Resting = true; RestingSince = time; restWakeAt = -1; World.RestsStarted++;
                return;
            }
            if (!busy && restWakeAt < 0) return;                                                         // still calm
            if (order && Waypoints[0].mode != WpMode.Agro && Waypoints[0].mode != WpMode.Dodge) { RestEnd(time); return; }   // an order: down at once
            if (restWakeAt < 0) restWakeAt = time + ZWorld.IdleRestWakeSeconds;                          // woken by an enemy: the crew needs a moment
            if (hit && restWakeAt > time + ZWorld.IdleRestHitWakeSeconds) restWakeAt = time + ZWorld.IdleRestHitWakeSeconds;   // under fire: down almost at once
            if (time >= restWakeAt) RestEnd(time);
        }

        void RestEnd(double calmFrom) { Resting = false; RestingSince = -1; restWakeAt = -1; restCalmSince = calmFrom; }

        /// <summary>DV75: the snipe threshold sc of a hit on t, raised while t's crew rests (x RestSnipeFactor, capped at RestSnipeCap;
        /// a threshold already above the cap is kept). Zod exact: sc.</summary>
        internal static double RestSnipeChance(ZObject t, double sc) =>
            ZWorld.SnipeDeviation && t != null && t.Resting ? Math.Max(sc, Math.Min(ZWorld.RestSnipeCap, sc * ZWorld.RestSnipeFactor)) : sc;
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV75: vehicle types whose crew rests in the hatch. light / medium / heavy are Zod's lid tanks and can be sniped
        /// while resting. The crane only shows its driver (Zod never lets him be sniped; unchanged). The missile launcher is left
        /// out: its roof hatch is a boarding frame and a figure standing in it is cut by that frame.</summary>
        public static readonly HashSet<string> IdleRestTypes = new HashSet<string> { "light", "medium", "heavy", "crane" };
        /// <summary>DV75: seconds of calm before a crew rests; each vehicle adds its own 0..IdleRestSpreadSeconds (from its id).</summary>
        public static double IdleRestSeconds = 25.0, IdleRestSpreadSeconds = 8.0;
        /// <summary>DV75: seconds between an enemy waking a resting crew (seen, in range, a stun) and the end of the rest: long
        /// enough for a squad that walks up to get into its own range (a vehicle sees it 40 px earlier).</summary>
        public static double IdleRestWakeSeconds = 4.0;
        /// <summary>DV75: seconds between a hit on a resting vehicle and the end of the rest (at most IdleRestWakeSeconds after the
        /// crew was woken). Counted: with 0.1-0.2 the first two volleys of an ambush find the hatch open.</summary>
        public static double IdleRestHitWakeSeconds = 0.2;
        /// <summary>DV75: the snipe threshold of a hit on a resting driver is x RestSnipeFactor, at most RestSnipeCap (Zod: one
        /// attack_snipe_chance per attacker against every target, zobject.cpp:2190).</summary>
        public static double RestSnipeFactor = 1.5, RestSnipeCap = 0.9;
        /// <summary>DV75 statistics (checks): rests begun in this world.</summary>
        public int RestsStarted;
    }
}
