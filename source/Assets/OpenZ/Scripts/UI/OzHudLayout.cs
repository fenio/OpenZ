using System.Globalization;
using UnityEngine;

namespace OpenZ
{
    /// <summary>The HUD groups the player can move in ARRANGE INTERFACE (OpenZHud.Arrange.cs). A group = panels that belong together
    /// and move as one: Status = the top command strip + the territory tab hanging under it; Unit = the unit / comms panel with its
    /// under-attack tab and its EXIT CREW tab; Command = the command bar (the factory list rides with it); Messages = the computer
    /// messages and the fort alarm; Cannons = the CANNON READY plates; Menu = the MENU button; Comms = the transmission window of
    /// the commander at HQ (ZodComms, layout ZodLayout.InGame: it shows while he speaks during play, on a canvas of its own).</summary>
    public enum OzPanel { Status, Minimap, Unit, Command, Messages, Cannons, Menu, Comms }

    /// <summary>
    /// Where the player put each HUD group. One number pair per group, saved in PlayerPrefs "oz_hud_pos_&lt;Group&gt;" as "x;y":
    /// the FRACTION OF THE GROUP'S FREE TRAVEL on the canvas, 0 .. 1 on both axes (0;0 = flush in the top-left corner, 1;1 = flush
    /// in the bottom-right corner). The free travel is the canvas size minus the group's own size, so a saved place is inside the
    /// screen by construction at every resolution, aspect and UI scale, and a group dropped against an edge stays against that edge.
    /// A group that was never moved has no entry: every layout number then comes straight from OzLayout, exactly as before.
    ///
    /// The HUD and everything that reads a panel's place (the production window's placement, the F1 help panel, the camera's lower
    /// limit) ask At(group, the OzLayout rectangle): it returns that rectangle moved by the group's offset from its default place on
    /// the canvas of this moment, in the same OzRect convention (same anchor), so Place / XOn / "RefH - y" keep working unchanged.
    /// The transmission window is the one group that is not on the HUD canvas: its own canvas is always RefH units tall, whatever
    /// the UI scale, so on the HUD canvas its frame is the window's size divided by the UI scale (Box), and ZodLayout.InGame takes
    /// the window's place from CommsPlace: the SAME saved fraction, applied to the window's free travel on its own canvas.
    /// View and input side only: the sim never reads it.
    /// </summary>
    public static class OzHudLayout
    {
        public const int Count = 8;
        public const string PrefPrefix = "oz_hud_pos_";
        /// <summary>The OPTIONS row (pause menu, GAME tab) and its tooltip.</summary>
        public const string Label = "ARRANGE INTERFACE";
        public const string Tip = "ARRANGE INTERFACE|Move the panels of the in-game interface: drag each one to where you want it. A panel always stays inside the screen and its place is remembered. RESET LAYOUT puts every panel back.|";
        public static readonly string[] Names = { "STATUS", "MINIMAP", "UNIT · COMMS", "COMMAND BAR", "MESSAGES", "CANNON READY", "MENU", "HQ TRANSMISSION" };
        /// <summary>Room above the unit panel for its tabs: EXIT CREW stands 38 u above the panel's top edge (OpenZHud.BuildUnitPanel),
        /// the under-attack tab 26 u (OzLayout.AlertTab).</summary>
        public const float UnitTabs = 38f;
        /// <summary>A message slides in from 12 u above its place (OpenZHud.RefreshToasts).</summary>
        public const float ToastRise = 12f;

