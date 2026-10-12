using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    public enum PfTile { Normal, Impassable, Water, Road }

    /// <summary>
    /// Port of zpath_finding.cpp + zpath_finding_astar.cpp: four tile grids (robot / vehicle, with and without destroyable
    /// barriers), integer side/diagonal weights (road 1/(1.689+0.5), water 1/0.7, vehicles summed over their 2x2),
    /// 4-connected regions, the straight-line "direct path" test and the 8-direction A* with Zod's quirks (Manhattan
    /// heuristic in tiles against weights of ~100, first-lowest open-list scan with swap-remove, the closed-list update
    /// bug, collinear-point removal, tile-centre output + the exact end point). Runs synchronously; the world delivers the
    /// result one tick later like Zod's path thread.
    /// </summary>
    public sealed class ZPathFinder
    {
        public struct Info { public int side, diag; public bool passable; }

        public int W, H, WidthPix, HeightPix;
        Info[,] robot, vehicle, robotNoRocks, vehicleNoRocks;
        int[,] robotRegion, vehicleRegion;
        int nextThreadId;
        /// <summary>OpenZ pathing layer: bumped on every passability change (ZReach components and lanes are cached by it).</summary>
        public int Version { get; private set; }
        /// <summary>OpenZ pathing layer: the next FindPath skips the straight-line shortcut and runs the A* (set when the unit's
        /// real box would clip a wall on that line: the test below starts at loc + 8, a vehicle's centre is loc + 16).</summary>
        public bool ForceSearch;
        /// <summary>DV44 (ZCommit.cs): Zod A* searches run so far, and their work (open-list scans + closed-list walks; the path budget
        /// counts work per tick). Counters only: the search itself is Zod's.</summary>
        public int AStarRuns; public long Work;

        public void Reset(int w, int h)
        {
            Version++;
            W = w; H = h; WidthPix = w * 16; HeightPix = h * 16;
            robot = new Info[w, h]; vehicle = new Info[w, h]; robotNoRocks = new Info[w, h]; vehicleNoRocks = new Info[w, h];
            robotRegion = new int[w, h]; vehicleRegion = new int[w, h];
        }

        /// <summary>zpath_finding.cpp:417-471 (C float tile_speed, int weights = truncation)</summary>
        public void SetTileInfo(int x, int y, PfTile t)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            Version++;
            float speed = 1.0f; bool r = true, v = true;
            switch (t)
            {
                case PfTile.Normal: speed = 1.0f; break;
                case PfTile.Impassable: speed = 1.0f; r = v = false; break;
                case PfTile.Water: speed = (float)(1.0 / ZConst.WATER_SPEED); v = false; break;
                case PfTile.Road: speed = (float)(1.0 / (ZConst.ROAD_SPEED + 0.5)); break;
            }
            int side = (int)(100 * speed);                 // int * float -> float, truncated to int
            int diag = (int)((1.414 * 100) * speed);       // double * float -> double, truncated
            robot[x, y] = new Info { side = side, diag = diag, passable = r };
            vehicle[x, y] = new Info { side = side, diag = diag, passable = v };
            robotNoRocks[x, y] = robot[x, y];
            vehicleNoRocks[x, y] = vehicle[x, y];
        }

        /// <summary>zpath_finding.cpp:473-546: a vehicle tile weighs the sum of its 2x2 footprint.</summary>
        public void SetTileWideWeights()
        {
            var ws = new int[W, H]; var wd = new int[W, H];
            for (int i = 0; i < W; i++)
                for (int j = 0; j < H; j++)
                {
                    int s = vehicle[i, j].side, d = vehicle[i, j].diag;
                    if (i + 1 < W) { s += vehicle[i + 1, j].side; d += vehicle[i + 1, j].diag; }
                    if (j + 1 < H) { s += vehicle[i, j + 1].side; d += vehicle[i, j + 1].diag; }
                    if (i + 1 < W && j + 1 < H) { s += vehicle[i + 1, j + 1].side; d += vehicle[i + 1, j + 1].diag; }
                    ws[i, j] = s; wd[i, j] = d;
                }
            for (int i = 0; i < W; i++)
                for (int j = 0; j < H; j++)
                {
                    vehicle[i, j].side = vehicleNoRocks[i, j].side = ws[i, j];
                    vehicle[i, j].diag = vehicleNoRocks[i, j].diag = wd[i, j];
                }
        }

        /// <summary>zpath_finding.cpp:608-627</summary>
        public void SetImpassable(int x, int y, bool impassable = true, bool destroyable = false)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            Version++;
            robot[x, y].passable = !impassable;
            vehicle[x, y].passable = !impassable;
            robotNoRocks[x, y].passable = !impassable || destroyable;
            vehicleNoRocks[x, y].passable = !impassable || destroyable;
        }

        public bool TileOnMap(int tx, int ty) => tx >= 0 && ty >= 0 && tx < W && ty < H;
        public bool TilePassable(int tx, int ty, bool isRobot) =>
            TileOnMap(tx, ty) && (isRobot ? robot[tx, ty].passable : vehicle[tx, ty].passable);
        /// <summary>OpenZ pathing layer: passability on one of the four grids (with / without destroyable rocks).</summary>
        public bool TilePassableGrid(int tx, int ty, bool isRobot, bool noRocks) => TileOnMap(tx, ty) && Grid(isRobot, noRocks)[tx, ty].passable;
        /// <summary>OpenZ pathing layer: the exact A* move rule (TileOk below); ex, ey == sx, sy tests the footprint alone.</summary>
        public bool StepOk(int sx, int sy, int ex, int ey, bool isRobot, bool noRocks) => TileOk(sx, sy, ex, ey, isRobot, Grid(isRobot, noRocks));
        Info[,] Grid(bool isRobot, bool noRocks) => isRobot ? (noRocks ? robotNoRocks : robot) : (noRocks ? vehicleNoRocks : vehicle);

        // ------------------------------------------------------------------ regions (zpath_finding.cpp:159-285)
        public void RebuildRegions() { BuildRegions(robot, robotRegion); BuildRegions(vehicle, vehicleRegion); }

        void BuildRegions(Info[,] info, int[,] region)
        {
            for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) region[x, y] = info[x, y].passable ? -1 : -2;
            int cur = 0; var q = new List<(int, int)>(W * H);
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    if (region[x, y] == -1)
                    {
                        q.Clear(); region[x, y] = cur; q.Add((x, y));
                        for (int i = 0; i < q.Count; i++)
                        {
                            var (nx, ny) = q[i];
                            Fill(region, q, nx - 1, ny, cur); Fill(region, q, nx + 1, ny, cur);
                            Fill(region, q, nx, ny - 1, cur); Fill(region, q, nx, ny + 1, cur);
                        }
                        cur++;
                    }
        }

        void Fill(int[,] region, List<(int, int)> q, int x, int y, int cur)
        {
            if (x < 0 || y < 0 || x >= W || y >= H || region[x, y] != -1) return;
            region[x, y] = cur; q.Add((x, y));
        }

        public bool InSameRegion(int sx, int sy, int ex, int ey, bool isRobot)
        {
            sx >>= 4; sy >>= 4; ex >>= 4; ey >>= 4;
            if (sx < 0 || sy < 0 || ex < 0 || ey < 0 || sx >= W || sy >= H || ex >= W || ey >= H) return true;
            return isRobot ? robotRegion[sx, sy] == robotRegion[ex, ey] : vehicleRegion[sx, sy] == vehicleRegion[ex, ey];
        }

        // ------------------------------------------------------------------ movement blocking (zpath_finding.cpp:629-698)
        public bool WithinImpassable(int x, int y, int w, int h, out int stopX, out int stopY, bool isRobot)
        {
            stopX = stopY = 0;
            if (x < 0 || y < 0 || x + w >= WidthPix || y + h >= HeightPix) return true;
            var tc = isRobot ? robot : vehicle;
            int tx = x >> 4, ty = y >> 4, tw = w >> 4, th = h >> 4;
            if (w % 16 != 0) tw += 1;
            if (h % 16 != 0) th += 1;
            int tex = Math.Min(tx + tw, (WidthPix >> 4) - 1), tey = Math.Min(ty + th, (HeightPix >> 4) - 1);
            for (int i = tx, ip = tx << 4; i <= tex; i++, ip += 16)
                for (int j = ty, jp = ty << 4; j <= tey; j++, jp += 16)
                    if (!tc[i, j].passable)
                    {
                        if (x >= ip + 16 || y >= jp + 16 || x + w <= ip || y + h <= jp) continue;
                        stopX = i << 4; stopY = j << 4;
                        return true;
                    }
            return false;
        }

        /// <summary>zpath_finding.cpp:568-595</summary>
        public bool ShouldBeAbleToMoveTo(int sx, int sy, int ex, int ey, bool isRobot)
        {
            if (!InSameRegion(sx, sy, ex, ey, isRobot)) return false;
            int tx = ex >> 4, ty = ey >> 4;
            if (isRobot) return TilePassable(tx, ty, true);
            return TilePassable(tx, ty, false) && TilePassable(tx + 1, ty, false) && TilePassable(tx, ty + 1, false) && TilePassable(tx + 1, ty + 1, false);
        }

        /// <summary>zpath_finding.cpp:700-712: impassable for the normal grid but passable for the no-rocks grid.</summary>
        public bool HasDestroyableBarrier(int x, int y) =>
            TileOnMap(x, y) && robotNoRocks[x, y].passable && !robot[x, y].passable;

        /// <summary>ZPath_Finding_Bresenham (zpath_finding.cpp:13-92): tiles after the start up to and including the end.</summary>
        public static IEnumerable<(int x, int y)> Bresenham(int stx, int sty, int etx, int ety, int w, int h)
        {
            if (stx < 0 || sty < 0 || stx >= w || sty >= h || etx < 0 || ety < 0 || etx >= w || ety >= h) yield break;
            int nx = stx, ny = sty, dx = etx - stx, dy = ety - sty;
            int stepX = dx < 0 ? -1 : 1, stepY = dy < 0 ? -1 : 1;
            dx = Math.Abs(dx * 2); dy = Math.Abs(dy * 2);
            int fraction = dy > dx ? dx * 2 - dy : dy * 2 - dx;
            while (true)
            {
                if (dy > dx)
                {
                    if (ny == ety) yield break;
                    if (fraction >= 0) { nx += stepX; fraction -= dy; }
                    ny += stepY; fraction += dx;
                }
                else
                {
                    if (nx == etx) yield break;
                    if (fraction >= 0) { ny += stepY; fraction -= dx; }
                    nx += stepX; fraction += dy;
                }
                yield return (nx, ny);
            }
        }

        // ------------------------------------------------------------------ Find_Path (zpath_finding.cpp:810-900)
        /// <summary>Returns 0 when no search is needed (direct line possible, or start/end in different regions: the unit
        /// then heads straight for the target like Zod). Otherwise a non-zero id and the path in <paramref name="points"/>.</summary>
        public int FindPath(int sx, int sy, int ex, int ey, bool isRobot, bool hasExplosives, out List<(int x, int y)> points)
        {
            points = null;
            bool force = ForceSearch; ForceSearch = false;
            if (!force && DirectPathPossible(sx, sy, ex, ey, isRobot, hasExplosives)) return 0;
            if (!InSameRegion(sx, sy, ex, ey, isRobot)) return 0;
            if (++nextThreadId == 0) nextThreadId++;
            var tiles = isRobot ? (hasExplosives ? robotNoRocks : robot) : (hasExplosives ? vehicleNoRocks : vehicle);
            AStarRuns++;                                                                   // DV44 path budget (ZCommit.cs): searches per tick
            points = AStar(sx, sy, ex, ey, isRobot, tiles);
            return nextThreadId;
        }

        /// <summary>zpath_finding.cpp:903-1019</summary>
        public bool DirectPathPossible(int sx, int sy, int ex, int ey, bool isRobot, bool hasExplosives)
        {
            const int crawl = 4;
            if (sx == ex && sy == ey) return true;
            double x = sx, y = sy, dx = ex - sx, dy = ey - sy;
            int distLeft = (int)Math.Sqrt(dx * dx + dy * dy);
            double hyp = Math.Abs(dx) > Math.Abs(dy) ? 1 / Math.Cos(Math.Atan2(Math.Abs(dy), Math.Abs(dx)))
                                                     : 1 / Math.Sin(Math.Atan2(Math.Abs(dy), Math.Abs(dx)));
            var tc = isRobot ? (hasExplosives ? robotNoRocks : robot) : (hasExplosives ? vehicleNoRocks : vehicle);
            double distCheck = (isRobot ? (8 + 8 + 1) : (16 + 8 + 1)) * hyp;
            int bc = isRobot ? 1 : 2;
            int A = -1 * (sy - ey), B = sx - ex, C = -1 * (A * sx + B * sy);
            double under = Math.Sqrt((double)(A * A + B * B));
            dx /= distLeft; dy /= distLeft; dx *= crawl; dy *= crawl;
            distCheck *= under;
            while (distLeft > 0)
            {
                int ctx = (int)(x / 16), cty = (int)(y / 16);
                for (int tx = ctx - bc; tx <= ctx + bc; tx++)
                    for (int ty = cty - bc; ty <= cty + bc; ty++)
                    {
                        if (tx < 0 || ty < 0 || tx >= W || ty >= H) continue;
                        if (tc[tx, ty].passable) continue;
                        int cx = tx * 16 + 8, cy = ty * 16 + 8;
                        if (distCheck >= Math.Abs(A * cx + B * cy + C)) return false;
                    }
                x += dx; y += dy; distLeft -= crawl;
            }
            return true;
        }

        // ------------------------------------------------------------------ A* (zpath_finding_astar.cpp)
        struct P { public int x, y, px, py, g, h, f; }

        sealed class PArray
        {
            public P[] list = new P[1000]; public int size; public readonly int[,] index;
            public PArray(int w, int h) { index = new int[w, h]; for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) index[i, j] = -1; }
            public void Add(P p) { if (size >= list.Length) Array.Resize(ref list, list.Length + 1000); list[size] = p; index[p.x, p.y] = size; size++; }
            public void Remove(int x, int y)
            {
                int i = index[x, y]; if (i == -1) return;
                index[x, y] = -1; size--; list[i] = list[size]; index[list[i].x, list[i].y] = i;
            }
        }

        List<(int, int)> AStar(int startPx, int startPy, int endPx, int endPy, bool isRobot, Info[,] t)
        {
            int sx = startPx / 16, sy = startPy / 16, ex = endPx / 16, ey = endPy / 16;
            var result = new List<(int, int)>();
            if (!TileOk(sx, sy, sx, sy, isRobot, t) || !TileOk(ex, ey, ex, ey, isRobot, t)) { result.Add((endPx, endPy)); return result; }
            var open = new PArray(W, H); var closed = new PArray(W, H);
            open.Add(new P { x = sx, y = sy });
            P cp = default;
            while (open.size > 0)
            {
                cp = open.list[0];                                         // lowest_f_cost: first strictly lower wins
                for (int i = 0; i < open.size; i++) if (cp.f > open.list[i].f) cp = open.list[i];
                Work += open.size + 8;                                         // DV44 path budget (ZCommit.cs): counter only
                if (cp.x == ex && cp.y == ey) break;
                open.Remove(cp.x, cp.y); closed.Add(cp);
                AddOpen(cp, cp.x - 1, cp.y - 1, open, closed, ex, ey, isRobot, t); AddOpen(cp, cp.x - 1, cp.y, open, closed, ex, ey, isRobot, t);
                AddOpen(cp, cp.x - 1, cp.y + 1, open, closed, ex, ey, isRobot, t); AddOpen(cp, cp.x, cp.y - 1, open, closed, ex, ey, isRobot, t);
                AddOpen(cp, cp.x, cp.y + 1, open, closed, ex, ey, isRobot, t); AddOpen(cp, cp.x + 1, cp.y - 1, open, closed, ex, ey, isRobot, t);
                AddOpen(cp, cp.x + 1, cp.y, open, closed, ex, ey, isRobot, t); AddOpen(cp, cp.x + 1, cp.y + 1, open, closed, ex, ey, isRobot, t);
            }
            var path = new List<P>();
            if (cp.x == ex && cp.y == ey)
                while (true)
                {
                    path.Insert(0, cp);
                    int i = 0;
                    for (; i < closed.size; i++) if (cp.px == closed.list[i].x && cp.py == closed.list[i].y) { cp = closed.list[i]; break; }
                    Work += i + 1;                                                 // DV44 path budget (ZCommit.cs): counter only
                    if (i >= closed.size) { path.Clear(); break; }
                    if (cp.x == sx && cp.y == sy) break;
                }
            // remove redundant points (same direction in and out)
            for (int i = 0; i < path.Count;)
            {
                if (i != 0 && i + 1 != path.Count && Dir(path[i - 1], path[i]) == Dir(path[i], path[i + 1])) path.RemoveAt(i);
                else i++;
            }
            foreach (var p in path) result.Add(isRobot ? (p.x * 16 + 8, p.y * 16 + 8) : (p.x * 16 + 16, p.y * 16 + 16));
            result.Add((endPx, endPy));
            return result;
        }

        static int Hf(int sx, int sy, int fx, int fy) => Math.Abs(sx - fx) + Math.Abs(sy - fy);
        static int Gf(P c, int nx, int ny, Info[,] t) => c.x == nx || c.y == ny ? t[nx, ny].side : t[nx, ny].diag;

        void AddOpen(P cp, int nx, int ny, PArray open, PArray closed, int ex, int ey, bool isRobot, Info[,] t)
        {
            if (!TileOk(cp.x, cp.y, nx, ny, isRobot, t)) return;
            if (open.index[nx, ny] != -1) return;
            if (closed.index[nx, ny] != -1)
            {
                // Zod: `if(int i = point_index[..] != -1)` assigns the comparison (1), so it re-parents closed entry #1
                if (closed.size > 1)
                {
                    ref P ip = ref closed.list[1];
                    int g = Gf(cp, nx, ny, t) + cp.g;
                    if (ip.g > g) { ip.g = g; ip.h = Hf(nx, ny, ex, ey); ip.f = ip.g + ip.h; ip.px = cp.x; ip.py = cp.y; }
                }
                return;
            }
            var np = new P { x = nx, y = ny, g = Gf(cp, nx, ny, t) + cp.g, h = Hf(nx, ny, ex, ey), px = cp.x, py = cp.y };
            np.f = np.g + np.h;
            open.Add(np);
        }

        bool Pass(Info[,] t, int x, int y) => x >= 0 && y >= 0 && x < W && y < H && t[x, y].passable;

        bool TileOk(int sx, int sy, int ex, int ey, bool isRobot, Info[,] t)
        {
            if (isRobot)
            {
                if (ex < 0 || ey < 0 || ex >= W || ey >= H) return false;
                if (!t[ex, ey].passable) return false;
                if (ex == sx || ey == sy) return true;
                if (!Pass(t, sx + (ex - sx), sy)) return false;
                if (!Pass(t, sx, sy + (ey - sy))) return false;
                return true;
            }
            if (ex < 0 || ey < 0 || ex + 1 >= W || ey + 1 >= H) return false;
            if (!t[ex, ey].passable || !t[ex + 1, ey].passable || !t[ex, ey + 1].passable || !t[ex + 1, ey + 1].passable) return false;
            if (ex == sx || ey == sy) return true;
            if (ex > sx && ey < sy) return Pass(t, sx, sy - 1) && Pass(t, sx + 2, sy + 1);        // upper right
            if (ex < sx && ey < sy) return Pass(t, sx + 1, sy - 1) && Pass(t, sx - 1, sy + 1);    // upper left
            if (ex > sx && ey > sy) return Pass(t, sx + 2, sy) && Pass(t, sx, sy + 2);            // lower right
            return Pass(t, sx - 1, sy) && Pass(t, sx + 1, sy + 2);                                  // lower left
        }

        static int Dir(P lp, P cp)
        {
            if (cp.x > lp.x) return cp.y == lp.y ? 0 : cp.y < lp.y ? 1 : 7;
            if (cp.x == lp.x) return cp.y > lp.y ? 2 : 6;
            return cp.y < lp.y ? 3 : cp.y > lp.y ? 5 : 4;
        }
    }
}
