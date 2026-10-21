using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// DV108 hero squads, the aim of the heroes.
    /// <list type="bullet">
    /// <item><b>The torso aim.</b> The robot torso aim (SolveRobot: the spine turns the upper body and the rifle onto the target) finds
    /// its three spine bones by the grunt rig's names; the real Alan and Brad models name them otherwise, so their torso never turned to
    /// the target: the rifle was laid along the body's front and the upper body and head stayed in the clip's pose. For a hero the
    /// three bones are now taken from his humanoid rig (Spine, Chest, UpperChest: root to tip), so the heroes aim exactly as the grunts
    /// do (the same solve, limits and ease).</item>
    /// <item><b>The face on the target.</b> While a hero aims (the torso aim has a target), his neck and head turn the face (the
    /// model's face marker) to the target point: the neck <see cref="HeroNeckShare"/> of the turn, the head the rest, at most
    /// <see cref="HeroLookMax"/> deg from the pose (natural limits), eased in over <see cref="HeroLookIn"/> s and out over
    /// <see cref="HeroLookOut"/> s, the look point followed over <see cref="HeroLookFollow"/> s (a new target never snaps the head).
    /// On top of the animated pose every frame (never accumulated). A/B in Play: <see cref="HeroHeadLook"/>, <see cref="HeroTorso"/>.</item>
    /// </list>
    /// View only. Every robot that is not a hero: untouched.
    /// </summary>
    public partial class UnitAim
    {
        public static bool HeroTorso = true, HeroHeadLook = true;
        public static float HeroLookMax = 70f, HeroNeckShare = 0.35f, HeroLookIn = 0.15f, HeroLookOut = 0.35f, HeroLookFollow = 0.08f;
        /// <summary>Play checks: hero frames with the face looking at a target, and the angle between the face and the target then.</summary>
        public static int HeroLookFrames; public static double HeroLookErrSum; public static float HeroLookErrMax;
        public float HeroFaceError { get; private set; } = -1f;

        Transform hHead, hNeck, hFace; Quaternion hHeadAnim, hNeckAnim, hHeadWrote, hNeckWrote; bool hInit, hWrote; float hW; Vector3 hLook; bool hLookOk;

        /// <summary>BindRobot hook: a hero whose rig has no grunt-named spine takes the three spine bones from its humanoid rig.</summary>
        void HeroSpine(ref Transform s0, ref Transform s1, ref Transform s2)
        {
            if (!HeroTorso || obj == null || !obj.IsHero || (s0 && s1 && s2)) return;
            var an = GetComponentInChildren<Animator>();
            if (!an || !an.isHuman) return;
            var a = an.GetBoneTransform(HumanBodyBones.Spine); var b = an.GetBoneTransform(HumanBodyBones.Chest); var c = an.GetBoneTransform(HumanBodyBones.UpperChest);
            if (!c) { c = b; b = null; }
            if (a && b && c) { s0 = a; s1 = b; s2 = c; }
        }

        /// <summary>SolveRobot hook (after the torso aim): a hero's face turns to the target point P while has (see the class notes).</summary>
        void HeroHead(bool has, Vector3 P, float dt)
        {
            if (obj == null || !obj.IsHero) return;
            if (!hInit)
            {
                hInit = true;
                var an = GetComponentInChildren<Animator>();
                if (an && an.isHuman) { hHead = an.GetBoneTransform(HumanBodyBones.Head); hNeck = an.GetBoneTransform(HumanBodyBones.Neck); }
                if (hHead)
                    foreach (var t in hHead.GetComponentsInChildren<Transform>(true))
                        if (t != hHead && t.name.ToLowerInvariant() == "headfront") { hFace = t; break; }
            }
            if (!hHead || !hFace) return;
            // the Animator culled off screen leaves last frame's turned bones: start from the clip's pose then (as the spine does)
            if (hWrote && hHead.localRotation == hHeadWrote && (!hNeck || hNeck.localRotation == hNeckWrote)) { hHead.localRotation = hHeadAnim; if (hNeck) hNeck.localRotation = hNeckAnim; }
            hHeadAnim = hHead.localRotation; if (hNeck) hNeckAnim = hNeck.localRotation;
            bool on = HeroHeadLook && has && view && !view.Dying;
            hW = Mathf.MoveTowards(hW, on ? 1f : 0f, dt / Mathf.Max(0.01f, on ? HeroLookIn : HeroLookOut));
            if (on) { hLook = hLookOk ? Vector3.Lerp(hLook, P, 1f - Mathf.Exp(-dt / Mathf.Max(0.005f, HeroLookFollow))) : P; hLookOk = true; }
            else if (hW <= 0f) hLookOk = false;
            HeroFaceError = -1f;
            if (hW > 0f && hLookOk)
            {
                float w = hW * hW * (3f - 2f * hW);
                for (int pass = 0; pass < 2; pass++)
                {
                    var bone = pass == 0 ? hNeck : hHead; if (!bone) continue;
                    var face = hFace.position - hHead.position; var to = hLook - hFace.position;
                    if (face.sqrMagnitude < 1e-8f || to.sqrMagnitude < 1e-6f) break;
                    Quaternion.FromToRotation(face.normalized, to.normalized).ToAngleAxis(out float ang, out Vector3 ax);
                    if (ang > 180f) ang -= 360f;
                    float lim = pass == 0 ? HeroLookMax * HeroNeckShare : HeroLookMax * (1f - HeroNeckShare);
                    float want = Mathf.Clamp(ang * (pass == 0 ? HeroNeckShare : 1f), -lim, lim) * w;
                    if (Mathf.Abs(want) > 1e-3f && ax.sqrMagnitude > 1e-8f) bone.rotation = Quaternion.AngleAxis(want, ax) * bone.rotation;
                }
            }
            if (on && hLookOk)
            {
                var f = hFace.position - hHead.position; var t = P - hFace.position;
                if (f.sqrMagnitude > 1e-8f && t.sqrMagnitude > 1e-6f)
                {
                    HeroFaceError = Vector3.Angle(f, t);
                    if (hW >= 0.999f) { HeroLookFrames++; HeroLookErrSum += HeroFaceError; HeroLookErrMax = Mathf.Max(HeroLookErrMax, HeroFaceError); }
                }
            }
            hHeadWrote = hHead.localRotation; if (hNeck) hNeckWrote = hNeck.localRotation; hWrote = true;
        }
    }
}
