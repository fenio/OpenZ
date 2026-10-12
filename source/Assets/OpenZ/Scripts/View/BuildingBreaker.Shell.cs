using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// The inside of a ruin. BuildingRuins cuts the model's outer skin, so the standing remains were paper-thin shells around bare
    /// ground. Two generated meshes are added under the ruin object (built with it at level load, shown and hidden with it):
    /// <list type="bullet">
    /// <item><b>Walls</b> (one per building key, in the ruin's mesh space): every open edge of the ruin's front faces (the jagged
    /// cut, door and window openings) is pushed into the wall by the wall thickness (the broken top / step face) and, where the wall
    /// hangs below the edge, dropped to the model's base (the inner wall face). Where the model already has a thickness (a double
    /// wall, a pillar, a small detail) the push is half of it, so both sides meet inside.</item>
    /// <item><b>Floor</b> (one per building): a grid over the ground cells the intact model covers from above, draped on the
    /// terrain and never below the model's base: no ground shows inside the footprint and higher ground never lies over it.</item>
    /// </list>
    /// Bare concrete: the chunk library's materials, or without the library a flat colour on the building's own shader.
    /// Visual only: no colliders.
    /// </summary>
    public partial class BuildingBreaker
    {
        public static bool RuinShell = true;
        /// <summary>Wall thickness as a fraction of the model's longer footprint side (a 20 m fort: 0.8 m); floor height over the ground (m).</summary>
        public static float ShellWallThick = 0.04f, ShellFloorLift = 0.08f;
        /// <summary>How far each face is taken from the level's concrete toward soot (0 = clean, 1 = soot): broken tops, inner wall faces, floor.</summary>
        public static float ShellTopSoot = 0.2f, ShellInnerSoot = 0.4f, ShellFloorSoot = 0.6f;
        public const int ShellGrid = 48;
        public static string LastShellStats = "";

        sealed class ShellSet
        {
            public Mesh walls; public bool[] mask; public Bounds box; public int edges, cells;
            // the rubble floor (BuildingBreaker.RuinFloor.cs): per cell the distance (m) to the nearest cell that is not free floor,
            // the scale and cell size (m), the free cell count, rebar anchors on the broken wall tops (mesh space) with their inward direction
            public float[] dist; public float metres = 1f, cellX = 1f, cellZ = 1f; public int free;
            public readonly List<Vector3> stubPos = new List<Vector3>(), stubIn = new List<Vector3>();
        }
        static readonly Dictionary<string, ShellSet> shellSets = new Dictionary<string, ShellSet>();
        static readonly Color ShellConcrete = new Color(0.56f, 0.54f, 0.5f);
        static MaterialPropertyBlock shellMpb;

        /// <summary>Frees a mesh generated for one building together with its object.</summary>
        public class ShellOwnedMesh : MonoBehaviour { public Mesh mesh; void OnDestroy() { if (mesh) Destroy(mesh); } }

        /// <summary>The ruin's walls and floor as children of the ruin object. `src` = the building's first material (fallback look).</summary>
        void AttachShell(BuildingRuins.Set set, Transform mt, Material src)
        {
            if (!RuinShell || !ruinGo || obj == null) return;
            var s = ShellFor(Key(obj.Type), set, Mathf.Abs(mt.lossyScale.x));
            if (s == null) return;
            bool lib = LibReady();
            var outer = lib ? ConcreteChunkLib.Outer : ShellFallback(src);
            var broken = lib ? ConcreteChunkLib.Fracture : outer;
            if (!outer || !broken) return;
            // the library's materials are white and take their tone from the property block: the level's concrete, as the debris chunks
            Color cOuter = lib ? ConcreteChunkLib.LevelOuter : ShellConcrete, cBroken = lib ? ConcreteChunkLib.LevelFracture : ShellConcrete, soot = ConcreteChunkLib.ScorchTone;
            if (s.walls)
            {
                var wr = ShellChild("walls", s.walls, new[] { broken, outer }, true);
                ShellPaint(wr, lib, Color.Lerp(cBroken, soot, RuinDetail ? RuinTopSoot : ShellTopSoot), Color.Lerp(cOuter, soot, RuinDetail ? RuinInnerSoot : ShellInnerSoot));
                if (RuinDetail) { RuinSetMap(wr, 0, RuinTex(true)); RuinSetMap(wr, 1, RuinTex(false)); }   // broken tops: aggregate; inner faces: slab concrete
            }
            var floor = RuinFloorFor(s, out var site);                                                // the rubble floor, or the plain draped grid (BuildingBreaker.RuinFloor.cs)
            if (floor)
            {
                var r = ShellChild("floor", floor, new[] { ShellFloorMat(outer) }, false);
                r.gameObject.AddComponent<ShellOwnedMesh>().mesh = floor;
                var cFloor = Color.Lerp(cOuter, soot, site != null ? RuinFloorSoot : ShellFloorSoot); ShellPaint(r, lib, cFloor, cFloor);
                if (site != null) { RuinSetMap(r, 0, RuinTex(false)); AttachRubble(site, lib, src); }
            }
        }

        /// <summary>The floor's material: a copy drawn after the ground marks (track marks, scorch, grime: queues 2840-2851). Those are drawn
        /// with a depth offset toward the camera, which beat a floor that lies a few cm over the ground from the normal view height up: a
        /// scorch mark showed through it in patches that changed with the zoom.</summary>
        public const int ShellFloorQueue = 2860;
        static readonly Dictionary<Material, Material> shellFloorMats = new Dictionary<Material, Material>();
        static Material ShellFloorMat(Material src)
        {
            if (!src) return null;
            if (!shellFloorMats.TryGetValue(src, out var m) || !m) { m = new Material(src) { name = src.name + "_floor", renderQueue = ShellFloorQueue }; shellFloorMats[src] = m; }
            return m;
        }

        MeshRenderer ShellChild(string childName, Mesh m, Material[] mats, bool shadows)
        {
            var go = new GameObject(childName) { layer = ruinGo.layer };
            go.transform.SetParent(ruinGo.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterials = mats;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off; r.receiveShadows = true;
            return r;
        }

        /// <summary>Scorched concrete: a library material keeps its texture and takes the tone (c0 = slot 0, c1 = the rest); the fallback is the flat tone.</summary>
        static void ShellPaint(MeshRenderer r, bool lib, Color c0, Color c1)
        {
            shellMpb ??= new MaterialPropertyBlock();
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var c = i == 0 ? c0 : c1;
                shellMpb.Clear();
                if (!lib) shellMpb.SetTexture(BaseMapId, Texture2D.whiteTexture);
                shellMpb.SetColor(BaseColorIdV1, new Color(c.r, c.g, c.b, 1f));
                r.SetPropertyBlock(shellMpb, i);
            }
        }

        /// <summary>Without the chunk library: the building's own shader, no team band, flat-shaded (the same material ChunkLook makes).</summary>
        static Material ShellFallback(Material src)
        {
            if (!src) return null;
            if (!chunkMats.TryGetValue(src, out var dm))
            {
                dm = new Material(src) { name = src.name + "_chunk" };
                if (dm.HasProperty("_TeamMode")) dm.SetFloat("_TeamMode", 0f);
                if (dm.HasProperty("_Accent")) dm.SetFloat("_Accent", 0f);
                if (dm.HasProperty("_FlatShade")) dm.SetFloat("_FlatShade", 1f);
                if (dm.HasProperty("_Smoothness")) dm.SetFloat("_Smoothness", 0.28f);
                chunkMats[src] = dm;
            }
            return dm;
        }

        static ShellSet ShellFor(string key, BuildingRuins.Set set, float metres)
        {
            if (shellSets.TryGetValue(key, out var s)) return s;
            s = null;
            if (set != null && set.ruin && set.ruin.isReadable)
            {
                try { s = BuildShell(set, metres); }
                catch (System.Exception e) { Debug.LogWarning($"[OpenZ] ruin shell {key}: {e.Message}"); s = null; }
            }
            shellSets[key] = s;
            if (s != null)
            {
                LastShellStats = $"{key}: {s.edges} open edges, walls {(s.walls ? s.walls.triangles.Length / 3 : 0)} tris, floor {s.cells} of {ShellGrid * ShellGrid} cells";
                Debug.Log("[OpenZ] ruin shell " + LastShellStats);
            }
            return s;
        }

        /// <summary>The front triangles of a BuildingRuins mesh (Make puts them first in every submesh, their flipped copies after them).</summary>
        static List<int> ShellFront(Mesh m)
        {
            var tl = new List<int>();
            for (int sm = 0; sm < m.subMeshCount; sm++) { var t = m.GetTriangles(sm); int half = (t.Length / 3 / 2) * 3; for (int k = 0; k < half; k++) tl.Add(t[k]); }
            return tl;
        }

        static long ShellEdge(int a, int c) => a == c ? -1L : a < c ? ((long)a << 32) | (uint)c : ((long)c << 32) | (uint)a;

        static ShellSet BuildShell(BuildingRuins.Set set, float metres)
        {
            var ruin = set.ruin; var v = ruin.vertices; var nrm = ruin.normals; var tl = ShellFront(ruin); var b = ruin.bounds;
            float size = Mathf.Max(b.size.x, b.size.z);
            if (size <= 0f || tl.Count < 3) return null;
            bool hasN = nrm != null && nrm.Length == v.Length;
            int N = ShellGrid;
            var s = new ShellSet { box = b };

            // weld by position (the model's vertices are split at UV seams and hard edges), then count the faces on every edge
            float q = 1f / (size * 2e-4f);
            var weld = new Dictionary<Vector3Int, int>(); var wid = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(v[i].x * q), Mathf.RoundToInt(v[i].y * q), Mathf.RoundToInt(v[i].z * q));
                if (!weld.TryGetValue(key, out int id)) { id = weld.Count; weld[key] = id; }
                wid[i] = id;
            }
            var use = new Dictionary<long, int>();
            for (int i = 0; i + 2 < tl.Count; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    long k = ShellEdge(wid[tl[i + e]], wid[tl[i + (e + 1) % 3]]); if (k < 0) continue;
                    use.TryGetValue(k, out int n); use[k] = n + 1;
                }

            // the triangles by ground cell (for the thickness rays)
            var cells = new List<int>[N * N];
            float cx = N / Mathf.Max(1e-6f, b.size.x), cz = N / Mathf.Max(1e-6f, b.size.z);
            for (int i = 0; i + 2 < tl.Count; i += 3)
            {
                Vector3 p0 = v[tl[i]], p1 = v[tl[i + 1]], p2 = v[tl[i + 2]];
                int x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x)) - b.min.x) * cx), 0, N - 1), x1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x)) - b.min.x) * cx), 0, N - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(p0.z, Mathf.Min(p1.z, p2.z)) - b.min.z) * cz), 0, N - 1), z1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(p0.z, Mathf.Max(p1.z, p2.z)) - b.min.z) * cz), 0, N - 1);
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) (cells[z * N + x] ??= new List<int>()).Add(i);
            }

            // walls: the broken top / step face on every open edge, the inner face below it
            var pv = new List<Vector3>(); var pn = new List<Vector3>(); var pu = new List<Vector2>(); var pc = new List<Color32>();
            var caps = new List<int>(); var inner = new List<int>();
            float thick = size * ShellWallThick, reach = thick * 2.5f, baseY = b.min.y, rimY = b.min.y + b.size.y * 0.04f, uvK = Mathf.Max(1e-4f, metres) * 0.5f;
            // inner faces: the slab texture's scale, the soot band over the floor (1.8 m); rebar only on tops higher than 0.6 m
            float uvW = Mathf.Max(1e-4f, metres) / Mathf.Max(0.5f, RuinWallTexMetres), sootY = baseY + 1.8f / Mathf.Max(1e-4f, metres), stubY = baseY + 0.6f / Mathf.Max(1e-4f, metres);
            for (int i = 0; i + 2 < tl.Count && s.edges < 30000; i += 3)
            {
                int open = 0;
                for (int e = 0; e < 3; e++) { long k = ShellEdge(wid[tl[i + e]], wid[tl[i + (e + 1) % 3]]); if (k >= 0 && use[k] == 1) open |= 1 << e; }
                if (open == 0) continue;
                Vector3 p0 = v[tl[i]], p1 = v[tl[i + 1]], p2 = v[tl[i + 2]];
                var fn = hasN ? nrm[tl[i]] + nrm[tl[i + 1]] + nrm[tl[i + 2]] : Vector3.Cross(p1 - p0, p2 - p0);
                var dir = new Vector3(-fn.x, 0f, -fn.z); float hl = dir.magnitude;
                if (hl < 1e-9f || hl < 0.5f * fn.magnitude) continue;                                   // ledges, floors, roofs: no wall behind them
                dir /= hl;
                // half of the model's own thickness where it has one (a double wall, a pillar, a small detail), else the full wall
                float room = ShellRay(v, tl, cells, b, (p0 + p1 + p2) / 3f, dir, reach, i);
                var inw = dir * (room < reach ? Mathf.Min(thick, room * 0.5f) : thick);
                for (int e = 0; e < 3; e++)
                {
                    if ((open & (1 << e)) == 0) continue;
                    Vector3 a = v[tl[i + e]], c = v[tl[i + (e + 1) % 3]]; float top = Mathf.Max(a.y, c.y);
                    if (top < rimY) continue;                                                             // the model's own open base
                    Vector3 a2 = a + inw, c2 = c + inw;
                    // a ragged inner edge (the same drop wherever two edges share a corner), never below half the wall's height there
                    a2.y = Mathf.Max(a2.y - thick * RuinCapRag * RuinHash(wid[tl[i + e]], 3, 71), baseY + (a.y - baseY) * 0.5f);
                    c2.y = Mathf.Max(c2.y - thick * RuinCapRag * RuinHash(wid[tl[i + (e + 1) % 3]], 3, 71), baseY + (c.y - baseY) * 0.5f);
                    float shade = 0.8f + 0.2f * RuinHash(i, e, 73);
                    ShellQuad(pv, pn, pu, caps, a, c, c2, a2, uvK);
                    RuinTone(pc, pv, shade, shade, 0f, 1f);                                              // broken top: every face its own tone
                    bool hangs = v[tl[i + (e + 2) % 3]].y < top - 1e-5f * size;                          // the wall is below this edge (not a door or window head)
                    float dx = a.x - c.x, dz = a.z - c.z;
                    if (hangs && dx * dx + dz * dz > 1e-10f * size * size)
                    {
                        ShellQuad(pv, pn, pu, inner, a2, c2, new Vector3(c2.x, baseY, c2.z), new Vector3(a2.x, baseY, a2.z), uvW);
                        RuinTone(pc, pv, RuinWallFootTone * (0.86f + 0.14f * shade), 0.86f + 0.14f * shade, baseY, sootY);   // inner face: soot at the foot
                        if (top > stubY && inw.sqrMagnitude > thick * thick * 0.16f) { s.stubPos.Add((a + c + a2 + c2) * 0.25f); s.stubIn.Add(dir); }
                    }
                    s.edges++;
                }
            }
            if (pv.Count > 0)
            {
                var m = new Mesh { name = ruin.name + "_shell" };
                if (pv.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(pv); m.SetNormals(pn); m.SetUVs(0, pu);
                if (pc.Count == pv.Count) m.SetColors(pc);
                m.subMeshCount = 2; m.SetTriangles(caps, 0, false); m.SetTriangles(inner, 1, false);
                m.RecalculateBounds(); m.RecalculateTangents();
                s.walls = m;
            }

            // floor mask: the cells the intact model covers from above (the ruin and the big parts together are the whole model),
            // pinholes closed, then one cell in from the outline, so the floor's edge stays under the walls
            var cover = new bool[N * N];
            ShellRaster(v, tl, b, cover);
            foreach (var pm in set.parts) if (pm && pm.isReadable) ShellRaster(pm.vertices, ShellFront(pm), b, cover);
            bool At(bool[] g, int gx, int gz) => gx >= 0 && gz >= 0 && gx < N && gz < N && g[gz * N + gx];
            var filled = new bool[N * N];
            for (int z = 0; z < N; z++) for (int x = 0; x < N; x++)
                filled[z * N + x] = cover[z * N + x] || (At(cover, x - 1, z) ? 1 : 0) + (At(cover, x + 1, z) ? 1 : 0) + (At(cover, x, z - 1) ? 1 : 0) + (At(cover, x, z + 1) ? 1 : 0) >= 3;
            s.mask = new bool[N * N];
            for (int z = 0; z < N; z++) for (int x = 0; x < N; x++)
            {
                bool inside = filled[z * N + x] && At(filled, x - 1, z) && At(filled, x + 1, z) && At(filled, x, z - 1) && At(filled, x, z + 1);
                s.mask[z * N + x] = inside; if (inside) s.cells++;
            }
            RuinField(s, cells, v, tl, metres);                                                         // free floor cells and their distance to the standing remains (BuildingBreaker.RuinFloor.cs)
            return s;
        }

        /// <summary>Distance along the level ray o + d t to the nearest other front triangle, or `max` when none is closer.</summary>
        static float ShellRay(Vector3[] v, List<int> tl, List<int>[] cells, Bounds b, Vector3 o, Vector3 d, float max, int skip)
        {
            int N = ShellGrid; float cx = N / Mathf.Max(1e-6f, b.size.x), cz = N / Mathf.Max(1e-6f, b.size.z), best = max;
            var end = o + d * max;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(o.x, end.x) - b.min.x) * cx), 0, N - 1), x1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(o.x, end.x) - b.min.x) * cx), 0, N - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(o.z, end.z) - b.min.z) * cz), 0, N - 1), z1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(o.z, end.z) - b.min.z) * cz), 0, N - 1);
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
            {
                var l = cells[z * N + x]; if (l == null) continue;
                for (int j = 0; j < l.Count; j++)
                {
                    int i = l[j]; if (i == skip) continue;
                    Vector3 a = v[tl[i]], p = v[tl[i + 1]], c = v[tl[i + 2]];
                    if ((a.y > o.y && p.y > o.y && c.y > o.y) || (a.y < o.y && p.y < o.y && c.y < o.y)) continue;      // the ray is level
                    Vector3 e1 = p - a, e2 = c - a, h = Vector3.Cross(d, e2); float det = Vector3.Dot(e1, h);
                    if (det > -1e-12f && det < 1e-12f) continue;
                    float f = 1f / det; var sv = o - a; float u = f * Vector3.Dot(sv, h); if (u < 0f || u > 1f) continue;
                    var qv = Vector3.Cross(sv, e1); float w = f * Vector3.Dot(d, qv); if (w < 0f || u + w > 1f) continue;
                    float t = f * Vector3.Dot(e2, qv);
                    if (t > max * 0.02f && t < best) best = t;
                }
            }
            return best;
        }

        /// <summary>A two-sided flat quad a-b-c-d (own vertices per side), planar UVs by its facing.</summary>
        static void ShellQuad(List<Vector3> pv, List<Vector3> pn, List<Vector2> pu, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uvK)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-20f) n = Vector3.Cross(c - a, d - a);
            if (n.sqrMagnitude < 1e-20f) return;
            n.Normalize();
            for (int side = 0; side < 2; side++)
            {
                int o = pv.Count; var nn = side == 0 ? n : -n;
                pv.Add(a); pv.Add(b); pv.Add(c); pv.Add(d);
                for (int k = 0; k < 4; k++) pn.Add(nn);
                pu.Add(ShellUv(a, n, uvK)); pu.Add(ShellUv(b, n, uvK)); pu.Add(ShellUv(c, n, uvK)); pu.Add(ShellUv(d, n, uvK));
                if (side == 0) { tris.Add(o); tris.Add(o + 1); tris.Add(o + 2); tris.Add(o); tris.Add(o + 2); tris.Add(o + 3); }
                else { tris.Add(o); tris.Add(o + 2); tris.Add(o + 1); tris.Add(o); tris.Add(o + 3); tris.Add(o + 2); }
            }
        }

        static Vector2 ShellUv(Vector3 p, Vector3 n, float k) =>
            Mathf.Abs(n.y) > 0.7f ? new Vector2(p.x, p.z) * k : Mathf.Abs(n.x) > Mathf.Abs(n.z) ? new Vector2(p.z, p.y) * k : new Vector2(p.x, p.y) * k;

        /// <summary>Marks the grid cells whose centre lies under a front triangle (seen from above).</summary>
        static void ShellRaster(Vector3[] v, List<int> tl, Bounds b, bool[] cover)
        {
            int N = ShellGrid; float cx = N / Mathf.Max(1e-6f, b.size.x), cz = N / Mathf.Max(1e-6f, b.size.z);
            for (int i = 0; i + 2 < tl.Count; i += 3)
            {
                float ax = (v[tl[i]].x - b.min.x) * cx, az = (v[tl[i]].z - b.min.z) * cz, bx = (v[tl[i + 1]].x - b.min.x) * cx, bz = (v[tl[i + 1]].z - b.min.z) * cz;
                float qx = (v[tl[i + 2]].x - b.min.x) * cx, qz = (v[tl[i + 2]].z - b.min.z) * cz;
                float d = (bz - qz) * (ax - qx) + (qx - bx) * (az - qz);
                if (d > -1e-9f && d < 1e-9f) continue;                                                    // edge-on from above (a wall face)
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ax, Mathf.Min(bx, qx)))), x1 = Mathf.Min(N - 1, Mathf.FloorToInt(Mathf.Max(ax, Mathf.Max(bx, qx))));
                int z0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(az, Mathf.Min(bz, qz)))), z1 = Mathf.Min(N - 1, Mathf.FloorToInt(Mathf.Max(az, Mathf.Max(bz, qz))));
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                {
                    if (cover[z * N + x]) continue;
                    float px = x + 0.5f, pz = z + 0.5f;
                    float u = ((bz - qz) * (px - qx) + (qx - bx) * (pz - qz)) / d, w = ((qz - az) * (px - qx) + (ax - qx) * (pz - qz)) / d;
                    if (u >= 0f && w >= 0f && u + w <= 1f) cover[z * N + x] = true;
                }
            }
        }

        /// <summary>This building's floor: the masked cells as a grid draped on the terrain, never below the model's base.
        /// `rt` = the ruin object (its local space is the ruin's mesh space).</summary>
        static Mesh ShellFloor(ShellSet s, Transform rt)
        {
            if (s.cells == 0 || s.mask == null) return null;
            int N = ShellGrid, W = N + 1; var b = s.box; var game = ZGame.I;
            var idx = new int[W * W]; for (int i = 0; i < idx.Length; i++) idx[i] = -1;
            var pv = new List<Vector3>(); var pu = new List<Vector2>(); var tris = new List<int>();
            int Vert(int gx, int gz)
            {
                int k = gz * W + gx; if (idx[k] >= 0) return idx[k];
                var w = rt.TransformPoint(new Vector3(b.min.x + b.size.x * gx / N, b.min.y, b.min.z + b.size.z * gz / N));
                float gy = game ? game.GroundY(w.x, w.z) : w.y;
                w.y = Mathf.Max(w.y, gy) + ShellFloorLift;
                idx[k] = pv.Count; pv.Add(rt.InverseTransformPoint(w)); pu.Add(new Vector2(w.x, w.z) * 0.5f);
                return idx[k];
            }
            for (int z = 0; z < N; z++) for (int x = 0; x < N; x++)
            {
                if (!s.mask[z * N + x]) continue;
                int a = Vert(x, z), c = Vert(x + 1, z), d = Vert(x + 1, z + 1), e = Vert(x, z + 1);
                tris.Add(a); tris.Add(e); tris.Add(d); tris.Add(a); tris.Add(d); tris.Add(c);
            }
            if (tris.Count == 0) return null;
            var m = new Mesh { name = "ruin_floor" };
            m.SetVertices(pv); m.SetUVs(0, pu); m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            var nr = m.normals;
            if (nr.Length > 0 && rt.TransformVector(nr[0]).y < 0f)
            {   // a mirrored or turned-over model: the floor must face up
                for (int i = 0; i < tris.Count; i += 3) { int t = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = t; }
                m.SetTriangles(tris, 0); m.RecalculateNormals();
            }
            m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }
    }
}
