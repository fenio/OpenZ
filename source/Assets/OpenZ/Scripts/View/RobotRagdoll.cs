using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// colliders on 11 main bones, character joints with human-ish limits, Animator off, and an impulse from the killing
    /// shot or blast (up and away, tumbling). Cheap: at most MaxActive simulating at once (others play the death clip),
    /// bodies freeze (kinematic) once settled or after MaxSimSeconds, and the corpse stays like Z's dead robot sprite
    /// until UnitView sinks it. Parts sit on the debris layer, which ignores itself and collides with the ground and decks.
    /// </summary>
    public class RobotRagdoll : MonoBehaviour
    {
        public const int MaxActive = 14;
        const float MaxSimSeconds = 6f, TotalMass = 90f;
        /// <summary>A body that overlaps the ground is pushed out at most this fast (m/s; at the engine default of 10 a limb is thrown
        /// back out of the ground in one step). Hand / ankle / toe balls: factors on the forearm's / lower leg's capsule radius.</summary>
        const float MaxPushOut = 2.5f, HandBall = 1.2f, HandBallOut = 0.8f, FootBall = 0.9f, ToeBall = 0.6f;
        /// <summary>Check: corpses frozen, and the deepest any of their colliders lay under the ground at the freeze (m).</summary>
        public static int FrozenCount; public static float MaxSinkM;
        static readonly List<RobotRagdoll> active = new List<RobotRagdoll>();

        readonly List<Rigidbody> bodies = new List<Rigidbody>();
        float age, calm;
        public bool Frozen { get; private set; }

        struct BoneDef { public string name, parent, child; public float radius, mass; public Vector3 lo, hi; }
        // twist (x) and swing (y, z) limits in degrees
        static readonly BoneDef[] Defs =
        {
            new BoneDef { name = "Hips",         parent = null,          child = "Spine1",      radius = 0.24f, mass = 0.20f },
            new BoneDef { name = "Spine1",       parent = "Hips",        child = "Neck",        radius = 0.26f, mass = 0.22f, lo = new Vector3(-20, 25, 20), hi = new Vector3(25, 0, 0) },
            new BoneDef { name = "Head",         parent = "Spine1",      child = null,          radius = 0.2f,  mass = 0.07f, lo = new Vector3(-30, 35, 25), hi = new Vector3(30, 0, 0) },
            new BoneDef { name = "LeftUpLeg",    parent = "Hips",        child = "LeftLeg",     radius = 0.13f, mass = 0.09f, lo = new Vector3(-50, 35, 25), hi = new Vector3(80, 0, 0) },
            new BoneDef { name = "LeftLeg",      parent = "LeftUpLeg",   child = "LeftFoot",    radius = 0.11f, mass = 0.07f, lo = new Vector3(-100, 5, 5), hi = new Vector3(0, 0, 0) },
            new BoneDef { name = "RightUpLeg",   parent = "Hips",        child = "RightLeg",    radius = 0.13f, mass = 0.09f, lo = new Vector3(-50, 35, 25), hi = new Vector3(80, 0, 0) },
            new BoneDef { name = "RightLeg",     parent = "RightUpLeg",  child = "RightFoot",   radius = 0.11f, mass = 0.07f, lo = new Vector3(-100, 5, 5), hi = new Vector3(0, 0, 0) },
            new BoneDef { name = "LeftArm",      parent = "Spine1",      child = "LeftForeArm", radius = 0.1f,  mass = 0.045f, lo = new Vector3(-70, 60, 45), hi = new Vector3(40, 0, 0) },
            new BoneDef { name = "LeftForeArm",  parent = "LeftArm",     child = "LeftHand",    radius = 0.09f, mass = 0.035f, lo = new Vector3(-90, 10, 10), hi = new Vector3(0, 0, 0) },
            new BoneDef { name = "RightArm",     parent = "Spine1",      child = "RightForeArm",radius = 0.1f,  mass = 0.045f, lo = new Vector3(-70, 60, 45), hi = new Vector3(40, 0, 0) },
            new BoneDef { name = "RightForeArm", parent = "RightArm",    child = "RightHand",   radius = 0.09f, mass = 0.035f, lo = new Vector3(-90, 10, 10), hi = new Vector3(0, 0, 0) },
        };

        // ragdoll bone name -> humanoid bone (BS-01 and any Humanoid rig); Generic Mixamo rigs fall back to the mixamorig names
        static readonly (string name, HumanBodyBones bone, HumanBodyBones alt)[] Human =
        {
            ("Hips", HumanBodyBones.Hips, HumanBodyBones.Hips), ("Spine1", HumanBodyBones.Chest, HumanBodyBones.Spine),
            ("Neck", HumanBodyBones.Neck, HumanBodyBones.Head), ("Head", HumanBodyBones.Head, HumanBodyBones.Head),
            ("LeftUpLeg", HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftUpperLeg), ("LeftLeg", HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftLowerLeg),
            ("LeftFoot", HumanBodyBones.LeftFoot, HumanBodyBones.LeftFoot), ("RightUpLeg", HumanBodyBones.RightUpperLeg, HumanBodyBones.RightUpperLeg),
            ("RightLeg", HumanBodyBones.RightLowerLeg, HumanBodyBones.RightLowerLeg), ("RightFoot", HumanBodyBones.RightFoot, HumanBodyBones.RightFoot),
            ("LeftArm", HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftUpperArm), ("LeftForeArm", HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftLowerArm),
            ("LeftHand", HumanBodyBones.LeftHand, HumanBodyBones.LeftHand), ("RightArm", HumanBodyBones.RightUpperArm, HumanBodyBones.RightUpperArm),
            ("RightForeArm", HumanBodyBones.RightLowerArm, HumanBodyBones.RightLowerArm), ("RightHand", HumanBodyBones.RightHand, HumanBodyBones.RightHand),
        };

        static Dictionary<string, Transform> Bones(GameObject robot, Animator anim)
        {
            var bones = new Dictionary<string, Transform>();
            if (anim && anim.isHuman)
                foreach (var h in Human) { var t = anim.GetBoneTransform(h.bone); if (!t) t = anim.GetBoneTransform(h.alt); if (t) bones[h.name] = t; }
            else
                foreach (var t in robot.GetComponentsInChildren<Transform>())
                    if (t.name.StartsWith("mixamorig")) bones[t.name.Substring(9)] = t;
            return bones;
        }

        public static bool CanStart() { active.RemoveAll(r => !r || r.Frozen); return active.Count < MaxActive; }

        /// <summary>Turn this robot into a ragdoll and throw it. push = horizontal direction away from the killer,
        /// strength 1 = rifle hit, ~2.5 = missile / explosion.</summary>
        /// <summary></summary>
        public static float LastStrength, LastLift, LastAt, LastSpeed; public static int Launched; public static Vector3 LastPos;
        public static RobotRagdoll Launch(GameObject robot, Vector3 push, float strength) => Launch(robot, push, strength, 1f, 0u);
        /// <summary>lift = share of the upward throw (blast power at the robot); seed = per robot and blast (0 = Random).</summary>
        public static RobotRagdoll Launch(GameObject robot, Vector3 push, float strength, float lift, uint seed)
        {
            var anim = robot.GetComponentInChildren<Animator>();
            var bones = Bones(robot, anim);
            if (!bones.ContainsKey("Hips") || !bones.ContainsKey("Spine1")) return null;

            if (anim) anim.enabled = false;
            foreach (var sr in robot.GetComponentsInChildren<SkinnedMeshRenderer>()) sr.updateWhenOffscreen = true;
            var rd = robot.AddComponent<RobotRagdoll>();
            int layer = BuildingBreaker.DebrisLayer;
            float scale = robot.transform.lossyScale.y;
            var rbOf = new Dictionary<string, Rigidbody>();
            foreach (var d in Defs)
            {
                if (!bones.TryGetValue(d.name, out var b)) continue;
                var rb = b.gameObject.AddComponent<Rigidbody>();
                rb.mass = TotalMass * d.mass; rb.linearDamping = 0.05f; rb.angularDamping = 0.6f;
                rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.maxAngularVelocity = 18f;
                rb.maxDepenetrationVelocity = MaxPushOut; rb.solverIterations = 12; rb.solverVelocityIterations = 4;   // eased out of the ground, joints held together
                b.gameObject.layer = layer;
                var cap = b.gameObject.AddComponent<CapsuleCollider>();
                float r = RobotV3.RagdollRadius(robot, d.name, d.radius) * scale;                              // robot_models_v2 review: v3 body sizes
                if (d.child != null && bones.TryGetValue(d.child, out var c))
                {   // capsule along the bone, in the bone's local space
                    var local = b.InverseTransformPoint(c.position);
                    int axis = Mathf.Abs(local.x) > Mathf.Abs(local.y) ? (Mathf.Abs(local.x) > Mathf.Abs(local.z) ? 0 : 2) : (Mathf.Abs(local.y) > Mathf.Abs(local.z) ? 1 : 2);
                    cap.direction = axis; cap.center = local * 0.5f;
                    float len = local.magnitude; var ls = b.lossyScale; float s = axis == 0 ? ls.x : axis == 1 ? ls.y : ls.z;
                    cap.radius = r / Mathf.Max(1e-4f, Mathf.Abs(s)); cap.height = len + cap.radius;
                }
                else { cap.direction = 1; var ls = b.lossyScale.y; cap.radius = r / Mathf.Max(1e-4f, ls); cap.height = cap.radius * 2.4f; cap.center = new Vector3(0, cap.radius * 0.6f, 0); }
                rbOf[d.name] = rb; rd.bodies.Add(rb);
                if (d.parent != null && rbOf.TryGetValue(d.parent, out var prb))
                {
                    var j = b.gameObject.AddComponent<CharacterJoint>();
                    j.connectedBody = prb; j.enableProjection = true; j.enablePreprocessing = false;
                    j.axis = Vector3.right; j.swingAxis = Vector3.forward;
                    j.lowTwistLimit = new SoftJointLimit { limit = d.lo.x }; j.highTwistLimit = new SoftJointLimit { limit = d.hi.x };
                    j.swing1Limit = new SoftJointLimit { limit = d.lo.y }; j.swing2Limit = new SoftJointLimit { limit = d.lo.z };
                }
            }
            RobotV3.AddPads(robot, bones, rbOf, scale);                                                 // robot_models_v2 review: v3 shoulder pads never sink into the ground
            AddEnds(robot, bones, rbOf, scale, layer);                                                  // hands, feet and the held weapon rest on the ground too
            // throw: up and away from the killer, with tumble (Z: blasted robots fly; shot robots fall back)
            push.y = 0; if (push.sqrMagnitude < 1e-4f) push = Random.insideUnitSphere; push.y = 0; push.Normalize();
            // with little lift), +-12 deg of seeded scatter on the direction and +-15 % on the speed (never the same arc twice)
            float u0 = (seed & 1023u) / 1023f, u1 = ((seed >> 10) & 1023u) / 1023f, u2 = ((seed >> 20) & 1023u) / 1023f;
            if (seed != 0u) push = Quaternion.Euler(0f, (u0 - 0.5f) * 24f, 0f) * push;
            float spd = seed != 0u ? Mathf.Lerp(2.4f, 3.3f, u1) : Random.Range(2.2f, 3.4f), upv = seed != 0u ? Mathf.Lerp(1.7f, 2.5f, u2) : Random.Range(1.6f, 2.6f);
            var v = push * spd * strength + Vector3.up * upv * strength * lift;
            foreach (var rb in rd.bodies) rb.linearVelocity = v * Random.Range(0.9f, 1.1f);
            LastStrength = strength; LastLift = lift; Launched++; LastPos = robot.transform.position; LastAt = Time.time; LastSpeed = v.magnitude;
            if (rbOf.TryGetValue("Spine1", out var chest)) chest.AddForce(push * 55f * strength, ForceMode.Impulse);   // the chest takes the hit first
            if (rbOf.TryGetValue("Hips", out var hips)) hips.angularVelocity = Vector3.Cross(Vector3.up, push) * Random.Range(2f, 4.5f) * Mathf.Min(strength, 2f);
            active.Add(rd);
            OzRockBody.Ragdoll(robot, rd);                                                              // items 331 + 332: the body meets the rocks (when one is near)
            OzBodySounds.Launched(rd, v);                                                               // J15: a soft whoosh when thrown hard, a metal thud when it lands
            return rd;
        }

        static readonly (string end, string body, float radius)[] Ends =
        {
            ("LeftHand", "LeftForeArm", 0.09f), ("RightHand", "RightForeArm", 0.09f), ("LeftFoot", "LeftLeg", 0.11f), ("RightFoot", "RightLeg", 0.11f),
        };

        /// <summary>Hands, feet and the held weapon have no body of their own: each gets a collider on the body that carries it (a ball
        /// on the fist, on the ankle and on the toes; a capsule along the weapon), so they rest on the ground with the limbs instead of
        /// hanging through it. Capsule colliders on the debris layer: Freeze turns them off with the others.</summary>
        static void AddEnds(GameObject robot, Dictionary<string, Transform> bones, Dictionary<string, Rigidbody> bodies, float scale, int layer)
        {
            foreach (var e in Ends)
            {
                if (!bones.TryGetValue(e.end, out var b) || !b || !bodies.ContainsKey(e.body) || !bones.TryGetValue(e.body, out var pb) || !pb) continue;
                float r = RobotV3.RagdollRadius(robot, e.body, e.radius) * scale;
                var along = b.position - pb.position; if (along.sqrMagnitude < 1e-8f) continue;
                if (!e.end.EndsWith("Foot")) Ball(b, b.position + along.normalized * (r * HandBallOut), r * HandBall, layer);
                else
                {
                    Ball(b, b.position, r * FootBall, layer);
                    if (b.childCount > 0) Ball(b, b.GetChild(0).position, r * ToeBall, layer);
                }
            }
            var view = robot.GetComponent<UnitView>(); var w = view ? AnticSockets.Weapon(view) : null;
            if (!w || !w.GetComponentInParent<Rigidbody>()) return;
            bool any = false; var lb = new Bounds();
            foreach (var mf in w.GetComponentsInChildren<MeshFilter>())
            {
                var m = mf.sharedMesh; var mr = mf.GetComponent<Renderer>(); if (!m || !mr || !mr.enabled) continue;
                var M = w.worldToLocalMatrix * mf.transform.localToWorldMatrix; var mb = m.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = M.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f)));
                    if (any) lb.Encapsulate(p); else { lb = new Bounds(p, Vector3.zero); any = true; }
                }
            }
            if (!any) return;
            var go = new GameObject("RagdollWeaponCol") { layer = layer };
            go.transform.SetParent(w, false);
            var wc = go.AddComponent<CapsuleCollider>();
            var sz = lb.size; int ax = sz.x > sz.y ? (sz.x > sz.z ? 0 : 2) : (sz.y > sz.z ? 1 : 2);
            float len = sz[ax], thick = (sz.x + sz.y + sz.z - len) * 0.25f;                              // half the mean of the two short sides
            wc.direction = ax; wc.center = lb.center; wc.radius = Mathf.Max(thick, len * 0.03f); wc.height = Mathf.Max(len, wc.radius * 2f);
        }

        /// <summary>A ball collider (a capsule of height 2 r) on bone `host`, centred on the world point, world radius r.</summary>
        static void Ball(Transform host, Vector3 world, float r, int layer)
        {
            host.gameObject.layer = layer;
            var cap = host.gameObject.AddComponent<CapsuleCollider>();
            float ls = Mathf.Max(1e-4f, Mathf.Abs(host.lossyScale.y));
            cap.direction = 1; cap.center = host.InverseTransformPoint(world); cap.radius = r / ls; cap.height = cap.radius * 2f;
        }

        void FixedUpdate()
        {
            if (Frozen) return;
            age += Time.fixedDeltaTime;
            float maxV = 0; foreach (var rb in bodies) if (rb) maxV = Mathf.Max(maxV, rb.linearVelocity.magnitude);
            calm = maxV < 0.15f ? calm + Time.fixedDeltaTime : 0;
            if ((calm > 0.6f && age > 1f) || age > MaxSimSeconds) Freeze();
        }

        /// <summary>Settled: stop simulating, keep the pose as a corpse.</summary>
        public void Freeze()
        {
            if (Frozen) return;
            Frozen = true; active.Remove(this);
            var fxp = OpenZFxPlayer.I; FrozenCount++;
            if (fxp) foreach (var c in GetComponentsInChildren<CapsuleCollider>()) if (c.enabled) { var cb = c.bounds; MaxSinkM = Mathf.Max(MaxSinkM, fxp.GroundAt(cb.center) - cb.min.y); }
            foreach (var j in GetComponentsInChildren<CharacterJoint>()) Destroy(j);
            foreach (var rb in bodies) if (rb) { rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None; }
            foreach (var c in GetComponentsInChildren<CapsuleCollider>()) c.enabled = false;
        }

        /// <summary>Lowest point of the body (for the sink-away).</summary>
        public void OnDisable() { active.Remove(this); }
    }
}
