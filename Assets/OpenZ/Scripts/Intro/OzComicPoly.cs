using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OpenZ.Intro
{
    /// <summary>
    /// mapped over <see cref="uvRect"/> (the panel's bbox), so a panel's RenderTexture or comic background fills exactly its
    /// angled shape. <see cref="inflate"/> > 0 grows the shape outward (the ink outline drawn under the panel).</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class OzComicPoly : MaskableGraphic
    {
        public List<Vector2> points = new List<Vector2>();
        public Rect uvRect = new Rect(0, 0, 1, 1);
        public float inflate;
        public bool radialInflate;
        public Texture tex;

        public override Texture mainTexture => tex != null ? tex : s_WhiteTexture;

        public void Set(List<Vector2> pts, Rect uv, float grow = 0f, bool radial = false)
        {
            points = pts; uvRect = uv; inflate = grow; radialInflate = radial; SetVerticesDirty();
        }

        public static Vector2 Centroid(List<Vector2> pts)
        {
            Vector2 c = Vector2.zero; if (pts == null || pts.Count == 0) return c;
            foreach (var p in pts) c += p; return c / pts.Count;
        }

        public static List<Vector2> Inflate(List<Vector2> pts, float w, bool radial)
        {
            var o = new List<Vector2>(pts.Count);
            if (w == 0f) { o.AddRange(pts); return o; }
            var c = Centroid(pts);
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                var p = pts[i];
                if (radial) { o.Add(p + (p - c).normalized * w); continue; }
                var a = pts[(i + n - 1) % n]; var b = pts[(i + 1) % n];
                Vector2 n1 = Normal(a, p, c), n2 = Normal(p, b, c);
                var m = (n1 + n2); if (m.sqrMagnitude < 1e-6f) m = n1; m.Normalize();
                float d = Vector2.Dot(m, n1); if (d < 0.25f) d = 0.25f;
                o.Add(p + m * (w / d));
            }
            return o;
        }

        static Vector2 Normal(Vector2 a, Vector2 b, Vector2 c)
        {
            var e = (b - a).normalized; var nrm = new Vector2(e.y, -e.x);
            if (Vector2.Dot(nrm, (a + b) * 0.5f - c) < 0f) nrm = -nrm;
            return nrm;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (points == null || points.Count < 3) return;
            var pts = Inflate(points, inflate, radialInflate);
            var c = Centroid(pts);
            var col = (Color32)color;
            UIVertex v = UIVertex.simpleVert; v.color = col;
            v.position = c; v.uv0 = Uv(c); vh.AddVert(v);
            foreach (var p in pts) { v.position = p; v.uv0 = Uv(p); vh.AddVert(v); }
            int n = pts.Count;
            for (int i = 0; i < n; i++) vh.AddTriangle(0, 1 + i, 1 + (i + 1) % n);
        }

        Vector2 Uv(Vector2 p) => new Vector2(uvRect.width == 0 ? 0 : (p.x - uvRect.x) / uvRect.width, uvRect.height == 0 ? 0 : (p.y - uvRect.y) / uvRect.height);

        // ---------------------------------------------------------------- shapes
        public static List<Vector2> Star(Vector2 c, float rOut, float rIn, int spikes, float rotDeg, int seed)
        {
            var r = new System.Random(seed); var o = new List<Vector2>();
            for (int i = 0; i < spikes * 2; i++)
            {
                float a = (rotDeg + 180f * i / spikes) * Mathf.Deg2Rad;
                float rr = (i % 2 == 0 ? rOut * (0.82f + 0.3f * (float)r.NextDouble()) : rIn * (0.9f + 0.15f * (float)r.NextDouble()));
                o.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr);
            }
            return o;
        }

        public static List<Vector2> Ellipse(Vector2 c, float rx, float ry, int seg)
        {
            var o = new List<Vector2>();
            for (int i = 0; i < seg; i++) { float a = 2f * Mathf.PI * i / seg; o.Add(c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry)); }
            return o;
        }

        /// <summary>An oval with a tail toward <paramref name="tip"/>: the tail's base sits inside the oval, its tip at the oval's edge
        /// + 0.6 x the bubble height toward the speaker (STORYBOARD comic_rules.bubbles), one outline round both.</summary>
        public static List<Vector2> Bubble(Vector2 c, float rx, float ry, Vector2 toward)
        {
            var dir = (toward - c); if (dir.sqrMagnitude < 1f) dir = new Vector2(-1, -1); dir.Normalize();
            float ang = Mathf.Atan2(dir.y / ry, dir.x / rx);
            Vector2 edge = c + new Vector2(Mathf.Cos(ang) * rx, Mathf.Sin(ang) * ry);
            Vector2 tip = edge + dir * Mathf.Min(0.6f * 2f * ry, Mathf.Max(8f, (toward - edge).magnitude));
            const int seg = 40; float half = 0.30f;
            var o = new List<Vector2>();
            for (int i = 0; i < seg; i++)
            {
                float a = ang + half + (2f * Mathf.PI - 2f * half) * i / (seg - 1);
                o.Add(c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry));
            }
            o.Add(tip);
            return o;
        }
    }
}
