// OpenZ (GPL-3.0) - Level 16: the far land of a low shot. The map and the jungle that closes it stand on a plateau; the game's
// steep camera never sees past it. A LOW camera that shows the horizon (the mission-start flyover until its last shot, an ending's
// scene, a picture with OzL16FlyoverFlora.ForceOn) saw the plateau and the far pyramid stand in nothing. So while the low-shot look
// is on, a wide sheet of swamp water lies round the plateau out to the haze and three ridges of far jungle hills close the horizon,
// each farther one paler in the level's own distance haze. The ridges stay low under the sun's bearing, so the rising sun is never
// behind a hill. Lit by the level's own light (the material of the ground skirt, recoloured), drawn only: no rule reads it, nothing
// of it exists in normal play.
// Play check: return OpenZ.OzL16FlyoverFlora.FarStatus();
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class OzL16FlyoverFlora
    {
        public static bool FarLand = true;
        /// <summary>The water's height (m), how far it reaches from the map's middle (m), and the gap the ridges leave under the sun:
        /// half its width (deg) and the most a ridge may stand there (m).</summary>
        public static float FarWaterY = -3.2f, FarWaterOut = 1150f, FarSunGapDeg = 24f, FarSunGapHeight = 6f;
        /// <summary>Three ridges: metres from the map's middle, their lowest and highest crest (m).</summary>
        public static float[] FarRidgeAt = { 430f, 560f, 700f }, FarRidgeLow = { 22f, 30f, 40f }, FarRidgeHigh = { 44f, 54f, 66f };
        public static Color FarWater = new Color(0.25f, 0.23f, 0.15f), FarRidgeNear = new Color(0.12f, 0.19f, 0.10f), FarRidgeFar = new Color(0.16f, 0.23f, 0.15f);
        /// <summary>A ridge's foot stands in the water's own tone and a band of haze lies over it (no hard line where the hills meet
        /// the water): the foot's colour, and the share of a ridge's height at which the haze ends.</summary>
        public static Color FarFoot = new Color(0.25f, 0.23f, 0.15f);
        public static float FarHazeShare = 0.38f, FarHazeMix = 0.55f;
        /// <summary>The water is laid in square tiles of this size (m): the level's fog is worked out at the corners of each triangle, so a
        /// few huge triangles fogged unevenly and showed hard pale wedges in the sky's haze (the sunrise shot); small tiles keep it even.</summary>
        public static float FarWaterTile = 32f;
        /// <summary>Set in Play after a number was changed: the far land is built again on the next frame.</summary>
        public static bool FarRebuild;
        public static int FarTriangles;

        GameObject farGo; Mesh farMesh; readonly List<Material> farMats = new List<Material>();

        void ShowFar(bool show)
        {
            if (!show) { if (farGo) farGo.SetActive(false); return; }
            if (!FarLand) return;
            BuildFar();
            if (farGo) farGo.SetActive(true);
        }

        void TickFar() { if (FarRebuild) { FarRebuild = false; ShowFar(FarLand); if (!FarLand && farGo) farGo.SetActive(false); } }

        void BuildFar()
        {
            var skirt = GameObject.Find("SKIRT"); var sr = skirt ? skirt.GetComponent<Renderer>() : null;
            if (!sr || !sr.sharedMaterial) { Debug.LogWarning("[OpenZ L16 flyover flora] the ground skirt was not found: no far land in the low shots"); return; }
            if (!farGo)
            {
                farGo = new GameObject("L16_FarLand"); farGo.transform.SetParent(transform, false);
                farMesh = new Mesh { name = "L16_FarLand", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                farGo.AddComponent<MeshFilter>().sharedMesh = farMesh;
                var mr = farGo.AddComponent<MeshRenderer>(); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                int parts = 1 + FarRidgeAt.Length;
                for (int i = 0; i < parts; i++)
                {
                    var m = new Material(sr.sharedMaterial) { name = "L16FarLand " + i + " (runtime)" };
                    if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
                    farMats.Add(m);
                }
                mr.sharedMaterials = farMats.ToArray();
            }
            int ridges = Mathf.Min(FarRidgeAt.Length, Mathf.Min(FarRidgeLow.Length, FarRidgeHigh.Length));
            for (int i = 0; i < farMats.Count; i++)
            {
                Color c = i == 0 ? FarWater : Color.Lerp(FarRidgeNear, FarRidgeFar, ridges > 1 ? (i - 1) / (float)(ridges - 1) : 0f);
                if (farMats[i].HasProperty("_BaseColor")) farMats[i].SetColor("_BaseColor", i == 0 ? c : Color.white);   // a ridge's colour runs in its points (foot, haze, hill)
                if (farMats[i].HasProperty("_UseVertexColor")) farMats[i].SetFloat("_UseVertexColor", 1f);
                if (farMats[i].HasProperty("_Smoothness")) farMats[i].SetFloat("_Smoothness", i == 0 ? 0.55f : 0.05f);
            }
            Vector3 mid = new Vector3(map.x * 0.5f, 0f, -map.y * 0.5f);
            var sun = RenderSettings.sun; Vector3 sd = sun ? -sun.transform.forward : Vector3.right; sd.y = 0f;
            float sunAz = sd.sqrMagnitude > 1e-4f ? Mathf.Atan2(sd.x, sd.z) * Mathf.Rad2Deg : 90f;
            var v = new List<Vector3>(); var nrm = new List<Vector3>(); var col = new List<Color>(); var subs = new List<List<int>>();
            // the water: round the plateau (none under the map), in small square tiles (FarWaterTile)
            var w = new List<int>(); subs.Add(w);
            float x0 = -GroundMargin, x1 = map.x + GroundMargin, z0 = -map.y - GroundMargin, z1 = GroundMargin, o = FarWaterOut, y = FarWaterY;
            var xs = TileLines(mid.x - o, x0, x1, mid.x + o); var zs = TileLines(mid.z - o, z0, z1, mid.z + o);
            for (int i = 0; i + 1 < xs.Count; i++)
                for (int j = 0; j + 1 < zs.Count; j++)
                {
                    float cx = (xs[i] + xs[i + 1]) * 0.5f, cz = (zs[j] + zs[j + 1]) * 0.5f;
                    if (cx > x0 && cx < x1 && cz > z0 && cz < z1) continue;                                // the plateau's own ground
                    Quad(v, nrm, w, new Vector3(xs[i], y, zs[j]), new Vector3(xs[i + 1], y, zs[j]), new Vector3(xs[i + 1], y, zs[j + 1]), new Vector3(xs[i], y, zs[j + 1]));
                }
            while (col.Count < v.Count) col.Add(Color.white);
            // the ridges, the farthest first
            const int seg = 240;
            for (int r = 0; r < ridges; r++)
            {
                var t = new List<int>(); subs.Add(t);
                float rad = FarRidgeAt[r], p1 = 1.7f + r * 2.3f, p2 = 0.4f + r * 1.1f, p3 = 5.2f + r * 0.7f; int b0 = v.Count;
                Color ridgeC = Color.Lerp(FarRidgeNear, FarRidgeFar, ridges > 1 ? r / (float)(ridges - 1) : 0f).linear, footC = FarFoot.linear;
                for (int s = 0; s <= seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f, deg = a * Mathf.Rad2Deg;
                    float n = 0.5f + 0.5f * (Mathf.Sin(a * 3f + p1) * 0.5f + Mathf.Sin(a * 7f + p2) * 0.3f + Mathf.Sin(a * 13f + p3) * 0.2f);
                    float h = Mathf.Lerp(FarRidgeLow[r], FarRidgeHigh[r], n);
                    float gap = Mathf.SmoothStep(0f, 1f, Mathf.Abs(Mathf.DeltaAngle(deg, sunAz)) / Mathf.Max(1f, FarSunGapDeg));
                    h = Mathf.Lerp(Mathf.Min(h, FarSunGapHeight), h, FarGapWanted() ? gap : 1f);          // round 4: the endings keep the hills' own crest (OzL16FlyoverFlora.Endings.cs)
                    Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)), foot = mid + dir * rad; foot.y = y - 1.5f;
                    Vector3 nn = (-dir * 0.55f + Vector3.up * 0.83f).normalized, nf = (-dir * 0.10f + Vector3.up).normalized;
                    // three rows: the foot in the water's tone and light, the end of the haze band, the crest
                    v.Add(foot); nrm.Add(nf); col.Add(footC);
                    v.Add(new Vector3(foot.x, y + (h - y) * FarHazeShare, foot.z)); nrm.Add(nn); col.Add(Color.Lerp(footC, ridgeC, FarHazeMix));
                    v.Add(foot + Vector3.up * (h - foot.y)); nrm.Add(nn); col.Add(ridgeC);
                }
                for (int s = 0; s < seg; s++)
                {
                    for (int row = 0; row < 2; row++)
                    {
                        int b = b0 + s * 3 + row;
                        t.Add(b); t.Add(b + 1); t.Add(b + 3); t.Add(b + 1); t.Add(b + 4); t.Add(b + 3);
                        t.Add(b); t.Add(b + 3); t.Add(b + 1); t.Add(b + 1); t.Add(b + 3); t.Add(b + 4);
                    }
                }
            }
            farMesh.Clear();
            farMesh.SetVertices(v); farMesh.SetNormals(nrm); farMesh.SetColors(col); farMesh.subMeshCount = subs.Count; int tris = 0;
            for (int i = 0; i < subs.Count; i++) { farMesh.SetTriangles(subs[i], i); tris += subs[i].Count / 3; }
            farMesh.RecalculateBounds(); FarTriangles = tris;
        }

        /// <summary>The tile lines from a to d through b and c (b and c kept exactly: the edge of the plateau's hole), at most FarWaterTile apart.</summary>
        static List<float> TileLines(float a, float b, float c, float d)
        {
            var l = new List<float> { a };
            float[] ends = { b, c, d };
            foreach (float e in ends)
            {
                float s0 = l[l.Count - 1]; int n = Mathf.Max(1, Mathf.CeilToInt((e - s0) / Mathf.Max(4f, FarWaterTile)));
                for (int k = 1; k <= n; k++) l.Add(Mathf.Lerp(s0, e, k / (float)n));
            }
            return l;
        }

        static void Quad(List<Vector3> v, List<Vector3> n, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            for (int k = 0; k < 4; k++) n.Add(Vector3.up);
            t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2);
            t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        void DestroyFar() { foreach (var m in farMats) if (m) Destroy(m); farMats.Clear(); if (farMesh) Destroy(farMesh); }

        /// <summary>One-line state for the Play check.</summary>
        public static string FarStatus() => !I ? "far land: not in this scene" : $"far land: {(I.farGo ? (I.farGo.activeSelf ? "drawn" : "built, not drawn") : "not built")}, {FarTriangles} triangles, water at {FarWaterY:F1} m out to {FarWaterOut:F0} m, {FarRidgeAt.Length} ridges";
    }
}
