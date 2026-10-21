using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    /// <summary>
    /// Item 212, round 3: WHY the broken fort cannon reads near black only once its pieces lie on the ground, decided by measurement
    /// in one Play session, and the honest brightness measure the fort-blast checks use from now on.
    /// <list type="bullet">
    /// <item><b>The measure</b> (<see cref="MeasureNow"/>, <see cref="KindMask"/>, <see cref="Analyse"/>): the pieces' OWN pixels. One frame is
    /// drawn with the pieces in a flat magenta, the concrete chunks beside them (within <see cref="NearRadius"/> m) in flat yellow and the
    /// other chunks in flat cyan (unlit materials on the same meshes, their paint blocks lifted for that frame: depth, shadows and occlusion
    /// stay exactly as drawn), one frame as is, the same instant. A pixel belongs to what the first frame says it is, but ONLY where the
    /// two frames differ by more than <see cref="SwapDiff"/> (of 255) in a channel: a swapped surface jumps to its saturated flat colour,
    /// an unswapped surface is the same pixel in both frames, so the jungle, a yellow chevron on the fort, fire-lit ground and the sky
    /// never count as a chunk, and a piece behind a chunk counts as the chunk, as drawn (edges eroded 1 px). The earlier checks projected
    /// mesh vertices into a 1280 x 720 frame but mapped them with the Game view's own aspect, so near the frame's edge the points landed
    /// beside the pieces (on the ground, the scorch, a shadow) and half the median was not the pieces at all; the A/B prints that offset.
    /// The reference is the chunks BESIDE the pieces (the same light, the same vignette), else the chunks at the pieces' distance from the
    /// frame's centre, else all debris; all are printed, with the mask's pixel counts per kind (a plausible count is a few thousand pixels
    /// on 2-4 chunks, never hundreds of thousands), the ground beside the pieces against the ground at the debris and the vignette's
    /// model value at both places.</item>
    /// <item><b>The A/B</b> (<see cref="Begin"/>, menu OpenZ/V1.1/KD3): at +2 s the thrown cannon in flight, at <see cref="ProbeAt"/> s the resting
    /// pieces, one real frame per probe with the ending camera held still (its orbit off, the cut to the loungers and the ending's safety
    /// release held <see cref="HoldSeconds"/> s for this session only): as is; effects shown (the player's eye: smoke, dust and fire in front);
    /// ambient occlusion off; the ending vignette off; post-processing off; the blaze's and the wrecks' point lights off; the sun's shadows
    /// off; the pieces casting no shadows; the torn rims, ribs and rebar hidden; the pieces drawn as concrete chunks; the scorch marks
    /// hidden (the ground beside them); the atmosphere pass off; as is again (noise); then a search over the rest look's two numbers
    /// (OzFortGunRestLook.PieceTone / FaceLift). Everything is put back in the same frame. The verdict names the cause from the numbers
    /// and the values to set; if the hold runs out before the last row, the log says so where it happened. Test tool only: nothing is saved.</item>
    /// </list>
    /// </summary>
    public sealed class OzKD3Probe : MonoBehaviour
    {
        public const float BandLo = 0.60f, BandHi = 1.00f;
        /// <summary>When the resting pieces are measured (s after the blast), when the flight is, what "beside them" means (m from the pieces'
        /// middle), and how long the ending camera is held on the fort for the A/B (s after the blast; the probes need about 16 s from ProbeAt).</summary>
        public static float ProbeAt = 8f, FlightAt = 2f, NearRadius = 16f, HoldSeconds = 24f;
        /// <summary>A pixel counts as a swapped surface only where the id frame differs from the as-is frame by more than this (of 255) in a channel.</summary>
        public const int SwapDiff = 60;
        /// <summary>The last A/B's log and verdict (the menu prints it; also readable as OpenZ.OzKD3Probe.Report).</summary>
        public static string Report { get; private set; } = "-";
        public static bool Running => I;
        public static float LastRest, LastFlight;
        static OzKD3Probe I;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static Material idPiece, idNear, idOther, chunkMat;
        static readonly float[] lin = Lin();

        static float[] Lin()
        {
            var t = new float[256];
            for (int i = 0; i < 256; i++) { float c = i / 255f; t[i] = c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f); }
            return t;
        }

        // ------------------------------------------------------------------ the measure
        public sealed class Measure
        {
            public int width, height, pieces, refPixels, all, ground, groundFort, maskPieces, maskNear, maskOther;
            public string refKind = "-";
            public float piecesY, refY, allY, groundY, groundFortY, piecesP75, refP75, pieceVig = 1f, refVig = 1f, allVig = 1f;
            public Vector2 pieceCentre = new Vector2(0.5f, 0.5f);
            public bool Few => pieces < 60 || refPixels < 200;
            /// <summary>The pieces against the concrete chunks beside them (0 = too few pixels).</summary>
            public float Ratio => refY > 0f && !Few ? piecesY / refY : 0f;
            public float RatioAll => allY > 0f && pieces >= 60 && all >= 200 ? piecesY / allY : 0f;
            public float GroundRatio => groundFortY > 0f && ground >= 100 && groundFort >= 100 ? groundY / groundFortY : 0f;
            public string Info =>
                $"id mask (pixels the swap changed): pieces {maskPieces}, chunks within {NearRadius:0} m {maskNear}, other chunks {maskOther}; "
                + $"pieces median {piecesY:0.000} ({pieces} px, their own pixels; lit quarter {piecesP75:0.000}), the concrete chunks beside them median {refY:0.000} ({refPixels} px, {refKind}; lit quarter {refP75:0.000}), ratio {Ratio:0.00}; "
                + $"all debris median {allY:0.000} ({all} px), ratio {RatioAll:0.00}; the ground beside the pieces {groundY:0.000} ({ground} px) against the ground at the debris {groundFortY:0.000} ({groundFort} px) = x{GroundRatio:0.00}; "
                + $"vignette model x{pieceVig:0.00} on the pieces, x{refVig:0.00} on their chunks, x{allVig:0.00} on all debris; pieces at ({pieceCentre.x:0.00}, {pieceCentre.y:0.00}) of the {width}x{height} frame (display luminance, linear)"
                + (Few ? " (too few visible pixels: turn the camera onto them)" : "");
        }

        struct Vig { public bool on; public float i3, s5, cx, cy, ax; }

        /// <summary>The live vignette (the volume stack as the camera sees it), as the pipeline computes it.</summary>
        static Vig LiveVignette(float aspect)
        {
            var v = new Vig { ax = 1f };
            var vg = VolumeManager.instance != null && VolumeManager.instance.stack != null ? VolumeManager.instance.stack.GetComponent<Vignette>() : null;
            if (vg == null || !vg.IsActive()) return v;
            v.on = true; v.i3 = vg.intensity.value * 3f; v.s5 = vg.smoothness.value * 5f; v.cx = vg.center.value.x; v.cy = vg.center.value.y; v.ax = vg.rounded.value ? aspect : 1f;
            return v;
        }

        static float VigAt(in Vig v, float u, float w)
        {
            if (!v.on) return 1f;
            float dx = Mathf.Abs(u - v.cx) * v.i3 * v.ax, dy = Mathf.Abs(w - v.cy) * v.i3;
            return Mathf.Pow(Mathf.Clamp01(1f - (dx * dx + dy * dy)), v.s5);
        }

        /// <summary>The live vignette's display factor at a frame point (u, v in 0..1).</summary>
        public static float VignetteAt(float u, float w, float aspect) { var v = LiveVignette(aspect); return VigAt(v, u, w); }

        static int Kind(Color32 c)
        {
            float r = c.r, g = c.g, b = c.b; const float k = 2.2f, lo = 14f;
            if (r > lo && b > lo && r > k * g && b > k * g) return 1;                            // magenta: a piece
            if (r > lo && g > lo && r > k * b && g > k * b) return 2;                            // yellow: a chunk beside the pieces
            if (g > lo && b > lo && g > k * r && b > k * r) return 3;                            // cyan: another chunk
            return 0;
        }

        /// <summary>The kind of every pixel of an id frame (1 a piece, 2 a chunk beside them, 3 another chunk, 0 nothing), counted only where
        /// the id frame differs from the as-is frame of the same instant by more than SwapDiff in a channel: a swapped surface jumps to its
        /// saturated flat colour, an unswapped surface (the jungle, a yellow chevron, fire-lit ground, the sky) is the same pixel in both
        /// frames and drops out, as does a piece pixel hidden behind a chunk. Built once per id frame and reused for every probe of it.</summary>
        public static byte[] KindMask(Color32[] ids, Color32[] asIs, int W, int H)
        {
            var mask = new byte[W * H];
            if (ids == null || asIs == null || ids.Length != W * H || asIs.Length != W * H) return mask;
            for (int i = 0; i < mask.Length; i++)
            {
                Color32 a = ids[i], b = asIs[i];
                int d = Mathf.Max(Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g)), Mathf.Abs(a.b - b.b));
                if (d > SwapDiff) mask[i] = (byte)Kind(a);
            }
            return mask;
        }

        static float Y(Color32 c) => lin[c.r] * 0.2126f + lin[c.g] * 0.7152f + lin[c.b] * 0.0722f;

        static float Median(List<float> a, float q = 0.5f) { if (a.Count == 0) return 0f; a.Sort(); return a[Mathf.Clamp((int)(a.Count * q), 0, a.Count - 1)]; }

        /// <summary>One frame and the kind mask of its id frame (KindMask): what the pieces read against the chunks beside them.</summary>
        public static Measure Analyse(Color32[] src, byte[] mask, int W, int H)
        {
            var m = new Measure { width = W, height = H };
            if (src == null || mask == null || src.Length != W * H || mask.Length != W * H) return m;
            for (int i = 0; i < mask.Length; i++) { byte k = mask[i]; if (k == 1) m.maskPieces++; else if (k == 2) m.maskNear++; else if (k == 3) m.maskOther++; }
            var k2 = new byte[W * H];
            for (int y = 1; y < H - 1; y++)
                for (int x = 1; x < W - 1; x++)
                {
                    int i = y * W + x; byte k = mask[i];
                    if (k != 0 && mask[i - 1] == k && mask[i + 1] == k && mask[i - W] == k && mask[i + W] == k) k2[i] = k;
                }
            var lp = new List<float>(); var ln = new List<float>(); var lo = new List<float>();
            var xs = new List<float>(); var ys = new List<float>(); double cx = 0, cy = 0; int nc = 0;
            float aspect = W / (float)H; var vig = LiveVignette(aspect); double vp = 0, vn = 0, va = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x; byte k = k2[i]; if (k == 0) continue;
                    float yy = Y(src[i]); float vg = VigAt(vig, x / (float)W, y / (float)H);
                    if (k == 1) { lp.Add(yy); xs.Add(x); ys.Add(y); vp += vg; }
                    else { if (k == 2) { ln.Add(yy); vn += vg; } lo.Add(yy); va += vg; cx += x; cy += y; nc++; }
                }
            m.pieces = lp.Count; m.all = lo.Count;
            if (lp.Count > 0) { m.pieceVig = (float)(vp / lp.Count); m.piecesP75 = Median(new List<float>(lp), 0.75f); m.piecesY = Median(lp); }
            if (lo.Count > 0) { m.allVig = (float)(va / lo.Count); m.allY = Median(new List<float>(lo)); }
            // the reference: the chunks beside the pieces; else chunks at the pieces' own vignette value; else all debris
            if (ln.Count >= 200) { m.refKind = "within " + NearRadius.ToString("0") + " m"; m.refPixels = ln.Count; m.refVig = (float)(vn / ln.Count); m.refP75 = Median(new List<float>(ln), 0.75f); m.refY = Median(ln); }
            else
            {
                var lr = new List<float>(); double vr = 0;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int i = y * W + x; if (k2[i] != 2 && k2[i] != 3) continue;
                        float vg = VigAt(vig, x / (float)W, y / (float)H);
                        if (Mathf.Abs(vg - m.pieceVig) <= 0.12f) { lr.Add(Y(src[i])); vr += vg; }
                    }
                if (lr.Count >= 200) { m.refKind = "at the pieces' distance from the frame's centre"; m.refPixels = lr.Count; m.refVig = (float)(vr / lr.Count); m.refP75 = Median(new List<float>(lr), 0.75f); m.refY = Median(lr); }
                else { m.refKind = "all debris (none beside them)"; m.refPixels = lo.Count; m.refVig = m.allVig; m.refP75 = lo.Count > 0 ? Median(new List<float>(lo), 0.75f) : 0f; m.refY = m.allY; }
            }
            // the ground: a box round the pieces (their 5-95 % extent, grown 35 %) and the same box round the debris' middle
            if (xs.Count > 0)
            {
                xs.Sort(); ys.Sort();
                float x0 = xs[(int)(xs.Count * 0.05f)], x1 = xs[Mathf.Min(xs.Count - 1, (int)(xs.Count * 0.95f))], y0 = ys[(int)(ys.Count * 0.05f)], y1 = ys[Mathf.Min(ys.Count - 1, (int)(ys.Count * 0.95f))];
                float mx = Mathf.Max(24f, (x1 - x0) * 0.35f), my = Mathf.Max(24f, (y1 - y0) * 0.35f);
                m.pieceCentre = new Vector2((x0 + x1) * 0.5f / W, (y0 + y1) * 0.5f / H);
                var g1 = Box(src, k2, W, H, x0 - mx, y0 - my, x1 + mx, y1 + my); m.ground = g1.Count; m.groundY = Median(g1);
                if (nc > 0)
                {
                    float hw = (x1 - x0) * 0.5f + mx, hh = (y1 - y0) * 0.5f + my; float fx = (float)(cx / nc), fy = (float)(cy / nc);
                    var g2 = Box(src, k2, W, H, fx - hw, fy - hh, fx + hw, fy + hh); m.groundFort = g2.Count; m.groundFortY = Median(g2);
                }
            }
            return m;
        }

        static List<float> Box(Color32[] src, byte[] k2, int W, int H, float x0, float y0, float x1, float y1)
        {
            var l = new List<float>();
            int ax = Mathf.Clamp((int)x0, 0, W - 1), bx = Mathf.Clamp((int)x1, 0, W - 1), ay = Mathf.Clamp((int)y0, 0, H - 1), by = Mathf.Clamp((int)y1, 0, H - 1);
            for (int y = ay; y <= by; y++) for (int x = ax; x <= bx; x++) { int i = y * W + x; if (k2[i] == 0) l.Add(Y(src[i])); }
            return l;
        }

        // ------------------------------------------------------------------ the id frame (unlit flat colours on the same meshes)
        static Material IdMat(ref Material m, Color c, string name)
        {
            if (m) return m;
            var sh = Shader.Find("Universal Render Pipeline/Unlit"); if (!sh) return null;
            m = new Material(sh) { name = name }; m.SetColor(BaseColorId, c);
            return m;
        }

        sealed class Saved { public Renderer r; public Material[] mats; public MaterialPropertyBlock[] blocks; public MaterialPropertyBlock whole; }

        static Saved Keep(Renderer r)
        {
            var s = new Saved { r = r, mats = r.sharedMaterials, blocks = new MaterialPropertyBlock[r.sharedMaterials.Length], whole = new MaterialPropertyBlock() };
            r.GetPropertyBlock(s.whole);
            for (int i = 0; i < s.blocks.Length; i++) { s.blocks[i] = new MaterialPropertyBlock(); r.GetPropertyBlock(s.blocks[i], i); }
            return s;
        }

        /// <summary>The renderers in one flat unlit colour, their paint blocks lifted (a block's _BaseColor would tint the flat colour).</summary>
        static List<Saved> SwapIds(IList<Renderer> rs, Material id)
        {
            var saved = new List<Saved>(); if (!id) return saved;
            foreach (var r in rs)
            {
                if (!r) continue;
                var s = Keep(r); saved.Add(s);
                var ids = new Material[s.mats.Length]; for (int i = 0; i < ids.Length; i++) ids[i] = id;
                r.sharedMaterials = ids; r.SetPropertyBlock(null);
                for (int i = 0; i < ids.Length; i++) r.SetPropertyBlock(null, i);
            }
            return saved;
        }

        static void Restore(List<Saved> saved)
        {
            foreach (var s in saved)
            {
                if (!s.r) continue;
                s.r.sharedMaterials = s.mats;
                s.r.SetPropertyBlock(s.whole.isEmpty ? null : s.whole);
                for (int i = 0; i < s.blocks.Length; i++) s.r.SetPropertyBlock(s.blocks[i].isEmpty ? null : s.blocks[i], i);
            }
        }

        /// <summary>The effects a measured frame leaves out (as the fort-blast checks: particles, the mushroom, the sprite batches); the
        /// list of components is kept for the callers (nothing but renderers is switched for the frame).</summary>
        public static List<Behaviour> HideEffects(List<Renderer> hidden)
        {
            var comps = new List<Behaviour>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.enabled && (r is ParticleSystemRenderer || r.GetComponentInParent<NukeFx>() || r.GetComponentInParent<OpenZNukeMushroom>())) { r.enabled = false; hidden.Add(r); }
            return comps;
        }

        public static void ShowEffects(List<Renderer> hidden, List<Behaviour> comps)
        {
            foreach (var r in hidden) if (r) r.enabled = true;
            foreach (var c in comps) if (c) c.enabled = true;
            hidden.Clear(); comps.Clear();
        }

        static List<Renderer> Renderers(IEnumerable<Transform> roots)
        {
            var rs = new List<Renderer>();
            foreach (var t in roots) if (t) foreach (var r in t.GetComponentsInChildren<Renderer>()) if (r.enabled) rs.Add(r);
            return rs;
        }

        static Vector3 Middle(List<Renderer> rs)
        {
            var c = Vector3.zero; int n = 0;
            foreach (var r in rs) if (r) { c += r.bounds.center; n++; }
            return n > 0 ? c / n : Vector3.zero;
        }

        static void Chunks(Vector3 middle, out List<Renderer> near, out List<Renderer> other)
        {
            near = new List<Renderer>(); other = new List<Renderer>();
            foreach (var p in FindObjectsByType<BuildingBreaker.Piece>(FindObjectsSortMode.None))
            {
                if (!p) continue;
                foreach (var r in p.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled) continue;
                    var d = r.bounds.center - middle; d.y = 0f;
                    (d.magnitude <= NearRadius ? near : other).Add(r);
                }
            }
        }

        static Texture2D RenderTo(Camera cam, int W, int H)
        {
            var rt = RenderTexture.GetTemporary(W, H, 24); var prevT = cam.targetTexture; var prevA = RenderTexture.active;
            cam.targetTexture = rt; cam.Render(); cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            cam.targetTexture = prevT; RenderTexture.active = prevA; RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        /// <summary>The honest measure for the checks, now: two renders of the camera at the Game view's own size (the id frame and the frame
        /// as is, the same instant), the effects hidden unless `effectsShown`. roots = the thrown cannon or its pieces.</summary>
        public static Measure MeasureNow(Camera cam, IEnumerable<Transform> roots, bool effectsShown)
        {
            if (!cam) return new Measure();
            var pieceRs = Renderers(roots);
            Chunks(Middle(pieceRs), out var near, out var other);
            int W = Mathf.Max(320, cam.pixelWidth), H = Mathf.Max(200, cam.pixelHeight);
            var hidden = new List<Renderer>(); var comps = effectsShown ? new List<Behaviour>() : HideEffects(hidden);
            var s1 = SwapIds(pieceRs, IdMat(ref idPiece, new Color(1f, 0f, 1f), "KD3_id_piece"));
            var s2 = SwapIds(near, IdMat(ref idNear, new Color(1f, 1f, 0f), "KD3_id_near"));
            var s3 = SwapIds(other, IdMat(ref idOther, new Color(0f, 1f, 1f), "KD3_id_other"));
            var ids = RenderTo(cam, W, H);
            Restore(s1); Restore(s2); Restore(s3);
            var src = RenderTo(cam, W, H);
            ShowEffects(hidden, comps);
            var px = src.GetPixels32();
            var m = Analyse(px, KindMask(ids.GetPixels32(), px, W, H), W, H);
            Destroy(ids); Destroy(src);
            return m;
        }

        // ------------------------------------------------------------------ the A/B (Play; one real frame per probe)
        /// <summary>Starts the A/B: the enemy fort falls (the ending runs as in the game; its camera orbit is held off for this session and
        /// put back at the end), the cannon is measured in flight at +FlightAt s and its pieces at +ProbeAt s. False = cannot start.</summary>
        public static bool Begin()
        {
            if (!Application.isPlaying || I) return false;
            var go = new GameObject("OZ_KD3Probe"); I = go.AddComponent<OzKD3Probe>();
            return true;
        }

        readonly StringBuilder sb = new StringBuilder();
        readonly List<Renderer> hidden = new List<Renderer>(); readonly List<Behaviour> comps = new List<Behaviour>();
        readonly List<System.Action> undo = new List<System.Action>();
        float pid0, mh0, fz0, pz0; bool orbit0; bool began;
        float blast = -1f, lateAt = -1f;                                                       // the blast's time; when the hold ran out (-1 = it did not)
        Texture2D shot;

        void Start()
        {
            pid0 = OpenZVictory.PostImpactDelay; mh0 = OpenZVictory.MaxHold; fz0 = OpenZEnding.FortZoom; pz0 = OpenZEnding.PushInZoom; orbit0 = OpenZEnding.Orbit;
            // the cut to the loungers waits HoldSeconds after the impact; the ending's safety release counts from the game over (+ the nuke
            // intro's hold), 4-6 s BEFORE the impact, so it is moved out as far for this session; both are put back in PutBack
            OpenZVictory.PostImpactDelay = HoldSeconds; OpenZVictory.MaxHold = Mathf.Max(mh0, HoldSeconds + 16f);
            OpenZEnding.FortZoom = 70f; OpenZEnding.PushInZoom = 70f; OpenZEnding.Orbit = false;
            began = true;
            StartCoroutine(Run());
        }

        void PutBack()
        {
            for (int i = undo.Count - 1; i >= 0; i--) { try { undo[i]?.Invoke(); } catch (System.Exception) { } }
            undo.Clear();
            ShowEffects(hidden, comps);
            if (began) { OpenZVictory.PostImpactDelay = pid0; OpenZVictory.MaxHold = mh0; OpenZEnding.FortZoom = fz0; OpenZEnding.PushInZoom = pz0; OpenZEnding.Orbit = orbit0; began = false; }
        }

        void OnDestroy() { PutBack(); if (shot) Destroy(shot); if (I == this) I = null; }

        /// <summary>The next frame's Game view, as drawn (post-processing, the vignette, the sprite batches and the scorch marks included). Once
        /// the hold on the fort is about to run out (the ending camera leaves at +HoldSeconds), the log says so at that place: the rows
        /// after that line were not measured on the pieces.</summary>
        IEnumerator Shot()
        {
            if (shot) { Destroy(shot); shot = null; }
            yield return new WaitForEndOfFrame();
            shot = ScreenCapture.CaptureScreenshotAsTexture();
            if (lateAt < 0f && blast >= 0f && Time.time - blast > HoldSeconds - 1f)
            {
                lateAt = Time.time - blast;
                sb.AppendLine($"  info  +{lateAt:0.0} s: the {HoldSeconds:0} s hold on the fort has run out (the ending camera leaves): the rows below this line were NOT measured on the pieces; run again in a new session (a longer OzKD3Probe.HoldSeconds if it happens again)");
            }
        }

        static string Row(string what, float r, float asIs, string extra = "") => $"  probe  {what}: ratio {r:0.00} (as is {asIs:0.00}, x{(asIs > 0f ? r / asIs : 0f):0.00}){extra}";

        IEnumerator Run()
        {
            sb.Length = 0;
            if (!IdMat(ref idPiece, new Color(1f, 0f, 1f), "KD3_id_piece")) { Finish("[OpenZ KD3] the unlit shader is missing: no id frame, no measure"); yield break; }
            int n = OpenZVictory.DebugFortFalls(true);
            if (n == 0) { Finish("[OpenZ KD3] no enemy fort standing"); yield break; }
            sb.AppendLine($"[OpenZ KD3] cannon rest darkness A/B, {n} enemy fort part(s) destroyed; {OzFortGunRestLook.State}; {OzFortGunWreckTone.State}; wreck look {(OzFortGunWreckLook.Enabled ? "on" : "OFF")}, Tone {OzFortGunWreckLook.Tone:0.00}; the camera held {HoldSeconds:0} s after the blast");
            float until = Time.realtimeSinceStartup + 120f; blast = -1f;
            while (blast < 0f)
            {
                if (OpenZNukeMushroom.Current && OpenZNukeMushroom.Current.Age > 0f) blast = Time.time - OpenZNukeMushroom.Current.Age;
                else if (Time.realtimeSinceStartup > until) { Finish("[OpenZ KD3] no mushroom appeared"); yield break; }
                yield return null;
            }
            var cam = Camera.main;
            // ---- in flight (two renders of the same instant: the cannon moves)
            while (Time.time - blast < FlightAt) yield return null;
            var yaw = OzFortGunWreckLook.LastYaw;
            if (cam && yaw && yaw.gameObject.activeInHierarchy)
            {
                var mf = MeasureNow(cam, new[] { yaw }, false);
                LastFlight = mf.Ratio;
                sb.AppendLine($"+{Time.time - blast:0.00} s, the thrown cannon IN FLIGHT, as is: {mf.Info}");
            }
            else { LastFlight = 0f; sb.AppendLine("  info  in flight: no thrown cannon (the wreck look did not run?)"); }
            // ---- at rest
            while (Time.time - blast < ProbeAt) yield return null;
            var pieces = new List<OzFortGunBreakup>(); foreach (var p in OzFortGunBreakup.LastPieces) if (p) pieces.Add(p);
            if (!cam || pieces.Count == 0) { Finish(sb.ToString() + "  info  no cannon pieces at +" + ProbeAt.ToString("0") + " s (the cannon landed whole, or over water)"); yield break; }
            int rested = 0; var roots = new List<Transform>(); foreach (var p in pieces) { roots.Add(p.transform); if (p.Rested) rested++; }
            var pieceRs = Renderers(roots); var middle = Middle(pieceRs);
            Chunks(middle, out var near, out var other);
            int W = cam.pixelWidth, H = cam.pixelHeight;
            sb.AppendLine($"+{Time.time - blast:0.00} s, the RESTING PIECES ({pieces.Count}, {rested} at rest), {near.Count} concrete chunk(s) within {NearRadius:0} m, {other.Count} elsewhere; the Game view {W}x{H}; ending vignette live {OzEndingVignette.Live:0.00}");
            // the id frame (effects hidden), then the frame as is; the kind mask of the two (the pixels the swap changed) serves every probe below
            comps.AddRange(HideEffects(hidden));
            IdMat(ref idNear, new Color(1f, 1f, 0f), "KD3_id_near"); IdMat(ref idOther, new Color(0f, 1f, 1f), "KD3_id_other");
            var s1 = SwapIds(pieceRs, idPiece); var s2 = SwapIds(near, idNear); var s3 = SwapIds(other, idOther);
            yield return Shot(); Restore(s1); Restore(s2); Restore(s3);
            if (!shot || shot.width != W || shot.height != H) { Finish(sb.ToString() + $"  info  the Game view could not be captured ({(shot ? shot.width + "x" + shot.height : "nothing")} for {W}x{H}): run the KD check's brightness line instead"); yield break; }
            var ids = shot.GetPixels32(); int Wi = shot.width, Hi = shot.height;
            yield return Shot(); var src0 = shot.GetPixels32(); var mask = KindMask(ids, src0, Wi, Hi); var m0 = Analyse(src0, mask, Wi, Hi);
            float asIs = m0.Ratio;
            sb.AppendLine($"  as is (effects hidden): {m0.Info}");
            if (m0.Few) { Finish(sb.ToString() + "  info  too few visible pixels for a verdict: turn the camera onto the pieces and run again"); yield break; }
            // the earlier checks' sampling on the same scene: a 1280 x 720 frame mapped with the view's aspect
            sb.AppendLine("  info  " + OldSampling(cam, roots, pieceRs, near, other));
            // the player's eye: effects shown (the same mask: a piece behind smoke is still the piece)
            ShowEffects(hidden, comps); yield return Shot(); var mE = Analyse(shot.GetPixels32(), mask, Wi, Hi); comps.AddRange(HideEffects(hidden));
            sb.AppendLine(Row("effects shown (smoke, dust and fire in front: the player's eye)", mE.Ratio, asIs, $"; all debris {mE.RatioAll:0.00}"));
            // ambient occlusion
            var ssao = Feature("ScreenSpaceAmbientOcclusion"); float rS = asIs;
            if (ssao && ssao.isActive) { ssao.SetActive(false); undo.Add(() => ssao.SetActive(true)); yield return Shot(); ssao.SetActive(true); undo.RemoveAt(undo.Count - 1); rS = Analyse(shot.GetPixels32(), mask, Wi, Hi).Ratio; sb.AppendLine(Row("ambient occlusion off", rS, asIs)); }
            else sb.AppendLine("  probe  ambient occlusion: " + (ssao ? "already off on this quality" : "feature not found") + ": nothing to measure");
            // the ending vignette
            Volume endVol = null; foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None)) if (v && v.name == "OZ_EndingVignette") endVol = v;
            float rV = asIs, rVall = m0.RatioAll, gV = m0.GroundRatio;
            if (endVol)
            {
                float w0 = endVol.weight; endVol.weight = 0f; undo.Add(() => endVol.weight = w0); yield return Shot(); endVol.weight = w0; undo.RemoveAt(undo.Count - 1);
                var mV = Analyse(shot.GetPixels32(), mask, Wi, Hi); rV = mV.Ratio; rVall = mV.RatioAll; gV = mV.GroundRatio;
                sb.AppendLine(Row("the ending vignette off (the game's own 0.2 stays)", rV, asIs, $"; all debris {rVall:0.00} (as is {m0.RatioAll:0.00}, x{(m0.RatioAll > 0f ? rVall / m0.RatioAll : 0f):0.00}); the ground beside the pieces x{gV:0.00} of the ground at the debris (as is x{m0.GroundRatio:0.00})"));
            }
            else sb.AppendLine("  probe  the ending vignette: no OZ_EndingVignette volume in the scene (the ending is not running?)");
            // post-processing
            var acd = cam.GetUniversalAdditionalCameraData(); float rP = asIs, rPall = m0.RatioAll;
            if (acd && acd.renderPostProcessing) { acd.renderPostProcessing = false; undo.Add(() => acd.renderPostProcessing = true); yield return Shot(); acd.renderPostProcessing = true; undo.RemoveAt(undo.Count - 1); var mP = Analyse(shot.GetPixels32(), mask, Wi, Hi); rP = mP.Ratio; rPall = mP.RatioAll; sb.AppendLine(Row("post-processing off (no vignette, grade, tonemap, bloom)", rP, asIs, $"; all debris {rPall:0.00}")); }
            // the point lights (the blaze, the burning wrecks, lamps)
            var lights = new List<Light>(); foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.enabled && l.type != LightType.Directional) lights.Add(l);
            foreach (var l in lights) l.enabled = false; undo.Add(() => { foreach (var l in lights) if (l) l.enabled = true; });
            yield return Shot(); foreach (var l in lights) if (l) l.enabled = true; undo.RemoveAt(undo.Count - 1);
            var mL = Analyse(shot.GetPixels32(), mask, Wi, Hi);
            sb.AppendLine(Row($"the {lights.Count} point light(s) off (the blaze's fire light, the burning wrecks)", mL.Ratio, asIs, $"; all debris {mL.RatioAll:0.00} (as is {m0.RatioAll:0.00}); their chunks' median {mL.refY:0.000} (as is {m0.refY:0.000}), all debris {mL.allY:0.000} (as is {m0.allY:0.000})"));
            // the sun's shadows
            var sun = RenderSettings.sun; if (!sun) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional && l.isActiveAndEnabled && (!sun || l.intensity > sun.intensity)) sun = l;
            float rSh = asIs;
            if (sun && sun.shadows != LightShadows.None) { var sh0 = sun.shadows; sun.shadows = LightShadows.None; undo.Add(() => sun.shadows = sh0); yield return Shot(); sun.shadows = sh0; undo.RemoveAt(undo.Count - 1); rSh = Analyse(shot.GetPixels32(), mask, Wi, Hi).Ratio; sb.AppendLine(Row("the sun's shadows off (everything)", rSh, asIs)); }
            // the pieces cast no shadows (the clump shading itself)
            var cast = new List<(Renderer r, ShadowCastingMode m)>(); foreach (var r in pieceRs) if (r) { cast.Add((r, r.shadowCastingMode)); r.shadowCastingMode = ShadowCastingMode.Off; }
            undo.Add(() => { foreach (var c in cast) if (c.r) c.r.shadowCastingMode = c.m; });
            yield return Shot(); foreach (var c in cast) if (c.r) c.r.shadowCastingMode = c.m; undo.RemoveAt(undo.Count - 1);
            float rSc = Analyse(shot.GetPixels32(), mask, Wi, Hi).Ratio; sb.AppendLine(Row("the pieces cast no shadows (no shading of each other)", rSc, asIs));
            // the dressing hidden (its own id frame and mask: the outline changes)
            var dress = new List<GameObject>(); foreach (var t in roots) foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == "OZ_WreckDress" && c.gameObject.activeSelf) dress.Add(c.gameObject);
            foreach (var d in dress) d.SetActive(false); undo.Add(() => { foreach (var d in dress) if (d) d.SetActive(true); });
            var pieceRs2 = Renderers(roots);
            var d1 = SwapIds(pieceRs2, idPiece); var d2 = SwapIds(near, idNear); var d3 = SwapIds(other, idOther);
            yield return Shot(); Restore(d1); Restore(d2); Restore(d3); var idsD = shot.GetPixels32();
            yield return Shot(); var srcD = shot.GetPixels32(); float rD = Analyse(srcD, KindMask(idsD, srcD, Wi, Hi), Wi, Hi).Ratio;
            foreach (var d in dress) if (d) d.SetActive(true); undo.RemoveAt(undo.Count - 1);
            sb.AppendLine(Row($"the torn rims, ribs and rebar hidden ({dress.Count} dressings; the paint and the cut faces stay)", rD, asIs));
            // drawn as concrete chunks
            var mats = AsChunk(pieceRs); yield return Shot(); Restore(mats); float rM = Analyse(shot.GetPixels32(), mask, Wi, Hi).Ratio;
            sb.AppendLine(Row("the pieces drawn as concrete chunks (the chunk library's material + tint on the same meshes)", rM, asIs));
            // the scorch marks (flat ground meshes: they can only change the ground beside the pieces)
            var marks = new List<Renderer>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None)) if (r.enabled && r.sharedMaterial && r.sharedMaterial.shader && r.sharedMaterial.shader.name == "OpenZ/ScorchDecal") marks.Add(r);
            float gS = m0.GroundRatio, gY = m0.groundY;
            if (marks.Count > 0)
            {
                foreach (var r in marks) r.enabled = false; undo.Add(() => { foreach (var r in marks) if (r) r.enabled = true; });
                yield return Shot(); foreach (var r in marks) if (r) r.enabled = true; undo.RemoveAt(undo.Count - 1);
                var mG = Analyse(shot.GetPixels32(), mask, Wi, Hi); gS = mG.GroundRatio; gY = mG.groundY;
                sb.AppendLine($"  probe  the {marks.Count} scorch mark(s) hidden: the pieces {mG.Ratio:0.00} (as is {asIs:0.00}); the ground beside the pieces {gY:0.000} (as is {m0.groundY:0.000}, x{(m0.groundY > 0f ? gY / m0.groundY : 0f):0.00}), against the ground at the debris x{gS:0.00} (as is x{m0.GroundRatio:0.00})");
            }
            else sb.AppendLine("  probe  the scorch marks: none drawn");
            // the atmosphere pass
            var atm = Feature("FullScreenPassRendererFeature"); float rA = asIs;
            if (atm && atm.isActive) { atm.SetActive(false); undo.Add(() => atm.SetActive(true)); yield return Shot(); atm.SetActive(true); undo.RemoveAt(undo.Count - 1); rA = Analyse(shot.GetPixels32(), mask, Wi, Hi).Ratio; sb.AppendLine(Row("the atmosphere pass off", rA, asIs)); }
            // as is again: the noise of one frame against the next (the fire light flickers)
            yield return Shot(); var m2 = Analyse(shot.GetPixels32(), mask, Wi, Hi);
            sb.AppendLine($"  info  everything put back: as is again {m2.Ratio:0.00} (all debris {m2.RatioAll:0.00})");
            float noise = asIs > 0f ? Mathf.Abs(m2.Ratio - asIs) / asIs : 0f;
            // the rest look's numbers: a search (the paint on the pieces x tone, the fixed surfaces x lift), against the chunks beside them;
            // it stops, and says so, when the hold runs out: a cut-short search never reads as a failed one
            var rows = new List<string>(); float bestT = -1f, bestL = -1f, bestR = 0f; bool cut = false;
            foreach (var (t, l) in new[] { (1f, 1f), (1f, 1.6f), (1.3f, 1.8f), (1.6f, 2.0f), (1.8f, 2.2f), (2.0f, 2.6f), (2.4f, 3.0f) })
            {
                if (lateAt < 0f) { OzFortGunRestLook.Probe(t, l); yield return Shot(); }
                if (lateAt >= 0f) { cut = true; rows.Add($"paint x{t:0.0} / fixed x{l:0.0} and the rows after it: not measured (the hold ran out)"); break; }
                float r = Analyse(shot.GetPixels32(), mask, Wi, Hi).Ratio;
                rows.Add($"paint x{t:0.0} / fixed x{l:0.0} -> {r:0.00}");
                if (r >= 0.70f && r <= BandHi && bestT < 0f) { bestT = t; bestL = l; bestR = r; }
            }
            OzFortGunRestLook.Probe(OzFortGunRestLook.PieceTone, OzFortGunRestLook.FaceLift);
            sb.AppendLine("  info  rest look search on the resting pieces (the paint's tone / the fixed surfaces' lift -> ratio against the chunks beside them): " + string.Join(", ", rows));
            ShowEffects(hidden, comps);
            // ---- the verdict
            sb.AppendLine("VERDICT (from the numbers above):");
            if (lateAt >= 0f) sb.AppendLine($"  info  the hold ran out at +{lateAt:0.0} s: the probe rows below that info line, and what this verdict builds on them, are not valid; run again in a new session");
            float X(float r) => asIs > 0f ? r / asIs : 1f;
            var causes = new List<(float x, string name, string detail)>
            {
                (m0.RatioAll > 0f ? rVall / m0.RatioAll : 1f, "the ending vignette at the landing spot", $"x{m0.pieceVig:0.00} there against x{m0.allVig:0.00} on the debris at the fort; off, the pieces read {rVall:0.00} of all debris instead of {m0.RatioAll:0.00}"),
                (X(rM), "the pieces' own levels", $"drawn as concrete they read {rM:0.00} of the chunks beside them instead of {asIs:0.00}"),
                (X(mE.Ratio), "smoke, dust and fire in front", $"the player's eye: {mE.Ratio:0.00} against {asIs:0.00} without them"),
                (X(rS), "ambient occlusion", $"off: {rS:0.00}"), (X(rSh), "the sun's shadows", $"off: {rSh:0.00}"), (X(rSc), "the clump shading itself", $"no cast shadows: {rSc:0.00}"),
                (X(rD), "the torn rims, ribs and rebar", $"hidden: {rD:0.00}"), (X(mL.Ratio), "the point lights", $"off: {mL.Ratio:0.00}"), (X(rP), "post-processing", $"off: {rP:0.00}"), (X(rA), "the atmosphere pass", $"off: {rA:0.00}"),
            };
            causes.Sort((a, b) => Mathf.Abs(Mathf.Log(Mathf.Max(1e-3f, b.x))).CompareTo(Mathf.Abs(Mathf.Log(Mathf.Max(1e-3f, a.x)))));
            float big = Mathf.Max(1.25f, 1f + 2f * noise);
            var named = new List<string>(); var none = new List<string>();
            foreach (var c in causes) { if (c.x >= big || c.x <= 1f / big) named.Add($"x{c.x:0.00} {c.name} ({c.detail})"); else none.Add(c.name); }
            sb.AppendLine($"  noise between two as-is frames {noise * 100f:0} %; a probe counts from x{big:0.00}");
            sb.AppendLine($"  the place: the ground beside the pieces reads x{m0.GroundRatio:0.00} of the ground at the debris (the scorch marks hidden x{gS:0.00}, the ending vignette off x{gV:0.00}): the pieces lie in a {(m0.GroundRatio > 0f && m0.GroundRatio < 0.6f ? "DARKER" : "comparable")} light than the fort's debris");
            sb.AppendLine("  CAUSE" + (named.Count > 1 ? "S" : "") + ": " + (named.Count > 0 ? string.Join("; ", named) : "none of the probes moves the pieces by x" + big.ToString("0.00") + ": they read as their material and their resting shading give"));
            if (none.Count > 0) sb.AppendLine("  not the cause: " + string.Join(", ", none));
            LastRest = asIs;
            bool landed = asIs >= BandLo && asIs <= BandHi, landedF = LastFlight >= BandLo && LastFlight <= BandHi;
            sb.AppendLine($"  {(landed ? "PASS" : "FAIL")}  the cannon pieces at rest read {asIs:0.00} of the concrete chunks beside them (band {BandLo:0.00}-{BandHi:0.00}; against all debris {m0.RatioAll:0.00}, of which the ending vignette takes x{(m0.allVig > 0f ? m0.pieceVig / m0.allVig : 1f):0.00})");
            sb.AppendLine($"  {(landedF ? "PASS" : "FAIL")}  the thrown cannon in flight reads {LastFlight:0.00} of the chunks beside it (band {BandLo:0.00}-{BandHi:0.00})");
            if (!landed) sb.AppendLine(bestT > 0f ? $"  -> set OzFortGunRestLook.PieceTone = {bestT:0.0}f and OzFortGunRestLook.FaceLift = {bestL:0.0}f (measured {bestR:0.00}) and run again in a new session"
                                                  : cut ? "  -> the rest look search was cut short by the hold before a row landed in the band: nothing is decided; run again in a new session"
                                                  : asIs < BandLo ? "  -> no search row lands in the band: send this log (the rest look's numbers may need to go above 2.4 / 3.0, or the place is the cause)" : "  -> too bright: lower OzFortGunRestLook.PieceTone by 0.2 and run again in a new session");
            Finish(sb.ToString());
        }

        /// <summary>The earlier checks' point sampling on this scene: a 1280 x 720 frame, the pieces' vertices mapped with the camera's current
        /// (Game view) aspect; where those points land against the pieces' own pixels in that frame, and what that method reads.</summary>
        string OldSampling(Camera cam, List<Transform> roots, List<Renderer> pieceRs, List<Renderer> near, List<Renderer> other)
        {
            const int W = 1280, H = 720;
            var s1 = SwapIds(pieceRs, idPiece); var s2 = SwapIds(near, idNear); var s3 = SwapIds(other, idOther);
            var ids = RenderTo(cam, W, H); Restore(s1); Restore(s2); Restore(s3);
            var src = RenderTo(cam, W, H);
            var px = src.GetPixels32(); var mask = KindMask(ids.GetPixels32(), px, W, H);
            var mm = Analyse(px, mask, W, H);
            // the old points: vertices pulled 8 % to the mesh centre, mapped with WorldToViewportPoint after the render (the view's aspect)
            var ys = new List<float>();
            int onPiece = 0, count = 0; double sumX = 0;
            foreach (var t in roots)
                foreach (var mf in t.GetComponentsInChildren<MeshFilter>())
                {
                    var m = mf.sharedMesh; if (!m || !m.isReadable || mf.name == "OZ_WreckDress") continue;
                    var v = m.vertices; var c = m.bounds.center; int step = Mathf.Max(1, v.Length / 160);
                    for (int i = 0; i < v.Length; i += step)
                    {
                        var w = mf.transform.TransformPoint(Vector3.Lerp(v[i], c, 0.08f)); var vp = cam.WorldToViewportPoint(w);
                        if (vp.z <= 0f || vp.x < 0.01f || vp.x > 0.99f || vp.y < 0.01f || vp.y > 0.99f) continue;
                        int x = Mathf.Clamp((int)(vp.x * W), 0, W - 1), y = Mathf.Clamp((int)(vp.y * H), 0, H - 1);
                        count++; sumX += x; ys.Add(Y(px[y * W + x])); if (mask[y * W + x] == 1) onPiece++;
                    }
                }
            Destroy(ids); Destroy(src);
            float aspect = cam.pixelWidth / (float)Mathf.Max(1, cam.pixelHeight);
            float modelPx = (mm.pieceCentre.x - 0.5f) * W * ((W / (float)H) / aspect - 1f);
            float measuredPx = count > 0 ? (float)(sumX / count) - mm.pieceCentre.x * W : 0f;
            float oldMedian = Median(ys);
            return $"the earlier checks' sampling on this scene: {count} points, {onPiece} of them ({(count > 0 ? 100f * onPiece / count : 0f):0} %) on the pieces' own pixels; the Game view's aspect {aspect:0.000} against the frame's {W / (float)H:0.000}: "
                 + $"a point at the pieces' place is read {Mathf.Abs(modelPx):0} px {(modelPx >= 0f ? "right" : "left")} of where it is drawn by that mismatch (measured: the points' middle lies {Mathf.Abs(measuredPx):0} px {(measuredPx >= 0f ? "right" : "left")} of the pieces' pixels' middle); "
                 + $"that method reads the pieces at {oldMedian:0.000} where their own pixels read {mm.piecesY:0.000} (ratio {(mm.refY > 0f ? oldMedian / mm.refY : 0f):0.00} against {mm.Ratio:0.00} by the pixels, both against the chunks beside them)";
        }

        static List<Saved> AsChunk(List<Renderer> rs)
        {
            if (!chunkMat)
            {
                ConcreteChunkLib.Load();
                var src = ConcreteChunkLib.Outer ? ConcreteChunkLib.Outer : Resources.Load<Material>(ConcreteChunkLib.Root + "Materials/OZC_Outer");
                if (src) chunkMat = new Material(src) { name = "KD3_probe_chunk" };
            }
            var saved = new List<Saved>(); if (!chunkMat) return saved;
            var tint = ConcreteChunkLib.LevelOuter * 0.98f; tint.a = 1f;
            foreach (var r in rs)
            {
                if (!r) continue;
                var s = Keep(r); saved.Add(s);
                var cm = new Material[s.mats.Length]; for (int i = 0; i < cm.Length; i++) cm[i] = chunkMat;
                r.sharedMaterials = cm; r.SetPropertyBlock(null);
                var b = new MaterialPropertyBlock(); b.SetColor(BaseColorId, tint);
                for (int i = 0; i < cm.Length; i++) r.SetPropertyBlock(b, i);
            }
            return saved;
        }

        /// <summary>A renderer feature of the active pipeline's renderers by its type name (ambient occlusion, the atmosphere pass).</summary>
        public static ScriptableRendererFeature Feature(string typeName)
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset; if (!asset) return null;
            var f = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
            var list = f != null ? f.GetValue(asset) as ScriptableRendererData[] : null; if (list == null) return null;
            foreach (var rd in list) if (rd) foreach (var feat in rd.rendererFeatures) if (feat && feat.GetType().Name == typeName) return feat;
            return null;
        }

        void Finish(string text)
        {
            Report = text;
            Debug.Log(text);
            PutBack();
            Destroy(gameObject);
        }
    }
}
