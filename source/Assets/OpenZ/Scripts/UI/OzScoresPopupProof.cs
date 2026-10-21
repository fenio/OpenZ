#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
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
    /// Counted check of the HIGH SCORES button and popup of the battlefield screen, and of the end screen's rectangles (editor
    /// only: this file compiles to nothing in a player build). Menu OpenZ / QC / High Scores Popup Proof (Play), or
    /// <c>OpenZ.OzScoresPopupProof.Run()</c>. It runs the part that fits the scene and sets the Game view itself to
    /// 1920x1080, 2560x1600, 3840x2160, 1440x1080 and 2560x1080, one after the other. Every rectangle is the REAL one (world
    /// corners on the overlay canvas, a text's line box or its drawn letters), never a layout number.
    ///
    /// In the main menu (about a minute), per size:
    ///   S   the Game view has the size
    ///   B1  the button is inside the screen and meets nothing: BACK, the title, the eyebrow, the key hints, every card, the
    ///       deploy bar, the commander's window when it is up
    ///   B2  the key hint row names the key, is drawn at its full size and meets nothing
    ///   B3  the button's star, words and key cap are inside it and apart; the cap shows the key
    ///   B4  every card's chip says the staged best result and is inside its card; no table tab or plate hangs on a card
    ///   P1  a click on the button opens the popup on the chosen difficulty, with ui_panel_open
    ///   P2  the plate is inside the screen, centred, and sorts above the commander's window
    ///   P3  every text of the popup is inside the plate (the table's inside the view), at its full size, and no two texts or
    ///       controls meet
    ///   P4  blocks in the order Desert, Jungle, Car Park; rows, cells, the marked best row and the "no victory" line equal
    ///       the staged table
    ///   P5  no click falls through: cards, BACK, DEPLOY, the difficulty buttons, the head start row and the button itself
    ///       are under the popup; a click on the plate changes nothing
    ///   P6  the HARD tab by click: ui_tab_switch, three full tables, the view scrolls, the chosen difficulty stays
    ///   P7  scrolled to the end: the last row is inside the view
    ///   P8  left / left / left: MEDIUM, EASY (three "no victory" lines, nothing to scroll), EASY again with ui_disabled
    ///   P9  the close button closes it with ui_panel_close
    ///   K   the real key opens it, ESC closes it and the battlefield screen stays, ENTER deploys nothing while it is open,
    ///       the key closes it (real key events; when they do not reach the game the same handler is called and K fails)
    ///   P10 a click on the scrim closes it; with HARD chosen it opens on HARD
    /// The tables come from a file of its own in the temporary folder (the player's table is not touched): MEDIUM Desert 10
    /// entries, Jungle none, Car Park 3; HARD 10 each; EASY none. The first entry of a table carries the widest numbers
    /// (25 000, 1:23:45, 999, 16/16) and a full table shows the longest rank names of the ladder, the widest texts of the
    /// RANK column. Ranks are always asked from OzHighScores.Rank. The entries are made-up results: they exist in that file
    /// only, and it is deleted at the end.
    ///
    /// In a level (about three minutes; start it in a running level with the HUD up, test mode off): three endings are staged
    /// one after the other (the end screen's own Show; a victory enters the staged table): a victory that is the level's
    /// record (the staged table is still empty), then, with a better made-up result put into the table, the defeat and a
    /// victory that is not the record. The record is looked at in the first two sizes, the defeat and the last victory in
    /// all five, each with the commander's line playing in his end-screen window:
    ///   S   the Game view has the size                 E1  the commander's window is up
    ///   E2  the stat plates, the chart, the score strip, the three buttons, the key hint, the two flank plates, the eyebrow
    ///       chip, the subtitle, the hazard bands (defeat) and the commander's window: none meets another, all on the screen
    ///   E3  inside the strip: star, SCORE, the number, the rule, RANK, the rank, the note and TOP 10 are inside it, apart,
    ///       and no text is cut (a text drawn smaller to fit its box is listed, not failed)
    ///   E4  what the strip says about the table: the defeat's note starts "WIN THE LEVEL", the record's "NEW RECORD", the
    ///       other victory's "PLACE"; the TOP 10 sign stands under the note, and both end at the strip's right margin
    /// It ends the Play session (the level is left on a staged ending).
    ///
    /// Result: one log line "[OpenZ scores popup proof] PASS n/n checks (...)" or "... FAIL ...", also in Report and in
    /// f6b_scores_popup. Pictures are named by screen size. What it changes (Game view size, chosen difficulty, the table
    /// file in use) is put back, also when Play stops in the middle.
    /// </summary>
    public sealed class OzScoresPopupProof : MonoBehaviour
    {
        const string Tag = "[OpenZ scores popup proof]";
        public static string Report = "not run";
        /// <summary>Where the pictures and check files go (empty = see the summary).</summary>
        public static string Folder;
        static readonly int[][] Sizes = { new[] { 1920, 1080 }, new[] { 2560, 1600 }, new[] { 3840, 2160 }, new[] { 1440, 1080 }, new[] { 2560, 1080 } };
        static readonly string[] StripParts = { "Glyph", "Label", "Value", "Rule", "RankLabel", "Rank", "Note", "TopChip" };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Vector3[] corners = new Vector3[4];

        [MenuItem("OpenZ/QC/High Scores Popup Proof (Play)")]
        public static void Run()
        {
            if (!Application.isPlaying) { Debug.LogWarning(Tag + " enter Play mode first (the main menu, or a level)"); return; }
            if (FindFirstObjectByType<OzScoresPopupProof>()) { Debug.LogWarning(Tag + " a run is still going"); return; }
            new GameObject("OzScoresPopupProof").AddComponent<OzScoresPopupProof>();
        }

        OpenZMainMenu menu;
        int ok, bad, pictures, gv0 = -1;
        readonly StringBuilder fails = new StringBuilder(), lines = new StringBuilder();
        string dir, tablePath, view0, sizeInfo = "";
        bool staged, kept, sizeOk, keysReal = true;
        GameDifficulty diff0;

        // ------------------------------------------------------------------ helpers
        static float U => Screen.height / 1080f;                                                           // screen pixels per canvas unit

        static Rect Px(RectTransform rt)
        {
            rt.GetWorldCorners(corners);                                                                    // overlay canvas: world = screen pixels
            return Rect.MinMaxRect(Mathf.Min(corners[0].x, corners[2].x), Mathf.Min(corners[0].y, corners[2].y), Mathf.Max(corners[0].x, corners[2].x), Mathf.Max(corners[0].y, corners[2].y));
        }

        /// <summary>The line box of a text (false: no text component, hidden, or nothing drawn): from the first letter's start to
        /// the end of the last letter's step, which carries the letter spacing that follows it, and from the descender line to the
        /// ascender line. Wider and taller than the letters themselves: the careful measure for texts side by side and in rows.</summary>
        static bool Ink(Component c, out Rect r)
        {
            r = default;
            var t = c ? c.GetComponent<TMP_Text>() : null;
            if (!t || !t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text)) return false;
            t.ForceMeshUpdate();
            var b = t.textBounds; Vector3 a = t.transform.TransformPoint(b.min), z = t.transform.TransformPoint(b.max);
            r = Rect.MinMaxRect(Mathf.Min(a.x, z.x), Mathf.Min(a.y, z.y), Mathf.Max(a.x, z.x), Mathf.Max(a.y, z.y));
            return r.width > 0.01f && r.height > 0.01f;
        }

        /// <summary>The drawn letters of a text: the box around its glyphs as they are drawn (false as for Ink). For lines of
        /// capitals that stand under each other: a line box holds empty room under its capitals (down to the descender line), so
        /// two such lines are reported as touching while their letters are far apart.</summary>
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

        static bool Meet(Rect a, Rect b, float slack = 0.5f) => a.xMin < b.xMax - slack && b.xMin < a.xMax - slack && a.yMin < b.yMax - slack && b.yMin < a.yMax - slack;
        static bool Inside(Rect a, Rect b, float slack = 0.5f) => a.xMin >= b.xMin - slack && a.xMax <= b.xMax + slack && a.yMin >= b.yMin - slack && a.yMax <= b.yMax + slack;
        static string Plain(string s) => Regex.Replace(s ?? "", "<[^>]+>", "");

        /// <summary>A screen rectangle in canvas units, top-left origin (as the layout numbers are written).</summary>
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

        void Check(bool pass, string what)
        {
            if (pass) ok++; else { bad++; fails.Append("\n  FAIL ").Append(what); }
            lines.Append(pass ? "PASS " : "FAIL ").AppendLine(what);
        }

        static string OutDir()
        {
            if (!string.IsNullOrEmpty(Folder)) return Path.GetFullPath(Folder);
            string all = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones")), stage = "v1_1";
            if (Directory.Exists(all))
            {
                var ds = Directory.GetDirectories(all, "v1_1*");
                Array.Sort(ds, StringComparer.Ordinal);
                if (ds.Length > 0) stage = Path.GetFileName(ds[ds.Length - 1]);
            }
            return Path.Combine(all, stage, "f6b_scores_popup");
        }

        IEnumerator SetSize(int w, int h)
        {
            string info;
            OzFinalGateView.SetFixed(w, h, "OpenZ " + w + "x" + h, out info);
            float t0 = Time.realtimeSinceStartup;
            while ((Screen.width != w || Screen.height != h) && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            yield return Wait(0.5f);
            sizeOk = Screen.width == w && Screen.height == h; sizeInfo = info;
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            try { File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG()); pictures++; }
            catch (Exception ex) { Debug.LogWarning(Tag + " picture " + name + " not written: " + ex.Message); }
            Destroy(tex);
        }

        /// <summary>A key down for three frames, then up (a real key event; it reaches the game only while the Game view has the keyboard).</summary>
        static IEnumerator Tap(Key k)
        {
            var kb = Keyboard.current;
            if (kb == null) yield break;
            try { InputSystem.QueueStateEvent(kb, new KeyboardState(k)); } catch (Exception) { }
            yield return null; yield return null; yield return null;
            try { InputSystem.QueueStateEvent(kb, new KeyboardState()); } catch (Exception) { }
            yield return null; yield return null;
        }

        /// <summary>The commander's transmission window (a canvas of its own under ZodBriefing), or null; its alpha.</summary>
        static RectTransform ZodWindow(out float alpha)
        {
            alpha = 0f;
            var zb = ZodBriefing.I; var zc = zb ? zb.transform.Find("ZodComms") : null; var zw = zc ? zc.Find("ZodWindow") as RectTransform : null;
            if (!zw || !zw.gameObject.activeInHierarchy) return null;
            var g = zw.GetComponent<CanvasGroup>(); alpha = g ? g.alpha : 1f;
            return zw;
        }

        /// <summary>Waits until no line of the commander is playing and his window is gone (at most max seconds).</summary>
        static IEnumerator ZodQuiet(float max)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < max)
            {
                float a; var w = ZodWindow(out a);
                if (ZodBriefing.DebugLineId == null && (!w || a < 0.02f)) yield break;
                yield return null;
            }
        }

        void PutBack()
        {
            if (staged)
            {
                staged = false;
                OzHighScores.UseFile(null);                                                                 // the player's own table again
                try { if (tablePath != null && File.Exists(tablePath)) File.Delete(tablePath); } catch (Exception) { }
            }
            if (!kept) return;
            kept = false;
            if (menu)
            {
                if (menu.ProofHighScoresOpen) menu.ProofHighScores(false);
                if (OpenZDifficulty.Level != diff0) OpenZDifficulty.Level = diff0;
                if (view0 == "Main" && menu.ProofView == "Select") menu.ProofMain();
            }
            OzFinalGateView.Select(gv0);
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
                File.WriteAllText(Path.Combine(dir, "checks_" + where.Replace(' ', '_').Replace(',', '_') + ".txt"), lines.ToString());
            }
            catch (Exception ex) { Debug.LogWarning(Tag + " could not write the check files: " + ex.Message); }
        }

        IEnumerator Start()
        {
            dir = OutDir(); Directory.CreateDirectory(dir);
            var gv = Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) EditorWindow.FocusWindowIfItsOpen(gv);
            menu = FindFirstObjectByType<OpenZMainMenu>();
            var hud = OpenZHud.I;
            if (menu) yield return MenuPart();
            else if (hud && hud.Canvas) yield return LevelPart(hud);
            else { Check(false, "a main menu or a level is needed"); Finish("no scene", "open the MainMenu scene or a level, then Play"); }
            Destroy(gameObject);
        }

        // ================================================================== the battlefield screen
        /// <summary>The ladder's longest rank names (the most letters; asked from OzHighScores.Rank over every score a result
        /// can have): the widest texts of the RANK column.</summary>
        static List<string> LongestRanks()
        {
            var all = new List<string>(); int most = 0;
            int top = OzHighScores.VictoryPoints + OzHighScores.SpeedMax + OzHighScores.CombatMax + OzHighScores.TerritoryMax;
            for (int s = 0; s <= top; s += 250) { string n = OzHighScores.Rank(s); if (!all.Contains(n)) { all.Add(n); most = Mathf.Max(most, n.Length); } }
            all.RemoveAll(n => n.Length < most);
            return all;
        }

        static OzScoreEntry Entry(int i) => new OzScoreEntry
        {
            score = 25000 - 1300 * i, seconds = i == 0 ? 5025 : 400 + 61 * i,
            kills = i == 0 ? 999 : 40 - i, losses = i == 0 ? 999 : 10 + i, built = i == 0 ? 999 : 60 + i, zones = i == 0 ? 16 : 7 - i % 3, zonesTotal = i == 0 ? 16 : 9,
        };

        /// <summary>MEDIUM: Desert 10, Jungle none, Car Park 3. HARD: 10 each. EASY: none. On a file of its own.</summary>
        void Stage()
        {
            tablePath = Path.Combine(Application.temporaryCachePath, "oz_scores_popup_proof.json");
            if (File.Exists(tablePath)) File.Delete(tablePath);
            OzHighScores.UseFile(tablePath); staged = true;
            var ids = OzLevelDef.All;
            for (int i = 0; i < 10; i++) OzHighScores.Submit(ids[0].Id, GameDifficulty.Medium, Entry(i));
            for (int i = 0; i < 3; i++) OzHighScores.Submit(ids[ids.Length - 1].Id, GameDifficulty.Medium, Entry(i));
            foreach (var d in ids) for (int i = 0; i < 10; i++) OzHighScores.Submit(d.Id, GameDifficulty.Hard, Entry(i));
        }

        IEnumerator MenuPart()
        {
            diff0 = OpenZDifficulty.Level; view0 = menu.ProofView; gv0 = OzFinalGateView.SelectedIndex(); kept = true;
            Stage();
            foreach (var s in Sizes)
            {
                string at = s[0] + "x" + s[1];
                yield return SetSize(s[0], s[1]);
                Check(sizeOk, at + " S the Game view has this size (it has " + Screen.width + "x" + Screen.height + "; " + sizeInfo + ")");
                if (!sizeOk) continue;
                if (menu.ProofView != "Select") menu.ProofSelect();
                if (OpenZDifficulty.Level != GameDifficulty.Medium) OpenZDifficulty.Level = GameDifficulty.Medium;
                yield return Wait(1.5f);                                                                     // the rebuild, the cards' entrance, the chips' next look-up
                if (menu.ProofView != "Select" || !menu.ProofSelectRoot || !menu.ProofHighScoresButton) { Check(false, at + " the battlefield screen with its HIGH SCORES button is up (view " + menu.ProofView + ")"); continue; }
                ButtonChecks(at);
                yield return Shot(at + "_1_battlefield");
                yield return PopupChecks(at);
            }
            PutBack();
            Finish("battlefield screen", "5 screen sizes; real key events " + (keysReal ? "reached the game" : "did NOT reach the game: click into the Game view and run it again (the key's own handler was called instead)"));
        }

        /// <summary>Everything of the screen the button or the hint row could meet.</summary>
        List<(string name, Rect r)> Around(RectTransform sel)
        {
            var l = new List<(string name, Rect r)>(); Rect r;
            var back = sel.Find("Back") as RectTransform; if (back) l.Add(("BACK", Px(back)));
            // the header's three lines by their drawn letters: on a 4:3 screen the key hint row runs under the title's last
            // letters on a line of its own, 12 u under the capitals; the line boxes of the two overlap by 1.3 u of empty room
            if (Drawn(sel.Find("Title"), out r)) l.Add(("the title", r));
            if (Drawn(sel.Find("Eyebrow"), out r)) l.Add(("the eyebrow", r));
            if (Drawn(sel.Find("Keys"), out r)) l.Add(("the key hints", r));
            var bar = sel.Find("DeployBar") as RectTransform; if (bar) l.Add(("the deploy bar", Px(bar)));
            for (int i = 0; i < menu.ProofCardCount; i++) { var c = menu.ProofCard(i); if (c) l.Add(("card " + (i + 1), Px(c))); }
            float za; var zw = ZodWindow(out za); if (zw && za >= 0.02f) l.Add(("the commander's window", Px(zw)));
            return l;
        }

        void ButtonChecks(string at)
        {
            var sel = menu.ProofSelectRoot; var btn = menu.ProofHighScoresButton;
            Rect screen = new Rect(0, 0, Screen.width, Screen.height), b = Px(btn);
            var around = Around(sel);
            string key = OzKeyNames.Name(OpenZMainMenu.HsKey);

            // B1 the button against everything of the screen
            var hit = new StringBuilder(); float gapBack = -1f, gapKeys = -1f, gapCard = 9999f; int cardsSeen = 0;
            foreach (var o in around)
            {
                if (Meet(b, o.r)) hit.Append(' ').Append(o.name).Append(" (").Append(R(o.r)).Append(')');
                if (o.name == "BACK") gapBack = (o.r.xMin - b.xMax) / U;
                if (o.name == "the key hints") gapKeys = (b.yMin - o.r.yMax) / U;
                if (o.name.StartsWith("card ")) { cardsSeen++; gapCard = Mathf.Min(gapCard, (b.yMin - o.r.yMax) / U); }
            }
            Check(Inside(b, screen) && hit.Length == 0 && gapBack >= 8f && gapKeys >= 8f && cardsSeen == OzLevelDef.All.Length && gapCard >= 8f,
                  at + " B1 the HIGH SCORES button (" + R(b) + ") is inside the screen and meets nothing" + (hit.Length > 0 ? "; it MEETS" + hit : "")
                  + "; BACK " + gapBack.ToString("0", Inv) + " u to its right, the key hints " + gapKeys.ToString("0", Inv) + " u under it, the nearest of " + cardsSeen + " cards " + gapCard.ToString("0", Inv) + " u under it");

            // B2 the key hint row
            var kt = sel.Find("Keys") ? sel.Find("Keys").GetComponent<TMP_Text>() : null;
            Rect kr = default; bool kink = kt && Drawn(kt, out kr);
            var hit2 = new StringBuilder();
            if (kink) foreach (var o in around) if (o.name != "the key hints" && Meet(kr, o.r)) hit2.Append(' ').Append(o.name);
            if (kink && Meet(kr, b)) hit2.Append(" the HIGH SCORES button");
            bool full = kt && kt.fontSize >= OzText.MinSize - 0.01f && kt.overflowMode != TextOverflowModes.Ellipsis;
            Check(kink && kt.text.Contains(key + "  HIGH SCORES") && full && Inside(kr, screen) && hit2.Length == 0,
                  at + " B2 the key hint row names the key (\"" + key + "  HIGH SCORES\"), is drawn at its full size" + (kt ? " (" + kt.fontSize.ToString("0.0", Inv) + " u)" : "") + " and meets nothing"
                  + (kink ? " (" + R(kr) + ")" : " (no text)") + (hit2.Length > 0 ? "; it MEETS" + hit2 : ""));

            // B3 the button's own parts
            var lab = btn.Find("Content/Label"); var cap = btn.Find("Content/Key") as RectTransform; var star = btn.Find("Content/Glyph") as RectTransform;
            Rect lr = default; bool li = Ink(lab, out lr);
            var lt = lab ? lab.GetComponent<TMP_Text>() : null; var ct = cap ? cap.GetComponentInChildren<TMP_Text>() : null;
            bool parts = li && cap && star && Inside(lr, b) && Inside(Px(cap), b) && Inside(Px(star), b) && !Meet(lr, Px(cap)) && !Meet(lr, Px(star)) && !Meet(Px(cap), Px(star))
                         && lt && lt.fontSize >= OzText.MinSize - 0.01f && lt.overflowMode != TextOverflowModes.Ellipsis;
            Check(parts && ct && ct.text == key && lt.text == "HIGH SCORES",
                  at + " B3 the button's star, words and key cap are inside it and apart; the cap shows \"" + (ct ? ct.text : "?") + "\" (the key is " + key + ")"
                  + (li ? "; words " + R(lr) : "") + (cap ? ", cap " + R(Px(cap)) : "") + (star ? ", star " + R(Px(star)) : ""));

            // B4 the chips on the cards (labels only: no table tab, no table plate)
            var chipsBad = new StringBuilder(); var chipsAll = new StringBuilder();
            for (int i = 0; i < menu.ProofCardCount; i++)
            {
                var card = menu.ProofCard(i); if (!card) { chipsBad.Append(" card " + (i + 1) + " missing"); continue; }
                var ct2 = card.Find("BestChip"); var chip = ct2 ? ct2.GetComponent<OzBestChip>() : null;
                var best = OzHighScores.Best(OzLevelDef.All[i].Id, GameDifficulty.Medium);
                string result = best != null ? OzHighScores.Group(best.score) + " · " + OzHighScores.Rank(best.score) : "";
                string fullLine = best != null ? "BEST · MEDIUM · " + result : "NO RECORD YET · MEDIUM", shortLine = best != null ? "BEST · " + result : fullLine;
                string shown = chip ? Plain(chip.Shown) : "(no chip)";
                chipsAll.Append(" | ").Append(OzLevelDef.All[i].Id).Append(": ").Append(shown);
                if (!chip || (shown != fullLine && shown != shortLine)) chipsBad.Append(" " + OzLevelDef.All[i].Id + " says \"" + shown + "\", expected \"" + fullLine + "\"");
                else if (!Inside(Px(ct2 as RectTransform), Px(card), 1f)) chipsBad.Append(" " + OzLevelDef.All[i].Id + " chip leaves its card");
                if (card.Find("BestTop")) chipsBad.Append(" " + OzLevelDef.All[i].Id + " still has the TOP 10 tab");
            }
            foreach (Transform ch in sel) if (ch.name.StartsWith("BestTable_", StringComparison.Ordinal)) chipsBad.Append(" a table plate (" + ch.name + ") is still on the screen");
            Check(chipsBad.Length == 0, at + " B4 every card's chip says the best result on MEDIUM and is inside its card; no table hangs on a card" + chipsAll + (chipsBad.Length > 0 ? "; WRONG:" + chipsBad : ""));
        }

        /// <summary>The popup's rows against the table itself. "" = the same.</summary>
        string Compare(GameDifficulty d)
        {
            var sb = new StringBuilder(); var ids = OzLevelDef.All;
            if (menu.ProofHighScoresLevels != ids.Length) return " " + menu.ProofHighScoresLevels + " blocks for " + ids.Length + " levels";
            for (int li = 0; li < ids.Length; li++)
            {
                var list = OzHighScores.Top(ids[li].Id, d); int want = Mathf.Min(list.Count, OzHighScores.TableSize), got = menu.ProofHighScoresRows(li);
                if (got != want) { sb.Append(" " + ids[li].Id + ": " + got + " rows, the table has " + want); continue; }
                string none = "NO VICTORY ON THIS LEVEL ON " + OpenZDifficulty.Label(d) + " YET";
                if (want == 0 && Plain(menu.ProofHighScoresEmpty(li)) != none) sb.Append(" " + ids[li].Id + ": the empty line reads \"" + Plain(menu.ProofHighScoresEmpty(li)) + "\"");
                for (int r = 0; r < want; r++)
                {
                    var e = list[r];
                    string[] cells =
                    {
                        (r + 1) + ".", OzHighScores.Rank(e.score), OzHighScores.Group(e.score), UnitInfo.Duration(e.seconds),
                        e.kills.ToString(Inv), e.losses.ToString(Inv), e.built.ToString(Inv), e.zonesTotal > 0 ? e.zones + "/" + e.zonesTotal : "-",
                    };
                    for (int c = 0; c < cells.Length; c++)
                        if (Plain(menu.ProofHighScoresCell(li, r, c)) != cells[c]) sb.Append(" " + ids[li].Id + " row " + (r + 1) + " column " + (c + 1) + ": \"" + Plain(menu.ProofHighScoresCell(li, r, c)) + "\", the table says \"" + cells[c] + "\"");
                    if (menu.ProofHighScoresBest(li, r) != (r == 0)) sb.Append(" " + ids[li].Id + " row " + (r + 1) + ": the best mark is " + (r == 0 ? "missing" : "on a lower row"));
                }
            }
            return sb.ToString();
        }

        IEnumerator PopupChecks(string at)
        {
            var sel = menu.ProofSelectRoot; var btn = menu.ProofHighScoresButton;
            Rect screen = new Rect(0, 0, Screen.width, Screen.height);

            // P1 a click on the button
            float t0 = Time.unscaledTime; Vector2 bp = Px(btn).center;
            string onBtn = menu.ProofHighScoresHot(bp);
            menu.ProofClickAt(bp);
            yield return Wait(0.5f);
            Check(onBtn == "button" && menu.ProofHighScoresOpen && menu.ProofHighScoresTab == 1 && Heard(t0, "ui_panel_open"),
                  at + " P1 a click on the button opens the popup on the chosen difficulty: pointer on \"" + onBtn + "\", open " + menu.ProofHighScoresOpen + ", tab " + menu.ProofHighScoresTab + " (1 = MEDIUM), ui_panel_open " + Heard(t0, "ui_panel_open"));
            if (!menu.ProofHighScoresOpen) { menu.ProofHighScores(true); yield return Wait(0.5f); }
            var panel = menu.ProofHighScoresPanel; var view = menu.ProofHighScoresView;
            if (!menu.ProofHighScoresOpen || !panel || !view) { Check(false, at + " P2 the popup's plate exists"); yield break; }
            Rect pr = Px(panel), vr = Px(view);

            // P2 the plate
            var cv = menu.ProofHighScoresCanvas; int under = ZodLayout.Quip().sorting;
            Check(Inside(pr, screen) && Mathf.Abs(pr.center.x - Screen.width * 0.5f) <= 2f && Mathf.Abs(pr.center.y - Screen.height * 0.5f) <= 2f && cv && cv.overrideSorting && cv.sortingOrder > under,
                  at + " P2 the plate (" + R(pr) + ") is inside the screen and centred; it sorts at " + (cv ? cv.sortingOrder.ToString(Inv) : "?") + ", above the commander's window (" + under + ")");

            // P3 texts and controls
            var texts = new List<OzText>(); menu.ProofHighScoresTexts(texts);
            var boxes = new List<(string name, Rect r)>(); var wrong = new StringBuilder(); int measured = 0;
            foreach (var t in texts)
            {
                var tt = t.graphic as TMP_Text; Rect r;
                if (!tt || !Ink(tt, out r)) continue;
                measured++;
                string name = t.gameObject.name + " \"" + Plain(t.text) + "\"";
                if (tt.fontSize < t.Size - 0.01f || tt.overflowMode == TextOverflowModes.Ellipsis) wrong.Append("\n    shrunk or cut: ").Append(name);
                if (!Inside(r, pr)) wrong.Append("\n    leaves the plate: ").Append(name);
                if (t.rectTransform.IsChildOf(view) && !Inside(r, vr, 1f)) wrong.Append("\n    leaves the view: ").Append(name);
                boxes.Add((name, r));
            }
            for (int i = 0; i < 3; i++) { var tr = menu.ProofHighScoresTabRt(i); if (tr) boxes.Add(("tab " + i, Px(tr))); else wrong.Append("\n    tab " + i + " missing"); }
            var cl = menu.ProofHighScoresClose; if (cl) boxes.Add(("the close button", Px(cl))); else wrong.Append("\n    the close button is missing");
            for (int i = 0; i < boxes.Count; i++)
            {
                if (!Inside(boxes[i].r, pr)) { if (!boxes[i].name.Contains("\"")) wrong.Append("\n    leaves the plate: ").Append(boxes[i].name); }
                for (int j = i + 1; j < boxes.Count; j++)
                    if (Meet(boxes[i].r, boxes[j].r)) wrong.Append("\n    meet: ").Append(boxes[i].name).Append(" and ").Append(boxes[j].name);
            }
            Check(measured >= 100 && wrong.Length == 0, at + " P3 the popup's " + measured + " texts, 3 tabs and close button: inside the plate (the table's inside the view), at their full size, none meets another" + wrong);

            // P4 order and contents (MEDIUM: Desert 10, Jungle none, Car Park 3)
            string diffM = Compare(GameDifficulty.Medium);
            bool order = true; var ids = OzLevelDef.All;
            for (int li = 0; li + 1 < ids.Length; li++)
            {
                var a = menu.ProofHighScoresBlock(li); var c = menu.ProofHighScoresBlock(li + 1);
                if (!a || !c || Px(a).yMin < Px(c).yMax - 0.5f) order = false;                              // a block ends above the next one's top
            }
            bool widest = menu.ProofHighScoresColumns == 8 && Plain(menu.ProofHighScoresCell(0, 0, 1)) == OzHighScores.Rank(25000) && Plain(menu.ProofHighScoresCell(0, 0, 2)) == "25 000"
                          && Plain(menu.ProofHighScoresCell(0, 0, 3)) == "1:23:45" && Plain(menu.ProofHighScoresCell(0, 0, 4)) == "999" && Plain(menu.ProofHighScoresCell(0, 0, 5)) == "999"
                          && Plain(menu.ProofHighScoresCell(0, 0, 6)) == "999" && Plain(menu.ProofHighScoresCell(0, 0, 7)) == "16/16";
            var longest = LongestRanks(); var shownRanks = new List<string>(); string longMissing = "";      // the widest texts of the RANK column: the full table of the first level must show them
            for (int r = 0; r < menu.ProofHighScoresRows(0); r++) shownRanks.Add(Plain(menu.ProofHighScoresCell(0, r, 1)));
            foreach (var n in longest) if (!shownRanks.Contains(n)) longMissing += " " + n;
            Check(diffM == "" && order && widest && longMissing == "" && menu.ProofHighScoresRows(0) == 10 && menu.ProofHighScoresRows(1) == 0 && menu.ProofHighScoresRows(ids.Length - 1) == 3 && ids[0].Id == "L02" && ids[ids.Length - 1].Id == "L17" && menu.ProofHighScoresOverflow < 0.5f,
                  at + " P4 blocks in the order " + ids[0].Name + ", " + ids[1].Name + ", " + ids[ids.Length - 1].Name + " (" + order + "); MEDIUM rows " + menu.ProofHighScoresRows(0) + " / " + menu.ProofHighScoresRows(1) + " / " + menu.ProofHighScoresRows(ids.Length - 1)
                  + ", the first row marked, every cell of the " + menu.ProofHighScoresColumns + " columns equal to the table, the widest numbers in row 1 (" + widest + "), the longest rank names in the table (" + string.Join(", ", longest)
                  + (longMissing.Length > 0 ? "; MISSING:" + longMissing : "") + "), \"" + Plain(menu.ProofHighScoresEmpty(1)) + "\"" + (diffM.Length > 0 ? "; DIFFERENT:" + diffM : ""));

            // P5 no click falls through
            var pts = new List<(string name, Vector2 p)>();
            for (int i = 0; i < menu.ProofCardCount; i++) { var c = menu.ProofCard(i); if (c) pts.Add(("card " + (i + 1), Px(c).center)); }
            foreach (var n in new[] { "Back", "Deploy", "CpuEasy", "CpuMedium", "CpuHard", "HeadRow" }) { var t = sel.Find(n) as RectTransform; if (t) pts.Add((n, Px(t).center)); }
            pts.Add(("HighScores", bp));
            var leak = new StringBuilder();
            foreach (var q in pts)
            {
                string h = menu.ProofHighScoresHot(q.p);
                if (!(h == "scrim" || h == "plate" || h == "close" || h.StartsWith("tab", StringComparison.Ordinal))) leak.Append(' ').Append(q.name).Append(" -> ").Append(h);
            }
            int focus0 = menu.ProofFocusCard; string onPlate = menu.ProofHighScoresHot(pr.center);
            menu.ProofClickAt(pr.center);
            yield return null; yield return null;
            Check(leak.Length == 0 && pts.Count == menu.ProofCardCount + 7 && onPlate == "plate" && menu.ProofHighScoresOpen && menu.ProofFocusCard == focus0 && OpenZDifficulty.Level == GameDifficulty.Medium && menu.ProofView == "Select",
                  at + " P5 no click falls through: " + pts.Count + " places of the screen under the popup" + (leak.Length > 0 ? "; REACHABLE:" + leak : "") + "; a click on the plate (\"" + onPlate + "\") changes nothing (open " + menu.ProofHighScoresOpen + ", card " + menu.ProofFocusCard + ")");
            yield return Shot(at + "_2_popup_medium");

            // P6 the HARD tab by a click
            t0 = Time.unscaledTime;
            var tab2 = menu.ProofHighScoresTabRt(2); Vector2 tp = tab2 ? Px(tab2).center : Vector2.zero; string onTab = tab2 ? menu.ProofHighScoresHot(tp) : "none";
            if (onTab == "tab2") menu.ProofClickAt(tp);
            yield return Wait(0.3f);
            string diffH = Compare(GameDifficulty.Hard); float over = menu.ProofHighScoresOverflow;
            Check(onTab == "tab2" && menu.ProofHighScoresTab == 2 && Heard(t0, "ui_tab_switch") && diffH == "" && menu.ProofHighScoresRows(1) == 10 && over > 1f && menu.ProofHighScoresTrackOn && OpenZDifficulty.Level == GameDifficulty.Medium,
                  at + " P6 the HARD tab by a click (\"" + onTab + "\"): tab " + menu.ProofHighScoresTab + ", ui_tab_switch " + Heard(t0, "ui_tab_switch") + ", three full tables equal to the table file, " + over.ToString("0", Inv) + " u to scroll, the thumb shown " + menu.ProofHighScoresTrackOn
                  + ", the chosen difficulty still " + OpenZDifficulty.Label(OpenZDifficulty.Level) + (diffH.Length > 0 ? "; DIFFERENT:" + diffH : ""));
            yield return Shot(at + "_3_popup_hard_top");

            // P7 scrolled to the end
            menu.ProofHighScoresScroll(1f);
            yield return null; yield return null;
            var last = menu.ProofHighScoresRow(menu.ProofHighScoresLevels - 1, OzHighScores.TableSize - 1); var first = menu.ProofHighScoresRow(0, 0);
            Check(last && first && Inside(Px(last), Px(view), 1f) && !Inside(Px(first), Px(view), 1f),
                  at + " P7 scrolled to the end: the last row of the last level is inside the view" + (last ? " (" + R(Px(last)) + " in " + R(Px(view)) + ")" : " (no row)") + ", the first row has left it");
            yield return Shot(at + "_4_popup_hard_end");

            // P8 the tabs by the keys' own handler
            t0 = Time.unscaledTime;
            menu.ProofHighScoresInput(false, false, true, false); yield return Wait(0.15f); int k1 = menu.ProofHighScoresTab;
            menu.ProofHighScoresInput(false, false, true, false); yield return Wait(0.15f); int k2 = menu.ProofHighScoresTab;
            float t1 = Time.unscaledTime;
            menu.ProofHighScoresInput(false, false, true, false); yield return Wait(0.15f); int k3 = menu.ProofHighScoresTab;
            string diffE = Compare(GameDifficulty.Easy);
            Check(k1 == 1 && k2 == 0 && k3 == 0 && Heard(t0, "ui_tab_switch") && Heard(t1, "ui_disabled") && diffE == "" && menu.ProofHighScoresRows(0) == 0 && menu.ProofHighScoresOverflow < 0.5f && !menu.ProofHighScoresTrackOn,
                  at + " P8 left, left, left: tabs " + k1 + ", " + k2 + ", " + k3 + " (MEDIUM, EASY, EASY with ui_disabled " + Heard(t1, "ui_disabled") + "); EASY shows three \"no victory\" lines and nothing to scroll" + (diffE.Length > 0 ? "; DIFFERENT:" + diffE : ""));
            yield return Shot(at + "_5_popup_easy_empty");

            // P9 the close button
            t0 = Time.unscaledTime;
            Vector2 cp = cl ? Px(cl).center : Vector2.zero; string onClose = cl ? menu.ProofHighScoresHot(cp) : "none";
            if (onClose == "close") menu.ProofClickAt(cp);
            yield return Wait(0.4f);
            Check(onClose == "close" && !menu.ProofHighScoresOpen && !menu.ProofHighScoresShown && Heard(t0, "ui_panel_close"),
                  at + " P9 the close button (\"" + onClose + "\") closes it: open " + menu.ProofHighScoresOpen + ", shown " + menu.ProofHighScoresShown + ", ui_panel_close " + Heard(t0, "ui_panel_close"));
            if (menu.ProofHighScoresOpen) { menu.ProofHighScores(false); yield return Wait(0.4f); }

            // K the real keys
            yield return Tap(OpenZMainMenu.HsKey);
            if (!menu.ProofHighScoresOpen) { keysReal = false; menu.ProofHighScoresInput(true, false); }
            yield return Wait(0.3f);
            bool a1 = menu.ProofHighScoresOpen;
            if (a1) { if (keysReal) yield return Tap(Key.Escape); else menu.ProofHighScoresInput(false, true); }
            yield return Wait(0.3f);
            bool a2 = a1 && !menu.ProofHighScoresOpen && menu.ProofView == "Select";
            if (keysReal) yield return Tap(OpenZMainMenu.HsKey); else menu.ProofHighScoresInput(true, false);
            yield return Wait(0.3f);
            bool a3 = menu.ProofHighScoresOpen;
            if (a3 && keysReal) { yield return Tap(Key.Enter); yield return Wait(0.2f); }
            bool a4 = menu.ProofView == "Select" && menu.ProofHighScoresOpen;
            if (a3) { if (keysReal) yield return Tap(OpenZMainMenu.HsKey); else menu.ProofHighScoresInput(true, false); }
            yield return Wait(0.4f);
            bool a5 = !menu.ProofHighScoresOpen;
            Check(a1 && a2 && a3 && a4 && a5 && keysReal,
                  at + " K the key " + OzKeyNames.Name(OpenZMainMenu.HsKey) + " opens it (" + a1 + "), ESC closes it and the battlefield screen stays (" + a2 + "), the key opens it again (" + a3 + "), ENTER deploys nothing (" + a4 + "), the key closes it (" + a5 + ")"
                  + (keysReal ? ", by real key events" : ": the key events did NOT reach the game, the key's own handler was called instead"));
            if (menu.ProofView != "Select") yield break;                                                    // the screen was left: the rest cannot be asked
            if (menu.ProofHighScoresOpen) { menu.ProofHighScores(false); yield return Wait(0.4f); }

            // P10 the scrim; the tab it opens on
            menu.ProofHighScores(true);
            yield return Wait(0.4f);
            Vector2 sp = new Vector2(3f, Screen.height * 0.5f); string onScrim = menu.ProofHighScoresHot(sp);
            if (onScrim == "scrim") menu.ProofClickAt(sp);
            yield return Wait(0.4f);
            bool s1 = onScrim == "scrim" && !menu.ProofHighScoresOpen;
            if (menu.ProofHighScoresOpen) { menu.ProofHighScores(false); yield return Wait(0.4f); }
            OpenZDifficulty.Level = GameDifficulty.Hard;
            menu.ProofHighScores(true);
            yield return Wait(0.3f);
            int openedOn = menu.ProofHighScoresTab;
            menu.ProofHighScores(false);
            OpenZDifficulty.Level = GameDifficulty.Medium;
            yield return Wait(0.4f);
            Check(s1 && openedOn == 2 && !menu.ProofHighScoresOpen,
                  at + " P10 a click on the scrim (\"" + onScrim + "\") closes it (" + s1 + "); with HARD chosen it opens on tab " + openedOn + " (2 = HARD)");
        }

        // ================================================================== the end screen
        IEnumerator LevelPart(OpenZHud hud)
        {
            const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
            var es = hud.EndScreen; var game = hud.game;
            var show = typeof(OpenZEndScreen).GetMethod("Show", BF);
            var shownProp = typeof(OpenZEndScreen).GetProperty("IsShown", BF);
            var setShown = shownProp != null ? shownProp.GetSetMethod(true) : null;
            var submitted = typeof(OpenZEndScreen).GetField("scoreSubmitted", BF);
            if (!es || !game || game.World == null || show == null || setShown == null)
            {
                Check(false, "the end screen can be staged here (end screen " + (es ? "found" : "missing") + ", its Show " + (show != null ? "found" : "missing") + ")");
                Finish("end screen", "nothing staged"); yield break;
            }
            if (game.World.GameOver || es.IsShown || hud.Modal || !hud.hudVisible)
            {
                Check(false, "a running level with the HUD up and no menu open is needed (game over " + game.World.GameOver + ", end screen " + es.IsShown + ")");
                Finish("end screen", "nothing staged"); yield break;
            }
            gv0 = OzFinalGateView.SelectedIndex(); kept = true;
            tablePath = Path.Combine(Application.temporaryCachePath, "oz_scores_popup_proof.json");
            if (File.Exists(tablePath)) File.Delete(tablePath);
            OzHighScores.UseFile(tablePath); staged = true;                                                 // a table of its own, empty: no staged result ever enters the player's
            Team me = hud.Player, foe = me == Team.Red ? Team.Blue : Team.Red;
            yield return ZodQuiet(40f);
            string[] kinds = { "victory_record", "defeat", "victory" };                                     // the first victory is the empty table's record; then a better result goes in: the defeat names it as the best, the second victory stands under it
            for (int k = 0; k < kinds.Length; k++)
            {
                string kind = kinds[k]; bool win = kind != "defeat";
                if (k == 1) OzHighScores.Submit(game.level, game.gameDifficulty, new OzScoreEntry { score = 25000, seconds = 61, kills = 40, losses = 10, built = 60, zones = 9, zonesTotal = 9 });   // a better, made-up result: in the staged table only
                for (var old = GameObject.Find("OZ_EndScreen"); old; old = GameObject.Find("OZ_EndScreen")) DestroyImmediate(old);   // the last staging's grade
                if (es.IsShown) setShown.Invoke(es, new object[] { false });
                if (submitted != null) submitted.SetValue(es, false);
                show.Invoke(es, new object[] { win ? me : foe });
                yield return Wait(4.6f);                                                                    // the reveal is over, the strip is live
                for (int si = 0; si < (k == 0 ? 2 : Sizes.Length); si++)
                {
                    var s = Sizes[si];
                    string at = s[0] + "x" + s[1] + " " + kind.Replace('_', ' ');
                    yield return SetSize(s[0], s[1]);
                    Check(sizeOk, at + " S the Game view has this size (it has " + Screen.width + "x" + Screen.height + "; " + sizeInfo + ")");
                    if (!sizeOk) continue;
                    yield return ZodQuiet(30f);
                    string said = ZodBriefing.DebugPunchline(win);
                    RectTransform zw = null; float za = 0f, w0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - w0 < 5f) { zw = ZodWindow(out za); if (zw && za >= 0.98f) break; yield return null; }
                    yield return Wait(0.6f);                                                                // his window's slide-in is over
                    zw = ZodWindow(out za);
                    EndChecks(hud, at, kind, zw, za, said);
                    yield return Shot(s[0] + "x" + s[1] + "_end_" + kind);
                }
            }
            string level = game.level;
            PutBack();
            Finish("end screen, level " + level, "a record victory, the defeat and a victory under a better result staged with the commander's window (5 screen sizes, the record at 2); test mode " + (GameBoot.TestMode ? "ON (a victory then enters no table: switch it off)" : "off"));
            EditorApplication.isPlaying = false;                                                             // the level stands on a staged ending
        }

        /// <summary>kind: "defeat", "victory_record" (the victory that is the level's record) or "victory" (under a better result).</summary>
        void EndChecks(OpenZHud hud, string at, string kind, RectTransform zw, float za, string said)
        {
            bool win = kind != "defeat";
            var root = hud.transform.Find("OpenZ_EndScreen/Root") as RectTransform;
            if (!root) { Check(false, at + " E1 the end screen's root is found"); return; }
            Rect screen = new Rect(0, 0, Screen.width, Screen.height), r;
            bool window = zw && za >= 0.98f;
            Check(window, at + " E1 the commander's window is up (" + said + (zw ? "; " + R(Px(zw)) + ", alpha " + za.ToString("0.00", Inv) : "; no window") + ")");

            // E2 every rectangle of the screen
            var parts = new List<(string name, Rect r)>(); int stats = 0, buttons = 0, flanks = 0, hazards = 0; bool chart = false, strip = false, chip = false, hint = false, sub = false;
            foreach (Transform ch in root)
            {
                if (!ch.gameObject.activeInHierarchy) continue;
                string n = ch.name; var rt = ch as RectTransform;
                if (n == "Hint") { if (Ink(ch, out r)) { parts.Add(("the key hint", r)); hint = true; } }
                else if (n == "Subtitle") { if (Ink(ch, out r)) { parts.Add(("the subtitle", r)); sub = true; } }
                else if (!rt) continue;
                else if (n.StartsWith("Stat", StringComparison.Ordinal)) { parts.Add(("stat plate " + n.Substring(4), Px(rt))); stats++; }
                else if (n.StartsWith("Btn", StringComparison.Ordinal)) { parts.Add(("button " + n.Substring(3), Px(rt))); buttons++; }
                else if (n == "Chart") { parts.Add(("the chart", Px(rt))); chart = true; }
                else if (n == "Score") { parts.Add(("the score strip", Px(rt))); strip = true; }
                else if (n == "Flank") { parts.Add(("flank plate " + (++flanks), Px(rt))); }
                else if (n == "Eyebrow") { parts.Add(("the eyebrow chip", Px(rt))); chip = true; }
                else if (n == "HazardTop" || n == "HazardBottom") { parts.Add((n == "HazardTop" ? "the upper hazard band" : "the lower hazard band", Px(rt))); hazards++; }
            }
            if (window) parts.Add(("the commander's window", Px(zw)));
            var wrong = new StringBuilder(); var all = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                all.Append("\n    ").Append(parts[i].name).Append(": ").Append(R(parts[i].r));
                if (!Inside(parts[i].r, screen, 1f)) wrong.Append("\n    OFF THE SCREEN: ").Append(parts[i].name);
                for (int j = i + 1; j < parts.Count; j++)
                    if (Meet(parts[i].r, parts[j].r)) wrong.Append("\n    MEET: ").Append(parts[i].name).Append(" and ").Append(parts[j].name);
            }
            bool whole = stats == 4 && buttons == 3 && flanks == 2 && chart && strip && chip && hint && sub && hazards == (win ? 0 : 2);
            Check(whole && wrong.Length == 0, at + " E2 " + parts.Count + " rectangles (4 stat plates " + (stats == 4) + ", chart " + chart + ", score strip " + strip + ", 3 buttons " + (buttons == 3) + ", key hint " + hint + ", 2 flank plates " + (flanks == 2)
                  + ", eyebrow chip " + chip + ", subtitle " + sub + ", hazard bands " + hazards + ", the commander's window " + window + "): none meets another, all on the screen" + wrong + all);

            // E3 inside the strip
            var sr0 = root.Find("Score") as RectTransform;
            if (!sr0) { Check(false, at + " E3 the score strip is found"); return; }
            Rect sr = Px(sr0); var inner = new List<(string name, Rect r)>(); var wrong2 = new StringBuilder(); var smaller = new StringBuilder(); string noteText = "";
            foreach (var n in StripParts)
            {
                var c = sr0.Find(n); if (!c || !c.gameObject.activeInHierarchy) continue;
                var tt = c.GetComponent<TMP_Text>();
                if (tt)
                {
                    if (n == "Note") noteText = Plain(tt.text);
                    if (!Ink(tt, out r)) continue;
                    if (tt.overflowMode == TextOverflowModes.Ellipsis) wrong2.Append("\n    CUT: ").Append(n).Append(" \"").Append(Plain(tt.text)).Append('"');
                    float nominal = n == "Value" ? 34f : n == "Rank" ? 20f : n == "Note" ? 12f : 11f;
                    if (tt.fontSize < nominal - 0.01f) smaller.Append(" " + n + " \"" + Plain(tt.text) + "\" at " + tt.fontSize.ToString("0.0", Inv) + " of " + nominal.ToString("0", Inv) + " u;");
                    inner.Add((n + " \"" + Plain(tt.text) + "\"", r));
                }
                else inner.Add((n, Px(c as RectTransform)));
            }
            for (int i = 0; i < inner.Count; i++)
            {
                if (!Inside(inner[i].r, sr, 1f)) wrong2.Append("\n    LEAVES THE STRIP: ").Append(inner[i].name);
                for (int j = i + 1; j < inner.Count; j++)
                    if (Meet(inner[i].r, inner[j].r)) wrong2.Append("\n    MEET: ").Append(inner[i].name).Append(" and ").Append(inner[j].name);
            }
            Check(inner.Count >= 8 && wrong2.Length == 0, at + " E3 inside the score strip (" + R(sr) + "): " + inner.Count + " parts inside it, apart, no text cut" + (smaller.Length > 0 ? "; drawn smaller to fit its box:" + smaller : "") + wrong2);

            // E4 what the strip says about the table; the TOP 10 sign under the note, both ending at the strip's right margin (16 u)
            var topRt = sr0.Find("TopChip") as RectTransform; Rect tr = topRt ? Px(topRt) : default, nr;
            bool noteInk = Ink(sr0.Find("Note"), out nr);
            float margin = topRt ? (sr.xMax - tr.xMax) / U : -999f, ends = topRt && noteInk ? Mathf.Abs(nr.xMax - tr.xMax) / U : 999f, under = topRt && noteInk ? (nr.yMin - tr.yMax) / U : -999f;
            int place = hud.EndScreen ? hud.EndScreen.ProofPlace : -1;
            string want = !win ? "WIN THE LEVEL" : kind == "victory_record" ? "NEW RECORD" : "PLACE ";
            bool entered = !win ? place == 0 : kind == "victory_record" ? place == 1 : place > 1;
            Check(noteText.StartsWith(want, StringComparison.Ordinal) && entered && topRt && Mathf.Abs(margin - 16f) <= 1f && ends <= 3f && under >= 2f,
                  at + " E4 the note reads \"" + noteText + "\" (it must start \"" + want.Trim() + "\"; place in the staged table " + place + "); the TOP 10 sign ends " + margin.ToString("0.0", Inv) + " u before the strip's right edge (16), "
                  + ends.ToString("0.0", Inv) + " u from the note's end and stands " + under.ToString("0.0", Inv) + " u under the note"
                  + (win && place <= 0 ? "; with test mode on, a CPU on the player's side or the testing overlay used, a victory enters no table" : ""));
        }
    }
}
#endif
