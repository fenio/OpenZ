using UnityEngine;

namespace OpenZ
{
    /// <summary>Which canvas corner / edge a HUD element is anchored to (UI_STYLE_GUIDE.md 8.1: anchors carry the layout).</summary>
    public enum OzAnchor { TL, TC, TR, BL, BC, BR, C }

    /// <summary>
    /// A rectangle in the guide's convention: canvas units, top-left origin, measured on the 1920 x 1080 reference canvas.
    /// The anchor says which edge keeps its distance when the canvas is narrower (16:10 = 1728 u): TR / BR keep the distance
    /// to the right edge, TC / BC / C keep the offset from the centre, TL / BL keep x. The canvas is always 1080 u tall
    /// (CanvasScaler match height), so y never changes. Child rects use TL = relative to the parent's top-left corner.
    /// </summary>
    public readonly struct OzRect
    {
        public readonly OzAnchor a; public readonly float x, y, w, h;
        public OzRect(OzAnchor a, float x, float y, float w, float h) { this.a = a; this.x = x; this.y = y; this.w = w; this.h = h; }
        public OzRect(float x, float y, float w, float h) : this(OzAnchor.TL, x, y, w, h) { }
        public Vector2 Size => new Vector2(w, h);
        public OzRect Move(float dx, float dy) => new OzRect(a, x + dx, y + dy, w, h);
        public OzRect Resize(float nw, float nh) => new OzRect(a, x, y, nw, nh);

        /// <summary>Left edge on a canvas `cw` units wide (the Python preview uses the same rule).</summary>
        public float XOn(float cw) => a == OzAnchor.TR || a == OzAnchor.BR ? x - OzLayout.RefW + cw : a == OzAnchor.TC || a == OzAnchor.BC || a == OzAnchor.C ? x - OzLayout.RefW * 0.5f + cw * 0.5f : x;
    }

    /// <summary>
    /// Every HUD / end-screen / pause number of UI_STYLE_GUIDE.md sections 8-9 and Materials/ui/kit/README_IMPORT.md 4-4b, in
    /// and OzRects, simple arithmetic only) and draws the kit sprites at these numbers for the 1920x1080 and 2560x1600
    /// previews, so the preview IS this file. Change a number here and both the game and the preview follow.
    /// </summary>
    public static class OzLayout
    {
        // @layout begin
        public const float RefW = 1920, RefH = 1080, M = 16;
        public const float NarrowCanvas = 1578;   // menu_exe_v1: 16 + 710 command bar + 12 + 480 unit panel + 12 + 332 minimap + 16 (was 1620)

        // ---- top command strip v2 (guide 9.1 + 15, kit topbar layout_u), strip-local coordinates
        public static readonly OzRect TopStrip = new OzRect(OzAnchor.TC, 570, 12, 780, 64);
        public const float TopCountLX = 80, TopCountRX = 700, TopCountY = 11, TopCountSize = 22;
        public const float TopOf70Size = 11, TopOf70Gap = 4, TopOf70Y = 19;
        public static readonly OzRect TopBarL = new OzRect(79, 42, 216, 10);
        public static readonly OzRect TopBarR = new OzRect(485, 42, 216, 10);
        public static readonly OzRect TopClock = new OzRect(312, 8, 156, 48);
        public const float ClockDigitsMid = 21, ClockDigitsSize = 30, ClockSubMid = 41, ClockSubSize = 8;

        // ---- territory tab (guide 9.2 + 15), tab-local
        public static readonly OzRect TerrTab = new OzRect(OzAnchor.TC, 700, 74, 520, 28);
        public static readonly OzRect TerrSegs = new OzRect(64, 10, 392, 9);
        public const float TerrSegGap = 2, TerrSplitY = 5, TerrFlagSize = 14, TerrFlagLX = 12, TerrFlagRX = 494, TerrFlagY = 7;
        public const float TerrCountLX = 44, TerrCountRX = 476, TerrCountMid = 14.5f, TerrCountSize = 16;

        // ---- menu button, stored cannons, toasts, mission banner (guide 8.3, 9.8, 9.12, 9.17)
        public static readonly OzRect MenuButton = new OzRect(OzAnchor.TR, 1792, 16, 112, 34);
        public static readonly OzRect StoredCannon = new OzRect(OzAnchor.TL, 16, 16, 232, 44);
        public const float StoredCannonStep = 52, StoredCannonMax = 8;
        public static readonly OzRect Toast = new OzRect(OzAnchor.TC, 770, 118, 380, 38);
        public const float ToastStep = 46, ToastMax = 3, ToastIconX = 20, ToastIconY = 7, ToastIconSize = 24, ToastTextX = 54, ToastTextSize = 15;
        public static readonly OzRect FortAlert = new OzRect(OzAnchor.TC, 700, 118, 520, 44);
        public static readonly OzRect Banner = new OzRect(OzAnchor.TC, 640, 150, 640, 96);
        public const float BannerTextX = 26, BannerEyebrowY = 22, BannerLineY = 44, BannerFootY = 74;

