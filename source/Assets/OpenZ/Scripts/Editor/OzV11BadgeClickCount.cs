using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of kit K429: a click on a building's production badge (the medallion with its time and TECH plate, the reminder's
    /// ring and "!" chip) does what a click on the building does.
    /// Menu: OpenZ/V1.1/K429 Badge Click Check (Play mode), or OpenZ.EditorTools.OzV11BadgeClickCount.Run().
    /// Play mode in a level, a running match, no menu open, the Game view in front. It ends with one log line
    /// "[OpenZ V1.1 K429 badge click] PASS n/n checks (...)" or "... FAIL ..." (also in Report and in checks.txt: Folder, by default the
    /// opened from the badge.
    ///
    /// The menus are COUNTED (OpenZProductionWindow.Opens), the sounds read from the sound log. For the own fort and one own factory:
    ///  B0  the badge's clickable box holds a point off the building's own click rectangle (where the click failed before)
    ///  B1  a click on the building itself: 1 menu (the reference, and its sounds)
    ///  B2  a click on that badge point: 1 menu, of that building; the selection kept; a press counted on the badge
    ///  B3  the same sounds as the click on the building
    ///  B4  a second click on the badge with its menu open: 0 more, still open
    ///  B5  a press on the badge that travels past the click slop: 0 menus (it is a box)
    ///  B6  with BadgeClicks off: 0 menus (the old way: the badge was a part of the map)
    ///  B7  a factory with the reminder on (marked for the run, unmarked after): a click on the "!" chip above the medallion: 1 menu
    ///  T1  a tap on the badge through the touch layer, an own unit selected: 1 menu, no order, the selection kept
    /// Nothing real is touched: the check adds a mouse and a touch screen of its own (OzK429Mouse, OzK429Touch), queues their events like
    /// a device would and switches the editor's own mouse off for the run; the touch setting, the reminder mark and the devices are put
    /// back when it ends, also when a step throws.
    /// </summary>
    public static class OzV11BadgeClickCount
    {
        const string Tag = "[OpenZ V1.1 K429 badge click]";
        public static string Report = "not run";
        /// <summary></summary>
        public static string Folder;
        /// <summary>The view's height over a factory and over the fort.</summary>
        public static float FactoryHeight = 40f, FortHeight = 70f;

        static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        static int waitFrame, ok, bad, touchId = 900;
        static float waitUntil;
        static readonly StringBuilder fails = new StringBuilder();
        static string dir;
        static Touchscreen touch; static Mouse mouse, sysMouse;
        static bool kept; static int mode0, help0;
        static int markedId = -1;

        [MenuItem("OpenZ/V1.1/K429 Badge Click Check (Play mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning(Tag + " enter Play mode first (a level)"); return; }
            if (stack.Count > 0) { Debug.LogWarning(Tag + " a run is still going"); return; }
            var hud = OpenZHud.I;
            if (!hud || !hud.Canvas || hud.W == null) { Report = Tag + " FAIL: no HUD in this scene (a level is needed)"; Debug.LogError(Report); return; }
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) EditorWindow.FocusWindowIfItsOpen(gv);
            ok = bad = 0; fails.Clear(); Report = "running";
            dir = OutDir(); Directory.CreateDirectory(dir);
            stack.Push(Level(hud));
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
            return Path.Combine(all, stage, "k429_badge_click");
        }

        static void Step()
        {
            if (stack.Count == 0 || !EditorApplication.isPlaying) { stack.Clear(); PutBack(); EditorApplication.update -= Step; return; }
            if (Time.frameCount < waitFrame || Time.realtimeSinceStartup < waitUntil) return;
            var top = stack.Peek(); bool more;
            try { more = top.MoveNext(); }
            catch (System.Exception ex) { Debug.LogException(ex); stack.Clear(); PutBack(); Report = Tag + " FAIL: exception " + ex.Message; Debug.LogError(Report); EditorApplication.update -= Step; return; }
            if (!more) { stack.Pop(); if (stack.Count == 0) EditorApplication.update -= Step; return; }
            if (top.Current is IEnumerator child) stack.Push(child);
            else if (top.Current is float s) waitUntil = Time.realtimeSinceStartup + s;
            else waitFrame = Time.frameCount + (top.Current is int n ? n : 1);
        }

        static void Check(bool pass, string what) { if (pass) ok++; else { bad++; fails.Append(" | ").Append(what); } Debug.Log($"{Tag} {(pass ? "ok  " : "FAIL")} {what}"); }

        static void Finish(string where, string detail)
        {
            PutBack();
            bool pass = bad == 0 && ok > 0;
            Report = $"{Tag} {(pass ? "PASS" : "FAIL")} {ok}/{ok + bad} checks ({where}, {Screen.width}x{Screen.height}): {detail}{(bad == 0 ? "" : "; FAILED:" + fails)}";
            if (pass) Debug.Log(Report); else Debug.LogError(Report);
            File.AppendAllText(Path.Combine(dir, "checks.txt"), Report + "\n");
        }

        // ------------------------------------------------------------------ devices and the reminder mark of the check
        static void Devices()
        {
            mode0 = PlayerPrefs.GetInt(OzTouch.Pref, 0); help0 = PlayerPrefs.GetInt(OzTouch.HelpPref, 0); kept = true;
            OzTouch.Setting = OzTouch.Mode.Auto;
            PlayerPrefs.SetInt(OzTouch.HelpPref, 1);                                                   // the first-time touch card would cover the map
            sysMouse = Mouse.current;
            mouse = InputSystem.AddDevice<Mouse>("OzK429Mouse");
            if (sysMouse != null && sysMouse != mouse) InputSystem.DisableDevice(sysMouse);
            touch = InputSystem.AddDevice<Touchscreen>("OzK429Touch");
            MouseEv(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), 0);
        }

        static void PutBack()
        {
            OpenZUnitOverlay.BadgeClicks = true;
            Unmark();
            if (!kept) return;
            kept = false;
            OzTouch.Setting = (OzTouch.Mode)Mathf.Clamp(mode0, 0, 2);
            PlayerPrefs.SetInt(OzTouch.HelpPref, help0);
            if (touch != null && touch.added) InputSystem.RemoveDevice(touch);
            if (sysMouse != null) { InputSystem.EnableDevice(sysMouse); sysMouse.MakeCurrent(); }
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            touch = null; mouse = null; sysMouse = null;
            PlayerPrefs.Save();
        }

        static System.Collections.IDictionary Marks() =>
            typeof(OzFactoryReminder).GetField("marks", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as System.Collections.IDictionary;

        /// <summary>Puts the reminder on this factory for the run (as if the player had just captured it).</summary>
        static bool Mark(ZObject f)
        {
            var marks = Marks(); var t = typeof(OzFactoryReminder).GetNestedType("Mark", BindingFlags.NonPublic);
            if (marks == null || t == null || !OzFactoryReminder.Eligible(f)) return false;
            if (marks.Contains(f.Id)) return true;                                                       // marked by the match itself: left as it is
            marks[f.Id] = System.Activator.CreateInstance(t, true); markedId = f.Id;
            return true;
        }

        static void Unmark() { var marks = Marks(); if (markedId >= 0 && marks != null && marks.Contains(markedId)) marks.Remove(markedId); markedId = -1; }

        static void TouchEv(int id, TouchPhase ph, Vector2 p) => InputSystem.QueueStateEvent(touch, new TouchState { touchId = id, phase = ph, position = p });
        static void MouseEv(Vector2 p, int buttons)
        {
            var st = new MouseState { position = p };
            if ((buttons & 1) != 0) st = st.WithButton(MouseButton.Left);
            InputSystem.QueueStateEvent(mouse, st);
        }

        static IEnumerator MouseClick(Vector2 p) { MouseEv(p, 0); yield return 3; MouseEv(p, 1); yield return 3; MouseEv(p, 0); yield return 8; }
        static IEnumerator MouseSlip(Vector2 a, Vector2 b) { MouseEv(a, 0); yield return 3; MouseEv(a, 1); yield return 3; MouseEv(b, 1); yield return 3; MouseEv(b, 0); yield return 8; }
        static IEnumerator Tap(Vector2 p) { int id = ++touchId; TouchEv(id, TouchPhase.Began, p); yield return 3; TouchEv(id, TouchPhase.Ended, p); yield return 8; }

        // ------------------------------------------------------------------ points on the screen
        static bool OnScreen(Vector2 p, float m = 14f) => p.x > m && p.y > m && p.x < Screen.width - m && p.y < Screen.height - m;

        static int OwnUnder(OpenZHud hud, Vector2 p)
        {
            int n = 0;
            foreach (var o in hud.W.Objects)
                if (o.Owner == hud.Player && !o.Destroyed && o.KillAt < 0 && (o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon) && !o.IsFortDefender
                    && hud.input.ScreenRect(o).Contains(p)) n++;
            return n;
        }

        /// <summary>A point of box r that is NOT on the building's own click rectangle, free of panels and own units, and that the game
        /// takes as that building now (with `reach` px to its right also so, for the travel test).</summary>
        static bool OffBuilding(OpenZHud hud, ZObject P, Rect r, float reach, out Vector2 bp)
        {
            var mesh = hud.input.ScreenRect(P);
            for (int iy = 8; iy >= 1; iy--) for (int ix = 1; ix < 9; ix++)                              // from the top: the medallion first
            {
                bp = new Vector2(Mathf.Lerp(r.xMin, r.xMax, ix / 9f), Mathf.Lerp(r.yMin, r.yMax, iy / 9f));
                if (!OnScreen(bp) || mesh.Contains(bp) || hud.IsOverUi(bp) || OwnUnder(hud, bp) > 0) continue;
                if (reach > 0f && (mesh.Contains(bp + new Vector2(reach, 0f)) || hud.IsOverUi(bp + new Vector2(reach, 0f)))) continue;
                if (hud.Production.ProducerUnder(bp) == P) return true;
            }
            bp = r.center; return false;
        }

        /// <summary>A point of the building's own click rectangle off its badge, free of panels and own units.</summary>
        static bool OnBuilding(OpenZHud hud, ZObject P, out Vector2 bp)
        {
            var r = hud.input.ScreenRect(P);
            for (int iy = 1; iy < 9; iy++) for (int ix = 1; ix < 9; ix++)
            {
                bp = new Vector2(Mathf.Lerp(r.xMin, r.xMax, ix / 9f), Mathf.Lerp(r.yMin, r.yMax, iy / 9f));
                if (OnScreen(bp) && !hud.IsOverUi(bp) && OwnUnder(hud, bp) == 0 && OpenZUnitOverlay.BadgeUnder(hud.W, hud.Player, bp) == null && hud.Production.ProducerUnder(bp) == P) return true;
            }
            bp = r.center; return false;
        }

        /// <summary>The UI sounds played since t0 (key "ui_..." or the UI bus): a gun or a voice elsewhere on the map at that moment is
        /// not the click's, so B3 does not fail on it.</summary>
        static HashSet<string> Sounds(float t0) =>
            new HashSet<string>(OzSoundLog.Since(t0).Where(e => e.key != null && e.outcome != null && e.outcome.StartsWith("PLAY")
                && (e.key.StartsWith("ui", System.StringComparison.OrdinalIgnoreCase) || e.outcome.EndsWith(" UI"))).Select(e => e.key));

        // ------------------------------------------------------------------ the run
        static IEnumerator Level(OpenZHud hud)
        {
            var W = hud.W; var prod = hud.Production;
            if (hud.Modal || W.GameOver || !hud.rts || !prod || !hud.hudVisible || !hud.input) { Report = Tag + " FAIL: a running match with the HUD shown and no menu open is needed"; Debug.LogError(Report); yield break; }
            var picks = new List<ZObject>();
            var fort = W.Objects.FirstOrDefault(o => o.IsProducer && o.Owner == hud.Player && !o.Destroyed && o.IsFort);
            var fac = W.Objects.FirstOrDefault(o => o.IsProducer && o.Owner == hud.Player && !o.Destroyed && !o.IsFort);
            if (fort != null) picks.Add(fort); if (fac != null) picks.Add(fac);
            if (picks.Count == 0) { Report = Tag + " FAIL: the player owns no fort and no factory in this match"; Debug.LogError(Report); yield break; }
            Devices();
            int menus0 = OpenZProductionWindow.Opens, hits0 = OpenZUnitOverlay.BadgeClickHits;
            foreach (var P in picks) yield return Building(hud, P);
            if (prod.IsOpen) prod.Close();
            Finish("level " + hud.game.level, $"{string.Join(" + ", picks.Select(p => p.Type))}; menus opened in the run {OpenZProductionWindow.Opens - menus0}, presses on a badge {OpenZUnitOverlay.BadgeClickHits - hits0}; " +
                $"click slop {OpenZHud.BuildingClickSlopPx:0.0} px; badge alpha needed {OpenZUnitOverlay.ClickMinAlpha:0.00}");
        }

        static IEnumerator Look(OpenZHud hud, ZObject P)
        {
            if (hud.Production.IsOpen) hud.Production.Close();
            hud.input.Selection.Clear();
            hud.rts.focus = hud.game.CenterWorld(P); hud.rts.SetHeightImmediate(P.IsFort ? FortHeight : FactoryHeight);
            yield return 0.4f; yield return 6;
        }

        static IEnumerator Shut(OpenZHud hud) { if (hud.Production.IsOpen) hud.Production.Close(); yield return 0.3f; yield return 3; }

        static void SelectOne(OpenZHud hud, ZObject unit) { hud.input.Selection.Clear(); if (unit != null) hud.input.Selection.Add(unit); }

        static IEnumerator Building(OpenZHud hud, ZObject P)
        {
            var input = hud.input; var prod = hud.Production; string n = P.Type;
            yield return Look(hud, P);
            var unit = hud.W.OwnedSelectable(hud.Player).FirstOrDefault(o => !o.IsShuttle && o.Kind != ZKind.Building);   // an own unit to keep selected (none: the selection checks see an empty one)
            float reach = Mathf.Ceil(OpenZHud.BuildingClickSlopPx) + 4f;
            bool gotR = OpenZUnitOverlay.BadgeClickRect(P.Id, out var br);
            Vector2 bp = br.center;
            bool gotP = gotR && OffBuilding(hud, P, br, reach, out bp);
            bool gotB = OnBuilding(hud, P, out var onB);
            Check(gotR && gotP, $"{n} B0 the badge's box ({br.xMin:0},{br.yMin:0} to {br.xMax:0},{br.yMax:0}) holds a point off the building's own rectangle that the game takes as the building ({(gotP ? $"{bp.x:0},{bp.y:0}" : "none")})");
            if (!gotR || !gotP || !gotB) { Check(false, $"{n}: could not be set up (badge drawn {gotR}, a point off the building {gotP}, a point on the building {gotB}); move the view and run again"); yield break; }
            int o0, h0; float t0;

            SelectOne(hud, unit); yield return 2; o0 = OpenZProductionWindow.Opens; t0 = Time.unscaledTime;
            yield return MouseClick(onB);
            var refSounds = Sounds(t0);
            Check(OpenZProductionWindow.Opens == o0 + 1 && prod.Building == P, $"{n} B1 a click on the building: {OpenZProductionWindow.Opens - o0} menu(s), open {prod.Building == P}; sounds [{string.Join(", ", refSounds)}]");
            yield return Shut(hud);

            SelectOne(hud, unit); yield return 2; o0 = OpenZProductionWindow.Opens; h0 = OpenZUnitOverlay.BadgeClickHits; t0 = Time.unscaledTime;
            yield return MouseClick(bp);
            var badgeSounds = Sounds(t0);
            bool kept = unit == null ? input.Selection.Count == 0 : input.Selection.Count == 1 && input.Selection.Contains(unit);
            Check(OpenZProductionWindow.Opens == o0 + 1 && prod.Building == P && kept && OpenZUnitOverlay.BadgeClickHits == h0 + 1,
                $"{n} B2 a click on the badge: {OpenZProductionWindow.Opens - o0} menu(s), open {prod.Building == P}, selection kept {kept} ({input.Selection.Count} selected), presses on the badge {OpenZUnitOverlay.BadgeClickHits - h0}");
            Check(refSounds.Count > 0 && badgeSounds.SetEquals(refSounds), $"{n} B3 the badge click's sounds [{string.Join(", ", badgeSounds)}] = the building click's [{string.Join(", ", refSounds)}]");
            yield return 0.5f;
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"k429_{n}_menu_from_badge.png"));
            yield return 3;

            o0 = OpenZProductionWindow.Opens;
            bool open0 = prod.Building == P;
            Vector2 bp2 = bp;
            bool gotP2 = OpenZUnitOverlay.BadgeClickRect(P.Id, out var br2) && OffBuilding(hud, P, br2, 0f, out bp2);
            if (!gotP2) bp2 = bp;
            if (open0) yield return MouseClick(bp2);
            Check(open0 && OpenZProductionWindow.Opens == o0 && prod.Building == P, $"{n} B4 a second click on the badge, menu open: {OpenZProductionWindow.Opens - o0} more, still open {prod.Building == P}");
            yield return Shut(hud);

            SelectOne(hud, unit); yield return 2; o0 = OpenZProductionWindow.Opens; int d0 = OpenZHud.BuildingDrags;
            yield return MouseSlip(bp, bp + new Vector2(reach, 0f));
            Check(OpenZProductionWindow.Opens == o0 && !prod.IsOpen && OpenZHud.BuildingDrags == d0 + 1,
                $"{n} B5 a press on the badge that travels {reach:0} px is a box: {OpenZProductionWindow.Opens - o0} menus, hand-overs to the box {OpenZHud.BuildingDrags - d0}");
            yield return Shut(hud);

            OpenZUnitOverlay.BadgeClicks = false;
            SelectOne(hud, unit); yield return 2; o0 = OpenZProductionWindow.Opens;
            yield return MouseClick(bp);
            Check(OpenZProductionWindow.Opens == o0 && !prod.IsOpen, $"{n} B6 with BadgeClicks off the same click opens {OpenZProductionWindow.Opens - o0} menus (the old way)");
            OpenZUnitOverlay.BadgeClicks = true;
            yield return Shut(hud);

            if (OzFactoryReminder.Eligible(P))
            {
                bool marked = Mark(P);
                yield return 0.3f; yield return 4;
                bool gotC = OpenZUnitOverlay.BadgeChipRect(P.Id, out var cr);
                Vector2 cp = default; bool gotCp = false;
                if (gotC)
                {
                    OpenZUnitOverlay.BadgeClickRect(P.Id, out var bodyR); var mesh = input.ScreenRect(P);
                    foreach (var f in new[] { new Vector2(0.5f, 0.85f), new Vector2(0.4f, 0.8f), new Vector2(0.6f, 0.8f), new Vector2(0.5f, 0.7f), new Vector2(0.5f, 0.5f) })
                    {   // the "!" chip's upper part, above the badge body's own rect where it can be: the part that stands out
                        cp = new Vector2(cr.xMin + cr.width * f.x, cr.yMin + cr.height * f.y);
                        if (OnScreen(cp) && !mesh.Contains(cp) && !hud.IsOverUi(cp) && OwnUnder(hud, cp) == 0 && hud.Production.ProducerUnder(cp) == P) { gotCp = true; if (!bodyR.Contains(cp)) break; }
                    }
                }
                SelectOne(hud, unit); yield return 2; o0 = OpenZProductionWindow.Opens;
                if (gotCp) yield return MouseClick(cp);
                Check(marked && gotCp && OpenZProductionWindow.Opens == o0 + 1 && prod.Building == P,
                    $"{n} B7 the reminder on, a click on its \"!\" chip ({(gotCp ? $"{cp.x:0},{cp.y:0}" : "none")}): {OpenZProductionWindow.Opens - o0} menu(s), open {prod.Building == P} (marked {marked}, chip drawn {gotC})");
                yield return 0.5f;
                if (gotCp) ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"k429_{n}_menu_from_reminder_chip.png"));
                yield return 3;
                Unmark();
                yield return Shut(hud);
            }

            if (OzTouch.Now != OzTouch.Driver.Touch)
            {   // the first touch takes the pointer and brings the on-screen keys: the badge point is taken again after that
                for (int iy = 2; iy < 8; iy++) for (int ix = 2; ix < 8; ix++)
                {
                    var g = new Vector2(Screen.width * ix / 10f, Screen.height * iy / 10f);
                    if (!hud.IsOverUi(g) && hud.Production.ProducerUnder(g) == null && OwnUnder(hud, g) == 0) { input.Selection.Clear(); yield return Tap(g); iy = 9; break; }
                }
                yield return 0.6f;
            }
            Vector2 tp = bp;
            bool gotT = OpenZUnitOverlay.BadgeClickRect(P.Id, out var br3) && OffBuilding(hud, P, br3, 0f, out tp);
            if (!gotT) tp = bp;
            SelectOne(hud, unit); yield return 2; o0 = OpenZProductionWindow.Opens; int ot0 = OzTouch.OrderTaps;
            if (gotT && OzTouch.Now == OzTouch.Driver.Touch) yield return Tap(tp);
            bool keptT = unit == null ? input.Selection.Count == 0 : input.Selection.Count == 1 && input.Selection.Contains(unit);
            Check(gotT && OzTouch.Now == OzTouch.Driver.Touch && OpenZProductionWindow.Opens == o0 + 1 && prod.Building == P && OzTouch.OrderTaps == ot0 && keptT,
                $"{n} T1 a tap on the badge: {OpenZProductionWindow.Opens - o0} menu(s), open {prod.Building == P}, order taps {OzTouch.OrderTaps - ot0}, selection kept {keptT} (pointer {OzTouch.Now})");
            yield return Shut(hud);
            input.Selection.Clear();
        }
    }
}
