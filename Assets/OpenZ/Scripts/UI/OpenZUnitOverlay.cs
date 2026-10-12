using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// Z's on-map unit marks (zobject.cpp:555-808) in the kit's style (guide 9.9), drawn on a screen overlay canvas under
    /// the HUD panels:
    ///  - selection brackets: Bits/sel_brackets_lg / _sm (one sliced image, thin, half-transparent) in RED_HI / BLUE_HI /
    ///    NEUTRAL around every selected unit (the whole spread-out squad of the 3D view), 3 u outside it;
    ///  - Zod's small health bar over selected and hovered units: Bars/hp_world_frame with green = health and Zod's "lost"
    ///    yellow up to max health, the box itself as long as the unit is tough (zobject.cpp:611-700), ALERT under 30 %;
    ///  - hover label chip (unit type, a team bar, an amber star for own units instead of Zod's " *");
    ///  - control group badge at the top-left of selected units (zobject.cpp:796-805);
    ///  - Zod's building timer (zbuilding.cpp:516-542) over every producing fort / factory (enemy ones too, like Zod) as ONE
    ///    production badge (production_badge_v2, OzProductionBadge.cs: the unit's art inside the progress ring, the time and the
    ///    building's TECH level on the fused plate, READY / PAUSED / PLACE / SELECT states); amber brackets around the building whose
    ///    window is open;
    ///  - the "-ED" order marker (Cursors/mark_*, INK: amber is never used in world space) for 3 s after an order, the RALLY
    ///    chip on the open factory's last rally point, and a small grenade marker over every crate so map items read at a
    ///    glance.
    /// SelectionOverlay (ground rings, paths) keeps running; set hideSelectionRings to rely on the brackets only.
    /// </summary>
    [DefaultExecutionOrder(70)]                                                     // after UnitView (60) placed the models
    public class OpenZUnitOverlay : MonoBehaviour
    {
        [Tooltip("Zod bar length for 10000 HP in HUD units (Zod: 36 px at its 648x484 frame)")] public float barPer10k = 64f;
        [Tooltip("Shortest health bar (guide 9.9: 26 u)")] public float minBar = 26f;
        [Tooltip("Longest health bar (guide 9.9: 48 u)")] public float maxBar = 48f;
        public bool buildingTimers = true;
        /// <summary>
        /// production_badge_v2: TimerScale scales the whole production badge on top of the UI SCALE (the canvas
        /// scales with OzText.UiScale: x2.0 at 1600p with the 1.35 default), enemy badges EnemyTimerScale of that (was 0.72: their digits
        /// were 8 px at 1080p). TimerDigits is still the digit size of the bridge repair badge (BridgeRepairBadges) and matches the
        /// production badge's (OzLayout.TagDigits); TimerChipW / H / PausedW belonged to the old separate chip and are no longer read.</summary>
        public static float TimerDigits = 17f, TimerChipW = 60f, TimerChipH = 24f, TimerPausedW = 84f, TimerScale = 1f, EnemyTimerScale = 0.82f;
        /// <summary>production_badge_v2: TimerPortrait > 0 = the unit's art in the medallion (robot portrait / production icon), 0 = the
        /// class glyph instead. EnemyTimerPortrait shows the CPU's unit too (Zod: only your own; else the owner's insignia).</summary>
        public static float TimerPortrait = 36f; public static bool EnemyTimerPortrait = false;
        /// <summary>production_badge_v2: idle producers (nothing chosen) keep a badge with the TECH level: own = SELECT (TimerIdle, on), enemy
        /// = IDLE (EnemyTimerIdle, off). LevelTags: every robot / vehicle factory without a badge (neutral, idle enemy, destroyed) keeps its
        /// level as a small TECH [n] tag, like Zod's level_img (brobot.cpp:377-380, bvehicle.cpp:381). CompactEnemy / CompactOwn: badges
        /// shrink to the medallion above OzLayout.TagCompactEnter m of camera height (the overview; enemies on, own off).</summary>
        public static bool TimerIdle = true, EnemyTimerIdle = false, LevelTags = true, CompactEnemy = true, CompactOwn = false;
        /// <summary>production_badge_v2, view-only: this frame's badge / level tag screen rects by building id (px, bottom-left origin),
        /// read by the proof captures to crop around the real badge.</summary>
        public static readonly Dictionary<int, Rect> BadgeRects = new Dictionary<int, Rect>();
        [Tooltip("Small grenade marker over every grenade crate (map items readable at a glance)")] public bool itemMarkers = true;
        [Tooltip("Turn SelectionOverlay's rings off (brackets only, like Z)")] public bool hideSelectionRings;
        /// <summary>Options: SHOW HEALTH BARS always (60 % on every damaged unit on screen).</summary>
        public static bool AlwaysBars { get => PlayerPrefs.GetInt("oz_hp_always", 0) == 1; set => PlayerPrefs.SetInt("oz_hp_always", value ? 1 : 0); }

        OpenZHud hud; ZGame game; Canvas canvas; CanvasScaler scaler; RectTransform root;
        readonly List<Mark> marks = new List<Mark>(); int used;
        readonly List<OzProductionBadge> badges = new List<OzProductionBadge>(); int bUsed;                 // production_badge_v2: pooled visuals
        readonly List<OzLevelTag> lvlTags = new List<OzLevelTag>(); int lUsed;                              // the level alone (factories without a badge)
        readonly Dictionary<int, OzBadgeState> tags = new Dictionary<int, OzBadgeState>();                    // per building (lives off screen too)
        sealed class LevelState { public bool want, seen; public float at = -10f; }
        readonly Dictionary<int, LevelState> levelStates = new Dictionary<int, LevelState>();
        readonly List<Drawn> drawn = new List<Drawn>(); readonly List<int> dead = new List<int>();
        struct Drawn { public OzBadgeState st; public OzProductionBadge badge; public Vector2 pos; public float s, up, down, left, right; }
        RectTransform badgeRoot;                                                                              // first child of Root: every mark draws above it
        readonly List<ItemMark> items = new List<ItemMark>(); int iUsed;
        readonly Dictionary<ZObject, int> groupOf = new Dictionary<ZObject, int>();
        SelectionOverlay rings;
        Image order; float orderShownFor = -100f; string orderGlyph;
        RectTransform rally; bool always;

        sealed class Mark
        {
            public RectTransform rt; public Image glow, brackets, barFrame, barFill, barLost, badge, chipBg, chipBar, star, nade; public RectTransform bar, chip; public OzText label;
            public int group = -2;
        }
        sealed class ItemMark { public RectTransform rt; }

        public void Build(OpenZHud h)
        {
            hud = h; game = h.game;
            var go = new GameObject("OpenZ_UnitOverlay", typeof(RectTransform)) { layer = 5 };
            go.transform.SetParent(h.transform, false);
            canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = h.sortingOrder - 10;
            scaler = go.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = 1f;
            root = Hud.Node("Root", go.transform); root.Fill(); root.pivot = Vector2.zero;
            badgeRoot = Hud.Node("Badges", root); badgeRoot.Fill(); badgeRoot.pivot = Vector2.zero;           // production_badge_v2: Root's first child (every mark draws above it)
            rings = FindFirstObjectByType<SelectionOverlay>();
            order = Hud.Kit(root, "OrderMarker", "Cursors/mark_placed", new OzRect(0, 0, 28, 28), OzUi.A(OzUi.Ink, 0.85f), Image.Type.Simple);
            order.rectTransform.anchorMin = order.rectTransform.anchorMax = Vector2.zero; order.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            order.gameObject.SetActive(false);
            rally = Hud.Node("RallyChip", root); rally.anchorMin = rally.anchorMax = Vector2.zero; rally.pivot = new Vector2(0, 0.5f); rally.sizeDelta = new Vector2(52, 16);
            Hud.Chip(rally, "Chip", new OzRect(0, 0, 52, 16), "RALLY", OzUi.A(OzUi.GM950, 0.92f), OzUi.Ink, 9, 0.16f, OzUi.A(OzUi.Khaki, 0.24f), 3);
            rally.gameObject.SetActive(false);
        }

        public void SetVisible(bool v) { if (canvas) canvas.enabled = v; }
        /// <summary>ui_quality_v1: a screen point on a whole pixel, in canvas units (world-following marks never sit between pixels).</summary>
        static Vector2 Px(Vector3 screen, float sf) => new Vector2(Mathf.Round(screen.x), Mathf.Round(screen.y)) / sf;
        static Vector2 SnapU(Vector2 u, float sf) => new Vector2(Mathf.Round(u.x * sf), Mathf.Round(u.y * sf)) / sf;
        public void SetScale(float s) { if (scaler) scaler.referenceResolution = new Vector2(1920f, 1080f) / Mathf.Max(0.5f, s); }

        // ------------------------------------------------------------------ pools
        Mark NextMark()
        {
            if (used >= marks.Count)
            {
                var m = new Mark(); m.rt = Hud.Node("Mark", root); m.rt.anchorMin = m.rt.anchorMax = Vector2.zero; m.rt.pivot = Vector2.zero;
                // 8 px padding = 4 canvas units at 200 ppu): post bloom cannot reach the ScreenSpaceOverlay canvas, so the glow is a sprite
                m.glow = Hud.Kit(m.rt, "Glow", "Bits/sel_brackets_lg_glow", new OzRect(0, 0, 48, 48), Color.clear, Image.Type.Sliced, false);
                m.glow.rectTransform.Fill(-GlowPad, -GlowPad, -GlowPad, -GlowPad); m.glow.raycastTarget = false;
                m.brackets = Hud.Kit(m.rt, "Brackets", "Bits/sel_brackets_lg", new OzRect(0, 0, 40, 40), Color.white, Image.Type.Sliced, false);
                m.brackets.rectTransform.Fill();
                if (!OzKit.Available) { m.brackets.color = Color.clear; }
                m.bar = Hud.Node("Bar", m.rt); m.bar.anchorMin = m.bar.anchorMax = new Vector2(0.5f, 1); m.bar.pivot = new Vector2(0.5f, 0);
                m.barFrame = Hud.Kit(m.bar, "Frame", "Bars/hp_world_frame", new OzRect(0, 0, 16, 5), null); m.barFrame.rectTransform.Fill();
                if (!OzKit.Available) m.barFrame.color = OzUi.A(Color.black, 0.78f);
                m.barLost = Hud.Img(m.bar, "Lost", OzUi.HpLost); m.barLost.rectTransform.Fill(1, 1, 1, 1);
                m.barFill = Hud.Img(m.bar, "Fill", OzUi.Go); m.barFill.rectTransform.Fill(1, 1, 1, 1);
                if (OzKit.Available)
                {
                    m.barLost.sprite = OzKit.GetSprite("Bars/bar_fill_lost"); m.barFill.sprite = OzKit.GetSprite("Bars/bar_fill_health");
                    m.barLost.color = m.barFill.color = Color.white;
                }
                foreach (var f in new[] { m.barLost, m.barFill }) { f.type = Image.Type.Filled; f.fillMethod = Image.FillMethod.Horizontal; f.fillOrigin = 0; }
                m.nade = Hud.Img(m.bar, "Nade", OzUi.Amber); m.nade.preserveAspect = true; m.nade.raycastTarget = false;
                var nrt = m.nade.rectTransform; nrt.anchorMin = nrt.anchorMax = new Vector2(1f, 0.5f); nrt.pivot = new Vector2(0f, 0.5f);
                nrt.anchoredPosition = new Vector2(2f, 0f); nrt.sizeDelta = new Vector2(9f, 9f);
                if (OzKit.Available) Hud.SetIcon(m.nade, "grenade", 24);
                m.nade.enabled = false;
                m.badge =Hud.Kit(m.rt, "Group", "Badges/badge_1_sm", new OzRect(-4, -4, 14, 14), null, Image.Type.Simple);
                m.badge.rectTransform.anchorMin = m.badge.rectTransform.anchorMax = new Vector2(0, 1); m.badge.rectTransform.pivot = new Vector2(0, 1);
                m.badge.rectTransform.anchoredPosition = new Vector2(-4, 4);
                m.chip = Hud.Node("Label", m.rt); m.chip.anchorMin = m.chip.anchorMax = new Vector2(0.5f, 1); m.chip.pivot = new Vector2(0.5f, 0);
                m.chipBg = Hud.Kit(m.chip, "Bg", "Panels/chip_3", new OzRect(0, 0, 16, 16), OzUi.A(OzUi.GM950, 0.92f)); m.chipBg.rectTransform.Fill();
                m.chipBar = Hud.Kit(m.chip, "Team", "Bits/stripe", new OzRect(1, 1, 3, OzLayout.HoverChipH - 2), OzUi.RedHi);
                m.label = Hud.Label(m.chip, "Text", "", OzLayout.HoverChipSize, OzUi.Ink, 0.12f, OzStyle.Plain);
                m.star = Hud.Kit(m.chip, "Star", "Bits/star", new OzRect(0, 0, 7, 7), OzUi.Amber, Image.Type.Simple);
                marks.Add(m);
            }
            var mk = marks[used++]; mk.rt.gameObject.SetActiveFast(true); return mk;
        }

        /// <summary>production_badge_v2: the next pooled production badge / level tag (built once, reused every frame), under Badges.</summary>
        OzProductionBadge NextBadge()
        {
            if (bUsed >= badges.Count) badges.Add(new OzProductionBadge(badgeRoot));
            var r = badges[bUsed++]; r.SetActive(true); return r;
        }

        OzLevelTag NextLevelTag()
        {
            if (lUsed >= lvlTags.Count) lvlTags.Add(new OzLevelTag(badgeRoot));
            var r = lvlTags[lUsed++]; r.SetActive(true); return r;
        }

        ItemMark NextItem()
        {
            if (iUsed >= items.Count)
            {
                var m = new ItemMark(); m.rt = Hud.Node("Item", root); m.rt.anchorMin = m.rt.anchorMax = Vector2.zero; m.rt.pivot = new Vector2(0.5f, 0f); m.rt.sizeDelta = new Vector2(20, 24);
                var bg = Hud.Kit(m.rt, "Bg", "Panels/chip_3", new OzRect(0, 0, 20, 18), OzUi.A(OzUi.GM950, 0.85f));
                Hud.Kit(m.rt, "Edge", "Panels/chip_3_outline", new OzRect(0, 0, 20, 18), OzUi.A(OzUi.Ink, 0.5f)).enabled = OzKit.Available;
                Hud.Icon(m.rt, "Glyph", "grenade", 4, 3, 12, OzUi.Ink);
                var tip = Hud.Img(m.rt, "Pointer", OzUi.A(OzUi.Ink, 0.7f)); tip.rectTransform.Place(new OzRect(9, 18, 2, 6));
                items.Add(m);
            }
            var r = items[iUsed++]; r.rt.gameObject.SetActiveFast(true); return r;
        }

        // ------------------------------------------------------------------ frame
        float nextAlways;
        Camera rtsCamC; RtsCamera rtsCamOf;
        Camera RtsCam() { if (rtsCamOf != hud.rts || !rtsCamC) { rtsCamOf = hud.rts; rtsCamC = hud.rts.GetComponent<Camera>(); } return rtsCamC; }
        void LateUpdate()
        {
            used = 0; bUsed = 0; lUsed = 0; iUsed = 0;
            if (hud == null || game == null || game.World == null || !canvas || !canvas.enabled || hud.Modal) { HoverMember = null; HoverShownId = -1; HoverShown01 = -1f; Hide(); return; }
            if (hideSelectionRings && rings && rings.enabled) rings.enabled = false;
            var cam = hud.rts ? RtsCam() : Camera.main; if (!cam) { Hide(); return; }
            float sf = canvas.scaleFactor > 0 ? canvas.scaleFactor : 1f;
            var W = game.World; var sel = hud.input.Selection;
            if (Time.unscaledTime > nextAlways) { nextAlways = Time.unscaledTime + 0.5f; always = AlwaysBars; }

            groupOf.Clear();
            var groups = hud.Bridge?.Groups;
            if (groups != null) foreach (var kv in groups) foreach (var o in kv.Value) if (o != null && !groupOf.ContainsKey(o)) groupOf[o] = kv.Key;

            HoverMember = null; HoverShownId = -1; HoverShown01 = -1f;
            ZObject hover = hud.PointerOverUi ? null : HoverUnit(cam, hud.Pointer);
            var pointed = HoverMember;                                                                       // the robot under the pointer itself (a squad member, not its leader)
            foreach (var u in sel)
            {
                if (u.Destroyed) continue;
                if (!UnitRect(cam, u, out var r)) continue;
                bool split = SquadSplit(u);
                Draw(r, sf, u, BracketColor(u), 1, u == hover, groupOf.TryGetValue(u, out var g) ? g : -1, !split, 1f, false, u == hover ? pointed : null);
                if (split) DrawSquadBars(cam, sf, u, 1f, u == hover ? pointed : null);
                if (u == hover) HoverRectLog(u, r);
            }
            if (hover != null && !sel.Contains(hover) && UnitRect(cam, hover, out var hr))
            {
                bool split = SquadSplit(hover);
                Draw(hr, sf, hover, BracketColor(hover), 2, true, -1, !split, 1f, false, pointed);
                if (split) DrawSquadBars(cam, sf, hover, 1f, pointed);
                HoverRectLog(hover, hr);
            }
            if (always)
                foreach (var o in W.Objects)
                {
                    if (!o.IsUnit || o.IsMinion || o.Destroyed || o.KillAt >= 0 || o.Health >= o.MaxHealth || sel.Contains(o) || o == hover) continue;
                    if (!UnitRect(cam, o, out var ar)) continue;
                    if (SquadSplit(o)) DrawSquadBars(cam, sf, o, 0.6f); else Draw(ar, sf, o, default, 0, false, -1, true, 0.6f);
                }

            var open = hud.Production ? hud.Production.Building : null;
            if (open != null)
            {
                var br = MeshRect(cam, open, out var mr) ? mr : hud.input.ScreenRect(open);
                if (br.width > 0 && br.height > 0) Draw(br, sf, open, OzUi.Amber, 1, false, -1, false, 1f);
            }
            if (buildingTimers) DrawBadges(cam, sf);
            if (itemMarkers)
                foreach (var o in W.Objects)
                {
                    if (o.Kind != ZKind.MapItem || o.Type != "grenades" || o.Destroyed || o.KillAt >= 0) continue;
                    var sp = cam.WorldToScreenPoint(game.CenterWorld(o) + Vector3.up * 1.6f);
                    if (sp.z < 0 || sp.x < -20 || sp.x > Screen.width + 20 || sp.y < -20 || sp.y > Screen.height + 20) continue;
                    var m = NextItem(); m.rt.anchoredPosition = Px(new Vector2(sp.x, sp.y + 2f * sf * Mathf.Sin(Time.unscaledTime * 2.2f + o.Id)), sf);   // ui_quality_v1: whole pixels
                }
            DrawOrderMarker(cam, sf);
            DrawRallyChip(cam, sf, open);
            Hide();
        }

        void Hide()
        {
            for (int i = used; i < marks.Count; i++) marks[i].rt.gameObject.SetActiveFast(false);
            for (int i = bUsed; i < badges.Count; i++) badges[i].SetActive(false);
            for (int i = lUsed; i < lvlTags.Count; i++) lvlTags[i].SetActive(false);
            for (int i = iUsed; i < items.Count; i++) items[i].rt.gameObject.SetActiveFast(false);
            if (hud == null || hud.Modal || !canvas || !canvas.enabled) { if (order) order.gameObject.SetActiveFast(false); if (rally) rally.gameObject.SetActiveFast(false); }
        }

        // ------------------------------------------------------------------ production badges (production_badge_v2)
        /// <summary>This frame's UnitBuilt / CannonStored events by building id (what left the door / was stored): read in Update, before
        /// CombatView clears the Sim's event list in its LateUpdate (the FactoryViews / OpenZHud pattern). View-only.</summary>
        readonly Dictionary<int, string> builtNow = new Dictionary<int, string>();
        int evSeen; CombatView combatView; bool lookedCv, compactView;

        void Update()
        {
            builtNow.Clear();
            if (game == null || game.World == null) return;
            var W = game.World; var ev = W.Events;
            if (!lookedCv) { lookedCv = true; combatView = FindFirstObjectByType<CombatView>(); }
            int start = combatView && combatView.enabled ? 0 : (ev.Count < evSeen ? 0 : evSeen);        // CombatView clears the list every frame
            for (int i = start; i < ev.Count; i++)
            {
                var e = ev[i];
                if (e.type == ZEventType.UnitBuilt) { var u = W.GetAny(e.objId); builtNow[e.otherId] = u?.Type; }               // objId unit, otherId building
                else if (e.type == ZEventType.CannonStored) { var b = W.GetAny(e.objId); builtNow[e.objId] = b != null && b.StoredCannons.Count > 0 ? b.StoredCannons[b.StoredCannons.Count - 1] : null; }
            }
            evSeen = ev.Count;
        }

        /// <summary>Top to bottom on screen (no per-frame allocation: a static comparer, not a lambda).</summary>
        sealed class TopDown : IComparer<Drawn> { public int Compare(Drawn a, Drawn c) => c.pos.y.CompareTo(a.pos.y); }
        static readonly TopDown topDown = new TopDown();

        /// <summary>Anchor handling at the screen edges: while the anchor is on screen the badge is kept TagEdge u inside (fully
        /// readable); once the anchor leaves, the badge slides off with it and fades within TagEdgeFade u, then it is culled (no pop, never
        /// far from its building). lo / hi = the badge's extents toward the lower / higher coordinate. Returns the alpha factor.</summary>
        static float Edge(ref float p, float a, float lo, float hi, float size, float e)
        {
            float min = e + lo, max = Mathf.Max(min, size - e - hi);
            if (a < 0f) { p = min + a; return Mathf.Clamp01(1f + a / OzLayout.TagEdgeFade); }
            if (a > size) { p = max + (a - size); return Mathf.Clamp01(1f - (a - size) / OzLayout.TagEdgeFade); }
            p = Mathf.Clamp(p, min, max);
            return 1f;
        }

        /// <summary>Zod's building timer (zbuilding.cpp:516-542; every producing building, enemy ones too: the server relays building
        /// state to all players, zserver.cpp:2398-2413) as the one-piece production badge (OzProductionBadge), and Zod's level_img on
        /// every factory without a badge (OzLevelTag). Per building an OzBadgeState watches the Sim (read only) on and off screen: unit
        /// ready (the Sim's events), enter / exit fades, the face swap, the plate width, the compact form. On screen: anchor =
        /// CenterWorld(b) + 7 m up on a whole pixel (the medallion centre), then neighbouring badges are nudged apart (upper one keeps its
        /// place) and the edge rule applies (Edge). Enemy badges turn compact (the medallion only) above TagCompactEnter m.</summary>
        /// <summary></summary>
        public static int DeadTagsOff;
        bool ls0(int id) => levelStates.TryGetValue(id, out var l) && l.want;
        void DrawBadges(Camera cam, float sf)
        {
            var W = game.World; float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            bool limRed = W.UnitLimitReached(Team.Red), limBlue = W.UnitLimitReached(Team.Blue);        // once per frame, not per building
            var player = hud.Player;
            float camH = hud.rts ? hud.rts.height : 70f;
            compactView = compactView ? camH >= OzLayout.TagCompactExit : camH >= OzLayout.TagCompactEnter;
            var size = badgeRoot.rect.size; float e = OzLayout.TagEdge;
            BadgeRects.Clear();
            foreach (var st in tags.Values) st.Seen = false;
            foreach (var lt in levelStates.Values) lt.seen = false;
            drawn.Clear();
            foreach (var b in W.Objects)
            {
                if (!b.IsProducer) continue;
                bool own = b.Owner == player;
                bool want = OzBadgeState.Wants(b, own);
                bool built = builtNow.TryGetValue(b.Id, out var builtType);
                if (!tags.TryGetValue(b.Id, out var st) && (want || built)) { st = new OzBadgeState(b.Id); tags[b.Id] = st; }
                if (st != null)
                {
                    st.Observe(b, own, want, built, builtType, W.Time, now);
                    if (!st.Alive(now)) st = null;
                }
                var sp = cam.WorldToScreenPoint(game.CenterWorld(b) + Vector3.up * 7f);
                if (sp.z < 0) continue;
                var anc = Px(sp, sf);
                // ---- the level alone (Zod's level_img on every robot / vehicle factory): neutral, idle enemy, destroyed, own idle with TimerIdle off
                // a destroyed building shows no level tag (it fades out with the building; a rebuilt one gets it back)
                bool tagWant = LevelTags && st == null && !b.IsFort && !b.Destroyed;
                if (b.Destroyed && ls0(b.Id)) DeadTagsOff++;
                if (!levelStates.TryGetValue(b.Id, out var ls) && tagWant) { ls = new LevelState(); levelStates[b.Id] = ls; }
                if (ls != null)
                {
                    ls.seen = true;
                    if (tagWant != ls.want) { ls.want = tagWant; ls.at = now; }
                    float lk = Mathf.Clamp01((now - ls.at) / Mathf.Max(0.001f, OzMotion.Dur(tagWant ? OzBadgeState.Enter : OzBadgeState.Exit, true)));
                    float la = tagWant ? lk : 1f - lk;
                    float ts = own ? TimerScale : TimerScale * EnemyTimerScale;
                    var B = compactView ? OzLayout.TagLevelTagCBox : OzLayout.TagLevelTagBox;
                    float px = anc.x, py = anc.y;
                    la *= Edge(ref px, anc.x, -B.x * ts, (B.x + B.w) * ts, size.x, e) * Edge(ref py, anc.y, (B.y + B.h) * ts, -B.y * ts, size.y, e);
                    if (la > 0.004f)
                    {
                        var tag = NextLevelTag(); tag.Rt.anchoredPosition = SnapU(new Vector2(px, py), sf);
                        int col = b.Destroyed || b.Owner == Team.Neutral ? 2 : own ? 0 : 1;
                        Color sc = b.Destroyed ? (Color)OzUi.NeutralDeep : b.Owner == Team.Neutral ? (Color)OzUi.Neutral : OzUi.Mark(b.Owner);
                        tag.Apply(ZBuildLists.DisplayTech(b.Type, b.BuildLevel), col, sc, compactView, la, sf, ts);
                        BadgeRects[b.Id] = new Rect((px + B.x * ts) * sf, (py - (B.y + B.h) * ts) * sf, B.w * ts * sf, B.h * ts * sf);
                    }
                }
                if (st == null) continue;
                // ---- the badge
                bool paused = b.Owner == Team.Red ? limRed : b.Owner == Team.Blue && limBlue;
                bool compact = own ? CompactOwn && compactView : CompactEnemy && compactView;
                if (st.Want) st.Build(b, own, paused, compact, now, dt); else st.Fade(now);
                float s = own ? TimerScale : TimerScale * EnemyTimerScale;
                var badge = NextBadge();
                float need = badge.Measure(ref st.View);
                st.View.plateW = st.Plate(need, now, sf * s);
                float k = s * st.View.scale, full = 1f - st.View.compact;
                bool flag = !string.IsNullOrEmpty(st.View.flag) && st.View.flagAlpha > 0f && full > 0f;
                drawn.Add(new Drawn
                {
                    st = st, badge = badge, pos = anc, s = s,
                    up = Mathf.Max(OzLayout.TagR, flag ? -OzLayout.TagFlagY : 0f) * k, down = (OzLayout.TagNotchTip + OzLayout.TagShadowY) * k,
                    left = (OzLayout.TagR + 1f) * k, right = Mathf.Lerp(OzLayout.TagR + 1f, st.View.plateW + OzLayout.TagShadowX, full) * k,
                });
            }
            // neighbours: sort top to bottom on screen; a badge overlapping one above it moves down until it clears (eased)
            drawn.Sort(topDown);
            float gap = OzLayout.TagGap, ease = 1f - Mathf.Exp(-14f * dt);
            for (int i = 0; i < drawn.Count; i++)
            {
                var d = drawn[i];
                float shift = 0f;
                for (int pass = 0; pass < 4; pass++)
                {
                    bool moved = false;
                    for (int j = 0; j < i; j++)
                    {
                        var o = drawn[j]; var op = o.pos + o.st.Nudge;
                        bool xo = d.pos.x - d.left < op.x + o.right + gap && d.pos.x + d.right > op.x - o.left - gap;
                        float top = d.pos.y - shift + d.up, bottom = d.pos.y - shift - d.down;
                        if (xo && top > op.y - o.down - gap && bottom < op.y + o.up + gap) { shift = d.pos.y + d.up - (op.y - o.down - gap); moved = true; }
                    }
                    if (!moved) break;
                }
                shift = Mathf.Min(shift, 1.6f * (d.up + d.down));
                var target = new Vector2(0f, -shift);
                d.st.Nudge = (d.st.Nudge - target).sqrMagnitude < 0.01f ? target : Vector2.Lerp(d.st.Nudge, target, ease);
                var p = d.pos + d.st.Nudge;
                float ea = Edge(ref p.x, d.pos.x, d.left, d.right, size.x, e) * Edge(ref p.y, d.pos.y + d.st.Nudge.y, d.down, d.up, size.y, e);
                if (ea <= 0.004f) { d.badge.SetActive(false); continue; }
                d.st.View.alpha *= ea;
                d.badge.Rt.anchoredPosition = SnapU(p, sf);                                                   // whole pixels
                d.badge.Apply(ref d.st.View, sf, d.s);
                BadgeRects[d.st.Id] = new Rect((p.x - d.left) * sf, (p.y - d.down) * sf, (d.left + d.right) * sf, (d.up + d.down) * sf);
            }
            dead.Clear();
            foreach (var kv in tags) if (!kv.Value.Seen || (!kv.Value.Want && !kv.Value.Alive(now))) dead.Add(kv.Key);
            foreach (var id in dead) tags.Remove(id);
            dead.Clear();
            foreach (var kv in levelStates) if (!kv.Value.seen || (!kv.Value.want && now - kv.Value.at > 0.5f)) dead.Add(kv.Key);
            foreach (var id in dead) levelStates.Remove(id);
        }

        /// <summary>
        /// OUR units = RTS green brackets + green glow (the ground ring's green, whatever team colour the
        /// player chose); an enemy = the enemy's shown team colour (blue when the enemy is blue, red when the player chose blue);
        /// neutral grey. Buildings keep the amber frame; health bars keep green / amber / red by health.</summary>
        public static Color OwnSelection = new Color(0.34f, 0.96f, 0.46f, 1f);
        Color BracketColor(ZObject o) => o.Owner == hud.Player ? OwnSelection : o.Owner == Team.Neutral ? (Color)OzUi.Neutral : OzUi.Mark(o.Owner);

        /// <summary>(g) proof: hover rect changes of a unit that has not moved (the rect is logged in whole pixels; a still unit under a
        /// still camera must give 0 changes). Counters read by the Play check.</summary>
        public static int HoverSamples, HoverRectChanges; int hoverId = -1; Rect hoverLast; Vector3 hoverPos, hoverCam;
        void HoverRectLog(ZObject o, Rect r)
        {
            var cam = hud.rts ? hud.rts.transform.position : Vector3.zero; var p = ViewPos(o);
            var q = Rect.MinMaxRect(Mathf.Round(r.xMin), Mathf.Round(r.yMin), Mathf.Round(r.xMax), Mathf.Round(r.yMax));
            if (o.Id == hoverId && (p - hoverPos).sqrMagnitude < 1e-6f && (cam - hoverCam).sqrMagnitude < 1e-6f)
            {
                HoverSamples++;
                if (q != hoverLast) { HoverRectChanges++; Debug.Log($"[OzBracket] hover rect of still {o.Type} #{o.Id} changed {hoverLast} -> {q}"); }
            }
            hoverId = o.Id; hoverLast = q; hoverPos = p; hoverCam = cam;
        }

        /// <summary>brackets: 0 none, 1 selected (lg / sm by size, 0.85 alpha), 2 hover only (sm, thinner, 0.7 alpha).</summary>
        /// <summary>H15 bracket glow: padding of the baked glow sprites in canvas units, and the halo alpha when selected.</summary>
        public const float GlowPad = 4f; public static float GlowAlpha = 0.42f;
        /// <summary>
        /// A member is apart beyond SquadSplitM of the squad centre (drawn positions)
        /// and back in under SquadJoinM (hysteresis: no flicker at the edge). Spread: the squad frame keeps its width without a bar; the
        /// members still together share ONE bar over their own group (the leader's health while he is among them, as the squad bar
        /// shows today), and every member that is apart gets its own small bar over its head with its own health.</summary>
        public static float SquadSplitM = 4.5f, SquadJoinM = 3.5f;
        /// <summary>Play check: frames that drew a spread squad, member bars drawn.</summary>
        public static int SplitFrames, MemberBars;
        readonly HashSet<int> apartIds = new HashSet<int>();
        static readonly List<ZObject> sqAll = new List<ZObject>(), sqCore = new List<ZObject>(), sqApart = new List<ZObject>();
        static readonly List<Vector3> sqPts = new List<Vector3>();

        bool SquadSplit(ZObject u)
        {
            if (u == null || u.Kind != ZKind.Robot || u.Minions.Count == 0) return false;
            sqAll.Clear(); sqAll.Add(u);
            foreach (var m in u.Minions) if (m != null && !m.Destroyed && m.KillAt < 0) sqAll.Add(m);
            if (sqAll.Count < 2) { apartIds.Remove(u.Id); return false; }
            var c = Vector3.zero;
            foreach (var m in sqAll) { var p = ViewPos(m); c += new Vector3(p.x, 0f, p.z); }
            c /= sqAll.Count;
            bool any = false;
            foreach (var m in sqAll)
            {
                var p = ViewPos(m); float d = new Vector2(p.x - c.x, p.z - c.z).magnitude;
                bool apart = d > (apartIds.Contains(m.Id) ? SquadJoinM : SquadSplitM);
                if (apart) { apartIds.Add(m.Id); any = true; } else apartIds.Remove(m.Id);
            }
            return any;
        }

        /// <summary>pointed = the squad member under the pointer (null = none): the shared bar of the members still together shows
        /// its health while the pointer is on one of them.</summary>
        void DrawSquadBars(Camera cam, float sf, ZObject u, float alpha, ZObject pointed = null)
        {
            sqCore.Clear(); sqApart.Clear();
            foreach (var m in sqAll) (apartIds.Contains(m.Id) ? sqApart : sqCore).Add(m);
            SplitFrames++;
            if (sqCore.Count > 0)
            {
                sqPts.Clear(); foreach (var m in sqCore) sqPts.Add(ViewPos(m));
                if (PointsRect(cam, sqPts, 0.55f, 1.9f, out var cr)) Draw(cr, sf, sqCore.Contains(u) ? u : sqCore[0], default, 0, false, -1, true, alpha, true, pointed != null && sqCore.Contains(pointed) ? pointed : null);
            }
            foreach (var m in sqApart)
            {
                sqPts.Clear(); sqPts.Add(ViewPos(m));
                if (PointsRect(cam, sqPts, 0.55f, 1.9f, out var mr)) { Draw(mr, sf, m, default, 0, false, -1, true, alpha, true, m == pointed ? m : null); MemberBars++; }
            }
        }

        static bool PointsRect(Camera cam, List<Vector3> pts, float rad, float h, out Rect r)
        {
            Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue);
            bool any = false;
            foreach (var p in pts)
                for (int i = 0; i < 8; i++)
                {
                    var s = cam.WorldToScreenPoint(p + new Vector3((i & 1) == 0 ? -rad : rad, (i & 4) == 0 ? 0 : h, (i & 2) == 0 ? -rad : rad));
                    if (s.z < 0) continue;
                    any = true; mn = Vector2.Min(mn, s); mx = Vector2.Max(mx, s);
                }
            r = any ? Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y) : default;
            return any && mx.x > 0 && mn.x < Screen.width && mx.y > 0 && mn.y < Screen.height;
        }

        /// <summary>hpOf = the robot under the pointer when it belongs to o's squad (null = none): the bar then shows that robot's
        /// own health (every squad member has its own), never the leader's in its place. The star and the grenade icon stay the
        /// squad's (o).</summary>
        void Draw(Rect screen, float sf, ZObject o, Color bracket, int brackets, bool hovered, int group, bool bar, float alpha, bool barOnly = false, ZObject hpOf = null)
        {
            var m = NextMark();
            var ho = hpOf != null && !hpOf.Destroyed && hpOf.MaxHealth > 0 ? hpOf : o;                        // whose health the bar shows
            float out3 = OzLayout.BracketOut;
            var r = new Rect(screen.x / sf - out3, screen.y / sf - out3, screen.width / sf + 2 * out3, screen.height / sf + 2 * out3);   // Zod: 3 px outside the unit
            m.rt.anchoredPosition = SnapU(r.position, sf); m.rt.sizeDelta = SnapU(r.size, sf);                 // ui_quality_v1: brackets on whole pixels
            m.brackets.enabled = brackets > 0 && OzKit.Available;
            if (m.brackets.enabled)
            {
                bool big = brackets == 1 && Mathf.Min(r.width, r.height) >= 36f;
                var s = OzKit.GetSprite(big ? "Bits/sel_brackets_lg" : "Bits/sel_brackets_sm");
                if (m.brackets.sprite != s) m.brackets.sprite = s;
                var c = bracket; c.a = brackets == 1 ? 0.85f : 0.7f;
                if (m.brackets.color != c) m.brackets.color = c;
                var gs = OzKit.GetSprite(big ? "Bits/sel_brackets_lg_glow" : "Bits/sel_brackets_sm_glow");
                if (m.glow.sprite != gs) m.glow.sprite = gs;
                var g = bracket; g.a = brackets == 1 ? GlowAlpha : GlowAlpha * 0.55f;                          // H15: soft team-colour halo
                if (m.glow.color != g) m.glow.color = g;
            }
            m.glow.enabled = m.brackets.enabled && m.glow.sprite != null;
            bool showBar = bar && o.MaxHealth > 0 && (brackets > 0 || hovered || alpha < 1f || barOnly) && o.Kind != ZKind.Building;
            m.bar.gameObject.SetActiveFast(showBar);
            if (showBar)
            {
                // Zod (zobject.cpp:611-700): the box is as long as the unit's max health (36 px per 10000), green = health, yellow = lost
                float len = Mathf.Clamp(barPer10k * ho.MaxHealth / (float)ZConst.MAX_UNIT_HEALTH, minBar, maxBar);
                m.bar.sizeDelta = new Vector2(len, OzLayout.HpBarH); m.bar.anchoredPosition = new Vector2(0, OzLayout.HpBarGap);
                float f = Mathf.Clamp01(ho.Health / (float)ho.MaxHealth);
                m.barFill.fillAmount = f; m.barLost.fillAmount = 1f;
                if (hpOf != null) { HoverShownId = ho.Id; HoverShown01 = f; }
                if (OzKit.Available) { var s = OzKit.GetSprite(f < 0.3f ? "Bars/bar_fill_health_low" : "Bars/bar_fill_health"); if (m.barFill.sprite != s) m.barFill.sprite = s; }
                else m.barFill.color = f < 0.3f ? (Color)OzUi.Alert : (Color)OzUi.Go;
                SetAlpha(m.barFrame, alpha); SetAlpha(m.barFill, alpha); SetAlpha(m.barLost, alpha);
                VeteranMarks.BarStar(m.bar, o, alpha);                                              // veterancy_v1 DV12: veteran star
                bool nd = o.Kind == ZKind.Robot && o.Grenades > 0 && m.nade.sprite != null;
                if (m.nade.enabled != nd) m.nade.enabled = nd;
                if (nd) { SetAlpha(m.nade, alpha); NadeShown++; }
            }
            m.chip.gameObject.SetActiveFast(hovered);
            if (hovered)
            {
                bool own = o.Owner == hud.Player;
                m.label.Set(UnitInfo.Name(o.Type));
                float tw = m.label.preferredWidth, w = tw + 16f + (own ? 11f : 0f);
                m.chip.sizeDelta = new Vector2(w, OzLayout.HoverChipH);
                m.chip.anchoredPosition = new Vector2(0, OzLayout.HpBarGap + (showBar ? OzLayout.HpBarH + 4f : 0f) + 4f);
                m.label.At(9, OzLayout.HoverChipH * 0.5f, OzH.L, true, tw + 8);
                var tc = o.Owner == Team.Neutral ? (Color)OzUi.Neutral : OzUi.Mark(o.Owner);
                if (m.chipBar.color != tc) m.chipBar.color = tc;
                m.star.enabled = own; if (own) m.star.rectTransform.Place(new OzRect(9 + tw + 3, (OzLayout.HoverChipH - 7) * 0.5f, 7, 7));
            }
            bool showGroup = group >= 0 && OzKit.Available;
            m.badge.enabled = showGroup;
            if (showGroup && m.group != group) { m.group = group; m.badge.sprite = OzKit.GetSprite("Badges/badge_" + group + "_sm"); }
        }

        /// <summary></summary>
        public static int NadeShown;
        static void SetAlpha(Image i, float a) { var c = i.color; if (Mathf.Abs(c.a - a) > 0.004f) { c.a = a; i.color = c; } }

        // ------------------------------------------------------------------ order marker ("-ED" cursor echo, guide 9.9)
        void DrawOrderMarker(Camera cam, float sf)
        {
            var inp = hud.input; float age = Time.unscaledTime - inp.MarkerTime;
            // 1.0 scale in 0.12 s) that fades out by OrderMarkLife and is gone
            bool on = age >= 0f && age < OrderMarkLife && order.sprite != null;
            order.gameObject.SetActiveFast(on);
            if (!on) return;
            if (!Mathf.Approximately(orderShownFor, inp.MarkerTime))
            {
                orderShownFor = inp.MarkerTime;
                string g = hud.Cursors ? hud.Cursors.OrderMark : "placed";
                if (g != orderGlyph) { orderGlyph = g; var s = OzKit.GetSprite("Cursors/mark_" + g); if (s) order.sprite = s; }
            }
            var sp = cam.WorldToScreenPoint(inp.MarkerPos);
            if (sp.z < 0) { order.gameObject.SetActiveFast(false); return; }
            order.rectTransform.anchoredPosition = Px(sp, sf);
            const float popT = 0.12f, fadeT = 0.14f;
            float sc = age < popT ? Mathf.Lerp(0.6f, 1.25f, OzEase.OutCubic(age / popT)) : Mathf.Lerp(1.25f, 1f, OzEase.OutCubic(Mathf.Clamp01((age - popT) / (OrderMarkLife - popT))));
            order.rectTransform.localScale = Vector3.one * sc;
            order.color = OzUi.A(OzUi.Ink, 0.9f * (age < fadeT ? 1f : 1f - Mathf.Clamp01((age - fadeT) / (OrderMarkLife - fadeT))));
        }
        public const float OrderMarkLife = 0.36f;

        void DrawRallyChip(Camera cam, float sf, ZObject open)
        {
            bool on = false;
            if (open != null && open.RallyPoints.Count > 0)
            {
                var w = open.RallyPoints[open.RallyPoints.Count - 1];
                var p = ZGame.PxToWorld(w.x, w.y); p.y = game.GroundY(p.x, p.z);
                var sp = cam.WorldToScreenPoint(p);
                if (sp.z > 0) { on = true; rally.anchoredPosition = Px(new Vector2(sp.x + 13f * sf, sp.y), sf); }
            }
            rally.gameObject.SetActiveFast(on);
        }

        // ------------------------------------------------------------------ geometry
        static readonly List<Vector3> members = new List<Vector3>();

        /// <summary>Screen rectangle around the unit's model(s): the whole squad for a robot leader.</summary>
        /// <summary>
        /// The combined renderer bounds of the object's view (buildings: ZGame.StaticViews; units / guns: ZGame.Views), cached per id
        /// in the view's local space, projected corner by corner.</summary>
        readonly Dictionary<int, (Transform t, Bounds local)> meshBounds = new Dictionary<int, (Transform, Bounds)>();
        bool MeshRect(Camera cam, ZObject o, out Rect r)
        {
            if (hud.input) return hud.input.MeshRect(o, out r);
            r = default;
            if (!meshBounds.TryGetValue(o.Id, out var mb) || !mb.t)
            {
                GameObject go = null;
                if (game.Views.TryGetValue(o.Id, out var v) && v) go = v.gameObject; else if (game.StaticViews.TryGetValue(o.Id, out var sgo)) go = sgo;
                if (!go) return false;
                var t = go.transform; bool any = false; var lb = new Bounds();
                foreach (var rr in go.GetComponentsInChildren<Renderer>())
                {
                    if (!rr.enabled || rr is ParticleSystemRenderer || rr is TrailRenderer || rr is LineRenderer) continue;
                    var n = rr.gameObject.name; if (n.Contains("Glow") || n.Contains("Beam") || n.Contains("Flag") || n.Contains("Shadow")) continue;
                    var wb = rr.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = t.InverseTransformPoint(wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                        if (!any) { lb = new Bounds(c, Vector3.zero); any = true; } else lb.Encapsulate(c);
                    }
                }
                if (!any) return false;
                meshBounds[o.Id] = mb = (t, lb);
            }
            Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue); bool vis = false;
            for (int i = 0; i < 8; i++)
            {
                var lc = mb.local.center + Vector3.Scale(mb.local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var sp = cam.WorldToScreenPoint(mb.t.TransformPoint(lc)); if (sp.z < 0) continue;
                vis = true; mn = Vector2.Min(mn, sp); mx = Vector2.Max(mx, sp);
            }
            if (!vis) return false;
            r = Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y);
            return true;
        }

        bool UnitRect(Camera cam, ZObject u, out Rect r)
        {
            if ((u.Kind == ZKind.Cannon || u.Kind == ZKind.Vehicle || u.Kind == ZKind.Building) && MeshRect(cam, u, out r)) return true;   // PA2: tight to the model
            members.Clear();
            members.Add(ViewPos(u));
            foreach (var m in u.Minions) if (!m.Destroyed && m.KillAt < 0) members.Add(ViewPos(m));
            float rad = u.Kind == ZKind.Robot ? 0.55f : u.Kind == ZKind.Cannon ? 1.5f : 1.8f, h = u.Kind == ZKind.Robot ? 1.9f : 2.3f;
            Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue);
            bool any = false;
            foreach (var p in members)
                for (int i = 0; i < 8; i++)
                {
                    var w = p + new Vector3((i & 1) == 0 ? -rad : rad, (i & 4) == 0 ? 0 : h, (i & 2) == 0 ? -rad : rad);
                    var s = cam.WorldToScreenPoint(w); if (s.z < 0) continue;
                    any = true; mn = Vector2.Min(mn, s); mx = Vector2.Max(mx, s);
                }
            r = any ? Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y) : default;
            return any && mx.x > 0 && mn.x < Screen.width && mx.y > 0 && mn.y < Screen.height;
        }

        Vector3 ViewPos(ZObject o) => game.Views.TryGetValue(o.Id, out var v) && v && v.gameObject.activeInHierarchy ? v.transform.position : game.CenterWorld(o);

        /// <summary>The robot, vehicle or cannon whose own box is under the pointer this frame: a squad member itself, not its leader
        /// (null = none). Where the boxes of several members of the hovered squad overlap under the pointer: the one whose box centre is
        /// nearest to it. The hover bar and the unit panel show this one's health.</summary>
        public ZObject HoverMember { get; private set; }
        /// <summary>Play check: the unit whose health the hover bar showed this frame (-1 = no hover bar) and the fill drawn (0..1).</summary>
        public static int HoverShownId = -1; public static float HoverShown01 = -1f;
        static readonly List<ZObject> hoverHits = new List<ZObject>(); static readonly List<float> hoverDist = new List<float>();

        /// <summary>The unit under the pointer (own or not; last in list order like Zod, minion = its leader). Sets HoverMember.</summary>
        ZObject HoverUnit(Camera cam, Vector2 mp)
        {
            ZObject hit = null; float best = float.MaxValue;
            foreach (var o in game.World.Objects)
            {
                if (!o.IsUnit || o.Destroyed || o.KillAt >= 0) continue;
                var p = ViewPos(o);
                var c = cam.WorldToScreenPoint(p + Vector3.up * 0.9f);
                if (c.z < 0 || Mathf.Abs(c.x - mp.x) > 140 || Mathf.Abs(c.y - mp.y) > 140) continue;         // cheap reject before the box
                float rad = o.Kind == ZKind.Robot ? 0.6f : o.Kind == ZKind.Cannon ? 1.5f : 1.8f, h = o.Kind == ZKind.Robot ? 1.9f : 2.3f;
                Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue);
                for (int i = 0; i < 8; i++)
                {
                    var s = cam.WorldToScreenPoint(p + new Vector3((i & 1) == 0 ? -rad : rad, (i & 4) == 0 ? 0 : h, (i & 2) == 0 ? -rad : rad));
                    if (s.z < 0) continue; mn = Vector2.Min(mn, s); mx = Vector2.Max(mx, s);
                }
                if (!Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y).Contains(mp)) continue;
                hit = o.Leader ?? o;                                                                         // the last in list order, as before
                hoverHits.Add(o); hoverDist.Add(((mn + mx) * 0.5f - mp).sqrMagnitude);
            }
            HoverMember = null;
            for (int i = 0; i < hoverHits.Count; i++)                                                        // of that unit's robots under the pointer: the nearest
                if ((hoverHits[i].Leader ?? hoverHits[i]) == hit && hoverDist[i] < best) { HoverMember = hoverHits[i]; best = hoverDist[i]; }
            hoverHits.Clear(); hoverDist.Clear();
            return hit;
        }
    }
}