        // ---- unit panel (guide 9.3 + kit 4), panel-local; x = CW - 840 at 1920
        public static readonly OzRect UnitPanel = new OzRect(OzAnchor.BR, 1080, 876, 480, 188);
        public static readonly OzRect UnitPortrait = new OzRect(18, 16, 156, 156);
        public const float UnitBadgeX = 24, UnitBadgeY = 22;
        public const float UnitNameX = 192, UnitNameY = 18, UnitNameSize = 30, UnitNameSmall = 26, UnitNameMaxW = 186;
        public const float UnitSubY = 56, UnitSubSize = 10;
        public static readonly OzRect UnitTypeWell = new OzRect(386, 16, 76, 40);
        public const float UnitTypeIconSize = 32, UnitTypeCountRX = 454, UnitTypeCountMid = 36;
        public const float UnitDividerY = 74, UnitDividerX0 = 192, UnitDividerX1 = 462;
        public const float UnitLabelY = 86, UnitValueY = 101, UnitWeaponIconX = 190, UnitWeaponIconY = 96, UnitWeaponIconSize = 26, UnitWeaponX = 222;
        public const float UnitNadeLabelX = 342, UnitNadeIconX = 340, UnitNadeIconSize = 24, UnitNadeX = 370;
        public const float UnitHpLabelY = 134, UnitHpValueRX = 462, UnitHpValueY = 131;
        public static readonly OzRect UnitHpBar = new OzRect(192, 150, 270, 14);
        public static readonly OzRect AlertTab = new OzRect(18, -26, 250, 28);
        public static readonly OzRect AlertHazard = new OzRect(3, 3, 52, 22);
        public static readonly OzRect AlertChip = new OzRect(13, 6, 32, 16);
        public const float AlertTextX = 66, AlertTextMid = 14.5f, AlertTextSize = 10;

        // ---- minimap (guide 9.5, kit mm_frame), frame-local
        public static readonly OzRect Minimap = new OzRect(OzAnchor.BR, 1572, 716, 332, 348);
        public static readonly OzRect MinimapWell = new OzRect(16, 32, 300, 300);
        public const float MinimapTitleX = 18, MinimapTitleY = 9, MinimapLevelRX = 314, MinimapLevelY = 10;

        // ---- command bar (guide 9.4 + kit cmdbar layout_u), bar-local
        public static readonly OzRect CommandBar = new OzRect(OzAnchor.BL, 16, 1004, 710, 60);
        public const float KeyRX0 = 12, KeyRStep = 54, KeyY = 8, KeyRW = 48, KeyH = 44;
        public const float KeyGX0 = 242, KeyGStep = 46, KeyGW = 42;
        public const float KeyLetterSize = 20, KeyCountRX = 42, KeyCountY = 29, KeyCountSize = 9;
        public const float GroupBadgeX = 3, GroupBadgeY = 2, GroupGlyphX = 5, GroupGlyphY = 19, GroupGlyphSize = 16;
        public const float GroupCountRX = 37, GroupCountY = 18, GroupCountSize = 15, GroupEmptyX = 7, GroupEmptyY = 5, GroupEmptySize = 10;

        // ---- factory list (guide 9.7), panel-local; bottom sits 12 u above the command bar
        public const float FactoryX = 16, FactoryBottom = 992, FactoryW = 330, FactoryHead = 30, FactoryTop = 36, FactoryRowStep = 62, FactoryMinY = 140;
        public static readonly OzRect FactoryRow = new OzRect(8, 36, 314, 56);
        public static readonly OzRect FactoryRowWell = new OzRect(8, 6, 44, 44);
        public const float FactoryRowTextX = 62, FactoryRowNameY = 9, FactoryRowUnitY = 25, FactoryRowTimeRX = 306, FactoryRowTimeY = 24;
        public const float FactoryPipsX = 252, FactoryPipsY = 9, FactoryBarsW = 236, FactoryProgY = 40, FactoryHpY = 47;