        static readonly Vector2[] frac = new Vector2[Count];
        static readonly bool[] has = new bool[Count];
        static bool loaded;
        /// <summary>Bumped on every change (move, reset, reload).</summary>
        public static int Version { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { loaded = false; }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            for (int i = 0; i < Count; i++)
            {
                has[i] = false;
                string s = PlayerPrefs.GetString(PrefPrefix + (OzPanel)i, "");
                int k = s.IndexOf(';');
                if (k <= 0) continue;
                if (float.TryParse(s.Substring(0, k), NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    && float.TryParse(s.Substring(k + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                    && !float.IsNaN(x) && !float.IsNaN(y))
                { frac[i] = new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y)); has[i] = true; }
            }
        }

        /// <summary>Read the saved places again (after PlayerPrefs were changed from outside, e.g. a check restoring them).</summary>
        public static void Reload() { loaded = false; Version++; }

        public static bool Moved(OzPanel p) { Load(); return has[(int)p]; }
        public static bool AnyMoved { get { Load(); for (int i = 0; i < Count; i++) if (has[i]) return true; return false; } }
        public static Vector2 Fraction(OzPanel p) { Load(); return frac[(int)p]; }

        /// <summary>The HUD canvas in canvas units: 1080 / UI scale tall (CanvasScaler match height), as wide as the screen's aspect.</summary>
        public static void CanvasSize(out float cw, out float ch)
        {
            ch = OzLayout.RefH / Mathf.Clamp(OzText.UiScale, OzText.ScaleMin, OzText.ScaleMax);
            cw = Screen.height > 0 ? Screen.width * ch / Screen.height : OzLayout.RefW;
        }

        /// <summary>The rectangle a group keeps inside the screen, at its DEFAULT place (OzRect convention; cw picks the unit
        /// panel's default place: wide, raised or above the minimap; uiScale sizes the transmission window's frame: the live UI
        /// scale when it is not given).</summary>
        public static OzRect Box(OzPanel p, float cw, float uiScale = -1f)
        {
            switch (p)
            {
                case OzPanel.Comms:
                {   // the window's own canvas is always RefH units tall: one of its units is 1 / UI scale units of the HUD canvas
                    float s = uiScale > 0f ? uiScale : Mathf.Clamp(OzText.UiScale, OzText.ScaleMin, OzText.ScaleMax);
                    var z = ZodLayout.InGameDefault();
                    return new OzRect(OzAnchor.TL, z.x / s, z.y / s, z.W * z.scale / s, z.H * z.scale / s);
                }
                case OzPanel.Status: { var a = OzLayout.TopStrip; var b = OzLayout.TerrTab; return new OzRect(a.a, a.x, a.y, a.w, b.y + b.h - a.y); }
                case OzPanel.Minimap: return OzLayout.Minimap;
                case OzPanel.Unit: { var u = OzLayout.UnitPanelFor(cw); return new OzRect(u.a, u.x, u.y - UnitTabs, u.w, u.h + UnitTabs); }
                case OzPanel.Command: return OzLayout.CommandBar;
                case OzPanel.Messages:
                {   // the fort alarm, then up to ToastMax messages under it (OpenZHud.RefreshToasts), plus the slide-in rise
                    var f = OzLayout.FortAlert;
                    float h = f.h + 8f + OzLayout.ToastStep * (OzLayout.ToastMax - 1f) + OzLayout.Toast.h;
                    return new OzRect(f.a, f.x, f.y - ToastRise, f.w, h + ToastRise);
                }
                case OzPanel.Cannons: { var c = OzLayout.StoredCannon; return new OzRect(c.a, c.x, c.y, c.w, c.h + OzLayout.StoredCannonStep * (OzLayout.StoredCannonMax - 1f)); }
                default: return OzLayout.MenuButton;
            }
        }

        static bool FromBottom(OzAnchor a) => a == OzAnchor.BL || a == OzAnchor.BC || a == OzAnchor.BR;

        /// <summary>Top-left corner of an OzRect on a canvas cw x ch, measured from the canvas's top-left corner (y down).</summary>
        public static Vector2 TopLeft(OzRect r, float cw, float ch)
            => new Vector2(r.XOn(cw), FromBottom(r.a) ? ch - (OzLayout.RefH - r.y) : r.a == OzAnchor.C ? r.y + (ch - OzLayout.RefH) * 0.5f : r.y);

        /// <summary>Pure: the group's rectangle (x, y from the canvas's top-left corner, y down) when it stands at fraction f of its
        /// free travel. Inside the canvas for every f, as long as the group is not larger than the canvas.</summary>
        public static Rect BoxAt(OzPanel p, Vector2 f, float cw, float ch)
        {
            var b = Box(p, cw, OzLayout.RefH / ch);
            return new Rect(Mathf.Clamp01(f.x) * Mathf.Max(0f, cw - b.w), Mathf.Clamp01(f.y) * Mathf.Max(0f, ch - b.h), b.w, b.h);
        }

        /// <summary>Pure: the group's rectangle at its default place (same convention as BoxAt).</summary>
        public static Rect BoxDefault(OzPanel p, float cw, float ch)
        {
            var b = Box(p, cw, OzLayout.RefH / ch); var tl = TopLeft(b, cw, ch);
            return new Rect(tl.x, tl.y, b.w, b.h);
        }

        /// <summary>The group's rectangle on the HUD canvas now.</summary>
        public static Rect BoxNow(OzPanel p)
        {
            Load(); CanvasSize(out float cw, out float ch);
            return has[(int)p] ? BoxAt(p, frac[(int)p], cw, ch) : BoxDefault(p, cw, ch);
        }

        /// <summary>How far the group stands from its default place now (x right, y down, canvas units); zero while it was never moved.</summary>
        public static Vector2 Delta(OzPanel p)
        {
            Load();
            if (!has[(int)p]) return Vector2.zero;
            CanvasSize(out float cw, out float ch);
            var at = BoxAt(p, frac[(int)p], cw, ch); var def = BoxDefault(p, cw, ch);
            return new Vector2(at.x - def.x, at.y - def.y);
        }

        /// <summary>A layout rectangle of this group at the place the player gave the group. Never moved = the rectangle itself.</summary>
        public static OzRect At(OzPanel p, OzRect def)
        {
            Load();
            if (!has[(int)p]) return def;
            var d = Delta(p);
            return def.Move(d.x, d.y);
        }

        /// <summary>The transmission window's top-left corner on ITS OWN canvas (always RefH units tall, as wide as the screen's
        /// aspect), for a window w x h of that canvas's units: the default (defX, defY) while the Comms group was never moved, else
        /// the saved fraction of the window's free travel there. It is the same fraction the frame shows on the HUD canvas, so
        /// the window opens exactly where its frame was dropped, fully inside the screen at every resolution and UI scale.</summary>
        public static Vector2 CommsPlace(float defX, float defY, float w, float h)
        {
            Load();
            int i = (int)OzPanel.Comms;
            if (!has[i]) return new Vector2(defX, defY);
            float cwZ = Screen.height > 0 ? Screen.width * OzLayout.RefH / Screen.height : OzLayout.RefW;
            return new Vector2(frac[i].x * Mathf.Max(0f, cwZ - w), frac[i].y * Mathf.Max(0f, OzLayout.RefH - h));
        }

        /// <summary>Drag: put the group's top-left corner at this point (from the canvas's top-left corner, y down). Clamped fully
        /// inside the canvas. Kept in memory; Save writes it.</summary>
        public static void SetTopLeft(OzPanel p, Vector2 topLeft)
        {
            Load(); CanvasSize(out float cw, out float ch);
            var b = Box(p, cw); float tx = cw - b.w, ty = ch - b.h;
            frac[(int)p] = new Vector2(tx > 0.5f ? Mathf.Clamp01(topLeft.x / tx) : 0f, ty > 0.5f ? Mathf.Clamp01(topLeft.y / ty) : 0f);
            has[(int)p] = true; Version++;
        }

        /// <summary>Checks: put a group at a fraction of its free travel (in memory).</summary>
        public static void SetFraction(OzPanel p, Vector2 f)
        {
            Load();
            frac[(int)p] = new Vector2(Mathf.Clamp01(f.x), Mathf.Clamp01(f.y)); has[(int)p] = true; Version++;
        }

        /// <summary>Checks: back to the default place (in memory).</summary>
        public static void Clear(OzPanel p) { Load(); has[(int)p] = false; Version++; }

        /// <summary>Write one group's place (or remove its entry when it stands at the default place). Saved at once.</summary>
        public static void Save(OzPanel p)
        {
            Load();
            int i = (int)p; string key = PrefPrefix + p;
            if (has[i]) PlayerPrefs.SetString(key, frac[i].x.ToString("R", CultureInfo.InvariantCulture) + ";" + frac[i].y.ToString("R", CultureInfo.InvariantCulture));
            else PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }

        /// <summary>RESET LAYOUT: every group back to its default place, every entry removed, saved at once.</summary>
        public static void ResetAll()
        {
            Load();
            for (int i = 0; i < Count; i++) { has[i] = false; PlayerPrefs.DeleteKey(PrefPrefix + (OzPanel)i); }
            PlayerPrefs.Save();
            Version++;
        }

        /// <summary>One line for the logs: "Minimap 0.000;1.000, Command 0.500;0.000 (2 moved)".</summary>
        public static string Describe()
        {
            Load();
            var sb = new System.Text.StringBuilder(); int n = 0;
            for (int i = 0; i < Count; i++)
            {
                if (!has[i]) continue;
                if (n++ > 0) sb.Append(", ");
                sb.Append((OzPanel)i).Append(' ').Append(frac[i].x.ToString("0.000", CultureInfo.InvariantCulture)).Append(';').Append(frac[i].y.ToString("0.000", CultureInfo.InvariantCulture));
            }
            return n == 0 ? "default layout (0 moved)" : sb + " (" + n + " moved)";
        }
    }
}
