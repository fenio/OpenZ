using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K256g2 (view only): the level's DRAWN water surface as a height grid, for the shuttle's landing mark (View/OzShuttleShadow.cs,
    /// OpenZ/ShuttleMarkDecal). Built once per level from the WAT_ meshes the scene draws (the way the fishing spots' grid is laid:
    /// the highest water triangle over the middle of each <see cref="Cell"/> cell), so every pool, ravine stream and the water under a
    /// bridge has its own height, also where the map's tiles are land (the deep channels of the Jungle, the wide river of the Desert).
    /// The same numbers go to a small float texture (<see cref="Texture"/>, point sampled: one texel a cell, -10000 = no drawn water).
    /// No readable water mesh: the grid is empty (the mark then lies on what the scene depth shows, as before over land).
    /// </summary>
    public static class OzWaterSurface
    {
        public const float Cell = 0.5f, None = -10000f;
        /// <summary>Proof: cells with drawn water, the grid's size, water meshes read / unreadable, builds.</summary>
        public static int WaterCells, Width, Height, Meshes, Unreadable, Builds;
        static float x0, z0; static float[] h; static Texture2D tex; static int builtFor = int.MinValue;

        public static float X0 => x0;
        public static float Z0 => z0;
        public static bool Has => h != null && WaterCells > 0;

        /// <summary>The grid's texture (null: no drawn water in this level).</summary>
        public static Texture2D Texture { get { Ensure(); return tex; } }

        /// <summary>The drawn water surface's height at (x, z): false where no water is drawn.</summary>
        public static bool At(float x, float z, out float y)
        {
            Ensure(); y = None;
            if (h == null) return false;
            int i = Mathf.FloorToInt((x - x0) / Cell), j = Mathf.FloorToInt((z - z0) / Cell);
            if (i < 0 || j < 0 || i >= Width || j >= Height) return false;
            y = h[j * Width + i];
            return y > None + 1f;
        }

        /// <summary>The shader's grid numbers: (x0, z0, 1 / cell, on) and (width, height).</summary>
        public static Vector4 ST { get { Ensure(); return new Vector4(x0, z0, 1f / Cell, Has && tex ? 1f : 0f); } }
        public static Vector4 Size => new Vector4(Mathf.Max(1, Width), Mathf.Max(1, Height), 0f, 0f);

        /// <summary>Built once per loaded level (keyed by the game object of the level's ZGame).</summary>
        public static void Ensure()
        {
            var g = ZGame.I; int key = g ? g.GetInstanceID() : 0;
            if (key == builtFor) return;
            builtFor = key; Build();
        }

        static void Build()
        {
            Builds++; h = null; WaterCells = 0; Width = Height = 0; Meshes = 0; Unreadable = 0;
            if (tex) { Object.Destroy(tex); tex = null; }
            var tris = new List<Vector3>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r || !r.name.StartsWith("WAT_") || !r.gameObject.activeInHierarchy || !r.enabled) continue;
                var mf = r.GetComponent<MeshFilter>(); var m = mf ? mf.sharedMesh : null;
                if (!m) continue;
                if (!m.isReadable) { Unreadable++; continue; }
                Meshes++;
                var v = m.vertices; var t = m.triangles; var M = r.transform.localToWorldMatrix;
                for (int i = 0; i + 2 < t.Length; i += 3) { tris.Add(M.MultiplyPoint3x4(v[t[i]])); tris.Add(M.MultiplyPoint3x4(v[t[i + 1]])); tris.Add(M.MultiplyPoint3x4(v[t[i + 2]])); }
            }
            if (tris.Count == 0)
            {
                Debug.Log("[OpenZ] shuttle mark: no readable drawn water (" + Unreadable + " unreadable water mesh(es)): the mark lies on the drawn ground only");
                return;
            }
            float ax = float.MaxValue, az = float.MaxValue, bx = float.MinValue, bz = float.MinValue;
            foreach (var p in tris) { ax = Mathf.Min(ax, p.x); az = Mathf.Min(az, p.z); bx = Mathf.Max(bx, p.x); bz = Mathf.Max(bz, p.z); }
            x0 = ax - Cell; z0 = az - Cell;
            Width = Mathf.Clamp(Mathf.CeilToInt((bx - ax + 2f * Cell) / Cell), 1, 4096); Height = Mathf.Clamp(Mathf.CeilToInt((bz - az + 2f * Cell) / Cell), 1, 4096);
            h = new float[Width * Height];
            for (int k = 0; k < h.Length; k++) h[k] = None;
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector2 a = new Vector2(tris[i].x, tris[i].z), b = new Vector2(tris[i + 1].x, tris[i + 1].z), c = new Vector2(tris[i + 2].x, tris[i + 2].z);
                float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (Mathf.Abs(area) < 1e-6f) continue;
                int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - x0) / Cell)), i1 = Mathf.Min(Width - 1, Mathf.FloorToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - x0) / Cell));
                int j0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - z0) / Cell)), j1 = Mathf.Min(Height - 1, Mathf.FloorToInt((Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - z0) / Cell));
                for (int j = j0; j <= j1; j++)
                    for (int ii = i0; ii <= i1; ii++)
                    {
                        float px = x0 + (ii + 0.5f) * Cell, pz = z0 + (j + 0.5f) * Cell;
                        float w0 = (b.x - px) * (c.y - pz) - (b.y - pz) * (c.x - px), w1 = (c.x - px) * (a.y - pz) - (c.y - pz) * (a.x - px), w2 = (a.x - px) * (b.y - pz) - (a.y - pz) * (b.x - px);
                        if (area > 0f ? (w0 >= 0f && w1 >= 0f && w2 >= 0f) : (w0 <= 0f && w1 <= 0f && w2 <= 0f))
                        {
                            int k = j * Width + ii; float y = (w0 * tris[i].y + w1 * tris[i + 1].y + w2 * tris[i + 2].y) / area;
                            if (y > h[k]) { if (h[k] <= None + 1f) WaterCells++; h[k] = y; }
                        }
                    }
            }
            var fmt = SystemInfo.SupportsTextureFormat(TextureFormat.RFloat) ? TextureFormat.RFloat : TextureFormat.RHalf;
            tex = new Texture2D(Width, Height, fmt, false, true) { name = "OZ_DrawnWaterHeights", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0 };
            var px2 = new Color[h.Length];
            for (int k = 0; k < h.Length; k++) px2[k] = new Color(h[k], 0f, 0f, 1f);
            tex.SetPixels(px2); tex.Apply(false, true);
            Debug.Log($"[OpenZ] shuttle mark: the drawn water surface from {Meshes} water mesh(es): {WaterCells} cells of {Cell} m ({Width} x {Height}, {fmt})");
        }
    }
}
