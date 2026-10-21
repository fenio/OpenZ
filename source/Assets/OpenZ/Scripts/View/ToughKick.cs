using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// feels heavier; the free hand must not stay frozen). VIEW ONLY: nothing in the simulation changes (no timing, damage, range or rate).
    /// <para>On top of what the game already draws for a rocket (the tough_fire clip, BS01Stance's 9 deg tube climb, RobotWeaponPose's
    /// 0.16 m tube slide on the pad), from the frame AFTER the rocket left the muzzle (WeaponsV2Runner reads the muzzle before this runs
    /// again), for 1.2 s:</para>
    /// <list type="bullet">
    /// <item>the torso rocks back about the robot's right axis (Spine / Chest / UpperChest 35 / 35 / 30 % of <see cref="RockDeg"/>), comes
    /// forward past rest by a fifth and settles (<see cref="ToughKickCurve.Rock"/>); the shoulders, both arms, the head and the bazooka
    /// ride it, so the muzzle flips up with it;</item>
    /// <item>the blast throws the right shoulder back: the upper chest turns into the gun (<see cref="TwistDeg"/>, the right shoulder
    /// back, the tube with it), the head keeps looking ahead (WeaponHold squares the chest to its 12 deg every frame, which took the
    /// clip's own twist out);</item>
    /// <item>the head nods back a little after the body (<see cref="HeadDeg"/>);</item>
    /// <item>the free LEFT arm, which WeaponHold hangs straight below the shoulder (leftHang), swings with the kick: the fist forward as the
    /// body snaps back, then back past rest, the elbow flexing a moment later (<see cref="ArmSwingDeg"/>, <see cref="ElbowDeg"/>). It does
    /// not come up to hold the bazooka: on the real mesh the fist stays 2-7 cm short of the tube in every aim even with the clavicle at its
    /// limit, and the arm stretched toward it runs through the chest, the belly and the face (measured: kit K307's fit check);</item>
    /// <item>the boots stay where they stand (<see cref="PlantFeet"/>): UnitView's body jolt (the whole robot 12 cm back on every rocket,
    /// the standing boots skating with it) is not applied to a Tough this kick draws (<see cref="BodyJolt"/>); the clip's own step and
    /// the rock carry the shot instead.</item>
    /// </list>
    /// Every addition is a time curve of the last shot (<see cref="ToughKickCurve"/>), turned on top of the pose the Animator, UnitAim
    /// and WeaponHold wrote this frame (never accumulated; a bone the Animator did not re-write keeps nothing of it), about the AIM's own
    /// front and right (the tube's heading as WeaponHold laid it this frame, <see cref="AimFront"/>: WeaponHold squares the chest and hangs
    /// the free fist in that frame, and a walking Tough's torso is turned up to 75-80 deg off his legs toward the target), facing-independent,
    /// for Toughs only, no allocations per frame. A rocket while a kick still runs starts the new kick from where the old one is, so a
    /// rocket every reload interval never drifts. <see cref="On"/> = false: the game exactly as before. Added to a Tough by UnitView's
    /// recoil on its first rocket (<see cref="BodyJolt"/>).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(88)]   // after WeaponHold (80) posed the tube and both arms, before WeaponsV2Runner (96) launches from the muzzle
    public sealed class ToughKick : MonoBehaviour
    {
        // ------------------------------------------------------------------------------------------------ the switch and the numbers to turn
        /// <summary>Off = exactly the kick of before (no rock, no twist, no arm swing, UnitView's body jolt as before).</summary>
        public static bool On = true;
        /// <summary>Torso rock back at its peak (deg).</summary>
        public static float RockDeg = 8f;
        /// <summary>The right shoulder thrown back at its peak (deg the upper chest turns into the gun).</summary>
        public static float TwistDeg = 6f;
        /// <summary>The free arm's swing at its peak (deg, the fist forward) and its elbow's flex (deg).</summary>
        public static float ArmSwingDeg = 12f, ElbowDeg = 16f;
        /// <summary>The swing also takes the fist a little out from the body (deg per deg of swing), and a walking Tough swings it this
        /// share only (his waddling hip and thigh come forward under that fist).</summary>
        public static float ArmOut = 0.6f, WalkArm = 0.5f;
        /// <summary>The head's extra nod back at its peak (deg).</summary>
        public static float HeadDeg = 4f;
        /// <summary>A Tough's boots stay planted: UnitView's body jolt (which moves the whole robot, boots included) is not applied to it.</summary>
        public static bool PlantFeet = true;

        // ------------------------------------------------------------------------------------------------ proof counters
        /// <summary>Rockets that started a kick, frames a kick was drawn, frames UnitView's body jolt was held off (boots kept), frames skipped (not drawn).</summary>
        public static int Kicks, Applied, JoltsHeld, NotDrawnSkips;
        /// <summary>Largest angles drawn since <see cref="ResetCounters"/> (deg): rock, twist, arm swing.</summary>
        public static float MaxRock, MaxTwist, MaxArm;
        public static void ResetCounters() { Kicks = Applied = JoltsHeld = NotDrawnSkips = 0; MaxRock = MaxTwist = MaxArm = 0f; }

        static readonly float[] SpineShare = { 0.35f, 0.35f, 0.30f };

        UnitView view; WeaponHold hold; Animator anim; Renderer skin; Transform tube; bool ok;
        Transform spine, chest, upper, head, lArm, lFore;
        Transform[] touched; Quaternion[] baseRot, wroteRot; bool wrote;
        ToughKickCurve.Channel rock, twist, nod, arm, elbow;
        float lastFire = -100f; int stepFrame = -1;

        // ------------------------------------------------------------------------------------------------ hook (UnitView.ApplyRecoil)
        /// <summary>UnitView.ApplyRecoil, the Tough's body jolt (authored 0.06 m): 0 while this kick keeps a Tough's boots planted, else
        /// the authored value. Its first call on a Tough (its first rocket) adds this component.</summary>
        public static float BodyJolt(UnitView v, float authored)
        {
            if (!On || !v) return authored;
            if (!v.TryGetComponent(out ToughKick k)) k = v.gameObject.AddComponent<ToughKick>();
            if (!k.ok || !k.isActiveAndEnabled || !PlantFeet) return authored;
            JoltsHeld++;
            return 0f;
        }

        void Awake()
        {
            view = GetComponent<UnitView>(); hold = GetComponent<WeaponHold>();
            anim = GetComponentInChildren<Animator>();
            ok = view && view.Obj != null && view.Obj.Type == "tough" && hold && anim && anim.isHuman;
            if (ok)
            {
                spine = anim.GetBoneTransform(HumanBodyBones.Spine); chest = anim.GetBoneTransform(HumanBodyBones.Chest);
                upper = anim.GetBoneTransform(HumanBodyBones.UpperChest); head = anim.GetBoneTransform(HumanBodyBones.Head);
                lArm = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); lFore = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                if (!chest) chest = spine;
                if (!upper) upper = chest;
                ok = spine && chest && upper && head && lArm && lFore;
            }
            if (!ok) { enabled = false; return; }
            touched = new[] { spine, chest, upper, head, lArm, lFore };
            baseRot = new Quaternion[touched.Length]; wroteRot = new Quaternion[touched.Length];
            skin = GetComponentInChildren<SkinnedMeshRenderer>();
            foreach (var t in GetComponentsInChildren<Transform>(true))                              // the node WeaponHold poses (its own pick: the first of these)
                if (t.name.StartsWith("BS01_Weapon_", System.StringComparison.Ordinal) || t.name == "Bazooka" || t.name == "Rifle_Heavy") { tube = t; break; }
            rock = new ToughKickCurve.Channel(ToughKickCurve.Rock, 1f); twist = new ToughKickCurve.Channel(ToughKickCurve.Twist, 1f);
            nod = new ToughKickCurve.Channel(ToughKickCurve.Head, 1f); arm = new ToughKickCurve.Channel(ToughKickCurve.Arm, 1f);
            elbow = new ToughKickCurve.Channel(ToughKickCurve.Elbow, 1f);
        }

        void OnDisable() { wrote = false; }

        /// <summary>Once a frame: a rocket that left since the last look starts a kick (UnitView.OnFire stamps LastFireAt on the launch frame).</summary>
        void Step()
        {
            if (stepFrame == Time.frameCount) return;
            stepFrame = Time.frameCount;
            float f = view.LastFireAt;
            if (f <= lastFire + 1e-5f) return;
            lastFire = f;
            if (Time.time - f > 0.25f) return;                                                       // an old shot (the robot was away): no late kick
            rock.Shape = ToughKickCurve.Rock; twist.Shape = ToughKickCurve.Twist; nod.Shape = ToughKickCurve.Head;   // the shapes can be turned live
            arm.Shape = ToughKickCurve.Arm; elbow.Shape = ToughKickCurve.Elbow;
            rock.Trigger(f); twist.Trigger(f); nod.Trigger(f); arm.Trigger(f); elbow.Trigger(f);
            Kicks++;
        }

        void LateUpdate()
        {
            if (!ok) return;
            var F = AimFront();                                                                      // before Restore: the tube exactly as WeaponHold laid it this frame
            Restore();
            if (!On || view == null || view.Obj == null || view.Dying || view.Reacting || !anim.enabled || hold.Suspended) return;
            Step();
            float now = Time.time;
            if (!(rock.Live(now) || twist.Live(now) || nod.Live(now) || arm.Live(now) || elbow.Live(now))) return;
            if (skin && !skin.isVisible) { NotDrawnSkips++; return; }                              // culled: the Animator does not write, so nothing is added
            for (int i = 0; i < touched.Length; i++) baseRot[i] = touched[i].localRotation;
            var R = Vector3.Cross(Vector3.up, F).normalized;                                         // the aim's right (WeaponHold's: Cross(up, flat aim))
            // the torso rock: AngleAxis(-deg, right) turns the top back (UnitView.RecoilRock's sign), each bone about its own joint
            float th = rock.Value(now) * RockDeg;
            if (Mathf.Abs(th) > 0.001f)
            {
                spine.rotation = Quaternion.AngleAxis(-th * SpineShare[0], R) * spine.rotation;
                if (chest != spine) chest.rotation = Quaternion.AngleAxis(-th * SpineShare[1], R) * chest.rotation;
                if (upper != chest) upper.rotation = Quaternion.AngleAxis(-th * SpineShare[2], R) * upper.rotation;
            }
            // the right shoulder thrown back: AngleAxis(+deg, up) is clockwise seen from above (the right shoulder back, the left forward)
            float tw = twist.Value(now) * TwistDeg;
            if (Mathf.Abs(tw) > 0.001f) upper.rotation = Quaternion.AngleAxis(tw, Vector3.up) * upper.rotation;
            float ps = nod.Value(now) * HeadDeg;
            if (Mathf.Abs(ps) > 0.001f || Mathf.Abs(tw) > 0.001f)
                head.rotation = Quaternion.AngleAxis(-ps, R) * Quaternion.AngleAxis(-tw, Vector3.up) * head.rotation;   // nods back, keeps looking ahead
            // the free arm: AngleAxis(-deg, right) swings a hanging limb's end forward, AngleAxis(-deg, front) takes it out to his left
            float k = Walking() ? WalkArm : 1f;
            float ph = arm.Value(now) * ArmSwingDeg * k, ep = elbow.Value(now) * ElbowDeg * k;
            if (Mathf.Abs(ph) > 0.001f) lArm.rotation = Quaternion.AngleAxis(-Mathf.Abs(ph) * ArmOut, F) * Quaternion.AngleAxis(-ph, R) * lArm.rotation;
            if (Mathf.Abs(ep) > 0.001f) lFore.rotation = Quaternion.AngleAxis(-Mathf.Max(ep, -0.25f * ElbowDeg), R) * lFore.rotation;   // the elbow never bends far backwards
            for (int i = 0; i < touched.Length; i++) wroteRot[i] = touched[i].localRotation;
            wrote = true; Applied++;
            if (th > MaxRock) MaxRock = th; if (tw > MaxTwist) MaxTwist = tw; if (ph > MaxArm) MaxArm = ph;
        }

        static readonly int HWalk = Animator.StringToHash("walking"), HRun = Animator.StringToHash("run_forward"), HBack = Animator.StringToHash("run_backward"),
            HStrafeL = Animator.StringToHash("strafe_left"), HStrafeR = Animator.StringToHash("strafe_right");
        static bool Gait(AnimatorStateInfo s) => s.shortNameHash == HWalk || s.shortNameHash == HRun || s.shortNameHash == HBack || s.shortNameHash == HStrafeL || s.shortNameHash == HStrafeR;
        /// <summary>He walks (the sim moves him, or the base layer plays a walk / run / strafe).</summary>
        bool Walking() => view.Obj.IsMoving || Gait(anim.GetCurrentAnimatorStateInfo(0)) || (anim.IsInTransition(0) && Gait(anim.GetNextAnimatorStateInfo(0)));

        /// <summary>The aim's heading: the tube as WeaponHold laid it this frame (weapon -z = the barrel along the aim), flattened. WeaponHold
        /// squares the chest to it and hangs the free fist in its frame, so the rock, the nod and the arm swing go along it too: a walking
        /// Tough faces his path while his torso is turned toward the target, and a kick about the legs' axes would lean him sideways and
        /// swing the free arm into his hip. The robot's own front (models face -z) when there is no tube or it points nearly straight up.</summary>
        Vector3 AimFront()
        {
            var f = tube ? -tube.forward : -transform.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.04f) { f = -transform.forward; f.y = 0f; }
            return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
        }

        /// <summary>Our last write still on the bones (the Animator did not write them this frame): take it off before anything else.</summary>
        void Restore()
        {
            if (!wrote) return;
            wrote = false;
            for (int i = 0; i < touched.Length; i++) if (touched[i] && touched[i].localRotation == wroteRot[i]) touched[i].localRotation = baseRot[i];
        }

        /// <summary>The numbers and counters (for the Play check: execute it, or read it in the console).</summary>
        public static string Report() =>
            $"ToughKick on {On}: rock {RockDeg:F1} deg, twist {TwistDeg:F1} deg, arm {ArmSwingDeg:F0} / elbow {ElbowDeg:F0} deg (out {ArmOut:F2}, walking x{WalkArm:F2}), head {HeadDeg:F1} deg, plant feet {PlantFeet}; " +
            $"kicks {Kicks}, drawn frames {Applied}, body-jolt frames held {JoltsHeld}, frames not drawn {NotDrawnSkips}; largest rock {MaxRock:F1} / twist {MaxTwist:F1} / arm {MaxArm:F1} deg";
    }
}
