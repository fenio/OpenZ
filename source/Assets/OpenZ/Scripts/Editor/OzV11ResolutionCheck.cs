using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of the RESOLUTION row of OPTIONS (the rule: View/OzSettings.cs; the row: the main menu's and a level's
    /// Options screen). Editor only. Menu: OpenZ/V1.1/Check Resolution Setting (f22, Play mode), or
    /// OpenZ.EditorTools.OzV11ResolutionCheck.Run(). It runs the part that fits the loaded scene and ends with one log line
    /// "[OpenZ resolution setting] PASS n/n checks (...)" or "... FAIL ...". That line is added to checks.txt, every check's own
    /// line is written to checks_main_menu.txt / checks_level.txt, in Folder (by default the newest v1_1* folder under
    ///
    /// The editor never changes the screen (the game applies a step in the Windows build only; here a step is saved and logged).
    /// So this check counts what can be counted here: the rule, the saved value, the row in both Options screens, and the HUD
    /// at three Game view sizes (1920x1080, 2560x1600, 1280x720), which it sets itself and puts back.
    ///
    /// In every scene, the rule (7 checks):
    ///  R1  the steps of 12 displays: NATIVE first, then every standard height below the display's own, tallest first
    ///  R2  every step has the display's own shape (width even, within one pixel of height x the display's shape), none taller
    ///      than the display, none under 720
    ///  R3  NATIVE asks for the display's own size on all 12 (the size the game asked for before the row existed)
    ///  R4  no saved value, 0, and a value the display does not offer read NATIVE; a step it offers is read as saved
    ///  R5  this display: the right arrow walks down every step, each saved at once (oz_res_h) and shown as "W x H"; the last
    ///      step stops
    ///  R6  the left arrow walks back up to NATIVE, which stops
    ///  R7  the line of saved settings says "resolution NATIVE", and "resolution WxH" at a step
    /// Per Game view size, the Options screen of the scene (6 checks):
    ///  S   the Game view has the size
    ///  O1  on the MAIN tab the row stands directly under FULLSCREEN and over HUD SCALE (main menu) / under FULLSCREEN and over UI SCALE (in a
    ///      level)
    ///  O2  the row is inside the plate, the plate inside the screen; the row is as far under its neighbour as the rows above
    ///      are from each other, with the same edges
    ///  O3  the value reads NATIVE and the display's size between the two arrows, at its full size, not cut, clear of the label
    ///  O4  the row has the RESOLUTION hint
    ///  O5  the row steps by click: the right arrow one step down, the left arrow back to NATIVE (main menu: the menu's own
    ///      click on the arrows; level: the row's own click, shift-click and right-click)
    /// In a level, per size, the HUD (2 checks, and 1 more at the last size):
    ///  H1  a HUD scale the player never set follows the size: it is the default of this size
    ///  H2  the HUD's own layout check at this size: every panel rectangle inside the screen at all 9 places
    ///  H3  (last size) a saved HUD scale stays as it is
    /// Pictures (for_owner): menu_options_WxH.png, level_WxH.png, level_options_WxH.png.
    /// The saved resolution, the saved HUD scale, the HUD's scale, the Game view size and the open menus are put back when it
    /// ends, also when a step throws or Play stops.
    /// </summary>
    public static class OzV11ResolutionCheck
    {
        const string Tag = "[OpenZ resolution setting]";
        const string RunKey = "OpenZ.V11.ResolutionCheck.startedAt";                                          // SessionState: a run is going (it survives a script reload, a static field does not)
        const float MaxSeconds = 180f;
        public static string Report = "not run";
        /// <summary></summary>
        public static string Folder;
        static readonly int[][] Sizes = { new[] { 1920, 1080 }, new[] { 2560, 1600 }, new[] { 1280, 720 } };
        static readonly int[][] Displays =
        {
            new[] { 3840, 2160 }, new[] { 2560, 1600 }, new[] { 2560, 1440 }, new[] { 1920, 1200 }, new[] { 1920, 1080 }, new[] { 3440, 1440 },
            new[] { 5120, 1440 }, new[] { 2880, 1800 }, new[] { 1600, 900 }, new[] { 1366, 768 }, new[] { 1280, 1024 }, new[] { 1280, 720 },
        };
        static readonly int[] Standard = { 2160, 1800, 1600, 1440, 1200, 1080, 900, 720 };

        static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        static int waitFrame, ok, bad, pictures, gv0 = -1, res0;
        static float waitUntil, scale0, uiScale0;
        static readonly StringBuilder fails = new StringBuilder(), lines = new StringBuilder();
        static string dir, sizeInfo = "";
        static bool kept, hadRes, hadScale, sizeOk;
        static readonly Vector3[] corners = new Vector3[4];

        [MenuItem("OpenZ/V1.1/Check Resolution Setting (f22, Play mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning(Tag + " enter Play mode first (the main menu, or a level)"); return; }
            float started = SessionState.GetFloat(RunKey, -1f);
            if (stack.Count > 0 || (started >= 0f && EditorApplication.timeSinceStartup - started < MaxSeconds)) { Debug.LogWarning(Tag + " a run is still going"); return; }
            SessionState.SetFloat(RunKey, (float)EditorApplication.timeSinceStartup);
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) EditorWindow.FocusWindowIfItsOpen(gv);
            ok = bad = pictures = 0; fails.Clear(); lines.Clear(); Report = "running";
            dir = OutDir(); Directory.CreateDirectory(Path.Combine(dir, "for_owner"));
            var menu = Object.FindFirstObjectByType<OpenZMainMenu>(); var hud = OpenZHud.I;
            if (hud && hud.Canvas && hud.W != null) stack.Push(Level(hud));
            else if (menu) stack.Push(Menu(menu));
            else { Report = Tag + " FAIL: no HUD and no main menu in this scene"; Debug.LogError(Report); SessionState.EraseFloat(RunKey); return; }
            waitFrame = 0; waitUntil = 0f; EditorApplication.update += Step;
        }

        static string OutDir()
        {
            if (!string.IsNullOrEmpty(Folder)) return Path.GetFullPath(Folder);
            string all = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones")), stage = "v1_1";
            if (Directory.Exists(all))
            {
                var ds = Directory.GetDirectories(all, "v1_1*");
                System.Array.Sort(ds, System.StringComparer.Ordinal);
                if (ds.Length > 0) stage = Path.GetFileName(ds[ds.Length - 1]);
            }
            return Path.Combine(all, stage, "f22_resolution_setting");
        }

        // a step yields an int (frames), a float (seconds of real time: the pause menu stops the game clock) or another step
        static void Step()
        {
            if (stack.Count == 0 || !EditorApplication.isPlaying) { End(); return; }
            if (Time.frameCount < waitFrame || Time.realtimeSinceStartup < waitUntil) return;
            var top = stack.Peek(); bool more;
            try { more = top.MoveNext(); }
            catch (System.Exception ex) { Debug.LogException(ex); Report = Tag + " FAIL: exception " + ex.Message; Debug.LogError(Report); End(); return; }
            if (!more) { stack.Pop(); if (stack.Count == 0) End(); return; }
            if (top.Current is IEnumerator child) stack.Push(child);
            else if (top.Current is float s) waitUntil = Time.realtimeSinceStartup + s;
            else waitFrame = Time.frameCount + (top.Current is int n ? n : 1);
        }

        static void End() { stack.Clear(); PutBack(); EditorApplication.update -= Step; SessionState.EraseFloat(RunKey); }

        static void Check(bool pass, string what)
        {
            if (pass) ok++; else { bad++; fails.Append("\n  FAIL ").Append(what); }
            lines.Append(pass ? "PASS  " : "FAIL  ").AppendLine(what);
        }

        static void Finish(string where, string detail)
        {
            PutBack();
            bool pass = bad == 0 && ok > 0;
            Report = $"{Tag} {(pass ? "PASS" : "FAIL")} {ok}/{ok + bad} checks ({where}): {detail}; {pictures} pictures in {Path.Combine(dir, "for_owner").Replace('\\', '/')}{(bad == 0 ? "" : "; FAILED:" + fails)}";
            if (pass) Debug.Log(Report); else Debug.LogError(Report);
            try
            {
                File.AppendAllText(Path.Combine(dir, "checks.txt"), Report + "\n");
                File.WriteAllText(Path.Combine(dir, "checks_" + (where.StartsWith("level") ? "level" : "main_menu") + ".txt"), lines + Report + "\n");
            }
            catch (System.Exception ex) { Debug.LogWarning(Tag + " could not write the check files: " + ex.Message); }
        }

        // ------------------------------------------------------------------ what the check changes, and puts back
        static void Keep()
        {
            hadRes = PlayerPrefs.HasKey(OzSettings.ResPref); res0 = PlayerPrefs.GetInt(OzSettings.ResPref, 0);
            hadScale = PlayerPrefs.HasKey("oz_ui_scale"); scale0 = PlayerPrefs.GetFloat("oz_ui_scale", 1f); uiScale0 = OzText.UiScale;
            gv0 = OzFinalGateView.SelectedIndex(); kept = true;
        }

        static void PutBack()
        {
            if (!kept) return;
            kept = false;
            if (hadRes) PlayerPrefs.SetInt(OzSettings.ResPref, res0); else PlayerPrefs.DeleteKey(OzSettings.ResPref);
            if (hadScale) PlayerPrefs.SetFloat("oz_ui_scale", scale0); else PlayerPrefs.DeleteKey("oz_ui_scale");
            PlayerPrefs.Save();
            OzFinalGateView.Select(gv0);
            if (!Application.isPlaying) return;
            var hud = OpenZHud.I;
            if (hud && hud.Canvas)
            {
                if (hud.Pause && hud.Pause.IsOpen) hud.Pause.Close();
                if (!Mathf.Approximately(OzText.UiScale, uiScale0)) { OzText.UiScale = uiScale0; hud.ApplyUiScale(); }
            }
            var menu = Object.FindFirstObjectByType<OpenZMainMenu>();
            if (menu) { menu.ProofOptTip(null); if (menu.ProofSide == "Options") menu.ProofOpen("none"); }
        }

        // ------------------------------------------------------------------ helpers
        static float U => Screen.height / 1080f;                                                           // screen pixels per unit of the two menus' canvases (both 1080 u tall)
        static string Plain(string s) => Regex.Replace(s ?? "", "<[^>]+>", "");
        static string Size(Vector2Int s) => s.x + " x " + s.y;
        static string Value(string name) => "◀  " + name + "  ▶";

        static Rect Px(RectTransform rt)
        {
            rt.GetWorldCorners(corners);                                                                    // overlay canvas: world = screen pixels
            return Rect.MinMaxRect(Mathf.Min(corners[0].x, corners[2].x), Mathf.Min(corners[0].y, corners[2].y), Mathf.Max(corners[0].x, corners[2].x), Mathf.Max(corners[0].y, corners[2].y));
        }

        static bool Inside(Rect a, Rect b, float slack = 0.75f) => a.xMin >= b.xMin - slack && a.xMax <= b.xMax + slack && a.yMin >= b.yMin - slack && a.yMax <= b.yMax + slack;

        /// <summary>The box around the letters of a text as they are drawn (false: no text, hidden, nothing drawn).</summary>
        static bool Drawn(TMP_Text t, out Rect r)
        {
            r = default;
            if (!t || !t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text)) return false;
            t.ForceMeshUpdate();
            var info = t.textInfo; bool any = false; Vector2 lo = Vector2.zero, hi = Vector2.zero;
            for (int i = 0; i < info.characterCount && i < info.characterInfo.Length; i++)
            {
                if (!info.characterInfo[i].isVisible) continue;
                Vector3 a = t.transform.TransformPoint(info.characterInfo[i].bottomLeft), z = t.transform.TransformPoint(info.characterInfo[i].topRight);
                Vector2 p = new Vector2(Mathf.Min(a.x, z.x), Mathf.Min(a.y, z.y)), q = new Vector2(Mathf.Max(a.x, z.x), Mathf.Max(a.y, z.y));
                lo = any ? Vector2.Min(lo, p) : p; hi = any ? Vector2.Max(hi, q) : q; any = true;
            }
            r = Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
            return any && r.width > 0.01f && r.height > 0.01f;
        }

        sealed class Row { public string label; public HudButton row; public OzText value; public int tab; }

        /// <summary>The OPTIONS rows of a menu, from its own row list (both menus keep one, with the same members).</summary>
        static List<Row> Rows(object menu)
        {
            var l = new List<Row>();
            var f = menu.GetType().GetField("toggles", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null || !(f.GetValue(menu) is IEnumerable list)) return l;
            foreach (var t in list)
            {
                var ty = t.GetType(); object tab = ty.GetField("tab")?.GetValue(t);
                var r = new Row { label = ty.GetField("label")?.GetValue(t) as string, row = ty.GetField("row")?.GetValue(t) as HudButton, value = ty.GetField("value")?.GetValue(t) as OzText, tab = tab is int n ? n : -1 };
                if (r.row != null && r.row.Rt) l.Add(r);
            }
            return l;
        }

        static IEnumerator SetSize(int w, int h)
        {
            OzFinalGateView.SetFixed(w, h, "OpenZ " + w + "x" + h, out string info);
            float t0 = Time.realtimeSinceStartup;
            while ((Screen.width != w || Screen.height != h) && Time.realtimeSinceStartup - t0 < 4f) yield return 1;
            yield return 0.5f;
            sizeOk = Screen.width == w && Screen.height == h; sizeInfo = info;
        }

        static IEnumerator Shot(string name)
        {
            string path = Path.Combine(dir, "for_owner", name + "_" + Screen.width + "x" + Screen.height + ".png");
            try { if (File.Exists(path)) File.Delete(path); } catch (System.Exception) { }
            ScreenCapture.CaptureScreenshot(path);
            float t0 = Time.realtimeSinceStartup;
            while (!File.Exists(path) && Time.realtimeSinceStartup - t0 < 3f) yield return 1;
            if (File.Exists(path)) pictures++; else Debug.LogWarning(Tag + " picture not written: " + path);
            yield return 2;
        }

        // ------------------------------------------------------------------ the rule (every scene)
        static void RuleChecks()
        {
            // R1 - R3: the steps of 12 displays
            int listsOk = 0, sizes = 0, sizesOk = 0, nativeOk = 0; var shown = new StringBuilder();
            foreach (var d in Displays)
            {
                var n = new Vector2Int(d[0], d[1]); var steps = OzSettings.ResolutionSteps(n.y);
                var want = new List<int> { 0 }; foreach (int h in Standard) if (h < n.y) want.Add(h);
                bool same = steps.Length == want.Count; for (int i = 0; same && i < steps.Length; i++) same = steps[i] == want[i];
                if (same) listsOk++;
                if (OzSettings.ResolutionSize(n, 0) == n) nativeOk++;
                shown.Append("\n      ").Append(Size(n)).Append(": NATIVE");
                for (int i = 1; i < steps.Length; i++)
                {
                    var s = OzSettings.ResolutionSize(n, steps[i]); sizes++;
                    if (s.y == steps[i] && s.y < n.y && s.y >= 720 && s.x <= n.x && s.x % 2 == 0 && Mathf.Abs(s.x - (float)((double)n.x * s.y / n.y)) <= 1.001f) sizesOk++;
                    shown.Append(", ").Append(Size(s));
                }
            }
            Check(listsOk == Displays.Length, $"R1 NATIVE first, then every standard height below the display's own, tallest first: {listsOk} of {Displays.Length} displays{shown}");
            Check(sizesOk == sizes && sizes > 0, $"R2 every step has the display's own shape, width even, none taller than the display, none under 720: {sizesOk} of {sizes} steps");
            Check(nativeOk == Displays.Length, $"R3 NATIVE asks for the display's own size: {nativeOk} of {Displays.Length} displays");

            // R4: what a saved value reads as
            var here = OzSettings.NativeResolution(); var mine = OzSettings.ResolutionSteps(here.y);
            int cases = 0, casesOk = 0; var said = new StringBuilder();
            void Reads(string what, bool has, int saved, int nativeH, int want)
            {
                if (has) PlayerPrefs.SetInt(OzSettings.ResPref, saved); else PlayerPrefs.DeleteKey(OzSettings.ResPref);
                int got = OzSettings.ResolutionHeight(nativeH); cases++; if (got == want) casesOk++;
                said.Append(said.Length > 0 ? "; " : "").Append(what).Append(" = ").Append(got == 0 ? "NATIVE" : got.ToString());
            }
            Reads("nothing saved", false, 0, here.y, 0);
            Reads("0", true, 0, here.y, 0);
            Reads("the display's own height", true, here.y, here.y, 0);
            Reads("1440 saved, a display 1080 tall", true, 1440, 1080, 0);
            Reads("1000 (no standard height)", true, 1000, 2160, 0);
            Reads("-5", true, -5, 2160, 0);
            Reads("1440 saved, a display 2160 tall", true, 1440, 2160, 1440);
            Reads("720 saved, a display 768 tall", true, 720, 768, 720);
            Check(casesOk == cases, $"R4 a saved value that does not fit reads NATIVE, one that fits is kept: {casesOk} of {cases} ({said})");

            // R5, R6: the row's arrows on this display
            PlayerPrefs.DeleteKey(OzSettings.ResPref);
            bool start = OzSettings.ResolutionName == "NATIVE (" + Size(here) + ")";
            int down = 0, up = 0; var walk = new StringBuilder("NATIVE (" + Size(here) + ")");
            for (int i = 1; i < mine.Length; i++)
            {
                OzSettings.StepResolution(1);
                string name = OzSettings.ResolutionName, want = Size(OzSettings.ResolutionSize(here, mine[i]));
                if (PlayerPrefs.GetInt(OzSettings.ResPref, -1) == mine[i] && name == want && OzSettings.ResolutionWanted(here) == OzSettings.ResolutionSize(here, mine[i])) down++;
                walk.Append(", ").Append(name);
            }
            OzSettings.StepResolution(1);
            bool stopLow = PlayerPrefs.GetInt(OzSettings.ResPref, 0) == mine[mine.Length - 1];
            Check(start && down == mine.Length - 1 && stopLow, $"R5 this display, {Size(here)}: the right arrow walks down {down} of {mine.Length - 1} steps, each saved at once and shown as written; the last one stops ({stopLow}). The row's values: {walk}");
            for (int i = mine.Length - 2; i >= 0; i--)
            {
                OzSettings.StepResolution(-1);
                if (PlayerPrefs.GetInt(OzSettings.ResPref, -1) == mine[i]) up++;
            }
            OzSettings.StepResolution(-1);
            bool stopTop = PlayerPrefs.GetInt(OzSettings.ResPref, -1) == 0 && OzSettings.ResolutionName == "NATIVE (" + Size(here) + ")" && OzSettings.ResolutionWanted(here) == here;
            Check(up == mine.Length - 1 && stopTop, $"R6 the left arrow walks back up {up} of {mine.Length - 1} steps to NATIVE, which stops and asks for {Size(here)} again ({stopTop})");

            // R7: the line of saved settings
            string atNative = OzSettings.Describe(); bool lineNative = atNative.Contains("resolution NATIVE,"), lineStep = true; string word = "";
            if (mine.Length > 1)
            {
                OzSettings.StepResolution(1); var s = OzSettings.ResolutionSize(here, mine[1]); word = "resolution " + s.x + "x" + s.y + ",";
                lineStep = OzSettings.Describe().Contains(word);
                OzSettings.StepResolution(-1);
            }
            Check(lineNative && lineStep, $"R7 the line of saved settings says \"resolution NATIVE\" ({lineNative}){(mine.Length > 1 ? " and \"" + word.TrimEnd(',') + "\" at the first step (" + lineStep + ")" : "; this display offers NATIVE only")}");
        }

        // ------------------------------------------------------------------ the row on an Options screen
        /// <summary>O1 to O4 for the row list of a menu whose MAIN tab is open. plate = the Options plate; above / below = the
        /// labels the row must stand between. Returns the row (null when it is missing).</summary>
        static Row RowChecks(string at, object menu, RectTransform plate, string above, string below, string tip)
        {
            var here = OzSettings.NativeResolution();
            var rows = Rows(menu).FindAll(r => r.tab == OzSettingTips.TabMain && r.row.Rt.gameObject.activeInHierarchy);
            rows.Sort((a, b) => Px(b.row.Rt).yMax.CompareTo(Px(a.row.Rt).yMax));                              // top to bottom
            int i = rows.FindIndex(r => r.label == OzSettings.ResLabel);
            var names = new List<string>(); foreach (var r in rows) names.Add(r.label);
            Check(i > 0 && rows[i - 1].label == above && i + 1 < rows.Count && rows[i + 1].label == below,
                  $"{at} O1 the row stands directly under {above} and over {below} (MAIN tab, top to bottom: {string.Join(" | ", names)})");
            if (i < 0) { Check(false, $"{at} O2 to O5: no {OzSettings.ResLabel} row on this screen"); return null; }
            var me = rows[i]; Rect mr = Px(me.row.Rt), pr = plate ? Px(plate) : default, screen = new Rect(0, 0, Screen.width, Screen.height);
            float step = i > 0 ? Px(rows[i - 1].row.Rt).yMax - mr.yMax : 0f, stepAbove = i > 1 ? Px(rows[i - 2].row.Rt).yMax - Px(rows[i - 1].row.Rt).yMax : step;
            bool edges = i > 0 && Mathf.Abs(Px(rows[i - 1].row.Rt).xMin - mr.xMin) <= 1f && Mathf.Abs(Px(rows[i - 1].row.Rt).xMax - mr.xMax) <= 1f && Mathf.Abs(Px(rows[i - 1].row.Rt).height - mr.height) <= 1f;
            Check(plate && Inside(mr, pr) && Inside(pr, screen, 1f) && edges && Mathf.Abs(step - stepAbove) <= 1.5f && step > mr.height,
                  $"{at} O2 the row is inside the plate ({Inside(mr, pr)}: {(pr.yMax - mr.yMax) / U:0} u under the plate's top, {(mr.yMin - pr.yMin) / U:0} u over its bottom), the plate inside the screen ({Inside(pr, screen, 1f)}), "
                  + $"the row {step / U:0.0} u under its neighbour (the rows above: {stepAbove / U:0.0} u), same edges and height ({edges})");
            var vt = me.value != null && me.value.rectTransform ? me.value.rectTransform.GetComponent<TMP_Text>() : null;
            var lt = me.row.Content ? me.row.Content.Find("Label") : null; var ltx = lt ? lt.GetComponent<TMP_Text>() : null;
            string text = me.value != null ? Plain(me.value.text) : "", wantText = Value("NATIVE (" + Size(here) + ")");
            Rect vr = default, lr = default; bool vInk = Drawn(vt, out vr), lInk = Drawn(ltx, out lr);
            bool full = vt && me.value != null && vt.fontSize >= me.value.Size - 0.01f && vt.overflowMode != TextOverflowModes.Ellipsis;
            float gap = vInk && lInk ? (vr.xMin - lr.xMax) / U : -999f;
            Check(text == wantText && full && vInk && lInk && Inside(vr, mr) && gap >= 12f,
                  $"{at} O3 the value reads \"{text}\" (wanted \"{wantText}\"), at its full size ({full}{(vt ? ": " + vt.fontSize.ToString("0.0") + " of " + me.value.Size.ToString("0.0") : "")}), "
                  + $"inside the row ({vInk && Inside(vr, mr)}), {gap:0} u right of the label's last letter");
            Check(tip != null && tip.StartsWith("RESOLUTION|") && tip.Contains("NATIVE is the sharpest") && tip == OzSettingTips.For(OzSettings.ResLabel), $"{at} O4 the row's hint: \"{tip ?? "none"}\"");
            return me;
        }

        // ------------------------------------------------------------------ the main menu
        static IEnumerator Menu(OpenZMainMenu menu)
        {
            Keep();
            RuleChecks();
            var here = OzSettings.NativeResolution(); var mine = OzSettings.ResolutionSteps(here.y);
            foreach (var s in Sizes)
            {
                string at = s[0] + "x" + s[1];
                yield return SetSize(s[0], s[1]);
                Check(sizeOk, $"{at} S the Game view has this size (it has {Screen.width}x{Screen.height}; {sizeInfo})");
                if (!sizeOk) continue;
                PlayerPrefs.DeleteKey(OzSettings.ResPref);                                                  // as on a computer that never saw the row
                if (menu.ProofView != "Main") { menu.ProofMain(); yield return 1.0f; }
                yield return 1.0f;                                                                          // the menu builds itself again for the new size
                if (menu.ProofSide != "None") { menu.ProofOpen("none"); yield return 0.5f; }
                menu.ProofOpen("options"); yield return 0.8f;
                menu.ProofOptTab(OzSettingTips.TabMain); yield return 4;
                string tip = menu.ProofOptTip(OzSettings.ResLabel); yield return 0.5f;                       // its hint plate shows beside the Options plate, as on a hover
                var me = RowChecks(at, menu, menu.ProofPanel("options"), OzSettings.FullLabel, "HUD SCALE", tip);
                yield return Shot("menu_options");
                if (me == null) continue;
                if (mine.Length < 2) { Check(OzSettings.ResolutionHeight(here.y) == 0, $"{at} O5 this display offers NATIVE only: the arrows keep it"); continue; }
                bool pts = menu.ProofArrowPoints(OzSettings.ResLabel, out var left, out var right);
                string hit = pts ? menu.ProofClickAt(right) : "no arrows"; yield return 4;
                int h1 = PlayerPrefs.GetInt(OzSettings.ResPref, -1); string v1 = Plain(me.value.text), want1 = Value(Size(OzSettings.ResolutionSize(here, mine[1])));
                yield return 0.3f;
                pts &= menu.ProofArrowPoints(OzSettings.ResLabel, out left, out right);                     // the value is narrower now: its arrows moved
                if (pts) menu.ProofClickAt(left);
                yield return 4;
                int h2 = PlayerPrefs.GetInt(OzSettings.ResPref, -1); string v2 = Plain(me.value.text);
                Check(pts && hit == "control" && h1 == mine[1] && v1 == want1 && h2 == 0 && v2 == Value("NATIVE (" + Size(here) + ")"),
                      $"{at} O5 a click on the right arrow steps down (saved {h1}, the row reads \"{v1}\", wanted \"{want1}\"), a click on the left arrow steps back to NATIVE (saved {h2}, \"{v2}\"); the click landed on: {hit}");
                menu.ProofOptTip(null);
            }
            Finish("main menu", $"this display {Size(here)}, {mine.Length} values in the row; the editor saves a step and never changes the screen");
        }

        // ------------------------------------------------------------------ a level
        static IEnumerator Level(OpenZHud hud)
        {
            if (hud.Modal || !hud.hudVisible || hud.W.GameOver || !hud.Pause) { Report = Tag + " FAIL: a running level with the HUD shown and no menu open is needed"; Debug.LogError(Report); yield break; }
            Keep();
            RuleChecks();
            var here = OzSettings.NativeResolution(); var mine = OzSettings.ResolutionSteps(here.y);
            var plate = hud.transform.Find("OpenZ_Pause/Root/SidePanel") as RectTransform;
            for (int k = 0; k < Sizes.Length; k++)
            {
                var s = Sizes[k]; string at = s[0] + "x" + s[1];
                yield return SetSize(s[0], s[1]);
                Check(sizeOk, $"{at} S the Game view has this size (it has {Screen.width}x{Screen.height}; {sizeInfo})");
                if (!sizeOk) continue;
                PlayerPrefs.DeleteKey(OzSettings.ResPref);

                // ---- the HUD at this size
                PlayerPrefs.DeleteKey("oz_ui_scale");                                                       // as for a player who never set the HUD scale
                hud.FollowScreenSize(); yield return 8;                                                     // what the game's display host calls when the screen changed size
                float def = Mathf.Clamp(OzSettings.DefaultHudScale(Screen.width, Screen.height), OzText.ScaleMin, OzText.ScaleMax);
                Check(Mathf.Abs(OzText.UiScale - def) < 0.001f, $"{at} H1 a HUD scale the player never set follows the size: {Mathf.RoundToInt(OzText.UiScale * 100)} %, the default of this size is {Mathf.RoundToInt(def * 100)} %");
                string layout = hud.ArrangeProof(); yield return 4;
                Check(layout.EndsWith("PASS"), $"{at} H2 {layout}");
                if (k == Sizes.Length - 1)
                {
                    PlayerPrefs.SetFloat("oz_ui_scale", 1.3f); OzText.UiScale = 1.3f; hud.ApplyUiScale(); yield return 4;   // what the UI SCALE row does
                    hud.FollowScreenSize(); yield return 4;
                    Check(Mathf.Abs(OzText.UiScale - 1.3f) < 0.001f, $"{at} H3 a saved HUD scale stays as the player set it: saved 130 %, now {Mathf.RoundToInt(OzText.UiScale * 100)} %");
                    PlayerPrefs.DeleteKey("oz_ui_scale"); hud.FollowScreenSize(); yield return 8;
                }
                yield return Shot("level");

                // ---- the Options screen of the level
                hud.Pause.Open(); hud.Pause.ProofOptTab(OzSettingTips.TabMain);
                yield return 0.8f;
                if (!hud.Pause.IsOpen) { Check(false, $"{at} O1 to O5: the pause menu did not open"); continue; }
                Row found = Rows(hud.Pause).Find(r => r.label == OzSettings.ResLabel);
                var me = RowChecks(at, hud.Pause, plate, OzSettings.FullLabel, "UI SCALE", found != null ? found.row.Tooltip : null);
                yield return Shot("level_options");
                if (me != null && mine.Length < 2) Check(OzSettings.ResolutionHeight(here.y) == 0, $"{at} O5 this display offers NATIVE only: the arrows keep it");
                else if (me != null && me.row.OnClick != null && me.row.OnRightClick != null)
                {
                    var m = Mouse.current; int dir = OzSettings.ArrowDir(me.value, m != null ? m.position.ReadValue() : default);   // the same question the row's click asks (and it keeps its answer for an unmoved pointer)
                    me.row.OnClick(default); yield return 4;
                    int h1 = PlayerPrefs.GetInt(OzSettings.ResPref, -1), want1 = dir > 0 ? mine[1] : 0; string v1 = Plain(me.value.text);
                    if (h1 != mine[1]) { PlayerPrefs.DeleteKey(OzSettings.ResPref); OzSettings.StepResolution(1); yield return 4; }   // the pointer rested on the left arrow: one step down by the rule itself
                    me.row.OnClick(new HudClick { shift = true }); yield return 4;
                    int h2 = PlayerPrefs.GetInt(OzSettings.ResPref, -1);
                    OzSettings.StepResolution(1); yield return 2;
                    me.row.OnRightClick(new HudClick { right = true }); yield return 4;
                    int h3 = PlayerPrefs.GetInt(OzSettings.ResPref, -1); string v3 = Plain(me.value.text);
                    Check(h1 == want1 && h2 == 0 && h3 == 0 && v3 == Value("NATIVE (" + Size(here) + ")") && (dir < 0 || v1 == Value(Size(OzSettings.ResolutionSize(here, mine[1])))),
                          $"{at} O5 the row's own click steps {(dir > 0 ? "down" : "towards NATIVE (the pointer rests on its left arrow)")} (saved {h1}, the row read \"{v1}\"), a shift-click steps back (saved {h2}), a right-click steps back (saved {h3}, \"{v3}\")");
                }
                else if (me != null) Check(false, $"{at} O5 the row has no click");
                hud.Pause.Close();
                yield return 0.6f;
            }
            Finish("level " + hud.game.level, $"this display {Size(here)}, {mine.Length} values in the row; the editor saves a step and never changes the screen");
        }
    }
}
