using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// The killed fort cannon as a burnt wreck that reads like the fort's other debris, not as a black silhouette, and that breaks
    /// unevenly instead of in clean cuts.
    /// <list type="bullet">
    /// <item><b>Retextured</b> the moment it is thrown off the dying fort (FortGunView.StartToss calls <see cref="Killed"/>): its war
    /// paint material (A or B) is swapped for a wreck copy with the wreck textures (Resources/OpenZ/FortV2/Wreck): scorch gradients and
    /// soot blotches over the paint (25-30 % of the paint still shows through the soot), paint chipped to dark primer, bare-steel
    /// scratches, branching cracks, heat-browned paint, a little rust bleed, dents and grooves in the normal map, matt soot / shiny
    /// scratches in the mask map; the team-mask alpha is the original texel for texel, so the team paint stays readable. Its burn
    /// (TeamPaint.Charred) goes from 0.75 to <see cref="Charred"/>. Why the old cannon read near black: TeamPaint puts the burn on
    /// _BaseColor with MaterialPropertyBlock.SetColor, which treats it as sRGB and turns it into linear (a linear-colour project), so
    /// the burn 0.75 (k 0.415) darkened the albedo x0.144, while the team stripes (_OZTeamVivid, SetVector, linear) only got x0.415:
    /// a black body with blue stripes, and the tonemapper's toe crushed it further (tools/darkness_model.py reproduces the judged frame).
    /// The wreck's level is set with that step in mind: burn <see cref="Charred"/> x <see cref="Tone"/> (both in TeamPaint's sRGB
    /// space), modelled at 0.78 (paint B) / 0.93 (paint A) of the fort debris (SPEC part 3); the KD check measures it in Play.</item>
    /// <item><b>Break faces</b> (OzFortGunBreakup's pieces, on its Broke event): the cut faces get a torn steel cross-section (layered
    /// plates, rib grid, rebar ends, rust, soot) instead of the plain dark core, and every real cut plane gets <b>torn edges</b> (a
    /// jagged rim of bent plate teeth 12-45 cm, leaning up to 20 cm out past the edge, so the outline itself is ragged), <b>ribs</b>
    /// (1-3 torn internal plates, 10-30 cm) and <b>rebar</b> (2-6 bent bars, 9 cm thick, 35-90 cm, rusty): sized to read from the RTS
    /// camera at its standard 70 m. The caps of a piece are found by plane (a later cut clips an earlier cap into several fans, so one
    /// cap is many fans: grouped by their plane, outlined by their convex hull, dressed once). Drawn only: the pieces' colliders,
    /// masses and physics are KC5c's, unchanged.</item>
    /// <item>The living cannon is untouched (nothing here runs before the fort dies).</item>
    /// </list>
    /// Switch <see cref="Enabled"/> (on). Off, or the textures missing = the old charred look (Charred 0.75, the dark core caps).
    /// </summary>
    [DefaultExecutionOrder(95)]                                                                   // after FortGunWarPaint (90)
    public sealed class OzFortGunWreckLook : MonoBehaviour
    {
        public static bool Enabled = true;
        /// <summary>TeamPaint burn of the killed cannon (0 = none, 1 = burnt out). Was 0.75 (x0.144 on the albedo after SetColor's sRGB
        /// step, x0.415 on the team stripes).</summary>
        public static float Charred = 0.05f;
        /// <summary>The wreck materials' base colour (TeamPaint multiplies it by the burn, in sRGB space, then SetColor makes it linear):
        /// one number to lift / lower the whole wreck; the KD check prints the value to set when its measured ratio is off.</summary>
        public static float Tone = 1.0f;                                                              // item 212 round 2: 1.1 -> 1.0 with the re-levelled wreck paints (OzFortGunWreckTone.cs)
        /// <summary>World metres per repeat of the break-face texture.</summary>
        public static float FaceTile = 1.1f;
        /// <summary>Torn rim (m): tooth spacing, lowest / highest tooth, share a broken-short tooth keeps, how far a tooth leans out past
        /// the edge in the cut's own plane (most out, a few in), the tallest tooth as a share of the cut's size (small shards).</summary>
        // was: public static float RimStep = 0.25f, RimMin = 0.12f, RimMax = 0.45f, RimShort = 0.5f, RimOut = 0.2f, RimOfCut = 0.6f;
        public static float RimStep = 0.6f, RimMin = 0.03f, RimMax = 0.10f, RimShort = 0.5f, RimOut = 0.02f, RimOfCut = 0.12f;   // subtle: a cleanly broken edge, a few small tears
        /// <summary>Ribs (torn internal plates): lowest / highest (m).</summary>
        // was: public static float RibMin = 0.1f, RibMax = 0.3f;
        public static float RibMin = 0.04f, RibMax = 0.10f;
        /// <summary>Rebar per cut (by its area), bar radius (m), whole bar length (m).</summary>
        // was: public static int RebarMin = 2, RebarMax = 6;
        public static int RebarMin = 1, RebarMax = 3;
        // was: public static float RebarRadius = 0.045f, RebarLenMin = 0.35f, RebarLenMax = 0.9f;
        public static float RebarRadius = 0.045f, RebarLenMin = 0.18f, RebarLenMax = 0.40f;
        /// <summary>A cap smaller than this (m2) gets no dressing (slivers).</summary>
        public static float MinCutArea = 0.02f;
        /// <summary>Cap triangles on one plane: normals within ~1.8 degrees, plane offsets within 2 cm.</summary>
        public const float PlaneCos = 0.9995f, PlaneTol = 0.02f;
        public const string ResDir = "OpenZ/FortV2/Wreck/";
        /// <summary>Fallbacks the KD check's darkness probe may point to (both off). RepaintPieces: every piece gets its own TeamPaint
        /// (the fort's team, the wreck's burn) instead of the paint values it copied from the thrown gun; the team stripes stay.
        /// StandardLitPieces (last resort, a probe only): the pipeline's standard lit shader with the wreck albedo; it DROPS the team stripes.</summary>
        public static bool RepaintPieces = false, StandardLitPieces = false;

        // proof
        public static int KilledCount, MaterialsSwapped, DressedPieces, DressedCuts, DressTris, RebarCount, CapFans, SkippedCuts;
        public static float LastDressMs;
        public static string LastState = "-";
        /// <summary>The last killed cannon (its thrown yaw) and its team, for the KD check and the repaint fallback.</summary>
        public static Transform LastYaw;
        public static Team LastTeam = Team.Neutral;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        static Texture2D albA, albB, nrm, mask, faceAlb, faceNrm; static bool loaded, loadOk;
        static readonly Dictionary<Material, Material> wreckOf = new Dictionary<Material, Material>();
        static readonly HashSet<Material> wrecks = new HashSet<Material>();
        static readonly Dictionary<int, Material> faceMats = new Dictionary<int, Material>();
        static Material rebarMat;

        TeamPaint paint; float nextCheck;

        static bool Load()
        {
            if (loaded) return loadOk;
            loaded = true;
            albA = Resources.Load<Texture2D>(ResDir + "FortV2_Gun_Albedo_Wreck");
            albB = Resources.Load<Texture2D>(ResDir + "FortV2_Gun_Albedo_B_Wreck");
            nrm = Resources.Load<Texture2D>(ResDir + "FortV2_Gun_Normal_Wreck");
            mask = Resources.Load<Texture2D>(ResDir + "FortV2_Gun_MaskMap_Wreck");
            faceAlb = Resources.Load<Texture2D>(ResDir + "FortGun_BreakFace_Albedo");
            faceNrm = Resources.Load<Texture2D>(ResDir + "FortGun_BreakFace_Normal");
            loadOk = albA && albB && nrm && mask && faceAlb && faceNrm;
            if (!loadOk) Debug.LogWarning("[OpenZ] fort cannon wreck look: textures missing under Resources/" + ResDir + ": the old charred look");
            return loadOk;
        }

        static bool IsGunMat(Material m) => m && m.name.StartsWith("m01_fort_v2_gun_mat") && !wrecks.Contains(m);

        static Material WreckOf(Material src)
        {
            if (wreckOf.TryGetValue(src, out var w) && w) return w;
            w = new Material(src) { name = src.name + "_Wreck" };
            bool b = src.name.Contains("_B");
            if (w.HasProperty("_BaseMap")) w.SetTexture("_BaseMap", b ? albB : albA);
            if (w.HasProperty("_BumpMap")) { w.SetTexture("_BumpMap", nrm); w.SetFloat("_UseNormalMap", 1f); }
            if (w.HasProperty("_MaskMap")) { w.SetTexture("_MaskMap", mask); w.SetFloat("_UseMaskMap", 1f); }
            if (w.HasProperty("_EmissionMapColor")) w.SetColor("_EmissionMapColor", Color.black);   // its lamps are dead
            if (w.HasProperty("_BaseColor")) w.SetColor("_BaseColor", new Color(Tone, Tone, Tone, 1f));
            wreckOf[src] = w; wrecks.Add(w);
            return w;
        }

        static int Swap(Transform root)
        {
            int n = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials; bool any = false;
                for (int i = 0; i < mats.Length; i++) if (IsGunMat(mats[i])) { mats[i] = WreckOf(mats[i]); any = true; n++; }
                    else if (OzFortGunWreckTone.IsRamSleeve(mats[i])) { mats[i] = OzFortGunWreckTone.BurntRam(mats[i]); any = true; n++; }   // item 212 round 2: the dark hydraulic sleeves as burnt steel (OzFortGunWreckTone.cs)
                if (any) r.sharedMaterials = mats;
            }
            return n;
        }

        /// <summary>FortGunView.StartToss: the cannon was just thrown off its dying fort (tp = its TeamPaint, team set).</summary>
        public static void Killed(Transform yaw, TeamPaint tp)
        {
            if (!yaw || !tp) return;
            LastYaw = yaw; LastTeam = tp.Team;
            if (!Enabled || !Load()) { tp.Charred = 0.75f; LastState = "off: the old charred look"; return; }
            int n = Swap(yaw);
            tp.Charred = Charred;                                                                    // re-applies the paint blocks (team, burn, band)
            var look = yaw.GetComponent<OzFortGunWreckLook>(); if (!look) look = yaw.gameObject.AddComponent<OzFortGunWreckLook>();
            look.paint = tp;
            KilledCount++; MaterialsSwapped += n;
            LastState = $"wreck look on {yaw.name}: {n} material slot(s) swapped, burn {Charred:0.00} (was 0.75), tone {Tone:0.00}";
        }

        void LateUpdate()
        {   // the war paint follows the team: if it ever puts the live paint back, the wreck paint goes on again
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + 0.5f;
            if (Swap(transform) > 0 && paint) paint.Apply();
        }

        // ------------------------------------------------------------------ the pieces (KC5c)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { OzFortGunBreakup.Broke -= OnBroke; OzFortGunBreakup.Broke += OnBroke; }

        static void OnBroke(Vector3 at)
        {
            if (!Enabled || !Load()) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int seed = Mathf.RoundToInt(at.x * 13f + at.z * 7f);
            foreach (var pc in OzFortGunBreakup.LastPieces) if (pc) Dress(pc, seed++);
            LastDressMs = (float)clock.Elapsed.TotalMilliseconds;
            Debug.Log($"[OpenZ] fort cannon wreck: {DressedPieces} pieces dressed, {DressedCuts} cuts with torn rims / ribs / rebar (from {CapFans} cap fans grouped by plane, {SkippedCuts} slivers left plain; {RebarCount} bars, {DressTris} triangles) in {LastDressMs:0.0} ms");
        }

        static Material FaceMat(Material like, float scale)
        {
            int key = Mathf.RoundToInt(scale * 100f);
            if (faceMats.TryGetValue(key, out var m) && m) return m;
            m = new Material(like) { name = "FortGun_BreakFace" };
            if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", faceAlb); m.SetTextureScale("_BaseMap", Vector2.one * (scale / Mathf.Max(0.05f, FaceTile))); }
            if (m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", faceNrm); m.SetFloat("_UseNormalMap", 1f); }
            if (m.HasProperty("_UseMaskMap")) m.SetFloat("_UseMaskMap", 0f);
            if (m.HasProperty("_TeamMode")) m.SetFloat("_TeamMode", 0f);                             // no team paint inside the steel
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.35f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.22f);
            if (m.HasProperty("_EmissionMapColor")) m.SetColor("_EmissionMapColor", Color.black);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            OzFortGunWreckTone.Face(m);                                                               // item 212 round 2: the torn steel's level (OzFortGunWreckTone.cs)
            faceMats[key] = m;
            return m;
        }

        static Material RebarMat(Material like)
        {
            if (rebarMat) return rebarMat;
            rebarMat = new Material(like) { name = "FortGun_Rebar" };
            if (rebarMat.HasProperty("_BaseMap")) rebarMat.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (rebarMat.HasProperty("_UseNormalMap")) rebarMat.SetFloat("_UseNormalMap", 0f);
            if (rebarMat.HasProperty("_UseMaskMap")) rebarMat.SetFloat("_UseMaskMap", 0f);
            if (rebarMat.HasProperty("_TeamMode")) rebarMat.SetFloat("_TeamMode", 0f);
            if (rebarMat.HasProperty("_Metallic")) rebarMat.SetFloat("_Metallic", 0.25f);
            if (rebarMat.HasProperty("_Smoothness")) rebarMat.SetFloat("_Smoothness", 0.2f);
            if (rebarMat.HasProperty("_EmissionMapColor")) rebarMat.SetColor("_EmissionMapColor", Color.black);
            if (rebarMat.HasProperty("_BaseColor")) rebarMat.SetColor("_BaseColor", new Color(0.55f, 0.36f, 0.25f, 1f));   // rusty steel (sRGB; light enough to read as thin bars on grass)
            OzFortGunWreckTone.Rebar(rebarMat);                                                       // item 212 round 2: a light rust that reads at 70 m (OzFortGunWreckTone.cs)
            return rebarMat;
        }

        static void Dress(OzFortGunBreakup pc, int seed)
        {
            var vis = pc.transform.Find(pc.PieceName);
            if (!vis || !vis.TryGetComponent<MeshFilter>(out var mf) || !mf.sharedMesh || !vis.TryGetComponent<MeshRenderer>(out var r)) return;
            var mesh = mf.sharedMesh; var mats = r.sharedMaterials;
            if (mats.Length == 0 || !mats[0]) return;
            float s = Mathf.Max(1e-4f, (Mathf.Abs(vis.lossyScale.x) + Mathf.Abs(vis.lossyScale.y) + Mathf.Abs(vis.lossyScale.z)) / 3f);   // mesh units -> m
            if (pc.Fracture) return;                                                                    // item 375: the fracture look dresses these pieces itself (OzFortGunBreakup.Fracture.cs)
            var face = FaceMat(mats[0], s);
            if (pc.Capped && mesh.subMeshCount >= 2 && mats.Length >= mesh.subMeshCount)
            {
                mats[mesh.subMeshCount - 1] = face; r.sharedMaterials = mats;                          // the torn steel instead of the plain dark core
            }
            DressedPieces++;
            if (RepaintPieces) Repaint(pc);
            if (StandardLitPieces) StandardLit(r);
            if (!pc.Capped || mesh.subMeshCount < 2 || !mesh.isReadable) return;
            var capIdx = mesh.GetTriangles(mesh.subMeshCount - 1);
            if (capIdx.Length < 3) return;
            var rnd = new System.Random(seed * 7919 + pc.PieceName.GetHashCode());
            var b = new Builder();
            foreach (var cp in CapPlanes(mesh.vertices, mesh.normals, capIdx, s))
            {
                CapFans += cp.fans;
                var poly = Outline(cp);
                if (poly.Count < 3) { SkippedCuts++; continue; }
                float areaM = 0f; for (int i = 1; i + 1 < poly.Count; i++) areaM += Vector3.Cross(poly[i] - poly[0], poly[i + 1] - poly[0]).magnitude * 0.5f;
                areaM *= s * s;
                if (areaM < MinCutArea) { SkippedCuts++; continue; }
                Cut(b, poly, cp.n, s, areaM, rnd);
                DressedCuts++;
            }
            if (b.Tris == 0) return;
            var dm = b.ToMesh("OZ_WreckDress_" + pc.PieceName);
            var go = new GameObject("OZ_WreckDress") { layer = vis.gameObject.layer };
            go.transform.SetParent(vis, false);
            go.AddComponent<MeshFilter>().sharedMesh = dm;
            var dr = go.AddComponent<MeshRenderer>(); dr.sharedMaterials = new[] { face, RebarMat(mats[0]) };
            dr.shadowCastingMode = ShadowCastingMode.On; dr.receiveShadows = r.receiveShadows;
            dr.lightProbeUsage = r.lightProbeUsage; dr.reflectionProbeUsage = r.reflectionProbeUsage;
            dr.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            DressTris += b.Tris;
        }

        /// <summary>One real cut of a piece: its plane (outward normal n, offset d in metres) and every cap point on it.</summary>
        public sealed class CapPlane
        {
            public Vector3 n; public float d; public int fans;
            public readonly List<Vector3> pts = new List<Vector3>();
        }

        /// <summary>The piece's cap triangles grouped by plane. OzMeshSlice.Cap closes each cut with one convex fan, but a LATER cut that
        /// crosses an earlier cap clips that cap's triangles one by one into small fans of their own (OzMeshSlice.Split), so one cut
        /// can be many fans: grouping by plane gives exactly one group per real cut (a cut is convex, so clipping it by more planes
        /// leaves it convex: the hull of its points is its outline). Mesh space; s = metres per mesh unit.</summary>
        public static List<CapPlane> CapPlanes(Vector3[] verts, Vector3[] norms, int[] capIdx, float s)
        {
            var planes = new List<CapPlane>();
            bool hasN = norms != null && norms.Length == verts.Length;
            int lastFirst = -1;
            for (int k = 0; k + 2 < capIdx.Length; k += 3)
            {
                int i0 = capIdx[k], i1 = capIdx[k + 1], i2 = capIdx[k + 2];
                if (i0 >= verts.Length || i1 >= verts.Length || i2 >= verts.Length) continue;
                Vector3 a = verts[i0], b = verts[i1], c = verts[i2];
                var g = Vector3.Cross(b - a, c - a);
                var n = hasN ? norms[i0] : g;
                if (n.sqrMagnitude < 1e-12f) { if (g.sqrMagnitude < 1e-20f) continue; n = g; }
                n.Normalize();
                float d = Vector3.Dot(n, (a + b + c) / 3f) * s;
                CapPlane hit = null;
                foreach (var p in planes) if (Vector3.Dot(p.n, n) > PlaneCos && Mathf.Abs(p.d - d) < PlaneTol) { hit = p; break; }
                if (hit == null) { hit = new CapPlane { n = n, d = d }; planes.Add(hit); }
                if (i0 != lastFirst) { hit.fans++; lastFirst = i0; }                                   // proof only: fans met (a fan's triangles share its first vertex)
                hit.pts.Add(a); hit.pts.Add(b); hit.pts.Add(c);
            }
            return planes;
        }

        /// <summary>The outline of one cut: its points in the plane's own axes, their convex hull (counter-clockwise about n), back in mesh space.</summary>
        public static List<Vector3> Outline(CapPlane cp)
        {
            var res = new List<Vector3>();
            if (cp.pts.Count < 3) return res;
            var e1 = Vector3.Cross(cp.n, Mathf.Abs(cp.n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var e2 = Vector3.Cross(cp.n, e1);                                                           // e1 x e2 = n
            var o = cp.pts[0];
            var flat = new List<Vector2>(cp.pts.Count);
            foreach (var p in cp.pts) { var q = p - o; flat.Add(new Vector2(Vector3.Dot(q, e1), Vector3.Dot(q, e2))); }
            foreach (var h in Hull(flat)) res.Add(o + e1 * h.x + e2 * h.y);
            return res;
        }

        static float Cross2(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>Andrew's monotone chain (as OzMeshSlice.Hull): counter-clockwise, no repeated or collinear points.</summary>
        static List<Vector2> Hull(List<Vector2> pts)
        {
            pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var h = new List<Vector2>(pts.Count + 1);
            if (pts.Count < 3) return h;
            var span = pts[pts.Count - 1] - pts[0]; float tol = Mathf.Max(1e-14f, span.sqrMagnitude * 1e-12f);
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int k = 0; k < pts.Count; k++)
                {
                    var p = pass == 0 ? pts[k] : pts[pts.Count - 1 - k];
                    while (h.Count >= start + 2 && Cross2(h[h.Count - 1] - h[h.Count - 2], p - h[h.Count - 2]) <= tol) h.RemoveAt(h.Count - 1);
                    h.Add(p);
                }
                h.RemoveAt(h.Count - 1);
            }
            return h;
        }

        /// <summary>The paint-keeping fallback: the piece paints itself (TeamPaint: the fort's team, the wreck's burn): team colour, vivid
        /// stripes, band and burn are rebuilt for every slot of the piece and its dressing.</summary>
        static void Repaint(OzFortGunBreakup pc)
        {
            var tp = pc.GetComponent<TeamPaint>(); if (!tp) tp = pc.gameObject.AddComponent<TeamPaint>();
            tp.Team = LastTeam; tp.Charred = Charred;
        }

        static Material stdLit;
        static void StandardLit(Renderer r)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit"); if (!sh) return;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (!mats[i] || !wrecks.Contains(mats[i])) continue;
                var m = new Material(sh) { name = mats[i].name + "_Std" };
                m.SetTexture("_BaseMap", mats[i].GetTexture("_BaseMap")); m.SetColor("_BaseColor", new Color(Tone, Tone, Tone, 1f) * (1f - 0.78f * Charred));
                m.SetFloat("_Metallic", 0.2f); m.SetFloat("_Smoothness", 0.25f);
                mats[i] = m; stdLit = m;
            }
            r.sharedMaterials = mats;
            for (int i = 0; i < mats.Length; i++) r.SetPropertyBlock(null, i);
            r.SetPropertyBlock(null);
        }

        static IEnumerable<MeshRenderer> PieceRenderers()
        {
            foreach (var pc in OzFortGunBreakup.LastPieces)
            {
                if (!pc) continue;
                var vis = pc.transform.Find(pc.PieceName);
                if (vis && vis.TryGetComponent<MeshRenderer>(out var r)) yield return r;
            }
        }

        /// <summary>KD check, darkness probe on the resting pieces, now: 1 = the paint-keeping repaint (RepaintPieces), 2 = the standard
        /// lit shader (StandardLitPieces: drops the team stripes); returns the pieces changed.</summary>
        public static int Probe(int step)
        {
            int n = 0;
            foreach (var pc in OzFortGunBreakup.LastPieces)
            {
                if (!pc) continue;
                var vis = pc.transform.Find(pc.PieceName);
                if (!vis || !vis.TryGetComponent<MeshRenderer>(out var r)) continue;
                if (step == 1) Repaint(pc); else StandardLit(r);
                n++;
            }
            return n;
        }

        /// <summary>KD check, Tone search: the pieces' wreck paint as Tone = `tone` would give it on a new run (TeamPaint: material
        /// _BaseColor (Tone) x the burn, through SetColor), now; returns the slots changed. ProbeTone(Tone) puts it back.</summary>
        public static int ProbeTone(float tone)
        {
            int n = 0; float k = (1f - 0.78f * Charred) * tone; var mpb = new MaterialPropertyBlock();
            foreach (var r in PieceRenderers())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!mats[i] || !wrecks.Contains(mats[i])) continue;
                    r.GetPropertyBlock(mpb, i); mpb.SetVector(BaseColorId, OzFortGunRestLook.PaintValue(k)); r.SetPropertyBlock(mpb, i); n++;   // item 212 round 3: the resting pieces' own tone included (OzFortGunRestLook.cs)
                }
            }
            return n;
        }

        static float R(System.Random rnd, float a, float b) => a + (float)rnd.NextDouble() * (b - a);

        /// <summary>Torn rim, ribs and rebar on one cut (mesh space; poly = its convex outline; n = out of the cut; s = metres per mesh
        /// unit; areaM = its area in m2). Sizes are metres, made to read from the RTS camera at 70 m (about 27 px a metre at 1080p).</summary>
        static void Cut(Builder b, List<Vector3> poly, Vector3 n, float s, float areaM, System.Random rnd)
        {
            int m = poly.Count;
            var c = Vector3.zero; foreach (var p in poly) c += p; c /= m;
            float inv = 1f / s;
            float hCap = Mathf.Max(0.08f, RimOfCut * Mathf.Sqrt(areaM));                                 // small shards: no tooth taller than the shard
            // torn rim: plate teeth standing out of the cut edge, jagged, most bent out past the edge (the outline itself is ragged)
            for (int i = 0; i < m; i++)
            {
                Vector3 a = poly[i], e = poly[(i + 1) % m];
                float len = (e - a).magnitude * s; int steps = Mathf.Clamp(Mathf.CeilToInt(len / RimStep), 1, 40);
                Vector3 prevB = a, prevT = Tooth(a, c, n, hCap, rnd, inv);
                for (int k = 1; k <= steps; k++)
                {
                    var p = Vector3.Lerp(a, e, k / (float)steps);
                    var t = Tooth(p, c, n, hCap, rnd, inv);
                    b.Quad(0, prevB - n * (0.03f * inv), p - n * (0.03f * inv), t, prevT);
                    prevB = p; prevT = t;
                }
            }
            // ribs: torn internal plates across the cut
            int ribs = areaM > 1.2f ? 3 : areaM > 0.4f ? 2 : 1;
            for (int k = 0; k < ribs; k++)
            {
                var pa = poly[rnd.Next(m)]; var pb = poly[(rnd.Next(m / 3 + 1) + m / 2 + rnd.Next(m)) % m];
                pa = Vector3.Lerp(pa, c, 0.12f); pb = Vector3.Lerp(pb, c, 0.12f);
                float len = (pb - pa).magnitude * s; if (len < 0.2f) continue;
                int steps = Mathf.Clamp(Mathf.CeilToInt(len / 0.2f), 2, 30);
                Vector3 prevB = pa, prevT = pa + n * (Mathf.Min(hCap, R(rnd, RibMin, RibMax)) * inv);
                for (int j = 1; j <= steps; j++)
                {
                    var p = Vector3.Lerp(pa, pb, j / (float)steps);
                    var t = p + n * (Mathf.Min(hCap, R(rnd, RibMin, RibMax)) * inv);
                    b.Quad(0, prevB - n * (0.03f * inv), p - n * (0.03f * inv), t, prevT);
                    prevB = p; prevT = t;
                }
            }
            // rebar: bent bars out of the cut
            int bars = Mathf.Clamp(Mathf.RoundToInt(areaM / 0.4f), RebarMin, RebarMax);
            var t1 = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized; var t2 = Vector3.Cross(n, t1);
            for (int k = 0; k < bars; k++)
            {
                int tri = 1 + rnd.Next(Mathf.Max(1, m - 2));
                float u = (float)rnd.NextDouble(), v = (float)rnd.NextDouble(); if (u + v > 1f) { u = 1f - u; v = 1f - v; }
                var p0 = poly[0] + (poly[tri] - poly[0]) * u + (poly[Mathf.Min(tri + 1, m - 1)] - poly[0]) * v;
                p0 = Vector3.Lerp(p0, c, 0.15f) - n * (0.05f * inv);
                float L = Mathf.Min(R(rnd, RebarLenMin, RebarLenMax), Mathf.Max(0.25f, 1.6f * Mathf.Sqrt(areaM)));
                float l1 = L * R(rnd, 0.45f, 0.7f), l2 = L - l1;
                float tilt = R(rnd, -0.35f, 0.35f), bend = R(rnd, 0.35f, 1.2f), ang = R(rnd, 0f, 6.2831853f);
                var side = (t1 * Mathf.Cos(ang) + t2 * Mathf.Sin(ang)).normalized;
                var d1 = (n + side * tilt).normalized;
                var d2 = (n * Mathf.Cos(bend) + side * Mathf.Sin(bend)).normalized;
                var p1 = p0 + d1 * (l1 * inv);
                var p2 = p1 + d2 * (l2 * inv);
                float rad = RebarRadius * R(rnd, 0.85f, 1.15f) * inv;
                b.Tube(1, p0, p1, rad); b.Tube(1, p1, p2, rad * 0.95f);
                RebarCount++;
            }
        }

        /// <summary>One rim tooth's tip over the edge point p (c = the cut's centre): up out of the cut by 12-45 cm (a third broken short),
        /// leaning out past the edge in the cut's own plane by up to RimOut (a few lean in).</summary>
        static Vector3 Tooth(Vector3 p, Vector3 c, Vector3 n, float hCap, System.Random rnd, float inv)
        {
            float h = R(rnd, RimMin, RimMax); if (rnd.NextDouble() < 0.35) h *= RimShort;            // ragged: some teeth broken short
            h = Mathf.Min(h, hCap);
            var outward = p - c; outward -= n * Vector3.Dot(outward, n); outward = outward.sqrMagnitude > 1e-12f ? outward.normalized : Vector3.zero;
            float lean = R(rnd, -0.05f, RimOut);                                                         // bent out past the edge (or a little in)
            return p + n * (h * inv) + outward * (lean * inv);
        }

        /// <summary>A tiny mesh builder: two sub-meshes (torn steel, rebar), every face two-sided.</summary>
        sealed class Builder
        {
            readonly List<Vector3> v = new List<Vector3>(); readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int>[] sub = { new List<int>(), new List<int>() };
            public int Tris => (sub[0].Count + sub[1].Count) / 3;

            public void Quad(int si, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                for (int side = 0; side < 2; side++)
                {
                    int o = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                    float w = (b - a).magnitude, h = (d - a).magnitude;
                    uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(w, 0f)); uv.Add(new Vector2(w, h)); uv.Add(new Vector2(0f, h));
                    if (side == 0) { sub[si].Add(o); sub[si].Add(o + 2); sub[si].Add(o + 1); sub[si].Add(o); sub[si].Add(o + 3); sub[si].Add(o + 2); }
                    else { sub[si].Add(o); sub[si].Add(o + 1); sub[si].Add(o + 2); sub[si].Add(o); sub[si].Add(o + 2); sub[si].Add(o + 3); }
                }
            }

            public void Tube(int si, Vector3 a, Vector3 b, float r)
            {
                var ax = (b - a); if (ax.sqrMagnitude < 1e-12f) return; ax.Normalize();
                var e1 = Vector3.Cross(ax, Mathf.Abs(ax.y) < 0.9f ? Vector3.up : Vector3.right).normalized; var e2 = Vector3.Cross(ax, e1);
                const int S = 6; int o = v.Count;
                for (int k = 0; k <= S; k++)
                {
                    float t = k * Mathf.PI * 2f / S; var off = (e1 * Mathf.Cos(t) + e2 * Mathf.Sin(t)) * r;
                    v.Add(a + off); v.Add(b + off); uv.Add(new Vector2(k / (float)S, 0f)); uv.Add(new Vector2(k / (float)S, 1f));
                }
                for (int k = 0; k < S; k++)
                {
                    int i0 = o + k * 2, i1 = i0 + 1, i2 = i0 + 2, i3 = i0 + 3;
                    sub[si].Add(i0); sub[si].Add(i1); sub[si].Add(i3); sub[si].Add(i0); sub[si].Add(i3); sub[si].Add(i2);
                }
                int cap = v.Count; v.Add(b); uv.Add(new Vector2(0.5f, 0.5f));                            // the bar's torn end
                for (int k = 0; k < S; k++) { sub[si].Add(cap); sub[si].Add(o + k * 2 + 1); sub[si].Add(o + k * 2 + 3); }
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(v); m.SetUVs(0, uv); m.subMeshCount = 2;
                m.SetTriangles(sub[0], 0); m.SetTriangles(sub[1], 1);
                m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
                return m;
            }
        }
    }
}
