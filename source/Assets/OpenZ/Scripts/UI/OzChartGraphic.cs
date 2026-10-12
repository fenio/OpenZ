using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    // AddComponent can fail in the editor and the FORCES OVER TIME chart stayed empty
    /// <summary>The FORCES OVER TIME chart (guide 9.13): red and blue unit counts (0..70) as 2 u lines over 24 % fills, 3 grid
    /// lines, amber markers at the winner's zone captures; `Reveal` draws it left to right.</summary>
    public sealed class OzChartGraphic : MaskableGraphic
    {
        public List<(float t, int red, int blue)> Samples; public List<float> Marks = new List<float>();
        public float Duration = 1f, Reveal = 1f;
        /// <summary></summary>
        public float YMax = 70f;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect; float w = r.width, h = r.height;
            for (int g = 1; g <= 3; g++) Quad(vh, r.xMin, r.yMin + h * g / 4f - 0.5f, w, 1f, OzUi.A(OzUi.Khaki, 0.07f));
            if (Samples == null || Samples.Count == 0) return;
            float end = Mathf.Max(1f, Duration), cut = r.xMin + w * Mathf.Clamp01(Reveal);
            Series(vh, r, end, cut, true, OzUi.A(OzUi.TeamPaint(Team.Blue), 0.24f), OzUi.Mark(Team.Blue));
            Series(vh, r, end, cut, false, OzUi.A(OzUi.TeamPaint(Team.Red), 0.24f), OzUi.Mark(Team.Red));
            foreach (var t in Marks)
            {
                float x = r.xMin + w * Mathf.Clamp01(t / end); if (x > cut) continue;
                Quad(vh, x - 0.5f, r.yMin, 1f, h, OzUi.A(OzUi.Amber, 0.35f));
                int i = vh.currentVertCount; Color32 c = OzUi.Amber;
                vh.AddVert(new Vector3(x - 4, r.yMax), c, Vector2.zero); vh.AddVert(new Vector3(x + 4, r.yMax), c, Vector2.zero); vh.AddVert(new Vector3(x, r.yMax - 6), c, Vector2.zero);
                vh.AddTriangle(i, i + 1, i + 2);
            }
        }

        void Series(VertexHelper vh, Rect r, float end, float cut, bool blue, Color fill, Color line)
        {
            Vector2 prev = default; bool has = false;
            for (int k = 0; k < Samples.Count; k++)
            {
                var s = Samples[k];
                var p = new Vector2(r.xMin + r.width * Mathf.Clamp01(s.t / end), r.yMin + r.height * Mathf.Clamp01((blue ? s.blue : s.red) / Mathf.Max(1f, YMax)));
                if (has)
                {
                    var a = prev; var b = p;
                    if (a.x >= cut) break;
                    if (b.x > cut) b = Vector2.Lerp(a, b, (cut - a.x) / Mathf.Max(0.001f, b.x - a.x));
                    int i = vh.currentVertCount;                                                            // area under the segment
                    vh.AddVert(new Vector3(a.x, r.yMin), fill, Vector2.zero); vh.AddVert(new Vector3(a.x, a.y), fill, Vector2.zero);
                    vh.AddVert(new Vector3(b.x, b.y), fill, Vector2.zero); vh.AddVert(new Vector3(b.x, r.yMin), fill, Vector2.zero);
                    vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
                    var d = (b - a); var n = new Vector2(-d.y, d.x).normalized * 1f;                          // 2 u line
                    i = vh.currentVertCount;
                    vh.AddVert(a - n, line, Vector2.zero); vh.AddVert(a + n, line, Vector2.zero); vh.AddVert(b + n, line, Vector2.zero); vh.AddVert(b - n, line, Vector2.zero);
                    vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
                }
                prev = p; has = true;
            }
        }

        static void Quad(VertexHelper vh, float x, float y, float w, float h, Color c)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(x, y), c, Vector2.zero); vh.AddVert(new Vector3(x, y + h), c, Vector2.zero);
            vh.AddVert(new Vector3(x + w, y + h), c, Vector2.zero); vh.AddVert(new Vector3(x + w, y), c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
