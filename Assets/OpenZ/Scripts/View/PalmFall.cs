using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// View only: the sim still decides when a palm dies (DetailProp.Break via
    /// DetailPropHub.SimDied, or the view damage of a view-only palm); this only changes how the break looks.
    /// The palm prefab (OpenZ > Palms v3 > 1 Swap palm meshes) holds BODY_ (the intact palm), RUIN_Stump (DetailProp shows it on
    /// the break) and the inactive BREAK_ parts cut from the same mesh in their assembled spots: BREAK_Seg1..n (trunk segments,
    /// split at the reference's rings), BREAK_Crown (leaves + trunk top), BREAK_Coco1..n (coconuts).
    /// On DetailProp.BrokenBy (after DetailProp hid the body, threw its bits, raised dust and played the break):
    ///   1. crack + creak; the trunk above the stump tips over AWAY from the blast about the far edge of the snap, as one rigid
    ///      piece (th'' = 3g / 2L sin th), the leaves swaying a little;
    ///   2. when the crown reaches the ground every part becomes a rigid body with the velocity it had (box / capsule / sphere
    ///      colliders on the debris layer, world contacts only): the trunk comes apart at the rings, the crown hits with a
    ///      bounce (thud + leaf rustle + dust), segments thump, coconuts bounce (plops) and roll;
    ///   3. after 8-12 s each part lies still, sinks into the ground and shrinks away; parts that land in water splash and sink.
    /// </summary>
    [DisallowMultipleComponent]
    public class PalmFall : MonoBehaviour
    {
        [Tooltip("s: parts rest, then sink and fade between these times after the break")] public Vector2 life = new Vector2(8f, 12f);
        [Tooltip("first tilt speed (rad/s) of the falling trunk, a bigger blast pushes harder")] public float kick = 0.45f;

        public static int Falls;
        static readonly List<Transform> live = new List<Transform>();
        public static int Active { get { live.RemoveAll(h => !h); return live.Count; } }
        public const int MaxActive = 10;               // a nuke field of palms: the 11th at once just shows its stump (DetailProp's break)
        public static string LastLog = "";

        static readonly string[] Crack = { "sd_debris_wood_a", "sd_debris_wood_b" };
        static readonly string[] Creak = { "s3_creak_c", "s3_creak_d" };
        static readonly string[] CrownThud = { "Foley/sd_dive_land_sand_a", "Foley/sd_dive_land_sand_b", "Foley/sd_dive_land_sand_c" };
        static readonly string[] LeafRustle = { "DetailsV2/dv2_tumbleweed_rustle_a", "DetailsV2/dv2_tumbleweed_rustle_b" };
        static readonly string[] SegThud = { "sd_seat_thud_a", "sd_seat_thud_b" };
        static readonly string[] CocoBounce = { "sd_debris_plop_small_a", "sd_debris_plop_small_b", "sd_debris_plop_small_c" };
        static readonly string[] Splash = { "sd_debris_splash_a", "sd_debris_splash_b" };
        static PhysicsMaterial woodMat, crownMat, cocoMat;

        internal enum Kind { Seg, Crown, Coco }

        DetailProp prop;
        readonly List<Transform> parts = new List<Transform>();

        void Awake()
        {
            prop = GetComponent<DetailProp>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (DetailKit.Base(t.name).StartsWith("BREAK_")) { parts.Add(t); t.gameObject.SetActive(false); }
            if (prop && parts.Count > 0) prop.BrokenBy += OnBroken;
        }

        void OnDestroy() { if (prop) prop.BrokenBy -= OnBroken; }

        void OnBroken(DetailProp p, Vector3 from)
        {
            if (Active >= MaxActive) { LastLog = "skipped (" + Active + " palms already falling)"; return; }
            var away = transform.position - from; away.y = 0f;
            if (away.sqrMagnitude < 0.04f) { var r = Random.insideUnitCircle.normalized; away = new Vector3(r.x, 0f, r.y); }
            away.Normalize();
            float blast = Mathf.Clamp01(1.5f - (transform.position - from).magnitude / 8f);   // a close blast tips it faster
            var holder = new GameObject("PalmFall_" + (string.IsNullOrEmpty(p.id) ? "palm" : p.id)).transform;
            holder.position = transform.position;
            var bodies = new List<Part>();
            foreach (var t in parts)
            {
                if (!t) continue;
                string n = DetailKit.Base(t.name);
                var k = n.StartsWith("BREAK_Crown") ? Kind.Crown : n.StartsWith("BREAK_Coco") ? Kind.Coco : Kind.Seg;
                // a parent per part: physics moves the parent, the mesh child can sway (the crown) without fighting the body
                var b = new GameObject(n).transform;
                b.SetPositionAndRotation(t.position, t.rotation); b.SetParent(holder, true);
                t.gameObject.SetActive(true);
                t.SetParent(b, true);
                foreach (var rr in t.GetComponentsInChildren<Renderer>(true)) rr.enabled = true;   // DetailProp hid every body renderer
                bodies.Add(new Part { t = b, mesh = t, kind = k, rest = t.localRotation });
            }
            Falls++; live.Add(holder);
            StartCoroutine(Fall(holder, bodies, away, blast));
        }

        internal sealed class Part
        {
            public Transform t, mesh; public Kind kind; public Quaternion rest; public Rigidbody rb;
            public int hits; public float nextHit, sinkAt; public bool wet, sinking, landed; public PalmFall owner;
        }

        static float Ground(Vector3 p)
        {
            var g = ZGame.I;
            return g ? g.GroundY(p.x, p.z) : 0f;
        }

        IEnumerator Fall(Transform holder, List<Part> ps, Vector3 away, float blast)
        {
            var game = ZGame.I; var fx = OpenZFxPlayer.I;
            float s = Mathf.Abs(transform.lossyScale.y);
            float L = Mathf.Max(2f, prop.hitHeight);                                        // already scaled by DetailProp.Start
            // hinge: the far edge of the snap, just above the stump
            float footR = 0.35f * s;
            foreach (var q in ps) if (q.kind == Kind.Seg) { var b = Bounds(q); footR = Mathf.Max(footR, Mathf.Min(b.extents.x, b.extents.z) * 0.8f); break; }
            var pivot = transform.position + Vector3.up * (0.22f * s) + away * footR;
            var axis = Vector3.Cross(Vector3.up, away).normalized;
            var mid = transform.position + Vector3.up * (L * 0.45f);
            DetailProp.Play("", Crack, mid, 0.7f, Random.Range(0.8f, 0.92f), -1, 0f);          // the snap
            Log("crack", mid);
            var p0 = new Vector3[ps.Count]; var r0 = new Quaternion[ps.Count];
            for (int i = 0; i < ps.Count; i++) { p0[i] = ps[i].t.position; r0[i] = ps[i].t.rotation; }
            Part crown = ps.Find(q => q.kind == Kind.Crown);
            float th = 3f * Mathf.Deg2Rad, w = kick + 0.5f * blast, k3 = 3f * 9.81f / (2f * L), t0 = Time.time;
            bool creaked = false;
            while (true)
            {
                float dt = game && game.paused ? 0f : Mathf.Min(Time.deltaTime, 0.05f);
                w += k3 * Mathf.Sin(th) * dt; th += w * dt;
                if (!creaked && Time.time - t0 > 0.12f)
                {
                    creaked = true;
                    DetailProp.Play("", Creak, mid, 0.55f, Random.Range(0.62f, 0.75f), -1, 0f); Log("creak", mid);
                }
                var q = Quaternion.AngleAxis(th * Mathf.Rad2Deg, axis);
                for (int i = 0; i < ps.Count; i++) ps[i].t.SetPositionAndRotation(pivot + q * (p0[i] - pivot), q * r0[i]);
                if (crown != null && crown.mesh)
                {   // the leaves sway a little on the way down (the mesh child only)
                    float a = Mathf.Min(1f, th * 2f) * 3.5f;
                    crown.mesh.localRotation = crown.rest * Quaternion.Euler(Mathf.Sin((Time.time - t0) * 7.3f) * a, 0f, Mathf.Sin((Time.time - t0) * 5.1f + 1f) * a);
                }
                // the crown (or the trunk top) reaches the ground / water: hand over to physics
                bool hit = th > 88f * Mathf.Deg2Rad || Time.time - t0 > 4f;
                if (!hit && crown != null)
                {
                    var b = Bounds(crown); var c = b.center;
                    float g = Ground(c); if (OpenZFxPlayer.IsWaterTile(c)) g = Mathf.Max(g, DetailKit.WaterY(game ? game.level : "L02"));
                    hit = b.min.y <= g + 0.25f * s;
                }
                if (hit) break;
                yield return null;
            }
            // release: every part keeps the velocity of the rigid fall (v = w x r), the trunk splits at the rings
            if (!woodMat) woodMat = new PhysicsMaterial("PalmWood") { bounciness = 0.12f, dynamicFriction = 0.75f, staticFriction = 0.9f, bounceCombine = PhysicsMaterialCombine.Maximum };
            if (!crownMat) crownMat = new PhysicsMaterial("PalmCrown") { bounciness = 0.38f, dynamicFriction = 0.9f, staticFriction = 1f, bounceCombine = PhysicsMaterialCombine.Maximum };
            if (!cocoMat) cocoMat = new PhysicsMaterial("PalmCoconut") { bounciness = 0.5f, dynamicFriction = 0.45f, staticFriction = 0.6f, bounceCombine = PhysicsMaterialCombine.Maximum };
            var omega = axis * w;
            int layer = BuildingBreaker.DebrisLayer;
            float now = Time.time;
            foreach (var q in ps)
            {
                if (!q.t) continue;
                q.owner = this;
                q.t.gameObject.layer = layer;
                var mf = q.mesh.GetComponent<MeshFilter>();
                var mb = mf && mf.sharedMesh ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.5f);
                var col = q.mesh.gameObject;                                                 // colliders on the mesh child (its own frame = tight boxes)
                col.layer = layer;
                var size = mb.size;
                if (q.kind == Kind.Coco)
                {
                    var sc = col.AddComponent<SphereCollider>(); sc.center = mb.center; sc.radius = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 0.47f; sc.sharedMaterial = cocoMat;
                }
                else if (q.kind == Kind.Crown)
                {
                    var bc = col.AddComponent<BoxCollider>(); bc.center = mb.center; bc.size = Vector3.Scale(size, new Vector3(0.7f, 0.7f, 0.55f)); bc.sharedMaterial = crownMat;
                }
                else
                {   // chunky faceted blocks tumble and settle on a face (box); a long slim segment rolls (capsule)
                    int d = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                    float len = size[d], dia = (size.x + size.y + size.z - len) * 0.5f;
                    if (len > dia * 1.4f)
                    {
                        var cc = col.AddComponent<CapsuleCollider>(); cc.direction = d; cc.center = mb.center; cc.radius = dia * 0.45f; cc.height = len * 0.96f; cc.sharedMaterial = woodMat;
                    }
                    else
                    {
                        var bc = col.AddComponent<BoxCollider>(); bc.center = mb.center; bc.size = size * 0.9f; bc.sharedMaterial = woodMat;
                    }
                }
                var rb = q.t.gameObject.AddComponent<Rigidbody>();
                var wb = Bounds(q);
                rb.mass = q.kind == Kind.Coco ? 3f : q.kind == Kind.Crown ? 70f : Mathf.Clamp(wb.size.x * wb.size.y * wb.size.z * 120f, 20f, 260f);
                rb.linearDamping = q.kind == Kind.Crown ? 0.6f : 0.05f;
                rb.angularDamping = q.kind == Kind.Coco ? 0.35f : q.kind == Kind.Crown ? 2.5f : 0.8f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var c = wb.center;
                var v = Vector3.Cross(omega, c - pivot);
                if (q.kind == Kind.Seg) v += (Random.insideUnitSphere * 0.6f + Vector3.up * 0.4f);              // the rings let go
                if (q.kind == Kind.Coco) v += away * Random.Range(0.8f, 2.2f) + Random.insideUnitSphere * 1.5f + Vector3.up * 1.2f;
                rb.linearVelocity = v;
                rb.angularVelocity = omega + Random.insideUnitSphere * (q.kind == Kind.Coco ? 6f : q.kind == Kind.Seg ? 1.2f : 0.4f);
                q.rb = rb;
                q.sinkAt = now + Random.Range(life.x, life.y);
                var relay = q.t.gameObject.AddComponent<PalmPartContact>(); relay.part = q;
            }
            Log("release " + ps.Count + " parts at " + (th * Mathf.Rad2Deg).ToString("0") + " deg", pivot);
            // rest, then sink and fade
            float wy = DetailKit.WaterY(game ? game.level : "L02");
            while (true)
            {
                bool any = false;
                float t = Time.time;
                foreach (var q in ps)
                {
                    if (!q.t) continue;
                    any = true;
                    var pos = q.t.position;
                    if (!q.sinking && !q.wet && pos.y < wy && OpenZFxPlayer.IsWaterTile(pos) && Ground(pos) < wy - 0.05f)
                    {   // into the water: splash, then sink slowly
                        q.wet = true; q.sinkAt = t;
                        DetailProp.Play("", Splash, pos, 0.5f, Random.Range(0.85f, 1.1f), -1, 0f); Log("splash", pos);
                        if (fx) { fx.Splash(new Vector3(pos.x, wy, pos.z), q.kind == Kind.Crown ? 0.8f : 0.45f); fx.Ripple(new Vector3(pos.x, wy, pos.z), 0.8f); }
                    }
                    if (!q.sinking && pos.y < Ground(pos) - 2f) q.sinkAt = t;                    // fell through a hole: go now
                    if (!q.sinking && t >= q.sinkAt)
                    {
                        q.sinking = true; q.sinkAt = t;
                        if (q.rb) { q.rb.isKinematic = true; q.rb.interpolation = RigidbodyInterpolation.None; }
                        foreach (var cl in q.t.GetComponentsInChildren<Collider>()) cl.enabled = false;
                    }
                    if (q.sinking)
                    {
                        float k = (t - q.sinkAt) / (q.wet ? 3f : 2.2f);
                        float dt = game && game.paused ? 0f : Time.deltaTime;
                        var b = Bounds(q);
                        q.t.position = pos + Vector3.down * (dt * Mathf.Max(0.25f, b.size.y) / (q.wet ? 3f : 2.2f) * 1.1f);
                        if (k > 0.7f) q.t.localScale = Vector3.one * Mathf.Clamp01(1f - (k - 0.7f) / 0.3f);   // the last bit shrinks away
                        if (k >= 1f) { Destroy(q.t.gameObject); q.t = null; }
                    }
                }
                if (!any) break;
                yield return null;
            }
            if (holder) Destroy(holder.gameObject);
        }

        internal void Contact(Part q, Collision c)
        {
            if (q.sinking || q.wet || Time.time < q.nextHit) return;
            float v = c.relativeVelocity.magnitude;
            if (v < (q.kind == Kind.Coco ? 0.9f : 1.3f)) return;
            if (q.hits >= (q.kind == Kind.Coco ? 4 : 3)) return;
            q.hits++; q.nextHit = Time.time + 0.16f;
            var at = c.contactCount > 0 ? c.GetContact(0).point : q.t.position;
            float vol = Mathf.Clamp(v / 7f, 0.2f, 1f);
            var fx = OpenZFxPlayer.I;
            switch (q.kind)
            {
                case Kind.Crown:
                    DetailProp.Play("", CrownThud, at, 0.75f * vol + 0.15f, Random.Range(0.72f, 0.85f), -1, 0f);
                    DetailProp.Play("", LeafRustle, at, 0.55f * vol + 0.1f, Random.Range(0.9f, 1.05f), -1, 0f);
                    if (fx && q.hits == 1) { fx.Dust(at + Vector3.up * 0.2f, 0.9f); fx.Dust(at + Random.insideUnitSphere.normalized * 1.2f, 0.6f); }
                    Log("crown thud v " + v.ToString("0.0"), at);
                    break;
                case Kind.Seg:
                    DetailProp.Play("", SegThud, at, 0.6f * vol + 0.1f, Random.Range(0.68f, 0.85f), -1, 0f);
                    if (fx && q.hits == 1) fx.Dust(at + Vector3.up * 0.15f, 0.45f);
                    Log("segment thud v " + v.ToString("0.0"), at);
                    break;
                default:
                    DetailProp.Play("", CocoBounce, at, 0.5f * vol + 0.1f, Random.Range(0.85f, 1.12f), -1, 0f);
                    Log("coconut bounce v " + v.ToString("0.0"), at);
                    break;
            }
        }

        static Bounds Bounds(Part q)
        {
            var r = q.mesh ? q.mesh.GetComponent<Renderer>() : null;
            return r ? r.bounds : new Bounds(q.t.position, Vector3.one * 0.5f);
        }

        void Log(string what, Vector3 at)
        {
            LastLog = (prop ? prop.id : "palm") + " " + what;
            if (Debug.isDebugBuild) Debug.Log("[OpenZ PalmFall] " + LastLog + " at " + at.ToString("F1"));
        }

    }

    /// <summary>palms_v3: collision relay on each falling part's body (added at run time, like AudioImpactRelay; the colliders sit
    /// on its mesh child and Unity sends the contacts to the rigid body's object too).</summary>
    public sealed class PalmPartContact : MonoBehaviour
    {
        internal PalmFall.Part part;
        void OnCollisionEnter(Collision c) { if (part != null && part.owner) part.owner.Contact(part, c); }
    }
}
