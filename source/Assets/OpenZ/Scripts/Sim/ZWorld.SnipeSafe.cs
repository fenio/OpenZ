using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV82 "no driver sniping on a bridge or in a door".
    /// <para>Zod: a hit of a sniping weapon on a vehicle whose driver can be hit (zvehicle.cpp:178-186) is a driver hit when one
    /// roll passes attack_snipe_chance (zobject.cpp:2187-2199), wherever the vehicle stands; the driver's death leaves the vehicle
    /// empty on the spot (zobject.cpp:334-359). Zod has no unit collision, so an empty hull was never in anybody's way.</para>
    /// <para>OpenZ keeps units apart, and an empty hull is an obstacle nothing pushes (ZPathing.Static). A bridge deck is two
    /// tiles wide between its rails, exactly one vehicle (bbridge.cpp:168-190): a hull emptied on it closes the bridge for every
    /// vehicle until a robot gets into it. A hull emptied in a fort gate or a factory door stands where the next new unit comes
    /// out.</para>
    /// <para>DV82: a hit on a vehicle that stands on such a spot is never a driver hit. The snipe roll is still drawn (the number
    /// of random draws does not change); a roll that would have hit the driver hits the hull instead, with the hull damage and
    /// the sound of any other hit. The spots: (1) the vehicle's box reaches into a standing bridge's own width, on the deck or up
    /// to <see cref="SnipeSafeBridgeReach"/> px before either end: the area the pathing layer keeps clear for the deck's lane
    /// (ZLanes.cs keep-clear area: approach 40 px + 48 px, the deck's width + 1 tile each side), where a parked hull stops the
    /// traffic of the bridge; (2) the vehicle's
    /// centre in the exit lane of a fort, a robot factory or a vehicle factory, the same lane DV66 keeps free of parked vehicles
    /// (from the point a new unit appears on, through the exit point and <see cref="ExitLaneReach"/> px on,
    /// <see cref="ExitLaneHalf"/> px + the chassis radius either side). Other buildings (repair station, radar) have no such
    /// lane and are not protected. Guns are not vehicles and do not move: unchanged. The driver's rest (DV75), the tank lid, the
    /// chances and the driver's health are unchanged everywhere else, and a driver who leaves the spot can be hit again at once.</para>
    /// Deterministic: positions only, no random draw of its own. Switch: ZWorld.SnipeSafeSpotDeviation (the game turns it on at
    /// every start, off in 'Zod exact'; headless default off, so the parity rows and every earlier count run as before).
    /// </summary>
    public sealed partial class ZWorld
    {
        public static bool SnipeSafeSpotDeviation = false;
        /// <summary>DV82: how far (px) before each end of a bridge the rule reaches along its axis: the pathing layer's keep-clear
        /// area of a bridge lane (ZPathing.LaneApproach 40 px + its 48 px margin, ZLanes.cs AddLane).</summary>
        public const int SnipeSafeBridgeReach = 88;
        /// <summary>DV82 statistics (checks): snipe rolls that passed and were turned into hull hits by the rule (all worlds).</summary>
        public static int SnipesHeld;

        /// <summary>DV82: v is a vehicle on a bridge or in the door lane of a fort or factory (see the class notes). Positions only: asked by the shot
        /// (ZObject.ProcessAttackDamage) after the snipe roll, and by checks.</summary>
        public bool SnipeSafeSpot(ZObject v)
        {
            if (!SnipeSafeSpotDeviation || v == null || v.Kind != ZKind.Vehicle) return false;
            int x0 = v.LocX, y0 = v.LocY, x1 = v.LocX + v.WidthPix, y1 = v.LocY + v.HeightPix, reach = SnipeSafeBridgeReach;
            double r = ZPathing.VehicleRadiusOf(v.Type);
            foreach (var b in Objects)
            {
                if (b.Kind != ZKind.Building) continue;
                if (b.IsBridge)
                {
                    if (b.Destroyed) continue;
                    int ax = b.Type == "bridge_horz" ? reach : 0, ay = b.Type == "bridge_horz" ? 0 : reach;   // along its axis only
                    if (x0 < b.LocX + b.WidthPix + ax && b.LocX - ax < x1 && y0 < b.LocY + b.HeightPix + ay && b.LocY - ay < y1) return true;
                    continue;
                }
                int cx, cy, ex, ey;
                if (!ExitLine(b, out cx, out cy, out ex, out ey)) continue;                           // forts and factories only (DV66's exit line)
                double dx = ex - cx, dy = ey - cy, l = Math.Sqrt(dx * dx + dy * dy); if (l < 1e-6) continue;
                double ux = dx / l, uy = dy / l, len = l + ExitLaneReach;
                double t = Math.Max(0, Math.Min(len, (v.CenterX - cx) * ux + (v.CenterY - cy) * uy));
                double qx = cx + ux * t - v.CenterX, qy = cy + uy * t - v.CenterY, lim = ExitLaneHalf + r;
                if (qx * qx + qy * qy < lim * lim) return true;
            }
            return false;
        }

        /// <summary>DV82, ProcessAttackDamage: the snipe roll of a hit on t has passed; true = the rule turns it into a hull hit.</summary>
        internal bool SnipeHeld(ZObject t)
        {
            if (!SnipeSafeSpot(t)) return false;
            if (SnipesHeld < int.MaxValue) SnipesHeld++;
            return true;
        }
    }
}
