using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;
using static OpenZ.Intro.IntroMath;

namespace OpenZ.Intro
{
    /// <summary>
    /// rendering into a RenderTexture of its polygon's bbox, masked by the angled panel shape (never full screen); the newest
    /// panel is live, earlier panels freeze. Cut-outs = the robot alone on the IntroCutout layer over a flat comic background
    /// (sunburst / halftone / speed lines, OzComicPanel shader); stickers, starbursts and the three bubbles pop on their beats;
    /// typewriter cards; the end title (OPENZ slams in, three bullet holes, falls back). The sound is the reviewed master mix
    /// (audio\preview\intro_cutscene_preview.wav, 74.25 s cut + 1.5 s menu tail; mix-checked word for word) and it is the clock.
    /// Skip: SPACE / ESC / click after the prompt (3.0 s); Options INTRO CUTSCENE, -nointro, once per launch (OzIntro).
    /// </summary>
    public sealed class OzIntroPlayer : MonoBehaviour
    {
        [Header("Data")] public TextAsset timelineJson; public TextAsset setJson; public AudioClip mix;
        [Header("Actors")] public GameObject alan; public GameObject bradGo; public GameObject shuttle;
        public AnimationClip[] alanClips; public AnimationClip[] bradClips;
        [Header("Set")] public Transform setRoot; public GameObject planet; public GameObject ring; public Light sunBig, sunSmall;
        public Mesh[] canMeshes; public Material canMat; public Material starMat; public Material glowMat; public Material cloudMat; public Material flameMat;
        [Tooltip("Data/intro_look.txt: read at run time for the per-panel look, flame sizes and the lift-off sand burst (no recompile)")] public TextAsset lookTxt;
        [Header("UI")] public TMP_FontAsset font; public Texture2D logoTex; public Material panelMat;
        [Header("Switches")] public bool buildEnabled = true; public bool endCaptionNoOpenSource; public bool endCaptionAnniversary = true;
        [Tooltip("Editor 'Play live': ignore the once-per-launch / Options gates")] public bool forcePlay;

        public const int CutLayer = 30, ScreenLayer = 31, UiLayer = 5;
        /// <summary>Editor mode handshake (OzIntroBuilder menus): "live" = the real-time intro, "record" = the offline pre-render
        /// (frame-exact 60 fps), "" = as shipped (the pre-rendered video when it exists, else live). Read + cleared in Awake.</summary>
        public const string ModePref = "OpenZ.Intro.Mode";
        /// <summary>Editor: "stay" = after a pre-render, import it and keep Play running (else Play stops and OzIntroBuilder imports).</summary>
        public const string AfterRecordPref = "OpenZ.Intro.AfterRecord";
        /// <summary></summary>
        public const string VideoResource = "OpenZ/Intro/openz_intro";
        /// <summary>Cut-out matte: false = the RT's own alpha (needs _AtmoKeepAlpha), true = difference matte of a black and a white
        /// render (pipeline-proof fallback, two cameras per cut-out).</summary>
        public static bool CutoutDiffMatte = false;
        /// <summary>
        /// measured the VideoPlayer -> AudioSource route 86 ms later than an ordinary in-game AudioSource sound (flash + beep reference).</summary>
        public const float AudioLeadS = 0.085f;
        bool recording, videoMode, videoEnded, videoError, recDone; VideoClip videoClip; VideoPlayer vp; RenderTexture videoRt;
        int recFrame; float lastT, frameDt, atmoEnabled0; RectTransform canvasRoot;
        const float TurnDur = 0.45f;

        IntroTimeline tl; IntroSet set;
        AudioSource src; float clockStart; bool started, leaving, skipped; float leaveAt;
        Camera uiCam; Canvas canvas; RectTransform pagesRoot, cardC0, cardC9, fade, skipRt; CanvasGroup skipCg;
        float pageScale = 1f;
        readonly List<PageUi> pages = new List<PageUi>();
        readonly List<PanelUi> all = new List<PanelUi>();
        PanelUi live;
        Color ink = new Color32(14, 14, 16, 255), paper = new Color32(255, 253, 247, 255), desk = new Color32(24, 30, 44, 255);
        Dictionary<string, AnimationClip> clipsA = new Dictionary<string, AnimationClip>(), clipsB = new Dictionary<string, AnimationClip>();
        Material tmpStickerMat;

        sealed class PageUi { public IntroPage d; public RectTransform rt, slots, overlay; public List<PanelUi> panels = new List<PanelUi>(); public float show; }
        sealed class PanelUi
        {
            public IntroPanel d; public PageUi page; public int index; public RectTransform rt; public OzComicPoly fill, inkPoly; public Material mat;
            public Camera cam, camW; public RenderTexture rtTex, rtTexW; public Vector2 centroid; public float show; public bool isLive, shown;
            public List<StickerUi> stickers = new List<StickerUi>(); public Vector2 slideFrom;
        }
        sealed class StickerUi { public IntroSticker d; public RectTransform rt; }

        // ---------------------------------------------------------------- boot
        void Awake()
        {
            bool play = forcePlay || Application.isEditor || (buildEnabled && OzIntro.Enabled && !OzIntro.CommandLineOff && !OzIntro.PlayedThisLaunch);
            if (!play)
            {
                Debug.Log($"[OpenZ Intro] skipped (oz_intro {(OzIntro.Enabled ? 1 : 0)}, build {buildEnabled}, -nointro {OzIntro.CommandLineOff}, played {OzIntro.PlayedThisLaunch}): main menu");
                OzIntro.PlayedThisLaunch = true;
                SceneManager.LoadScene(OzIntro.MenuScene);
                enabled = false; return;
            }
            OzIntro.PlayedThisLaunch = true; OzIntro.Running = true;
            string mode = "";
#if UNITY_EDITOR
            mode = UnityEditor.EditorPrefs.GetString(ModePref, ""); UnityEditor.EditorPrefs.DeleteKey(ModePref);
#endif
            recording = mode == "record";
            if (!forcePlay && mode != "live" && !recording) videoClip = Resources.Load<VideoClip>(VideoResource);
            videoMode = videoClip != null;
            Debug.Log($"[OpenZ Intro] mode {(recording ? "RECORD (offline pre-render, 60 fps frame-exact)" : videoMode ? "VIDEO (pre-rendered " + videoClip.width + "x" + videoClip.height + ", " + videoClip.length.ToString("0.00") + " s)" : "LIVE (real time)")}");
            tl = JsonUtility.FromJson<IntroTimeline>(timelineJson.text);
            set = JsonUtility.FromJson<IntroSet>(setJson.text);
            foreach (var c in alanClips) if (c) clipsA[c.name] = c;
            foreach (var c in bradClips) if (c) clipsB[c.name] = c;
            ink = tl.Pal("ink", ink);
            if (alan) baseRot[alan] = Quaternion.Inverse(Quaternion.Euler(0, set.alanYaw, 0)) * alan.transform.rotation;
            if (bradGo) baseRot[bradGo] = Quaternion.Inverse(Quaternion.Euler(0, set.bradYaw, 0)) * bradGo.transform.rotation;
            if (shuttle) shBase = Quaternion.Inverse(Quaternion.Euler(0, set.shuttleYaw, 0)) * shuttle.transform.rotation;
        }
        readonly Dictionary<GameObject, Quaternion> baseRot = new Dictionary<GameObject, Quaternion>(); Quaternion shBase = Quaternion.identity;

        void Start()
        {
            if (!enabled) return;
            Cursor.visible = true;
            // the game's atmosphere pass (renderer feature) is off in the intro and passes the cut-out cameras' alpha through
            atmoEnabled0 = Shader.GetGlobalFloat("_AtmoEnabled"); Shader.SetGlobalFloat("_AtmoEnabled", 0f); Shader.SetGlobalFloat("_AtmoKeepAlpha", 1f);
            if (videoMode) { StartVideo(); return; }
            fog0 = RenderSettings.fog; ambient0 = RenderSettings.ambientIntensity;                         // the look (Intro scene / intro_look.json)
            if (sunBig) { sunRot0 = sunBig.transform.rotation; } if (sunSmall) sunSmallRot0 = sunSmall.transform.rotation;
            BuildUi();
            BuildScreen();
            LoadLook(); InitLookTargets();                                                                   // before the piles: they take the run-time can material
            BuildPiles();
            BuildMoundInstances();
            BuildFx();
            CacheShuttle();
            BuildShuttleColliders();
            BuildDust();
            BuildHeroCans();
            BuildEndTitle();
            var mixGo = new GameObject("IntroMix"); mixGo.transform.SetParent(transform, false);
            src = mixGo.AddComponent<AudioSource>();
            src.clip = mix; src.playOnAwake = false; src.spatialBlend = 0f; src.volume = 1f; src.priority = 0;
            src.ignoreListenerPause = true;
            if (!FindFirstObjectByType<AudioListener>()) uiCam.gameObject.AddComponent<AudioListener>();
            warm = 3;
            BuildSpaceCans();
#if UNITY_EDITOR
            if (recording) StartRecording();
#endif
            Debug.Log($"[OpenZ Intro] start: {all.Count} panels on {pages.Count} pages, mix {mix.length:0.00} s, page scale {pageScale:0.000}, screen {Screen.width}x{Screen.height}, cut-out matte {(CutoutDiffMatte ? "difference" : "alpha")}");
        }

        int warm = -1;
        float Clock()
        {
            if (!started) return 0f;
            if (recording) return recFrame / 60f;                                                        // frame-exact pre-render clock
            if (videoMode) return vp ? (float)vp.time : 0f;                                             // the VideoPlayer keeps its own sound with it
            if (src && src.isPlaying && src.clip == mix)
            {
                // the mix is the clock, smoothed: timeSamples only moves once per DSP buffer (1024 samples = 21 ms), so raw it stepped
                // mix (time constant ~20 frames, snap on a > 150 ms jump): smooth per frame, locked to the sound.
                if (lastClockFrame != Time.frameCount)
                {
                    lastClockFrame = Time.frameCount;
                    float a = src.timeSamples / (float)mix.frequency, target = Time.unscaledTime - a;
                    if (!clockLocked || Mathf.Abs(target - clockStart) > 0.15f) { clockStart = target; clockLocked = true; }
                    else clockStart += (target - clockStart) * 0.05f;
                }
                return Mathf.Max(0f, Time.unscaledTime - clockStart);
            }
            return Time.unscaledTime - clockStart;
        }
        bool clockLocked; int lastClockFrame = -1;

        // ---------------------------------------------------------------- UI
        void BuildUi()
        {
            BuildCanvas();
            var root = canvasRoot;
            BuildPages(root);
        }

        void BuildCanvas()
        {
            var camGo = new GameObject("IntroUiCamera"); camGo.transform.SetParent(transform, false);
            uiCam = camGo.AddComponent<Camera>();
            uiCam.clearFlags = CameraClearFlags.SolidColor; uiCam.backgroundColor = Color.black;
            uiCam.cullingMask = 1 << UiLayer; uiCam.fieldOfView = 30f; uiCam.nearClipPlane = 1f; uiCam.farClipPlane = 4000f; uiCam.depth = 10;
            camGo.transform.position = new Vector3(0, 0, -2000);
            var ud = camGo.AddComponent<UniversalAdditionalCameraData>(); ud.renderPostProcessing = false; ud.antialiasing = AntialiasingMode.None;
            var cgo = new GameObject("IntroCanvas"); cgo.layer = UiLayer; cgo.transform.SetParent(transform, false);
            canvas = cgo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCam; canvas.planeDistance = 1000f;
            var cs = cgo.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = new Vector2(1920, 1080);
            cs.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            float aspect = Screen.width / (float)Screen.height; cs.matchWidthOrHeight = aspect >= 16f / 9f ? 1f : 0f;
            pageScale = aspect >= 16f / 9f ? Screen.height / 1080f : Screen.width / 1920f;
            var root = (RectTransform)cgo.transform; canvasRoot = root;

            Img(root, "Desk", desk, true);
        }

