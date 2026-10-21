// OpenZ (GPL-3.0) - Level 16, round 4: the fort's blast fells the trees round it as its wave reaches them.
// When the blast's shockwave starts (OpenZFortShockwave.Fire: the strike on the fallen fort) and the game is already over
// (OpenZEnding runs: the fort fell, the rules decide nothing any more), the trees of the level within BlastFellRadius of the blast
// are felled VIEW ONLY: each falls when the wave's front reaches it (OpenZFortShockwave.TimeAt: the same front as the ring, the dust
// wall and the shove), so they go down ring by ring outward, away from the blast, with the trees' own break, creak, crash and dust.
// Every tree in the reach falls: the trees of the rules AND the level's other trees (the tall plants of the flora's tree cells,
// BlastFellFlora, read at the level start; each gets its holder the moment its turn comes, as the trees of the rules got theirs at
// the level start). In the wave every tree falls whole from its foot (no stump is cut: that is the cost of a felled tree). The felled
// trees' cells keep their near objects from then on (the game is over: no far mesh is folded in the wave).
// Paced: at most BlastFellPerFrame begin in one frame and at most BlastFellBudgetMs of work (the rest wait for the next frames);
// BlastFellMost caps the total (0 = no cap, the nearest first when a cap is set). No rule is read or changed (a game that is still on never sees it: then nothing happens and
// the log says why).
// Never INTO the ending's set: a tree whose fall (from its foot, away from the blast, as long as it is tall plus its crown) would
// come within its crown + BlastSceneClear metres of the lounger set, the endings' camera or the line between them stays up (it
// would fall onto the loungers, across the shot or across the lens); a tree beside the set that falls away from it falls.
// BlastLast says how many stayed up.
// kept; BlastFrameStatus() gives frames, median, 95th share, longest, frames over 50 ms.
// Switch: BlastFell (true = on). If the count shows a cost (median more than about 2 ms over the count without it, or new frames
// over 50 ms), a total cap (BlastFellMost) is the setting that keeps it under that.
// Play check: return OpenZ.OzTreeFall.BlastStatus();   return OpenZ.OzTreeFall.BlastFrameStatus();
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    public partial class OzTreeFall
    {
        public static bool BlastFell = true;
        public static float BlastFellRadius = 95f; public static int BlastFellPerFrame = 4, BlastFellMost = 0;
        /// <summary>Milliseconds one frame may spend starting falls (the rest wait for the next frame; at least one tree a frame).</summary>
        public static float BlastFellBudgetMs = 8f;
        /// <summary>The level's other trees fall too (the flora's tree cells), from this height up (m).</summary>
        public static bool BlastFellFlora = true; public static float BlastFloraMinH = 2.5f;
        /// <summary>Counts: trees felled by the blast, waiting now, the most begun in one frame, frames a tree had to wait for the pace,
        /// trees within the reach left standing by a total cap, the last blast's place and its reach in trees; of these the flora's.</summary>
        public static int BlastFelled, BlastWaiting, BlastMostInFrame, BlastCapFrames, BlastOverMost, BlastInReach, BlastFloraInReach, BlastFloraFelled;
        /// <summary>A tree stays up when its fall comes within its crown + this (m) of the set, the endings' camera or the line between them; the trees kept up so.</summary>
        public static float BlastSceneClear = 4f; public static int BlastNearScene;
        public static string BlastLast = "no blast yet";
        public static float FrameWindow = 14f;
        /// <summary>The count's split: the most milliseconds one frame spent starting falls (the cut, the holder) and landing them (dust,
        /// leaves, sounds), and the frame they were in.</summary>
        public static float BlastBeginMsMost, BlastLandMsMost; static float landMsFrame; static int landFrame = -1;
        /// <summary>The count's notes: the frames after the blast longer than 50 ms and the frames that spent more than 8 ms starting
        /// falls (time after the blast, ms, trees and the slowest one).</summary>
        public static readonly List<string> BlastSlowNotes = new List<string>();
        internal static void LandTime(float ms) { if (Time.frameCount != landFrame) { landFrame = Time.frameCount; landMsFrame = 0f; } landMsFrame += ms; if (landMsFrame > BlastLandMsMost) BlastLandMsMost = landMsFrame; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetBlast() { BlastFelled = BlastWaiting = BlastMostInFrame = BlastCapFrames = BlastOverMost = BlastInReach = BlastNearScene = BlastFloraInReach = BlastFloraFelled = 0; BlastBeginMsMost = BlastLandMsMost = 0f; BlastSlowNotes.Clear(); BlastLast = "no blast yet"; }

        /// <summary>One tree in the wave's queue: a tree of the rules (t) or a plant of the flora that gets its holder when it falls.</summary>
        struct BlastItem { public Stand t; public Transform plant; public Vector3 foot; public float height, radius, at, dist; public bool lagged; }   // lagged: item 215, it waited once for the front along the ground (OzTreeFall.Throw.cs)

        /// <summary>The flora's tall plants, read once at the level start (so the blast's frame only measures distances).</summary>
        struct FloraTree { public Transform plant; public Vector3 foot; public float height, radius; }
        readonly List<FloraTree> floraTrees = new List<FloraTree>();
        public static int BlastFloraKnown;

        /// <summary>Level start: the tall plants of the flora's tree cells (not the trees of the rules: those stand in their holders
        /// by now), and one cut of a small mesh so the first felled tree does not pay for the cut's first run.</summary>
        void CollectBlastFlora(Transform root)
        {
            floraTrees.Clear();
            if (root)
                foreach (Transform cell in root)
                {
                    if (!cell.name.StartsWith("T_")) continue;
                    foreach (Transform p in cell)
                    {
                        if (p.name == "FAR" || p.name.StartsWith("TreeTile_")) continue;
                        var mr = p.GetComponent<MeshRenderer>(); if (!mr) continue;
                        var box = mr.bounds; var foot = p.position;
                        if (foot.y - box.min.y > 1f) foot.y = box.min.y;                                    // a hanging vine: its own origin is at its top
                        float h = box.max.y - foot.y; if (h < BlastFloraMinH) continue;
                        floraTrees.Add(new FloraTree { plant = p, foot = foot, height = h, radius = Mathf.Max(0.5f, Mathf.Max(box.extents.x, box.extents.z)) });
                    }
                }
            BlastFloraKnown = floraTrees.Count;
            // warm the cut: a small closed prism through the plane, cut once (its meshes thrown away)
            var m = new Mesh { name = "OzCutWarm" };
            m.vertices = new[] { new Vector3(-0.5f, -1f, -0.5f), new Vector3(0.5f, -1f, -0.5f), new Vector3(0f, -1f, 0.5f), new Vector3(-0.5f, 1f, -0.5f), new Vector3(0.5f, 1f, -0.5f), new Vector3(0f, 1f, 0.5f) };
            m.triangles = new[] { 0, 3, 4, 0, 4, 1, 1, 4, 5, 1, 5, 2, 2, 5, 3, 2, 3, 0 };
            m.RecalculateNormals();
            var vs = m.vertices; var side = new float[vs.Length]; for (int i = 0; i < vs.Length; i++) side[i] = vs[i].y;
            OzMeshCut.Split(m, vs, side, Vector3.up, out var lo, out var hi);
            OzMeshCut.Widen(lo, Vector3.up, stumpWide); var cap = OzMeshCut.CapMesh(lo, OzMeshCut.LowCap, Vector3.up);
            Destroy(m); Destroy(lo); Destroy(hi); if (cap) Destroy(cap);
        }

        float blastSeen = -100f; Vector3 blastAt;
        readonly Vector3[] sceneSpots = new Vector3[4]; int sceneSpotCount;
        readonly List<BlastItem> blastQueue = new List<BlastItem>();
        readonly List<float> frameMs = new List<float>(); float frameFrom = -100f;

        /// <summary>Update, every frame (before the trees' own poll).</summary>
        void BlastTick()
        {
            ThrowTick();                                                                                    // item 214: the thrown trees move (OzTreeFall.Throw.cs)
            float lf = OpenZFortShockwave.LastFireTime;
            if (lf > blastSeen + 0.01f && Time.time - lf < 5f) { blastSeen = lf; OnFortBlast(OpenZFortShockwave.LastCentre, lf); }
            if (frameFrom > 0f && Time.unscaledTime - frameFrom <= FrameWindow)
            {
                frameMs.Add(Time.unscaledDeltaTime * 1000f);
                if (Time.unscaledDeltaTime > 0.05f && BlastSlowNotes.Count < 40) BlastSlowNotes.Add($"frame of {Time.unscaledDeltaTime * 1000f:F0} ms ending at blast + {Time.unscaledTime - frameFrom:F2} s (felled so far {BlastFelled})");
            }
            if (blastQueue.Count == 0) { BlastWaiting = 0; return; }
            int begun = 0; var sw = System.Diagnostics.Stopwatch.StartNew(); float slowMs = 0f; string slowName = "";
            while (blastQueue.Count > 0 && Time.time >= blastQueue[0].at)
            {
                if (begun >= Mathf.Max(1, BlastFellPerFrame) || (begun > 0 && sw.Elapsed.TotalMilliseconds > BlastFellBudgetMs)) { BlastCapFrames++; break; }
                var q = blastQueue[0]; blastQueue.RemoveAt(0);
                if (q.t != null ? (q.t.fallen || !q.t.holder) : (!q.plant || !q.plant.gameObject.activeInHierarchy)) continue;
                if (BlastSurfaceTiming && !q.lagged && Lagged(ref q)) continue;                                 // item 215: a tree up a slope waits for the front along the ground
                var cam = ZodBriefing.EndingHolding ? Camera.main : null;                                    // the scene's camera where it stands now
                if (cam && FallReaches(q.foot, q.height, q.radius, cam.transform.position, cam.transform.position)) { BlastNearScene++; continue; }
                var one = System.Diagnostics.Stopwatch.StartNew();
                var t = q.t ?? FloraStand(q.plant, q.foot, q.height, q.radius);
                if (q.t == null) BlastFloraFelled++;
                FellByBlast(t, blastAt); begun++;
                float oneMs = (float)one.Elapsed.TotalMilliseconds; if (oneMs > slowMs) { slowMs = oneMs; slowName = (q.t != null ? "rule tree " : "") + (t.main ? t.main.name : "?"); }
            }
            BlastWaiting = blastQueue.Count;
            if (begun > BlastMostInFrame) BlastMostInFrame = begun;
            float ms = (float)sw.Elapsed.TotalMilliseconds; if (begun > 0 && ms > BlastBeginMsMost) BlastBeginMsMost = ms;
            if (ms > 8f && BlastSlowNotes.Count < 40) BlastSlowNotes.Add($"starting falls took {ms:F1} ms at blast + {Time.unscaledTime - frameFrom:F2} s: {begun} trees, the slowest {slowMs:F1} ms ({slowName})");
        }

        void OnFortBlast(Vector3 at, float fireTime)
        {
            blastAt = at; frameMs.Clear(); frameFrom = Time.unscaledTime; BlastSlowNotes.Clear();
            if (!BlastFell) { BlastLast = "blast at (" + at.x.ToString("F0") + ", " + at.z.ToString("F0") + "): tree felling switched off"; return; }
            if (!OpenZEnding.Running) { BlastLast = "blast at (" + at.x.ToString("F0") + ", " + at.z.ToString("F0") + "): the game is still on, no tree is felled by the view"; return; }
            var near = new List<BlastItem>();
            sceneSpotCount = OzL16LoungerSet.SceneSpots(at, sceneSpots);                                    // the set and the endings' camera
            int keptUp = 0, rules = 0, flora = 0;
            foreach (var t in trees)
            {
                if (t == null || t.fallen || !t.holder) continue;
                var foot = t.holder.position; Vector3 d = foot - at; d.y = 0f; float dist = d.magnitude;
                if (dist > BlastFellRadius) continue;
                if (FallsIntoScene(foot, t.height, t.radius)) { keptUp++; continue; }
                near.Add(new BlastItem { t = t, foot = foot, height = t.height, radius = t.radius, at = fireTime + OpenZFortShockwave.TimeAt(dist), dist = dist }); rules++;
            }
            if (BlastFellFlora)
                foreach (var f in floraTrees)
                {   // the level's other trees (read at the level start; one that went away or fell meanwhile is passed over)
                    if (!f.plant || !f.plant.gameObject.activeInHierarchy || f.plant.parent == null || !f.plant.parent.name.StartsWith("T_")) continue;
                    Vector3 d = f.foot - at; d.y = 0f; float dist = d.magnitude;
                    if (dist > BlastFellRadius) continue;
                    if (FallsIntoScene(f.foot, f.height, f.radius)) { keptUp++; continue; }
                    near.Add(new BlastItem { plant = f.plant, foot = f.foot, height = f.height, radius = f.radius, at = fireTime + OpenZFortShockwave.TimeAt(dist), dist = dist }); flora++;
                }
            BlastNearScene = keptUp; BlastFloraInReach = flora;
            BlastInReach = near.Count + keptUp;                                                            // in reach = to fall + left by a cap + kept up
            near.Sort((a, b) => a.dist.CompareTo(b.dist));
            BlastOverMost = BlastFellMost > 0 ? Mathf.Max(0, near.Count - BlastFellMost) : 0;
            if (BlastFellMost > 0 && near.Count > BlastFellMost) near.RemoveRange(BlastFellMost, near.Count - BlastFellMost);
            near.Sort((a, b) => a.at.CompareTo(b.at));
            blastQueue.Clear(); blastQueue.AddRange(near);
            BlastLast = $"blast at ({at.x:F0}, {at.z:F0}): {BlastInReach} trees within {BlastFellRadius:F0} m ({rules} of the rules, {flora} more of the flora to fall), {blastQueue.Count} to fall as the wave reaches them, "
                        + (BlastFellMost > 0 ? $"{BlastOverMost} left standing by the cap of {BlastFellMost}, " : "no total cap, ")
                        + $"{keptUp} kept up as their fall would reach the set, the endings' camera or the line between them ({sceneSpotCount} spots, crown + {BlastSceneClear:F0} m)";
            Debug.Log("[OpenZ] DV88 trees, the fort's blast: " + BlastLast);
        }

        /// <summary>The tree's fall (from its foot, away from the blast, its height + its crown long) comes within its crown +
        /// BlastSceneClear of the set, an endings' camera spot, or the line from the set to such a spot.</summary>
        bool FallsIntoScene(Vector3 foot, float height, float radius)
        {
            if (sceneSpotCount <= 0) return false;
            for (int i = 0; i < sceneSpotCount; i++)
                if (FallReaches(foot, height, radius, sceneSpots[0], sceneSpots[i])) return true;
            return false;
        }

        /// <summary>Flat test: does the fall line [foot, foot + away x (height + radius)] come within radius + BlastSceneClear of the
        /// segment a-b (a point when a == b)?</summary>
        bool FallReaches(Vector3 foot, float height, float radius, Vector3 a, Vector3 b)
        {
            var away = foot - blastAt; away.y = 0f;
            away = away.sqrMagnitude > 0.0004f ? away.normalized : Vector3.forward;
            float reach = height + radius, clear = radius + BlastSceneClear;
            Vector2 f0 = new Vector2(foot.x, foot.z), f1 = f0 + new Vector2(away.x, away.z) * reach, s0 = new Vector2(a.x, a.z), s1 = new Vector2(b.x, b.z);
            return SegDist(f0, f1, s0, s1) < clear;
        }

        static float SegDist(Vector2 p0, Vector2 p1, Vector2 q0, Vector2 q1)
        {
            float d = Mathf.Min(Mathf.Min(PtSeg(p0, q0, q1), PtSeg(p1, q0, q1)), Mathf.Min(PtSeg(q0, p0, p1), PtSeg(q1, p0, p1)));
            // crossing segments: distance 0
            Vector2 r = p1 - p0, s = q1 - q0; float den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) > 1e-6f)
            {
                Vector2 w = q0 - p0; float u = (w.x * s.y - w.y * s.x) / den, v = (w.x * r.y - w.y * r.x) / den;
                if (u >= 0f && u <= 1f && v >= 0f && v <= 1f) return 0f;
            }
            return d;
        }

        static float PtSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a; float l = ab.sqrMagnitude;
            float k = l > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l) : 0f;
            return (p - (a + ab * k)).magnitude;
        }

        /// <summary>A plant of the flora gets a holder at its foot the moment it falls (as the trees of the rules have from the start).</summary>
        Stand FloraStand(Transform p, Vector3 foot, float height, float radius)
        {
            var cellT = p.parent;
            var t = new Stand { tx = Mathf.FloorToInt(foot.x * 0.5f), ty = Mathf.FloorToInt(-foot.z * 0.5f), main = p, cell = cellT ? cellT.GetComponent<LODGroup>() : null, height = Mathf.Max(1f, height), radius = radius, flora = true };
            var mr = p.GetComponent<Renderer>();
            var h = new GameObject("TreeBlast_" + p.name).transform; h.SetParent(cellT, false); h.position = foot; t.holder = h;
            t.plants.Add(p); t.boxes.Add(mr ? mr.bounds : new Bounds(foot, Vector3.one)); p.SetParent(h, true);
            return t;
        }

        /// <summary>The tree breaks and falls away from the blast (view only: the same steps as a tree the rules destroy).</summary>
        void FellByBlast(Stand t, Vector3 from)
        {
            t.fallen = true; Falls++; BlastFelled++;
            var foot = t.holder.position; var away = Away(t, foot, from);
            if (!BlastThrow) DetailProp.Play("dtl_tree_break", Crack, foot + Vector3.up * (t.height * 0.3f), 0.8f, Random.Range(0.92f, 1.08f), -1, 0.05f);   // item 214: a thrown tree's crack is in the grouped sound layer (OzTreeFall.Throw.cs)
            var rng = new System.Random(t.tx * 131 + t.ty * 17 + BlastFelled);
            var top = foot + Vector3.up * (t.height * 0.6f);
            if (bits != null) { bits.Burst(top, 10, 2, 3.2f, 0.24f, rng); bits.Burst(foot + Vector3.up * (t.height * 0.25f), 8, 0, 2.4f, 0.2f, rng); }
            // the game is over and this is the view only: the tree's cell keeps its near objects from now on (folding the tree out of
            // the cell's far mesh would copy that whole mesh in this frame)
            if (t.cell && nearOnly.Add(t.cell)) t.cell.ForceLOD(0);
            FarForced++;
            OzFloraWind.Still(t.holder.gameObject);
            // the tree falls whole from its foot, no stump is cut (the cut of a few thousand points costs 8 to 28 ms for one tree, and
            // hundreds fall in the wave; a tree the rules fell during the game keeps its stump as before)
            if (BlastThrow) Throw(t, away, from); else StartCoroutine(Fall(t, away, 1f, 0f, null));   // item 214: thrown, snapped and thrown, or broken over (OzTreeFall.Throw.cs)
            Log(t, "falls in the fort's blast");
        }

        /// <summary>Play check: the blast's tree felling in one line.</summary>
        public static string BlastStatus() => $"blast trees: {(BlastFell ? "on" : "OFF")}, reach {BlastFellRadius:F0} m, at most {BlastFellPerFrame} a frame, {(BlastFellMost > 0 ? BlastFellMost + " in all" : "no total cap")}, flora {(BlastFellFlora ? "on" : "off")}; {BlastLast}; felled {BlastFelled} (of the flora {BlastFloraFelled}), waiting {BlastWaiting}, most begun in one frame {BlastMostInFrame}, frames a tree waited for the pace {BlastCapFrames}, kept up {BlastNearScene}";

        /// <summary>Play check: the frames from the blast for FrameWindow seconds (the before / after count).</summary>
        public static string BlastFrameStatus()
        {
            if (!I || I.frameMs.Count == 0) return "blast frames: none recorded yet";
            var a = new List<float>(I.frameMs); a.Sort();
            int n = a.Count, over = 0; foreach (var ms in a) if (ms > 50f) over++;
            return $"blast frames: {n} frames in the {FrameWindow:F0} s after the blast, median {a[n / 2]:F2} ms, 95th share {a[Mathf.Min(n - 1, (int)(n * 0.95f))]:F2} ms, longest {a[n - 1]:F2} ms, over 50 ms {over}; tree felling {(BlastFell ? "ON" : "OFF")}, felled {BlastFelled}; the most one frame spent starting falls {BlastBeginMsMost:F1} ms, landing them {BlastLandMsMost:F1} ms";
        }
    }
}
