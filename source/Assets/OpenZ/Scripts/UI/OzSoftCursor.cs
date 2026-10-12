using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// A crisp SOFTWARE cursor. Hardware cursors (Cursor.SetCursor) are drawn by
    /// the OS at the texture's own pixel size with no filtering, so the kit's 48 px set looked small and stair-stepped at
    /// 2560 x 1600. This draws the kit's cursor set from 256 px anti-aliased sources (Kit/Cursors/HD, the kit's own vector shapes
    /// at whole screen pixels: 32 px at 1080p x (screen height / 1080) x the HUD's UI SCALE (64 px at 2560 x 1600 with the 1.35
    /// default), hotspot on the exact mouse pixel.
    ///  - The OS pointer is replaced by a fully transparent hardware cursor (not Cursor.visible = false), so the other systems that
    ///    own Cursor.visible (CaptureKit cinematics, ZodBriefing) keep working: this cursor simply follows Cursor.visible.
    ///  - Window unfocused / mouse outside the window: the kit's hardware cursor comes back (a visible pointer over a background
    ///    window), the software one hides.
    ///  - Every cursor state OpenZCursors decides (arrow, move, attack, enter, exit, grab, nono, repair, cannon, place) with the
    ///    kit's 4 frames x 0.2 s; "move" is baked in the GO green (OzKit.GreenCursors) like the hardware path recolours it.
    ///  - PlayerPrefs oz_soft_cursor = 0 keeps the old hardware path (OzKit.SetHardwareCursor); oz_kit_cursor = 0 (OpenZCursors)
    ///    still means the plain OS cursor.
    /// Called only through OzKit.SetCursor / OzKit.ResetCursor; never changes what an order does.
    /// Cause = the 40 HD PNGs were imported as CUBEMAPS (textureShape 2), so Resources.Load<Texture2D>
    /// returned null, this overlay never showed and the game always fell back to the kit's 32 / 48 px pixel-art hardware set. Fixed
    /// to Texture2D, and by default the cursor is now a HARDWARE cursor baked from the 256 px HD frames at the SAME size as before
    /// (OzKit.HardwareCursorSize: 32 / 48 / 64 by screen height; GPU down-sample of the mip chain, cached per state /
    /// frame / size): the OS draws it at native pixels, no frame of lag. Same art, hotspots and sizes.
    /// PlayerPrefs oz_cursor_hw = 0 = the software overlay as before.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class OzSoftCursor : MonoBehaviour
    {
        public const string HdPrefix = "Cursors/HD/cur_";
        public const int HdSize = 256;
        /// <summary>Cursor side in screen px at 1080p and UI scale 1 (the kit's 32 px set); scaled by height / 1080 x UI scale.</summary>
        public static float BaseSize = 32f;
        /// <summary>Close-up captures / QA: show this cursor instead of the decided one (null = off).</summary>
        public static string ForceName;
        /// <summary>Close-up captures: draw every cursor state in a row, left edge at this screen point (bottom-left origin).</summary>
        public static bool Gallery; public static Vector2 GalleryAt = new Vector2(200f, 200f);
        public static readonly string[] Names = { "arrow", "move", "attack", "enter", "exit", "grab", "nono", "repair", "cannon", "place" };

        static OzSoftCursor inst;
        static int prefsFrame = -1; static bool prefsOn = true, prefsHw = true; static float prefsScale = 1f;
        Canvas canvas; RectTransform rt; RawImage img; RawImage[] gallery;
        string cur = "arrow"; bool active, hwBlank, hwKit; int sizePx;
        string hwName; int hwFrame = -1, hwN; static bool hwLogged;
        static Texture2D blank;

        /// <summary>The software cursor's on-screen rect (screen px, bottom-left origin) while shown; else an empty rect.</summary>
        public static Rect ScreenRect => inst && inst.img && inst.img.enabled ? new Rect(inst.rt.anchoredPosition, inst.rt.sizeDelta) : default;
        /// <summary>Is the software cursor drawing right now?</summary>
        public static bool Showing => inst && inst.active && inst.img && inst.img.enabled;
        public static int SizePx => inst ? inst.sizePx : 0;
        /// <summary></summary>
        public static int HardwarePx => inst && inst.active && inst.hwName != null ? inst.hwN : 0;

        static bool On { get { RefreshPrefs(); return prefsOn; } }

        /// <summary>PlayerPrefs and the UI scale, re-read twice a second (never per frame).</summary>
        static void RefreshPrefs()
        {
            if (Time.frameCount - prefsFrame <= 30 && prefsFrame >= 0) return;
            prefsFrame = Time.frameCount;
            prefsOn = PlayerPrefs.GetInt("oz_soft_cursor", 1) != 0;
            prefsHw = PlayerPrefs.GetInt("oz_cursor_hw", 1) != 0;
            prefsScale = OpenZHud.I ? OzText.UiScale : OzText.SavedScale;                             // the HUD's live scale, else the saved one (menus)
        }

        /// <summary>Show the software cursor with this state. False = unavailable (disabled, HD set not imported): use hardware.</summary>
        public static bool Show(string name)
        {
            if (!On || string.IsNullOrEmpty(name) || Tex(name, 0) == null) { if (inst && inst.active) inst.Deactivate(false); return false; }
            if (!inst) Create();
            inst.cur = name;
            if (!inst.active) { inst.active = true; inst.hwBlank = inst.hwKit = false; }
            return true;
        }

        /// <summary>Back to the plain OS cursor (OpenZCursors disabled / oz_kit_cursor 0).</summary>
        public static void Hide() { if (inst && inst.active) inst.Deactivate(true); }

        static readonly System.Collections.Generic.Dictionary<string, Texture2D[]> frames = new System.Collections.Generic.Dictionary<string, Texture2D[]>();
        /// <summary>HD frame texture (cached per state: no per-frame string building / allocation).</summary>
        static Texture2D Tex(string name, int frame)
        {
            if (!frames.TryGetValue(name, out var f))
            {
                f = new Texture2D[4];
                for (int i = 0; i < 4; i++) f[i] = OzKit.GetTexture(HdPrefix + name + "_" + HdSize + "_" + i);
                frames[name] = f;
            }
            return f[frame & 3];
        }

        static readonly System.Collections.Generic.Dictionary<string, Texture2D> hwCache = new System.Collections.Generic.Dictionary<string, Texture2D>();
        /// <summary>
        /// exactly), read back as a readable RGBA32 texture without mips (what Cursor.SetCursor needs). Cached; null without the HD set.</summary>
        public static Texture2D HardwareTexture(string name, int frame, int n)
        {
            if (string.IsNullOrEmpty(name)) return null;
            n = Mathf.Clamp(n, 8, HdSize);
            string key = name + "_" + (frame & 3) + "_" + n;
            if (hwCache.TryGetValue(key, out var t) && t) return t;
            var src = Tex(name, frame);
            if (src == null) return null;
            t = null;
            RenderTexture prev = RenderTexture.active, rt = null;
            try
            {
                rt = RenderTexture.GetTemporary(n, n, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                t = new Texture2D(n, n, TextureFormat.RGBA32, false, false) { name = "OZ_CursorHD_" + key, hideFlags = HideFlags.DontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                t.ReadPixels(new Rect(0, 0, n, n), 0, 0, false);
                t.Apply(false, false);                                                                 // stays readable: Cursor.SetCursor needs it
            }
            catch (System.Exception e) { Debug.LogWarning("[OzCursor] HD bake " + key + ": " + e.Message); t = null; }
            finally { RenderTexture.active = prev; if (rt) RenderTexture.ReleaseTemporary(rt); }
            hwCache[key] = t;
            return t;
        }

        /// <summary></summary>
        bool HardwareTick(string name, int frame)
        {
            int n = OzKit.HardwareCursorSize;
            if (hwName != null && name == hwName && frame == hwFrame && n == hwN) return true;
            var t = HardwareTexture(name, frame, n);
            if (t == null) return false;
            float hx = name == "arrow" ? 4f / 32f : 0.5f, hy = name == "arrow" ? 3f / 32f : 0.5f;         // hotspot from the top-left (kit: arrow (4,3) of 32)
            Cursor.SetCursor(t, new Vector2(Mathf.Round(hx * n), Mathf.Round(hy * n)), CursorMode.Auto);
            hwName = name; hwFrame = frame; hwN = n; hwBlank = true; hwKit = false;
            if (!hwLogged)
            {
                hwLogged = true;
                Debug.Log("[OzCursor] HD hardware cursor " + n + "x" + n + " px (" + name + ", baked from " + HdSize + " px; overlay size " + sizePx + ", screen " + Screen.width + "x" + Screen.height
                          + ", display " + Screen.currentResolution.width + "x" + Screen.currentResolution.height + ", " + Screen.fullScreenMode + ")");
            }
            return true;
        }

        static void Create()
        {
            var go = new GameObject("OpenZ_SoftCursor", typeof(RectTransform)) { layer = 5 };
            DontDestroyOnLoad(go);
            inst = go.AddComponent<OzSoftCursor>();
            var c = go.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 32767; c.pixelPerfect = true;   // no CanvasScaler: 1 unit = 1 screen px
            inst.canvas = c;
            inst.img = NewImage(go.transform, "Cursor", out inst.rt);
        }

        static RawImage NewImage(Transform parent, string name, out RectTransform r)
        {
            var g = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            r = (RectTransform)g.transform; r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = Vector2.zero; r.pivot = Vector2.zero;
            var im = g.AddComponent<RawImage>(); im.raycastTarget = false; im.enabled = false;
            return im;
        }

        void Deactivate(bool restoreOs)
        {
            active = false; hwName = null;
            if (img) img.enabled = false;
            if (gallery != null) foreach (var g in gallery) if (g) g.enabled = false;
            if (hwBlank || restoreOs) Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            hwBlank = hwKit = false;
        }

        static Texture2D Blank()
        {
            if (blank) return blank;
            blank = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "OZ_CursorBlank", hideFlags = HideFlags.DontSave };
            var px = new Color32[32 * 32]; blank.SetPixels32(px); blank.Apply(false, false);            // stays readable: Cursor.SetCursor needs it
            return blank;
        }

        void LateUpdate()
        {
            if (!active) return;
            var m = Mouse.current;
            Vector2 mp = m != null ? m.position.ReadValue() : new Vector2(-1f, -1f);
            bool inside = mp.x >= 0f && mp.y >= 0f && mp.x < Screen.width && mp.y < Screen.height;
            bool focused = Application.isFocused;
            string name = string.IsNullOrEmpty(ForceName) ? cur : ForceName;
            int frame = (int)(Time.unscaledTime / 0.2f) & 3;
            RefreshPrefs();
            float s = Screen.height / 1080f * Mathf.Clamp(prefsScale, 0.5f, 2f);
            sizePx = Mathf.Clamp(Mathf.RoundToInt(BaseSize * s * 0.5f) * 2, 16, 192);                 // even: the centre hotspot sits on a pixel corner
            if (prefsHw && HardwareTick(name, frame)) { img.enabled = false; SetGallery(Gallery && Cursor.visible, frame); return; }
            if (!focused)
            {   // background window: the OS draws the kit's hardware cursor, no software one
                img.enabled = false; SetGallery(false, 0);
                if (!hwKit) { OzKit.SetHardwareCursor(name, frame); hwKit = true; hwBlank = false; }
                return;
            }
            if (!hwBlank || hwName != null) { Cursor.SetCursor(Blank(), Vector2.zero, CursorMode.Auto); hwBlank = true; hwKit = false; hwName = null; }
            var tex = Tex(name, frame);
            bool show = Cursor.visible && inside && tex != null;
            img.enabled = show;
            if (show)
            {
                if (img.texture != tex) img.texture = tex;
                float hx = name == "arrow" ? 4f / 32f : 0.5f, hy = name == "arrow" ? 3f / 32f : 0.5f;          // hotspot from the top-left (kit: arrow (4,3) of 32)
                var bl = new Vector2(Mathf.Round(mp.x) - Mathf.Round(hx * sizePx), Mathf.Round(mp.y) - (sizePx - Mathf.Round(hy * sizePx)));
                rt.anchoredPosition = bl; rt.sizeDelta = new Vector2(sizePx, sizePx);
            }
            SetGallery(Gallery && Cursor.visible, frame);
        }

        void SetGallery(bool on, int frame)
        {
            if (!on) { if (gallery != null) foreach (var g in gallery) if (g && g.enabled) g.enabled = false; return; }
            if (gallery == null)
            {
                gallery = new RawImage[Names.Length];
                for (int i = 0; i < Names.Length; i++) gallery[i] = NewImage(transform, "Gallery_" + Names[i], out _);
            }
            for (int i = 0; i < Names.Length; i++)
            {
                var g = gallery[i]; var t = Tex(Names[i], frame);
                g.enabled = t != null; if (!g.enabled) continue;
                g.texture = t;
                var r = g.rectTransform;
                r.anchoredPosition = new Vector2(Mathf.Round(GalleryAt.x) + i * (sizePx + 12), Mathf.Round(GalleryAt.y));
                r.sizeDelta = new Vector2(sizePx, sizePx);
            }
        }

        void OnApplicationFocus(bool f) { if (f) { hwBlank = false; hwName = null; } else hwKit = false; }                  // re-apply after every switch
        void OnDestroy() { if (inst == this) { inst = null; if (hwBlank) Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); } }
    }
}
