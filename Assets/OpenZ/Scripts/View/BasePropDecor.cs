using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// base_props_v2 ground decor (non-blocking, never on the sim): around every installation, from the layout
    /// (Resources/OpenZ/BaseProps2/base_props_v2_layout.json, tools/layout_v2.py, A* over Zod's walkable tiles):
    /// <list type="bullet">
    /// <item>fuel dumps and supply compounds: a jeep-track path (two tyre prints, a second older pair) from inside the gate to
    /// the nearest road;</item>
    /// <item>solar farms, generator posts and comms posts: a trodden foot trail (three walkers' robot boot prints) toward the
    /// nearest building (or road), and a power cable lying in the sand from the battery / distribution box / radio cabinet out
    /// through the gate into that building.</item>
    /// </list>
    /// Prints use TrackMarks' own look (OpenZ/TrackMark: multiplies the lit ground, ZTest Always + the camera depth texture with a
    /// 10 cm tolerance, so it can never z-fight or paint a unit standing on it) with an endless life, the vertices on the
    /// RENDERED terrain (TrackMarks.Surface) + 2 cm, faint on roads / concrete (TrackSurface.MarkStrength). Cables are thin tubes
    /// half sunk into the ground (the base props paint material, the atlas cable swatch). Two static meshes, two draw calls.
    /// </summary>
    public class BasePropDecor : MonoBehaviour
    {
        public static BasePropDecor I { get; private set; }
        /// <summary>The thin dark lines between the fort, the huts and the solar array (base-prop cables and foot trails): off. Ground
        /// wear is only a small trodden patch at factory / fort doors (GW1), never lines or paths between buildings.</summary>
        public static bool On = false;
        public string level = "L02";
        public int trackQuads, cableVerts;
        const float Lift = 0.02f, TyreW = 0.26f, Gauge = 1.42f, BermPad = 0.18f, CableR = 0.026f;

        Mesh trackMesh, cableMesh;
        Material trackMat;

        public static void Ensure(Scene s, string level)
        {
            if (!Application.isPlaying || !On) return;
            if (I && I.gameObject.scene == s) return;
            var go = new GameObject("BasePropDecor");
            SceneManager.MoveGameObjectToScene(go, s);
            var d = go.AddComponent<BasePropDecor>();
            d.level = level;
        }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; if (trackMesh) Destroy(trackMesh); if (cableMesh) Destroy(cableMesh); if (trackMat) Destroy(trackMat); }

        IEnumerator Start()
        {
            // TrackMarks rasterises the rendered terrain heights at its Start: wait for it (up to 6 s), else use the collider
            float t0 = Time.realtimeSinceStartup;
            while (!(TrackMarks.Active && TrackMarks.I.Surface != null) && Time.realtimeSinceStartup - t0 < 6f) yield return null;
            yield return null;
            try { Build(); }
            catch (System.Exception e) { Debug.LogWarning("[OpenZ] base props v2 decor: " + e.Message); }
        }

        // ------------------------------------------------------------------------------------------------ heights
        TrackSurface surf; Collider terrain;
        readonly List<(Vector3 c, float top)> pads = new List<(Vector3, float)>();

        bool Ground(float x, float z, out float y, out float strength)
        {
            strength = 1f;
            bool ok;
            if (surf != null)
            {
                ok = surf.Sample(x, z, out y, out bool deck);
                if (deck) { strength = 0f; return false; }
                strength = surf.MarkStrength(x, z);
            }
            else
            {
                y = 0f; ok = false;
                if (terrain && terrain.Raycast(new Ray(new Vector3(x, 200f, z), Vector3.down), out var h, 400f)) { y = h.point.y; ok = true; }
                else if (ZGame.I) { y = ZGame.I.GroundY(x, z); ok = true; }
            }
            // on an installation pad: the pad top (the prints / cables run over it)
            foreach (var p in pads)
                if (Mathf.Abs(x - p.c.x) < 1.42f && Mathf.Abs(z - p.c.z) < 1.22f && y < p.top) { y = p.top; strength *= 0.6f; }
            return ok;
        }

        /// <summary>A cable crossing an installation's ring (sandbags / blocks / kerb, ~0.27 m) lies over it, not through it.</summary>
        void OverRing(float x, float z, ref float y)
        {
            foreach (var p in pads)
            {
                float dx = Mathf.Abs(x - p.c.x), dz = Mathf.Abs(z - p.c.z);
                if (dx > 1.74f || dz > 1.54f) continue;
                float ring = Mathf.Max(Mathf.InverseLerp(1.36f, 1.5f, dx) * (1f - Mathf.InverseLerp(1.62f, 1.76f, dx)),
                                       Mathf.InverseLerp(1.16f, 1.3f, dz) * (1f - Mathf.InverseLerp(1.42f, 1.56f, dz)));
                if (ring > 0f) y = Mathf.Max(y, Mathf.Lerp(y, p.top - 0.045f + 0.29f, Mathf.Clamp01(ring * 1.6f)));
            }
        }

        // ------------------------------------------------------------------------------------------------ build
        void Build()
        {
            var lay = BasePropHuts.LevelLayout(level);
            if (lay?.huts == null) { Debug.Log($"[OpenZ] base props v2 decor: no layout for {level}"); return; }
            surf = TrackMarks.Active ? TrackMarks.I.Surface : null;
            var col = GameObject.Find("COL_Terrain"); terrain = col ? col.GetComponent<Collider>() : null;
            Material paint = null;
            pads.Clear();
            foreach (var bp in FindObjectsByType<BaseProp>(FindObjectsSortMode.None))
            {
                pads.Add((bp.transform.position, bp.transform.position.y + 0.045f));
                if (!paint && bp.chunkMaterial) paint = bp.chunkMaterial;
            }
            var tv = new List<Vector3>(); var tc = new List<Color32>(); var tu0 = new List<Vector2>(); var tu1 = new List<Vector4>(); var ti = new List<int>();
            var cv = new List<Vector3>(); var cn = new List<Vector3>(); var cu = new List<Vector2>(); var cc = new List<Color32>(); var ci = new List<int>();
            var cableUV = lay.cable_uv != null && lay.cable_uv.Length >= 2 ? new Vector2(lay.cable_uv[0], lay.cable_uv[1]) : new Vector2(0.03f, 0.03f);
            foreach (var h in lay.huts)
            {
                if (h?.track == null || h.track.Length < 4) continue;
                var pts = new List<Vector2>();
                for (int i = 0; i + 1 < h.track.Length; i += 2) pts.Add(new Vector2(h.track[i], h.track[i + 1]));
                var rnd = new System.Random(h.x * 7919 + h.y * 104729);
                if (h.track_kind == "tyre")
                {
                    Tyres(pts, 0f, 1f, 0.37f, tv, tc, tu0, tu1, ti);
                    Tyres(pts, 0.17f, 0.5f, 0.71f, tv, tc, tu0, tu1, ti);                       // an older pair: the path is in use
                }
                else
                {
                    float[] lanes = { -0.2f, 0.04f, 0.24f };
                    for (int k = 0; k < lanes.Length; k++) Foot(pts, lanes[k], 0.2f * k + (float)rnd.NextDouble() * 0.3f, k == 1 ? 0.85f : 0.62f, rnd, tv, tc, tu0, tu1, ti);
                }
                if (h.cables != null)
                    foreach (var line in h.cables)
                    {
                        if (line?.xz == null || line.xz.Length < 4) continue;
                        var cp = new List<Vector2>();
                        for (int i = 0; i + 1 < line.xz.Length; i += 2) cp.Add(new Vector2(line.xz[i], line.xz[i + 1]));
                        Cable(cp, cableUV, cv, cn, cu, cc, ci);
                    }
            }
            trackQuads = ti.Count / 6; cableVerts = cv.Count;
            if (tv.Count > 0) BuildTrackMesh(tv, tc, tu0, tu1, ti);
            if (cv.Count > 0 && paint) BuildCableMesh(cv, cn, cu, cc, ci, paint);
            Debug.Log($"[OpenZ] base props v2 decor ({level}): {trackQuads} print quads, {cv.Count} cable vertices, heights from {(surf != null ? (surf.RenderHeights ? "rendered TER_*" : "COL_Terrain") : "collider")}");
        }

        static Vector2 Tangent(List<Vector2> p, int i)
        {
            var a = p[Mathf.Max(0, i - 1)]; var b = p[Mathf.Min(p.Count - 1, i + 1)];
            var d = b - a; return d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.up;
        }

        /// <summary>Fade in over the first 0.6 m, out over the last 3 m (the path thins out toward the road).</summary>
        static float EndFade(float s, float total) => Mathf.Clamp01(s / 0.6f) * Mathf.Clamp01((total - s) / 3f);

        void Tyres(List<Vector2> pts, float shift, float str, float seed, List<Vector3> v, List<Color32> c, List<Vector2> u0, List<Vector4> u1, List<int> tri)
        {
            float total = 0f; for (int i = 1; i < pts.Count; i++) total += (pts[i] - pts[i - 1]).magnitude;
            float w = TyreW * (1f + 2f * BermPad);
            foreach (float side in new[] { -1f, 1f })
            {
                int start = v.Count; float s = 0f;
                for (int i = 0; i < pts.Count; i++)
                {
                    if (i > 0) s += (pts[i] - pts[i - 1]).magnitude;
                    var t = Tangent(pts, i); var n = new Vector2(-t.y, t.x);
                    var cc = pts[i] + n * (side * Gauge * 0.5f + shift + 0.04f * Mathf.Sin(s * 0.7f + seed * 9f));
                    var a = cc - n * (w * 0.5f); var b = cc + n * (w * 0.5f);
                    Ground(a.x, a.y, out float ya, out float sa); Ground(b.x, b.y, out float yb, out float sb);
                    float k = str * EndFade(s, total) * Mathf.Min(sa, sb);
                    byte al = (byte)Mathf.RoundToInt(Mathf.Clamp01(k) * 255f);
                    v.Add(new Vector3(a.x, ya + Lift, a.y)); v.Add(new Vector3(b.x, yb + Lift, b.y));
                    c.Add(new Color32(255, 255, 255, al)); c.Add(new Color32(255, 255, 255, al));
                    u0.Add(new Vector2(0f, s)); u0.Add(new Vector2(1f, s));
                    var d = new Vector4(-1f, 1e8f, 1e9f, 0f + seed);
                    u1.Add(d); u1.Add(d);
                    if (i > 0) { int q = start + (i - 1) * 2; tri.Add(q); tri.Add(q + 2); tri.Add(q + 1); tri.Add(q + 1); tri.Add(q + 2); tri.Add(q + 3); }
                }
            }
        }

        void Foot(List<Vector2> pts, float lane, float phase, float str, System.Random rnd, List<Vector3> v, List<Color32> c, List<Vector2> u0, List<Vector4> u1, List<int> tri)
        {
            // a walker's boot prints: left / right every 0.42 m, 0.3 x 0.16 m (TrackMarks robot foot kind 2)
            float total = 0f; var cum = new List<float> { 0f };
            for (int i = 1; i < pts.Count; i++) { total += (pts[i] - pts[i - 1]).magnitude; cum.Add(total); }
            const float stride = 0.42f, L = 0.3f, Wd = 0.16f;
            float pl = L * (1f + 2f * BermPad), pw = Wd * (1f + 2f * BermPad);
            int step = 0; int seg = 1;
            for (float s = 0.5f + phase * stride; s < total - 0.3f; s += stride, step++)
            {
                while (seg < cum.Count - 1 && cum[seg] < s) seg++;
                float k0 = Mathf.InverseLerp(cum[seg - 1], cum[seg], s);
                var p = Vector2.Lerp(pts[seg - 1], pts[seg], k0);
                var t = (pts[seg] - pts[seg - 1]).normalized;
                float yaw = ((float)rnd.NextDouble() - 0.5f) * 16f * Mathf.Deg2Rad + (step % 2 == 0 ? -0.08f : 0.08f);
                var f = new Vector2(t.x * Mathf.Cos(yaw) - t.y * Mathf.Sin(yaw), t.x * Mathf.Sin(yaw) + t.y * Mathf.Cos(yaw));
                var n = new Vector2(-f.y, f.x);
                var cc = p + new Vector2(-t.y, t.x) * (lane + (step % 2 == 0 ? -0.11f : 0.11f) + ((float)rnd.NextDouble() - 0.5f) * 0.05f);
                var heel = cc - f * (pl * 0.5f); var toe = cc + f * (pl * 0.5f);
                Vector2[] q = { heel - n * (pw * 0.5f), heel + n * (pw * 0.5f), toe - n * (pw * 0.5f), toe + n * (pw * 0.5f) };
                Vector2[] uv = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
                float kmin = 1f; int b0 = v.Count;
                var ys = new float[4];
                for (int j = 0; j < 4; j++) { Ground(q[j].x, q[j].y, out ys[j], out float sj); kmin = Mathf.Min(kmin, sj); }
                float k = str * EndFade(s, total) * kmin * (0.8f + 0.2f * (float)rnd.NextDouble());
                byte al = (byte)Mathf.RoundToInt(Mathf.Clamp01(k) * 255f);
                var d = new Vector4(-1f, 1e8f, 1e9f, 2f + 0.13f + 0.5f * (float)rnd.NextDouble());
                for (int j = 0; j < 4; j++)
                {
                    v.Add(new Vector3(q[j].x, ys[j] + Lift, q[j].y)); c.Add(new Color32(255, 255, 255, al)); u0.Add(uv[j]); u1.Add(d);
                }
                tri.Add(b0); tri.Add(b0 + 2); tri.Add(b0 + 1); tri.Add(b0 + 1); tri.Add(b0 + 2); tri.Add(b0 + 3);
            }
        }

        void Cable(List<Vector2> pts, Vector2 uv, List<Vector3> v, List<Vector3> nrm, List<Vector2> u, List<Color32> c, List<int> tri)
        {
            const int S = 6;
            int start = v.Count;
            var centres = new List<Vector3>();
            foreach (var p in pts) { Ground(p.x, p.y, out float y, out _); OverRing(p.x, p.y, ref y); centres.Add(new Vector3(p.x, y + CableR * 0.55f, p.y)); }
            for (int i = 0; i < centres.Count; i++)
            {
                var a = centres[Mathf.Max(0, i - 1)]; var b = centres[Mathf.Min(centres.Count - 1, i + 1)];
                var t = (b - a).sqrMagnitude > 1e-8f ? (b - a).normalized : Vector3.forward;
                var side = Vector3.Cross(Vector3.up, t); if (side.sqrMagnitude < 1e-6f) side = Vector3.right; side.Normalize();
                var up = Vector3.Cross(t, side).normalized;
                for (int k = 0; k < S; k++)
                {
                    float ang = k * Mathf.PI * 2f / S;
                    var dir = side * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                    v.Add(centres[i] + dir * CableR); nrm.Add(dir); u.Add(uv); c.Add(new Color32(200, 200, 200, 255));
                }
                if (i > 0)
                    for (int k = 0; k < S; k++)
                    {
                        int a0 = start + (i - 1) * S + k, a1 = start + (i - 1) * S + (k + 1) % S, b0 = start + i * S + k, b1 = start + i * S + (k + 1) % S;
                        tri.Add(a0); tri.Add(a1); tri.Add(b0); tri.Add(a1); tri.Add(b1); tri.Add(b0);      // outward (Unity: front = Cross(B-A, C-A))
                    }
            }
        }

        void BuildTrackMesh(List<Vector3> v, List<Color32> c, List<Vector2> u0, List<Vector4> u1, List<int> tri)
        {
            var src = Resources.Load<Material>("OpenZ/FX/M_TrackMark");
            if (src && src.shader && src.shader.name == "OpenZ/TrackMark") trackMat = new Material(src);
            else { var sh = Shader.Find("OpenZ/TrackMark"); if (sh) trackMat = new Material(sh); }
            if (!trackMat) { Debug.LogWarning("[OpenZ] base props v2 decor: OpenZ/TrackMark shader missing, no tracks"); return; }
            trackMat.name = "BasePropTracks";
            trackMat.SetFloat("_Intensity", 0.95f); trackMat.SetFloat("_Compress", 0.26f); trackMat.SetFloat("_Groove", 0.13f);
            trackMat.SetFloat("_Berm", 0.07f); trackMat.SetFloat("_BermPad", BermPad); trackMat.SetFloat("_FadeStart", 1f);
            trackMat.SetFloat("_DissolveScale", 2.6f);
            trackMat.SetFloat("_DepthTolerance", surf != null && surf.RenderHeights ? 0.1f : 0.18f);
            bool sceneDepth = true;
            var urp = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp) sceneDepth = urp.supportsCameraDepthTexture;
            trackMat.SetFloat("_SceneDepthTest", sceneDepth ? 1f : 0f);
            trackMat.SetFloat("_ZTest", (float)(sceneDepth ? CompareFunction.Always : CompareFunction.LessEqual));
            trackMesh = new Mesh { name = "BasePropTracks", indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            trackMesh.SetVertices(v); trackMesh.SetColors(c); trackMesh.SetUVs(0, u0); trackMesh.SetUVs(1, u1); trackMesh.SetTriangles(tri, 0);
            trackMesh.RecalculateBounds();
            var go = new GameObject("Tracks"); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = trackMesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = trackMat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        void BuildCableMesh(List<Vector3> v, List<Vector3> n, List<Vector2> u, List<Color32> c, List<int> tri, Material paint)
        {
            cableMesh = new Mesh { name = "BasePropCables", indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            cableMesh.SetVertices(v); cableMesh.SetNormals(n); cableMesh.SetUVs(0, u); cableMesh.SetColors(c); cableMesh.SetTriangles(tri, 0);
            cableMesh.RecalculateBounds();
            var go = new GameObject("Cables"); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = cableMesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = paint;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true;
        }
    }
}
