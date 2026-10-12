using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    ///  * grab_scoop_L / _R (11 frames, 0.458 s at 24 fps, the lead foot planted f2-f8, the free LEFT fist sweeps the crate at f5) while
    ///    the robot walks across or onto the crate (Zod picks it up on the move: ZObject.ProcessPickupWP / CheckWalkOverCrate), played at
    ///    the drawn stride over the clip's own 1.2 m/s stride, root XZ not baked (the body stays on the sim's path, COM pinned);
    ///  * grab_stand_L / _R (17 frames, 0.708 s, grab frame 9) when it stands at the crate.
    /// _L = the crate on the robot's left, _R = on its right (the left fist reaches across, the rifle lifted muzzle-up in the right hand).
    /// The weapon stays in the right hand every frame (WeaponHold suspended like the grenade throw: its left-hand IK off, the clip holds
    /// the rifle), and a small two-bone IK nudge (GrabReach: Animator IK, left hand, bell-shaped weight round the grab frame) closes the
    /// last centimetres to the crate handle (the tough / sniper arms are 6-14 cm short in the fitted clips). The crate pops on the grab
    /// </summary>
    public partial class UnitView
    {
        public static bool GrabEnabled = true;
        /// <summary>Clip facts (robot_clips_v1 rc_events.json): length / grab frame in seconds at 1x, the scoop's authored stride (m/s).</summary>
        public const float ScoopLen = 11f / 24f, ScoopFrame = 5f / 24f, StandLen = 17f / 24f, StandFrame = 9f / 24f, ScoopStrideMps = 0.55f / (11f / 24f);
        public static float GrabReachWeight = 0.85f;
        public static int GrabsStarted;
        float grabUntil = -1f, grabFrameAt = -1f, grabSpeed = 1f; string grabState; int grabCrate = -1; bool grabActive; int grabStatesKnown = -1;
        GrabReach grabReach;

        public bool Grabbing => Time.time < grabUntil;
        /// <summary>Seconds to the grab frame of a grab running for crate `crateId` (0 when it has passed), -1 = none.</summary>
        public float GrabFrameIn(int crateId) => Grabbing && grabCrate == crateId ? Mathf.Max(0.001f, grabFrameAt - Time.time) : -1f;

        public bool CanGrab
        {
            get
            {
                if (!GrabEnabled || !anim || !anim.isHuman || Obj == null || Obj.Kind != ZKind.Robot || Dying) return false;
                if (grabStatesKnown < 0) grabStatesKnown = anim.HasState(0, Animator.StringToHash("grab_scoop_L")) && anim.HasState(0, Animator.StringToHash("grab_stand_L")) ? 1 : 0;
                return grabStatesKnown == 1;
            }
        }

        bool GrabMoving => Obj != null && Obj.IsMoving && strideSpeed > 0.3f;

        float ScoopSpeed()
        {
            float native = WalkStrideHu * anim.humanScale * Mathf.Max(0.1f, anim.transform.lossyScale.y) * (ScoopStrideMps / 0.92f);
            return Mathf.Clamp(strideSpeed / Mathf.Max(0.05f, native), 0.7f, 2.4f);
        }

        /// <summary>Real seconds from a grab's start to its grab frame (moving = scoop at the current stride, else the standing grab).</summary>
        public float GrabLead(bool moving) => !anim ? 0f : moving ? ScoopFrame / ScoopSpeed() : StandFrame;

        /// <summary>Starts the grab toward `handle` (the crate handle facing the robot). Returns the real seconds to the grab frame
        /// (-1 = this robot cannot grab).</summary>
        public float StartGrab(Transform handle, Vector3 fallback, int crateId)
        {
            if (!CanGrab) return -1f;
            if (Grabbing && grabCrate == crateId) return GrabFrameIn(crateId);
            var target = handle ? handle.position : fallback;
            bool moving = GrabMoving;
            // which side: the robot's model faces its -z (modelYaw 180); right = Cross(up, fwd)
            var fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward; var right = Vector3.Cross(Vector3.up, fwd);
            bool left = Vector3.Dot(target - transform.position, right) < 0f;
            grabState = (moving ? "grab_scoop_" : "grab_stand_") + (left ? "L" : "R");
            grabSpeed = moving ? ScoopSpeed() : 1f;
            float len = (moving ? ScoopLen : StandLen) / grabSpeed, frame = (moving ? ScoopFrame : StandFrame) / grabSpeed;
            grabUntil = Time.time + len - 0.06f; grabFrameAt = Time.time + frame; grabCrate = crateId; grabActive = true; GrabsStarted++;
            grabCentre = fallback; grabKeepUntil = grabFrameAt + 0.08f;                                     // keep-out until the crate has popped
            if (!grabReach) { grabReach = anim.GetComponent<GrabReach>(); if (!grabReach) grabReach = anim.gameObject.AddComponent<GrabReach>(); }
            grabReach.Begin(handle, target, grabFrameAt, frame, len - frame);
            state = null;                                                                                  // GrabAnim plays it from frame 0
            Debug.Log($"[OpenZ Grab] {Obj.Type} #{Obj.Id} {grabState} x{grabSpeed:F2}: grab frame in {frame:F2} s (crate #{crateId}, {(handle ? handle.name : "lid")})");
            return frame;
        }

        /// <summary>
        /// before the pop): from the grab start until the crate pops, the drawn body keeps at least GrabKeepR from the crate centre
        /// (the lid point), sliding round it on the side it comes from; after the pop the offset eases back to the sim path
        /// (GrabKeepEase s). View only: the sim position and the pickup timing are untouched.</summary>
        public static float GrabKeepR = 1.1f, GrabKeepEase = 0.3f;
        public static float GrabKeepMaxCm;                                                                 // proof: the largest push seen
        Vector3 grabCentre, grabKeepOff; float grabKeepUntil = -1f;
        /// <summary>
        /// leader standing INSIDE the crate: Zod lets units stand on a map item, the drawn body must not): every drawn robot keeps
        /// CrateKeepR from every live grenade crate's centre, sliding round it; the sim position is untouched.</summary>
        public static bool CrateKeep = true;
        public static float CrateKeepR = 1.1f;
        static int crateFrame = -1;
        static readonly System.Collections.Generic.List<Vector3> liveCrates = new System.Collections.Generic.List<Vector3>(16);
        static void LiveCrates(ZGame g)
        {
            if (crateFrame == Time.frameCount) return;
            crateFrame = Time.frameCount; liveCrates.Clear();
            if (g == null || g.World == null) return;
            foreach (var o in g.World.Objects)
                if (o != null && !o.Destroyed && o.Kind == ZKind.MapItem && o.Type == "grenades") liveCrates.Add(g.CenterWorld(o));
        }
        static Vector3 PushOut(Vector3 p, Vector3 c, float r, Vector3 want, float yawDeg)
        {
            var q = p + want; var d = new Vector3(q.x - c.x, 0f, q.z - c.z); float m = d.magnitude;
            if (m >= r) return want;
            if (m < 1e-3f) d = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.back; else d /= m;           // dead centre: back off where it came from
            return want + d * (r - m);
        }
        Vector3 GrabKeepOut(Vector3 p, float dt)
        {
            if (!Dying && Obj != null && Obj.Kind == ZKind.Robot)
            {
                Vector3 want = Vector3.zero;
                if (Time.time < grabKeepUntil) want = PushOut(p, grabCentre, GrabKeepR, want, yaw);   // the collector, until the crate pops
                if (CrateKeep)
                {
                    LiveCrates(game);
                    foreach (var c in liveCrates)
                        if ((c.x - p.x) * (c.x - p.x) + (c.z - p.z) * (c.z - p.z) < 4f) want = PushOut(p, c, CrateKeepR, want, yaw);
                }
                if (want.sqrMagnitude > 1e-8f)
                {
                    grabKeepOff = want;                                                                    // grows with the approach: continuous, no snap
                    GrabKeepMaxCm = Mathf.Max(GrabKeepMaxCm, want.magnitude * 100f);
                    return p + grabKeepOff;
                }
            }
            if (grabKeepOff.sqrMagnitude > 1e-8f)
            {
                grabKeepOff = Vector3.Lerp(grabKeepOff, Vector3.zero, 1f - Mathf.Exp(-dt / Mathf.Max(0.05f, GrabKeepEase)));
                if (grabKeepOff.sqrMagnitude < 1e-6f) grabKeepOff = Vector3.zero;
                return p + grabKeepOff;
            }
            return p;
        }

        /// <summary>UnitView's animation step: true while the grab owns the base layer (the upper-body aim layer eases out).</summary>
        bool GrabAnim(float dt)
        {
            if (!grabActive) return false;
            if (Time.time >= grabUntil || Dying) { grabActive = false; state = null; return false; }
            if (state != grabState) { anim.CrossFadeInFixedTime(grabState, 0.07f, 0, 0f); state = grabState; }
            anim.speed = grabSpeed;
            if (hold) hold.Suspended = true;
            if (anim.layerCount > 1) { upWeight = Mathf.MoveTowards(upWeight, 0f, dt * 10f); anim.SetLayerWeight(1, upWeight); }
            return true;
        }
    }

    /// <summary>
    /// weight peaking on the grab frame (GrabReachWeight), so the fist closes on the handle even when the fitted arm is short.</summary>
    public class GrabReach : MonoBehaviour
    {
        Animator anim; Transform target; Vector3 fallback; float frameAt = -1f, up = 0.2f, down = 0.2f;
        public static float LastMissCm = -1f;
        public void Begin(Transform t, Vector3 p, float frameAt, float upT, float downT)
        {
            target = t; fallback = p; this.frameAt = frameAt; up = Mathf.Max(0.08f, upT * 0.9f); down = Mathf.Max(0.08f, Mathf.Min(0.25f, downT));
            if (!anim) anim = GetComponent<Animator>();
        }

        void OnAnimatorIK(int layer)
        {
            if (layer != 0 || !anim || frameAt < 0f) return;
            float now = Time.time, w;
            if (now < frameAt - up || now > frameAt + down) { if (now > frameAt + down) frameAt = -1f; anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f); return; }
            float u = now <= frameAt ? 1f - (frameAt - now) / up : 1f - (now - frameAt) / down;
            w = u * u * (3f - 2f * u) * UnitView.GrabReachWeight;
            var p = target ? target.position : fallback;
            anim.SetIKPosition(AvatarIKGoal.LeftHand, p);
            anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, w);
            if (Mathf.Abs(now - frameAt) < 0.02f) { var h = anim.GetBoneTransform(HumanBodyBones.LeftHand); if (h) LastMissCm = Vector3.Distance(h.position, p) * 100f; }
        }
    }
}
