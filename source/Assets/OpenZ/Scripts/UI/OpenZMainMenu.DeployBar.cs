using System.Collections.Generic;
using System.Text.RegularExpressions;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    ///   [ OBJECTIVE card ]  ....  [ DIFFICULTY | HEAD START | SHUTTLE ]  [ DEPLOY ]
    ///  * Three shared lines: every block and the card put their small label (caps top <see cref="LabelY"/>), their control row
    ///    (<see cref="RowY"/>, <see cref="RowH"/> tall) and their hint (caps top <see cref="HintY"/>) on the same y; the stack is
    ///    centred on the bar (1020), as DEPLOY is.
    ///  * The objective is a compact card at the bar's left margin: the tag OBJECTIVE, a short headline (the level's goal in a few
    ///    words, its team word in the team's text colour) and one small line with the level's own flag count. The card is as wide as
    ///    the longest words of every level plus its pads, so it never changes size when the level changes.
    ///  * The option blocks stand in one row, right-aligned against DEPLOY, all the same width, a hairline in the middle of each gap.
    ///    Level 16 has three (DIFFICULTY, HEAD START, SHUTTLE; on HARD the SHUTTLE slot stays, locked); levels 2 and 17 have two,
    ///    set against DEPLOY: when the level changes the SHUTTLE block fades out (0.08 s) and DIFFICULTY and HEAD START slide one slot
    ///    (0.16 s, ease out; CALM MODE: at once); back on level 16 they slide first and the block fades in once the slot is free, so no
    ///    text ever lies over another. Within a level nothing moves.
    ///  * Wide canvases (16:10 and wider, <see cref="WideFrom"/> u and more), narrow ones (4:3) and compact ones (5:4, under
    ///    <see cref="CompactBelow"/> u) have their own margins, widths and gaps; the card drops its fort glyph when the bar has no room
    ///    for it (then the plain rule line, then a 16 u headline).
    /// The menu canvas keeps its reference layout (match height): UI 100 % and 160 % give the same bar.
    /// </summary>
    public static class OzDeployBar
    {
        // ---- the shared lines (canvas units, 1080 u tall, top-left origin)
        public const float LabelY = 987f, RowY = 1001f, RowH = 34f, HintY = 1045f, LineTop = 985f, LineBottom = 1055f;
        public const float CardY = 975f, CardH = 90f, DeployY = 990f, DeployH = 60f, ErrorY = 944f;
        // ---- wide (16:10 and wider) / narrow (4:3) / compact (5:4) canvases
        public const float WideFrom = 1600f, CompactBelow = 1400f;
        public const float MarginWide = 120f, MarginNarrow = 96f, MarginCompact = 96f, DeployWide = 300f, DeployNarrow = 264f, DeployCompact = 240f;
        public const float BlockWide = 210f, BlockNarrow = 200f, BlockCompact = 196f, GapWide = 24f, GapNarrow = 16f, GapCompact = 12f;
        public const float ToDeployWide = 32f, ToDeployNarrow = 20f, ToDeployCompact = 16f, CardGapWide = 32f, CardGapNarrow = 24f, CardGapCompact = 20f;
        public const float DeployLabel = 18f, DeployLabelCompact = 16f;
        // ---- the objective card: the fort glyph 16 u in, 30 u, 14 u to the text (text 18 u in without it); 20 u right pad
        public const float IconX = 16f, IconSize = 30f, IconGap = 14f, TextX = 18f, CardPadR = 20f, CardBoxPadR = 16f, HeadSize = 18f, HeadSizeMin = 16f;
        // ---- the switch rows (as HEAD START's): words 12 u in, ON / OFF right-aligned 66 u before the right edge, track 48 x 18 60 u before it
        public const float WordsX = 12f, StateRX = 66f, TrackRX = 60f, TrackW = 48f, TrackH = 18f, KnobW = 22f, KnobH = 14f;
        // ---- tooltips: centred over their own block, lower edge 8 u over the bar, inside the margins, right of the commander's strip
        public const float TipW = 336f, TipH = 136f, TipBottom = 954f, TipStripGap = 10f;
        public const float HairAlpha = 0.086f, LockLabelA = 0.45f, LockTrackA = 0.4f, LockKnobA = 0.5f;
        /// <summary>The SHUTTLE slot's fade (OzMotion.Micro); fading in it waits until HEAD START has left the slot: SlotInAfter of the slide.</summary>
        public const float SlotFade = 0.08f, SlotInAfter = 0.65f;

        // ---- words (short, specific; the tooltips keep the full lists)
        public const string CardTag = "OBJECTIVE";
        /// <summary>Every level's objective today is "CAPTURE THE FLAGS TO TAKE TERRITORY. BREAK INTO THE BLUE FORT TO WIN."
        /// (OzLevelDef.Objective): the headline keeps the goal, the small line the flags-for-territory rule with the level's own count.</summary>
        public const string CardHeadline = "BREAK INTO THE BLUE FORT", CardRule = "CAPTURE FOR TERRITORY", CardRulePlain = "CAPTURE FLAGS FOR TERRITORY";
        public const string CpuEasy = "ALL UNITS · GENTLE CPU", CpuMedium = "+2 TECH LEVELS · ZOD CPU", CpuHard = "Z BUILD LISTS · MISSILES";
        public const string HeadL02 = "2 TANKS · 2 JEEPS · 1 MECH", HeadL16 = "2 JEEPS · 1 LIGHT TANK", HeadL17 = "1 MLRS · 1 HEAVY TANK", HeadOff = "STANDARD MAP START";

        public static string Headline(string level) => CardHeadline;
        /// <summary>The card's small line: "13 FLAGS · CAPTURE FOR TERRITORY" (no count known, or no room: "CAPTURE FLAGS FOR TERRITORY").</summary>
        public static string Sub(int flags, bool withCount) => withCount && flags > 0 ? flags + " FLAGS · " + CardRule : CardRulePlain;
        /// <summary>The DIFFICULTY block's hint (the bar's own short words; OpenZDifficulty.Hint stays the OPTIONS row's).</summary>
        public static string CpuHint(GameDifficulty d) => d == GameDifficulty.Easy ? CpuEasy : d == GameDifficulty.Medium ? CpuMedium : CpuHard;
        /// <summary>The HEAD START block's hint while it is on (OpenZDifficulty.HeadStartHint stays the long one, for the log).</summary>
        public static string HeadHint(string level) => level == "L02" ? HeadL02 : level == "L16" ? HeadL16 : level == "L17" ? HeadL17 : "";

        static readonly Regex TeamWord = new Regex(@"\b(BLUE|RED)\b");
        /// <summary>The team words of a headline in their team text colour (guide 2.1: team text from 14 u up). Give it the line as
        /// shown (TeamColors.Words has swapped the words already, so the word names the colour it is drawn in).</summary>
        public static string TeamWords(string s) => string.IsNullOrEmpty(s) ? s : TeamWord.Replace(s, m =>
            "<color=" + OzUi.Hex(m.Groups[1].Value == "BLUE" ? OzUi.BlueText : OzUi.RedText) + ">" + m.Groups[1].Value + "</color>");

        /// <summary>The bar on a canvas cw u wide.</summary>
        public struct Layout
        {
            public bool wide, compact; public float cw, m, deployW, deployLabel, blockW, gap, toDeploy, cardGap, deployX;
            /// <summary>x of block i when n blocks stand in the row (3: DIFFICULTY, HEAD START, SHUTTLE; 2: the first two, against DEPLOY).</summary>
            public float X(int i, int n) => deployX - toDeploy - (n - i) * blockW - (n - 1 - i) * gap;
            /// <summary>How far DIFFICULTY and HEAD START move when the SHUTTLE slot comes or goes: one block and one gap.</summary>
            public float Slide => blockW + gap;
            /// <summary>The room the card has: from the margin to the gap before DIFFICULTY in the three-block row.</summary>
            public float CardRoom => X(0, 3) - cardGap - m;
        }

        public static Layout Lay(float cw)
        {
            bool w = cw >= WideFrom, c = !w && cw < CompactBelow;
            var l = new Layout
            {
                wide = w, compact = c, cw = cw, m = w ? MarginWide : c ? MarginCompact : MarginNarrow, deployW = w ? DeployWide : c ? DeployCompact : DeployNarrow,
                deployLabel = c ? DeployLabelCompact : DeployLabel, blockW = w ? BlockWide : c ? BlockCompact : BlockNarrow, gap = w ? GapWide : c ? GapCompact : GapNarrow,
                toDeploy = w ? ToDeployWide : c ? ToDeployCompact : ToDeployNarrow, cardGap = w ? CardGapWide : c ? CardGapCompact : CardGapNarrow
            };
            l.deployX = cw - l.m - l.deployW;
            return l;
        }

        /// <summary>A tooltip's left edge over the block at x (block width bw): centred, inside the margins, right of the strip.</summary>
        public static float TipX(Layout l, float x, float stripRight) =>
            Mathf.Min(Mathf.Max(x + l.blockW * 0.5f - TipW * 0.5f, Mathf.Max(l.m, stripRight + TipStripGap)), l.cw - l.m - TipW);
    }

    // ============================================================================================================================
    // K347: the battlefield screen's deploy bar, built and refreshed (OzDeployBar has the numbers and the words). The project's
    // BuildDeployBar calls BuildDeployConsole; RefreshDeployBar calls RefreshDeployConsole after RefreshHeadStart. The parts keep the
    // names (and fields) they had (DeployBar, CpuLabel, CpuKey, CpuEasy, CpuMedium, CpuHard, CpuHint, HeadLabel, HeadKey, HeadRow,
    // HeadHint, Deploy, DeployFocus, Error), so the rest of the menu and its proofs find them as before; new: ObjectiveCard, Line0,
    // ShuttleBlock (UI/OpenZMainMenu.ShuttleStart.cs), ShuttleStartTip.
    // ============================================================================================================================
    public partial class OpenZMainMenu
    {
        OzDeployBar.Layout deployLay;
        RectTransform cardRt; OzText cardTag, cardHead, cardSub; Image cardIcon; bool cardCount; int cardFor = -1, cardSwap = -1;
        readonly List<(RectTransform rt, float x)> slideParts = new List<(RectTransform rt, float x)>();
        float slideFrom, slideTo, slideNow, slideAt; int slotsWant = 3; float stripRight = 632f;

        /// <summary>The option blocks' width on this canvas (RefreshHeadStart places HEAD START's knob with it).</summary>
        float DeployBlockW => deployLay.blockW > 0f ? deployLay.blockW : OzDeployBar.BlockWide;

        void BuildDeployConsole(RectTransform p)
        {
            var L = deployLay = OzDeployBar.Lay(CW);
            slideParts.Clear(); cardFor = -1; cardSwap = -1;
            shownDeployCard = -1;                                                                          // a rebuild (a new screen size) names the level on the new DEPLOY again ("DEPLOY · JUNGLE", RefreshDeployBar)
            var quip = ZodLayout.Quip(); stripRight = quip.x + quip.W * quip.scale;
            // the bar: the kit's deploy bar plate, full width, 118 u (as before)
            var bar = Hud.Node("DeployBar", p);
            bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(1, 0); bar.pivot = new Vector2(0.5f, 0);
            bar.offsetMin = new Vector2(-2, -2); bar.offsetMax = new Vector2(2, OzMenuLayout.DeployBarH);
            var bg = Hud.Kit(bar, "Bg", "Select/deploy_bar", new OzRect(0, 0, 10, 10), null); bg.rectTransform.Fill();
            if (OzKit.GetSprite("Select/deploy_bar") == null) bg.color = OzUi.A(OzUi.GM800, 0.96f);

            BuildObjectiveCard(p, L);
            slotsWant = OzShuttleStart.HasSlot(levels[focusCard].Id) ? 3 : 2;
            slideNow = slideFrom = slideTo = slotsWant == 3 ? 0f : L.Slide; slideAt = -10f;
            BuildShuttleStart(p, L);                                                                       // first: the blocks that slide into its slot draw (and take clicks) over it while it fades

            float bw = L.blockW, ry = OzDeployBar.RowY, rh = OzDeployBar.RowH, mid = rh * 0.5f;
            SlidePart(Hairline(p, "Line0", L.X(0, 3) + bw + L.gap * 0.5f, OzDeployBar.LineTop));                                 // between DIFFICULTY and HEAD START (the one left of SHUTTLE comes and goes with it)

            // DIFFICULTY [EASY | MEDIUM | HARD]
            float cx = L.X(0, 3), sw = (bw - 8f) / 3f;
            SlidePart(Hud.Label(p, "CpuLabel", "DIFFICULTY", 10, OzUi.Ink2, 0.24f, OzStyle.Plain).At(cx, OzDeployBar.LabelY, OzH.L, false, bw - 22f).rectTransform);
            SlidePart(Hud.Keycap(p, "CpuKey", "C", cx + bw, OzDeployBar.LabelY - 2f));
            cpuEasy = SmallButton(p, "CpuEasy", "EASY", new OzRect(cx, ry, sw, rh), View.Select, Side.Any, () => SetDifficulty(GameDifficulty.Easy), 11); SlidePart(cpuEasy.Rt);
            cpuMedium = SmallButton(p, "CpuMedium", "MEDIUM", new OzRect(cx + sw + 4f, ry, sw, rh), View.Select, Side.Any, () => SetDifficulty(GameDifficulty.Medium), 11); SlidePart(cpuMedium.Rt);
            cpuNormal = SmallButton(p, "CpuHard", "HARD", new OzRect(cx + 2f * (sw + 4f), ry, sw, rh), View.Select, Side.Any, () => SetDifficulty(GameDifficulty.Hard), 11); SlidePart(cpuNormal.Rt);
            cpuHint = Hud.Label(p, "CpuHint", "", 9, OzUi.Muted, 0.16f, OzStyle.Plain).At(cx, OzDeployBar.HintY, OzH.L, false, bw); SlidePart(cpuHint.rectTransform);

            // HEAD START: the switch row (menu_exe_v1's, in the shared row)
            float hx = L.X(1, 3);
            SlidePart(Hud.Label(p, "HeadLabel", "HEAD START", 10, OzUi.Ink2, 0.24f, OzStyle.Plain).At(hx, OzDeployBar.LabelY, OzH.L, false, bw - 22f).rectTransform);
            SlidePart(Hud.Keycap(p, "HeadKey", "H", hx + bw, OzDeployBar.LabelY - 2f));
            headRow = HudButton.Make(p, "HeadRow", null, new Vector2(bw, rh), HudButtonKind.Small);
            headRow.Rt.Place(new OzRect(hx, ry, bw, rh)); SlidePart(headRow.Rt);
            headText = Hud.Label(headRow.Content, "Text", "+5 VEHICLES", 11, OzUi.Ink, 0.1f, OzStyle.Plain).At(OzDeployBar.WordsX, mid, OzH.L, true, WordsBox(bw, true));
            Hud.Kit(headRow.Content, "Track", "Buttons/switch_track", new OzRect(bw - OzDeployBar.TrackRX, (rh - OzDeployBar.TrackH) * 0.5f, OzDeployBar.TrackW, OzDeployBar.TrackH), OzKit.Available ? (Color?)null : OzUi.GM950);
            headKnob = Hud.Kit(headRow.Content, "Knob", "Buttons/switch_knob", new OzRect(0, (rh - OzDeployBar.KnobH) * 0.5f, OzDeployBar.KnobW, OzDeployBar.KnobH), OzUi.Amber, Image.Type.Simple);
            headState = Hud.Label(headRow.Content, "State", "", 9, OzUi.Amber, 0.2f, OzStyle.Plain).At(bw - OzDeployBar.StateRX, mid, OzH.R, true, 40);
            AddHot(headRow.Rt, View.Select, ToggleHeadStart, h => headTipOn = h, headRow, null);
            headHint = Hud.Label(p, "HeadHint", "", 9, OzUi.Muted, 0.16f, OzStyle.Plain).At(hx, OzDeployBar.HintY, OzH.L, false, bw); SlidePart(headHint.rectTransform);
            ExeHeadStart(p, default);                                                                      // its tooltip (placed over its block below) and the label's colour; no "L02 ONLY" chip any more
            BuildShuttleTip(p);

            // DEPLOY (primary) + keyboard focus ring: the strong button, centred on the bar's line (1020)
            float dx = L.deployX, dw = L.deployW, dy = OzDeployBar.DeployY, dh = OzDeployBar.DeployH;
            deployFocus = Hud.Kit(p, "DeployFocus", "Buttons/btn_l_focus", new OzRect(dx - 4, dy - 4, dw + 8, dh + 8), OzUi.A(Color.white, 0f));
            if (OzKit.GetSprite("Buttons/btn_l_focus") == null) deployFocus.enabled = false;
            deployBtn = HudButton.Make(p, "Deploy", "DEPLOY", new Vector2(dw, dh), HudButtonKind.Large, L.deployLabel, 0.14f, HudButtonStyle.Primary, true);
            deployBtn.Rt.Place(new OzRect(dx, dy, dw, dh));
            AddHot(deployBtn.Rt, View.Select, Deploy, null, deployBtn, null);
            // the editor's "not in the build settings" line: right-aligned over DEPLOY, above the bar, clear of the commander's strip
            float ew = Mathf.Max(200f, dx + dw - (stripRight + OzDeployBar.TipStripGap));
            errorLine = Hud.Label(p, "Error", "", 11, OzUi.AlertText, 0.14f, OzStyle.Plain).At(dx + dw, OzDeployBar.ErrorY, OzH.R, false, ew);

            ApplySlide(slideNow); PlaceDeployTips(); RefreshObjectiveCard(true);
            Debug.Log($"[OpenZ Menu] deploy bar ({(L.wide ? "wide" : L.compact ? "compact" : "narrow")}, {CW:0} u): card {L.m:0}..{L.m + cardRt.sizeDelta.x:0} u ({(cardIcon ? "fort glyph" : "no glyph")}, headline {cardHead.FontSize:0} u, "
                      + $"{(cardCount ? "flag count" : "plain rule")}), blocks {bw:0} u at {L.X(0, 3):0} / {L.X(1, 3):0} / {L.X(2, 3):0} (two-block row {L.X(0, 2):0} / {L.X(1, 2):0}), DEPLOY {dx:0}..{dx + dw:0} u; {slotsWant} block(s) now");
        }

        /// <summary>The words' box of a switch row: from 12 u in to 8 u before ON / OFF (with the state), or to 10 u before the track.</summary>
        static float WordsBox(float bw, bool withState) => withState ? bw - OzDeployBar.WordsX - OzDeployBar.StateRX - 36f : bw - OzDeployBar.WordsX - OzDeployBar.TrackRX - 10f;

        /// <summary>A 1 u hairline (INK at 9 %) from the label line to the hint line, its middle at x, its top at y in the parent.</summary>
        RectTransform Hairline(RectTransform parent, string name, float x, float y)
        {
            float px = Screen.height > 0 ? OzLayout.RefH / Screen.height : 1f, w = Mathf.Max(1f, Mathf.Round(1f / px)) * px;   // one whole pixel, crisp at any height
            var img = Hud.Img(parent, name, OzUi.A(OzUi.Ink, OzDeployBar.HairAlpha));
            img.rectTransform.Place(new OzRect(x - w * 0.5f, y, w, OzDeployBar.LineBottom - OzDeployBar.LineTop));
            return img.rectTransform;
        }

        void SlidePart(RectTransform rt) { if (rt) slideParts.Add((rt, rt.anchoredPosition.x)); }

        void ApplySlide(float s)
        {
            foreach (var (rt, x) in slideParts)
            {
                if (!rt) continue;
                var at = new Vector2(x + s, rt.anchoredPosition.y);
                if (rt.anchoredPosition != at) rt.anchoredPosition = at;
            }
        }

        // ---------------------------------------------------------------- the objective card
        void BuildObjectiveCard(RectTransform p, OzDeployBar.Layout L)
        {
            cardRt = Hud.Node("ObjectiveCard", p);
            cardTag = Hud.Label(cardRt, "Tag", OzDeployBar.CardTag, 10, OzUi.Amber, 0.24f, OzStyle.Plain);
            cardHead = Hud.Label(cardRt, "Headline", "", OzDeployBar.HeadSize, OzUi.Ink, 0.04f);                // the shadowed material, as the old objective line
            cardSub = Hud.Label(cardRt, "Sub", "", 9, OzUi.Muted, 0.16f, OzStyle.Plain);
            // the card is as wide as the longest words of every level (measured with the game's own text) plus its pads, so it never
            // changes size; where the row leaves less room: no fort glyph, then the plain rule line, then a 16 u headline
            bool icon = L.wide, count = true; float hs = OzDeployBar.HeadSize;
            float Need()
            {
                float m = 0f;
                cardTag.Set(OzDeployBar.CardTag); m = Mathf.Max(m, cardTag.preferredWidth);
                cardHead.FontSize = hs;
                for (int i = 0; i < levels.Length; i++)
                {
                    string h = OzDeployBar.Headline(levels[i].Id);
                    cardHead.Set(OzDeployBar.TeamWords(h)); m = Mathf.Max(m, cardHead.preferredWidth);
                    cardHead.Set(OzDeployBar.TeamWords(TeamColors.Words(h))); m = Mathf.Max(m, cardHead.preferredWidth);
                    cardSub.Set(OzDeployBar.Sub(FlagsOf(i), count)); m = Mathf.Max(m, cardSub.preferredWidth);
                }
                return (icon ? OzDeployBar.IconX + OzDeployBar.IconSize + OzDeployBar.IconGap : OzDeployBar.TextX) + m + OzDeployBar.CardPadR;
            }
            float room = L.CardRoom, need = Need();
            while (need > room && (icon || count || hs > OzDeployBar.HeadSizeMin))
            {
                if (icon) icon = false; else if (count) count = false; else hs -= 1f;
                need = Need();
            }
            float w = Mathf.Min(need, room); cardCount = count;
            cardRt.Place(new OzRect(L.m, OzDeployBar.CardY, w, OzDeployBar.CardH));
            Hud.Plate(cardRt, "sm", 0f, OzUi.Amber, null, false, false);                                       // the kit's small plate (8 u chamfers) and its amber tab; no shadow inside the bar
            float rowMid = OzDeployBar.RowY + OzDeployBar.RowH * 0.5f - OzDeployBar.CardY;
            cardIcon = icon ? Hud.Icon(cardRt, "Fort", "fort", OzDeployBar.IconX, rowMid - OzDeployBar.IconSize * 0.5f, OzDeployBar.IconSize, OzUi.Mark(Team.Blue)) : null;   // the enemy's fort, in the enemy's colour
            float tx = icon ? OzDeployBar.IconX + OzDeployBar.IconSize + OzDeployBar.IconGap : OzDeployBar.TextX, box = Mathf.Max(40f, w - tx - OzDeployBar.CardBoxPadR);
            cardTag.At(tx, OzDeployBar.LabelY - OzDeployBar.CardY, OzH.L, false, box);
            cardHead.FontSize = hs; cardHead.Set(""); cardHead.At(tx, rowMid, OzH.L, true, box);
            cardSub.Set(""); cardSub.At(tx, OzDeployBar.HintY - OzDeployBar.CardY, OzH.L, false, box);
            objLine = Hud.Label(cardRt, "ObjectiveFull", "", 11, OzUi.Ink, 0f, OzStyle.Plain); objLine.SetActive(false);   // the long sentence (OzLevelDef.Objective) is not drawn any more; the menu's old writes land here
        }

        /// <summary>The level's flag count: its map's (OzLevelInfo), else its card's fallback chip ("13 FLAGS"), else 0.</summary>
        int FlagsOf(int i)
        {
            if (infos != null && i < infos.Length && infos[i] != null && infos[i].Ok) return infos[i].Flags;
            foreach (var c in levels[i].FallbackChips)
                if (c.EndsWith(" FLAGS") && int.TryParse(c.Substring(0, c.Length - 6), out int n)) return n;
            return 0;
        }

        void RefreshObjectiveCard(bool force)
        {
            int swap = TeamColors.Swap ? 1 : 0;
            if (!force && cardFor == focusCard && cardSwap == swap) return;
            cardFor = focusCard; cardSwap = swap;
            var d = levels[focusCard];
            cardHead.Set(OzDeployBar.TeamWords(TeamColors.Words(OzDeployBar.Headline(d.Id))));
            cardSub.Set(OzDeployBar.Sub(FlagsOf(focusCard), cardCount));
            if (cardIcon) cardIcon.color = OzUi.Mark(Team.Blue);
        }

        // ---------------------------------------------------------------- every frame on the battlefield screen (RefreshDeployBar)
        void RefreshDeployConsole(GameDifficulty d)
        {
            if (!cardRt) return;
            int slots = OzShuttleStart.HasSlot(levels[focusCard].Id) ? 3 : 2;
            if (slots != slotsWant)
            {
                slotsWant = slots; slideFrom = slideNow; slideTo = slots == 3 ? 0f : deployLay.Slide; slideAt = OzMotion.Now;
                Debug.Log($"[OpenZ Menu] deploy bar: {slots} blocks ({levels[focusCard].Id}): DIFFICULTY and HEAD START {(slots == 3 ? "make room for SHUTTLE" : "close up against DEPLOY")}");
            }
            float wait = slideTo > slideFrom ? Mathf.Max(0.01f, OzMotion.Dur(OzDeployBar.SlotFade, true)) : 0f;                      // closing up: after the SHUTTLE block has faded out
            float k = OzMotion.T(slideAt, OzMotion.Dur(OzMotion.Short), wait), s = Mathf.Lerp(slideFrom, slideTo, OzEase.OutCubic(k));   // CALM MODE: at once
            if (!Mathf.Approximately(s, slideNow)) { slideNow = s; ApplySlide(s); }
            RefreshShuttleStart(d, slots == 3);
            PlaceDeployTips();
            RefreshObjectiveCard(false);
        }

        void PlaceDeployTips()
        {
            float ty = -(OzDeployBar.TipBottom - OzDeployBar.TipH);
            if (headTip)
            {
                var at = new Vector2(OzDeployBar.TipX(deployLay, deployLay.X(1, 3) + slideNow, stripRight), ty);
                if (headTip.anchoredPosition != at) headTip.anchoredPosition = at;
            }
            if (shuttleTip)
            {
                var at = new Vector2(OzDeployBar.TipX(deployLay, deployLay.X(2, 3), stripRight), ty);
                if (shuttleTip.anchoredPosition != at) shuttleTip.anchoredPosition = at;
            }
        }

        // ---------------------------------------------------------------- proof hooks (OzShuttleStartProof, Play only)
        public RectTransform ProofCardRt => cardRt;
        public string ProofCardText => cardTag != null ? cardTag.text + " | " + cardHead.text + " | " + cardSub.text : "";
        public bool ProofCardGlyph => cardIcon;
        public float ProofBlockW => deployLay.blockW;
        public float ProofBlockX(int i, int n) => deployLay.X(i, n);
        public float ProofSlideNow => slideNow;
        public float ProofSlideStep => deployLay.Slide;
        public int ProofSlots => slotsWant;
        public float ProofStripRight => stripRight;
        public bool ProofWide => deployLay.wide;
    }
}
