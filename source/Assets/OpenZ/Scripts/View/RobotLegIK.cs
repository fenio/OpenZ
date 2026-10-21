using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// its leg muscle data is degenerate: bent knees are stored as a 150-190 deg thigh twist plus a shin counter-twist
    /// (walking: Upper Leg Twist +0.60..+3.11, Lower Leg Twist -2.03..-0.22 muscle units; a real walk stays inside +-0.3), and
    /// the decomposition jumps branch each time the knee straightens (most likely because both avatars were built from an
    /// "enforced T-pose" with exactly straight knees). Anything that interpolates those muscles (keys at 24 fps, the LegFix
    /// smoothing, every idle / walk / run / strafe cross-fade) swings the leg through that twist: boots flick ~8 cm
    /// sideways twice a step and pop 28-47 cm at gait changes.
    /// (walking: at most 0.95 cm per 60 fps frame). So on every live BS-01 robot this pins both feet to those goals (blended
    /// goals during cross-fades = a straight glide, never a twist sweep) and bends the knees toward the robot's front (a hint,
    /// so the thigh twist can no longer flip the knee). A body-space slew guard (12 m/s, 1080 deg/s x anim speed) catches any
    /// goal pop that is left (phase restarts, sign-flipped FootQ keys). Needs IK Pass on RobotBS01.controller's Base Layer.
    /// Toggles for A/B in Play: RobotLegIK.On, RobotLegIK.KneeHints.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(70)]   // LateUpdate after UnitView (60): reads the root where it was drawn this frame
    public partial class RobotLegIK : MonoBehaviour                                               // KH: + View/RobotLegIK.Hero.cs (the heroes' legs)
    {
        public static bool On = true, KneeHints = true;
        /// <summary>Largest foot speed / turn rate relative to the body (x max(1, anim.speed)); a real stride never gets near it.</summary>
        public static float MaxFootSpeed = 12f, MaxFootTurn = 1080f;
        /// <summary>Knee hint distance in front of the hip-foot midpoint, in human scale units (x Animator.humanScale).</summary>
        public static float KneeAhead = 0.5f;

        Animator anim; UnitView view;
        Transform hipL, hipR;
        readonly Vector3[] lastLocal = new Vector3[2];
        readonly Quaternion[] lastRot = new Quaternion[2];
        Vector3 lastBody; int lastFrame = -10;
        /// <summary>Frames on which the slew guard had to hold a foot back (LegJitterGate reports it).</summary>
        public int Guarded { get; private set; }
        /// <summary>True while this component drove the legs in the last evaluated frame.</summary>
        public bool Active { get; private set; }

        /// <summary>
        /// without IK): the retargeted clips' foot goals sit ~6 cm above the sole the muscles give; the goals are lowered by this (world m
        /// at scale 1).</summary>
        public static bool GroundAdapt = false;
        ZGame game => ZGame.I;
        public static float GoalDrop = 0f;
        /// <summary>
        /// a foot whose clip goal is at its recent lowest (contact) is pinned to a world spot on the ground under it (ankle at its bind
        /// height), released on lift-off or when the clip has moved on more than ReleaseDist; the pelvis drops by what a planted foot needs so
        /// </summary>
        // popped 4-16 cm, because UnitView moves the root in LateUpdate AFTER the Animator posed the pinned boot (the boot is drawn a
        // frame's root step off its pin). Root-lead prediction made it worse. Needs the root placed before the Animator evaluates
        // (MASTER_TODO legs status).
        public static bool StanceLock = true;
        /// <summary>Reach guard: a pin farther from the hip than this share of the leg's length is released (minions shoved by formation /
        /// spacing stretched a pinned leg straight and flipped the knee: 2-5 flips per minion in the legs gate).</summary>
        public static float MaxReach = 0.93f; float[] legLen; Transform[] kneeB, footBB;
        /// <summary>Contact also needs the clip boot nearly still over the ground (m/s x scale): the walking clip keeps a boot within 3 cm
        /// of its lowest for 56 % of the cycle while the first part is still the end of its swing (gliding forward), so height alone
        /// </summary>
        public static float ContactSpeed = 0.8f;
        /// <summary>
        /// flickers 0.2-2.5 m/s frame to frame (uneven frame times), so height / speed tests caught only fragments of each stance. Contact
        /// is now the clip's own stance window by phase (normalized time), measured on the LegFix curves (boot low and moving steadily
        /// back): walking L 0.150-0.680 / R 0.650-0.180, run_forward L 0.155-0.662 / R 0.655-0.163, run_backward L 0.323-0.853 /
        /// R 0.823-0.353. Locks start PhaseIn after the window opens and end PhaseOut before it closes (the hand-back ends while the boot
        /// is still slow).</summary>
        public static bool PhaseContact = true; public static float PhaseIn = 0.02f, PhaseOut = 0.12f;
        /// <summary>A lock holds at least this long (s): a phase jump on a gait cross-fade flickered locks on / off.</summary>
        public static float MinLock = 0.12f; readonly float[] lockT = new float[2];
        static bool InWindow(float ph, float a, float b)
        {
            a = Mathf.Repeat(a + PhaseIn, 1f); b = Mathf.Repeat(b - PhaseOut, 1f);
            return a <= b ? ph >= a && ph <= b : ph >= a || ph <= b;
        }
        static bool PhaseStance(string st, int foot, float ph)
        {
            switch (st)
            {
                // measured in game (clip boot ground speed by phase): the curves' left window starts 0.15 but the boot still glides until
                // a mirrored 0.800-0.200 was tried and was worse)
                case "walking": return foot == 0 ? InWindow(ph, 0.300f, 0.700f) : InWindow(ph, 0.650f, 0.240f);
                case "run_forward": return foot == 0 ? InWindow(ph, 0.155f, 0.662f) : InWindow(ph, 0.655f, 0.163f);
                case "run_backward": return foot == 0 ? InWindow(ph, 0.323f, 0.853f) : InWindow(ph, 0.823f, 0.353f);
            }
            return false;
        }
        readonly Vector3[] clipLast = new Vector3[2]; bool clipLastOk;
        /// <summary>Outside a steady gait (stopping / starting cross-fades) an unplanted boot is eased onto the ground over LandTime s:
        /// </summary>
        public static float LandTime = 0.15f; float landW;
        // ---------------------------------------------------------------------------------------------------------- legs_gate_v2
        /// <summary>
        /// free RIGHT boot never leaves the ground in a steady walk (ankle at the ground clamp on every walking frame, the left at most
        /// 8 cm up: the pelvis drop pulls the clip swing under the clamp); (3) the right boot is never planted (reach guard) and stands
        /// on the ground toe-up 3-4 cm (R stance +4.2, L/R 2.7-3.6 cm), and every idle robot holds its LEFT boot toe-up 12 cm.
        /// ProcSwing: in a steady walk / run (and walk <-> run fades) the free boot's HEIGHT is ground + ankle rest + SwingLift x a
        /// smooth bump over the clip's own swing window (zero height and slope at lift-off and touch-down, the same for both feet);
        /// horizontal = the clip goal as before; eased in / out over LandTime.</summary>
        public static bool ProcSwing = true; public static float SwingLift = 0.13f;   // m at model scale 1 (x lossyScale)
        /// <summary>
        /// and get no phase lock (PhaseStance), e.g. a v3 type whose step phase fails the EditMode phase gate (OpenZLegPhaseGate,
        /// </summary>
        public static readonly System.Collections.Generic.HashSet<string> ProcSwingSkip = new System.Collections.Generic.HashSet<string> { "electrobot", "sniper", "tough" };
        float procW; string swingGait; readonly float[] liftNow = new float[2];
        /// <summary>GroundFlat: a boot whose ankle goal is on the ground (within FlatBand) is levelled like a planted one (toe at its
        /// bind floor, LateUpdate): no toe-up / toe-down boot standing or sliding on the ground while no lock holds it.</summary>
        public static bool GroundFlat = true; public static float FlatBand = 0.04f; readonly float[] groundW = new float[2];
        /// <summary>RetimeFades (fallback, OFF by default): a fade into / out of the standing pose (idle, bazooka_idle) is re-timed from
        /// the Animator's linear blend to smoothstep on the foot goals (arrives / leaves at rest). Uses the robot's own stand pose,
        /// captured on every steady standing frame. Turn on only if the gate still shows an any-frame jerk > 8 cm at a walk <-> idle fade.</summary>
        public static bool RetimeFades = false; readonly Vector3[] standLocal = new Vector3[2]; readonly bool[] standOk = new bool[2];
        /// <summary>GaitFadeStep (on): during a walk <-> run <-> strafe cross-fade the boots keep stepping (landW stays 0) instead of
        /// height jerk > 5 cm at a gait-to-gait fade (trace_why: trans 1 between two gait states); false = the pre-v2 landing.</summary>
        public static bool GaitFadeStep = true;
        /// <summary>Swing window per foot = from the clip's stance end to its stance start (the LegFix curve windows in PhaseStance's
        /// notes): smooth bump, 0 outside. The v3 tough / sniper / electrobot walks and runs are retargeted from the Blender 'walking'
        /// action, so OpenZLegPhaseGate (EditMode) measures their step phase against the BS-01 clips; a type that fails goes into
        /// ProcSwingSkip.</summary>
        static float SwingBump(string st, int foot, float ph)
        {
            float a, b;
            switch (st)
            {
                case "walking": if (foot == 0) { a = 0.680f; b = 0.150f; } else { a = 0.180f; b = 0.650f; } break;
                case "run_forward": if (foot == 0) { a = 0.662f; b = 0.155f; } else { a = 0.163f; b = 0.655f; } break;
                case "run_backward": if (foot == 0) { a = 0.853f; b = 0.323f; } else { a = 0.353f; b = 0.823f; } break;
                default: return 0f;
            }
            float len = Mathf.Repeat(b - a, 1f), s = Mathf.Repeat(ph - a, 1f) / Mathf.Max(1e-3f, len);
            if (s >= 1f) return 0f;
            float u = 4f * s * (1f - s); return u * u;
        }
        static bool IsGaitState(AnimatorStateInfo s) => s.IsName("walking") || s.IsName("run_forward") || s.IsName("run_backward") || s.IsName("strafe_left") || s.IsName("strafe_right");
        static bool ForwardSwing(AnimatorStateInfo s) => s.IsName("walking") || s.IsName("run_forward");
        /// <summary>RetimeFades (see above); always captures the stand pose (root frame) so it can be switched on in Play.</summary>
        Vector3 RetimeFade(int i, Vector3 p)
        {
            var rt = view.transform; var cur = anim.GetCurrentAnimatorStateInfo(0);
            bool curStand = cur.IsName("idle") || cur.IsName("bazooka_idle");
            var lr = Quaternion.Inverse(rt.rotation) * (p - rt.position);
            if (!anim.IsInTransition(0)) { if (curStand) { standLocal[i] = lr; standOk[i] = true; } return p; }
            if (!RetimeFades || !standOk[i]) return p;
            var nxt = anim.GetNextAnimatorStateInfo(0);
            bool nxtStand = nxt.IsName("idle") || nxt.IsName("bazooka_idle");
            if (curStand == nxtStand) return p;
            float w = Mathf.Clamp01(anim.GetAnimatorTransitionInfo(0).normalizedTime);
            var o = nxtStand ? standLocal[i] + (1f - w) * (1f + 2f * w) * (lr - standLocal[i])      // into the stand pose: eases in, arrives at rest
                             : standLocal[i] + (3f * w - 2f * w * w) * (lr - standLocal[i]);        // out of it: leaves at rest, hands over at the gait's speed
            return rt.position + rt.rotation * o;
        }
        public static float ContactBand = 0.06f, ContactExit = 0.08f, ReleaseDist = 0.25f, LockIn = 0.15f, LockOut = 0.35f, MaxPelvisDrop = 0.2f, PelvisSpeed = 0.6f;
        readonly bool[] locked = new bool[2];
        readonly Vector3[] lockPos = new Vector3[2];
        readonly float[] lockW = new float[2], minH = { 9f, 9f };
        float[] ankleRest; float pelvisDrop, pelvisVel, pelvisPrevFrame; float pelvisPrev => pelvisPrevFrameLast;
        float pelvisPrevFrameLast;
        /// <summary>Pelvis drop follow time (s, SmoothDamp).</summary>
        public static float PelvisSmooth = 0.12f;
        /// <summary>Toe floor (see LateUpdate): boots never push the toe under its bind height. ToeLifts counts corrected foot-frames.</summary>
        public static bool ToeFloor = true; public static int ToeLifts;
        /// <summary>Checks: frame of the last toe lift per foot and the toe height right after it.</summary>
        public readonly int[] LiftFrame = { -1, -1 }; public readonly float[] LiftedTo = new float[2];
        /// <summary>Largest hip-to-goal distance of a free (unplanted) leg as a share of its length (pelvis drop keeps the knee bent).</summary>
        public static float KeepBend = 0.95f;
        /// <summary></summary>
        public static bool ClampFreeReach = false;
        /// <summary>Cross-fade easing of the body-space foot goals (see OnAnimatorIK).</summary>
        public static bool StopHold = false; public static float StopLowBand = 0.06f;
        public static bool EaseBlends = false; public static float BlendEase = 0.07f, BlendTail = 0.3f;
        readonly Vector3[] easeLocal = new Vector3[2], easeVel = new Vector3[2], rawLast = new Vector3[2]; readonly bool[] easing = new bool[2]; float blendUntil = -1f;
        /// <summary>
        /// drawn ahead by that frame's root step (2-4.5 cm with the frame time: a shimmer, and the legs gate's walking pops). The pin now
        /// sits one predicted root step back (drawn root velocity x this frame's dt), so it lands on its spot once the root has moved.</summary>
        public static bool RootLead = false; Vector3 rootVel, rootPrev; bool rootPrevOk; readonly float[] baseOff = new float[2];
        /// <summary>
        /// a planted boot takes the avatar's idle foot rotation (flat, captured from standing robots) turned to the clip's heading.
        /// (A per-frame toe-height feedback was tried first: it lagged the clip's heel-toe roll and popped 13-15 cm.)</summary>
        public static bool FlattenFeet = true;
        /// <summary>Per avatar: each foot goal's rotation relative to the robot root while standing idle (flat boots, legs gate OK).</summary>
        static readonly System.Collections.Generic.Dictionary<Avatar, Quaternion[]> idleFoot = new System.Collections.Generic.Dictionary<Avatar, Quaternion[]>();
        float idleSeen;
        /// <summary>Per avatar: slow pitch trim of each planted boot (deg, about the robot's x axis; + lifts the toe), learned while
        /// </summary>
        static readonly System.Collections.Generic.Dictionary<Avatar, float[]> footTrim = new System.Collections.Generic.Dictionary<Avatar, float[]>();
        /// <summary>
        /// right -3 deg (3.2 -> ~2.3). Learning (TrimRate) is off: its bind estimate disagreed with the gate's on some robots.</summary>
        public static float DefaultTrimL = -8f, DefaultTrimR = -3f;
        public static float TrimRate = 0f, TrimMax = 25f;
        float toeBind = float.NaN;
        /// <summary>
        /// motion vs the root speed and anim.speed. Skate = foot speed along the motion (0 when the stride matches the ground).</summary>
        public static double ProbeFoot, ProbeRoot, ProbeAnim; public static int ProbeN; public static string ProbeState = "walking";
        readonly Vector3[] probeLast = new Vector3[2]; Vector3 probeRoot; bool probeOk;
        /// <summary>Frames with a planted (locked) foot and releases because the clip ran away (LegJitterGate / checks).</summary>
        public int LockedFrames { get; private set; }
        public int ForcedReleases { get; private set; }
        /// <summary>Checks: the stance-lock weight of a foot (0 free, 1 planted).</summary>
        public float LockWeight(int i) => lockW[i];
        /// <summary>Checks: forced releases per foot by the clip running away (ReleaseDist) and by the reach guard (MaxReach).</summary>
        public readonly int[] ForcedDist = new int[2], ForcedReach = new int[2];
        public static readonly double[] ReachRatioSum = new double[2]; public static readonly int[] ReachRatioN = new int[2]; public static readonly float[] ReachRatioMax = new float[2];
        static readonly AvatarIKGoal[] Goals = { AvatarIKGoal.LeftFoot, AvatarIKGoal.RightFoot };
        static readonly AvatarIKHint[] Hints = { AvatarIKHint.LeftKnee, AvatarIKHint.RightKnee };

        /// <summary>UnitView.Bind: add to the Animator of a humanoid robot (BS-01); the Generic laser robot is left alone.</summary>
        public static RobotLegIK Attach(Animator a)
        {
            if (!a || !a.avatar || !a.avatar.isHuman) return null;
            var k = a.GetComponent<RobotLegIK>();
            return k ? k : a.gameObject.AddComponent<RobotLegIK>();
        }

        void Awake()
        {
            anim = GetComponent<Animator>();
            view = GetComponentInParent<UnitView>();
            if (anim && anim.isHuman) { hipL = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg); hipR = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg); }
        }

        void OnAnimatorIK(int layer)
        {
            if (layer != 0 || !anim) return;
            if (OzHeroLegs.Drives(view)) { Release(); return; }   // hero legs (item 209): Hips + legs written by OzHeroLegs after the Animator, no IK on them
            if (On && view && !view.Dying && anim.isHuman && view.AnticPosesLegs && !BS01Stance.LegsPosed(view) && AnticLegs) { PosedLegs(); return; }
            // boarding crews are clones without a UnitView (BoardingView.CloneView): they keep the plain clip pose, as before
            if (!On || !view || view.Dying || !anim.isHuman || BS01Stance.LegsPosed(view) || view.AnticPosesLegs) { Release(); return; }   // robot_models_v2 review: kneel / prone legs = the clip's own foot IK
            posedOn = false;
            float dt = Time.deltaTime;
            var bodyP = anim.bodyPosition; var bodyQ = anim.bodyRotation; var inv = Quaternion.Inverse(bodyQ);
            bool fresh = Time.frameCount - lastFrame > 1 || dt <= 0f || (bodyP - lastBody).sqrMagnitude > 1f;   // first frame, culled gap, teleport
            float k = Mathf.Max(1f, Mathf.Abs(anim.speed));
            float maxStep = MaxFootSpeed * k * dt, maxTurn = MaxFootTurn * k * dt;
            // robots face their -z (UnitView: yaw + modelYaw 180), never trust the avatar's body front for the knee direction
            var fwd = -view.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            float ahead = KneeAhead * Mathf.Max(0.1f, anim.humanScale);
            if (FlattenFeet && anim.avatar && !idleFoot.ContainsKey(anim.avatar))
            {   // capture the flat standing boots once per avatar (idle, settled, not blending)
                var st = anim.GetCurrentAnimatorStateInfo(0);
                idleSeen = st.IsName("idle") && !anim.IsInTransition(0) ? idleSeen + dt : 0f;
                if (idleSeen > 0.5f)
                {
                    var rinv = Quaternion.Inverse(view.transform.rotation);
                    var rq = rinv * anim.GetIKRotation(AvatarIKGoal.RightFoot);
                    // left flat boot is the right one mirrored across the robot's centre plane (root x)
                    idleFoot[anim.avatar] = new[] { new Quaternion(rq.x, -rq.y, -rq.z, rq.w), rq };
                }
            }
            var P = new Vector3[2]; var Q = new Quaternion[2];
            for (int i = 0; i < 2; i++)
            {
                var goal = Goals[i];
                var p = anim.GetIKPosition(goal); var q = anim.GetIKRotation(goal);
                p = RetimeFade(i, p);   // legs_gate_v2: captures the stand pose; re-times stand fades only while RetimeFades is on
                var lp = inv * (p - bodyP); var lq = inv * q;
                if (!fresh)
                {
                    var step = lp - lastLocal[i];
                    if (step.magnitude > maxStep) { lp = lastLocal[i] + step.normalized * maxStep; Guarded++; }
                    if (Quaternion.Angle(lastRot[i], lq) > maxTurn) { lq = Quaternion.RotateTowards(lastRot[i], lq, maxTurn); Guarded++; }
                }
                // dead on the frame it ends (jerk 9 cm). Around cross-fades the body-space goal is followed critically damped, so the
                // boot eases in and out of that glide (outside them the clip goal is used as is: velocity handed over continuously)
                bool blendNow = anim.IsInTransition(0);
                if (blendNow) blendUntil = Time.time + BlendTail;
                // snapped the lag away: walk jerk 5-10 cm)
                if (!fresh && EaseBlends && (blendNow || easing[i]))
                {
                    var sm = Vector3.SmoothDamp(easeLocal[i], lp, ref easeVel[i], BlendEase, Mathf.Infinity, dt);
                    var rawV = dt > 0f ? (lp - rawLast[i]) / dt : Vector3.zero;
                    easing[i] = blendNow || (sm - lp).sqrMagnitude > 0.0015f * 0.0015f || (easeVel[i] - rawV).sqrMagnitude > 0.05f * 0.05f;
                    rawLast[i] = lp;
                    lp = sm;
                }
                else { if (!fresh && dt > 0f) easeVel[i] = (lp - rawLast[i]) / dt; rawLast[i] = lp; easing[i] = false; }
                easeLocal[i] = lp;
                lastLocal[i] = lp; lastRot[i] = lq;
                P[i] = bodyP + bodyQ * lp; Q[i] = bodyQ * lq;
                P[i].y -= GoalDrop;
                if (GroundAdapt && game)
                {
                    // follows the ground under ITSELF relative to the ground under the robot's root (clamped, smooth: GroundY is continuous)
                    var root = view.transform.position;
                    float dy = game.GroundY(P[i].x, P[i].z) - game.GroundY(root.x, root.z);
                    P[i].y += Mathf.Clamp(dy, -0.08f, 0.35f);                                                  // lower at most 8 cm: more over-extends the leg (knee flips)
                }
            }
            HeroStandHold(P, Q, dt, fresh);                                                               // KH2 (item 197): a hero's feet held through a standing antic (View/RobotLegIK.Hero2.cs)
            HeroGoals(P, bodyP, fwd);                                                                     // KH (items 184, 185): a hero's feet spread to his own hips, each on its own side
            if (game && dt > 0f)
            {   // stride probe (read only)
                var root = view.transform.position; var rv = (root - probeRoot) / dt; rv.y = 0f;
                if (probeOk && !fresh && rv.magnitude > 0.2f && anim.GetCurrentAnimatorStateInfo(0).IsName(ProbeState))
                {
                    var dir = rv.normalized;
                    for (int i = 0; i < 2; i++)
                    {
                        float h = P[i].y - game.GroundY(P[i].x, P[i].z);
                        if (h < minH[i] + ContactBand)
                        {
                            var fv = (P[i] - probeLast[i]) / dt;
                            ProbeFoot += Vector3.Dot(fv, dir); ProbeRoot += rv.magnitude; ProbeAnim += anim.speed; ProbeN++;
                        }
                    }
                }
                for (int i = 0; i < 2; i++) probeLast[i] = P[i];
                probeRoot = root; probeOk = true;
                if (!StanceLock) for (int i = 0; i < 2; i++) { float h = P[i].y - game.GroundY(P[i].x, P[i].z); minH[i] = fresh ? h : Mathf.Min(minH[i] + 0.04f * dt, h); }
            }
            pelvisPrevFrameLast = pelvisPrevFrame;                                                         // the drop the hip bones were posed with
            if (StanceLock && game) StanceLockStep(P, dt, fresh, bodyP);
            FadeEaseStep(P, dt, fresh);
            for (int i = 0; i < 2; i++)
            {
                var goal = Goals[i];
                if (StanceLock && FlattenFeet && lockW[i] > 0f && idleFoot.TryGetValue(anim.avatar, out var fq))
                {
                    var rootQ = view.transform.rotation;
                    if (!footTrim.TryGetValue(anim.avatar, out var tr)) footTrim[anim.avatar] = tr = new[] { DefaultTrimL, DefaultTrimR };
                    var flatQ = rootQ * Quaternion.AngleAxis(tr[i], Vector3.right) * fq[i];
                    var cf = Q[i] * Vector3.forward; var ff = flatQ * Vector3.forward; cf.y = 0f; ff.y = 0f;
                    if (cf.sqrMagnitude > 1e-6f && ff.sqrMagnitude > 1e-6f) flatQ = Quaternion.FromToRotation(ff, cf) * flatQ;   // keep the clip's heading
                    Q[i] = Quaternion.Slerp(Q[i], flatQ, Smooth(lockW[i]));
                }
                anim.SetIKPosition(goal, P[i]); anim.SetIKRotation(goal, Q[i]);
                anim.SetIKPositionWeight(goal, 1f); anim.SetIKRotationWeight(goal, 1f);
                if (KneeHints)
                {
                    var hipT = i == 0 ? hipL : hipR;
                    var hip = hipT ? hipT.position : bodyP;                                                // last frame's hip: a direction hint, 3 cm never matter
                    anim.SetIKHintPosition(Hints[i], KneeHint(i, Vector3.Lerp(hip, P[i], 0.5f), fwd, ahead));
                    anim.SetIKHintPositionWeight(Hints[i], 1f);
                }
                else anim.SetIKHintPositionWeight(Hints[i], 0f);
            }
            lastBody = bodyP; lastFrame = Time.frameCount; Active = true;
        }

        /// <summary>
        /// 0.00 s after a walk -> idle fade; 2-4 knee flips). The Animator's linear cross-fade moves the blended foot goal at
        /// (idle - walk) / fade length and stops it dead on the frame the fade ends (it starts it the same way): a one-frame velocity step
        /// of the free boot in the robot's frame, which is what the gate's jerk measures. FadeEase: at BOTH ends of a fade into or out of
        /// a standing state (idle, bazooka_idle) the foot goal (robot frame) is handed over with a cubic Hermite offset over FadeEaseTime
        /// (0.2 s, the row's 0.15-0.25 s): position AND velocity continuous, the offset gone after FadeEaseTime. It is not a global blend:
        /// a planted boot (lock weight 1) is never offset, so nothing skates, and outside the hand-over the goal is the clip's as before.
        /// FadeHint: while such a fade runs, and FadeEaseTime after it, each knee hint is held in the robot frame where it was when the
        /// fade started (never less than KneeAhead / 2 in front of the hip-foot midpoint) and handed back with a smoothstep, so the knee
        /// plane does not swing while hip and boot glide (the flips sat on those near-straight fade legs). StanceLock, the stance fix
        /// and the gate limits are unchanged.</summary>
        public static bool FadeEase = true, FadeHint = true; public static float FadeEaseTime = 0.2f;
        readonly Vector3[] feOut = new Vector3[2], feOutV = new Vector3[2], feRaw = new Vector3[2], feO0 = new Vector3[2], feV0 = new Vector3[2];
        float feT = -1f, hintW; bool feWasFade, feOk;
        readonly Vector3[] hintHeld = new Vector3[2];
        /// <summary>Checks: hand-overs started (both ends of every stand fade).</summary>
        public int FadeEases { get; private set; }

        static bool IsStand(AnimatorStateInfo s) => s.IsName("idle") || s.IsName("bazooka_idle");

        void FadeEaseStep(Vector3[] P, float dt, bool fresh)
        {
            var rt = view.transform; var inv = Quaternion.Inverse(rt.rotation);
            bool standNow = IsStand(anim.GetCurrentAnimatorStateInfo(0));
            bool fade = anim.IsInTransition(0) && standNow != IsStand(anim.GetNextAnimatorStateInfo(0));
            bool live = feOk && !fresh && dt > 0f;
            bool edge = live && FadeEase && fade != feWasFade;
            if (!live) feT = -1f;
            if (edge) { feT = Time.time; FadeEases++; }
            hintW = !live ? 0f : fade ? 1f : Mathf.MoveTowards(hintW, 0f, dt / Mathf.Max(0.01f, FadeEaseTime));
            float T = Mathf.Max(0.05f, FadeEaseTime), s = feT >= 0f ? (Time.time - feT) / T : 1f;
            for (int i = 0; i < 2; i++)
            {
                var raw = inv * (P[i] - rt.position);
                var rawV = live ? (raw - feRaw[i]) / dt : Vector3.zero;
                if (edge)
                {
                    // output - raw held the boot still on the edge frame: a velocity step of its own, as big as the stop it replaces);
                    // once a fade INTO a standing state has ended the raw goal stands still (this frame's rawV is still the fade's last step)
                    feO0[i] = feOut[i] + feOutV[i] * dt - raw;
                    feV0[i] = feOutV[i] - (!fade && standNow ? Vector3.zero : rawV);
                }
                var o = Vector3.zero;
                if (feT >= 0f && s < 1f)
                {
                    float s2 = s * s, s3 = s2 * s;
                    o = feO0[i] * (2f * s3 - 3f * s2 + 1f) + feV0[i] * (T * (s3 - 2f * s2 + s));      // Hermite: offset and its velocity -> 0
                    o *= 1f - Smooth(lockW[i]);                                                          // planted boots stay on their pins
                }
                var w = rt.position + rt.rotation * (raw + o);
                if (o != Vector3.zero && ankleRest != null && game) w.y = Mathf.Max(w.y, game.GroundY(w.x, w.z) + ankleRest[i]);   // never through the ground
                P[i] = w;
                var outL = inv * (w - rt.position);
                feOutV[i] = live ? (outL - feOut[i]) / dt : Vector3.zero;
                feOut[i] = outL; feRaw[i] = raw;
            }
            if (s >= 1f) feT = -1f;
            feWasFade = fade; feOk = true;
        }

        /// <summary>FadeHint (see FadeEase): the live hint (hip-foot midpoint + KneeAhead to the front) outside stand fades; held in the
        /// robot frame while one runs, handed back over FadeEaseTime.</summary>
        Vector3 KneeHint(int i, Vector3 mid, Vector3 fwd, float ahead)
        {
            var live = mid + fwd * ahead;
            if (!FadeHint || !view) return live;
            var rt = view.transform;
            if (hintW <= 0f) { hintHeld[i] = Quaternion.Inverse(rt.rotation) * (live - rt.position); return live; }
            var held = rt.position + rt.rotation * hintHeld[i];
            float f = Vector3.Dot(held - mid, fwd);
            if (f < ahead * 0.5f) held += fwd * (ahead * 0.5f - f);                                       // never behind the leg
            return Vector3.Lerp(live, held, Smooth(hintW));
        }

        /// <summary>Stance lock + pelvis drop (see StanceLock). P = the clip goals in world space, rewritten in place.</summary>
        void StanceLockStep(Vector3[] P, float dt, bool fresh, Vector3 bodyP)
        {
            if (ankleRest == null) ankleRest = RestAnkles();
            if (legLen == null)
            {
                kneeB = new[] { anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg), anim.GetBoneTransform(HumanBodyBones.RightLowerLeg) };
                footBB = new[] { anim.GetBoneTransform(HumanBodyBones.LeftFoot), anim.GetBoneTransform(HumanBodyBones.RightFoot) };
                legLen = new float[2];
                for (int k = 0; k < 2; k++) { var h = k == 0 ? hipL : hipR; if (h && kneeB[k] && footBB[k]) legLen[k] = Vector3.Distance(h.position, kneeB[k].position) + Vector3.Distance(kneeB[k].position, footBB[k].position); }
            }
            var need = 0f; var gy = new float[2];
            // (locking in idle popped 10-17 cm on every idle <-> walk cross-fade)
            var cs = anim.GetCurrentAnimatorStateInfo(0);
            bool gaitNow = !anim.IsInTransition(0) && (cs.IsName("walking") || cs.IsName("run_forward") || cs.IsName("run_backward") || cs.IsName("strafe_left") || cs.IsName("strafe_right"));
            string gaitName = !gaitNow ? null : cs.IsName("walking") ? "walking" : cs.IsName("run_forward") ? "run_forward" : cs.IsName("run_backward") ? "run_backward" : null;
            float phase = Mathf.Repeat(cs.normalizedTime, 1f); dbgPhase = phase;
            float sc = Mathf.Max(0.1f, view.transform.lossyScale.y) * HeroSize();                   // KH: a hero's leg distances x his size (View/RobotLegIK.Hero.cs); 1 for every other robot
            // and stopped dead when it ended, jerk 9 cm): while a gait fades into idle and the robot stands, boots on the ground stay
            // planted; when the fade ends they let go and ease (smoothstep lock weight) into the idle pose
            bool stopping = StopHold && anim.IsInTransition(0) && anim.GetNextAnimatorStateInfo(0).IsName("idle") && rootVel.magnitude < 0.35f * sc;
            // legs_gate_v2: a ProcSwingSkip type (its step phase failed OpenZLegPhaseGate) keeps the clip's boot height (procOn) AND gets
            // no phase lock (PhaseStance holds the same phase windows); GroundFlat keeps its grounded boots flat
            bool skipType = view.Obj != null && ProcSwingSkip.Contains(view.Obj.Type);
            for (int i = 0; i < 2; i++)
            {
                gy[i] = game.GroundY(P[i].x, P[i].z);
                float h = P[i].y - gy[i];
                var cv = clipLastOk && dt > 0f ? (P[i] - clipLast[i]) / dt : Vector3.zero; cv.y = 0f; clipLast[i] = P[i];
                bool still = clipLastOk && !fresh && cv.magnitude < ContactSpeed * sc;
                if (fresh) { locked[i] = false; lockW[i] = 0f; minH[i] = h; }
                minH[i] = Mathf.Min(minH[i] + 0.04f * dt, h);                                              // the clip's recent lowest (slowly forgets)
                // release as soon as the clip boot starts its swing (moving over the ground), not only once it is 8 cm up and fast:
                bool moving = clipLastOk && !fresh && cv.magnitude > ContactSpeed * 1.5f * sc;
                bool contact = stopping ? (locked[i] || h < ankleRest[i] + StopLowBand * sc)
                             : (PhaseContact && gaitName != null) ? ((!skipType || FootLockV2) && LockPhase(gaitName, i, phase))   // legs_gate_v2: no phase lock for a ProcSwingSkip type; KL v2: every type, its own clip's window (View/RobotLegIK.FootLockV2.cs)
                             : locked[i] ? h < minH[i] + ContactExit && !moving : h < minH[i] + ContactBand && still;
                dbgH[i] = h; dbgV[i] = cv.magnitude;
                if (gaitNow) { bool low = h < minH[i] + ContactBand; CondN[i * 5]++; if (low) CondN[i * 5 + 1]++; if (still) CondN[i * 5 + 2]++; if (low && still) CondN[i * 5 + 3]++; if (locked[i]) CondN[i * 5 + 4]++; }
                var flat = new Vector2(P[i].x - lockPos[i].x, P[i].z - lockPos[i].z);
                if (locked[i] && ((!contact && Time.time - lockT[i] > MinLock) || (!stopping && flat.magnitude > ReleaseDistFor(sc)))) { if (contact) { ForcedReleases++; ForcedDist[i]++; } locked[i] = false; }
                else if (!locked[i] && contact && lockW[i] < 0.05f && (gaitNow || stopping)) { locked[i] = true; lockT[i] = Time.time; lockPos[i] = new Vector3(P[i].x, gy[i] + ankleRest[i], P[i].z); }
                if (locked[i]) lockPos[i].y = game.GroundY(lockPos[i].x, lockPos[i].z) + ankleRest[i];   // a deck / slope under a planted boot
                // away in the frame it was made (190-306 forced releases per robot, right stance toe +9..+12 cm). A pin is now kept when the
                // pelvis can drop far enough to reach it (the drop it needs goes into `need`); only a pin out of reach even with the full
                // MaxPelvisDrop (or horizontally beyond the leg) is released. (The old test also subtracted last frame's drop twice.)
                float reachNeed = 0f;
                if (locked[i] && legLen != null && legLen[i] > 0f)
                {
                    var hipT = i == 0 ? hipL : hipR;
                    if (hipT)
                    {
                        var hipU = FootLockV2 ? HipNow(i, hipT, bodyP) : hipT.position + Vector3.up * pelvisDrop;   // the hip without this robot's drop (KL v2: read once, View/RobotLegIK.FootLockV2.cs)
                        float rmax = MaxReach * legLen[i];
                        var dh = new Vector2(hipU.x - lockPos[i].x, hipU.z - lockPos[i].z).magnitude;
                        float rr = Vector3.Distance(hipU, lockPos[i]) / legLen[i];
                        ReachRatioSum[i] += rr; ReachRatioN[i]++; ReachRatioMax[i] = Mathf.Max(ReachRatioMax[i], rr);
                        if (FootLockV2) reachNeed = V2Reach(i, hipU, rmax, sc);                                // KL v2: an out-of-reach pin moved toward the hip, not torn
                        else if (dh >= rmax * 0.98f) { locked[i] = false; ForcedReleases++; ForcedReach[i]++; }
                        else
                        {
                            reachNeed = Mathf.Max(0f, (hipU.y - lockPos[i].y) - Mathf.Sqrt(rmax * rmax - dh * dh));
                            if (reachNeed > MaxPelvisDrop * sc) { locked[i] = false; ForcedReleases++; ForcedReach[i]++; reachNeed = 0f; }
                        }
                    }
                }
                lockW[i] = Mathf.MoveTowards(lockW[i], locked[i] ? 1f : 0f, dt / LockTime(locked[i], gaitName, cs));                 // KL: LockIn / LockOut (v2: at most a share of the gait's cycle)
                if (locked[i]) { LockedFrames++; need = Mathf.Max(need, Mathf.Max(P[i].y - lockPos[i].y, reachNeed * Smooth(lockW[i]))); }
            }
            // for heel strike: knee +-3-5 cm either side of the hip-ankle line). Every leg, planted or not, keeps a bend: the pelvis drops
            // what a free boot's goal (floored at its rest height) needs to stay within KeepBend of the leg's length.
            if (legLen != null)
                for (int i = 0; i < 2; i++)
                {
                    if (locked[i] || legLen[i] <= 0f) continue;
                    var hipT = i == 0 ? hipL : hipR; if (!hipT) continue;
                    var hipU = FootLockV2 && FootLockV2Upright ? HipNow(i, hipT, bodyP) : hipT.position + Vector3.up * pelvisDrop;   // KL: FootLockV2Upright
                    var goal = P[i]; goal.y = Mathf.Max(goal.y, gy[i] + ankleRest[i]);
                    float rk = KeepBend * legLen[i];
                    float dh = new Vector2(hipU.x - goal.x, hipU.z - goal.z).magnitude;
                    if (dh >= rk) continue;
                    need = Mathf.Max(need, (hipU.y - goal.y) - Mathf.Sqrt(rk * rk - dh * dh));
                }
            clipLastOk = true;
            // legs_gate_v2: a walk <-> run <-> strafe cross-fade keeps the boots stepping (they landed mid-stride on every gait switch);
            // ProcSwing follows the steady walk / run (and walk <-> run fades) and eases out over LandTime on any other change
            var nextS = anim.GetNextAnimatorStateInfo(0); bool inFade = anim.IsInTransition(0);
            bool gaitFade = GaitFadeStep && inFade && IsGaitState(cs) && IsGaitState(nextS);                 // GaitFadeStep: LIK1 (fallback toggle)
            landW = fresh ? (gaitNow || gaitFade ? 0f : 1f) : Mathf.MoveTowards(landW, gaitNow || gaitFade ? 0f : 1f, dt / LandTime);
            swingGait = cs.IsName("walking") ? "walking" : cs.IsName("run_forward") ? "run_forward" : cs.IsName("run_backward") ? "run_backward" : null;
            bool procOn = ProcSwing && swingGait != null && !(skipType && KeepsClipHeight(swingGait))       // skipType: LIK6; KL v2: only while a v3 clip plays (View/RobotLegIK.FootLockV2.cs)
                          && (!inFade || (ForwardSwing(cs) && ForwardSwing(nextS)));
            procW = fresh ? (procOn ? 1f : 0f) : Mathf.MoveTowards(procW, procOn ? 1f : 0f, dt / LandTime);
            float scale = sc;
            // started and stopped at full speed (walk jerk 4-6.7 cm on the swinging boot): a critically damped follow instead
            if (fresh) { pelvisDrop = Mathf.Clamp(need, 0f, MaxPelvisDrop * scale); pelvisVel = 0f; }
            else pelvisDrop = Mathf.SmoothDamp(pelvisDrop, Mathf.Clamp(need, 0f, MaxPelvisDrop * scale), ref pelvisVel, PelvisSmooth, PelvisSpeed * scale * 2f, dt);
            anim.bodyPosition = bodyP + Vector3.down * pelvisDrop;
            V2Body(bodyP, pelvisDrop);                                                                    // KL: this frame's body for the hip measurement (read only)
            pelvisPrevFrame = pelvisDrop;
            for (int i = 0; i < 2; i++)
            {
                var swing = P[i] + Vector3.down * (FootLockV2 ? SwingDrop(skipType, cs, dt, fresh) : pelvisDrop);   // the clip goal moves with the pelvis (KL v2: a v3 clip's free boot keeps its own height)
                swing.y = Mathf.Max(swing.y, gy[i] + ankleRest[i]);                                          // never through the ground
                // legs_gate_v2 ProcSwing: the free boot's height in a steady walk / run (the lift is held while procW fades out)
                if (swingGait != null) liftNow[i] = LiftBump(swingGait, i, phase) * SwingLift * scale;    // KL: LiftBump (v2: the clip's own air time)
                if (procW > 0f) swing.y = Mathf.Lerp(swing.y, gy[i] + ankleRest[i] + liftNow[i], Smooth(procW));
                swing.y = Mathf.Lerp(swing.y, gy[i] + ankleRest[i], Smooth(landW));                          // stopping / starting: boots land
                groundW[i] = 1f - Mathf.Clamp01((swing.y - gy[i] - ankleRest[i]) / Mathf.Max(1e-4f, FlatBand * scale));   // legs_gate_v2 GroundFlat
                var pin = RootLead ? lockPos[i] - rootVel * dt : lockPos[i];
                P[i] = Vector3.Lerp(swing, pin, Smooth(lockW[i]));
                if (FootLockV2) V2Peel(i, ref P[i]);                                                       // KL v2: leaves / reaches its pin through the air
                // -> +8 cm): a free boot's goal is kept within KeepBend of the leg's length from the hip, so the knee never straightens
                if (ClampFreeReach && legLen != null && legLen[i] > 0f && lockW[i] < 0.5f)
                {
                    var hipT = i == 0 ? hipL : hipR;
                    if (hipT)
                    {
                        var hp = hipT.position + Vector3.down * (pelvisDrop - pelvisPrev);                  // this frame's hip (drop change since the pose)
                        var dv = P[i] - hp; float rk = KeepBend * legLen[i];
                        if (dv.magnitude > rk) P[i] = hp + dv.normalized * rk;
                    }
                }
            }
        }

        /// <summary>Bind-pose ankle heights above the robot's root (the soles' plane), world metres (like LegJitterGate.RestHeight);
        /// both boots get their mean so the planted feet stand at one height (L/R stance).</summary>
        float[] RestAnkles()
        {
            var r = new float[2];
            var smr = view.GetComponentInChildren<SkinnedMeshRenderer>();
            var feet = new[] { anim.GetBoneTransform(HumanBodyBones.LeftFoot), anim.GetBoneTransform(HumanBodyBones.RightFoot) };
            for (int i = 0; i < 2; i++)
            {
                r[i] = float.NaN;
                if (smr && smr.sharedMesh && feet[i])
                {
                    int b = System.Array.IndexOf(smr.bones, feet[i]); var bp = smr.sharedMesh.bindposes;
                    if (b >= 0 && b < bp.Length)
                    {
                        Vector3 w = (smr.transform.localToWorldMatrix * bp[b].inverse).GetColumn(3);
                        r[i] = (Quaternion.Inverse(view.transform.rotation) * (w - view.transform.position)).y;
                    }
                }
            }
            float m = !float.IsNaN(r[0]) && !float.IsNaN(r[1]) ? (r[0] + r[1]) * 0.5f : !float.IsNaN(r[0]) ? r[0] : !float.IsNaN(r[1]) ? r[1] : 0.1f;
            return new[] { m, m };
        }

        static float Smooth(float w) => w * w * (3f - 2f * w);

        /// <summary>
        /// same for the clip-only pose (lock weight 0) of walking robots. [foot*2 + (0 ankle, 1 toe)]</summary>
        public static readonly double[] PlantSum = new double[4]; public static readonly int[] PlantN = new int[2];
        /// <summary>Probe: summed distance of the planted ankle bone from its lock (IK reach error) and the frames over 3 cm, per foot.</summary>
        public static readonly double[] ReachSum = new double[2]; public static readonly int[] ReachOver = new int[2];
        /// <summary>Probe: walking frames per foot [0 all, 1 low, 2 still, 3 low+still, 4 locked].</summary>
        public static readonly int[] CondN = new int[10];
        /// <summary>Per-frame recorder for one robot (DebugId): t, foot, clip h, minH, clip speed, locked, lockW, ankle speed (world), toe h.</summary>
        public static int DebugId = -1; public static readonly System.Text.StringBuilder DebugLog = new System.Text.StringBuilder();
        float dbgPhase; readonly float[] dbgH = new float[2], dbgV = new float[2]; readonly Vector3[] dbgAnk = new Vector3[2];
        Transform[] pFoot, pToe;
        void LateUpdate()
        {
            if (view)
            {
                var rp = view.transform.position; float ldt = Time.deltaTime;
                if (rootPrevOk && ldt > 0f) { var v = (rp - rootPrev) / ldt; v.y = 0f; v = Vector3.ClampMagnitude(v, 4f * Mathf.Max(0.1f, view.transform.lossyScale.y)); rootVel = Vector3.Lerp(rootVel, v, 1f - Mathf.Exp(-ldt / 0.05f)); }
                rootPrev = rp; rootPrevOk = true;
            }
            if (!Active || !game || !anim) return;
            V2MeasureHips();                                                                              // KL: the hips' body-frame offsets on the drawn pose (read only)
            if (pFoot == null)
            {
                pFoot = new[] { anim.GetBoneTransform(HumanBodyBones.LeftFoot), anim.GetBoneTransform(HumanBodyBones.RightFoot) };
                pToe = new[] { anim.GetBoneTransform(HumanBodyBones.LeftToes), anim.GetBoneTransform(HumanBodyBones.RightToes) };
            }
            if (DebugId >= 0 && view.Obj != null && view.Obj.Id == DebugId && pFoot[0] && pToe[0])
                for (int i = 0; i < 2; i++)
                {
                    var f0 = pFoot[i].position; var t0 = pToe[i].position; float dt0 = Mathf.Max(1e-4f, Time.deltaTime);
                    var av = (f0 - dbgAnk[i]) / dt0; av.y = 0f; dbgAnk[i] = f0;
                    DebugLog.Append(Time.time.ToString("0.000")).Append(',').Append(i).Append(',').Append(dbgH[i].ToString("0.000")).Append(',').Append(minH[i].ToString("0.000"))
                            .Append(',').Append(dbgV[i].ToString("0.00")).Append(',').Append(locked[i] ? 1 : 0).Append(',').Append(lockW[i].ToString("0.00"))
                            .Append(',').Append(av.magnitude.ToString("0.00")).Append(',').Append((t0.y - game.GroundY(t0.x, t0.z)).ToString("0.000")).Append(',').Append(anim.GetCurrentAnimatorStateInfo(0).IsName("walking") ? "walk" : "other").Append(',').Append(dbgPhase.ToString("0.000")).Append(',').Append(view.transform.position.x.ToString("0.0000")).Append(',').Append(view.transform.position.z.ToString("0.0000")).Append(',').Append(Time.deltaTime.ToString("0.0000")).Append(',').Append(anim.GetCurrentAnimatorClipInfo(0).Length > 0 ? anim.GetCurrentAnimatorClipInfo(0)[0].clip.name : "-").Append('\n');
                }
            // (never-through-the-ground clamp), so the right toe went 3-6 cm into the ground (R stance -4..-6 cm on 4 robots).
            // Toe floor: after the IK pass, a toe below its bind height is lifted by turning the boot about its ankle.
            if (ToeFloor && pFoot[0] && pToe[0])
            {
                if (float.IsNaN(toeBind)) { toeBind = BindToe(); if (float.IsNaN(toeBind)) toeBind = 0.0235f * Mathf.Max(0.1f, view.transform.lossyScale.y); }
                for (int i = 0; i < 2; i++)
                {
                    if (!pFoot[i] || !pToe[i]) continue;
                    var f = pFoot[i].position; var t = pToe[i].position;
                    var rp0 = view.transform.position;
                    float floor = game.GroundY(t.x, t.z) + toeBind + (rp0.y - game.GroundY(rp0.x, rp0.z));   // bind toe over the soles' plane (the root may sit a few cm off the ground sample)
                    // the flat-boot rotation is captured per avatar from whichever robot idled first)
                    float want = t.y < floor ? floor : Mathf.Lerp(t.y, floor, Mathf.Max(Smooth(lockW[i]), GroundFlat && StanceLock ? Smooth(groundW[i]) : 0f));   // legs_gate_v2 GroundFlat: grounded boots flat too
                    if (Mathf.Abs(t.y - want) < 0.002f) continue;
                    floor = want;
                    // the BS-01 ankle sits 32 cm up in the boot, so ankle->toe is mostly vertical: turn it (fixed length) until the toe
                    var d = t - f; float L = d.magnitude, yT = floor - f.y;
                    if (L < 1e-4f || yT <= -L) continue;
                    var hd = new Vector3(d.x, 0f, d.z); if (hd.sqrMagnitude < 1e-8f) hd = -view.transform.forward; hd.y = 0f; hd.Normalize();
                    var d2 = hd * Mathf.Sqrt(Mathf.Max(0f, L * L - yT * yT)) + Vector3.up * yT;
                    pFoot[i].rotation = Quaternion.FromToRotation(d, d2) * pFoot[i].rotation;
                    ToeLifts++; LiftFrame[i] = Time.frameCount; LiftedTo[i] = pToe[i].position.y - game.GroundY(pToe[i].position.x, pToe[i].position.z);
                }
            }
            for (int i = 0; i < 2; i++)
            {
                if (lockW[i] < 0.999f || !pFoot[i] || !pToe[i]) continue;
                var f = pFoot[i].position; var t = pToe[i].position;
                float toeH = t.y - game.GroundY(t.x, t.z);
                PlantSum[i * 2] += f.y - game.GroundY(f.x, f.z); PlantSum[i * 2 + 1] += toeH; PlantN[i]++;
                float reach = Vector3.Distance(f, lockPos[i]); ReachSum[i] += reach; if (reach > 0.03f) ReachOver[i]++;
                if (FlattenFeet && anim.avatar && footTrim.TryGetValue(anim.avatar, out var tr))
                {
                    if (float.IsNaN(toeBind)) { toeBind = BindToe(); if (float.IsNaN(toeBind)) toeBind = 0.0235f * Mathf.Max(0.1f, view.transform.lossyScale.y); }   // the gate's BS-01 bind toe (2.4 / 2.3 cm)
                    if (!float.IsNaN(toeBind)) tr[i] = Mathf.Clamp(tr[i] - TrimRate * (toeH - toeBind) * Time.deltaTime * 0.25f, -TrimMax, TrimMax);
                }
            }
        }

        /// <summary>Bind-pose toe height above the root (mean of both toes, world m), as LegJitterGate measures it.</summary>
        float BindToe()
        {
            var smr = view.GetComponentInChildren<SkinnedMeshRenderer>(); float sum = 0f; int n = 0;
            for (int i = 0; i < 2; i++)
            {
                if (!smr || !smr.sharedMesh || !pToe[i]) continue;
                int b = System.Array.IndexOf(smr.bones, pToe[i]); var bp = smr.sharedMesh.bindposes;
                if (b < 0 || b >= bp.Length) continue;
                Vector3 w = (smr.transform.localToWorldMatrix * bp[b].inverse).GetColumn(3);
                sum += (Quaternion.Inverse(view.transform.rotation) * (w - view.transform.position)).y; n++;
            }
            return n > 0 ? sum / n : float.NaN;
        }

        /// <summary>Checks: the learned trims of an avatar (deg), or null.</summary>
        public static float[] TrimOf(Avatar a) => a && footTrim.TryGetValue(a, out var t) ? t : null;

        /// <summary>
        /// The sunbed antic released the legs to the plain clip pose (weights 0), so the BS-01's degenerate leg muscles (see the header:
        /// a bent knee stored as a thigh twist) swung the knees through that twist as sunbed_up straightened them and as it cross-faded
        /// to idle, and on the frame the antic ended the normal pass came back "fresh" (pelvis drop, landing and stance state reset,
        /// weights 0 -> 1 in one frame). AnticLegs: while an antic poses the legs, the feet follow the clip's own foot goals (the pose is
        /// the clip's) with the knees held in the plane of the boot's FORWARD (the robot's front in the boot's own frame, captured from
        /// the standing boot as the antic starts; square to the hip-foot line, followed with a short ease), so a knee can never flip
        /// (measured: the toe - ankle vector of the BS-01 boot is mostly "down" (ankle 32 cm up in the boot) and bent the knees backward
        /// on the sit-down / stand-up; the boot forward keeps them forward, and up while lying); it eases in from the last normal output over
        /// PosedEaseIn, and it keeps the normal pass's state (outputs, velocities, held knee hints, slew guard) up to date, so the
        /// hand-back to idle is the normal stand-fade hand-over (FadeEase / FadeHint) and never a snap. False = the old release.</summary>
        public static bool AnticLegs = true; public static float PosedEaseIn = 0.25f, PosedDirFollow = 0.06f;
        bool posedOn, posedFromOk; float posedT, posedDrop0; readonly Vector3[] posedFrom = new Vector3[2], posedDir = new Vector3[2], bootFwdLocal = new Vector3[2];
        /// <summary>Checks: frames driven by the posed-legs pass.</summary>
        public int PosedFrames { get; private set; }

        void PosedLegs()
        {
            float dt = Time.deltaTime;
            var rt = view.transform; var rq = rt.rotation; var rinv = Quaternion.Inverse(rq);
            var bodyP = anim.bodyPosition; var bodyQ = anim.bodyRotation; var inv = Quaternion.Inverse(bodyQ);
            var fwd = -rt.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward; fwd.Normalize();   // robots face their -z
            float ahead = KneeAhead * Mathf.Max(0.1f, anim.humanScale);
            bool enter = !posedOn || Time.frameCount - lastFrame > 1;
            if (pFoot == null)
            {
                pFoot = new[] { anim.GetBoneTransform(HumanBodyBones.LeftFoot), anim.GetBoneTransform(HumanBodyBones.RightFoot) };
                pToe = new[] { anim.GetBoneTransform(HumanBodyBones.LeftToes), anim.GetBoneTransform(HumanBodyBones.RightToes) };
            }
            if (enter)
            {
                posedOn = true; posedT = 0f; posedFromOk = feOk && Time.frameCount - lastFrame <= 1;      // a normal frame just ran: ease in from its output
                posedDrop0 = posedFromOk ? pelvisDrop : 0f;
                for (int i = 0; i < 2; i++)
                {
                    posedFrom[i] = feOut[i]; posedDir[i] = fwd;
                    // the boot's forward in its own frame: the robot's front, read off the standing boot the antic starts from
                    bootFwdLocal[i] = pFoot[i] ? Quaternion.Inverse(pFoot[i].rotation) * fwd : Vector3.zero;
                }
            }
            else posedT += dt;
            float w = posedFromOk ? Smooth(Mathf.Clamp01(posedT / Mathf.Max(0.01f, PosedEaseIn))) : 1f;
            if (posedDrop0 > 0f && w < 1f) anim.bodyPosition = bodyP + Vector3.down * (posedDrop0 * (1f - w));   // the pelvis drop eases out over the hand-over
            float follow = enter ? 1f : 1f - Mathf.Exp(-dt / Mathf.Max(0.005f, PosedDirFollow));
            for (int i = 0; i < 2; i++)
            {
                var goal = Goals[i];
                var p = anim.GetIKPosition(goal); var q = anim.GetIKRotation(goal);
                bool heroLie = HeroLieGoal(i, ref p, ref q, bodyP, enter);                                  // KH2 (item 196): a lying hero's own legs (View/RobotLegIK.Hero2.cs)
                var raw = rinv * (p - rt.position);
                var outL = Vector3.Lerp(posedFrom[i], raw, w);
                var P = rt.position + rq * outL;
                anim.SetIKPosition(goal, P); anim.SetIKRotation(goal, q);
                anim.SetIKPositionWeight(goal, 1f); anim.SetIKRotationWeight(goal, 1f);
                // the knee plane: the boot's forward (the clip's goal rotation this frame: the boot never follows the knee), square to
                // the hip-foot line; the robot's front if that is degenerate
                var hipT = i == 0 ? hipL : hipR; var hip = hipT ? hipT.position : bodyP;
                var leg = P - hip; var dir = fwd;
                var ft = pFoot[i];
                if (ft && bootFwdLocal[i].sqrMagnitude > 0.5f && leg.sqrMagnitude > 1e-6f)
                {
                    var bootFwd = ft.rotation * bootFwdLocal[i];                                            // last frame's boot = last frame's goal rotation
                    var d = Vector3.ProjectOnPlane(bootFwd, leg.normalized);
                    if (d.sqrMagnitude > 0.04f) dir = d.normalized;                                        // a boot pointing along the leg: keep the last plane
                    else dir = posedDir[i];
                }
                if (heroLie) { dir = HeroLieKnee(dir); if (enter) posedDir[i] = dir; }                    // KH2: a lying hero's knees bend straight up
                posedDir[i] = Vector3.Slerp(posedDir[i], dir, enter ? 0f : follow);
                if (posedDir[i].sqrMagnitude < 1e-6f) posedDir[i] = fwd;
                var hint = Vector3.Lerp(hip, P, 0.5f) + posedDir[i].normalized * ahead;
                anim.SetIKHintPosition(Hints[i], hint); anim.SetIKHintPositionWeight(Hints[i], 1f);
                // the normal pass's state, so it takes over from exactly this output (FadeEase hand-over, held knee hint, slew guard)
                var lp = inv * (P - bodyP);
                lastLocal[i] = lp; lastRot[i] = inv * q; rawLast[i] = lp; easeLocal[i] = lp; easeVel[i] = Vector3.zero; easing[i] = false;
                feOutV[i] = (posedFromOk || !enter) && dt > 0f ? (outL - feOut[i]) / dt : Vector3.zero;
                feOut[i] = outL; feRaw[i] = outL;
                hintHeld[i] = rinv * (hint - rt.position);
                locked[i] = false; lockW[i] = 0f; groundW[i] = 0f; liftNow[i] = 0f; clipLast[i] = P;
                if (game) minH[i] = P.y - game.GroundY(P.x, P.z);
            }
            feWasFade = false; feOk = true; feT = -1f; hintW = 0f; landW = 0f; procW = 0f; blendUntil = -1f;
            pelvisDrop = 0f; pelvisVel = 0f; pelvisPrevFrame = 0f; clipLastOk = true;
            lastBody = bodyP; lastFrame = Time.frameCount; Active = false; PosedFrames++;
        }

        void Release()
        {
            posedOn = false;
            Active = false; lastFrame = -10;
            for (int i = 0; i < 2; i++)
            {
                anim.SetIKPositionWeight(Goals[i], 0f); anim.SetIKRotationWeight(Goals[i], 0f);
                anim.SetIKHintPositionWeight(Hints[i], 0f);
            }
        }
    }
}
