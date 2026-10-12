using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// Exact A* reachability for the pathing layer. Zod decides "reachable" with 4-connected single-tile regions built from
    /// the terrain at map load (zpath_finding.cpp:159-285), so a target behind buildings, rock walls, a destroyed bridge or a
    /// gap too narrow for a 2x2 vehicle still looks reachable and the unit drives straight into the obstacle and stops
    /// (Find_Path returns the bare end point, zpath_finding_astar.cpp). Here the components are the connected sets of the A*
    /// graph itself: nodes = footprint tiles TileOk accepts (1 tile for robots, 2x2 for vehicles), edges = the 8 A* moves with
    /// Zod's own corner rules (<see cref="ZPathFinder.StepOk"/>), on the current grids (with / without destroyable rocks),
    /// rebuilt lazily whenever the pathfinder's passability changes (rock destroyed, bridge destroyed or repaired).
    /// </summary>
    public sealed class ZReach
    {
        readonly ZWorld W;
        readonly int[][] comp = new int[4][];
        readonly int[] builtVersion = { int.MinValue, int.MinValue, int.MinValue, int.MinValue };
        int[] queue;
        public int Rebuilds { get; private set; }

        public ZReach(ZWorld w) { W = w; }

        static int G(bool robot, bool noRocks) => (robot ? 0 : 2) + (noRocks ? 1 : 0);

        /// <summary>Component id of footprint tile (tx, ty), -1 when the footprint does not fit there.</summary>
        public int Comp(int tx, int ty, bool robot, bool noRocks)
        {
            var pf = W.Path;
            if (tx < 0 || ty < 0 || tx >= pf.W || ty >= pf.H) return -1;
            int g = G(robot, noRocks);
            if (comp[g] == null || builtVersion[g] != pf.Version) Flood(g, robot, noRocks);
            return comp[g][ty * pf.W + tx];
        }

        void Flood(int g, bool robot, bool noRocks)
        {
            var pf = W.Path; int w = pf.W, h = pf.H, n = w * h;
            if (comp[g] == null || comp[g].Length != n) comp[g] = new int[n];
            if (queue == null || queue.Length != n) queue = new int[n];
            var c = comp[g];
            for (int i = 0; i < n; i++) c[i] = pf.StepOk(i % w, i / w, i % w, i / w, robot, noRocks) ? -2 : -1;   // -2 = valid, unlabelled
            int next = 0;
            for (int s = 0; s < n; s++)
            {
                if (c[s] != -2) continue;
                int head = 0, tail = 0; queue[tail++] = s; c[s] = next;
                while (head < tail)
                {
                    int cur = queue[head++], cx = cur % w, cy = cur / w;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = cx + dx, ny = cy + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int ni = ny * w + nx;
                            if (c[ni] != -2 || !pf.StepOk(cx, cy, nx, ny, robot, noRocks)) continue;
                            c[ni] = next; queue[tail++] = ni;
                        }
                }
                next++;
            }
            builtVersion[g] = pf.Version;
            Rebuilds++;
        }

        /// <summary>Footprint tile of a unit, like Find_Path's start (loc + 8, zobject.cpp:2719).</summary>
        public static (int tx, int ty) FootTile(int locX, int locY) => ((locX + 8) >> 4, (locY + 8) >> 4);
        /// <summary>Footprint tile a unit would occupy with its centre on (x, y).</summary>
        public static (int tx, int ty) FootTileAt(int x, int y, bool robot) => robot ? (x >> 4, y >> 4) : ((x - 8) >> 4, (y - 8) >> 4);
        /// <summary>Centre point of a footprint tile (robots: tile centre; vehicles: the corner shared by the 2x2 = Zod's A* points).</summary>
        public static (int x, int y) FootCentre(int tx, int ty, bool robot) => robot ? (tx * 16 + 8, ty * 16 + 8) : (tx * 16 + 16, ty * 16 + 16);

        /// <summary>The unit's component; a unit standing off the graph (inside a building exit, clipped into a wall) takes the
        /// nearest valid footprint within 2 tiles, else -1.</summary>
        public int UnitComp(ZObject o, bool noRocks)
        {
            bool robot = o.IsRobot;
            var (tx, ty) = FootTile(o.LocX, o.LocY);
            int c = Comp(tx, ty, robot, noRocks);
            if (c >= 0) return c;
            long best = long.MaxValue; int bc = -1;
            for (int r = 1; r <= 2; r++)
                for (int y = ty - r; y <= ty + r; y++)
                    for (int x = tx - r; x <= tx + r; x++)
                    {
                        if (Math.Max(Math.Abs(x - tx), Math.Abs(y - ty)) != r) continue;
                        int k = Comp(x, y, robot, noRocks); if (k < 0) continue;
                        var (px, py) = FootCentre(x, y, robot);
                        long d = (long)(px - o.CenterX) * (px - o.CenterX) + (long)(py - o.CenterY) * (py - o.CenterY);
                        if (d < best) { best = d; bc = k; }
                    }
            return bc;
        }

        /// <summary>A unit of this kind can stand with its centre on (x, y) (box shrunk by 1 px like WithinImpassable) and
        /// that spot is in component c.</summary>
        public bool PointOk(int x, int y, bool robot, bool noRocks, int c)
        {
            var (tx, ty) = FootTileAt(x, y, robot);
            if (Comp(tx, ty, robot, noRocks) != c) return false;
            int half = robot ? 8 : 16;
            return BoxFree(x - half, y - half, half * 2, half * 2, robot, noRocks);
        }

        /// <summary>Box (top-left x, y, size w x h) shrunk by 1 px touches no blocked tile of the grid and stays on the map.</summary>
        public bool BoxFree(int x, int y, int w, int h, bool robot, bool noRocks)
        {
            var pf = W.Path;
            x += 1; y += 1; w -= 2; h -= 2;
            if (x < 0 || y < 0 || x + w >= pf.WidthPix || y + h >= pf.HeightPix) return false;
            int tx0 = x >> 4, ty0 = y >> 4, tx1 = (x + w - 1) >> 4, ty1 = (y + h - 1) >> 4;
            for (int j = ty0; j <= ty1; j++)
                for (int i = tx0; i <= tx1; i++)
                    if (!pf.TilePassableGrid(i, j, robot, noRocks)) return false;
            return true;
        }

        /// <summary>Nearest point to (x, y) where the unit fits and that lies in component c (ring search, deterministic
        /// scan order, up to <paramref name="maxTiles"/> tiles away).</summary>
        public bool Nearest(int x, int y, bool robot, bool noRocks, int c, out int rx, out int ry, int maxTiles = 64) =>
            Nearest(x, y, robot, noRocks, c, null, out rx, out ry, maxTiles);

        /// <summary>Nearest as above, skipping points <paramref name="skip"/> rejects (null = none).</summary>
        public bool Nearest(int x, int y, bool robot, bool noRocks, int c, Func<int, int, bool> skip, out int rx, out int ry, int maxTiles = 64)
        {
            rx = x; ry = y;
            if (c < 0) return false;
            var (cx, cy) = FootTileAt(x, y, robot);
            long best = long.MaxValue;
            for (int r = 0; r <= maxTiles; r++)
            {
                if (best != long.MaxValue)
                {
                    long lim = (long)(r * 16 - 24); if (lim > 0 && lim * lim > best) break;   // no later ring can be closer
                }
                for (int ty = cy - r; ty <= cy + r; ty++)
                    for (int tx = cx - r; tx <= cx + r; tx++)
                    {
                        if (Math.Max(Math.Abs(tx - cx), Math.Abs(ty - cy)) != r) continue;
                        if (Comp(tx, ty, robot, noRocks) != c) continue;
                        var (px, py) = FootCentre(tx, ty, robot);
                        if (skip != null && skip(px, py)) continue;
                        long d = (long)(px - x) * (px - x) + (long)(py - y) * (py - y);
                        if (d < best) { best = d; rx = px; ry = py; }
                    }
            }
            return best != long.MaxValue;
        }
    }
}
