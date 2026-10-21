using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace OpenZ
{
    /// <summary>
    /// Game speed on the keyboard: + faster, - slower (the two keys right of 0, any key the layout labels + / = / -, and the
    /// numpad + and -: OzKeyNames.SpeedDir). The steps are the GAME SPEED buttons of the pause menu (OpenZPauseMenu.Speeds), one
    /// step per press, stopping at both ends; the buttons follow because they show whatever ZGame.gameSpeed is.
    ///  - Notice: the HUD clock's own state line (where SPEED xN already stands while the speed is not 1) shows the new speed for
    ///    NoticeSeconds, bright then normal; at x1 it shows SPEED x1 and fades out; at an end stop it turns to the limit colour
    ///    of the unit counters. Nothing new is drawn anywhere else.
    ///  - Sound: a step is answered by the game's speed cue (the audio director plays ui_game_speed whenever the speed changes,
    ///    also for the pause menu buttons), an end stop by ui_disabled (the HUD's "nothing happens" click).
    ///  - No effect in the menus, during the briefing, while the game is paused (the pause menu has the buttons), after the
    ///    match has ended, over a question (YES / NO), with Ctrl or Alt held, while a text field has the keyboard, and while the
    ///    capture tool is on (it has its own speed keys). A key the player has bound to an action keeps that action only.
    /// Lives for the whole run (made once after the first scene); view side only, the rules never read a key.
    /// </summary>
    public sealed class OzSpeedKeys : MonoBehaviour
    {
        public const float NoticeSeconds = 1.5f, NoticeFade = 0.3f, SaveDelay = 2f;
        public const string StepSound = "ui_game_speed", StopSound = "ui_disabled", Pref = "oz_speed";

        /// <summary>The main block keys that can carry + / = / - on a layout (the numpad pair is read by name).</summary>
        static readonly Key[] LabelKeys = { Key.Minus, Key.Equals, Key.LeftBracket, Key.RightBracket, Key.Semicolon, Key.Quote, Key.Backquote, Key.Backslash, Key.Slash };

        static OzSpeedKeys inst;
        static float noticeAt = -100f, saveAt = -1f, labelFor = -1f;
        static bool noticeStop;
        static string label = "", lastLayout;

        /// <summary>Checks: steps taken and end stops hit since the start.</summary>
        public static int Steps, Stops;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (inst) return;
            var go = new GameObject("OzSpeedKeys"); DontDestroyOnLoad(go); inst = go.AddComponent<OzSpeedKeys>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            WatchLayout(kb);
            if (saveAt > 0f && Time.unscaledTime >= saveAt) { saveAt = -1f; PlayerPrefs.Save(); }
            if (kb == null) return;
            int dir = 0;
            if (kb.numpadPlusKey.wasPressedThisFrame) dir++;
            if (kb.numpadMinusKey.wasPressedThisFrame) dir--;
            for (int i = 0; i < LabelKeys.Length; i++)
            {
                var c = kb[LabelKeys[i]];
                if (c == null || !c.wasPressedThisFrame || Bound(LabelKeys[i])) continue;
                dir += OzKeyNames.SpeedDir(LabelKeys[i], c.displayName);
            }
            if (dir == 0 || kb.ctrlKey.isPressed || kb.altKey.isPressed || OzTrackpadScroll.CmdHeld(kb)) return;   // Ctrl / Cmd + + / - zoom instead (OzTrackpadScroll.cs)
            if (CanStep(out var game)) Step(game, dir > 0 ? 1 : -1);
        }

        /// <summary>The player switched the input language: the key caps that show a layout label are drawn again.</summary>
        static void WatchLayout(Keyboard kb)
        {
            string id = kb != null ? kb.keyboardLayout : null;
            if (id == lastLayout) return;
            bool first = lastLayout == null;
            lastLayout = id;
            if (!first) OzBindings.Reload();
        }

        /// <summary>The player gave this key to an action in CONTROLS: it does that and nothing else.</summary>
        static bool Bound(Key k)
        {
            var defs = OzBindings.Defs;
            for (int i = 0; i < defs.Length; i++) if (OzBindings.KeyOf(defs[i].action) == k) return true;
            return false;
        }

        /// <summary>A running match with nothing in front of it.</summary>
        static bool CanStep(out ZGame game)
        {
            game = ZGame.I;
            var hud = OpenZHud.I;
            if (!game || !game.isActiveAndEnabled || game.World == null) return false;                // menus, loading, the briefing (the sim waits)
            if (!hud || !hud.isActiveAndEnabled || !hud.Canvas) return false;                          // the HUD is not up yet
            if (game.World.GameOver || game.paused || hud.Modal || OzConfirm.Blocks) return false;     // the end, the pause menu, a question on screen
            if (CaptureKit.Enabled || OzControlsTable.Capturing) return false;                         // the capture tool's own speed keys; a key being bound
            var es = EventSystem.current; var sel = es ? es.currentSelectedGameObject : null;
            if (sel && (sel.GetComponent<TMPro.TMP_InputField>() || sel.GetComponent<UnityEngine.UI.InputField>())) return false;   // a text field has the keyboard
            return true;
        }

        static void Step(ZGame game, int dir)
        {
            var speeds = OpenZPauseMenu.Speeds;
            float cur = game.gameSpeed; int j = -1;
            if (dir > 0) { for (int n = 0; n < speeds.Length; n++) if (speeds[n] > cur + 0.01f) { j = n; break; } }
            else { for (int n = speeds.Length - 1; n >= 0; n--) if (speeds[n] < cur - 0.01f) { j = n; break; } }
            noticeAt = Time.unscaledTime;
            if (j < 0)
            {   // already at that end: the line answers in the limit colour, the HUD's "nothing happens" click
                noticeStop = true; Stops++;
                OzSfx.Ui(StopSound);
                OzSoundLog.Add("hud", "speed", StopSound, "end stop at " + Label(cur));
                return;
            }
            float sp = speeds[j];
            game.gameSpeed = sp;                                                                       // the audio director hears the change and plays ui_game_speed
            PlayerPrefs.SetFloat(Pref, sp); saveAt = Time.unscaledTime + SaveDelay;                    // the same key the pause menu buttons write
            noticeStop = false; Steps++;
            OzSoundLog.Add("hud", "speed", StepSound, (dir > 0 ? "faster: " : "slower: ") + Label(sp));
        }

        static string Label(float sp)
        {
            if (!Mathf.Approximately(sp, labelFor)) { labelFor = sp; label = "SPEED ×" + sp.ToString("0.##", CultureInfo.InvariantCulture); }
            return label;
        }

        // ------------------------------------------------------------------ the HUD clock's state line (OpenZHud)
        /// <summary>True for NoticeSeconds after a speed key.</summary>
        public static bool NoticeFresh => Time.unscaledTime - noticeAt < NoticeSeconds;

        /// <summary>The state line under the clock digits. busy = GAME OVER or PAUSED stands there (kept as it is). Otherwise the
        /// speed while it is not 1 (as before, now with a point as the decimal mark on every system) and for NoticeSeconds after a
        /// speed key also at 1.</summary>
        public static string ClockLine(string line, ZGame game, bool busy)
        {
            if (busy || !game) return line;
            float sp = game.gameSpeed;
            if (Mathf.Abs(sp - 1f) <= 0.01f && !NoticeFresh) return "";
            return Label(sp);
        }

        /// <summary>The line's colour while the notice is fresh: bright amber after a step, the limit colour at an end stop, both
        /// easing to the line's normal amber; at x1 (the line leaves afterwards) it fades out over the last NoticeFade seconds.</summary>
        public static Color NoticeColor
        {
            get
            {
                float age = Time.unscaledTime - noticeAt, k = Mathf.Clamp01(age / NoticeSeconds);
                Color c = Color.Lerp(noticeStop ? (Color)OzUi.Signal : (Color)OzUi.AmberHi, (Color)OzUi.Amber, k * k);
                var g = ZGame.I;
                if (g && Mathf.Abs(g.gameSpeed - 1f) <= 0.01f) c.a *= Mathf.Clamp01((NoticeSeconds - age) / NoticeFade);
                return c;
            }
        }

        // ------------------------------------------------------------------ checks
        /// <summary>One step as a key would give it (same conditions). False = the keys have no effect now.</summary>
        public static bool ProofStep(int dir) { if (!CanStep(out var g)) return false; Step(g, dir > 0 ? 1 : -1); return true; }
        /// <summary>What the clock's state line shows now for the speed (empty at x1 once the notice is over).</summary>
        public static string ProofLine => ClockLine("", ZGame.I, false);
    }
}