        void BuildPages(RectTransform root)
        {
            pagesRoot = Rt(root, "Pages", new Vector2(1920, 1080));
            tmpStickerMat = new Material(font.material) { name = "IntroStickerMat" };
            tmpStickerMat.EnableKeyword(ShaderUtilities.Keyword_Outline); tmpStickerMat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            tmpStickerMat.SetColor(ShaderUtilities.ID_OutlineColor, ink); tmpStickerMat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.24f);
            tmpStickerMat.SetFloat(ShaderUtilities.ID_FaceDilate, 0.12f);
            tmpStickerMat.SetColor(ShaderUtilities.ID_UnderlayColor, ink); tmpStickerMat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.62f);
            tmpStickerMat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.62f); tmpStickerMat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.32f);

            int idx = 0;
            foreach (var pg in tl.pages)
            {
                if (pg.kind != "comic_page") continue;
                var pu = new PageUi { d = pg };
                pu.rt = Rt(pagesRoot, pg.id, new Vector2(1920, 1080));
                // item 42: a soft gradient shadow glued to the paper's left edge (no gap, no hard band)
                var shGo = new GameObject("TurnShadow", typeof(RectTransform)); shGo.layer = UiLayer; shGo.transform.SetParent(pu.rt, false);
                var shadow = shGo.AddComponent<RawImage>(); shadow.texture = EdgeShadowTex(); shadow.raycastTarget = false;
                shadow.rectTransform.sizeDelta = new Vector2(72, 1064); shadow.rectTransform.anchoredPosition = new Vector2(-938 - 36, 0);
                var pap = Img(pu.rt, "Paper", paper, false); pap.rectTransform.sizeDelta = new Vector2(1898 - 22, 1064 - 16);
                pap.rectTransform.anchoredPosition = Page((22 + 1898) * 0.5f, (16 + 1064) * 0.5f);
                pu.slots = Rt(pu.rt, "Slots", Vector2.zero);                                                  // every panel's empty frame from the page turn (the animatic)
                pu.overlay = Rt(pu.rt, "Stickers", Vector2.zero);                                             // stickers over the gutters + later panels
                foreach (var pd in pg.panels) { var p = BuildPanel(pu, pd, idx++); pu.panels.Add(p); all.Add(p); }
                pu.overlay.SetAsLastSibling();
                pu.show = pu.panels.Count > 0 ? pu.panels[0].show : pg.t0;
                pu.rt.gameObject.SetActive(false);
                pages.Add(pu);
            }
            for (int i = 1; i < all.Count; i++)
                if (all[i].d.trans == "slide") { var dir = (all[i].centroid - all[i - 1].centroid).normalized; all[i].slideFrom = -dir * 260f; }
            BuildCards(root);
            cardC0.SetSiblingIndex(pagesRoot.GetSiblingIndex());                                            // the first page turns in OVER the cold open
            fade = Img(root, "Fade", Color.black, true).rectTransform; fade.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            BuildSkip(root);
        }

        static Texture2D edgeShadow;
        static Texture2D EdgeShadowTex()
        {
            if (edgeShadow) return edgeShadow;
            edgeShadow = new Texture2D(64, 1, TextureFormat.RGBA32, false) { name = "IntroEdgeShadow", wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 64; x++) { float u = x / 63f; edgeShadow.SetPixel(x, 0, new Color(0, 0, 0, 0.5f * u * u)); }
            edgeShadow.Apply(); return edgeShadow;
        }

        PanelUi BuildPanel(PageUi pu, IntroPanel d, int index)
        {
            var p = new PanelUi { d = d, page = pu, index = index };
            var pts = new List<Vector2>();
            for (int i = 0; i + 1 < d.poly.Length; i += 2) pts.Add(Page(d.poly[i], d.poly[i + 1]));
            p.centroid = OzComicPoly.Centroid(pts);
            for (int i = 0; i < pts.Count; i++) pts[i] -= p.centroid;
            var r = d.rect; var uv = new Rect(r[0] - 960f - p.centroid.x, 540f - r[1] - r[3] - p.centroid.y, r[2], r[3]);
            var slot = Rt(pu.slots, d.id + "_Slot", Vector2.zero); slot.anchoredPosition = p.centroid;
            Poly(slot, "Ink", ink).Set(pts, uv, 6f);
            Poly(slot, "Empty", new Color32(236, 234, 226, 255)).Set(pts, uv);
            p.rt = Rt(pu.rt, d.id, Vector2.zero); p.rt.anchoredPosition = p.centroid;
            p.inkPoly = Poly(p.rt, "Ink", ink); p.inkPoly.Set(pts, uv, 6f);
            p.fill = Poly(p.rt, "Fill", Color.white); p.fill.Set(pts, uv);
            p.mat = new Material(panelMat) { name = "Panel_" + d.id };
            p.mat.SetVector("_Size", new Vector4(r[2], r[3], 0, 0));
            p.mat.SetFloat("_Seed", index * 3.7f);
            p.mat.SetColor("_Ink", ink);
            if (d.render == "3d") p.mat.SetFloat("_Style", 0f);
            else
            {
                var b = d.bg; string st = b != null ? b.style : "";
                p.mat.SetFloat("_Style", st == "sunburst" ? 1 : st == "halftone" ? 2 : st == "speedlines" ? 3 : 4);
                p.mat.SetColor("_Col1", C255(b?.c1)); p.mat.SetColor("_Col2", C255(b?.c2));
                var cen = st == "speedlines" ? V2(b.vanish) : V2(b?.centre); p.mat.SetVector("_Centre", new Vector4(cen.x, cen.y, 0, 0));
                p.mat.SetFloat("_Rays", b != null && b.rays > 0 ? b.rays : 20); p.mat.SetFloat("_Dot", b != null && b.dot > 0 ? b.dot : 14);
                string cn = b?.corner ?? ""; p.mat.SetVector("_Corner", new Vector4(cn.Contains("left") ? 0 : 1, cn.Contains("top") ? 0 : 1, 0, 0));
                p.mat.SetFloat("_Cut", d.render == "cutout" ? 1 : 0);
            }
            p.fill.material = p.mat;
            if (d.hasCam && (d.render == "3d" || d.render == "cutout"))
            {
                int w = Mathf.Max(64, Mathf.RoundToInt(r[2] * pageScale)), h = Mathf.Max(64, Mathf.RoundToInt(r[3] * pageScale));
                p.rtTex = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "RT_" + d.id, antiAliasing = 4 };
                p.rtTex.Create();
                var cg = new GameObject("Cam_" + d.id); cg.transform.SetParent(transform, false);
                p.cam = cg.AddComponent<Camera>(); p.cam.enabled = false; p.cam.targetTexture = p.rtTex; p.cam.aspect = r[2] / r[3];
                p.cam.nearClipPlane = 0.05f; p.cam.farClipPlane = d.space ? 9000f : 3000f; p.cam.allowMSAA = true;
                var ad = cg.AddComponent<UniversalAdditionalCameraData>();
                if (d.render == "cutout")
                {
                    p.cam.cullingMask = 1 << CutLayer; p.cam.clearFlags = CameraClearFlags.SolidColor; p.cam.backgroundColor = new Color(0, 0, 0, 0);
                    p.cam.allowHDR = false;
                    ad.renderPostProcessing = false; ad.antialiasing = AntialiasingMode.None;
                    // difference-matte twin: same view on a WHITE clear (alpha = 1 - (white - black)); off unless CutoutDiffMatte
                    p.rtTexW = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "RT_" + d.id + "_W", antiAliasing = 4 }; p.rtTexW.Create();
                    var wg = new GameObject("CamW_" + d.id); wg.transform.SetParent(cg.transform, false);
                    p.camW = wg.AddComponent<Camera>(); p.camW.CopyFrom(p.cam); p.camW.enabled = false; p.camW.targetTexture = p.rtTexW;
                    p.camW.backgroundColor = new Color(1, 1, 1, 0);
                    var adw = wg.AddComponent<UniversalAdditionalCameraData>(); adw.renderPostProcessing = false; adw.antialiasing = AntialiasingMode.None;
                    p.mat.SetTexture("_WhiteTex", p.rtTexW);
                }
                else
                {
                    p.cam.cullingMask = ~((1 << CutLayer) | (1 << ScreenLayer) | (1 << UiLayer) | (1 << EndLayer));
                    if (d.space) { p.cam.clearFlags = CameraClearFlags.SolidColor; p.cam.backgroundColor = new Color(0.004f, 0.005f, 0.012f, 1); }
                    else p.cam.clearFlags = CameraClearFlags.Skybox;
                    ad.renderPostProcessing = true; ad.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; ad.antialiasingQuality = AntialiasingQuality.High;
                }
                p.fill.tex = p.rtTex;
            }
            p.show = d.t0;                                                                                   // item 6: the turn starts AT t_start (the animatic), never 0.45 s early
            foreach (var s in d.stickers ?? new IntroSticker[0]) p.stickers.Add(BuildSticker(p, s, uv));
            p.rt.gameObject.SetActive(false);
            return p;
        }

        Color FillCol(string n)
        {
            switch (n)
            {
                case "red": return tl.Pal("red", new Color32(226, 52, 40, 255));
                case "yellow": return tl.Pal("yellow", new Color32(250, 202, 46, 255));
                case "white": return Color.white;
                case "ink": return ink;
                default: return tl.Pal(n, new Color32(250, 202, 46, 255));
            }
        }

        StickerUi BuildSticker(PanelUi p, IntroSticker s, Rect uv)
        {
            var su = new StickerUi { d = s };
            var pos = new Vector2(uv.x + s.uv[0] * uv.width, uv.y + (1f - s.uv[1]) * uv.height);
            // keep every sticker / bubble inside the paper with a margin (P05B's NOPE. bubble ran over the page edge onto the desk)
            float hw, hh;
            if (s.kind == "burst") hw = hh = s.size * 0.62f + 8f;
            else if (s.kind == "bubble") { hw = s.size * (0.42f * s.text.Length + 0.9f) + 8f; hh = s.size * 0.78f + 8f; }
            else { hw = s.size * 0.34f * s.text.Length + 8f; hh = s.size * 0.62f + 8f; }
            var abs = p.centroid + pos;
            float minX = -938f + 14f + hw, maxX = 938f - 14f - hw, minY = -524f + 14f + hh, maxY = 524f - 14f - hh;
            if (minX < maxX) abs.x = Mathf.Clamp(abs.x, minX, maxX);
            if (minY < maxY) abs.y = Mathf.Clamp(abs.y, minY, maxY);
            if ((abs - (p.centroid + pos)).sqrMagnitude > 1f) Debug.Log($"[OpenZ Intro] sticker {p.d.id} '{s.text}' kept inside the paper: moved {(abs - (p.centroid + pos)).x:0} / {(abs - (p.centroid + pos)).y:0} px");
            pos = abs - p.centroid;
            su.rt = Rt(p.page.overlay, "Sticker_" + p.d.id + "_" + s.text, Vector2.zero); su.rt.anchoredPosition = p.centroid + pos; su.rt.localEulerAngles = new Vector3(0, 0, s.rot);
            if (s.kind == "burst")
            {
                float R = s.size * 0.62f;
                var star = OzComicPoly.Star(Vector2.zero, R, R * 0.62f, 13, s.rot, s.text.GetHashCode());
                var sh = Poly(su.rt, "Shadow", ink); sh.Set(star, new Rect(0, 0, 1, 1), 5f, true); sh.rectTransform.anchoredPosition = new Vector2(7, -7);
                var o = Poly(su.rt, "Ink", ink); o.Set(star, new Rect(0, 0, 1, 1), 5f, true);
                var f = Poly(su.rt, "Fill", FillCol(s.fill)); f.Set(star, new Rect(0, 0, 1, 1));
                var t = Tmp(su.rt, s.text, s.size * 0.62f, tl.Pal("red", Color.red), false); t.rectTransform.localEulerAngles = new Vector3(0, 0, -s.rot * 0.5f);
            }
            else if (s.kind == "bubble")
            {
                su.rt.localEulerAngles = Vector3.zero;
                float rx = s.size * (0.42f * s.text.Length + 0.9f), ry = s.size * 0.78f;
                Vector2 tail = s.tail != null && s.tail.Length > 1 && s.tail[0] >= 0 ? new Vector2(uv.x + s.tail[0] * uv.width, uv.y + (1f - s.tail[1]) * uv.height) - pos : new Vector2(-rx, -ry * 2);
                var b = OzComicPoly.Bubble(Vector2.zero, rx, ry, tail);
                var sh = Poly(su.rt, "Shadow", ink); sh.Set(b, new Rect(0, 0, 1, 1), 4f, true); sh.rectTransform.anchoredPosition = new Vector2(6, -6);
                var o = Poly(su.rt, "Ink", ink); o.Set(b, new Rect(0, 0, 1, 1), 4f, true);
                var f = Poly(su.rt, "Fill", Color.white); f.Set(b, new Rect(0, 0, 1, 1));
                Tmp(su.rt, s.text, s.size, ink, false);
            }
            else
            {
                var t = Tmp(su.rt, s.text, s.size, Color.white, true);
                var c = FillCol(s.fill);
                t.enableVertexGradient = true;
                t.colorGradient = new VertexGradient(Color.Lerp(c, Color.white, 0.38f), Color.Lerp(c, Color.white, 0.38f), c * 0.86f, c * 0.86f);
            }
            su.rt.gameObject.SetActive(false);
            return su;
        }

        TextMeshProUGUI Tmp(RectTransform parent, string text, float size, Color col, bool sticker)
        {
            var go = new GameObject("Text", typeof(RectTransform)); go.layer = UiLayer; go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font; t.fontSize = size; t.text = text; t.color = col; t.alignment = TextAlignmentOptions.Center; t.textWrappingMode = TextWrappingModes.NoWrap;
            t.fontStyle = FontStyles.Bold; t.characterSpacing = -2f; t.raycastTarget = false;
            if (sticker) t.fontSharedMaterial = tmpStickerMat;
            t.rectTransform.sizeDelta = new Vector2(size * text.Length, size * 1.4f);
            return t;
        }

        // ---------------------------------------------------------------- cards (typewriter) + skip
        Text c0l1, c0l2, c0l3, c9l1, c9l2; RectTransform logoRt, logoHinge, floorLine; readonly List<RectTransform> holes = new List<RectTransform>();
        readonly List<Spark> sparks = new List<Spark>(); RectTransform sparkRoot; Font typeFont;
        sealed class Spark { public RectTransform rt; public Vector2 v; public float life, age; public Image img; public bool dust; public CanvasGroup cg; }

        void BuildCards(RectTransform root)
        {
            typeFont = Font.CreateDynamicFontFromOSFont(new[] { "Courier New", "Consolas", "Lucida Console" }, 64);
            cardC0 = Img(root, "CardC0", Color.black, true).rectTransform;
            c0l1 = TypeText(cardC0, "L1", new Vector2(-840, 120), TextAnchor.MiddleLeft);
            c0l2 = TypeText(cardC0, "L2", new Vector2(-840, 40), TextAnchor.MiddleLeft);
            c0l3 = TypeText(cardC0, "L3", new Vector2(0, 0), TextAnchor.MiddleCenter); c0l3.fontSize = 68;           // item 20: the Star Wars beat, 1.5x
            cardC9 = Img(root, "CardC9", Color.black, true).rectTransform;
            c9l1 = TypeText(cardC9, "Caption", Page(960, 250), TextAnchor.MiddleCenter);
            c9l2 = TypeText(cardC9, "Caption2", Page(960, 312), TextAnchor.MiddleCenter);
            var e = tl.ending;
            floorLine = Img(cardC9, "Floor", new Color(0.45f, 0.40f, 0.32f, 0.35f), false).rectTransform;
            floorLine.sizeDelta = new Vector2(1500, 3); floorLine.anchoredPosition = Page(960, 789);
            logoHinge = Rt(cardC9, "LogoHinge", new Vector2(e.logoSize[0], e.logoSize[1]));
            logoHinge.pivot = new Vector2(0.5f, 0f); logoHinge.anchoredPosition = Page(e.logoCentre[0], e.logoCentre[1] + e.logoSize[1] * 0.5f);
            var lg = new GameObject("Logo", typeof(RectTransform)); lg.layer = UiLayer; lg.transform.SetParent(logoHinge, false);
            var raw = lg.AddComponent<RawImage>(); raw.texture = logoTex; raw.raycastTarget = false;
            logoRt = raw.rectTransform; logoRt.anchorMin = Vector2.zero; logoRt.anchorMax = Vector2.one; logoRt.offsetMin = logoRt.offsetMax = Vector2.zero;
            foreach (var h in e.holes) holes.Add(BuildHole(logoRt, h, e.logoSize));
            sparkRoot = Rt(cardC9, "Sparks", Vector2.zero);
            cardC9.gameObject.SetActive(false);
        }

        RectTransform BuildHole(RectTransform logo, IntroHole h, float[] size)
        {
            var rt = Rt(logo, "Hole", Vector2.zero);
            rt.anchoredPosition = new Vector2((h.uv[0] - 0.5f) * size[0], (0.5f - h.uv[1]) * size[1]);
            var soot = Poly(rt, "Soot", new Color(0.08f, 0.06f, 0.05f, 0.55f)); soot.Set(OzComicPoly.Ellipse(Vector2.zero, h.r * 1.9f, h.r * 1.9f, 28), new Rect(0, 0, 1, 1));
            var petals = OzComicPoly.Star(Vector2.zero, h.r * 1.32f, h.r * 0.95f, 8, 12f, (int)(h.at * 1000));
            var rim = Poly(rt, "Rim", new Color(0.93f, 0.88f, 0.74f, 1)); rim.Set(petals, new Rect(0, 0, 1, 1));
            var core = Poly(rt, "Core", new Color(0.02f, 0.02f, 0.03f, 1)); core.Set(OzComicPoly.Ellipse(Vector2.zero, h.r * 0.92f, h.r * 0.88f, 24), new Rect(0, 0, 1, 1));
            for (int i = 0; i < 4; i++)
            {
                var c = Img(rt, "Crack", new Color(0.05f, 0.04f, 0.04f, 0.8f), false).rectTransform;
                c.sizeDelta = new Vector2(h.r * (1.6f + 0.5f * i % 2), 2f); c.pivot = new Vector2(0, 0.5f); c.localEulerAngles = new Vector3(0, 0, 35 + 90 * i + 17 * (i % 2));
            }
            rt.gameObject.SetActive(false);
            return rt;
        }

        Text TypeText(RectTransform parent, string name, Vector2 pos, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = UiLayer; go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>(); t.font = typeFont; t.fontSize = 46; t.fontStyle = FontStyle.Bold; t.color = OzUi.Amber; t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
            t.rectTransform.sizeDelta = new Vector2(1700, 80); t.rectTransform.anchoredPosition = pos;
            if (anchor == TextAnchor.MiddleLeft) { t.rectTransform.pivot = new Vector2(0, 0.5f); }
            return t;
        }

        void BuildSkip(RectTransform root)
        {
            skipRt = Rt(root, "SkipPrompt", new Vector2(372, 44));
            skipRt.anchorMin = skipRt.anchorMax = new Vector2(1, 0); skipRt.pivot = new Vector2(1, 0);
            skipRt.anchoredPosition = new Vector2(-Mathf.Clamp(0.04f * 1920, 20, 65), Mathf.Clamp(0.04f * 1080, 20, 50));
            skipCg = skipRt.gameObject.AddComponent<CanvasGroup>(); skipCg.alpha = 0; skipCg.blocksRaycasts = false;
            var plate = new List<Vector2> { new Vector2(-186 + 10, -22), new Vector2(186, -22), new Vector2(186, 22 - 10), new Vector2(186 - 10, 22), new Vector2(-186, 22), new Vector2(-186, -22 + 10) };
            var border = Poly(skipRt, "Border", OzUi.Amber); border.Set(plate, new Rect(0, 0, 1, 1), 1.5f);
            var fill = Poly(skipRt, "Plate", new Color(0.16f, 0.17f, 0.18f, 1f)); fill.Set(plate, new Rect(0, 0, 1, 1));
            var t = Tmp(skipRt, "PRESS SPACE TO SKIP", 17, new Color(0.92f, 0.9f, 0.85f, 1), false); t.characterSpacing = 6; t.rectTransform.anchoredPosition = new Vector2(-38, 0);
            t.rectTransform.sizeDelta = new Vector2(290, 30);
            var key = Poly(skipRt, "Key", new Color(0.95f, 0.72f, 0.29f, 1));
            key.Set(new List<Vector2> { new Vector2(118, -13), new Vector2(172, -13), new Vector2(172, 13), new Vector2(118, 13) }, new Rect(0, 0, 1, 1));
            var kt = Tmp(skipRt, "SPACE", 12, new Color(0.1f, 0.09f, 0.08f, 1), false); kt.rectTransform.anchoredPosition = new Vector2(145, 0); kt.characterSpacing = 2;
        }

        // ---------------------------------------------------------------- the console screen (a world-space UI on its own layer -> RT)
        RenderTexture screenRt; RectTransform scrRoot; readonly Dictionary<string, RectTransform> scrStates = new Dictionary<string, RectTransform>();
        TextMeshProUGUI scrCounter, scr30; RectTransform scrList; Material screenMat; string scrState = "";

        void BuildScreen()
        {
            screenRt = new RenderTexture(1024, 576, 16, RenderTextureFormat.ARGB32) { name = "RT_ConsoleScreen", antiAliasing = 4 }; screenRt.Create();
            var cgo = new GameObject("ScreenCanvas", typeof(RectTransform)); cgo.layer = ScreenLayer; cgo.transform.SetParent(transform, false);
            cgo.transform.position = new Vector3(0, -3000, 0);
            var c = cgo.AddComponent<Canvas>(); c.renderMode = RenderMode.WorldSpace;
            scrRoot = (RectTransform)cgo.transform; scrRoot.sizeDelta = new Vector2(1024, 576); scrRoot.localScale = Vector3.one * 0.01f;
            var camGo = new GameObject("ScreenCam"); camGo.transform.SetParent(transform, false); camGo.transform.position = new Vector3(0, -3000, -10);
            var sc = camGo.AddComponent<Camera>(); sc.orthographic = true; sc.orthographicSize = 2.88f; sc.cullingMask = 1 << ScreenLayer; sc.clearFlags = CameraClearFlags.SolidColor;
            sc.backgroundColor = new Color(0.02f, 0.03f, 0.03f, 1); sc.targetTexture = screenRt; sc.nearClipPlane = 1; sc.farClipPlane = 20;
            var ad = camGo.AddComponent<UniversalAdditionalCameraData>(); ad.renderPostProcessing = false;
            Color amber = OzUi.Amber;
            // off
            scrStates["off"] = Rt(scrRoot, "Off", new Vector2(1024, 576)); Img(scrStates["off"], "Bg", new Color(0.02f, 0.025f, 0.03f, 1), true);
            // TIME ASLEEP 30 YEARS
            var ta = scrStates["time_asleep"] = Rt(scrRoot, "TimeAsleep", new Vector2(1024, 576)); Img(ta, "Bg", new Color(0.06f, 0.05f, 0.03f, 1), true);
            Tmp(ta, "TIME ASLEEP", 64, amber, false).rectTransform.anchoredPosition = new Vector2(0, 170);
            scr30 = Tmp(ta, "30", 300, new Color(1f, 0.82f, 0.3f, 1), false); scr30.rectTransform.anchoredPosition = new Vector2(0, -10);
            Tmp(ta, "YEARS", 60, amber, false).rectTransform.anchoredPosition = new Vector2(0, -200);
            // 10,000 MISSED MESSAGES
            var ms = scrStates["messages"] = Rt(scrRoot, "Messages", new Vector2(1024, 576)); Img(ms, "Bg", new Color(0.06f, 0.05f, 0.03f, 1), true);
            // the Zod lines scroll up UNDER an opaque header (counter + MISSED MESSAGES): text can never overlap (storyboard P02D)
            scrList = Rt(ms, "List", new Vector2(900, 300)); scrList.anchoredPosition = Vector2.zero;
            string[] lines = { "FROM: ZOD - WHERE ARE YOU, YOU RUSTY BUCKETS?!", "FROM: ZOD - ANSWER ME!!!", "FROM: ZOD - ANSWER ME!!!!!", "FROM: ZOD - I CAN SEE YOU READING THIS", "FROM: ZOD - 30 YEARS?! REALLY?!", "FROM: ZOD - ANSWER ME!!!!!!!" };
            for (int i = 0; i < 14; i++)
            {
                var t = Tmp(scrList, lines[i % lines.Length], 31, new Color(0.98f, 0.9f, 0.75f, 1), false);
                t.alignment = TextAlignmentOptions.Left; t.rectTransform.sizeDelta = new Vector2(900, 46); t.rectTransform.anchoredPosition = new Vector2(0, -52 - 47 * i);
            }
            var head = Img(ms, "HeaderBg", new Color(0.06f, 0.05f, 0.03f, 1), false).rectTransform; head.sizeDelta = new Vector2(1024, 296); head.anchoredPosition = new Vector2(0, 140);
            scrCounter = Tmp(ms, "10,000", 150, new Color(1f, 0.82f, 0.3f, 1), false); scrCounter.rectTransform.anchoredPosition = new Vector2(0, 186);
            Tmp(ms, "MISSED MESSAGES", 48, amber, false).rectTransform.anchoredPosition = new Vector2(0, 74);
            var bar = Img(ms, "Bar", amber, false).rectTransform; bar.sizeDelta = new Vector2(760, 4); bar.anchoredPosition = new Vector2(0, 34);
            var rule = Img(ms, "Rule", new Color(amber.r, amber.g, amber.b, 0.45f), false).rectTransform; rule.sizeDelta = new Vector2(900, 2); rule.anchoredPosition = new Vector2(0, -6);
            // the shuttle assistant's smiley (green), hazard (red, radiation eyes), go (bright green)
            foreach (var kv in new[] { ("smiley", new Color(0.12f, 0.36f, 0.22f, 1), false), ("smiley_hazard", new Color(0.62f, 0.07f, 0.06f, 1), true), ("smiley_go", new Color(0.2f, 0.62f, 0.3f, 1), false) })
            {
                var s = scrStates[kv.Item1] = Rt(scrRoot, kv.Item1, new Vector2(1024, 576)); Img(s, "Bg", kv.Item2, true);
                var face = Color.white;
                for (int e = -1; e <= 1; e += 2)
                {
                    if (kv.Item3)
                    {
                        var eye = Poly(s, "EyeBg", new Color(1f, 0.85f, 0.15f, 1)); eye.Set(OzComicPoly.Ellipse(new Vector2(e * 170, 90), 80, 80, 36), new Rect(0, 0, 1, 1));
                        var hub = Poly(s, "Hub", ink); hub.Set(OzComicPoly.Ellipse(new Vector2(e * 170, 90), 13, 13, 16), new Rect(0, 0, 1, 1));
                        for (int k = 0; k < 3; k++)
                        {
                            float a0 = (90 + k * 120 - 30) * Mathf.Deg2Rad, a1 = (90 + k * 120 + 30) * Mathf.Deg2Rad; var c0 = new Vector2(e * 170, 90);
                            var wedge = new List<Vector2> { c0 + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 20, c0 + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 72, c0 + new Vector2(Mathf.Cos((a0 + a1) / 2), Mathf.Sin((a0 + a1) / 2)) * 74, c0 + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 72, c0 + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 20 };
                            Poly(s, "Wedge", ink).Set(wedge, new Rect(0, 0, 1, 1));
                        }
                    }
                    else Poly(s, "Eye", face).Set(OzComicPoly.Ellipse(new Vector2(e * 140, 80), 30, 30, 24), new Rect(0, 0, 1, 1));
                }
                var smile = new List<Vector2>();
                for (int i = 0; i <= 16; i++) { float a = Mathf.Lerp(200, 340, i / 16f) * Mathf.Deg2Rad; smile.Add(new Vector2(Mathf.Cos(a) * 230, Mathf.Sin(a) * 140 - 10)); }
                for (int i = 16; i >= 0; i--) { float a = Mathf.Lerp(200, 340, i / 16f) * Mathf.Deg2Rad; smile.Add(new Vector2(Mathf.Cos(a) * 200, Mathf.Sin(a) * 112 - 4)); }
                Poly(s, "Smile", face).Set(smile, new Rect(0, 0, 1, 1));
            }
            foreach (var kv in scrStates) kv.Value.gameObject.SetActive(false);
            if (shuttle)
            {
                foreach (var r in shuttle.GetComponentsInChildren<Renderer>(true))
                    if (r.name == "IC_Shuttle_Screen")
                    {
                        screenMat = new Material(r.sharedMaterial) { name = "M_IC_Screen_RT" };
                        screenMat.SetTexture("_BaseMap", screenRt); screenMat.SetColor("_BaseColor", Color.white);
                        screenMat.EnableKeyword("_EMISSION"); screenMat.SetTexture("_EmissionMap", screenRt); screenMat.SetColor("_EmissionColor", Color.white * 1.3f);
                        screenMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                        r.sharedMaterial = screenMat;
                    }
            }
            SetScreen("off", 0);
        }

        void SetScreen(string state, float pt)
        {
            if (!scrStates.ContainsKey(state)) state = "off";
            if (state != scrState) { foreach (var kv in scrStates) kv.Value.gameObject.SetActive(kv.Key == state); scrState = state; }
            if (state == "time_asleep") scr30.alpha = Mathf.Repeat(pt, 0.6f) < 0.42f ? 1f : 0.35f;
            if (state == "messages")
            {
                int n = Mathf.RoundToInt(Mathf.Lerp(0, 10000, EaseOut(pt / 0.35f)) / 7f) * 7; if (pt >= 0.35f) n = 10000;   // item 25: 10,000 on screen before "Ten thousand"
                scrCounter.text = n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
                scrList.anchoredPosition = new Vector2(0, pt * 115f);
            }
        }

        // ---------------------------------------------------------------- set dressing at runtime: piles (physics), instanced mound cans, fx
        readonly List<Rigidbody> pileA = new List<Rigidbody>(), pileB = new List<Rigidbody>(), blastCans = new List<Rigidbody>();
        GameObject pileARoot, pileBRoot, blastRoot;

        void BuildPiles()
        {
            var ground = new GameObject("IntroGroundCollider"); ground.transform.SetParent(transform, false);
            var bc = ground.AddComponent<BoxCollider>(); bc.size = new Vector3(400, 1, 400); bc.center = new Vector3(0, -0.5f, 0);
            var pm = new PhysicsMaterial("IntroCan") { bounciness = 0.15f, dynamicFriction = 0.5f, staticFriction = 0.6f };
            pileARoot = BuildPile("PileAlan", set.pileA, V3(set.alanPile), set.alanYaw, pileA, pm);
            pileBRoot = BuildPile("PileBrad", set.pileB, V3(set.bradPile), set.bradYaw, pileB, pm);
            blastRoot = new GameObject("LiftOffCans"); blastRoot.transform.SetParent(transform, false);
            var sp = V3(set.shuttlePos); var rnd = new System.Random(7);
            for (int i = 0; i < 70; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2, d = 4.5f + (float)rnd.NextDouble() * 4f;
                var go = Can(blastRoot.transform, i % 3, sp + new Vector3(Mathf.Cos(a) * d, 0.09f, Mathf.Sin(a) * d), Quaternion.Euler(90, a * Mathf.Rad2Deg, 0), pm);
                var rb = go.GetComponent<Rigidbody>(); blastCans.Add(rb);
            }
            blastRoot.SetActive(false);
        }

        GameObject BuildPile(string name, IntroInst inst, Vector3 root, float yaw, List<Rigidbody> list, PhysicsMaterial pm)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(root, Quaternion.Euler(0, yaw, 0));
            for (int i = 0; i < inst.Count; i++)
            {
                var lp = new Vector3(inst.p[i * 3], inst.p[i * 3 + 1], inst.p[i * 3 + 2]);
                var lf = new Vector3(inst.f[i * 3], inst.f[i * 3 + 1], inst.f[i * 3 + 2]); var lu = new Vector3(inst.u[i * 3], inst.u[i * 3 + 1], inst.u[i * 3 + 2]);
                var c = Can(go.transform, inst.v[i], Vector3.zero, Quaternion.identity, pm);
                c.transform.localPosition = lp; c.transform.localRotation = Quaternion.LookRotation(lf, lu);
                list.Add(c.GetComponent<Rigidbody>());
            }
            return go;
        }

        GameObject Can(Transform parent, int v, Vector3 pos, Quaternion rot, PhysicsMaterial pm)
        {
            var go = new GameObject("Can"); go.transform.SetParent(parent, false); go.transform.SetPositionAndRotation(pos, rot);
            go.AddComponent<MeshFilter>().sharedMesh = canMeshes[Mathf.Clamp(v, 0, canMeshes.Length - 1)];
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = canMat; mr.shadowCastingMode = ShadowCastingMode.On;
            var cc = go.AddComponent<CapsuleCollider>(); cc.radius = 0.085f; cc.height = 0.32f; cc.direction = 1; cc.sharedMaterial = pm;
            var rb = go.AddComponent<Rigidbody>(); rb.mass = 0.6f; rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            return go;
        }

        // instanced surface cans of every mound (no colliders): near = every can, far (> 45 m from the live camera) = every 7th at x2.6
        sealed class MoundCans { public Vector3 centre; public bool far; public List<Matrix4x4>[] full = new List<Matrix4x4>[3], sparse = new List<Matrix4x4>[3]; }
        /// <summary>item 22: the density is chosen ONCE per panel from the camera's start and end (no single-frame pop during a push-in).</summary>
        void ChooseMoundDensity(IntroPanel d)
        {
            float near = d.mound3 ? 40f : 45f; Vector3 a = V3(d.c0.pos), b = V3(d.c1.pos);
            foreach (var mc in moundCans) mc.far = Mathf.Min((mc.centre - a).magnitude, (mc.centre - b).magnitude) > near;
        }
        readonly List<MoundCans> moundCans = new List<MoundCans>(); readonly List<Matrix4x4>[] loose = new List<Matrix4x4>[3];
        readonly Matrix4x4[] batch = new Matrix4x4[1023];

        void BuildMoundInstances()
        {
            // the mound bodies in the scene are seated on the dunes by OzIntroBuilder (item 12): their cans follow the same height
            var moundRoot = setRoot ? setRoot.Find("CanMounds") : null; int mi = 0;
            foreach (var m in set.mounds)
            {
                var inst = m.type == "S" ? set.mS : m.type == "M" ? set.mM : set.mL;
                var mpos = V3(m.pos); if (moundRoot && mi < moundRoot.childCount) mpos.y = moundRoot.GetChild(mi).position.y; mi++;
                var mc = new MoundCans { centre = mpos };
                for (int k = 0; k < 3; k++) { mc.full[k] = new List<Matrix4x4>(); mc.sparse[k] = new List<Matrix4x4>(); }
                var M = Matrix4x4.TRS(mpos, Quaternion.Euler(0, m.yaw, 0), Vector3.one * m.scale);
                for (int i = 0; i < inst.Count; i++)
                {
                    var lp = new Vector3(inst.p[i * 3], inst.p[i * 3 + 1], inst.p[i * 3 + 2]);
                    var q = Quaternion.LookRotation(new Vector3(inst.f[i * 3], inst.f[i * 3 + 1], inst.f[i * 3 + 2]), new Vector3(inst.u[i * 3], inst.u[i * 3 + 1], inst.u[i * 3 + 2]));
                    int v = Mathf.Clamp(inst.v[i], 0, 2);
                    mc.full[v].Add(M * Matrix4x4.TRS(lp, q, Vector3.one));
                    if (i % 4 == 0) mc.sparse[v].Add(M * Matrix4x4.TRS(lp, q, Vector3.one * 2.0f));      // item 22: x2.0 every 4th (was x2.6 every 7th: giant cans)
                }
                moundCans.Add(mc);
            }
            for (int k = 0; k < 3; k++) loose[k] = new List<Matrix4x4>();
            for (int i = 0; i < set.loose.Count; i++)
            {
                var p = new Vector3(set.loose.p[i * 3], set.loose.p[i * 3 + 1], set.loose.p[i * 3 + 2]);
                var q = Quaternion.LookRotation(new Vector3(set.loose.f[i * 3], set.loose.f[i * 3 + 1], set.loose.f[i * 3 + 2]), new Vector3(set.loose.u[i * 3], set.loose.u[i * 3 + 1], set.loose.u[i * 3 + 2]));
                loose[Mathf.Clamp(set.loose.v[i], 0, 2)].Add(Matrix4x4.TRS(p, q, Vector3.one));
            }
        }

        void DrawCans(Camera cam, bool x3)
        {
            if (!cam || !canMat) return;
            var rp = new RenderParams(canMat) { camera = cam, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true, layer = 0 };
            foreach (var mc in moundCans)
                for (int k = 0; k < 3; k++) Draw(rp, canMeshes[k], mc.far ? mc.sparse[k] : mc.full[k]);
            for (int k = 0; k < 3; k++) Draw(rp, canMeshes[k], loose[k]);
        }

        void Draw(RenderParams rp, Mesh mesh, List<Matrix4x4> list)
        {
            for (int s = 0; s < list.Count; s += 1023)
            {
                int n = Mathf.Min(1023, list.Count - s);
                list.CopyTo(s, batch, 0, n);
                Graphics.RenderMeshInstanced(rp, mesh, 0, batch, n);
            }
        }

        // engine flames (lift jets down + rear thrusters), the space trail, stars, the nuke cloud
        readonly List<Transform> jets = new List<Transform>(); Transform trail, stars, cloud, cloudFlash, cloudRing; Light liftLight;
        void BuildFx()
        {
            var flame = FlameMesh();
            if (shuttle)
                foreach (var t in shuttle.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("LiftJet_") || t.name.StartsWith("Thruster_"))
                    {
                        var go = new GameObject("Flame_" + t.name); go.transform.SetParent(t, false);
                        go.AddComponent<MeshFilter>().sharedMesh = flame; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = flameMat ? flameMat : glowMat; mr.shadowCastingMode = ShadowCastingMode.Off;
                        go.transform.localScale = t.name.StartsWith("LiftJet_") ? new Vector3(0.55f, 0.55f, 2.2f) : new Vector3(0.6f, 0.6f, 2.8f);
                        // the sockets' own +Z is not the thrust axis (lift jets: shuttle-backward, pod thrusters: shuttle-down, from the FBX
                        // empties): aim each flame in shuttle space, lift jets straight down, the pod thrusters out of the back (+Z, the pods)
                        var shT = shuttle.transform; var wdir = t.name.StartsWith("LiftJet_") ? -shT.up : shT.forward; var wup = t.name.StartsWith("LiftJet_") ? shT.forward : shT.up;
                        go.transform.rotation = Quaternion.LookRotation(wdir, wup);
                        go.SetActive(false); jets.Add(go.transform);
                    }
            if (shuttle)
            {
                var lg = new GameObject("LiftLight"); lg.transform.SetParent(shuttle.transform, false); lg.transform.localPosition = new Vector3(0, -0.3f, 0);
                liftLight = lg.AddComponent<Light>(); liftLight.type = LightType.Point; liftLight.color = new Color(1f, 0.62f, 0.3f); liftLight.range = 16; liftLight.intensity = 0; liftLight.shadows = LightShadows.None;
                var tr = new GameObject("SpaceTrail"); tr.transform.SetParent(shuttle.transform, false);
                tr.AddComponent<MeshFilter>().sharedMesh = flame; var tmr = tr.AddComponent<MeshRenderer>(); tmr.sharedMaterial = flameMat ? flameMat : glowMat; tmr.shadowCastingMode = ShadowCastingMode.Off;
                tr.transform.localPosition = new Vector3(0, 2.0f, 2.4f); tr.transform.localScale = new Vector3(1.9f, 1.9f, 16f); tr.SetActive(false); trail = tr.transform;
            }
            // stars: 1100 soft billboard quads (varied size, tint, a few bright ones with spikes) on a far sphere round the space camera
            var st = new GameObject("Stars"); st.transform.SetParent(transform, false); st.AddComponent<MeshFilter>().sharedMesh = StarMesh();
            var stMr = st.AddComponent<MeshRenderer>(); stMr.sharedMaterial = starMat; stMr.shadowCastingMode = ShadowCastingMode.Off; stMr.receiveShadows = false;
            st.SetActive(false); stars = st.transform;
            // the mushroom cloud on the planet's limb (P07C): a modelled cauliflower cloud (~60 noise-lumped lobes: base surge, stem,
            // condensation skirt, rolling cap torus + dome), hot orange underside, cream top; small on the limb ("a tiny mushroom")
            var cl = new GameObject("NukeCloud").transform; cl.SetParent(transform, false); cloud = cl;
            var mush = new GameObject("Mushroom"); mush.transform.SetParent(cl, false);
            mush.AddComponent<MeshFilter>().sharedMesh = MushroomMesh();
            var cool = new Material(cloudMat) { name = "M_Intro_CloudCool" }; cool.SetColor("_BaseColor", new Color(1f, 0.93f, 0.84f)); cool.SetColor("_EmissionColor", new Color(1f, 0.62f, 0.36f) * 0.28f);
            var hot = new Material(cloudMat) { name = "M_Intro_CloudHot" }; hot.SetColor("_BaseColor", new Color(1f, 0.7f, 0.42f)); hot.SetColor("_EmissionColor", new Color(1f, 0.42f, 0.12f) * 2.4f);
            var mmr = mush.AddComponent<MeshRenderer>(); mmr.sharedMaterials = new[] { cool, hot }; mmr.shadowCastingMode = ShadowCastingMode.Off;
            cloudFlash = Prim(PrimitiveType.Sphere, cl, "Flash", new Vector3(0, 5, 0), new Vector3(30, 17, 30), glowMat).transform;
            var ringGo = new GameObject("GlowRing"); ringGo.transform.SetParent(cl, false); ringGo.AddComponent<MeshFilter>().sharedMesh = RingMesh();
            var rmat = new Material(glowMat); rmat.SetFloat("_Ring", 1); rmat.SetColor("_Color", new Color(3.2f, 1.4f, 0.4f, 1)); ringGo.AddComponent<MeshRenderer>().sharedMaterial = rmat;
            cloudRing = ringGo.transform; cl.gameObject.SetActive(false);
        }

        GameObject Prim(PrimitiveType t, Transform parent, string name, Vector3 lp, Vector3 ls, Material m)
        {
            var go = GameObject.CreatePrimitive(t); go.name = name; if (Application.isPlaying) Destroy(go.GetComponent<Collider>()); else DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false); go.transform.localPosition = lp; go.transform.localScale = ls; go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        static Mesh FlameMesh()
        {
            // item 17: a tapered cone along +Z (uv.y 0 at the nozzle -> 1 at the tip, uv.x round it), a slight belly then a long taper to a
            // point: drawn by OpenZ/Intro/Flame (soft view-angle edges, flicker), never the old crossed planks with square ends
            var m = new Mesh { name = "IntroFlameCone" }; var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            const int rings = 14, segs = 20;
            for (int r = 0; r <= rings; r++)
            {
                float t = r / (float)rings, rad = 0.5f * Mathf.Pow(1f - t, 0.8f) * (1f + 0.18f * Mathf.Sin(t * Mathf.PI));
                for (int s = 0; s <= segs; s++)
                {
                    float a = s / (float)segs * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad, t)); uv.Add(new Vector2(s / (float)segs, t));
                    if (r < rings && s < segs) { int b = r * (segs + 1) + s, n = b + segs + 1; tri.AddRange(new[] { b, n, b + 1, b + 1, n, n + 1 }); }
                }
            }
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }

        static Mesh RingMesh()
        {
            var m = new Mesh { name = "IntroGlowRing" }; var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>(); const int n = 64;
            for (int i = 0; i <= n; i++)
            {
                float a = 2 * Mathf.PI * i / n; var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                v.Add(d * 55f); v.Add(d * 85f); uv.Add(new Vector2(i / (float)n, 0)); uv.Add(new Vector2(i / (float)n, 1));
                if (i < n) { int b = i * 2; tri.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 }); }
            }
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateBounds(); return m;
        }

        // ---------------------------------------------------------------- the shuttle's moving parts
        public static bool FixCanopyFrame = true;
        Transform shCanopy, shLever, shButton, shLegs, shDoors, shStep; Quaternion canopy0, lever0; Vector3 button0; Material beaconMat, lampMat; Color beacon0, lamp0;
        readonly List<Renderer> leverRenderers = new List<Renderer>(); readonly List<Material> cabinMats = new List<Material>(); readonly List<Color> cabin0 = new List<Color>();

        void CacheShuttle()
        {
            if (!shuttle) return;
            foreach (var t in shuttle.GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "IC_Shuttle_Canopy": shCanopy = t; canopy0 = t.localRotation; SymPanes(t); break;
                    // item 10: IC_Shuttle.fbx gives the canopy's frame (window seals) a stray Lcl Rotation (90,0,0), the only rotation in the
                    // Y-up file: it swung the seals behind the tail as a tangle of rubber loops. Identity puts them back round the panes.
                    case "IC_Shuttle_CanopyFrame": if (FixCanopyFrame) t.localRotation = Quaternion.identity; SymPanes(t); break;
                    case "IC_Shuttle_ThrottleLever": shLever = t; lever0 = t.localRotation; break;
                    case "IC_Shuttle_AlarmButton": shButton = t; button0 = t.localPosition; break;
                    case "IC_Shuttle_Legs": shLegs = t; break;
                    case "IC_Shuttle_GearDoors": shDoors = t; break;
                    case "IC_Shuttle_NoseStep": shStep = t; break;
                }
                var r = t.GetComponent<Renderer>(); if (!r) continue;
                if (t.name == "IC_Shuttle_ThrottleLever" || t.name == "IC_Shuttle_ThrottleBox") leverRenderers.Add(r);
                if (t.name == "IC_Shuttle_Beacon") { beaconMat = r.material; beacon0 = beaconMat.GetColor("_EmissionColor"); }
                if (t.name == "IC_Shuttle_AlarmLamp") { lampMat = r.material; lamp0 = lampMat.GetColor("_EmissionColor"); }
                if (t.name.StartsWith("IC_Shuttle_CabinLight")) { var m = r.material; cabinMats.Add(m); cabin0.Add(m.GetColor("_EmissionColor")); }
            }
        }

        // crumpled, torn shape that runs 0.46 m past the hinge (milky and jagged in P05A at 0:43); the right pane is clean. Both meshes are
        // three separate parts (left pane, windshield, right pane), so the left part is replaced by the mirror image of the right one
        // (x -> -x, winding flipped, normals / tangents mirrored): the same thick dark seal and clear glass on both sides.
        static readonly Dictionary<Mesh, Mesh> symCache = new Dictionary<Mesh, Mesh>();
        static void SymPanes(Transform t)
        {
            var mf = t.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) return;
            mf.sharedMesh = SymmetricPanes(mf.sharedMesh, out var rep);
            Debug.Log($"[OpenZ Intro] {t.name}: {rep}");
        }

        /// <summary>Replaces the one mesh part that lies wholly on -x by the mirror of the one part wholly on +x (parts = triangle islands
        /// welded by position); everything else is kept. Returns the source mesh unchanged unless exactly one part sits on each side.</summary>
        public static Mesh SymmetricPanes(Mesh src, out string report)
        {
            report = "";
            if (symCache.TryGetValue(src, out var cached) && cached) { report = "mirrored pane (cached)"; return cached; }
            if (!src.isReadable) { report = "NOT mirrored: mesh not readable (Read/Write off); the scene uses the baked Intro/Models/Fixed/*_SymPanes meshes"; return src; }   // never cache a failure
            var v = src.vertices; int n = v.Length; var par = new int[n]; for (int i = 0; i < n; i++) par[i] = i;
            int Find(int a) { while (par[a] != a) { par[a] = par[par[a]]; a = par[a]; } return a; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) par[a] = b; }
            var tris = new List<int[]>(); for (int s = 0; s < src.subMeshCount; s++) tris.Add(src.GetTriangles(s));
            foreach (var tr in tris) for (int i = 0; i + 2 < tr.Length; i += 3) { Union(tr[i], tr[i + 1]); Union(tr[i], tr[i + 2]); }
            var weld = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < n; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(v[i].x * 2000f), Mathf.RoundToInt(v[i].y * 2000f), Mathf.RoundToInt(v[i].z * 2000f));
                if (weld.TryGetValue(k, out var j)) Union(i, j); else weld[k] = i;
            }
            var lo = new Dictionary<int, float>(); var hi = new Dictionary<int, float>();
            for (int i = 0; i < n; i++) { int r = Find(i); lo[r] = lo.TryGetValue(r, out var a) ? Mathf.Min(a, v[i].x) : v[i].x; hi[r] = hi.TryGetValue(r, out var b) ? Mathf.Max(b, v[i].x) : v[i].x; }
            var left = new List<int>(); var right = new List<int>();
            foreach (var kv in lo) { if (hi[kv.Key] < -0.05f) left.Add(kv.Key); else if (kv.Value > 0.05f) right.Add(kv.Key); }
            if (left.Count != 1 || right.Count != 1) { report = $"NOT mirrored: {left.Count} left / {right.Count} right side parts of {lo.Count}"; symCache[src] = src; return src; }
            int L = left[0], R = right[0];
            var nrm = src.normals; var tan = src.tangents; var col = src.colors; var uv0 = new List<Vector2>(); src.GetUVs(0, uv0); var uv1 = new List<Vector2>(); src.GetUVs(1, uv1);
            var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nt = new List<Vector4>(); var nu0 = new List<Vector2>(); var nu1 = new List<Vector2>(); var nc = new List<Color>();
            var map = new int[n]; var mir = new int[n];
            void Add(int i, bool m)
            {
                var p = v[i]; if (m) p.x = -p.x; nv.Add(p);
                if (nrm.Length == n) { var q = nrm[i]; if (m) q.x = -q.x; nn.Add(q); }
                if (tan.Length == n) { var q = tan[i]; if (m) { q.x = -q.x; q.w = -q.w; } nt.Add(q); }
                if (uv0.Count == n) nu0.Add(uv0[i]); if (uv1.Count == n) nu1.Add(uv1[i]); if (col.Length == n) nc.Add(col[i]);
            }
            for (int i = 0; i < n; i++) { map[i] = -1; mir[i] = -1; if (Find(i) != L) { map[i] = nv.Count; Add(i, false); } }
            for (int i = 0; i < n; i++) if (Find(i) == R) { mir[i] = nv.Count; Add(i, true); }
            var m2 = new Mesh { name = src.name + "_SymPanes", indexFormat = nv.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m2.SetVertices(nv); if (nn.Count == nv.Count) m2.SetNormals(nn); if (nt.Count == nv.Count) m2.SetTangents(nt);
            if (nu0.Count == nv.Count) m2.SetUVs(0, nu0); if (nu1.Count == nv.Count) m2.SetUVs(1, nu1); if (nc.Count == nv.Count) m2.SetColors(nc);
            m2.subMeshCount = tris.Count; int dropped = 0, added = 0;
            for (int s = 0; s < tris.Count; s++)
            {
                var tr = tris[s]; var o = new List<int>(tr.Length);
                for (int i = 0; i + 2 < tr.Length; i += 3)
                {
                    int c = Find(tr[i]);
                    if (c == L) { dropped++; continue; }
                    o.Add(map[tr[i]]); o.Add(map[tr[i + 1]]); o.Add(map[tr[i + 2]]);
                    if (c == R) { o.Add(mir[tr[i]]); o.Add(mir[tr[i + 2]]); o.Add(mir[tr[i + 1]]); added++; }
                }
                m2.SetTriangles(o, s);
            }
            m2.RecalculateBounds();
            report = $"left side part ({dropped} tris) replaced by the mirrored right side part ({added} tris)";
            symCache[src] = m2; return m2;
        }

        // ---------------------------------------------------------------- per-panel state
        readonly Dictionary<Renderer, int> movedLayers = new Dictionary<Renderer, int>();

        void GoLive(PanelUi p)
        {
            if (live != null && live.cam) live.cam.enabled = false;
            if (live != null && live.camW) live.camW.enabled = false;
            RestoreLayers();
            live = p; p.isLive = true;
            var d = p.d;
            Debug.Log($"[OpenZ Intro] panel {d.id} ({d.render}) live at {Clock():0.00} s (storyboard {d.t0:0.00})");
            // set visibility: space or the can planet
            if (setRoot) setRoot.gameObject.SetActive(!d.space);
            if (planet) planet.SetActive(d.space); if (ring) ring.SetActive(d.space);
            if (stars) stars.gameObject.SetActive(d.space);
            if (spaceCans) spaceCans.SetActive(d.space);
            RenderSettings.fog = !d.space && fog0;                                                           // the look's own fog setting (item 1: off)
            float k = d.space ? 0.22f : 1f; if (sunBig) sunBig.intensity = sunBigI * k; if (sunSmall) sunSmall.intensity = sunSmallI * k;
            RenderSettings.ambientIntensity = d.space ? 0.25f : ambient0;
            if (d.space && stars && p.cam) stars.position = p.cam.transform.position;
            RakeSun(p);
            ApplyPanelLook(d);
            if (d.hasCam) ChooseMoundDensity(d);
            if (heroCans) heroCans.SetActive(d.sunDiscs != null && d.sunDiscs.Length > 0);                // P01A's x5 foreground spill (item 9)
            // piles: Alan's burst spill stays on the sand in every later desert panel (item 28)
            pileARoot.SetActive(d.pileAlan > 0 || pileABurst); pileBRoot.SetActive(d.pileBrad > 0);
            burstAt = d.pileAlan == 2 && !pileABurst ? WakeRiseTime() : -1f;                                     // item 13: on his rising frame, not the panel's first
            // robots
            bool aIn = false, bIn = false;
            foreach (var a in d.actors) { if (a.robot == "alan") aIn = true; else bIn = true; }
            if (alan) alan.SetActive(aIn); if (bradGo) bradGo.SetActive(bIn);
            foreach (var a in d.actors) PlaceActor(a, 0f);
            // shuttle
            ApplyShuttle(d, 0f);
            // P07A keys its flames, dust and can blast to the ignition / blast cues (item 17); the space shot keeps its trail
            foreach (var j in jets) j.gameObject.SetActive(d.flames && d.id != "P07A");
            if (trail) trail.gameObject.SetActive(d.space);
            if (cloud) cloud.gameObject.SetActive(false);
            foreach (var m in cabinMats) m.SetColor("_EmissionColor", d.cabinGreen ? new Color(0.35f, 1f, 0.47f) * 2.2f : cabin0[cabinMats.IndexOf(m)]);
            // cut-out subjects
            if (d.render == "cutout")
            {
                if (d.actors.Length > 0) foreach (var a in d.actors) { var rgo = a.robot == "alan" ? alan : bradGo; MoveToCut(rgo); CutSeat(rgo); }
                else if (d.id == "P06E") foreach (var r in leverRenderers) MoveRendererToCut(r);
                else MoveToCut(shuttle);
                // the cut-out subject is lit like the outdoor cut-outs: the rest of the shuttle casts no shadow on it while it is live
                if (shuttle) foreach (var r in shuttle.GetComponentsInChildren<Renderer>(true))
                        if (r.gameObject.layer != CutLayer && !shadowOff.ContainsKey(r)) { shadowOff[r] = r.shadowCastingMode; r.shadowCastingMode = ShadowCastingMode.Off; }
            }
            if (p.cam) { ApplyCam(p, 0f); p.cam.enabled = true; }
            if (p.camW) { p.camW.enabled = CutoutDiffMatte; p.mat.SetFloat("_Matte", CutoutDiffMatte ? 1f : 0f); }
        }

        void MoveToCut(GameObject go) { if (!go) return; foreach (var r in go.GetComponentsInChildren<Renderer>(true)) MoveRendererToCut(r); }
        void MoveRendererToCut(Renderer r) { if (!movedLayers.ContainsKey(r)) movedLayers[r] = r.gameObject.layer; r.gameObject.layer = CutLayer; }
        void RestoreLayers()
        {
            foreach (var kv in movedLayers) if (kv.Key) kv.Key.gameObject.layer = kv.Value; movedLayers.Clear();
            foreach (var kv in shadowOff) if (kv.Key) kv.Key.shadowCastingMode = kv.Value; shadowOff.Clear();
        }
        readonly Dictionary<Renderer, ShadowCastingMode> shadowOff = new Dictionary<Renderer, ShadowCastingMode>();

        /// <summary>A seated robot's cut-out takes its seat with it (the previs P05B / P06C / P06D): no robot sitting on thin air.</summary>
        void CutSeat(GameObject robot)
        {
            if (!robot || !shuttle) return;
            Renderer best = null; float bd = 1.2f; var rp = robot.transform.position;
            foreach (var r in shuttle.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("IC_Shuttle_Seat"))
                {
                    var c = r.bounds.center; float dd = new Vector2(c.x - rp.x, c.z - rp.z).magnitude;
                    if (dd < bd) { bd = dd; best = r; }
                }
            if (best) { MoveRendererToCut(best); Debug.Log($"[OpenZ Intro] cut-out {robot.name} takes {best.name} ({bd:0.00} m)"); }
        }

        bool pileABurst;
        void BurstPile()
        {
            pileABurst = true;
            // item 13: staged by the body that rises out of it: 2-5 m/s off the chest (not a 9 m/s blast under a limp body), the cans
            // nearest the chest pop higher on the CRASH!, his rising chest / arms / helmet capsules shove the rest (kinematic colliders)
            var chestT = alan ? Bone(alan, "mixamorigSpine2") : null;
            var chest = chestT ? chestT.position : V3(set.alanPile) + Vector3.up * 0.35f;
            BodyColliders(alan);
            var near = new List<Rigidbody>(pileA); near.Sort((a, b) => (a.position - chest).sqrMagnitude.CompareTo((b.position - chest).sqrMagnitude));
            for (int i = 0; i < near.Count; i++)
            {
                var rb = near[i]; rb.isKinematic = false; rb.maxDepenetrationVelocity = 2.5f;
                var d = rb.position - chest; float dist = d.magnitude; d.y = Mathf.Abs(d.y) + 0.35f; d.Normalize();
                float v = Mathf.Lerp(5f, 2f, Mathf.Clamp01(dist / 1.6f)) * Random.Range(0.8f, 1.15f);
                if (i < 10) v += Random.Range(1.5f, 3f);                                                             // the hero cans on the CRASH!
                rb.linearVelocity = d * v; rb.angularVelocity = Random.insideUnitSphere * 8f;
            }
        }

        bool bodyCols;
        void BodyColliders(GameObject robot)
        {
            if (bodyCols || !robot) return; bodyCols = true;
            void Cap(string bone, float r, float h, int dir)
            {
                var b = Bone(robot, bone); if (!b) return;
                var go = new GameObject("BodyCol_" + bone); go.transform.SetParent(b, false); go.layer = 2;
                var cc = go.AddComponent<CapsuleCollider>(); cc.radius = r; cc.height = h; cc.direction = dir;
                var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None;
            }
            Cap("mixamorigSpine2", 0.3f, 0.75f, 0); Cap("mixamorigSpine", 0.26f, 0.6f, 0); Cap("mixamorigHead", 0.2f, 0.4f, 1);
            Cap("mixamorigLeftArm", 0.1f, 0.4f, 1); Cap("mixamorigRightArm", 0.1f, 0.4f, 1); Cap("mixamorigLeftForeArm", 0.09f, 0.4f, 1); Cap("mixamorigRightForeArm", 0.09f, 0.4f, 1);
        }

        void BlastCans()
        {
            blastRoot.SetActive(true); var c = V3(set.shuttlePos);
            foreach (var rb in blastCans)
            {
                rb.isKinematic = false; var dir = (rb.position - c); dir.y = 0; dir.Normalize();
                rb.linearVelocity = dir * Random.Range(5f, 10f) + Vector3.up * Random.Range(3f, 7f); rb.angularVelocity = Random.insideUnitSphere * 12f;
            }
        }

        void PlaceActor(IntroActor a, float pt)
        {
            var go = a.robot == "alan" ? alan : bradGo; if (!go) return;
            go.transform.SetPositionAndRotation(V3(a.pos), Quaternion.Euler(0, a.yaw, 0) * (baseRot.TryGetValue(go, out var br) ? br : Quaternion.identity));
            var dict = a.robot == "alan" ? clipsA : clipsB;
            if (!string.IsNullOrEmpty(a.clip) && dict.TryGetValue(a.clip, out var clip))
            {
                float ct;
                if (a.clipStart > -998f) ct = pt - a.clipStart;
                else ct = a.clipTime + (clip.isLooping ? pt : 0f);
                ct = clip.isLooping ? Mathf.Repeat(ct, clip.length) : Mathf.Clamp(ct, 0f, clip.length);
                clip.SampleAnimation(go, ct);
                // fix-list item 16: after the one-shot, blend into the storyboard's talking loop ("then", P05C ic_seat_lean_talk)
                if (!string.IsNullOrEmpty(a.thenClip) && a.thenStart >= 0f && pt > a.thenStart && dict.TryGetValue(a.thenClip, out var then))
                {
                    float w = a.thenBlend > 0f ? Mathf.Clamp01((pt - a.thenStart) / a.thenBlend) : 1f;
                    if (w < 1f) SnapPose(go);
                    float tt = pt - a.thenStart; tt = then.isLooping ? Mathf.Repeat(tt, then.length) : Mathf.Clamp(tt, 0f, then.length);
                    then.SampleAnimation(go, tt);
                    if (w < 1f) BlendFromSnap(go, EaseInOut(w));
                }
                ArmContacts(a, go, pt);
                // mouths v2 (staging models README section 9): the jaw + head bob from the mix clock, on top of the sampled pose
                var jawPlayer = go.GetComponent<IC_RobotJaw>();
                if (jawPlayer) jawPlayer.ApplyAt(Clock());
            }
            ApplyFace(go, a);
            SetCan(go, a.robot == "brad" && !string.IsNullOrEmpty(a.prop) && a.prop.Contains("FuelCan"));
            if (live != null) TwistCan(go, live.cam);
        }

        // ---------------------------------------------------------------- pose blend (one-shot -> talking loop)
        readonly Dictionary<GameObject, Transform[]> poseBones = new Dictionary<GameObject, Transform[]>();
        Quaternion[] snapR = new Quaternion[0]; Vector3[] snapP = new Vector3[0];
        void SnapPose(GameObject go)
        {
            if (!poseBones.TryGetValue(go, out var bs)) { bs = go.GetComponentsInChildren<Transform>(true); poseBones[go] = bs; }
            if (snapR.Length < bs.Length) { snapR = new Quaternion[bs.Length]; snapP = new Vector3[bs.Length]; }
            for (int i = 0; i < bs.Length; i++) { snapR[i] = bs[i].localRotation; snapP[i] = bs[i].localPosition; }
        }
        void BlendFromSnap(GameObject go, float w)
        {
            var bs = poseBones[go];
            for (int i = 1; i < bs.Length; i++) { bs[i].localRotation = Quaternion.Slerp(snapR[i], bs[i].localRotation, w); bs[i].localPosition = Vector3.Lerp(snapP[i], bs[i].localPosition, w); }
        }

        // A two-bone IK (upper arm, forearm; the hand keeps its animated rotation) moves the fist onto the real surface it touches, 1.5 cm
        // proud of it: P02A the palm on the cockpit rim in front of him (the slap; the clip reached 0.27 m past the sill into the cabin),
        // P02C the knuckles on top of the red dome (the dome stays visible, the fist never sinks into it), P05C Brad's knuckles on the
        // speaker grille. Surfaces come from the shuttle's own renderers (query-only mesh colliders), so the fix follows the model.
        sealed class Contact { public string panel, robot, side, kind; public float t0, t1, ramp; }
        static readonly Contact[] contacts =
        {
            new Contact { panel = "P02A", robot = "alan", side = "Right", kind = "rim",    t0 = -0.02f, t1 = 99f, ramp = 0.07f },
            new Contact { panel = "P02C", robot = "alan", side = "Right", kind = "dome",   t0 = 0.04f,  t1 = 99f, ramp = 0.03f },
            new Contact { panel = "P05C", robot = "brad", side = "Right", kind = "grille", t0 = -99f,   t1 = 99f, ramp = 0f },
        };
        readonly Dictionary<string, Transform> boneCache = new Dictionary<string, Transform>();
        readonly List<Collider> shuttleCols = new List<Collider>(); readonly Dictionary<string, Renderer> shParts = new Dictionary<string, Renderer>();
        public static int ContactsApplied; public static float ContactMaxFix;
        readonly Dictionary<Contact, Vector3> rimTarget = new Dictionary<Contact, Vector3>();

        Transform Bone(GameObject go, string n)
        {
            string key = go.GetInstanceID() + n;
            if (boneCache.TryGetValue(key, out var t)) return t;
            foreach (var x in go.GetComponentsInChildren<Transform>(true)) if (x.name == n) { t = x; break; }
            boneCache[key] = t; return t;
        }

        void BuildShuttleColliders()
        {
            if (!shuttle) return;
            foreach (var mf in shuttle.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue;
                var r = mf.GetComponent<Renderer>(); if (r) shParts[r.name] = r;
                var go = new GameObject("Q_" + mf.name); go.transform.SetParent(mf.transform, false); go.layer = 2;   // Ignore Raycast for everything else
                var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; shuttleCols.Add(mc);
            }
        }

        bool RayShuttle(Vector3 from, Vector3 dir, float len, out RaycastHit best, string only = null, bool noCanopy = true)
        {
            best = default; float bd = float.MaxValue; bool any = false;
            foreach (var c in shuttleCols)
            {
                if (!c || !c.gameObject.activeInHierarchy) continue;
                string pn = c.transform.parent.name;
                if (only != null && !pn.Contains(only)) continue;
                if (noCanopy && (pn.Contains("Canopy") || pn.Contains("Glass"))) continue;   // the open canopy above is not a surface to rest on
                if (c.Raycast(new Ray(from, dir), out var h, len) && h.distance < bd) { bd = h.distance; best = h; any = true; }
            }
            return any;
        }

        void ArmContacts(IntroActor a, GameObject go, float pt)
        {
            if (live == null || shuttleCols.Count == 0) return;
            foreach (var c in contacts)
            {
                if (c.panel != live.d.id || c.robot != a.robot || pt < c.t0 - c.ramp || pt > c.t1) continue;
                float w = c.ramp > 0f ? Mathf.Clamp01((pt - (c.t0 - c.ramp)) / c.ramp) : 1f;
                Transform up = Bone(go, "mixamorig" + c.side + "Arm"), mid = Bone(go, "mixamorig" + c.side + "ForeArm"), hand = Bone(go, "mixamorig" + c.side + "Hand");
                if (!up || !mid || !hand) continue;
                // the fist: its centre sits ~9 cm past the wrist along the forearm, radius ~6.5 cm (BS-01 mitt)
                Vector3 along = (hand.position - mid.position).normalized, fist = hand.position + along * 0.09f; const float R = 0.065f;
                Physics.SyncTransforms();
                Vector3 want = fist; bool ok = false;
                if (c.kind == "dome" && shParts.TryGetValue("IC_Shuttle_AlarmButton", out var dome))
                {
                    var b = dome.bounds; want = new Vector3(Mathf.Lerp(fist.x, b.center.x, 0.85f), b.max.y + R + 0.012f, Mathf.Lerp(fist.z, b.center.z, 0.85f));
                    // above the dome before the press: never inside it
                    if (pt < c.t0) want.y = Mathf.Max(fist.y, want.y);
                    ok = true;
                }
                else if (c.kind == "rim")
                {
                    // the first surface the forearm crosses on its way to the fist = the cockpit rim; the palm lands on its top, 4 cm in.
                    // Found once per panel and held (the rim height differs 5 cm between the hull and the window trim: no frame-to-frame pop)
                    if (rimTarget.TryGetValue(c, out var held)) { want = held; ok = true; }
                    else
                    {
                        var from = mid.position; var d = fist - from;
                        if (RayShuttle(from, d.normalized, d.magnitude + R, out var h))
                        {
                            var flat = new Vector3(d.x, 0, d.z).normalized;
                            var probe = h.point + flat * 0.04f + Vector3.up * 0.35f;
                            if (RayShuttle(probe, Vector3.down, 0.9f, out var top)) { want = top.point + Vector3.up * (R + 0.015f); ok = true; rimTarget[c] = want; }
                        }
                    }
                }
                else if (c.kind == "grille" && shParts.TryGetValue("IC_Shuttle_Speaker", out var spk))
                {
                    float top = spk.bounds.max.y + R + 0.012f;
                    if (fist.y < top && new Vector2(fist.x - spk.bounds.center.x, fist.z - spk.bounds.center.z).magnitude < spk.bounds.extents.x + R) { want = new Vector3(fist.x, top, fist.z); ok = true; }
                }
                if (!ok) continue;
                var target = Vector3.Lerp(fist, want, w);
                float fix = (target - fist).magnitude; if (fix < 0.002f) continue;
                TwoBoneIK(up, mid, hand, target - along * 0.09f);
                ContactsApplied++; ContactMaxFix = Mathf.Max(ContactMaxFix, fix);
            }
        }

        static float TriAngle(float a, float b, float c) => Mathf.Acos(Mathf.Clamp((b * b + c * c - a * a) / (2f * b * c), -1f, 1f));
        /// <summary>Analytic two-bone IK (as Unity's AnimationRuntimeUtils.SolveTwoBoneIK): bends in the animated plane, the end keeps its rotation.</summary>
        static void TwoBoneIK(Transform root, Transform mid, Transform tip, Vector3 target)
        {
            var tipRot = tip.rotation;
            Vector3 ab = mid.position - root.position, bc = tip.position - mid.position, ac = tip.position - root.position, at = target - root.position;
            float abL = ab.magnitude, bcL = bc.magnitude, acL = ac.magnitude, atL = Mathf.Clamp(at.magnitude, 0.01f, abL + bcL - 0.001f);
            float oldA = TriAngle(acL, abL, bcL), newA = TriAngle(atL, abL, bcL);
            var axis = Vector3.Cross(ab, bc); if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(ab, Vector3.up); axis.Normalize();
            float half = 0.5f * (oldA - newA), s = Mathf.Sin(half);
            mid.rotation = new Quaternion(axis.x * s, axis.y * s, axis.z * s, Mathf.Cos(half)) * mid.rotation;
            root.rotation = Quaternion.FromToRotation(tip.position - root.position, at) * root.rotation;
            tip.rotation = tipRot;
        }

        readonly Dictionary<GameObject, GameObject> cans = new Dictionary<GameObject, GameObject>();
        void SetCan(GameObject robot, bool on)
        {
            if (!cans.TryGetValue(robot, out var can))
            {
                Transform grip = null; foreach (var t in robot.GetComponentsInChildren<Transform>(true)) if (t.name == "CanGrip_R") grip = t;
                if (!grip) { cans[robot] = null; return; }
                can = new GameObject("HeldCan"); can.transform.SetParent(grip, false);
                can.AddComponent<MeshFilter>().sharedMesh = canMeshes[0]; can.AddComponent<MeshRenderer>().sharedMaterial = canMat; cans[robot] = can;
                // item 15: the raw mesh under the grip pointed its lid at the camera (the Blender Z-up / Unity Y-up 90 deg). Stand it upright
                // in the fist: its long axis along the grip axis that points most nearly up in the first pose it is held in (P03B, the
                // mug pose), the label round it, the can centred in the fist
                var bsz = canMeshes[0].bounds.size; Vector3 ax = bsz.x >= bsz.y && bsz.x >= bsz.z ? Vector3.right : bsz.y >= bsz.z ? Vector3.up : Vector3.forward;
                Vector3 best = Vector3.up; float bd = -2f;
                foreach (var cand in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
                { float dd = Vector3.Dot(grip.TransformDirection(cand), Vector3.up); if (dd > bd) { bd = dd; best = cand; } }
                can.transform.localRotation = Quaternion.FromToRotation(ax, best);
                can.transform.localPosition = -(can.transform.localRotation * canMeshes[0].bounds.center);
                canPose[can] = (ax, CanLabelDir(canMeshes[0], ax), can.transform.localRotation, can.transform.localPosition);
                Debug.Log($"[OpenZ Intro] held can: mesh axis {ax} -> grip {best} (up dot {bd:0.00})");
            }
            if (can) { can.SetActive(on); can.layer = robot.GetComponentInChildren<Renderer>(true).gameObject.layer; }
        }

        // axis so the nearer of its two ROCKET FUEL labels (u 0.25 / 0.75 of IC_FuelCan_BaseColor) faces the live camera
        readonly Dictionary<GameObject, (Vector3 ax, Vector3 lab, Quaternion rot0, Vector3 pos0)> canPose = new Dictionary<GameObject, (Vector3, Vector3, Quaternion, Vector3)>();
        static Vector3 CanLabelDir(Mesh m, Vector3 ax)
        {
            var v = m.vertices; var uv = new List<Vector2>(); m.GetUVs(0, uv); var c = m.bounds.center; var sum = Vector3.zero;
            for (int i = 0; i < v.Length && i < uv.Count; i++)
                if (uv[i].y > 0.52f && Mathf.Abs(uv[i].x - 0.25f) < 0.05f) sum += Vector3.ProjectOnPlane(v[i] - c, ax);
            return sum.sqrMagnitude > 1e-8f ? sum.normalized : Vector3.zero;
        }
        void TwistCan(GameObject robot, Camera cam)
        {
            if (!cam || !cans.TryGetValue(robot, out var can) || !can || !can.activeSelf || !canPose.TryGetValue(can, out var cp) || cp.lab == Vector3.zero) return;
            can.transform.localRotation = cp.rot0; can.transform.localPosition = cp.pos0;
            var centre = can.transform.TransformPoint(canMeshes[0].bounds.center);
            var axisW = can.transform.TransformDirection(cp.ax).normalized;
            var labW = Vector3.ProjectOnPlane(can.transform.TransformDirection(cp.lab), axisW);
            var toCam = Vector3.ProjectOnPlane(cam.transform.position - centre, axisW);
            if (labW.sqrMagnitude < 1e-8f || toCam.sqrMagnitude < 1e-8f) return;
            float a = Vector3.SignedAngle(labW, toCam, axisW);
            if (a > 90f) a -= 180f; else if (a < -90f) a += 180f;                                                   // two labels 180 deg apart
            can.transform.RotateAround(centre, axisW, a);
        }

        readonly Dictionary<GameObject, (SkinnedMeshRenderer smr, Material glow, Color c0)> faces = new Dictionary<GameObject, (SkinnedMeshRenderer, Material, Color)>();
        void ApplyFace(GameObject go, IntroActor a)
        {
            if (!faces.TryGetValue(go, out var f))
            {
                SkinnedMeshRenderer sm = null; Material g = null; Color c0 = Color.white;
                foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (r.sharedMesh && r.sharedMesh.blendShapeCount > 0) sm = r;
                    foreach (var m in r.sharedMaterials) if (m && m.name.Contains("EyeGlow")) { g = r.material; c0 = g.GetColor("_EmissionColor"); }
                }
                f = (sm, g, c0); faces[go] = f;
            }
            if (f.smr)
            {
                var mesh = f.smr.sharedMesh;
                for (int i = 0; i < mesh.blendShapeCount; i++) f.smr.SetBlendShapeWeight(i, 0f);
                if (a.bsN != null) for (int i = 0; i < a.bsN.Length; i++) { int bi = mesh.GetBlendShapeIndex(a.bsN[i]); if (bi >= 0) f.smr.SetBlendShapeWeight(bi, a.bsV[i] * 100f); }
            }
            if (f.glow) f.glow.SetColor("_EmissionColor", f.c0 * a.emis);
        }

        void ApplyShuttle(IntroPanel d, float pt)
        {
            if (!shuttle) return;
            var pos = V3(d.shPos); var rot = Quaternion.Euler(d.shPitch, set.shuttleYaw, 0); float scale = 1f;
            if (d.id == "P07A")
            {
                // item 17: parked through the cough, the jets light on the ignition, it lifts on the roar (a short shudder first)
                float lift = d.Cue("shuttle_takeoff_roar", 0, 0.55f), ign = d.Cue("shuttle_engine_start", 1, 0.48f);
                float u = Mathf.Clamp01((pt - lift) / Mathf.Max(0.1f, d.dur - lift));
                pos.y = Mathf.Lerp(0f, 1.5f, u * u);                                                                  // item 36: stays inside the panel
                if (pt > ign && pt < lift + 0.3f) pos += new Vector3(Mathf.PerlinNoise(pt * 30f, 1f) - 0.5f, Mathf.PerlinNoise(pt * 30f, 2f) - 0.5f, 0f) * 0.03f;
                rot = Quaternion.Euler(Mathf.Lerp(0, d.shPitch * 1.2f, u), set.shuttleYaw, 0);
            }
            if (d.id == "P07B") { pos.y += pt * 6f; }
            if (d.space)
            {
                var s = d.spaceData; float u = Mathf.Clamp01(pt / d.dur);
                pos = Vector3.Lerp(V3(s.shStart), V3(s.shEnd), u); rot = Quaternion.LookRotation(-V3(s.shNose), V3(s.shUp)); scale = s.shScale;
            }
            shuttle.transform.SetPositionAndRotation(pos, rot * shBase); shuttle.transform.localScale = Vector3.one * scale;
            if (shCanopy) shCanopy.localRotation = canopy0 * Quaternion.Euler(d.shCanopy, 0, 0);
            float lever = d.shLever;
            if (d.id == "P06E")
            {
                // item 6: the throttle "pulls itself": a small creep on throttle_lever, then a SNAP to +45 on the KA-CHUNK transient
                float pull = d.Cue("throttle_lever", 0, 1.40f), snap = d.Cue("lever_ka_chunk", 0, 1.42f);
                lever = pt < pull ? 0f : pt < snap ? Mathf.Lerp(0f, 6f, (pt - pull) / Mathf.Max(0.01f, snap - pull)) : 45f + 5f * Mathf.Sin(Mathf.Clamp01((pt - snap) / 0.12f) * Mathf.PI) * (1f - Mathf.Clamp01((pt - snap) / 0.12f));
            }
            if (shLever) shLever.localRotation = lever0 * Quaternion.Euler(lever, 0, 0);
            if (shLegs) shLegs.gameObject.SetActive(!d.gearUp); if (shDoors) shDoors.gameObject.SetActive(d.gearUp); if (shStep) shStep.gameObject.SetActive(!d.gearUp);
            bool blink = d.alarm && Mathf.Repeat(pt, 0.5f) < 0.25f;
            if (beaconMat) beaconMat.SetColor("_EmissionColor", blink ? new Color(1f, 0.08f, 0.04f) * 6f : beacon0);
            if (lampMat) lampMat.SetColor("_EmissionColor", blink ? new Color(1f, 0.1f, 0.05f) * 5f : lamp0);
            if (shButton)
            {
                bool pressed = d.id == "P02C" && pt > 0.06f || d.id == "P02D";
                shButton.localPosition = button0 + (pressed ? shButton.localRotation * Vector3.down * 0.015f : Vector3.zero);
            }
            if (liftLight) liftLight.intensity = d.flames ? (3.5f + Mathf.PerlinNoise(pt * 9f, 0.3f) * 2f) * (d.id == "P07A" ? Mathf.Clamp01((pt - d.Cue("shuttle_engine_start", 1, 0.48f)) / 0.15f) : 1f) : 0f;
            float ignK = d.id == "P07A" ? Mathf.Clamp01((pt - d.Cue("shuttle_engine_start", 1, 0.48f)) / 0.15f) : 1f;          // item 17: the jets light up on the ignition
            Vector3 sLift = LookV3("flameLift", new Vector3(0.95f, 0.95f, 3.0f)), sThr = LookV3("flameThruster", new Vector3(0.8f, 0.8f, 3.2f));
            foreach (var j in jets) if (j.gameObject.activeSelf) { float fl = 0.88f + 0.24f * Mathf.PerlinNoise(pt * 14f, j.GetInstanceID() * 0.01f); var s0 = j.name.Contains("LiftJet") ? sLift : sThr; j.localScale = new Vector3(s0.x * Mathf.Lerp(0.4f, 1f, ignK), s0.y * Mathf.Lerp(0.4f, 1f, ignK), s0.z * fl * ignK + 0.001f); }
            SetScreen(d.screen, pt);
        }

        void ApplyCam(PanelUi p, float pt)
        {
            var d = p.d; float u0 = d.dur > 0 ? Mathf.Clamp01(pt / d.dur) : 0f, u = u0;
            u = d.ease.Contains("in-out") ? EaseInOut(u) : d.ease.Contains("out") ? EaseOut(u) : d.ease.Contains("in") ? EaseIn(u) : u;
            var pos = Vector3.Lerp(V3(d.c0.pos), V3(d.c1.pos), u);
            var fwd = Vector3.Slerp(V3(d.c0.fwd), V3(d.c1.fwd), u); var up = Vector3.Slerp(V3(d.c0.up), V3(d.c1.up), u);
            // push-in on locked shots, a crash zoom on every hit sticker (a softer one on the gag bubbles), an impact shake, the roar rumble
            float seed = p.index * 3.17f;
            float hy = (Mathf.PerlinNoise(pt * 0.33f + seed, 0.5f) - 0.5f) * 2f, hp = (Mathf.PerlinNoise(0.5f, pt * 0.29f + seed) - 0.5f) * 2f;
            float rx = hp * 0.32f, ry = hy * 0.42f, rz = (Mathf.PerlinNoise(pt * 0.21f + seed, 3.3f) - 0.5f) * 0.3f;
            float fovK = 1f, shake = 0f;
            bool locked = (V3(d.c0.pos) - V3(d.c1.pos)).sqrMagnitude < 1e-4f && Mathf.Abs(d.c0.fov - d.c1.fov) < 0.01f;
            if (locked) fovK *= 1f - 0.07f * EaseInOut(u0);
            if (d.stickers != null)
                foreach (var s in d.stickers)
                {
                    float st = pt - s.at; if (st < 0f || st > 0.5f) continue;
                    bool hit = s.kind != "bubble"; float k = hit ? 0.11f : 0.05f;
                    float punch = st < 0.06f ? st / 0.06f : 1f - EaseOut((st - 0.06f) / 0.44f);
                    fovK *= 1f - k * punch;
                    if (hit && st < 0.2f) shake = Mathf.Max(shake, 0.55f * (1f - st / 0.2f));
                }
            if (d.id == "P01C" && burstAt >= 0f) { float st = pt - burstAt; if (st >= 0f && st < 0.3f) shake = Mathf.Max(shake, 0.9f * (1f - st / 0.3f)); }
            if (d.id == "P07A") { float roar = d.Cue("shuttle_takeoff_roar", 0, 0.55f); if (pt > roar) shake = Mathf.Max(shake, 0.35f * Mathf.Clamp01((pt - roar) / 0.5f)); }
            if (shake > 0f) { rx += (Mathf.PerlinNoise(pt * 23f, 1.7f) - 0.5f) * 2f * shake; ry += (Mathf.PerlinNoise(4.1f, pt * 23f) - 0.5f) * 2f * shake; }
            p.cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, up) * Quaternion.Euler(rx, ry, rz));
            p.cam.fieldOfView = Mathf.Lerp(d.c0.fov, d.c1.fov, u) * fovK;
            if (p.camW) p.camW.fieldOfView = p.cam.fieldOfView;
            if (d.space && stars) stars.position = pos;
        }

        // ---------------------------------------------------------------- item 11: a raking key light per desert panel (contact shadows)
        bool fog0; float ambient0 = 1f; Quaternion sunRot0 = Quaternion.identity, sunSmallRot0 = Quaternion.identity;
        [Header("Look")] public float rakeYaw = 50f, rakeElev = 32f;   // look v4e A/B (78 deg put the camera-facing mound sides in shade); set from intro_look.txt
        void RakeSun(PanelUi p)
        {
            if (!sunBig) return;
            var d = p.d;
            if (d.space || d.render != "3d" || !p.cam) { sunBig.transform.rotation = sunRot0; if (sunSmall) sunSmall.transform.rotation = sunSmallRot0; return; }
            // the sun rakes ~50 deg across this panel's camera axis (on the side nearest the set's own sun), so every foot, pad and pile
            // throws a visible shadow instead of hiding it behind itself (SunBig sat 3-22 deg behind the cameras)
            var f = V3(d.c0.fwd); float camYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, yaw0 = sunRot0.eulerAngles.y;
            float ry = PL(d.id, "rakeYaw", rakeYaw), re = PL(d.id, "rakeElev", rakeElev);                     // per-panel override (intro_look.txt)
            float a = camYaw + ry, b = camYaw - ry;
            float yaw = Mathf.Abs(Mathf.DeltaAngle(a, yaw0)) <= Mathf.Abs(Mathf.DeltaAngle(b, yaw0)) ? a : b;
            var q = Quaternion.Euler(re, yaw, 0f);
            if (sunSmall) sunSmall.transform.rotation = q * Quaternion.Inverse(sunRot0) * sunSmallRot0;
            sunBig.transform.rotation = q;
        }

        // A too-white look still showed at 0:43 (P05A: the cream hull rim round the cockpit clipped, p50 +10 % on the animatic) and
        // P04A sat 10 % dark: Data/intro_look.txt lines "panel <ID> key value ..." set them per panel, read at run time (no recompile):
        // sun (x both suns), cabin (x the cabin fill light), hull (x the shuttle albedo), exposure (+EV), contrast (+), ambient (x the ambient
        // probe), rakeYaw / rakeElev (deg, replace the global rake). Plain "key v1 [v2 v3]" lines feed the flames and the lift-off sand.
        readonly Dictionary<string, Dictionary<string, float>> panelLook = new Dictionary<string, Dictionary<string, float>>();
        readonly Dictionary<string, float[]> lookVals = new Dictionary<string, float[]>();
        Light cabinFill; float cabinFill0 = 1f; Material hullMat; Color hull0 = Color.white;
        ColorAdjustments colAdj; float exposure0, contrast0; SphericalHarmonicsL2 ambientProbe0; bool lookReady;
        Material skyRun, canRun; float sky0 = 1f; Color can0 = Color.white;                              // "sky" (x panorama exposure), "cans" (x can albedo)

        void LoadLook()
        {
            panelLook.Clear(); lookVals.Clear();
            if (!lookTxt) { Debug.LogWarning("[OpenZ Intro] lookTxt missing: built-in look values"); return; }
            var inv = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
            foreach (var raw in lookTxt.text.Split('\n'))
            {
                var s = raw.Trim(); int hash = s.IndexOf('#'); if (hash >= 0) s = s.Substring(0, hash).Trim(); if (s.Length == 0) continue;
                var p = s.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (p[0] == "panel" && p.Length >= 4)
                {
                    var dct = new Dictionary<string, float>();
                    for (int i = 2; i + 1 < p.Length; i += 2) if (float.TryParse(p[i + 1], fs, inv, out var pv)) dct[p[i]] = pv;
                    panelLook[p[1]] = dct; continue;
                }
                var vals = new List<float>(); for (int i = 1; i < p.Length; i++) if (float.TryParse(p[i], fs, inv, out var v)) vals.Add(v);
                if (vals.Count > 0) lookVals[p[0]] = vals.ToArray();
            }
            Debug.Log($"[OpenZ Intro] look data: {lookVals.Count} values, panel overrides {string.Join(", ", panelLook.Keys)}");
        }
        float LookF(string k, float d) => lookVals.TryGetValue(k, out var a) && a.Length > 0 ? a[0] : d;
        Vector3 LookV3(string k, Vector3 d) => lookVals.TryGetValue(k, out var a) && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : d;
        Color LookC(string k, Color d) => lookVals.TryGetValue(k, out var a) && a.Length >= 3 ? new Color(a[0] / 255f, a[1] / 255f, a[2] / 255f, 1f) : d;
        float PL(string id, string k, float d) => id != null && panelLook.TryGetValue(id, out var m) && m.TryGetValue(k, out var v) ? v : d;

        void InitLookTargets()
        {
            var vol = FindFirstObjectByType<Volume>();
            if (vol && vol.sharedProfile) { var prof = vol.profile; if (prof.TryGet(out colAdj)) { exposure0 = colAdj.postExposure.value; contrast0 = colAdj.contrast.value; } }
            ambientProbe0 = RenderSettings.ambientProbe;
            // run-time instances (the assets stay untouched): the sky panorama and the rocket-fuel can material (piles, mounds, held can)
            if (RenderSettings.skybox && RenderSettings.skybox.HasProperty("_Exposure"))
            { skyRun = new Material(RenderSettings.skybox) { name = RenderSettings.skybox.name + "_Run" }; sky0 = skyRun.GetFloat("_Exposure"); RenderSettings.skybox = skyRun; }
            if (canMat && canMat.HasProperty("_BaseColor")) { canRun = new Material(canMat) { name = canMat.name + "_Run" }; can0 = canRun.GetColor("_BaseColor"); canMat = canRun; }
            if (shuttle)
            {
                foreach (var l in shuttle.GetComponentsInChildren<Light>(true)) if (l.name == "IntroCabinFill") { cabinFill = l; cabinFill0 = l.intensity; }
                // the hull: one run-time instance of M_Intro_Shuttle for the whole shuttle (the asset stays untouched)
                foreach (var r in shuttle.GetComponentsInChildren<Renderer>(true))
                {
                    var ms = r.sharedMaterials; bool ch = false;
                    for (int i = 0; i < ms.Length; i++)
                        if (ms[i] && ms[i].name == "M_Intro_Shuttle")
                        {
                            if (!hullMat) { hullMat = new Material(ms[i]) { name = "M_Intro_Shuttle_Run" }; hull0 = hullMat.GetColor("_BaseColor"); }
                            ms[i] = hullMat; ch = true;
                        }
                    if (ch) r.sharedMaterials = ms;
                }
            }
            lookReady = true;
        }

        void ApplyPanelLook(IntroPanel d)
        {
            if (!lookReady) return;
            string id = d.id; float sk = PL(id, "sun", 1f);
            if (sunBig) sunBig.intensity *= sk; if (sunSmall) sunSmall.intensity *= sk;                    // on top of GoLive's space scale
            if (cabinFill) cabinFill.intensity = cabinFill0 * PL(id, "cabin", 1f);
            if (hullMat) { float hk = PL(id, "hull", 1f); hullMat.SetColor("_BaseColor", new Color(hull0.r * hk, hull0.g * hk, hull0.b * hk, hull0.a)); }
            if (colAdj) { colAdj.postExposure.value = exposure0 + PL(id, "exposure", 0f); colAdj.contrast.value = contrast0 + PL(id, "contrast", 0f); }
            var sh = ambientProbe0; sh *= PL(id, "ambient", 1f); RenderSettings.ambientProbe = sh;
            if (skyRun) skyRun.SetFloat("_Exposure", sky0 * PL(id, "sky", 1f));
            if (canRun) { float ck = PL(id, "cans", 1f); canRun.SetColor("_BaseColor", new Color(can0.r * ck, can0.g * ck, can0.b * ck, can0.a)); }
        }

        // ---------------------------------------------------------------- item 9: P01A's foreground spill of big hero cans (x5, lying, 23-34 m out)
        GameObject heroCans;
        void BuildHeroCans()
        {
            IntroPanel pa = null; foreach (var p in all) if (p.d.sunDiscs != null && p.d.sunDiscs.Length > 0 && p.d.hasCam) { pa = p.d; break; }
            if (pa == null || canMeshes == null || canMeshes.Length == 0) return;
            heroCans = new GameObject("HeroCansP01A"); heroCans.transform.SetParent(transform, false);
            var ground = setRoot ? setRoot.GetComponentInChildren<MeshFilter>(true) : null; MeshCollider gc = null;
            foreach (var mf in setRoot ? setRoot.GetComponentsInChildren<MeshFilter>(true) : new MeshFilter[0]) if (mf.name.Contains("Ground")) { ground = mf; break; }
            if (ground) { var g = new GameObject("Q_Ground"); g.transform.SetParent(ground.transform, false); g.layer = 2; gc = g.AddComponent<MeshCollider>(); gc.sharedMesh = ground.sharedMesh; }
            Physics.SyncTransforms();
            Vector3 cp = V3(pa.c0.pos), cf = V3(pa.c0.fwd); cf.y = 0; cf.Normalize(); var cr = Vector3.Cross(Vector3.up, cf);
            var rnd = new System.Random(18); float R() => (float)rnd.NextDouble();
            var bsz = canMeshes[0].bounds.size; Vector3 ax = bsz.x >= bsz.y && bsz.x >= bsz.z ? Vector3.right : bsz.y >= bsz.z ? Vector3.up : Vector3.forward;
            float radius = 0.5f * Mathf.Min(bsz.x, Mathf.Min(bsz.y, bsz.z)) * 5f;
            float halfW = Mathf.Tan(pa.c0.fov * 0.5f * Mathf.Deg2Rad * 1.7f);
            for (int i = 0; i < 18; i++)
            {
                float dist = 23f + R() * 11f, side = (R() * 2f - 1f) * halfW * dist * 0.85f;
                var at = cp + cf * dist + cr * side; float gy = 0f;
                if (gc && gc.Raycast(new Ray(new Vector3(at.x, 50f, at.z), Vector3.down), out var h, 100f)) gy = h.point.y;
                var lieDir = Quaternion.Euler(0, R() * 360f, 0) * Vector3.forward;
                var rot = Quaternion.AngleAxis(R() * 360f, lieDir) * Quaternion.FromToRotation(ax, lieDir);
                var go = new GameObject("HeroCan"); go.transform.SetParent(heroCans.transform, false);
                go.transform.SetPositionAndRotation(new Vector3(at.x, gy + radius - 0.06f, at.z), rot); go.transform.localScale = Vector3.one * 5f;
                go.AddComponent<MeshFilter>().sharedMesh = canMeshes[i % canMeshes.Length];
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = canMat; mr.shadowCastingMode = ShadowCastingMode.On;
            }
            heroCans.SetActive(false);
        }

        float sunBigI = 1f, sunSmallI = 0.45f;
        void OnEnable() { if (sunBig) sunBigI = sunBig.intensity; if (sunSmall) sunSmallI = sunSmall.intensity; }

        // ---------------------------------------------------------------- the frame
        void Update()
        {
            if (!started)
            {
                if (videoMode)
                {
                    if (videoError) { Debug.LogWarning("[OpenZ Intro] video failed: main menu"); leaving = true; skipped = true; leaveAt = Time.unscaledTime; }
                    else if (!vp || !vp.isPrepared) return;
                    else
                    {
                        vp.Play(); started = true;
                        Debug.Log($"[OpenZ Intro] video start (frame {Time.frameCount}), sound = the video's track via VideoPlayer -> AudioSource (muxed {AudioLeadS * 1000f:0} ms early)");
                    }
                }
                else
                {
                    if (warm < 0) return;
                    if (--warm > 0 || (!recording && Time.unscaledDeltaTime > 0.1f)) { if (warm < 1) warm = 1; return; }      // wait for two smooth frames
                    if (!recording) src.Play();
                    clockStart = Time.unscaledTime; started = true;
                    Debug.Log($"[OpenZ Intro] {(recording ? "pre-render" : "mix")} + timeline start (frame {Time.frameCount})");
                }
            }
            float t = Clock();
            frameDt = Mathf.Clamp(t - lastT, 0f, 0.1f); lastT = t;
            HandleSkip(t);
            if (leaving) { Leave(); return; }
            if (videoMode)
            {
                if (started && (videoEnded || t >= tl.total + 0.05f)) { Debug.Log($"[OpenZ Intro] video end at {t:0.00} s: main menu"); leaving = true; leaveAt = Time.unscaledTime; }
                return;
            }

            // cards
            float c0End = tl.pages.Length > 1 ? tl.pages[1].t0 : 6.2f;
            cardC0.gameObject.SetActive(t < c0End + TurnDur);                                                // the first page slides in over the cold open
            if (t < c0End + TurnDur) UpdateC0(t);
            var c9 = tl.pages[tl.pages.Length - 1];
            bool inC9 = t >= c9.t0;
            cardC9.gameObject.SetActive(inC9);
            if (inC9) UpdateC9(t - c9.t0);
            // pages: a page shows from its turn start until the next page has landed
            for (int i = 0; i < pages.Count; i++)
            {
                // item 6 + 42: the page slides in over [t0, t0 + 0.45] with a small settle, the outgoing page is pushed 35 % left under
                // it and stays drawn until the new one has landed
                var pg = pages[i]; float next = i + 1 < pages.Count ? pages[i + 1].d.t0 : c9.t0; float end = next + TurnDur;
                bool vis = t >= pg.d.t0 && t < end;
                if (pg.rt.gameObject.activeSelf != vis) pg.rt.gameObject.SetActive(vis);
                if (!vis) continue;
                float uin = (t - pg.d.t0) / TurnDur, x = 0f;
                if (uin < 1f) x = 1920f * (1f - EaseOut(uin)) - 14f * Mathf.Sin(Mathf.Clamp01(uin) * Mathf.PI);
                else if (uin < 1.5f) x = -10f * Mathf.Sin((uin - 1f) * 2f * Mathf.PI) * (1.5f - uin) * 2f;        // settle
                if (t >= next && i + 1 < pages.Count) x = -0.35f * 1920f * EaseOut((t - next) / TurnDur);       // pushed left under the new page
                pg.rt.anchoredPosition = new Vector2(x, 0);
                pg.rt.SetAsLastSibling();
            }
            // panels
            PanelUi want = null;
            foreach (var p in all) if (t >= p.show) want = p;
            if (want != null && want != live) GoLive(want);
            foreach (var p in all)
            {
                bool on = t >= p.show; if (p.rt.gameObject.activeSelf != on) p.rt.gameObject.SetActive(on);
                if (!on) continue;
                float pt = t - p.d.t0;
                // transitions
                float sc = 1f; Vector2 off = Vector2.zero;
                if (p.d.trans == "pop") { float u = Mathf.Clamp01(pt / 0.22f); sc = u < 0.55f ? Mathf.Lerp(0.92f, 1.03f, u / 0.55f) : Mathf.Lerp(1.03f, 1f, (u - 0.55f) / 0.45f); }
                if (p.d.trans == "slide") off = p.slideFrom * (1f - EaseOut(pt / 0.3f));
                p.rt.localScale = Vector3.one * sc; p.rt.anchoredPosition = p.centroid + off;
                foreach (var s in p.stickers)
                {
                    float st = pt - s.d.at; bool vis = st >= 0f;
                    if (s.rt.gameObject.activeSelf != vis) s.rt.gameObject.SetActive(vis);
                    if (!vis) continue;
                    float u = Mathf.Clamp01(st / 0.16f);
                    float k = u < 0.5f ? Mathf.Lerp(0.3f, 1.25f, u / 0.5f) : Mathf.Lerp(1.25f, 1f, (u - 0.5f) / 0.5f);
                    s.rt.localScale = Vector3.one * k;
                }
            }
            if (live != null)
            {
                float pt = Mathf.Max(0f, t - live.d.t0);
                LiveEvents(live.d, pt);
                foreach (var a in live.d.actors) PlaceActor(a, pt);
                ApplyShuttle(live.d, pt);
                if (live.cam) ApplyCam(live, pt);
                if (live.d.space) UpdateCloud(pt, live.d);
                if (live.cam && live.d.render == "3d" && !live.d.space) DrawCans(live.cam, live.d.mound3);
                if (live.d.space && spaceCans) { int k = 0; foreach (Transform c in spaceCans.transform) c.Rotate(spaceSpin[k++] * frameDt, Space.Self); }
                if (live.camW) { live.camW.enabled = CutoutDiffMatte && live.cam.enabled; live.mat.SetFloat("_Matte", CutoutDiffMatte ? 1f : 0f); }
            }
#if UNITY_EDITOR
            if (recording) { if (t >= tl.total + 0.05f && !recDone) FinishRecording(); return; }
#endif
            if (t >= tl.total + 0.05f) { Debug.Log($"[OpenZ Intro] end at {t:0.00} s: main menu (the mix rings {mix.length - t:0.00} s into it)"); leaving = true; leaveAt = Time.unscaledTime; }
        }

        // ---------------------------------------------------------------- live events keyed to the cue sheet
        float burstAt = -1f, burstT0 = -1f; bool coughDone, ignited, blasted; float wakeRise = -1f;
        void LiveEvents(IntroPanel d, float pt)
        {
            // item 13: the can burst on Alan's first rising frame (pushed by his body: kinematic capsules on chest, arms, helmet)
            if (burstAt >= 0f && !pileABurst && pt >= burstAt) { BurstPile(); burstT0 = Time.unscaledTime; }
            // item 28: the spill settles, then freezes where it lies for every later desert panel
            if (pileABurst && burstT0 > 0f && Time.unscaledTime - burstT0 > 3.5f) { foreach (var rb in pileA) if (rb && !rb.isKinematic) rb.isKinematic = true; burstT0 = -1f; }
            if (d.id != "P07A") return;
            // item 17: the cough (black puff), the ignition (flames + dust under every jet), the blast (cans blown away in a ring)
            float cough = d.Cue("shuttle_engine_start", 0, 0f), ign = d.Cue("shuttle_engine_start", 1, 0.48f), blast = d.Cue("cans_blast_away", 0, 0.7f);
            if (!coughDone && pt >= cough) { coughDone = true; Cough(); }
            if (!ignited && pt >= ign) { ignited = true; foreach (var j in jets) j.gameObject.SetActive(true); ignT = pt; }
            if (ignited) JetDust(pt - ign);
            if (!blasted && pt >= blast) { blasted = true; BlastCans(); }
        }
        float ignT = -1f;

        /// <summary>Panel time where ic_wake_burst lifts his chest 4 cm off the pile (measured once from the clip itself).</summary>
        float WakeRiseTime()
        {
            if (wakeRise >= 0f) return wakeRise;
            wakeRise = 0.25f;
            if (!alan || !clipsA.TryGetValue("ic_wake_burst", out var c)) return wakeRise;
            var chest = Bone(alan, "mixamorigSpine2") ?? Bone(alan, "mixamorigSpine1"); if (!chest) return wakeRise;
            c.SampleAnimation(alan, 0f); float y0 = alan.transform.InverseTransformPoint(chest.position).y;
            for (float x = 0f; x < Mathf.Min(1.2f, c.length); x += 1f / 60f)
            {
                c.SampleAnimation(alan, x);
                if (alan.transform.InverseTransformPoint(chest.position).y - y0 > 0.04f) { wakeRise = x; break; }
            }
            Debug.Log($"[OpenZ Intro] P01C can burst keyed to Alan's rise at {wakeRise:0.000} s into the panel");
            return wakeRise;
        }

        ParticleSystem dustPs; Material dustMat;
        void BuildDust()
        {
            var psh = Shader.Find("Universal Render Pipeline/Particles/Simple Lit"); if (!psh) { Debug.LogWarning("[OpenZ Intro] particle shader missing: no lift-off dust"); return; }
            var go = new GameObject("IntroDust"); go.transform.SetParent(transform, false);
            dustPs = go.AddComponent<ParticleSystem>();
            var main = dustPs.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.playOnAwake = false; main.maxParticles = 1500;
            main.startLifetime = 2f; main.startSpeed = 0f; main.loop = false; main.scalingMode = ParticleSystemScalingMode.Hierarchy; main.gravityModifier = -0.02f;
            var em = dustPs.emission; em.enabled = false;
            var sh = dustPs.shape; sh.enabled = false;
            var sz = dustPs.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.45f), new Keyframe(0.35f, 0.85f), new Keyframe(1, 1.25f)));
            var col = dustPs.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.08f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var vl = dustPs.limitVelocityOverLifetime; vl.enabled = true; vl.dampen = 0.08f; vl.limit = 0.4f;
            var rot = dustPs.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            dustMat = new Material(psh) { name = "M_Intro_Dust" };
            dustMat.SetFloat("_Surface", 1f); dustMat.SetFloat("_Blend", 0f); dustMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            dustMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); dustMat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); dustMat.SetFloat("_ZWrite", 0f);
            dustMat.SetFloat("_SoftParticlesEnabled", 1f); dustMat.EnableKeyword("_SOFTPARTICLES_ON"); dustMat.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1.2f, 0, 0));
            dustMat.renderQueue = (int)RenderQueue.Transparent; dustMat.SetTexture("_BaseMap", PuffTex()); dustMat.SetColor("_BaseColor", Color.white);
            r.sharedMaterial = dustMat; r.renderMode = ParticleSystemRenderMode.Billboard; r.sortMode = ParticleSystemSortMode.Distance; r.shadowCastingMode = ShadowCastingMode.Off;
            BuildSand(SandPuffTex());
        }

        /// <summary>The sand billow: a dense cauliflower puff (opaque core to ~60 % radius, fbm-eroded rim) with a lighter sunlit top and a
        /// darker underside, so the burst reads as solid rolling sand (the animatic), never a thin haze.</summary>
        static Texture2D SandPuffTex()
        {
            const int N = 256; var tex = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { name = "IntroSandPuff", wrapMode = TextureWrapMode.Clamp };   // linear: the shade is used as is
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f, r = Mathf.Sqrt(u * u + v * v);
                    float n = 0f, amp = 0.5f, f = 3f;
                    for (int o = 0; o < 4; o++) { n += amp * Mathf.PerlinNoise(u * f + 5.7f * o + 3f, v * f + 9.3f * o + 1f); amp *= 0.5f; f *= 2.1f; }
                    float a = Mathf.Clamp01(1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.98f, r + (n - 0.47f) * 0.45f)));
                    float lit = Mathf.Clamp01(0.5f + 0.5f * v + (n - 0.47f) * 0.8f);                               // top lit, bottom in its own shade
                    byte sh = (byte)Mathf.RoundToInt(Mathf.Lerp(200f, 255f, lit));                                          // 0.78-1.0: warm sand, never olive
                    px[y * N + x] = new Color32(sh, sh, sh, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply(true, true); return tex;
        }

        // sun cannot bleach them to a pale haze on the bright sky, seated above the sand (the old puffs sat half under it and faded out)
        ParticleSystem sandPs;
        void BuildSand(Texture2D puff)
        {
            var psh = Shader.Find("Universal Render Pipeline/Particles/Unlit"); if (!psh) { Debug.LogWarning("[OpenZ Intro] particle unlit shader missing: no sand burst"); return; }
            var go = new GameObject("IntroSandBurst"); go.transform.SetParent(transform, false);
            sandPs = go.AddComponent<ParticleSystem>();
            var main = sandPs.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.playOnAwake = false; main.maxParticles = 2500;
            main.startLifetime = 3f; main.startSpeed = 0f; main.loop = false; main.scalingMode = ParticleSystemScalingMode.Hierarchy; main.gravityModifier = 0f;
            var em = sandPs.emission; em.enabled = false; var sh = sandPs.shape; sh.enabled = false;
            var sz = sandPs.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(0.3f, 0.92f), new Keyframe(1, 1.3f)));
            var col = sandPs.colorOverLifetime; col.enabled = true; var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(0.94f, 0.94f, 0.94f), 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.05f), new GradientAlphaKey(0.95f, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var vl = sandPs.limitVelocityOverLifetime; vl.enabled = true; vl.dampen = 0.1f; vl.limit = 0.5f;
            var rot = sandPs.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            var m = new Material(psh) { name = "M_Intro_SandBurst" };
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SoftParticlesEnabled", 1f); m.EnableKeyword("_SOFTPARTICLES_ON"); m.SetVector("_SoftParticleFadeParams", new Vector4(0f, 0.35f, 0, 0));
            m.renderQueue = (int)RenderQueue.Transparent; m.SetTexture("_BaseMap", puff); m.SetColor("_BaseColor", Color.white);
            r.sharedMaterial = m; r.renderMode = ParticleSystemRenderMode.Billboard; r.sortMode = ParticleSystemSortMode.Distance; r.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>A soft cauliflower puff (fbm-eroded radial falloff, no outline), 256 px, alpha only.</summary>
        static Texture2D PuffTex()
        {
            const int N = 256; var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "IntroPuff", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f, r = Mathf.Sqrt(u * u + v * v);
                    float n = 0f, amp = 0.5f, f = 3f;
                    for (int o = 0; o < 4; o++) { n += amp * Mathf.PerlinNoise(u * f + 11.3f * o, v * f + 7.1f * o); amp *= 0.5f; f *= 2.1f; }
                    float a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.35f, 1f, r + (n - 0.47f) * 0.55f));
                    byte sh = (byte)(215 + 40 * Mathf.Clamp01(1f - v * 0.5f - 0.3f));                                   // a little self-shading, lighter on top
                    px[y * N + x] = new Color32(sh, sh, sh, (byte)(a * a * 255));
                }
            tex.SetPixels32(px); tex.Apply(true, true); return tex;
        }

        void EmitDust(Vector3 at, Vector3 vel, float size, float life, Color c)
        {
            if (!dustPs) return;
            var ep = new ParticleSystem.EmitParams { position = at, velocity = vel, startSize = size, startLifetime = life, startColor = c, rotation = Random.Range(0f, 360f) };
            dustPs.Emit(ep, 1);
        }

        void Cough()
        {
            Transform sp = null; if (shuttle) foreach (var t in shuttle.GetComponentsInChildren<Transform>(true)) if (t.name == "SmokePuff") sp = t;
            var at = sp ? sp.position : (shuttle ? shuttle.transform.position + Vector3.up * 2f : Vector3.zero);
            for (int i = 0; i < 14; i++)
                EmitDust(at + Random.insideUnitSphere * 0.25f, Random.insideUnitSphere * 0.6f + Vector3.up * Random.Range(0.8f, 1.8f), Random.Range(0.6f, 1.3f), Random.Range(1.4f, 2.2f), new Color(0.13f, 0.12f, 0.115f, 0.85f));
        }

        float dustAcc;
        void JetDust(float since)
        {
            var sand = LookC("sandColor", new Color32(222, 176, 112, 255));
            int n = since < 0.05f && !dustRing ? Mathf.RoundToInt(LookF("sandBurstN", 34f)) : 0; if (n > 0) dustRing = true;
            dustAcc += frameDt * LookF("sandRate", 30f) * Mathf.Lerp(1f, 0.5f, Mathf.Clamp01(since / 1.5f)); int extra = Mathf.FloorToInt(dustAcc); dustAcc -= extra;
            float s0 = LookF("sandSizeMin", 1.8f), s1 = LookF("sandSizeMax", 3.6f), v0 = LookF("sandSpeedMin", 3.5f), v1 = LookF("sandSpeedMax", 9f);
            foreach (var j in jets)
            {
                if (!j.parent || !j.parent.name.StartsWith("LiftJet_")) continue;
                var p = j.parent.position; var ground = new Vector3(p.x, 0f, p.z);
                for (int i = 0; i < n + extra; i++)
                {
                    bool b = i < n; var dir = Random.insideUnitCircle.normalized; float sp = Random.Range(v0, v1) * (b ? 1f : 0.55f);
                    float size = Random.Range(s0, s1) * (b ? 1f : 0.8f), k = Random.Range(0.84f, 1.04f);
                    var c = new Color(sand.r * k, sand.g * k, sand.b * k, 1f);
                    var at = ground + new Vector3(dir.x, 0, dir.y) * Random.Range(0.2f, 1.3f) + Vector3.up * (size * LookF("sandLift", 0.25f));
                    var vel = new Vector3(dir.x * sp, Random.Range(LookF("sandUpMin", 0.2f), LookF("sandUpMax", 0.8f)), dir.y * sp);
                    if (sandPs) sandPs.Emit(new ParticleSystem.EmitParams { position = at, velocity = vel, startSize = size, startLifetime = Random.Range(2.2f, 3.4f), startColor = c, rotation = Random.Range(-20f, 20f) }, 1);   // lit top stays up
                    else EmitDust(at, vel, size, Random.Range(1.6f, 2.6f), c);
                }
            }
        }
        bool dustRing;

        void UpdateCloud(float pt, IntroPanel d)
        {
            var s = d.spaceData; if (!cloud) return;
            bool on = pt >= s.cloudAt; cloud.gameObject.SetActive(on); if (!on) return;
            cloud.position = V3(s.cloudBase); cloud.rotation = Quaternion.FromToRotation(Vector3.up, V3(s.cloudUp));
            float u = pt; float k = u < 0.78f ? Mathf.Lerp(0.05f, 0.8f, (u - 0.7f) / 0.08f) : u < 0.9f ? Mathf.Lerp(0.8f, 1.08f, (u - 0.78f) / 0.12f) : Mathf.Lerp(1.08f, 1f, Mathf.Clamp01((u - 0.9f) / 0.15f));
            cloud.localScale = Vector3.one * Mathf.Max(0.05f, k);
            cloudFlash.gameObject.SetActive(pt < 1.0f);
            float r = Mathf.Clamp01((pt - s.cloudAt) / 0.6f);
            if (live.cam) cloudRing.rotation = Quaternion.LookRotation(live.cam.transform.position - cloudRing.position);
            cloudRing.localScale = Vector3.one * Mathf.Lerp(0.12f, 0.72f, r) / Mathf.Max(0.05f, k);
            cloudRing.gameObject.SetActive(r < 1f);
        }

        // ---------------------------------------------------------------- typewriter cards
        static string Typed(string s, float t, float at, float cps) { if (t < at) return ""; int n = Mathf.Clamp(Mathf.FloorToInt((t - at) * cps), 0, s.Length); return s.Substring(0, n); }
        static string Caret(float t) => Mathf.Repeat(t, 0.6f) < 0.36f ? "█" : " ";

        void UpdateC0(float t)
        {
            var cap = tl.pages[0].panels[0].captions;
            string l1 = cap.Length > 0 ? cap[0] : "", l2 = cap.Length > 1 ? cap[1] : "", l3 = cap.Length > 2 ? cap[2] : "";
            string a = Typed(l1, t, 0.15f, 18), b = Typed(l2, t, 2.88f, 18), c = Typed(l3, t, 4.1f, 17);
            float wipe = Mathf.Clamp01((t - 4.05f) / 0.25f);
            c0l1.text = a + (t < 2.88f && t >= 0.15f ? Caret(t) : ""); c0l2.text = b + (t >= 2.88f && t < 4.05f ? Caret(t) : "");
            var col = (Color)OzUi.Amber; col.a = 1f - wipe; c0l1.color = c0l2.color = col;
            c0l1.rectTransform.anchoredPosition = new Vector2(-840 - wipe * 400, 120); c0l2.rectTransform.anchoredPosition = new Vector2(-840 - wipe * 400, 40);
            c0l3.text = t >= 4.1f ? c + Caret(t) : "";
            var fadeIn = Mathf.Clamp01(t / 0.3f); c0l1.canvasRenderer.SetAlpha(fadeIn); c0l2.canvasRenderer.SetAlpha(fadeIn);
        }

        // ---------------------------------------------------------------- the 3D end title (fix-list item 18; staging intro_end_title_v1)
        // A modelled OPENZ sign (traced letters, 0.24 m deep, back plate, hinge on the bottom-BACK edge) on its own layer in a sub-scene
        // 2 km below the set, seen by a perspective camera into a render texture under the typed captions (the logo never crosses
        // them). Fly-in + slam (punch-in, shake, dust), three real bullet holes swapped in on the rifle cracks (flash, HDR sparks, mesh
        // chips bouncing on the floor, a BANG! with overshoot), the creak, a real backwards fall on the hinge that shows the thickness
        // and the back (crane to the landed view), soft dust on the land frame, the 4 deg bounce; the floor takes the sign's shadow.
        public const int EndLayer = 29;
        [Header("End title (3D)")] public GameObject endTitle; public Mesh[] chipMeshes; public Material chipMat;
        Transform endRoot, endHinge; readonly GameObject[] endPlugs = new GameObject[3], endHoles = new GameObject[3];
        Camera c9Cam; RenderTexture c9Rt; RawImage c9Raw; Light keyL, flashL; ParticleSystem sparkPs, chipPs; bool c9Lit; int shots3d; bool slam3d, land3d;
        float punchAt = -9f, punchK; readonly List<RectTransform> bangs = new List<RectTransform>(); readonly List<float> bangAt = new List<float>();
        static readonly Vector3 HoldPos = new Vector3(-4.776f, 5.905f, -22.468f), HoldTgt = new Vector3(0f, 1.44f, 0f), LandPos = new Vector3(-3.924f, 10.106f, -17.362f), LandTgt = new Vector3(0f, 0.9f, 1.1f);
        static readonly Vector2[] BangUv = { new Vector2(0.22f, 0.34f), new Vector2(0.80f, 0.34f), new Vector2(0.50f, 0.86f) };

        void BuildEndTitle()
        {
            if (!endTitle) return;
            foreach (var t in endTitle.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "OPENZ_Logo3D_Root") endRoot = t;
                else if (t.name == "OPENZ_Logo3D_Hinge") endHinge = t;
                for (int i = 1; i <= 3; i++)
                {
                    if (t.name == "OPENZ_Logo3D_HolePlug_" + i) endPlugs[i - 1] = t.gameObject;
                    if (t.name == "OPENZ_Logo3D_BulletHole_" + i) endHoles[i - 1] = t.gameObject;
                }
            }
            if (!endHinge) { Debug.LogWarning("[OpenZ Intro] end title: OPENZ_Logo3D_Hinge missing, 2D title kept"); endRoot = null; return; }
            if (!endRoot) endRoot = endHinge.parent;
            for (int i = 0; i < 3; i++) { if (endPlugs[i]) endPlugs[i].SetActive(true); if (endHoles[i]) endHoles[i].SetActive(false); }   // never both on (coplanar rings)
            endRoot.gameObject.SetActive(false);
            var stage = endTitle.transform;
            // camera -> RT -> a RawImage under the captions
            var cg = new GameObject("Cam_C9"); cg.transform.SetParent(transform, false);
            c9Cam = cg.AddComponent<Camera>(); c9Cam.enabled = false; c9Cam.cullingMask = 1 << EndLayer; c9Cam.clearFlags = CameraClearFlags.SolidColor;
            c9Cam.backgroundColor = Color.black; c9Cam.fieldOfView = 22.9f; c9Cam.nearClipPlane = 0.1f; c9Cam.farClipPlane = 300f; c9Cam.allowMSAA = true;
            int w = Mathf.RoundToInt(1920f * pageScale), h = Mathf.RoundToInt(1080f * pageScale);
            c9Rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "RT_C9", antiAliasing = 4 }; c9Rt.Create(); c9Cam.targetTexture = c9Rt; c9Cam.aspect = 16f / 9f;
            var ad = cg.AddComponent<UniversalAdditionalCameraData>(); ad.renderPostProcessing = true; ad.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; ad.antialiasingQuality = AntialiasingQuality.High;
            var rg = new GameObject("C9_3D", typeof(RectTransform)); rg.layer = UiLayer; rg.transform.SetParent(cardC9, false); rg.transform.SetSiblingIndex(0);
            c9Raw = rg.AddComponent<RawImage>(); c9Raw.texture = c9Rt; c9Raw.raycastTarget = false; c9Raw.rectTransform.sizeDelta = new Vector2(1920, 1080); c9Raw.color = new Color(1, 1, 1, 0);
            SetCam(0f);
            // lights (c9_lights.json): warm key with soft shadows, cool rim from behind-right, low fill; only the end-title layer
            Light L(string n, Vector3 f, Color c, float i, bool sh)
            {
                var go = new GameObject(n); go.transform.SetParent(stage, false); go.transform.rotation = Quaternion.LookRotation(f, Vector3.up);
                var l = go.AddComponent<Light>(); l.type = LightType.Directional; l.color = c; l.intensity = i; l.cullingMask = 1 << EndLayer;
                l.shadows = sh ? LightShadows.Soft : LightShadows.None; l.shadowStrength = 0.85f; go.AddComponent<UniversalAdditionalLightData>(); l.enabled = false; return l;
            }
            keyL = L("C9_Key", new Vector3(0.3319f, -0.2419f, 0.9118f), new Color(1f, 0.95f, 0.88f), 0.907f * 2.2f, true);
            rimL = L("C9_Rim", new Vector3(0.1447f, -0.829f, -0.5401f), new Color(0.75f, 0.84f, 1f), 0.764f * 2.2f, false);
            fillL = L("C9_Fill", new Vector3(-0.5064f, -0.4695f, 0.7233f), new Color(0.8f, 0.84f, 0.95f), 0.446f * 2.2f, false);
            var fg = new GameObject("C9_Flash"); fg.transform.SetParent(stage, false); flashL = fg.AddComponent<Light>(); flashL.type = LightType.Point; flashL.range = 3.5f;
            flashL.color = new Color(1f, 0.86f, 0.6f); flashL.intensity = 0f; flashL.cullingMask = 1 << EndLayer; flashL.shadows = LightShadows.None;
            // sparks (HDR, stretched, gravity) + chips (the modelled flakes as mesh particles, bouncing on the floor plane)
            var ush = Shader.Find("Universal Render Pipeline/Particles/Unlit"); if (!ush) { Debug.LogWarning("[OpenZ Intro] end title: particle shader missing, 2D title kept"); endRoot = null; return; }
            var sm = new Material(ush) { name = "M_Intro_Sparks" };
            sm.SetFloat("_Surface", 1f); sm.SetFloat("_Blend", 2f); sm.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); sm.EnableKeyword("_BLENDMODE_ADD");
            sm.SetFloat("_SrcBlend", (float)BlendMode.One); sm.SetFloat("_DstBlend", (float)BlendMode.One); sm.SetFloat("_ZWrite", 0f); sm.renderQueue = (int)RenderQueue.Transparent;
            sm.SetTexture("_BaseMap", SoftDot()); sm.SetColor("_BaseColor", new Color(5f, 2.6f, 0.9f, 1f));
            sparkPs = MakePs("C9_Sparks", stage, sm, ParticleSystemRenderMode.Stretch, 1.4f, 400);
            var sr = sparkPs.GetComponent<ParticleSystemRenderer>(); sr.velocityScale = 0.035f; sr.lengthScale = 1.2f;
            if (chipMeshes != null && chipMeshes.Length > 0 && chipMat)
            {
                chipPs = MakePs("C9_Chips", stage, chipMat, ParticleSystemRenderMode.Mesh, 1f, 120);
                var cr = chipPs.GetComponent<ParticleSystemRenderer>(); cr.SetMeshes(chipMeshes); cr.alignment = ParticleSystemRenderSpace.Local; cr.shadowCastingMode = ShadowCastingMode.On;
                var col = chipPs.collision; col.enabled = true; col.type = ParticleSystemCollisionType.Planes; col.bounce = 0.3f; col.dampen = 0.45f; col.lifetimeLoss = 0f;
                var plane = new GameObject("C9_FloorPlane").transform; plane.SetParent(stage, false); col.SetPlane(0, plane);
                var main = chipPs.main; main.startRotation3D = true;
                var rot = chipPs.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true; rot.x = new ParticleSystem.MinMaxCurve(-8f, 8f); rot.y = new ParticleSystem.MinMaxCurve(-8f, 8f); rot.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
            }
            foreach (var t in stage.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = EndLayer;
            if (logoHinge) logoHinge.gameObject.SetActive(false); if (floorLine) floorLine.gameObject.SetActive(false);
            Debug.Log($"[OpenZ Intro] end title 3D: sign {endRoot.name}, hinge, plugs {System.Array.FindAll(endPlugs, x => x).Length}, holes {System.Array.FindAll(endHoles, x => x).Length}, chips {(chipPs ? chipMeshes.Length : 0)}, RT {w}x{h}");
        }
        Light rimL, fillL;

        ParticleSystem MakePs(string n, Transform parent, Material m, ParticleSystemRenderMode mode, float gravity, int max)
        {
            var go = new GameObject(n); go.transform.SetParent(parent, false); go.layer = EndLayer;
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.playOnAwake = false; main.loop = false; main.maxParticles = max; main.gravityModifier = gravity; main.startSpeed = 0f;
            var em = ps.emission; em.enabled = false; var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.renderMode = mode; r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        static Texture2D softDot;
        static Texture2D SoftDot()
        {
            if (softDot) return softDot; const int N = 64; softDot = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "IntroSoftDot", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) { float u = (x + 0.5f) / N * 2 - 1, v = (y + 0.5f) / N * 2 - 1; float a = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v)); a = a * a; softDot.SetPixel(x, y, new Color(1, 1, 1, a)); }
            softDot.Apply(true, true); return softDot;
        }

        void SetCam(float crane)
        {
            if (!c9Cam || !endTitle) return; var o = endTitle.transform.position; float k = EaseInOut(crane);
            var p = Vector3.Lerp(HoldPos, LandPos, k); var tg = Vector3.Lerp(HoldTgt, LandTgt, k);
            c9Cam.transform.SetPositionAndRotation(o + p, Quaternion.LookRotation(tg - p, Vector3.up));
        }

        void End3D(float u, float fly0, float slam, float tip, float land, float creak, float dtA)
        {
            var e = tl.ending;
            // the 3D page shows only once the comic page under the card is fully black (no floor / sign over the fading P07)
            float show = Mathf.Clamp01((u - 0.32f) / 0.25f); c9Raw.color = new Color(1, 1, 1, show);
            if (u >= 0.32f && !c9Lit)
            {
                c9Lit = true; c9Cam.enabled = true; if (live != null && live.cam) live.cam.enabled = false;
                if (sunBig) sunBig.enabled = false; if (sunSmall) sunSmall.enabled = false;
                keyL.enabled = rimL.enabled = fillL.enabled = true; RenderSettings.sun = keyL; RenderSettings.fog = false; RenderSettings.ambientIntensity = 0.45f;
                if (dustPs) { dustPs.gameObject.layer = EndLayer; dustPs.Clear(); }
            }
            // fly-in from behind the camera's side, ease-in, then the slam with a short squash
            bool vis = u >= fly0; if (endRoot.gameObject.activeSelf != vis) endRoot.gameObject.SetActive(vis);
            var basePos = Vector3.zero; float sy = 1f;
            if (vis && u < slam) basePos = Vector3.Lerp(new Vector3(0f, 1.2f, -14f), Vector3.zero, EaseIn((u - fly0) / Mathf.Max(0.01f, slam - fly0)));
            else if (vis && u < slam + 0.12f) sy = Mathf.Lerp(0.96f, 1f, (u - slam) / 0.12f);
            if (vis && u >= slam && !slam3d)
            {
                slam3d = true; Punch(u, 0.09f);
                for (int i = 0; i < 26; i++) { float x = Random.Range(-4.3f, 4.3f); EmitDust(endTitle.transform.position + new Vector3(x, 0.08f, Random.Range(-0.25f, 0.1f)), new Vector3(Mathf.Sign(x) * Random.Range(0.4f, 1.6f), Random.Range(0.3f, 1.0f), Random.Range(-1.4f, -0.3f)), Random.Range(0.7f, 1.6f), Random.Range(1.1f, 1.8f), new Color(0.62f, 0.52f, 0.42f, 0.75f)); }
            }
            float shake = vis && u >= slam && u < slam + 0.25f ? 0.06f * (1f - (u - slam) / 0.25f) : 0f;
            // the three shots: plug off + hole on, flash, sparks, chips, jolt, BANG! (the cracks are in the mix on these frames)
            Vector3 jolt = Vector3.zero;
            for (int i = 0; i < e.holes.Length && i < 3; i++)
            {
                float at = e.holes[i].at + dtA;
                if (u >= at && shots3d <= i) { shots3d = i + 1; Shot3D(i, u); }
                if (u >= at && u < at + 0.08f) { jolt += new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0) * 0.025f; shake = Mathf.Max(shake, 0.025f); }
            }
            if (flashL) flashL.intensity = Mathf.Max(0f, flashL.intensity - frameDt * 90f);
            endRoot.localPosition = basePos + jolt; endRoot.localScale = new Vector3(1f, sy, 1f);
            // the creak, the backwards fall on the bottom-back hinge, the land (dust, clang shake), the 4 deg bounce
            float ang = 0f;
            if (u >= tip) { float x = Mathf.Clamp01((u - tip) / (land - tip)); ang = 90f * x * x; }
            else if (u >= creak) ang = Mathf.Sin((u - creak) * 60f) * 0.6f;
            if (u >= land)
            {
                float b = u - land; ang = 90f - 4f * Mathf.Sin(Mathf.Clamp01(b / 0.25f) * Mathf.PI);
                if (!land3d)
                {
                    land3d = true; Punch(u, 0.05f);
                    var o = endTitle.transform.position;
                    for (int i = 0; i < 48; i++) { var p = o + new Vector3(Random.Range(-4.3f, 4.3f), 0.1f, Random.Range(0f, 2.4f) * (Random.value < 0.5f ? 1f : 0.96f)); if (Random.value < 0.4f) p.z = o.z + Random.Range(2.1f, 2.45f); var d = new Vector3(p.x - o.x, 0, p.z - o.z - 1.1f).normalized; EmitDust(p, d * Random.Range(0.8f, 2.4f) + Vector3.up * Random.Range(0.4f, 1.3f), Random.Range(0.9f, 2.0f), Random.Range(1.3f, 2.1f), new Color(0.6f, 0.5f, 0.4f, 0.8f)); }
                }
                if (b < 0.3f) shake = Mathf.Max(shake, 0.05f * (1f - b / 0.3f));
            }
            endHinge.localEulerAngles = new Vector3(ang, 0, 0);
            // dim the rim once the sign lies back (it would mirror straight into the camera and burn the cream face)
            if (rimL) rimL.intensity = 0.764f * 2.2f * (1f - Mathf.Clamp01((ang - 55f) / 30f) * 0.85f);
            // camera: hold, crane to the landed view from the tip, punch-ins, shake
            SetCam(u < tip ? 0f : Mathf.Clamp01((u - tip) / Mathf.Max(0.05f, land + 0.15f - tip)));
            float pu = u - punchAt; c9Cam.fieldOfView = 22.9f * (1f - (pu >= 0f && pu < 0.45f ? punchK * (pu < 0.05f ? pu / 0.05f : 1f - EaseOut((pu - 0.05f) / 0.4f)) : 0f));
            if (shake > 0f) c9Cam.transform.position += c9Cam.transform.right * Random.Range(-shake, shake) + c9Cam.transform.up * Random.Range(-shake, shake);
            // the BANG! stickers pop with overshoot and clear before the fall
            for (int i = 0; i < bangs.Count; i++)
            {
                float st = u - bangAt[i]; float k = st < 0.16f ? (st < 0.08f ? Mathf.Lerp(0.3f, 1.25f, st / 0.08f) : Mathf.Lerp(1.25f, 1f, (st - 0.08f) / 0.08f)) : 1f;
                bangs[i].localScale = Vector3.one * k; var cg = bangs[i].GetComponent<CanvasGroup>(); cg.alpha = u < tip ? 1f : 1f - Mathf.Clamp01((u - tip) / 0.2f);
            }
            UpdateSparks();
        }

        void Punch(float u, float k) { punchAt = u; punchK = k; }

        void Shot3D(int i, float u)
        {
            if (endPlugs[i]) endPlugs[i].SetActive(false); if (endHoles[i]) endHoles[i].SetActive(true);
            var hole = endHoles[i] ? endHoles[i].transform : endRoot; var p = hole.position; var n = endRoot.TransformDirection(Vector3.back);
            if (flashL) { flashL.transform.position = p + n * 0.25f; flashL.intensity = 9f; }
            for (int k = 0; k < 28; k++)
            {
                var v = (n + Random.insideUnitSphere * 0.85f).normalized * Random.Range(4f, 10f);
                sparkPs.Emit(new ParticleSystem.EmitParams { position = p + n * 0.02f, velocity = v, startSize = Random.Range(0.025f, 0.05f), startLifetime = Random.Range(0.18f, 0.45f), startColor = Color.white }, 1);
            }
            if (chipPs)
                for (int k = 0; k < 9; k++)
                {
                    var v = (n * 1.4f + Random.insideUnitSphere + Vector3.up * 0.9f).normalized * Random.Range(2f, 4.5f);
                    chipPs.Emit(new ParticleSystem.EmitParams { position = p + n * 0.03f, velocity = v, startSize = Random.Range(0.8f, 1.3f), startLifetime = Random.Range(1.2f, 1.8f), startColor = Color.white, rotation3D = Random.insideUnitSphere * 180f }, 1);
                }
            Punch(u, 0.035f);
            // BANG! (comic sticker with ink outline + hard shadow) at the storyboard's page spot for this crack
            var uv = BangUv[Mathf.Clamp(i, 0, BangUv.Length - 1)];
            var rt = Rt(cardC9, "Bang" + (i + 1), Vector2.zero); rt.anchoredPosition = Page(uv.x * 1920f, uv.y * 1080f); rt.localEulerAngles = new Vector3(0, 0, i == 1 ? 9f : -8f);
            rt.gameObject.AddComponent<CanvasGroup>();
            float R = 96f; var star = OzComicPoly.Star(Vector2.zero, R, R * 0.62f, 13, 0f, 1234 + i);
            var sh = Poly(rt, "Shadow", ink); sh.Set(star, new Rect(0, 0, 1, 1), 6f, true); sh.rectTransform.anchoredPosition = new Vector2(8, -8);
            Poly(rt, "Ink", ink).Set(star, new Rect(0, 0, 1, 1), 6f, true);
            Poly(rt, "Fill", FillCol("yellow")).Set(star, new Rect(0, 0, 1, 1));
            var t = Tmp(rt, "BANG!", 60f, tl.Pal("red", Color.red), false); t.fontStyle = FontStyles.Bold;
            bangs.Add(rt); bangAt.Add(u);
        }

        int shotsDone; bool landed, slammed;
        void UpdateC9(float u)
        {
            var e = tl.ending; var T = e.times;
            float dtA = endCaptionAnniversary ? 0f : -T.dt_anniversary;
            string cap = endCaptionNoOpenSource ? e.captionAlt : e.caption;
            c9l1.text = Typed(cap, u, T.type, T.cps) + (u >= T.type && (u < T.type2 || !endCaptionAnniversary) ? Caret(u) : "");
            c9l2.text = endCaptionAnniversary ? Typed(e.caption2, u, T.type2, T.cps) + (u >= T.type2 ? Caret(u) : "") : "";
            float bgA = Mathf.Clamp01(u / 0.3f); cardC9.GetComponent<Image>().color = new Color(0, 0, 0, bgA);
            float fly0 = T.fly0 + dtA, slam = T.slam + dtA, tip = T.tip + dtA, land = T.land + dtA;
            if (endRoot)
            {
                End3D(u, fly0, slam, tip, land, T.creak + dtA, dtA);
                float fa = T.fade + dtA;
                if (u >= fa) fade.GetComponent<Image>().color = new Color(0, 0, 0, Mathf.Clamp01((u - fa) / 0.3f));
                return;
            }
            Vector2 shake = Vector2.zero;
            if (u < fly0) logoHinge.gameObject.SetActive(false);
            else
            {
                logoHinge.gameObject.SetActive(true);
                float k;
                if (u < slam) k = Mathf.Lerp(3.2f, 1f, EaseIn((u - fly0) / (slam - fly0)));
                else k = u < slam + 0.12f ? Mathf.Lerp(0.96f, 1f, (u - slam) / 0.12f) : 1f;
                logoHinge.localScale = Vector3.one * k;
                var raw = logoRt.GetComponent<RawImage>(); raw.color = new Color(1, 1, 1, Mathf.Clamp01((u - fly0) / 0.12f));
                if (u >= slam && !slammed) { slammed = true; Dust(Page(960, 789), 14, 520f); }
                if (u >= slam && u < slam + 0.25f) shake += Random.insideUnitCircle * 14f * (1f - (u - slam) / 0.25f);
            }
            for (int i = 0; i < e.holes.Length; i++)
            {
                float at = e.holes[i].at + dtA; bool on = u >= at; holes[i].gameObject.SetActive(on);
                if (on && shotsDone <= i) { shotsDone = i + 1; Hit(holes[i]); }
                if (on && u < at + 0.07f) shake += new Vector2(Random.Range(-6f, 6f), Random.Range(-6f, 6f));
            }
            float ang = 0f;
            if (u >= tip) { float x = Mathf.Clamp01((u - tip) / (land - tip)); ang = 90f * x * x; }
            if (u >= land) { float b = u - land; ang = 90f - 4f * Mathf.Sin(Mathf.Clamp01(b / 0.25f) * Mathf.PI); if (!landed) { landed = true; Dust(Page(960, 789), 26, 900f); } if (b < 0.3f) shake += Random.insideUnitCircle * 10f * (1f - b / 0.3f); }
            else if (u >= T.creak + dtA) ang += Mathf.Sin((u - T.creak) * 60f) * 0.6f;
            logoHinge.localEulerAngles = new Vector3(ang, 0, 0);
            cardC9.anchoredPosition = shake;
            UpdateSparks();
            float fadeAt = T.fade + dtA;
            if (u >= fadeAt) fade.GetComponent<Image>().color = new Color(0, 0, 0, Mathf.Clamp01((u - fadeAt) / 0.3f));
        }

        void Hit(RectTransform hole)
        {
            var wp = sparkRoot.InverseTransformPoint(hole.position);
            var fl = Poly(sparkRoot, "Flash", Color.white); fl.Set(OzComicPoly.Ellipse(Vector2.zero, 46, 46, 20), new Rect(0, 0, 1, 1)); fl.rectTransform.anchoredPosition = wp;
            sparks.Add(new Spark { rt = fl.rectTransform, v = Vector2.zero, life = 0.05f, img = null });
            for (int i = 0; i < 21; i++)
            {
                bool chip = i >= 16;
                var im = Img(sparkRoot, chip ? "Chip" : "Spark", chip ? new Color(0.55f, 0.53f, 0.5f, 1) : new Color(1f, 0.92f, 0.55f, 1), false);
                im.rectTransform.sizeDelta = chip ? new Vector2(7, 6) : new Vector2(14, 3); im.rectTransform.anchoredPosition = wp;
                var v = Random.insideUnitCircle.normalized * Random.Range(chip ? 180f : 380f, chip ? 420f : 900f); v.y = Mathf.Abs(v.y) * 0.8f + 120f;
                sparks.Add(new Spark { rt = im.rectTransform, v = v, life = chip ? 0.7f : Random.Range(0.25f, 0.5f), img = im });
            }
            logoRt.anchoredPosition = Random.insideUnitCircle.normalized * 6f;
        }

        void Dust(Vector2 at, int n, float width)
        {
            for (int i = 0; i < n; i++)
            {
                // a comic dust puff: a bumpy cloud shape, ink rim + warm pale fill, rolling out and up, growing, fading
                var c = Rt(sparkRoot, "Dust", Vector2.zero); var cg = c.gameObject.AddComponent<CanvasGroup>();
                float r = Random.Range(20f, 48f); var shape = CloudShape(r, r * 0.72f, Random.Range(0f, 6.28f));
                Poly(c, "Ink", ink).Set(shape, new Rect(0, 0, 1, 1), 3.5f, true);
                Poly(c, "Fill", Color.Lerp(new Color(0.93f, 0.86f, 0.74f), new Color(0.8f, 0.72f, 0.6f), Random.value)).Set(shape, new Rect(0, 0, 1, 1));
                float x = Random.Range(-width * 0.5f, width * 0.5f);
                c.anchoredPosition = at + new Vector2(x, Random.Range(-4f, 12f));
                sparks.Add(new Spark { rt = c, v = new Vector2(Mathf.Sign(x) * Random.Range(40f, 140f), Random.Range(25f, 85f)), life = Random.Range(0.75f, 1.05f), dust = true, img = null, cg = cg });
            }
        }

        void UpdateSparks()
        {
            float dt = frameDt;                                                                               // the intro clock (real time, video or the frame-exact pre-render)
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                var s = sparks[i]; s.age += dt;
                if (s.age >= s.life) { Destroy(s.rt.gameObject); sparks.RemoveAt(i); continue; }
                if (!s.dust) s.v += Vector2.down * 1400f * dt; else s.v *= 1f - 1.5f * dt;
                s.rt.anchoredPosition += s.v * dt;
                if (s.img) { s.rt.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(s.v.y, s.v.x) * Mathf.Rad2Deg); var c = s.img.color; c.a = 1f - s.age / s.life; s.img.color = c; }
                else if (s.cg) { float u = s.age / s.life; s.cg.alpha = u < 0.55f ? 1f : 1f - (u - 0.55f) / 0.45f; s.rt.localScale = Vector3.one * (0.7f + s.age * 1.1f); }
                else { var g = s.rt.GetComponent<Graphic>(); var c = g.color; c.a = (s.dust ? 0.5f : 1f) * (1f - s.age / s.life); g.color = c; if (s.dust) s.rt.localScale = Vector3.one * (1f + s.age * 1.2f); }
            }
            if (logoRt) logoRt.anchoredPosition = Vector2.Lerp(logoRt.anchoredPosition, Vector2.zero, 0.3f);
        }

        // ---------------------------------------------------------------- skip / leave
        AudioClip click;
        void HandleSkip(float t)
        {
            if (recording) { skipCg.alpha = 0f; return; }                                                  // the pre-render carries no UI prompt (drawn live over the video)
            float a = Mathf.Clamp01((t - tl.skipAt) / 0.4f);
            if (t > tl.total - 0.4f) a = 0f;
            skipCg.alpha = a * 0.86f;
            if (leaving || t < tl.skipAt) return;
            var kb = Keyboard.current; var ms = Mouse.current;
            bool hit = (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)) || (ms != null && ms.leftButton.wasPressedThisFrame);
            if (!hit) return;
            skipped = true; leaving = true; leaveAt = Time.unscaledTime;
            if (!click) click = Resources.Load<AudioClip>("OpenZ/Audio/Sfx/sd_ui_click_a");
            if (click) AudioSource.PlayClipAtPoint(click, uiCam.transform.position, 0.6f);
            Debug.Log($"[OpenZ Intro] skipped at {t:0.00} s ({(kb != null && kb.spaceKey.wasPressedThisFrame ? "SPACE" : kb != null && kb.escapeKey.wasPressedThisFrame ? "ESC" : "click")})");
        }

        bool loading; float skipVol0 = -1f;
        void Leave()
        {
            float u = Time.unscaledTime - leaveAt;
            if (skipped)
            {
                if (src) { if (skipVol0 < 0f) skipVol0 = src.volume; src.volume = skipVol0 * (1f - Mathf.Clamp01(u / 0.5f)); }
                fade.GetComponent<Image>().color = new Color(0, 0, 0, Mathf.Clamp01(u / 0.5f));
                if (u < 0.5f) return;
            }
            if (loading) return; loading = true;
            if (videoMode)
            {
                if (vp) vp.Stop();
                if (!skipped && mix)
                {
                    // the victory sting rings ~1.5 s into the menu: the mix's tail from where the video's sound stopped
                    var tg = new GameObject("IntroMixTail"); DontDestroyOnLoad(tg); var ts = tg.AddComponent<AudioSource>();
                    ts.clip = mix; ts.spatialBlend = 0f; ts.priority = 0; ts.time = Mathf.Min(mix.length - 0.05f, tl.total + 0.05f + AudioLeadS); ts.Play();
                    Destroy(tg, Mathf.Max(0.1f, mix.length - ts.time + 0.1f));
                }
            }
            else if (!skipped && src && src.isPlaying && src.clip == mix)
            {
                // the victory sting rings ~1.5 s into the menu: keep the mix alive across the load, then drop it
                src.transform.SetParent(null, false); src.gameObject.name = "IntroMixTail"; DontDestroyOnLoad(src.gameObject);
                Destroy(src.gameObject, Mathf.Max(0.1f, mix.length - src.time + 0.1f)); src = null;
            }
            OzIntro.Running = false;
            RestoreLayers();
            SceneManager.LoadScene(OzIntro.MenuScene);
        }

        void OnDestroy()
        {
            OzIntro.Running = false; foreach (var p in all) { if (p.rtTex) p.rtTex.Release(); if (p.rtTexW) p.rtTexW.Release(); }
            if (screenRt) screenRt.Release(); if (videoRt) videoRt.Release();
            Shader.SetGlobalFloat("_AtmoKeepAlpha", 0f); Shader.SetGlobalFloat("_AtmoEnabled", atmoEnabled0);
#if UNITY_EDITOR
            if (ff != null && !recDone) { try { ffIn.Close(); ff.Kill(); } catch { } Time.captureDeltaTime = 0f; }
#endif
        }

        void StartVideo()
        {
            foreach (var go in new[] { alan, bradGo, shuttle, planet, ring }) if (go) go.SetActive(false);
            if (setRoot) setRoot.gameObject.SetActive(false);
            if (sunBig) sunBig.enabled = false; if (sunSmall) sunSmall.enabled = false;
            BuildCanvas();
            videoRt = new RenderTexture((int)videoClip.width, (int)videoClip.height, 0, RenderTextureFormat.ARGB32)
            { name = "RT_IntroVideo", useMipMap = false, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            videoRt.Create();
            var vgo = new GameObject("Video", typeof(RectTransform)); vgo.layer = UiLayer; vgo.transform.SetParent(canvasRoot, false);
            var raw = vgo.AddComponent<RawImage>(); raw.texture = videoRt; raw.raycastTarget = false; raw.color = Color.white;
            raw.rectTransform.sizeDelta = new Vector2(1920, 1080);                                              // exactly the live page (the desk shows round it)
            fade = Img(canvasRoot, "Fade", new Color(0, 0, 0, 0), true).rectTransform;
            BuildSkip(canvasRoot);
            // sound = the video's own track (the reviewed master mix, muxed AudioLeadS early) through VideoPlayer -> AudioSource: Unity keeps
            // in OBS): this route +223 ms (= +86 ms, hence the lead), duplicate scan pass (<= 3 repeated / s in motion, no 3-frame hold).
            // Tried and dropped: the mix on its own AudioSource + a free-running picture (picture ~200 ms ahead), a hard-seek resync (picture
            // froze at 8 s), ExternalTime on the mix clock (10-36 repeated frames / s at the lift-off).
            var ag = new GameObject("IntroVideoAudio"); ag.transform.SetParent(transform, false);
            src = ag.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 0f; src.priority = 0; src.ignoreListenerPause = true; src.volume = 1f;
            vp = gameObject.AddComponent<VideoPlayer>();
            vp.playOnAwake = false; vp.source = VideoSource.VideoClip; vp.clip = videoClip; vp.isLooping = false; vp.waitForFirstFrame = true;
            vp.skipOnDrop = false;
            vp.renderMode = VideoRenderMode.RenderTexture; vp.targetTexture = videoRt; vp.aspectRatio = VideoAspectRatio.FitInside;
            vp.audioOutputMode = VideoAudioOutputMode.AudioSource;
            vp.controlledAudioTrackCount = 1; vp.EnableAudioTrack(0, true); vp.SetTargetAudioSource(0, src);
            vp.loopPointReached += v => videoEnded = true;
            vp.errorReceived += (v, m) => { Debug.LogWarning("[OpenZ Intro] video error: " + m); videoError = true; };
            vp.Prepare();
            if (!FindFirstObjectByType<AudioListener>()) uiCam.gameObject.AddComponent<AudioListener>();
            Debug.Log($"[OpenZ Intro] video path: {videoClip.name} {videoClip.width}x{videoClip.height} {videoClip.frameRate:0.##} fps {videoClip.length:0.00} s, audio tracks {videoClip.audioTrackCount}, screen {Screen.width}x{Screen.height}");
        }

        // ---------------------------------------------------------------- P07C: the big hero cans floating on the ring's near side (storyboard in_shot)
        GameObject spaceCans; readonly List<Vector3> spaceSpin = new List<Vector3>();
        void BuildSpaceCans()
        {
            IntroSpace sd = null; foreach (var p in all) if (p.d.space) { sd = p.d.spaceData; break; }
            if (sd == null || sd.heroCans == null || canMeshes == null || canMeshes.Length == 0 || !canMat) return;
            spaceCans = new GameObject("SpaceHeroCans"); spaceCans.transform.SetParent(transform, false);
            var rnd = new System.Random(5);
            for (int i = 0; i + 2 < sd.heroCans.Length; i += 3)
            {
                var go = new GameObject("HeroCan"); go.transform.SetParent(spaceCans.transform, false);
                go.transform.position = new Vector3(sd.heroCans[i], sd.heroCans[i + 1], sd.heroCans[i + 2]);
                go.transform.rotation = Quaternion.Euler((float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f);
                go.transform.localScale = Vector3.one * sd.heroScale * (0.8f + 0.4f * (float)rnd.NextDouble());
                go.AddComponent<MeshFilter>().sharedMesh = canMeshes[(i / 3) % canMeshes.Length];
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = canMat; mr.shadowCastingMode = ShadowCastingMode.Off;
                spaceSpin.Add(new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f) * 36f);
            }
            spaceCans.SetActive(false);
        }

        // ---------------------------------------------------------------- stars: soft quads (OpenZ/Intro/Star: colour = tint, alpha = spike strength)
        static Mesh StarMesh()
        {
            var rnd = new System.Random(3); const int N = 1100;
            var v = new List<Vector3>(N * 4); var uv = new List<Vector2>(N * 4); var col = new List<Color>(N * 4); var tri = new List<int>(N * 6);
            for (int i = 0; i < N; i++)
            {
                Vector3 d;
                do { d = new Vector3((float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1); } while (d.sqrMagnitude > 1f || d.sqrMagnitude < 0.01f);
                d.Normalize();
                float u = (float)rnd.NextDouble(); bool big = u > 0.965f, mid = u > 0.75f;
                float size = big ? 34f + 30f * (float)rnd.NextDouble() : mid ? 17f + 10f * (float)rnd.NextDouble() : 9f + 7f * (float)rnd.NextDouble();
                float b = big ? 1f : mid ? 0.55f : 0.2f + 0.22f * (float)rnd.NextDouble();
                float tint = (float)rnd.NextDouble();
                var c = tint < 0.2f ? new Color(0.78f, 0.87f, 1f) : tint < 0.35f ? new Color(1f, 0.9f, 0.72f) : Color.white;
                c *= b; c.a = big ? 1f : 0f;
                var right = Vector3.Cross(d, Mathf.Abs(d.y) > 0.95f ? Vector3.right : Vector3.up).normalized; var up = Vector3.Cross(right, d);
                var p = d * 6000f; int k = v.Count;
                v.Add(p + (-right - up) * size); v.Add(p + (right - up) * size); v.Add(p + (right + up) * size); v.Add(p + (-right + up) * size);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                for (int j = 0; j < 4; j++) col.Add(c);
                tri.Add(k); tri.Add(k + 2); tri.Add(k + 1); tri.Add(k); tri.Add(k + 3); tri.Add(k + 2);
            }
            var m = new Mesh { name = "IntroStars" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(col); m.SetTriangles(tri, 0); m.bounds = new Bounds(Vector3.zero, Vector3.one * 20000f);
            return m;
        }

        // ---------------------------------------------------------------- the mushroom cloud mesh: noise-lumped icosphere lobes, 2 submeshes (cool, hot)
        static Mesh MushroomMesh()
        {
            var ico = Ico2(out var icoTris);
            var v = new List<Vector3>(); var tCool = new List<int>(); var tHot = new List<int>(); var rnd = new System.Random(11);
            float R() => (float)rnd.NextDouble();
            void Lobe(Vector3 c, float r, bool hot)
            {
                int b = v.Count; float o1 = R() * 50f, o2 = R() * 50f;
                foreach (var d in ico)
                {
                    float n = Mathf.PerlinNoise(d.x * 1.7f + o1, d.y * 1.7f + o2) * 0.6f + Mathf.PerlinNoise(d.z * 3.1f + o2, d.x * 3.1f + o1) * 0.4f;
                    v.Add(c + d * r * (0.8f + 0.42f * n));
                }
                var list = hot ? tHot : tCool; foreach (var t in icoTris) list.Add(b + t);
            }
            for (int i = 0; i < 10; i++) { float a = i * 36f * Mathf.Deg2Rad + R() * 0.3f, dd = 6.5f + R() * 2.5f; Lobe(new Vector3(Mathf.Cos(a) * dd, 0.6f + R() * 1.2f, Mathf.Sin(a) * dd), 2.8f + R() * 1.6f, i % 3 == 0); }   // base surge
            for (int i = 0; i < 9; i++) Lobe(new Vector3(Mathf.Sin(i * 1.3f) * 0.7f, 1.5f + i * 3.3f, Mathf.Cos(i * 1.1f) * 0.7f), 4.4f - i * 0.12f, i < 3);                                    // stem
            for (int i = 0; i < 9; i++) { float a = i * 40f * Mathf.Deg2Rad; Lobe(new Vector3(Mathf.Cos(a) * 5.4f, 17.5f + R(), Mathf.Sin(a) * 5.4f), 1.9f + R() * 0.6f, false); }        // condensation skirt
            for (int i = 0; i < 10; i++) { float a = (i * 36f + 18f) * Mathf.Deg2Rad; Lobe(new Vector3(Mathf.Cos(a) * 8.5f, 28.2f + R(), Mathf.Sin(a) * 8.5f), 4.6f + R(), true); }     // hot underside
            for (int i = 0; i < 14; i++) { float a = i * (360f / 14f) * Mathf.Deg2Rad; Lobe(new Vector3(Mathf.Cos(a) * 11.8f, 31.5f + Mathf.Sin(a * 3f) * 0.9f, Mathf.Sin(a) * 11.8f), 6f + R() * 1.4f, false); }   // cap torus
            for (int i = 0; i < 9; i++) { float a = R() * 6.283f, dd = R() * 7f; Lobe(new Vector3(Mathf.Cos(a) * dd, 36f + R() * 2.5f, Mathf.Sin(a) * dd), 6.2f + R() * 1.6f, false); }   // dome
            Lobe(new Vector3(0, 40f, 0), 6.5f, false);
            var m = new Mesh { name = "IntroMushroom" };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v); m.subMeshCount = 2; m.SetTriangles(tCool, 0); m.SetTriangles(tHot, 1);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Unit icosphere, 2 subdivisions (162 verts, 320 tris), every face wound outward for Unity (Cross(b-a, c-a) out).</summary>
        static List<Vector3> Ico2(out List<int> tris)
        {
            float g = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3> { new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0), new Vector3(0, -1, g), new Vector3(0, 1, g),
                new Vector3(0, -1, -g), new Vector3(0, 1, -g), new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1) };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            var f = new List<int> { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            for (int s = 0; s < 2; s++)
            {
                var cache = new Dictionary<long, int>(); var nf = new List<int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (cache.TryGetValue(key, out int mi)) return mi;
                    v.Add(((v[a] + v[b]) * 0.5f).normalized); cache[key] = v.Count - 1; return v.Count - 1;
                }
                for (int i = 0; i < f.Count; i += 3)
                {
                    int a = f[i], b = f[i + 1], c = f[i + 2]; int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                f = nf;
            }
            for (int i = 0; i < f.Count; i += 3)
            {
                Vector3 a = v[f[i]], b = v[f[i + 1]], c = v[f[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f) { int t = f[i + 1]; f[i + 1] = f[i + 2]; f[i + 2] = t; }
            }
            tris = f; return v;
        }

        /// <summary>A comic cloud outline (scalloped ellipse, flatter underneath) for the dust puffs.</summary>
        static List<Vector2> CloudShape(float rx, float ry, float phase)
        {
            var pts = new List<Vector2>(); const int n = 36;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n; float bump = 1f + 0.17f * Mathf.Abs(Mathf.Sin(a * 4f + phase));
                float y = Mathf.Sin(a) * ry * bump; if (y < 0) y *= 0.55f;
                pts.Add(new Vector2(Mathf.Cos(a) * rx * bump, y));
            }
            return pts;
        }

#if UNITY_EDITOR
        // ---------------------------------------------------------------- RECORD: the offline pre-render (OpenZ/Intro/Re-record video)
        // Frame-exact: the intro clock = frame / 60 and Time.captureDeltaTime = 1/60 (physics, animation, stickers on the same clock), every
        // finished frame (full look, MSAA + SMAA, render scale 1) piped raw into ffmpeg (page crop -> 2560x1440, x264 CRF 14, bt709),
        // then muxed with the reviewed master mix. No skip prompt in the picture (it is drawn live over the video).
        System.Diagnostics.Process ff; System.IO.Stream ffIn; string recDir; readonly System.Text.StringBuilder ffLog = new System.Text.StringBuilder(); float recT0;

        static string FfmpegExe()
        {
            var env = System.Environment.GetEnvironmentVariable("OPENZ_FFMPEG"); if (!string.IsNullOrEmpty(env) && System.IO.File.Exists(env)) return env;
            foreach (var dir in (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator))
            { try { var p = System.IO.Path.Combine(dir.Trim('"'), "ffmpeg.exe"); if (System.IO.File.Exists(p)) return p; } catch { } }
            var la = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            var link = System.IO.Path.Combine(la, "Microsoft", "WinGet", "Links", "ffmpeg.exe"); if (System.IO.File.Exists(link)) return link;
            var pk = System.IO.Path.Combine(la, "Microsoft", "WinGet", "Packages");
            if (System.IO.Directory.Exists(pk))
                foreach (var d in System.IO.Directory.GetDirectories(pk, "*FFmpeg*"))
                    foreach (var f in System.IO.Directory.GetFiles(d, "ffmpeg.exe", System.IO.SearchOption.AllDirectories)) return f;
            return "ffmpeg";
        }

        void StartRecording()
        {
            Time.captureDeltaTime = 1f / 60f;
            recDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Recordings", "intro"));
            System.IO.Directory.CreateDirectory(recDir);
            recT0 = Time.realtimeSinceStartup;
            StartCoroutine(CaptureLoop());
            Debug.Log($"[OpenZ Intro] RECORD: frame-exact 60 fps pre-render of the live intro -> {recDir}");
        }

        IEnumerator CaptureLoop()
        {
            var eof = new WaitForEndOfFrame();
            while (!recDone)
            {
                yield return eof;
                if (!started || recDone) continue;
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                try
                {
                    if (ff == null) OpenFfmpeg(tex.width, tex.height, tex.format);
                    var data = tex.GetRawTextureData();
                    ffIn.Write(data, 0, data.Length);
                    if (recFrame == 1200 || recFrame == 3000) System.IO.File.WriteAllBytes(System.IO.Path.Combine(recDir, "check_f" + recFrame + ".png"), tex.EncodeToPNG());
                }
                catch (System.Exception e) { Debug.LogError("[OpenZ Intro] RECORD failed at frame " + recFrame + ": " + e.Message + "\n" + ffLog); recDone = true; Time.captureDeltaTime = 0f; }
                Destroy(tex);
                recFrame++;
                if (recFrame % 600 == 0) Debug.Log($"[OpenZ Intro] RECORD frame {recFrame} ({recFrame / 60f:0.0} of {tl.total:0.00} s), {Time.realtimeSinceStartup - recT0:0} s real");
            }
        }

        void OpenFfmpeg(int w, int h, TextureFormat fmt)
        {
            string pix = fmt == TextureFormat.RGBA32 ? "rgba" : fmt == TextureFormat.ARGB32 ? "argb" : fmt == TextureFormat.BGRA32 ? "bgra" : fmt == TextureFormat.RGB24 ? "rgb24" : "rgba";
            int pw = Mathf.RoundToInt(1920f * pageScale / 2f) * 2, ph = Mathf.RoundToInt(1080f * pageScale / 2f) * 2;
            pw = Mathf.Min(pw, w - (w & 1)); ph = Mathf.Min(ph, h - (h & 1));
            int x = (w - pw) / 2, y = (h - ph) / 2;
            string vf = $"vflip,crop={pw}:{ph}:{x}:{y},scale=2560:1440:flags=lanczos:out_color_matrix=bt709:out_range=tv,format=yuv420p";
            string outV = System.IO.Path.Combine(recDir, "openz_intro_video.mp4");
            var psi = new System.Diagnostics.ProcessStartInfo(FfmpegExe(),
                $"-y -hide_banner -loglevel error -f rawvideo -pix_fmt {pix} -s {w}x{h} -r 60 -i - -vf \"{vf}\" -c:v libx264 -preset medium -crf 14 -profile:v high -bf 0 -g 120 " +
                $"-colorspace bt709 -color_primaries bt709 -color_trc bt709 -movflags +faststart \"{outV}\"")
            { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
            ff = System.Diagnostics.Process.Start(psi);
            ff.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (ffLog) ffLog.AppendLine(e.Data); };
            ff.OutputDataReceived += (s, e) => { };
            ff.BeginErrorReadLine(); ff.BeginOutputReadLine();
            ffIn = ff.StandardInput.BaseStream;
            Debug.Log($"[OpenZ Intro] RECORD: ffmpeg {w}x{h} {fmt} ({pix}) -> crop {pw}x{ph}+{x}+{y} -> 2560x1440 60 fps x264 CRF 14 -> {outV}");
        }

        void FinishRecording()
        {
            recDone = true; Time.captureDeltaTime = 0f;
            // "hold": keep the take in Recordings\intro without the .ready marker (no auto-import into Resources until it passed its checks)
            string after = UnityEditor.EditorPrefs.GetString(AfterRecordPref, ""); bool hold = after == "hold";
            int frames = recFrame; float real = Time.realtimeSinceStartup - recT0; int code = -1, code2 = -1; string log2 = "";
            try { ffIn.Flush(); ffIn.Close(); ff.WaitForExit(); code = ff.ExitCode; } catch (System.Exception e) { Debug.LogError("[OpenZ Intro] RECORD close: " + e.Message); }
            string outV = System.IO.Path.Combine(recDir, "openz_intro_video.mp4"), outF = System.IO.Path.Combine(recDir, "openz_intro.mp4");
            string wav = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", UnityEditor.AssetDatabase.GetAssetPath(mix)));
            float dur = frames / 60f;
            if (code == 0)
            {
                var psi = new System.Diagnostics.ProcessStartInfo(FfmpegExe(),
                    $"-y -hide_banner -loglevel error -i \"{outV}\" -i \"{wav}\" -map 0:v:0 -map 1:a:0 -c:v copy -bsf:v h264_metadata=colour_primaries=1:transfer_characteristics=1:matrix_coefficients=1 " +
                    $"-af atrim=start={AudioLeadS.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)},asetpts=PTS-STARTPTS -c:a aac -b:a 256k -ar 48000 " +
                    $"-t {dur.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)} -movflags +faststart \"{outF}\"")
                { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
                using (var p = System.Diagnostics.Process.Start(psi)) { log2 = p.StandardError.ReadToEnd(); p.WaitForExit(); code2 = p.ExitCode; }
                if (code2 == 0 && !hold) System.IO.File.WriteAllText(outF + ".ready", System.DateTime.Now.ToString("o"));
            }
            if (hold) { UnityEditor.EditorPrefs.DeleteKey(AfterRecordPref); Debug.Log($"[OpenZ Intro] RECORD held (no auto-import): {outF}, {frames} frames in {real:0} s real, video exit {code}, mux exit {code2} {(code2 == 0 ? "" : ffLog + log2)}"); UnityEditor.EditorApplication.isPlaying = false; return; }
            Debug.Log($"[OpenZ Intro] RECORD done: {frames} frames ({dur:0.00} s) in {real:0} s real; video exit {code}, mux exit {code2}: " +
                      (code2 == 0 ? outF + " (copied to Assets/OpenZ/Resources/OpenZ/Intro on Play exit)" : "FAILED " + ffLog + log2));
            // "stay": import it now and keep Play running (the as-shipped video check follows in the same session); else stop Play
            bool stay = UnityEditor.EditorPrefs.GetString(AfterRecordPref, "") == "stay"; UnityEditor.EditorPrefs.DeleteKey(AfterRecordPref);
            if (stay && code2 == 0)
            {
                const string dst = "Assets/OpenZ/Resources/OpenZ/Intro/openz_intro.mp4";
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dst));
                System.IO.File.Copy(outF, dst, true); System.IO.File.Delete(outF + ".ready");
                UnityEditor.AssetDatabase.ImportAsset(dst, UnityEditor.ImportAssetOptions.ForceUpdate);
                Debug.Log($"[OpenZ Intro] RECORD: imported {dst} in Play ({new System.IO.FileInfo(dst).Length / 1048576f:0.0} MB); Play stays on");
                return;
            }
            UnityEditor.EditorApplication.isPlaying = false;
        }
#endif

#if UNITY_EDITOR
        // ---------------------------------------------------------------- edit-mode probe (look gate and visual checks without a Play entry)
        /// <summary>Edit mode only: poses the set for panel <paramref name="id"/> at panel time <paramref name="pt"/> exactly like GoLive /
        /// Update do (look data, rake, actors, shuttle, camera life, instanced cans; P07A replays its cue events and particles at 60 fps), renders
        /// the panel camera at its shipped size (2560 x 1440 page) with the full look and writes a PNG. It leaves the scene dirty: reopen the
        /// scene without saving afterwards.</summary>
        public string EditProbe(string id, float pt, string file)
        {
            if (Application.isPlaying) return "edit mode only";
            if (tl == null) ProbeInit();
            PanelUi p = null; foreach (var q in all) if (q.d.id == id) p = q;
            if (p == null || !p.d.hasCam) return "no camera panel " + id;
            var d = p.d; var r = d.rect; int w = Mathf.RoundToInt(r[2] * pageScale), h = Mathf.RoundToInt(r[3] * pageScale);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; rt.Create();
            var cg = new GameObject("ProbeCam_" + id); p.cam = cg.AddComponent<Camera>(); p.cam.enabled = false; p.cam.targetTexture = rt; p.cam.aspect = r[2] / r[3];
            p.cam.nearClipPlane = 0.05f; p.cam.farClipPlane = d.space ? 9000f : 3000f; p.cam.allowMSAA = true;
            var ad = cg.AddComponent<UniversalAdditionalCameraData>();
            if (d.render == "cutout") { p.cam.cullingMask = 1 << CutLayer; p.cam.clearFlags = CameraClearFlags.SolidColor; p.cam.backgroundColor = new Color(0.27f, 0.67f, 0.38f, 1); p.cam.allowHDR = false; ad.renderPostProcessing = false; }
            else
            {
                p.cam.cullingMask = ~((1 << CutLayer) | (1 << ScreenLayer) | (1 << UiLayer) | (1 << EndLayer));
                if (d.space) { p.cam.clearFlags = CameraClearFlags.SolidColor; p.cam.backgroundColor = new Color(0.004f, 0.005f, 0.012f, 1); } else p.cam.clearFlags = CameraClearFlags.Skybox;
                ad.renderPostProcessing = true; ad.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; ad.antialiasingQuality = AntialiasingQuality.High;
            }
            live = null; coughDone = ignited = blasted = dustRing = false; dustAcc = 0f;
            if (dustPs) dustPs.Clear(); if (sandPs) sandPs.Clear();
            GoLive(p); p.cam.enabled = false;
            const float step = 1f / 60f;
            if (d.id == "P07A")
                for (float t = 0f; t <= pt + 1e-4f; t += step)
                {
                    frameDt = step; LiveEvents(d, t);
                    if (dustPs) dustPs.Simulate(step, true, false, false); if (sandPs) sandPs.Simulate(step, true, false, false);
                }
            foreach (var a in d.actors) PlaceActor(a, pt);
            ApplyShuttle(d, pt); ApplyCam(p, pt);
            if (d.render == "3d" && !d.space) DrawCans(p.cam, d.mound3);
            p.cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = prev;
            System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
            DestroyImmediate(tex); p.cam.targetTexture = null; rt.Release(); DestroyImmediate(rt); DestroyImmediate(cg); p.cam = null;
            RestoreLayers();
            return $"{id} @{pt:0.00}: {w}x{h} -> {file}";
        }

        void ProbeInit()
        {
            tl = JsonUtility.FromJson<IntroTimeline>(timelineJson.text); set = JsonUtility.FromJson<IntroSet>(setJson.text);
            clipsA.Clear(); clipsB.Clear(); foreach (var c in alanClips) if (c) clipsA[c.name] = c; foreach (var c in bradClips) if (c) clipsB[c.name] = c;
            ink = tl.Pal("ink", ink);
            if (alan) baseRot[alan] = Quaternion.Inverse(Quaternion.Euler(0, set.alanYaw, 0)) * alan.transform.rotation;
            if (bradGo) baseRot[bradGo] = Quaternion.Inverse(Quaternion.Euler(0, set.bradYaw, 0)) * bradGo.transform.rotation;
            if (shuttle) shBase = Quaternion.Inverse(Quaternion.Euler(0, set.shuttleYaw, 0)) * shuttle.transform.rotation;
            fog0 = RenderSettings.fog; ambient0 = RenderSettings.ambientIntensity;
            if (sunBig) { sunRot0 = sunBig.transform.rotation; sunBigI = sunBig.intensity; } if (sunSmall) { sunSmallRot0 = sunSmall.transform.rotation; sunSmallI = sunSmall.intensity; }
            pageScale = 1440f / 1080f;
            all.Clear(); int idx = 0;
            foreach (var pg in tl.pages) { if (pg.kind != "comic_page") continue; foreach (var pd in pg.panels) all.Add(new PanelUi { d = pd, index = idx++ }); }
            CacheShuttle(); BuildShuttleColliders(); LoadLook(); InitLookTargets();
            BuildPiles(); BuildMoundInstances(); BuildFx(); BuildDust(); BuildHeroCans();
        }
#endif

        // ---------------------------------------------------------------- tiny UI helpers
        static RectTransform Rt(RectTransform parent, string name, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = parent ? parent.gameObject.layer : UiLayer;
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false); rt.sizeDelta = size; return rt;
        }
        static Image Img(RectTransform parent, string name, Color c, bool stretch)
        {
            var rt = Rt(parent, name, new Vector2(100, 100)); var im = rt.gameObject.AddComponent<Image>(); im.color = c; im.raycastTarget = false;
            if (stretch) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(-400, -400); rt.offsetMax = new Vector2(400, 400); }
            return im;
        }
        static OzComicPoly Poly(RectTransform parent, string name, Color c)
        {
            var rt = Rt(parent, name, Vector2.zero); var p = rt.gameObject.AddComponent<OzComicPoly>(); p.color = c; p.raycastTarget = false; return p;
        }
    }
}
