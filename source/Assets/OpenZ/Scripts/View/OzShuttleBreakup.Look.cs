using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// The look of the shuttle's break-up (rule DV118, view only; the third round). At 70 m the pieces were near-black slabs (paint burnt
    /// 85 %, the cut faces closed by the wreck's black core) with nothing burning once they lay. Now:
    /// <list type="bullet">
    /// <item>the pieces keep the hull's own paint, lightly burnt (<see cref="PaintCharred"/>: x0.65 of the paint);</item>
    /// <item>the rear body breaks into eight curved hull panels: open cuts down its middle, at the roof line and across its length (each
    /// panel at most about 2.7 m, under half the ship's 6.4 m width), never a capped block. Every panel shows its inside: grey ribbed
    /// metal (<see cref="InnerMat"/>, albedo about 0.3, metallic 0.6, ribs every 0.3 m and frames every 0.6 m) and an ember-orange rim
    /// along its cuts that cools over <see cref="EmberSec"/> s. A capped cut elsewhere takes the same inside metal;</item>
    /// <item>the three biggest pieces (the biggest body panel of each side and the heavier engine pod) burn with a dark smoke column, the
    /// main one for 15 s, the others for 12-13 s (the wreck burn the vehicles use, sized for the RTS camera), with the fire crackle; the
    /// main one leaves a scorch where it lies; every other piece smoulders for 5 s;</item>
    /// <item>every piece's first contact is heard: the landing sound shuttle_piece_land (no gap, no cooldown) when the sound table has
    /// it, else the debris clank; one the table drops (its 0.04 s gap) is queued and played in turn.</item>
    /// </list>
    /// </summary>
    public sealed partial class OzShuttleBreakup
    {
        // ------------------------------------------------------------------ numbers
        /// <summary>The pieces' paint (TeamPaint.Charred at the break; the paint keeps 1 - 0.78 c of itself: 0.45 = x0.65).</summary>
        public static float PaintCharred = 0.05f;   // run 28 knob (Proof K painted 41/43/44/49 < 45, SPEC: 0.05 once): K419a 0.18; was 0.45 (x0.65 of the paint): x0.86, Break-up Proof K's painted faces 45-55
        /// <summary>The panels' inside: tint (sRGB; x the rib map: linear albedo about 0.29 between the ribs), metallic, smoothness, the rib
        /// map's tile (m).</summary>
        public static Color InnerTint = new Color(0.64f, 0.64f, 0.66f, 1f);
        public static float InnerMetallic = 0.6f, InnerSmoothness = 0.38f, RibTileM = 1.2f;
        /// <summary>The ember rim along the cuts: colour (linear HDR), gain, how long it glows (s), its width and its lift off the surface (m).</summary>
        public static Vector4 EmberColor = new Vector4(2.6f, 0.80f, 0.20f, 1f);
        public static float EmberGain = 2.2f, EmberSec = 5f, RimWidthM = 0.12f, RimLiftM = 0.015f;
        /// <summary>The fires: the main one's and the two others' seconds, how often a burning piece puffs (s), the rest's smoulder (s, how
        /// often, how strong).</summary>
        public static float FireSecMain = 15f, FireSecOther = 12.2f, FireEvery = 0.25f, SmoulderSec = 5f, SmoulderEvery = 0.6f, SmoulderK = 0.35f;
        /// <summary>A piece lying on a repair station's deck sinks away this long after it rests (s; its sink takes 3 s: gone within 4 s,
        /// the deck clear for the next vehicle).</summary>
        public static float DeckClearDelay = 0.6f;
        /// <summary>The roof cut's height over the pad soles (m) and the queued clanks' spacing and patience (s).</summary>
        public const float RoofCutY = 3.3f, ClankGapS = 0.05f, ClankLateS = 1.2f;

        // ------------------------------------------------------------------ counts (the Play proof reads them)
        public static int ShellPieces, RimPieces, FirePieces, Smoulderers, FireBurns, SmoulderPuffs, FireLoops, WreckFiresAtRest, MainScorches,
                          FirstContacts, FirstContactsHeard, QueuedClanks, QueuedDropped, DeckCleared;
        /// <summary>The body panels of the plan, and the largest side of any of them (m).</summary>
        public static string LastShellSizes = "-";
        public static float ShellMaxM;

        // ------------------------------------------------------------------ per piece
        MeshRenderer rimR; MaterialPropertyBlock rimBlock;
        float smoulderUntil, smoulderT, deckGone = -1f; bool mainFire;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly Dictionary<Mesh, Mesh> rims = new Dictionary<Mesh, Mesh>();

        /// <summary>A queued first-contact clank.</summary>
        sealed class Clank { public Vector3 at; public float vol, pitch, t; public int emitter; }

        /// <summary>A burning piece now (the proof's count of fires after the pieces lie).</summary>
        public bool BurningNow => bigFire && !retiring && age < burnUntil;
        public static int Burning() { int n = 0; foreach (var p in LastPieces) if (p && p.BurningNow) n++; return n; }
        /// <summary>The farthest and the mean distance (m, flat) of the last break's pieces from the ground point under where it broke.</summary>
        public static void Scatter(out float far, out float mean)
        {
            far = 0f; mean = 0f; int n = 0;
            foreach (var p in LastPieces)
            {
                if (!p) continue;
                var d = p.transform.position - LastBreakGround; d.y = 0f; float m = d.magnitude;
                far = Mathf.Max(far, m); mean += m; n++;
            }
            if (n > 0) mean /= n;
        }

        /// <summary>The last break's body panels: how wide their field is (m, the largest flat distance between two of them) and the
        /// farthest one from the ground under where it broke (m).</summary>
        public static void BodyField(out float wide, out float far)
        {
            wide = 0f; far = 0f;
            var bs = new List<Vector3>(); foreach (var p in LastPieces) if (p && p.Kind == "body") bs.Add(p.transform.position);
            foreach (var a in bs)
            {
                var d = a - LastBreakGround; d.y = 0f; far = Mathf.Max(far, d.magnitude);
                foreach (var b in bs) wide = Mathf.Max(wide, new Vector2(a.x - b.x, a.z - b.z).magnitude);
            }
        }

        // ------------------------------------------------------------------ the plan: eight curved body panels
        struct CutPlane { public Vector3 n; public float w; }

        /// <summary>Steps(): the rear body cut open (no caps) down its middle, at the roof line and across its length; each panel gets its
        /// inside (the outer surface again, turned round, in the cap sub-mesh: the inner metal) and its ember rim (a strip along the cuts).</summary>
        static IEnumerator<bool> BodyShells(OzMeshSlice.Part bp, Material mat)
        {
            rims.Clear();
            if (bp == null) yield break;
            var work = new List<(OzMeshSlice.Part part, string name, List<CutPlane> cuts)> { (bp, "Body", new List<CutPlane>()) };
            float zMid = bp.Box().center.z;
            var planes = new (Vector3 n, float w, string a, string b)[] { (Vector3.right, 0f, "R", "L"), (Vector3.up, RoofCutY, "Roof", "Low"), (Vector3.forward, zMid, "F", "B") };
            foreach (var pl in planes)
            {
                var next = new List<(OzMeshSlice.Part part, string name, List<CutPlane> cuts)>();
                foreach (var it in work)
                {
                    if (OzMeshSlice.Split(it.part, pl.n, pl.w, out var front, out var back, false, false) && front != null && back != null)
                    {
                        next.Add((front, it.name + "_" + pl.a, new List<CutPlane>(it.cuts) { new CutPlane { n = pl.n, w = pl.w } }));
                        next.Add((back, it.name + "_" + pl.b, new List<CutPlane>(it.cuts) { new CutPlane { n = pl.n, w = pl.w } }));
                    }
                    else next.Add(it);
                    yield return true;
                }
                work = next;
            }
            var sizes = new StringBuilder(); ShellMaxM = 0f;
            foreach (var it in work)
            {
                var shell = Shell(it.part, it.cuts, out var rim);
                int before = plan.Count;
                Add(it.name, "body", shell, mat);
                if (plan.Count > before)
                {
                    if (rim) rims[plan[plan.Count - 1].mesh] = rim;
                    var s = it.part.Box().size; ShellMaxM = Mathf.Max(ShellMaxM, Mathf.Max(s.x, Mathf.Max(s.y, s.z)));
                    sizes.Append(it.name).Append(' ').Append(s.x.ToString("F2")).Append(" x ").Append(s.y.ToString("F2")).Append(" x ").Append(s.z.ToString("F2")).Append(", ");
                }
                yield return true;
            }
            LastShellSizes = sizes.ToString();
        }

        /// <summary>A panel: its outer surface as cut, its inside (every outer triangle again, turned round, normals in, metre UVs for the
        /// rib map) in the cap sub-mesh, and the ember rim strip along the edges that lie on its cut planes.</summary>
        static OzMeshSlice.Part Shell(OzMeshSlice.Part p, List<CutPlane> cuts, out Mesh rim)
        {
            rim = null;
            int cs = p.CapSub; var cap = p.sub[cs]; cap.Clear();
            var box = p.Box(); float eps = Mathf.Max(1e-5f, Mathf.Max(box.size.x, Mathf.Max(box.size.y, box.size.z)) * 2e-5f);
            var edges = new List<(Vector3 a, Vector3 b, Vector3 into, Vector3 n)>();
            for (int s = 0; s < cs; s++)
            {
                var tri = p.sub[s]; int count = tri.Count;
                for (int k = 0; k + 2 < count; k += 3)
                {
                    var v0 = p.v[tri[k]]; var v1 = p.v[tri[k + 1]]; var v2 = p.v[tri[k + 2]];
                    var fn = Vector3.Cross(v1.p - v0.p, v2.p - v0.p);
                    if (fn.sqrMagnitude < 1e-14f) continue;
                    fn.Normalize();
                    int j = p.v.Count;
                    p.v.Add(Inner(v0, fn)); p.v.Add(Inner(v1, fn)); p.v.Add(Inner(v2, fn));
                    cap.Add(j); cap.Add(j + 2); cap.Add(j + 1);
                    foreach (var c in cuts)
                    {
                        bool o0 = Mathf.Abs(Vector3.Dot(c.n, v0.p) - c.w) <= eps, o1 = Mathf.Abs(Vector3.Dot(c.n, v1.p) - c.w) <= eps, o2 = Mathf.Abs(Vector3.Dot(c.n, v2.p) - c.w) <= eps;
                        if (o0 && o1 && !o2) edges.Add(Edge(v0, v1, v2.p));
                        else if (o1 && o2 && !o0) edges.Add(Edge(v1, v2, v0.p));
                        else if (o2 && o0 && !o1) edges.Add(Edge(v2, v0, v1.p));
                    }
                }
            }
            rim = RimMesh(edges);
            ShellPieces++;
            return p;
        }

        static OzMeshSlice.V Inner(OzMeshSlice.V v, Vector3 fn)
        {
            float ax = Mathf.Abs(fn.x), ay = Mathf.Abs(fn.y), az = Mathf.Abs(fn.z);
            Vector2 uv = ax >= ay && ax >= az ? new Vector2(v.p.z, v.p.y) : ay >= az ? new Vector2(v.p.x, v.p.z) : new Vector2(v.p.x, v.p.y);
            var n = (v.n.sqrMagnitude > 1e-8f ? v.n : fn).normalized;
            var tan = Vector3.Cross(-n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            return new OzMeshSlice.V { p = v.p - n * 0.003f, n = -n, u0 = uv / RibTileM, u1 = default, c = Color.white, t = new Vector4(tan.x, tan.y, tan.z, 1f) };
        }

        static (Vector3 a, Vector3 b, Vector3 into, Vector3 n) Edge(in OzMeshSlice.V a, in OzMeshSlice.V b, Vector3 third)
        {
            var e = b.p - a.p; float len = e.magnitude; e = len > 1e-6f ? e / len : Vector3.right;
            var q = third - a.p; var into = q - e * Vector3.Dot(q, e); into = into.sqrMagnitude > 1e-10f ? into.normalized : Vector3.up;
            var n = (a.n + b.n); n = n.sqrMagnitude > 1e-8f ? n.normalized : Vector3.Cross(e, into).normalized;
            return (a.p, b.p, into, n);
        }

        /// <summary>The ember strip: per cut edge a band RimWidthM wide running into the panel from the cut, on both faces (lifted off them).</summary>
        static Mesh RimMesh(List<(Vector3 a, Vector3 b, Vector3 into, Vector3 n)> edges)
        {
            if (edges.Count == 0) return null;
            var vs = new List<Vector3>(edges.Count * 8); var ns = new List<Vector3>(edges.Count * 8); var uv = new List<Vector2>(edges.Count * 8); var tr = new List<int>(edges.Count * 12);
            foreach (var (a, b, into, n) in edges)
            {
                foreach (float side in new[] { 1f, -1f })
                {
                    var lift = n * (RimLiftM * side); var w = into * RimWidthM;
                    int i0 = vs.Count;
                    vs.Add(a + lift); vs.Add(b + lift); vs.Add(b + w + lift); vs.Add(a + w + lift);
                    for (int i = 0; i < 4; i++) ns.Add(n * side);
                    uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(1f, 1f)); uv.Add(new Vector2(0f, 1f));
                    bool faces = Vector3.Dot(Vector3.Cross(b - a, w), n * side) > 0f;                      // a front face: cross(b - a, c - a) points out
                    if (faces) { tr.Add(i0); tr.Add(i0 + 1); tr.Add(i0 + 2); tr.Add(i0); tr.Add(i0 + 2); tr.Add(i0 + 3); }
                    else { tr.Add(i0); tr.Add(i0 + 2); tr.Add(i0 + 1); tr.Add(i0); tr.Add(i0 + 3); tr.Add(i0 + 2); }
                }
            }
            var m = new Mesh { name = "OZ_ShuttlePieceRim", indexFormat = vs.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(vs); m.SetNormals(ns); m.SetUVs(0, uv); m.SetTriangles(tr, 0, true);
            return m;
        }

        // ------------------------------------------------------------------ materials
        static Material innerMat, rimMat; static Shader innerFor, rimFor; static Texture2D ribMap;
        static void SetF(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }
        static void SetC(Material m, string p, Color c) { if (m.HasProperty(p)) m.SetColor(p, c); }
        static readonly string[] PlainSwitches = { "_TeamMode", "_Accent", "_PaintFromAlpha", "_OwnerPart", "_UseVertexColor", "_Triplanar", "_Stylize", "_UseMaskMap", "_UseNormalMap", "_Rust", "_LegacyHeavy", "_SeeThru", "_TopBlend", "_CreamToTeam" };

        /// <summary>The panels' inside and every cut face: grey ribbed metal in the hull's own shader (lit like the hull), no team paint.</summary>
        internal static Material InnerMat(Material like)
        {
            if (!like) return like;
            if (innerMat && innerFor == like.shader) return innerMat;
            var m = new Material(like) { name = "OZ_ShuttleWreckInner" };
            if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", RibMap()); m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero); }
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", RibMap());
            SetC(m, "_BaseColor", InnerTint); SetC(m, "_Color", InnerTint);
            SetF(m, "_Metallic", InnerMetallic); SetF(m, "_Smoothness", InnerSmoothness);
            SetC(m, "_EmissionColor", Color.black); SetC(m, "_EmissionMapColor", Color.black);
            foreach (var p in PlainSwitches) SetF(m, p, 0f);
            innerMat = m; innerFor = like.shader;
            return m;
        }

        /// <summary>The ember rim: a dark strip in the hull's shader whose emission (set per piece) glows orange and cools.</summary>
        static Material RimMat(Material like)
        {
            if (rimMat && rimFor == like.shader) return rimMat;
            var m = new Material(like) { name = "OZ_ShuttleEmberRim" };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            SetC(m, "_BaseColor", new Color(0.10f, 0.06f, 0.04f, 1f)); SetC(m, "_Color", new Color(0.10f, 0.06f, 0.04f, 1f));
            SetF(m, "_Metallic", 0f); SetF(m, "_Smoothness", 0.2f);
            foreach (var p in PlainSwitches) SetF(m, p, 0f);
            if (m.HasProperty("_EmissionColor")) m.SetVector("_EmissionColor", EmberColor * EmberGain);
            rimMat = m; rimFor = like.shader;
            return m;
        }

        /// <summary>The inside's map (128 px = RibTileM): brushed grey, a rib every 32 px (0.3 m) with its lit edge, a frame every 64 px
        /// (0.6 m) with its rivets.</summary>
        static Texture2D RibMap()
        {
            if (ribMap) return ribMap;
            const int N = 128;
            ribMap = new Texture2D(N, N, TextureFormat.RGBA32, true, false) { name = "OZ_ShuttleRibMap", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                    float v = 0.88f + 0.08f * ((h & 1023) / 1023f) + 0.03f * Mathf.Sin(y * 0.9f + x * 0.05f);
                    int rx = x % 32, fy = y % 64;
                    if (rx < 5) v *= 0.64f; else if (rx == 5) v = Mathf.Min(1f, v * 1.12f);
                    if (fy < 4) v *= 0.72f; else if (fy == 4) v = Mathf.Min(1f, v * 1.08f);
                    if ((fy == 1 || fy == 2) && rx % 8 == 3) v *= 0.6f;
                    byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f), bb = (byte)Mathf.RoundToInt(Mathf.Clamp01(v * 1.02f) * 255f);
                    px[y * N + x] = new Color32(b, b, bb, 255);
                }
            ribMap.SetPixels32(px); ribMap.Apply(true, false);
            return ribMap;
        }

        /// <summary>Build(): the piece's ember rim, a child of its drawn mesh (none for a part that was never cut).</summary>
        static MeshRenderer AddRim(Transform vis, Mesh mesh, Material like, int layer)
        {
            if (!mesh || !like || !rims.TryGetValue(mesh, out var rm) || !rm) return null;
            var go = new GameObject("Rim") { layer = layer };
            go.transform.SetParent(vis, false);
            go.AddComponent<MeshFilter>().sharedMesh = rm;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = RimMat(like);
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            RimPieces++;
            return r;
        }

        // ------------------------------------------------------------------ fires
        /// <summary>The break: the biggest body panel of each side and the heavier engine pod burn (the main one 15 s, the others 12 s);
        /// the rest smoulder 5 s. Without the kit's panels: the three heaviest pieces.</summary>
        static void AssignFires(Group grp, System.Func<float, float, float> R)
        {
            OzShuttleBreakup bodyR = null, bodyL = null, engine = null;
            foreach (var pc in grp.pieces)
            {
                if (!pc) continue;
                if (pc.Kind == "body" && pc.PieceName.StartsWith("Body_R")) { if (!bodyR || pc.MassKg > bodyR.MassKg) bodyR = pc; }
                else if (pc.Kind == "body" && pc.PieceName.StartsWith("Body_L")) { if (!bodyL || pc.MassKg > bodyL.MassKg) bodyL = pc; }
                else if (pc.Kind == "engine") { if (!engine || pc.MassKg > engine.MassKg) engine = pc; }
            }
            var fires = new List<OzShuttleBreakup>();
            foreach (var f in new[] { bodyR, bodyL, engine }) if (f) fires.Add(f);
            if (fires.Count < 3)
            {
                var byMass = new List<OzShuttleBreakup>(grp.pieces); byMass.Sort((a, b) => b.MassKg.CompareTo(a.MassKg));
                foreach (var pc in byMass) { if (fires.Count >= 3) break; if (pc && !fires.Contains(pc)) fires.Add(pc); }
            }
            fires.Sort((a, b) => b.MassKg.CompareTo(a.MassKg));
            for (int i = 0; i < fires.Count; i++)
            {
                var f = fires[i];
                f.bigFire = true; f.mainFire = i == 0; f.burnUntil = i == 0 ? FireSecMain : FireSecOther + R(0f, 0.8f); f.burnT = R(0f, 0.15f);
                FirePieces++;
            }
            foreach (var pc in grp.pieces)
                if (pc && !pc.bigFire) { pc.smoulderUntil = SmoulderSec * R(0.85f, 1.15f); pc.smoulderT = R(0f, SmoulderEvery); Smoulderers++; }
        }

        /// <summary>A burning piece, every FireEvery s: the vehicles' wreck burn (fire tongues on its top, a dark smoke column, the fire's
        /// light), then a thinning grey smoulder for 3 s; the fire's crackle starts with the first flame.</summary>
        void BurnStep()
        {
            bool burning = age < burnUntil;
            float k = burning ? Mathf.Lerp(1f, 0.75f, age / Mathf.Max(1f, burnUntil)) : Mathf.Lerp(0.75f, 0.3f, Mathf.Clamp01((age - burnUntil) / 3f));
            burnT = burning ? FireEvery : Mathf.Lerp(0.35f, 0.8f, 1f - k);
            var fxp = OpenZFxPlayer.I; if (!fxp || !fxp.isActiveAndEnabled) return;
            var c = Centre(); float top = Mathf.Clamp(Size * 0.38f, 0.5f, 1.6f);
            fxp.WreckBurn(c + Vector3.up * 0.3f, k, burning, Mathf.Clamp(Size * 0.22f, 0.35f, 0.8f), burning ? c + Vector3.up * top : (Vector3?)null);
            FireBurns++;
            if (burning && grp != null && !grp.fireLoopOn)
            {
                var ad = AudioDirector.I;
                grp.fireLoopKey = "shuttle_wreck_fire_" + grp.emitter;
                if (ad && AudioDirector.Has("dst_fire_loop") && ad.StartLoop(grp.fireLoopKey, "dst_fire_loop", c, transform, 0.6f) > 0) { grp.fireLoopOn = true; FireLoops++; }
                else grp.fireLoopOn = true;                                                          // no fire sound in this build: not asked again
            }
        }

        /// <summary>Freeze(): a burning piece lying on the ground: the wreck fire under it; the main one leaves the wreck's scorch.</summary>
        void FireAtRest(Vector3 low)
        {
            var fx = OpenZFxPlayer.I; if (fx && fx.isActiveAndEnabled) { fx.WreckFire(low, 1.2f); WreckFiresAtRest++; }
            if (mainFire && grp != null && !grp.scorched) { grp.scorched = true; if (ScorchDecals.Add(low, 2.6f, ScorchKind.Wreck)) MainScorches++; }
        }

        /// <summary>Every frame of a piece: the ember rim cooling (gone after EmberSec), the smoulder of a piece that does not burn.</summary>
        void LookStep(float dt)
        {
            if (deckGone > 0f && age >= deckGone && !retiring) { deckGone = -1f; DeckCleared++; Retire(); return; }
            if (rimR && rimR.enabled)
            {
                float k = 1f - age / Mathf.Max(0.1f, EmberSec);
                if (k <= 0f) rimR.enabled = false;
                else
                {
                    float fl = EmberBeat();                                                                  // item 400: its own waves (OzShuttleBreakup.FireBeat.cs)
                    rimBlock ??= new MaterialPropertyBlock();
                    rimBlock.SetVector(EmissionId, EmberColor * (EmberGain * Mathf.Pow(k, 1.4f) * fl));
                    rimR.SetPropertyBlock(rimBlock);
                }
            }
            if (smoulderUntil > 0f && age < smoulderUntil && (smoulderT -= dt) <= 0f)
            {
                smoulderT = SmoulderEvery * (0.8f + 0.4f * Random.value);
                var fxp = OpenZFxPlayer.I;
                if (fxp && fxp.isActiveAndEnabled) { fxp.WreckBurn(Centre() + Vector3.up * 0.2f, SmoulderK * Mathf.Lerp(1f, 0.6f, age / smoulderUntil), false, 0.4f); SmoulderPuffs++; }
            }
        }

        /// <summary>Freeze(): a piece of a ship shot down over (or on) a repair station's deck that comes to rest on the deck clears away.</summary>
        void DeckRest()
        {
            if (grp == null || grp.deck < 0 || deckGone > 0f) return;
            if (OzStationCrane.DeckY(grp.deck, transform.position, out float dy) && Centre().y > dy - 1.5f) deckGone = age + DeckClearDelay;
        }

        // ------------------------------------------------------------------ the first contacts, heard
        void QueueClank(Vector3 at, float vol, float pitch, int emitter)
        {
            if (grp == null) return;
            grp.clanks.Add(new Clank { at = at, vol = vol, pitch = pitch, t = grp.age, emitter = emitter }); QueuedClanks++;
        }

        static void ClankStep(Group grp)
        {
            if (grp.clanks.Count == 0) return;
            float now = Time.unscaledTime;
            if (now - grp.lastClankAt < ClankGapS) return;
            var c = grp.clanks[0];
            if (grp.age - c.t > ClankLateS) { grp.clanks.RemoveAt(0); QueuedDropped++; return; }
            if (FirstClank(c.at, c.emitter, c.vol, c.pitch)) { grp.clanks.RemoveAt(0); Clanks++; FirstContactsHeard++; grp.lastClankAt = now; }
        }

        /// <summary>A piece's first contact: the round's own landing sound (shuttle_piece_land: never dropped by a gap or a cooldown) when
        /// the sound table has it, else the debris clank.</summary>
        static bool FirstClank(Vector3 at, int emitter, float vol, float pitch) => PlayAd("shuttle_piece_land", at, emitter, vol, pitch) || PlayAd("dst_debris_metal", at, emitter, vol, pitch);
    }
}
