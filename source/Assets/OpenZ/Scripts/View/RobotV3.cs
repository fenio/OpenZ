using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// with the grunt's 0.62 m radius two psychos side by side overlapped by ~0.6 m). StrideScale (hips / BS-01's 1.317 m) is kept for
    /// WalkStrideHu x Animator.humanScale, and humanScale already carries the avatar's hips height, so UnitView must NOT multiply by it.
    /// Both are measured from the skinned body's bind pose (mesh data, never the animated pose).
    /// </summary>
    [DisallowMultipleComponent]
    public class RobotV3 : MonoBehaviour
    {
        [Tooltip("tough / psycho / sniper / pyro / laser")] public string type;

        /// <summary>BS-01's hips height in its own model space (BS01_Robot.fbx bind pose, m): UnitView.WalkStrideMps belongs to it.</summary>
        public const float BS01HipsHeight = 1.317f;
        /// <summary>View radius = this x half the bind-pose body width (the grunt's 0.62 m is 0.98 x its 1.26 m / 2).</summary>
        public static float RadiusPerHalfWidth = 1.1f;

        struct Measure { public float halfWidth, hips; }
        static readonly Dictionary<Mesh, Measure> cache = new Dictionary<Mesh, Measure>();

        static bool Measured(UnitView v, out Measure m)
        {
            m = default;
            if (!v || !v.GetComponent<RobotV3>()) return false;
            var smr = v.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (!smr || !smr.sharedMesh) return false;
            if (cache.TryGetValue(smr.sharedMesh, out m)) return true;
            var anim = v.GetComponentInChildren<Animator>(true);
            var model = anim ? anim.transform : smr.transform.parent;
            // body width: the mesh's local x extent (the -90 deg FBX axis turn is about x) in the Model's space
            var b = smr.sharedMesh.bounds;
            float sx = model ? Mathf.Abs(smr.transform.lossyScale.x) / Mathf.Max(1e-6f, Mathf.Abs(model.lossyScale.x)) : 1f;
            m.halfWidth = b.extents.x * sx;
            // hips height of the bind pose in the Model's space (the same formula as RobotLegIK.RestAnkles)
            m.hips = BS01HipsHeight;
            var hips = anim && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            int i = hips ? System.Array.IndexOf(smr.bones, hips) : -1;
            var bp = smr.sharedMesh.bindposes;
            if (i >= 0 && i < bp.Length && model)
            {
                Vector3 w = (smr.transform.localToWorldMatrix * bp[i].inverse).GetColumn(3);
                m.hips = model.InverseTransformPoint(w).y;
            }
            cache[smr.sharedMesh] = m;
            return true;
        }

        /// <summary>UnitView.Bind: the view-side separation radius (world m) of a v3 robot, never below the type's fallback.</summary>
        public static float ViewRadius(UnitView v, float fallback)
        {
            if (!Measured(v, out var m) || m.halfWidth <= 0.05f) return fallback;
            var anim = v.GetComponentInChildren<Animator>(true);
            float modelScale = anim ? Mathf.Abs(anim.transform.lossyScale.x) : Mathf.Abs(v.transform.lossyScale.x);
            return Mathf.Clamp(m.halfWidth * modelScale * RadiusPerHalfWidth, fallback, 1.45f);
        }

        /// <summary>UnitView.Bind: the Humanoid stride of this body relative to BS-01's (1 for every non-v3 robot).</summary>
        public static float StrideScale(UnitView v)
        {
            if (!Measured(v, out var m) || m.hips <= 0.1f) return 1f;
            return Mathf.Clamp(m.hips / BS01HipsHeight, 0.5f, 1.5f);
        }

        /// <summary>UnitView (los_clearance_v1 PushOut): the building clearance of a v3 robot in sim px (8 px = 1 m): 0.8 x its view
        /// radius, capped at 6.5 px so a 16 px (one tile) passage stays walkable; 0 = the type's own value.</summary>
        public static double ClearancePx(UnitView v, float radius)
        {
            if (!v || !v.GetComponent<RobotV3>()) return 0.0;
            return System.Math.Min(6.5, radius * 0.8 * 8.0);
        }

        // RobotRagdoll's capsule radii fit the slim BS-01 (torso 0.24 / 0.26 m = the 75th percentile of its torso parts' distance from the
        // spine). The v3 bodies measure about twice that (tough hips 0.50 / chest 0.48 m), so a dead v3 robot lay half inside the ground
        // tools/ragdoll_radii.py), scaled like BS-01's: radius = BS-01's x (v3 part / BS-01 part); head capped at 0.28 m. The pads sit on
        // the shoulder bones, which have no ragdoll body: a sphere at the upper-arm joint (0.9 x the pad's farthest point) carries them.
        static readonly Dictionary<string, (float hips, float chest, float head, float upLeg, float leg, float arm, float foreArm, float pad)> RagdollOf =
            new Dictionary<string, (float, float, float, float, float, float, float, float)>
        {
            { "tough",  (0.51f, 0.49f, 0.27f, 0.17f, 0.18f, 0.11f, 0.13f, 0.29f) },
            { "psycho", (0.46f, 0.40f, 0.28f, 0.17f, 0.17f, 0.11f, 0.12f, 0.33f) },
            { "sniper", (0.35f, 0.38f, 0.28f, 0.12f, 0.12f, 0.10f, 0.08f, 0.23f) },
            { "pyro",   (0.48f, 0.53f, 0.24f, 0.16f, 0.17f, 0.10f, 0.11f, 0.33f) },
            { "laser",  (0.46f, 0.46f, 0.23f, 0.17f, 0.17f, 0.11f, 0.13f, 0.34f) },
            { "electrobot", (0.44f, 0.47f, 0.28f, 0.22f, 0.20f, 0.14f, 0.16f, 0.30f) },
        };

        static bool RagdollFor(GameObject robot, out (float hips, float chest, float head, float upLeg, float leg, float arm, float foreArm, float pad) r)
        {
            r = default;
            var mk = robot ? robot.GetComponent<RobotV3>() : null;
            return mk && mk.type != null && RagdollOf.TryGetValue(mk.type, out r);
        }

        /// <summary>RobotRagdoll.Launch: the capsule radius of ragdoll bone `bone` (its Defs name) for this robot (m, before the root
        /// scale), `def` for every non-v3 robot.</summary>
        public static float RagdollRadius(GameObject robot, string bone, float def)
        {
            if (!RagdollFor(robot, out var r)) return def;
            switch (bone)
            {
                case "Hips": return r.hips;
                case "Spine1": return r.chest;
                case "Head": return r.head;
                case "LeftUpLeg": case "RightUpLeg": return r.upLeg;
                case "LeftLeg": case "RightLeg": return r.leg;
                case "LeftArm": case "RightArm": return r.arm;
                case "LeftForeArm": case "RightForeArm": return r.foreArm;
                case "Pad": return r.pad;
                default: return def;
            }
        }

        /// <summary>electrobot_v1 (model track): the Electrobot's two tesla coils ride on CoilMast_L / CoilMast_R (children of Spine2, carried
        /// Electrobot lying on his back rests on his coils instead of sinking them into the ground. Same layer / Freeze rules as the pads.</summary>
        static void AddCoils(GameObject robot, Dictionary<string, Rigidbody> bodies, float scale)
        {
            var mk = robot ? robot.GetComponent<RobotV3>() : null;
            if (!mk || mk.type != "electrobot" || !bodies.ContainsKey("Spine1")) return;
            foreach (var t in robot.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "CoilMast_L" && t.name != "CoilMast_R") continue;
                var cap = t.gameObject.AddComponent<CapsuleCollider>();
                float ls = Mathf.Max(1e-4f, Mathf.Abs(t.lossyScale.y));
                cap.direction = 1; cap.radius = 0.15f * scale / ls; cap.height = 0.65f * scale / ls; cap.center = new Vector3(0f, cap.height * 0.5f, 0f);
            }
        }

        /// <summary>RobotRagdoll.Launch, after its bodies exist: a pad sphere on each upper-arm body of a v3 robot (a CapsuleCollider of
        /// height 2r, so RobotRagdoll.Freeze turns it off with the others; the debris layer never collides with itself).</summary>
        public static void AddPads(GameObject robot, Dictionary<string, Transform> bones, Dictionary<string, Rigidbody> bodies, float scale)
        {
            if (!RagdollFor(robot, out var r) || r.pad <= 0f) return;
            AddCoils(robot, bodies, scale);
            foreach (var n in new[] { "LeftArm", "RightArm" })
            {
                if (!bones.TryGetValue(n, out var b) || !b || !bodies.ContainsKey(n)) continue;
                var cap = b.gameObject.AddComponent<CapsuleCollider>();
                float ls = Mathf.Max(1e-4f, Mathf.Abs(b.lossyScale.y));
                cap.direction = 1; cap.center = Vector3.zero; cap.radius = r.pad * scale / ls; cap.height = cap.radius * 2f;
            }
        }
    }
}
