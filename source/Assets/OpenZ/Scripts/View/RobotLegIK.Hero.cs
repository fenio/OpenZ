using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Hero legs (DV108 hero squads, owner items 184 and 185.
    /// The heroes play the grunt's own clips through the humanoid retarget, and RobotLegIK pins their feet to the clips' foot goals. Those
    /// goals are relative to the body and scale with the human scale (the hips height), so on a body built unlike the BS-01 they land
    /// wrong:
    /// <list type="bullet">
    /// <item><b>Sideways.</b> The walk's body is turned ~16 deg against the walking line (the rifle stance), so the left foot's swing
    /// passes the body's middle line (the grunt's own walk does it too, by about a boot's width). Alan's hip joints are half as far
    /// apart for his size as a grunt's, so the same goals put his feet outside his hips at the touch-down and across his other foot in
    /// the swing: bow-legged with crossing feet. The goals' sideways offsets from the body are scaled by the hero's own hip width per leg
    /// length against the BS-01's (<see cref="RefHipPerLeg"/>; never wider, at least <see cref="HeroSideMin"/>), then each foot is
    /// kept on its own side, at least <see cref="HeroFeetGap"/> x his hip joint's distance from the middle and at most
    /// <see cref="HeroFeetOut"/> x it (a smooth clamp over <see cref="HeroFeetSoft"/> x it: no kink in the path).</item>
    /// <item><b>Size.</b> The stance lock, the swing lift and the pelvis drop work in metres sized for a grunt (MaxPelvisDrop 0.2 m,
    /// SwingLift 0.13 m, x the unit root's scale, which is 1 for the heroes: their size sits in the model). The heroes are 1.3-1.4 x a
    /// grunt with relatively longer legs and lower ankles, so a 0.2 m drop could not bend their legs enough: the legs locked straight
    /// and the ankles stopped up to 9 cm short of their goals. For a hero those distances are scaled by his leg length against the
    /// BS-01's (<see cref="RefLeg"/>, at most <see cref="HeroSizeMax"/>).</item>
    /// </list>
    /// Heights need no change: RobotLegIK already puts every foot at the robot's own bind ankle height (stance pins, the swing lift,
    /// landing, the ground clamp). Worked out off the engine on the walking clip (kit KH: legs/walk_sim.py, out/walk_counts.txt):
    /// before (Brad, Alan): a foot over the middle on 31 of 164 frames, ankles up to 9 cm short; after: 0 and 0, knees out of their
    /// bending plane 0 frames over 3 cm. Only robots that are heroes (ZObject.IsHero); every other robot is untouched. A/B in Play:
    /// <see cref="HeroFeet"/>.
    /// </summary>
    public partial class RobotLegIK
    {
        /// <summary>KH: the hero leg correction on (A/B in Play: false = the grunt's goals as they come).</summary>
        public static bool HeroFeet = true;
        /// <summary>Each foot at least this share of the hip joint's distance from the body's middle (and at most HeroFeetOut of it),
        /// eased over HeroFeetSoft of it.</summary>
        public static float HeroFeetGap = 0.8f, HeroFeetOut = 2.0f, HeroFeetSoft = 0.35f;
        /// <summary>The sideways share is never under this (a very narrow-hipped robot keeps some step width).</summary>
        public static float HeroSideMin = 0.4f;
        /// <summary>The hero size factor is never over this.</summary>
        public static float HeroSizeMax = 2f;
        /// <summary>The BS-01 grunt's leg (thigh + shin, world m at its prefab scale) and its hip joint's distance from the middle per leg
        /// length: the body the clips' goals fit (measured from BS01_Robot.fbx's avatar: legs 0.908 m x 1.0297, hip joints 0.157 m out).</summary>
        public const float RefLeg = 0.935f, RefHipPerLeg = 0.1728f;
        bool heroInit, heroOn; float heroHalf, heroSide = 1f, heroSize = 1f;

        /// <summary>Checks: this robot's hero numbers (on, hip joint's distance from the middle m, sideways share, size factor).</summary>
        public (bool on, float half, float side, float size) HeroNumbers => (HeroLegsOn(), heroHalf, heroSide, heroSize);

        bool HeroLegsOn()
        {
            if (!HeroFeet || !view || view.Obj == null || !view.Obj.IsHero || !anim) return false;
            if (!heroInit)
            {
                heroInit = true;
                var kL = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg); var kR = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                var fL = anim.GetBoneTransform(HumanBodyBones.LeftFoot); var fR = anim.GetBoneTransform(HumanBodyBones.RightFoot);
                if (!hipL || !hipR || !kL || !kR || !fL || !fR) return heroOn = false;
                // bone lengths and the pelvis width are the same in every pose (rigid bones): measured once, world m
                float leg = 0.5f * (Vector3.Distance(hipL.position, kL.position) + Vector3.Distance(kL.position, fL.position)
                                  + Vector3.Distance(hipR.position, kR.position) + Vector3.Distance(kR.position, fR.position));
                heroHalf = 0.5f * Vector3.Distance(hipL.position, hipR.position);
                float root = Mathf.Max(0.1f, view.transform.lossyScale.y);
                heroOn = leg > 1e-3f && heroHalf > 1e-3f;
                if (heroOn)
                {
                    heroSide = Mathf.Clamp(heroHalf / leg / RefHipPerLeg, HeroSideMin, 1f);
                    heroSize = Mathf.Clamp(leg / (RefLeg * root), 1f, HeroSizeMax);
                }
            }
            return heroOn;
        }

        /// <summary>StanceLockStep's distance scale for this robot: 1, a hero's leg length against the BS-01's.</summary>
        float HeroSize() => HeroLegsOn() ? heroSize : 1f;

        /// <summary>OnAnimatorIK, right after the clip goals are read (before the stance lock pins them): a hero's foot goals spread to his
        /// own hips and kept each on its own side of the body (see the class notes). bodyP = this frame's body, fwd = the robot's front.</summary>
        void HeroGoals(Vector3[] P, Vector3 bodyP, Vector3 fwd)
        {
            if (!HeroLegsOn()) return;
            var right = Vector3.Cross(Vector3.up, fwd); if (right.sqrMagnitude < 1e-8f) return; right.Normalize();
            float lo = HeroFeetGap * heroHalf, hi = HeroFeetOut * heroHalf, k = HeroFeetSoft * heroHalf;
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? -1f : 1f;                                                                 // the left foot's side is the robot's left
                float d = s * Vector3.Dot(P[i] - bodyP, right);                                              // + = on its own side
                float d2 = SoftFloor(d * heroSide, lo, k);
                if (hi > lo) d2 = -SoftFloor(-d2, -hi, k);
                P[i] += right * (s * (d2 - d));
            }
        }

        /// <summary>max(d, m), eased over [m - k, m + k] (value and slope continuous).</summary>
        static float SoftFloor(float d, float m, float k)
        {
            if (k <= 1e-6f) return Mathf.Max(d, m);
            if (d >= m + k) return d;
            if (d <= m - k) return m;
            float u = d - m + k; return m + u * u / (4f * k);
        }
    }
}
