using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The crushed can leaves the fist on the toss release frame with the
    /// authored velocity, tumbles as a rigid body (capsule on the debris layer: it touches the world, never units or other debris),
    /// clatters on every real bounce (gain by impact speed, 0.08 s apart), comes to rest and pops away exactly DisposeAfter s after
    /// its first landing (golden puff + the crunchy pop). Into open water: a splash, then it sinks out of sight (never rests in water).
    /// View only: UnityEngine physics on a cosmetic layer, the sim never sees it.
    /// </summary>
    public sealed class AnticCan : MonoBehaviour
    {
        /// <summary>Release velocity in the robot frame (m/s at authored scale): x = to the robot's LEFT, y = up, z = forward.</summary>
        public static Vector3 TossVel = new Vector3(1.6f, 2.4f, 2.2f);
        public static float DisposeAfter = 15f, Radius = 0.072f, HalfHeight = 0.013f, ClatterMin = 0.55f, ClatterGap = 0.08f;
        public const int MaxLive = 8;
        public static int Thrown, Clatters, Disposed, Sunk;
        public static readonly List<string> Log = new List<string>();
        static readonly List<AnticCan> live = new List<AnticCan>();

        Rigidbody rb; int id; float age, landedAt = -1f, lastClatter = -9f, restT, sinkT = -1f, wetAt = -1f; bool popping, frozen, wet; int clatters; Vector3 sinkFrom; float k = 1f;

        static PhysicsMaterial tin;
        static PhysicsMaterial Tin => tin ? tin : (tin = new PhysicsMaterial("OZ_Can") { bounciness = 0.35f, dynamicFriction = 0.45f, staticFriction = 0.6f, bounceCombine = PhysicsMaterialCombine.Maximum });

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { live.Clear(); Log.Clear(); Thrown = Clatters = Disposed = Sunk = 0; }

        /// <summary>Unparents the can holder `h` (on the fist) and throws it with world velocity `v`. `k` = robot scale.</summary>
        public static AnticCan Throw(Transform h, Vector3 v, int id, float k)
        {
            if (!h) return null;
            live.RemoveAll(c => !c);
            if (live.Count >= MaxLive) live[0].Dispose("cap");
            h.SetParent(null, true);
            foreach (var r in h.GetComponentsInChildren<Renderer>(true)) r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;   // thrown free of the robot: it streaks again (the living unit's re-check may have switched it while it was held)
            var go = h.gameObject;
            float s = Mathf.Abs(h.lossyScale.y);
            var hull = new GameObject("Hull") { layer = BuildingBreaker.DebrisLayer };
            hull.transform.SetParent(h, false);
            // the collider follows the crushed mesh itself (a squat crumpled cylinder): its bounds in the holder's space, so the can
            // rests on its side or its end like the drawn mesh, never floating or sunk by a generic radius
            var b = new Bounds(); bool any = false;
            foreach (var mf in h.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var lp = h.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { b = new Bounds(lp, Vector3.zero); any = true; } else b.Encapsulate(lp);
                }
            }
            if (any)
            {
                var box = hull.AddComponent<BoxCollider>(); box.center = b.center; box.size = b.size * 0.94f; box.sharedMaterial = Tin;
            }
            else
            {   // no mesh found: the README fit (radius 0.072 m at the hand scale)
                var cap = hull.AddComponent<CapsuleCollider>();
                cap.direction = 1; cap.radius = Radius / Mathf.Max(1e-3f, s) * k; cap.height = Mathf.Max(cap.radius * 2.05f, (HalfHeight * 2f + Radius * 2f) / Mathf.Max(1e-3f, s) * k);
                cap.center = Vector3.zero; cap.sharedMaterial = Tin;
            }
            var body = go.AddComponent<Rigidbody>();
            body.mass = 0.25f; body.linearDamping = 0.05f; body.angularDamping = 0.25f; body.maxAngularVelocity = 30f;
            body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.linearVelocity = v;
            body.angularVelocity = new Vector3(Random.Range(-9f, 9f), Random.Range(-4f, 4f), Random.Range(-9f, 9f));
            var c = go.AddComponent<AnticCan>();
            c.rb = body; c.id = id; c.k = k;
            live.Add(c); Thrown++;
            c.Note("thrown " + v.magnitude.ToString("F1") + " m/s");
            return c;
        }

        void Update()
        {
            float dt = Time.deltaTime; age += dt;
            if (popping) return;
            if (sinkT >= 0f)
            {
                sinkT += dt; transform.position = sinkFrom + Vector3.down * 0.6f * k * Mathf.SmoothStep(0f, 1f, sinkT / 1.2f);
                if (sinkT >= 1.2f) { Sunk++; Note("sank"); Destroy(gameObject); }
                return;
            }
            var p = transform.position;
            var fxp = OpenZFxPlayer.I;
            if (!wet && fxp && fxp.isActiveAndEnabled && fxp.WetAt(p))
            {
                float wy = fxp.SurfaceY(p);
                if (p.y - Radius * k <= wy)
                {   // into the water: splash, then it sinks away (a can never floats or rests in water)
                    wet = true; wetAt = age;
                    if (WreckFx.OnScreen(p)) { fxp.Splash(new Vector3(p.x, wy, p.z), 0.3f); fxp.Ripple(new Vector3(p.x, wy, p.z), 0.4f); }
                    AnticFx.Sound("dst_debris_splash", p, id, 0.45f);
                    if (rb) { rb.linearDamping = 5f; rb.angularDamping = 5f; }
                    Note("splash");
                }
            }
            if (wet)
            {
                if (age - wetAt > 0.5f) { sinkT = 0f; sinkFrom = transform.position; if (rb) rb.isKinematic = true; foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false; live.Remove(this); }
                return;
            }
            if (!frozen && rb)
            {
                float sp = rb.linearVelocity.magnitude;
                bool still = landedAt >= 0f && (rb.IsSleeping() || (sp < 0.08f && rb.angularVelocity.magnitude < 0.3f));
                restT = still ? restT + dt : 0f;
                if (restT > 0.5f)
                {
                    frozen = true; rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None;
                    var g = ZGame.I; float gy = g ? g.GroundY(p.x, p.z) : p.y;
                    Note($"rest {clatters} clatters, body {(p.y - gy) * 100f:F1} cm over GroundY");
                }
            }
            if (landedAt >= 0f ? age - landedAt >= DisposeAfter : age >= DisposeAfter + 3f) Dispose("timer");
        }

        void OnCollisionEnter(Collision c)
        {
            var at = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            if (landedAt < 0f) { landedAt = age; Note("landed"); }
            float n = c.contactCount > 0 ? Mathf.Abs(Vector3.Dot(c.relativeVelocity, c.GetContact(0).normal)) : c.relativeVelocity.magnitude;
            if (n < ClatterMin || age - lastClatter < ClatterGap || clatters >= 12) return;
            lastClatter = age; clatters++; Clatters++;
            AnticFx.Sound("rob_antic_can_clatter", at, id, Mathf.Clamp(0.3f + n * 0.14f, 0.3f, 1f));
            if (n > 2.5f && WreckFx.OnScreen(at)) { var fxp = OpenZFxPlayer.I; if (fxp && fxp.isActiveAndEnabled) fxp.Dust(at, 0.12f); }
        }

        /// <summary>The disposal pop (15 s after the landing, or the cap): puff + pop sound + the debris scale-out.</summary>
        public void Dispose(string why)
        {
            if (popping) return;
            popping = true; live.Remove(this);
            if (rb) { rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None; }
            foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false;
            AnticFx.Fx(transform.position, "can_dispose_pop", id, 1f);
            Disposed++;
            Note($"dispose ({why}) {(landedAt >= 0f ? age - landedAt : age):F2} s after {(landedAt >= 0f ? "landing" : "release")}");
            AnticFx.PopOut(transform, true, false, "can_dispose", id);
        }

        void Note(string s)
        {
            string line = $"can #{id}: {s} at {age:F2} s";
            if (Log.Count < 80) Log.Add(line);
            if (AnticDirector.Verbose) Debug.Log("[OpenZ Antics] " + line);
        }

        void OnDestroy() { live.Remove(this); }
    }
}