        // ---- production window (guide 9.6 + kit prod_frame), window-local; height = 326 + 98 x rows
        public const float ProdW = 452, ProdBaseH = 326, ProdRowH = 98, ProdAnchorDX = 56, ProdAnchorDY = 40, ProdTopLimit = 112;
        public const float ProdGlyphX = 16, ProdGlyphY = 12, ProdGlyphSize = 22, ProdTitleX = 46, ProdTitleY = 14, ProdTitleSize = 17;
        public const float ProdTechX = 46, ProdTechY = 36, ProdPipsX = 80, ProdHpX = 148;
        public static readonly OzRect ProdChip = new OzRect(308, 14, 92, 20);
        public static readonly OzRect ProdClose = new OzRect(408, 12, 26, 24);
        public static readonly OzRect ProdCurIcon = new OzRect(20, 70, 72, 72);
        public const float ProdCurNameX = 110, ProdCurNameY = 68, ProdCurNameSize = 22, ProdCurSubY = 96, ProdTimeRX = 436, ProdTimeY = 66, ProdTimeSize = 30;
        public static readonly OzRect ProdProgress = new OzRect(111, 117, 324, 10);
        public const float ProdFormulaY = 136, ProdBuildY = 166, ProdHintRX = 438;
        public const float ProdClassY = 180, ProdTileY = 192, ProdTileX0 = 15, ProdTileStep = 60, ProdTileW = 56, ProdTileH = 80, ProdTileCols = 7;
        public const float ProdTileIconX = 7, ProdTileIconY = 3, ProdTileIconSize = 42, ProdTileNameMid = 53, ProdTileTimeMid = 67;
        public const float ProdQueueY0 = 198, ProdQueueSlotY = 16, ProdQueueX0 = 15, ProdQueueStep = 58, ProdQueueSize = 52;
        public const float ProdQueueIconX = 8, ProdQueueIconY = 1, ProdQueueIconSize = 36, ProdQueueCapMid = 45, ProdRepeatX = 311, ProdRepeatDY = 30, ProdRepeatBtnX = 340, ProdRepeatBtnW = 98;
        public const float ProdActionDY = 82, ProdActionX0 = 15, ProdActionStep = 107, ProdActionW = 101, ProdActionH = 32;

        // ---- tooltip (guide 9.11)
        public const float TipDX = 18, TipDY = 20, TipMaxW = 320, TipPadX = 14, TipPadY = 9, TipTitleSize = 12, TipBodySize = 11;

        // ---- world overlays (guide 9.9)
        public const float BracketOut = 3, BracketArmMin = 6, BracketArmMax = 14, HpBarMinW = 26, HpBarMaxW = 48, HpBarH = 5, HpBarGap = 5;
        public const float HoverChipH = 18, HoverChipSize = 10, TimerRing = 40, TimerIcon = 22, TimerChipW = 44, TimerChipH = 18;
        // ONE "dog tag" per producing fort / factory, badge-local u,
        // origin = the medallion centre (= the building's screen anchor), y down. The medallion's rim IS the progress ring and the unit's
        // own card art fills it; the fused plate carries two rows: the time in a recessed glass well, and under it the building's level
        // as "TECH [n]" (the production window's word, Zod's level numeral in a socket). A cue flag on the plate's top edge says PAUSED /
        public const float TagR = 27, TagChO = 25.5f, TagChI = 21.5f, TagFillO = 25, TagFillI = 22, TagFaceR = 20.5f;
        public const float TagPlateW = 90, TagPlateH = 36, TagPlateCut = 6, TagCol0 = 31, TagColPad = 7, TagWellY = -14.5f, TagWellH = 18, TagWellCut = 2.5f, TagWellPad = 4;
        public const float TagDigits = 17, TagWord = 12, TagTechY = 10.5f, TagTechSize = 11, TagLvlSize = 12;
        public const float TagTabX = 26.5f, TagTabW = 20, TagTabH = 2.5f, TagNotchW = 14, TagNotchY = 22.5f, TagNotchTip = 33;
        public const float TagShadowX = 1.5f, TagShadowY = 2, TagShadowSoft = 0.5f, TagFrame = 35, TagFaceBox = 44, TagSliceL = 48, TagSliceR = 14, TagBodyTop = -35, TagBodyH = 75;
        public const float TagEdge = 8, TagMaxW = 132, TagBodyRX = 6, TagGlow = 45, TagGap = 4, TagEdgeFade = 24;
        public const float TagStripeX = 3, TagStripeY = -14, TagStripeW = 2, TagStripeH = 26;
        public const float TagFlagX = 28, TagFlagY = -30, TagFlagH = 13, TagFlagSize = 11, TagFlagPad = 6, TagFlagEnd = 7, TagFlagTrack = 0.04f;
        public const float TagCompactEnter = 130, TagCompactExit = 118;
        public static readonly OzRect TagTabBox = new OzRect(23, -19, 24, 4);
        public static readonly OzRect TagNotchBox = new OzRect(-8, 20, 16, 14);
        public static readonly OzRect TagTechBox = new OzRect(-28, -7, 56, 14);
        public static readonly OzRect TagLvlBox = new OzRect(7, -5, 18, 10);
        public static readonly OzRect TagLevelTag = new OzRect(-29, -9, 58, 18);
        public static readonly OzRect TagLevelTagBox = new OzRect(-31, -11, 64, 30);
        public static readonly OzRect TagLevelTagC = new OzRect(-12, -7, 24, 14);
        public static readonly OzRect TagLevelTagCBox = new OzRect(-14, -9, 30, 26);

