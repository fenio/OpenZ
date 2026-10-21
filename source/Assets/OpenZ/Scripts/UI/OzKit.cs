using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>Button art states (UI_STYLE_GUIDE.md section 6); file suffix = the lower-case name (PrimaryHover = primary_hover).</summary>
    public enum OzButtonState { Normal, Hover, Pressed, Selected, Disabled, Alert, Primary, PrimaryHover, Danger, DangerHover }

    /// <summary>
    /// Loader + helpers for the OpenZ UI kit (Materials/ui/kit -> Assets/OpenZ/Resources/OpenZ/Kit, import settings by
    /// OzKitImporter from kit.json). Sprites are 2x (200 PPU): with the HUD CanvasScaler (reference 1920x1080, match height,
    /// reference PPU 100) Image.SetNativeSize gives the size in canvas units and 9-slice borders render at the guide's sizes.
    /// Everything here only dresses RectTransforms; it never changes HUD logic or input.
    /// </summary>
    public static class OzKit
    {
        public const string Root = "OpenZ/Kit/";   // NOT under Resources/OpenZ/UI: the staged UnitArt scans that folder recursively
        public static readonly Color32 ShadowTint = new Color32(2, 5, 3, 84);          // #020503 33 %, hard offset +5, +6 u
        /// <summary></summary>
        public static readonly Color32 Led = new Color32(255, 199, 102, 255), LedGlow = new Color32(255, 143, 42, 255);   // #FFC766, #FF8F2A
        static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
        static readonly string[] StateFile = { "normal", "hover", "pressed", "selected", "disabled", "alert", "primary", "primary_hover", "danger", "danger_hover" };
        static Font font;

        public static bool Available => GetSprite("Panels/panel_lg") != null;

        /// <summary>Inter Tight ExtraBold (SIL OFL 1.1), the only UI font: replaces the OS fonts of HudFonts.</summary>
        public static Font Font => font ? font : font = Resources.Load<Font>(Root + "Fonts/InterTight-ExtraBold");

        /// <summary>rel = path inside the kit without extension, e.g. "Panels/panel_lg", "Buttons/btn_s_hover".</summary>
        public static Sprite GetSprite(string rel)
        {
            if (!spriteCache.TryGetValue(rel, out var s)) { s = Resources.Load<Sprite>(Root + rel); spriteCache[rel] = s; }
            return s;
        }

        public static Texture2D GetTexture(string rel)
        {
            if (!texCache.TryGetValue(rel, out var t)) { t = Resources.Load<Texture2D>(Root + rel); texCache[rel] = t; }
            return t;
        }

        public static Sprite Button(OzButtonState st, bool large = false) => GetSprite("Buttons/btn_" + (large ? "l_" : "s_") + StateFile[(int)st]);

        /// <summary>New child Image showing a kit sprite (raycast off). Sliced falls back to Simple for sprites without borders.</summary>
        public static Image Img(Transform parent, string name, string rel, Image.Type type = Image.Type.Sliced, Color? tint = null)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            Apply(img, rel, type, tint);
            return img;
        }

        public static void Apply(Image img, string rel, Image.Type type = Image.Type.Sliced, Color? tint = null)
        {
            var s = GetSprite(rel);
            img.sprite = s;
            bool sliceable = s != null && s.border != Vector4.zero;
            img.type = (type == Image.Type.Sliced || type == Image.Type.Tiled) && !sliceable ? Image.Type.Simple : type;
            img.color = tint ?? Color.white;
            img.pixelsPerUnitMultiplier = 1f;
        }

        /// <summary>Stretch rt over its parent with insets in canvas units (l, b, r, t; Unity y-up).</summary>
        public static RectTransform FillParent(RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        public static int Cut(string size) => size == "lg" ? 12 : size == "md" ? 10 : 8;

        /// <summary>
        /// Dress a panel root (already sized, in canvas units) as an OpenZ plate (guide 4.1): hard shadow, 9-sliced body,
        /// optional olive header band (height = header u), amber / alert tab, owner stripe and 4 rivets. The pieces are
        /// inserted as the first children, so the panel's own content keeps drawing on top. Returns the body Image.
        /// size = "lg" (chamfer 12), "md" (10), "sm" (8).
        /// </summary>
        public static Image Plate(RectTransform panel, string size = "lg", float header = 0f, Color? tab = null, Color? owner = null, bool rivets = false, bool shadow = true)
        {
            int cut = Cut(size), i = 0;
            if (shadow)
            {
                var sh = Img(panel, "OzShadow", "Panels/panel_" + size + "_mask", Image.Type.Sliced, ShadowTint);
                FillParent(sh.rectTransform, 5, -6, -5, 6);
                sh.transform.SetSiblingIndex(i++);
            }
            var body = Img(panel, "OzBody", "Panels/panel_" + size);
            FillParent(body.rectTransform);
            body.transform.SetSiblingIndex(i++);
            if (header > 0f)
            {
                var h = Img(panel, "OzHeader", "Panels/panel_" + size + "_header");
                var r = h.rectTransform;
                r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1);
                r.offsetMin = new Vector2(0, -(header + 2f)); r.offsetMax = Vector2.zero;
                h.transform.SetSiblingIndex(i++);
            }
            if (owner.HasValue)
            {
                var st = Img(panel, "OzOwner", "Bits/stripe", Image.Type.Sliced, owner);
                var r = st.rectTransform;
                r.anchorMin = new Vector2(0, 0); r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 0.5f);
                r.offsetMin = new Vector2(2, 6); r.offsetMax = new Vector2(6, -(cut + 6));
                st.transform.SetSiblingIndex(i++);
            }
            if (tab.HasValue)
            {
                var tb = Img(panel, "OzTab", "Bits/tab", Image.Type.Simple, tab);
                tb.SetNativeSize();
                var r = tb.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
                r.anchoredPosition = new Vector2(cut + 1, 0);
                tb.transform.SetSiblingIndex(i++);
            }
            if (rivets)
            {
                float c = cut * 0.42f;
                var spots = new[] { (new Vector2(0, 1), new Vector2(9 + c, -(9 + c))), (new Vector2(1, 1), new Vector2(-9, -9)),
                                    (new Vector2(1, 0), new Vector2(-(9 + c), 9 + c)), (new Vector2(0, 0), new Vector2(9, 9)) };
                foreach (var (anchor, pos) in spots)
                {
                    var rv = Img(panel, "OzRivet", "Bits/rivet", Image.Type.Simple);
                    rv.SetNativeSize();
                    var r = rv.rectTransform;
                    r.anchorMin = r.anchorMax = anchor; r.pivot = new Vector2(0.45f, 0.55f);
                    r.anchoredPosition = pos;
                    rv.transform.SetSiblingIndex(i++);
                }
            }
            return body;
        }

        /// <summary>Swap a button Image to the sprite of its state (keep the staged HudButton polling; call from Refresh).</summary>
        public static void SetButton(Image img, OzButtonState st, bool large = false)
        {
            var s = Button(st, large);
            if (s != null && img.sprite != s) { img.sprite = s; img.type = Image.Type.Sliced; }
        }

        /// <summary>Command-bar keycap (Buttons/key_*: R V B G and control groups). Primary / danger map to normal;
        /// empty = unlit key of an unused group. Content sits on the key face: centre y = (h - 4) / 2 from the top, +3 u pressed.</summary>
        public static void SetKey(Image img, OzButtonState st, bool empty = false)
        {
            string f = empty ? "empty" : st == OzButtonState.Hover || st == OzButtonState.Pressed || st == OzButtonState.Selected
                || st == OzButtonState.Disabled || st == OzButtonState.Alert ? StateFile[(int)st] : "normal";
            var s = GetSprite("Buttons/key_" + f);
            if (s != null && img.sprite != s) { img.sprite = s; img.type = Image.Type.Sliced; }
        }

        /// <summary>Bar fill (Bars/bar_fill_&lt;fill&gt;: health, health_low, lost, red, blue, amber, neutral, signal, white) as
        /// Image.Type.Filled, horizontal; blue strength fills from the right. Put it inside a Bars/bar_track, inset 1 u.</summary>
        public static Image FillBar(Transform parent, string name, string fill, bool fromRight = false)
        {
            var img = Img(parent, name, "Bars/bar_fill_" + fill, Image.Type.Filled);
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = fromRight ? (int)Image.OriginHorizontal.Right : (int)Image.OriginHorizontal.Left;
            img.fillAmount = 0f;
            return img;
        }

        /// <summary>Progress ring fill (Production/ring_fill or ring_sm_fill): Radial 360 from the top, clockwise.</summary>
        public static Image RingFill(Transform parent, string name, bool small = false)
        {
            var img = Img(parent, name, small ? "Production/ring_sm_fill" : "Production/ring_fill", Image.Type.Filled);
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Top;
            img.fillClockwise = true;
            img.fillAmount = 0f;
            return img;
        }

        static string curName; static int curFrame = -1, curSize;

        /// <summary>
        /// OzSoftCursor (256 px anti-aliased sources, sized to the screen and the UI SCALE, whole pixels); the hardware set below
        /// stays as the fallback (PlayerPrefs oz_soft_cursor = 0, HD set not imported, window in the background).
        /// Names: arrow, move, attack, enter, exit, grab, nono, repair, cannon, place. Frames 0..3 (0.2 s each).</summary>
        public static void SetCursor(string name, int frame = 0)
        {
            if (OzSoftCursor.Show(name)) { curName = null; return; }                              // the software cursor animates its own frames
            SetHardwareCursor(name, frame);
        }

        /// <summary>Back to the plain OS cursor (software cursor off, hardware cursor reset).</summary>
        public static void ResetCursor()
        {
            OzSoftCursor.Hide();
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            curName = null;
        }

        /// <summary>Hardware cursor side (guide 9.10): 32 px below 1300 px screen height, 48 px up to 2000, else 64.</summary>
        public static int HardwareCursorSize { get { int h = Mathf.Max(Screen.height, OzSettings.DisplayHeight); return h >= 2000 ? 64 : h >= 1300 ? 48 : 32; } }   // a lower RESOLUTION step fills the display: the system draws the pointer in the display's pixels, so the display's height picks its size

        /// <summary>Hardware cursor from the kit (guide 9.10): 32 px below 1300 px screen height, 48 px up to 2000, else 64.</summary>
        public static void SetHardwareCursor(string name, int frame = 0)
        {
            int size = HardwareCursorSize;
            frame &= 3;
            if (name == curName && frame == curFrame && size == curSize) return;
            var tex = OzSoftCursor.HardwareTexture(name, frame, size);
            if (tex == null)
            {
                tex = GetTexture("Cursors/cur_" + name + "_" + size + "_" + frame);
                if (tex == null) return;
                if (GreenCursors.Contains(name)) tex = Green(tex);                               // endgame_ui_v1: move = green, red only to attack
            }
            var hot = name == "arrow" ? new Vector2(4f, 3f) * (size / 32f) : new Vector2(size * 0.5f, size * 0.5f);
            Cursor.SetCursor(tex, new Vector2(Mathf.Floor(hot.x + 0.5f), Mathf.Floor(hot.y + 0.5f)), CursorMode.Auto);   // arrow (4,3) (6,5) (8,6)
            curName = name; curFrame = frame; curSize = size;
        }

        /// <summary>
        /// kit draws every cursor in the player's red; these get the kit's GO green instead (hue swap of the red paint, outline, white
        /// core and shading kept). Recoloured once per texture (the cursor textures are Read/Write), cached.</summary>
        public static readonly HashSet<string> GreenCursors = new HashSet<string> { "move" };
        static readonly Dictionary<Texture2D, Texture2D> greenCache = new Dictionary<Texture2D, Texture2D>();

        public static Texture2D Green(Texture2D src)
        {
            if (!src) return src;
            if (greenCache.TryGetValue(src, out var g) && g) return g;
            try
            {
                Color.RGBToHSV((Color)OzUi.Go, out float gh, out float gs, out _);
                var px = src.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i]; if (c.a == 0) continue;
                    Color.RGBToHSV(c, out float h, out float s, out float v);
                    if (s < 0.18f || v < 0.1f || (h > 0.14f && h < 0.86f)) continue;                   // outline, white core, non-red: as drawn
                    var n = Color.HSVToRGB(gh, Mathf.Clamp01(s * Mathf.Lerp(1f, gs, 0.35f)), v * 0.94f);
                    px[i] = new Color32((byte)(n.r * 255f + 0.5f), (byte)(n.g * 255f + 0.5f), (byte)(n.b * 255f + 0.5f), c.a);
                }
                g = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false) { name = src.name + "_green", filterMode = src.filterMode, wrapMode = TextureWrapMode.Clamp };
                g.SetPixels32(px); g.Apply(false, false);                                            // stays readable: Cursor.SetCursor needs it
            }
            catch (System.Exception e) { Debug.LogWarning("[OzKit] green cursor: " + e.Message); g = src; }
            greenCache[src] = g;
            return g;
        }

        /// <summary>Animated cursor: call every frame with the cursor Zod's decision tree picked.</summary>
        public static void TickCursor(string name) => SetCursor(name, (int)(Time.unscaledTime / 0.2f));
    }
}
