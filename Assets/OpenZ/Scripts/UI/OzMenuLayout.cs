namespace OpenZ
{
    /// <summary>
    /// Front-end numbers (UI_STYLE_GUIDE.md 9.14 main menu, 9.15 level select, 9.16 loading screen, and the kit mock-ups
    /// kit_main_menu / kit_level_select), in canvas units on the 1080 u tall reference canvas, top-left origin. OzRects with
    /// a TR / BR / TC anchor are measured on the 1920 u canvas and keep their distance to that edge on other widths (OzRect).
    /// these numbers, so the offline preview IS this file.
    /// </summary>
    public static class OzMenuLayout
    {
        // @layout begin
        // ---- main menu (guide 9.14): left column
        public const float MenuX = 110, EyebrowX = 112, EyebrowY = 150, EyebrowSize = 12;
        public const float LogoY = 176, LogoPad = 40, LogoW = 517, LogoH = 187, UnderlineGap = 22;
        public const float ItemY0 = 390, ItemStep = 62, ItemSize = 26, ItemHoverSize = 32, ItemShift = 10, ItemCapDY = 6;
        public const float ItemHitDX = -20, ItemHitDY = -6, ItemHitW = 460, ItemHitH = 54;
        public const float RailX = 61, RailW = 3, RailH = 46, RailDY = -2;
        public const float HintGap = 18, HintDY = 17, HintSize = 11;
        public const float FooterY = 1020, FooterSize = 9;
        public const float VersionRX = 60, VersionY = 1046;
        // preview caption plate, bottom-right (CW - 420, 954)
        public static readonly OzRect Caption = new OzRect(OzAnchor.BR, 1500, 954, 360, 66);
        public static readonly OzRect CaptionChip = new OzRect(16, 14, 40, 18);
        public const float CaptionTitleX = 66, CaptionTitleY = 16, CaptionSubX = 16, CaptionSubY = 44;
        // test-mode warning chip (PlayerPrefs oz_testmode = 1), top-right
        public static readonly OzRect TestChip = new OzRect(OzAnchor.TR, 1704, 40, 136, 22);
        // side panels next to the list: options / controls (the pause menu's 460 x 560 plates) and credits
        public const float SideX = 620, SideY = 260, SideW = 460, SideH = 560;
        public const float CreditsX = 620, CreditsY = 150, CreditsW = 1000, CreditsH = 780, CreditsHeader = 44, CreditsPad = 28, CreditsBody = 13;
        public const float CpuRowLabelY = 64, CpuRowY = 82, CpuRowH = 44, CpuRowHintY = 136;

        // ---- level select (guide 9.15)
        public const float SelEyebrowX = 120, SelEyebrowY = 92, SelTitleX = 118, SelTitleY = 116, SelTitleSize = 44;
        public static readonly OzRect Back = new OzRect(OzAnchor.TR, 1650, 104, 150, 40);
        public const float SelHintRX = 1800, SelHintY = 160;
        public const float CardTop = 210, CardGap = 60, CardSide = 150, CardImgW = 810, CardImgH = 456, CardMeta = 118;
        public const float CardMaxImgH = 604, CardLift = 8, CardGlow = 24, CardShadowDX = 8, CardShadowDY = 10;
        public static readonly OzRect CardChip = new OzRect(24, 22, 76, 22);
        public static readonly OzRect CardPreviewChip = new OzRect(108, 22, 106, 22);
        public const float CardMiniWell = 112, CardMiniMargin = 24, CardMiniInset = 4;
        public const float CardLevelX = 30, CardLevelY = 22, CardNameX = 28, CardNameY = 42, CardNameSize = 42;
        public const float CardMoodRX = 28, CardMoodY = 24, CardChipsY = 66, CardChipH = 22, CardChipGap = 6, CardFadeH = 150;
        public const float CardProgInset = 16, CardProgH = 3;
        // deploy bar (full width, 118 u at the bottom)
        public const float DeployBarH = 118, ObjX = 120, ObjLabelY = 996, ObjLineY = 1016, ObjFootY = 1044, ObjSize = 16;
        public static readonly OzRect Deploy = new OzRect(OzAnchor.BR, 1500, 991, 300, 60);
        public static readonly OzRect HeadStart = new OzRect(OzAnchor.BR, 1276, 996, 196, 58);
        public static readonly OzRect Cpu = new OzRect(OzAnchor.BR, 1038, 996, 210, 58);
        public const float BlockLabelDY = 0, BlockRowDY = 14, BlockRowH = 34, BlockHintY = 1054;

        // ---- loading screen (guide 9.16): content block at x 96, bottom 96
        public const float LoadX = 96, LoadTop = 716, LoadW = 460, LoadCap = 96;
        public const float LoadWordY = 740, LoadGoalY = 858, LoadMeterY = 896, LoadBarY = 918, LoadBarH = 5, LoadTipY = 944;
        // @layout end
    }
}
