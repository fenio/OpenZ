using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace OpenZ
{
    /// <summary>
    /// On top of the blind
    /// sectors seen from the gun (AttackRangeRings.BlindFromGun.cs; the same sectors, the rules' own cells, redrawn when a blocker goes):
    /// <list type="bullet">
    /// <item>the blind ground gets a slightly darker grey (blindVeilDay / blindVeilNight, its strength blindDarkDay / blindDarkNight), so
    /// every blind sector reads as one even grey area beside the open ground, also where there are many; it fades in and out with the ring.
    /// The grey reads grey on every ground: the shader OpenZ/BlindGrey (Resources/OpenZ/FX/OzBlindGrey.shader) darkens the ground exactly as
    /// the plain overlay does and pulls it toward a neutral grey of its own brightness (blindGreyWarm on sand, rock and soil, blindGreyCool on
    /// green grass, jungle and the night's blue cast), so green grass turns grey instead of a darker green; without the camera's opaque copy
    /// (or with blindGreyGround off) the plain darkening is drawn;</item>
    /// <item>the ring's dots over each blind arc turn grey and faint (blindDotGreyDay / blindDotGreyNight, blindDotAlphaDay / blindDotAlphaNight:
    /// semi-transparent, never fully gone): the team-coloured dot fades out where the grey one comes in, eased over the arc's ends, so the
    /// open arcs stay in the team colour and the blind arcs read grey. The grey dots are the ring's own dots (same place, size and crawl),
    /// drawn unlit with no glow right after the ring.</item>
    /// </list>
    /// The side lines from the gun and the marks stay as they are. View only.
    /// </summary>
    public partial class AttackRangeRings
    {
        [Header("Blind arcs: grey ring dots")]
        [Tooltip("Over a blind arc the ring's dots turn grey and faint (on), or keep the team colour, dimmed by blindRingDim / blindRingFade (off)")]
        public bool blindDotsGrey = true;
        [Tooltip("The grey dots' colour by day / by night (linear, unlit, no glow)")]
        public Color blindDotGreyDay = new Color(0.42f, 0.42f, 0.42f, 1f), blindDotGreyNight = new Color(0.3f, 0.3f, 0.31f, 1f);
        [Tooltip("The grey dots' alpha by day / by night (x the ring's alpha): semi-transparent, never fully gone")]
        [Range(0.1f, 1f)] public float blindDotAlphaDay = 0.5f, blindDotAlphaNight = 0.40f;

        [Header("Blind sectors: grey ground")]
        [Tooltip("The blind ground reads grey on every ground (on): on top of the darkening it is pulled toward a neutral grey of its own brightness " +
                 "(OpenZ/BlindGrey, from the camera's opaque copy). Off, or no opaque copy: the plain darkening (on green grass only a darker green)")]
        public bool blindGreyGround = true;
        [Tooltip("Colour taken out of the blind ground at full strength: warm ground (sand, rock, soil; the darkening alone already reads grey there) / " +
                 "green and blue ground (grass, jungle, the night's blue cast)")]
        [Range(0f, 1f)] public float blindGreyWarm = 0.25f, blindGreyCool = 0.85f;

        sealed partial class Blind
        {
            public float[] greyA;                                                                        // per ring dot this frame: its grey share (the ring's alpha x the arc's k; 0 none)
            public int greyFrame = -1;
            public Mesh greyMesh; public Vector3[] gv; public Color32[] gc; public int[] gtri;
        }

        Material greyDotMat;
        Ring greyRing; Blind greyBlind; int greyRingFrame = -1, greyCountFrame = -1;

        /// <summary>This frame's grey ring dots (over a blind arc, k over 0.5) of rings that show blind sectors (the proof).</summary>
        public int BlindGreyDots { get; private set; }

        /// <summary>Build's hook: a ring dot's colour (alpha a) at the ring angle ang (rad), dot index d. With the grey dots on, a dot over a
        /// blind arc of its ring's own sectors (drawn this frame) keeps the team colour at alpha a x (1 - k) and leaves the rest, a x k, to
        /// its grey twin (DrawBlindGreyDots); off: K333b's dimmed dot (BlindRingDot). The proof's counts stay BlindRingDot's.</summary>
        Color32 BlindRingDotGrey(Ring r, int d, float ang, byte a)
        {
            var ca = BlindRingDot(r, ang, a);
            if (!blindDotsGrey) return ca;
            if (greyRing != r || greyRingFrame != Time.frameCount)
            {   // once per ring and frame: this ring's sectors, if they are drawn this frame, and its dots' grey shares cleared
                greyRing = r; greyRingFrame = Time.frameCount;
                if (greyCountFrame != Time.frameCount) { greyCountFrame = Time.frameCount; BlindGreyDots = 0; }
                if (!blindSectors || !blinds.TryGetValue(r, out greyBlind) || greyBlind.shownFrame != Time.frameCount || greyBlind.t0 != r.t0 || !greyBlind.arcAny
                    || greyBlind.arcDim == null || greyBlind.nTri == 0) greyBlind = null;
                else
                {
                    if (greyBlind.greyA == null || greyBlind.greyA.Length < r.n) greyBlind.greyA = new float[Mathf.NextPowerOfTwo(Mathf.Max(64, r.n))];
                    System.Array.Clear(greyBlind.greyA, 0, greyBlind.greyA.Length);
                    greyBlind.greyFrame = Time.frameCount;
                }
            }
            if (greyBlind == null || a == 0 || d < 0 || d >= greyBlind.greyA.Length) return ca;
            float f = Mathf.Repeat(ang, 2f * Mathf.PI) / (2f * Mathf.PI) * ArcBins;
            int i = Mathf.FloorToInt(f); float t = f - i; i %= ArcBins; int j = (i + 1) % ArcBins;
            float k = Mathf.Lerp(greyBlind.arcDim[i], greyBlind.arcDim[j], t);
            if (k <= 0.001f) return ca;                                                                 // an open dot: as it is
            greyBlind.greyA[d] = a / 255f * k;
            if (k > 0.5f) BlindGreyDots++;
            return new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * (1f - k)));                   // the team colour fades out where the grey comes in
        }

        /// <summary>Draw's hook, right after the ring: the grey twins of the ring's dots over its blind arcs (the ring's own vertices of this
        /// frame: same place, size, slope and crawl), unlit, in the grey's colour and alpha (day / night), soft rim as the ring's.</summary>
        void DrawBlindGreyDots(Ring r)
        {
            if (!blindDotsGrey || !blindSectors || r.n == 0 || r.v == null) return;
            if (!blinds.TryGetValue(r, out var b) || b.greyFrame != Time.frameCount || b.greyA == null || b.t0 != r.t0) return;
            int n = Mathf.Min(r.n, b.greyA.Length);
            if (b.gv == null || b.gv.Length < r.n * V)
            {
                int cap = r.n;
                b.gv = new Vector3[cap * V]; b.gc = new Color32[cap * V]; b.gtri = new int[cap * T * 3];
                for (int q = 0, t = 0; q < cap; q++)
                {   // the ring's dot: centre + 6 inner (solid core) + 6 outer (soft rim), 18 triangles (Alloc)
                    int o = q * V;
                    for (int i = 0; i < 6; i++)
                    {
                        int i1 = (i + 1) % 6;
                        b.gtri[t++] = o; b.gtri[t++] = o + 1 + i1; b.gtri[t++] = o + 1 + i;
                        b.gtri[t++] = o + 1 + i; b.gtri[t++] = o + 1 + i1; b.gtri[t++] = o + 7 + i;
                        b.gtri[t++] = o + 1 + i1; b.gtri[t++] = o + 7 + i1; b.gtri[t++] = o + 7 + i;
                    }
                }
            }
            float night = NightGrade.NightK;
            var col = Color.Lerp(blindDotGreyDay, blindDotGreyNight, night);
            float ga = Mathf.Clamp(Mathf.Lerp(blindDotAlphaDay, blindDotAlphaNight, night), 0.1f, 1f);       // never fully gone
            byte cr = Byte(col.r), cg = Byte(col.g), cb = Byte(col.b);
            var rim = new Color32(cr, cg, cb, 0);
            int m = 0;
            for (int d = 0; d < n; d++)
            {
                float k = b.greyA[d];
                if (k <= 0.002f) continue;
                int src = d * V, dst = m * V;
                var core = new Color32(cr, cg, cb, Byte(k * ga));
                for (int i = 0; i < V; i++) { b.gv[dst + i] = r.v[src + i]; b.gc[dst + i] = i < 7 ? core : rim; }
                m++;
            }
            if (m == 0) return;
            if (!b.greyMesh) { b.greyMesh = new Mesh { name = "OZ_RangeRing_BlindDots" }; b.greyMesh.MarkDynamic(); }
            b.greyMesh.Clear();
            b.greyMesh.SetVertices(b.gv, 0, m * V);
            b.greyMesh.SetColors(b.gc, 0, m * V);
            b.greyMesh.SetTriangles(b.gtri, 0, m * T * 3, 0, false);
            b.greyMesh.bounds = r.mesh ? r.mesh.bounds : new Bounds(r.center, new Vector3(r.radius * 2f + 4f, 60f, r.radius * 2f + 4f));
            var mat = GreyDotMat(r);
            if (mat) Graphics.DrawMesh(b.greyMesh, Matrix4x4.identity, mat, 0, null, 0, null, ShadowCastingMode.Off, false);
        }

        /// <summary>The grey dots' material: the overlay with a white colour (the vertex colours carry the grey), depth tested as the ring,
        /// in the ring dots' own queue (over the grey of the blind ground).</summary>
        Material GreyDotMat(Ring r)
        {
            if (greyDotMat) return greyDotMat;
            var baseMat = overlayMaterial;
            if (!baseMat) { var so = GetComponent<SelectionOverlay>(); if (!so) so = Object.FindFirstObjectByType<SelectionOverlay>(); if (so) baseMat = so.overlayMaterial; }
            if (!baseMat) { var sh = Shader.Find("OpenZ/Overlay"); if (!sh) return null; baseMat = new Material(sh); }
            greyDotMat = new Material(baseMat) { name = "OZ_RangeRing_BlindDots" };
            greyDotMat.SetColor("_Color", Color.white);
            greyDotMat.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            if (greyDotMat.HasProperty("_Scan")) greyDotMat.SetFloat("_Scan", 0f);
            var rm = Mat(r.team);
            if (rm) greyDotMat.renderQueue = rm.renderQueue;
            return greyDotMat;
        }

        // ------------------------------------------------------------------ the grey ground (OpenZ/BlindGrey)
        Material greyFillMat; bool greyFillTried; int greyFillFrame = -1; bool greyFillOk; string greyShaderWhy = "ok";
        static readonly int GreyFullAId = Shader.PropertyToID("_FullA"), GreyWarmId = Shader.PropertyToID("_DesatWarm"), GreyCoolId = Shader.PropertyToID("_DesatCool");

        /// <summary>This frame the blind ground is drawn grey with OpenZ/BlindGrey (else the plain darkening: why in <see cref="BlindGreyGroundWhy"/>).</summary>
        public bool BlindGreyGroundOn { get; private set; }
        public string BlindGreyGroundWhy { get; private set; } = "-";

        /// <summary>Show's hook: the material the blind sectors' mesh is drawn with. OpenZ/BlindGrey (the same mesh and vertex colours: the
        /// fill darkens the ground as the plain overlay does and pulls it toward its own grey; the dashes and marks as the plain overlay) when
        /// it is on and the camera makes its opaque copy; else the plain overlay material (plain).</summary>
        Material BlindFillMat(Material plain)
        {
            if (greyFillFrame != Time.frameCount)
            {   // once a frame: the switch, the camera's opaque copy (a graphics preset can change it), the shader
                greyFillFrame = Time.frameCount;
                string why;
                if (!blindGreyGround) { greyFillOk = false; why = "switched off (blindGreyGround)"; }
                else if (!GreyCopy(out why)) greyFillOk = false;
                else { greyFillOk = GreyFillMaterial(plain); why = greyFillOk ? "ok" : greyShaderWhy; }
                BlindGreyGroundOn = greyFillOk; BlindGreyGroundWhy = why;
            }
            if (!greyFillOk) return plain;
            greyFillMat.SetFloat(GreyFullAId, Mathf.Max(0.02f, Mathf.Lerp(blindDarkDay, blindDarkNight, NightGrade.NightK) * alpha));   // the fill's alpha in the ring's hold
            greyFillMat.SetFloat(GreyWarmId, blindGreyWarm);
            greyFillMat.SetFloat(GreyCoolId, blindGreyCool);
            return greyFillMat;
        }

        /// <summary>True when the camera makes its opaque copy (the active pipeline asset, and the main camera's own setting).</summary>
        static UniversalRenderPipelineAsset greyOffAsset; static string greyOffWhy;

        static bool GreyCopy(out string why)
        {
            var a = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!a) { why = "no URP asset active"; return false; }
            var cam = Camera.main;
            var data = cam ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
            bool color = data ? data.requiresColorTexture : a.supportsCameraOpaqueTexture;
            if (color) { why = "ok"; return true; }
            if (greyOffAsset != a || greyOffWhy == null) { greyOffAsset = a; greyOffWhy = "the camera's opaque copy is off (" + a.name + ")"; }   // made once per pipeline asset: no garbage per frame
            why = greyOffWhy;
            return false;
        }

        /// <summary>The grey ground's material, made once: OpenZ/BlindGrey (Resources/OpenZ/FX/OzBlindGrey.shader), depth tested as the
        /// ring, in the plain material's queue.</summary>
        bool GreyFillMaterial(Material plain)
        {
            if (greyFillMat) return true;
            if (greyFillTried) return false;
            greyFillTried = true;
            var sh = Resources.Load<Shader>("OpenZ/FX/OzBlindGrey");
            if (!sh) sh = Shader.Find("OpenZ/BlindGrey");
            if (!sh || !sh.isSupported)
            {
                greyShaderWhy = "shader OpenZ/BlindGrey not found or not supported";
                Debug.LogWarning("[OpenZ] blind sectors: " + greyShaderWhy + ": the plain darkening");
                return false;
            }
            greyFillMat = new Material(sh) { name = "OZ_RangeRing_BlindGrey" };
            greyFillMat.SetColor("_Color", Color.white);
            greyFillMat.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            if (plain) greyFillMat.renderQueue = plain.renderQueue;
            return true;
        }

        /// <summary>BlindDestroy's hook: the grey dots' meshes and material, the grey ground's material.</summary>
        void BlindGreyDestroy()
        {
            foreach (var kv in blinds) if (kv.Value.greyMesh) Destroy(kv.Value.greyMesh);
            if (greyDotMat) Destroy(greyDotMat);
            if (greyFillMat) Destroy(greyFillMat);
            greyFillTried = false; greyFillFrame = -1;
        }
    }
}
