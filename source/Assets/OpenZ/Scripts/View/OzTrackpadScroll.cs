using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace OpenZ
{
    /// <summary>
    /// Options, ADVANCED tab (under TOUCH · GAMEPAD): TRACKPAD SCROLL, a cycle row ZOOM / MOVE MAP, saved as oz_trackpad_scroll,
    /// by accident.
    ///  ZOOM (default)  the scroll zooms exactly as it always did: RtsCamera's wheel code gets the wheel untouched.
    ///  MOVE MAP        the scroll pans the map, both ways (x and y). The ground moves the way a scroll view moves its content, so
    ///                  with the system's natural scrolling (a Mac's default) the map follows the fingers. Smooth (a short glide,
    ///                  GlideSeconds), never faster than MaxScreensPerSecond, metres per pixel scaled with the zoom as the
    ///                  middle-button drag and the two-finger touch drag (RtsCamera.DragPixels). Ctrl + scroll (on a Mac also
    ///                  Cmd + scroll) zooms by as much as was scrolled: one wheel notch = one step of the wheel.
    ///  Every setting   Ctrl + the + / - keys (on a Mac also Cmd + + / -) zoom one wheel step per press; held, on in steps. The
    ///                  + / - keys alone keep changing the game speed (OzSpeedKeys), and with Cmd held they leave the speed alone.
    /// With MOVE MAP, while a menu or a question is in front in a match (pause menu, end screen, ARRANGE INTERFACE, YES / NO) the
    /// camera leaves the scroll alone. The main menu has no map camera; its lists read their own wheel as before.
    /// Called once a frame by RtsCamera.Update where it reads the wheel (the window has focus and the ending does not hold the view).
    /// View and input side only: the rules never read it.
    /// </summary>
    public static class OzTrackpadScroll
    {
        // ------------------------------------------------------------------ the setting
        public const string Label = "TRACKPAD SCROLL", Pref = "oz_trackpad_scroll";
        public const string Tip = "TRACKPAD SCROLL|Zoom (default): scrolling zooms. Move map: two-finger swipe pans the map; zoom with Ctrl/Cmd + scroll, or Ctrl/Cmd and + / -.|";
        public static readonly string[] Names = { "ZOOM", "MOVE MAP" };
        static int setting = -1;

        public static int Setting
        {
            get { if (setting < 0) setting = PlayerPrefs.GetInt(Pref, 0) == OzTrackpadMath.MoveMap ? OzTrackpadMath.MoveMap : OzTrackpadMath.Zoom; return setting; }
            set
            {
                setting = value == OzTrackpadMath.MoveMap ? OzTrackpadMath.MoveMap : OzTrackpadMath.Zoom;
                PlayerPrefs.SetInt(Pref, setting); PlayerPrefs.Save();
                pendX = pendY = 0f; keyHeld = 0f;
                Debug.Log($"[OpenZ Options] trackpad scroll: {Names[setting]} ({Pref} {setting}, saved, live)");
            }
        }
        public static bool MoveMap => Setting == OzTrackpadMath.MoveMap;
        /// <summary>The cycle row's value.</summary>
        public static string Name => Names[Setting];
        /// <summary>The cycle row's step: two values, either arrow flips it.</summary>
        public static void Step(int dir) => Setting = Setting == OzTrackpadMath.MoveMap ? OzTrackpadMath.Zoom : OzTrackpadMath.MoveMap;

        // ------------------------------------------------------------------ tuning
        /// <summary>MOVE MAP: the pan of one wheel notch of scroll, as a part of the screen height (0.09 = 97 px at 1080 lines, about
        /// what a page moves for one notch).</summary>
        public static float PixelsPerNotch = 0.09f;
        /// <summary>MOVE MAP: the glide's time (s): each frame the pan still to go moves 1 - e^(-dt / GlideSeconds) of the way.</summary>
        public static float GlideSeconds = 0.06f;
        /// <summary>MOVE MAP: the fastest pan, screen heights a second (2.5 = about the top speed of the held scroll keys at the
        /// standard zoom), and the most pan still to go after a swipe (screen heights).</summary>
        public static float MaxScreensPerSecond = 2.5f, MaxPendingScreens = 0.75f;
        /// <summary>The zoom keys held: after this many seconds, this many wheel steps a second.</summary>
        public static float KeyRepeatDelay = 0.4f, KeyNotchesPerSecond = 6f;
        /// <summary>MOVE MAP: the direction of the pan per axis (1 = as a scroll view's content; -1 turns that axis round).</summary>
        public static float SignX = 1f, SignY = 1f;

        // ------------------------------------------------------------------ counters for the checks
        public static int PanFrames, ZoomFrames, KeySteps, MenuFrames, Gestures;
        public static float PannedPx, ZoomedNotches;
        /// <summary>The last finished MOVE MAP gesture, as the log line writes it (frames, the scroll's sum and largest frame,
        /// the pan in pixels): the numbers to tune PixelsPerNotch for a trackpad.</summary>
        public static string LastGesture = "";
        /// <summary>The pan still to go (pixels).</summary>
        public static float Pending => Mathf.Sqrt(pendX * pendX + pendY * pendY);

        static float pendX, pendY, keyHeld;
        static RtsCamera lastCam;
        static int gFrames; static float gIdle, gSumX, gSumY, gMax, gPx, gZoom;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            setting = -1; pendX = pendY = keyHeld = 0f; lastCam = null; gFrames = 0;
            PanFrames = ZoomFrames = KeySteps = MenuFrames = Gestures = 0; PannedPx = ZoomedNotches = 0f; LastGesture = "";
        }

        // ------------------------------------------------------------------ platform
        /// <summary>A Mac: Cmd is a zoom key there as well as Ctrl.</summary>
        public static bool IsMac => Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor;
        /// <summary>Cmd (a Mac) / the Windows key is held.</summary>
        public static bool CmdHeld(Keyboard kb) => kb != null && (kb.leftMetaKey.isPressed || kb.rightMetaKey.isPressed);
        /// <summary>How much scroll is one wheel notch: 1 with the input's uniform range (the project's setting), 120 on Windows
        /// where the raw platform range is kept.</summary>
        public static float PerNotch()
        {
            var s = InputSystem.settings;
            bool raw = s != null && s.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange;
            return raw && (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor) ? 120f : 1f;
        }
        /// <summary>A menu or a question is in front of the map in a match.</summary>
        public static bool MenuInFront()
        {
            var hud = OpenZHud.I;
            return (hud && hud.Modal) || OzConfirm.Blocks;
        }

        // ------------------------------------------------------------------ the frame (RtsCamera.Update)
        /// <summary>RtsCamera.Update, where it reads the wheel. Returns the wheel the camera's own code zooms by: ZOOM (default) =
        /// the wheel, untouched; MOVE MAP = 0 (the scroll pans here, Ctrl / Cmd + scroll zooms here). The zoom keys work on every
        /// setting. flying: the camera is on a fly-to (a minimap click, a message, Space).</summary>
        public static float Wheel(RtsCamera cam, Mouse mouse, Keyboard kb, bool flying)
        {
            Vector2 s = mouse != null ? mouse.scroll.ReadValue() : Vector2.zero;
            bool ctrl = kb != null && kb.ctrlKey.isPressed, cmd = CmdHeld(kb);
            float dt = Time.unscaledDeltaTime;
            int dir = 0; bool pressed = false;
            ZoomKeys(kb, ctrl, cmd, ref dir, ref pressed);
            KeyFrame(cam, dir, pressed, dt);
            return Frame(cam, s, ctrl, cmd, flying, dt);
        }

        /// <summary>One frame of scroll (the checks call it too). Returns the wheel for the camera's own zoom code.</summary>
        public static float Frame(RtsCamera cam, Vector2 s, bool ctrl, bool cmd, bool flying, float dt)
        {
            if (!cam) return s.y;
            if (cam != lastCam) { lastCam = cam; pendX = pendY = 0f; gFrames = 0; }
            int set = Setting;
            if (set != OzTrackpadMath.MoveMap) { pendX = pendY = 0f; return s.y; }                    // ZOOM (default): the wheel as before
            var r = OzTrackpadMath.Decide(set, s.x, s.y, ctrl, cmd, IsMac, MenuInFront(), PerNotch());
            float H = Mathf.Max(1, Screen.height);
            switch (r.act)
            {
                case OzTrackpadMath.Act.Pan:
                    pendX += r.dragX * SignX * PixelsPerNotch * H; pendY += r.dragY * SignY * PixelsPerNotch * H;
                    OzTrackpadMath.LimitPending(ref pendX, ref pendY, MaxPendingScreens * H);
                    PanFrames++;
                    break;
                case OzTrackpadMath.Act.ZoomBy:
                    pendX = pendY = 0f;
                    cam.ZoomBy(OzTrackpadMath.ZoomFactor(r.notches, cam.wheelStep));
                    ZoomFrames++; ZoomedNotches += r.notches;
                    break;
                case OzTrackpadMath.Act.MenuHasIt:
                    pendX = pendY = 0f; MenuFrames++;
                    break;
            }
            if (r.act != OzTrackpadMath.Act.Pan && (flying || cam.follow)) pendX = pendY = 0f;          // the glide's tail never ends a fly-to or a follow
            if (pendX != 0f || pendY != 0f)
            {
                OzTrackpadMath.Glide(ref pendX, ref pendY, dt, GlideSeconds, MaxScreensPerSecond * H, out float tx, out float ty);
                if (tx != 0f || ty != 0f)
                {
                    cam.DragPixels(new Vector2(tx, ty));                                                     // metres per pixel from the zoom height, the map limits as always
                    float d = Mathf.Sqrt(tx * tx + ty * ty); PannedPx += d; if (gFrames > 0) gPx += d;
                }
            }
            Gesture(r, s, dt);
            return 0f;
        }

        /// <summary>The zoom keys held now: Ctrl + / - (on a Mac also Cmd), the same keys the game speed reads (the two keys right
        /// of 0, any key the layout labels + / = / -, the numpad + and -); a key bound to an action in CONTROLS is left to it.</summary>
        static readonly Key[] LabelKeys = { Key.Minus, Key.Equals, Key.LeftBracket, Key.RightBracket, Key.Semicolon, Key.Quote, Key.Backquote, Key.Backslash, Key.Slash };
        static void ZoomKeys(Keyboard kb, bool ctrl, bool cmd, ref int dir, ref bool pressed)
        {
            if (kb == null || !(ctrl || (IsMac && cmd)) || kb.altKey.isPressed || !KeysAllowed()) return;
            int sum = 0;
            Key1(kb.numpadPlusKey, 1, ref sum, ref pressed);
            Key1(kb.numpadMinusKey, -1, ref sum, ref pressed);
            for (int i = 0; i < LabelKeys.Length; i++)
            {
                var c = kb[LabelKeys[i]];
                if (c == null || !c.isPressed || Bound(LabelKeys[i])) continue;
                Key1(c, OzKeyNames.SpeedDir(LabelKeys[i], c.displayName), ref sum, ref pressed);
            }
            dir = sum > 0 ? 1 : sum < 0 ? -1 : 0;
            if (dir == 0) pressed = false;
        }
        static void Key1(KeyControl c, int d, ref int sum, ref bool pressed)
        {
            if (c == null || d == 0 || !c.isPressed) return;
            sum += d; if (c.wasPressedThisFrame) pressed = true;
        }
        static bool Bound(Key k)
        {
            var defs = OzBindings.Defs;
            for (int i = 0; i < defs.Length; i++) if (OzBindings.KeyOf(defs[i].action) == k) return true;
            return false;
        }
        /// <summary>The zoom keys answer in a match with nothing in front: no menu or question, no key being bound, no text field.</summary>
        static bool KeysAllowed()
        {
            if (MenuInFront() || OzControlsTable.Capturing) return false;
            var es = EventSystem.current; var sel = es ? es.currentSelectedGameObject : null;
            if (sel && (sel.GetComponent<TMPro.TMP_InputField>() || sel.GetComponent<UnityEngine.UI.InputField>())) return false;
            return true;
        }

        /// <summary>The zoom keys' frame (the checks call it too): dir +1 closer, -1 back, 0 none; pressed = went down this frame.</summary>
        public static void KeyFrame(RtsCamera cam, int dir, bool pressed, float dt)
        {
            float n = OzTrackpadMath.KeyNotches(dir, pressed, ref keyHeld, dt, KeyRepeatDelay, KeyNotchesPerSecond);
            if (n == 0f || !cam) return;
            cam.ZoomBy(OzTrackpadMath.ZoomFactor(n, cam.wheelStep));
            if (pressed) KeySteps++;
        }

        /// <summary>MOVE MAP: one log line per finished gesture (half a second without scroll and the glide done): the numbers a
        /// trackpad gives, to tune PixelsPerNotch.</summary>
        static void Gesture(OzTrackpadMath.Result r, Vector2 s, float dt)
        {
            if (r.act == OzTrackpadMath.Act.Pan || r.act == OzTrackpadMath.Act.ZoomBy)
            {
                if (gFrames == 0) { gSumX = gSumY = gMax = gPx = gZoom = 0f; }
                gFrames++; gSumX += s.x; gSumY += s.y; gMax = Mathf.Max(gMax, s.magnitude); gIdle = 0f;
                if (r.act == OzTrackpadMath.Act.ZoomBy) gZoom += r.notches;
                return;
            }
            if (gFrames == 0) return;
            gIdle += dt;
            if (gIdle < 0.5f || pendX != 0f || pendY != 0f) return;
            Gestures++;
            LastGesture = $"{gFrames} frames, scroll sum x {gSumX:0.###} y {gSumY:0.###}, most in one frame {gMax:0.###}, panned {gPx:0} px, zoomed {gZoom:0.##} notches";
            Debug.Log($"[OpenZ Camera] trackpad scroll (MOVE MAP): {LastGesture} (a notch = {PixelsPerNotch * Screen.height:0} px at {Screen.width}x{Screen.height})");
            gFrames = 0;
        }

        /// <summary>One line for the logs and the checks.</summary>
        public static string Describe()
            => $"trackpad scroll {Name}: pan frames {PanFrames} ({PannedPx:0} px), zoom frames {ZoomFrames} ({ZoomedNotches:0.##} notches), zoom key presses {KeySteps}, frames left to a menu {MenuFrames}, gestures {Gestures}";
    }
}
