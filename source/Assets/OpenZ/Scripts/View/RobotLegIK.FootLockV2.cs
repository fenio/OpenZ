using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Foot lock v2 (item 203.3). A switch: <see cref="FootLockV2"/>
    /// (PlayerPrefs <see cref="FootLockV2Pref"/>, 1 = on, the default; menu OpenZ/Legs). OFF = the stance lock exactly as before,
    /// frame for frame (every hook below falls back to the old expression, and the hip measurement only reads transforms).
    /// <para><b>Why no planted boot ever stayed put.</b> The stance lock's reach guard and the pelvis drop read the hip joint inside
    /// OnAnimatorIK as <c>hipT.position + up x pelvisDrop</c>, meant as "the hip without this robot's drop". The bone there already
    /// stands where this frame's clip puts it, WITHOUT the drop (the drop is applied to the pose only after OnAnimatorIK), so the hip
    /// was read one full pelvis drop too high. Two things follow, both counted in Play and reproduced off the engine: the free
    /// leg's keep-bend need grows with the drop it already has, so the drop runs to its cap on every walking robot and stays there
    /// (Play: "drop now 0.200" for every grunt, 0.307 Brad, 0.279 Alan, on every frame); and a new pin then needs "the true drop +
    /// the cap", which is always more than the cap, so the guard tears every lock in the frame it is made (Play: lock weight 0 on
    /// every frame, a forced reach release on every stance frame, reach ratio hip-pin 1.21-1.30 leg lengths, which no leg can
    /// have). The boots therefore followed the clip's own foot goals, which glide over the ground (the walk's left boot 35-42 cm a
    /// stance), at 30 and at 60 frames a second alike.</para>
    /// <para><b>What v2 does</b> (worked out frame by frame off the engine: kit KL sim/feet_sim.py):
    /// <list type="bullet">
    /// <item>The reach guard reads the hip of THIS frame once: the body (anim.bodyPosition / bodyRotation, before the drop) plus the
    /// hip joint's offset in the body's frame, measured on the drawn pose in LateUpdate (<see cref="HipNow"/>). The pelvis drop
    /// itself is computed as before (the walk keeps its crouch), unless <see cref="FootLockV2Upright"/> (then the drop is only
    /// what the legs need: the walk stands taller).</item>
    /// <item>A pin the leg cannot reach even with the full pelvis drop is moved toward the hip just as far as needed (counted in
    /// <see cref="V2Drags"/>), instead of being torn away; only a jump of more than <see cref="V2DragMax"/> (a shove) still tears it.</item>
    /// <item>The lock holds from the clip's own landing to its own lift-off (the phases where the clip's foot goal stands still on
    /// the ground at the clip's stride speed, measured on the walk / run / back-pedal clips), and ProcSwing's boot is in the air
    /// exactly outside them (<see cref="V2Stance"/>, <see cref="V2Bump"/>); before, the boot was put down 0.05 of a cycle before the
    /// clip stopped gliding and the lock started 0.15 later.</item>
    /// <item>A boot handed back from its pin (or taken over) is lifted by its distance from the pin x <see cref="V2PeelSlope"/> x the
    /// lock weight: it never slides along the ground, and nothing pops (the lift is 0 at both ends).</item>
    /// <item>The clip-ran-away release distance scales with the robot's size; the hand-over times never exceed a share of the gait's
    /// cycle (a fast walk never skips a lock).</item>
    /// <item>The tough, the sniper and the electrobot (<see cref="ProcSwingSkip"/>: no phase lock before, their boots slid the most once
    /// the pelvis drop sat at its cap) are locked too: in their own v3 clips' planted windows (<see cref="V3Window"/>), their swinging boot
    /// at the clip's own height over the ground (<see cref="SwingDrop"/>, no ProcSwing); the tough's runs play the BS-01 clips and get
    /// the BS-01 windows and ProcSwing (<see cref="KeepsClipHeight"/>).</item>
    /// </list>
    /// What the switch also touches: strafes and stop / start fades (no phase lock; their height / speed locks) read the hip once, move
    /// an out-of-reach pin instead of tearing it, release at <see cref="V2ReleaseDist"/> x size and leave / reach their pin through the
    /// air, like the phase locks.</para>
    /// Counters for the checks: LockedFrames, ForcedReleases, ForcedReach, ForcedDist (as before), <see cref="DragFrames"/>,
    /// <see cref="V2Drags"/>, <see cref="V2DragCm"/>, <see cref="V2Torn"/>; <see cref="PelvisDropNow"/>.
    /// </summary>
    public partial class RobotLegIK
    {
        /// <summary>The switch (false = the stance lock exactly as before).</summary>
        public static bool FootLockV2 = true;
        /// <summary>PlayerPrefs key read at start (1 = on, the default).</summary>
        public const string FootLockV2Pref = "oz_foot_lock_v2";
        /// <summary>With v2 on: the pelvis drop from the hip counted once too (the walk stands taller). False = the drop as before.</summary>
        public static bool FootLockV2Upright = false;
        /// <summary>Lift of a boot leaving / reaching its pin per metre of distance from it (x the lock weight's smoothstep).</summary>
        public static float V2PeelSlope = 1f;
        /// <summary>m x the robot's leg size: the clip's goal this far from its pin releases the lock (before: 0.25 m for every size).</summary>
        public static float V2ReleaseDist = 0.35f;
        /// <summary>m x size: a pin out of reach is moved toward the hip up to this far in one frame; more tears it (a shove).</summary>
        public static float V2DragMax = 0.25f;
        /// <summary>Hand-over times never longer than this share of the gait's cycle (in, out).</summary>
        public static float V2InShare = 0.11f, V2OutShare = 0.30f;
        /// <summary>Checks (all robots): pins moved toward the hip, how far in all (cm), pins torn by a shove.</summary>
        public static int V2Drags, V2Torn; public static double V2DragCm;
        /// <summary>Checks: frames this robot moved a pin toward its hip.</summary>
        public int DragFrames { get; private set; }
        /// <summary>Checks: this robot's pelvis drop now (m).</summary>
        public float PelvisDropNow => pelvisDrop;
        /// <summary>Checks: this robot's bind ankle height over its soles (m), NaN before the first stance step.</summary>
        public float AnkleRest(int i) => ankleRest != null ? ankleRest[i] : float.NaN;
        /// <summary>Checks: this robot's leg distance scale (root scale x a hero's size).</summary>
        public float LegScale => view ? Mathf.Max(0.1f, view.transform.lossyScale.y) * HeroSize() : 1f;

        readonly Vector3[] v2HipOff = new Vector3[2]; bool v2HipOk; Vector3 v2BodyP; Quaternion v2BodyQ = Quaternion.identity; float v2Drop; int v2BodyFrame = -10;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void FootLockV2Boot() { FootLockV2 = PlayerPrefs.GetInt(FootLockV2Pref, 1) == 1; }

        /// <summary>The clip's own planted window per foot (phase), v2: a v3 clip's own (<see cref="V3Window"/>), else the BS-01 LegFix clip's.</summary>
        static bool V2Stance(string type, string st, int foot, float ph)
        {
            if (V3Window(type, st, foot, out float a, out float b)) return InSpan(ph, a, b);
            switch (st)
            {
                case "walking": return foot == 0 ? InSpan(ph, 0.200f, 0.760f) : InSpan(ph, 0.690f, 0.260f);
                case "run_forward": return foot == 0 ? InSpan(ph, 0.220f, 0.750f) : InSpan(ph, 0.700f, 0.260f);
                case "run_backward": return foot == 0 ? InSpan(ph, 0.250f, 0.780f) : InSpan(ph, 0.740f, 0.300f);
            }
            return false;
        }

        /// <summary>v2: the robot types (<see cref="ProcSwingSkip"/>) whose walk / run state plays a v3 clip instead of the BS-01 LegFix clip
        /// (tough walking = tough_walk; sniper and electrobot walking / run_forward / run_backward = their own clips; the tough's runs play the
        /// BS-01 clips) and that clip's planted window per foot: the BS-01 window of the same gait moved edge by edge by the v3 clip's own stance
        /// </summary>
        static bool V3Window(string type, string st, int foot, out float a, out float b)
        {
            a = b = 0f;
            switch (type)
            {
                case "tough":
                    if (st == "walking") { if (foot == 0) { a = 0.208f; b = 0.755f; } else { a = 0.690f; b = 0.276f; } return true; }
                    return false;
                case "sniper":
                    switch (st)
                    {
                        case "walking": if (foot == 0) { a = 0.225f; b = 0.776f; } else { a = 0.703f; b = 0.280f; } return true;
                        case "run_forward": if (foot == 0) { a = 0.237f; b = 0.771f; } else { a = 0.713f; b = 0.285f; } return true;
                        case "run_backward": if (foot == 0) { a = 0.237f; b = 0.759f; } else { a = 0.719f; b = 0.288f; } return true;
                    }
                    return false;
                case "electrobot":
                    switch (st)
                    {
                        case "walking": if (foot == 0) { a = 0.229f; b = 0.751f; } else { a = 0.694f; b = 0.280f; } return true;
                        case "run_forward": if (foot == 0) { a = 0.233f; b = 0.738f; } else { a = 0.705f; b = 0.285f; } return true;
                        case "run_backward": if (foot == 0) { a = 0.262f; b = 0.759f; } else { a = 0.719f; b = 0.296f; } return true;
                    }
                    return false;
            }
            return false;
        }

        /// <summary>This robot's type (ZObject.Type), null without one.</summary>
        string TypeNow => view && view.Obj != null ? view.Obj.Type : null;

        /// <summary>True when this robot's state <paramref name="st"/> plays a v3 clip (see <see cref="V3Window"/>).</summary>
        bool V3Clip(string st) => st != null && V3Window(TypeNow, st, 0, out _, out _);

        static string GaitOf(AnimatorStateInfo s) => s.IsName("walking") ? "walking" : s.IsName("run_forward") ? "run_forward" : s.IsName("run_backward") ? "run_backward" : null;

        static bool InSpan(float ph, float a, float b) => a <= b ? ph >= a && ph <= b : ph >= a || ph <= b;

        /// <summary>The boot's air time (smooth bump, 0 with zero slope at lift-off and landing), v2: exactly outside V2Stance.</summary>
        static float V2Bump(string st, int foot, float ph)
        {
            float a, b;
            switch (st)
            {
                case "walking": if (foot == 0) { a = 0.760f; b = 0.200f; } else { a = 0.260f; b = 0.690f; } break;
                case "run_forward": if (foot == 0) { a = 0.750f; b = 0.220f; } else { a = 0.260f; b = 0.700f; } break;
                case "run_backward": if (foot == 0) { a = 0.780f; b = 0.250f; } else { a = 0.300f; b = 0.740f; } break;
                default: return 0f;
            }
            float len = Mathf.Repeat(b - a, 1f), s = Mathf.Repeat(ph - a, 1f) / Mathf.Max(1e-3f, len);
            if (s >= 1f) return 0f;
            float u = 4f * s * (1f - s); return u * u;
        }

        /// <summary>StanceLockStep hook: the lock's phase window (v2 or as before). With v2 the hook also asks it for a ProcSwingSkip type
        /// (tough, sniper, electrobot: no phase lock before), which then locks in its own clip's window.</summary>
        bool LockPhase(string st, int foot, float ph) => FootLockV2 ? V2Stance(TypeNow, st, foot, ph) : PhaseStance(st, foot, ph);

        /// <summary>StanceLockStep hook (ProcSwing): true = a ProcSwingSkip type keeps its clip's own boot height in state <paramref name="st"/>
        /// (no ProcSwing). As before: always. v2: only while it plays a v3 clip; on a BS-01 clip (the tough's runs, which pass the phase gate)
        /// ProcSwing lifts its boots like every other robot's (the BS-01 clips' own foot goals stand under the ground on its avatar).</summary>
        bool KeepsClipHeight(string st) => !FootLockV2 || V3Clip(st);

        float v3W; int v3WFrame = -1;
        /// <summary>StanceLockStep hook (v2): how far the free boot goes down with the pelvis drop. A ProcSwingSkip type playing a v3 clip keeps
        /// its clip's own boot height over the ground (the drop no longer pulls its swinging boot onto the ground), eased in and out over
        /// LandTime; every other robot: the drop, as before.</summary>
        float SwingDrop(bool skipType, AnimatorStateInfo cs, float dt, bool fresh)
        {
            if (v3WFrame != Time.frameCount)
            {
                if (v3WFrame != Time.frameCount - 1) v3W = 0f;                                         // the switch was off (or no IK pass): ease in from the drop
                v3WFrame = Time.frameCount;
                float want = skipType && V3Clip(GaitOf(cs)) ? 1f : 0f;
                v3W = fresh ? want : Mathf.MoveTowards(v3W, want, dt / Mathf.Max(1e-3f, LandTime));
            }
            return pelvisDrop * (1f - Smooth(v3W));
        }

        /// <summary>StanceLockStep hook: ProcSwing's lift bump (v2 or as before).</summary>
        static float LiftBump(string st, int foot, float ph) => FootLockV2 ? V2Bump(st, foot, ph) : SwingBump(st, foot, ph);

        /// <summary>StanceLockStep hook: the clip-ran-away release distance (v2: x the robot's size).</summary>
        static float ReleaseDistFor(float sc) => FootLockV2 ? V2ReleaseDist * sc : ReleaseDist;

        /// <summary>StanceLockStep hook: the lock weight's time to 1 (taking over) or to 0 (handing back); v2 in a phase-locked gait: never
        /// more than a share of the gait's cycle.</summary>
        float LockTime(bool taking, string gaitName, AnimatorStateInfo cs)
        {
            float t = taking ? LockIn : LockOut;
            if (!FootLockV2 || gaitName == null) return t;
            float cycle = cs.length / Mathf.Max(0.05f, Mathf.Abs(anim.speed));
            return Mathf.Max(0.02f, Mathf.Min(t, (taking ? V2InShare : V2OutShare) * cycle));
        }

        /// <summary>The hip joint of THIS frame without the pelvis drop: the body (anim.bodyPosition before the drop, bodyRotation) plus
        /// the joint's offset in the body's frame, measured on the last drawn pose. Until the first measurement: the bone as read.</summary>
        Vector3 HipNow(int i, Transform hipT, Vector3 bodyP)
        {
            if (!v2HipOk) return hipT.position;
            return bodyP + anim.bodyRotation * v2HipOff[i];
        }

        /// <summary>StanceLockStep hook (v2): the reach guard for a pin just checked. Moves an out-of-reach pin toward the hip as far as
        /// the full pelvis drop needs (or tears it on a jump over V2DragMax); returns the pelvis drop this pin needs (at most the cap).</summary>
        float V2Reach(int i, Vector3 hip, float rmax, float sc)
        {
            float cap = MaxPelvisDrop * sc;
            var d = new Vector2(lockPos[i].x - hip.x, lockPos[i].z - hip.z); float dh = d.magnitude;
            float vy = (hip.y - cap) - lockPos[i].y;                                                    // the hip over the pin with the full drop
            float dmax = Mathf.Abs(vy) < rmax ? Mathf.Sqrt(rmax * rmax - vy * vy) * 0.98f : 0f;
            if (dh > dmax)
            {
                float move = dh - dmax;
                if (dmax <= 0f || move > V2DragMax * sc) { locked[i] = false; ForcedReleases++; ForcedReach[i]++; V2Torn++; return 0f; }
                var u = d / Mathf.Max(dh, 1e-6f);
                lockPos[i].x = hip.x + u.x * dmax; lockPos[i].z = hip.z + u.y * dmax;
                if (game) lockPos[i].y = game.GroundY(lockPos[i].x, lockPos[i].z) + ankleRest[i];
                dh = dmax; DragFrames++; V2Drags++; V2DragCm += move * 100.0;
            }
            float need = (hip.y - lockPos[i].y) - Mathf.Sqrt(Mathf.Max(0f, rmax * rmax - dh * dh));
            return Mathf.Clamp(need, 0f, cap);
        }

        /// <summary>StanceLockStep hook (v2): a boot between its pin and its swing (lock weight between 0 and 1) is lifted by its
        /// distance from the pin x V2PeelSlope x the weight's smoothstep: it leaves and reaches its spot through the air.</summary>
        void V2Peel(int i, ref Vector3 p)
        {
            float w = lockW[i]; if (w <= 0f || w >= 1f) return;
            float dd = new Vector2(p.x - lockPos[i].x, p.z - lockPos[i].z).magnitude;
            float y = lockPos[i].y + V2PeelSlope * dd * Smooth(w);
            if (p.y < y) p.y = y;
        }

        /// <summary>StanceLockStep hook, right after the pelvis drop is applied: this frame's body before the drop and the drop.</summary>
        void V2Body(Vector3 bodyP, float drop) { v2BodyP = bodyP; v2BodyQ = anim.bodyRotation; v2Drop = drop; v2BodyFrame = Time.frameCount; }

        /// <summary>LateUpdate hook (read only): the hip joints' offsets from the body in the body's frame, on the pose just drawn.</summary>
        void V2MeasureHips()
        {
            if (v2BodyFrame != Time.frameCount || !hipL || !hipR) return;
            var b = v2BodyP + Vector3.down * v2Drop; var iq = Quaternion.Inverse(v2BodyQ);
            v2HipOff[0] = iq * (hipL.position - b); v2HipOff[1] = iq * (hipR.position - b); v2HipOk = true;
        }
    }
}
