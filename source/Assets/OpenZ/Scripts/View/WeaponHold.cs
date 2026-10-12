using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// in the right hand with the barrel along the robot's front (its aim: UnitView turns robots to their target), and the
    /// hand on the forward grip). Rifle: Rifle_grip / Rifle_forward_grip; Tough bazooka: Bazooka_grip / Bazooka_forward_grip.
    /// Skipped during the grenade throw (the rifle rides the throwing hand) and after death (ragdoll).
    /// </summary>
    [DefaultExecutionOrder(80)]
    public class WeaponHold : MonoBehaviour
    {
        Transform weapon, grip, foregrip, rHand, lArm, lFore, lHand, lGrip;   // lGrip: BS-01 fist-centre socket under the left hand
        Transform rArm, rFore, rHandBone, head, chest, neck;                      // the tough shoulder hold
        Animator anim;
        UnitAim gunAim;                                                          // combatview: aim direction while shooting
        public bool Suspended;                                                   // set by UnitView (grenade throw, death)
        // every pose, worse while walking because the BS-01 upper mask has no Body): the tube rests ON the right shoulder pad,
        // the right hand is IK'd to the grip, the left fist to the front grip. Tunable live.
        [Tooltip("Tube axis rest point above the right shoulder joint (m)")] public float bazookaRestUp = 0.425f;
        [Tooltip("... and outward from it (m)")] public float bazookaRestOut = 0.14f;
        [Tooltip("Minimum sideways distance tube axis <-> head bone (m): the 0.4 m tube clears the helmet kit, also in twisted clips")] public float bazookaHeadClear = 0.46f;
        [Tooltip("Weapon-local z of the tube point that sits on the pad (grip = 0, rear = +)")] public float bazookaRestAlong = 0.24f;
        [Tooltip("Weapon-local height of the tube axis above the grip")] public float bazookaTubeAxisY = 0.232f;
        [Tooltip("Bazooka stance: how much the chest is levelled upright against leaning leg clips (strafes), 0..1")] public float bazookaChestLevel = 0.85f;
        [Tooltip("Bazooka stance: chest turned into the gun (deg, + = right shoulder back, left shoulder forward)")] public float bazookaChestYaw = 30f;
        [Tooltip("robots_v3_detail tweaks: outward offset (m) of the right-elbow IK hint in the bazooka hold; 0.25 = the old fixed hint, the v3 Tough uses 0.8 (its fist sits in front of the pad, the forearm must clear the fat belly)")] public float bazookaElbowOut = 0.25f;
        bool shoulderHold;
        // ---- robots_v3: the free left arm (Tough: bazooka on the right shoulder held by the right fist; Laser: arm cannon)
        [Tooltip("robots_v3: the left fist hangs beside the body instead of reaching the foregrip (the Tough / Laser references)")] public bool leftHang;
        [Tooltip("robots_v3: hang target below the left shoulder, x the arm's reach")] public float leftHangDown = 0.86f;
        [Tooltip("robots_v3: ... outward (m, unit-root scale)")] public float leftHangOut = 0.14f;
        [Tooltip("robots_v3: ... forward (m, unit-root scale)")] public float leftHangFwd = 0.06f;
        //      The fat v3 bodies need the left elbow OUT; the default hint (0.4 m under the clip's elbow, 0.15 m back)
        //      folds the left arm into the belly / chest (pyro 22 cm, psycho 8-9 cm, measured). With this flag the left-arm IK keeps
        //      the arm plane the clip authored (hint = the clip's own elbow) and runs twice (the fist offset turns with the hand).
        [Tooltip("robot_polish_v1: the left-arm IK hint is the clip's own elbow (keeps the arm plane of pyro_hold / psycho_*: elbows out, clear of the fat body) instead of 0.4 m below / 0.15 m behind it")] public bool leftElbowFromClip;
        /// <summary>robots_v3: IK the left fist to a point below the left shoulder (a little out and forward), elbow slightly out and back.</summary>
        void LeftHang(Vector3 aim)
        {
            var flat = aim; flat.y = 0f; flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : -transform.forward;
            var right = Vector3.Cross(Vector3.up, flat).normalized;
            float k = transform.lossyScale.y;
            float reach = (lFore.position - lArm.position).magnitude + (lHand.position - lFore.position).magnitude + (lGrip ? (lGrip.position - lHand.position).magnitude : 0f);
            var fist = lArm.position + Vector3.down * (reach * leftHangDown) - right * (leftHangOut * k) + flat * (leftHangFwd * k);
            TwoBone(lArm, lFore, lHand, fist - (lGrip ? lGrip.position - lHand.position : Vector3.zero), lFore.position - right * (0.3f * k) - flat * (0.2f * k));
        }
        // ---- robot_models_v2 (v2 Tough): carry pitch of the tube in the shoulder hold
        [Tooltip("robot_models_v2: extra tube pitch (deg, + = muzzle up): BS01Stance's own carry when weapons_v2 is not in the project")] public float carryPitch;
        [Tooltip("robot_models_v2: the most the tube may point above its aim (deg): a safety cap for the v2 Tough (BS01Stance carries it at 16 deg; much steeper and the tube rear sinks into the pad)")] public float maxCarryPitch = 90f;
        [Tooltip("robot_models_v2: bazookaRestUp + carryLift x sin(pitch) (m): the tube pivots on its shoulder cushion, a pitched carry lifts it clear of the pad")] public float carryLift;
        [Tooltip("robot_models_v2: 0 = the left fist on the foregrip, 1 = the clip's own left hand (BS01Stance: a walking psycho carries the MG one-handed, the left fist on its gut)")] [Range(0f, 1f)] public float leftHandFree;
        /// <summary>robot_models_v2: the tube's pitch over its aim (weapons_v2 raise + BS01Stance carry), capped at maxCarryPitch (elevation
        /// angles, so a yaw difference never counts as pitch).</summary>
        Vector3 CarryAim(Vector3 aim, out float pitchUp)
        {
            pitchUp = 0f;
            var b = -transform.forward; b.y = 0f; if (b.sqrMagnitude < 1e-6f) return aim; b.Normalize();
            b = Quaternion.AngleAxis(4f, Vector3.Cross(Vector3.up, b)) * b;
            if (gunAim && gunAim.TryAimDirection(out var onT)) b = onT;
            var flat = new Vector3(aim.x, 0f, aim.z); if (flat.sqrMagnitude < 1e-6f) return aim;
            float cur = (Mathf.Asin(Mathf.Clamp(aim.normalized.y, -1f, 1f)) - Mathf.Asin(Mathf.Clamp(b.normalized.y, -1f, 1f))) * Mathf.Rad2Deg;   // + = muzzle above the aim
            float want = Mathf.Min(cur + carryPitch, maxCarryPitch);
            pitchUp = want;
            return Quaternion.AngleAxis(-(want - cur), Vector3.Cross(Vector3.up, flat).normalized) * aim;
        }

        public static WeaponHold Attach(GameObject robot)
        {
            var h = robot.GetComponent<WeaponHold>(); if (!h) h = robot.AddComponent<WeaponHold>();
            return h.Init() ? h : null;
        }

        bool Init()
        {
            Transform socket = null;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (!weapon && t.name.StartsWith("BS01_Weapon_")) { weapon = t; continue; }          // BS-01: root pivot = grip
                switch (t.name)
                {
                    case "Rifle_Heavy": case "Bazooka": if (!weapon) weapon = t; break;
                    case "Rifle_grip": case "Bazooka_grip": case "Grip": grip = t; break;
                    case "Rifle_forward_grip": case "Bazooka_forward_grip": case "Foregrip": foregrip = t; break;
                    case "RightHandWeapon": socket = t; break;                                        // BS-01 palm socket
                    case "mixamorigRightHand": rHand = t; break;
                    case "mixamorigLeftArm": lArm = t; break;
                    case "mixamorigLeftForeArm": lFore = t; break;
                    case "mixamorigLeftHand": lHand = t; break;
                    case "LeftHandGrip": lGrip = t; break;
                }
            }
            anim = GetComponentInChildren<Animator>();
            if (anim && anim.isHuman)
            {   // Humanoid rigs (BS-01): bones from the avatar, whatever they are called
                var r = anim.GetBoneTransform(HumanBodyBones.RightHand); if (r) rHand = r;
                var a = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); if (a) lArm = a;
                var f = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm); if (f) lFore = f;
                var h = anim.GetBoneTransform(HumanBodyBones.LeftHand); if (h) lHand = h;
            }
            if (anim && anim.isHuman)
            {
                rArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm); rFore = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
                rHandBone = anim.GetBoneTransform(HumanBodyBones.RightHand); head = anim.GetBoneTransform(HumanBodyBones.Head);
                chest = anim.GetBoneTransform(HumanBodyBones.UpperChest); if (!chest) chest = anim.GetBoneTransform(HumanBodyBones.Chest);
                neck = anim.GetBoneTransform(HumanBodyBones.Neck); if (!neck) neck = head;
            }
            shoulderHold = weapon && weapon.name.Contains("Tough") && rArm && rFore && rHandBone;
            if (socket) rHand = socket;                                                               // the grip goes to the palm socket
            if (weapon && !grip) grip = weapon;
            enabled = weapon && grip && rHand;
            return enabled;
        }

        void LateUpdate()
        {
            if (Suspended || !weapon || (anim && !anim.enabled)) return;
            // robot models face -z (UnitView modelYaw 180): the front is -transform.forward
            var aim = -transform.forward; aim.y = 0; if (aim.sqrMagnitude < 1e-4f) return; aim.Normalize();
            aim = Quaternion.AngleAxis(4f, Vector3.Cross(Vector3.up, aim)) * aim;            // a touch muzzle-down, like a ready carry
            if (!gunAim) gunAim = GetComponent<UnitAim>();
            if (gunAim && gunAim.TryAimDirection(out var onTarget)) aim = onTarget;          // combatview: the barrel on the target (yaw + pitch)
            aim = WeaponsV2.HoldAim(this, aim);                                             // weapons_v2: Tough carry / raise / recoil climb, MG kick, sniper sway
            if (shoulderHold)
            {   // robot_models_v2: carry pitch (BS01Stance / weapons_v2) capped for the v2 Tough, the tube lifted on its cushion by the pitch
                aim = CarryAim(aim, out float pitchUp); float up0 = bazookaRestUp;
                bazookaRestUp = up0 + carryLift * Mathf.Sin(Mathf.Max(0f, pitchUp) * Mathf.Deg2Rad);
                ShoulderHold(aim); bazookaRestUp = up0; return;
            }
            weapon.rotation = Quaternion.LookRotation(-aim, Vector3.up);                    // weapon -z (barrel) along the aim
            weapon.position += rHand.position - grip.position;                               // grip in the palm
            if (foregrip && lArm && lFore && lHand)
            {   // BS-01 README: the FIST (LeftHandGrip), not the wrist, lands on the foregrip
                var target = Vector3.Lerp(foregrip.position - (lGrip ? lGrip.position - lHand.position : Vector3.zero), lHand.position, leftHandFree);   // robot_models_v2: 1 = the clip's own left hand (walking psycho)
                if (leftHang) LeftHang(aim);                                                                 // robots_v3: the Laser's left fist hangs free
                else if (leftHandFree < 0.999f && leftElbowFromClip)
                {   // robot_polish_v1: hint = the clip's own elbow (its arm plane); the fist offset turns with the hand, a second pass lands it
                    var hint = lFore.position;
                    for (int i = 0; i < 2; i++)
                        TwoBone(lArm, lFore, lHand, Vector3.Lerp(foregrip.position - (lGrip ? lGrip.position - lHand.position : Vector3.zero), lHand.position, leftHandFree), hint);
                }
                else if (leftHandFree < 0.999f) TwoBone(lArm, lFore, lHand, target, lFore.position + Vector3.down * 0.4f - aim * 0.15f);   // robot_models_v2: a free left hand keeps its clip pose
            }
        }

        /// <summary>Tough: the target weapon pose comes from the shoulder, both arms are IK'd onto it, then the weapon (a child of
        /// the right hand) is placed last in world space.</summary>
        void ShoulderHold(Vector3 aim)
        {
            var flat = aim; flat.y = 0; flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : -transform.forward;
            var right = Vector3.Cross(Vector3.up, flat).normalized;                           // the robot's right side
            float k = transform.lossyScale.y;
            var rot = Quaternion.LookRotation(-aim, Vector3.up);
            if (chest && neck && lArm)
            {   // bazooka stance: the upper body stays upright over leaning / twisting leg clips (the BS-01 upper mask has no Body),
                // squared to the aim and turned a little into the gun; the head keeps looking along the aim
                var lean = Quaternion.FromToRotation(neck.position - chest.position, Vector3.up);
                chest.rotation = Quaternion.Slerp(Quaternion.identity, lean, bazookaChestLevel) * chest.rotation;
                var sh = rArm.position - lArm.position; sh.y = 0;
                float yaw = sh.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(sh, Quaternion.AngleAxis(bazookaChestYaw, Vector3.up) * right, Vector3.up) : 0f;
                var turn = Quaternion.AngleAxis(yaw, Vector3.up);
                chest.rotation = turn * chest.rotation;
                if (head) head.rotation = Quaternion.AngleAxis(-bazookaChestYaw, Vector3.up) * head.rotation;
            }
            var rest = rArm.position + Vector3.up * (bazookaRestUp * k) + right * (bazookaRestOut * k);
            rest += WeaponsV2.HoldKick(this, aim);                                          // weapons_v2: recoil shove along the tube
            if (head)
            {   // clips that twist the torso (strafes, back-pedal) swing the helmet towards the tube: keep the tube beside it
                float side = Vector3.Dot(rest - head.position, right);
                if (side < bazookaHeadClear * k) rest += right * (bazookaHeadClear * k - side);
            }
            var pos = rest - rot * (new Vector3(0f, bazookaTubeAxisY, bazookaRestAlong) * k);
            // grip / foregrip in the target pose (their local offsets in the weapon)
            var gripT = pos + rot * Vector3.Scale(weapon.InverseTransformPoint(grip.position), weapon.lossyScale);
            var foreT = foregrip ? pos + rot * Vector3.Scale(weapon.InverseTransformPoint(foregrip.position), weapon.lossyScale) : gripT;
            // the palm socket / fist sit off the wrist and turn with the IK'd hand: a few passes re-aim the wrist until they land
            for (int i = 0; i < 3; i++)
            {
                TwoBone(rArm, rFore, rHandBone, gripT - (rHand.position - rHandBone.position), rFore.position + Vector3.down * 0.4f + right * bazookaElbowOut);
                if (leftHang && lArm && lFore && lHand) LeftHang(aim);                               // robots_v3: the Tough's left fist hangs free (reference)
                else if (foregrip && lArm && lFore && lHand)
                    TwoBone(lArm, lFore, lHand, foreT - (lGrip ? lGrip.position - lHand.position : Vector3.zero), lFore.position + Vector3.down * 0.4f - right * 0.2f);
            }
            weapon.SetPositionAndRotation(pos, rot);
        }

        /// <summary>Analytic two-bone IK: upper a, lower b, end c to target, elbow on the hint side.</summary>
        static void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint)
        {
            float la = (b.position - a.position).magnitude, lb = (c.position - b.position).magnitude;
            var at = target - a.position; float d = Mathf.Clamp(at.magnitude, 0.01f, la + lb - 0.001f);
            var dir = at.normalized;
            float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2 * la * d), -1f, 1f), sinA = Mathf.Sqrt(1 - cosA * cosA);
            var hp = hint - a.position; var perp = hp - dir * Vector3.Dot(hp, dir);
            perp = perp.sqrMagnitude < 1e-6f ? Vector3.down : perp.normalized;
            var elbow = a.position + dir * (la * cosA) + perp * (la * sinA);
            a.rotation = Quaternion.FromToRotation(b.position - a.position, elbow - a.position) * a.rotation;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, a.position + dir * d - b.position) * b.rotation;
        }
    }
}
