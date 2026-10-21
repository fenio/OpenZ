using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// k252: ready guns by the building. Every finished gun an own fort or factory holds (ZObject.StoredCannons, Zod's stored cannons)
    /// floats as a small tile (dark HUD face, thin amber edge, the gun's icon, READY) right of that building's round production badge
    /// (OpenZUnitOverlay.BadgeRects), in the order they were finished, laid out by OzBuildMenuLayout.ReadyTiles: side by side with
    /// small gaps, wrapping, never over a badge, another building's tiles or the screen edge, following the camera with the badge.
    /// Click = the build menu's PLACE for that gun. Drag onto the map = the same placement (PlayerInput.BeginPlaceCannon with the
    /// tile's gun: its ghost, the green / red footprint, the gun zone and the range ring), a dotted amber line from the tile to the
    /// pointer, and the gun is placed where the button is let go, exactly as PLACE places it (same rules, same sounds). Let go over
    /// the HUD, on a refused spot, Esc or a right click = cancelled, the tile stays. Input: OzReadyGunsInput (before PlayerInput);
    /// this component lays the tiles out after the badges are drawn. View / input only: it reads the Sim and calls the placement
    /// PLACE uses, it never writes the Sim. Hidden with the HUD (briefing, F10, pause, the end of the match) and where a HUD panel
    /// or an open build menu covers a tile.
    /// </summary>
    [DefaultExecutionOrder(75)]                                                     // after OpenZUnitOverlay (70) drew this frame's badges
    public sealed class OzReadyGuns : MonoBehaviour
    {
        public static OzReadyGuns I { get; private set; }
        /// <summary>Play check counters: tiles drawn now / the most at once, frames with a tile left overlapping, and the drags.</summary>
        public static int Shown, MostShown, OverlapFrames, Drags, DragPlaced, DragCancelled, Clicks;

        OpenZHud hud; RectTransform root, dotsRoot;
        sealed class Tile { public HudButton btn; public UnitIcon icon; public ZObject b; public int index; public string type; public Rect px; }
        readonly List<Tile> tiles = new List<Tile>(); int used;
        readonly List<Image> dots = new List<Image>(); int dotsUsed;
        static readonly List<Rect> blocked = new List<Rect>(), rects = new List<Rect>();

        // the drag (OzReadyGunsInput)
        Tile press; Vector2 pressAt; ZObject dragB; int dragIndex = -1, releasedFrame = -1;
        bool Dragging => dragB != null;

        // ------------------------------------------------------------------ install: every scene with a ZGame (the HUD may come later)
        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInstall()
        {
            if (!hooked) { hooked = true; UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => TryInstall(); }
            TryInstall();
        }
        static void TryInstall()
        {
            if (FindFirstObjectByType<OzReadyGuns>() || !FindFirstObjectByType<ZGame>()) return;
            var go = new GameObject("OpenZ_ReadyGuns");
            go.AddComponent<OzReadyGuns>(); go.AddComponent<OzReadyGunsInput>();
        }

        void Awake() { I = this; Shown = MostShown = OverlapFrames = Drags = DragPlaced = DragCancelled = Clicks = 0; }
        void OnDestroy() { if (I == this) I = null; }

        bool Bind()
        {
            if (!hud) { hud = OpenZHud.I; if (!hud) return false; }
            if (!hud.Root || hud.W == null || !hud.input || !hud.Canvas) return false;
            if (!root)
            {
                root = Hud.Node("ReadyGuns", hud.Root); root.Fill(); root.pivot = Vector2.zero;
                root.SetSiblingIndex(0);                                                                   // under every HUD panel
                dotsRoot = Hud.Node("DragLine", root); dotsRoot.Fill(); dotsRoot.pivot = Vector2.zero;
            }
            return true;
        }

        // ------------------------------------------------------------------ tiles
        Tile NewTile()
        {
            float w = OzBuildMenuLayout.ReadyW, h = OzBuildMenuLayout.ReadyH, ic = OzBuildMenuLayout.ReadyIcon;
            var t = new Tile();
            t.btn = HudButton.Make(root, "ReadyGun" + tiles.Count, null, new Vector2(w, h), HudButtonKind.Small, 11, 0.08f, HudButtonStyle.Normal, true);
            var rt = t.btn.Rt; rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = new Vector2(0f, 1f);
            var es = OzKit.GetSprite("Buttons/btn_s_edge");
            if (es != null)
            {   // the thin amber edge, over the face, under the content
                var e = Hud.Img(rt, "Edge", OzUi.A(OzUi.Amber, 0.95f)); e.sprite = es; e.type = Image.Type.Sliced; e.rectTransform.Fill();
                e.transform.SetSiblingIndex(t.btn.Bg.transform.GetSiblingIndex() + 1);
            }
            else Hud.Border(rt, OzUi.Amber);
            t.icon = new UnitIcon(t.btn.Content, "Icon", new Vector2(ic, ic), false, 0f);
            t.icon.Rt.Place(new OzRect((w - ic) * 0.5f, OzBuildMenuLayout.ReadyIconY, ic, ic));
            Hud.Label(t.btn.Content, "Word", "READY", 11, OzUi.Amber, 0.08f).At(w * 0.5f, OzBuildMenuLayout.ReadyWordMid, OzH.C, true, w - 2f);
            t.btn.Icon = t.icon; t.btn.Priority = 5;                                                       // under the build menu's buttons (10)
            var tile = t; t.btn.OnClick = c => Click(tile);
            hud.AddBlocker(rt);
            rt.gameObject.SetActive(false);
            tiles.Add(t);
            return t;
        }

        void Click(Tile t)
        {
            var b = t.b;
            if (b == null || b.Destroyed || b.Owner != hud.Player || t.index >= b.StoredCannons.Count) return;
            hud.input.BeginPlaceCannon(b, t.index);
            if (hud.input.Placing != b) return;
            Clicks++;
            hud.Toast($"PLACE THE {UnitInfo.Name(b.StoredCannons[t.index])}: CLICK A SPOT IN THE ZONE", HudStyle.Amber, 3f);   // the menu's PLACE words
            if (hud.Production && hud.Production.IsOpen) hud.Production.Close();
        }

        // ------------------------------------------------------------------ input (called by OzReadyGunsInput, after the HUD took the press, before PlayerInput)
        internal void TickInput()
        {
            if (!Bind()) return;
            var m = Mouse.current; if (m == null) return;
            var input = hud.input; Vector2 p = m.position.ReadValue();
            if (m.leftButton.wasPressedThisFrame)
            {
                press = null; pressAt = p;
                if (hud.PressCapture == OpenZHud.Capture.Ui && hud.HotButton != null)
                    for (int i = 0; i < used; i++) if (tiles[i].btn == hud.HotButton && tiles[i].b != null) press = tiles[i];
            }
            float slop = OpenZHud.BuildingClickSlopPx;
            if (press != null && !Dragging && m.leftButton.isPressed && !hud.Modal && (p - pressAt).sqrMagnitude > slop * slop)
            {   // the press became a drag: the placement PLACE starts, for this tile's gun, and the press goes to the map
                var b = press.b;
                if (b != null && !b.Destroyed && b.Owner == hud.Player && press.index < b.StoredCannons.Count)
                {
                    input.BeginPlaceCannon(b, press.index);
                    if (input.Placing == b)
                    {
                        hud.HandPressToMap(press.btn);
                        dragB = b; dragIndex = press.index; releasedFrame = -1; Drags++;
                        if (hud.Production && hud.Production.IsOpen) hud.Production.Close();
                    }
                }
                press = null;
            }
            if (!m.leftButton.isPressed && !m.leftButton.wasReleasedThisFrame) press = null;
            if (!Dragging) return;
            if (releasedFrame >= 0)
            {   // the frame after the button came up: PlayerInput placed the gun (placement over) or refused the spot (its sound and
                // notice played; still armed): then the drag is cancelled and the tile stays
                if (Time.frameCount <= releasedFrame) return;
                if (input.Placing == dragB) { input.CancelPlace(); DragCancelled++; } else DragPlaced++;
                EndDrag();
            }
            else if (input.Placing != dragB) { DragCancelled++; EndDrag(); }                                // Esc / right click (PlayerInput)
            else if (m.leftButton.wasReleasedThisFrame)
            {
                if (hud.PointerOverUi || hud.Modal) { input.CancelPlace(); AudioDirector.UI("ui_cancel"); DragCancelled++; EndDrag(); }   // let go over the HUD
                else releasedFrame = Time.frameCount;                                                    // PlayerInput places it now (its own release rule)
            }
            else if (m.rightButton.wasPressedThisFrame && hud.PointerOverUi)
            {   // over the HUD PlayerInput does not get the right click: cancel here, as PlayerInput does on the map
                input.CancelPlace(); AudioDirector.UI("ui_cancel"); DragCancelled++; EndDrag();
            }
        }

        void EndDrag() { dragB = null; dragIndex = -1; releasedFrame = -1; }

        // ------------------------------------------------------------------ layout (after the badges)
        void LateUpdate()
        {
            used = 0; dotsUsed = 0;
            bool on = Bind() && hud.isActiveAndEnabled && hud.hudVisible && !hud.Modal && !hud.HasExited && !hud.W.GameOver && hud.Canvas.enabled;
            int shown = 0; bool overlap = false;
            if (on)
            {
                float sw = Screen.width, sh = Screen.height, sf = Mathf.Max(0.01f, hud.Canvas.scaleFactor);
                blocked.Clear();
                foreach (var r in OpenZUnitOverlay.BadgeRects.Values) blocked.Add(r);
                foreach (var b in hud.W.Objects)
                {
                    if (b.Kind != ZKind.Building || b.Owner != hud.Player || b.Destroyed || b.StoredCannons.Count == 0) continue;
                    if (!OpenZUnitOverlay.BadgeRects.TryGetValue(b.Id, out var br) || br.width <= 0f || br.height <= 0f) continue;
                    float k = OzBuildMenuLayout.BadgeUnit(br, b.Building && b.CurUnit != null);
                    if (OzBuildMenuLayout.ReadyTiles(br, k, b.StoredCannons.Count, sw, sh, blocked, rects) > 0) overlap = true;
                    for (int i = 0; i < rects.Count; i++)
                    {
                        var r = rects[i]; blocked.Add(r);
                        if (Covered(r)) continue;                                                          // under a HUD panel or an open build menu
                        var t = used < tiles.Count ? tiles[used] : NewTile(); used++;
                        Show(t, b, i, r, k, sf); shown++;
                    }
                }
            }
            for (int i = used; i < tiles.Count; i++) { tiles[i].b = null; tiles[i].btn.Rt.gameObject.SetActiveFast(false); }
            Shown = shown; if (shown > MostShown) MostShown = shown; if (overlap) OverlapFrames++;
            if (on && Dragging) DragLine();
            for (int i = dotsUsed; i < dots.Count; i++) dots[i].gameObject.SetActiveFast(false);
        }

        bool Covered(Rect r)
        {
            var mine = root;
            return hud.OverOtherUi(r.center, mine) || hud.OverOtherUi(new Vector2(r.xMin + 1f, r.yMin + 1f), mine) || hud.OverOtherUi(new Vector2(r.xMax - 1f, r.yMin + 1f), mine)
                || hud.OverOtherUi(new Vector2(r.xMin + 1f, r.yMax - 1f), mine) || hud.OverOtherUi(new Vector2(r.xMax - 1f, r.yMax - 1f), mine);
        }

        void Show(Tile t, ZObject b, int index, Rect r, float k, float sf)
        {
            t.b = b; t.index = index; t.px = r;
            string type = b.StoredCannons[index];
            if (t.type != type)
            {
                t.type = type; t.icon.Set(type, b.Owner);
                t.btn.Tooltip = $"{UnitInfo.Name(type)} READY|Drag it onto the map: green = it may stand there; let go to place it. Or click it: PLACE. Esc or right click cancels.|" + OzBindings.Cap(OzAction.PlaceCannon);
            }
            var rt = t.btn.Rt;
            rt.gameObject.SetActiveFast(true);
            var pos = new Vector2(r.xMin / sf, r.yMax / sf);
            if (rt.anchoredPosition != pos) rt.anchoredPosition = pos;
            var sc = Vector3.one * (k / sf);
            if (rt.localScale != sc) rt.localScale = sc;
            bool lifted = Dragging && b == dragB && index == dragIndex;                                   // the tile being dragged shows the amber selected face
            if (t.btn.Selected != lifted) { t.btn.Selected = lifted; t.btn.Refresh(); }
        }

        /// <summary>The dotted amber line from the dragged tile to the pointer (canvas units, under the HUD panels).</summary>
        void DragLine()
        {
            Tile from = null;
            for (int i = 0; i < used; i++) if (tiles[i].b == dragB && tiles[i].index == dragIndex) from = tiles[i];
            if (from == null || Mouse.current == null) return;                                            // its tile is covered: no line
            Vector2 a = hud.ScreenToCanvas(from.px.center), z = hud.ScreenToCanvas(Mouse.current.position.ReadValue());
            float d = (z - a).magnitude, step = 10f;
            int n = Mathf.Min(160, Mathf.FloorToInt(d / step));
            var dir = d > 0.01f ? (z - a) / d : Vector2.zero;
            var dotSprite = OzKit.GetSprite("Bits/dot");
            for (int i = 1; i <= n; i++)
            {
                if (dotsUsed >= dots.Count)
                {
                    var im = Hud.Img(dotsRoot, "Dot", OzUi.A(OzUi.Amber, 0.9f));
                    if (dotSprite != null) im.sprite = dotSprite;
                    var drt = im.rectTransform; drt.anchorMin = drt.anchorMax = Vector2.zero; drt.pivot = new Vector2(0.5f, 0.5f); drt.sizeDelta = new Vector2(4.5f, 4.5f);
                    dots.Add(im);
                }
                var dot = dots[dotsUsed++];
                dot.gameObject.SetActiveFast(true);
                dot.rectTransform.anchoredPosition = a + dir * (i * step);
            }
        }
    }
}