        // ---- end screens (guide 9.13 + kit 4 refinements), absolute y, x from the canvas centre
        public const float EndBandY = 196, EndBandH = 236, EndHazardY0 = 188, EndHazardY1 = 432, EndHazardH = 8;
        public const float EndChipW = 150, EndChipH = 22, EndChipY = 216, EndWordmarkMid = 312, EndSubtitleMid = 397, EndSubtitleSize = 17;
        public const float EndFlankDX = 628, EndFlankY = 250, EndFlankW = 112, EndFlankH = 150, EndFlankWell = 96;
        public const float EndStatY = 470, EndStatW = 232, EndStatH = 132, EndStatGap = 16;
        public const float EndChartY = 618, EndChartW = 976, EndChartH = 168;
        public static readonly OzRect EndChartGraph = new OzRect(16, 38, 944, 106);
        public const float EndButtonsY = 822, EndButtonH = 50, EndHintY = 898;

        // ---- pause + options (guide 9.12), absolute y, x from the canvas centre
        public const float PauseW = 460, PauseH = 600, PauseY = 260, PauseDX = -472, OptionsDX = 12;
        public const float PauseWordX = 28, PauseWordY = 40, PauseWordCap = 56, PauseSubX = 30, PauseSubY = 116;
        public const float PauseBtnX = 28, PauseBtnY0 = 150, PauseBtnStep = 64, PauseBtnW = 404, PauseBtnH = 52, PauseFootY = 526;
        public const float OptHeader = 44, OptX = 24, OptSpeedLabelY = 64, OptSpeedY = 82, OptSpeedH = 44;
        public const float OptTabY = 46, OptTabH = 26, OptTabGap = 4, OptTabShift = 28, OptTabRowY0 = 88;   // options tabs (both menus): the strip under the title, MAIN's fixed controls moved down by OptTabShift, the other tabs' rows from OptTabRowY0; with tabs no tab has more than 7 rows, so the rows have room (OptToggleStep / OptToggleH)
        public const float OptSliderY0 = 166, OptSliderStep = 72, OptToggleY0 = 300, OptToggleStep = 56, OptToggleH = 44, OptSliderGap = 20, OptRowText = 12;
        // unit panel above the minimap on canvases narrower than NarrowCanvas (4:3): right-aligned, bottom at 1080 - 16 - 348 - 12 (guide 8.1)
        public static readonly OzRect UnitPanelNarrow = new OzRect(OzAnchor.BR, RefW - M - 480, RefH - M - 348 - 12 - 188, 480, 188);
        // menu_exe_v1: canvases RaisedCanvas .. NarrowCanvas (2560x1600 at 115 % and more): left of the minimap as on wide screens,
        // lifted 12 u above the command bar (bottom 992 = the factory list's bottom), its own place, never on the minimap
        public const float RaisedCanvas = 1186;
        public static readonly OzRect UnitPanelRaised = new OzRect(OzAnchor.BR, 1080, 1004 - 12 - 188, 480, 188);
        // @layout end

        /// <summary>menu_exe_v1: the unit panel's place on a canvas cw units wide (0 wide, 1 raised, 2 above the minimap).</summary>
        public static OzRect UnitPanelFor(float cw) => cw >= NarrowCanvas ? UnitPanel : cw >= RaisedCanvas ? UnitPanelRaised : UnitPanelNarrow;
        public static int UnitPanelMode(float cw) => cw >= NarrowCanvas ? 0 : cw >= RaisedCanvas ? 1 : 2;
    }
}
