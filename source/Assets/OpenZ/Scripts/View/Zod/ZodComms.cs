// OpenZ (GPL-3.0) - Commander Zod cameo: the comms window (UI_STYLE_GUIDE: a gunmetal "md" plate with the olive header band and
// amber tab, a recessed well with the screen finish: scanlines, glass sheen, corner brackets) showing ZodStage's live feed,
// the subtitles (Inter Tight via OzText, sentence case on a plate: text never sits on the raw world), a signal meter that
// follows Zod's jaw, the 30th-anniversary stamp for the menu greeting, and the briefing letterbox (MISSION BRIEFING bar with the
// skip keycap + subtitle bar). Own Screen Space Overlay canvas, no GraphicRaycaster and no HUD blocker: it never takes a click.
// Motion on unscaled time with the kit tokens; reduced motion = fades only.
// static, sync tearing (horizontal bands of the picture slid sideways), an RGB colour split, a rolling bar, brighter scanlines
// and a dimming flicker. The window opens with a burst, closes with one (then the CRT-off fold), and ZodBriefing fires a few
// mid-line (zod_beats.json fx "glitch"); small signal hiccups come by themselves every 3-6 s. Reduced motion: static only.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    public enum ZodSubMode { None, Below, Right }

    /// <summary>Window placement and parts. x, y = top-left on the 1920 x 1080 reference canvas; the anchor keeps the distance to
    /// its edge on other widths (OzRect rule). scale shrinks the whole window around its top-left corner.</summary>
    public struct ZodLayout
    {
        public OzAnchor anchor; public float x, y, scale;
        public float wellW, wellH, textW;
        public ZodSubMode sub; public bool footer, badge, rivets;
        public int sorting, slide;                      // slide: -1 in from the left, +1 from the right, 0 fade
        public string eyebrow, channel;

        public const float Pad = 10f, Header = 30f, Gap = 8f, FooterH = 24f, SubH = 66f, BadgeH = 36f;
        public float W => Pad * 2f + wellW + (sub == ZodSubMode.Right ? 16f + textW : 0f);
        public float H => Header + Gap + wellH + (footer ? Gap + FooterH : 0f) + (sub == ZodSubMode.Below ? Gap + SubH : 0f) + (badge ? Gap + BadgeH : 0f) + Pad;

        /// <summary>Level start: big feed top-left under the letterbox bar; the subtitles go to the bottom bar.</summary>
        public static ZodLayout Briefing() => new ZodLayout
        {
            anchor = OzAnchor.TL, x = 48f, y = 92f, scale = 1f, wellW = 400f, wellH = 300f, sub = ZodSubMode.None, footer = true, rivets = true,
            sorting = 103, slide = -1, eyebrow = "INCOMING TRANSMISSION", channel = "HQ · CH 01"
        };
        /// <summary>In game (idle barb): compact, left side between the stored-cannon plates (bottom 164 u, 205 u at UI scale 125 %)
        /// and the factory list (top 698 u, 602 u at 125 %). The windows keep the reference layout whatever the HUD scale.</summary>
        public static ZodLayout InGame()
        {   // ARRANGE INTERFACE: the player may have put the transmission window elsewhere (OzHudLayout, group Comms); never moved = the standard place
            var l = InGameDefault(); var at = OzHudLayout.CommsPlace(l.x, l.y, l.W * l.scale, l.H * l.scale);
            l.x = at.x; l.y = at.y;
            return l;
        }
        /// <summary>The in-game window at its standard place (ARRANGE INTERFACE reads the place and the size from here).</summary>
        public static ZodLayout InGameDefault() => new ZodLayout
        {
            anchor = OzAnchor.TL, x = 16f, y = 212f, scale = 1f, wellW = 280f, wellH = 210f, sub = ZodSubMode.Below,
            sorting = 102, slide = -1, eyebrow = "COMMS · HQ", channel = "CH 01"
        };
        /// <summary>Over the end screen: bottom-left beside the stat plates / chart (976 u row centred); narrow canvases shrink it
        /// under the chart, left of the buttons.</summary>
        public static ZodLayout EndScreen(float cw)
        {
            var l = new ZodLayout { anchor = OzAnchor.BL, scale = 1f, wellW = 300f, wellH = 225f, sub = ZodSubMode.Below, sorting = 112, slide = -1, eyebrow = "COMMS · HQ", channel = "CH 01" };
            float free = (cw - OzLayout.EndChartW) * 0.5f - 36f;                                           // gutter left of the stat row / chart
            if (free < l.W && free >= l.W * 0.7f) l.scale = free / l.W;
            else if (free < l.W * 0.7f)
            {   // 4:3 and narrower: under the score strip (bottom y 864; the chart above it ends at 786) and left of the buttons (x from CW / 2 - 330): room 182 u, scale 0.52
                float roomW = cw * 0.5f - 330f - 40f, roomH = OzLayout.RefH - 20f - (OzLayout.EndScoreY + OzLayout.EndScoreH + 14f);
                l.scale = Mathf.Clamp(Mathf.Min(roomW / l.W, roomH / l.H), 0.5f, 1f);
            }
            l.x = 20f; l.y = OzLayout.RefH - 20f - l.H * l.scale;
            return l;
        }
        /// <summary>Main menu greeting: right side above the flyby caption plate (CW - 420, 954), with the anniversary stamp.</summary>
        public static ZodLayout Menu()
        {
            var l = new ZodLayout { anchor = OzAnchor.BR, scale = 1f, wellW = 336f, wellH = 252f, sub = ZodSubMode.Below, badge = true, sorting = 101, slide = 1, eyebrow = "INCOMING TRANSMISSION", channel = "HQ · CH 01" };
            l.x = OzLayout.RefW - 60f - l.W; l.y = 954f - 18f - l.H;
            return l;
        }
        /// <summary>Level select quip: a strip in the gap between the cards (bottom 792) and the deploy bar (962).</summary>
        public static ZodLayout Quip() => new ZodLayout
        {
            anchor = OzAnchor.BL, x = 120f, y = 804f, scale = 1f, wellW = 136f, wellH = 102f, textW = 340f, sub = ZodSubMode.Right,
            sorting = 101, slide = -1, eyebrow = "COMMS · HQ", channel = "CH 01"
        };
    }

    public sealed class ZodComms
    {
        public bool IsOpen => state == State.Opening || state == State.Open;
        public bool Visible => state != State.Closed;
        public bool Closed => state == State.Closed;
        public ZodLayout Layout => lay;
        public Vector2Int FeedPixels { get; private set; }

        enum State { Closed, Opening, Open, Closing }
        State state = State.Closed; float stateAt; ZodLayout lay;
        readonly Canvas canvas; readonly CanvasScaler scaler; readonly RectTransform canvasRt;
        RectTransform win, screen, reveal, feedRt, bandRt, badgeRt, badgeShine; CanvasGroup winCg, badgeCg;
        RawImage feed, noise; Image dot, flashLine, band, noSignal, badgeFlash; OzText eyebrow, channel, subSpeaker, subBody;
        readonly List<Image> vu = new List<Image>(); readonly float[] vuLevel = new float[5];
        Texture feedTex; string cue, cueShown; float cueAt = -10f, badgeAt = -100f, nextGlitch;
        static Texture2D[] noiseTex;
        // v2 signal bursts
        RawImage splitR, splitC, scan; readonly List<RawImage> tears = new List<RawImage>();
        float burstFrom = -10f, burstUntil = -10f, burstK, nextTear; float[] tearShift = new float[5];
        /// <summary>v2: current signal-burst strength 0..1 (Play check / sound sync).</summary>
        public float Burst { get; private set; }

        // letterbox (briefing)
        RectTransform lbTop, lbBottom, lbProgress; CanvasGroup lbCg; OzText lbLeft, lbLeft2, lbCue, lbSpeaker; RectTransform lbSkip;
        // briefing's joke every plate this mission reads GENERAL ZOD and every speaker label GEN ZOD (reset on each level load)
        public static bool General;
        /// <summary>level 16: who holds the transmission instead of the commander (null = he does). Set before the window opens.</summary>
        public static string Host;
        public static string SpeakerName => !string.IsNullOrEmpty(Host) ? Host : General ? "GEN ZOD" : "CMDR ZOD";
        OzText nameLabel; Vector2 namePos;
        public RectTransform Window => win;
        public OzText NameLabel => nameLabel;
        /// <summary>Caps centre-left of the "COMMANDER ZOD" plate, in the window's top-left units.</summary>
        public Vector2 NamePos => namePos;
        public void ApplyRank() { subSpeaker?.Set(SpeakerName); lbSpeaker?.Set(SpeakerName); if (unitLabel != null && unitLabel.graphic) unitLabel.graphic.enabled = string.IsNullOrEmpty(Host); }
        OzText unitLabel;                                                                                // the plate's right half (the commander's unit): not shown while somebody else holds the window
        bool lbOn; float lbAt = -100f, progress;
        /// <summary>level 16 endings: the bars draw the subtitle themselves, whatever layout the last transmission window had (after an
        /// in-game transmission that layout has its own subtitle place and the bars' text was never drawn). Set it after
        /// Letterbox(true, ...); it falls back to false by itself when the bars have slid out after Letterbox(false).</summary>
        public bool LetterboxCue;
        /// <summary>level 16 endings: who speaks, on the bars' speaker label (null or empty = the commander's rank name again).</summary>
        public void LetterboxSpeaker(string name) { lbSpeaker?.Set(string.IsNullOrEmpty(name) ? SpeakerName : name); }
        /// <summary>Play checks: the text and the speaker label of the lower bar as they stand now.</summary>
        public string LetterboxCueText => lbCue != null ? lbCue.text : "";
        public string LetterboxSpeakerText => lbSpeaker != null ? lbSpeaker.text : "";

        public ZodComms(Transform owner)
        {
            var go = new GameObject("ZodComms", typeof(RectTransform)) { layer = 5 };
            go.transform.SetParent(owner, false);
            canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 103; canvas.pixelPerfect = false;
            scaler = go.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = 1f; scaler.referencePixelsPerUnit = 100f;
            scaler.referenceResolution = new Vector2(OzLayout.RefW, OzLayout.RefH);
            canvasRt = (RectTransform)go.transform;
            if (noiseTex == null) { noiseTex = new Texture2D[4]; for (int i = 0; i < 4; i++) noiseTex[i] = OzKit.GetTexture("Textures/tex_noise_" + i); }
        }

        /// <summary>Canvas scale factor (1 = the 1920 x 1080 reference layout, which every Zod window uses).</summary>
        public void SetUiScale(float s) { scaler.referenceResolution = new Vector2(OzLayout.RefW, OzLayout.RefH) / Mathf.Clamp(s, 0.9f, 1.25f); }
        public float CanvasWidth => Screen.height > 0 ? Screen.width * scaler.referenceResolution.y / Screen.height : OzLayout.RefW;
        float K => Screen.height / Mathf.Max(1f, scaler.referenceResolution.y);                             // physical px per canvas unit

        // ============================================================================================================ window
        /// <summary>Opens the window (static, then the scan band reveals the feed). feed may be null (NO SIGNAL card).</summary>
        public void Open(ZodLayout layout, Texture feedTexture)
        {
            lay = layout; feedTex = feedTexture;
            canvas.sortingOrder = layout.sorting;
            Rebuild();
            state = State.Opening; stateAt = OzMotion.Now; cue = cueShown = null; badgeAt = -100f;
            nextGlitch = OzMotion.Now + Random.Range(3f, 6f);
            Glitch(1f, 0.42f);                                                                          // v2: the transmission opens in static
        }

        /// <summary>v2: a signal burst: static, tearing, colour split, rolling bar for `seconds` at `strength` (0..1.5).</summary>
        public void Glitch(float strength = 1f, float seconds = 0.25f)
        {
            float now = OzMotion.Now;
            burstK = now < burstUntil ? Mathf.Max(burstK, strength) : strength;
            if (now >= burstUntil) burstFrom = now;
            burstUntil = Mathf.Max(burstUntil, now + Mathf.Clamp(seconds, 0.04f, 1.5f));
            nextTear = 0f;
        }

        public void SetFeed(Texture t)
        {
            feedTex = t; if (feed) { feed.texture = t; feed.enabled = t != null; } if (noSignal) noSignal.enabled = t == null;
            if (splitR) splitR.texture = t; if (splitC) splitC.texture = t; foreach (var r in tears) if (r) r.texture = t;
        }

        public void Close()
        {
            if (state == State.Closed || state == State.Closing) return;
            state = State.Closing; stateAt = OzMotion.Now;
            Glitch(0.9f, 0.22f);                                                                        // v2: the signal breaks up, then folds
        }

        public void Destroy() { if (canvas) Object.Destroy(canvas.gameObject); }

        /// <summary>The well size in physical pixels (for the stage's RenderTexture).</summary>
        public Vector2Int WellPixels(ZodLayout l)
        {
            float k = K * l.scale;
            return new Vector2Int(Mathf.CeilToInt((l.wellW - 6f) * k), Mathf.CeilToInt((l.wellH - 6f) * k));
        }

        void Rebuild()
        {
            if (win) Object.Destroy(win.gameObject);
            vu.Clear(); nameLabel = null; unitLabel = null;
            float W = lay.W, H = lay.H;
            win = Hud.Node("ZodWindow", canvasRt).Place(new OzRect(lay.anchor, lay.x, lay.y, W, H));
            win.localScale = Vector3.one * lay.scale;
            winCg = win.gameObject.AddComponent<CanvasGroup>(); winCg.blocksRaycasts = false; winCg.interactable = false; winCg.alpha = 0f;
            Hud.Plate(win, "md", ZodLayout.Header, OzUi.Amber, null, lay.rivets, true);

            // header: live dot, eyebrow, channel, signal meter
            dot = Hud.Kit(win, "LiveDot", "Bits/dot", new OzRect(15, 11, 8, 8), OzUi.Alert, Image.Type.Simple);
            eyebrow = Hud.Label(win, "Eyebrow", lay.eyebrow ?? "COMMS", 10, OzUi.Amber, 0.26f).At(30, 15, OzH.L, true, W - 150);
            float vx = W - 14f - 5 * 5f;
            for (int i = 0; i < 5; i++)
            {
                var b = Hud.Img(win, "Vu" + i, OzUi.A(OzUi.Amber, 0.85f));
                b.rectTransform.anchorMin = b.rectTransform.anchorMax = new Vector2(0, 1); b.rectTransform.pivot = new Vector2(0, 0);
                b.rectTransform.anchoredPosition = new Vector2(vx + i * 5f, -22f); b.rectTransform.sizeDelta = new Vector2(3f, 2f);
                vu.Add(b);
            }
            channel = Hud.Label(win, "Channel", lay.channel ?? "", 8, OzUi.Muted, 0.22f, OzStyle.Plain).At(vx - 8f, 15, OzH.R, true, 120);

            // the well (recessed screen): feed inside a reveal mask, NO SIGNAL card, noise, scanlines, sheen, brackets, scan band
            float wy = ZodLayout.Header + ZodLayout.Gap;                                                      // under the header band
            var well = Hud.Kit(win, "Well", "Panels/well_6", new OzRect(ZodLayout.Pad, wy, lay.wellW, lay.wellH), OzKit.Available ? (Color?)null : OzUi.GM950);
            screen = Hud.Node("Screen", well.transform); screen.Fill(3, 3, 3, 3);
            var bg = Hud.Img(screen, "Black", new Color32(6, 8, 7, 255)); bg.rectTransform.Fill();
            noSignal = Hud.Kit(screen, "NoSignal", "Portrait/portrait_nosignal", new OzRect(0, 0, 10, 10), null, Image.Type.Simple);
            noSignal.rectTransform.anchorMin = noSignal.rectTransform.anchorMax = new Vector2(0.5f, 0.5f); noSignal.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            noSignal.rectTransform.anchoredPosition = Vector2.zero; noSignal.rectTransform.sizeDelta = Vector2.one * (lay.wellH - 6f);
            reveal = Hud.Node("Reveal", screen); reveal.gameObject.AddComponent<RectMask2D>();
            feedRt = Hud.Node("Feed", reveal);
            feed = feedRt.gameObject.AddComponent<RawImage>(); feed.raycastTarget = false; feed.texture = feedTex; feed.enabled = feedTex != null;
            noSignal.enabled = feedTex == null;
            // v2: colour split ghosts + tearing bands, children of the feed (they move with it and stay inside the reveal mask)
            splitR = FeedLayer("SplitR", new Color(1f, 0.18f, 0.16f, 0f)); splitC = FeedLayer("SplitC", new Color(0.2f, 0.85f, 1f, 0f));
            tears.Clear();
            for (int i = 0; i < tearShift.Length; i++) { var r = FeedLayer("Tear" + i, Color.white); r.enabled = false; tears.Add(r); }
            noise = Hud.Node("Noise", screen).gameObject.AddComponent<RawImage>(); noise.rectTransform.Fill(); noise.raycastTarget = false;
            noise.texture = noiseTex[0]; noise.color = new Color(1, 1, 1, 0);
            float sw = lay.wellW - 6f, shh = lay.wellH - 6f;
            scan = Hud.Tiled(screen, "Scanlines", "Textures/tex_scanlines", new OzRect(0, 0, sw, shh), new Color(1, 1, 1, 0.08f)); scan.rectTransform.Fill();
            var sheen = Hud.Kit(screen, "Sheen", "Bits/glass_sheen", new OzRect(0, 0, sw, shh), new Color(1, 1, 1, 0.07f), Image.Type.Simple); sheen.rectTransform.Fill();
            sheen.enabled = OzKit.GetSprite("Bits/glass_sheen") != null;
            Bracket(screen, true); Bracket(screen, false);
            band = Hud.Kit(screen, "ScanBand", "Bits/scan_band", new OzRect(0, 0, sw, 24), OzUi.A(OzUi.AmberHi, 0.55f), Image.Type.Simple);
            bandRt = band.rectTransform; bandRt.anchorMin = new Vector2(0, 1); bandRt.anchorMax = new Vector2(1, 1); bandRt.pivot = new Vector2(0.5f, 0.5f);
            bandRt.sizeDelta = new Vector2(0, 24); band.enabled = false;
            flashLine = Hud.Img(screen, "CollapseLine", OzUi.A(OzUi.Ink, 0f));
            flashLine.rectTransform.anchorMin = new Vector2(0, 0.5f); flashLine.rectTransform.anchorMax = new Vector2(1, 0.5f); flashLine.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            flashLine.rectTransform.sizeDelta = new Vector2(0, 2f); flashLine.rectTransform.anchoredPosition = Vector2.zero;

            float y = wy + lay.wellH + ZodLayout.Gap;
            if (lay.footer)
            {
                nameLabel = Hud.Label(win, "Name", !string.IsNullOrEmpty(Host) ? Host : General ? "GENERAL ZOD" : "COMMANDER ZOD", 13, OzUi.Ink, 0.1f).At(ZodLayout.Pad + 2, y + 11, OzH.L, true, 220);
                namePos = new Vector2(ZodLayout.Pad + 2, y + 11);
                unitLabel = Hud.Label(win, "Unit", "HIGH COMMAND · " + TeamColors.Name(Team.Red) + " FORCE", 8, OzUi.Muted, 0.24f, OzStyle.Plain).At(W - ZodLayout.Pad - 2, y + 11, OzH.R, true, 200);
                if (unitLabel != null && unitLabel.graphic) unitLabel.graphic.enabled = string.IsNullOrEmpty(Host);   // the plate of the two robots reads their names only
                y += ZodLayout.FooterH + ZodLayout.Gap;
            }
            subSpeaker = subBody = null;
            if (lay.sub == ZodSubMode.Below)
            {
                subSpeaker = Hud.Label(win, "Speaker", SpeakerName, 9, OzUi.Amber, 0.26f).At(ZodLayout.Pad + 2, y + 2, OzH.L, false, 200);
                subBody = Hud.Label(win, "Subtitle", "", 15, OzUi.Ink, 0.01f).Body(ZodLayout.Pad + 2, y + 16, lay.wellW - 4);
                y += ZodLayout.SubH + ZodLayout.Gap;
            }
            else if (lay.sub == ZodSubMode.Right)
            {
                float tx = ZodLayout.Pad + lay.wellW + 16f;
                subSpeaker = Hud.Label(win, "Speaker", SpeakerName, 9, OzUi.Amber, 0.26f).At(tx, wy + 4, OzH.L, false, lay.textW);
                subBody = Hud.Label(win, "Subtitle", "", 15, OzUi.Ink, 0.01f).Body(tx, wy + 20, lay.textW);
            }
            badgeRt = null;
            if (lay.badge) BuildBadge(y);
            FeedPixels = WellPixels(lay);
        }

        RawImage FeedLayer(string name, Color c)
        {
            var rt = Hud.Node(name, feedRt); rt.Fill();
            var r = rt.gameObject.AddComponent<RawImage>(); r.raycastTarget = false; r.texture = feedTex; r.color = c;
            return r;
        }

        /// <summary>v2: burst envelope (0..1) with a ragged flicker; small hiccups every 3-6 s while the window is open.</summary>
        float BurstNow(float now)
        {
            if (state == State.Open && now > nextGlitch) { Glitch(0.32f, Random.Range(0.05f, 0.1f)); nextGlitch = now + Random.Range(3f, 6f); }
            if (now >= burstUntil) return 0f;
            float a = Mathf.Clamp01((now - burstFrom) / 0.03f), b = Mathf.Clamp01((burstUntil - now) / 0.08f);
            float flick = 0.65f + 0.35f * Mathf.PerlinNoise(now * 37f, 3.3f);
            return Mathf.Clamp01(burstK * a * b * flick);
        }

        void TickBurst(float now, Rect sr)
        {
            float g = BurstNow(now); Burst = g;
            bool reduced = OzMotion.Reduced;
            float w = Mathf.Max(1f, sr.width);
            // colour split: red ghost one way, cyan the other (a faint 1 u split stays while the burst fades)
            float dx = reduced ? 0f : (2f + 6f * g) / w;
            if (splitR) { splitR.enabled = g > 0.02f && !reduced && feedTex; splitR.uvRect = new Rect(-dx, 0f, 1f, 1f); splitR.color = new Color(1f, 0.18f, 0.16f, 0.42f * g); }
            if (splitC) { splitC.enabled = g > 0.02f && !reduced && feedTex; splitC.uvRect = new Rect(dx, 0f, 1f, 1f); splitC.color = new Color(0.2f, 0.85f, 1f, 0.42f * g); }
            // sync tearing: 5 bands re-rolled every 45 ms, the picture inside each slid sideways (clamped edge smear)
            if (now >= nextTear)
            {
                nextTear = now + 0.045f;
                for (int i = 0; i < tears.Count; i++)
                {
                    var r = tears[i]; if (!r) continue;
                    bool on = !reduced && g > 0.12f && feedTex && Random.value < 0.35f + 0.5f * g;
                    r.enabled = on; if (!on) continue;
                    float y0 = Random.value * 0.92f, h = Random.Range(0.03f, 0.2f) * (0.5f + g);
                    y0 = Mathf.Min(y0, 1f - h);
                    tearShift[i] = Random.Range(-1f, 1f) * (6f + 26f * g) / w;
                    r.rectTransform.anchorMin = new Vector2(0f, y0); r.rectTransform.anchorMax = new Vector2(1f, y0 + h);
                    r.rectTransform.offsetMin = r.rectTransform.offsetMax = Vector2.zero;
                    r.uvRect = new Rect(tearShift[i], y0, 1f, h);
                    r.color = new Color(1f, 1f, 1f, 1f);
                }
            }
            if (scan) scan.color = new Color(1f, 1f, 1f, 0.08f + 0.2f * g);
            if (feed) { float d = g > 0f ? 1f - 0.28f * g * Random.value : 1f; feed.color = new Color(d, d, d, 1f); }
        }

        void Bracket(RectTransform parent, bool topLeft)
        {
            var c = OzUi.A(OzUi.Ink, 0.45f); float arm = 14f, t = 2f, inset = 7f;
            var a = Hud.Img(parent, topLeft ? "BracketTL_h" : "BracketBR_h", c); var b = Hud.Img(parent, topLeft ? "BracketTL_v" : "BracketBR_v", c);
            Vector2 anchor = topLeft ? new Vector2(0, 1) : new Vector2(1, 0), pivot = anchor;
            foreach (var i in new[] { a, b }) { i.rectTransform.anchorMin = i.rectTransform.anchorMax = anchor; i.rectTransform.pivot = pivot; }
            float sx = topLeft ? inset : -inset, sy = topLeft ? -inset : inset;
            a.rectTransform.anchoredPosition = new Vector2(sx, sy); a.rectTransform.sizeDelta = new Vector2(arm, t);
            b.rectTransform.anchoredPosition = new Vector2(sx, sy); b.rectTransform.sizeDelta = new Vector2(t, arm);
        }

        // ------------------------------------------------------------------------------------------------ 30th anniversary stamp
        static readonly Color Gold = new Color32(224, 180, 92, 255), GoldHi = new Color32(255, 230, 160, 255), GoldLo = new Color32(150, 110, 48, 255);

        void BuildBadge(float y)
        {
            float w = lay.wellW, h = ZodLayout.BadgeH;
            badgeRt = Hud.Node("Anniversary30", win).Place(new OzRect(ZodLayout.Pad, y, w, h));
            badgeRt.pivot = new Vector2(0.5f, 0.5f); badgeRt.anchoredPosition += new Vector2(w * 0.5f, -h * 0.5f);   // the stamp lands around its centre
            badgeCg = badgeRt.gameObject.AddComponent<CanvasGroup>(); badgeCg.alpha = 0f; badgeCg.blocksRaycasts = false;
            var plate = Hud.Img(badgeRt, "Plate", OzUi.A(OzUi.GM950, 0.9f)); plate.rectTransform.Fill();
            Hud.Border(plate.transform, OzUi.A(GoldLo, 0.9f), 1f);
            var tab = Hud.Img(badgeRt, "Tab", Gold); tab.rectTransform.Place(new OzRect(6, 5, 48, h - 10));
            Hud.Label(tab.transform, "N", "30", 20, new Color32(20, 18, 14, 255), 0.04f, OzStyle.Plain).FillParent(OzH.C);
            Hud.Label(badgeRt, "Line1", "YEARS OF Z", 12, Gold, 0.26f, OzStyle.Plain).At(66, 11, OzH.L, true, w - 76);
            Hud.Label(badgeRt, "Line2", "1996 – 2026 · ANNIVERSARY EDITION", 8, OzUi.A(GoldHi, 0.8f), 0.24f, OzStyle.Plain).At(66, 26, OzH.L, true, w - 76);
            badgeFlash = Hud.Img(badgeRt, "Flash", new Color(1f, 0.95f, 0.8f, 0f)); badgeFlash.rectTransform.Fill();
            // shine: a soft band that sweeps across every few seconds (kept inside the plate)
            var mask = Hud.Node("ShineMask", badgeRt); mask.Fill(1, 1, 1, 1); mask.gameObject.AddComponent<RectMask2D>();
            badgeShine = Hud.Node("Shine", mask);
            var ri = badgeShine.gameObject.AddComponent<RawImage>(); ri.texture = ShineTex(); ri.color = OzUi.A(GoldHi, 0.35f); ri.raycastTarget = false;
            badgeShine.anchorMin = new Vector2(0, 0); badgeShine.anchorMax = new Vector2(0, 1); badgeShine.pivot = new Vector2(0.5f, 0.5f);
            badgeShine.sizeDelta = new Vector2(70f, 20f); badgeShine.localRotation = Quaternion.Euler(0, 0, -18f); badgeShine.gameObject.SetActive(false);
        }

        /// <summary>The gold "30" stamp lands (ZodBriefing fires it on the word "thirty" of the menu greeting).</summary>
        public void StampBadge()
        {
            if (!badgeRt || badgeAt > 0f) return;
            badgeAt = OzMotion.Now;
            if (AudioDirector.Has("ui_stamp")) AudioDirector.UI("ui_stamp", 0.8f);
        }

        static Texture2D shineTex;
        static Texture2D ShineTex()
        {
            if (shineTex) return shineTex;
            shineTex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ZodShine" };
            for (int i = 0; i < 64; i++) { float u = i / 63f * 2f - 1f; shineTex.SetPixel(i, 0, new Color(1, 1, 1, Mathf.Pow(Mathf.Max(0f, 1f - u * u), 2f))); }
            shineTex.Apply(); return shineTex;
        }

        // ------------------------------------------------------------------------------------------------ live content
        /// <summary>The current subtitle cue (null = none). Changes cross-fade in 120 ms with a 6 u rise.</summary>
        public void SetCue(string text)
        {
            if (text == cue) return;
            cue = text; cueAt = OzMotion.Now;
        }

        /// <summary>Signal meter from Zod's mouth opening (0..1).</summary>
        public void SetVu(float v)
        {
            for (int i = 0; i < vuLevel.Length; i++)
            {
                float target = Mathf.Clamp01(v * 1.6f - i * 0.18f + (v > 0.05f ? Random.Range(-0.08f, 0.08f) : 0f));
                vuLevel[i] = target > vuLevel[i] ? target : Mathf.MoveTowards(vuLevel[i], target, Time.unscaledDeltaTime * 4f);
            }
        }

        // ============================================================================================================ letterbox
        /// <summary>Briefing bars: top = "MISSION BRIEFING · OPERATION L02 · DESERT" + SPACE SKIP keycap + progress line; bottom =
        /// the subtitle bar. They slide in from the screen edges (medium) and out when the briefing hands over.</summary>
        public void Letterbox(bool on, string title = null, string operation = null)
        {
            if (on && !lbTop) BuildLetterbox(title, operation);
            if (lbOn == on) return;
            lbOn = on; lbAt = OzMotion.Now;
        }
        public bool LetterboxGone => !lbOn && (!lbTop || OzMotion.Now - lbAt > OzMotion.Dur(OzMotion.Medium) + 0.05f);
        public void SetProgress(float p) => progress = Mathf.Clamp01(p);

        const float LbTopH = 64f, LbBottomH = 112f;

        void BuildLetterbox(string title, string operation)
        {
            var root = Hud.Node("Letterbox", canvasRt); root.Fill(); root.SetAsFirstSibling();
            lbCg = root.gameObject.AddComponent<CanvasGroup>(); lbCg.blocksRaycasts = false; lbCg.interactable = false;
            lbTop = Hud.Node("Top", root); lbTop.anchorMin = new Vector2(0, 1); lbTop.anchorMax = new Vector2(1, 1); lbTop.pivot = new Vector2(0.5f, 1); lbTop.sizeDelta = new Vector2(0, LbTopH);
            var tb = Hud.Img(lbTop, "Bar", OzUi.A(OzUi.GM950, 0.97f)); tb.rectTransform.Fill();
            var tl = Hud.Img(lbTop, "Keyline", OzUi.A(OzUi.Khaki, 0.24f)); tl.rectTransform.anchorMin = new Vector2(0, 0); tl.rectTransform.anchorMax = new Vector2(1, 0); tl.rectTransform.pivot = new Vector2(0.5f, 0); tl.rectTransform.sizeDelta = new Vector2(0, 1f);
            lbLeft = Hud.Label(lbTop, "Title", title ?? "MISSION BRIEFING", 11, OzUi.Amber, 0.3f).At(48, 32, OzH.L, true, 400);
            lbLeft2 = Hud.Label(lbTop, "Operation", operation ?? "", 11, OzUi.Ink2, 0.24f).At(48 + lbLeft.preferredWidth + 22, 32, OzH.L, true, 700);
            var sep = Hud.Img(lbTop, "Sep", OzUi.A(OzUi.Khaki, 0.35f)); sep.rectTransform.Place(new OzRect(48 + lbLeft.preferredWidth + 10, 26, 1, 12));
            lbSkip = Hud.Node("Skip", lbTop); lbSkip.anchorMin = lbSkip.anchorMax = new Vector2(1, 1); lbSkip.pivot = new Vector2(1, 1); lbSkip.anchoredPosition = new Vector2(-48, 0); lbSkip.sizeDelta = new Vector2(260, LbTopH);
            var skipTxt = Hud.Label(lbSkip, "Hint", "SKIP BRIEFING", 9, OzUi.Muted, 0.22f, OzStyle.Plain).At(260, 32, OzH.R, true, 200);
            Hud.Keycap(lbSkip, "Key", "SPACE", 260 - skipTxt.preferredWidth - 10, 25);
            lbProgress = Hud.Node("Progress", lbTop).Anchor(new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(0, 2f));
            Hud.Img(lbProgress, "Fill", OzUi.A(OzUi.Amber, 0.85f)).rectTransform.Fill();

            lbBottom = Hud.Node("Bottom", root); lbBottom.anchorMin = new Vector2(0, 0); lbBottom.anchorMax = new Vector2(1, 0); lbBottom.pivot = new Vector2(0.5f, 0); lbBottom.sizeDelta = new Vector2(0, LbBottomH);
            var bb = Hud.Img(lbBottom, "Bar", OzUi.A(OzUi.GM950, 0.97f)); bb.rectTransform.Fill();
            var bl = Hud.Img(lbBottom, "Keyline", OzUi.A(OzUi.Khaki, 0.24f)); bl.rectTransform.anchorMin = new Vector2(0, 1); bl.rectTransform.anchorMax = new Vector2(1, 1); bl.rectTransform.pivot = new Vector2(0.5f, 1); bl.rectTransform.sizeDelta = new Vector2(0, 1f);
            var spk = Hud.Node("SpeakerBox", lbBottom); spk.anchorMin = new Vector2(0, 1); spk.anchorMax = new Vector2(1, 1); spk.pivot = new Vector2(0.5f, 1); spk.anchoredPosition = new Vector2(0, -16); spk.sizeDelta = new Vector2(0, 16);
            lbSpeaker = Hud.Label(spk, "Speaker", SpeakerName, 10, OzUi.Amber, 0.28f).FillParent(OzH.C);
            var cb = Hud.Node("CueBox", lbBottom); cb.anchorMin = new Vector2(0, 1); cb.anchorMax = new Vector2(1, 1); cb.pivot = new Vector2(0.5f, 1); cb.anchoredPosition = new Vector2(0, -40); cb.sizeDelta = new Vector2(-160, 44);
            lbCue = Hud.Label(cb, "Cue", "", 26, OzUi.Ink, 0.005f).FillParent(OzH.C);
            lbSpeaker.Alpha = 0f;
        }

        // ============================================================================================================ frame
        public void Tick()
        {
            float now = OzMotion.Now;
            TickLetterbox(now);
            if (state == State.Closed || !win) return;
            float e = now - stateAt;
            bool reduced = OzMotion.Reduced;
            float slideU = reduced ? 0f : 40f * lay.slide;
            float open = 0f, rv = 1f, noiseA = 0f, bandY = -1f, lineA = 0f;
            switch (state)
            {
                case State.Opening:
                {
                    float kIn = OzEase.OutCubic(OzMotion.T(stateAt, OzMotion.Dur(OzMotion.Medium, true)));
                    open = kIn;
                    float s0 = reduced ? 0f : 0.22f, sweep = reduced ? 0.12f : 0.34f;
                    float u = Mathf.Clamp01((e - s0) / sweep);
                    noiseA = e < s0 ? 0.85f : Mathf.Lerp(0.5f, 0.06f, u);
                    rv = e < s0 ? 0f : OzEase.OutQuad(u);
                    bandY = e >= s0 && u < 1f ? u : -1f;
                    if (e > s0 + sweep + 0.05f) state = State.Open;
                    break;
                }
                case State.Open:
                    open = 1f; rv = 1f; noiseA = 0.05f;                                                 // v2: hiccups come from BurstNow
                    break;
                case State.Closing:
                {
                    float c1 = reduced ? 0.01f : 0.16f, c2 = OzMotion.Dur(0.22f, true);
                    float u = Mathf.Clamp01(e / Mathf.Max(0.01f, c1));
                    rv = 1f - OzEase.InCubic(u);                                                         // CRT off: the picture folds to a line
                    lineA = u < 1f ? u : Mathf.Clamp01(1f - (e - c1) / 0.12f);
                    noiseA = 0.15f * (1f - u);
                    open = e < c1 ? 1f : 1f - OzEase.InCubic(Mathf.Clamp01((e - c1) / c2));
                    if (e > c1 + c2 + 0.02f) { state = State.Closed; win.gameObject.SetActive(false); return; }
                    break;
                }
            }
            win.gameObject.SetActive(true);
            winCg.alpha = open;
            win.Place(new OzRect(lay.anchor, lay.x + slideU * (1f - open), lay.y, lay.W, lay.H));
            // reveal: opening grows from the top edge, closing folds to the centre line
            var sr = screen.rect; float h = sr.height;
            if (state == State.Closing)
            {
                reveal.anchorMin = new Vector2(0, 0.5f); reveal.anchorMax = new Vector2(1, 0.5f); reveal.pivot = new Vector2(0.5f, 0.5f);
                reveal.anchoredPosition = Vector2.zero; reveal.sizeDelta = new Vector2(0, Mathf.Max(0f, h * rv));
            }
            else
            {
                reveal.anchorMin = new Vector2(0, 1); reveal.anchorMax = new Vector2(1, 1); reveal.pivot = new Vector2(0.5f, 1);
                reveal.anchoredPosition = Vector2.zero; reveal.sizeDelta = new Vector2(0, h * rv);
            }
            // the feed stays glued to the screen whatever the mask does (+ a jolt during a signal burst)
            feedRt.anchorMin = feedRt.anchorMax = new Vector2(0.5f, 0.5f); feedRt.pivot = new Vector2(0.5f, 0.5f); feedRt.sizeDelta = sr.size;
            Vector3 centre = screen.TransformPoint(sr.center);
            TickBurst(now, sr);
            float gb = OzMotion.Reduced ? 0f : Burst;
            noiseA = Mathf.Max(noiseA, 0.62f * Burst);
            if (gb > 0.25f && bandY < 0f) bandY = Mathf.Repeat(now * 3.4f, 1f);                       // rolling bar through the burst
            feedRt.position = centre + (gb > 0.05f ? screen.TransformVector(new Vector3(Random.Range(-1f, 1f) * (2f + 8f * gb), Random.value < 0.2f * gb ? Random.Range(-3f, 3f) : 0f, 0f)) : Vector3.zero);
            if (noise && noiseTex[0])
            {
                var nt = noiseTex[(int)(now * 30f) & 3];
                noise.texture = nt ? nt : noiseTex[0];
                noise.uvRect = new Rect(Random.value, Random.value, sr.width * 2f / Mathf.Max(1, noise.texture.width), sr.height * 2f / Mathf.Max(1, noise.texture.height));
                noise.color = new Color(1f, 1f, 1f, noiseA);
            }
            band.enabled = bandY >= 0f;
            if (band.enabled) bandRt.anchoredPosition = new Vector2(0f, -h * bandY);
            flashLine.color = OzUi.A(OzUi.Ink, lineA * 0.9f);
            if (dot) dot.color = OzUi.A(OzUi.Alert, OzMotion.Reduced ? 1f : (Mathf.Repeat(now, 1f) < 0.62f ? 1f : 0.25f));
            for (int i = 0; i < vu.Count; i++) vu[i].rectTransform.sizeDelta = new Vector2(3f, 2f + 10f * vuLevel[i]);
            TickCue(now, subBody, subSpeaker, false);
            TickBadge(now);
        }

        void TickCue(float now, OzText body, OzText speaker, bool hideSpeakerWhenEmpty)
        {
            if (body == null) return;
            if (cueShown != cue)
            {
                float k = string.IsNullOrEmpty(cueShown) ? 1f : OzMotion.T(cueAt, OzMotion.Dur(0.08f, true));
                body.Alpha = 1f - k;                                                                     // old text out (80 ms) ...
                if (k >= 1f) { cueShown = cue; body.Set(cue ?? ""); cueAt = now; }
            }
            else
            {
                float kin = OzEase.OutCubic(OzMotion.T(cueAt, OzMotion.Dur(OzMotion.Short, true)));
                body.Alpha = string.IsNullOrEmpty(cueShown) ? 0f : kin;                                  // ... new text in (160 ms)
            }
            if (speaker != null) speaker.Alpha = hideSpeakerWhenEmpty && string.IsNullOrEmpty(cueShown) ? 0f : 1f;
        }

        void TickBadge(float now)
        {
            if (!badgeRt) return;
            if (badgeAt < 0f) { badgeCg.alpha = 0f; return; }
            float e = now - badgeAt;
            float k = OzMotion.T(badgeAt, OzMotion.Dur(OzMotion.Stamp));
            float s = OzMotion.Reduced ? 1f : Mathf.Lerp(1.6f, 1f, OzEase.OutBack(k));
            badgeRt.localScale = Vector3.one * s; badgeCg.alpha = Mathf.Clamp01(k * 1.5f);
            badgeFlash.color = new Color(1f, 0.95f, 0.8f, k >= 1f ? Mathf.Clamp01(0.55f - (e - OzMotion.Stamp) * 5f) : 0f);
            if (OzMotion.Reduced) { badgeShine.gameObject.SetActive(false); return; }
            float t = (e - 0.5f) % 4.5f;
            bool on = e > 0.5f && t < 1.1f;
            badgeShine.gameObject.SetActive(on);
            if (on) badgeShine.anchoredPosition = new Vector2(Mathf.Lerp(-40f, lay.wellW + 40f, OzEase.InOutSine(t / 1.1f)), 0f);
        }

        void TickLetterbox(float now)
        {
            if (!lbTop) return;
            float k = OzMotion.T(lbAt, OzMotion.Dur(OzMotion.Medium, true));
            float v = lbOn ? OzEase.OutCubic(k) : 1f - OzEase.InCubic(k);
            if (OzMotion.Reduced) { lbCg.alpha = v; v = 1f; } else lbCg.alpha = 1f;
            lbTop.anchoredPosition = new Vector2(0, LbTopH * (1f - v));
            lbBottom.anchoredPosition = new Vector2(0, -LbBottomH * (1f - v));
            lbProgress.sizeDelta = new Vector2(CanvasWidth * progress, 2f);
            {   // level 16 endings (the bars' own switch is on): an EMPTY top bar: no briefing title, no skip hint, no progress line
                bool labels = !LetterboxCue;
                if (lbLeft != null) lbLeft.SetActive(labels); if (lbLeft2 != null) lbLeft2.SetActive(labels);
                var sepT = lbTop.Find("Sep"); if (sepT && sepT.gameObject.activeSelf != labels) sepT.gameObject.SetActive(labels);
                if (lbSkip && lbSkip.gameObject.activeSelf != labels) lbSkip.gameObject.SetActive(labels);
                if (lbProgress && lbProgress.gameObject.activeSelf != labels) lbProgress.gameObject.SetActive(labels);
            }
            lbTop.gameObject.SetActive(lbOn || k < 1f); lbBottom.gameObject.SetActive(lbOn || k < 1f);
            if (lay.sub == ZodSubMode.None || LetterboxCue) TickCue(now, lbCue, lbSpeaker, true);   // level 16 endings: the bars' own switch
            if (LetterboxCue && !lbOn && k >= 1f) LetterboxCue = false;                               // the bars have slid out: the old rule again
        }
    }
}
