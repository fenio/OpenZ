using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1 (item 375 for the shuttle): the shot-down shuttle's pieces as dark scorched broken metal in the ship's own paint, the
    /// way the fort cannon's pieces are (OzFortGunBreakup.Fracture.cs, OzFractureCut). Before, the hull panels showed their insides and
    /// cut faces as light grey ribbed metal with a rivet grid (128 px tiles every 1.2 m: a light grid on every panel at the RTS camera).
    /// Now (switch <see cref="ShellLook"/>, on; off = exactly the look before):
    /// <list type="bullet">
    /// <item>the rear body's cuts are torn, not straight (OzFractureCut.Tear: jagged, stepped, the painted lip bent in and sooted), made
    /// <see cref="ShellOnWorker"/></item>
    /// <item>every hull panel is a plate with thickness: its inside dark scorched broken steel (the cannon's broken-steel maps
    /// FortGun_Fracture_*, at 3 m a repeat with a turn of its own per panel: no grid, no visible repeat), sooty near the tear, rust and
    /// temper colours along the rim, and a wall along every torn edge (OzFractureCut.Plate);</item>
    /// <item>the dome halves' shard cut is the cannon's relief cut (OzFractureCut.ReliefQuiet) with the same broken steel;</item>
    /// <item>the paint of the cut pieces shows the soot near the tears (their vertex colours on: <see cref="ShellPaint"/>);</item>
    /// <item>the ember rim follows the torn edge on the paint, the inside and the wall, and cools as before.</item>
    /// </list>
    /// Also here: a piece coming to rest never stays inside a building, gun or rock (<see cref="OutOfStatics"/>, the statics switched on
    /// round it first), the resting pieces' clearing pass sees them (<see cref="ArmStaticsFor"/>), and the crew's landing on a piece
    /// counts only on a piece that lies still (<see cref="CrewTouchOf"/>). View only.
    /// </summary>
    public sealed partial class OzShuttleBreakup
    {
        // ------------------------------------------------------------------ switches and numbers
        public static bool ShellLook = true;
        /// <summary>The broken steel's colour (sRGB, x its texture and the vertex colours): about the level of the ship's burnt paint.</summary>
        public static Color ShellColor = new Color(0.95f, 0.92f, 0.89f, 1f);   // K419a: was 0.85, 0.82, 0.80: (Proof K's K2 read 43)
        public static float ShellMetallic = 0.45f, ShellSmoothness = 0.30f;
        public const string ShellTexDir = "OpenZ/FortV2/Wreck/";
        /// <summary>Proof: the plan's torn cuts, plates and relief cuts made here, the slowest of its steps (ms), the pieces whose inside wears
        /// the broken steel, the pieces whose paint shows their soot, the material state.</summary>
        public static int ShellCutsMade, ShellPlates, ShellReliefs, ShellFaces, ShellPaintVc;
        public static float ShellStepMs;
        public static string ShellMatState = "-";

        /// <summary>The numbers of one cut or plate, from the part itself (the same plan on every level start, whatever ran before).</summary>
        static System.Random ShellRnd(int salt, OzMeshSlice.Part p, Vector3 n, float w)
        {
            var c = p != null ? p.Box().center : Vector3.zero;
            unchecked
            {
                int h = 375 * 7919 + salt;
                h = h * 31 + (p != null ? p.v.Count : 0);
                h = h * 31 + Mathf.RoundToInt(c.x * 100f); h = h * 31 + Mathf.RoundToInt(c.y * 100f); h = h * 31 + Mathf.RoundToInt(c.z * 100f);
                h = h * 31 + Mathf.RoundToInt(n.x * 1000f); h = h * 31 + Mathf.RoundToInt(n.y * 1000f); h = h * 31 + Mathf.RoundToInt(n.z * 1000f) + Mathf.RoundToInt(w * 100f);
                return new System.Random(h & 0x7fffffff);
            }
        }

        /// <summary>
        /// frame on the main thread). The main thread only builds the panels' meshes and rims, one a frame.</summary>
        public static bool ShellOnWorker = true;
        /// <summary></summary>
        public static float ShellWorkerMs; public static int ShellWaitFrames, ShellForcedWaits;

        /// <summary>
        /// (OzShuttleBreakup.Look.cs BodyShells' planes and names), then each panel a plate with its rim strips (all managed math).</summary>
        sealed class ShellJob
        {
            public OzMeshSlice.Part body;
            public readonly List<(string name, OzMeshSlice.Part part, RimData rim)> panels = new List<(string, OzMeshSlice.Part, RimData)>();
            public volatile bool done; public string error; public float ms;
            public readonly System.Threading.ManualResetEventSlim finished = new System.Threading.ManualResetEventSlim(false);

            public IEnumerator<bool> Work()
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var work = new List<(OzMeshSlice.Part part, string name)> { (body, "Body") };
                float zMid = body.Box().center.z;
                var planes = new (Vector3 n, float w, string a, string b)[] { (Vector3.right, 0f, "R", "L"), (Vector3.up, RoofCutY, "Roof", "Low"), (Vector3.forward, zMid, "F", "B") };
                foreach (var pl in planes)
                {
                    var next = new List<(OzMeshSlice.Part part, string name)>();
                    foreach (var it in work)
                    {
                        if (OzFractureCut.Tear(it.part, pl.n, pl.w, ShellRnd(1000, it.part, pl.n, pl.w), out var front, out var back) && front != null && back != null)
                        { next.Add((front, it.name + "_" + pl.a)); next.Add((back, it.name + "_" + pl.b)); ShellCutsMade++; }
                        else next.Add(it);
                        yield return true;
                    }
                    work = next;
                }
                foreach (var it in work)
                {
                    var edges = OzFractureCut.Plate(it.part, ShellRnd(2000, it.part, Vector3.zero, 0f));
                    panels.Add((it.name, it.part, RimData.Of(edges))); ShellPlates++; ShellPieces++;
                    yield return true;
                }
                ms = (float)clock.Elapsed.TotalMilliseconds;
            }
        }

        /// <summary>
        /// anything wrong: the panels as before (BodyShells).</summary>
        static IEnumerator<bool> ShellSteps(OzMeshSlice.Part bp, Material mat)
        {
            if (!ShellLook || bp == null)
            {
                var old = BodyShells(bp, mat);
                while (old.MoveNext()) yield return true;
                yield break;
            }
            rims.Clear();
            var job = new ShellJob { body = bp };
            var it = job.Work();
            if (ShellOnWorker)
            {
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { while (it.MoveNext()) { } }
                    catch (System.Exception e) { job.error = e.GetType().Name + ": " + e.Message; }
                    finally { job.done = true; job.finished.Set(); }
                });
                int frame = -1;
                while (!job.done)
                {
                    if (Time.frameCount == frame) { ShellForcedWaits++; job.finished.Wait(); break; }       // asked twice in one frame: the kill frame finishing the plan
                    frame = Time.frameCount; ShellWaitFrames++;
                    yield return true;
                }
            }
            else
            {
                while (true)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew(); bool more;
                    try { more = it.MoveNext(); } catch (System.Exception e) { job.error = e.GetType().Name + ": " + e.Message; more = false; }
                    ShellStepMs = Mathf.Max(ShellStepMs, (float)sw.Elapsed.TotalMilliseconds);
                    if (!more) break;
                    yield return true;
                }
            }
            ShellWorkerMs = Mathf.Max(ShellWorkerMs, job.ms);
            if (job.error != null || job.panels.Count == 0)
            {
                Debug.LogError("[OpenZ] shuttle pieces: the torn panels failed (" + (job.error ?? "no panel") + "): the panels as before");
                rims.Clear();
                var old = BodyShells(bp, mat);
                while (old.MoveNext()) yield return true;
                yield break;
            }
            var sizes = new System.Text.StringBuilder(); ShellMaxM = 0f;
            foreach (var (name, part, rim) in job.panels)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int before = plan.Count;
                Add(name, "body", part, mat);
                if (plan.Count > before)
                {
                    var rm = rim.ToMesh(); if (rm) rims[plan[plan.Count - 1].mesh] = rm;
                    var s = part.Box().size; ShellMaxM = Mathf.Max(ShellMaxM, Mathf.Max(s.x, Mathf.Max(s.y, s.z)));
                    sizes.Append(name).Append(' ').Append(s.x.ToString("F2")).Append(" x ").Append(s.y.ToString("F2")).Append(" x ").Append(s.z.ToString("F2")).Append(", ");
                }
                ShellStepMs = Mathf.Max(ShellStepMs, (float)sw.Elapsed.TotalMilliseconds);
                yield return true;
            }
            LastShellSizes = sizes.ToString();
        }

        /// <summary>Steps: a dome half's shard cut (a closed part: the relief cut when the look is on).</summary>
        static bool DomeCut(OzMeshSlice.Part part, Vector3 n, float w, out OzMeshSlice.Part shard, out OzMeshSlice.Part rest)
        {
            if (!ShellLook) return OzMeshSlice.Split(part, n, w, out shard, out rest);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool ok = OzFractureCut.ReliefQuiet(part, n, w, ShellRnd(3000, part, n, w), out shard, out rest);
            ShellStepMs = Mathf.Max(ShellStepMs, (float)sw.Elapsed.TotalMilliseconds);
            if (ok) ShellReliefs++;
            return ok;
        }

        /// <summary><see cref="RimWidthM"/>
        /// wide on the paint (lifted off it), on the inside (under the plate) and over the wall.</summary>
        sealed class RimData
        {
            readonly List<Vector3> vs = new List<Vector3>(); readonly List<Vector3> ns = new List<Vector3>(); readonly List<Vector2> uv = new List<Vector2>(); readonly List<int> tr = new List<int>();

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
            {
                int i0 = vs.Count; vs.Add(a); vs.Add(b); vs.Add(c); vs.Add(d);
                for (int i = 0; i < 4; i++) ns.Add(facing);
                uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(1f, 1f)); uv.Add(new Vector2(0f, 1f));
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) > 0f) { tr.Add(i0); tr.Add(i0 + 1); tr.Add(i0 + 2); tr.Add(i0); tr.Add(i0 + 2); tr.Add(i0 + 3); }
                else { tr.Add(i0); tr.Add(i0 + 2); tr.Add(i0 + 1); tr.Add(i0); tr.Add(i0 + 3); tr.Add(i0 + 2); }
            }

            public static RimData Of(List<OzFractureCut.TornEdge> edges)
            {
                var r = new RimData(); float t = OzFractureCut.PlateM;
                if (edges == null) return r;
                foreach (var e in edges)
                {
                    var w = e.into * RimWidthM; var up = e.n * RimLiftM; var down = -e.n * (t + RimLiftM); var outw = -e.into * RimLiftM;
                    r.Quad(e.a + up, e.b + up, e.b + w + up, e.a + w + up, e.n);                                    // on the paint
                    r.Quad(e.a + down, e.b + down, e.b + w + down, e.a + w + down, -e.n);                            // on the inside
                    r.Quad(e.a + outw, e.b + outw, e.b - e.n * t + outw, e.a - e.n * t + outw, -e.into);             // over the wall
                }
                return r;
            }

            public Mesh ToMesh()
            {
                if (vs.Count == 0) return null;
                var m = new Mesh { name = "OZ_ShuttlePieceRim", indexFormat = vs.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(vs); m.SetNormals(ns); m.SetUVs(0, uv); m.SetTriangles(tr, 0, true);
                return m;
            }
        }

        // ------------------------------------------------------------------ materials
        static Material shellMat; static Shader shellFor; static Texture2D sAlb, sNrm, sMask; static bool sLoaded, sOk;
        static readonly int UseVcId = Shader.PropertyToID("_UseVertexColor"), VcSrgbId = Shader.PropertyToID("_VertexColorSRGB");
        static MaterialPropertyBlock vcBlock;

        static bool ShellLoad()
        {
            if (sLoaded) return sOk;
            sLoaded = true;
            sAlb = Resources.Load<Texture2D>(ShellTexDir + "FortGun_Fracture_Albedo");
            sNrm = Resources.Load<Texture2D>(ShellTexDir + "FortGun_Fracture_Normal");
            sMask = Resources.Load<Texture2D>(ShellTexDir + "FortGun_Fracture_Mask");
            sOk = sAlb && sNrm && sMask;
            ShellMatState = sOk ? "broken-steel maps loaded" : "broken-steel maps MISSING under Resources/" + ShellTexDir + ": the insides keep the ribbed look";
            if (!sOk) Debug.LogWarning("[OpenZ] shuttle pieces: " + ShellMatState);
            return sOk;
        }

        /// <summary>Build: the inside / cut faces of a piece: dark scorched broken steel in the hull's own shader when the look is on (else
        /// the ribbed inside as before).</summary>
        internal static Material ShellInnerMat(Material like)
        {
            if (!ShellLook || !like || !ShellLoad()) return InnerMat(like);
            ShellFaces++;
            if (shellMat && shellFor == like.shader) return shellMat;
            var m = new Material(like) { name = "OZ_ShuttleWreckFracture" };
            if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", sAlb); m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero); }
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", sAlb);
            if (m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", sNrm); SetF(m, "_UseNormalMap", 1f); }
            if (m.HasProperty("_MaskMap")) { m.SetTexture("_MaskMap", sMask); SetF(m, "_UseMaskMap", 1f); }
            foreach (var p in PlainSwitches) if (p != "_UseVertexColor" && p != "_UseMaskMap" && p != "_UseNormalMap") SetF(m, p, 0f);
            foreach (var p in new[] { "_Degreen", "_UnitLook", "_VertexColorSRGB" }) SetF(m, p, 0f);
            SetF(m, "_UseVertexColor", 1f);
            SetF(m, "_Metallic", ShellMetallic); SetF(m, "_Smoothness", ShellSmoothness);
            SetC(m, "_EmissionColor", Color.black); SetC(m, "_EmissionMapColor", Color.black);
            SetC(m, "_BaseColor", ShellColor); SetC(m, "_Color", ShellColor);
            shellMat = m; shellFor = like.shader;
            return m;
        }

        /// <summary>Build: the paint on a piece's sub-mesh i; a piece cut here (its mesh has colours: the soot near the tears) shows them.</summary>
        static void ShellPaint(Renderer r, MaterialPropertyBlock paint, int i, Mesh mesh)
        {
            r.SetPropertyBlock(paint, i);
            if (!ShellLook || !mesh || !mesh.HasVertexAttribute(VertexAttribute.Color)) return;
            vcBlock ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(vcBlock, i); vcBlock.SetFloat(UseVcId, 1f); vcBlock.SetFloat(VcSrgbId, 0f); r.SetPropertyBlock(vcBlock, i);
            if (i == 0) ShellPaintVc++;
        }

        // ------------------------------------------------------------------ the drawn metal (the crew and the proofs ask it)
        static readonly Dictionary<Mesh, (Vector3[] v, int[] t)> drawn = new Dictionary<Mesh, (Vector3[] v, int[] t)>();

        /// <summary>The distance (m) from `origin` along `dir` (a unit vector) to this piece's drawn surface (its paint, inside or walls),
        /// within `max`; float.MaxValue when the ray misses it. Its collider is the convex hull: over a panel's hollow it lies far above the
        /// metal.</summary>
        public float DrawnDistance(Vector3 origin, Vector3 dir, float max)
        {
            var mf = vis ? vis.GetComponent<MeshFilter>() : null; var m = mf ? mf.sharedMesh : null;
            if (!m || !m.isReadable) return float.MaxValue;
            if (!drawn.TryGetValue(m, out var d)) { d = (m.vertices, m.triangles); drawn[m] = d; }
            var w2l = vis.worldToLocalMatrix; var o = w2l.MultiplyPoint3x4(origin); var dv = w2l.MultiplyVector(dir);
            float k = dv.magnitude; if (k < 1e-8f) return float.MaxValue; dv /= k;
            float best = max * k, hit = float.MaxValue;
            var v = d.v; var t = d.t;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                var a = v[t[i]]; var e1 = v[t[i + 1]] - a; var e2 = v[t[i + 2]] - a;
                var pv = Vector3.Cross(dv, e2); float det = Vector3.Dot(e1, pv);
                if (det > -1e-9f && det < 1e-9f) continue;
                float inv = 1f / det; var tv = o - a; float u = Vector3.Dot(tv, pv) * inv; if (u < 0f || u > 1f) continue;
                var qv = Vector3.Cross(tv, e1); float vv = Vector3.Dot(dv, qv) * inv; if (vv < 0f || u + vv > 1f) continue;
                float s = Vector3.Dot(e2, qv) * inv; if (s >= 0f && s < best) { best = s; hit = s; }
            }
            return hit < float.MaxValue ? hit / k : float.MaxValue;
        }

        // ------------------------------------------------------------------ resting out of the buildings, guns and rocks
        /// <summary>A piece comes to rest at most this deep in a building, gun or rock (m); deeper, it is moved out (at most
        /// <see cref="StaticClearMaxM"/> m, never down), to touching.</summary>
        public static float StaticClearFromM = 0.02f, StaticClearMaxM = 0.8f;
        /// <summary>Proof: pieces moved out of a static or rock at rest, the largest move (m), the last one.</summary>
        public static int StaticClears; public static float StaticClearLargest; public static string StaticClearLast = "-";
        static readonly Collider[] staticHits = new Collider[32];

        /// <summary>Freeze: the statics and rocks round the piece are switched on (their colliders made at once where baked), and a piece
        /// lying more than <see cref="StaticClearFromM"/> inside one (a pod pressed into a factory wall) is moved out along the shortest way
        /// (its downward part dropped) until it only touches. The physics keeps a moving piece out; this keeps the resting one out.</summary>
        void OutOfStatics()
        {
            if (!col || !col.enabled || OzRockSolid.Layer < 0 || !OzRockSolid.AllOn) return;
            var b0 = col.bounds; float reach = Mathf.Max(b0.extents.x, b0.extents.z) + 2f;
            OzRockSolid.ArmAround(b0.center, reach, 1.5f, reach);
            Physics.SyncTransforms();
            var total = Vector3.zero; string what = "-"; float first = 0f;
            for (int it = 0; it < 4; it++)
            {
                var b = col.bounds; float deep = 0f; var way = Vector3.zero; Collider worst = null;
                int n = Physics.OverlapBoxNonAlloc(b.center, b.extents + Vector3.one * 0.02f, staticHits, Quaternion.identity, OzRockSolid.Mask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var rc = staticHits[i]; if (!rc || !rc.enabled || !OzRockSolid.IsRock(rc)) continue;
                    if (Physics.ComputePenetration(col, col.transform.position, col.transform.rotation, rc, rc.transform.position, rc.transform.rotation, out var dir, out float d) && d > deep)
                    { deep = d; way = dir; worst = rc; }
                }
                if (it == 0) first = deep;
                if (deep <= StaticClearFromM) break;
                if (way.y < 0f) { float flat = new Vector2(way.x, way.z).magnitude; if (flat < 0.2f) break; deep /= flat; way.y = 0f; way.Normalize(); }
                var move = way * (deep - 0.005f);
                if ((total + move).magnitude > StaticClearMaxM) move = Vector3.ClampMagnitude(total + move, StaticClearMaxM) - total;
                if (move.sqrMagnitude < 1e-8f) break;
                transform.position += move; total += move; Physics.SyncTransforms();
                what = worst ? (OzStaticSolid.IsStatic(worst) ? OzStaticSolid.StaticOf(worst) : worst.transform.root.name) : "-";
            }
            if (total.sqrMagnitude < 1e-8f) return;
            StaticClears++; StaticClearLargest = Mathf.Max(StaticClearLargest, total.magnitude);
            StaticClearLast = $"{PieceName} out of {what}: {first * 100f:F1} cm deep, moved {total.magnitude * 100f:F1} cm";
        }

        /// <summary>RestClear, as a pass begins: the statics and rocks round the whole wreck are switched on, so the pass's scenery test sees
        /// the buildings and guns too (never a piece pushed into one).</summary>
        static void ArmStaticsFor(Group g)
        {
            if (g == null || !OzRockSolid.AllOn) return;
            var c = Vector3.zero; int n = 0; foreach (var p in g.pieces) if (p) { c += p.transform.position; n++; }
            if (n == 0) return;
            c /= n; float r = 0f; foreach (var p in g.pieces) if (p) r = Mathf.Max(r, Vector3.Distance(p.transform.position, c) + p.Size);
            OzRockSolid.ArmAround(c, r + 2f, 2f, r + 2f);
        }

        // ------------------------------------------------------------------ the crew's landing on a piece
        /// <summary>Switch: a crew body's contact with a piece is its landing only when that piece lies still (on: a piece that has touched down
        /// once but still tumbles is a knock in the air, as a flying one).</summary>
        public static bool CrewStrict = true;
        public static float CrewStillMps = 0.6f, CrewStillSpin = 1.0f;
        /// <summary>Proof: crew contacts with a tumbling piece that the plain rule would have taken as a landing.</summary>
        public static int CrewKnocks;

        /// <summary>OzShuttleCrewLanding: what a collider is to a falling crew body. As <see cref="TouchOf"/>, except that a piece counts as
        /// lying only when it is at rest, lies still, or has nearly stopped (slower than <see cref="CrewStillMps"/> m/s and
        /// <see cref="CrewStillSpin"/> rad/s): the thud is never asked 3-6 m up on a piece that then falls on with the body.</summary>
        internal static OzShuttleDeathPlan.Touch CrewTouchOf(Collider other)
        {
            var t = TouchOf(other);
            if (!CrewStrict || t != OzShuttleDeathPlan.Touch.Resting || !other) return t;
            var host = other.GetComponentInParent<OzShuttleBreakup>();
            if (!host || host.keeper) return t;
            if (host.frozen || host.Rested || host.LyingStill()) return t;
            var b = host.rb;
            if (b && b.linearVelocity.sqrMagnitude < CrewStillMps * CrewStillMps && b.angularVelocity.sqrMagnitude < CrewStillSpin * CrewStillSpin) return t;
            CrewKnocks++;
            return OzShuttleDeathPlan.Touch.Flying;
        }

        // ------------------------------------------------------------------ checks (the K256k Play report)
        /// <summary>Play check: the last break's resting pieces against the buildings, guns and rocks round them (switched on first): the
        /// deepest exact overlap of a piece's collider with one (m) and which; and the pieces with a point of their DRAWN shape (up to 64 of
        /// their vertices, 5 cm in toward their middle) inside a static's or rock's drawn shape (the K331b test on the real shape instead of
        /// the box corners).</summary>
        public static float RestDepthInStatics(out string which, out int sampleIn, out string sampleWhich)
        {
            which = "-"; sampleIn = 0; sampleWhich = "-"; float worst = 0f;
            if (OzRockSolid.Layer < 0 || !OzRockSolid.AllOn) return 0f;
            foreach (var pc in LastPieces) if (pc && pc.col && pc.col.enabled) { var b = pc.col.bounds; OzRockSolid.ArmAround(b.center, Mathf.Max(b.extents.x, b.extents.z) + 2f, 3f, Mathf.Max(b.extents.x, b.extents.z) + 2f); }
            Physics.SyncTransforms();
            foreach (var pc in LastPieces)
            {
                if (!pc || !pc.col || !pc.col.enabled || pc.retiring) continue;
                var b = pc.col.bounds;
                int n = Physics.OverlapBoxNonAlloc(b.center, b.extents + Vector3.one * 0.02f, staticHits, Quaternion.identity, OzRockSolid.Mask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var rc = staticHits[i]; if (!rc || !rc.enabled || !OzRockSolid.IsRock(rc)) continue;
                    if (Physics.ComputePenetration(pc.col, pc.col.transform.position, pc.col.transform.rotation, rc, rc.transform.position, rc.transform.rotation, out _, out float d) && d > worst)
                    { worst = d; which = pc.PieceName + " in " + (OzStaticSolid.IsStatic(rc) ? OzStaticSolid.StaticOf(rc) : rc.transform.root.name); }
                }
                pc.World(); if (wpts.Count == 0) continue;
                var mid = b.center; int step = Mathf.Max(1, wpts.Count / 64);
                for (int k = 0; k < wpts.Count; k += step)
                {
                    var q = wpts[k]; var to = mid - q; float m = to.magnitude; if (m > 0.05f) q += to / m * 0.05f;
                    if (OzRockSolid.InsideRock(q, out var w)) { sampleIn++; sampleWhich = pc.PieceName + " in " + w; break; }
                }
            }
            return worst;
        }

        /// <summary>Play check: the last break's pieces: (capped pieces, those whose inside wears the broken steel, those still on the ribbed
        /// inside), meshes with colours whose paint shows them, the triangles of all pieces.</summary>
        public static void LookCounts(out int capped, out int steel, out int ribbed, out int vcMeshes, out int vcShown, out int tris)
        {
            capped = steel = ribbed = vcMeshes = vcShown = tris = 0;
            var blk = new MaterialPropertyBlock();
            foreach (var pc in LastPieces)
            {
                if (!pc || !pc.vis) continue;
                var r = pc.vis.GetComponent<MeshRenderer>(); var mf = pc.vis.GetComponent<MeshFilter>(); var m = mf ? mf.sharedMesh : null;
                if (!r || !m) continue;
                for (int s = 0; s < m.subMeshCount; s++) tris += (int)(m.GetIndexCount(s) / 3);
                var mats = r.sharedMaterials;
                if (m.subMeshCount > 1 && mats.Length > 1)
                {
                    capped++; var last = mats[mats.Length - 1];
                    if (last && last.name.StartsWith("OZ_ShuttleWreckFracture")) steel++;
                    else if (last && last.name.StartsWith("OZ_ShuttleWreckInner")) ribbed++;
                }
                if (m.HasVertexAttribute(VertexAttribute.Color))
                {
                    vcMeshes++; r.GetPropertyBlock(blk, 0);
                    if (blk.GetFloat(UseVcId) > 0.5f) vcShown++;
                }
            }
        }

        /// <summary>Play check: a point's height over the ground or the water under it (m).</summary>
        public static float OverSurfaceOf(Vector3 p) => OverSurface(p);

        /// <summary>One line for the proofs and the log.</summary>
        public static string ShellState => $"shuttle pieces look {(ShellLook ? "on" : "OFF")} ({ShellMatState}): {OzFractureCut.ShellState}; plan steps made here: torn cuts {ShellCutsMade}, plates {ShellPlates}, " +
            $"relief cuts {ShellReliefs}, the heaviest main-thread step {ShellStepMs:0.0} ms, the background thread {ShellWorkerMs:0} ms (frames waiting {ShellWaitFrames}, waits on a kill frame {ShellForcedWaits}); insides in broken steel {ShellFaces}, paints showing their soot {ShellPaintVc}; out of a static at rest {StaticClears} (largest {StaticClearLargest * 100f:F1} cm; last: {StaticClearLast}); crew knocks on tumbling pieces {CrewKnocks}";
    }
}
