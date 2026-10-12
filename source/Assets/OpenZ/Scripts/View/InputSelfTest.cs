#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ
{
    /// <summary>
    /// Editor-only: drives the real Input System (queued keyboard / mouse state events, frame by frame) through the RTS
    /// basics and checks PlayerInput's result: Ctrl+1 store, 1 recall, 1 again = camera flies, Shift+click
    /// add/remove, Ctrl+A, double-click = all of that type on screen, Shift+right-click queue (markers) + send on release.
    /// Report: InputSelfTest.Report (also logged).
    /// </summary>
    public class InputSelfTest : MonoBehaviour
    {
        public static string Report = "running";
        public static string ShotPath;                                               // optional Game view capture with the Ctrl+A selection
        readonly StringBuilder sb = new StringBuilder(); int pass, fail;
        PlayerInput input; ZGame game; Camera cam;
        Vector2 mousePos;

        public static void Run() { Report = "running"; new GameObject("InputSelfTest").AddComponent<InputSelfTest>(); }

        void Start() { input = FindFirstObjectByType<PlayerInput>(); game = ZGame.I; cam = Camera.main; StartCoroutine(Go()); }

        void Check(string name, bool ok, string info = "") { if (ok) pass++; else fail++; sb.AppendLine($"| {name} | {(ok ? "PASS" : "**FAIL**")} | {info} |"); }

        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        IEnumerator Keys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys)); yield return Frames(2);
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); yield return Frames(2);
        }

        void MouseState(Vector2 pos, bool left, bool right)
        {
            var st = new MouseState { position = pos };
            if (left) st = st.WithButton(MouseButton.Left); if (right) st = st.WithButton(MouseButton.Right);
            InputSystem.QueueStateEvent(Mouse.current, st); mousePos = pos;
        }

        IEnumerator Click(Vector2 pos, bool right = false, params Key[] held)
        {
            if (held.Length > 0) { InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(held)); }
            MouseState(pos, false, false); yield return Frames(2);
            MouseState(pos, !right, right); yield return Frames(2);
            MouseState(pos, false, false); yield return Frames(2);
        }

        /// <summary>A screen point in the middle band of the view whose ground has no own unit within 5 m (not under the HUD).</summary>
        Vector2 EmptyGround()
        {
            for (float fy = 0.45f; fy < 0.8f; fy += 0.05f)
                for (float fx = 0.3f; fx < 0.75f; fx += 0.05f)
                {
                    var sp = new Vector2(UnityEngine.Screen.width * fx, UnityEngine.Screen.height * fy);
                    if (!game.GroundPoint(cam.ScreenPointToRay(sp), out var gp)) continue;
                    if (game.World.Objects.Any(o => !o.Destroyed && o.Kind != ZKind.MapItem && (game.CenterWorld(o) - gp).sqrMagnitude < 25f)) continue;
                    return sp;
                }
            return new Vector2(UnityEngine.Screen.width * 0.5f, UnityEngine.Screen.height * 0.7f);
        }

        Vector2 Screen(ZObject o) { var p = game.Views.TryGetValue(o.Id, out var v) && v ? v.transform.position + Vector3.up * 1.2f : game.CenterWorld(o); return cam.WorldToScreenPoint(p); }

        IEnumerator Go()
        {
            yield return Frames(10);
            sb.AppendLine("| Test | Result | Detail |"); sb.AppendLine("|---|---|---|");
            var mine = game.World.OwnedSelectable(game.player).Where(o => o.Kind == ZKind.Robot).ToList();
            if (mine.Count < 2) { Report = "not enough own robots"; yield break; }
            // put the camera over two own squads
            var a = mine[0]; var b = mine.OrderBy(o => (game.CenterWorld(o) - game.CenterWorld(a)).sqrMagnitude).Skip(1).First();
            var rc = FindFirstObjectByType<RtsCamera>(); rc.FlyTo((game.CenterWorld(a) + game.CenterWorld(b)) * 0.5f); yield return new WaitForSeconds(1.5f);

            // click select
            yield return Click(Screen(a)); Check("left click selects a unit", input.Selection.Contains(a), $"sel {input.Selection.Count}");
            // Shift+click add, then remove
            yield return Click(Screen(b), false, Key.LeftShift); InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); yield return Frames(2);
            Check("Shift+click adds", input.Selection.Contains(a) && input.Selection.Contains(b), $"sel {input.Selection.Count}");
            yield return Click(Screen(b), false, Key.LeftShift); InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); yield return Frames(2);
            Check("Shift+click removes", input.Selection.Contains(a) && !input.Selection.Contains(b), $"sel {input.Selection.Count}");
            // control group: Ctrl+1 store, deselect, 1 recall, 1 again flies
            yield return Keys(Key.LeftCtrl, Key.Digit1);
            yield return Click(EmptyGround());
            Check("click on empty ground deselects", input.Selection.Count == 0, $"sel {input.Selection.Count}");
            yield return Keys(Key.Digit1); Check("1 recalls group 1", input.Selection.Count == 1 && input.Selection.Contains(a), $"sel {input.Selection.Count}");
            rc.FlyTo(game.CenterWorld(a) + new Vector3(40, 0, 40)); yield return new WaitForSeconds(1.2f);
            var camBefore = rc.transform.position; yield return Keys(Key.Digit1); yield return new WaitForSeconds(1.2f);
            float moved = (rc.transform.position - camBefore).magnitude;
            Check("1 again (double tap) centres the camera on the group", moved > 10f, $"camera moved {moved:F1} m");
            yield return new WaitForSeconds(0.5f);
            // Ctrl+A: all own robots + vehicles (Zod)
            yield return Keys(Key.LeftCtrl, Key.A);
            int expect = game.World.OwnedSelectable(game.player).Count(o => o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle);
            if (ShotPath != null) { ScreenCapture.CaptureScreenshot(ShotPath); yield return Frames(3); }
            Check("Ctrl+A selects all robots and vehicles (Zod)", input.Selection.Count == expect && expect > 0, $"{input.Selection.Count}/{expect}");
            // double-click: all units of that type on screen
            rc.FlyTo(game.CenterWorld(a)); yield return new WaitForSeconds(1.2f);
            yield return Click(EmptyGround());
            var sa = Screen(a); MouseState(sa, false, false); yield return Frames(2);
            MouseState(sa, true, false); yield return Frames(1); MouseState(sa, false, false); yield return Frames(3);
            MouseState(sa, true, false); yield return Frames(1); MouseState(sa, false, false); yield return Frames(3);
            var screen = new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height);
            int sameType = game.World.OwnedSelectable(game.player).Count(o => o.Type == a.Type && screen.Overlaps(input.ScreenRect(o)));
            Check("double-click selects every unit of that type on screen", input.Selection.Count == sameType && input.Selection.All(o => o.Type == a.Type), $"{input.Selection.Count}/{sameType} ({a.Type})");
            // Shift+right-click queue: two waypoints pending (markers drawn), sent on Shift release
            yield return Click(Screen(a)); var u = input.Selection.FirstOrDefault();
            var g0 = game.CenterWorld(a); var p1 = cam.WorldToScreenPoint(g0 + new Vector3(6, 0, 0)); var p2 = cam.WorldToScreenPoint(g0 + new Vector3(6, 0, -6));
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.LeftShift)); yield return Frames(2);
            MouseState(p1, false, true); yield return Frames(2); MouseState(p1, false, false); yield return Frames(2);
            MouseState(p2, false, true); yield return Frames(2); MouseState(p2, false, false); yield return Frames(2);
            int queued = u != null && input.Pending.TryGetValue(u, out var q) ? q.Count : 0;
            Check("Shift+right-click queues orders (markers shown while Shift is held)", queued == 2, $"pending {queued}");
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); yield return Frames(3);
            Check("releasing Shift sends the queue", u != null && input.Pending.Count == 0 && u.Waypoints.Count >= 2, $"waypoints {u?.Waypoints.Count}");
            Report = $"input self-test: {pass} PASS / {fail} FAIL\n" + sb;
            Debug.Log("[OpenZ] " + Report);
            Destroy(gameObject);
        }
    }
}
#endif
