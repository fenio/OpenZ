using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// Material / MaterialPropertyBlock.SetColor treat the colour as sRGB and linearise it channel by channel, so a linear HDR glow
    /// such as 2.4 x (0.06, 0.45, 1) reached the shader as (0.02, 1.19, 7.6): royal blue, blown out, and a small stun tint became
    /// invisible (the live NukeFx "pale white ball", NightV2 lamp and HarbourDetails lessons). Every electro colour is written with
    /// SetVector in linear values, read back and restored the same way.
    /// </summary>
    public static class ElectroColour
    {
        public static bool LinearSpace => QualitySettings.activeColorSpace == ColorSpace.Linear;

        /// <summary>A colour given in LINEAR values, as the shader must receive it (for SetVector). Gamma projects get it encoded.</summary>
        public static Vector4 Shader(Color linear) => LinearSpace ? (Vector4)linear : (Vector4)linear.gamma;

        /// <summary>What the shader receives for a material's own colour property (the authored sRGB colour, linearised in a linear
        /// project), so SetVector(OfMaterial(m, id)) restores exactly what the material shows without us.</summary>
        public static Vector4 OfMaterial(Material m, int id)
        {
            if (!m || !m.HasProperty(id)) return new Vector4(0f, 0f, 0f, 1f);
            var c = m.GetColor(id);
            return LinearSpace ? (Vector4)c.linear : (Vector4)c;
        }
    }

    /// <summary>
    /// electrobot_v1: the shapes of every electric arc (tesla bolts, forks, ground strikes, stun arcs, coil / emitter crackle).
    /// Jagged lines by midpoint displacement: a straight line from A to B, each segment's midpoint pushed sideways by a random
    /// offset that halves (x rough) at every level, so 4 levels = 16 segments with the big kinks of a real discharge and fine
    /// crackle on top. The offsets are kept in the line's own frame (along t, sideways u / v in units of the line's length), so a
    /// shape stays the same while its end points move (the gun recoils, the target shakes) and changes only when it is
    /// re-randomised (every ~33 ms, like a strike's flicker). Deterministic per seed (view-side random only, never the sim's).
    /// </summary>
    public static class ElectroBoltShape
    {
        /// <summary>xorshift32: view-side random that never touches UnityEngine.Random or the sim's Rng.</summary>
        public struct Rng
        {
            uint s;
            public Rng(uint seed) { s = seed == 0u ? 0x9E3779B9u : seed; Next(); }
            public float Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / 16777216f; }
            public float Range(float a, float b) => a + (b - a) * Next();
            public int Int(int n) => Mathf.Min(n - 1, (int)(Next() * n));
            public Vector2 Disc() { float a = Next() * 6.2831853f, r = Mathf.Sqrt(Next()); return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r); }
        }

        public static uint Hash(int a, int b, int c)
        {
            unchecked
            {
                uint x = (uint)a * 0x9E3779B1u ^ ((uint)b * 0x85EBCA77u + 0x27d4eb2fu) ^ ((uint)c * 0xC2B2AE3Du);
                x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
                return x;
            }
        }

        /// <summary>Midpoint displacement into t[] (0..1) / off[] (sideways, in line lengths): 2^levels + 1 points. amp = the first
        /// level's displacement (share of the length), rough = the factor per level (0.5 = fractal, higher = crackly).</summary>
        public static int Jag(ref Rng r, int levels, float amp, float rough, float[] t, Vector2[] off)
        {
            int n = (1 << levels) + 1;
            t[0] = 0f; off[0] = Vector2.zero; t[n - 1] = 1f; off[n - 1] = Vector2.zero;
            float a = amp;
            for (int step = n - 1; step > 1; step >>= 1, a *= rough)
                for (int i = 0; i + step < n; i += step)
                {
                    int m = i + step / 2;
                    t[m] = (t[i] + t[i + step]) * 0.5f + (r.Next() - 0.5f) * 0.18f * (t[i + step] - t[i]);   // uneven segments
                    off[m] = (off[i] + off[i + step]) * 0.5f + r.Disc() * a;
                }
            return n;
        }

        /// <summary>Two unit vectors perpendicular to dir (and to each other).</summary>
        public static void Frame(Vector3 dir, out Vector3 u, out Vector3 v)
        {
            var d = dir.sqrMagnitude > 1e-10f ? dir.normalized : Vector3.forward;
            var up = Mathf.Abs(d.y) < 0.95f ? Vector3.up : Vector3.right;
            u = Vector3.Cross(d, up).normalized; v = Vector3.Cross(u, d);
        }

        /// <summary>World points of a shape between a and b. helix = an extra corkscrew (m) of `turns` turns, 0 at both ends (the twin
        /// bolts twist around each other); bulge = a push along `bulgeDir` of bulge x length x sin(pi t) (arcs that curve over a body).</summary>
        public static int Place(Vector3 a, Vector3 b, float[] t, Vector2[] off, int n, Vector3[] outPts,
                                float helix = 0f, float turns = 0f, float phase = 0f, float bulge = 0f, Vector3 bulgeDir = default)
        {
            var d = b - a; float len = d.magnitude;
            Frame(d, out var u, out var v);
            for (int i = 0; i < n; i++)
            {
                float ti = t[i], s = Mathf.Sin(ti * Mathf.PI);
                var p = a + d * ti + (u * off[i].x + v * off[i].y) * len;
                if (helix > 0f) { float w = phase + ti * turns * 6.2831853f; p += (u * Mathf.Cos(w) + v * Mathf.Sin(w)) * helix * s; }
                if (bulge != 0f) p += bulgeDir * (bulge * len * s);
                outPts[i] = p;
            }
            return n;
        }
    }

    /// <summary>
    /// electrobot_v1: draws every electric element of a frame as ONE dynamic mesh with OpenZ/ElectroArc (one draw call): camera-facing
    /// ribbons (arcs), camera-facing round glows (charge ball, flashes, halos) and ground-conforming ring bands (shock ring, the stun
    /// timer). Callers push during LateUpdate (ElectrobotView at 96, OpenZElectroFx at 97); OpenZElectroFx.LateUpdate uploads and
    /// renders, then clears. Widths have a screen floor (minPx at the ribbon's distance) so a bolt still reads at the full overview.
    /// </summary>
    public sealed class ElectroArcs
    {
        readonly List<Vector3> pos = new List<Vector3>(8192);
        readonly List<Color> col = new List<Color>(8192);
        readonly List<Vector2> uv0 = new List<Vector2>(8192);
        readonly List<Vector4> uv1 = new List<Vector4>(8192), uv2 = new List<Vector4>(8192);
        readonly List<int> idx = new List<int>(16384);
        readonly Mesh mesh;
        readonly Material mat;
        public int MaxVerts = 16000;
        public int Ribbons { get; private set; }
        public int Glows { get; private set; }
        public int Verts => pos.Count;
        public int PeakVerts { get; private set; }
        public int Dropped { get; private set; }
        Camera cam; Vector3 camPos; float pxPerRad = 900f;
        Bounds bounds; bool any;

        public ElectroArcs(Material m)
        {
            mat = m;
            mesh = new Mesh { name = "OZ_ElectroArcs", indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
        }

        public void Dispose() { if (mesh) Object.Destroy(mesh); }

        Camera Cam()
        {
            if (!cam) cam = Camera.main;
            if (cam)
            {
                camPos = cam.transform.position;
                pxPerRad = cam.pixelHeight / Mathf.Max(0.05f, 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            }
            return cam;
        }

        float Floor(Vector3 p, float minPx) => minPx <= 0f ? 0f : minPx * Vector3.Distance(camPos, p) / pxPerRad;

        void Grow(Vector3 p) { if (!any) { bounds = new Bounds(p, Vector3.one); any = true; } else bounds.Encapsulate(p); }

        /// <summary>A camera-facing strip through pts[0..n-1]. width = full glow width (m), coreFrac = core share of it, gains = HDR,
        /// strength 0..1, taper = width at the two ends (share). mode 0 = an arc (core + glow + beads), 4 = a soft aura (a Gaussian
        /// across, few beads: the wide blue halo of the main bolts). Colours are linear (vertex colours reach the shader unchanged).</summary>
        public void Ribbon(Vector3[] pts, int n, float width, float coreFrac, float coreGain, float glowGain, Color glow, float strength,
                           float seed, float startW = 0.45f, float endW = 0.55f, float minPx = 3f, float mode = 0f)
        {
            if (n < 2 || strength <= 0.002f || !Cam()) return;
            if (pos.Count + n * 2 > MaxVerts) { Dropped++; return; }
            int b0 = pos.Count;
            for (int i = 0; i < n; i++)
            {
                var p = pts[i];
                var tan = (i == 0 ? pts[1] - pts[0] : i == n - 1 ? pts[n - 1] - pts[n - 2] : pts[i + 1] - pts[i - 1]);
                var view = camPos - p;
                var side = Vector3.Cross(tan, view);
                float sl = side.magnitude; side = sl > 1e-6f ? side / sl : Vector3.up;
                float t = i / (float)(n - 1);
                float taper = t < 0.15f ? Mathf.Lerp(startW, 1f, t / 0.15f) : t > 0.85f ? Mathf.Lerp(1f, endW, (t - 0.85f) / 0.15f) : 1f;
                float w = Mathf.Max(width * taper, Floor(p, minPx)) * 0.5f;
                var c = new Color(glow.r, glow.g, glow.b, strength);
                var d1 = new Vector4(coreFrac, coreGain, glowGain, seed); var d2 = new Vector4(mode, 0f, 0f, 0f);
                pos.Add(p - side * w); col.Add(c); uv0.Add(new Vector2(t, -1f)); uv1.Add(d1); uv2.Add(d2);
                pos.Add(p + side * w); col.Add(c); uv0.Add(new Vector2(t, 1f)); uv1.Add(d1); uv2.Add(d2);
                Grow(p);
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a = b0 + i * 2;
                idx.Add(a); idx.Add(a + 1); idx.Add(a + 3); idx.Add(a); idx.Add(a + 3); idx.Add(a + 2);
            }
            Ribbons++;
        }

        /// <summary>A camera-facing round glow of diameter size (m).</summary>
        public void Glow(Vector3 at, float size, float coreFrac, float coreGain, float glowGain, Color glow, float strength, float minPx = 4f)
        {
            if (strength <= 0.002f || size <= 0.001f || !Cam()) return;
            if (pos.Count + 4 > MaxVerts) { Dropped++; return; }
            var ct = cam.transform;
            float h = Mathf.Max(size, Floor(at, minPx)) * 0.5f;
            // pulled toward the camera by most of its radius: a glow centred ON a hull / body point is not cut in half by that body
            var toCam = camPos - at; float dc = toCam.magnitude;
            if (dc > 1e-3f) at += toCam / dc * Mathf.Min(h * 0.8f, dc * 0.5f);
            var r = ct.right * h; var u = ct.up * h;
            int b0 = pos.Count;
            var c = new Color(glow.r, glow.g, glow.b, strength); var d1 = new Vector4(coreFrac, coreGain, glowGain, 0f); var d2 = new Vector4(1f, 0f, 0f, 0f);
            pos.Add(at - r - u); uv0.Add(new Vector2(-1f, -1f));
            pos.Add(at + r - u); uv0.Add(new Vector2(1f, -1f));
            pos.Add(at + r + u); uv0.Add(new Vector2(1f, 1f));
            pos.Add(at - r + u); uv0.Add(new Vector2(-1f, 1f));
            for (int k = 0; k < 4; k++) { col.Add(c); uv1.Add(d1); uv2.Add(d2); }
            idx.Add(b0); idx.Add(b0 + 1); idx.Add(b0 + 2); idx.Add(b0); idx.Add(b0 + 2); idx.Add(b0 + 3);
            Grow(at);
            Glows++;
        }

        /// <summary>A flat ring band lying on the ground around c (radius r, band width w), `fill` = share drawn (the stun timer drains
        /// clockwise from 12 o'clock as seen from above), conforming to ground(x, z) + lift. mode 2 = a ring band, 5 = a filled disc
        /// (r = w / 2: from the centre out; brightest at the centre: the blue ground wash).</summary>
        public void Ring(Vector3 c, float r, float w, float coreFrac, float coreGain, float glowGain, Color glow, float strength, float fill,
                         System.Func<float, float, float> ground, float lift = 0.06f, int seg = 48, float mode = 2f)
        {
            if (strength <= 0.002f || r <= 0.01f || !Cam()) return;
            if (pos.Count + (seg + 1) * 2 > MaxVerts) { Dropped++; return; }
            int b0 = pos.Count;
            var cc = new Color(glow.r, glow.g, glow.b, strength);
            var d1 = new Vector4(coreFrac, coreGain, glowGain, Mathf.Clamp01(fill)); var d2 = new Vector4(mode, 0f, 0f, 0f);
            float ri = Mathf.Max(0.01f, r - w * 0.5f), ro = r + w * 0.5f;
            for (int i = 0; i <= seg; i++)
            {
                float t = i / (float)seg, a = Mathf.PI * 0.5f - t * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                var pi = new Vector3(c.x + ca * ri, 0f, c.z + sa * ri); var po = new Vector3(c.x + ca * ro, 0f, c.z + sa * ro);
                pi.y = (ground != null ? ground(pi.x, pi.z) : c.y) + lift; po.y = (ground != null ? ground(po.x, po.z) : c.y) + lift;
                pos.Add(pi); col.Add(cc); uv0.Add(new Vector2(t, -1f)); uv1.Add(d1); uv2.Add(d2);
                pos.Add(po); col.Add(cc); uv0.Add(new Vector2(t, 1f)); uv1.Add(d1); uv2.Add(d2);
                if (i == 0 || i == seg / 2) Grow(po);
            }
            for (int i = 0; i < seg; i++)
            {
                int a = b0 + i * 2;
                idx.Add(a); idx.Add(a + 1); idx.Add(a + 3); idx.Add(a); idx.Add(a + 3); idx.Add(a + 2);
            }
            Ribbons++;
        }

        /// <summary>Uploads and draws what was pushed this frame, then clears.</summary>
        public void Render()
        {
            PeakVerts = Mathf.Max(PeakVerts, pos.Count);
            if (pos.Count > 0 && mat && any)
            {
                mesh.Clear();
                mesh.SetVertices(pos); mesh.SetColors(col); mesh.SetUVs(0, uv0); mesh.SetUVs(1, uv1); mesh.SetUVs(2, uv2);
                mesh.SetTriangles(idx, 0, false);
                bounds.Expand(2f);
                mesh.bounds = bounds;
                Graphics.RenderMesh(new RenderParams(mat) { layer = 0, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = bounds },
                                    mesh, 0, Matrix4x4.identity);
            }
            pos.Clear(); col.Clear(); uv0.Clear(); uv1.Clear(); uv2.Clear(); idx.Clear();
            Ribbons = 0; Glows = 0; any = false;
        }
    }
}
