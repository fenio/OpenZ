using System.Text;
using UnityEngine.InputSystem;

namespace OpenZ
{
    /// <summary>
    /// The one place where a key becomes text: the CONTROLS screens, the HUD key caps, the tooltips and the messages all ask
    /// OzBindings.Cap, and that asks Name. The game reads every key by its place on the keyboard (the Input System's Key), so a
    /// hotkey works on any keyboard layout; only the printed name followed the active layout and came out in another script.
    ///  - A key that types no character (Space, Enter, the F keys, the numpad, the arrows ...) has one fixed name everywhere.
    ///  - A letter or punctuation key shows what the active layout prints on it when that is one Latin character, in capitals
    ///    (a French or German keyboard keeps showing the key the player has to press).
    ///  - Any other label (another script, a dead key, none) falls back to the key's name by its place: the Latin letter
    ///    printed on the same key of those keyboards.
    ///  - A layout that prints another script on its letter keys (Cyrillic, Greek, Hebrew, Arabic ...) shows EVERY key by its
    ///    place, also the few keys on which it prints a Latin sign: on a Ukrainian or Russian layout the / key prints "." while
    ///    the . key (a Cyrillic letter) falls back to ".", so mixing the two rules would put the same text on two keys.
    /// The digit row is always 1 .. 0 (the group keys), whatever the layout types there.
    /// </summary>
    public static class OzKeyNames
    {
        /// <summary>True: a Latin layout shows its own labels (see above). False: every key by its place, the same text on any layout.</summary>
        public static bool LatinLayoutLabels = true;

#if UNITY_EDITOR
        /// <summary>Checks only: stands in for the layout's own label, so the screens can be looked at as on another layout.</summary>
        public static System.Func<Key, string> LabelOverride;
#endif

        /// <summary>The text for a key on the active layout.</summary>
        public static string Name(Key k) => Name(k, IsLabelKey(k) && LatinLayout() ? LayoutLabel(k) : null);

        /// <summary>The text for a key whose layout prints `layoutLabel` on it (no device is asked: the rule itself).</summary>
        public static string Name(Key k, string layoutLabel)
        {
            string pos = PositionName(k);
            if (!LatinLayoutLabels || !IsLabelKey(k) || string.IsNullOrEmpty(layoutLabel)) return pos;
            string t = layoutLabel.Trim();
            if (t.Length != 1) return pos;
            char c = char.ToUpperInvariant(t[0]);
            return IsLatinLabel(c) ? c.ToString() : pos;
        }

        /// <summary>A letter or punctuation key: the keys whose printed label changes with the layout.</summary>
        public static bool IsLabelKey(Key k)
        {
            if (k >= Key.A && k <= Key.Z) return true;
            switch (k)
            {
                case Key.Backquote: case Key.Quote: case Key.Semicolon: case Key.Comma: case Key.Period: case Key.Slash: case Key.Backslash:
                case Key.LeftBracket: case Key.RightBracket: case Key.Minus: case Key.Equals:
                case Key.OEM1: case Key.OEM2: case Key.OEM3: case Key.OEM4: case Key.OEM5:
                    return true;
            }
            return false;
        }

        /// <summary>A, 7, ; and the like, and Latin letters with marks; nothing from another script.</summary>
        static bool IsLatinLabel(char c)
        {
            if (c > ' ' && c < (char)127) return true;
            return c >= 'À' && c <= 'ɏ' && char.IsLetter(c);
        }

        static string LayoutLabel(Key k)
        {
#if UNITY_EDITOR
            if (LabelOverride != null) return LabelOverride(k);
#endif
            var kb = Keyboard.current; if (kb == null) return null;
            try { var c = kb[k]; return c != null ? c.displayName : null; }
            catch (System.Exception) { return null; }
        }

        static int latinFrame = -1; static string latinFor; static bool latin = true;

        /// <summary>False when the active layout prints a letter of another script on one of its 26 letter keys. Asked once a frame
        /// (the labels are the keyboard's own cached texts); the checks' stand-in table is never cached.</summary>
        static bool LatinLayout()
        {
#if UNITY_EDITOR
            if (LabelOverride != null) return LatinLetters();
#endif
            int frame = UnityEngine.Time.frameCount; string id = LayoutId;
            if (frame == latinFrame && id == latinFor) return latin;
            latinFrame = frame; latinFor = id; latin = LatinLetters();
            return latin;
        }

        static bool LatinLetters()
        {
            for (Key k = Key.A; k <= Key.Z; k++)
            {
                string t = LayoutLabel(k);
                if (string.IsNullOrEmpty(t)) continue;
                t = t.Trim();
                if (t.Length == 1 && char.IsLetter(t[0]) && !IsLatinLabel(char.ToUpperInvariant(t[0]))) return false;
            }
            return true;
        }

        /// <summary>The name of the active keyboard layout (it changes when the player switches the input language), or null.</summary>
        public static string LayoutId { get { var kb = Keyboard.current; return kb != null ? kb.keyboardLayout : null; } }

