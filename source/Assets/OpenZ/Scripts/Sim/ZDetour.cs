using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// Detours around standing units. Zod's A* only knows terrain, so a unit whose way is blocked by units that do not move
    /// (a parked crowd that cannot step aside, a cannon, an empty vehicle) re-plans the same path forever. When the stuck
    /// guard sees a unit held behind such a dead end, it runs one local search on the same A* graph (footprint tiles, the
    /// 8 moves with Zod's corner rules, ZPathFinder.StepOk), with the tiles under standing units closed, and hands the unit
    /// the result as its path (tile centres, vehicles on 2x2 corners, like Zod's own points). Deterministic (binary heap
    /// with id tie-break), bounded (8000 expansions), at most once per waypoint and stuck step. If the goal itself is walled
    /// in by units, the path ends at the reachable tile nearest to it.
    /// </summary>
    public sealed partial class ZPathing
    {
        bool[] occ; int[] gScore, came, heapNode, heapKey; int heapN; readonly List<int> touched = new List<int>();
        public int Detours { get; private set; }

        bool Detour(ZObject o)
        {
            var (gxp, gyp) = o.PathFinalPoint;
            var pts = Search(o, gxp, gyp, true, out bool found);
            pts = ParkedDetour(o, gxp, gyp, pts, ref found);                                // boarding_nb_v1 DV36: no local way round: the whole map
            if (DetourTooLong(o, pts) || LongWayToNearGoal(o, pts) || NoWayRound(o, ref pts)) return false;   // DV44: not a long way round standing units, nor a "detour" straight through them (ZCommit.Settle.cs)
            if (pts == null || BridgeRejectsPath(o, pts)) return false;                     // (never back against the flow on a deck, ZBridges.cs)
            pts = NoBackSteps(o, pts);                                                      // DV44 (ZCommit.cs)
            var (endX, endY) = pts[pts.Count - 1]; int nPts = pts.Count;                   // (PathSetPoints takes the first point off the list: a one-point detour left it empty)
            o.PathSetPoints(pts);
            Detours++;
            Log(o, $"detour around standing units: {nPts} points to ({endX},{endY}){(found ? "" : " (goal walled in)")}");
            return true;
        }

        /// <summary>ZObject.StartPath hook: Zod's A* result is kept unless one of its legs (from where the unit really stands,
        /// then point to point) would clip a blocked tile with the unit's own box. Zod's search re-parents closed entry #1 to
        /// a node that is not its neighbour (zpath_finding_astar.cpp, the `if(int i = ... != -1)` bug) and starts vehicles from
        /// loc + 8; such a leg makes the unit stop at the wall and drop the order. Then a clean search on the same graph
        /// (same TileOk moves, same grid) supplies the points.</summary>
        public List<(int x, int y)> CheckPath(ZObject o, List<(int x, int y)> pts)
        {
            if (!Enabled || pts == null || pts.Count == 0) return pts;
            try { return NoBackSteps(o, ParkedPath(o, BridgePath(o, CheckPathCore(o, pts)))); } catch (Exception e) { Fail(e); return pts; }   // boarding_nb_v1 DV36: round parked units; DV44 NoBackSteps (ZCommit.cs)
        }

        List<(int x, int y)> CheckPathCore(ZObject o, List<(int x, int y)> pts)
        {
            bool robot = o.IsRobot, nr = NoRocks(o);
            int px = o.CenterX, py = o.CenterY; bool clip = false;
            for (int i = 0; i < pts.Count && !clip; i++) { clip = !LegClear(px, py, pts[i].x, pts[i].y, robot, nr, o, i == 0); px = pts[i].x; py = pts[i].y; }
            if (!clip) return pts;
            var (gx, gy) = pts[pts.Count - 1];
            var better = Search(o, gx, gy, false, out bool found);
            if (better == null || !found) return pts;
            CleanPaths++;
            Log(o, $"Zod path clips a wall: clean search, {better.Count} points");
            return better;
        }
        public int CleanPaths { get; internal set; }

        bool LegClear(int x0, int y0, int x1, int y1, bool robot, bool nr, ZObject o, bool first)
        {
            int half = robot ? 8 : 16; double dx = x1 - x0, dy = y1 - y0, len = Math.Sqrt(dx * dx + dy * dy);
            if (first && !Reach.BoxFree(x0 - half, y0 - half, half * 2, half * 2, robot, nr)) return true;   // already embedded: Zod's escape
            int steps = (int)(len / 4) + 1;
            for (int i = 1; i <= steps; i++)
            {
                double f = Math.Min(1.0, i * 4 / Math.Max(len, 1e-9));
                int cx = (int)Math.Round(x0 + dx * f), cy = (int)Math.Round(y0 + dy * f);
                if (!Reach.BoxFree(cx - half, cy - half, half * 2, half * 2, robot, nr)) return false;
            }
            return true;
        }

        /// <summary>A* on the Zod graph (footprint tiles, TileOk moves) from the unit to (gxp, gyp); with avoidUnits the tiles
        /// under standing units are closed. Returns Zod-style points (tile centres / 2x2 corners, straight runs merged, the
        /// exact goal last when reached), or null.</summary>
        List<(int x, int y)> Search(ZObject o, int gxp, int gyp, bool avoidUnits, out bool found)
        {
            found = false; layerSearches++;                                                // DV44 path budget (ZCommit.cs)
            var pf = W.Path; int w = pf.W, h = pf.H, n = w * h;
            bool robot = o.IsRobot, nr = NoRocks(o);
            if (occ == null || occ.Length != n)
            {
                occ = new bool[n]; gScore = new int[n]; came = new int[n]; heapNode = new int[MaxExpansions * 8 + 16]; heapKey = new int[heapNode.Length];
                for (int i = 0; i < n; i++) gScore[i] = int.MaxValue;
            }
            var (sx, sy) = ZReach.FootTile(o.LocX, o.LocY);
            if (!pf.StepOk(sx, sy, sx, sy, robot, nr)) return null;
            var (gx, gy) = ZReach.FootTileAt(gxp, gyp, robot);
            // close the tiles under standing units (not this unit, not units that are walking on)
            var closed = new List<int>();
            if (avoidUnits) foreach (var u in units)
            {
                if (u == o || !Separable(u)) continue;
                bool standing = (Static(u) && !u.Stunned) || u.Waypoints.Count == 0 || BridgeParked(u);   // parked (or waiting for a bridge turn); held units will move on (electrobot_v1 DV45: so does a stunned one with orders)
                if (!standing) continue;
                int r = Radius(u) + 2;
                for (int ty = (u.CenterY - r) >> 4; ty <= (u.CenterY + r - 1) >> 4; ty++)
                    for (int tx = (u.CenterX - r) >> 4; tx <= (u.CenterX + r - 1) >> 4; tx++)
                    {
                        if (tx < 0 || ty < 0 || tx >= w || ty >= h) continue;
                        int i = ty * w + tx; if (!occ[i]) { occ[i] = true; closed.Add(i); }
                    }
            }
            bool Free(int tx, int ty)
            {
                if (tx < 0 || ty < 0 || tx >= w || ty >= h) return false;
                if (robot) return !occ[ty * w + tx];
                return tx + 1 < w && ty + 1 < h && !occ[ty * w + tx] && !occ[ty * w + tx + 1] && !occ[(ty + 1) * w + tx] && !occ[(ty + 1) * w + tx + 1];
            }
            int start = sy * w + sx, goal = gy * w + gx;
            int best = start, bestH = Heur(sx, sy, gx, gy);
            heapN = 0; touched.Clear();
            gScore[start] = 0; came[start] = -1; touched.Add(start); Push(start, bestH);
            int expansions = 0;
            while (heapN > 0 && expansions < MaxExpansions)
            {
                int cur = Pop(); expansions++;
                if (cur == goal) { found = true; best = cur; break; }
                int cx = cur % w, cy = cur / w, cg = gScore[cur];
                int hc = Heur(cx, cy, gx, gy); if (hc < bestH) { bestH = hc; best = cur; }
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx, ny = cy + dy;
                        if (!pf.StepOk(cx, cy, nx, ny, robot, nr)) continue;
                        if (BridgeAvoidsTile(o, nx, ny)) continue;                                // an order routed round a bridge stays off it (ZBridges.cs)
                        int ni = ny * w + nx;
                        if (!Free(nx, ny) && ni != goal) continue;
                        int ng = cg + (dx != 0 && dy != 0 ? 141 : 100);
                        if (ng >= gScore[ni]) continue;
                        if (gScore[ni] == int.MaxValue) touched.Add(ni);
                        gScore[ni] = ng; came[ni] = cur;
                        Push(ni, ng + Heur(nx, ny, gx, gy));
                    }
            }
            var tiles = new List<int>();
            for (int c = best; c != -1 && c != start; c = came[c]) tiles.Add(c);
            foreach (var i in touched) gScore[i] = int.MaxValue;
            foreach (var i in closed) occ[i] = false;
            if (tiles.Count == 0) return null;
            if (!found && Heur(sx, sy, gx, gy) - bestH < 200) return null;                     // gets less than 2 tiles closer: not worth it
            tiles.Reverse();
            var pts = new List<(int x, int y)>();
            for (int k = 0; k < tiles.Count; k++)
            {
                int t = tiles[k];
                if (k > 0 && k + 1 < tiles.Count)
                {                                                                              // drop points in the middle of a straight run
                    int a = tiles[k - 1], b = tiles[k + 1];
                    if (t % w - a % w == b % w - t % w && t / w - a / w == b / w - t / w) continue;
                }
                pts.Add(ZReach.FootCentre(t % w, t / w, robot));
            }
            if (found) pts.Add((gxp, gyp));
            return pts;
        }

        const int MaxExpansions = 8000;

        static int Heur(int x, int y, int gx, int gy)
        {
            int dx = Math.Abs(x - gx), dy = Math.Abs(y - gy);
            return 100 * Math.Max(dx, dy) + 41 * Math.Min(dx, dy);
        }

        void Push(int node, int key)
        {
            if (heapN >= heapNode.Length) return;
            int i = heapN++;
            heapNode[i] = node; heapKey[i] = key;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (heapKey[p] < heapKey[i] || (heapKey[p] == heapKey[i] && heapNode[p] <= heapNode[i])) break;
                (heapNode[p], heapNode[i]) = (heapNode[i], heapNode[p]); (heapKey[p], heapKey[i]) = (heapKey[i], heapKey[p]);
                i = p;
            }
        }

        int Pop()
        {
            int top = heapNode[0]; layerWork += PopWork;                                  // DV44 path budget (ZCommit.cs): counter only
            heapN--;
            heapNode[0] = heapNode[heapN]; heapKey[0] = heapKey[heapN];
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < heapN && (heapKey[l] < heapKey[m] || (heapKey[l] == heapKey[m] && heapNode[l] < heapNode[m]))) m = l;
                if (r < heapN && (heapKey[r] < heapKey[m] || (heapKey[r] == heapKey[m] && heapNode[r] < heapNode[m]))) m = r;
                if (m == i) break;
                (heapNode[m], heapNode[i]) = (heapNode[i], heapNode[m]); (heapKey[m], heapKey[i]) = (heapKey[i], heapKey[m]);
                i = m;
            }
            return top;
        }
    }
}
