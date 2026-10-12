using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    /// <summary>
    /// Play mode, a level scene. For DAY (timeOfDay .08) and NIGHT (.62) it frames the
    /// enemy fort like the ending (RtsCamera focus + 9 m north, 78 m), renders a baseline, fires the nuke's look exactly as the
    /// strike's impact does (NukeFx.Trigger + OpenZBloomDirector.Flash(1); the sim is NOT touched: no fort dies, repeatable),
    /// and at game time 0.3 / 0.6 / 1.5 / 3 / 5 s after the blast it freezes the game (time scale 0) and renders the game
    /// camera three times in the same frame (Camera.Render into render textures, no UI):
    /// <list type="number">
    /// <item>DISPLAY: post-processing on (ACES, bloom) into an sRGB target = what the player sees;</item>
    /// <item>NO-BLOOM: the same with a priority-1000 volume forcing Bloom intensity 0;</item>
    /// <item>HDR: post-processing off into an ARGBHalf target = the linear scene colour the bloom prefilter sees.</item>
    /// </list>
    /// The fireball disc (centre + radius projected from NukeFx.FireballCentre / FireballRadius) gives the metrics and
    /// PASS rules below (justified in README_IMPORT.md "Gate": tools/nuke_model.py for old vs new).
    /// Usage (an editor script): <c>return OpenZ.NukeGlowCheck.Run();</c> then poll <c>OpenZ.NukeGlowCheck.LastReport</c>;
    /// or the editor menu OpenZ/Checks/Nuke Glow (Play). <c>Run(real: true)</c> kills the enemy fort instead (the real strike,
    /// one pass at the current time of day; ends the match).
    /// </summary>
    public sealed class NukeGlowCheck : MonoBehaviour
    {
        public static bool Running { get; private set; }
        public static string LastReport { get; private set; } = "not run";
        public static bool LastPass { get; private set; }
        public static readonly float[] SampleTimes = { 0.3f, 0.6f, 1.5f, 3f, 5f };

        // ============================================================================================ PASS rules (README "Gate")
        /// <summary>Display luma (Rec.709, 0-255) that counts as white-hot / burned-in.</summary>
        public const float HotLuma = 245f;
        /// <summary>Min share of the fireball CORE (r &lt; .45 R) at luma &gt;= HotLuma: a white-hot heart that shrinks as it cools.
        /// </summary>
        static float MinCoreHot(float t) => t <= 1.5f ? 0.5f : t <= 3f ? 0.2f : 0.08f;
        /// <summary>Min 99.5th percentile display luma in the fireball (old frames: 228-234).</summary>
        const float MinP995 = HotLuma;
        /// <summary>Min share of fireball pixels whose linear max(rgb) is &gt;= 1.5 x the LIVE linear bloom threshold (they send
        /// &gt;= 1/3 of their energy to the bloom): the floor against a future global gain cut.</summary>
        static float MinHdrFrac(float t) => t <= 3f ? 0.40f : 0.25f;
        /// <summary>Min bloom halo: mean display luma (bloom on - bloom off) in the ring 1.0-1.5 R around the fireball.</summary>
        static float MinHalo(float t, bool night) => night ? (t <= 0.6f ? 15f : t <= 3f ? 25f : 12f) : (t <= 0.3f ? 8f : t <= 3f ? 10f : 5f);
        /// <summary>At t &gt;= 3 s the halo must fall off (inner ring 1.0-1.2 R lifted more than 1.3-1.5 R): a glow, not a flat wash.</summary>
        const float FalloffFrom = 3f;
        /// <summary>Fading to orange: at t &gt;= 5 s at least this share of the rim band (.6-.95 R) is saturated orange
        /// (hue 8-48 deg, saturation &gt;= .6).</summary>
        const float MinRimOrange = 0.20f;
        /// <summary>Never a white blob again (fx_v2 "pale ball"): at t &gt;= 3 s at most this share of the fireball is white
        /// (saturation &lt; .15 and luma &gt;= 235).</summary>
        const float MaxWhite = 0.45f;
        /// <summary>
        /// the SAME frozen frame rendered once more with every nuke-born renderer / light hidden, see <see cref="CaptureBaseline"/>).</summary>
        static float MinGroundLift(float t) => t <= 0.6f ? 1.5f : t <= 3f ? 1.2f : 0f;

        public static string Run(bool day = true, bool night = true, bool real = false)
        {
            if (!Application.isPlaying) return "NukeGlowCheck: Play mode only (open L02 or L17 and press Play)";
            if (Running) return "NukeGlowCheck: already running";
            if (!ZGame.I || ZGame.I.World == null) return "NukeGlowCheck: no game in this scene";
            var go = new GameObject("NukeGlowCheck");
            var c = go.AddComponent<NukeGlowCheck>();
            c.StartCoroutine(c.Main(day, night, real));
            return $"NukeGlowCheck started ({(real ? "real strike" : (day ? "day " : "") + (night ? "night" : ""))}); poll OpenZ.NukeGlowCheck.LastReport";
        }

        sealed class Sample
        {
            public string pass; public float t, R; public int px, py, pr;
            public float coreHot, p995, hdrFrac, thr, halo, haloIn, haloOut, rimOrange, white, groundLift, peakHdr;
            public float groundLiftPass; public int baseHidden;
            public Vector3 coreRgb, coreHdr;
            public readonly List<string> fails = new List<string>();
        }

        readonly List<Sample> samples = new List<Sample>();
        readonly StringBuilder notes = new StringBuilder();
        Volume noBloom; VolumeProfile noBloomProf;
        string outDir, level;

        IEnumerator Main(bool day, bool night, bool real)
        {
            Running = true; LastPass = false; LastReport = "running";
            var g = ZGame.I; level = g.level;
            outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/qc"));
            Directory.CreateDirectory(Path.Combine(outDir, "nuke_glow"));
            var rts = FindFirstObjectByType<RtsCamera>(); var cam = Camera.main;
            var dn = FindFirstObjectByType<DayNight>();
            bool dnRun = dn && dn.running; float dnT = dn ? dn.timeOfDay : 0f;
            float shockShake = OpenZFortShockwave.ShakeScale; var shake = OpenZFxPlayer.I ? OpenZFxPlayer.I.Shake : null; float shakeScale = shake != null ? shake.scale : 1f;
            BuildNoBloom();
            var fort = FindFort(g);
            if (fort == null) { Finish("FAIL: no fort in this level"); yield break; }
            var centre = g.CenterWorld(fort);
            OpenZFortShockwave.ShakeScale = 0f; if (shake != null) { shake.scale = 0f; shake.Reset(); }
            if (real)
            {
                yield return Pass("real", -1f, centre, rts, cam, dn, true);
            }
            else
            {
                if (day) yield return Pass("day", 0.08f, centre, rts, cam, dn, false);
                if (night) yield return Pass("night", 0.62f, centre, rts, cam, dn, false);
            }
            // restore
            OpenZFortShockwave.ShakeScale = shockShake; if (shake != null) shake.scale = shakeScale;
            if (dn && !real) { dn.timeOfDay = dnT; dn.Apply(dnT); dn.running = dnRun; }
            Finish(null);
        }

        static ZObject FindFort(ZGame g)
        {
            ZObject any = null;
            foreach (var o in g.World.Objects)
            {
                if (o == null || !o.IsFort || o.Destroyed) continue;
                if (o.Owner != g.player && o.Owner != Team.Neutral) return o;
                any ??= o;
            }
            return any;
        }

        IEnumerator Pass(string name, float timeOfDay, Vector3 centre, RtsCamera rts, Camera cam, DayNight dn, bool real)
        {
            if (dn && timeOfDay >= 0f) { dn.running = false; dn.timeOfDay = timeOfDay; dn.Apply(timeOfDay); }
            if (rts && !real) { rts.enabled = true; rts.focus = new Vector3(centre.x, 0f, centre.z + 9f); rts.SetHeightImmediate(78f); }
            yield return new WaitForSecondsRealtime(2.0f);                                           // night grade, lights, camera settle
            cam = Camera.main;
            if (!cam) { notes.AppendLine($"- {name}: no main camera"); yield break; }
            var baseHdr = Capture(cam, true, false);                                                  // v3b: linear, post off (as the per-sample HDR)
            SnapshotShown(centre);
            NukeFx n = null;
            if (real)
            {
                OpenZVictory.DebugFortFalls(true);
                float wait = 0f;
                while ((n = NukeFx.Current) == null && wait < 15f) { wait += Time.unscaledDeltaTime; yield return null; }
                if (!n) { notes.AppendLine($"- {name}: the strike never impacted within 15 s"); Destroy(baseHdr); yield break; }
            }
            else
            {
                n = NukeFx.Trigger(centre);
                OpenZBloomDirector.Flash(1f);
            }
            foreach (var t in SampleTimes)
            {
                while (n && n.Age < t) yield return null;
                if (!n) { notes.AppendLine($"- {name}: the nuke ended before {t:F1} s"); break; }
                float scale = Time.timeScale; Time.timeScale = 0f;
                yield return null; yield return null;                                                   // shimmer / sustain / lights settle on the frozen frame
                cam = Camera.main;
                var s = new Sample { pass = name, t = t };
                var disp = Capture(cam, false, true);
                noBloom.weight = 1f; var flat = Capture(cam, false, true); noBloom.weight = 0f;
                var hdr = Capture(cam, true, false);
                var sameBase = CaptureBaseline(cam, n, out s.baseHidden);
                s.thr = OpenZBloomDirector.I ? OpenZBloomDirector.I.LinearThreshold : Mathf.GammaToLinearSpace(1.25f);
                Measure(s, n, cam, disp, flat, hdr, sameBase ? sameBase : baseHdr, name == "night" || (name == "real" && NightGrade.NightK > 0.5f), sameBase ? baseHdr : null);
                try
                {
                    File.WriteAllBytes(Path.Combine(outDir, "nuke_glow", $"{level}_{name}_{t:F1}s.png"), disp.EncodeToPNG());
                    SaveHdrCrop(hdr, s, Path.Combine(outDir, "nuke_glow", $"{level}_{name}_{t:F1}s_hdr.exr"));   // v3b: linear scene around the fireball (offline tuning)
                }
                catch (System.Exception e) { notes.AppendLine("- png/exr: " + e.Message); }
                Destroy(disp); Destroy(flat); Destroy(hdr); if (sameBase) Destroy(sameBase);
                samples.Add(s);
                if (Time.timeScale == 0f) Time.timeScale = scale > 0f ? scale : 1f;
                yield return null;
            }
            Destroy(baseHdr);
            if (n && !real) Destroy(n.gameObject);                                                    // the check's own nuke: done
            yield return new WaitForSecondsRealtime(0.5f);
        }

        /// <summary>Renderer / light instance ids that SHOW at the pass start (just before the trigger / the real strike).</summary>
        readonly HashSet<int> shownAtStart = new HashSet<int>();
        Vector3 nukeAt;
        static readonly int AtmoHoleId = Shader.PropertyToID("_AtmoHole");
        /// <summary>Lights born after the pass start are hidden in the baseline only within this horizontal distance (m) of the blast
        /// (at least 3 x the fireball radius): the nuke's own, FxManager's and OpenZFxPlayer's pooled blast lights all sit at the blast;
        /// a far headlight that NightLightBudget grants meanwhile stays in both frames.</summary>
        public static float BaselineLightRadius = 40f;

        static bool Shows(Renderer r)
        {
            if (!r || !r.enabled || !r.gameObject.activeInHierarchy) return false;
            if (r is ParticleSystemRenderer) { var ps = r.GetComponent<ParticleSystem>(); return ps && ps.particleCount > 0; }   // pooled systems sit enabled + empty
            return true;
        }

        static bool Lit(Light l) => l && l.enabled && l.gameObject.activeInHierarchy && l.intensity > 0.001f;

        static bool Under(Transform t, List<Transform> roots)
        {
            for (var p = t; p; p = p.parent) for (int i = 0; i < roots.Count; i++) if (p == roots[i]) return true;
            return false;
        }

        void SnapshotShown(Vector3 at)
        {
            shownAtStart.Clear(); nukeAt = at;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) if (Shows(r)) shownAtStart.Add(r.GetInstanceID());
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) if (Lit(l)) shownAtStart.Add(l.GetInstanceID());
        }

        /// <summary>The ground-light baseline of ONE sample: the same frozen frame (same weather, rain, lightning pulse, lamp budget, units)
        /// rendered HDR once more with the nuke taken out: every renderer / light of NukeFx, the puff mushroom and the shockwave, plus every
        /// renderer that shows now but did not at the pass start (the pooled blast particles, dust, scorch meshes) and every point / spot
        /// light lit now but not then within <see cref="BaselineLightRadius"/> of the blast (the pooled blast lights); NukeFx's fog hole
        /// (_AtmoHole) is closed for it. Everything is put back before the next line runs (no frame passes). Why: the one pass-start
        /// baseline drifted with the L17 night weather and with NightLightBudget taking real lamp lights away while the FX light pools
        /// stay; FxManager's instanced draws have no component and stay in both frames (lit by the nuke only in the sample).</summary>
        Texture2D CaptureBaseline(Camera cam, NukeFx n, out int hidden)
        {
            hidden = 0;
            if (!cam || shownAtStart.Count == 0) return null;
            var nuke = new List<Transform>();
            if (n) nuke.Add(n.transform);
            if (OpenZNukeMushroom.Current) nuke.Add(OpenZNukeMushroom.Current.transform);
            foreach (var w in FindObjectsByType<OpenZFortShockwave>(FindObjectsSortMode.None)) if (w) nuke.Add(w.transform);
            var keep = new List<Transform> { transform };
            if (OzWeather.I) keep.Add(OzWeather.I.transform);
            float lightR = Mathf.Max(BaselineLightRadius, 3f * (n ? n.FireballRadius : 0f));
            var offR = new List<Renderer>(); var offL = new List<Light>();
            var hole = Shader.GetGlobalVector(AtmoHoleId);
            try
            {
                foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (!Shows(r)) continue;
                    if (!Under(r.transform, nuke) && (shownAtStart.Contains(r.GetInstanceID()) || Under(r.transform, keep))) continue;
                    r.enabled = false; offR.Add(r);
                }
                foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (!Lit(l) || l.type == LightType.Directional) continue;
                    if (!Under(l.transform, nuke))
                    {
                        if (shownAtStart.Contains(l.GetInstanceID()) || Under(l.transform, keep)) continue;
                        var d = l.transform.position - nukeAt; d.y = 0f;
                        if (d.magnitude > lightR) continue;
                    }
                    l.enabled = false; offL.Add(l);
                }
                Shader.SetGlobalVector(AtmoHoleId, Vector4.zero);
                hidden = offR.Count + offL.Count;
                return Capture(cam, true, false);
            }
            catch (System.Exception e) { notes.AppendLine("- per-sample baseline: " + e.Message + " (fell back to the pass-start baseline)"); return null; }
            finally
            {
                foreach (var r in offR) if (r) r.enabled = true;
                foreach (var l in offL) if (l) l.enabled = true;
                Shader.SetGlobalVector(AtmoHoleId, hole);
            }
        }

        void BuildNoBloom()
        {
            var go = new GameObject("NukeGlowCheck_NoBloom"); go.transform.SetParent(transform, false);
            noBloomProf = ScriptableObject.CreateInstance<VolumeProfile>(); noBloomProf.name = "NukeGlowCheck NoBloom (runtime)"; noBloomProf.hideFlags = HideFlags.DontSave;
            var b = noBloomProf.Add<Bloom>(true); b.intensity.Override(0f);
            noBloom = go.AddComponent<Volume>(); noBloom.isGlobal = true; noBloom.priority = 1000f; noBloom.weight = 0f; noBloom.sharedProfile = noBloomProf;
        }

        /// <summary>v3b: ALWAYS a float target. URP takes the camera's intermediate colour format from its target texture: the
        /// v3 display capture into ARGB32 rendered the whole frame in LDR (scene clamped to 1.0 before bloom and ACES: every day
        /// frame topped out at luma 213-215 = ACES(1.0), and no day pixel could exceed the 1.36 bloom threshold). Now the display
        /// frame is rendered HDR like the game view and its tone-mapped linear output is sRGB-encoded here.</summary>
        static Texture2D Capture(Camera c, bool hdr, bool post)
        {
            var f = CaptureFloat(c, post);
            if (hdr) return f;
            var px = f.GetPixels(); var o = new Color32[px.Length];
            for (int i = 0; i < px.Length; i++) o[i] = new Color32(Enc(px[i].r), Enc(px[i].g), Enc(px[i].b), 255);
            var tex = new Texture2D(f.width, f.height, TextureFormat.RGBA32, false, false);
            tex.SetPixels32(o); tex.Apply(false);
            Destroy(f);
            return tex;
        }

        static byte[] srgbLut;
        static byte Enc(float v)
        {
            if (srgbLut == null) { srgbLut = new byte[4096]; for (int i = 0; i < 4096; i++) srgbLut[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.LinearToGammaSpace(i / 4095f) * 255f), 0, 255); }
            return srgbLut[Mathf.Clamp((int)(v * 4095f + 0.5f), 0, 4095)];
        }

        static Texture2D CaptureFloat(Camera c, bool post)
        {
            int w = Mathf.Max(64, c.pixelWidth), h = Mathf.Max(64, c.pixelHeight);
            var desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGBHalf, 24) { msaaSamples = 1, sRGB = false };
            var rt = RenderTexture.GetTemporary(desc);
            var data = c.GetUniversalAdditionalCameraData();
            bool oldPost = data && data.renderPostProcessing; var oldT = c.targetTexture;
            try
            {
                if (data) data.renderPostProcessing = post;
                c.targetTexture = rt;
                c.Render();
            }
            finally { c.targetTexture = oldT; if (data) data.renderPostProcessing = oldPost; }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBAHalf, false, true);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(false);
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        /// <summary>v3b: the HDR (linear, post off) crop of 3.2 R around the fireball as a ZIP EXR.</summary>
        static void SaveHdrCrop(Texture2D hdr, Sample s, string path)
        {
            if (s.pr <= 0) return;
            int r = Mathf.CeilToInt(s.pr * 3.2f), x0 = Mathf.Clamp(s.px - r, 0, hdr.width - 1), y0 = Mathf.Clamp(s.py - r, 0, hdr.height - 1);
            int w = Mathf.Clamp(2 * r, 8, hdr.width - x0), h = Mathf.Clamp(2 * r, 8, hdr.height - y0);
            var crop = new Texture2D(w, h, TextureFormat.RGBAHalf, false, true);
            crop.SetPixels(hdr.GetPixels(x0, y0, w, h)); crop.Apply(false);
            File.WriteAllBytes(path, crop.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));
            Destroy(crop);
        }

        static float Luma(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        static float LumaLin(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        void Measure(Sample s, NukeFx n, Camera cam, Texture2D disp, Texture2D flat, Texture2D hdr, Texture2D baseHdr, bool night, Texture2D passBase = null)
        {
            int w = disp.width, h = disp.height;
            var cw = n.FireballCentre; float R = n.FireballRadius; s.R = R;
            var sc = cam.WorldToScreenPoint(cw); var se = cam.WorldToScreenPoint(cw + cam.transform.right * R);
            float kx = w / (float)cam.pixelWidth, ky = h / (float)cam.pixelHeight;
            float cx = sc.x * kx, cy = sc.y * ky, pr = Mathf.Abs(se.x - sc.x) * kx;
            s.px = Mathf.RoundToInt(cx); s.py = Mathf.RoundToInt(cy); s.pr = Mathf.RoundToInt(pr);
            if (sc.z <= 0f || pr < 8f || cx < 0 || cy < 0 || cx >= w || cy >= h) { s.fails.Add($"fireball not framed (screen {cx:F0},{cy:F0} r {pr:F0} px)"); return; }
            var D = disp.GetPixels32(); var F = flat.GetPixels32(); var H = hdr.GetPixels(); var B = baseHdr && baseHdr.width == w && baseHdr.height == h ? baseHdr.GetPixels() : null;
            var P0 = passBase && passBase.width == w && passBase.height == h ? passBase.GetPixels() : null; double gPass = 0;
            float thr15 = 1.5f * s.thr;
            var discL = new List<float>(); int nDisc = 0, nHdr = 0, nWhite = 0, nCore = 0, nCoreHot = 0, nRim = 0, nRimOr = 0;
            double haloOn = 0, haloOff = 0, inOn = 0, inOff = 0, outOn = 0, outOff = 0; int nHalo = 0, nIn = 0, nOut = 0;
            double gNow = 0, gBase = 0; int nG = 0; float peak = 0f; Vector3 cSum = Vector3.zero, hSum = Vector3.zero;
            int x0 = Mathf.Max(0, (int)(cx - pr * 3.1f)), x1 = Mathf.Min(w - 1, (int)(cx + pr * 3.1f));
            int y0 = Mathf.Max(0, (int)(cy - pr * 3.1f)), y1 = Mathf.Min(h - 1, (int)(cy + pr * 3.1f));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x - cx, dy = y - cy, r = Mathf.Sqrt(dx * dx + dy * dy) / pr;
                    int i = y * w + x;
                    if (r < 1f)
                    {
                        var d = D[i]; float L = Luma(d); discL.Add(L); nDisc++;
                        var hc = H[i]; float mx = Mathf.Max(hc.r, Mathf.Max(hc.g, hc.b)); if (mx >= thr15) nHdr++; peak = Mathf.Max(peak, mx);
                        float dmx = Mathf.Max(d.r, Mathf.Max(d.g, d.b)), dmn = Mathf.Min(d.r, Mathf.Min(d.g, d.b)), sat = dmx > 0 ? (dmx - dmn) / dmx : 0f;
                        if (sat < 0.15f && L >= 235f) nWhite++;
                        if (r < 0.45f) { nCore++; if (L >= HotLuma) nCoreHot++; cSum += new Vector3(d.r, d.g, d.b); hSum += new Vector3(hc.r, hc.g, hc.b); }
                        if (r >= 0.6f && r < 0.95f)
                        {
                            nRim++;
                            float hue = Mathf.Atan2(1.7320508f * (d.g - d.b), 2f * d.r - d.g - d.b) * Mathf.Rad2Deg; if (hue < 0f) hue += 360f;
                            if (hue > 8f && hue < 48f && sat >= 0.6f) nRimOr++;
                        }
                    }
                    else if (r < 1.5f)
                    {
                        float on = Luma(D[i]), off = Luma(F[i]);
                        haloOn += on; haloOff += off; nHalo++;
                        if (r < 1.2f) { inOn += on; inOff += off; nIn++; } else if (r >= 1.3f) { outOn += on; outOff += off; nOut++; }
                    }
                    else if (r >= 1.6f && r < 3f && B != null) { gNow += LumaLin(H[i]); gBase += LumaLin(B[i]); nG++; if (P0 != null) gPass += LumaLin(P0[i]); }
                }
            discL.Sort();
            s.p995 = discL.Count > 0 ? discL[Mathf.Clamp((int)(discL.Count * 0.995f), 0, discL.Count - 1)] : 0f;
            s.coreHot = nCore > 0 ? nCoreHot / (float)nCore : 0f;
            s.hdrFrac = nDisc > 0 ? nHdr / (float)nDisc : 0f;
            s.white = nDisc > 0 ? nWhite / (float)nDisc : 0f;
            s.rimOrange = nRim > 0 ? nRimOr / (float)nRim : 0f;
            s.halo = nHalo > 0 ? (float)((haloOn - haloOff) / nHalo) : 0f;
            s.haloIn = nIn > 0 ? (float)((inOn - inOff) / nIn) : 0f; s.haloOut = nOut > 0 ? (float)((outOn - outOff) / nOut) : 0f;
            s.groundLift = nG > 0 && gBase > 1e-6 ? (float)(gNow / gBase) : 0f;
            s.groundLiftPass = nG > 0 && gPass > 1e-6 ? (float)(gNow / gPass) : 0f;
            s.peakHdr = peak;
            if (nCore > 0) { s.coreRgb = cSum / nCore; s.coreHdr = hSum / nCore; }
            // rules
            float t = s.t;
            if (s.coreHot < MinCoreHot(t)) s.fails.Add($"white-hot core {s.coreHot * 100f:F0} % < {MinCoreHot(t) * 100f:F0} %");
            if (s.p995 < MinP995) s.fails.Add($"peak luma p99.5 {s.p995:F0} < {MinP995:F0}");
            if (s.hdrFrac < MinHdrFrac(t)) s.fails.Add($"bloom-eligible {s.hdrFrac * 100f:F0} % < {MinHdrFrac(t) * 100f:F0} % (>= 1.5 x linear threshold {s.thr:F2})");
            if (s.halo < MinHalo(t, night)) s.fails.Add($"bloom halo {s.halo:F1} < {MinHalo(t, night):F0} luma");
            if (t >= FalloffFrom && s.haloIn <= s.haloOut) s.fails.Add($"halo does not fall off ({s.haloIn:F1} inner vs {s.haloOut:F1} outer)");
            if (t >= 5f && s.rimOrange < MinRimOrange) s.fails.Add($"rim orange {s.rimOrange * 100f:F0} % < {MinRimOrange * 100f:F0} %");
            if (t >= 3f && s.white > MaxWhite) s.fails.Add($"white blob {s.white * 100f:F0} % > {MaxWhite * 100f:F0} %");
            if (night && B != null && MinGroundLift(t) > 0f && s.groundLift < MinGroundLift(t)) s.fails.Add($"ground light x{s.groundLift:F2} < x{MinGroundLift(t):F2}");
        }

        void Finish(string fatal)
        {
            if (noBloom) Destroy(noBloom.gameObject);
            if (noBloomProf) { foreach (var c in noBloomProf.components) if (c) Destroy(c); Destroy(noBloomProf); }
            bool pass = fatal == null && samples.Count > 0; foreach (var s in samples) if (s.fails.Count > 0) pass = false;
            var inv = CultureInfo.InvariantCulture;
            var md = new StringBuilder();
            md.AppendLine($"# Nuke glow gate: {level} ({System.DateTime.Now:yyyy-MM-dd HH:mm})");
            md.AppendLine();
            md.AppendLine($"**{(pass ? "PASS" : "FAIL")}**{(fatal != null ? " - " + fatal : "")}. NukeGlowCheck (nuke_fx_v3): game camera rendered 3x per sample (display with bloom, display without bloom, HDR linear), fireball disc from NukeFx.FireballCentre / FireballRadius. Rules: README_IMPORT.md \"Gate\".");
            md.AppendLine();
            md.AppendLine("| pass | t (s) | R (m / px) | core >= 245 | p99.5 luma | bloom-eligible (thr lin) | peak HDR | halo (in / out) | rim orange | white | ground x | result |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var s in samples)
                md.AppendLine(string.Format(inv, "| {0} | {1:F1} | {2:F1} / {3} | {4:F0} % | {5:F0} | {6:F0} % ({7:F2}) | {8:F1} | {9:F1} ({10:F1} / {11:F1}) | {12:F0} % | {13:F0} % | {14:F2} | {15} |",
                    s.pass, s.t, s.R, s.pr, s.coreHot * 100f, s.p995, s.hdrFrac * 100f, s.thr, s.peakHdr, s.halo, s.haloIn, s.haloOut, s.rimOrange * 100f, s.white * 100f, s.groundLift,
                    s.fails.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", s.fails)));
            md.AppendLine();
            md.AppendLine($"Contract: NukeFx GainStart {NukeFx.GainStart:F1} / GainHold {NukeFx.GainHold:F1} (min {NukeFx.MinHoldGain}) / GainEnd {NukeFx.GainEnd:F1}, HeatTau {NukeFx.HeatTau:F2}, HeatEnd {NukeFx.HeatEnd:F2}, BloomSustain {NukeFx.BloomSustain:F1}, FireLightPeak {NukeFx.FireLightPeak:F0} x (1 + {NukeFx.FireLightNightBoost:F2} x night), FlashNightSlow {NukeFx.FlashNightSlow:F2}.");
            md.AppendLine($"Frames: work/records/qc/nuke_glow/{level}_<pass>_<t>s.png (display, bloom on) + _hdr.exr (linear scene crop, 3.2 R). Captures render into a float target (HDR intermediate, as the game view; nuke_fx_v3b).");
            md.AppendLine();
            md.AppendLine("| pass | t (s) | core mean display RGB | core mean HDR RGB |");
            md.AppendLine("|---|---|---|---|");
            foreach (var s in samples) md.AppendLine(string.Format(inv, "| {0} | {1:F1} | {2:F0}, {3:F0}, {4:F0} | {5:F2}, {6:F2}, {7:F2} |", s.pass, s.t, s.coreRgb.x, s.coreRgb.y, s.coreRgb.z, s.coreHdr.x, s.coreHdr.y, s.coreHdr.z));
            md.AppendLine();
            md.AppendLine("Ground light baseline: **ground x** = the same frozen frame with the nuke hidden; the pass-start column is the old single baseline, for comparison only (it drifts with the weather and the lamp budget).");
            md.AppendLine();
            md.AppendLine("| pass | t (s) | ground x (same frame) | ground x (pass start, old) | nuke renderers + lights hidden |");
            md.AppendLine("|---|---|---|---|---|");
            foreach (var s in samples) md.AppendLine(string.Format(inv, "| {0} | {1:F1} | {2:F2} | {3:F2} | {4} |", s.pass, s.t, s.groundLift, s.groundLiftPass, s.baseHidden));
            if (notes.Length > 0) { md.AppendLine(); md.AppendLine("Notes:"); md.Append(notes); }
            string path = Path.Combine(outDir ?? ".", $"nuke_glow_{level}.md");
            try { File.WriteAllText(path, md.ToString()); } catch (System.Exception e) { Debug.LogWarning("[OpenZ] nuke glow report: " + e.Message); }
            LastPass = pass;
            LastReport = $"Nuke glow gate {level}: {(pass ? "PASS" : "FAIL")} ({samples.Count} samples) -> {path}" + (fatal != null ? " " + fatal : "");
            foreach (var s in samples) if (s.fails.Count > 0) LastReport += $"\n  {s.pass} {s.t:F1}s: {string.Join("; ", s.fails)}";
            (pass ? (System.Action<object>)Debug.Log : Debug.LogError)("[OpenZ] " + LastReport);
            Running = false;
            Destroy(gameObject);
        }

        void OnDestroy() { if (Running) { Running = false; if (Time.timeScale == 0f) Time.timeScale = 1f; } }
    }
}