        /// <summary>The key by its place on the keyboard: Latin capitals, the same on any layout.</summary>
        public static string PositionName(Key k)
        {
            switch (k)
            {
                case Key.None: return "-";
                case Key.Space: return "SPACE";
                case Key.Enter: return "ENTER";
                case Key.Tab: return "TAB";
                case Key.Backquote: return "`";
                case Key.Quote: return "'";
                case Key.Semicolon: return ";";
                case Key.Comma: return ",";
                case Key.Period: return ".";
                case Key.Slash: return "/";
                case Key.Backslash: return "\\";
                case Key.LeftBracket: return "[";
                case Key.RightBracket: return "]";
                case Key.Minus: return "-";
                case Key.Equals: return "=";
                case Key.LeftShift: return "LEFT SHIFT";
                case Key.RightShift: return "RIGHT SHIFT";
                case Key.LeftAlt: return "LEFT ALT";
                case Key.RightAlt: return "RIGHT ALT";
                case Key.LeftCtrl: return "LEFT CTRL";
                case Key.RightCtrl: return "RIGHT CTRL";
                case Key.LeftMeta: return "LEFT WIN";
                case Key.RightMeta: return "RIGHT WIN";
                case Key.ContextMenu: return "MENU";
                case Key.Escape: return "ESC";
                case Key.LeftArrow: return "LEFT";
                case Key.RightArrow: return "RIGHT";
                case Key.UpArrow: return "UP";
                case Key.DownArrow: return "DOWN";
                case Key.Backspace: return "BACKSPACE";
                case Key.PageDown: return "PG DN";
                case Key.PageUp: return "PG UP";
                case Key.Home: return "HOME";
                case Key.End: return "END";
                case Key.Insert: return "INS";
                case Key.Delete: return "DEL";
                case Key.CapsLock: return "CAPS";
                case Key.NumLock: return "NUM LOCK";
                case Key.PrintScreen: return "PRT SC";
                case Key.ScrollLock: return "SCROLL LOCK";
                case Key.Pause: return "PAUSE";
                case Key.NumpadEnter: return "NUM ENTER";
                case Key.NumpadDivide: return "NUM /";
                case Key.NumpadMultiply: return "NUM *";
                case Key.NumpadPlus: return "NUM +";
                case Key.NumpadMinus: return "NUM -";
                case Key.NumpadPeriod: return "NUM .";
                case Key.NumpadEquals: return "NUM =";
                case Key.OEM1: return "OEM 1";
                case Key.OEM2: return "OEM 2";
                case Key.OEM3: return "OEM 3";
                case Key.OEM4: return "OEM 4";
                case Key.OEM5: return "OEM 5";
            }
            if (k >= Key.A && k <= Key.Z) return ((char)('A' + ((int)k - (int)Key.A))).ToString();
            if (k >= Key.Digit1 && k <= Key.Digit9) return ((char)('1' + ((int)k - (int)Key.Digit1))).ToString();
            if (k == Key.Digit0) return "0";
            if (k >= Key.Numpad0 && k <= Key.Numpad9) return "NUM " + ((int)k - (int)Key.Numpad0);
            if (k >= Key.F1 && k <= Key.F12) return "F" + (1 + (int)k - (int)Key.F1);
            return Spaced(k.ToString());                                                    // a key added by a later input package: its own name, whole, in words
        }

        static string Spaced(string s)
        {
            var sb = new StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (i > 0 && char.IsUpper(c) && char.IsLower(s[i - 1])) sb.Append(' ');
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>The sign keys on which some layouts print a letter (Turkish F: Y on the ; key; French: M on the ; key).</summary>
        static readonly Key[] SignKeys = { Key.Semicolon, Key.Quote, Key.Comma, Key.Period, Key.Slash, Key.Backslash, Key.LeftBracket, Key.RightBracket, Key.Backquote };

        /// <summary>A key went down this frame whose shown name is `letter` (Y for YES is the key that shows Y: on a German
        /// keyboard the one printed Y, on a Cyrillic layout the key with Y next to its own letter).</summary>
        public static bool LetterPressed(Keyboard kb, char letter)
        {
            if (kb == null) return false;
            char want = char.ToUpperInvariant(letter);
            for (Key k = Key.A; k <= Key.Z; k++) if (PressedAs(kb, k, want)) return true;
            for (int i = 0; i < SignKeys.Length; i++) if (PressedAs(kb, SignKeys[i], want)) return true;
            return false;
        }

        static bool PressedAs(Keyboard kb, Key k, char want)
        {
            var c = kb[k];
            if (c == null || !c.wasPressedThisFrame) return false;
            string n = Name(k);
            return n.Length == 1 && n[0] == want;
        }

        /// <summary>The game speed keys. +1 faster, -1 slower, 0 not a speed key. The numpad + and - on every keyboard; on the main
        /// block a key that the layout labels + or = is faster and one it labels - is slower; the two keys right of 0 keep their
        /// place rule (first slower, second faster) when the layout prints something else on them. A label always wins over the
        /// place, so no key ever does the opposite of what is printed on it. Letters and the digit row are never speed keys.</summary>
        public static int SpeedDir(Key k, string layoutLabel)
        {
            if (k == Key.NumpadPlus) return 1;
            if (k == Key.NumpadMinus) return -1;
            if (!IsLabelKey(k) || (k >= Key.A && k <= Key.Z)) return 0;
            string t = string.IsNullOrEmpty(layoutLabel) ? "" : layoutLabel.Trim();
            if (t == "+" || t == "=") return 1;
            if (t == "-") return -1;
            if (k == Key.Equals) return 1;
            if (k == Key.Minus) return -1;
            return 0;
        }
    }
}
