using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// The low block in the middle of a fort's ruin, broken up (on top of BuildingBreaker.RuinFloor.cs: same objects, same life time).
    /// The ruin is every model triangle under the cut height, so a part of the model that stands low inside the walls stays whole:
    /// in the fort a rack of parallel dark ribs under the gun deck (6 m x 10 m, 1.4 m high), which showed as a clean ribbed block
    /// in the middle of the rubble.
    /// <list type="bullet">
    /// <item><b>Found</b> once per building type on the ruin floor's grid: floor cells whose remains stand higher than the rubble
    /// rule (RuinSolidHeight) but lower than RuinCoreMaxHeight, in one group of at least RuinCoreMinArea that does not touch the
    /// outline (walls, towers and the gate porch are not it). Its parts are what is joined above the floor only: each rib is
    /// one part. The triangles of a long part are sliced along its length every RuinCoreStep or less (an edge is always halved,
    /// so two triangles that share an edge slice it at the same points: no gap opens), so a top edge has points to break at.</item>
    /// <item><b>Broken</b> per building: the ruin object gets its own ruin mesh in which every part has its own fate from the
    /// building's seed: knocked flat (under the rubble floor), a low stump, or left standing but lower. A part is shortened from
    /// both ends by its own amount, its top runs at a slope from one end to the other, it leans to one side, and along its top
    /// every point drops by its own amount, some of them deep (bites). Heights only go down and the feet stay where they
    /// are: nothing floats.</item>
    /// <item><b>Rubble and rods</b>: the floor cells that came free take the ruin floor's rubble (the free floor is worked out
    /// again for this building with the same rule), extra chunk library pieces are laid over and against what is left, and bent
    /// rods stand out of the broken ends, each with its own length, direction and bend. All of it is in the ruin's one rubble
    /// mesh: no new object, removed and freed with the ruin.</item>
    /// </list>
    /// Visual only: no colliders, nothing the rules read. Forts only unless RuinCoreAllBuildings is set.
    /// </summary>
    public partial class BuildingBreaker
    {
        public static bool RuinCoreBreak = true, RuinCoreAllBuildings = false;
        /// <summary>The two numbers to tune by eye. Damage 0-1: how much of the block is knocked down (0.45 of it flat, 0.45 of it
        /// to low stumps, the rest stands, up to 0.45 of it lower) and how ragged the tops are. Rubble: multiplier on the pieces
        /// and rods laid over it.</summary>
        public static float RuinCoreDamage = 0.7f, RuinCoreRubble = 1f;
        /// <summary>A block is lower than this over the model's base (m) and covers at least this floor area (m2).</summary>
        public static float RuinCoreMaxHeight = 2.5f, RuinCoreMinArea = 12f;
        /// <summary>A long part is sliced along its length into steps of at most this (m); how far a top point drops (share of the
        /// part's height, x damage); the share of top points that take a deep bite (x damage).</summary>
        public static float RuinCoreStep = 0.45f, RuinCoreRag = 0.3f, RuinCoreBite = 0.2f;
        /// <summary>Triangles the block's own rubble and rods may add to a ruin's rubble mesh (after the floor's own, which RuinTriCapFort caps).</summary>
        public static int RuinCoreTris = 6000;
        public static string LastRuinCoreStats = "";

        /// <summary>Per building type: the block's floor cells, and the ruin mesh with the block's parts sliced along their length.
        /// Only the mesh's FRONT half is kept (a ruin mesh holds every face twice, the back copies after the front ones:
        /// BuildingRuins.Make); a building's own mesh is built from it with the back copies added again.</summary>
        sealed class RuinCoreMap
        {
            public bool[] cell; public int cells, parts, slices; public float area, top;                // m2; the block's height over the base (mesh units)
            public Vector3[] v, n; public Vector2[] uv; public Vector4[] tan; public Color[] col;      // front vertices: the mesh's own, then the new ones (a channel the mesh lacks is null)
            public int[][] tri; public List<int> tl;                                                    // front triangles per submesh, and all of them in one list
            public int[] partOf; public Vector3[] lo, hi; public bool[] alongX, beam;                   // per vertex its part (-1 = none); per part its box (mesh space), its long axis, long enough to slice
        }

        /// <summary>Per building: what the rubble pass needs to know about its broken block.</summary>
        sealed class RuinCore
        {
            public string name; public bool[] cell; public float area;
            public int parts, flat, low, standing, freed, tris;
            public readonly List<Vector3> rodPos = new List<Vector3>(), rodDir = new List<Vector3>();   // broken ends (ruin mesh space) and the way out of them
        }

        static readonly Dictionary<string, RuinCoreMap> ruinCoreMaps = new Dictionary<string, RuinCoreMap>();
        RuinCore ruinCore;

        static int RuinCoreRoot(int[] parent, int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }

        static RuinCoreMap RuinCoreMapFor(string key, ShellSet s, Mesh ruin)
        {
            if (ruinCoreMaps.TryGetValue(key, out var map)) return map;
            map = null;
            try { map = RuinCoreBuild(s, ruin); }
            catch (System.Exception ex) { Debug.LogWarning($"[OpenZ] ruin core {key}: {ex.Message}"); map = null; }
            ruinCoreMaps[key] = map;
            if (map != null) Debug.Log($"[OpenZ] ruin core {key}: {map.cells} floor cells ({map.area:0.0} m2), {map.parts} parts, {map.top * s.metres:0.00} m high, sliced {map.slices} times: {map.tl.Count / 3} front triangles, {map.v.Length} front vertices");
            return map;
        }

        static RuinCoreMap RuinCoreBuild(ShellSet s, Mesh ruin)
        {
            int N = ShellGrid; var b = s.box; float k = Mathf.Max(1e-4f, s.metres), baseY = b.min.y;
            var all = ruin.vertices; var tl = ShellFront(ruin); int F = all.Length / 2;
            if (F == 0 || all.Length != F * 2 || tl.Count < 3 || b.size.x <= 0f || b.size.z <= 0f) return null;
            for (int i = 0; i < tl.Count; i++) if (tl[i] >= F) return null;                              // not the layout of a ruin mesh
            float cx = N / Mathf.Max(1e-6f, b.size.x), cz = N / Mathf.Max(1e-6f, b.size.z);
            // the highest point of the ruin over every floor cell (the same cells a triangle is listed in for the rubble rule)
            var top = new float[N * N];
            for (int i = 0; i < top.Length; i++) top[i] = baseY;
            for (int i = 0; i + 2 < tl.Count; i += 3)
            {
                Vector3 p0 = all[tl[i]], p1 = all[tl[i + 1]], p2 = all[tl[i + 2]]; float y = Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y));
                int x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x)) - b.min.x) * cx), 0, N - 1), x1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x)) - b.min.x) * cx), 0, N - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(p0.z, Mathf.Min(p1.z, p2.z)) - b.min.z) * cz), 0, N - 1), z1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(p0.z, Mathf.Max(p1.z, p2.z)) - b.min.z) * cz), 0, N - 1);
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) if (y > top[z * N + x]) top[z * N + x] = y;
            }
            // low standing remains inside the floor, in groups; a group at the outline or a small one is not a block
            float solidY = baseY + RuinSolidHeight / k, lowY = baseY + RuinCoreMaxHeight / k, minCells = RuinCoreMinArea / Mathf.Max(1e-6f, s.cellX * s.cellZ);
            var seen = new bool[N * N]; var cell = new bool[N * N]; var stack = new List<int>(); var members = new List<int>();
            int cells = 0; float topMax = 0f;
            for (int start = 0; start < N * N; start++)
            {
                if (seen[start] || !s.mask[start] || top[start] <= solidY || top[start] > lowY) continue;
                members.Clear(); stack.Clear(); stack.Add(start); seen[start] = true; bool edge = false;
                while (stack.Count > 0)
                {
                    int c = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1); members.Add(c);
                    int x = c % N, z = c / N;
                    for (int oz = -1; oz <= 1; oz++) for (int ox = -1; ox <= 1; ox++)
                    {
                        if (ox == 0 && oz == 0) continue;
                        int nx = x + ox, nz = z + oz;
                        if (nx < 0 || nz < 0 || nx >= N || nz >= N || !s.mask[nz * N + nx]) { edge = true; continue; }
                        int nc = nz * N + nx;
                        if (seen[nc] || top[nc] <= solidY || top[nc] > lowY) continue;
                        seen[nc] = true; stack.Add(nc);
                    }
                }
                if (edge || members.Count < minCells) continue;
                for (int m = 0; m < members.Count; m++) { int c = members[m]; cell[c] = true; cells++; topMax = Mathf.Max(topMax, top[c] - baseY); }
            }
            if (cells == 0) return null;
            // its parts: the block's triangles joined by the vertices that stand over the floor (the floor itself joins nothing)
            float size = Mathf.Max(b.size.x, b.size.z), q = 1f / (size * 2e-4f), highY = baseY + 0.05f / k;
            var weld = new Dictionary<Vector3Int, int>(); var wid = new int[F];
            for (int i = 0; i < F; i++)
            {
                var wk = new Vector3Int(Mathf.RoundToInt(all[i].x * q), Mathf.RoundToInt(all[i].y * q), Mathf.RoundToInt(all[i].z * q));
                if (!weld.TryGetValue(wk, out int id)) { id = weld.Count; weld[wk] = id; }
                wid[i] = id;
            }
            var parent = new int[weld.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = -1;
            for (int i = 0; i + 2 < tl.Count; i += 3)
            {
                var c = (all[tl[i]] + all[tl[i + 1]] + all[tl[i + 2]]) / 3f;
                int gx = Mathf.Clamp(Mathf.FloorToInt((c.x - b.min.x) * cx), 0, N - 1), gz = Mathf.Clamp(Mathf.FloorToInt((c.z - b.min.z) * cz), 0, N - 1);
                if (!cell[gz * N + gx]) continue;
                int first = -1;
                for (int e = 0; e < 3; e++)
                {
                    int vi = tl[i + e]; if (all[vi].y <= highY) continue;
                    int w = wid[vi]; if (parent[w] < 0) parent[w] = w;
                    if (first < 0) { first = w; continue; }
                    int ra = RuinCoreRoot(parent, first), rb = RuinCoreRoot(parent, w);
                    if (ra != rb) parent[rb] = ra;
                }
            }
            var index = new Dictionary<int, int>(); var part = new List<int>(F + 8192); var lo = new List<Vector3>(); var hi = new List<Vector3>();
            for (int i = 0; i < F; i++)
            {
                int w = wid[i];
                if (parent[w] < 0) { part.Add(-1); continue; }
                int r = RuinCoreRoot(parent, w);
                if (!index.TryGetValue(r, out int p)) { p = lo.Count; index[r] = p; lo.Add(all[i]); hi.Add(all[i]); }
                part.Add(p); lo[p] = Vector3.Min(lo[p], all[i]); hi[p] = Vector3.Max(hi[p], all[i]);
            }
            int np = lo.Count; if (np == 0) return null;
            var alongX = new bool[np]; var beam = new bool[np];
            for (int p = 0; p < np; p++)
            {
                var ext = hi[p] - lo[p]; alongX[p] = ext.x >= ext.z;
                float len = alongX[p] ? ext.x : ext.z;
                beam[p] = len > 2f * Mathf.Min(ext.x, ext.z) && len * k > 1.5f;                           // long enough to be shortened, to slope and to be sliced
            }
            // the front half of the mesh, the block's long parts sliced along their length (new points at edge middles)
            Vector3[] srcN = ruin.normals; Vector2[] srcU = ruin.uv; Vector4[] srcT = ruin.tangents; Color[] srcC = ruin.colors;
            bool hasN = srcN != null && srcN.Length == all.Length, hasU = srcU != null && srcU.Length == all.Length, hasT = srcT != null && srcT.Length == all.Length, hasC = srcC != null && srcC.Length == all.Length;
            var v = new List<Vector3>(F + 8192); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tan = new List<Vector4>(); var col = new List<Color>();
            for (int i = 0; i < F; i++)
            {
                v.Add(all[i]);
                if (hasN) n.Add(srcN[i]); if (hasU) uv.Add(srcU[i]); if (hasT) tan.Add(srcT[i]); if (hasC) col.Add(srcC[i]);
            }
            var mids = new Dictionary<long, int>(); int limit = F + 24000, slices = 0;
            int Mid(int i, int j, int p)
            {
                long key = i < j ? ((long)i << 32) | (uint)j : ((long)j << 32) | (uint)i;
                if (mids.TryGetValue(key, out int h)) return h;
                h = v.Count; var pos = (v[i] + v[j]) * 0.5f; v.Add(pos);
                if (hasN) { var nn = n[i] + n[j]; n.Add(nn.sqrMagnitude > 1e-12f ? nn.normalized : n[i]); }
                if (hasU) uv.Add((uv[i] + uv[j]) * 0.5f);
                if (hasT) tan.Add((tan[i] + tan[j]) * 0.5f);
                if (hasC) col.Add((col[i] + col[j]) * 0.5f);
                part.Add(pos.y > highY ? p : -1);
                mids[key] = h; return h;
            }
            float step = Mathf.Max(0.1f, RuinCoreStep) / k; var tri = new int[ruin.subMeshCount][]; var flat = new List<int>(tl.Count * 2);
            for (int sm = 0; sm < ruin.subMeshCount; sm++)
            {
                var t = ruin.GetTriangles(sm); int half = (t.Length / 3 / 2) * 3; var keepT = new List<int>(half * 2);
                for (int i = 0; i + 2 < half; i += 3)
                {
                    int a = t[i], c1 = t[i + 1], c2 = t[i + 2];
                    int p = part[a] >= 0 ? part[a] : part[c1] >= 0 ? part[c1] : part[c2];                // every triangle that touches a part is sliced with it: both sides of an edge that will move
                    if (p < 0 || !beam[p]) { keepT.Add(a); keepT.Add(c1); keepT.Add(c2); continue; }
                    bool ax = alongX[p];
                    stack.Clear(); stack.Add(a); stack.Add(c1); stack.Add(c2);
                    while (stack.Count > 0)
                    {
                        int top3 = stack.Count, tc = stack[top3 - 1], tb = stack[top3 - 2], ta = stack[top3 - 3]; stack.RemoveRange(top3 - 3, 3);
                        float ua = ax ? v[ta].x : v[ta].z, ub = ax ? v[tb].x : v[tb].z, uc = ax ? v[tc].x : v[tc].z;
                        float ab = Mathf.Abs(ua - ub), bc = Mathf.Abs(ub - uc), ca = Mathf.Abs(uc - ua), longest = Mathf.Max(ab, Mathf.Max(bc, ca));
                        if (longest <= step || v.Count >= limit) { keepT.Add(ta); keepT.Add(tb); keepT.Add(tc); continue; }
                        slices++;
                        if (longest == ab) { int h = Mid(ta, tb, p); stack.Add(ta); stack.Add(h); stack.Add(tc); stack.Add(h); stack.Add(tb); stack.Add(tc); }
                        else if (longest == bc) { int h = Mid(tb, tc, p); stack.Add(ta); stack.Add(tb); stack.Add(h); stack.Add(ta); stack.Add(h); stack.Add(tc); }
                        else { int h = Mid(tc, ta, p); stack.Add(ta); stack.Add(tb); stack.Add(h); stack.Add(h); stack.Add(tb); stack.Add(tc); }
                    }
                }
                tri[sm] = keepT.ToArray(); flat.AddRange(keepT);
            }
            return new RuinCoreMap
            {
                cell = cell, cells = cells, parts = np, slices = slices, area = cells * s.cellX * s.cellZ, top = topMax,
                v = v.ToArray(), n = hasN ? n.ToArray() : null, uv = hasU ? uv.ToArray() : null, tan = hasT ? tan.ToArray() : null, col = hasC ? col.ToArray() : null,
                tri = tri, tl = flat, partOf = part.ToArray(), lo = lo.ToArray(), hi = hi.ToArray(), alongX = alongX, beam = beam
            };
        }

        /// <summary>The floor layout's input for this building. With a low inner block (forts): breaks the block in this building's
        /// own ruin mesh and returns the free floor worked out again on it; else, or when anything fails, `s` as it is and the
        /// ruin mesh untouched.</summary>
        ShellSet RuinCoreSet(ShellSet s)
        {
            ruinCore = null;
            if (!RuinCoreBreak || RuinCoreDamage <= 0.001f || s == null || s.dist == null || s.mask == null || obj == null || !ruinGo) return s;
            if (!obj.IsFort && !RuinCoreAllBuildings) return s;
            try
            {
                var mf = ruinGo.GetComponent<MeshFilter>(); var ruin = mf ? mf.sharedMesh : null;
                if (!ruin || !ruin.isReadable) return s;
                var map = RuinCoreMapFor(Key(obj.Type), s, ruin);
                if (map == null || map.parts == 0 || map.tri.Length != ruin.subMeshCount) return s;
                int N = ShellGrid, np = map.parts, F = map.v.Length; var b = s.box;
                float k = Mathf.Max(1e-4f, s.metres), baseY = b.min.y, dmg = Mathf.Clamp01(RuinCoreDamage);
                var rnd = new System.Random(obj.Id * 9973 + 39 + 303);
                // which parts go down: ranked by a roll of their own mixed with a slow noise, so they fall in patches
                var roll = new float[np]; var order = new int[np];
                for (int p = 0; p < np; p++)
                {
                    var c = (map.lo[p] + map.hi[p]) * 0.5f;
                    roll[p] = 0.65f * (float)rnd.NextDouble() + 0.35f * RuinNoise(c.x * k * 0.3f + 13.1f, c.z * k * 0.3f + 7.7f, 65536, obj.Id & 1023);
                    order[p] = p;
                }
                System.Array.Sort(roll, order);
                int nFlat = Mathf.RoundToInt(0.45f * dmg * np), nLow = Mathf.RoundToInt(0.45f * dmg * np);
                if (np >= 3) { nFlat = Mathf.Max(1, nFlat); nLow = Mathf.Max(1, nLow); }
                nFlat = Mathf.Min(nFlat, np); nLow = Mathf.Min(nLow, np - nFlat);
                var core = new RuinCore { name = name, cell = map.cell, area = map.area, parts = np };
                var keepA = new float[np]; var keepB = new float[np]; var cutA = new float[np]; var cutB = new float[np]; var lean = new float[np];
                for (int r = 0; r < np; r++)
                {
                    int p = order[r], fate = r < nFlat ? 0 : r < nFlat + nLow ? 1 : 2;                   // 0 flat, 1 a low stump, 2 standing
                    Vector3 lo = map.lo[p], hi = map.hi[p];
                    bool ax = map.alongX[p], beam = map.beam[p];
                    float a0 = ax ? lo.x : lo.z, a1 = ax ? hi.x : hi.z, len = a1 - a0, hp = hi.y - baseY;
                    float ka = 0f, kb = 0f, ca = 0f, cb = 0f, ln = 0f;
                    if (fate == 0) core.flat++;
                    else if (fate == 1)
                    {
                        ka = RuinRange(rnd, 0.12f, 0.42f); kb = RuinRange(rnd, 0f, 0.3f);
                        ca = RuinRange(rnd, 0f, 0.3f); cb = RuinRange(rnd, 0f, 0.3f); ln = Mathf.Tan(RuinRange(rnd, -30f, 30f) * Mathf.Deg2Rad); core.low++;
                    }
                    else
                    {
                        ka = RuinRange(rnd, 1f - 0.45f * dmg, 1f); kb = RuinRange(rnd, 1f - 0.45f * dmg, 1f);
                        ca = RuinRange(rnd, 0f, 0.22f) * dmg; cb = RuinRange(rnd, 0f, 0.22f) * dmg; ln = Mathf.Tan(RuinRange(rnd, -13f, 13f) * Mathf.Deg2Rad); core.standing++;
                    }
                    if (rnd.NextDouble() < 0.5) { float t = ka; ka = kb; kb = t; }
                    if (!beam) { ca = 0f; cb = 0f; kb = ka; }
                    keepA[p] = ka; keepB[p] = kb; cutA[p] = ca; cutB[p] = cb; lean[p] = ln;
                    if (fate == 0 || !beam) continue;
                    // rod anchors: in the two broken ends (not an end that lies in the rubble), on a stump also one on its top
                    float mid = ax ? (lo.z + hi.z) * 0.5f : (lo.x + hi.x) * 0.5f;
                    for (int end = 0; end < 3; end++)
                    {
                        if (end == 2 && fate != 1) break;
                        float t = end == 0 ? 0f : end == 1 ? 1f : RuinRange(rnd, 0.25f, 0.75f);
                        float u = Mathf.Lerp(a0 + len * ca, a1 - len * cb, t), h = hp * Mathf.Lerp(ka, kb, t) * RuinRange(rnd, 0.25f, 0.5f);   // in the lower half: a ragged top does not leave it in the air
                        if (h * k < 0.08f) continue;
                        float w = mid + ln * h;
                        core.rodPos.Add(ax ? new Vector3(u, baseY + h, w) : new Vector3(w, baseY + h, u));
                        core.rodDir.Add(end == 2 ? Vector3.up : ax ? new Vector3(end == 0 ? -1f : 1f, 0f, 0f) : new Vector3(0f, 0f, end == 0 ? -1f : 1f));
                    }
                }
                // the building's own ruin vertices: every part shortened, lowered along its slope, its top ragged and bitten, leaning
                var v = (Vector3[])map.v.Clone();
                float q = 1f / (Mathf.Max(b.size.x, b.size.z) * 2e-4f);
                for (int i = 0; i < F; i++)
                {
                    int p = map.partOf[i]; if (p < 0) continue;
                    var pt = v[i]; Vector3 lo = map.lo[p], hi = map.hi[p]; bool ax = map.alongX[p];
                    float a0 = ax ? lo.x : lo.z, a1 = ax ? hi.x : hi.z, len = Mathf.Max(1e-6f, a1 - a0);
                    float uA = a0 + len * cutA[p], uB = a1 - len * cutB[p], u = Mathf.Clamp(ax ? pt.x : pt.z, uA, uB);
                    float keep = Mathf.Lerp(keepA[p], keepB[p], Mathf.Clamp01((u - uA) / Mathf.Max(1e-6f, uB - uA)));
                    if (keep > 0f)
                    {   // the same drop for every vertex at this spot of the plan (the two faces of the mesh, a seam), another one a step on
                        int qx = Mathf.RoundToInt(pt.x * q), qz = Mathf.RoundToInt(pt.z * q);
                        keep -= RuinCoreRag * dmg * RuinHash(qx, qz, 97);
                        if (RuinHash(qx, qz, 131) < RuinCoreBite * dmg) keep -= 0.3f + 0.35f * RuinHash(qx, qz, 137);
                        if (keep < 0f) keep = 0f;
                    }
                    float h = Mathf.Min(pt.y - baseY, (hi.y - baseY) * keep), w = (ax ? pt.z : pt.x) + lean[p] * h;
                    v[i] = ax ? new Vector3(u, baseY + h, w) : new Vector3(w, baseY + h, u);
                }
                // this building's free floor: the rubble rule again, on its own ruin (what was knocked down takes rubble now)
                var tl = map.tl; var cellsNow = new List<int>[N * N];
                float cx = N / Mathf.Max(1e-6f, b.size.x), cz = N / Mathf.Max(1e-6f, b.size.z);
                for (int i = 0; i + 2 < tl.Count; i += 3)
                {
                    Vector3 p0 = v[tl[i]], p1 = v[tl[i + 1]], p2 = v[tl[i + 2]];
                    int x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x)) - b.min.x) * cx), 0, N - 1), x1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x)) - b.min.x) * cx), 0, N - 1);
                    int z0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(p0.z, Mathf.Min(p1.z, p2.z)) - b.min.z) * cz), 0, N - 1), z1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(p0.z, Mathf.Max(p1.z, p2.z)) - b.min.z) * cz), 0, N - 1);
                    for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) (cellsNow[z * N + x] ??= new List<int>()).Add(i);
                }
                var mine = new ShellSet { walls = s.walls, mask = s.mask, box = s.box, edges = s.edges, cells = s.cells };
                mine.stubPos.AddRange(s.stubPos); mine.stubIn.AddRange(s.stubIn);
                RuinField(mine, cellsNow, v, tl, s.metres);
                if (mine.dist == null) return s;
                // only now the ruin object changes: its own mesh (front faces, then their back copies as in every ruin mesh), freed with it
                var pv = new Vector3[F * 2];
                for (int i = 0; i < F; i++) { pv[i] = v[i]; pv[i + F] = v[i]; }
                var m = new Mesh { name = ruin.name + "_core" };
                if (pv.Length > 65000) m.indexFormat = IndexFormat.UInt32;
                m.vertices = pv;
                if (map.n != null) { var a = new Vector3[F * 2]; for (int i = 0; i < F; i++) { a[i] = map.n[i]; a[i + F] = -map.n[i]; } m.normals = a; }
                if (map.uv != null) { var a = new Vector2[F * 2]; for (int i = 0; i < F; i++) { a[i] = map.uv[i]; a[i + F] = map.uv[i]; } m.uv = a; }
                if (map.tan != null) { var a = new Vector4[F * 2]; for (int i = 0; i < F; i++) { var t = map.tan[i]; a[i] = t; a[i + F] = new Vector4(-t.x, -t.y, -t.z, t.w); } m.tangents = a; }
                if (map.col != null) { var a = new Color[F * 2]; for (int i = 0; i < F; i++) { a[i] = map.col[i]; a[i + F] = map.col[i]; } m.colors = a; }
                m.subMeshCount = map.tri.Length;
                for (int sm = 0; sm < map.tri.Length; sm++)
                {
                    var t = map.tri[sm]; var two = new int[t.Length * 2];
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        two[i] = t[i]; two[i + 1] = t[i + 1]; two[i + 2] = t[i + 2];
                        int o = t.Length + i; two[o] = t[i] + F; two[o + 1] = t[i + 2] + F; two[o + 2] = t[i + 1] + F;
                    }
                    m.SetTriangles(two, sm, false); core.tris += two.Length / 3;
                }
                m.RecalculateBounds();
                mf.sharedMesh = m; ruinGo.AddComponent<ShellOwnedMesh>().mesh = m;
                core.freed = mine.free - s.free;
                ruinCore = core;
                return mine;
            }
            catch (System.Exception ex) { Debug.LogWarning($"[OpenZ] ruin core {name}: {ex.Message}"); ruinCore = null; return s; }
        }

        sealed partial class RuinSite
        {
            /// <summary>This building's broken inner block (null when it has none).</summary>
            public RuinCore core;
            int corePieces, coreRods;

            /// <summary>Rubble over and against the broken inner block and bent rods out of its broken ends, after the floor's own
            /// rubble, within RuinCoreTris triangles of its own. If it fails, the floor's own rubble stays as it is.</summary>
            void CoreRubble(RuinBuf buf, bool lib)
            {
                if (core == null) return;
                try { CoreLay(buf, lib); }
                catch (System.Exception ex) { Debug.LogWarning($"[OpenZ] ruin core rubble {core.name}: {ex.Message}"); }
            }

            void CoreLay(RuinBuf buf, bool lib)
            {
                var rnd = new System.Random(seed + 303);
                float mul = Mathf.Max(0f, RuinCoreRubble); int nn = N * N, start = buf.Tris, top = start + Mathf.RoundToInt(RuinCoreTris * Mathf.Min(mul, 2f));
                if (mul > 0f)
                {
                    // rods out of the broken ends first (they are few triangles): about two of three anchors, in a shuffled order, every rod its own way
                    int have = core.rodPos.Count, want = Mathf.Min(have, Mathf.RoundToInt(have * 0.7f * Mathf.Min(mul, 1.4f)));
                    var pick = new int[have]; var pts = new List<Vector3>();
                    for (int i = 0; i < have; i++) pick[i] = i;
                    for (int i = have - 1; i > 0; i--) { int j = rnd.Next(i + 1), t = pick[i]; pick[i] = pick[j]; pick[j] = t; }
                    for (int n = 0; n < want && buf.Tris < top - 40; n++)
                    {
                        var p = rt.TransformPoint(core.rodPos[pick[n]]); var o = rt.TransformDirection(core.rodDir[pick[n]]).normalized;
                        float len = RuinRange(rnd, 0.35f, 1.5f), straight = RuinRange(rnd, 0.35f, 0.75f), kink = RuinRange(rnd, 20f, 85f) * Mathf.Deg2Rad;
                        var side = Quaternion.AngleAxis(RuinRange(rnd, 0f, 360f), Vector3.up) * Vector3.forward; Vector3 d0;
                        if (Mathf.Abs(o.y) > 0.7f) d0 = (Vector3.up + side * Mathf.Tan(RuinRange(rnd, 5f, 40f) * Mathf.Deg2Rad)).normalized;       // out of a stump's top
                        else
                        {   // out of a broken end: up to 70 degrees upward, up to 45 to either side
                            side = Vector3.Cross(Vector3.up, o).normalized;
                            d0 = Quaternion.AngleAxis(RuinRange(rnd, -45f, 45f), Vector3.up) * (Quaternion.AngleAxis(-RuinRange(rnd, -5f, 70f), side) * o);
                        }
                        var bendTo = rnd.NextDouble() < 0.6 ? Vector3.down : rnd.NextDouble() < 0.5 ? side : -side;
                        var d1 = (d0 * Mathf.Cos(kink) + bendTo * Mathf.Sin(kink)).normalized;
                        pts.Clear();
                        pts.Add(p - d0 * 0.3f); pts.Add(p + d0 * (len * straight));
                        pts.Add(pts[1] + d1 * (len * (1f - straight) * 0.6f));
                        pts.Add(pts[2] + (d1 * Mathf.Cos(0.4f) + bendTo * Mathf.Sin(0.4f)).normalized * (len * (1f - straight) * 0.4f));
                        for (int j = 1; j < pts.Count; j++) { var w = pts[j]; w.y = Mathf.Max(w.y, FloorYW(w) + RuinRodRadius * 0.5f); pts[j] = w; }   // a rod that droops lies on the rubble
                        buf.Rod(pts, RuinRodRadius, 0.6f, White); coreRods++;
                    }
                    // pieces: on the block's cells that are free floor now, most of all right at what still stands
                    var bw = new float[nn]; float total = 0f;
                    for (int i = 0; i < nn; i++)
                    {
                        float d = s.dist[i]; if (!core.cell[i] || d <= 0f) continue;
                        bw[i] = 0.35f + Mathf.Exp(-d / 0.5f); total += bw[i];
                    }
                    if (total > 0f)
                    {
                        float per10 = core.area / 10f * mul;
                        int nHeap = Mathf.Clamp(Mathf.RoundToInt(0.25f * per10), 1, 3), nM = Mathf.Clamp(Mathf.RoundToInt(per10), 2, 10);
                        int nS = Mathf.Clamp(Mathf.RoundToInt(2.2f * per10), 4, 22), nX = Mathf.Clamp(Mathf.RoundToInt(2.8f * per10), 6, 28);
                        for (int i = 0; i < nHeap && buf.Tris < top - 740; i++) CorePiece(buf, rnd, lib, "HEAP", i + 1, bw, total);
                        for (int i = 0; i < nM && buf.Tris < top - 320; i++) CorePiece(buf, rnd, lib, "M", i, bw, total);
                        for (int i = 0; i < nS && buf.Tris < top - 120; i++) CorePiece(buf, rnd, lib, "S", i, bw, total);
                        for (int i = 0; i < nX && buf.Tris < top - 60; i++) CorePiece(buf, rnd, lib, "XS", i, bw, total);
                    }
                }
                LastRuinCoreStats = $"{core.name}: {core.parts} parts ({core.flat} flat, {core.low} low stumps, {core.standing} standing), ruin mesh {core.tris} tris, {core.freed} floor cells freed of {core.area:0.0} m2, {coreRods} rods + {corePieces} pieces laid on it ({buf.Tris - start} tris), damage {RuinCoreDamage:0.00}, rubble {RuinCoreRubble:0.00}";
                Debug.Log("[OpenZ] ruin core " + LastRuinCoreStats);
            }

            /// <summary>One chunk library piece on the block's free floor: in its own resting pose and partly sunk, or tumbled.</summary>
            void CorePiece(RuinBuf buf, System.Random rnd, bool lib, string cls, int n, float[] bw, float total)
            {
                ConcreteChunkLib.Entry e = null;
                if (lib) e = cls == "HEAP" ? ConcreteChunkLib.Heap(n) : ConcreteChunkLib.Pick(rnd, cls);
                float len = cls == "HEAP" ? 1.9f : cls == "M" ? 1.2f : cls == "S" ? 0.6f : 0.32f;
                Vector3 size = SizeOf(e, new Vector3(len, len * (cls == "HEAP" ? 0.3f : 0.5f), len * 0.75f) * RuinRange(rnd, 0.85f, 1.15f));
                var sc = e != null ? ConcreteChunkLib.RandomScale(rnd, e.max_scale) : Vector3.one;
                float r = 0.45f * Vector3.Scale(size, sc).magnitude, hy = size.y * sc.y; bool small = cls == "S" || cls == "XS";
                for (int tries = 0; tries < 8; tries++)
                {
                    float pick = (float)rnd.NextDouble() * total; int hit = -1;
                    for (int i = 0; i < bw.Length; i++) { if (bw[i] <= 0f) continue; hit = i; pick -= bw[i]; if (pick <= 0f) break; }
                    if (hit < 0) return;
                    float fx = hit % N + RuinRange(rnd, 0.2f, 0.8f), fz = hit / N + RuinRange(rnd, 0.2f, 0.8f);
                    var w = World(fx, fz); if (!Free(placed, w.x, w.z, r, small ? 0.35f : 0.6f)) continue;
                    float fy = FloorY(fx, fz), cy; Quaternion q;
                    if (cls != "HEAP" && rnd.NextDouble() < 0.45)
                    {   // tumbled against what still stands
                        q = ConcreteChunkLib.RandomRotation(rnd); cy = fy + 0.2f * Mathf.Min(size.x * sc.x, Mathf.Min(hy, size.z * sc.z));
                    }
                    else
                    {
                        float sink = cls == "HEAP" ? 0.05f : hy * RuinRange(rnd, 0.1f, 0.45f);
                        q = Quaternion.Euler(RuinRange(rnd, -12f, 12f), RuinRange(rnd, 0f, 360f), RuinRange(rnd, -12f, 12f));
                        cy = fy - (e != null ? e.rest_bottom_y : -0.5f * size.y) * sc.y - sink;
                    }
                    if (rnd.NextDouble() < 0.5) sc.x = -sc.x;
                    AddPiece(buf, e, Matrix4x4.TRS(new Vector3(w.x, cy, w.z), q, sc), size, PieceTone(w.x, w.z, rnd), rnd);
                    placed.Add(new Vector3(w.x, w.z, r)); corePieces++;
                    return;
                }
            }
        }
    }
}
