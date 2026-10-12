using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// Z's production window (gwproduction.cpp) for an own fort, robot factory or vehicle factory, in the kit's machined
    /// frame (guide 9.6, Production/prod_frame: header band, amber tab, rivets, baked current-unit well, progress track and
    /// queue slots; height 326 + 98 x rows).
    /// Opens like Zod: left-click the building when no own unit is under the pointer (zplayer.cpp:3327-3331), or from the
    /// factory list, a stored-cannon plate or a "gun manufactured" message; any left press on the map outside it closes it
    /// (zplayer.cpp:3313-3324); it lives next to its building in map space (gwproduction.cpp:417-445) with a leader line to
    /// it, clamped inside the view and clear of the HUD clusters. Only one window at a time.
    /// Shows the building's Zod build list for its tech level (ZBuildLists.For), one row per class like Zod's full picker
    /// (gwproduction_fus.cpp), the unit in production with its progress bar and time left straight from the Sim
    /// (ZObject.ProductionProgress / ProductionTimeLeft, i.e. Zod's T = base x (1 - 0.5 flags) x (1 + 1.25 damage),
    /// zbuilding.cpp:633-667, with the three factors spelled out), and the queue with Zod's rules (zbuilding.cpp:115-189,
    /// 477-506): new entries go to the FRONT (built next), the last entry repeats forever, max 5, click an entry to remove it.
    /// Click a unit tile = produce it now (Zod's Cancel, pick, OK in one click: STOP_BUILDING then START_BUILDING, so the new
    /// unit becomes the repeating one); right-click or Shift+click = ADD_BUILDING_QUEUE. STOP = Zod's Cancel while building.
    /// PLACE = stored cannon placement (PlayerInput.BeginPlaceCannon). SET RALLY / CLEAR = rally points (OpenZRallyPoints).
    /// </summary>
    [DefaultExecutionOrder(10)]
    public class OpenZProductionWindow : MonoBehaviour
    {
        [Tooltip("Zod: the window sits next to its building in map space; off = docked at the left of the screen")] public bool anchorToBuilding = true;

        public ZObject Building { get; private set; }
        public bool IsOpen => Building != null;
        public RectTransform WindowRect => win;

        OpenZHud hud; ZGame game; ZWorld W;
        RectTransform win, body, leaderRoot; CanvasGroup cg;
        Image frame, frameShadow, glyph, leader, leaderShade, anchorDot, chipBg, chipEdge, progFill, progSheen, progEdge, progFlash, repeatGlyph;
        RawImage progPaused;
        OzText title, hpText, chipText, curName, curSub, curTime, curFormula, queueHint, buildHint;
        readonly Image[] pips = new Image[7];
        OzText techNum; int techShown = -1;
        UnitIcon curIcon;
        readonly List<Tile> tiles = new List<Tile>();
        readonly List<OzText> classLabels = new List<OzText>();
        readonly List<Chip> chips = new List<Chip>();
        HudButton bStop, bPlace, bRally, bClear, bClose, bRepeat;
        RectTransform queueRoot, actionsRoot;
        int rows = 1;
        float openAt = -10f, closeAt = -10f, progShown, lastProg, completedAt = -10f, sheenAt;
        string lastCurUnit; ZObject closing;

        sealed class Tile { public string unit, tipTime; public HudButton btn; public UnitIcon icon; public OzText name, time, badge; public Image badgeBg; }
        sealed class Chip { public HudButton btn; public UnitIcon icon; public OzText tag; public string tipUnit; public bool tipFirst, tipLast; }

        static float H(int rows) => OzLayout.ProdBaseH + OzLayout.ProdRowH * rows;

        // ------------------------------------------------------------------ build
        public void Build(OpenZHud h)
        {
            hud = h; game = h.game; W = game.World;
            leaderRoot = Hud.Node("ProductionLeader", h.Root); leaderRoot.Fill(); leaderRoot.pivot = Vector2.zero;
            leaderShade = Hud.Img(leaderRoot, "LeaderShade", OzUi.A(Color.black, 0.47f)); leaderShade.rectTransform.anchorMin = leaderShade.rectTransform.anchorMax = Vector2.zero;
            leader = Hud.Img(leaderRoot, "Leader", OzUi.A(OzUi.Ink, 0.59f)); leader.rectTransform.anchorMin = leader.rectTransform.anchorMax = Vector2.zero;
            anchorDot = Hud.Kit(leaderRoot, "AnchorDot", "Bits/anchor_dot", new OzRect(0, 0, 10, 10), null, Image.Type.Simple);
            anchorDot.rectTransform.anchorMin = anchorDot.rectTransform.anchorMax = Vector2.zero; anchorDot.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            if (!OzKit.Available) anchorDot.color = OzUi.Ink;
            leaderRoot.gameObject.SetActive(false);

            win = Hud.Node("ProductionWindow", h.Root);
            win.anchorMin = win.anchorMax = Vector2.zero; win.pivot = new Vector2(0, 1); win.sizeDelta = new Vector2(OzLayout.ProdW, H(1));
            h.AddBlocker(win);
            win.gameObject.AddComponent<Canvas>();                                                     // nested canvas: it moves with its building every frame
            cg = win.gameObject.AddComponent<CanvasGroup>(); cg.blocksRaycasts = false;
            frameShadow = Hud.Img(win, "Shadow", Color.white); frameShadow.rectTransform.Fill(5, -6, -5, 6);
            Hud.Dress(frameShadow, "Production/prod_frame_mask", OzKit.ShadowTint);
            frame = Hud.Img(win, "Frame", Color.white); frame.rectTransform.Fill();
            Hud.Dress(frame, "Production/prod_frame");
            if (!OzKit.Available) { frame.color = HudStyle.Slate; Hud.Img(win, "Accent", OzUi.Amber).rectTransform.TL(13, 0, 36, 3); }
            body = Hud.Node("Body", win); body.Fill();

            // header: glyph, title, tech pips, HP, state chip, close
            glyph = Hud.Icon(body, "Glyph", "factory", OzLayout.ProdGlyphX, OzLayout.ProdGlyphY, OzLayout.ProdGlyphSize, OzUi.Ink2);
            title = Hud.Label(body, "Title", "", OzLayout.ProdTitleSize, OzUi.Ink, 0.04f).At(OzLayout.ProdTitleX, OzLayout.ProdTitleY, OzH.L, false, 250);
            // clear gap, sized like the HP text (11 u tall text -> 10 x 8 u segments, 13 u apart), HP after them
            var techLbl = Hud.Label(body, "Tech", "TECH", 11, OzUi.Muted, 0.2f, OzStyle.Plain).At(OzLayout.ProdTechX, OzLayout.ProdTechY);
            float techW = techLbl.preferredWidth; if (techW < 1f) techW = 4 * 11f * 0.72f;
            techNum = Hud.Label(body, "TechNum", "I", 11, OzUi.Amber, 0.1f, OzStyle.Plain).At(OzLayout.ProdTechX + techW + 6f, OzLayout.ProdTechY, OzH.L, false, 24);
            float pip0 = OzLayout.ProdTechX + techW + 6f + 22f;
            for (int p = 0; p < 7; p++) pips[p] = Hud.Kit(body, "Pip" + p, "Bits/pip", new OzRect(pip0 + p * 13f, OzLayout.ProdTechY + 2f, 10f, 8f), OzUi.GM500);
            hpText = Hud.Label(body, "Hp", "", 11, OzUi.Muted, 0.2f, OzStyle.Plain).At(pip0 + 7 * 13f + 12f, OzLayout.ProdTechY, OzH.L, false, 120);
            var chr = OzLayout.ProdChip;
            var chip = Hud.Chip(body, "State", chr, "", OzUi.GM950, OzUi.AmberHi, 10, 0.18f, OzUi.Amber);
            chipBg = chip.bg; chipEdge = chip.edge; chipText = chip.text;
            var cl = OzLayout.ProdClose;
            bClose = HudButton.Make(body, "Close", null, cl.Size, HudButtonKind.Small);
            bClose.Rt.Place(cl); Hud.Icon(bClose.Content, "X", "close", 6, 5, 14, OzUi.Ink2);
            bClose.OnClick = c => Close(); bClose.Tooltip = "CLOSE|Or click anywhere on the map.|ESC"; bClose.Priority = 10;

            // current unit (baked well + progress track in the frame)
            curIcon = new UnitIcon(body, "CurIcon", OzLayout.ProdCurIcon.Size, false, 0f); curIcon.Rt.Place(OzLayout.ProdCurIcon);
            curName = Hud.Label(body, "CurName", "", OzLayout.ProdCurNameSize, OzUi.Ink, -0.005f).At(OzLayout.ProdCurNameX, OzLayout.ProdCurNameY, OzH.L, false, 230);
            curSub = Hud.Label(body, "CurSub", "", 9, OzUi.Muted, 0.14f, OzStyle.Plain).At(OzLayout.ProdCurNameX, OzLayout.ProdCurSubY, OzH.L, false, 326);
            curTime = Hud.Label(body, "Time", "", OzLayout.ProdTimeSize, OzUi.Amber).At(OzLayout.ProdTimeRX, OzLayout.ProdTimeY, OzH.R, false, 120).Pivot(new Vector2(1f, 0.5f));
            var pr = OzLayout.ProdProgress;
            if (!OzKit.Available) Hud.Img(body, "Track", OzUi.GM950).rectTransform.Place(pr.Move(-1, -1).Resize(pr.w + 2, pr.h + 2));
            var pRoot = Hud.Node("Progress", body); pRoot.Place(pr); pRoot.gameObject.AddComponent<RectMask2D>();
            progFill = OzKit.Available ? OzKit.FillBar(pRoot, "Fill", "amber") : Hud.Img(pRoot, "Fill", OzUi.Amber);
            progFill.rectTransform.Fill(); progFill.type = Image.Type.Filled; progFill.fillMethod = Image.FillMethod.Horizontal; progFill.fillOrigin = 0; progFill.fillAmount = 0;
            progSheen = Hud.Kit(pRoot, "Sheen", "Bars/bar_sheen_sweep", new OzRect(-60, 0, 40, pr.h), OzUi.A(Color.white, 0.8f), Image.Type.Simple);
            progEdge = Hud.Kit(pRoot, "Edge", "Bars/bar_edge", new OzRect(0, 0, 6, pr.h), null, Image.Type.Simple);
            progPaused = Hud.Tiled(pRoot, "Paused", "Textures/tex_hazard_signal_fine", new OzRect(0, 0, pr.w, pr.h), Color.white);
            progFlash = Hud.Img(pRoot, "Flash", OzUi.A(OzUi.AmberHi, 0f)); progFlash.rectTransform.Fill();
            progPaused.gameObject.SetActive(false);
            curFormula = Hud.Label(body, "Formula", "", 9, OzUi.Muted, 0.08f, OzStyle.Plain).At(OzLayout.ProdCurNameX, OzLayout.ProdFormulaY, OzH.L, false, 330);

            // build list header
            Hud.Label(body, "Build", "BUILD", 9, OzUi.Muted, 0.24f, OzStyle.Plain).At(14, OzLayout.ProdBuildY);
            buildHint = Hud.Label(body, "BuildHint", ZWorld.FifoQueueDeviation ? "CLICK ADD · RIGHT-CLICK REMOVE" : "CLICK BUILD · RIGHT-CLICK QUEUE", 8, OzUi.Dim, 0.14f, OzStyle.Plain).At(OzLayout.ProdHintRX, OzLayout.ProdBuildY, OzH.R, false, 300);

            // queue (baked empty slots in the frame's bottom border) + actions
            queueRoot = Hud.Node("Queue", body); queueRoot.Place(new OzRect(0, 0, OzLayout.ProdW, 120));
            Hud.Label(queueRoot, "Label", "QUEUE", 9, OzUi.Muted, 0.24f, OzStyle.Plain).At(14, 0);
            queueHint = Hud.Label(queueRoot, "Hint", "MAX 5 · CLICK TO REMOVE", 8, OzUi.Dim, 0.14f, OzStyle.Plain).At(OzLayout.ProdHintRX, 0, OzH.R, false, 320);
            for (int i = 0; i < ZObject.MAX_QUEUE_ITEMS; i++)
            {
                int idx = i;
                float qs = OzLayout.ProdQueueSize;
                if (!OzKit.Available) Hud.Img(queueRoot, "Slot" + i, OzUi.A(OzUi.GM900, 0.6f)).rectTransform.TL(OzLayout.ProdQueueX0 + OzLayout.ProdQueueStep * i, OzLayout.ProdQueueSlotY, qs, qs);
                var b = HudButton.Make(queueRoot, "Q" + i, null, new Vector2(qs, qs), HudButtonKind.Small);
                b.Rt.Place(new OzRect(OzLayout.ProdQueueX0 + OzLayout.ProdQueueStep * i, OzLayout.ProdQueueSlotY, qs, qs));
                var ic = new UnitIcon(b.Content, "Icon", new Vector2(OzLayout.ProdQueueIconSize, OzLayout.ProdQueueIconSize), false, 0f);
                ic.Rt.Place(new OzRect(OzLayout.ProdQueueIconX, OzLayout.ProdQueueIconY, OzLayout.ProdQueueIconSize, OzLayout.ProdQueueIconSize));
                var tg = Hud.Label(b.Content, "Tag", "", 8, OzUi.Muted, 0.14f, OzStyle.Plain).At(qs * 0.5f, OzLayout.ProdQueueCapMid, OzH.C, true, qs);
                b.Icon = ic; b.OnClick = c => CancelQueue(idx); b.OnRightClick = c => CancelQueue(idx); b.Priority = 10;
                chips.Add(new Chip { btn = b, icon = ic, tag = tg });
            }
            repeatGlyph = Hud.Icon(queueRoot, "Repeat", "repeat", OzLayout.ProdRepeatX, OzLayout.ProdRepeatDY, 24, OzUi.Muted);
            bRepeat = HudButton.Make(queueRoot, "RepeatToggle", "REPEAT", new Vector2(OzLayout.ProdRepeatBtnW, OzLayout.ProdActionH), HudButtonKind.Small, 12, 0.14f, HudButtonStyle.Normal);
            bRepeat.Rt.Place(new OzRect(OzLayout.ProdRepeatBtnX, OzLayout.ProdQueueSlotY + (OzLayout.ProdQueueSize - OzLayout.ProdActionH) * 0.5f, OzLayout.ProdRepeatBtnW, OzLayout.ProdActionH));
            bRepeat.Priority = 10;
            bRepeat.Tooltip = "REPEAT|On: when the queue is empty the last unit keeps being built (Zod auto-production). Off: the factory stops after the queue.|";
            bRepeat.OnClick = c => ToggleRepeat();
            bRepeat.Rt.gameObject.SetActive(ZWorld.FifoQueueDeviation);
            actionsRoot = Hud.Node("Actions", body); actionsRoot.Place(new OzRect(0, 0, OzLayout.ProdW, OzLayout.ProdActionH));
            int ai = 0;
            HudButton Btn(string n, string label, string tip, HudButtonStyle st)
            {
                var b = HudButton.Make(actionsRoot, n, label, new Vector2(OzLayout.ProdActionW, OzLayout.ProdActionH), HudButtonKind.Small, 12, 0.14f, st);
                b.Rt.Place(new OzRect(OzLayout.ProdActionX0 + OzLayout.ProdActionStep * ai++, 0, OzLayout.ProdActionW, OzLayout.ProdActionH));
                b.Tooltip = tip; b.Priority = 10; return b;
            }
            bStop = Btn("Stop", "STOP", "STOP|Stop production and clear the queue (Zod: Cancel).|", HudButtonStyle.Danger);
            bPlace = Btn("Place", "PLACE", "PLACE|Place the stored cannon: then click a free 2x2 spot inside this zone.|" + OzBindings.Cap(OzAction.PlaceCannon), HudButtonStyle.Normal);
            bRally = Btn("Rally", "SET RALLY", "SET RALLY|Click the map or minimap (Shift+clicks = a route, Esc cancels). Zod way: nothing selected, right-click the map.|", HudButtonStyle.Normal);
            bClear = Btn("ClearRally", "CLEAR", "CLEAR RALLY|Remove the rally points: new units stop at the door.|", HudButtonStyle.Normal);
            bStop.OnClick = c => { if (Building != null && W.StopBuilding(hud.Player, Building)) { OzSfx.Ui("ui_production_cancel"); AudioDirector.Announce("ann_manufacturing_cancelled"); hud.Toast("PRODUCTION STOPPED", HudStyle.Muted); } };
            bPlace.OnClick = c => { var b = Building; if (b != null && b.StoredCannons.Count > 0) { hud.input.BeginPlaceCannon(b); hud.Toast($"PLACE THE {UnitInfo.Name(b.StoredCannons[0])}: CLICK A SPOT IN THE ZONE", HudStyle.Amber, 3f); Close(); } };
            // keeps its building, OpenZRallyPoints.armedFor)
            bRally.OnClick = c => { if (hud.Rally.Armed) hud.Rally.Disarm(); else { hud.Rally.Arm(); if (hud.Rally.Armed) Close(true); } };
            bClear.OnClick = c => hud.Rally.Clear(Building);
            AudioDirector.manualProductionAnnounce = true;
            win.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ open / close
        public void Open(ZObject b)
        {
            if (b == null || !b.IsProducer || b.Owner != hud.Player || b.Destroyed) return;             // bfort/brobot/bvehicle MakeGuiWindow: no window for a destroyed building
            if (Building != b && hud.Rally) hud.Rally.Disarm();
            bool was = Building != null;
            Building = b; builtId = -1; nextText = 0f; closing = null;
            win.gameObject.SetActive(true); win.SetAsLastSibling();
            if (!was) openAt = OzMotion.Now;
            progShown = (float)b.ProductionProgress; lastProg = progShown; lastCurUnit = b.CurUnit;
            Refresh(); Position(was ? 1f : 0f);
            OpenZHud.UiClickSound();
        }

        public void Close() => Close(false);
        public void Close(bool keepRally)
        {
            if (Building == null) return;
            closing = Building; closeAt = OzMotion.Now;
            Building = null; pinned = null; pinnedFor = null;
            if (hud.Rally && !keepRally) hud.Rally.Disarm();
            leaderRoot.gameObject.SetActive(false);
        }

        /// <summary>Own, alive fort / factory under the pointer (last in list order, like Zod's hover object).</summary>
        public ZObject ProducerUnder(Vector2 mp)
        {
            if (W == null) return null;
            ZObject hit = null;
            foreach (var o in W.Objects)
                if (o.IsProducer && o.Owner == hud.Player && !o.Destroyed && hud.input.ScreenRect(o).Contains(mp)) hit = o;
            return hit;
        }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            if (hud == null || W == null) return;
            var m = Mouse.current; var kb = Keyboard.current; if (m == null) return;
            var b = Building;
            if (b != null && (b.Owner != hud.Player || W.Get(b.Id) == null)) { Close(); b = null; }            // captured or gone (gwproduction.cpp:106-111)
            if (b != null && kb != null && kb.escapeKey.wasPressedThisFrame && !hud.Rally.Armed && hud.input.Placing == null && !hud.Modal) { Close(); b = null; }
            if (m.leftButton.wasPressedThisFrame && !hud.Modal)
            {
                // Zod (zplayer.cpp:3313-3331): a press on an own fort / factory with no own unit under the pointer opens its
                // window at once and keeps the selection (OpenZHud captured that press away from PlayerInput); any other
                // press on the map closes the window (and PlayerInput deselects / starts a box as usual).
                if (hud.PressCapture == OpenZHud.Capture.Building && hud.PressBuilding != null) { if (hud.PressBuilding != b) Open(hud.PressBuilding); }
                else if (b != null && hud.PressCapture == OpenZHud.Capture.None && hud.input.Placing == null) Close();
            }
            if (Building == null)
            {
                if (closing != null)                                                                    // close: 100 ms fade
                {
                    float k = OzMotion.T(closeAt, OzMotion.Dur(0.1f, true));
                    cg.alpha = 1f - k;
                    if (k >= 1f) { closing = null; win.gameObject.SetActive(false); }
                }
                return;
            }
            Refresh();
            Position(OzEase.OutCubic(OzMotion.T(openAt, OzMotion.Dur(OzMotion.Short))));
        }

        // ------------------------------------------------------------------ content
        static readonly string[] ClassNames = { "ROBOTS", "VEHICLES", "CANNONS" };
        static int ClassOf(string u) => ZBuildLists.IsRobot(u) ? 0 : ZBuildLists.IsCannon(u) ? 2 : 1;

        void RebuildTiles()
        {
            foreach (var t in tiles) t.btn.Kill();
            tiles.Clear();
            foreach (var l in classLabels) if (l != null) Destroy(l.gameObject);
            classLabels.Clear();
            var b = Building; var list = ZBuildLists.For(b.Type, b.BuildLevel);
            // one row per class (Zod's full picker), up to 7 tiles per row; a longer class wraps onto the next row
            var perClass = new List<string>[3] { new List<string>(), new List<string>(), new List<string>() };
            foreach (var u in list) perClass[ClassOf(u)].Add(u);
            int row = 0;
            for (int c = 0; c < 3; c++)
            {
                if (perClass[c].Count == 0) continue;
                classLabels.Add(Hud.Label(body, "Class" + c, ClassNames[c], 8, OzUi.Dim, 0.24f, OzStyle.Plain).At(15, OzLayout.ProdClassY + OzLayout.ProdRowH * row));
                for (int i = 0; i < perClass[c].Count; i++)
                {
                    if (i > 0 && i % (int)OzLayout.ProdTileCols == 0) row++;
                    int col = i % (int)OzLayout.ProdTileCols;
                    tiles.Add(MakeTile(perClass[c][i], col, row, b.Owner));
                }
                row++;
            }
            rows = Mathf.Max(1, row);
            float h = H(rows), yq = OzLayout.ProdQueueY0 + OzLayout.ProdRowH * rows;
            win.sizeDelta = new Vector2(OzLayout.ProdW, h);
            queueRoot.Place(new OzRect(0, yq, OzLayout.ProdW, 120));
            actionsRoot.Place(new OzRect(0, yq + OzLayout.ProdActionDY, OzLayout.ProdW, OzLayout.ProdActionH));
        }

        Tile MakeTile(string u, int col, int row, Team owner)
        {
            var btn = HudButton.Make(body, "Tile_" + u, null, new Vector2(OzLayout.ProdTileW, OzLayout.ProdTileH), HudButtonKind.Small);
            btn.Rt.Place(new OzRect(OzLayout.ProdTileX0 + OzLayout.ProdTileStep * col, OzLayout.ProdTileY + OzLayout.ProdRowH * row, OzLayout.ProdTileW, OzLayout.ProdTileH));
            var c = btn.Content;
            var icon = new UnitIcon(c, "Icon", new Vector2(OzLayout.ProdTileIconSize, OzLayout.ProdTileIconSize), false, 0f);
            icon.Rt.Place(new OzRect(OzLayout.ProdTileIconX, OzLayout.ProdTileIconY, OzLayout.ProdTileIconSize, OzLayout.ProdTileIconSize));
            icon.Set(u, owner);
            string sn = UnitInfo.Short(u);
            var name = Hud.Label(c, "Name", sn, sn.Length < 8 ? 9 : 8, OzUi.Ink, 0.04f, OzStyle.Plain).At(OzLayout.ProdTileW * 0.5f, OzLayout.ProdTileNameMid, OzH.C, true, OzLayout.ProdTileW + 8);
            var time = Hud.Label(c, "Time", "", 10, OzUi.Muted, 0f, OzStyle.Plain).At(OzLayout.ProdTileW * 0.5f, OzLayout.ProdTileTimeMid, OzH.C, true, OzLayout.ProdTileW);
            var bb = Hud.Kit(c, "BadgeBg", "Panels/chip_3", new OzRect(OzLayout.ProdTileW - 17, 2, 15, 14), OzUi.Amber);
            var badge = Hud.Label(bb.transform, "Badge", "", 9, OzUi.GM950, 0f, OzStyle.Plain).FillParent(OzH.C);
            btn.Icon = icon; btn.Priority = 10;
            OzUnitPopup.Note(btn, u, col, row);                                              // the unit card of this portrait (OzUnitPopup)
            if (ZWorld.FifoQueueDeviation)
            {
                btn.OnClick = cl => { var bb = Building; if (bb != null && bb.Building) Queue(u); else Produce(u); };
                btn.OnRightClick = cl => RemoveLast(u);
            }
            else
            {
                btn.OnClick = cl => { if (cl.shift) Queue(u); else Produce(u); };
                btn.OnRightClick = cl => Queue(u);
            }
            return new Tile { unit = u, btn = btn, icon = icon, name = name, time = time, badge = badge, badgeBg = bb };
        }

        void Produce(string unit)
        {
            var b = Building; if (b == null) return;
            // gwproduction.cpp:478-503: OK is hidden in PLACE and only closes the window in PAUSED (destroyed / 70 units)
            var st = State(b, out _);
            if (st == "PLACE") { OzSfx.Ui("ui_error"); hud.Toast("PLACE THE STORED CANNON FIRST", HudStyle.Amber); return; }
            if (st == "PAUSED") { if (!b.Destroyed) AudioDirector.Announce("ann_production_paused"); hud.Toast(b.Destroyed ? "BUILDING DESTROYED: PRODUCTION PAUSED" : "UNIT LIMIT (70): PRODUCTION PAUSED", HudStyle.Danger); return; }
            if (b.Building && b.CurUnit == unit) { OzSfx.Ui("ui_production_pick"); hud.Toast($"{UnitInfo.Name(unit)} IS ALREADY IN PRODUCTION", HudStyle.Muted); return; }
            if (b.Building) W.StopBuilding(hud.Player, b);                                                 // Zod: Cancel ...
            bool started = W.StartBuilding(hud.Player, b, unit);
            if (started) { OzSfx.Ui("ui_production_ok"); AudioDirector.Announce("ann_starting_manufacture"); } else OzSfx.Ui("ui_error");
            if (started)                                                                                   // ... pick, OK
                hud.Toast($"PRODUCING {UnitInfo.Name(unit)} · {UnitInfo.Mmss(b.BFinal - b.BInit)}", HudStyle.Amber);
        }

        void Queue(string unit)
        {
            var b = Building; if (b == null) return;
            if (b.Building && b.Queue.Count >= ZObject.MAX_QUEUE_ITEMS) { hud.Toast("QUEUE FULL (5)", HudStyle.Danger); return; }
            bool idle = !b.Building;
            bool queued = W.AddBuildingQueue(hud.Player, b, unit);
            if (!queued) OzSfx.Ui("ui_error");
            else if (idle) { OzSfx.Ui("ui_production_ok"); AudioDirector.Announce("ann_starting_manufacture"); }
            else { OzSfx.Ui("ui_queue_add"); AudioDirector.Announce("ann_production_queued"); }
            if (queued)
                hud.Toast(idle ? $"PRODUCING {UnitInfo.Name(unit)}" : ZWorld.FifoQueueDeviation ? $"{UnitInfo.Name(unit)} QUEUED · #{b.Queue.Count}" : $"{UnitInfo.Name(unit)} QUEUED · BUILT NEXT", HudStyle.Amber);
        }

        /// <summary>DV21: right-click on a unit tile removes the last queued copy of it.</summary>
        void RemoveLast(string unit)
        {
            var b = Building; if (b == null) return;
            int i = b.Queue.LastIndexOf(unit);
            if (i < 0) { OzSfx.Ui("ui_error"); hud.Toast(b.Building && b.CurUnit == unit ? $"{UnitInfo.Name(unit)} IS IN PRODUCTION · STOP ENDS IT" : $"NO {UnitInfo.Name(unit)} IN THE QUEUE", HudStyle.Muted); return; }
            CancelQueue(i);
        }

        void ToggleRepeat()
        {
            var b = Building; if (b == null) return;
            bool on = !b.RepeatProduction;
            if (!W.SetBuildingRepeat(hud.Player, b, on)) { OzSfx.Ui("ui_error"); return; }
            OzSfx.Ui(on ? "ui_toggle_on" : "ui_toggle_off");
            hud.Toast(on ? "REPEAT ON · THE LAST UNIT KEEPS BEING BUILT" : "REPEAT OFF · STOPS AFTER THE QUEUE", on ? HudStyle.Amber : HudStyle.Muted);
        }

        void CancelQueue(int i)
        {
            var b = Building; if (b == null || i >= b.Queue.Count) return;
            var u = b.Queue[i];
            if (W.CancelBuildingQueue(hud.Player, b, i, u)) { OzSfx.Ui("ui_queue_remove"); hud.Toast($"{UnitInfo.Name(u)} REMOVED FROM THE QUEUE", HudStyle.Muted); }
        }

        /// <summary>Zod's window state in Zod's order (zbuilding.cpp:77-83 GetBuildState, then gwproduction.cpp:383-387 SetState):
        /// PAUSED at the 70-unit cap, else BUILDING / SELECT; stored cannons turn it into PLACE; a destroyed building is
        /// always PAUSED.</summary>
        string State(ZObject b, out Color c)
        {
            string s = W.UnitLimitReached(b.Owner) ? "PAUSED" : b.Building ? "BUILDING" : "SELECT";
            if (b.StoredCannons.Count > 0) s = "PLACE";
            if (b.Destroyed) s = "PAUSED";
            c = s == "PAUSED" ? HudStyle.Danger : s == "PLACE" ? HudStyle.Amber : s == "BUILDING" ? HudStyle.Good : HudStyle.Muted;
            return s;
        }

        /// <summary>State chips (guide 4.3): BUILDING green, SELECT olive + amber, PAUSED hazard ink + signal, PLACE signal
        /// (blinking 300 ms like Zod's state labels).</summary>
        void ChipLook(string st)
        {
            bool blink = !OzMotion.Reduced && OzMotion.Blink(OzMotion.BlinkSlow);
            Color bg, fg, edge;
            switch (st)
            {
                case "BUILDING": bg = OzUi.BuildingChip; fg = OzUi.BuildingText; edge = OzUi.A(OzUi.Go, 0.63f); break;
                case "PAUSED": bg = OzUi.HazardInk; fg = blink ? OzUi.A(OzUi.Signal, 0.35f) : (Color)OzUi.Signal; edge = OzUi.Signal; break;
                case "PLACE": bg = blink ? (Color)OzUi.GM950 : (Color)OzUi.Signal; fg = blink ? (Color)OzUi.Signal : (Color)OzUi.GM950; edge = OzUi.Signal; break;
                default: bg = OzUi.OD600; fg = OzUi.AmberHi; edge = OzUi.A(OzUi.Amber, 0.6f); break;
            }
            if (chipBg.color != bg) chipBg.color = bg;
            if (chipEdge && chipEdge.color != edge) chipEdge.color = edge;
            chipText.color = fg; chipText.Set(st);
        }

        // text refresh: at 10 Hz, or at once when something discrete changes (hover, unit, queue, state, rally); the window
        // stays open for long stretches and used to rebuild ~30 strings (tile tooltips, formula, times) every frame
        int builtId = -1, builtLevel; string builtType;
        float nextText; string sigHover, sigCur, sigState; bool sigBuilding, sigArmed; int sigQueue = -1, sigStored = -1, sigRally = -1, sigPend = -1;

        bool TextDue(ZObject b, string hover, string st, int pend, float now)
        {
            bool armed = hud.Rally.Armed;
            bool changed = hover != sigHover || b.CurUnit != sigCur || st != sigState || b.Building != sigBuilding || armed != sigArmed
                        || b.Queue.Count != sigQueue || b.StoredCannons.Count != sigStored || b.RallyPoints.Count != sigRally || pend != sigPend;
            if (!changed && now < nextText) return false;
            sigHover = hover; sigCur = b.CurUnit; sigState = st; sigBuilding = b.Building; sigArmed = armed;
            sigQueue = b.Queue.Count; sigStored = b.StoredCannons.Count; sigRally = b.RallyPoints.Count; sigPend = pend;
            nextText = now + 0.1f;
            return true;
        }

        void Refresh()
        {
            var b = Building;
            if (b.Id != builtId || b.BuildLevel != builtLevel || b.Type != builtType) { builtId = b.Id; builtLevel = b.BuildLevel; builtType = b.Type; RebuildTiles(); nextText = 0f; }
            float now = OzMotion.Now;

            int tl = ZBuildLists.DisplayTech(b.Type, b.BuildLevel);
            if (tl != techShown) { techShown = tl; techNum.Set(ZBuildLists.Roman(tl)); if (pips[6].gameObject.activeSelf != (tl >= 7)) pips[6].gameObject.SetActive(tl >= 7); }
            for (int p = 0; p < 7; p++) { var c = p < tl ? (Color)OzUi.Amber : (Color)OzUi.GM500; if (pips[p].color != c) pips[p].color = c; }
            float hp = b.MaxHealth > 0 ? b.Health / (float)b.MaxHealth : 0;
            var st = State(b, out _);
            ChipLook(st);

            // current unit (or the hovered tile's estimate while idle)
            string hover = null;
            foreach (var t in tiles) if (t.btn == hud.HotButton) hover = t.unit;
            bool building = b.Building && b.CurUnit != null;
            string shown = building ? b.CurUnit : hover ?? (tiles.Count > 0 ? tiles[0].unit : null);
            float prog = building ? (float)b.ProductionProgress : 0f;
            if (building && b.CurUnit == lastCurUnit && prog < lastProg - 0.5f) { completedAt = now; OzSoundV3.UnitReady(); }   // sound_v3           // a unit left the door: flash + stamp
            lastProg = prog; lastCurUnit = b.CurUnit;
            progShown = prog < progShown - 0.5f ? prog : Mathf.Lerp(progShown, prog, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));   // eases between sim ticks
            progFill.fillAmount = progShown;
            float pw = OzLayout.ProdProgress.w;
            bool edgeOn = progShown > 0.005f && progShown < 0.995f; progEdge.enabled = edgeOn && progEdge.sprite != null;
            if (edgeOn) progEdge.rectTransform.anchoredPosition = new Vector2(pw * progShown - 3f, 0);
            bool sheen = building && st == "BUILDING" && progSheen.sprite != null && !OzMotion.Reduced;
            if (sheen && now - sheenAt > 2.4f) sheenAt = now;
            float sk = (now - sheenAt) / 0.9f;
            progSheen.enabled = sheen && sk < 1f;
            if (progSheen.enabled) progSheen.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-40f, pw * progShown, sk), 0);
            bool paused = st == "PAUSED" && building;
            progPaused.gameObject.SetActiveFast(paused && progPaused.texture);
            float fk = OzMotion.T(completedAt, OzMotion.Dur(0.2f, true));
            var flashC = OzUi.A(OzUi.AmberHi, fk < 1f ? 0.8f * (1f - fk) : 0f); if (progFlash.color != flashC) progFlash.color = flashC;
            float stamp = OzMotion.T(completedAt, OzMotion.Dur(OzMotion.Stamp));                           // time-left ticker resets with a stamp
            curTime.rectTransform.localScale = Vector3.one * (stamp < 1f ? Mathf.Lerp(1.3f, 1f, OzEase.OutBack(stamp)) : 1f);
            curTime.color = paused ? (Color)OzUi.Signal : (Color)OzUi.Amber;

            var pend = hud.Rally.PendingFor(b);
            if (TextDue(b, hover, st, pend != null ? pend.Count : -1, now)) RefreshText(b, st, shown, building, paused, hp, pend);

            SetB(bStop, b.Building && st == "BUILDING");                                                   // Zod: Cancel stops production only in BUILDING
            SetB(bPlace, b.StoredCannons.Count > 0);
            SetB(bClear, b.RallyPoints.Count > 0 || pend != null);
            SetB(bRally, true);
            if (bRally.Selected != hud.Rally.Armed) { bRally.Selected = hud.Rally.Armed; bRally.Refresh(); }
            // PLACE: disabled unless a cannon is stored; then primary, blinking primary / alert every 300 ms (guide 9.6)
            var placeStyle = b.StoredCannons.Count > 0 ? HudButtonStyle.Primary : HudButtonStyle.Normal;
            bool placeBlink = b.StoredCannons.Count > 0 && !OzMotion.Reduced && OzMotion.Blink(OzMotion.BlinkSlow);
            if (bPlace.Style != placeStyle || bPlace.Alert != placeBlink) { bPlace.Style = placeStyle; bPlace.Alert = placeBlink; bPlace.Refresh(); }
            bRally.Label.Set(hud.Rally.Armed ? "CANCEL" : "SET RALLY");
        }

        void RefreshText(ZObject b, string st, string shown, bool building, bool paused, float hp, IReadOnlyList<ZWaypoint> pend)
        {
            Hud.SetIcon(glyph, b.IsFort ? "fort" : "factory", OzLayout.ProdGlyphSize);
            title.Set(UnitInfo.Name(b.Type));
            hpText.Set(OzText.Tab($"HP {Mathf.CeilToInt(hp * 100)}%"));
            hpText.color = hp < 0.3f ? (Color)OzUi.AlertText : (Color)OzUi.Muted;
            var us = shown != null ? W.S.Unit(shown) : null;
            curIcon.Set(shown, b.Owner);
            curName.Set(shown == null ? "" : UnitInfo.Name(shown));
            int group = us != null && ZBuildLists.IsRobot(shown) ? Mathf.Max(1, us.group_amount) : 1;
            double total = us != null ? b.BuildTimeModified(us.build_time) : 0;                             // the Sim's own formula
            if (building)
            {
                // queue summary (Zod queue rules): next entries, the last one repeats
                string next = b.Queue.Count > 0 ? $"NEXT {UnitInfo.Name(b.Queue[0])}" : "";
                string rep = b.Queue.Count > 0 ? $" · THEN {UnitInfo.Name(b.Queue[b.Queue.Count - 1])} REPEATS" : $"{UnitInfo.Name(b.CurUnit)} REPEATS";
                if (ZWorld.FifoQueueDeviation)
                {   // DV21: the queue runs in order; the last unit repeats only with REPEAT on
                    string tail = b.RepeatProduction ? $"{UnitInfo.Name(b.Queue.Count > 0 ? b.Queue[b.Queue.Count - 1] : b.CurUnit)} REPEATS" : "THEN STOP";
                    curSub.Set(paused ? "PAUSED · UNIT LIMIT 70" : (b.Queue.Count > 0 ? $"NEXT {UnitInfo.Name(b.Queue[0])} · {(b.Queue.Count > 1 ? $"+{b.Queue.Count - 1} · " : "")}{tail}" : b.RepeatProduction ? tail : "LAST ONE · THEN STOP") + (group > 1 ? $" · ×{group}" : ""));
                }
                else
                    curSub.Set(paused ? "PAUSED · UNIT LIMIT 70" : (b.Queue.Count > 1 ? next + rep : b.Queue.Count == 1 ? $"NEXT {UnitInfo.Name(b.Queue[0])} · REPEATS" : rep) + (group > 1 ? $" · ×{group}" : ""));
                curTime.Set(OzText.Tab(UnitInfo.Mmss(b.ProductionTimeLeft)));
            }
            else
            {
                curSub.Set(shown == null ? "" : st == "PLACE" ? "CANNON READY · PRESS PLACE" : $"NOT IN PRODUCTION · PICK A UNIT{(group > 1 ? $" · ×{group}" : "")}");
                curTime.Set(shown == null ? "" : OzText.Tab(UnitInfo.Mmss(total)));
            }
            if (us != null)
            {
                int zPct = Mathf.RoundToInt(50f * Mathf.Clamp01((float)b.ZoneOwnage)), dPct = Mathf.RoundToInt(125f * (1f - hp));
                curFormula.Set(OzText.Tab($"{UnitInfo.Mmss(us.build_time)} BASE · −{zPct}% FLAGS · +{dPct}% DAMAGE = {UnitInfo.Mmss(b.BuildTimeModified(us.build_time))}"));
            }
            else curFormula.Set("");

            // tiles: time for each unit from the Sim formula, current unit highlighted, queue counts
            foreach (var t in tiles)
            {
                var s = W.S.Unit(t.unit);
                string tt = s != null ? UnitInfo.Mmss(b.BuildTimeModified(s.build_time)) : "";
                t.time.Set(OzText.Tab(tt));
                bool current = b.Building && b.CurUnit == t.unit;
                if (t.btn.Selected != current) { t.btn.Selected = current; t.btn.Refresh(); }
                t.time.color = current ? (Color)OzUi.Amber : (Color)OzUi.Muted;
                t.name.color = current ? (Color)OzUi.AmberHi : (Color)OzUi.Ink;
                int n = 0; foreach (var q in b.Queue) if (q == t.unit) n++;
                t.badgeBg.gameObject.SetActiveFast(n > 0); t.badge.Set(n > 0 ? n.ToString() : "");
                if (t.tipTime != tt)
                {
                    t.tipTime = tt;
                    t.btn.Tooltip = $"{UnitInfo.Name(t.unit)}{(ZBuildLists.IsRobot(t.unit) && s != null ? " ×" + s.group_amount : "")}|{tt} at this factory. " + (ZWorld.FifoQueueDeviation
                        ? "Click: add to the queue (starts it when idle). Right-click: remove its last queued copy.|" : "Click: produce now (it repeats). Right-click or Shift+click: add to the queue.|");
                }
            }

            // queue slots: index 0 is built next; the last one repeats (zbuilding.cpp:477-506)
            for (int i = 0; i < chips.Count; i++)
            {
                var c = chips[i]; bool on = i < b.Queue.Count;
                c.btn.Rt.gameObject.SetActiveFast(on);
                if (!on) continue;
                var u = b.Queue[i];
                c.icon.Set(u, b.Owner);
                bool last = i == b.Queue.Count - 1 && (!ZWorld.FifoQueueDeviation || b.RepeatProduction);
                c.tag.Set(i == 0 ? "NEXT" : last ? "REPEAT" : "");
                c.tag.color = i == 0 ? (Color)OzUi.Amber : (Color)OzUi.Muted;
                if (c.tipUnit != u || c.tipFirst != (i == 0) || c.tipLast != last)
                {
                    c.tipUnit = u; c.tipFirst = i == 0; c.tipLast = last;
                    c.btn.Tooltip = $"{UnitInfo.Name(u)}|{(i == 0 ? "Built next. " : "")}{(last ? "The last entry repeats forever. " : "")}Click to remove it from the queue.|";
                }
            }
            repeatGlyph.enabled = ZWorld.FifoQueueDeviation ? b.RepeatProduction : b.Queue.Count > 0;
            if (bRepeat != null && ZWorld.FifoQueueDeviation)
            {
                if (bRepeat.Selected != b.RepeatProduction) { bRepeat.Selected = b.RepeatProduction; bRepeat.Refresh(); }
                bRepeat.Label.Set(b.RepeatProduction ? "REPEAT ON" : "REPEAT OFF");
            }
            queueHint.Set(hud.Rally.Armed ? "RALLY TOOL ARMED · CLICK THE MAP · ESC CANCELS"
                        : pend != null ? $"RALLY ROUTE · {pend.Count} POINTS · RELEASE SHIFT TO SEND"
                        : b.RallyPoints.Count > 0 ? $"RALLY · {b.RallyPoints.Count} POINT{(b.RallyPoints.Count > 1 ? "S" : "")} · MAX 5 · CLICK TO REMOVE" : "MAX 5 · CLICK TO REMOVE");
            queueHint.color = hud.Rally.Armed || pend != null ? (Color)OzUi.Amber : (Color)OzUi.Dim;
            bPlace.Label.Set(b.StoredCannons.Count > 1 ? $"PLACE ×{b.StoredCannons.Count}" : "PLACE");
        }

        static void SetB(HudButton b, bool on) { if (b.Interactable != on) { b.Interactable = on; b.Refresh(); } }

        // ------------------------------------------------------------------ placement (map space, clamped to the view and clear of the HUD)
        /// <summary>
        /// drawn at this scale (layout, hit boxes and the placement clamp use the scaled size).</summary>
        public const float ProdScale = 1.1f;                                                   // on top of the HUD scale (1.35 at 1600p)
        float curScale = ProdScale;

        Vector2? pinned; ZObject pinnedFor; Vector2 pinnedCs; bool pinnedRight;
        /// <summary></summary>
        public static int PinCount { get; private set; }
        public Vector2 ProofWindowPos => win ? win.anchoredPosition : Vector2.zero;

        void Position(float openK)
        {
            var cs = hud.CanvasSize;
            // (2560x1600 check: 1.35 HUD x 1.3 window ran the queue row and the buttons off the screen)
            curScale = Mathf.Min(ProdScale, (cs.y - OzLayout.ProdTopLimit - OzLayout.M) / Mathf.Max(1f, win.sizeDelta.y));
            var size = win.sizeDelta * curScale;
            Vector2 topLeft = new Vector2(OzLayout.M, cs.y * 0.5f + size.y * 0.5f);
            Vector2? anchor = null;
            // The window is placed beside the building ONCE (open / another building / a canvas resize) and then holds that
            // screen spot while the camera scrolls; only the leader line follows the building
            bool fresh = pinnedFor != Building || pinnedCs != cs || !pinned.HasValue;
            if (anchorToBuilding)
            {
                var sr = hud.input.ScreenRect(Building);
                if (sr.width > 0 && sr.height > 0 && sr.xMax > 0 && sr.xMin < Screen.width && sr.yMax > 0 && sr.yMin < Screen.height)
                {
                    var a = hud.ScreenToCanvas(new Vector2(sr.xMin, sr.yMax)); var c = hud.ScreenToCanvas(new Vector2(sr.xMax, sr.yMax));
                    anchor = hud.ScreenToCanvas(sr.center);
                    float x = c.x + 40;
                    if (x + size.x > cs.x - OzLayout.M) x = a.x - 40 - size.x;                                // no room on the right: left side
                    if (fresh) topLeft = new Vector2(x, a.y + OzLayout.ProdAnchorDY);
                }
            }
            if (fresh) { pinned = topLeft; pinnedFor = Building; pinnedCs = cs; PinCount++; }
            else topLeft = pinned.Value;
            topLeft.x = Mathf.Clamp(topLeft.x, OzLayout.M, Mathf.Max(OzLayout.M, cs.x - OzLayout.M - size.x));
            // clear of the HUD clusters (guide 9.6 / 8.3): below the top strip + territory tab, and above EVERY bottom cluster
            // whose column it overlaps (minimap 348 u tall, unit panel + alert tab, command bar, open factory list); when it
            // cannot fit above them it steps sideways out of their column (before, only the unit panel / command bar heights
            // were used, so a window near the right edge sat on the minimap)
            float top = cs.y - OzLayout.ProdTopLimit;
            Clusters(cs);
            float minBottom = MinBottom(topLeft.x, topLeft.x + size.x);
            if (minBottom + size.y > top)
            {
                for (int i = 0; i < zoneCount; i++)
                {
                    var z = zones[i];
                    if (topLeft.x + size.x <= z.x + 0.5f || topLeft.x >= z.y - 0.5f || z.z + 12f + size.y <= top) continue;
                    if ((z.x + z.y) * 0.5f > cs.x * 0.5f) topLeft.x = Mathf.Min(topLeft.x, z.x - 12f - size.x);    // right-hand cluster: go left of it
                    else topLeft.x = Mathf.Max(topLeft.x, z.y + 12f);                                              // left-hand cluster: go right of it
                }
                topLeft.x = Mathf.Clamp(topLeft.x, OzLayout.M, Mathf.Max(OzLayout.M, cs.x - OzLayout.M - size.x));
                minBottom = MinBottom(topLeft.x, topLeft.x + size.x);
            }
            topLeft.y = Mathf.Clamp(topLeft.y, Mathf.Min(top, minBottom + size.y), top);
            win.anchoredPosition = topLeft;
            // open: scale 0.96 -> 1 from the anchor side + alpha, 160 ms
            bool rightSide = anchor.HasValue && anchor.Value.x > topLeft.x + size.x * 0.5f;
            if (fresh) pinnedRight = rightSide; else rightSide = pinnedRight;                                // (5): the scale-in side is fixed too
            win.pivot = new Vector2(rightSide ? 1 : 0, 1);
            win.anchoredPosition = topLeft + new Vector2(rightSide ? size.x : 0, 0);
            win.localScale = Vector3.one * curScale * Mathf.Lerp(0.96f, 1f, openK);
            cg.alpha = openK;
            // leader line from the nearest window edge to a dot on the building (1.5 u INK 59 % over a 3 u shade)
            bool lead = anchor.HasValue;
            if (lead)
            {
                var A = anchor.Value;
                var P = new Vector2(Mathf.Clamp(A.x, topLeft.x, topLeft.x + size.x), Mathf.Clamp(A.y, topLeft.y - size.y, topLeft.y));
                lead = (A - P).sqrMagnitude > 64f;
                if (lead)
                {
                    Line(leaderShade, P, A, OzPx.Line(3f)); Line(leader, P, A, OzPx.Line(1.5f));
                    anchorDot.rectTransform.anchoredPosition = A;
                    var lc = leader.color; lc.a = 0.59f * openK; leader.color = lc;
                }
            }
            leaderRoot.gameObject.SetActiveFast(lead && hud.hudVisible);
        }

        // bottom HUD clusters in canvas units from the bottom-left: (left, right, top edge); BR / BL rects keep their distance
        // to the bottom (OzRect: canvas-level y from the 1080 reference), so the tops hold at every UI scale
        readonly Vector3[] zones = new Vector3[4]; int zoneCount;
        void Clusters(Vector2 cs)
        {
            zoneCount = 0;
            var mm = OzLayout.Minimap; float mx = mm.XOn(cs.x);
            zones[zoneCount++] = new Vector3(mx, mx + mm.w, OzLayout.RefH - mm.y);
            var up = OzLayout.UnitPanelFor(OpenZHud.CanvasWidth);   /* menu_exe_v1 */ float ux = up.XOn(cs.x);
            zones[zoneCount++] = new Vector3(ux, ux + up.w, OzLayout.RefH - up.y + OzLayout.AlertTab.h);        // + the A alert tab above it
            var cb = OzLayout.CommandBar;
            zones[zoneCount++] = new Vector3(cb.x, cb.x + cb.w, OzLayout.RefH - cb.y);
            float ft = hud.FactoryListTop;
            if (ft > 0f) zones[zoneCount++] = new Vector3(OzLayout.FactoryX, OzLayout.FactoryX + OzLayout.FactoryW, ft);
        }

        float MinBottom(float x0, float x1)
        {
            float mb = OzLayout.M;
            for (int i = 0; i < zoneCount; i++) { var z = zones[i]; if (x1 > z.x + 0.5f && x0 < z.y - 0.5f) mb = Mathf.Max(mb, z.z + 12f); }
            return mb;
        }

        static void Line(Image im, Vector2 a, Vector2 b, float w)
        {
            var d = b - a; var rt = im.rectTransform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = (a + b) * 0.5f;
            rt.sizeDelta = new Vector2(d.magnitude, w);
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }
    }
}
