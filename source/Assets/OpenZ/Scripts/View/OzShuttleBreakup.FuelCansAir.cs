using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The rocket fuel cans of an AIR break (burst, split, the rifles' crack) tumble out of the broken hull and fall with the wreck, where
    /// the camera looks (view only; the cans: OzFuelCan.cs, their places: OzShuttleBreakup.FuelCans.cs). They used to copy the speed of a
    /// light piece of the same break; since the big blast throws those pieces much harder (OzShuttleBreakup.BigBlast.cs) and each its own
    /// way (OzShuttleBreakup.ThrowVary.cs), the cans flew 20-70 m out, fast and spread thin, through the fireball's own sparks and metal
    /// bits, and read as nothing from the game camera. Now each air can gets its own small throw out of the hull, along its own way out:
    /// <see cref="CanAirOut0"/>..<see cref="CanAirOut1"/> m/s out and <see cref="CanAirUp0"/>..<see cref="CanAirUp1"/> m/s up (the crack x
    /// <see cref="CanAirCrackK"/>), plus a little of the killing round's push (x <see cref="CanAirShoveK"/>) and the ship's own way: they
    /// drop out under the fireball and land among the wreck's pieces. The ground death and the drop keep their throw (a light piece's
    /// speed: those are not thrown harder). Same seed per death as before (the draws stay in a fixed order).
    /// Switch: <see cref="CanAirOwn"/> (off = the air cans copy a light piece's speed again).
    /// </summary>
    public sealed partial class OzShuttleBreakup
    {
        /// <summary>Switch: the air cans' own tumble out of the hull (on).</summary>
        public static bool CanAirOwn = true;
        /// <summary>The air cans' throw out of the hull (m/s, relative to the ship): out, up; the crack's factor; the share of the round's push.</summary>
        public static float CanAirOut0 = 3f, CanAirOut1 = 6f, CanAirUp0 = 0.5f, CanAirUp1 = 2.5f, CanAirCrackK = 0.75f, CanAirShoveK = 0.35f;

        /// <summary>FuelCans' loop: one air can's velocity (the ship's way + its own throw along dir + a little of the round's push).
        /// Three draws from the death's own generator, always in this order.</summary>
        static Vector3 CanAirThrow(System.Random rnd, Vector3 vel, Vector3 dir, Vector3 shove, string how)
        {
            float k = how == OzShuttleDeathPlan.Crack ? CanAirCrackK : 1f;
            float vo = (CanAirOut0 + (float)rnd.NextDouble() * (CanAirOut1 - CanAirOut0)) * k;
            float vu = (CanAirUp0 + (float)rnd.NextDouble() * (CanAirUp1 - CanAirUp0)) * k;
            float sk = CanAirShoveK * (0.8f + 0.4f * (float)rnd.NextDouble());
            return vel + dir * vo + Vector3.up * vu + shove * sk;
        }

        /// <summary>FuelCans' end: the batch keeps how the cans left the ship (the Play check reads it): the ship's way, the fastest throw
        /// out and the throw up (relative to the ship), the cans' mean height over the ground at the start.</summary>
        static OzFuelCan.Batch NoteCanThrow(OzFuelCan.Batch bt, Vector3 vel, bool air)
        {
            if (bt == null) return null;
            bt.shipVel = vel; bt.ownAirThrow = air && CanAirOwn;
            var g = ZGame.I; float hSum = 0f; int n = 0;
            for (int i = 0; i < canVel.Count && i < canAt.Count; i++)
            {
                var rel = canVel[i] - vel; float up = rel.y; rel.y = 0f;
                bt.relOutMax = Mathf.Max(bt.relOutMax, rel.magnitude); bt.relUpMin = Mathf.Min(bt.relUpMin, up); bt.relUpMax = Mathf.Max(bt.relUpMax, up);
                var p = canAt[i]; hSum += g ? p.y - g.GroundY(p.x, p.z) : 0f; n++;
            }
            bt.dropH = n > 0 ? hSum / n : 0f;
            return bt;
        }
    }
}
