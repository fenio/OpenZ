using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    /// Counted check of kit b30: the build menu of an own fort / factory must not open on a box-select.
    /// Menu: OpenZ/V1.1/Count Build Menu On Drag-Select (b30, Play mode), or OpenZ.EditorTools.OzV11MenuOnDragCount.Run().
    /// Play mode in a level, a running match, no menu open. It ends with one log line
    /// "[OpenZ V1.1 b30 menu on drag-select] PASS n/n checks (...)" or "... FAIL ..." (also in Report and in checks.txt: Folder,
    ///
    /// The menus are COUNTED: OpenZProductionWindow.Opens goes up by one each time a build menu is opened, by whatever route.
    /// For the own fort and for one own factory (when the player has one), 15 checks each:
    ///  M (10)  the mouse: a box that starts on the building, a box that ends on it, a box across it: 0 menus each and the
    ///          box selected the unit in it; a plain click on the building: 1 menu, the selection kept; a second click on it:
    ///          0 more, still open; with the menu open a box from the building: the menu closes, 0 opened; a click that slips
    ///          less than the click slop: 1 menu; a press that travels just past the slop: 0 menus, it was a box; a click on a
    ///          unit that stands in front of the building: that unit selected, 0 menus; a long still press: 1 menu.
    ///  T (5)   the same through the touch layer (OzTouch): a tap on the building: 1 menu, the selection kept; a finger drag
    ///          that starts on it, ends on it, crosses it: 0 menus each; a tap on the unit in front of it: selected, 0 menus.
    ///
    /// Nothing real is touched: the check adds a mouse and a touch screen of its own (OzB30Mouse, OzB30Touch), queues their
    /// events like a device would and switches the editor's own mouse off for the run. Keep the Game view in front.
    /// It stages two lone grunts per building (one with its pick box over the building's click rectangle: in front of the building
    /// where that rectangle reaches the ground there, else beside or behind it; one on open ground near it) with the game's own
    /// calls, as the testing overlay does; they cannot walk during the run, walk again afterwards and stay in the match, which
    /// is marked as not ranked. The touch setting, the first-time touch card mark and the devices are put back when it ends,
    /// also when a step throws.
    /// </summary>
    public static class OzV11MenuOnDragCount
    {
        const string Tag = "[OpenZ V1.1 b30 menu on drag-select]";
        public static string Report = "not run";
        /// <summary></summary>
        public static string Folder;
        /// <summary>"fort" or "factory": only that building in this run (one clean run each, a fresh Play session each); empty = both.</summary>
        public static string Only = "";
        /// <summary>The view's height over a factory (a small building: from 70 the open menu covers it) and over the fort.</summary>
        public static float FactoryHeight = 40f, FortHeight = 70f;

        sealed class Stage { public ZObject P, front, open; public int frontSpeed, openSpeed; public string name; }

        static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        static readonly List<Stage> stages = new List<Stage>();
        static int waitFrame, ok, bad, touchId = 300;
        static float waitUntil;
        static readonly StringBuilder fails = new StringBuilder();
        static string dir;
        static Touchscreen touch; static Mouse mouse, sysMouse;
        static bool kept; static int mode0, help0; static Vector2 mousePos;

        [MenuItem("OpenZ/V1.1/Count Build Menu On Drag-Select (b30, Play mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning(Tag + " enter Play mode first (a level)"); return; }
            if (stack.Count > 0) { Debug.LogWarning(Tag + " a run is still going"); return; }
            var hud = OpenZHud.I;
            if (!hud || !hud.Canvas || hud.W == null) { Report = Tag + " FAIL: no HUD in this scene (a level is needed)"; Debug.LogError(Report); return; }
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) EditorWindow.FocusWindowIfItsOpen(gv);
            ok = bad = 0; fails.Clear(); stages.Clear(); Report = "running";
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
            return Path.Combine(all, stage, "b30_menu_on_drag");
        }

        // a step yields an int (frames), a float (seconds of real time) or another step (run to its end first)
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

        // ------------------------------------------------------------------ devices of the check
        static void Devices()
        {
            mode0 = PlayerPrefs.GetInt(OzTouch.Pref, 0); help0 = PlayerPrefs.GetInt(OzTouch.HelpPref, 0); kept = true;
            OzTouch.Setting = OzTouch.Mode.Auto;
            PlayerPrefs.SetInt(OzTouch.HelpPref, 1);                                                   // the first-time touch card would cover the map
            sysMouse = Mouse.current;
            mouse = InputSystem.AddDevice<Mouse>("OzB30Mouse");
            if (sysMouse != null && sysMouse != mouse) InputSystem.DisableDevice(sysMouse);
            touch = InputSystem.AddDevice<Touchscreen>("OzB30Touch");
            mousePos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            MouseEv(mousePos, 0);
        }

        static void PutBack()
        {
            foreach (var s in stages)
            {   // the staged grunts walk again
                if (s.front != null && !s.front.Destroyed) s.front.MoveSpeed = s.frontSpeed;
                if (s.open != null && !s.open.Destroyed) s.open.MoveSpeed = s.openSpeed;
            }
            stages.Clear();
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

        static void TouchEv(int id, TouchPhase ph, Vector2 p) => InputSystem.QueueStateEvent(touch, new TouchState { touchId = id, phase = ph, position = p });
        static void MouseEv(Vector2 p, int buttons)
        {
            var st = new MouseState { position = p };
            if ((buttons & 1) != 0) st = st.WithButton(MouseButton.Left);
            InputSystem.QueueStateEvent(mouse, st); mousePos = p;
        }

        // gestures: the mouse
        static IEnumerator MouseClick(Vector2 p) { MouseEv(p, 0); yield return 3; MouseEv(p, 1); yield return 3; MouseEv(p, 0); yield return 8; }
        static IEnumerator MouseHold(Vector2 p, float seconds) { MouseEv(p, 0); yield return 3; MouseEv(p, 1); yield return seconds; MouseEv(p, 0); yield return 8; }
        static IEnumerator MouseSlip(Vector2 a, Vector2 b) { MouseEv(a, 0); yield return 3; MouseEv(a, 1); yield return 3; MouseEv(b, 1); yield return 3; MouseEv(b, 0); yield return 8; }
        static IEnumerator MouseDrag(Vector2 a, Vector2 b)
        {
            MouseEv(a, 0); yield return 3; MouseEv(a, 1); yield return 3;
            for (int i = 1; i <= 5; i++) { MouseEv(Vector2.Lerp(a, b, i / 5f), 1); yield return 2; }
            MouseEv(b, 0); yield return 8;
        }
        // gestures: one finger
        static IEnumerator Tap(Vector2 p) { int id = ++touchId; TouchEv(id, TouchPhase.Began, p); yield return 3; TouchEv(id, TouchPhase.Ended, p); yield return 8; }
        static IEnumerator Drag(Vector2 a, Vector2 b)
        {
            int id = ++touchId; TouchEv(id, TouchPhase.Began, a); yield return 3;
            for (int i = 1; i <= 5; i++) { TouchEv(id, TouchPhase.Moved, Vector2.Lerp(a, b, i / 5f)); yield return 2; }
            TouchEv(id, TouchPhase.Ended, b); yield return 8;
        }

        // ------------------------------------------------------------------ points on the screen
        static float K => Screen.height / 1080f;
        static bool OnScreen(Vector2 p, float m = 14f) => p.x > m && p.y > m && p.x < Screen.width - m && p.y < Screen.height - m;   // clear of the edge-scroll band

        static bool Pickable(OpenZHud hud, ZObject o) =>
            o.Owner == hud.Player && !o.Destroyed && o.KillAt < 0 && (o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon) && !o.IsFortDefender;

        /// <summary>Own units whose pick box holds the point, other than `but` and its squad.</summary>
        static int OwnUnder(OpenZHud hud, Vector2 p, ZObject but)
        {
            int n = 0;
            foreach (var o in hud.W.Objects)
                if (Pickable(hud, o) && o != but && (but == null || o.Leader != but) && hud.input.ScreenRect(o).Contains(p)) n++;
            return n;
        }

        /// <summary>A point of the building's click rectangle that is the building for the game (its producer under the point, no
        /// panel over it, no own unit on it), together with the point `reach` px to its right.</summary>
        static bool BuildingPoint(OpenZHud hud, ZObject P, float reach, out Vector2 bp)
        {
            var r = hud.input.ScreenRect(P);
            for (int iy = 1; iy < 9; iy++) for (int ix = 1; ix < 9; ix++)
            {
                bp = new Vector2(Mathf.Lerp(r.xMin, r.xMax, ix / 9f), Mathf.Lerp(r.yMin, r.yMax, iy / 9f));
                var q = bp + new Vector2(reach, 0f);
                if (OnScreen(bp) && OnScreen(q) && !hud.IsOverUi(bp) && !hud.IsOverUi(q) && hud.Production.ProducerUnder(bp) == P && hud.Production.ProducerUnder(q) == P
                    && OwnUnder(hud, bp, null) == 0 && OwnUnder(hud, q, null) == 0) return true;
            }
            bp = r.center; return false;
        }

        /// <summary>A point of the staged unit's pick box that also lies on the building's click rectangle (the unit stands in front of
        /// the building, or beside or behind it, with its box over the building on the screen): a click there is on both.</summary>
        static bool FrontPoint(OpenZHud hud, ZObject P, ZObject u, out Vector2 fp)
        {
            var r = hud.input.ScreenRect(u);
            foreach (var o in new[] { new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.7f), new Vector2(0.5f, 0.85f), new Vector2(0.5f, 0.93f), new Vector2(0.3f, 0.85f), new Vector2(0.7f, 0.85f), new Vector2(0.5f, 0.3f), new Vector2(0.3f, 0.5f), new Vector2(0.7f, 0.5f), new Vector2(0.5f, 0.15f), new Vector2(0.25f, 0.2f), new Vector2(0.75f, 0.2f), new Vector2(0.2f, 0.8f), new Vector2(0.8f, 0.8f), new Vector2(0.5f, 0.05f) })
            {
                fp = new Vector2(r.xMin + r.width * o.x, r.yMin + r.height * o.y);
                if (OnScreen(fp) && !hud.IsOverUi(fp) && hud.Production.ProducerUnder(fp) == P && OwnUnder(hud, fp, u) == 0 && OpenZUnitOverlay.BadgeUnder(hud.W, hud.Player, fp) == null) return true;   // K429: not under a badge (a click there is the badge's)
            }
            { var br = hud.input.ScreenRect(P); Debug.Log($"{Tag} no point of the staged unit's box (box {r.xMin:0},{r.yMin:0} to {r.xMax:0},{r.yMax:0}) lies on the {P.Type}'s click rectangle ({br.xMin:0},{br.yMin:0} to {br.xMax:0},{br.yMax:0}) clear of panels and other units"); }
            fp = r.center; return false;
        }

        /// <summary>A free map point beyond the unit on open ground, seen from the building point: the box between the two holds the unit.</summary>
        static bool FarPoint(OpenZHud hud, ZObject u, Vector2 bp, out Vector2 far)
        {
            var c = hud.input.ScreenRect(u).center;
            float sx = c.x >= bp.x ? 1f : -1f, sy = c.y >= bp.y ? 1f : -1f;
            foreach (var o in new[] { new Vector2(60, 60), new Vector2(90, 40), new Vector2(40, 90), new Vector2(120, 120), new Vector2(34, 34) })
            {
                far = c + new Vector2(sx * o.x, sy * o.y) * K;
                if (OnScreen(far) && !hud.IsOverUi(far) && hud.Production.ProducerUnder(far) == null) return true;
            }
            far = c; return false;
        }

        /// <summary>Two free map points on opposite sides of the building's click rectangle: the straight way between them crosses it.</summary>
        static bool CrossPoints(OpenZHud hud, ZObject P, out Vector2 a, out Vector2 b)
        {
            var r = hud.input.ScreenRect(P); float m = 60f * K, t = 20f * K;
            var tries = new[]
            {
                (new Vector2(r.xMin - m, r.center.y), new Vector2(r.xMax + m, r.center.y + t)),
                (new Vector2(r.xMax + m, r.center.y), new Vector2(r.xMin - m, r.center.y + t)),
                (new Vector2(r.center.x, r.yMin - m), new Vector2(r.center.x + t, r.yMax + m)),
                (new Vector2(r.center.x, r.yMax + m), new Vector2(r.center.x + t, r.yMin - m)),
            };
            foreach (var (p, q) in tries)
            {
                a = p; b = q;
                if (OnScreen(a) && OnScreen(b) && !hud.IsOverUi(a) && hud.Production.ProducerUnder(a) == null && hud.Production.ProducerUnder(b) == null
                    && hud.Production.ProducerUnder((a + b) * 0.5f) == P) return true;
            }
            a = b = r.center; return false;
        }

        /// <summary>A plain map point: no panel, no own producer, no own unit (the first touch lands here).</summary>
        static bool GroundPoint(OpenZHud hud, out Vector2 g)
        {
            for (int iy = 2; iy < 8; iy++) for (int ix = 2; ix < 8; ix++)
            {
                g = new Vector2(Screen.width * ix / 10f, Screen.height * iy / 10f);
                if (!hud.IsOverUi(g) && hud.Production.ProducerUnder(g) == null && OwnUnder(hud, g, null) == 0) return true;
            }
            g = default; return false;
        }

        // ------------------------------------------------------------------ the two staged grunts
        /// <summary>Where a robot standing on that tile is seen: the point `up` m above its feet (its pick box is 1.85 m tall).</summary>
        static bool Seen(OpenZHud hud, int tx, int ty, float up, out Vector2 p)
        {
            var cam = hud.rts.GetComponent<Camera>();
            var w = ZGame.PxToWorld(tx * 16 + 8, ty * 16 + 8); w.y = hud.game.GroundY(w.x, w.z) + up;
            var s = cam.WorldToScreenPoint(w); p = new Vector2(s.x, s.y);
            return s.z > 0f && OnScreen(p, 60f * K);
        }

        static bool TileFree(OpenZHud hud, int tx, int ty)
        {
            if (!hud.W.Path.TilePassable(tx, ty, true)) return false;
            int cx = tx * 16 + 8, cy = ty * 16 + 8;
            foreach (var o in hud.W.Objects)
                if (o.IsUnit && !o.Destroyed && Mathf.Abs(o.CenterX - cx) < 20 && Mathf.Abs(o.CenterY - cy) < 20) return false;
            return true;
        }

        /// <summary>A lone grunt of the player, with the game's own calls (ZWorld.CreateRobotGroup + ZGame.SpawnUnitView, as the testing
        /// overlay does). front: on the free tile nearest the camera on which a robot's upper body is seen over the building's click
        /// rectangle (in front of the building where its rectangle reaches the ground there, else beside or behind it);
        /// else: on a free tile 5 to 12 tiles off the building whose place on the screen is plain map.</summary>
        static ZObject StageGrunt(OpenZHud hud, ZObject P, bool front, out int speed)
        {
            speed = 0;
            int x0 = P.LocX >> 4, y0 = P.LocY >> 4, x1 = (P.LocX + P.WidthPix - 1) >> 4, y1 = (P.LocY + P.HeightPix - 1) >> 4;
            int bx = -1, by = -1;
            if (front)
            {   // larger y = nearer the camera (the view looks north): from in front of the building back toward it
                // a robot's pick box is 1.85 m tall: a tile counts when a point of that box (1.6, 1.2 or 0.8 m over its feet) lies on the
                // building's click rectangle; two columns beside the building are searched too (a small factory has little in front)
                foreach (float up in new[] { 1.6f, 1.2f, 0.8f })
                    for (int ty = y1 + 6; ty >= y0 - 3 && bx < 0; ty--)
                        for (int k = 0; k <= x1 - x0 + 4 && bx < 0; k++)
                        {
                            int mid = (x0 + x1) / 2, tx = mid + ((k & 1) == 0 ? k / 2 : -(k + 1) / 2);
                            if (TileFree(hud, tx, ty) && Seen(hud, tx, ty, up, out var p) && !hud.IsOverUi(p) && hud.Production.ProducerUnder(p) == P && OpenZUnitOverlay.BadgeUnder(hud.W, hud.Player, p) == null) { bx = tx; by = ty; }
                        }
                if (bx < 0)
                {   // A small factory's click rectangle is as tight as its footprint: no robot on a free tile beside it has its box on it.
                    // The one place where a unit IS under the pointer on such a building is its door lane (a new unit stands there when
                    // it comes out): the unit is staged on a tile of the building's own two front rows.
                    foreach (float up in new[] { 1.2f, 0.8f, 1.6f })
                        for (int ty = y1; ty >= y1 - 1 && bx < 0; ty--)
                            for (int k = 0; k <= x1 - x0 && bx < 0; k++)
                            {
                                int mid = (x0 + x1) / 2, tx = mid + ((k & 1) == 0 ? k / 2 : -(k + 1) / 2); if (tx < x0 || tx > x1) continue;
                                bool unitThere = false; int cx = tx * 16 + 8, cy = ty * 16 + 8;
                                foreach (var o2 in hud.W.Objects) if (o2.IsUnit && !o2.Destroyed && Mathf.Abs(o2.CenterX - cx) < 20 && Mathf.Abs(o2.CenterY - cy) < 20) unitThere = true;
                                if (!unitThere && Seen(hud, tx, ty, up, out var p) && !hud.IsOverUi(p) && hud.Production.ProducerUnder(p) == P && OpenZUnitOverlay.BadgeUnder(hud.W, hud.Player, p) == null) { bx = tx; by = ty; }
                            }
                    var r = hud.input.ScreenRect(P);
                    Debug.Log($"{Tag} no free tile on which a robot's box lies over the {P.Type}'s click rectangle (rectangle {r.xMin:0},{r.yMin:0} to {r.xMax:0},{r.yMax:0}, tiles {x0},{y0} to {x1},{y1}): " + (bx >= 0 ? $"the unit is staged in the building's door lane, tile {bx},{by}" : "and none in its door lane"));
                }
            }
            else
            {
                for (int r = 5; r <= 12 && bx < 0; r++)
                    for (int ty = y1 + r; ty >= y0 - r && bx < 0; ty--)
                        for (int tx = x0 - r; tx <= x1 + r && bx < 0; tx++)
                        {
                            if (ty != y1 + r && ty != y0 - r && tx != x0 - r && tx != x1 + r) continue;       // the ring's edge only
                            if (TileFree(hud, tx, ty) && Seen(hud, tx, ty, 0.9f, out var p) && !hud.IsOverUi(p) && hud.Production.ProducerUnder(p) == null && OwnUnder(hud, p, null) == 0) { bx = tx; by = ty; }
                        }
            }
            if (bx < 0) return null;
            OzHighScores.Unranked = "CHECK B30 USED · NOT IN THE TABLE";                                    // staged units: this match does not enter the table
            var o = hud.W.CreateRobotGroup("grunt", bx * 16, by * 16, hud.Player, 1);
            if (o == null) return null;
            hud.game.SpawnUnitView(o); foreach (var mm in o.Minions) hud.game.SpawnUnitView(mm);
            speed = o.MoveSpeed; o.MoveSpeed = 0;                                                           // it stands where it was put until the run ends
            Debug.Log($"{Tag} staged a lone grunt {(front ? "in front of" : "beside")} the {P.Type} at tile {bx},{by} (id {o.Id})");
            return o;
        }

        // ------------------------------------------------------------------ the run
        static IEnumerator Level(OpenZHud hud)
        {
            var W = hud.W; var prod = hud.Production;
            if (hud.Modal || W.GameOver || !hud.rts || !prod || !hud.hudVisible || !hud.input) { Report = Tag + " FAIL: a running match with the HUD shown and no menu open is needed"; Debug.LogError(Report); yield break; }
            foreach (var o in W.Objects) if (o.IsProducer && o.Owner == hud.Player && !o.Destroyed && o.Type.StartsWith("fort")) { stages.Add(new Stage { P = o, name = o.Type }); break; }
            foreach (var o in W.Objects) if (o.IsProducer && o.Owner == hud.Player && !o.Destroyed && !o.Type.StartsWith("fort")) { stages.Add(new Stage { P = o, name = o.Type }); break; }
            if (Only == "fort") stages.RemoveAll(s => !s.name.StartsWith("fort")); else if (Only == "factory") stages.RemoveAll(s => s.name.StartsWith("fort"));
            if (stages.Count == 0) { Report = Tag + " FAIL: the player owns no fort and no factory in this match" + (Only != "" ? " (asked for: " + Only + ")" : ""); Debug.LogError(Report); yield break; }
            Devices();
            int menus0 = OpenZProductionWindow.Opens, drags0 = OpenZHud.BuildingDrags;
            foreach (var s in stages.ToArray()) yield return MousePart(hud, s);
            foreach (var s in stages.ToArray()) yield return TouchPart(hud, s);
            if (prod.IsOpen) prod.Close();
            hud.input.Selection.Clear();
            string names = ""; foreach (var s in stages) names += (names.Length > 0 ? " + " : "") + s.name;
            Finish("level " + hud.game.level,
                $"{names}; click slop {OpenZHud.BuildingClickSlopPx:0.0} px (box threshold {Mathf.Sqrt(PlayerInput.BoxSqr):0.0} px); build menus opened in the whole run {OpenZProductionWindow.Opens - menus0}, " +
                $"presses on a building that became a box {OpenZHud.BuildingDrags - drags0}; {OzTouch.Describe()}");
        }

        static IEnumerator Look(OpenZHud hud, Stage s)
        {
            if (hud.Production.IsOpen) hud.Production.Close();
            hud.input.Selection.Clear();
            hud.rts.focus = hud.game.CenterWorld(s.P); hud.rts.SetHeightImmediate(s.name.StartsWith("fort") ? FortHeight : FactoryHeight);
            yield return 0.4f;                                                                              // a closing menu has faded, the view stands
            yield return 6;
        }

        static IEnumerator Shut(OpenZHud hud) { if (hud.Production.IsOpen) hud.Production.Close(); yield return 0.3f; yield return 3; }

        static IEnumerator MousePart(OpenZHud hud, Stage s)
        {
            var input = hud.input; var prod = hud.Production; var P = s.P; string n = s.name;
            yield return Look(hud, s);
            s.front = StageGrunt(hud, P, true, out s.frontSpeed);
            s.open = StageGrunt(hud, P, false, out s.openSpeed);
            yield return 0.6f; yield return 6;                                                              // their models stand
            float slop = OpenZHud.BuildingClickSlopPx, reach = Mathf.Ceil(slop) + 4f;
            Vector2 bp = default, fp = default, far = default, ca = default, cb = default;
            bool gotB = BuildingPoint(hud, P, reach, out bp);
            bool gotF = s.front != null && FrontPoint(hud, P, s.front, out fp);
            bool gotO = s.open != null && gotB && FarPoint(hud, s.open, bp, out far);
            bool gotC = CrossPoints(hud, P, out ca, out cb);
            if (!gotB || !gotO)
            {
                Check(false, $"{n}: the mouse part could not be set up (a free point on the building: {gotB}, a unit on open ground with a free point beyond it: {gotO}); move the view or stage by hand and run again");
                yield break;
            }
            int o0;

            input.Selection.Clear(); yield return 2; o0 = OpenZProductionWindow.Opens; int d0 = OpenZHud.BuildingDrags;
            yield return MouseDrag(bp, far);
            Check(OpenZProductionWindow.Opens == o0 && !prod.IsOpen && input.Selection.Contains(s.open) && OpenZHud.BuildingDrags == d0 + 1,
                $"{n} M1 a box that starts on the building: {OpenZProductionWindow.Opens - o0} menus opened, the box ran from the press ({input.Selection.Count} selected, the unit in the box {(input.Selection.Contains(s.open) ? "selected" : "NOT selected")}, hand-overs {OpenZHud.BuildingDrags - d0})");

            input.Selection.Clear(); yield return 2; o0 = OpenZProductionWindow.Opens;
            yield return MouseDrag(far, bp);
            Check(OpenZProductionWindow.Opens == o0 && !prod.IsOpen && input.Selection.Contains(s.open),
                $"{n} M2 a box that ends on the building: {OpenZProductionWindow.Opens - o0} menus opened ({input.Selection.Count} selected, the unit in the box {(input.Selection.Contains(s.open) ? "selected" : "NOT selected")})");

            input.Selection.Clear(); yield return 2; o0 = OpenZProductionWindow.Opens;
            if (gotC) yield return MouseDrag(ca, cb);
            Check(gotC && OpenZProductionWindow.Opens == o0 && !prod.IsOpen,
                $"{n} M3 a box across the building: {OpenZProductionWindow.Opens - o0} menus opened (two free points on its two sides found: {gotC})");

            input.Selection.Clear(); input.Selection.Add(s.open); yield return 2; o0 = OpenZProductionWindow.Opens;
            yield return MouseClick(bp);
            Check(OpenZProductionWindow.Opens == o0 + 1 && prod.Building == P && input.Selection.Count == 1 && input.Selection.Contains(s.open),
                $"{n} M4 a plain click on the building opens its menu, the selection stays ({OpenZProductionWindow.Opens - o0} opened, open {prod.Building == P}, {input.Selection.Count} selected)");

            yield return 0.5f;                                                                              // the menu stands at its place next to the building
            bool gotB2 = BuildingPoint(hud, P, reach, out var bp2);                                         // a point of the building the menu does not cover
            o0 = OpenZProductionWindow.Opens;
            if (gotB2 && prod.IsOpen) yield return MouseClick(bp2);
            Check(gotB2 && OpenZProductionWindow.Opens == o0 && prod.Building == P,
                $"{n} M5 a second click on the same building: {OpenZProductionWindow.Opens - o0} more opened, still open {prod.Building == P} (a free point beside the menu found: {gotB2})");

            input.Selection.Clear(); yield return 2; o0 = OpenZProductionWindow.Opens; bool wasOpen = prod.IsOpen;
            if (gotB2 && wasOpen) yield return MouseDrag(bp2, far);
            Check(gotB2 && wasOpen && OpenZProductionWindow.Opens == o0 && !prod.IsOpen && input.Selection.Contains(s.open),
                $"{n} M6 with the menu open, a box from the building closes it and selects ({OpenZProductionWindow.Opens - o0} opened, menu open {prod.IsOpen}, {input.Selection.Count} selected)");
            yield return Shut(hud);

            float near = Mathf.Max(1f, Mathf.Floor(slop) - 1f);
            input.Selection.Clear(); input.Selection.Add(s.open); yield return 2; o0 = OpenZProductionWindow.Opens;
            yield return MouseSlip(bp, bp + new Vector2(near, 0f));
            Check(OpenZProductionWindow.Opens == o0 + 1 && prod.Building == P && input.Selection.Contains(s.open),
                $"{n} M7 a click that slips {near:0} px (slop {slop:0.0} px) still opens the menu ({OpenZProductionWindow.Opens - o0} opened, {input.Selection.Count} selected)");
            yield return Shut(hud);

            float past = Mathf.Ceil(slop) + 2f;
            input.Selection.Clear(); input.Selection.Add(s.open); yield return 2; o0 = OpenZProductionWindow.Opens;
            yield return MouseSlip(bp, bp + new Vector2(past, 0f));
            Check(OpenZProductionWindow.Opens == o0 && !prod.IsOpen && input.Selection.Count == 0,
                $"{n} M8 a press that travels {past:0} px is a box, not a click: {OpenZProductionWindow.Opens - o0} menus opened, the empty box replaced the selection ({input.Selection.Count} selected)");
            yield return Shut(hud);

            input.Selection.Clear(); yield return 2; o0 = OpenZProductionWindow.Opens;
            if (gotF) yield return MouseClick(fp);
            Check(gotF && OpenZProductionWindow.Opens == o0 && !prod.IsOpen && input.Selection.Count == 1 && input.Selection.Contains(s.front),
                $"{n} M9 a click on the unit in front of the building selects the unit: {OpenZProductionWindow.Opens - o0} menus opened, {input.Selection.Count} selected (a point of the unit on the building found: {gotF})");
            yield return Shut(hud);

            input.Selection.Clear(); input.Selection.Add(s.open); yield return 2; o0 = OpenZProductionWindow.Opens;
            yield return MouseHold(bp, 0.7f);
            Check(OpenZProductionWindow.Opens == o0 + 1 && prod.Building == P && input.Selection.Contains(s.open),
                $"{n} M10 a still press of 0.7 s on the building opens the menu when it is let go ({OpenZProductionWindow.Opens - o0} opened, {input.Selection.Count} selected)");
            yield return Shut(hud);
        }

        static IEnumerator TouchPart(OpenZHud hud, Stage s)
        {
            var input = hud.input; var prod = hud.Production; var P = s.P; string n = s.name;
            yield return Look(hud, s);
            if (OzTouch.Now != OzTouch.Driver.Touch)
            {   // the first touch takes the pointer and brings the on-screen keys: the points are taken after that
                if (GroundPoint(hud, out var g)) yield return Tap(g);
                yield return 0.6f;
            }
            float reach = Mathf.Ceil(OpenZHud.BuildingClickSlopPx) + 4f;
            Vector2 bp = default, fp = default, far = default, ca = default, cb = default;
            bool gotB = BuildingPoint(hud, P, reach, out bp);
            bool gotF = s.front != null && FrontPoint(hud, P, s.front, out fp);
            bool gotO = s.open != null && gotB && FarPoint(hud, s.open, bp, out far);
            bool gotC = CrossPoints(hud, P, out ca, out cb);
            if (OzTouch.Now != OzTouch.Driver.Touch || !gotB || !gotO)
            {
                Check(false, $"{n}: the touch part could not be set up (pointer {OzTouch.Now}, a free point on the building: {gotB}, a unit on open ground with a free point beyond it: {gotO})");
                yield break;
            }
            int o0;

            input.Selection.Clear(); input.Selection.Add(s.open); yield return 2; o0 = OpenZProductionWindow.Opens;
            yield return Tap(bp);
            Check(OpenZProductionWindow.Opens == o0 + 1 && prod.Building == P && input.Selection.Count == 1 && input.Selection.Contains(s.open),
                $"{n} T1 a tap on the building opens its menu, the selection stays ({OpenZProductionWindow.Opens - o0} opened, open {prod.Building == P}, {input.Selection.Count} selected)");
            yield return Shut(hud);

            input.Selection.Clear(); yield return 2; o0 = OpenZProductionWindow.Opens; int g0 = OzTouch.Drags;
            yield return Drag(bp, far);
            Check(OpenZProductionWindow.Opens == o0 && !prod.IsOpen && input.Selection.Contains(s.open) && OzTouch.Drags == g0 + 1,
                $"{n} T2 a finger drag that starts on the building: {OpenZProductionWindow.Opens - o0} menus opened, it is the box ({input.Selection.Count} selected, drags {OzTouch.Drags - g0})");

            input.Selection.Clear(); yield return 2; o0 = OpenZProductionWindow.Opens;
            yield return Drag(far, bp);
            Check(OpenZProductionWindow.Opens == o0 && !prod.IsOpen && input.Selection.Contains(s.open),
                $"{n} T3 a finger drag that ends on the building: {OpenZProductionWindow.Opens - o0} menus opened ({input.Selection.Count} selected)");

            input.Selection.Clear(); yield return 2; o0 = OpenZProductionWindow.Opens;
            if (gotC) yield return Drag(ca, cb);
            Check(gotC && OpenZProductionWindow.Opens == o0 && !prod.IsOpen,
                $"{n} T4 a finger drag across the building: {OpenZProductionWindow.Opens - o0} menus opened (two free points on its two sides found: {gotC})");

            input.Selection.Clear(); yield return 2; o0 = OpenZProductionWindow.Opens;
            if (gotF) yield return Tap(fp);
            Check(gotF && OpenZProductionWindow.Opens == o0 && !prod.IsOpen && input.Selection.Count == 1 && input.Selection.Contains(s.front),
                $"{n} T5 a tap on the unit in front of the building selects the unit: {OpenZProductionWindow.Opens - o0} menus opened, {input.Selection.Count} selected (a point of the unit on the building found: {gotF})");
            yield return Shut(hud);
        }
    }
}
