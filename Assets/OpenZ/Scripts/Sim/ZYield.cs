using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// Vehicles first: a vehicle on an order is never held, slowed, turned or pushed by robots of its own team.
    /// <para>Zod has no unit collision (units pass through each other; only idle units are nudged apart, ScuffleUnits,
    /// zserver.cpp:974-1041), so a Zod vehicle is never held up by its own infantry. Under the OpenZ spacing gate
    /// (ZSeparation.cs) a vehicle inside a crowd of its own robots side-stepped, queued and stood: a robot that shoots or has an
    /// order was neither nudged nor asked aside, an idle one only at its walking pace.</para>
    /// <para>1. YieldAhead (the vehicle's gate, every step): each robot of its team in the strip the hull is about to drive
    /// through (YieldAheadPx ahead, the gate spacing wide) gets the layer's own step-aside MOVE (idle: TryShove; with an order:
    /// MakeWay, the order goes on afterwards). A robot walking ahead at the vehicle's speed or faster is left alone. Not in a
    /// lane zone or keep-clear area (bridges, gaps: nobody is sent walking there).</para>
    /// <para>2. The robot runs that step at Zod's running speed (run_unit_speed 1.8, zsettings.cpp:285; stamina untouched).</para>
    /// <para>3. The vehicle never waits for it (PassesOwn): a robot of its own team is no blocker in the vehicle's gate, the
    /// vehicle takes no share of an overlap push against it and is not moved off a shared pixel. A robot that has no free spot,
    /// or is not out of the way in time, is driven through: the vehicle keeps Zod's speed on its own path, the robot is pushed
    /// out sideways where there is room (PushApart) and is never hurt (no friendly fire; the run-over rule is for enemies only,
    /// ZWorld.RunOver.cs). While a robot steps aside the pair is exempt from the spacing both ways (PairExempt), so the robot
    /// may cross in front of the hull.</para>
    /// <para>4. A standing vehicle is not moved by robots of its own team either: they never ask it to step aside (TryShove; a robot
    /// leaving a building still may) and it takes no share of an overlap push against them: a robot under its hull is pushed out or
    /// walks to a free spot. On a bridge deck or in a gap a parked vehicle still clears the way (the queue's own rules).</para>
    /// <para>5. In a one-lane gap (ZLanes.cs) robots of its own team are no lane traffic for the vehicle: they neither close the lane
    /// against it, nor fill its approach, nor take the turn from it.</para>
    /// Enemy units, other vehicles, buildings and terrain block as before; bridge decks keep their reservation queue (ZBridges.cs).
    /// VehicleFirst = false: the spacing gate alone, as before.
    /// </summary>
    public sealed partial class ZPathing
    {
        /// <summary>Vehicles first (the game sets it at every start; false = the spacing gate alone, for A/B checks).</summary>
        public static bool VehicleFirst = true;
        /// <summary>The strip ahead of the vehicle that is cleared (px beyond its radius, at most its remaining path) and the longest
        /// time (s) a step-aside counts as one made for a vehicle (running speed, spacing exemption).</summary>
        public const double YieldAheadPx = 72, YieldSeconds = 4;
        /// <summary>Statistics (checks): step-aside moves given to robots for a vehicle of their team.</summary>
        public static int YieldsAsked;
        readonly List<ZObject> yieldAhead = new List<ZObject>();

        /// <summary>The robot is on a step-aside move made for a vehicle of its team (it runs; no spacing against moving vehicles of its team).</summary>
        internal static bool Yielding(ZObject r) =>
            VehicleFirst && r.World != null && r.World.Time < r.PS.vehicleYieldUntil && r.Waypoints.Count > 0 && r.Waypoints[0].refId == RefStepAside;

        /// <summary>A crewed vehicle on an order and a robot of its own team: the vehicle is never blocked, pushed or unstacked by it.</summary>
        internal static bool PassesOwn(ZObject v, ZObject r) =>
            VehicleFirst && v.Kind == ZKind.Vehicle && r.Kind == ZKind.Robot && r.Owner == v.Owner && v.Owner != Team.Neutral && v.Waypoints.Count > 0 && !Static(v);

        /// <summary>A crewed vehicle and a robot of its own team, whatever either is doing.</summary>
        internal static bool OwnHull(ZObject v, ZObject r) =>
            VehicleFirst && v.Kind == ZKind.Vehicle && r.Kind == ZKind.Robot && r.Owner == v.Owner && v.Owner != Team.Neutral && !Static(v);

        /// <summary>PairExempt: a moving vehicle and a robot of its team that is stepping aside for a vehicle.</summary>
        static bool YieldPair(ZObject v, ZObject r) =>
            v.Kind == ZKind.Vehicle && r.Kind == ZKind.Robot && r.Owner == v.Owner && Moving(v) && Yielding(r);

        /// <summary>GateCore, vehicles only: the robots of its team in the strip ahead (heading (hx, hy)) step aside.</summary>
        void YieldAhead(ZObject a, double hx, double hy)
        {
            if (a.Owner == Team.Neutral || InNarrowArea(a)) return;                          // bridges, gaps: nobody is sent walking there
            double cx = CX(a), cy = CY(a), speed = Math.Sqrt((double)a.Dx * a.Dx + (double)a.Dy * a.Dy);
            double len = Math.Min(YieldAheadPx, a.PathRemaining()) + Radius(a) + RobotRadius;   // (never past its own goal)
            Near((int)(cx + hx * len * 0.5), (int)(cy + hy * len * 0.5), (int)(len * 0.5) + MaxQuery, yieldAhead);
            for (int i = 0; i < yieldAhead.Count; i++)
            {
                var r = yieldAhead[i];
                if (r == a || r.Kind != ZKind.Robot || r.Owner != a.Owner || !Separable(r) || Static(r) || !r.CanMove) continue;
                if (r.PathInBuildingStage || r.FortRushOn || r.PathMode == WpMode.ForceMove || r.PathMode == WpMode.Dodge || r.PathMode == WpMode.Enter) continue;
                double rx = CX(r) - cx, ry = CY(r) - cy, along = rx * hx + ry * hy, off = Math.Abs(rx * hy - ry * hx);
                if (along <= 0 || along > len || off >= GateSpacing(a, r) + 2) continue;      // not in the strip ahead
                if (Walking(r) && r.Dx * hx + r.Dy * hy >= speed) continue;                   // walks ahead at least as fast: never caught up
                if (InNarrowArea(r)) continue;
                if (r.Waypoints.Count > 0 && r.Waypoints[0].refId == RefStepAside) { r.PS.vehicleYieldUntil = W.Time + YieldSeconds; continue; }   // stepping aside anyway: it runs
                if (W.Time < r.PS.nextShove) continue;                                         // asked a moment ago
                int before = r.PS.Shoves;
                if (r.Waypoints.Count == 0) TryShove(a, r, hx, hy); else MakeWay(a, r, hx, hy);
                if (r.PS.Shoves == before) continue;                                           // no free spot: the vehicle drives through (PassesOwn)
                r.PS.vehicleYieldUntil = W.Time + YieldSeconds; YieldsAsked++;
            }
        }
    }

    public partial class ZObject
    {
        /// <summary>Vehicles first (ZYield.cs): this robot runs a step-aside made for a vehicle of its team (the view's run cycle and footsteps).</summary>
        public bool YieldRunOn => ZPathing.Yielding(this);
    }
}
