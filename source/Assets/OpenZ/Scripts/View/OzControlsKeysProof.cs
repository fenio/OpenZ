#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ
{
    /// <summary>
    /// Editor checks for the speed keys, the key names and the two controls screens (never in a build). Menu OpenZ / Proofs / H8 H10:
    ///  1 key names and speed keys by layout (pure rules, edit or Play mode);
    ///  2 show the real screens as on a Ukrainian / French / German layout (then look at CONTROLS, the pause menu, F1, the HUD keys);
    ///  3 rectangles of every controls table that is on screen now (Play mode: open CONTROLS first);
    ///  4 the speed keys in a running match (Play mode, the Game view has the keyboard).
    /// Every routine ends with one line "[H8H10] ... N violations"; pass = 0.
    /// </summary>
    public class OzControlsKeysProof : MonoBehaviour
    {
        const string Tag = "[H8H10] ";

        // ================================================================== 1. names
        static readonly Key[] Row1 = { Key.Q, Key.W, Key.E, Key.R, Key.T, Key.Y, Key.U, Key.I, Key.O, Key.P };
        static readonly Key[] Row2 = { Key.A, Key.S, Key.D, Key.F, Key.G, Key.H, Key.J, Key.K, Key.L };
        static readonly Key[] Row3 = { Key.Z, Key.X, Key.C, Key.V, Key.B, Key.N, Key.M };

        static Dictionary<Key, string> Letters(string r1, string r2, string r3)
        {
            var d = new Dictionary<Key, string>();
            for (int i = 0; i < Row1.Length; i++) d[Row1[i]] = r1[i].ToString();
            for (int i = 0; i < Row2.Length; i++) d[Row2[i]] = r2[i].ToString();
            for (int i = 0; i < Row3.Length; i++) d[Row3[i]] = r3[i].ToString();
            return d;
        }

        static Dictionary<Key, string> Layout(string name)
        {
            Dictionary<Key, string> d;
            switch (name)
            {
                case "UA":
                    d = Letters("йцукенгшщз", "фівапролд", "ячсмить");
                    d[Key.LeftBracket] = "х"; d[Key.RightBracket] = "ї"; d[Key.Semicolon] = "ж"; d[Key.Quote] = "є"; d[Key.Comma] = "б"; d[Key.Period] = "ю";
                    d[Key.Backquote] = "'"; d[Key.Minus] = "-"; d[Key.Equals] = "="; d[Key.Slash] = "."; d[Key.Backslash] = "\\";
                    return d;
                case "FR":
                    d = Letters("azertyuiop", "qsdfghjkl", "wxcvbn,");
                    d[Key.Semicolon] = "m"; d[Key.Minus] = ")"; d[Key.Equals] = "="; d[Key.LeftBracket] = "ACCENT CIRCONFLEXE"; d[Key.RightBracket] = "$";
                    d[Key.Comma] = ";"; d[Key.Period] = ":"; d[Key.Slash] = "!"; d[Key.Quote] = "ù"; d[Key.Backquote] = "²"; d[Key.Backslash] = "*";
                    return d;
                case "DE":
                    d = Letters("qwertzuiop", "asdfghjkl", "yxcvbnm");
                    d[Key.Minus] = "ß"; d[Key.Equals] = "AKUT"; d[Key.LeftBracket] = "ü"; d[Key.RightBracket] = "+"; d[Key.Semicolon] = "ö"; d[Key.Quote] = "ä";
                    d[Key.Backquote] = "ZIRKUMFLEX"; d[Key.Backslash] = "#"; d[Key.Comma] = ","; d[Key.Period] = "."; d[Key.Slash] = "-";
                    return d;
                default:
                    d = Letters("qwertyuiop", "asdfghjkl", "zxcvbnm");
                    d[Key.Minus] = "-"; d[Key.Equals] = "="; d[Key.LeftBracket] = "["; d[Key.RightBracket] = "]"; d[Key.Semicolon] = ";"; d[Key.Quote] = "'";
                    d[Key.Backquote] = "`"; d[Key.Backslash] = "\\"; d[Key.Comma] = ","; d[Key.Period] = "."; d[Key.Slash] = "/";
                    return d;
            }
        }

        static bool Plain(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (!(c >= ' ' && c < (char)127) && !(c >= 'À' && c <= 'ɏ')) return false;
            return true;
        }

        [MenuItem("OpenZ/Proofs/H8 H10/1 Key names and speed keys by layout")]
        public static void Names()
        {
            int n = 0, bad = 0; var sb = new StringBuilder();
            void Check(bool ok, string what) { n++; if (!ok) { bad++; sb.AppendLine("  FAIL " + what); } }

            // every key of the input package has a whole name by its place: capitals, digits, signs, nothing cut
            foreach (Key k in Enum.GetValues(typeof(Key)))
            {
                string p = OzKeyNames.PositionName(k);
                Check(Plain(p) && p == p.ToUpperInvariant() && p.Trim() == p && p.Length <= 20, $"place name of {k} = '{p}'");
            }
            // letters: Latin layouts keep their own label, another script falls back to the place
            var us = Layout("US"); var ua = Layout("UA"); var fr = Layout("FR"); var de = Layout("DE");
            for (Key k = Key.A; k <= Key.Z; k++)
            {
                string place = OzKeyNames.PositionName(k);
                Check(OzKeyNames.Name(k, us[k]) == place, $"US {k}");
                Check(OzKeyNames.Name(k, ua[k]) == place, $"UA {k}: '{ua[k]}' must show '{place}', shows '{OzKeyNames.Name(k, ua[k])}'");
                Check(OzKeyNames.Name(k, null) == place && OzKeyNames.Name(k, "") == place, $"no label {k}");
                Check(OzKeyNames.Name(k, fr[k]) == fr[k].ToUpperInvariant(), $"FR {k}: '{fr[k]}' shows '{OzKeyNames.Name(k, fr[k])}'");
                Check(OzKeyNames.Name(k, de[k]) == de[k].ToUpperInvariant(), $"DE {k}: '{de[k]}' shows '{OzKeyNames.Name(k, de[k])}'");
            }
            Check(OzKeyNames.Name(Key.A, "α") == "A" && OzKeyNames.Name(Key.A, "ש") == "A" && OzKeyNames.Name(Key.A, "ش") == "A", "Greek / Hebrew / Arabic fall back to the place");
            Check(OzKeyNames.Name(Key.LeftBracket, "х") == "[" && OzKeyNames.Name(Key.Semicolon, "ж") == ";" && OzKeyNames.Name(Key.Quote, "є") == "'", "UA punctuation keys by place");
            Check(OzKeyNames.Name(Key.Semicolon, "ö") == "Ö" && OzKeyNames.Name(Key.Semicolon, "m") == "M" && OzKeyNames.Name(Key.Equals, "AKUT") == "=", "DE / FR punctuation keys, a dead key by place");
            Check(OzKeyNames.Name(Key.Space, "пробіл") == "SPACE" && OzKeyNames.Name(Key.F10, "х") == "F10" && OzKeyNames.Name(Key.Digit1, "&") == "1" && OzKeyNames.Name(Key.NumpadPlus, "х") == "NUM +", "special keys and the digit row are the same on any layout");
            Check(OzKeyNames.PositionName(Key.NumpadMultiply) == "NUM *" && OzKeyNames.PositionName(Key.LeftBracket) == "[" && OzKeyNames.PositionName(Key.ScrollLock) == "SCROLL LOCK", "names that used to be cut after 10 letters");
            bool keep = OzKeyNames.LatinLayoutLabels; OzKeyNames.LatinLayoutLabels = false;
            Check(OzKeyNames.Name(Key.A, "q") == "A" && OzKeyNames.Name(Key.Z, "y") == "Z", "switch off: every key by its place");
            OzKeyNames.LatinLayoutLabels = keep;

            // every bound action under every layout: only Latin text, and no Cyrillic letter ever
            foreach (string lay in new[] { "US", "UA", "FR", "DE" })
            {
                var map = Layout(lay);
                OzKeyNames.LabelOverride = k => map.TryGetValue(k, out var v) ? v : null;
                foreach (var d in OzBindings.Defs)
                {
                    string cap = OzBindings.Cap(OzBindings.KeyOf(d.action));
                    Check(Plain(cap), $"{lay}: {d.action} shows '{cap}'");
                }
            }
            OzKeyNames.LabelOverride = null;

            // no two keys share a name on any layout; a layout of another script shows every key by its place (review: the Ukrainian
            // / key prints "." and the . key fell back to "." as well, the same for ' on the two keys left of Enter and of 1)
            foreach (string lay in new[] { "US", "UA", "FR", "DE" })
            {
                var map = Layout(lay);
                OzKeyNames.LabelOverride = k => map.TryGetValue(k, out var v) ? v : null;
                var shown = new Dictionary<string, Key>();
                foreach (Key k in Enum.GetValues(typeof(Key)))
                {
                    if (!OzKeyNames.IsLabelKey(k)) continue;
                    string nm = OzKeyNames.Name(k);
                    if (shown.TryGetValue(nm, out Key other) && other != k) Check(false, $"{lay}: the keys {other} and {k} both show '{nm}'");
                    else Check(true, "");
                    shown[nm] = k;
                }
                if (lay == "UA")
                    Check(OzKeyNames.Name(Key.Slash) == "/" && OzKeyNames.Name(Key.Period) == "." && OzKeyNames.Name(Key.Backquote) == "`" && OzKeyNames.Name(Key.Quote) == "'" && OzKeyNames.Name(Key.Backslash) == "\\",
                          $"UA: every key by its place, shows / '{OzKeyNames.Name(Key.Slash)}' . '{OzKeyNames.Name(Key.Period)}' ` '{OzKeyNames.Name(Key.Backquote)}' ' '{OzKeyNames.Name(Key.Quote)}'");
                if (lay == "FR") Check(OzKeyNames.Name(Key.A) == "Q" && OzKeyNames.Name(Key.Semicolon) == "M" && OzKeyNames.Name(Key.M) == ",", "FR: a Latin layout keeps its own labels");
            }
            OzKeyNames.LabelOverride = null;

            // speed keys: the label wins, the two keys right of 0 by place, the numpad always, never a letter or a digit
            void Dir(string lay, Key k, string lab, int want) => Check(OzKeyNames.SpeedDir(k, lab) == want, $"speed {lay} {k} '{lab}' must be {want}, is {OzKeyNames.SpeedDir(k, lab)}");
            Dir("US", Key.Minus, "-", -1); Dir("US", Key.Equals, "=", 1); Dir("US", Key.NumpadPlus, "+", 1); Dir("US", Key.NumpadMinus, "-", -1);
            Dir("UA", Key.Minus, "-", -1); Dir("UA", Key.Equals, "=", 1); Dir("UA", Key.RightBracket, "ї", 0); Dir("UA", Key.Slash, ".", 0);
            Dir("DE", Key.Minus, "ß", -1); Dir("DE", Key.Equals, "AKUT", 1); Dir("DE", Key.RightBracket, "+", 1); Dir("DE", Key.Slash, "-", -1);
            Dir("FR", Key.Minus, ")", -1); Dir("FR", Key.Equals, "=", 1); Dir("FR", Key.Digit6, "-", 0);
            Dir("SE", Key.Minus, "+", 1); Dir("SE", Key.Equals, "AKUT", 1); Dir("SE", Key.Slash, "-", -1);
            Dir("BE", Key.Equals, "-", -1); Dir("BE", Key.Minus, ")", -1); Dir("BE", Key.Slash, "=", 1);
            Dir("CZ", Key.Minus, "=", 1); Dir("CZ", Key.Slash, "-", -1);
            Dir("any", Key.Minus, null, -1); Dir("any", Key.Equals, "", 1); Dir("any", Key.A, "+", 0); Dir("any", Key.Space, "-", 0); Dir("any", Key.Comma, ",", 0);

            // the speed steps are the pause menu's, in rising order
            var sp = OpenZPauseMenu.Speeds; bool rising = sp.Length >= 2; for (int i = 1; i < sp.Length; i++) rising &= sp[i] > sp[i - 1] + 0.01f;
            Check(rising && Array.IndexOf(sp, 1f) >= 0, "speed steps rise and hold x1");
            // + and - cannot be given to an action
            Check(OzBindings.Reserved(Key.Minus) != null && OzBindings.Reserved(Key.Equals) != null && OzBindings.Reserved(Key.NumpadPlus) != null && OzBindings.Reserved(Key.NumpadMinus) != null, "the four speed keys are refused in CONTROLS");
            Debug.Log($"{Tag}NAMES: {n} checks, {bad} violations\n{sb}");
        }

        [MenuItem("OpenZ/Proofs/H8 H10/2 Show the screens as on a Ukrainian layout")] static void AsUa() => Simulate("UA");
        [MenuItem("OpenZ/Proofs/H8 H10/2 Show the screens as on a French layout")] static void AsFr() => Simulate("FR");
        [MenuItem("OpenZ/Proofs/H8 H10/2 Show the screens as on a German layout")] static void AsDe() => Simulate("DE");
        [MenuItem("OpenZ/Proofs/H8 H10/2 Show the screens with the real layout again")] static void AsReal() => Simulate(null);
        static void Simulate(string lay)
        {
            if (lay == null) OzKeyNames.LabelOverride = null;
            else { var map = Layout(lay); OzKeyNames.LabelOverride = k => map.TryGetValue(k, out var v) ? v : null; }
            OzBindings.Reload();                                                              // the main menu table and the F1 list draw again; the pause table and the HUD keys with the next level start
            Debug.Log($"{Tag}key labels now as on {(lay ?? "the real")} layout: " + OzBindings.Summary());
        }

        // ================================================================== 3. rectangles of the controls tables on screen
        struct Box { public string name; public Rect r; }

        static Rect World(RectTransform rt) { var c = new Vector3[4]; rt.GetWorldCorners(c); return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y); }

        static bool Ink(TMP_Text t, out Rect r)
        {
            r = default;
            if (!t || string.IsNullOrEmpty(t.text)) return false;
            t.ForceMeshUpdate();
            var b = t.textBounds; Vector3 a = t.transform.TransformPoint(b.min), z = t.transform.TransformPoint(b.max);
            r = Rect.MinMaxRect(Mathf.Min(a.x, z.x), Mathf.Min(a.y, z.y), Mathf.Max(a.x, z.x), Mathf.Max(a.y, z.y));
            return r.width > 0.01f && r.height > 0.01f;
        }

        static bool Meet(Rect a, Rect b, float slack) => a.xMin < b.xMax - slack && b.xMin < a.xMax - slack && a.yMin < b.yMax - slack && b.yMin < a.yMax - slack;
        static bool Inside(Rect a, Rect b, float slack) => a.xMin >= b.xMin - slack && a.xMax <= b.xMax + slack && a.yMin >= b.yMin - slack && a.yMax <= b.yMax + slack;

        static int CheckTable(RectTransform p, bool full, StringBuilder log)
        {
            var boxes = new List<Box>(); int rows = 0, bad = 0;
            Rect panel = World(p), screen = new Rect(0, 0, Screen.width, Screen.height);
            float want = full ? 14f : 11f;
            if (!Inside(panel, screen, 0.5f)) { bad++; log.AppendLine($"  CUT the panel {panel} leaves the screen {Screen.width}x{Screen.height}"); }
            foreach (Transform ch in p)
            {
                if (!ch.gameObject.activeInHierarchy || !(ch is RectTransform rt)) continue;
                string n = ch.name;
                if (n == "Key" || n.StartsWith("Key_"))
                {
                    Rect cap = World(rt); boxes.Add(new Box { name = n, r = cap });
                    var kt = ch.Find("Key") ? ch.Find("Key").GetComponent<TMP_Text>() : null;
                    if (Ink(kt, out var ki) && !Inside(ki, cap, 0.5f)) { bad++; log.AppendLine($"  OVERFLOW the text '{kt.text}' leaves its key cap"); }
                }
                else if (n.StartsWith("What") || n.StartsWith("Sec_") || n == "Sep" || n == "Title" || n == "Sub" || n == "Foot" || n == "Status")
                {
                    var t = ch.GetComponent<TMP_Text>();
                    if (!Ink(t, out var r)) continue;
                    boxes.Add(new Box { name = n + " '" + t.text + "'", r = r });
                    if (n.StartsWith("What"))
                    {
                        rows++;
                        if (t.fontSize < want - 0.01f || t.overflowMode == TextOverflowModes.Ellipsis) { bad++; log.AppendLine($"  SHRUNK '{t.text}' is drawn at {t.fontSize:0.0} u, not {want:0} u"); }
                    }
                }
                else if (n == "HeadRule" || n == "FootRule" || n.Contains("ResetKeys")) boxes.Add(new Box { name = n, r = World(rt) });
            }
            for (int i = 0; i < boxes.Count; i++)
            {
                if (!Inside(boxes[i].r, panel, 0.5f)) { bad++; log.AppendLine($"  CUT {boxes[i].name} leaves the panel"); }
                for (int j = i + 1; j < boxes.Count; j++)
                    if (Meet(boxes[i].r, boxes[j].r, 0.5f)) { bad++; log.AppendLine($"  OVERLAP {boxes[i].name} and {boxes[j].name}"); }
            }
            log.AppendLine($"  {(full ? "main menu CONTROLS" : "compact table")} under '{(p.parent ? p.parent.name : "-")}/{p.name}': {rows} rows, {boxes.Count} rectangles, panel {panel.width:0} x {panel.height:0} px, {bad} violations");
            return bad;
        }

        [MenuItem("OpenZ/Proofs/H8 H10/3 Controls rectangles of the open screens")]
        static void RectsMenu() => Rects();

        /// <summary>Every controls table that is on screen now. Returns the number of violations (-1: no table is open).</summary>
        public static int Rects()
        {
            if (!Application.isPlaying) { Debug.LogWarning(Tag + "RECTS: enter Play mode and open a CONTROLS screen first"); return -1; }
            Canvas.ForceUpdateCanvases();
            var log = new StringBuilder(); int bad = 0, tables = 0; var seen = new HashSet<Transform>();
            foreach (var rt in FindObjectsByType<RectTransform>(FindObjectsSortMode.None))
            {
                bool full = rt.name == "FootRule", compact = rt.name == "Band0";
                if (!(full || compact) || !rt.gameObject.activeInHierarchy || !rt.parent || !seen.Add(rt.parent)) continue;
                tables++; bad += CheckTable((RectTransform)rt.parent, full, log);
            }
            string head = $"{Tag}RECTS {Screen.width}x{Screen.height}, interface scale {OzText.UiScale * 100f:0} %: {tables} table(s), {bad} violations";
            if (tables == 0) { Debug.LogWarning(head + " (no table on screen: open CONTROLS in the main menu, or Esc then CONTROLS, or F1 in a match)"); return -1; }
            Debug.Log(head + "\n" + log);
            return bad;
        }

        // ================================================================== 4. the speed keys in a match
        [MenuItem("OpenZ/Proofs/H8 H10/4 Speed keys in a running match")]
        public static void SpeedKeys()
        {
            if (!Application.isPlaying || !ZGame.I || !OpenZHud.I) { Debug.LogWarning(Tag + "SPEED: start a level and wait for the HUD first"); return; }
            new GameObject("OzControlsKeysProof").AddComponent<OzControlsKeysProof>().StartCoroutine("Run");
        }

        int n, bad; readonly StringBuilder sb = new StringBuilder();
        void Check(bool ok, string what) { n++; if (!ok) { bad++; sb.AppendLine("  FAIL " + what); } }

        static IEnumerator Tap(params Key[] keys)
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys)); yield return null; yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); yield return null; yield return null;
        }

        static string Line(float sp) => "SPEED ×" + sp.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>The state line's ink stays inside the clock well with 8 u to spare on each side.</summary>
        bool LineInWell(out string why)
        {
            why = "";
            var top = GameObject.Find("TopStrip"); var well = top ? top.transform.Find("Clock") as RectTransform : null;
            var sub = well ? well.Find("Sub") : null; var t = sub ? sub.GetComponent<TMP_Text>() : null;
            if (!well || !t) { why = "clock not found"; return OpenZHud.MinimalHidden; }       // MINIMAL UI hides the strip: nothing to measure
            if (!Ink(t, out var ink)) { why = "line empty"; return false; }
            Rect w = World(well); float u = w.width / OzLayout.TopClock.w;
            why = $"'{t.text}' {ink.width / u:0.0} u in the {OzLayout.TopClock.w:0} u well";
            return ink.xMin >= w.xMin + 8f * u && ink.xMax <= w.xMax - 8f * u && ink.yMin >= w.yMin && ink.yMax <= w.yMax && t.fontSize >= 11f - 0.01f;
        }

        IEnumerator Run()
        {
            var g = ZGame.I; var hud = OpenZHud.I; var speeds = OpenZPauseMenu.Speeds;
            float wasSpeed = g.gameSpeed; bool hadPref = PlayerPrefs.HasKey(OzSpeedKeys.Pref); float wasPref = PlayerPrefs.GetFloat(OzSpeedKeys.Pref, 1f);
            int top = speeds.Length - 1; bool delivered = true;
            (Key up, Key down, string name)[] pairs = { (Key.Equals, Key.Minus, "main row = and -"), (Key.NumpadPlus, Key.NumpadMinus, "numpad + and -") };
            foreach (var pair in pairs)
            {
                g.gameSpeed = speeds[0]; yield return null; yield return null;
                for (int i = 1; i <= top + 1; i++)
                {   // up through every step, then once more at the top
                    int s0 = OzSpeedKeys.Steps, e0 = OzSpeedKeys.Stops;
                    yield return Tap(pair.up);
                    if (i == 1 && OzSpeedKeys.Steps == s0) { delivered = false; OzSpeedKeys.ProofStep(1); yield return null; }
                    else if (!delivered) { OzSpeedKeys.ProofStep(1); yield return null; }
                    float want = speeds[Mathf.Min(i, top)]; bool stop = i > top;
                    Check(Mathf.Abs(g.gameSpeed - want) < 0.001f, $"{pair.name} up {i}: speed {g.gameSpeed} must be {want}");
                    Check(stop ? OzSpeedKeys.Stops == e0 + 1 && OzSpeedKeys.Steps == s0 : OzSpeedKeys.Steps == s0 + 1 && OzSpeedKeys.Stops == e0, $"{pair.name} up {i}: {(stop ? "one end stop" : "one step")} counted");
                    Check(OzSpeedKeys.ProofLine == Line(want) && OzSpeedKeys.NoticeFresh, $"{pair.name} up {i}: the line must read '{Line(want)}', reads '{OzSpeedKeys.ProofLine}'");
                    Check(Mathf.Abs(PlayerPrefs.GetFloat(OzSpeedKeys.Pref, -1f) - want) < 0.001f, $"{pair.name} up {i}: the saved speed follows");
                    yield return null; Check(LineInWell(out string why), $"{pair.name} up {i}: the line inside the clock well ({why})");
                }
                for (int i = top - 1; i >= -1; i--)
                {   // down through every step, then once more at the bottom
                    int s0 = OzSpeedKeys.Steps, e0 = OzSpeedKeys.Stops;
                    yield return Tap(pair.down);
                    if (!delivered) { OzSpeedKeys.ProofStep(-1); yield return null; }
                    float want = speeds[Mathf.Max(i, 0)]; bool stop = i < 0;
                    Check(Mathf.Abs(g.gameSpeed - want) < 0.001f, $"{pair.name} down to {want}: speed {g.gameSpeed}");
                    Check(stop ? OzSpeedKeys.Stops == e0 + 1 && OzSpeedKeys.Steps == s0 : OzSpeedKeys.Steps == s0 + 1 && OzSpeedKeys.Stops == e0, $"{pair.name} down to {want}: {(stop ? "one end stop" : "one step")} counted");
                    Check(OzSpeedKeys.ProofLine == Line(want), $"{pair.name} down to {want}: the line reads '{OzSpeedKeys.ProofLine}'");
                    yield return null; Check(LineInWell(out string why), $"{pair.name} down to {want}: the line inside the clock well ({why})");
                }
            }
            // back to x1: the line says SPEED x1 for the notice time, then leaves
            g.gameSpeed = speeds[Array.IndexOf(speeds, 1f) - 1]; yield return null;
            OzSpeedKeys.ProofStep(1); yield return null;
            Check(Mathf.Abs(g.gameSpeed - 1f) < 0.001f && OzSpeedKeys.ProofLine == Line(1f), "at x1 the line reads SPEED x1 while the notice is fresh");
            yield return new WaitForSecondsRealtime(OzSpeedKeys.NoticeSeconds + 0.2f);
            Check(OzSpeedKeys.ProofLine == "" && !OzSpeedKeys.NoticeFresh, "at x1 the line is empty after the notice time");
            // no effect: Ctrl held, Alt held, the pause menu open
            float before = g.gameSpeed; int st = OzSpeedKeys.Steps + OzSpeedKeys.Stops;
            if (delivered)
            {
                yield return Tap(Key.LeftCtrl, Key.Equals); Check(g.gameSpeed == before, "Ctrl + the plus key changes nothing");
                yield return Tap(Key.LeftAlt, Key.Minus); Check(g.gameSpeed == before, "Alt + the minus key changes nothing");
            }
            var pause = FindFirstObjectByType<OpenZPauseMenu>();
            if (pause)
            {
                pause.Open(); yield return null; yield return null;
                if (delivered) yield return Tap(Key.NumpadPlus);
                Check(hud.Modal && !OzSpeedKeys.ProofStep(1) && g.gameSpeed == before && OzSpeedKeys.Steps + OzSpeedKeys.Stops == st, "no effect while the pause menu is open");
                pause.Close(); yield return new WaitForSecondsRealtime(0.4f);
            }
            else Check(false, "pause menu not found");
            // put everything back
            g.gameSpeed = wasSpeed; if (hadPref) PlayerPrefs.SetFloat(OzSpeedKeys.Pref, wasPref); else PlayerPrefs.DeleteKey(OzSpeedKeys.Pref);
            PlayerPrefs.Save();
            Debug.Log($"{Tag}SPEED {Screen.width}x{Screen.height}, interface scale {OzText.UiScale * 100f:0} %: {n} checks, {bad} violations; real key events {(delivered ? "reached the game (all four keys proven through the input path)" : "did NOT reach the game: click into the Game view and run again (the steps themselves were proven through the same code)")}\n{sb}");
            Destroy(gameObject);
        }
    }
}
#endif
