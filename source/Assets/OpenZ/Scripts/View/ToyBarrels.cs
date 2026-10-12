using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// (BarrelRecoil, mesh howitzer_recoil, 878 vertices) is rebuilt once per source mesh: thicker around its own bore axis
    /// (principal axis of the vertices), but only where it leaves the cradle: the cradle (howitzer_pitch) sleeves the first
    /// 0.69 m = 28 % of the 2.48 m barrel with a bore of the barrel's own radius (tools/toy_barrel_check.py), so the barrel stays
    /// as it is up to 29 % of its length, swells to the full factor by 38 %; and 12 % longer, measured from the breech. Node pivots stay; the Muzzle socket moves
    /// with the longer barrel so shells, flashes and UnitAim's aim still leave the real tip. View only, materials untouched.
    /// Called by UnitView.Bind before UnitAim measures the gun. PlayerPrefs oz_toy_barrels = 0 turns it off.
    /// </summary>
    public static class ToyBarrels
    {
        public const float Thick = 1.5f, Long = 1.12f, SleeveEnd = 0.29f, SwellEnd = 0.38f;
        static readonly Dictionary<Mesh, (Mesh mesh, Vector3 breech, Vector3 axis, float len)> cache = new Dictionary<Mesh, (Mesh, Vector3, Vector3, float)>();
        static int enabledState = -1;
        public static bool Enabled
        {
            get { if (enabledState < 0) enabledState = PlayerPrefs.GetInt("oz_toy_barrels", 1) != 0 ? 1 : 0; return enabledState == 1; }
            set => enabledState = value ? 1 : 0;
        }

        /// <summary>Returns true when a barrel was rebuilt on this unit.</summary>
        public static bool Apply(GameObject unit, string type)
        {
            if (!Enabled || !unit || type != "howitzer") return false;
            Transform barrel = null, muzzle = null;
            foreach (var t in unit.GetComponentsInChildren<Transform>(true))
            {
                if (!barrel && t.name == "BarrelRecoil") barrel = t;
                else if (!muzzle && t.name == "Muzzle") muzzle = t;
            }
            var mf = barrel ? barrel.GetComponent<MeshFilter>() : null;
            if (!mf || !mf.sharedMesh || mf.sharedMesh.name.EndsWith("_toy")) return false;
            if (mf.sharedMesh.name == "howitzer_barrel_v2") return false;                                // howitzer_towers_v1: the chunky barrel is already fat
            var src = mf.sharedMesh;
            if (!cache.TryGetValue(src, out var e))
            {
                if (!src.isReadable) return false;
                e = Build(src);
                cache[src] = e;
            }
            mf.sharedMesh = e.mesh;
            if (muzzle && muzzle.parent == barrel)
            {   // the tip moves out with the longer barrel (same stretch as the vertices, from the breech)
                var p = muzzle.localPosition; float s = Vector3.Dot(p - e.breech, e.axis);
                muzzle.localPosition = p + e.axis * (s * (Long - 1f));
            }
            return true;
        }

        static (Mesh, Vector3, Vector3, float) Build(Mesh src)
        {
            var v = src.vertices; int n = v.Length;
            var c = Vector3.zero; foreach (var p in v) c += p; c /= Mathf.Max(1, n);
            // principal axis (power iteration on the covariance)
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p in v) { var d = p - c; xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z; yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z; }
            var a = new Vector3(1f, 1f, 0.1f).normalized;
            for (int i = 0; i < 32; i++)
            {
                var b = new Vector3(xx * a.x + xy * a.y + xz * a.z, xy * a.x + yy * a.y + yz * a.z, xz * a.x + yz * a.y + zz * a.z);
                if (b.sqrMagnitude < 1e-12f) break;
                a = b.normalized;
            }
            // the breech is the end nearer the node origin (the pitch trunnion); the axis points from the breech to the muzzle
            float tMin = float.MaxValue, tMax = float.MinValue;
            foreach (var p in v) { float t = Vector3.Dot(p - c, a); tMin = Mathf.Min(tMin, t); tMax = Mathf.Max(tMax, t); }
            Vector3 endA = c + a * tMin, endB = c + a * tMax;
            if (endB.sqrMagnitude < endA.sqrMagnitude) { a = -a; var sw = tMin; tMin = -tMax; tMax = -sw; }
            var breech = c + a * tMin; float len = Mathf.Max(1e-4f, tMax - tMin);
            var o = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var d = v[i] - breech; float s = Vector3.Dot(d, a); var radial = d - a * s;
                float k = Mathf.Lerp(1f, Thick, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SleeveEnd * len, SwellEnd * len, s)));
                o[i] = breech + a * (s * Long) + radial * k;
            }
            var m = Object.Instantiate(src);
            m.name = src.name + "_toy";
            m.vertices = o;
            m.RecalculateBounds();                                                                         // normals: the same directions (radial scale keeps the round shading)
            {   // howitzer_towers_v1: TeamPaint's accent band is a fraction of mesh.bounds height; the recalculated bounds put it across the
                // middle of the slanted barrel at the next re-paint (capture, char). Keep the source's y range (the split's whole-model
                // bounds: the same band as every other part); x / z cover the fat barrel
                var nb = m.bounds; var sb = src.bounds;
                var mn = new Vector3(Mathf.Min(nb.min.x, sb.min.x), sb.min.y, Mathf.Min(nb.min.z, sb.min.z));
                var mx = new Vector3(Mathf.Max(nb.max.x, sb.max.x), sb.max.y, Mathf.Max(nb.max.z, sb.max.z));
                m.bounds = new Bounds((mn + mx) * 0.5f, mx - mn);
            }
            return (m, breech, a, len);
        }
    }
}
