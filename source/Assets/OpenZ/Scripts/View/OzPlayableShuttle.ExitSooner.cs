using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K426 (rule DV118, Sim/ZWorld.ShuttleExitSooner.cs: EXIT is taken from the touchdown and carried out as soon as the ship stands level),
    /// the view's part: the rear ramp may now start down while the legs' suspension still settles (a normal landing's ramp starts at the
    /// touchdown, when the hull still rides its unloaded legs; a crash's at the level moment, in the level settle's dip). The ramp's open
    /// angle is solved once, when an opening begins (ShuttleRearDoor.SolveOpen): solved on the hull as it is drawn in that frame, a ramp
    /// started on unloaded legs rests its tip up to the legs' travel (0.42 m) INTO the ground once the hull has settled. It is now solved on
    /// the PARKED pose (the hull's suspension offset and settle rock taken out), the pose the hull settles into, so the tip lands on the
    /// ground there. Without the visible suspension rig (K256g's fallback springs) nothing changes. View only.
    /// </summary>
    public sealed partial class OzPlayableShuttle
    {
        /// <summary>Proof: ramp openings solved on the parked pose, and the largest hull offset taken out at such a solve (m).</summary>
        public static int RampParkedSolves;
        public static float RampParkedLiftMax;
        static int rampSolveFrame = -1;

        /// <summary>ShuttleRearDoor.SolveOpen: the world point of root-space point p (the ramp's underside turned to a trial angle) with the
        /// ship standing PARKED on its legs (its drawn suspension offset and settle rock taken out). Any other view, or no rig: root's own.</summary>
        public static Vector3 RampSolvePoint(ZObject o, Transform root, Vector3 p)
        {
            var ship = Of(o);
            if (!ship || !root || ship.transform != root || ship.susRig == null) return root ? root.TransformPoint(p) : p;
            if (rampSolveFrame != Time.frameCount) { rampSolveFrame = Time.frameCount; RampParkedSolves++; RampParkedLiftMax = Mathf.Max(RampParkedLiftMax, Mathf.Abs(ship.susX)); }
            var rk = ship.susRk;
            var q = root.rotation * Quaternion.Inverse(rk);
            var basePos = root.position - q * (new Vector3(0f, ship.susX, 0f) + SuspRockPivot - rk * SuspRockPivot);
            return basePos + q * Vector3.Scale(p, root.lossyScale);
        }
    }
}
