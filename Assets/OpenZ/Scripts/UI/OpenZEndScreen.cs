using System.Collections.Generic;
using System.Reflection;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// Match statistics for the end screen (guide 9.13): units built per team (a grunt squad of 3 = 3), kills and losses
    /// (robots, vehicles, cannons; buildings excluded), zones owned and the peak per team, forces sampled every 10 s of game
    /// time, and every zone capture (the chart's markers). Fed by OpenZHud from the sim event list.
    /// </summary>
    public sealed class OpenZMatchStats
    {
        public readonly Dictionary<Team, int> Built = new Dictionary<Team, int>(), Kills = new Dictionary<Team, int>(), Losses = new Dictionary<Team, int>(), PeakZones = new Dictionary<Team, int>();
        public readonly List<(float t, int red, int blue)> Samples = new List<(float, int, int)>();
        public readonly List<(float t, Team team)> Captures = new List<(float, Team)>();
        public readonly HashSet<Team> FortLost = new HashSet<Team>();
        double nextSample;

        public static int Get(Dictionary<Team, int> d, Team t) => d.TryGetValue(t, out var n) ? n : 0;
        static void Add(Dictionary<Team, int> d, Team t, int n = 1) => d[t] = Get(d, t) + n;

        public void Begin(ZWorld w, ZGame g) { if (w == null) return; nextSample = 0; Tick(w); UpdatePeaks(w); }

        public void Tick(ZWorld w)
        {
            if (w == null || w.GameOver) return;
            while (w.Time >= nextSample) { Samples.Add(((float)nextSample, w.UnitCount(Team.Red), w.UnitCount(Team.Blue))); nextSample += 10.0; }
        }

        void UpdatePeaks(ZWorld w)
        {
            int r = 0, b = 0; foreach (var z in w.Zones) { if (z.Owner == Team.Red) r++; else if (z.Owner == Team.Blue) b++; }
            if (r > Get(PeakZones, Team.Red)) PeakZones[Team.Red] = r;
            if (b > Get(PeakZones, Team.Blue)) PeakZones[Team.Blue] = b;
        }

        public void OnEvent(ZWorld w, ZEvent e)
        {
            switch (e.type)
            {
                case ZEventType.UnitBuilt: { var u = w.GetAny(e.objId); if (u != null) Add(Built, u.Owner, 1 + u.Minions.Count); break; }
                case ZEventType.CannonStored: { var b = w.GetAny(e.objId); if (b != null) Add(Built, b.Owner); break; }
                case ZEventType.Death:
                {
                    var d = w.GetAny(e.objId); if (d == null || !d.IsUnit || d.Owner == Team.Neutral) break;
                    Add(Losses, d.Owner);
                    var a = w.GetAny(e.otherId);
                    if (a != null && a.Owner != d.Owner && a.Owner != Team.Neutral) Add(Kills, a.Owner);
                    break;
                }
                case ZEventType.FlagCaptured: Captures.Add(((float)w.Time, (Team)(int)e.value)); UpdatePeaks(w); break;
                case ZEventType.TeamEliminated: FortLost.Add((Team)(int)e.value); break;
            }
            Tick(w);
        }
    }

    /// <summary>
    /// VICTORY / DEFEAT / DRAW end screen (guide 9.13 + kit README 4 refinements; mock-ups kit_victory / kit_defeat). Zod has
    /// none (zserver.cpp:325-367 just ends the game): this replaces RulesView's plain banner. Listens to the sim's game over
    /// (ZWorld.GameOver / Winner), fades the world through a post volume, then reveals the band, the stencil wordmark stamp
    /// with its glow and sheen, the subtitle, four stat plates with ticking numbers, the forces chart and the buttons, on
    /// unscaled time. Any click / Enter / Esc during the reveal jumps to the final state. Enter = CONTINUE (next level) or
    /// RETRY, R = REPLAY (restart this level), Esc = MAIN MENU.
    /// </summary>
    [DefaultExecutionOrder(-43)]
    public class OpenZEndScreen : MonoBehaviour
    {
        public bool IsShown { get; private set; }
        /// <summary></summary>
        public static bool AnyShown { get; private set; }
        OpenZHud hud; ZGame game; Canvas canvas; RectTransform root, maskBox, bandRoot;
        OzPost post; float t0 = -100f; string kind; bool win; bool skipped;
        RawImage hazardTop, hazardBottom;
        Image wmGlow, wmFace, wmFlash, sheen, eyebrowBg; OzText eyebrow, subtitle, hint;
        RectTransform eyebrowRt, wmRoot, chartRt, flankL, flankR;
        readonly List<RectTransform> plates = new List<RectTransform>(); readonly List<OzText> plateValues = new List<OzText>(); readonly List<Image> plateFlash = new List<Image>();
        readonly List<System.Func<float, string>> plateFormat = new List<System.Func<float, string>>(); readonly List<float> plateTarget = new List<float>(), plateShown = new List<float>();
        readonly List<HudButton> buttons = new List<HudButton>(); Image focusRing; int focus;
        OzChartGraphic chart; readonly List<OzText> chartTicks = new List<OzText>();
        /// <summary></summary>
        Image dimImage; float dimTarget;
        public static float DimEase = 0.5f;
        /// <summary>Play proof: the chart's y scale, its tick step (s) and the flank label widths vs the plate.</summary>
        public string LayoutStatus { get; private set; } = "";
        RulesView rulesView; FieldInfo bannerField; bool shakeDone; float skipAt = -100f;

        public void Build(OpenZHud h)
        {
            hud = h; game = h.game;
            var go = new GameObject("OpenZ_EndScreen", typeof(RectTransform)) { layer = 5 };
            go.transform.SetParent(h.transform, false);
            canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = h.sortingOrder + 10;
            var cs = go.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920, 1080); cs.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; cs.matchWidthOrHeight = 1f;
            root = Hud.Node("Root", go.transform); root.Fill();
            h.AddModalBlocker(root);
            go.SetActive(false);
            rulesView = game ? game.GetComponent<RulesView>() : null;
            if (!rulesView) rulesView = FindFirstObjectByType<RulesView>();
            bannerField = typeof(RulesView).GetField("banner", BindingFlags.NonPublic | BindingFlags.Instance);   // its OnGUI banner is replaced by this screen
        }

        void Update()
        {
            if (hud == null || game == null || game.World == null) return;
            var W = game.World;
            if (!IsShown && W.GameOver && !OpenZVictory.HoldEndScreen) Show(W.Winner);   // fx_v2: after the nuke + celebration
            if (!IsShown) return;
            Animate();
            HandleKeys();
        }

        /// <summary>RulesView's plain OnGUI banner stays off: cleared after every Update (RulesView sets it in its own Update,
        /// which may run after this component in the game-over frame; OnGUI comes after LateUpdate, so it never flashes).</summary>
        void LateUpdate()
        {
            if (bannerField == null || !rulesView || game == null || game.World == null || !(IsShown || game.World.GameOver)) return;
            if (bannerField.GetValue(rulesView) != null) bannerField.SetValue(rulesView, null);
        }

        /// <summary>
        /// (216) touched it, so on a defeat the chip moves down 6 and the wordmark 1 (every gap >= 8 px at 1080p, ~12 px at 2560x1600).</summary>
        float ChipY => kind == "defeat" ? OzLayout.EndChipY + 6f : OzLayout.EndChipY;
        float WordmarkMid => kind == "defeat" ? OzLayout.EndWordmarkMid + 1f : OzLayout.EndWordmarkMid;

        // ------------------------------------------------------------------ build on demand (the level may end with any winner)
        void Show(Team winner)
        {
            // critic L02 #5a: a second Show (a new game-over state, or a staged one) must not stack on the first
            if (root) for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
            plates.Clear(); plateValues.Clear(); plateFlash.Clear(); plateFormat.Clear(); plateTarget.Clear(); plateShown.Clear(); buttons.Clear(); chartTicks.Clear();
            IsShown = true; AnyShown = true; t0 = OzMotion.Now; skipped = false; shakeDone = false;
            OzEndingVignette.Release();
            if (hud) hud.SetVisible(false);                                                              // critic: the HUD stayed drawn over the end screen
            var W = game.World; var me = hud.Player;
            kind = winner == Team.Neutral ? "draw" : winner == me ? "victory" : "defeat";
            win = kind == "victory";
            canvas.gameObject.SetActive(true);
            post = kind == "victory" ? new OzPost("OZ_EndScreen", -45f, -0.9f, 0.35f) : kind == "defeat" ? new OzPost("OZ_EndScreen", -70f, -1.1f, 0.45f) : new OzPost("OZ_EndScreen", -60f, -1f, 0.4f);
            Color accent = win ? OzUi.Amber : kind == "defeat" ? (Color)OzUi.Alert : (Color)OzUi.Ink2;
            float cx = OzLayout.RefW * 0.5f;
            string level = game.level == "L17" ? "THE CAR PARK" : "THE DESERT";
            string lvlCode = game.level == "L17" ? "L17" : game.level == "L02" ? "L02" : game.level;
            Team enemy = me == Team.Red ? Team.Blue : Team.Red;

            dimImage = null;
            if (kind != "victory")
            {
                dimTarget = kind == "defeat" ? 0.8f : 0.5f;
                var v = Hud.Kit(root, "Vignette", "Backdrops/bd_vignette", new OzRect(OzAnchor.C, 0, 0, OzLayout.RefW, OzLayout.RefH), OzUi.A(Color.white, 0f), Image.Type.Simple); v.enabled = OzKit.Available;
                dimImage = v;
            }
            // band + backdrop (baked band, glow, rays / scanlines, hairlines), revealed from the band's centre line
            maskBox = Hud.Node("BandReveal", root).Place(new OzRect(OzAnchor.C, -1000, OzLayout.EndBandY + OzLayout.EndBandH * 0.5f, OzLayout.RefW + 2000, 0));
            maskBox.pivot = new Vector2(0.5f, 0.5f); maskBox.anchoredPosition = new Vector2(0, OzLayout.RefH * 0.5f - (OzLayout.EndBandY + OzLayout.EndBandH * 0.5f));
            maskBox.gameObject.AddComponent<RectMask2D>();
            bandRoot = Hud.Node("Backdrop", maskBox); bandRoot.anchorMin = bandRoot.anchorMax = new Vector2(0.5f, 0.5f); bandRoot.pivot = new Vector2(0.5f, 0.5f);
            bandRoot.sizeDelta = new Vector2(OzLayout.RefW, OzLayout.RefH); bandRoot.anchoredPosition = new Vector2(0, -(OzLayout.RefH * 0.5f - (OzLayout.EndBandY + OzLayout.EndBandH * 0.5f)));
            var bd = Hud.Kit(bandRoot, "Band", "Backdrops/bd_" + kind, new OzRect(0, 0, OzLayout.RefW, OzLayout.RefH), null, Image.Type.Simple);
            if (!OzKit.Available) { bd.color = OzUi.A(new Color32(10, 12, 11, 255), 0.75f); bd.rectTransform.Place(new OzRect(0, OzLayout.EndBandY, OzLayout.RefW, OzLayout.EndBandH)); }
            if (kind == "defeat")
            {
                hazardTop = Hud.Tiled(root, "HazardTop", "Backdrops/bd_hazard_band_alert", new OzRect(OzAnchor.TC, 0, OzLayout.EndHazardY0, OzLayout.RefW, OzLayout.EndHazardH), Color.white);
                hazardBottom = Hud.Tiled(root, "HazardBottom", "Backdrops/bd_hazard_band_alert", new OzRect(OzAnchor.TC, 0, OzLayout.EndHazardY1, OzLayout.RefW, OzLayout.EndHazardH), Color.white);
                foreach (var hz in new[] { hazardTop, hazardBottom }) { hz.rectTransform.anchorMin = new Vector2(0, 1); hz.rectTransform.anchorMax = new Vector2(1, 1); hz.rectTransform.offsetMin = new Vector2(0, hz.rectTransform.offsetMin.y); hz.rectTransform.offsetMax = new Vector2(0, hz.rectTransform.offsetMax.y); }
            }

            // flanking plates: Zod's end-of-game face (left) and the winner's emblem (right)
            flankL = Flank(new OzRect(OzAnchor.TC, cx - OzLayout.EndFlankDX, OzLayout.EndFlankY, OzLayout.EndFlankW, OzLayout.EndFlankH));
            string face = FaceType(me, win);
            var well = Hud.Kit(flankL, "Well", "Panels/well_6", new OzRect(8, 8, OzLayout.EndFlankWell, OzLayout.EndFlankWell), OzKit.Available ? (Color?)null : OzUi.GM950);
            var pm = Hud.Kit(flankL, "PortraitMask", "Portrait/portrait_mask", new OzRect(8, 8, OzLayout.EndFlankWell, OzLayout.EndFlankWell), null, Image.Type.Simple);
            if (OzKit.GetSprite("Portrait/portrait_mask") != null) pm.gameObject.AddComponent<Mask>().showMaskGraphic = false; else pm.color = OzUi.GM950;
            Hud.Kit(pm.transform, "Bg", "Portrait/portrait_bg_" + (game.level == "L17" ? "city" : "desert"), new OzRect(0, 0, OzLayout.EndFlankWell, OzLayout.EndFlankWell), null, Image.Type.Simple);
            var art = Hud.Node("Art", pm.transform); art.Fill(); var ri = art.gameObject.AddComponent<RawImage>(); ri.raycastTarget = false;
            ri.texture = UnitArt.Portrait(face, me); ri.enabled = ri.texture != null;
            Hud.Kit(flankL, "Frame", "Portrait/portrait_frame", new OzRect(8, 8, OzLayout.EndFlankWell, OzLayout.EndFlankWell), null, Image.Type.Simple).enabled = OzKit.Available;
            Hud.Label(flankL, "Name", UnitInfo.Name(face), 11, OzUi.Ink, 0.16f).At(OzLayout.EndFlankW * 0.5f, 116, OzH.C, false, OzLayout.EndFlankW - 16f);
            Hud.Label(flankL, "Comms", "COMMS · " + TeamColors.Name(me), 8, OzUi.Muted, 0.24f, OzStyle.Plain).At(OzLayout.EndFlankW * 0.5f, 133, OzH.C, false, OzLayout.EndFlankW - 16f);
            flankR = Flank(new OzRect(OzAnchor.TC, cx + OzLayout.EndFlankDX - OzLayout.EndFlankW, OzLayout.EndFlankY, OzLayout.EndFlankW, OzLayout.EndFlankH));
            if (winner != Team.Neutral) Hud.Kit(flankR, "Emblem", "Bits/emblem_" + TeamColors.Key(winner) + "_lg", new OzRect(8, 8, 96, 96), null, Image.Type.Simple);
            else Hud.Kit(flankR, "Emblem", "Bits/emblem_neutral", new OzRect(20, 20, 72, 72), null, Image.Type.Simple);
            Hud.Label(flankR, "Force", winner == Team.Neutral ? "NO FORCE" : TeamColors.Name(winner) + " FORCE", 11, winner == Team.Neutral ? (Color)OzUi.Ink2 : OzUi.TeamText(winner), 0.16f).At(OzLayout.EndFlankW * 0.5f, 116, OzH.C, false, OzLayout.EndFlankW - 16f);
            var holds = Hud.Label(flankR, "Holds", "HOLDS THE FIELD", 8, OzUi.Muted, 0.12f, OzStyle.Plain).At(OzLayout.EndFlankW * 0.5f, 133, OzH.C, false, OzLayout.EndFlankW - 16f);

            // eyebrow chip, wordmark (glow > face > flash), subtitle
            eyebrowRt = Hud.Node("Eyebrow", root).Place(new OzRect(OzAnchor.TC, cx - OzLayout.EndChipW * 0.5f, ChipY, OzLayout.EndChipW, OzLayout.EndChipH));
            var chip = Hud.Chip(eyebrowRt, "Chip", new OzRect(0, 0, OzLayout.EndChipW, OzLayout.EndChipH), "OPERATION " + lvlCode, OzUi.GM950, accent, 11, 0.24f, accent);
            eyebrowBg = chip.bg; eyebrow = chip.text;
            var face0 = OzKit.GetSprite("Wordmarks/wm_" + kind + "_face");
            Vector2 wmSize = face0 != null ? new Vector2(face0.rect.width, face0.rect.height) * 0.5f : new Vector2(700, 160);
            wmRoot = Hud.Node("Wordmark", root);
            wmRoot.anchorMin = wmRoot.anchorMax = new Vector2(0.5f, 1); wmRoot.pivot = new Vector2(0.5f, 0.5f);
            wmRoot.anchoredPosition = new Vector2(0, -WordmarkMid); wmRoot.sizeDelta = wmSize;
            wmGlow = Hud.Kit(wmRoot, "Glow", "Wordmarks/wm_" + kind + "_glow", new OzRect(0, 0, wmSize.x, wmSize.y), null, Image.Type.Simple);
            wmFace = Hud.Kit(wmRoot, "Face", "Wordmarks/wm_" + kind + "_face", new OzRect(0, 0, wmSize.x, wmSize.y), null, Image.Type.Simple);
            var sheenMask = Hud.Kit(wmRoot, "SheenMask", "Wordmarks/wm_" + kind + "_mask", new OzRect(0, 0, wmSize.x, wmSize.y), null, Image.Type.Simple);
            if (OzKit.GetSprite("Wordmarks/wm_" + kind + "_mask") != null) sheenMask.gameObject.AddComponent<Mask>().showMaskGraphic = false; else sheenMask.enabled = false;
            sheen = Hud.Kit(sheenMask.transform, "Sheen", "Bits/glass_sheen", new OzRect(0, 0, 180, wmSize.y), OzUi.A(Color.white, 0.55f), Image.Type.Simple);
            sheen.rectTransform.localRotation = Quaternion.Euler(0, 0, -18f); sheen.enabled = false;
            wmFlash = Hud.Kit(wmRoot, "Flash", "Wordmarks/wm_" + kind + "_mask", new OzRect(0, 0, wmSize.x, wmSize.y), OzUi.A(Color.white, 0f), Image.Type.Simple);
            if (face0 == null)
            {
                wmFace.enabled = wmGlow.enabled = wmFlash.enabled = false;
                Hud.Label(wmRoot, "Text", kind.ToUpperInvariant(), 118, win ? (Color)OzUi.AmberHi : (Color)OzUi.Ink, -0.015f, OzStyle.Glow).FillParent(OzH.C);
            }
            string sub = win ? $"{TeamColors.Name(me)} FORCES HOLD {level}"
                       : kind == "defeat" ? $"{(W != null && hud.Stats.FortLost.Contains(me) ? "YOUR FORT HAS FALLEN" : "YOUR FORCES ARE GONE")} · {TeamColors.Name(enemy)} HOLDS {level.Replace("THE ", "THE ")}"
                       : "STALEMATE · NO FORCES LEFT";
            subtitle = Hud.Label(root, "Subtitle", sub, OzLayout.EndSubtitleSize, win ? (Color)OzUi.Ink : kind == "defeat" ? (Color)OzUi.AlertText : (Color)OzUi.Ink2, 0.22f);
            subtitle.rectTransform.anchorMin = subtitle.rectTransform.anchorMax = new Vector2(0.5f, 1);
            subtitle.At(0, OzLayout.EndSubtitleMid, OzH.C, true, 1400);
            subtitle.rectTransform.anchorMin = subtitle.rectTransform.anchorMax = new Vector2(0.5f, 1); subtitle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            subtitle.rectTransform.anchoredPosition = new Vector2(0, -OzLayout.EndSubtitleMid);

            // stat plates
            var st = hud.Stats;
            float sx0 = cx - (4 * OzLayout.EndStatW + 3 * OzLayout.EndStatGap) * 0.5f;
            int zonesTotal = W.Zones.Count, zonesMine = 0; foreach (var z in W.Zones) if (z.Owner == me) zonesMine++;
            float speed = game.gameSpeed;
            Stat(0, sx0, "clock", "MISSION TIME", (float)W.Time, v => UnitInfo.Duration(v), $"GAME TIME · SPEED {speed:0.##}×", accent);
            Stat(1, sx0, "factory", "UNITS BUILT", OpenZMatchStats.Get(st.Built, me), v => Mathf.RoundToInt(v).ToString(), $"{TeamColors.Name(enemy)} BUILT {OpenZMatchStats.Get(st.Built, enemy)}", accent);
            Stat(2, sx0, "flag", "TERRITORIES", zonesMine, v => $"{Mathf.RoundToInt(v)}/{zonesTotal}", $"PEAK {OpenZMatchStats.Get(st.PeakZones, me)} · {TeamColors.Name(enemy)} PEAK {OpenZMatchStats.Get(st.PeakZones, enemy)}", accent);
            Stat(3, sx0, "crosshair", "KILLS", OpenZMatchStats.Get(st.Kills, me), v => Mathf.RoundToInt(v).ToString(), $"LOSSES {OpenZMatchStats.Get(st.Losses, me)}", accent);

            // forces chart
            chartRt = Hud.Node("Chart", root).Place(new OzRect(OzAnchor.TC, sx0, OzLayout.EndChartY, OzLayout.EndChartW, OzLayout.EndChartH));
            if (OzKit.Available) Hud.Shadow(chartRt, "Panels/panel_lg_mask");
            var cp = Hud.Kit(chartRt, "Plate", "EndScreen/chart_plate", new OzRect(0, 0, OzLayout.EndChartW, OzLayout.EndChartH), null);
            if (!OzKit.Available) cp.color = HudStyle.Slate;
            Hud.Label(chartRt, "Title", "FORCES OVER TIME", 11, OzUi.Ink2, 0.2f, OzStyle.Plain).At(16, 14);
            Hud.Chip(chartRt, "Red", new OzRect(OzLayout.EndChartW - 16 - 58, 12, 58, 16), TeamColors.Name(Team.Red), OzUi.TeamPaint(Team.Red), OzUi.Ink, 9, 0.16f);
            Hud.Chip(chartRt, "Blue", new OzRect(OzLayout.EndChartW - 80 - 58, 12, 58, 16), TeamColors.Name(Team.Blue), OzUi.TeamPaint(Team.Blue), OzUi.Ink, 9, 0.16f);
            var gr = OzLayout.EndChartGraph;
            var cg = Hud.Node("Graph", chartRt).Place(gr);
            if (!cg.GetComponent<CanvasRenderer>()) cg.gameObject.AddComponent<CanvasRenderer>();          // gate re-check: the Graph had none, so the chart never drew
            chart = cg.gameObject.AddComponent<OzChartGraphic>(); chart.raycastTarget = false;
            st.Tick(W);
            var samples = new List<(float, int, int)>(st.Samples);
            samples.Add(((float)W.Time, W.UnitCount(Team.Red), W.UnitCount(Team.Blue)));
            if (samples.Count < 2) samples.Insert(0, (0f, samples[0].Item2, samples[0].Item3));        // critic #5b: a line needs 2 points
            chart.Samples = samples; chart.Duration = Mathf.Max(1f, (float)W.Time); chart.Reveal = 0f;
            foreach (var c in st.Captures) if (c.team == winner && winner != Team.Neutral) chart.Marks.Add(c.t);
            // peak + 10 % rounded up to a nice 4-gridline step (was a fixed 70: a 6-unit fight drew two flat lines on the floor), the
            // gridlines get their counts, the time ticks sit on round times (10 s .. 30 min steps) at their true x, plus the end time
            int peak = 1; foreach (var sm in samples) peak = Mathf.Max(peak, Mathf.Max(sm.Item2, sm.Item3));
            int ystep = NiceStep(peak * 1.1f / 4f, new[] { 1, 2, 3, 5, 10, 15, 20, 25, 50, 100 });
            chart.YMax = ystep * 4;
            for (int gl = 1; gl <= 4; gl++)
                chartTicks.Add(Hud.Label(chartRt, "YTick" + gl, (ystep * gl).ToString(), 8, OzUi.Muted, 0.06f, OzStyle.Plain).At(gr.x + 4, gr.y + gr.h - gr.h * gl / 4f + 2f, OzH.L, false, 40));
            float dur = Mathf.Max(1f, (float)W.Time);
            int tstep = NiceStep(dur / 4f, new[] { 10, 15, 20, 30, 60, 120, 180, 300, 600, 900, 1200, 1800 });
            int nt = 0;
            for (float tt = 0f; tt <= dur - tstep * 0.55f && nt < 12; tt += tstep, nt++)
                chartTicks.Add(Hud.Label(chartRt, "Tick" + nt, UnitInfo.Duration(tt), 9, OzUi.Muted, 0.06f, OzStyle.Plain)
                    .At(gr.x + gr.w * tt / dur, gr.y + gr.h + 8, nt == 0 ? OzH.L : OzH.C, false, 80));
            chartTicks.Add(Hud.Label(chartRt, "TickEnd", UnitInfo.Duration(dur), 9, OzUi.Muted, 0.06f, OzStyle.Plain).At(gr.x + gr.w, gr.y + gr.h + 8, OzH.R, false, 80));
            var holdsTmp = holds != null ? holds.graphic as TMPro.TMP_Text : null;
            float holdsW = holdsTmp ? holdsTmp.GetPreferredValues(holdsTmp.text, 99999f, 0f).x : -1f;
            LayoutStatus = $"chart y 0-{chart.YMax:0} (peak {peak}, step {ystep}), time ticks every {tstep} s x {nt} + end {UnitInfo.Duration(dur)}, HOLDS THE FIELD {holdsW:0.0} u wide in the {OzLayout.EndFlankW:0} u plate (padding {(OzLayout.EndFlankW - holdsW) * 0.5f:0.0} u each side)";
            Debug.Log("[OpenZ] end screen layout: " + LayoutStatus);

            // buttons + hint
            string primary = win ? "CONTINUE" : "RETRY";
            Button(0, "REPLAY", cx - 330, 220, HudButtonStyle.Normal, OzScenes.Restart, "REPLAY|Play this level again from the start.|R");
            Button(1, primary, cx - 100, 200, HudButtonStyle.Primary, win ? (System.Action)OzScenes.Next : OzScenes.Restart,
                   win ? "CONTINUE|The next operation (Car Park after the Desert).|ENTER" : "RETRY|Fight this battle again.|ENTER");
            Button(2, "MAIN MENU", cx + 110, 220, HudButtonStyle.Normal, OzScenes.MainMenu, OzScenes.HasMenu ? "MAIN MENU|Back to the front end.|ESC" : "MAIN MENU|The front end is not in the build yet: this loads the first level.|ESC");
            focusRing = Hud.Kit(root, "Focus", "Buttons/btn_l_focus", new OzRect(OzAnchor.TC, cx - 104, OzLayout.EndButtonsY - 4, 208, 58), OzUi.Amber);
            focusRing.enabled = OzKit.Available; focus = 1;
            hint = Hud.Label(root, "Hint", $"ENTER  {primary}     R  REPLAY     ESC  MAIN MENU", 9, OzUi.Muted, 0.2f, OzStyle.Plain);
            hint.At(0, OzLayout.EndHintY, OzH.C, false, 800);
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 1); hint.rectTransform.pivot = new Vector2(0.5f, 1);
            hint.rectTransform.anchoredPosition = new Vector2(0, -(OzLayout.EndHintY + 0.3635f * 9 - 8.1f));
            Animate();
        }

        RectTransform Flank(OzRect r)
        {
            var p = Hud.Node("Flank", root).Place(r);
            Hud.Plate(p, "sm");
            p.gameObject.AddComponent<CanvasGroup>();
            return p;
        }

        static string FaceType(Team me, bool win)
        {
            var hud = OpenZHud.I;
            if (hud != null && hud.W != null)
            {
                var n = new Dictionary<string, int>();
                foreach (var o in hud.W.Objects) if (o.Kind == ZKind.Robot && o.Owner == me && !o.Destroyed) n[o.Type] = n.TryGetValue(o.Type, out var c) ? c + 1 : 1;
                string best = null; int bn = 0; foreach (var kv in n) if (kv.Value > bn) { best = kv.Key; bn = kv.Value; }
                if (best != null) return best;
            }
            return win ? "grunt" : "psycho";
        }

        void Stat(int i, float sx0, string glyph, string label, float value, System.Func<float, string> fmt, string foot, Color accent)
        {
            var p = Hud.Node("Stat" + i, root).Place(new OzRect(OzAnchor.TC, sx0 + i * (OzLayout.EndStatW + OzLayout.EndStatGap), OzLayout.EndStatY, OzLayout.EndStatW, OzLayout.EndStatH));
            if (OzKit.Available) Hud.Shadow(p, "Panels/panel_md_mask");
            var bg = Hud.Kit(p, "Plate", win ? "EndScreen/stat_plate" : "EndScreen/stat_plate_defeat", new OzRect(0, 0, OzLayout.EndStatW, OzLayout.EndStatH), null);
            if (!OzKit.Available) bg.color = HudStyle.Slate;
            Hud.Icon(p, "Glyph", glyph, 16, 16, 20, accent);
            Hud.Label(p, "Label", label, 11, OzUi.Muted, 0.22f, OzStyle.Plain).At(44, 21, OzH.L, false, 180);
            var v = Hud.Label(p, "Value", fmt(0), 48, OzUi.Ink, -0.01f).At(16, 48, OzH.L, false, 210);
            Hud.Label(p, "Foot", foot, 10, OzUi.Muted, 0.16f, OzStyle.Plain).At(16, 110, OzH.L, false, 210);
            var fl = Hud.Kit(p, "Flash", "Panels/panel_md_mask", new OzRect(0, 0, OzLayout.EndStatW, OzLayout.EndStatH), OzUi.A(win ? OzUi.AmberHi : OzUi.Alert, 0f));
            p.gameObject.AddComponent<CanvasGroup>();
            plates.Add(p); plateValues.Add(v); plateFlash.Add(fl); plateFormat.Add(fmt); plateTarget.Add(value); plateShown.Add(0f);
        }

        void Button(int i, string label, float x, float w, HudButtonStyle style, System.Action act, string tip)
        {
            var b = HudButton.Make(root, "Btn" + label, label, new Vector2(w, OzLayout.EndButtonH), HudButtonKind.Large, style == HudButtonStyle.Primary ? 16 : 15, 0.16f, style, true);
            b.Rt.Place(new OzRect(OzAnchor.TC, x, OzLayout.EndButtonsY, w, OzLayout.EndButtonH));
            b.Priority = 100;                                                                           // the HUD tooltip draws under this canvas: keys are in the hint line
            b.OnClick = c => { if (Revealed && OzMotion.Now - skipAt > 0.3f) { OzSfx.Ui(i == 2 ? "ui_back" : "ui_confirm"); act(); } else Skip(); };
            b.Rt.gameObject.AddComponent<CanvasGroup>();
            buttons.Add(b);
        }

        bool Revealed => skipped || OzMotion.Now - t0 > 3.45f;
        void Skip() { if (!skipped) { skipAt = OzMotion.Now; OzSfx.Ui("ui_cancel"); } skipped = true; }   // sound_v3

        void HandleKeys()
        {
            var kb = Keyboard.current; var m = Mouse.current;
            if (!Revealed)
            {
                if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) || (m != null && m.leftButton.wasPressedThisFrame)) Skip();
                return;
            }
            if (kb == null) return;
            int v3f = focus;
            if (kb.leftArrowKey.wasPressedThisFrame) focus = Mathf.Max(0, focus - 1);
            if (kb.rightArrowKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame) focus = (focus + 1) % buttons.Count;
            if (focus != v3f) OzSfx.Ui("ui_hover");                                                    // sound_v3
            if (OzMotion.Now - skipAt < 0.3f) return;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) { OzSfx.Ui("ui_click"); buttons[focus].OnClick?.Invoke(default); }
            else if (kb.rKey.wasPressedThisFrame) { OzSfx.Ui("ui_click"); buttons[0].OnClick?.Invoke(default); }
            else if (kb.escapeKey.wasPressedThisFrame) { OzSfx.Ui("ui_click"); buttons[2].OnClick?.Invoke(default); }
        }

        // ------------------------------------------------------------------ reveal timeline (guide 9.13), unscaled seconds from game over
        float T(float at, float dur) => skipped ? 1f : OzMotion.T(t0 + at, dur);

        void Animate()
        {
            float now = OzMotion.Now, e = skipped ? 99f : now - t0;
            OzSoundV3.EndScreen(t0, now - t0, skipped, plates.Count);                                  // sound_v3: band, plates, chart, buttons
            float kdim = OzEase.InOutSine(T(0f, OzMotion.Dur(DimEase, true)));
            if (post != null) post.Weight = kdim;
            if (dimImage) SetA(dimImage, dimTarget * kdim);
            // 0.30 band opens from its centre line (240 ms); hazard bands slide in (300 ms) and scroll 20 u/s
            float kb = OzEase.OutCubic(T(0.3f, OzMotion.Dur(0.24f)));
            maskBox.sizeDelta = new Vector2(maskBox.sizeDelta.x, Mathf.Lerp(0f, OzLayout.RefH + 400f, kb * kb));
            if (hazardTop)
            {
                float kh = OzEase.OutCubic(T(0.3f, OzMotion.Dur(0.3f)));
                Hazard(hazardTop, 1f, kh, now); Hazard(hazardBottom, -1f, kh, now);                        // no tuple array per frame
            }
            // 0.45 eyebrow chip drops 8 u + fades
            float ke = OzEase.OutCubic(T(0.45f, OzMotion.Dur(OzMotion.Short)));
            eyebrowRt.Place(new OzRect(OzAnchor.TC, OzLayout.RefW * 0.5f - OzLayout.EndChipW * 0.5f, ChipY - 8f * (1f - ke), OzLayout.EndChipW, OzLayout.EndChipH));
            SetGroupAlpha(eyebrowRt, ke);
            // 0.55 stamp: scale 1.6 -> 1 + alpha (easeOutBack 1.4), white flash 90 ms, camera shake; defeat flickers
            float ks = T(0.55f, OzMotion.Dur(OzMotion.Stamp));
            float scale = ks <= 0f ? 1.6f : Mathf.Lerp(1.6f, 1f, OzEase.OutBack(ks, 1.4f));
            wmRoot.localScale = Vector3.one * scale;
            float faceA = Mathf.Clamp01(ks * 3f);
            if (kind == "defeat" && !skipped && !OzMotion.Reduced) { float f = e - 0.55f - 0.22f; if (f > 0f && f < 0.12f) faceA = f < 0.04f ? 1f : f < 0.08f ? 0.3f : 1f; }
            SetA(wmFace, faceA);
            float flash = skipped || ks < 1f ? 0f : 1f - Mathf.Clamp01((e - 0.55f - 0.22f) / 0.09f);
            SetA(wmFlash, ks >= 1f ? flash : 0f);
            if (!shakeDone && ks >= 1f && !skipped)
            {
                shakeDone = true;
                OzSfx.Ui("ui_stamp", 0.8f);
                var ss = PlayerPrefs.GetInt("oz_shake", 1) == 1 && !OzMotion.Reduced;
                if (ss && hud.rts) hud.rts.Shake(win ? 0.15f : 0.25f);
            }
            // 0.60-1.50 glow swells 0 -> 110 -> 60 (of 255); victory: a diagonal sheen sweeps across the word (900 ms)
            float kg = T(0.6f, OzMotion.Dur(0.9f, true));
            float glow = kg < 0.45f ? Mathf.Lerp(0f, 110f, kg / 0.45f) : Mathf.Lerp(110f, 60f, (kg - 0.45f) / 0.55f);
            if (kind == "defeat") glow = kg < 0.5f ? Mathf.Lerp(0f, 100f, kg / 0.5f) : Mathf.Lerp(100f, 60f, (kg - 0.5f) / 0.5f);
            SetA(wmGlow, glow / 255f * 2.2f);
            float ksh = T(0.6f, OzMotion.Dur(0.9f));
            sheen.enabled = win && ksh > 0f && ksh < 1f && sheen.sprite != null;
            if (sheen.enabled) { var w = wmRoot.sizeDelta.x; sheen.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-220f, w + 40f, OzEase.InOutSine(ksh)), 0f); }
            // 0.90 subtitle tracks in (+0.5 em -> +0.22 em, alpha)
            float kt = OzEase.OutCubic(T(0.9f, OzMotion.Dur(0.4f)));
            subtitle.Tracking = Mathf.Lerp(0.5f, 0.22f, kt); subtitle.Alpha = kt;
            // flanking plates with the band
            SetGroupAlpha(flankL, OzEase.OutCubic(T(0.55f, OzMotion.Dur(0.26f, true)))); SetGroupAlpha(flankR, OzEase.OutCubic(T(0.55f, OzMotion.Dur(0.26f, true))));
            // 1.30 stat plates rise 24 u + fade (260 ms, 110 ms stagger); values tick 0 -> final over 900 ms, flash when they land
            for (int i = 0; i < plates.Count; i++)
            {
                float at = 1.3f + 0.11f * i, kp = OzEase.OutCubic(T(at, OzMotion.Dur(0.26f)));
                var r = plates[i]; var home = new OzRect(OzAnchor.TC, OzLayout.RefW * 0.5f - (4 * OzLayout.EndStatW + 3 * OzLayout.EndStatGap) * 0.5f + i * (OzLayout.EndStatW + OzLayout.EndStatGap), OzLayout.EndStatY + 24f * (1f - kp), OzLayout.EndStatW, OzLayout.EndStatH);
                r.Place(home); SetGroupAlpha(r, kp);
                float kv = OzEase.OutQuart(T(at + 0.1f, OzMotion.Dur(0.9f)));
                float pv = plateTarget[i] * kv;                                                             // format only while the number ticks
                if (pv != plateShown[i]) { if (!skipped && plateFormat[i](pv) != plateFormat[i](plateShown[i])) OzSfx.Ui("ui_count_tick"); plateShown[i] = pv; plateValues[i].Set(OzText.Tab(plateFormat[i](pv))); }
                float kf = T(at + 1.0f, OzMotion.Dur(0.12f, true));
                SetA(plateFlash[i], kf > 0f && kf < 1f ? 0.35f * (1f - kf) : 0f);
            }
            // 2.20 chart plate rises; lines draw left -> right over 1.1 s
            float kc = OzEase.OutCubic(T(2.2f, OzMotion.Dur(0.26f)));
            chartRt.Place(new OzRect(OzAnchor.TC, OzLayout.RefW * 0.5f - OzLayout.EndChartW * 0.5f, OzLayout.EndChartY + 24f * (1f - kc), OzLayout.EndChartW, OzLayout.EndChartH));
            SetGroupAlpha(chartRt, kc);
            float reveal = T(2.3f, OzMotion.Dur(1.1f));
            if (!Mathf.Approximately(chart.Reveal, reveal)) { chart.Reveal = reveal; chart.SetVerticesDirty(); }
            // 3.20 buttons fade in (200 ms, 60 ms stagger); focus on CONTINUE / RETRY
            for (int i = 0; i < buttons.Count; i++) SetGroupAlpha(buttons[i].Rt, T(3.2f + 0.06f * i, OzMotion.Dur(0.2f, true)));
            float kbtn = T(3.2f, OzMotion.Dur(0.2f, true));
            if (focusRing)
            {
                var fb = buttons[Mathf.Clamp(focus, 0, buttons.Count - 1)].Rt;
                focusRing.rectTransform.anchorMin = fb.anchorMin; focusRing.rectTransform.anchorMax = fb.anchorMax; focusRing.rectTransform.pivot = fb.pivot;
                focusRing.rectTransform.anchoredPosition = fb.anchoredPosition + new Vector2(-4, 4); focusRing.rectTransform.sizeDelta = fb.sizeDelta + new Vector2(8, 8);
                SetA(focusRing, kbtn * (0.75f + 0.25f * Mathf.Sin(now * 4f)));
            }
            hint.Alpha = kbtn;
        }

        static void Hazard(RawImage hz, float dir, float kh, float now)
        {
            if (!hz) return;
            hz.color = OzUi.A(Color.white, kh);
            hz.rectTransform.anchoredPosition = new Vector2(dir * (1f - kh) * -600f, hz.rectTransform.anchoredPosition.y);
            if (hz.texture) { var uv = hz.uvRect; uv.x = (dir * now * 20f * 2f / hz.texture.width) % 1f; hz.uvRect = uv; }   // 20 u/s, opposite directions
        }

        static int NiceStep(float raw, int[] steps) { foreach (var st in steps) if (st >= raw) return st; return steps[steps.Length - 1]; }
        static void SetA(Graphic g, float a) { if (!g) return; var c = g.color; if (Mathf.Abs(c.a - a) > 0.003f) { c.a = a; g.color = c; } }
        static void SetGroupAlpha(RectTransform rt, float a) { var cg = rt ? rt.GetComponent<CanvasGroup>() : null; if (!cg && rt) cg = rt.gameObject.AddComponent<CanvasGroup>(); if (cg && Mathf.Abs(cg.alpha - a) > 0.003f) cg.alpha = a; }

        void OnDestroy() { post?.Destroy(); AnyShown = false; }
    }
}
