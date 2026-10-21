using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Hero legs, part 2 (DV108 hero squads; the heroes play the grunt's clips through the humanoid retarget, see RobotLegIK.Hero.cs).
    /// <list type="bullet">
    /// <item><b>Lying on the start-camp lounger</b> (item 196). The sun-lounger lie clip is the grunt's: its feet lie apart in a V at the
    /// grunt's width, and the posed-legs pass (PosedLegs) bends each knee toward the boot's front captured from the standing boot. On the
    /// heroes' own bodies (other hip widths, other leg lengths, the lie started at once at the level start) that gave twisted, crossed
    /// lower legs. While a hero lies (the lounger antic's lying loop) his legs get a pose of their own instead of the clip's: each foot
    /// straight ahead of its OWN hip joint along the lounger (the legs parallel, nothing crossed), reaching <see cref="HeroLieReach"/> of
    /// the leg's length (straight, the knee a little up), the heels at the clip's own foot height (the lower legs reach out over the
    /// lounger's foot end, as the clip's raised boots do: the lounger ends at about the knees), the toes pointing up
    /// (tilted <see cref="HeroLieToeTilt"/> deg toward the foot end, no twist), each knee bending straight up. The lounger's direction is
    /// the robot's own front (the antic lays the robot along its bed); the side is the one the clip's feet lie on. Eased in and out over
    /// <see cref="HeroLieEase"/> s. Every other robot and every other antic: untouched. A/B in Play: <see cref="HeroLie"/>.</item>
    /// <item><b>Standing antics</b> (item 197, Brad's drink). The drink clip's own legs stand still (both feet
    /// 0.00 cm per key, the knees at most 0.3 cm, measured on the clip file), so nothing in the clip moves a leg. While a hero plays a
    /// standing antic whose clip keeps the feet still (<see cref="HeroHoldsFeet"/>: the drink, the phone, the neck stretch, the
    /// fishing; not the turn or the 360 scan, whose clips turn the hips and step the feet themselves, nor the lounger, which poses its
    /// own legs), each foot goal is held where it stood when the antic began, position and turn,
    /// in the robot's own frame (a shove of the whole robot carries both feet): at full weight from the antic's first frame (the held
    /// goal IS that frame's goal, so nothing jumps), kept through the cross-fade back to the standing idle and <see cref="HeroHoldTail"/> s
    /// after it, then handed back to the clip over <see cref="HeroHoldOut"/> s (the idle's own stance, where the feet already stand).
    /// Found: the drink starts from and ends in the standing idle's rifle stance (left foot 0.33 m forward, right 0.21 m back), while the
    /// drink's own stance is feet side by side; the two cross-fades glided each foot 0.2 to 0.3 m x his size (Brad x 1.46: up to 0.46 m)
    /// in 0.12 / 0.2 s: the legs jerked twice a drink (measured off the engine: Brad's feet up to 6.4 cm a frame). With the feet held a leg
    /// only moves as the body above it moves. A/B in Play: <see cref="HeroAnticFeet"/>.</item>
    /// </list>
    /// Counters for the Play checks: <see cref="HeroLieFrames"/>, <see cref="HeroHoldFrames"/>.
    /// </summary>
    public partial class RobotLegIK
    {
        /// <summary>KH2: the hero's own lying legs on the lounger (false: the clip's goals and the boot-front knee plane, as before).</summary>
        public static bool HeroLie = true;
        /// <summary>KH2: a hero's feet held still through a standing antic (false: the clip's goals frame by frame, as before).</summary>
        public static bool HeroAnticFeet = true;
        /// <summary>Share of the leg's length (thigh + shin) the lying foot reaches from its hip joint; the toes' tilt toward the foot
        /// end (deg); how far apart the feet lie as a multiple of the hip joints' distance (1 = straight ahead of each hip).</summary>
        public static float HeroLieReach = 0.985f, HeroLieToeTilt = 12f, HeroLieSpread = 1.0f;
        /// <summary>Ease (s) of the lying legs in and out; how long the held feet stay after the antic and its cross-fade ended, and their
        /// hand-back to the clip.</summary>
        public static float HeroLieEase = 0.3f, HeroHoldTail = 0.25f, HeroHoldOut = 0.35f;
        /// <summary>The held feet go to the walk over this (s) when the robot walks off (a walk / run state, or the root faster than
        /// HeroHoldMoveSpeed m/s x its scale).</summary>
        public static float HeroHoldWalkOut = 0.12f, HeroHoldMoveSpeed = 0.25f;
        /// <summary>Play checks: frames posed by the lying legs and by the held feet (all heroes).</summary>
        public static int HeroLieFrames, HeroHoldFrames;

        float heroLieW; bool heroLieInit; float[] heroLegLen; Vector3 heroLieBody; bool heroLieBodyOk;
        readonly Vector3[] heroLieP = new Vector3[2]; readonly Quaternion[] heroLieQ = new Quaternion[2]; int heroLieFrame = -1;
        float heroHoldW, heroHoldUntil; bool heroHoldOn; readonly Vector3[] heroHoldL = new Vector3[2]; readonly Quaternion[] heroHoldQ = new Quaternion[2];

        /// <summary>The standing antics whose feet are held: their clips keep both feet still (measured on the clip file: drink, crush,
        /// phone, neck stretch, fishing hold and reel 0.0 cm; the toss and the cast under 2 cm). The turn (the hips 36 deg) and the
        /// 360 scan (the hips a full turn, the feet stepping about 0.5 m) move the feet themselves: held, the legs would wind up and
        /// cross, so they keep the clip's goals. The lounger poses its own legs (PosedLegs).</summary>
        public static bool HeroHoldsFeet(AnticKind k) => k == AnticKind.Drink || k == AnticKind.Phone || k == AnticKind.Neck || k == AnticKind.Fish;

        /// <summary>Checks: the hold's weight (0 = the clip's goals, 1 = held) and the lie's weight on this robot.</summary>
        public float HeroHoldWeight => heroHoldW;
        public float HeroLieWeight => heroLieW;

        bool HeroLying()
        {
            if (!HeroLie || !view || view.Obj == null || !view.Obj.IsHero) return false;
            var a = view.Antic;
            return a != null && !a.Finished && a.Kind == AnticKind.Sunbed && a.Step == "sunbed_lie";
        }

        /// <summary>PosedLegs hook, per foot, right after the clip goal is read: a lying hero's own leg pose replaces the clip goal (eased by
        /// the lie weight). True while it acts (the knee plane then points up: <see cref="HeroLieKnee"/>).</summary>
        bool HeroLieGoal(int i, ref Vector3 p, ref Quaternion q, Vector3 bodyP, bool enter)
        {
            if (i == 0)
            {
                bool want = HeroLying();
                float dt = Time.deltaTime;
                // entering the posed pass already lying (the level start): at once (PosedLegs eases from the last standing frame itself)
                heroLieW = enter && want ? 1f : Mathf.MoveTowards(heroLieW, want ? 1f : 0f, dt / Mathf.Max(0.01f, HeroLieEase));
                if (enter) heroLieBodyOk = false;
                if (heroLieW > 0f && !HeroLiePose(bodyP)) heroLieW = 0f;
                heroLieBody = bodyP; heroLieBodyOk = true;
                if (heroLieW > 0f) HeroLieFrames++;
            }
            if (heroLieW <= 0f || heroLieFrame != Time.frameCount) return false;
            float w = Smooth(heroLieW);
            p = Vector3.Lerp(p, heroLieP[i], w); q = Quaternion.Slerp(q, heroLieQ[i], w);
            return true;
        }

        /// <summary>PosedLegs hook: the knee plane of a lying hero points straight up (blended by the lie weight).</summary>
        Vector3 HeroLieKnee(Vector3 dir)
        {
            float w = Smooth(heroLieW);
            var d = Vector3.Slerp(dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.up, Vector3.up, w);
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.up;
        }

        /// <summary>Both lying goals of this frame (world): see the class notes. False when the bones are missing.</summary>
        bool HeroLiePose(Vector3 bodyP)
        {
            if (!hipL || !hipR || !view) return false;
            if (!heroLieInit)
            {
                heroLieInit = true;
                var kL = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg); var kR = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                var fL = anim.GetBoneTransform(HumanBodyBones.LeftFoot); var fR = anim.GetBoneTransform(HumanBodyBones.RightFoot);
                if (kL && kR && fL && fR)                                                                    // rigid bones: measured once, world m
                    heroLegLen = new[] { Vector3.Distance(hipL.position, kL.position) + Vector3.Distance(kL.position, fL.position),
                                         Vector3.Distance(hipR.position, kR.position) + Vector3.Distance(kR.position, fR.position) };
            }
            if (heroLegLen == null) return false;
            // the hip joints this frame: last frame's bones carried by the body's move since (the lie is nearly still)
            var shift = heroLieBodyOk ? bodyP - heroLieBody : Vector3.zero;
            if (shift.sqrMagnitude > 0.25f) shift = Vector3.zero;
            var H = new[] { hipL.position + shift, hipR.position + shift };
            var mid = 0.5f * (H[0] + H[1]);
            var cL = anim.GetIKPosition(AvatarIKGoal.LeftFoot); var cR = anim.GetIKPosition(AvatarIKGoal.RightFoot);   // the clip's goals (nothing set yet this frame)
            var front = -view.transform.forward; front.y = 0f;                                                // robots face their -z
            if (front.sqrMagnitude < 1e-6f) return false;
            front.Normalize();
            var cm = 0.5f * (cL + cR) - mid; cm.y = 0f;
            var along = Vector3.Dot(cm, front) >= 0f ? front : -front;                                        // the lounger's line, toward the clip's feet
            var right = Vector3.Cross(Vector3.up, along);                                                      // lying on the back: the body's right
            float heelY = 0.5f * (cL.y + cR.y);
            float a = HeroLieToeTilt * Mathf.Deg2Rad;
            var toes = (Vector3.up * Mathf.Cos(a) + along * Mathf.Sin(a)).normalized;
            var top = (-along * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)).normalized;                         // the instep faces the head
            // the heroes' rigs face their -z: the foot goal's forward is the heel's side (in Play, toes as the forward hung the boots toes
            // down: the foot's top to the head, its front to the ground); so the goal looks away from the toes, the instep still to the head
            var footQ = Quaternion.LookRotation(-toes, top);
            for (int k = 0; k < 2; k++)
            {
                float side = Vector3.Dot(H[k] - mid, right) * HeroLieSpread;
                var hipLine = mid + right * side; hipLine.y = H[k].y;
                float reach = HeroLieReach * heroLegLen[k];
                float dv = Mathf.Clamp(H[k].y - heelY, -0.5f * reach, 0.5f * reach);
                float dh = Mathf.Sqrt(Mathf.Max(0f, reach * reach - dv * dv));
                heroLieP[k] = hipLine + along * dh + Vector3.down * dv;
                heroLieQ[k] = footQ;
            }
            heroLieFrame = Time.frameCount;
            return true;
        }

        /// <summary>OnAnimatorIK hook (the normal pass, after the hero goals, before the stance lock): a hero's foot goals are held through
        /// a standing antic (see the class notes). P / Q = this frame's goals (world), rewritten in place.</summary>
        void HeroStandHold(Vector3[] P, Quaternion[] Q, float dt, bool fresh)
        {
            if (!view || view.Obj == null || !view.Obj.IsHero) { heroHoldW = 0f; heroHoldOn = false; return; }
            var a = view.Antic;
            bool want = HeroAnticFeet && a != null && !a.Finished && HeroHoldsFeet(a.Kind);
            var rt = view.transform; var rq = rt.rotation; var rinv = Quaternion.Inverse(rq);
            if (want && !heroHoldOn)
            {   // the antic began: the feet stay where they stand (blended with an earlier hold still handing back)
                float w0 = Smooth(heroHoldW);
                for (int i = 0; i < 2; i++)
                {
                    var p = Vector3.Lerp(P[i], rt.position + rq * heroHoldL[i], w0); var q = Quaternion.Slerp(Q[i], rq * heroHoldQ[i], w0);
                    heroHoldL[i] = rinv * (p - rt.position); heroHoldQ[i] = rinv * q;
                }
            }
            if (want) { heroHoldW = 1f; heroHoldUntil = -1f; }                                            // the held goal is this frame's goal: no jump
            else if (heroHoldW > 0f)
            {   // the antic ended: held through the cross-fade back to the idle and HeroHoldTail after it, then handed back; when he
                // walks off (an order ended it: a walk / run state, or the robot moving) the feet are handed to the walk at once
                bool walks = IsGaitState(anim.GetCurrentAnimatorStateInfo(0)) || (anim.IsInTransition(0) && IsGaitState(anim.GetNextAnimatorStateInfo(0)))
                             || rootVel.magnitude > HeroHoldMoveSpeed * Mathf.Max(0.1f, rt.lossyScale.y);
                if (!walks && (heroHoldOn || anim.IsInTransition(0))) heroHoldUntil = Time.time + HeroHoldTail;
                if (fresh) heroHoldW = 0f;
                else if (walks) heroHoldW = Mathf.MoveTowards(heroHoldW, 0f, dt / Mathf.Max(0.01f, HeroHoldWalkOut));
                else if (Time.time >= heroHoldUntil) heroHoldW = Mathf.MoveTowards(heroHoldW, 0f, dt / Mathf.Max(0.01f, HeroHoldOut));
            }
            heroHoldOn = want;
            if (heroHoldW <= 0f) return;
            float w = Smooth(heroHoldW);
            for (int i = 0; i < 2; i++)
            {
                P[i] = Vector3.Lerp(P[i], rt.position + rq * heroHoldL[i], w);
                Q[i] = Quaternion.Slerp(Q[i], rq * heroHoldQ[i], w);
            }
            HeroHoldFrames++;
        }
    }
}
