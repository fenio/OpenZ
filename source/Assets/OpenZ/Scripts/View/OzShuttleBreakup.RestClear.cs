using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The shot-down shuttle's pieces lie ON each other at rest, never inside each other (rule DV118, view only; the plain part:
    /// OzRestClearPlan). Pieces that start inside each other (the hull's cut panels, the legs under the body) ignore each other until
    /// they are apart; one that is still inside a neighbour when it comes to rest is pushed out along the way out of it, and that way can
    /// point into the ground, where the rest's settle on the ground then lifted it straight back into the neighbour. A light piece
    /// pressed under a heavy one can also come to rest a few centimetres inside it. Now, once every piece of a wreck lies still (and again
    /// after any piece settles anew), the piece on top of such a pair glides out of the one under it: up off it, or along the way out, or
    /// sideways, whichever is the shortest move that clears the pair without pushing it into another piece, a rock, a trunk, the ground
    /// or into the air (it stays lying on something). The keeper of the wreck runs it (its one-line hook in KeeperUpdate). Switch: <see cref="RestClearOn"/>.
    /// </summary>
    public sealed partial class OzShuttleBreakup
    {
        /// <summary>Switch: the resting pieces are cleared out of each other (on).</summary>
        public static bool RestClearOn = true;
        /// <summary>Proof: passes run, pieces moved, pairs that could not be cleared, the deepest pair seen before a pass (m), the longest move (m).</summary>
        public static int RestClearPasses, RestClearMoves, RestClearLeft; public static float RestClearWorstSeen, RestClearMostMove;
        /// <summary>Proof: the last pass's lines ("piece out of piece: how far, which way").</summary>
        public static readonly List<string> RestClearLog = new List<string>();

        int restClearSeen = -1, restClearPasses, restClearSteps; Heap restHeap; HashSet<long> restSkip; double restBefore;
        static float restBusyUntil = -1f;
        static Group restLogOwner;                                                                              // the wreck whose lines RestClearLog holds

        /// <summary>This wreck is the last break's (the proof reads its lines).</summary>
        bool RestClearLast() { foreach (var q in LastPieces) if (q && q.grp == grp) return true; return false; }

        /// <summary>The keeper, once a frame: a pass once every piece of the wreck lies still and something settled since the last one;
        /// one pair a frame (a pass is spread over frames), then every moved piece glides to its place. A piece that clears away (a repair
        /// station's deck) takes no part; the wreck's own sinking away ends it.</summary>
        void RestClear()
        {
            if (!RestClearOn || grp == null) return;
            if (restHeap != null) { RestClearStep(); return; }
            if (grp.pieces.Count < 2 || restClearPasses >= OzRestClearPlan.MaxPasses || RestedCount == restClearSeen) return;
            if (grp.age >= grp.wreckAt - 0.5f) { restClearPasses = OzRestClearPlan.MaxPasses; return; }         // the wreck sinks away now: never again
            foreach (var p in grp.pieces)
            {
                if (!p || p.retiring) continue;                                                                 // clearing away: not part of the pass
                if (!p.frozen || !p.Rested || p.snapT >= 0f) return;                                            // still falling or settling
            }
            restClearSeen = RestedCount; restClearPasses++; RestClearPasses++; restClearSteps = 0;
            if (restLogOwner != grp && RestClearLast()) { restLogOwner = grp; RestClearLog.Clear(); }          // a new break's wreck: its own lines only
            ArmStaticsFor(grp);                                                                                 // K256k: the buildings and guns round it on for the pass's scenery test
            restHeap = new Heap(grp.pieces); restSkip = new HashSet<long>();
            restBefore = OzRestClearPlan.Worst(restHeap, out _);
            if (restBefore <= OzRestClearPlan.ClearDepth) { restHeap = null; return; }                          // nothing inside anything
            RestClearStep();
        }

        void RestClearStep()
        {
            if (grp.age >= grp.wreckAt - 0.5f || restHeap.Broken()) { restHeap.End(); restHeap = null; return; }   // a piece of the pass went or clears away: this pass ends
            restBusyUntil = Time.time + 0.5f;
            restHeap.Begin();                                                                                   // the pass's trial places (drawn where they lay until the glide)
            bool more = restClearSteps++ < OzRestClearPlan.MaxSteps && OzRestClearPlan.Step(restHeap, restSkip, out _);
            if (more) { restHeap.End(); return; }
            var heap = restHeap; restHeap = null;
            double after = OzRestClearPlan.Worst(heap, out int left);
            heap.End();
            RestClearWorstSeen = Mathf.Max(RestClearWorstSeen, (float)restBefore);
            if (left > 0) RestClearLeft += left;
            heap.Glide();
            if (restLogOwner == grp && RestClearLast()) RestClearLog.AddRange(heap.Lines);                     // the last break's wreck (the proof reads its lines)
            Debug.Log($"[OpenZ] shuttle wreck: {heap.Lines.Count} piece move(s) out of a neighbour at rest (the deepest pair {restBefore * 100f:F1} cm -> {after * 100f:F1} cm, {left} left){(heap.Lines.Count > 0 ? ": " + string.Join("; ", heap.Lines) : "")}");
        }

        /// <summary>Proof: the last break's pieces are done settling (no piece gliding into its place).</summary>
        public static bool RestClearIdle() { if (Time.time < restBusyUntil) return false; foreach (var p in LastPieces) if (p && !p.retiring && p.snapT >= 0f) return false; return true; }

        /// <summary>Its lowest point over the ground (or the water) if it were moved by `d` (m), from its 48 lowest vertices.</summary>
        float GapMoved(Vector3 d, out Vector3 low)
        {
            var g = ZGame.I; World(); low = transform.position + d;
            if (!g || wpts.Count == 0) return 0f;
            wpts.Sort((a, b) => a.y.CompareTo(b.y));
            float best = float.MaxValue;
            for (int i = 0; i < wpts.Count && i < 48; i++) { var q = wpts[i] + d; float e = q.y - Surface(q); if (e < best) { best = e; low = q; } }
            return best;
        }

        static readonly Collider[] near = new Collider[16], solidHits = new Collider[64];
        static readonly List<Vector3> lowHalf = new List<Vector3>(4096);
        /// <summary>A point of a lying piece touches something this close (m).</summary>
        public static float TouchM = 0.08f;
        /// <summary>It lies on something where it is now: a vertex of its lower half (up to 64 of them, spread over it) on the ground / the
        /// water, or touching another body (a piece, a rock, a trunk, a lying body). by = what holds it up.</summary>
        internal bool LiesOnSomething(out string by)
        {
            by = "-";
            if (!col || !vis) return false;
            World(); if (wpts.Count == 0) return false;
            float mid = col.bounds.center.y;
            lowHalf.Clear(); foreach (var q in wpts) if (q.y <= mid) lowHalf.Add(q);
            if (lowHalf.Count == 0) return false;
            lowHalf.Sort((a, b) => a.y.CompareTo(b.y));
            int step = Mathf.Max(1, lowHalf.Count / 64);
            for (int i = 0; i < lowHalf.Count; i += (i < 16 ? 1 : step))                                   // its 16 lowest, then spread over the lower half
            {
                var q = lowHalf[i];
                if (q.y - Surface(q) <= TouchM) { by = "the ground"; return true; }
                int n = Physics.OverlapSphereNonAlloc(q, TouchM, near, ~0, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++)
                {
                    var c = near[k]; if (!c || c == col || c.transform.IsChildOf(transform)) continue;
                    if (c.gameObject.layer == ZGame.WalkLayer || CraterField.IsGroundCollider(c)) { by = "the ground"; return true; }
                    var host = c.GetComponentInParent<OzShuttleBreakup>();
                    by = host ? host.PieceName : c.attachedRigidbody ? "a body " + c.transform.root.name : c.transform.root.name;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Proof: rested pieces whose bottom is more than LyingOverM over the ground and that touch nothing under them (none should);
        /// the highest bottom of any piece (m) and what holds that one up.</summary>
        public static int Unsupported(out string which, out float highBottom, out string highOn)
        {
            int n = 0; which = "-"; highBottom = 0f; highOn = "-";
            foreach (var p in LastPieces)
            {
                if (!p || !p.Rested || !p.col || !p.col.enabled || p.retiring || p.RestOn == "water") continue;
                float gap = p.GapMoved(Vector3.zero, out _);
                bool on = p.LiesOnSomething(out string by);
                if (gap > highBottom) { highBottom = gap; highOn = p.PieceName + " on " + (on ? by : "NOTHING"); }
                if (gap <= LyingOverM || on) continue;
                n++; which = p.PieceName + " " + gap.ToString("F2") + " m";
            }
            return n;
        }

        // ------------------------------------------------------------------ the wreck as the plan sees it
        sealed class Heap : OzRestClearPlan.IHeap
        {
            readonly List<OzShuttleBreakup> p; readonly Vector3[] from, cur; readonly int[] onto; readonly bool[] el;
            readonly List<Vector3>[] lowPts, touchPts;                                                            // where they lay when the pass began (a pass only moves them, never turns them)
            public readonly List<string> Lines = new List<string>();
            public Heap(List<OzShuttleBreakup> pieces)
            {
                p = new List<OzShuttleBreakup>(pieces); from = new Vector3[p.Count]; cur = new Vector3[p.Count]; onto = new int[p.Count]; el = new bool[p.Count];
                lowPts = new List<Vector3>[p.Count]; touchPts = new List<Vector3>[p.Count];
                Physics.SyncTransforms();
                for (int i = 0; i < p.Count; i++)
                {
                    var q = p[i]; from[i] = cur[i] = q ? q.transform.position : Vector3.zero; onto[i] = -1;
                    el[i] = Base(i);                                                                             // only these are ever moved, placed or put back (a piece in the water or clearing away never)
                    lowPts[i] = new List<Vector3>(); touchPts[i] = new List<Vector3>();
                    if (!q || !q.col || !q.vis) continue;
                    q.World(); wpts.Sort((a, b) => a.y.CompareTo(b.y));
                    for (int k = 0; k < wpts.Count && k < 48; k++) lowPts[i].Add(wpts[k]);                     // its 48 lowest vertices (the ground gap)
                    float mid = q.col.bounds.center.y; int n = 0; foreach (var w in wpts) if (w.y <= mid) n++;
                    int step = Mathf.Max(1, n / 64);
                    for (int k = 0; k < n; k += (k < 16 ? 1 : step)) touchPts[i].Add(wpts[k]);                  // its lower half: the 16 lowest, then spread
                }
            }
            Vector3 Off(int i) => p[i].transform.position - from[i];
            /// <summary>The pieces at the pass's trial places (for its tests) / back where they lie (for drawing) at the end of the frame's step.</summary>
            bool Mine(int i) => el[i] && p[i] && !p[i].retiring;
            public void Begin() { for (int i = 0; i < p.Count; i++) if (Mine(i) && (cur[i] - from[i]).sqrMagnitude > 1e-10f) p[i].transform.position = cur[i]; Physics.SyncTransforms(); }
            public void End()
            {
                for (int i = 0; i < p.Count; i++)
                {
                    if (!Mine(i)) continue;                                                                     // never a piece the pass cannot move (one in the water bobs on its own)
                    var now = p[i].transform.position;
                    if ((now - from[i]).sqrMagnitude > 1e-10f) { cur[i] = now; p[i].transform.position = from[i]; } else cur[i] = from[i];
                }
                Physics.SyncTransforms();
            }
            /// <summary>A piece of the pass is gone or clears away (sinking): the pass ends.</summary>
            public bool Broken() { for (int i = 0; i < p.Count; i++) if (el[i] && (!p[i] || p[i].retiring)) return true; return false; }
            public int Count => p.Count;
            bool Base(int i) { var q = p[i]; return q && q.col && q.col.enabled && q.frozen && !q.retiring && q.RestOn != "water"; }
            public bool Eligible(int i) => el[i] && Base(i);
            public double Bottom(int i) => p[i].col.bounds.min.y;
            public bool Pen(int i, int j, out double nx, out double ny, out double nz, out double d)
            {
                var a = p[i].col; var b = p[j].col; nx = ny = nz = 0; d = 0;
                if (!Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation, out var dir, out float dist) || dist <= 0f) return false;
                nx = dir.x; ny = dir.y; nz = dir.z; d = dist; return true;
            }
            public double Gap(int i)
            {
                var o = Off(i); float best = float.MaxValue;
                foreach (var w in lowPts[i]) { var q = w + o; best = Mathf.Min(best, q.y - Surface(q)); }
                return lowPts[i].Count > 0 ? best : 0f;
            }
            public bool Supported(int i)
            {
                var me = p[i]; var o = Off(i);
                foreach (var w in touchPts[i])
                {
                    var q = w + o;
                    if (q.y - Surface(q) <= TouchM) return true;
                    int n = Physics.OverlapSphereNonAlloc(q, TouchM, near, ~0, QueryTriggerInteraction.Ignore);
                    for (int k = 0; k < n; k++) { var c = near[k]; if (c && c != me.col && !c.transform.IsChildOf(me.transform)) return true; }
                }
                return false;
            }
            /// <summary>Its deepest overlap with still scenery: a rock, a trunk, a lying body held still (any solid collider round it that is no
            /// piece of a wreck, no moving body and not the ground), m.</summary>
            public double Solid(int i)
            {
                var c = p[i].col; var b = c.bounds; float deep = 0f;
                int n = Physics.OverlapBoxNonAlloc(b.center, b.extents + Vector3.one * 0.02f, solidHits, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++)
                {
                    var o = solidHits[k];
                    if (!o || o == c || o is TerrainCollider || o.transform.IsChildOf(p[i].transform)) continue;
                    if (o.gameObject.layer == ZGame.WalkLayer || CraterField.IsGroundCollider(o)) continue;          // the ground: Gap's
                    var rb = o.attachedRigidbody; if (rb && !rb.isKinematic) continue;                              // a falling body moves out of the way itself
                    if (o.GetComponentInParent<OzShuttleBreakup>()) continue;                                       // a piece: Pen's
                    if (Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, o, o.transform.position, o.transform.rotation, out _, out float d)) deep = Mathf.Max(deep, d);
                }
                return deep;
            }
            public bool Near(int i, int j) { var a = p[i].col.bounds; a.Expand(0.3f); return a.Intersects(p[j].col.bounds); }
            public void Shift(int i, double dx, double dy, double dz) { p[i].transform.position += new Vector3((float)dx, (float)dy, (float)dz); Physics.SyncTransforms(); }
            public double Moved(int i) => Vector3.Distance(p[i].transform.position, from[i]);
            public void Kept(int i, int on, double dx, double dy, double dz)
            {
                onto[i] = on; float m = Mathf.Sqrt((float)(dx * dx + dy * dy + dz * dz));
                if (Lines.Count < 24) Lines.Add($"{p[i].PieceName} out of {p[on].PieceName}: {m * 100f:F1} cm ({(dy > 0.7 * m ? "up" : dy < -0.2 * m ? "down" : "sideways")})");
            }
            /// <summary>Every moved piece glides from where it lay to its new place (the settle's own 0.15 s glide).</summary>
            public void Glide()
            {
                for (int i = 0; i < p.Count; i++)
                {
                    var q = p[i]; if (!q) continue;
                    var to = cur[i]; float m = Vector3.Distance(to, from[i]);
                    if (m < 1e-4f) continue;
                    q.snapFrom = from[i]; q.snapTo = to; q.snapT = 0f;
                    if (onto[i] >= 0 && p[onto[i]] && to.y > from[i].y + 0.005f) q.RestOn = "on " + p[onto[i]].PieceName;
                    RestClearMoves++; RestClearMostMove = Mathf.Max(RestClearMostMove, m);
                }
                Physics.SyncTransforms();
            }
        }
    }
}
