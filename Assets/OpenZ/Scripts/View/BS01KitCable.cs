using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// A flexible part of a BS-01 type kit that spans two bones or reaches the weapon (bs01_types DESIGN.md section 4):
    /// the psycho's brass feed belt (drum -> right pad, pad -> MG receiver) and the pyro's fuel hose (right tank -> hip clip
    /// -> flamer). Every LateUpdate, after WeaponHold (order 80) has placed the gun, a Catmull-Rom tube is rebuilt through the
    /// socket transforms with a sag on each span. Belt: alternating brass rounds and dark links; hose: rubber with brass
    /// ferrules. UVs point at the flat cells of the shared kit atlas, so it uses the kit material (M_BS01_Kit). Hidden while
    /// Suspended (seated, ragdoll: set by BS01TypeKit), when an end is missing or inactive, or when a span is over maxSpan.
    /// </summary>
    [DefaultExecutionOrder(90), RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class BS01KitCable : MonoBehaviour
    {
        public enum Style { Belt, Hose }
        public Style style = Style.Belt;
        public Transform[] points;
        public float radius = 0.026f, sag = 0.1f, maxSpan = 1.2f;
        [Range(4, 16)] public int segmentsPerSpan = 10;
        [Range(4, 8)] public int sides = 6;
        [System.NonSerialized] public bool Suspended;

        // flat cells of textures/BS01_Kit_BaseColor.png (kitpal.py layout: 8 x 8 cells of 128 px in a 1024 atlas)
        static readonly Vector2 UvBrass = new Vector2(0.6875f, 0.8125f), UvLink = new Vector2(0.9375f, 0.8125f),
                                UvRubber = new Vector2(0.4375f, 0.6875f), UvFerrule = new Vector2(0.8125f, 0.8125f);

        MeshFilter mf; MeshRenderer mr; Mesh mesh;
        Vector3[] verts, norms; Vector2[] uvs; Color32[] cols; int[] tris;
        Vector3[] path = new Vector3[0];

        void OnEnable()
        {
            mf = GetComponent<MeshFilter>(); mr = GetComponent<MeshRenderer>();
            mesh = new Mesh { name = "BS01KitCable" };                 // never shared: a cloned unit gets its own
            mesh.MarkDynamic();
            mf.sharedMesh = mesh;
        }

        void OnDisable()
        {
            if (mesh) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
            mesh = null;
        }

        void LateUpdate() => Rebuild();

        public void Rebuild()
        {
            if (!mesh || !mr) return;
            bool ok = !Suspended && points != null && points.Length >= 2;
            float s = transform.lossyScale.y;
            if (ok)
                for (int i = 0; i < points.Length; i++)
                {
                    if (!points[i] || !points[i].gameObject.activeInHierarchy) { ok = false; break; }
                    if (i > 0 && (points[i].position - points[i - 1].position).magnitude > maxSpan * Mathf.Max(0.5f, s)) { ok = false; break; }
                }
            mr.forceRenderingOff = !ok;
            if (!ok) return;

            // control points: every span gets a sagging midpoint (gravity in world space)
            int nCtrl = points.Length * 2 - 1;
            var ctrl = new Vector3[nCtrl];
            for (int i = 0; i < points.Length; i++)
            {
                ctrl[i * 2] = points[i].position;
                if (i + 1 < points.Length)
                {
                    var a = points[i].position; var b = points[i + 1].position;
                    ctrl[i * 2 + 1] = (a + b) * 0.5f + Vector3.down * sag * s * Mathf.Clamp01((b - a).magnitude / (0.6f * s));
                }
            }
            int nSeg = (nCtrl - 1) * segmentsPerSpan / 2;
            nSeg = Mathf.Max(nSeg, 4);
            if (path.Length != nSeg + 1) path = new Vector3[nSeg + 1];
            for (int k = 0; k <= nSeg; k++)
            {
                float u = (float)k / nSeg * (nCtrl - 1);
                int i = Mathf.Min(Mathf.FloorToInt(u), nCtrl - 2); float t = u - i;
                var p0 = ctrl[Mathf.Max(i - 1, 0)]; var p1 = ctrl[i]; var p2 = ctrl[i + 1]; var p3 = ctrl[Mathf.Min(i + 2, nCtrl - 1)];
                path[k] = 0.5f * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);
            }

            // each segment owns its two rings, so a segment's UV never blends into another atlas cell
            int vPerSeg = sides * 2, nv = nSeg * vPerSeg, nt = nSeg * sides * 6;
            if (verts == null || verts.Length != nv)
            {
                verts = new Vector3[nv]; norms = new Vector3[nv]; uvs = new Vector2[nv]; cols = new Color32[nv]; tris = new int[nt];
                for (int i = 0; i < nv; i++) cols[i] = new Color32(255, 255, 255, 0);
                mesh.Clear();
            }
            var inv = transform.worldToLocalMatrix;
            Vector3 tPrev = (path[1] - path[0]).normalized;
            Vector3 n0 = Vector3.Cross(tPrev, Vector3.up); if (n0.sqrMagnitude < 1e-6f) n0 = Vector3.Cross(tPrev, Vector3.right); n0.Normalize();
            var frameN = new Vector3[nSeg + 1]; var frameB = new Vector3[nSeg + 1];
            for (int k = 0; k <= nSeg; k++)
            {   // parallel transport: no twisting when the belt swings
                var tk = (path[Mathf.Min(k + 1, nSeg)] - path[Mathf.Max(k - 1, 0)]).normalized;
                n0 = Vector3.ProjectOnPlane(n0, tk); if (n0.sqrMagnitude < 1e-8f) n0 = Vector3.Cross(tk, Vector3.up); n0.Normalize();
                frameN[k] = n0; frameB[k] = Vector3.Cross(tk, n0);
            }
            float r = radius * s;
            int vi = 0, ti = 0;
            for (int k = 0; k < nSeg; k++)
            {
                Vector2 uv; float rr = r;
                if (style == Style.Belt) uv = (k & 1) == 0 ? UvBrass : UvLink;
                else
                {
                    float f = (k + 0.5f) / nSeg;
                    bool ferrule = Mathf.Abs(f - 0.12f) < 0.03f || Mathf.Abs(f - 0.88f) < 0.03f || Mathf.Abs(f - 0.5f) < 0.02f;
                    uv = ferrule ? UvFerrule : UvRubber; rr = ferrule ? r * 1.3f : r;
                }
                int baseV = vi;
                for (int e = 0; e < 2; e++)
                {
                    int kk = k + e;
                    for (int j = 0; j < sides; j++)
                    {
                        float a = j * Mathf.PI * 2 / sides;
                        var dir = frameN[kk] * Mathf.Cos(a) + frameB[kk] * Mathf.Sin(a);
                        verts[vi] = inv.MultiplyPoint3x4(path[kk] + dir * rr);
                        norms[vi] = inv.MultiplyVector(dir).normalized;
                        uvs[vi] = uv;
                        vi++;
                    }
                }
                for (int j = 0; j < sides; j++)
                {
                    int j1 = (j + 1) % sides;
                    int a0 = baseV + j, a1 = baseV + j1, b0 = baseV + sides + j, b1 = baseV + sides + j1;
                    tris[ti++] = a0; tris[ti++] = a1; tris[ti++] = b0;          // clockwise seen from outside (Unity front face)
                    tris[ti++] = a1; tris[ti++] = b1; tris[ti++] = b0;
                }
            }
            mesh.vertices = verts; mesh.normals = norms; mesh.uv = uvs; mesh.colors32 = cols;
            mesh.SetTriangles(tris, 0, false);
            mesh.RecalculateBounds();
        }
    }
}
