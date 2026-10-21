using System.Collections.Generic;
using OpenZ.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// Zod's building timer (zbuilding.cpp:516-542, brobot.cpp:166-169 / 320-340) and Zod's level numeral
    /// (level_img, brobot.cpp:377-380, bvehicle.cpp:381) as ONE object, the design panel's "dog tag": a raised medallion whose rim IS
    /// the progress ring, the unit's own production-card art cut into its face, fused to a chamfered gunmetal plate with two rows: the
    /// time in a recessed glass well (the mission clock's), and under it "TECH [n]", the production window's word with the level
    /// numeral in a socket (n = BuildLevel + 1 = the window's lit TECH pips). A cue flag on the plate's top edge says PAUSED / PLACE
    /// while the time keeps running (Zod shows the time whenever the building is not in SELECT, bfort.cpp:191-194).
    /// One silhouette, one keyline, one bevel, one shadow; the kit's live tab on the top edge; the owner stripe at the plate's end;
    /// a pointer notch at the building. Factories without a badge (neutral, idle enemy, destroyed) keep their level as a small
    /// "TECH [n]" tag (OzLevelTag), as Zod draws level_img on every factory.
    ///
    /// Art: Resources/OpenZ/ProductionBadge/x1..x4 (tools/bake_badge.py, from the Tag* numbers in OzLayout), one tier per texture
    /// px per canvas u; the badge takes the smallest tier at or above its on-screen scale, so sprites are 1:1 at 1920x1080 UI 100 %
    /// and at 2560x1600 UI 135 % and never magnified. Positions are whole u from the medallion centre (whole pixels at k 1 and 2).
    /// Everything here is view-only: it reads the Sim (ZObject production fields, the Sim's UnitBuilt / CannonStored events), never
    /// writes it.
    /// </summary>
    public static class OzBadgeArt
    {
        public const string Root = "OpenZ/ProductionBadge/x";
        public const int MaxTier = 4;
        static readonly Dictionary<string, Sprite>[] cache = { null, new Dictionary<string, Sprite>(), new Dictionary<string, Sprite>(), new Dictionary<string, Sprite>(), new Dictionary<string, Sprite>() };
        static bool warnedSize, warnedMissing; static int available = -1;

        /// <summary>Smallest tier at or above k px per u (1..4): never magnified; exactly 1:1 at the defaults (1080p 100 %, 1600p 135 %),
        /// otherwise minified by 1.1 - 1.8x (bilinear, no mips: the rule asked for UI textures).</summary>
        public static int Tier(float k) => Mathf.Clamp(Mathf.CeilToInt(k - 0.05f), 1, MaxTier);

        /// <summary>Is the badge art imported (checked once)? Without it the badge falls back to a flat plate with plain text and never
        /// enables an Image whose sprite is missing (no white quads).</summary>
        public static bool Available
        {
            get
            {
                if (available < 0) available = Get(1, "body_l") != null || Get(2, "body_l") != null ? 1 : 0;
                return available == 1;
            }
        }

        /// <summary>Sprite of a layer at tier t (another tier's file when that one is missing; null when none exists).
        /// Built with Sprite.Create (FullRect, centre pivot), so it works whatever the import type; the u size always comes from
        /// the layout, never from the texture, so a wrong import (NPOT scaled, capped) keeps the geometry and is reported once.</summary>
        public static Sprite Get(int t, string name)
        {
            t = Mathf.Clamp(t, 1, MaxTier);
            var d = cache[t];
            if (d.TryGetValue(name, out var s)) return s;
            Texture2D tex = null; int from = t;
            for (int i = 0; i < MaxTier * 2 && tex == null; i++)
            {
                int c = i == 0 ? t : (i % 2 == 1 ? t + (i + 1) / 2 : t - i / 2);                       // t, t+1, t-1, t+2 ...
                if (c < 1 || c > MaxTier) continue;
                tex = Resources.Load<Texture2D>(Root + c + "/" + name); from = c;
            }
            if (tex != null)
            {
                tex.wrapMode = TextureWrapMode.Clamp;
                if (tex.filterMode != FilterMode.Bilinear) tex.filterMode = FilterMode.Bilinear;         // tiers are 1:1 or slightly minified: no trilinear blur
                s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f * from, 0, SpriteMeshType.FullRect);
                s.name = "OzBadge_" + name + "_x" + from;
                var eu = ExpectedU(name);
                if (!warnedSize && (tex.mipmapCount > 1 || (eu.x > 0f && (tex.width != Mathf.RoundToInt(eu.x * from) || tex.height != Mathf.RoundToInt(eu.y * from)))))
                {
                    warnedSize = true;
                    Debug.LogWarning("[OzBadge] " + name + " x" + from + " imported as " + tex.width + "x" + tex.height + (tex.mipmapCount > 1 ? " with mip maps" : "") +
                                     ": run OpenZ / UI / Production Badge / Reimport Art (the badge art must import uncompressed, no mips, NPOT kept)");
                }
            }
            else if (!warnedMissing) { warnedMissing = true; Debug.LogWarning("[OzBadge] missing art Resources/" + Root + t + "/" + name + " (badge art not imported); the badge falls back to a flat plate"); }
            d[name] = s;
            return s;
        }

        public static string TeamName(Team t) => TeamColors.Shown(t) == Team.Blue ? "blue" : "red";

        /// <summary>Size in u of a layer (what OzProductionBadge draws it at; texture px = u x tier), zero when unknown.</summary>
        public static Vector2 ExpectedU(string name)
        {
            float F = OzLayout.TagFrame, H = OzLayout.TagBodyH, FH = OzLayout.TagFlagH + 4f;
            switch (name)
            {
                case "body_l": return new Vector2(F + OzLayout.TagSliceL, H);
                case "body_m": return new Vector2(4, H);
                case "body_r": return new Vector2(OzLayout.TagSliceR + OzLayout.TagBodyRX, H);
                case "body_c": return new Vector2(2f * F, H);
                case "tab": return OzLayout.TagTabBox.Size;
                case "notch": return OzLayout.TagNotchBox.Size;
                case "tech": return OzLayout.TagTechBox.Size;
                case "lvltag": return OzLayout.TagLevelTagBox.Size;
                case "lvltag_c": return OzLayout.TagLevelTagCBox.Size;
                case "stripe": return new Vector2(OzLayout.TagStripeW, OzLayout.TagStripeH);
                case "flag_l": case "flagfill_l": return new Vector2(OzLayout.TagFlagEnd + 1f, FH);
                case "flag_m": case "flagfill_m": return new Vector2(4f, FH);
                case "flag_r": case "flagfill_r": return new Vector2(OzLayout.TagFlagEnd + 3f, FH);
                case "ring_glow": return Vector2.one * 2f * OzLayout.TagGlow;
            }
            if (name.StartsWith("num_", System.StringComparison.Ordinal)) return OzLayout.TagLvlBox.Size;
            if (name.StartsWith("ring_", System.StringComparison.Ordinal)) return Vector2.one * 2f * F;
            if (name.StartsWith("face_", System.StringComparison.Ordinal)) return Vector2.one * OzLayout.TagFaceBox;
            return Vector2.zero;
        }

        /// <summary>The unit's face: robots = the BS-01 portrait (team), vehicles / cannons = the production-card icon (the card art the
        /// player picked, red like the production window uses for both teams); else the class glyph.</summary>
        public static string FaceFor(string unit, Team team)
        {
            if (string.IsNullOrEmpty(unit)) return "face_glyph_robot";
            bool art = OpenZUnitOverlay.TimerPortrait > 0f;
            var key = (unit, TeamColors.Shown(team), art);                                             // swap-safe cache key
            if (faceCache.TryGetValue(key, out var f)) return f;                                      // no per-frame string building
            string k = !art ? null : ZBuildLists.IsRobot(unit) ? "face_" + unit + "_" + TeamName(team) : "face_" + unit + "_red";
            f = k != null && (Get(1, k) != null || Get(2, k) != null) ? k : "face_glyph_" + UnitInfo.ClassGlyph(unit);
            faceCache[key] = f;
            return f;
        }
        static readonly Dictionary<(string, Team, bool), string> faceCache = new Dictionary<(string, Team, bool), string>();
        public const string EmblemRed = "face_emblem_red", EmblemBlue = "face_emblem_blue", IdleFort = "face_glyph_fort", IdleFactory = "face_glyph_factory";
        public static string Emblem(Team t) => TeamColors.Shown(t) == Team.Blue ? EmblemBlue : EmblemRed;

        static readonly string[,] nums = new string[8, 3];
        /// <summary>
        /// col 0 amber (own) / 1 ink (enemy) / 2 dim (neutral, destroyed).</summary>
        public static string Num(int n, int col)
        {
            n = Mathf.Clamp(n, 1, 7); col = Mathf.Clamp(col, 0, 2);
            return nums[n, col] ??= "num_" + n + "_" + (col == 0 ? "amber" : col == 1 ? "ink" : "dim");
        }
    }

    /// <summary>What one badge shows this frame. OzBadgeState builds it from the Sim (read only) and the animation clocks;
    /// OzProductionBadge draws it. tools/compose_badge.py composites the same fields for the previews.</summary>
    public struct OzBadgeView
    {
        public float plateW, scale, alpha, compact;
        public string fillKey; public float fill, fillAlpha;
        public string fill2Key; public float fill2, fill2Alpha;
        public float white, hazard;
        public bool cap; public Color capTint;
        public float sheen, sheenAlpha;
        public string face, faceNext; public float faceAlpha, faceReveal, scan, rim;           // scan < 0 = no scan band; rim = READY edge flash
        public float glow, glowScale; public Color glowTint;
        public Color tab, notch, stripe;                                                        // a 0 = off
        public int level, levelCol;                                                             // TECH n (1..7, drawn I..VII), numeral colour 0 amber / 1 ink / 2 dim
        public string flag; public Color flagText, flagFill; public float flagAlpha;             // the cue flag (PAUSED / PLACE), null = none
        public string digits; public Color digTop, digBot; public float digAlpha, digScale;
        public string word; public Color wordCol; public float wordAlpha, wordScale, wordTrack;
        public float wipe;                                                                      // < 0 = none; 0..1 = READY -> time wipe edge across the well
    }

    /// <summary>One pooled badge: the uGUI stack (back to front) body_c (compact) / body_l / body_m (stretched) / body_r, stripe, tech row
    /// + level numeral, tab, cue flag (3-slice + fill + text), notch glow, hazard, fill, fill2, white-hot, sheen, cap halo + cap, face,
    /// next face (Filled Vertical reveal), scan band, rim flash, glow, and the well's two RectMask2D clips (digits | word, split by the
    /// READY wipe edge) with the wipe line. Root pivot = the medallion centre; a CanvasGroup carries the enter / exit fade.</summary>
    public sealed partial class OzProductionBadge
    {
        public readonly RectTransform Rt;
        readonly CanvasGroup cg;
        readonly Image bodyC, bodyL, bodyM, bodyR, stripe, tech, num, tab, flagL, flagM, flagR, flagFL, flagFM, flagFR, notch, hazard, fill, fill2, white, sheen, capHalo, cap, face, faceNext, scan, rim, glow, wipeLine, flat;
        readonly RectTransform capPivot, digitsClip, wordClip;
        readonly RectMask2D digitsMask, wordMask;
        readonly OzText digits, word, flagText, flatTech;
        readonly bool art;
        int tier = -1;
        string fillKey, fill2Key, faceKey, faceNextKey, numKey, flagSet;
        string digitsSet, wordSet; float wordTrackSet = -1f, wordW, digW, flagW;
        Color digTopSet, digBotSet;
        float plateSet = -1f, layoutW = -1f, layoutNeed = -1f, needW = OzLayout.TagPlateW, flagLaid = -1f; int flatLevel = -1;
        Vector4 dPadSet = new Vector4(-1, -1, -1, -1), wPadSet = new Vector4(-1, -1, -1, -1);

        const float F = OzLayout.TagFrame;                                   // design origin inside the root: (F, F) from its top-left
        static float FW => F + OzLayout.TagMaxW + OzLayout.TagBodyRX;
        const float FH = OzLayout.TagBodyH;
        public float NeedW => needW;

        public OzProductionBadge(Transform parent)
        {
            art = OzBadgeArt.Available;
            Rt = Hud.Node("Timer", parent);
            Rt.anchorMin = Rt.anchorMax = Vector2.zero;
            Rt.sizeDelta = new Vector2(FW, FH);
            Rt.pivot = new Vector2(F / FW, 1f - F / FH);
            cg = Rt.gameObject.AddComponent<CanvasGroup>(); cg.interactable = false; cg.blocksRaycasts = false;
            float top = OzLayout.TagBodyTop, H = OzLayout.TagBodyH;
            flat = Layer("Flat", -OzLayout.TagR, -OzLayout.TagPlateH * 0.5f, OzLayout.TagR + OzLayout.TagPlateW, OzLayout.TagPlateH);
            flat.color = OzUi.A(OzUi.GM950, 0.92f);
            bodyC = Layer("BodyC", -F, top, 2f * F, H);
            bodyL = Layer("BodyL", -F, top, F + OzLayout.TagSliceL, H);
            bodyM = Layer("BodyM", OzLayout.TagSliceL, top, 4, H);
            bodyR = Layer("BodyR", OzLayout.TagPlateW - OzLayout.TagSliceR, top, OzLayout.TagSliceR + OzLayout.TagBodyRX, H);
            stripe = Layer("Stripe", 0, OzLayout.TagStripeY, OzLayout.TagStripeW, OzLayout.TagStripeH);
            var tb = OzLayout.TagTechBox; tech = Layer("Tech", 0, OzLayout.TagTechY + tb.y, tb.w, tb.h);
            var lb = OzLayout.TagLvlBox; num = Layer("Level", 0, OzLayout.TagTechY + lb.y, lb.w, lb.h);
            var tab0 = OzLayout.TagTabBox; tab = Layer("Tab", tab0.x, tab0.y, tab0.w, tab0.h);
            float fy = OzLayout.TagFlagY - 1f, fh = OzLayout.TagFlagH + 4f, fe = OzLayout.TagFlagEnd;
            flagL = Layer("FlagL", OzLayout.TagFlagX - 1f, fy, fe + 1f, fh); flagM = Layer("FlagM", OzLayout.TagFlagX + fe, fy, 4f, fh); flagR = Layer("FlagR", OzLayout.TagFlagX + fe + 4f, fy, fe + 3f, fh);
            flagFL = Layer("FlagFillL", OzLayout.TagFlagX - 1f, fy, fe + 1f, fh); flagFM = Layer("FlagFillM", OzLayout.TagFlagX + fe, fy, 4f, fh); flagFR = Layer("FlagFillR", OzLayout.TagFlagX + fe + 4f, fy, fe + 3f, fh);
            flagText = Hud.Label(Rt, "FlagText", "", OzLayout.TagFlagSize, OzUi.Signal, OzLayout.TagFlagTrack, OzStyle.Plain);
            var nb = OzLayout.TagNotchBox; notch = Layer("Notch", nb.x, nb.y, nb.w, nb.h);
            hazard = Ring("Hazard", F); Radial(hazard, false);
            fill = Ring("Fill", F); Radial(fill, true);
            fill2 = Ring("Fill2", F); Radial(fill2, true);
            white = Ring("WhiteHot", F);
            sheen = Ring("Sheen", F);
            capPivot = Hud.Node("CapPivot", Rt); Centre(capPivot, 0, 0, 2 * F, 2 * F);
            capHalo = Hud.Img(capPivot, "CapHalo", Color.white); capHalo.rectTransform.Fill();
            cap = Hud.Img(capPivot, "Cap", Color.white); cap.rectTransform.Fill();
            float fb = OzLayout.TagFaceBox;
            face = Ring("Face", fb * 0.5f);
            faceNext = Ring("FaceNext", fb * 0.5f);
            faceNext.type = Image.Type.Filled; faceNext.fillMethod = Image.FillMethod.Vertical; faceNext.fillOrigin = (int)Image.OriginVertical.Top;
            scan = Hud.Img(Rt, "Scan", Color.white);
            var sb = OzKit.GetSprite("Bits/scan_band"); if (sb) scan.sprite = sb;
            rim = Ring("Rim", fb * 0.5f);
            glow = Ring("Glow", OzLayout.TagGlow);
            // the well: two RectMask2D clips over the whole frame (padding = the clip rect), digits left of the wipe edge, word right of it
            digitsClip = Hud.Node("DigitsClip", Rt); digitsClip.Fill(); digitsMask = digitsClip.gameObject.AddComponent<RectMask2D>();
            wordClip = Hud.Node("WordClip", Rt); wordClip.Fill(); wordMask = wordClip.gameObject.AddComponent<RectMask2D>();
            digits = Hud.Label(digitsClip, "Digits", "", OzLayout.TagDigits, Color.white, 0f, OzStyle.Underlay);
            word = Hud.Label(wordClip, "Word", "", OzLayout.TagWord, Color.white, 0.06f, OzStyle.Underlay);
            OzBadgeText.Apply(digits); OzBadgeText.Apply(word);
            if (digits.graphic is TextMeshProUGUI td) td.enableVertexGradient = true;
            if (word.graphic is TextMeshProUGUI tw) tw.enableVertexGradient = false;
            wipeLine = Hud.Img(Rt, "WipeLine", OzUi.AmberHi);
            flatTech = Hud.Label(Rt, "FlatTech", "", OzLayout.TagTechSize, OzUi.Muted, 0.06f, OzStyle.Plain);
            foreach (var i in new[] { bodyC, bodyL, bodyM, bodyR, stripe, tech, num, tab, flagL, flagM, flagR, flagFL, flagFM, flagFR, notch, hazard, fill, fill2, white, sheen, faceNext, scan, rim, glow, wipeLine, face, capHalo, cap })
                i.enabled = false;
            flat.enabled = !art; flatTech.SetActive(!art); flagText.SetActive(false);
        }

        Image Layer(string name, float x, float y, float w, float h)
        {
            var i = Hud.Img(Rt, name, Color.white);
            i.rectTransform.Place(new OzRect(x + F, y + F, w, h));
            return i;
        }

        /// <summary>A square of half size h centred on the medallion (the rotation / radial fill centre).</summary>
        Image Ring(string name, float h)
        {
            var i = Hud.Img(Rt, name, Color.white);
            Centre(i.rectTransform, 0, 0, 2 * h, 2 * h);
            return i;
        }

        static void Centre(RectTransform r, float cx, float cy, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(cx + F, -(cy + F)); r.sizeDelta = new Vector2(w, h);
        }

        static void Move(Image i, float x, float y) { var p = new Vector2(x + F, -(y + F)); if (i.rectTransform.anchoredPosition != p) i.rectTransform.anchoredPosition = p; }
        static void Width(Image i, float w) { if (!Mathf.Approximately(i.rectTransform.sizeDelta.x, w)) i.rectTransform.sizeDelta = new Vector2(w, i.rectTransform.sizeDelta.y); }

        static void Radial(Image i, bool clockwise)
        {
            i.type = Image.Type.Filled; i.fillMethod = Image.FillMethod.Radial360; i.fillOrigin = (int)Image.Origin360.Top; i.fillClockwise = clockwise; i.fillAmount = 0f;
        }

        /// <summary>Enable only with a sprite: a missing / failed import never shows a white quad.</summary>
        static void Show(Image i, bool on) { on &= i.sprite != null; if (i.enabled != on) i.enabled = on; }
        static void Tint(Image i, Color c) { if (i.color != c) i.color = c; }
        static void Amount(Image i, float a) { if (Mathf.Abs(i.fillAmount - a) > 0.0002f) i.fillAmount = a; }
        static void SetSprite(Image i, Sprite s) { if (i.sprite != s) i.sprite = s; }

        public void SetActive(bool on) { if (Rt.gameObject.activeSelf != on) Rt.gameObject.SetActive(on); }

        /// <summary>Set the texts (only when they change) and return the plate width their content needs (u, >= TagPlateW).</summary>
        public float Measure(ref OzBadgeView v)
        {
            if (v.digits != digitsSet) { digitsSet = v.digits; digits.Set(v.digits ?? ""); digW = string.IsNullOrEmpty(v.digits) ? 0f : NominalWidth(digits); }
            if (v.word != wordSet || !Mathf.Approximately(v.wordTrack, wordTrackSet))
            {
                wordSet = v.word; wordTrackSet = v.wordTrack; word.Tracking = v.wordTrack; word.Set(v.word ?? "");
                wordW = string.IsNullOrEmpty(v.word) ? 0f : NominalWidth(word);
            }
            if (v.flag != flagSet) { flagSet = v.flag; flagText.Set(v.flag ?? ""); flagW = string.IsNullOrEmpty(v.flag) ? 0f : NominalWidth(flagText) + 2f * OzLayout.TagFlagPad; }
            float content = Mathf.Max(string.IsNullOrEmpty(v.digits) || v.digAlpha <= 0f && v.wipe < 0f ? 0f : digW, string.IsNullOrEmpty(v.word) ? 0f : wordW);
            return needW = Mathf.Max(OzLayout.TagPlateW, OzLayout.TagCol0 + content + 2f * OzLayout.TagWellPad + OzLayout.TagColPad);
        }

        /// <summary>Width of the label at its nominal size (OzText.Fit may have shrunk it to its box while the plate was narrower).</summary>
        static float NominalWidth(OzText t)
        {
            if (t.graphic is TMP_Text tm) return tm.GetPreferredValues(t.text).x * (t.Size / Mathf.Max(0.01f, tm.fontSize));
            if (t.graphic is Text lg) return lg.preferredWidth * (t.Size / Mathf.Max(1f, lg.fontSize));
            return t.preferredWidth;
        }

        /// <summary>RectMask2D padding for a clip rect in badge-local u (x0..x1, y0..y1, y down) inside the full frame.</summary>
        static Vector4 Pad(float x0, float x1, float y0, float y1) => new Vector4(x0 + F, FH - F - y1, FW - F - x1, y0 + F);

        /// <summary>Draw the view. sf = canvas px per u, s = the badge's base scale (TimerScale, x EnemyTimerScale for enemies); the
        /// tier follows sf x s (not the READY pop, so no sprite swap mid-animation).</summary>
        public void Apply(ref OzBadgeView v, float sf, float s)
        {
            int t = OzBadgeArt.Tier(sf * s);
            if (t != tier)
            {
                tier = t; fillKey = fill2Key = faceKey = faceNextKey = numKey = null;
                SetSprite(bodyC, OzBadgeArt.Get(t, "body_c"));
                SetSprite(bodyL, OzBadgeArt.Get(t, "body_l")); SetSprite(bodyM, OzBadgeArt.Get(t, "body_m")); SetSprite(bodyR, OzBadgeArt.Get(t, "body_r"));
                SetSprite(stripe, OzBadgeArt.Get(t, "stripe")); SetSprite(tech, OzBadgeArt.Get(t, "tech"));
                SetSprite(notch, OzBadgeArt.Get(t, "notch")); SetSprite(tab, OzBadgeArt.Get(t, "tab")); SetSprite(hazard, OzBadgeArt.Get(t, "ring_hazard"));
                SetSprite(flagL, OzBadgeArt.Get(t, "flag_l")); SetSprite(flagM, OzBadgeArt.Get(t, "flag_m")); SetSprite(flagR, OzBadgeArt.Get(t, "flag_r"));
                SetSprite(flagFL, OzBadgeArt.Get(t, "flagfill_l")); SetSprite(flagFM, OzBadgeArt.Get(t, "flagfill_m")); SetSprite(flagFR, OzBadgeArt.Get(t, "flagfill_r"));
                SetSprite(white, OzBadgeArt.Get(t, "ring_fill_white")); SetSprite(sheen, OzBadgeArt.Get(t, "ring_sheen"));
                SetSprite(capHalo, OzBadgeArt.Get(t, "ring_cap_halo")); SetSprite(cap, OzBadgeArt.Get(t, "ring_cap"));
                SetSprite(rim, OzBadgeArt.Get(t, "face_rim")); SetSprite(glow, OzBadgeArt.Get(t, "ring_glow"));
                wipeLine.sprite = null;
            }
            if (!Mathf.Approximately(cg.alpha, v.alpha)) cg.alpha = v.alpha;
            var sc = Vector3.one * (v.scale * s); if (Rt.localScale != sc) Rt.localScale = sc;
            float k = sf * s, full = art ? 1f - v.compact : 1f;
            bool plateOn = full > 0.004f;

            // ---- body: the compact medallion under, the full body over it (fading out while an enemy badge goes compact)
            Show(bodyC, art && v.compact > 0.004f);
            var fullTint = new Color(1, 1, 1, full);
            Show(bodyL, art && plateOn); Show(bodyM, art && plateOn); Show(bodyR, art && plateOn);                  // no per-frame array
            if (bodyL.enabled) { Tint(bodyL, fullTint); Tint(bodyM, fullTint); Tint(bodyR, fullTint); }
            if (!Mathf.Approximately(v.plateW, plateSet))
            {   // the plate grows to the right (10:00+, SELECT); body_m is a horizontally invariant strip
                plateSet = v.plateW;
                Width(bodyM, Mathf.Max(0f, v.plateW - OzLayout.TagSliceR - OzLayout.TagSliceL));
                Move(bodyR, v.plateW - OzLayout.TagSliceR, OzLayout.TagBodyTop);
                Move(stripe, v.plateW - OzLayout.TagStripeX - OzLayout.TagStripeW, OzLayout.TagStripeY);
                if (flat.enabled) Width(flat, OzLayout.TagR + v.plateW);
            }
            float c0 = OzLayout.TagCol0, c1 = v.plateW - OzLayout.TagColPad;
            float rcx = k > 0f ? Mathf.Round((c0 + c1) * 0.5f * k) / k : (c0 + c1) * 0.5f;                // row 2 on whole pixels while the plate is wide
            float ncx = k > 0f ? Mathf.Round((c0 + needW - OzLayout.TagColPad) * 0.5f * k) / k : (c0 + needW - OzLayout.TagColPad) * 0.5f;   // the content's own column
            Show(stripe, art && plateOn && v.stripe.a > 0.004f); if (stripe.enabled) Tint(stripe, new Color(v.stripe.r, v.stripe.g, v.stripe.b, v.stripe.a * full));
            Show(tech, art && plateOn); if (tech.enabled) { Tint(tech, fullTint); Move(tech, rcx + OzLayout.TagTechBox.x, OzLayout.TagTechY + OzLayout.TagTechBox.y); }
            string nk = OzBadgeArt.Num(v.level, v.levelCol);
            if (nk != numKey) { numKey = nk; SetSprite(num, OzBadgeArt.Get(t, nk)); }
            Show(num, art && plateOn); if (num.enabled) { Tint(num, fullTint); Move(num, rcx + OzLayout.TagLvlBox.x, OzLayout.TagTechY + OzLayout.TagLvlBox.y); }
            if (!art && v.level != flatLevel) { flatLevel = v.level; flatTech.SetActive(true); flatTech.Set("TECH " + ZBuildLists.Roman(v.level)); flatTech.At(rcx + F, OzLayout.TagTechY + F, OzH.C, true); }
            bool flagOn = art && plateOn && !string.IsNullOrEmpty(v.flag) && v.flagAlpha > 0.004f;
            Show(tab, art && plateOn && !flagOn && v.tab.a > 0.004f); if (tab.enabled) Tint(tab, new Color(v.tab.r, v.tab.g, v.tab.b, v.tab.a * full));

            // ---- cue flag (PAUSED / PLACE): 3-slice body, blinking fill, plain text (the window's chips)
            Show(flagL, flagOn); Show(flagM, flagOn); Show(flagR, flagOn);
            bool ffOn = flagOn && v.flagFill.a > 0.004f; Show(flagFL, ffOn); Show(flagFM, ffOn); Show(flagFR, ffOn);
            flagText.SetActive(flagOn);
            if (flagOn)
            {
                float e = OzLayout.TagFlagEnd, fx = OzLayout.TagFlagX, a = v.flagAlpha * full;
                Width(flagM, flagW - 2f * e); Width(flagFM, flagW - 2f * e);
                Move(flagR, fx + flagW - e, OzLayout.TagFlagY - 1f); Move(flagFR, fx + flagW - e, OzLayout.TagFlagY - 1f);
                var ft = new Color(1, 1, 1, a); Tint(flagL, ft); Tint(flagM, ft); Tint(flagR, ft);
                if (ffOn) { var fc = v.flagFill; fc.a *= a; Tint(flagFL, fc); Tint(flagFM, fc); Tint(flagFR, fc); }
                var tc = v.flagText; tc.a *= a; flagText.color = tc;
                if (!Mathf.Approximately(flagLaid, flagW)) { flagLaid = flagW; flagText.At(fx + flagW * 0.5f + F, OzLayout.TagFlagY + OzLayout.TagFlagH * 0.5f + F, OzH.C, true, flagW); }
            }
            Show(notch, art && v.notch.a > 0.004f); if (notch.enabled) Tint(notch, v.notch);

            // ---- ring
            Show(hazard, art && v.hazard > 0.001f); if (hazard.enabled) Amount(hazard, v.hazard);
            if (v.fillKey != fillKey) { fillKey = v.fillKey; SetSprite(fill, fillKey != null ? OzBadgeArt.Get(t, "ring_fill_" + fillKey) : null); }
            Show(fill, art && fillKey != null && v.fill > 0.0005f && v.fillAlpha > 0.004f);
            if (fill.enabled) { Amount(fill, Mathf.Min(1f, v.fill)); Tint(fill, new Color(1, 1, 1, v.fillAlpha)); }
            if (v.fill2Key != fill2Key) { fill2Key = v.fill2Key; SetSprite(fill2, fill2Key != null ? OzBadgeArt.Get(t, "ring_fill_" + fill2Key) : null); }
            Show(fill2, art && fill2Key != null && v.fill2 > 0.0005f && v.fill2Alpha > 0.004f);
            if (fill2.enabled) { Amount(fill2, Mathf.Min(1f, v.fill2)); Tint(fill2, new Color(1, 1, 1, v.fill2Alpha)); }
            Show(white, art && v.white > 0.004f); if (white.enabled) Tint(white, new Color(1, 1, 1, v.white));
            Show(sheen, art && v.sheenAlpha > 0.004f);
            if (sheen.enabled) { Tint(sheen, new Color(1, 1, 1, v.sheenAlpha)); sheen.rectTransform.localRotation = Quaternion.Euler(0, 0, -360f * v.sheen); }
            bool capOn = art && v.cap && v.fill > 0.004f && v.fill < 0.996f;
            if (capPivot.gameObject.activeSelf != capOn) capPivot.gameObject.SetActive(capOn);
            if (capOn) { Show(cap, true); Show(capHalo, true); capPivot.localRotation = Quaternion.Euler(0, 0, -360f * v.fill); Tint(capHalo, v.capTint); }

            // ---- face: current, next (revealed top -> bottom under the scan band), READY rim flash, glow
            if (v.face != faceKey) { faceKey = v.face; SetSprite(face, faceKey != null ? OzBadgeArt.Get(t, faceKey) : null); }
            Show(face, art && v.faceAlpha > 0.004f); if (face.enabled) Tint(face, new Color(1, 1, 1, v.faceAlpha));
            bool nextOn = v.faceNext != null && v.faceReveal > 0.0005f;
            if (nextOn && v.faceNext != faceNextKey) { faceNextKey = v.faceNext; SetSprite(faceNext, OzBadgeArt.Get(t, faceNextKey)); }
            Show(faceNext, art && nextOn); if (faceNext.enabled) Amount(faceNext, v.faceReveal);
            float R = OzLayout.TagFaceR, yb = -R + 2f * R * v.scan, half = v.scan >= 0f ? Mathf.Sqrt(Mathf.Max(0f, R * R - yb * yb)) : 0f;
            Show(scan, art && half > 1f);
            if (scan.enabled) { Centre(scan.rectTransform, 0, yb, 2f * half, 2.4f); Tint(scan, OzUi.A(OzUi.AmberHi, 0.55f)); }
            Show(rim, art && v.rim > 0.004f); if (rim.enabled) Tint(rim, new Color(1, 1, 1, v.rim));
            Show(glow, art && v.glow > 0.004f);
            if (glow.enabled) { Tint(glow, new Color(v.glowTint.r, v.glowTint.g, v.glowTint.b, v.glow)); glow.rectTransform.localScale = Vector3.one * v.glowScale; }

            // ---- well: the texts on their own column, the two clips = the current well split at the wipe edge (never an empty well:
            // READY stamps in on the frame the time goes, and hands back with a left-to-right wipe; a wider text is revealed as the
            // plate widens instead of waiting for it)
            float wmid = OzLayout.TagWellY + OzLayout.TagWellH * 0.5f, wy0 = OzLayout.TagWellY + 1f, wy1 = OzLayout.TagWellY + OzLayout.TagWellH - 1f;
            if (!Mathf.Approximately(layoutW, v.plateW) || !Mathf.Approximately(layoutNeed, needW))
            {
                layoutW = v.plateW; layoutNeed = needW;
                float box = needW - OzLayout.TagColPad - c0 - 2f;
                digits.At(ncx + F, wmid + F, OzH.C, true, box).Pivot(new Vector2(0.5f, 0.5f));        // centre pivot: the READY stamp scales in place
                word.At(ncx + F, wmid + F, OzH.C, true, box).Pivot(new Vector2(0.5f, 0.5f));
            }
            float x0 = c0 + 1f, x1 = c1 - 1f, edge = v.wipe >= 0f ? Mathf.Lerp(x0, x1, v.wipe) : -1f;
            // RectMask2D applies its padding in canvas space (Clipping.FindCullAndClipWorldRect), so it is scaled by the badge's own
            float ps = v.scale * s;
            var dp = Pad(x0, edge >= 0f ? edge : x1, wy0, wy1) * ps; if (dp != dPadSet) { dPadSet = dp; digitsMask.padding = dp; }
            var wp = Pad(edge >= 0f ? edge : x0, x1, wy0, wy1) * ps; if (wp != wPadSet) { wPadSet = wp; wordMask.padding = wp; }
            bool lineOn = plateOn && edge >= 0f && v.wipe > 0f && v.wipe < 1f;
            wipeLine.enabled = lineOn;
            if (lineOn)
            {
                var r = wipeLine.rectTransform; r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
                r.anchoredPosition = new Vector2(edge - 1f + F, -(wy0 + F)); r.sizeDelta = new Vector2(k > 0f ? Mathf.Max(1f, Mathf.Round(1.5f * k)) / k : 1.5f, wy1 - wy0);
                Tint(wipeLine, OzUi.A(OzUi.AmberHi, 0.85f * full));
            }
            float dA = v.digAlpha * full, wA = v.wordAlpha * full;
            digits.SetActive(dA > 0.004f && !string.IsNullOrEmpty(v.digits));
            if (dA > 0.004f)
            {
                if (digits.graphic is TextMeshProUGUI td)
                {
                    if (v.digTop != digTopSet || v.digBot != digBotSet) { digTopSet = v.digTop; digBotSet = v.digBot; td.colorGradient = new VertexGradient(v.digTop, v.digTop, v.digBot, v.digBot); }
                    digits.color = new Color(1, 1, 1, dA);
                }
                else digits.color = new Color(v.digBot.r, v.digBot.g, v.digBot.b, dA);
                var ds = Vector3.one * v.digScale; if (digits.rectTransform.localScale != ds) digits.rectTransform.localScale = ds;
            }
            word.SetActive(wA > 0.004f && !string.IsNullOrEmpty(v.word));
            if (wA > 0.004f)
            {
                var wc = v.wordCol; wc.a *= wA; word.color = wc;
                var ws = Vector3.one * v.wordScale; if (word.rectTransform.localScale != ws) word.rectTransform.localScale = ws;
            }
        }
    }

    /// <summary>A factory's level without a badge: Zod draws level_img on every robot and vehicle factory whatever its owner or state
    /// (brobot.cpp:377-380, bvehicle.cpp:381), so a neutral factory you might capture, an idle enemy factory and a ruin keep their
    /// level as a small "TECH [n]" tag with a pointer (at the overview: the socket only). Forts carry none (Zod draws no level on
    /// forts; a producing fort shows it on its badge).</summary>
    public sealed class OzLevelTag
    {
        public readonly RectTransform Rt;
        readonly CanvasGroup cg;
        readonly Image body, stripe, num;
        int tier = -1; bool compactSet; string numKey;

        public OzLevelTag(Transform parent)
        {
            Rt = Hud.Node("LevelTag", parent);
            Rt.anchorMin = Rt.anchorMax = Vector2.zero;
            var B = OzLayout.TagLevelTagBox;
            Rt.sizeDelta = new Vector2(B.w, B.h);
            Rt.pivot = new Vector2(-B.x / B.w, 1f + B.y / B.h);
            cg = Rt.gameObject.AddComponent<CanvasGroup>(); cg.interactable = false; cg.blocksRaycasts = false;
            body = Hud.Img(Rt, "Body", Color.white); stripe = Hud.Img(Rt, "Stripe", Color.white); num = Hud.Img(Rt, "Level", Color.white);
            body.enabled = stripe.enabled = num.enabled = false;
            Layout(false);
        }

        void Place(Image i, OzRect r)
        {
            var B = OzLayout.TagLevelTagBox;
            i.rectTransform.Place(new OzRect(r.x - B.x, r.y - B.y, r.w, r.h));
        }

        void Layout(bool compact)
        {
            compactSet = compact;
            Place(body, compact ? OzLayout.TagLevelTagCBox : OzLayout.TagLevelTagBox);
            var lb = OzLayout.TagLvlBox;
            Place(num, compact ? new OzRect(-lb.w * 0.5f, lb.y, lb.w, lb.h) : lb);
            Place(stripe, new OzRect(OzLayout.TagLevelTag.x + 4f, OzLayout.TagLevelTag.y, 18f, OzLayout.TagStripeW));   // the owner cue as a tab on the top edge
        }

        public void SetActive(bool on) { if (Rt.gameObject.activeSelf != on) Rt.gameObject.SetActive(on); }

        public void Apply(int level, int col, Color stripeCol, bool compact, float alpha, float sf, float s)
        {
            int t = OzBadgeArt.Tier(sf * s);
            if (t != tier || compact != compactSet)
            {
                if (compact != compactSet || tier < 0) Layout(compact);
                tier = t; numKey = null;
                var bs = OzBadgeArt.Get(t, compact ? "lvltag_c" : "lvltag"); if (body.sprite != bs) body.sprite = bs;
                var ss = OzBadgeArt.Get(t, "stripe"); if (stripe.sprite != ss) stripe.sprite = ss;
            }
            string nk = OzBadgeArt.Num(level, col);
            if (nk != numKey) { numKey = nk; num.sprite = OzBadgeArt.Get(t, nk); }
            bool on = body.sprite != null;
            if (body.enabled != on) body.enabled = on;
            bool st = on && !compact && stripe.sprite != null; if (stripe.enabled != st) stripe.enabled = st;
            if (st && stripe.color != stripeCol) stripe.color = stripeCol;
            bool nu = on && num.sprite != null; if (num.enabled != nu) num.enabled = nu;
            if (!Mathf.Approximately(cg.alpha, alpha)) cg.alpha = alpha;
            var sc = Vector3.one * s; if (Rt.localScale != sc) Rt.localScale = sc;
        }
    }

    /// <summary>Proof captures only (OzProductionBadgeProof): show a state on the real buildings without touching the Sim.
    /// Place = a stored cannon while production goes on (the PLACE flag over the running time); PlaceIdle = a stored cannon and
    /// nothing in production (the well says PLACE).</summary>
    public enum OzBadgeForce { None, Ready, Paused, Place, PlaceIdle, Idle }

    /// <summary>
    /// The per-building side of the badge: what the Sim says (read only) and the animation clocks. Lives as long as the building
    /// shows a badge (plus the 100 ms exit fade), whether or not it is on screen, so scrolling never replays an entrance.
    /// Unit ready = the Sim's own UnitBuilt / CannonStored event for this building (ZWorld.Rules.cs BuildingCreateUnit: emitted exactly
    /// when a unit leaves the door or a cannon is stored; nothing for a cannon discarded because the zone is full, a creation point
    /// that failed, a STOP or a switch). The old BFinal heuristic is kept only as a fallback for a world without the event feed.
    /// </summary>
    public sealed class OzBadgeState
    {
        /// <summary>View-only state override for the proof captures (None in play). ForceReadyT = seconds into the READY timeline
        /// (negative = loop it in real time).</summary>
        public static OzBadgeForce Force = OzBadgeForce.None;
        public static float ForceReadyT = 0.15f;
        /// <summary>Unit ready from the Sim's events (true in the game); false = the BFinal heuristic (a world without events).</summary>
        public static bool UseEventFeed = true;

        public readonly int Id;
        public bool Want, Seen;
        public float EnterAt = -10f, ExitAt = -10f;
        public Vector2 Nudge;
        public OzBadgeView View;
        Team owner = (Team)(-1);
        bool init; double lastBInit, lastBFinal; bool lastBuilding; string lastUnit;
        float readyAt = -10f, ghostAt = -10f, swapAt = -10f, sheenAt = -10f, plateAt = -10f, builtAt = -10f, compactAt = -10f;
        string readyFace, face, swapFrom; float prog; bool progInit, compactOn, compactInit; float compactFrom;
        float plateFrom = OzLayout.TagPlateW, plateTo = OzLayout.TagPlateW;
        int shownSecs = int.MinValue; string digits = "";

        // timings (s, unscaled; OzMotion.Dur applies the reduced-motion rule)
        public const float ReadyHold = 1.0f, ReadyOut = 0.16f, Drain = 0.28f, Swap = 0.22f, Ghost = 0.2f, Enter = 0.16f, Exit = 0.1f, Widen = 0.16f, Compact = 0.16f;
        public const float SheenEvery = 2.4f, SheenRun = 0.6f, RimFlash = 0.09f;

        public OzBadgeState(int id) { Id = id; }

        public bool Alive(float now) => Want || now - ExitAt < OzMotion.Dur(Exit, true);

        /// <summary>Does this producer show a badge: building, a stored cannon to place (own), or idle with the idle toggles.</summary>
        public static bool Wants(ZObject b, bool own)
        {
            if (b.Owner == Team.Neutral || b.Destroyed) return false;
            if (b.Building && b.CurUnit != null) return true;
            if (own && b.StoredCannons.Count > 0) return true;
            return own ? OpenZUnitOverlay.TimerIdle : OpenZUnitOverlay.EnemyTimerIdle;
        }

        /// <summary>Every frame for every producer that has (or had) a badge, on screen or not: bookkeeping + unit-ready detection.
        /// built = the Sim emitted UnitBuilt / CannonStored for this building this frame (builtType = what left the door / was stored).</summary>
        public void Observe(ZObject b, bool own, bool want, bool built, string builtType, double simTime, float now)
        {
            Seen = true;
            if (b.Owner != owner)                                                                 // captured / first sight: no READY for the handover
            {
                owner = b.Owner; init = false; face = null; readyAt = ghostAt = swapAt = -10f; progInit = false;
            }
            else if (UseEventFeed)
            {
                if (built)
                {
                    if (own) { readyAt = now; readyFace = OzBadgeArt.FaceFor(builtType ?? lastUnit, b.Owner); }
                    else ghostAt = now;                                                            // enemy: no READY (Zod tells you nothing), the ring just restarts
                }
            }
            else if (init)
            {   // fallback without the event feed: the build that was running reached its BFinal and the building moved on
                bool done = lastBuilding && lastUnit != null && simTime >= lastBFinal - 1e-4 && (b.BInit != lastBInit || !b.Building || b.CurUnit != lastUnit);
                if (done)
                {
                    if (own) { readyAt = now; readyFace = OzBadgeArt.FaceFor(lastUnit, b.Owner); }
                    else ghostAt = now;
                }
            }
            init = true; lastBInit = b.BInit; lastBFinal = b.BFinal; lastBuilding = b.Building; lastUnit = b.CurUnit;
            want |= own && !b.Destroyed && b.Owner != Team.Neutral && ReadyActive(now);           // the READY moment plays out even if the building goes idle
            if (want && !Want && !Alive(now)) EnterAt = now;
            if (!want && Want) ExitAt = now;
            Want = want;
        }

        public bool ReadyActive(float now) { float t = now - readyAt; return t >= 0f && t < ReadyHold + ReadyOut + Drain; }

        static float Env(float t, float d) => d <= 0f ? (t >= 0f ? 1f : 0f) : Mathf.Clamp01(t / d);
        static float InOutCubic(float t) => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;

        /// <summary>Build this frame's view (everything but the plate width, see Plate()). compact = the camera is high enough for the
        /// compact form (enemy badges at the overview, OpenZUnitOverlay decides).</summary>
        public void Build(ZObject b, bool own, bool paused, bool compact, float now, float dt)
        {
            var v = new OzBadgeView();
            bool reduced = OzMotion.Reduced;
            bool producing = b.Building && b.CurUnit != null;
            bool place = own && b.StoredCannons.Count > 0;
            var force = Force;
            if (force == OzBadgeForce.Idle) { producing = false; place = false; }
            if (force == OzBadgeForce.Place && own) place = true;
            if (force == OzBadgeForce.PlaceIdle && own) { place = true; producing = false; }
            if (force == OzBadgeForce.Paused) paused = true;
            string placeUnit = b.StoredCannons.Count > 0 ? b.StoredCannons[0] : b.CurUnit != null && ZBuildLists.IsCannon(b.CurUnit) ? b.CurUnit : "gun";
            Team team = b.Owner;
            string teamKey = TeamColors.Shown(team) == Team.Blue ? "blue" : "red";
            float p = producing ? (float)b.ProductionProgress : 0f;
            if (now - builtAt > 0.25f) { progInit = false; face = null; }                                  // back on screen: show the present, no catch-up
            builtAt = now;
            if (!progInit || p < prog - 0.5f || !producing) { prog = p; progInit = true; }               // snaps down when the build restarts
            else prog = Mathf.Lerp(prog, p, 1f - Mathf.Exp(-12f * dt));                                  // eases between sim ticks (production window rule)

            // ---- face (swap under the scan band when it changes): the unit whose time is shown
            string want;
            if (!own && !OpenZUnitOverlay.EnemyTimerPortrait) want = OzBadgeArt.Emblem(team);
            else if (producing) want = OzBadgeArt.FaceFor(b.CurUnit, team);
            else if (place) want = OzBadgeArt.FaceFor(placeUnit, team);
            else want = own ? (b.IsFort ? OzBadgeArt.IdleFort : OzBadgeArt.IdleFactory) : OzBadgeArt.Emblem(team);
            float tr = now - readyAt;
            if (force == OzBadgeForce.Ready && own && producing) { tr = ForceReadyT >= 0f ? ForceReadyT : Mathf.Repeat(now, ReadyHold + ReadyOut + Drain + 0.8f); readyFace ??= OzBadgeArt.FaceFor(b.CurUnit, team); }
            bool ready = own && tr >= 0f && tr < ReadyHold + ReadyOut + Drain;
            if (ready && tr < ReadyHold) want = readyFace ?? want;                                       // READY shows the unit that just left the door
            if (face == null) face = want;
            else if (want != face && now - swapAt >= OzMotion.Dur(Swap)) { swapFrom = face; face = want; swapAt = now; }
            float sk = Env(now - swapAt, OzMotion.Dur(Swap));
            if (sk < 1f && swapFrom != null) { v.face = swapFrom; v.faceNext = face; v.faceReveal = OzEase.OutCubic(sk); v.scan = reduced ? -1f : v.faceReveal; }
            else { v.face = face; v.faceNext = null; v.scan = -1f; }
            v.faceAlpha = 1f;

            // ---- time left (Zod shows it whenever the building is not in SELECT, paused or not): the string only changes with the second
            int secs = producing ? Mathf.CeilToInt((float)System.Math.Max(0, b.ProductionTimeLeft)) : -1;
            if (secs != shownSecs) { shownSecs = secs; digits = secs >= 0 ? OzText.Tab(UnitInfo.Mmss(secs)) : ""; }

            // ---- base look: owner colours, the level, the owner stripe
            v.scale = 1f; v.alpha = 1f; v.cap = true; v.fillAlpha = 1f; v.digAlpha = 1f; v.digScale = 1f; v.wordScale = 1f; v.wordTrack = 0.06f; v.wipe = -1f;
            v.level = ZBuildLists.DisplayTech(b.Type, b.BuildLevel); v.levelCol = own ? 0 : 1;
            v.glowTint = OzUi.Go; v.notch = Color.clear; v.stripe = OzUi.Mark(team); v.flagAlpha = 1f;
            if (own) { v.fillKey = "amber"; v.tab = OzUi.Amber; v.capTint = OzUi.AmberHi; v.digTop = OzUi.AmberHi; v.digBot = OzUi.Amber; }
            else { v.fillKey = teamKey; v.tab = OzUi.Mark(team); v.capTint = OzUi.Mark(team); v.digTop = OzUi.Ink; v.digBot = OzUi.Ink2; }
            v.fill = prog; v.digits = digits;
            bool blink = !reduced && OzMotion.Blink(OzMotion.BlinkSlow);
            if (!producing && !place)
            {   // idle: empty gauge, the class glyph, SELECT (own, the production window's state name) / IDLE (enemy), level kept
                v.fill = 0f; v.cap = false; v.digits = null; v.digAlpha = 0f;
                v.word = own ? "SELECT" : "IDLE"; v.wordCol = own ? (Color)OzUi.AmberHi : (Color)OzUi.Ink2; v.wordAlpha = 1f; v.wordTrack = own ? 0.06f : 0.12f;
            }
            else if (!producing)
            {   // a stored cannon waits and nothing is in production: SIGNAL ring full, PLACE blinking like the window's chip (300 ms)
                v.fillKey = "signal"; v.fill = 1f; v.cap = false; v.tab = OzUi.Signal; v.digits = null; v.digAlpha = 0f;
                v.word = "PLACE"; v.wordCol = OzUi.Signal; v.wordAlpha = blink ? 0.38f : 1f; v.wordTrack = 0.08f;
            }
            else
            {
                if (paused)
                {   // the owner's 70-unit limit (zbuilding.cpp:77-83 GetBuildState): SIGNAL ring pulsing at 1 Hz, hazard in the unfilled channel,
                    // SIGNAL time (the window's curTime rule), the PAUSED flag blinking like the window's chip
                    v.fillKey = "signal"; v.fillAlpha = reduced ? 1f : 0.775f + 0.225f * Mathf.Cos(now * Mathf.PI * 2f);
                    v.hazard = Mathf.Clamp01(1f - prog); v.cap = false; v.tab = OzUi.Signal;
                    v.digTop = OzUi.Signal; v.digBot = OzUi.Signal;
                    v.flag = "PAUSED"; v.flagText = blink ? OzUi.A(OzUi.Signal, 0.35f) : (Color)OzUi.Signal; v.flagFill = Color.clear;
                }
                if (place)
                {   // a stored cannon waits while the next unit builds (ResetProduction runs after every stored cannon): the PLACE flag
                    // over the running time, blinking SIGNAL / GM_950 like the window's PLACE chip (it wins over PAUSED, window rule)
                    v.tab = OzUi.Signal;
                    v.flag = "PLACE"; v.flagFill = blink ? Color.clear : (Color)OzUi.Signal; v.flagText = blink ? (Color)OzUi.Signal : (Color)OzUi.GM950;
                }
                if (!paused && own && !reduced && prog > 0.12f)
                {   // building: a calm sheen along the lit ring every 2.4 s (the production bar's beat), so a working factory reads alive
                    if (now - sheenAt > SheenEvery) sheenAt = now;
                    float s = (now - sheenAt) / SheenRun;
                    if (s < 1f) { v.sheen = OzEase.InOutSine(s) * (prog - 0.035f); v.sheenAlpha = 0.30f * Mathf.Sin(Mathf.PI * s); }
                }
            }

            // ---- unit ready (own): white-hot ring 90 ms, rim flash 90 ms, two GO pulses, GO glow outside the bezel, READY stamped on the
            // frame the unit leaves the door, hold 1 s, then a left-to-right wipe back to the time + the GO ring draining to the live progress
            if (ready)
            {
                if (tr < ReadyHold)
                {
                    v.fillKey = "go"; v.fill = 1f; v.cap = false; v.sheenAlpha = 0f; v.hazard = 0f; v.fillAlpha = 1f;
                    v.tab = OzUi.Go; v.notch = OzUi.A(OzUi.Go, 0.9f); v.flagAlpha = 0f;
                    if (!reduced)
                    {
                        float fT = 0.09f, fK = 0.12f;
                        v.white = tr < fT ? 0.95f : Mathf.Max(0f, 0.95f * (1f - (tr - fT) / fK));
                        bool pulse = (tr >= 0.09f && tr < 0.215f) || (tr >= 0.34f && tr < 0.465f);
                        if (pulse) v.white = Mathf.Max(v.white, 0.32f);
                        v.rim = tr < RimFlash ? 0.9f * (1f - tr / RimFlash) : 0f;                         // an edge flash only: the unit keeps its colour
                        float gk = Mathf.Clamp01((tr - 0.03f) / 0.45f);
                        v.glow = tr < 0.03f ? 0f : 0.85f * Mathf.Pow(1f - gk, 1.2f); v.glowScale = Mathf.Lerp(1f, 1.3f, OzEase.OutCubic(gk));
                        float st = Mathf.Clamp01(tr / OzMotion.Stamp);
                        v.wordScale = Mathf.LerpUnclamped(1.12f, 1f, OzEase.OutBack(st));                      // stays inside the well
                        v.scale = 1f + 0.06f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(tr / 0.22f));                  // a small pop (the dial's stamp)
                    }
                    v.digAlpha = 0f;                                                                    // the time goes on the same frame READY stamps in
                    v.word = "READY"; v.wordCol = OzUi.Go; v.wordAlpha = 1f; v.wordTrack = 0.06f;
                }
                else
                {
                    float ot = tr - ReadyHold, wk = Env(ot, OzMotion.Dur(ReadyOut, true)), dk = Env(ot, OzMotion.Dur(Drain));
                    if (producing)
                    {   // READY and the next m:ss share the well, split at the wipe edge (digits left, READY right): never empty, never muddy
                        v.wipe = InOutCubic(wk);
                        if (v.wipe < 1f) { v.word = "READY"; v.wordCol = OzUi.Go; v.wordAlpha = 1f; v.wordTrack = 0.06f; }
                    }
                    else if (wk < 0.5f) { v.word = "READY"; v.wordCol = OzUi.Go; v.wordAlpha = 1f; v.wordTrack = 0.06f; }   // SELECT / PLACE cut in half-way
                    v.flagAlpha = wk;
                    v.fill2Key = "go"; v.fill2 = Mathf.Lerp(1f, v.fill, OzEase.InCubic(dk)); v.fill2Alpha = 1f - dk;
                    v.tab = Color.Lerp(OzUi.Go, v.tab, wk);
                }
            }
            // ---- enemy unit done: the full ring fades out (200 ms) over the restarted one
            float gt = now - ghostAt;
            if (!own && gt >= 0f && gt < OzMotion.Dur(Ghost, true)) { v.fill2Key = v.fillKey; v.fill2 = 1f; v.fill2Alpha = 1f - gt / OzMotion.Dur(Ghost, true); }

            // ---- compact form (160 ms): the plate fades off the medallion when the overlay asks for it (a badge that appears
            // at the overview starts compact: no plate retracting during its entrance)
            if (!compactInit) { compactInit = true; compactOn = compact; compactFrom = compact ? 1f : 0f; compactAt = -10f; }
            else if (compact != compactOn) { compactFrom = CompactNow(now); compactOn = compact; compactAt = now; }
            v.compact = CompactNow(now);

            // ---- enter (scale 0.9 -> 1, fade in, 160 ms easeOutCubic) / exit (fade 100 ms)
            float ek = Env(now - EnterAt, OzMotion.Dur(Enter, true));
            if (ek < 1f) { v.alpha *= ek; if (!reduced) v.scale *= Mathf.Lerp(0.9f, 1f, OzEase.OutCubic(ek)); }
            View = v;
        }

        float CompactNow(float now)
        {
            float d = OzMotion.Dur(Compact, true), k = d <= 0f ? 1f : Mathf.Clamp01((now - compactAt) / d);
            return Mathf.Lerp(compactFrom, compactOn ? 1f : 0f, OzEase.OutCubic(k));
        }

        /// <summary>Animate the plate toward the width the content needs (160 ms easeOutCubic), snapped to whole pixels at k.</summary>
        public float Plate(float need, float now, float k)
        {
            if (!Mathf.Approximately(need, plateTo))
            {
                float cur = PlateNow(now);
                plateFrom = cur; plateTo = need; plateAt = now;
            }
            float w = PlateNow(now);
            return k > 0f ? Mathf.Round(w * k) / k : w;
        }

        float PlateNow(float now)
        {
            float d = OzMotion.Dur(Widen);
            float s = d <= 0f ? 1f : Mathf.Clamp01((now - plateAt) / d);
            return Mathf.Lerp(plateFrom, plateTo, OzEase.OutCubic(s));
        }

        /// <summary>Exit fade: keep drawing the last view, fading out.</summary>
        public void Fade(float now)
        {
            float d = OzMotion.Dur(Exit, true);
            View.alpha = d <= 0f ? 0f : Mathf.Clamp01(1f - (now - ExitAt) / d);
        }
    }
}
