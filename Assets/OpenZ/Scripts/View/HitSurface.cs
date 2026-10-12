using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// View only. The views have no colliders (picking is screen space), so the shot's line is tested against the target's own
    /// drawn meshes on the CPU: every opaque MeshRenderer under the view (world-bounds reject first, then the ray in mesh space
    /// against the cached triangles, Moller-Trumbore); skinned robots against sphere-swept bone capsules of their Humanoid rig
    /// (hips-chest, chest-head, shoulders, arms, legs); non-readable meshes fall back to their renderer bounds. The hit is kept
    /// relative to the target (Anchor), so a burst that arrives after the tracer's flight still sits on the moving body.
    /// Misses: the line continued to the walkable ground (terrain, bridge decks) or the water surface.
    /// </summary>
    public static class HitSurface
    {
        public static bool On = true;
        /// <summary>Counters for the IM1 gate.</summary>
        public static int Resolved, MeshHits, BoneHits, BoundsHits, Fallbacks, GroundMisses;

        sealed class Tris { public Vector3[] v; public int[] t; }
        static readonly Dictionary<Mesh, Tris> cache = new Dictionary<Mesh, Tris>();
        static readonly List<Renderer> rs = new List<Renderer>(32);

        /// <summary>The last resolved hit, relative to its target (claimed by OpenZHitSparks / OpenZFxPlayer for the delayed burst).</summary>
        public struct Anchor { public Transform t; public Vector3 local, localNormal, world, normal; public int frame; }
        static Anchor last;

        /// <summary>Where the line from `from` through `aim` meets the target's drawn body. False = no body (caller keeps `aim`).</summary>
        public static bool OnTarget(UnitView v, Vector3 from, Vector3 aim, out Vector3 point, out Vector3 normal)
        {
            point = aim; normal = Vector3.up;
            if (!On || !v || !v.isActiveAndEnabled) return false;
            return OnBody(v.transform, from, aim, out point, out normal);
        }

        public static bool OnBody(Transform root, Vector3 from, Vector3 aim, out Vector3 point, out Vector3 normal)
        {
            point = aim; normal = Vector3.up;
            if (!root) return false;
            var d = aim - from; float len = d.magnitude;
            if (len < 0.05f) return false;
            var ray = new Ray(from, d / len); float maxT = len + 6f;
            float best = float.MaxValue; Vector3 bn = Vector3.up; bool anyMesh = false; Bounds all = default; bool haveAll = false;
            var anim = root.GetComponentInChildren<Animator>();
            rs.Clear(); root.GetComponentsInChildren(false, rs);
            foreach (var r in rs)
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var m = r.sharedMaterial; if (!m || m.renderQueue >= 2450) continue;                      // glows, rings, decals, fx
                var b = r.bounds; if (b.size.sqrMagnitude < 1e-4f) continue;
                if (haveAll) all.Encapsulate(b); else { all = b; haveAll = true; }
                if (!b.IntersectRay(ray, out float bt) || bt > maxT) continue;
                if (r is SkinnedMeshRenderer) continue;                                                     // skinned: bones below
                var mf = r.GetComponent<MeshFilter>(); var mesh = mf ? mf.sharedMesh : null; if (!mesh) continue;
                var tri = Get(mesh); if (tri == null) continue;
                anyMesh = true;
                var tr = r.transform; var lo = tr.InverseTransformPoint(ray.origin); var ld = tr.InverseTransformDirection(ray.direction);
                if (RayMesh(tri, lo, ld, out float lt, out Vector3 ln))
                {
                    var wp = tr.TransformPoint(lo + ld * lt); float wt = Vector3.Dot(wp - ray.origin, ray.direction);
                    if (wt > 0f && wt < best && wt <= maxT) { best = wt; bn = tr.TransformDirection(ln).normalized; }
                }
            }
            bool human = anim && anim.isHuman && anim.GetBoneTransform(HumanBodyBones.Hips);
            if (human && BoneHit(anim, ray, maxT, ref best, ref bn)) BoneHits++;
            else if (human && best == float.MaxValue)
            {   // the line passed between the limbs: re-aim at the chest (a hit always lands on the body, never in the air beside it)
                var chest = anim.GetBoneTransform(HumanBodyBones.Chest) ?? anim.GetBoneTransform(HumanBodyBones.Spine) ?? anim.GetBoneTransform(HumanBodyBones.Hips);
                var d2 = chest.position - from; ray = new Ray(from, d2.normalized); maxT = d2.magnitude + 2f;
                if (BoneHit(anim, ray, maxT, ref best, ref bn)) BoneHits++;
            }
            else if (best < float.MaxValue) MeshHits++;
            if (best == float.MaxValue && haveAll && !anyMesh && !human && all.IntersectRay(ray, out float at) && at <= maxT)
            {   // non-readable meshes: their drawn bounds (still on the body's skin, not in the air)
                best = at; var p = ray.GetPoint(at); var c = all.center; var e = all.extents;
                var q = new Vector3((p.x - c.x) / Mathf.Max(e.x, 1e-3f), (p.y - c.y) / Mathf.Max(e.y, 1e-3f), (p.z - c.z) / Mathf.Max(e.z, 1e-3f));
                bn = Mathf.Abs(q.x) > Mathf.Abs(q.y) && Mathf.Abs(q.x) > Mathf.Abs(q.z) ? new Vector3(Mathf.Sign(q.x), 0, 0) : Mathf.Abs(q.y) > Mathf.Abs(q.z) ? new Vector3(0, Mathf.Sign(q.y), 0) : new Vector3(0, 0, Mathf.Sign(q.z));
                BoundsHits++;
            }
            if (best == float.MaxValue) { Fallbacks++; return false; }
            if (Vector3.Dot(bn, ray.direction) > 0f) bn = -bn;                                             // two-sided: face the shooter
            point = ray.GetPoint(best); normal = bn; Resolved++;
            last = new Anchor { t = root, local = root.InverseTransformPoint(point), localNormal = root.InverseTransformDirection(normal), world = point, normal = normal, frame = Time.frameCount };
            return true;
        }

        /// <summary>The anchor of the hit resolved at `world` this frame (the burst follows the target), if any.</summary>
        public static bool Claim(Vector3 world, out Anchor a)
        {
            a = last;
            if (!last.t || Time.frameCount - last.frame > 1 || (last.world - world).sqrMagnitude > 1e-6f) return false;
            last.t = null; return true;
        }

        /// <summary>Anchor -> world now (the target moved during the tracer's flight). False when the target is gone (keep the old point).</summary>
        public static bool Now(in Anchor a, out Vector3 p, out Vector3 n)
        {
            p = a.world; n = a.normal;
            if (!a.t || !a.t.gameObject.activeInHierarchy) return false;
            p = a.t.TransformPoint(a.local); n = a.t.TransformDirection(a.localNormal).normalized; return true;
        }

        /// <summary>A miss: the shot's line from `from` through `aim`, continued to where it meets the walkable ground or the water.</summary>
        /// <summary></summary>
        public static int NearBankMisses;
        public static Vector3 MissGround(Vector3 from, Vector3 aim)
        {
            var g = ZGame.I; var d = aim - from;
            if (!On || g == null || d.sqrMagnitude < 1e-4f) return aim;
            var dir = d.normalized;
            if (dir.y > -0.02f) dir = (dir + Vector3.down * 0.25f).normalized;                              // a flat shot drops into the ground behind
            if (!g.GroundPoint(new Ray(from, dir), out var gp)) return aim;
            // bank right in front of him; a miss lands past the aim point (the drawn round flies over the bank: ShotClearance.Bend)
            float reach = new Vector2(aim.x - from.x, aim.z - from.z).magnitude, hitAt = new Vector2(gp.x - from.x, gp.z - from.z).magnitude;
            if (ShotClearance.Enabled && reach > 2f && hitAt < reach * 0.8f)
            {
                var o = aim + Vector3.up * 0.05f;
                if (!g.GroundPoint(new Ray(o, dir.y < -0.05f ? dir : (dir + Vector3.down * 0.35f).normalized), out gp)) { gp = aim; gp.y = g.GroundY(aim.x, aim.z); }
                NearBankMisses++;
            }
            var fx = OpenZFxPlayer.I;
            if (fx)
            {   // on water the line stops at the water surface
                float sy = fx.SurfaceY(gp);
                if (sy > gp.y + 0.01f && dir.y < -1e-3f) { float t = (sy - from.y) / dir.y; if (t > 0f) gp = from + dir * t; }
            }
            GroundMisses++;
            return gp;
        }

        // ------------------------------------------------------------------ meshes
        static Tris Get(Mesh m)
        {
            if (cache.TryGetValue(m, out var t)) return t;
            if (!m.isReadable) { cache[m] = null; return null; }
            t = new Tris { v = m.vertices, t = m.triangles };
            cache[m] = t; return t;
        }

        static bool RayMesh(Tris m, Vector3 o, Vector3 d, out float bestT, out Vector3 bestN)
        {
            bestT = float.MaxValue; bestN = Vector3.up; var v = m.v; var t = m.t;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                var a = v[t[i]]; var e1 = v[t[i + 1]] - a; var e2 = v[t[i + 2]] - a;
                var p = Vector3.Cross(d, e2); float det = Vector3.Dot(e1, p);
                if (det > -1e-7f && det < 1e-7f) continue;
                float inv = 1f / det; var s = o - a; float u = Vector3.Dot(s, p) * inv; if (u < 0f || u > 1f) continue;
                var q = Vector3.Cross(s, e1); float w = Vector3.Dot(d, q) * inv; if (w < 0f || u + w > 1f) continue;
                float tt = Vector3.Dot(e2, q) * inv;
                if (tt > 1e-4f && tt < bestT) { bestT = tt; bestN = Vector3.Cross(e1, e2); }
            }
            return bestT < float.MaxValue;
        }

        // ------------------------------------------------------------------ robots (Humanoid bones, sphere-swept segments)
        static readonly (HumanBodyBones a, HumanBodyBones b, float r)[] Segs =
        {
            (HumanBodyBones.Hips, HumanBodyBones.Chest, 0.30f), (HumanBodyBones.Chest, HumanBodyBones.Head, 0.26f), (HumanBodyBones.Head, HumanBodyBones.Head, 0.2f),
            (HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, 0.2f),
            (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.11f), (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.1f),
            (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.11f), (HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.1f),
            (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.11f), (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.1f),
            (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.11f), (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.1f),
        };

        static bool BoneHit(Animator an, Ray ray, float maxT, ref float best, ref Vector3 bn)
        {
            var hips = an.GetBoneTransform(HumanBodyBones.Hips); if (!hips) return false;
            var root = an.transform; float k = Mathf.Abs(root.lossyScale.y);                               // radii for a 1.0-scale robot
            bool hit = false;
            foreach (var s in Segs)
            {
                var ta = an.GetBoneTransform(s.a); var tb = an.GetBoneTransform(s.b);
                if (s.a == HumanBodyBones.Chest && !ta) ta = an.GetBoneTransform(HumanBodyBones.Spine);
                if (s.b == HumanBodyBones.Chest && !tb) tb = an.GetBoneTransform(HumanBodyBones.Spine);
                if (!ta || !tb) continue;
                var pa = ta.position; var pb = tb.position; float r = s.r * k;
                if (s.a == s.b) pb = pa + root.up * 0.12f * k;
                if (RayCapsule(ray, pa, pb, r, out float t, out Vector3 n) && t < best && t <= maxT) { best = t; bn = n; hit = true; }
            }
            return hit;
        }

        /// <summary>Ray vs capsule (segment pa-pb, radius r): distance and outward normal.</summary>
        static bool RayCapsule(Ray ray, Vector3 pa, Vector3 pb, float r, out float t, out Vector3 n)
        {
            t = 0f; n = Vector3.up;
            // march the closest-approach: sample the segment finely enough for robot limbs (cheap: ~12 segments per shot)
            var ab = pb - pa; float bestT = float.MaxValue; Vector3 bestN = Vector3.up;
            for (int i = 0; i <= 4; i++)
            {
                var c = pa + ab * (i / 4f);
                var oc = ray.origin - c; float bq = Vector3.Dot(oc, ray.direction), cq = Vector3.Dot(oc, oc) - r * r;
                float disc = bq * bq - cq; if (disc < 0f) continue;
                float tt = -bq - Mathf.Sqrt(disc); if (tt < 0f) continue;
                if (tt < bestT) { bestT = tt; bestN = (ray.GetPoint(tt) - c).normalized; }
            }
            if (bestT == float.MaxValue) return false;
            t = bestT; n = bestN; return true;
        }
    }
}
