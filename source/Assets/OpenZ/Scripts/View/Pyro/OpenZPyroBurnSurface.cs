using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The burn-surface sampler of our earlier projects caches 96 real mesh triangles per model and anchors each lick
    /// to the bone with the largest skin weight. OpenZ's unit meshes are imported without Read/Write (their vertices do not exist
    /// on the CPU in a player build), so the same idea is done without reading vertices:
    /// <list type="bullet">
    /// <item>robots (skinned): the body bones (hips, spine, neck, head, arms, legs; fingers / toes / ends skipped, the trunk counted
    /// twice) with a point on a small sphere around the bone, pushed out from the body's axis = an outward face normal; the lick
    /// rides that bone;</item>
    /// <item>vehicles, cannons, buildings, props (rigid): the model's box in its own space (union of its renderers), a point on a
    /// face that is not the bottom; buildings keep up to four burn spots where the stream really hit (the lick stays within
    /// 1.2 m of one, on that face) so a big building burns where it was hit, not all over; the lick rides the root.</item>
    /// </list>
    /// <para>critic round 2, things the stream touches: level decor meshes ARE readable (L02 / L17_environment.fbx isReadable 1, not
    /// meshes weighted by their box surface area, a triangle centroid on a face the camera sees. A small prop (half size &lt;= 1.6 m)
    /// </para>
    /// </summary>
    public sealed class OpenZPyroBurnSurface
    {
        public Transform Root { get; private set; }
        public bool Skinned { get; private set; }
        public Vector3 CenterLocal { get; private set; }
        public float Size { get; private set; }                  // largest box extent (m, world)
        readonly List<Transform> bones = new List<Transform>();
        Transform hips;
        Bounds box;                                              // root-local
        struct Spot { public Vector3 p, n; public float until; }
        readonly List<Spot> spots = new List<Spot>(4);
        public bool Structure { get; private set; }
        /// <summary></summary>
        public bool Whole { get; private set; }
        struct Tri { public Transform t; public Vector3 a, b, c; }                                   // mesh-local corners
        readonly List<Tri> tris = new List<Tri>();
        public int TriCount => tris.Count;

        static readonly string[] BodyKeys = { "hips", "spine", "neck", "head", "shoulder", "arm", "upleg", "leg", "pelvis", "chest", "torso" };
        static readonly string[] SkipKeys = { "hand", "finger", "thumb", "index", "middle", "ring", "pinky", "toe", "end", "eye", "jaw", "twist", "weapon", "muzzle", "grip", "ik" };

        public static OpenZPyroBurnSurface Build(GameObject go, bool structure, bool meshTriangles = false)
        {
            if (!go) return null;
            var s = new OpenZPyroBurnSurface { Root = go.transform, Structure = structure };
            // (BS01_Type_*.fbx: KitBone_Head / Hips / UpperChest / legs = 5 bones) are skinned too and could come first in the
            // hierarchy, which would put every lick on 5 kit bones (no arms, a kit-sized body)
            SkinnedMeshRenderer smr = null; int most = 4;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!r || r.bones == null) continue;
                int nb = r.bones.Length;
                if (nb > most) { most = nb; smr = r; }
            }
            if (smr && !structure)
            {
                s.Skinned = true;
                var seen = new HashSet<Transform>();
                foreach (var b in smr.bones)
                {
                    if (!b || !seen.Add(b)) continue;
                    string n = b.name.ToLowerInvariant();
                    bool body = false; foreach (var k in BodyKeys) if (n.Contains(k)) { body = true; break; }
                    if (!body) continue;
                    bool skip = false; foreach (var k in SkipKeys) if (n.Contains(k)) { skip = true; break; }
                    if (skip) continue;
                    s.bones.Add(b);
                    if (n.Contains("spine") || n.Contains("hips") || n.Contains("chest")) s.bones.Add(b);     // the trunk carries most of the fire
                    if (!s.hips && n.Contains("hips")) s.hips = b;
                }
                var wb = smr.bounds; s.Size = Mathf.Max(wb.extents.x, Mathf.Max(wb.extents.y, wb.extents.z));
                s.CenterLocal = go.transform.InverseTransformPoint(wb.center);
                if (s.bones.Count < 3) s.Skinned = false;                                                    // odd rig: use its box
            }
            if (!s.Skinned)
            {
                bool any = false, loose = false; var b = new Bounds();
                var inv = go.transform.worldToLocalMatrix;
                var rs = go.GetComponentsInChildren<Renderer>(true);
                bool filter = false;
                foreach (var r in rs) if (r && r.enabled && r.gameObject.activeInHierarchy && !NotBody(r) && (r is MeshRenderer || r is SkinnedMeshRenderer)) { filter = true; break; }
                foreach (var r in rs)
                {
                    if (!r || !r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || r is SpriteRenderer || r is BillboardRenderer) continue;
                    if (filter && NotBody(r)) continue;
                    // 45 degrees is ~1.4x too wide, and re-boxing its corners in the root's space doubled that again: licks floated up
                    // to a metre off the side of a vehicle that was not axis-aligned. Skinned parts keep the world AABB (their local
                    // bounds are in root-bone space).
                    Bounds lb; Matrix4x4 m;
                    // critic round 2: a statically batched renderer (level decor) draws a combined world-space mesh; its local
                    // bounds are not trustworthy there, its world AABB always is
                    if (r is SkinnedMeshRenderer || r.isPartOfStaticBatch) { lb = r.bounds; m = inv; loose = true; }
                    else { lb = r.localBounds; m = inv * r.localToWorldMatrix; }
                    for (int i = 0; i < 8; i++)
                    {
                        var c = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var lc = m.MultiplyPoint3x4(c);
                        if (!any) { b = new Bounds(lc, Vector3.zero); any = true; } else b.Encapsulate(lc);
                    }
                }
                if (!any) return null;
                // a box is still a box around a rounded hull: pull it in a little so licks sit on the hull, not in the air
                // (more when a skinned part forced a world AABB into it)
                b.extents *= loose ? 0.9f : 0.96f;
                s.box = b; s.CenterLocal = b.center;
                var ws = Vector3.Scale(b.extents, go.transform.lossyScale);
                s.Size = Mathf.Max(Mathf.Abs(ws.x), Mathf.Max(Mathf.Abs(ws.y), Mathf.Abs(ws.z)));
                if (structure && meshTriangles) { s.CacheTris(go); s.Whole = s.tris.Count > 0 && s.Size <= 1.6f; }
            }
            return s;
        }

        /// <summary>prepare(): 96 real triangles; each sample picks a mesh by its share of the summed box
        /// surface areas, then face floor(frac(i x .618) x faces). Only readable, non-static-batched, non-helper meshes (decor).</summary>
        void CacheTris(GameObject go)
        {
            var meshes = new List<MeshFilter>(); var areas = new List<float>(); float sum = 0f;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(false))
            {
                var m = mf.sharedMesh; var r = mf.GetComponent<Renderer>();
                if (!m || !m.isReadable || !r || !r.enabled || r.isPartOfStaticBatch || NotBody(r) || m.vertexCount > 60000) continue;
                var sc = Vector3.Scale(m.bounds.size, mf.transform.lossyScale); sc = new Vector3(Mathf.Abs(sc.x), Mathf.Abs(sc.y), Mathf.Abs(sc.z));
                float area = sc.x * sc.y + sc.y * sc.z + sc.z * sc.x; if (area <= 0.001f) continue;
                meshes.Add(mf); areas.Add(area); sum += area;
            }
            if (sum <= 0f) return;
            var idxOf = new Dictionary<Mesh, int[]>(); var vOf = new Dictionary<Mesh, Vector3[]>();
            for (int i = 0; i < 96; i++)
            {
                float w = (i + 0.5f) / 96f * sum; int e = meshes.Count - 1;
                for (int k = 0; k < meshes.Count; k++) { w -= areas[k]; if (w <= 0f) { e = k; break; } }
                var mesh = meshes[e].sharedMesh;
                if (!idxOf.TryGetValue(mesh, out var idx)) { idx = mesh.triangles; idxOf[mesh] = idx; vOf[mesh] = mesh.vertices; }
                var v = vOf[mesh]; int count = idx.Length / 3; if (count == 0) continue;
                int face = Mathf.FloorToInt(Mathf.Repeat(i * 0.61803398875f, 1f) * count) * 3;
                tris.Add(new Tri { t = meshes[e].transform, a = v[idx[face]], b = v[idx[face + 1]], c = v[idx[face + 2]] });
            }
        }

        /// <summary>sample(): 12 tries from a random start in steps of 37, a face the camera sees, above the
        /// ground; a big prop only near a live touched spot (1.5 m).</summary>
        bool SampleTris(Vector3 camPos, System.Func<float, float, float> ground, float now, out Vector3 point, out Vector3 normal, out Transform anchor)
        {
            point = normal = Vector3.zero; anchor = null;
            int start = Random.Range(0, tris.Count), live = Whole ? 0 : SpotCount(now);
            if (!Whole && live == 0) return false;
            for (int k = 0; k < 12; k++)
            {
                var tr = tris[(start + k * 37) % tris.Count]; if (!tr.t || !tr.t.gameObject.activeInHierarchy) continue;
                Vector3 a = tr.t.TransformPoint(tr.a), b = tr.t.TransformPoint(tr.b), c = tr.t.TransformPoint(tr.c);
                var n = Vector3.Cross(b - a, c - a); if (n.sqrMagnitude < 1e-10f) continue; n.Normalize();
                var p = (a + b + c) / 3f;
                if (!Whole)
                {
                    bool near = false;
                    foreach (var s in spots) if (s.until > now && (Root.TransformPoint(s.p) - p).sqrMagnitude < 2.25f) { near = true; break; }
                    if (!near) continue;
                }
                if (Vector3.Dot(n, (camPos - p).normalized) < 0.08f || p.y < ground(p.x, p.z) + 0.06f) continue;
                point = p + n * 0.09f; normal = n; anchor = tr.t;
                return true;
            }
            return false;
        }

        public Vector3 CenterWorld => Root ? Root.TransformPoint(CenterLocal) : Vector3.zero;

        static readonly string[] NotBodyKeys = { "muzzle", "flash", "flame", "spark", "halo", "glow", "beam", "shadow", "select", "bracket", "helper", "tracer", "shield", "zap" };
        /// <summary>skip muzzle / flame / spark / helper meshes and parts with only transparent materials
        /// (OpenZ adds halos, light cones, shadow blobs, selection rings: none of them is the hull the fire sits on).</summary>
        internal static bool NotBody(Renderer r)
        {
            string n = r.name.ToLowerInvariant();
            foreach (var k in NotBodyKeys) if (n.Contains(k)) return true;
            var mats = r.sharedMaterials; bool opaque = false;
            foreach (var m in mats) if (m && m.renderQueue < 2450) { opaque = true; break; }
            return !opaque;
        }

        /// <summary>A building / prop hit at world point `w`: remember the face spot (max 4, oldest replaced; a hit within 1.2 m of a
        /// spot refreshes it). Only for rigid boxes.</summary>
        public void AddSpot(Vector3 w, float until)
        {
            if (Skinned || !Root) return;
            var l = Root.InverseTransformPoint(w);
            ClosestFace(l, out var p, out var n);
            for (int i = 0; i < spots.Count; i++)
                if ((spots[i].p - p).magnitude < 1.2f / Mathf.Max(0.2f, Root.lossyScale.x)) { spots[i] = new Spot { p = spots[i].p, n = spots[i].n, until = until }; return; }
            if (spots.Count >= 4) { int o = 0; for (int i = 1; i < spots.Count; i++) if (spots[i].until < spots[o].until) o = i; spots.RemoveAt(o); }
            spots.Add(new Spot { p = p, n = n, until = until });
        }

        public int SpotCount(float now) { int n = 0; foreach (var s in spots) if (s.until > now) n++; return n; }

        /// <summary>The freshest live burn spot of a building / prop (world point on its face and the face's outward normal): its
        /// light, smoke and sound sit there, not in the middle of the building.</summary>
        public bool LatestSpot(float now, out Vector3 world, out Vector3 normal)
        {
            world = normal = Vector3.zero;
            if (Skinned || !Root) return false;
            int best = -1;
            for (int i = 0; i < spots.Count; i++) if (spots[i].until > now && (best < 0 || spots[i].until > spots[best].until)) best = i;
            if (best < 0) return false;
            world = Root.TransformPoint(spots[best].p); normal = Root.TransformDirection(spots[best].n).normalized;
            return true;
        }

        static readonly Vector3[] Faces = { Vector3.right, Vector3.left, Vector3.up, Vector3.forward, Vector3.back };   // no bottom
        /// <summary>A camera-facing box face next to a spot on a hidden face: the spot moved onto that face (its face coordinate).</summary>
        bool WrapFace(Vector3 viewL, ref Vector3 lp, ref Vector3 ln)
        {
            int start = Random.Range(0, Faces.Length);
            for (int k = 0; k < Faces.Length; k++)
            {
                var f = Faces[(start + k) % Faces.Length];
                if (f == ln || Vector3.Dot(f, viewL) < 0.08f) continue;
                if (f.x > 0.5f) lp.x = box.max.x; else if (f.x < -0.5f) lp.x = box.min.x;
                else if (f.y > 0.5f) lp.y = box.max.y;
                else if (f.z > 0.5f) lp.z = box.max.z; else lp.z = box.min.z;
                ln = f; return true;
            }
            return false;
        }

        void ClosestFace(Vector3 l, out Vector3 p, out Vector3 n)
        {
            var c = box.center; var e = box.extents; var d = l - c;
            float fx = e.x - Mathf.Abs(d.x), fy = e.y - Mathf.Abs(d.y), fz = e.z - Mathf.Abs(d.z);
            p = new Vector3(Mathf.Clamp(l.x, box.min.x, box.max.x), Mathf.Clamp(l.y, box.min.y, box.max.y), Mathf.Clamp(l.z, box.min.z, box.max.z));
            if (d.y > 0 && fy <= fx && fy <= fz) { n = Vector3.up; p.y = box.max.y; }
            else if (fx <= fz) { n = new Vector3(Mathf.Sign(d.x), 0, 0); p.x = d.x > 0 ? box.max.x : box.min.x; }
            else { n = new Vector3(0, 0, Mathf.Sign(d.z)); p.z = d.z > 0 ? box.max.z : box.min.z; }
        }

        /// <summary>sample(model, camera, ground): a point on the burning surface facing the viewer, its outward normal and the
        /// transform the lick rides. False after 12 tries (fully turned away / under ground).</summary>
        public bool Sample(Vector3 camPos, System.Func<float, float, float> ground, float now, out Vector3 point, out Vector3 normal, out Transform anchor)
        {
            point = normal = Vector3.zero; anchor = null;
            if (!Root || !Root.gameObject.activeInHierarchy) return false;
            if (tris.Count > 0)
            {
                if (SampleTris(camPos, ground, now, out point, out normal, out anchor)) return true;
                if (Whole) return false;
            }
            for (int k = 0; k < 12; k++)
            {
                if (Skinned)
                {
                    var b = bones[Random.Range(0, bones.Count)]; if (!b) continue;
                    var axisP = hips ? hips.position : Root.TransformPoint(CenterLocal);
                    var bp = b.position;
                    var outward = bp - new Vector3(axisP.x, bp.y, axisP.z);                                // away from the body's vertical axis
                    var dir = (outward.sqrMagnitude > 1e-4f ? outward.normalized * 0.7f : Vector3.zero) + Random.onUnitSphere;
                    if (dir.sqrMagnitude < 1e-4f) continue;
                    dir.Normalize();
                    point = bp + dir * (Size * 0.2f);
                    normal = dir; anchor = b;
                }
                else
                {
                    Vector3 lp = Vector3.zero, ln = Vector3.up;
                    int live = SpotCount(now);
                    if (Structure && live > 0)
                    {
                        int pick = Random.Range(0, live), q = -1; bool found = false;
                        foreach (var s in spots) { if (s.until <= now) continue; if (++q == pick) { lp = s.p; ln = s.n; found = true; break; } }
                        if (!found) continue;
                        // critic round 2 (seen in the touch previews): the stream usually touches the face turned TO the Pyro, which is
                        // often turned AWAY from the camera, and every lick there failed the view test: the thing burned invisibly.
                        // only ever sit on faces the viewer sees)
                        var viewL = Root.InverseTransformDirection(camPos - Root.TransformPoint(lp));
                        if (viewL.sqrMagnitude > 1e-6f && Vector3.Dot(ln, viewL.normalized) < 0.08f && !WrapFace(viewL.normalized, ref lp, ref ln)) continue;
                        var t1 = Vector3.Cross(ln, Mathf.Abs(ln.y) > 0.9f ? Vector3.right : Vector3.up).normalized; var t2 = Vector3.Cross(ln, t1);
                        float jr = 1.2f / Mathf.Max(0.2f, Root.lossyScale.x);
                        var q2 = Random.insideUnitCircle * jr;
                        lp = lp + t1 * q2.x + t2 * q2.y;
                        lp = new Vector3(Mathf.Clamp(lp.x, box.min.x, box.max.x), Mathf.Clamp(lp.y, box.min.y, box.max.y), Mathf.Clamp(lp.z, box.min.z, box.max.z));
                    }
                    else
                    {   // a face by area (bottom excluded), a point on it
                        var e = box.extents;
                        float ax = e.y * e.z, ay = e.x * e.z, az = e.x * e.y, tot = 2f * ax + ay + 2f * az, u = Random.value * tot;
                        var c = box.center;
                        var r2 = new Vector2(Random.value * 2f - 1f, Random.value * 2f - 1f);
                        if (u < ax) { ln = Vector3.right; lp = c + new Vector3(e.x, r2.x * e.y, r2.y * e.z); }
                        else if (u < 2f * ax) { ln = Vector3.left; lp = c + new Vector3(-e.x, r2.x * e.y, r2.y * e.z); }
                        else if (u < 2f * ax + ay) { ln = Vector3.up; lp = c + new Vector3(r2.x * e.x, e.y, r2.y * e.z); }
                        else if (u < 2f * ax + ay + az) { ln = Vector3.forward; lp = c + new Vector3(r2.x * e.x, r2.y * e.y, e.z); }
                        else { ln = Vector3.back; lp = c + new Vector3(r2.x * e.x, r2.y * e.y, -e.z); }
                    }
                    point = Root.TransformPoint(lp); normal = Root.TransformDirection(ln).normalized; anchor = Root;
                }
                var view = (camPos - point).normalized;
                if (Vector3.Dot(normal, view) < 0.08f) continue;
                if (point.y < ground(point.x, point.z) + 0.06f) continue;
                point += normal * 0.09f;
                return true;
            }
            return false;
        }
    }
}
