using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K433 (item 432: a missile cannon once stayed turned away from laser robots and fired its rockets over its own back, then later
    /// turned the right way). View only; the rules never read a turret direction.
    ///
    /// Cause: SolveGunFor (View/UnitAim.cs) turns a turret with several barrels (missile cannon 4 tubes, MLRS 8, heavy tank 2) by the
    /// MEAN of the barrels' signed yaw errors toward the aim point. With the target behind the turret, a left barrel sees it at about
    /// +178 deg and a right barrel at about -178 deg (the barrels sit a metre or so apart), so the mean is about 0: the turret does not
    /// turn. Within a few degrees of straight behind (+-3 to 6 deg, wider the closer the target) the mean even pulls the turret back to
    /// exactly 180 deg away, a stable lock that follows a walking target. Every shot is then held to its wait cap, the snap at the cap runs
    /// the same solve, and the rocket leaves tubes that point away from the target. It ends when the aim point leaves that narrow band
    /// (a scattered rocket, another target), which is why it was rare and turned right later.
    ///
    /// Fix: SameSide takes every barrel's error on the same side of +-180 as the first barrel's, so the mean is about 180 and the turret
    /// turns round (the shortest way, its usual slew). Whenever the barrels' errors do not straddle +-180 (every other case) the values,
    /// their order and the sum are exactly as before. The Proof members serve the Play proof only (View/OzTurretBehindProof.cs).
    /// </summary>
    public partial class UnitAim
    {
        /// <summary>The fix (on). The Play proof turns it off for its one comparison trial and back on at once.</summary>
        public static bool BehindFixOn = true;
        /// <summary>How many barrel errors were moved to the first barrel's side (the proof's count that the case happened).</summary>
        public static int BehindFixed;

        /// <summary>A barrel's signed yaw error `a` taken on the same side of +-180 as the first barrel's `first`.</summary>
        static float SameSide(float a, float first)
        {
            if (!BehindFixOn) return a;
            if (a - first > 180f) { BehindFixed++; return a - 360f; }
            if (a - first < -180f) { BehindFixed++; return a + 360f; }
            return a;
        }

        /// <summary>Proof: the world heading (deg, 0 = +z, clockwise from above) of the barrels' mean direction as drawn now; false without a gun rig.</summary>
        public bool ProofBarrelHeading(out float heading)
        {
            heading = 0f;
            if (robot || muzzles.Count == 0) return false;
            Vector3 s = Vector3.zero;
            foreach (var m in muzzles) if (m.t) s += MuzzleDir(m);
            s.y = 0f;
            if (s.sqrMagnitude < 1e-6f) return false;
            heading = Mathf.Atan2(s.x, s.z) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>Proof: turns the turret at rest so its barrels' mean points along `heading` (world, deg); false without a yaw joint.</summary>
        public bool ProofSetHeading(float heading)
        {
            if (robot || yawJ == null || !yawJ.t || !yawJ.t.parent) return false;
            for (int i = 0; i < 3; i++)
            {
                if (!ProofBarrelHeading(out float h)) return false;
                yawJ.ang = Wrap(yawJ.ang + Mathf.DeltaAngle(h, heading)); yawJ.vel = 0f;
                Pose(yawJ.ang, pitchJ != null ? pitchJ.ang : 0f, 0f);
            }
            return true;
        }

        /// <summary>Proof: the point the gun aims at now (a held shot, the last shot, the attack target or the predicted one).</summary>
        public bool ProofTarget(out Vector3 p) => Target(out p, out _, out _, out _);

        /// <summary>Proof: the turret's turning point (the yaw joint, else the unit).</summary>
        public Vector3 ProofPivot => yawJ != null && yawJ.t ? yawJ.t.position : transform.position;

        /// <summary>Proof: the gun's aim tolerance (deg).</summary>
        public float ProofTolerance => slew.tol;
    }
}
