using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Vehicles never glide
    /// sideways (VehicleSlip). The drawn vehicle follows its sim point like a vehicle, the hull steering into any sideways offset
    /// (tracks may pivot in place, wheeled vehicles steer with speed); the jeep's weave is steered, no longer a sideways offset;
    /// wheels / tracks roll with the drawn speed along the hull (UnitView.Sync reads Slip.AlongSpeed).
    /// </summary>
    public partial class UnitView
    {
        VehicleSlip slip;
        /// <summary>vehicles_nb_v1: the no-sideways-glide follower of this vehicle (null for robots / cannons / before the first frame).</summary>
        public VehicleSlip Slip => slip;

        /// <summary>View spacing weight of `self` against `other`: a standing vehicle is a wall for robots (they step round it; a real
        /// vehicle is not shoved sideways by infantry, and it could not follow the shove without crabbing), else the unit's weight.</summary>
        static float PairWeight(UnitView self, UnitView other)
        {
            if (VehicleSlip.Enabled && self.Obj != null && other.Obj != null && self.Obj.Kind == ZKind.Vehicle && other.Obj.Kind == ZKind.Robot
                && (!(self.Obj.IsMoving && !self.Obj.PathHeld) || (ZPathing.VehicleFirst && other.Obj.Owner == self.Obj.Owner))) return 0f;   // vehicles first: a moving one is not pushed by robots of its own team either (ZYield.cs)
            return self.weight;
        }

        /// <summary>Sync (vehicles), right after HullSteer: the drawn position + hull yaw of a vehicle that cannot crab.</summary>
        Vector3 NoCrab(Vector3 p, bool moving, float speed01, Vector3 moveDir, float dt)
        {
            if (!VehicleSlip.Enabled || Obj.Kind != ZKind.Vehicle || dt <= 0f) return p;
            if (Obj.Type == "jeep" && moving)
            {   // jeeps race: the same light weave across the path as before, now as a point the jeep STEERS after
                swerve += dt * 2.3f;
                p += Vector3.Cross(Vector3.up, moveDir) * Mathf.Sin(swerve) * 0.22f * speed01;
            }
            slip ??= new VehicleSlip(VehicleSlip.Of(Obj.Type));
            if (Obj.UnmannedHeld || (ZWorld.UnmannedStopDeviation && !moving && CaptureHold.IsHeld(Obj.Id)))
            {   // DV79 unmanned stop: nobody inside: the picture stops with the rules, in this frame (no catch-up drive, no pivot onto the sim spot).
                // The same while a robot is still climbing in (CaptureHold, NB17: the vehicle is still painted nobody's) and the rules do not
                // move it: the offset the hold left is driven off only once the robot sits and the vehicle shows its team colour
                slip.Hold(p.x, p.z, yaw);
                yaw = slip.Yaw;
                if (hullSteer != null) hullSteer.Adopt(yaw);
                return new Vector3(slip.X, p.y, slip.Z);
            }
            float gs = game ? Mathf.Max(0.25f, game.gameSpeed) : 1f;
            float sim = moving ? Mathf.Sqrt(Obj.Dx * Obj.Dx + Obj.Dy * Obj.Dy) / 8f * gs : 0f;            // m per real second
            bool rev = hullSteer != null && hullSteer.Reversing;
            float simYaw = moving && moveDir.sqrMagnitude > 1e-6f ? Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg : float.NaN;
            bool standing = slip.Step(p.x, p.z, yaw, sim, simYaw, rev, dt);
            yaw = slip.Yaw;
            if (standing && hullSteer != null) hullSteer.Adopt(yaw);                                     // the committed heading = where it really stands
            return new Vector3(slip.X, p.y, slip.Z);
        }
    }
}
