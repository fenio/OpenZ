#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OpenZ.Sim;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ
{
    /// <summary>
    /// K347, the battlefield screen's deploy bar with the SHUTTLE box (UI/OpenZMainMenu.DeployBar.cs, UI/OpenZMainMenu.ShuttleStart.cs,
    /// UI/OzShuttleStart.cs), checked in Play (editor only: this file compiles to nothing in a player build). Menu OpenZ/V1.1/Shuttle:
    /// K347 Start Box Proof (MainMenu or a level, Play). Every rectangle is the real one (world corners on the overlay canvas, a text's
    /// drawn letters), never a layout number; the block columns come from the bar's own layout (ProofBlockX), turned into pixels.
    ///
    /// In the MainMenu scene (about a minute and a half): the Game view is set to 1920x1080, 2560x1600 and 3840x2160 in turn (the menu
    /// keeps its reference layout at every UI scale); on each, the battlefield screen in seven states: level 16 EASY (the box on), MEDIUM
    /// (on), MEDIUM (off), HARD, level 2 MEDIUM, level 17 EASY and HARD (K444: level 17 has the slot too):
    ///   S  the SHUTTLE slot: live on levels 16 and 17 EASY / MEDIUM (faded in, the row takes clicks), locked on HARD (the disabled row
    ///      "NOT ON HARD"), not there on level 2 (two blocks against DEPLOY)
    ///   M  within level 16 nothing moved (the card, every block's parts, DEPLOY, the header: the same rectangles in its four states), nor
    ///      within level 17 (its two states); on level 2 the card, DEPLOY and the header stay, DIFFICULTY and HEAD START moved by exactly
    ///      one block + one gap
    ///   O  the card, the blocks, the hairlines and DEPLOY inside the bar and apart; the card's glyph, tag, headline and small line inside
    ///      the card and apart; each block's label, key cap, row and hint inside its column and apart; every text at its full size
    ///   L  one line: the labels, key caps, rows and hints of every block on the same y, the blocks the same width, the card's tag and
    ///      small line on the label and hint lines
    ///   T  the words: the card (OBJECTIVE | the headline | n FLAGS · CAPTURE FOR TERRITORY), the DIFFICULTY and HEAD START hints, the
    ///      SHUTTLE row ("ON / PARKED BY YOUR FORT", "OFF / NO SHUTTLE", "NOT ON HARD / EASY · MEDIUM ONLY")
    ///   then (level 16 MEDIUM) a click on the row turns the box off with ui_toggle_off, the key U turns it on with ui_toggle_on (a real
    ///   key event; if it does not reach the game its handler is called and the line says so); on level 2 the key changes nothing and
    ///   answers ui_disabled; on level 16 HARD the key and a click on the locked row change nothing; the SHUTTLE and HEAD START tooltips
    ///   open over their own blocks, inside the screen, above the bar, meeting nothing; the change level 16 -> 2 -> 16 watched frame by
    ///   frame (the moving blocks never over the SHUTTLE block while it shows, the end places exact) and once more in CALM MODE (at once);
    ///   OPTIONS: the ADVANCED tab has no row SHUTTLE any more (SHUTTLE ORDERS may be there).
    /// In a level (a few seconds; start it from the battlefield screen or directly, any difficulty): L1 the shuttle at the start is the
    /// one the rule gives (level 16: there with the box on EASY / MEDIUM, else none, and the scene's ship hidden); L2 the testing
    /// panel's own drop (OzCheatShuttle.Spawn) makes a red and an empty shuttle beside the player's fort, both drawn; L3 the pause menu's
    /// OPTIONS, ADVANCED tab: no row SHUTTLE (pictures at UI 100 % and 160 %). It leaves the two dropped shuttles in the match.
    /// changes (Game view size, the chosen difficulty, the box, HEAD START, the chosen card, CALM MODE, the UI scale) is put back, also
    /// when Play stops.
    /// </summary>
    public sealed class OzShuttleStartProof : MonoBehaviour
    {
        const string Tag = "[K347 shuttle box]";
        public static string Report = "not run";
        static readonly int[][] Sizes = { new[] { 1920, 1080 }, new[] { 2560, 1600 }, new[] { 3840, 2160 } };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Vector3[] corners = new Vector3[4];
        /// <summary>The header parts that never move (names under the select root).</summary>
        static readonly string[] Header = { "Eyebrow", "Title", "Back", "HighScores" };
        /// <summary>The bar's parts that stand in DIFFICULTY's and HEAD START's columns (they slide between levels).</summary>
        static readonly string[] Slides = { "Line0", "CpuLabel", "CpuKey", "CpuEasy", "CpuMedium", "CpuHard", "CpuHint", "HeadLabel", "HeadKey", "HeadRow", "HeadHint" };

        [MenuItem("OpenZ/V1.1/Shuttle: K347 Start Box Proof (MainMenu or a level, Play)")]
        public static void Run()
        {
            if (!Application.isPlaying) { Debug.LogWarning(Tag + " enter Play mode first (the MainMenu scene, or a level)"); return; }
            if (FindFirstObjectByType<OzShuttleStartProof>()) { Debug.LogWarning(Tag + " a run is still going"); return; }
            new GameObject("OzShuttleStartProof").AddComponent<OzShuttleStartProof>();
        }

        OpenZMainMenu menu;
        int ok, bad, pictures, gv0 = -1, focus0;
        readonly StringBuilder fails = new StringBuilder(), lines = new StringBuilder();
        string dir, view0;
        bool kept, sizeOk, box0, head0, calm0, scaleKept; float scale0 = 1f;
        GameDifficulty diff0;

        // ------------------------------------------------------------------ helpers (as the HIGH SCORES proof)
        static float U => Screen.height / 1080f;
        static Rect Px(RectTransform rt)
        {
            rt.GetWorldCorners(corners);
            return Rect.MinMaxRect(Mathf.Min(corners[0].x, corners[2].x), Mathf.Min(corners[0].y, corners[2].y), Mathf.Max(corners[0].x, corners[2].x), Mathf.Max(corners[0].y, corners[2].y));
        }
        /// <summary>Canvas units (top-left origin, 1080 u tall) to a screen rectangle in pixels.</summary>
        static Rect UR(float x, float y, float w, float h) => Rect.MinMaxRect(x * U, Screen.height - (y + h) * U, (x + w) * U, Screen.height - y * U);
        /// <summary>The drawn letters of a text (false: no text, hidden, nothing drawn).</summary>
        static bool Drawn(Component c, out Rect r)
        {
            r = default;
            var t = c ? c.GetComponent<TMP_Text>() : null;
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
        /// <summary>A part's rectangle: a text's drawn letters, else its RectTransform's.</summary>
        static bool Part(Transform t, out Rect r)
        {
            r = default;
            if (!t || !t.gameObject.activeInHierarchy) return false;
            if (t.GetComponent<TMP_Text>()) return Drawn(t, out r);
            r = Px((RectTransform)t); return r.width > 0.01f && r.height > 0.01f;
        }
        /// <summary>A text at the size it was made with (at least OzText.MinSize), not shrunk and not cut.</summary>
        static bool FullSize(Transform t, float size) { var x = t ? t.GetComponent<TMP_Text>() : null; return x && x.fontSize >= Mathf.Max(size, OzText.MinSize) - 0.01f && x.overflowMode != TextOverflowModes.Ellipsis; }
        static bool Meet(Rect a, Rect b, float slack = 0.5f) => a.xMin < b.xMax - slack && b.xMin < a.xMax - slack && a.yMin < b.yMax - slack && b.yMin < a.yMax - slack;
        static bool Inside(Rect a, Rect b, float slack = 0.5f) => a.xMin >= b.xMin - slack && a.xMax <= b.xMax + slack && a.yMin >= b.yMin - slack && a.yMax <= b.yMax + slack;
        static bool Same(Rect a, Rect b) => Mathf.Abs(a.xMin - b.xMin) <= 0.5f && Mathf.Abs(a.xMax - b.xMax) <= 0.5f && Mathf.Abs(a.yMin - b.yMin) <= 0.5f && Mathf.Abs(a.yMax - b.yMax) <= 0.5f;
        static string R(Rect r)
        {
            float u = U;
            return "x " + (r.xMin / u).ToString("0", Inv) + ".." + (r.xMax / u).ToString("0", Inv) + " y " + ((Screen.height - r.yMax) / u).ToString("0", Inv) + ".." + ((Screen.height - r.yMin) / u).ToString("0", Inv);
        }
        static IEnumerator Wait(float s) { float e = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < e) yield return null; }
        static bool Heard(float t0, string key)
        {
            foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && e.outcome.StartsWith("PLAY")) return true;
            return false;
        }
        static string TextOf(Transform t) { var x = t ? t.GetComponent<TMP_Text>() : null; return x ? x.text : ""; }
        void Check(bool pass, string what)
        {
            if (pass) ok++; else { bad++; fails.Append("\n  FAIL ").Append(what); }
            lines.Append(pass ? "PASS " : "FAIL ").AppendLine(what);
        }
        void Info(string what) => lines.Append("INFO ").AppendLine(what);

        static string OutDir()
        {
            string all = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones")), stage = "v1_1";
            if (Directory.Exists(all))
            {
                var ds = Directory.GetDirectories(all, "v1_1*");
                Array.Sort(ds, StringComparer.Ordinal);
                if (ds.Length > 0) stage = Path.GetFileName(ds[ds.Length - 1]);
            }
            return Path.Combine(all, stage, "k347_shuttle_start_checkbox");
        }

        IEnumerator SetSize(int w, int h)
        {
            OzFinalGateView.SetFixed(w, h, "OpenZ " + w + "x" + h, out string info);
            float t0 = Time.realtimeSinceStartup;
            while ((Screen.width != w || Screen.height != h) && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            yield return Wait(0.6f);
            sizeOk = Screen.width == w && Screen.height == h;
            if (!sizeOk) Info($"{w}x{h}: the Game view did not take the size ({info})");
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            try { File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG()); pictures++; }
            catch (Exception ex) { Debug.LogWarning(Tag + " picture " + name + " not written: " + ex.Message); }
            Destroy(tex);
        }

        static IEnumerator Tap(Key k)
        {
            var kb = Keyboard.current;
            if (kb == null) yield break;
            try { InputSystem.QueueStateEvent(kb, new KeyboardState(k)); } catch (Exception) { }
            yield return null; yield return null; yield return null;
            try { InputSystem.QueueStateEvent(kb, new KeyboardState()); } catch (Exception) { }
            yield return null; yield return null;
        }

        static RectTransform ZodWindow()
        {
            var zb = ZodBriefing.I; var zc = zb ? zb.transform.Find("ZodComms") : null; var zw = zc ? zc.Find("ZodWindow") as RectTransform : null;
            if (!zw || !zw.gameObject.activeInHierarchy) return null;
            var g = zw.GetComponent<CanvasGroup>();
            return g && g.alpha < 0.02f ? null : zw;
        }

        void PutBack()
        {
            if (scaleKept) { scaleKept = false; OzText.UiScale = scale0; if (OpenZHud.I) OpenZHud.I.ApplyUiScale(); }
            if (!kept) return;
            kept = false;
            if (OzShuttleStart.On != box0) OzShuttleStart.On = box0;
            if (OpenZDifficulty.HeadStartToggle != head0) OpenZDifficulty.HeadStartToggle = head0;
            if (OzMotion.Reduced != calm0) OzMotion.Reduced = calm0;
            if (menu)
            {
                if (OpenZDifficulty.Level != diff0) OpenZDifficulty.Level = diff0;
                menu.ProofFocus(focus0);
                if (view0 == "Main" && menu.ProofView == "Select") menu.ProofMain();
            }
            if (gv0 >= 0) OzFinalGateView.Select(gv0);
        }

        void OnDestroy() { PutBack(); }

        void Finish(string where, string detail)
        {
            bool pass = bad == 0 && ok > 0;
            Report = Tag + " " + (pass ? "PASS" : "FAIL") + " " + ok + "/" + (ok + bad) + " checks (" + where + "): " + detail + "; " + pictures + " pictures in " + dir.Replace('\\', '/') + (bad == 0 ? "" : "; FAILED:" + fails);
            if (pass) Debug.Log(Report); else Debug.LogError(Report);
            try
            {
                File.AppendAllText(Path.Combine(dir, "checks.txt"), Report + "\n");
                File.WriteAllText(Path.Combine(dir, "checks_" + where.Replace(' ', '_') + ".txt"), lines.ToString());
            }
            catch (Exception ex) { Debug.LogWarning(Tag + " could not write the check files: " + ex.Message); }
        }

        IEnumerator Start()
        {
            dir = OutDir(); Directory.CreateDirectory(dir);
            var gv = Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) EditorWindow.FocusWindowIfItsOpen(gv);
            menu = FindFirstObjectByType<OpenZMainMenu>();
            if (menu) yield return MenuPart();
            else if (ZGame.I && ZGame.I.World != null && OpenZHud.I) yield return LevelPart();
            else { Check(false, "the MainMenu scene or a running level is needed"); Finish("no scene", "open the MainMenu scene or a level, then Play"); }
            Destroy(gameObject);
        }

        // ================================================================== the battlefield screen
        struct Case { public string name, level; public GameDifficulty d; public bool box; }
        static readonly Case[] Cases =
        {
            new Case { name = "L16_easy_on", level = "L16", d = GameDifficulty.Easy, box = true },
            new Case { name = "L16_medium_on", level = "L16", d = GameDifficulty.Medium, box = true },
            new Case { name = "L16_medium_off", level = "L16", d = GameDifficulty.Medium, box = false },
            new Case { name = "L16_hard", level = "L16", d = GameDifficulty.Hard, box = true },
            new Case { name = "L02_medium", level = "L02", d = GameDifficulty.Medium, box = true },
            new Case { name = "L17_easy", level = "L17", d = GameDifficulty.Easy, box = true },
            new Case { name = "L17_hard", level = "L17", d = GameDifficulty.Hard, box = true },                         // K444: the slot locked on level 17 HARD
        };

        RectTransform Sel => menu.ProofSelectRoot;
        RectTransform Find(string path) => Sel ? Sel.Find(path) as RectTransform : null;
        /// <summary>A block's column in pixels (the bar's own layout, with the current slide for DIFFICULTY / HEAD START).</summary>
        Rect Column(int i, int slots)
        {
            float x = slots == 3 || i == 2 ? menu.ProofBlockX(i, 3) : menu.ProofBlockX(i, 2);
            return UR(x, OzDeployBar.LineTop, menu.ProofBlockW, OzDeployBar.LineBottom - OzDeployBar.LineTop);
        }

        /// <summary>The rectangles that must not change within a level (and the header, the card and DEPLOY in every state).</summary>
        Dictionary<string, Rect> Fixed()
        {
            var d = new Dictionary<string, Rect>();
            foreach (var n in Header.Concat(new[] { "DeployBar", "ObjectiveCard", "Deploy" }).Concat(Slides)) { var t = Find(n); if (t && t.gameObject.activeInHierarchy) d[n] = Px(t); }
            var sb = Find("ShuttleBlock");
            if (sb && sb.gameObject.activeInHierarchy) foreach (var n in new[] { "", "/Label", "/Key", "/ShuttleRow", "/Line1" }) { var t = Find("ShuttleBlock" + n); if (t) d["ShuttleBlock" + n] = Px(t); }
            return d;
        }

        IEnumerator MenuPart()
        {
            diff0 = OpenZDifficulty.Level; box0 = OzShuttleStart.On; head0 = OpenZDifficulty.HeadStartToggle; calm0 = OzMotion.Reduced;
            view0 = menu.ProofView; focus0 = menu.ProofFocusCard; gv0 = OzFinalGateView.SelectedIndex(); kept = true;
            if (OzMotion.Reduced) OzMotion.Reduced = false;                                                       // the normal motion first; CALM MODE is checked on its own
            var ids = OzLevelDef.All.Select(l => l.Id).ToList();
            foreach (var s in Sizes)
            {
                string at = s[0] + "x" + s[1];
                yield return SetSize(s[0], s[1]);
                Check(sizeOk, at + " the Game view has this size (it has " + Screen.width + "x" + Screen.height + ")");
                if (!sizeOk) continue;
                if (menu.ProofView != "Select") menu.ProofSelect();
                yield return Wait(1.2f);
                if (!Sel || menu.ProofView != "Select") { Check(false, at + " the battlefield screen is up"); continue; }
                Dictionary<string, Rect> ref16 = null, refAny = null; float slide0 = 0f;
                var refSlot = new Dictionary<string, Dictionary<string, Rect>>();                                     // K444: each level with the slot against its own first state
                foreach (var c in Cases)
                {
                    if (OpenZDifficulty.Level != c.d) OpenZDifficulty.Level = c.d;
                    if (OzShuttleStart.On != c.box) OzShuttleStart.On = c.box;
                    menu.ProofFocus(ids.IndexOf(c.level));
                    yield return Wait(0.6f);
                    string tag = at + " " + c.name + ":";
                    int slots = menu.ProofSlots; bool slot16 = OzShuttleStart.HasSlot(c.level, false), live = slot16 && c.d != GameDifficulty.Hard;   // K444: levels 16 and 17 have the slot
                    // S the SHUTTLE slot
                    var sb = Find("ShuttleBlock"); bool up = sb && sb.gameObject.activeInHierarchy; var cg = sb ? sb.GetComponent<CanvasGroup>() : null; float alpha = cg && up ? cg.alpha : 0f;
                    bool sOk = slot16 ? slots == 3 && up && alpha > 0.98f && menu.ProofShuttleShown == live && menu.ProofShuttleRowLive == live && (live ? menu.ProofShuttleLockK < 0.02f : menu.ProofShuttleLockK > 0.98f)
                                      : slots == 2 && !up && !menu.ProofShuttleShown;
                    Check(sOk, tag + " S the SHUTTLE slot is " + (slot16 ? (live ? "live" : "locked (HARD)") : "not there (two blocks)") + " (blocks " + slots + ", active " + up + ", alpha " + alpha.ToString("0.00", Inv)
                               + ", offered " + menu.ProofShuttleShown + ", row live " + menu.ProofShuttleRowLive + ", locked " + menu.ProofShuttleLockK.ToString("0.00", Inv) + ")");
                    // M nothing moved within a level; between levels only the two blocks, by one slot
                    var rects = Fixed();
                    if (refAny == null) { refAny = rects; slide0 = menu.ProofSlideNow; }
                    if (slot16)
                    {
                        if (!refSlot.TryGetValue(c.level, out ref16)) { refSlot[c.level] = rects; ref16 = null; }   // K444
                        if (ref16 == null) ref16 = rects;
                        else
                        {
                            var moved = new StringBuilder();
                            foreach (var kv in ref16) if (!rects.TryGetValue(kv.Key, out var r2) || !Same(kv.Value, r2)) moved.Append(' ').Append(kv.Key).Append(rects.TryGetValue(kv.Key, out var r3) ? " " + R(kv.Value) + " -> " + R(r3) : " gone");
                            foreach (var kv in rects) if (!ref16.ContainsKey(kv.Key)) moved.Append(' ').Append(kv.Key).Append(" new");
                            Check(moved.Length == 0, tag + " M nothing moved within level " + c.level.Substring(1) + " (" + rects.Count + " parts as in its first state)" + (moved.Length > 0 ? "; MOVED:" + moved : ""));
                        }
                    }
                    else
                    {
                        float dx = (menu.ProofSlideNow - slide0) * U, want = menu.ProofSlideStep * U; var wrong = new StringBuilder();
                        foreach (var n in Header.Concat(new[] { "DeployBar", "ObjectiveCard", "Deploy" }))
                            if (!refAny.TryGetValue(n, out var a) || !rects.TryGetValue(n, out var b2) || !Same(a, b2)) wrong.Append(' ').Append(n);
                        foreach (var n in Slides)
                            if (!refAny.TryGetValue(n, out var a) || !rects.TryGetValue(n, out var b2) || Mathf.Abs(b2.xMin - a.xMin - want) > 0.6f || Mathf.Abs(b2.yMin - a.yMin) > 0.5f) wrong.Append(' ').Append(n).Append(" (").Append(rects.TryGetValue(n, out var b3) && refAny.TryGetValue(n, out var a3) ? ((b3.xMin - a3.xMin) / U).ToString("0.0", Inv) + " u" : "gone").Append(')');
                        Check(wrong.Length == 0 && Mathf.Abs(dx - want) <= 0.6f, tag + " M the header, the card and DEPLOY stay; DIFFICULTY and HEAD START moved one slot (" + (dx / U).ToString("0", Inv) + " u, want " + menu.ProofSlideStep.ToString("0", Inv) + " u)" + (wrong.Length > 0 ? "; WRONG:" + wrong : ""));
                    }
                    yield return BarChecks(tag, c, slots);
                    yield return Shot(at + "_" + c.name);
                }
                yield return ToggleChecks(at, ids);
                yield return TipChecks(at, ids);
                yield return SlideChecks(at, ids);
                yield return OptionsCheck(at);
            }
            PutBack();
            Finish("battlefield screen", Sizes.Length + " screen sizes x " + Cases.Length + " states");
        }

        /// <summary>O inside / apart / full size, L one line, T the words.</summary>
        IEnumerator BarChecks(string tag, Case c, int slots)
        {
            yield return null;
            var screen = new Rect(0, 0, Screen.width, Screen.height);
            var bar = Find("DeployBar"); Rect br = bar ? Px(bar) : default; var inBar = new Rect(br.xMin + 1f * U, br.yMin + 1f * U, br.width - 2f * U, br.height - 2f * U);
            var outs = new StringBuilder(); var meets = new StringBuilder(); var small = new StringBuilder(); var miss = new StringBuilder();
            // the top-level parts: the card, the block columns, the hairlines, DEPLOY
            var tops = new List<(string n, Rect r)>();
            var card = Find("ObjectiveCard"); if (card) tops.Add(("card", Px(card))); else miss.Append(" ObjectiveCard");
            for (int i = 0; i < slots; i++) tops.Add(("column " + i, Column(i, slots)));
            foreach (var n in new[] { "Line0", "ShuttleBlock/Line1", "Deploy" }) { var t = Find(n); if (t && t.gameObject.activeInHierarchy) tops.Add((n, Px(t))); else if (n != "ShuttleBlock/Line1" || slots == 3) miss.Append(' ').Append(n); }
            foreach (var p in tops) if (!Inside(p.r, inBar)) outs.Append(' ').Append(p.n).Append(" (").Append(R(p.r)).Append(')');
            for (int i = 0; i < tops.Count; i++) for (int j = i + 1; j < tops.Count; j++) if (Meet(tops[i].r, tops[j].r)) meets.Append(' ').Append(tops[i].n).Append('/').Append(tops[j].n);
            // the card's parts
            if (card)
            {
                Rect cr = Px(card); var inner = new Rect(cr.xMin + 2f * U, cr.yMin + 2f * U, cr.width - 4f * U, cr.height - 4f * U);
                var parts = new List<(string n, Rect r)>();
                foreach (var (n, size) in new[] { ("Tag", 10f), ("Headline", OzDeployBar.HeadSize), ("Sub", 9f), ("Fort", 0f) })
                {
                    var t = card.Find(n);
                    if (Part(t, out var r)) parts.Add(("card " + n, r)); else if (n != "Fort" || menu.ProofCardGlyph) miss.Append(" card ").Append(n);
                    if (size > 0f && t && !FullSize(t, size)) small.Append(" card ").Append(n);
                }
                foreach (var p in parts) if (!Inside(p.r, inner)) outs.Append(' ').Append(p.n);
                for (int i = 0; i < parts.Count; i++) for (int j = i + 1; j < parts.Count; j++) if (Meet(parts[i].r, parts[j].r)) meets.Append(' ').Append(parts[i].n).Append('/').Append(parts[j].n);
            }
            // each block's parts inside its column and apart, at full size
            var cols = new List<(string label, string key, string[] rows, string hint)> { ("CpuLabel", "CpuKey", new[] { "CpuEasy", "CpuMedium", "CpuHard" }, "CpuHint"), ("HeadLabel", "HeadKey", new[] { "HeadRow" }, "HeadHint") };
            if (slots == 3) cols.Add(("ShuttleBlock/Label", "ShuttleBlock/Key", new[] { "ShuttleBlock/ShuttleRow" }, c.d == GameDifficulty.Hard ? "ShuttleBlock/HintLocked" : "ShuttleBlock/Hint"));
            var labelY = new List<float>(); var keyY = new List<float>(); var rowY = new List<(float a, float b)>(); var hintY = new List<float>();
            for (int i = 0; i < cols.Count; i++)
            {
                var col = Column(i, slots); var ps = new List<(string n, Rect r)>();
                foreach (var n in new[] { cols[i].label, cols[i].key }.Concat(cols[i].rows).Concat(new[] { cols[i].hint }))
                {
                    var t = Find(n);
                    if (Part(t, out var r)) ps.Add((n, r)); else miss.Append(' ').Append(n);
                    if (t && t.GetComponent<TMP_Text>() && !FullSize(t, 9f)) small.Append(' ').Append(n);
                }
                foreach (var p in ps) if (!Inside(p.r, col)) outs.Append(' ').Append(p.n).Append(" (").Append(R(p.r)).Append(" not in ").Append(R(col)).Append(')');
                for (int a = 0; a < ps.Count; a++) for (int b = a + 1; b < ps.Count; b++) if (Meet(ps[a].r, ps[b].r)) meets.Append(' ').Append(ps[a].n).Append('/').Append(ps[b].n);
                var lt = Find(cols[i].label); var kt = Find(cols[i].key); var rt0 = Find(cols[i].rows[0]); var ht = Find(cols[i].hint);
                if (lt) labelY.Add(Px(lt).yMax); if (kt) keyY.Add(Px(kt).yMax); if (rt0) { var q = Px(rt0); rowY.Add((q.yMin, q.yMax)); } if (ht) hintY.Add(Px(ht).yMax);
            }
            // the switch rows' parts (words, ON / OFF, track, knob) inside the row and apart, at full size
            foreach (var row in slots == 3 ? new[] { "HeadRow", "ShuttleBlock/ShuttleRow" } : new[] { "HeadRow" })
            {
                var rr = Find(row); if (!rr) continue; Rect r0 = Px(rr); var rp = new List<(string n, Rect r)>();
                bool locked = row.StartsWith("ShuttleBlock") && c.d == GameDifficulty.Hard;
                string[] names = row == "HeadRow" ? new[] { "Content/Text", "Content/State", "Content/Track", "Content/Knob" } : locked ? new[] { "Content/Locked/Text", "Content/Track", "Content/Locked/Knob" } : new[] { "Content/Live/Text", "Content/Live/State", "Content/Track", "Content/Live/Knob" };
                foreach (var n in names)
                {
                    var t = rr.Find(n);
                    if (Part(t, out var r)) rp.Add((n, r)); else miss.Append(' ').Append(row).Append('/').Append(n);
                    if (t && t.GetComponent<TMP_Text>() && !FullSize(t, 9f)) small.Append(' ').Append(row).Append('/').Append(n);
                }
                foreach (var p in rp) if (!Inside(p.r, r0)) outs.Append(' ').Append(row).Append('/').Append(p.n);
                for (int a = 0; a < rp.Count; a++) for (int b = a + 1; b < rp.Count; b++)
                    if (!rp[a].n.EndsWith("Knob") && !rp[b].n.EndsWith("Knob") && Meet(rp[a].r, rp[b].r)) meets.Append(' ').Append(row).Append(':').Append(rp[a].n).Append('/').Append(rp[b].n);
            }
            Check(miss.Length == 0 && outs.Length == 0 && meets.Length == 0 && small.Length == 0,
                  tag + " O the card, " + slots + " blocks, the hairlines and DEPLOY inside the bar (" + R(br) + ") and apart; every part inside its card / column / row and apart; every text at its full size"
                  + (miss.Length > 0 ? "; MISSING:" + miss : "") + (outs.Length > 0 ? "; OUTSIDE:" + outs : "") + (meets.Length > 0 ? "; MEET:" + meets : "") + (small.Length > 0 ? "; SHRUNK:" + small : ""));
            // L one line
            bool One(IEnumerable<float> v) { var a = v.ToList(); return a.Count > 0 && a.Max() - a.Min() <= 1f; }
            var tagT = card ? card.Find("Tag") : null; var subT = card ? card.Find("Sub") : null;
            bool cardOn = tagT && subT && labelY.Count > 0 && hintY.Count > 0 && Mathf.Abs(Px((RectTransform)tagT).yMax - labelY[0]) <= 1f && Mathf.Abs(Px((RectTransform)subT).yMax - hintY[0]) <= 1f;
            var widths = Enumerable.Range(0, slots).Select(i => Column(i, slots).width).ToList();
            Check(One(labelY) && One(keyY) && One(rowY.Select(q => q.a)) && One(rowY.Select(q => q.b)) && One(hintY) && cardOn && widths.Max() - widths.Min() < 0.01f && labelY.Count == slots,
                  tag + " L one line: " + slots + " labels, key caps, rows and hints on the same y, the blocks " + menu.ProofBlockW.ToString("0", Inv) + " u each, the card's tag and small line on the label and hint lines");
            // T the words
            var d = Def(c.level);
            string head = OzDeployBar.TeamWords(TeamColors.Words(OzDeployBar.Headline(c.level)));
            var cardParts = menu.ProofCardText.Split(new[] { " | " }, StringSplitOptions.None);
            var mt = cardParts.Length == 3 ? Regex.Match(cardParts[2], @"^(\d+) FLAGS · " + Regex.Escape(OzDeployBar.CardRule) + "$") : Match.Empty;
            int chip = d.FallbackChips.Select(x => x.EndsWith(" FLAGS") && int.TryParse(x.Substring(0, x.Length - 6), out int n) ? n : 0).FirstOrDefault(n => n > 0);
            bool cardOk = cardParts.Length == 3 && cardParts[0] == OzDeployBar.CardTag && cardParts[1] == head && (mt.Success || cardParts[2] == OzDeployBar.CardRulePlain);
            if (mt.Success && chip > 0 && int.Parse(mt.Groups[1].Value) != chip) Info(tag + " the card counts " + mt.Groups[1].Value + " flags from the map; the card's own chip says " + chip);
            string cpu = TextOf(Find("CpuHint")), hh = TextOf(Find("HeadHint"));
            string wantHead = OpenZDifficulty.HeadStartToggle ? OzDeployBar.HeadHint(c.level) : OzDeployBar.HeadOff;
            string wantSh = !IsL16(c) ? "" : c.d == GameDifficulty.Hard ? OzShuttleStart.LockedRow + " / " + OzShuttleStart.LockedHint : c.box ? "ON / " + OzShuttleStart.HintOn : "OFF / " + OzShuttleStart.HintOffFor(c.level);
            bool shOk = !IsL16(c) || menu.ProofShuttleText == wantSh;
            Check(cardOk && cpu == OzDeployBar.CpuHint(c.d) && hh == wantHead && shOk,
                  tag + " T the card \"" + menu.ProofCardText + "\"; DIFFICULTY \"" + cpu + "\" (want \"" + OzDeployBar.CpuHint(c.d) + "\"); HEAD START \"" + hh + "\" (want \"" + wantHead + "\")"
                  + (IsL16(c) ? "; SHUTTLE \"" + menu.ProofShuttleText + "\" (want \"" + wantSh + "\")" : ""));
        }

        static bool IsL16(Case c) => OzShuttleStart.HasSlot(c.level, false);                                     // K444: (16 and 17)
        static OzLevelDef Def(string id) => OzLevelDef.All.First(l => l.Id == id);

        IEnumerator ToggleChecks(string at, List<string> ids)
        {
            OpenZDifficulty.Level = GameDifficulty.Medium; OzShuttleStart.On = true; menu.ProofFocus(ids.IndexOf("L16"));
            yield return Wait(0.6f);
            var row = menu.ProofShuttleRow;
            if (!row) { Check(false, at + " K the row is there"); yield break; }
            var mid = Px(row).center;
            float t0 = Time.unscaledTime; int n0 = OpenZMainMenu.ShuttleToggles;
            string hot = menu.ProofHotAt(mid); menu.ProofClickAt(mid);
            yield return Wait(0.25f);
            Check(hot == "control" && !OzShuttleStart.On && PlayerPrefs.GetInt(OzShuttleStart.Pref, 1) == 0 && Heard(t0, "ui_toggle_off") && OpenZMainMenu.ShuttleToggles == n0 + 1,
                  at + " K a click on the row (\"" + hot + "\") turns the box OFF, saved (" + OzShuttleStart.Pref + " " + PlayerPrefs.GetInt(OzShuttleStart.Pref, 1) + "), ui_toggle_off " + Heard(t0, "ui_toggle_off"));
            yield return Shot(at + "_L16_medium_clicked_off");
            t0 = Time.unscaledTime; n0 = OpenZMainMenu.ShuttleToggles;
            yield return Tap(OpenZMainMenu.ShuttleKey);
            yield return Wait(0.2f);
            bool real = OpenZMainMenu.ShuttleToggles == n0 + 1;
            if (!real) { Info(at + " the key event did not reach the game (click into the Game view and run again for a real key): its handler is called instead"); menu.ProofToggleShuttle(true); yield return Wait(0.2f); }
            Check(OzShuttleStart.On && Heard(t0, "ui_toggle_on") && OpenZMainMenu.ShuttleToggles == n0 + 1,
                  at + " K the key " + OzKeyNames.Name(OpenZMainMenu.ShuttleKey) + " turns it ON again with ui_toggle_on (" + (real ? "a real key event" : "its handler") + ")");
            // level 2: the key answers ui_disabled
            menu.ProofFocus(ids.IndexOf("L02"));
            yield return Wait(0.6f);
            t0 = Time.unscaledTime; int refused0 = OpenZMainMenu.ShuttleKeyRefused; bool on0 = OzShuttleStart.On;
            menu.ProofToggleShuttle(true);
            yield return Wait(0.2f);
            Check(OzShuttleStart.On == on0 && OpenZMainMenu.ShuttleKeyRefused == refused0 + 1 && Heard(t0, "ui_disabled"), at + " K on level 2 the key changes nothing and answers ui_disabled");
            // level 16 HARD: the key and a click on the locked row change nothing
            OpenZDifficulty.Level = GameDifficulty.Hard; menu.ProofFocus(ids.IndexOf("L16"));
            yield return Wait(0.6f);
            t0 = Time.unscaledTime; refused0 = OpenZMainMenu.ShuttleKeyRefused; n0 = OpenZMainMenu.ShuttleToggles; on0 = OzShuttleStart.On;
            menu.ProofToggleShuttle(true);
            var lr = menu.ProofShuttleRow; string hotH = lr ? menu.ProofHotAt(Px(lr).center) : "no row"; if (lr) menu.ProofClickAt(Px(lr).center);
            yield return Wait(0.2f);
            Check(OzShuttleStart.On == on0 && OpenZMainMenu.ShuttleKeyRefused == refused0 + 1 && OpenZMainMenu.ShuttleToggles == n0 && Heard(t0, "ui_disabled") && hotH != "control",
                  at + " K on level 16 HARD the key answers ui_disabled and a click on the locked row (\"" + hotH + "\") changes nothing");
            OpenZDifficulty.Level = GameDifficulty.Medium;
        }

        IEnumerator TipChecks(string at, List<string> ids)
        {
            OpenZDifficulty.Level = GameDifficulty.Medium; OzShuttleStart.On = true; menu.ProofFocus(ids.IndexOf("L16"));
            yield return Wait(0.6f);
            var screen = new Rect(0, 0, Screen.width, Screen.height); var bar = Find("DeployBar"); Rect br = bar ? Px(bar) : default;
            var others = new Dictionary<string, Rect> { ["the deploy bar"] = br };
            foreach (var n in Header) { var t = Find(n); if (t) others[n] = Px(t); }
            if (Drawn(Find("Keys"), out var kr)) others["the key hints"] = kr;
            foreach (bool mine in new[] { true, false })
            {
                if (mine) menu.ProofShuttleTipHover(true); else menu.ProofHeadTip(true);
                yield return Wait(0.5f);
                var zw = ZodWindow(); var o2 = new Dictionary<string, Rect>(others); if (zw) o2["the commander's strip"] = Px(zw);
                var tip = mine ? menu.ProofShuttleTip : menu.ProofHeadTipRt; string what = mine ? "the SHUTTLE tooltip" : "HEAD START's tooltip";
                bool up = tip && tip.gameObject.activeInHierarchy; Rect tr = up ? Px(tip) : default; Rect col = Column(mine ? 2 : 1, 3);
                var hits = new StringBuilder();
                if (up) foreach (var kv in o2) if (Meet(tr, kv.Value)) hits.Append(' ').Append(kv.Key);
                bool over = up && tr.xMin < col.xMax && col.xMin < tr.xMax && tr.yMin >= br.yMax - 0.5f;
                Check(up && Inside(tr, screen) && hits.Length == 0 && over, at + " P " + what + " opens over its own block (" + (up ? R(tr) : "not up") + ", the block " + R(col) + "), inside the screen, above the bar, meeting nothing" + (hits.Length > 0 ? "; it MEETS" + hits : ""));
                yield return Shot(at + "_L16_medium_" + (mine ? "shuttle_tip" : "headstart_tip"));
                if (mine) menu.ProofShuttleTipHover(false); else menu.ProofHeadTip(false);
                yield return Wait(0.3f);
            }
        }

        /// <summary>The change level 16 -> 2 -> 16 watched every frame: the moving blocks never over the SHUTTLE block while it shows
        /// (alpha 0.05 or more), the card and DEPLOY never move, the end places exact; then the same in CALM MODE (at once).</summary>
        IEnumerator SlideChecks(string at, List<string> ids)
        {
            OpenZDifficulty.Level = GameDifficulty.Medium; OzShuttleStart.On = true; menu.ProofFocus(ids.IndexOf("L16"));
            yield return Wait(0.6f);
            Rect card0 = Px(Find("ObjectiveCard")), dep0 = Px(Find("Deploy")); Rect head3 = Px(Find("HeadRow"));
            foreach (bool calm in new[] { false, true })
            {
                if (OzMotion.Reduced != calm) OzMotion.Reduced = calm;
                var wrong = new StringBuilder(); int frames = 0; float worst = 0f;
                foreach (var to in new[] { "L02", "L16" })
                {
                    menu.ProofFocus(ids.IndexOf(to));
                    float end = Time.realtimeSinceStartup + 0.5f;
                    while (Time.realtimeSinceStartup < end)
                    {
                        yield return null; frames++;
                        var sb = Find("ShuttleBlock"); var cg = sb ? sb.GetComponent<CanvasGroup>() : null; Rect hr = Px(Find("HeadRow")), ln = Px(Find("Line0"));
                        if (sb && sb.gameObject.activeInHierarchy && cg && (Meet(hr, Px(sb)) || Meet(ln, Px(sb)))) { worst = Mathf.Max(worst, cg.alpha); if (cg.alpha >= 0.05f && wrong.Length < 300) wrong.Append(" over the SHUTTLE block at alpha ").Append(cg.alpha.ToString("0.00", Inv)); }
                        if (!Same(Px(Find("ObjectiveCard")), card0) || !Same(Px(Find("Deploy")), dep0)) { if (wrong.Length < 300) wrong.Append(" the card or DEPLOY moved"); }
                    }
                    var h = Px(Find("HeadRow")); float wantX = (to == "L02" ? menu.ProofBlockX(1, 2) : menu.ProofBlockX(1, 3)) * U;
                    var sb2 = Find("ShuttleBlock"); bool sbUp = sb2 && sb2.gameObject.activeInHierarchy;
                    if (Mathf.Abs(h.xMin - wantX) > 0.6f || sbUp != (to == "L16") || menu.ProofSlots != (to == "L16" ? 3 : 2)) wrong.Append(' ').Append(to).Append(": HEAD START at ").Append(R(h)).Append(", the SHUTTLE block ").Append(sbUp ? "up" : "gone");
                }
                bool back = Same(Px(Find("HeadRow")), head3);
                Check(wrong.Length == 0 && back, at + " " + (calm ? "C CALM MODE" : "D the change") + " level 16 -> 2 -> 16 (" + frames + " frames): the blocks end exactly one slot over and back, the card and DEPLOY still, the moving blocks over the SHUTTLE block only at alpha " + worst.ToString("0.00", Inv) + " or less" + (wrong.Length > 0 ? "; WRONG:" + wrong : ""));
                if (!calm) yield return Shot(at + "_L16_after_level_change");
            }
            if (OzMotion.Reduced) OzMotion.Reduced = false;
        }

        IEnumerator OptionsCheck(string at)
        {
            menu.ProofMain(); yield return Wait(0.8f);
            menu.ProofOpen("options"); yield return Wait(0.5f);
            menu.ProofOptTab(OzSettingTips.TabAdvanced); yield return Wait(0.3f);
            var rows = menu.ProofOptTabRows.Split(new[] { " | " }, StringSplitOptions.None).ToList();
            Check(!rows.Contains(OzShuttleOption.Label) && rows.Count > 0, at + " OPTIONS, ADVANCED: no row " + OzShuttleOption.Label + " (rows: " + string.Join(" | ", rows) + ")");
            yield return Shot(at + "_options_advanced");
            menu.ProofOpen(""); yield return Wait(0.4f);
            menu.ProofSelect(); yield return Wait(0.8f);
        }

        // ================================================================== a level
        IEnumerator LevelPart()
        {
            var g = ZGame.I; var w = g.World; var hud = OpenZHud.I;
            string level = g.level; var d = g.gameDifficulty; bool box = OzShuttleStart.On;
            bool expect = (level == OzShuttleStart.Level && OzShuttleStart.Rule(level, d, box) && ZWorld.ShuttlePlayableDeviation)
                          || (level == OzShuttleStart.Level17 && OzShuttleStart.L17Start(level, d, box) && ZWorld.ShuttlePlayableDeviation && ZWorld.L17ShuttlesDeviation);   // K444: level 17's box ship
            var own = w.PlayableShuttle; var ship = FindFirstObjectByType<L16Shuttle>(FindObjectsInactive.Include);
            bool shipOk = level != OzShuttleStart.Level || !ship || ship.gameObject.activeInHierarchy == expect;
            bool startOk = level != OzShuttleStart.Level || (expect ? own != null && !w.L16ShuttleOptionOff : own == null && w.L16ShuttleOptionOff);
            if (level == OzShuttleStart.Level17 && ZWorld.L17ShuttlesDeviation && ZWorld.ShuttlePlayableDeviation)                                // K444 (DV134): the empty car-park ship always, the box's ship by the red fort
                startOk = w.L17CarParkShuttle != null && (expect ? own != null && own == w.L17FortShuttle : own == null);   // other levels: nothing of their own (a shuttle there is one the testing panel dropped)
            Check(ZWorld.ShuttleOn == OzShuttleStart.Rule(level, d, box) && startOk && shipOk,
                  $"L1 {level} {OpenZDifficulty.Label(d)}, the box {(box ? "ON" : "OFF")}: the rule switch {(ZWorld.ShuttleOn ? "on" : "off")}, the level's own shuttle {(own != null ? "#" + own.Id + " there" : "none")} (want {(expect ? "there" : "none")}), the scene's ship {(ship ? (ship.gameObject.activeInHierarchy ? "shown" : "hidden") : "not in this scene")}");
            yield return Shot($"level_{level}_{OpenZDifficulty.Label(d)}_box_{(box ? "on" : "off")}_start");
            // L2 the testing panel's own drop beside the player's fort
            var fort = w.Objects.FirstOrDefault(o => o.Kind == ZKind.Building && o.Type == "fort_front" && o.Owner == g.player && !o.Destroyed);
            int made = 0; var why = new StringBuilder();
            if (fort != null)
                foreach (var t in new[] { g.player, Team.Neutral })
                {
                    ZObject s = null;
                    for (int r = 96; r <= 900 && s == null; r += 48)
                        for (int a = 0; a < 12 && s == null; a++)
                        {
                            double ang = a * Math.PI / 6;
                            s = OzCheatShuttle.Spawn(g, hud, t, new Vector2Int(fort.LocX + fort.WidthPix / 2 + (int)(Math.Cos(ang) * r), fort.LocY + fort.HeightPix / 2 + (int)(Math.Sin(ang) * r)));
                            if (s == null && OzCheatShuttle.LastWhy != "no room") { why.Append(' ').Append(t).Append(": ").Append(OzCheatShuttle.LastWhy); r = 9999; break; }
                        }
                    if (s != null && OzPlayableShuttle.Of(s) && (t == Team.Neutral ? s.ShuttleIsEmpty : s.Drivers.Count == 1)) made++;
                    else if (s != null) why.Append(' ').Append(t).Append(": made but not drawn or not as dropped");
                }
            yield return Wait(0.5f);
            Check(fort != null && made == 2, $"L2 the testing panel's drop beside the player's fort: {made} of 2 shuttles (one {g.player}, one empty) made and drawn{(why.Length > 0 ? ";" + why : "")}");
            yield return Shot($"level_{level}_{OpenZDifficulty.Label(d)}_box_{(box ? "on" : "off")}_testing_panel_drops");
            // L3 the pause menu's OPTIONS, ADVANCED, at UI 100 % and 160 %
            var pm = hud.Pause;
            scale0 = OzText.UiScale; scaleKept = true;
            foreach (float sc in new[] { 1f, 1.6f })
            {
                OzText.UiScale = sc; hud.ApplyUiScale(); yield return Wait(0.3f);
                pm.OpenOptions(OzSettingTips.TabAdvanced); yield return Wait(0.6f);
                var rows = pm.ProofOptTabRows.Split(new[] { " | " }, StringSplitOptions.None).ToList();
                Check(pm.IsOpen && rows.Count > 0 && !rows.Contains(OzShuttleOption.Label), $"L3 pause menu OPTIONS, ADVANCED at UI {Mathf.RoundToInt(sc * 100)} %: no row {OzShuttleOption.Label} (rows: {string.Join(" | ", rows)})");
                yield return Shot($"level_pause_options_advanced_ui{Mathf.RoundToInt(sc * 100)}");
                pm.Close(); yield return Wait(0.4f);
            }
            PutBack();
            Finish("level " + level, "the level start and the testing panel");
        }
    }
}
#endif
