using System;
using System.Collections.Generic;
using System.Reflection;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// HUD colours, mapped onto the OpenZ UI kit tokens (UI_STYLE_GUIDE.md 2.1 / 12: "Z, modernised as a military command
    /// console"). The member names stay as the staged HUD had them so nothing else changes.
    /// </summary>
    public static class HudStyle
    {
        public static readonly Color Slate = OzUi.A(OzUi.GM800, 0.94f);
        public static readonly Color SlateDeep = OzUi.GM950;
        public static readonly Color SlateHi = OzUi.GM600;
        public static readonly Color Line = OzUi.A(OzUi.Khaki, 0.24f);
        public static readonly Color Shadow = new Color32(2, 5, 3, 84);
        public static readonly Color Ink = OzUi.Ink;
        public static readonly Color Muted = OzUi.Muted;
        public static readonly Color Amber = OzUi.Amber;
        public static readonly Color Danger = OzUi.Alert;
        public static readonly Color Good = OzUi.Go;
        public static readonly Color HpGreen = OzUi.Go;
        public static readonly Color HpLost = OzUi.HpLost;

        /// <summary>Team colour for UI marks (RED_HI / BLUE_HI / NEUTRAL).</summary>
        public static Color TeamUi(Team t) => OzUi.Mark(t);
        public static Color WithA(Color c, float a) { c.a = a; return c; }
    }

    /// <summary>Inter Tight ExtraBold (SIL OFL 1.1) from the kit, the only UI font; Unity's built-in runtime font before the kit
    /// is imported. No OS fonts (they break the open-source build and the look).</summary>
    public static class HudFonts
    {
        public static Font Display => OzKit.Font ? OzKit.Font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        public static Font Body => Display;
    }

    /// <summary>Builders for code-made uGUI dressed with the kit.</summary>
    public static class Hud
    {
        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Anchor(this RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size; return rt;
        }

        public static RectTransform Fill(this RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t); return rt;
        }

        /// <summary>Place by an OzRect: canvas-level anchors (TL..BR, C) or a child rect relative to the parent's top-left (TL).</summary>
        public static RectTransform Place(this RectTransform rt, OzRect r)
        {
            Vector2 an, pos; float W = OzLayout.RefW, H = OzLayout.RefH;
            switch (r.a)
            {
                case OzAnchor.TC: an = new Vector2(0.5f, 1); pos = new Vector2(r.x - W * 0.5f, -r.y); break;
                case OzAnchor.TR: an = new Vector2(1, 1); pos = new Vector2(r.x - W, -r.y); break;
                case OzAnchor.BL: an = new Vector2(0, 0); pos = new Vector2(r.x, H - r.y); break;
                case OzAnchor.BC: an = new Vector2(0.5f, 0); pos = new Vector2(r.x - W * 0.5f, H - r.y); break;
                case OzAnchor.BR: an = new Vector2(1, 0); pos = new Vector2(r.x - W, H - r.y); break;
                case OzAnchor.C: an = new Vector2(0.5f, 0.5f); pos = new Vector2(r.x - W * 0.5f, H * 0.5f - r.y); break;
                default: an = new Vector2(0, 1); pos = new Vector2(r.x, -r.y); break;
            }
            rt.anchorMin = rt.anchorMax = an; rt.pivot = new Vector2(0, 1); rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(r.w, r.h);
            return rt;
        }

        /// <summary>Child rect at (x, y) from the parent's top-left, size w x h.</summary>
        public static RectTransform TL(this RectTransform rt, float x, float y, float w, float h) => rt.Place(new OzRect(x, y, w, h));

        public static Image Img(Transform parent, string name, Color c)
        {
            var rt = Node(name, parent); var i = rt.gameObject.AddComponent<Image>(); i.color = c; i.raycastTarget = false; return i;
        }

        public static Image[] Border(Transform parent, Color c, float w = 1f)
        {
            var r = new Image[4];
            r[0] = Img(parent, "BorderT", c); r[0].rectTransform.anchorMin = new Vector2(0, 1); r[0].rectTransform.anchorMax = Vector2.one; r[0].rectTransform.pivot = new Vector2(0.5f, 1); r[0].rectTransform.sizeDelta = new Vector2(0, w); r[0].rectTransform.anchoredPosition = Vector2.zero;
            r[1] = Img(parent, "BorderB", c); r[1].rectTransform.anchorMin = Vector2.zero; r[1].rectTransform.anchorMax = new Vector2(1, 0); r[1].rectTransform.pivot = new Vector2(0.5f, 0); r[1].rectTransform.sizeDelta = new Vector2(0, w); r[1].rectTransform.anchoredPosition = Vector2.zero;
            r[2] = Img(parent, "BorderL", c); r[2].rectTransform.anchorMin = Vector2.zero; r[2].rectTransform.anchorMax = new Vector2(0, 1); r[2].rectTransform.pivot = new Vector2(0, 0.5f); r[2].rectTransform.sizeDelta = new Vector2(w, 0); r[2].rectTransform.anchoredPosition = Vector2.zero;
            r[3] = Img(parent, "BorderR", c); r[3].rectTransform.anchorMin = new Vector2(1, 0); r[3].rectTransform.anchorMax = Vector2.one; r[3].rectTransform.pivot = new Vector2(1, 0.5f); r[3].rectTransform.sizeDelta = new Vector2(w, 0); r[3].rectTransform.anchoredPosition = Vector2.zero;
            return r;
        }

        // ------------------------------------------------------------------ kit sprites
        static readonly Color Fallback = OzUi.A(OzUi.GM800, 0.94f);

        /// <summary>Kit sprite as a child Image at a parent-local rect (sliced when the sprite has borders). Before the kit is
        /// imported the Image shows a flat gunmetal fill instead, so the HUD still works.</summary>
        public static Image Kit(Transform parent, string name, string rel, OzRect r, Color? tint = null, Image.Type type = Image.Type.Sliced, bool fillCenter = true)
        {
            var img = Img(parent, name, Color.white);
            img.rectTransform.Place(r);
            Dress(img, rel, tint, type, fillCenter);
            return img;
        }

        public static void Dress(Image img, string rel, Color? tint = null, Image.Type type = Image.Type.Sliced, bool fillCenter = true)
        {
            var s = OzKit.GetSprite(rel);
            if (s != null) { OzKit.Apply(img, rel, type, tint); img.fillCenter = fillCenter; }
            else { img.sprite = null; img.type = Image.Type.Simple; img.color = tint.HasValue ? (rel.StartsWith("Icons/") || rel.StartsWith("Bits/") ? tint.Value : Fallback) : Fallback; }
        }

        /// <summary>Kit sprite at native size with its top-left at (x, y) in the parent.</summary>
        public static Image KitNative(Transform parent, string name, string rel, float x, float y, Color? tint = null)
        {
            var s = OzKit.GetSprite(rel);
            var sz = s != null ? new Vector2(s.rect.width, s.rect.height) * 0.5f : new Vector2(16, 16);
            return Kit(parent, name, rel, new OzRect(x, y, sz.x, sz.y), tint, Image.Type.Simple);
        }

        /// <summary>Hard offset shadow (guide 4.1: +5, +6 u, #020503 33 %, no blur) from a *_mask sprite, created as the FIRST
        /// child of `panel` so everything else draws over it.</summary>
        public static Image Shadow(RectTransform panel, string maskRel, float dx = 5, float dy = 6, float alpha = 0.33f)
        {
            var sh = Img(panel, "OzShadow", Color.clear);
            sh.rectTransform.Fill(dx, -dy, -dx, dy);
            if (OzKit.GetSprite(maskRel) != null) OzKit.Apply(sh, maskRel, Image.Type.Sliced, new Color32(2, 5, 3, (byte)Mathf.RoundToInt(alpha * 255)));
            else sh.color = new Color32(2, 5, 3, 84);
            sh.transform.SetSiblingIndex(0);
            return sh;
        }

        /// <summary>Kit plate on an existing, sized panel root (OzKit.Plate: shadow, sliced body, header band, tab, owner stripe,
        /// rivets); flat fallback without the kit. size = "lg" / "md" / "sm".</summary>
        public static Image Plate(RectTransform panel, string size, float header = 0f, Color? tab = null, Color? owner = null, bool rivets = false, bool shadow = true)
        {
            if (OzKit.Available) return OzKit.Plate(panel, size, header, tab, owner, rivets, shadow);
            if (shadow) { var sh = Img(panel, "Shadow", HudStyle.Shadow); sh.rectTransform.Fill(5, -6, -5, 6); sh.transform.SetSiblingIndex(0); }
            var body = Img(panel, "Body", HudStyle.Slate); body.rectTransform.Fill(); body.transform.SetSiblingIndex(shadow ? 1 : 0);
            if (tab.HasValue) Img(panel, "Accent", tab.Value).rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(OzKit.Cut(size) + 1, 0), new Vector2(36, 3));
            return body;
        }

        /// <summary>Compatibility builder of the staged HUD: a positioned panel with the kit plate (lg when either side is
        /// >= 300 u, md from 50 u tall, else sm).</summary>
        public static RectTransform Panel(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, bool accent = true)
        {
            var root = Node(name, parent).Anchor(anchor, pivot, pos, size);
            Plate(root, size.x >= 300 || size.y >= 300 ? "lg" : size.y >= 50 ? "md" : "sm", 0f, accent ? (Color?)OzUi.Amber : null);
            return root;
        }

        /// <summary>Glyph from the kit icon set (Icons/ic_&lt;name&gt;_48 or _96 by size), white art tinted.</summary>
        public static Image Icon(Transform parent, string name, string glyph, float x, float y, float size, Color tint)
        {
            var img = Kit(parent, name, IconRel(glyph, size), new OzRect(x, y, size, size), tint, Image.Type.Simple);
            img.preserveAspect = true;
            return img;
        }

        static readonly Dictionary<string, string> iconRel48 = new Dictionary<string, string>(), iconRel96 = new Dictionary<string, string>(),
                                                   iconRel192 = new Dictionary<string, string>();
        /// <summary>
        /// The tier follows the ON-SCREEN size (size u x OzPx.K): 48 px up to 24 px on screen, 96 up to
        /// 48, else 192 (Kit/Icons/ic_*_192, the kit's own glyphs drawn bigger; 96 when the 192 set is not imported).</summary>
        public static string IconRel(string glyph, float size)
        {
            float px = size * OzPx.K;
            int tier = px <= 24.5f ? 48 : px <= 48.5f ? 96 : 192;
            var d = tier == 48 ? iconRel48 : tier == 96 ? iconRel96 : iconRel192;
            if (glyph == null) glyph = "";
            if (!d.TryGetValue(glyph, out var rel))
            {
                rel = "Icons/ic_" + glyph + "_" + tier;
                if (tier == 192 && OzKit.GetSprite(rel) == null) rel = "Icons/ic_" + glyph + "_96";
                d[glyph] = rel;
            }
            return rel;
        }

        public static void SetIcon(Image img, string glyph, float size)
        {
            var rel = IconRel(glyph, size); var s = OzKit.GetSprite(rel);
            if (img.sprite != s && s != null) img.sprite = s;
        }

        /// <summary>Repeat-wrapped kit texture (hazard, hatch, scanlines, noise) as a RawImage tiled at its native period.</summary>
        public static RawImage Tiled(Transform parent, string name, string rel, OzRect r, Color tint)
        {
            var rt = Node(name, parent); rt.Place(r);
            var ri = rt.gameObject.AddComponent<RawImage>(); ri.raycastTarget = false; ri.color = tint;
            var tex = OzKit.GetTexture(rel);
            ri.texture = tex; ri.enabled = tex != null;
            if (tex != null) ri.uvRect = new Rect(0, 0, r.w * 2f / tex.width, r.h * 2f / tex.height);
            return ri;
        }

        /// <summary>Label (OzText: TextMeshPro Inter Tight, or the legacy fallback).</summary>
        public static OzText Label(Transform parent, string name, string text, float size, Color c, float tracking = 0f, OzStyle style = OzStyle.Underlay)
            => OzText.Make(parent, name, text, size, c, tracking, style);

        /// <summary>Chip (guide 4.3): chip_3 / chip_4 tinted + optional 1 u outline + centred label.</summary>
        public static (Image bg, Image edge, OzText text) Chip(Transform parent, string name, OzRect r, string label, Color bg, Color fg, float size = 10, float tracking = 0.16f, Color? outline = null, int cut = 4)
        {
            var root = Node(name, parent); root.Place(r);
            var b = Kit(root, "Bg", $"Panels/chip_{cut}", new OzRect(0, 0, r.w, r.h), bg); b.rectTransform.Fill();
            Image e = null;
            if (outline.HasValue) { e = Kit(root, "Edge", $"Panels/chip_{cut}_outline", new OzRect(0, 0, r.w, r.h), outline.Value); e.rectTransform.Fill(); }
            var t = Label(root, "Text", label, size, fg, tracking, OzStyle.Plain).FillParent(OzH.C);
            return (b, e, t);
        }

        /// <summary>Keycap (guide 4.3): 14 u tall, min 14 wide, GM_950 + khaki keyline, 9 u INK_2 text. Right edge at xRight.</summary>
        public static RectTransform Keycap(Transform parent, string name, string key, float xRight, float y, float h = 14)
        {
            var root = Node(name, parent);
            var bg = Kit(root, "Bg", "Panels/keycap", new OzRect(0, 0, 14, h)); bg.rectTransform.Fill();
            var t = Label(root, "Key", key, 9, OzUi.Ink2, 0.06f, OzStyle.Plain).FillParent(OzH.C);
            float w = Mathf.Max(h, OzControlsTable.TextWidth(t) + 8f);                                   // menu_exe_v1: measured without the 14 u box (controls_unreadable)
            root.TL(xRight - w, y, w, h);
            return root;
        }

        public static void SetActiveFast(this Component c, bool on) { if (c && c.gameObject.activeSelf != on) c.gameObject.SetActive(on); }
        public static void SetActiveFast(this GameObject g, bool on) { if (g && g.activeSelf != on) g.SetActive(on); }
        public static void SetActiveFast(this OzText t, bool on) { if (t != null) t.SetActive(on); }
    }

    /// <summary>
    /// Horizontal bar (guide 4.5, kit Bars/*): sliced track, Filled fill sprite (health, health_low, lost, red, blue, amber,
    /// neutral, signal, white) inset 1 u, Zod's lost-health segment, leading edge, optional segment ticks. Blue strength
    /// fills from the right.
    /// </summary>
    public sealed class HudBar
    {
        public RectTransform Root; public Image Track, Fill, Lost, Edge, Ticks;
        readonly bool fromRight; string kind; float shown = -1f, lostShown = -1f;
        public float Value => Mathf.Max(0f, shown);

        public HudBar(Transform parent, string name, string fill, bool lost = false, bool fromRight = false, string ticks = null, bool edge = true)
        {
            this.fromRight = fromRight; kind = fill;
            Root = Hud.Node(name, parent);
            Track = Hud.Kit(Root, "Track", "Bars/bar_track", new OzRect(0, 0, 24, 12), null); Track.rectTransform.Fill();
            if (!OzKit.Available) Track.color = OzUi.A(OzUi.GM950, 0.92f);
            if (lost) Lost = MakeFill("Lost", "lost", OzUi.HpLost);
            Fill = MakeFill("Fill", fill, FallbackColor(fill));
            if (edge) { Edge = Hud.Kit(Root, "Edge", "Bars/bar_edge", new OzRect(0, 0, 6, 10), null, Image.Type.Simple); Edge.rectTransform.anchorMin = new Vector2(0, 0); Edge.rectTransform.anchorMax = new Vector2(0, 1); Edge.rectTransform.pivot = new Vector2(0.5f, 0.5f); Edge.rectTransform.offsetMin = new Vector2(-3, 1); Edge.rectTransform.offsetMax = new Vector2(3, -1); Edge.enabled = false; }
            if (ticks != null) { Ticks = Hud.Kit(Root, "Ticks", ticks, new OzRect(0, 0, 10, 10), null, Image.Type.Simple); Ticks.rectTransform.Fill(); }
        }

        /// <summary>Staged-HUD compatibility: a plain coloured bar (the fill kind is guessed from the colour).</summary>
        public HudBar(Transform parent, string name, Color fill, Color track, bool lost = false)
            : this(parent, name, fill == HudStyle.Amber ? "amber" : fill == HudStyle.Danger ? "health_low" : "health", lost) { }

        Image MakeFill(string n, string k, Color fb)
        {
            Image img;
            if (OzKit.Available) img = OzKit.FillBar(Root, n, k, fromRight);
            else { img = Hud.Img(Root, n, fb); img.type = Image.Type.Filled; img.fillMethod = Image.FillMethod.Horizontal; img.fillOrigin = fromRight ? 1 : 0; img.fillAmount = 0; }
            img.rectTransform.Fill(1, 1, 1, 1);
            return img;
        }

        static Color FallbackColor(string k) => k == "amber" ? OzUi.Amber : k == "red" ? OzUi.RedHi : k == "blue" ? OzUi.BlueHi : k == "health_low" ? OzUi.Alert
                                               : k == "signal" ? OzUi.Signal : k == "neutral" ? OzUi.Neutral : k == "lost" ? OzUi.HpLost : OzUi.Go;

        /// <summary>Swap the fill sprite (health -> health_low under 30 %).</summary>
        public void SetKind(string k)
        {
            if (k == kind) return; kind = k;
            if (OzKit.Available) { var s = OzKit.GetSprite("Bars/bar_fill_" + k); if (s != null) Fill.sprite = s; }
            else Fill.color = FallbackColor(k);
        }

        /// <summary>value = filled fraction; lostTo = end of Zod's lost segment (0 = none).</summary>
        public void Set(float value, float lostTo = 0f)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Abs(shown - value) > 0.0005f)
            {
                shown = value; Fill.fillAmount = value;
                if (Edge)
                {
                    bool on = value > 0.002f && value < 0.998f; if (Edge.enabled != on) Edge.enabled = on;
                    if (on) { float x = fromRight ? 1f - value : value; Edge.rectTransform.anchorMin = new Vector2(x, 0); Edge.rectTransform.anchorMax = new Vector2(x, 1); }
                }
            }
            if (Lost) { float l = Mathf.Clamp01(Mathf.Max(value, lostTo)); if (Mathf.Abs(lostShown - l) > 0.0005f) { lostShown = l; Lost.fillAmount = l; } }
        }
    }

    public struct HudClick { public bool shift, ctrl, alt, right; }

    public enum HudButtonKind { Small, Large, Key }
    public enum HudButtonStyle { Normal, Primary, Danger }

    /// <summary>
    /// A HUD button polled by <see cref="OpenZHud"/> (no EventSystem needed: the project runs the new Input System only and
    /// has no EventSystem in the level scenes). Left click on release over the same button like Zod (zhud.cpp:338-374);
    /// right click is offered too (production window: right-click an icon = add to queue). Look: the kit's button states
    /// (guide 6): Buttons/btn_s_* (chamfer 5), btn_l_* (chamfer 8) or the command bar's console keycaps key_*; content sinks
    /// 1 u (3 u on keys) while pressed; disabled adds the 5 % hatch.
    /// </summary>
    public sealed class HudButton
    {
        public RectTransform Rt; public Image Bg; public Image[] Edge = new Image[0]; public OzText Label; public UnitIcon Icon;
        public RectTransform Content; public RawImage Hatch; public Image Flash;
        public Action<HudClick> OnClick; public Action<HudClick> OnRightClick;
        public string Tooltip;
        /// <summary>Hit-test priority where panels overlap (the production window uses 10, drawn on top).</summary>
        public int Priority;
        public bool Interactable = true, Selected, Hovered, Pressed, Dead, Alert, Empty;
        public HudButtonKind Kind; public HudButtonStyle Style;
        public Color LabelColor = OzUi.Ink;
        public Color BgColor = HudStyle.SlateDeep, EdgeColor = HudStyle.Line, SelectedColor = HudStyle.Amber;   // flat fallback only
        OzButtonState shownState = (OzButtonState)(-1); bool shownEmpty; float sink;

        public bool Alive => !Dead && Rt && Rt.gameObject.activeInHierarchy;

        /// <summary>Staged-HUD signature: small buttons (large from 48 u tall), tracking +0.14.</summary>
        public static HudButton Make(Transform parent, string name, string text, Vector2 size, int fontSize = 15, bool display = true)
            => Make(parent, name, text, size, size.y >= 48 ? HudButtonKind.Large : HudButtonKind.Small, fontSize, 0.14f);

        public static HudButton Make(Transform parent, string name, string text, Vector2 size, HudButtonKind kind, float fontSize = 12, float tracking = 0.14f,
                                     HudButtonStyle style = HudButtonStyle.Normal, bool shadow = false, OzH align = OzH.C)
        {
            var b = new HudButton { Kind = kind, Style = style };
            b.Rt = Hud.Node(name, parent); b.Rt.sizeDelta = size;
            if (shadow && OzKit.Available) Hud.Shadow(b.Rt, kind == HudButtonKind.Large ? "Buttons/btn_l_mask" : "Buttons/btn_s_mask", 3, 4, 0.22f);
            b.Bg = Hud.Img(b.Rt, "Bg", Color.white); b.Bg.rectTransform.Fill();
            if (!OzKit.Available) { b.Bg.color = b.BgColor; b.Edge = Hud.Border(b.Rt, b.EdgeColor); }
            var hatch = OzKit.GetTexture("Textures/tex_hatch");
            if (hatch != null)
            {
                var hr = Hud.Node("Hatch", b.Rt); hr.Fill(2, kind == HudButtonKind.Key ? 6 : 2, 2, 2);
                b.Hatch = hr.gameObject.AddComponent<RawImage>(); b.Hatch.texture = hatch; b.Hatch.raycastTarget = false;
                b.Hatch.color = new Color(1, 1, 1, 0.05f); b.Hatch.uvRect = new Rect(0, 0, size.x * 2f / hatch.width, size.y * 2f / hatch.height);
                b.Hatch.enabled = false;
            }
            b.Content = Hud.Node("Content", b.Rt); b.Content.Fill();
            if (text != null)
            {
                b.Label = Hud.Label(b.Content, "Label", text, fontSize, OzUi.Ink, tracking, style == HudButtonStyle.Primary ? OzStyle.Plain : OzStyle.Underlay);
                if (kind == HudButtonKind.Key) b.Label.FillParent(align, 0, 4, 0, 0);                        // key face: centre y = (h - 4) / 2
                else if (align == OzH.L) b.Label.FillParent(OzH.L, 18, 0, 0, 0);
                else b.Label.FillParent(align);
            }
            b.Refresh();
            OpenZHud.RegisterButton(b);
            return b;
        }

        /// <summary>Alert also applies to a primary button: the production window's PLACE blinks primary / alert every 300 ms
        /// (guide 9.6; before, Primary won and the blink never showed).</summary>
        public OzButtonState State =>
            !Interactable ? OzButtonState.Disabled :
            Pressed ? (Style == HudButtonStyle.Primary ? OzButtonState.PrimaryHover : OzButtonState.Pressed) :
            Style == HudButtonStyle.Primary ? (Alert ? OzButtonState.Alert : Hovered ? OzButtonState.PrimaryHover : OzButtonState.Primary) :
            Style == HudButtonStyle.Danger ? (Hovered ? OzButtonState.DangerHover : OzButtonState.Danger) :
            Selected ? OzButtonState.Selected : Alert ? OzButtonState.Alert : Hovered ? OzButtonState.Hover : OzButtonState.Normal;

        public void Refresh()
        {
            if (!Rt) return;
            var st = State;
            if (OzKit.Available)
            {
                if (st != shownState || Empty != shownEmpty)
                {
                    if (Kind == HudButtonKind.Key) OzKit.SetKey(Bg, Empty ? OzButtonState.Normal : st, Empty);
                    else OzKit.SetButton(Bg, st, Kind == HudButtonKind.Large);
                    if (Bg.color != Color.white) Bg.color = Color.white;
                }
            }
            else
            {
                var bg = Pressed && Interactable ? Color.Lerp(BgColor, Color.black, 0.35f) : Hovered && Interactable ? Color.Lerp(BgColor, HudStyle.SlateHi, 0.9f) : BgColor;
                if (Selected) bg = Color.Lerp(bg, SelectedColor, 0.22f);
                if (Style == HudButtonStyle.Primary) bg = Color.Lerp(OzUi.AmberLo, OzUi.Amber, Hovered ? 1f : 0.6f);
                bg.a *= Interactable ? 1f : 0.7f;
                if (Bg.color != bg) Bg.color = bg;
                var e = Selected ? SelectedColor : Hovered && Interactable ? HudStyle.Amber : Style == HudButtonStyle.Danger ? OzUi.Alert : EdgeColor;
                foreach (var i in Edge) if (i.color != e) i.color = e;
            }
            shownState = st; shownEmpty = Empty;
            if (Hatch) Hatch.enabled = st == OzButtonState.Disabled && !Empty;
            float want = Pressed && Interactable ? (Kind == HudButtonKind.Key ? 3f : 1f) : 0f;              // content sinks while pressed
            if (Content && !Mathf.Approximately(sink, want)) { sink = want; Content.offsetMin = new Vector2(0, -want); Content.offsetMax = new Vector2(0, -want); }
            if (Label != null)
            {
                var c = st == OzButtonState.Primary || st == OzButtonState.PrimaryHover ? (Color)OzUi.GM950
                      : st == OzButtonState.Disabled || Empty ? (Color)OzUi.Dim
                      : Selected ? (Color)OzUi.AmberHi
                      : st == OzButtonState.Danger || st == OzButtonState.DangerHover ? (Color)OzUi.AlertText
                      : LabelColor;
                Label.color = c;
            }
            if (Icon != null) Icon.SetAlpha(Interactable ? 1f : 0.4f);
        }

        public void Kill() { Dead = true; if (Rt) UnityEngine.Object.Destroy(Rt.gameObject); }
    }

    /// <summary>
    /// Unit picture (guide 5): the other helper's rendered icon / portrait from Resources when present, in a GM_950 well; else
    /// the kit glyph of the unit's class (INK_2), so the HUD works before the art exists.
    /// </summary>
    public sealed class UnitIcon
    {
        public RectTransform Rt; RawImage raw; Image well, glyph; OzText code; string shownType; Team shownTeam; bool portrait;
        readonly float pad;
        public Color GlyphColor = OzUi.Ink2;

        public UnitIcon(Transform parent, string name, Vector2 size, bool withWell = true, float pad = 3f)
        {
            this.pad = pad;
            Rt = Hud.Node(name, parent); Rt.sizeDelta = size;
            if (withWell) { well = Hud.Kit(Rt, "Well", "Panels/well_4", new OzRect(0, 0, size.x, size.y), null); well.rectTransform.Fill(); if (!OzKit.Available) well.color = OzUi.GM950; }
            float side = Mathf.Min(size.x, size.y) - 2f * pad;                                             // square art, centred (aspect kept)
            var r = Hud.Node("Art", Rt); r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.pivot = new Vector2(0.5f, 0.5f); r.anchoredPosition = Vector2.zero; r.sizeDelta = new Vector2(side, side);
            raw = r.gameObject.AddComponent<RawImage>(); raw.raycastTarget = false; raw.enabled = false;
            var g = Hud.Node("Glyph", Rt); g.anchorMin = g.anchorMax = new Vector2(0.5f, 0.5f); g.pivot = new Vector2(0.5f, 0.5f); g.sizeDelta = new Vector2(side, side) * (withWell ? 0.72f : 1f);
            glyph = g.gameObject.AddComponent<Image>(); glyph.raycastTarget = false; glyph.preserveAspect = true; glyph.enabled = false;
            code = Hud.Label(Rt, "Code", "", Mathf.Max(9, size.y * 0.3f), OzUi.Ink2, 0.04f, OzStyle.Plain).FillParent(OzH.C);
        }

        /// <summary>type = unit type ("grunt", "light", ...); portrait = prefer the robot face (robots always use it).</summary>
        public void Set(string type, Team team, bool preferPortrait = false)
        {
            if (type == shownType && team == shownTeam && preferPortrait == portrait) return;
            shownType = type; shownTeam = team; portrait = preferPortrait;
            Texture tex = type == null ? null : preferPortrait || ZBuildLists.IsRobot(type) ? UnitArt.Portrait(type, team) : UnitArt.Icon(type, team);
            raw.texture = tex; raw.enabled = tex != null;
            Sprite gs = type == null || tex != null ? null : OzKit.GetSprite(Hud.IconRel(UnitInfo.ClassGlyph(type), 96));
            glyph.sprite = gs; glyph.enabled = gs != null; glyph.color = GlyphColor;
            code.Set(tex != null || gs != null || type == null ? "" : UnitInfo.Code(type));
        }

        /// <summary>Show a kit glyph (Icons/ic_*) instead of unit art, e.g. the robot class mark.</summary>
        public void SetGlyph(string g)
        {
            if (shownType == "glyph:" + g) return;
            shownType = "glyph:" + g; shownTeam = Team.Neutral; portrait = false;
            raw.texture = null; raw.enabled = false;
            var gs = OzKit.GetSprite(Hud.IconRel(g, 96));
            glyph.sprite = gs; glyph.enabled = gs != null; glyph.color = GlyphColor;
            code.Set(gs != null ? "" : g.Substring(0, Mathf.Min(3, g.Length)).ToUpperInvariant());
        }

        public void SetTint(Color c) { GlyphColor = c; if (glyph.enabled && glyph.color != c) glyph.color = c; }
        public bool HasArt => raw.enabled;

        public void SetAlpha(float a)
        {
            var c = raw.color; c.a = a; if (raw.color != c) raw.color = c;
            c = glyph.color; c.a = a; if (glyph.color != c) glyph.color = c;
            code.Alpha = a;
        }
    }

    /// <summary>
    /// Portraits (robots) and production icons (vehicles + cannons) from Resources. Folders in priority order (the first
    /// (Resources/OpenZ/UI/Portraits, Resources/OpenZ/UI/Icons). Names are matched loosely ("portrait_grunt_red.png",
    /// "icon_light_tank_red.png", "gatling_gun.png" ...); a "_red" / "_blue" variant wins for that team.
    /// </summary>
    public static class UnitArt
    {
        public static readonly string[] PortraitFolders = { "OpenZ/UI/PortraitsBS01", "OpenZ/UI/Portraits_BS01", "OpenZ/UI/portraits_bs01", "OpenZ/UI/Portraits", "OpenZ/UI/portraits", "UI/Portraits", "Portraits" };
        public static readonly string[] IconFolders = { "OpenZ/UI/Icons", "OpenZ/UI/icons", "UI/Icons", "Icons" };
        static Dictionary<string, Texture2D> portraits, icons;
        /// <summary>
        /// blue (Materials/ui/portraits_vehicles -> Resources/OpenZ/UI/PortraitsVehicles). A folder of their own, so a robot type never
        /// picks up a vehicle picture and the production icons stay the icons.</summary>
        public static readonly string[] VehiclePortraitFolders = { "OpenZ/UI/PortraitsVehicles", "OpenZ/UI/portraits_vehicles" };
        static Dictionary<string, Texture2D> vehiclePortraits;

        static readonly string[] Order = { "missile_launcher", "missile_cannon", "gatling", "howitzer", "crane", "apc", "jeep", "heavy", "medium", "light", "gun",
                                           "grunt", "psycho", "sniper", "tough", "pyro", "laser", "electrobot" };   // electrobot_v1: portrait_electrobot_* is its own token
        static readonly (string alias, string type)[] Aliases =
        {
            ("mobile_missile", "missile_launcher"), ("missilelauncher", "missile_launcher"), ("missile_launch", "missile_launcher"), ("rocket_launcher", "missile_launcher"), ("mlrs", "missile_launcher"),
            ("missilecannon", "missile_cannon"), ("gatlinggun", "gatling"), ("lighttank", "light"), ("mediumtank", "medium"), ("mech", "medium"), ("heavytank", "heavy"), ("bs01", "grunt"),
        };

        public static (string type, string team) Classify(string name)
        {
            var n = name.ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
            var tok = n.Split(new[] { '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
            string team = Array.IndexOf(tok, "red") >= 0 ? "red" : Array.IndexOf(tok, "blue") >= 0 ? "blue" :
                          (Array.IndexOf(tok, "neutral") >= 0 || Array.IndexOf(tok, "grey") >= 0 || Array.IndexOf(tok, "gray") >= 0) ? "neutral" : null;
            foreach (var t in Order)                                                                       // an explicit type wins over an alias ("bs01_tough")
            {
                if (t.Contains("_")) { if (n.Contains(t)) return (t, team); }
                else if (Array.IndexOf(tok, t) >= 0) return (t, team);
            }
            foreach (var (a, t) in Aliases) if (n.Contains(a)) return (t, team);
            foreach (var t in Order) if (t.Length >= 4 && !t.Contains("_") && n.Contains(t)) return (t, team);   // "grunt01", "iconJeep"
            return (null, team);
        }

        static Dictionary<string, Texture2D> Load(string[] folders)
        {
            var d = new Dictionary<string, Texture2D>();
            foreach (var f in folders)
                foreach (var t in Resources.LoadAll<Texture2D>(f))
                {
                    var (type, team) = Classify(t.name); if (type == null) continue;
                    var key = team == null ? type : type + "|" + team;
                    if (!d.ContainsKey(key)) d[key] = t;
                }
            return d;
        }

        static Texture2D Find(Dictionary<string, Texture2D> d, string type, Team team)
        {
            string tn = TeamColors.Key(team);
            if (d.TryGetValue(type + "|" + tn, out var t)) return t;
            if (d.TryGetValue(type, out t)) return t;
            if (d.TryGetValue(type + "|red", out t)) return t;                  // any variant beats the placeholder
            return d.TryGetValue(type + "|blue", out t) ? t : null;
        }

        public static Texture2D Portrait(string robotType, Team team)
        {
            portraits ??= Load(PortraitFolders); icons ??= Load(IconFolders);
            return Find(portraits, robotType, team) ?? Find(icons, robotType, team);
        }

        public static Texture2D Icon(string type, Team team)
        {
            portraits ??= Load(PortraitFolders); icons ??= Load(IconFolders);
            if (ZBuildLists.IsRobot(type)) return Portrait(type, team);
            return Find(icons, type, team) ?? Find(portraits, type, team);
        }

        /// <summary>The crewed vehicle's / cannon's own portrait for this team, or null (robots, neutral = empty vehicles, missing art).
        /// Exact team only: a red picture never stands in for a blue crew.</summary>
        public static Texture2D VehiclePortrait(string type, Team team)
        {
            if (string.IsNullOrEmpty(type) || ZBuildLists.IsRobot(type) || team == Team.Neutral) return null;
            vehiclePortraits ??= Load(VehiclePortraitFolders);
            return vehiclePortraits.TryGetValue(type + "|" + TeamColors.Key(team), out var t) ? t : null;
        }

        public static int Count { get { portraits ??= Load(PortraitFolders); icons ??= Load(IconFolders); return portraits.Count + icons.Count; } }
    }

    /// <summary>Names, glyphs and weapons for the HUD (guide 5 weapon names; plan/research/zod_units_combat.md 1.3).</summary>
    public static class UnitInfo
    {
        static readonly Dictionary<string, string> names = new Dictionary<string, string>();

        /// <summary>Display name of a unit / building type (cached: the overlay and the production window ask every frame).</summary>
        public static string Name(string t)
        {
            if (t == null) return "";
            if (names.TryGetValue(t, out var n)) return n;
            switch (t)
            {
                case "light": n = "LIGHT TANK"; break;
                case "medium": n = "MECH"; break;
                case "heavy": n = "HEAVY TANK"; break;
                case "missile_launcher": n = "MLRS"; break;
                case "missile_cannon": n = "MISSILE CANNON"; break;
                case "fort_front": case "fort_back": n = "FORT"; break;
                case "robot_factory": n = "ROBOT FACTORY"; break;
                case "vehicle_factory": n = "VEHICLE FACTORY"; break;
                case "repair": n = "REPAIR STATION"; break;
                default: n = t.Replace('_', ' ').ToUpperInvariant(); break;
            }
            names[t] = n;
            return n;
        }

        /// <summary></summary>
        public static string Short(string t)
        {
            switch (t)
            {
                case "light": return "LIGHT";
                case "medium": return "MECH";
                case "heavy": return "HEAVY";
                case "missile_launcher": return "MLRS";
                case "missile_cannon": return "MISSILE";
                case "howitzer": return "HOWITZ.";
                case "electrobot": return "ELECTRO";
            }
            return Name(t);
        }

        public static string Code(string t)
        {
            switch (t)
            {
                case "grunt": return "GRU"; case "psycho": return "PSY"; case "sniper": return "SNP"; case "tough": return "TGH";
                case "pyro": return "PYR"; case "laser": return "LSR"; case "electrobot": return "ELB"; case "jeep": return "JEP"; case "light": return "LT";
                case "medium": return "MCH"; case "heavy": return "HT"; case "apc": return "APC"; case "missile_launcher": return "ML";
                case "crane": return "CRN"; case "gatling": return "GTL"; case "gun": return "GUN"; case "howitzer": return "HOW";
                case "missile_cannon": return "MC";
            }
            return t == null ? "" : t.Substring(0, Math.Min(3, t.Length)).ToUpperInvariant();
        }

        /// <summary>Kit glyph of a unit / building type (Icons/ic_*).</summary>
        public static string ClassGlyph(string t)
        {
            if (t == null) return "robot";
            if (ZBuildLists.IsRobot(t)) return "robot";
            switch (t)
            {
                case "jeep": return "jeep";
                case "light": case "medium": return "tank";
                case "heavy": return "heavy";
                case "apc": return "apc";
                case "missile_launcher": case "missile_cannon": return "missile";
                case "crane": return "crane";
                case "gatling": return "gatling";
                case "gun": return "cannon";
                case "howitzer": return "howitzer";
                case "fort_front": case "fort_back": return "fort";
                case "robot_factory": case "vehicle_factory": return "factory";
                case "repair": return "repair_station";
                case "radar": return "radar";
            }
            return "robot";
        }

        public static string Weapon(ZObject o)
        {
            if (o == null) return "";
            if (o.Kind == ZKind.Vehicle && o.Type == "apc") return o.Drivers.Count == 0 ? "NONE" : WeaponOfType(o.CrewWeaponType) + " ×" + o.CrewFirepower;   // apc_v1: the crew's weapon x crew (vapc.cpp:266-342)
            return WeaponOfType(o.Type);
        }

        public static string WeaponOfType(string type)
        {
            switch (type)
            {
                case "grunt": return "RIFLE";
                case "psycho": return "MACHINE GUN";
                case "sniper": return "SNIPER RIFLE";
                case "tough": return "BAZOOKA";
                case "pyro": return "FLAMER";
                case "laser": return "LASER";
                case "electrobot": return "TESLA GATLING";                                   // electrobot_v1
                case "jeep": return "MACHINE GUN";
                case "light": case "medium": case "heavy": return "CANNON";
                case "apc": return "MISSILES";
                case "missile_launcher": return "MISSILES";
                case "crane": return "NONE";
                case "gatling": return "GATLING";
                case "gun": return "CANNON";
                case "howitzer": return "HOWITZER";
                case "missile_cannon": return "MISSILES";
            }
            return "";
        }

        public static string WeaponGlyph(ZObject o)
        {
            if (o == null) return "rifle";
            switch (o.Kind == ZKind.Vehicle && o.Type == "apc" && o.Drivers.Count > 0 ? o.CrewWeaponType : o.Type)   // apc_v1: the crew's weapon
            {
                case "tough": return "bazooka";
                case "pyro": return "flame";
                case "laser": return "laser";
                case "electrobot": return "tesla";                                          // electrobot_v1: Kit/Icons/ic_tesla_48 / 96 / 192
                case "psycho": case "jeep": case "gatling": return "gatling";
                case "light": case "medium": case "heavy": case "gun": return "cannon";
                case "apc": case "missile_launcher": case "missile_cannon": return "missile";
                case "crane": return "wrench";
                case "howitzer": return "howitzer";
            }
            return "rifle";
        }

        public static string Kind(string t) => ZBuildLists.IsRobot(t) ? "ROBOTS" : ZBuildLists.IsCannon(t) ? "CANNON" : "VEHICLE";

        /// <summary>Seconds as Zod's m:ss (rounded up so 0:00 means done).</summary>
        public static string Mmss(double s)
        {
            int t = Mathf.CeilToInt((float)Math.Max(0, s));
            return $"{t / 60}:{t % 60:00}";
        }

        /// <summary>Zod's clock (zhud.cpp:1018-1131): h:mm:ss of game time, h &gt; 9 shows "!".</summary>
        public static string Clock(double s)
        {
            int t = (int)Math.Max(0, s); int h = t / 3600, m = t / 60 % 60, sec = t % 60;
            return $"{(h > 9 ? "!" : h.ToString())}:{m:00}:{sec:00}";
        }

        /// <summary>End-screen time: m:ss under an hour, else h:mm:ss.</summary>
        public static string Duration(double s)
        {
            int t = (int)Math.Max(0, s);
            return t >= 3600 ? $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}" : $"{t / 60}:{t % 60:00}";
        }
    }

    /// <summary>
    /// Reaches PlayerInput's private order plumbing so the minimap and HUD buttons behave exactly like the map controls
    /// (same pending lists, Shift chains, Z-nearest, order marker, control groups, R/V/G cycling) without editing
    /// PlayerInput. Every member is looked up by name once; a missing member (drift) logs one warning and the caller
    /// falls back to a local copy of the same rule.
    /// </summary>
    public sealed class PlayerInputBridge
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static FieldInfo fPending, fGroups;
        static MethodInfo mFlush, mCycle, mSelectAll, mNearHostiles, sMarkerPos, sMarkerTime, sSelectTime;
        static bool resolved;
        public static readonly List<string> Missing = new List<string>();
        public readonly PlayerInput Input;

        public PlayerInputBridge(PlayerInput input) { Input = input; Resolve(); }

        static void Resolve()
        {
            if (resolved) return; resolved = true;
            var t = typeof(PlayerInput);
            fPending = t.GetField("pending", F); if (fPending == null) Missing.Add("field pending");
            fGroups = t.GetField("groups", F); if (fGroups == null) Missing.Add("field groups");
            mFlush = t.GetMethod("Flush", F, null, new[] { typeof(bool), typeof(Vector2) }, null); if (mFlush == null) Missing.Add("Flush(bool, Vector2)");
            mCycle = t.GetMethod("Cycle", F, null, new[] { typeof(char), typeof(ZKind), typeof(Vector2) }, null); if (mCycle == null) Missing.Add("Cycle(char, ZKind, Vector2)");
            mSelectAll = t.GetMethod("SelectAll", F, null, new[] { typeof(Func<ZObject, bool>) }, null); if (mSelectAll == null) Missing.Add("SelectAll(Func<ZObject,bool>)");
            mNearHostiles = t.GetMethod("NearHostiles", F, null, new[] { typeof(ZObject) }, null); if (mNearHostiles == null) Missing.Add("NearHostiles(ZObject)");
            sMarkerPos = t.GetProperty("MarkerPos", F)?.GetSetMethod(true); if (sMarkerPos == null) Missing.Add("MarkerPos setter");
            sMarkerTime = t.GetProperty("MarkerTime", F)?.GetSetMethod(true); if (sMarkerTime == null) Missing.Add("MarkerTime setter");
            sSelectTime = t.GetProperty("SelectTime", F)?.GetSetMethod(true); if (sSelectTime == null) Missing.Add("SelectTime setter");
            if (Missing.Count > 0) Debug.LogWarning("[OpenZ HUD] PlayerInput members not found (using local fallbacks): " + string.Join(", ", Missing));
        }

        public Dictionary<ZObject, List<ZWaypoint>> Pending => fPending?.GetValue(Input) as Dictionary<ZObject, List<ZWaypoint>>;
        public Dictionary<int, List<ZObject>> Groups => fGroups?.GetValue(Input) as Dictionary<int, List<ZObject>>;

        public bool Flush(bool zHeld, Vector2 mp) { if (mFlush == null) return false; mFlush.Invoke(Input, new object[] { zHeld, mp }); return true; }
        public bool Cycle(char kind, ZKind k, Vector2 mp) { if (mCycle == null) return false; mCycle.Invoke(Input, new object[] { kind, k, mp }); return true; }
        public bool SelectAll(Func<ZObject, bool> f) { if (mSelectAll == null) return false; mSelectAll.Invoke(Input, new object[] { f }); return true; }

        /// <summary>PlayerInput.NearHostiles (zplayer.cpp:2726-2744); local copy of the same test when the member drifted.</summary>
        public bool NearHostiles(ZObject u)
        {
            if (mNearHostiles != null) return (bool)mNearHostiles.Invoke(Input, new object[] { u });
            var W = Input.game.World; if (u.U == null) return false;
            double r = u.U.attack_radius + W.S.agro_distance;
            foreach (var o in W.Objects)
                if ((o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon) && o.Owner != Team.Neutral && o.Owner != u.Owner && !o.Destroyed
                    && (o.CenterX - u.CenterX) * (double)(o.CenterX - u.CenterX) + (o.CenterY - u.CenterY) * (double)(o.CenterY - u.CenterY) <= r * r)
                    return true;
            return false;
        }

        public void SetMarker(Vector3 world) { sMarkerPos?.Invoke(Input, new object[] { world }); sMarkerTime?.Invoke(Input, new object[] { Time.unscaledTime }); }
        public void MarkSelected() => sSelectTime?.Invoke(Input, new object[] { Time.unscaledTime });
    }
}
