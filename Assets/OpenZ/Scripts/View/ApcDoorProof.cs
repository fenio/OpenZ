using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ
{
    /// <summary>
    /// grunt squad boards up the ramp (frames: door open + walking in, seated, door shut + dome spinning); EXIT with the Z way (real
    /// synthetic mouse: left-click selects the APC, the hover cursor must be "exit", right-click on it) -> passengers out down the
    /// ramp, the driver stays (frames: walking out, empty APC + dome at rest); empty APC: no exit cursor, E does nothing; a tough
    /// squad (exit: the EXIT CREW button path) and an electrobot squad (exit: the E key) board with close frames; then the Z click
    /// exit on a crewed gun, gatling, howitzer and missile cannon, and the normal cursor over a fort tower cannon. Report + frames:
    /// </summary>
    public class ApcDoorProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("ApcDoorProof").AddComponent<ApcDoorProof>();
        }

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; readonly StringBuilder sb = new StringBuilder(); int fails, frames; string dir; float T0;

        void Start()
        {
            g = ZGame.I; W = g.World; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/milestones/apc_door"));
            System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string s) { sb.AppendLine(s); Debug.Log("[ApcDoorProof] " + s); }
        void Check(string what, bool ok) { if (!ok) fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); frames++; Log($"frame {name}.png"); }

        ZObject Spawn(string cat, string type, int tx, int ty, Team team, int amount = -1)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, cat == "robot" ? 1 : 2, cat == "robot", out tx, out ty);
            var o = cat == "robot" ? W.CreateRobotGroup(type, tx * 16, ty * 16, team, amount) : W.CreateObject(cat, type, tx * 16, ty * 16, team);
            if (o == null) { Log($"spawn {cat}:{type} failed"); return null; }
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        bool Order(ZObject u, WpMode mode, ZObject t, int x = 0, int y = 0)
        {
            var wp = t != null ? new ZWaypoint { mode = mode, refId = t.Id, x = t.CenterX, y = t.CenterY, attackTo = true } : new ZWaypoint { mode = mode, x = x, y = y };
            return W.SendWaypoints(u.Owner, u, new[] { wp });
        }
        UnitView V(ZObject o) => o != null && g.Views.TryGetValue(o.Id, out var v) ? v : null;
        string CursorName() { var c = FindFirstObjectByType<OpenZCursors>(); return c ? c.Current : "?"; }

        static void MouseEv(Vector2 p, MouseButton? b = null)
        {
            var st = new MouseState { position = p }; if (b.HasValue) st = st.WithButton(b.Value);
            InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current, st);
        }
        IEnumerator Hover(Vector2 p) { for (int i = 0; i < 4; i++) { MouseEv(p); yield return null; } }
        IEnumerator Click(Vector2 p, MouseButton b) { MouseEv(p); yield return null; MouseEv(p, b); yield return null; yield return null; MouseEv(p); yield return null; yield return null; }
        IEnumerator PressKey(UnityEngine.InputSystem.Key k) { InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(k)); yield return null; yield return null; InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); yield return null; yield return null; }
        Vector2 At(ZObject o) => input.ScreenRect(o).center;

        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }

        int Count(Team t, string type) { int n = 0; foreach (var o in W.Objects) if (!o.Destroyed && o.Kind == ZKind.Robot && o.Type == type && o.Owner == t) n++; return n; }

        IEnumerator All()
        {
            T0 = Time.unscaledTime; Application.targetFrameRate = 60;
            Log($"task_apc_rear_door proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}");
            var f = Field();
            yield return new WaitForSeconds(1.0f);
            var apc = Spawn("vehicle", "apc", f.x, f.y + 3, Team.Red);
            var av = V(apc); var door = ApcRearDoor.Of(av); var dome = av ? av.GetComponent<ApcDome>() : null;
            Log($"APC {apc?.Id}: door built {door != null} (cut {ApcRearDoor.CutTris} hull triangles), drivers {apc?.Drivers.Count}, dome {dome != null}");
            Check("rear door + bay built on the APC", door != null);
            if (door == null) { Finish(); yield break; }
            Order(apc, WpMode.Move, null, apc.CenterX, apc.CenterY - 6 * 16);                              // drive north: the rear faces the camera
            yield return WaitFor(() => apc.IsMoving, 2f);
            yield return WaitFor(() => !apc.IsMoving, 10f);
            yield return new WaitForSeconds(0.4f);
            rts.FlyTo(av.transform.position + new Vector3(0f, 0f, -1.5f)); rts.ZoomTo(24f);
            yield return new WaitForSeconds(1.2f);
            Log($"APC heading {av.transform.eulerAngles.y:F0} deg (rear toward the camera when ~0)");

            // ---- 1 grunt squad boards
            int ax = apc.CenterX >> 4, ay = apc.CenterY >> 4;
            var gr = Spawn("robot", "grunt", ax + 1, ay + 6, Team.Red);
            yield return new WaitForSeconds(0.6f);
            float t1 = Time.unscaledTime; int opens0 = ApcRearDoor.Opens, latch0 = ApcRearDoor.Latched;
            Check("enter order APC (grunt squad)", Order(gr, WpMode.Enter, apc));
            yield return WaitFor(() => door.Open01 > 0.01f, 20f);
            float tOpen = Time.unscaledTime;
            yield return WaitFor(() => door.IsOpen, 2f);
            yield return new WaitForSeconds(0.35f); Shot("01_grunt_door_open_walking_in");
            yield return new WaitForSeconds(0.6f); Shot("02_grunt_boarding_doorway");
            yield return WaitFor(() => door.Seated >= 1 && door.IsOpen, 4f); Shot("03_grunt_seated_door_open");
            yield return WaitFor(() => door.IsShut && apc.Passengers.Count > 0, 8f);
            yield return new WaitForSeconds(1.4f); Shot("04_door_shut_dome_spinning");
            Log($"grunts aboard: passengers {apc.Passengers.Count} {apc.PassengerType}, drivers {apc.Drivers.Count}, door opens {ApcRearDoor.Opens - opens0}, latched {ApcRearDoor.Latched - latch0}, dome spin {dome?.SpinRate:F0} deg/s, first open {tOpen - t1:F1} s after the order");
            Check("ramp opened + shut + latched for the boarding", ApcRearDoor.Opens - opens0 >= 1 && ApcRearDoor.Latched - latch0 >= 1 && door.IsShut);
            Check("dome spins with crew aboard", dome && dome.SpinRate > 10f);

            // ---- 2 the Z way: left-click selects, the exit cursor on the APC, right-click exits; the driver stays
            var p = At(apc);
            input.Selection.Clear();
            yield return Click(p, MouseButton.Left);
            yield return Hover(p);
            string cur = CursorName();
            Log($"selected after left-click: {input.Selection.Count} ({(input.Selection.Count > 0 ? input.Selection[0].Type : "-")}), cursor over the crewed APC '{cur}'");
            Check("Z exit cursor over the selected APC with passengers", input.Selection.Count == 1 && input.Selection[0] == apc && cur == "exit");
            int g0 = Count(Team.Red, "grunt"); float t2 = Time.unscaledTime; int opens1 = ApcRearDoor.Opens;
            yield return Click(p, MouseButton.Right);
            yield return new WaitForSeconds(0.2f);
            Log($"after right-click: passengers {apc.Passengers.Count}, drivers {apc.Drivers.Count}, owner {apc.Owner}, waypoints {apc.Waypoints.Count}, selection {input.Selection.Count}, red grunts {g0} -> {Count(Team.Red, "grunt")}");
            Check("APC Z-click exit: only the passengers out, the grunt driver stays, APC stays red, no move order stole it", apc.Passengers.Count == 0 && apc.Drivers.Count > 0 && apc.Owner == Team.Red && apc.Waypoints.Count == 0 && input.Selection.Contains(apc));
            yield return WaitFor(() => door.IsOpen, 2f);
            yield return new WaitForSeconds(0.7f); Shot("05_exit_door_open_walking_out");
            yield return new WaitForSeconds(0.5f); Shot("06_exit_down_the_ramp");
            yield return WaitFor(() => door.IsShut, 8f);
            yield return new WaitForSeconds(1.6f); Shot("07_empty_apc_dome_at_rest");
            Check("ramp opened for the exit and shut again", ApcRearDoor.Opens - opens1 >= 1 && door.IsShut);
            Log($"after the exit: dome {dome?.SpinRate:F0} deg/s (ZF21: it turns while anyone is aboard; DV15 keeps the grunt driver in, so only a crewless APC rests)");
            Check("exit + latch sounds after the Z-click exit", Played(t2, "exit") >= 1 && Played(t2, "latch") >= 1 && Played(t2, "apc_ramp") >= 2);
            // empty APC: normal cursor, E does nothing
            yield return Hover(p);
            string cur2 = CursorName(); int ev0 = ApcRearDoor.Opens, dr0 = apc.Drivers.Count;
            yield return PressKey(UnityEngine.InputSystem.Key.E);
            yield return new WaitForSeconds(0.4f);
            Log($"empty APC: cursor '{cur2}', after E: drivers {apc.Drivers.Count}, owner {apc.Owner}, door opens +{ApcRearDoor.Opens - ev0}");
            Check("empty APC: no exit cursor, E does nothing (driver stays, no ramp)", cur2 != "exit" && apc.Drivers.Count == dr0 && apc.Owner == Team.Red && ApcRearDoor.Opens == ev0);

            // ---- 3 tough squad (exit: EXIT CREW button) and electrobot squad (exit: E key), close frames
            rts.ZoomTo(22f);
            foreach (var type in new[] { "tough", "electrobot" })
            {
                var sq = Spawn("robot", type, ax - 1, ay + 6, Team.Red);
                yield return new WaitForSeconds(0.6f);
                Check($"enter order APC ({type} squad)", Order(sq, WpMode.Enter, apc));
                yield return WaitFor(() => door.IsOpen, 20f);
                yield return new WaitForSeconds(0.5f); Shot($"08_{type}_ramp");
                yield return new WaitForSeconds(0.55f); Shot($"09_{type}_doorway");
                yield return WaitFor(() => door.Seated >= 1 && door.IsOpen, 4f); Shot($"10_{type}_seated");
                yield return WaitFor(() => door.IsShut && apc.Passengers.Count > 0, 8f);
                Log($"{type} aboard: passengers {apc.Passengers.Count} {apc.PassengerType}, drivers {apc.Drivers.Count}");
                Check($"{type} squad boarded as passengers (driver stays)", apc.Passengers.Count > 0 && apc.PassengerType == type && apc.Drivers.Count > 0);
                yield return new WaitForSeconds(0.5f);
                int n0 = Count(Team.Red, type); float t3 = Time.unscaledTime;
                input.Selection.Clear(); input.Selection.Add(apc); yield return null;
                string how;
                if (type == "tough") { how = "EXIT CREW button"; var hud = OpenZHud.I; if (hud) hud.ExitSelected(); }
                else { how = "E key"; yield return PressKey(UnityEngine.InputSystem.Key.E); }
                yield return new WaitForSeconds(0.3f);
                Log($"{how}: passengers {apc.Passengers.Count}, drivers {apc.Drivers.Count}, owner {apc.Owner}, red {type} {n0} -> {Count(Team.Red, type)}");
                Check($"APC exit via the {how}: {type}s out, driver stays, APC red", apc.Passengers.Count == 0 && apc.Drivers.Count > 0 && apc.Owner == Team.Red && Count(Team.Red, type) > n0);
                yield return WaitFor(() => door.IsOpen, 2f);
                yield return new WaitForSeconds(0.9f); Shot($"11_{type}_walking_out");
                yield return WaitFor(() => door.IsShut, 8f);
                yield return new WaitForSeconds(0.4f);
            }
            foreach (var kv in BoardingView.ApcFitLog) Log($"fit {kv.Key}: full height {kv.Value.x:F2} m -> scale x{kv.Value.y:F2} inside ({kv.Value.x * kv.Value.y:F2} m; doorway / bay 1.45 m)");

            // ---- empty (crewless, neutral) APC: ramp shut, dome at rest
            var napc = Spawn("vehicle", "apc", ax + 5, ay, Team.Neutral);
            var nd = napc != null && V(napc) ? V(napc).GetComponent<ApcDome>() : null; var ndoor = ApcRearDoor.Of(V(napc));
            if (napc != null) { rts.FlyTo(V(napc).transform.position); yield return new WaitForSeconds(2.0f); Shot("12_empty_neutral_apc_door_shut_dome_rest"); }
            Check("crewless APC: ramp shut, dome at rest", nd && nd.SpinRate < 1f && ndoor && ndoor.IsShut);

            // ---- 4 the Z click exit on crewed guns; the normal cursor over a fort tower cannon
            rts.ZoomTo(26f);
            int k = 0;
            foreach (var type in new[] { "gun", "gatling", "howitzer", "missile_cannon" })
            {
                var c = Spawn("cannon", type, ax - 6 + 4 * k++, ay + 10, Team.Red);
                if (c == null) continue;
                rts.FlyTo(V(c).transform.position); yield return new WaitForSeconds(1.6f);
                var cp = At(c); input.Selection.Clear();
                yield return Click(cp, MouseButton.Left);
                yield return Hover(cp);
                string cc = CursorName(); float t4 = Time.unscaledTime; int dr = c.Drivers.Count;
                yield return Click(cp, MouseButton.Right);
                yield return new WaitForSeconds(1.0f);
                bool ok = cc == "exit" && dr > 0 && c.Owner == Team.Neutral && c.Drivers.Count == 0 && Played(t4, "exit") >= 1;
                Log($"{type}: selected {input.Selection.Count}, cursor '{cc}', drivers {dr} -> {c.Drivers.Count}, owner {c.Owner}, exit sound {Played(t4, "exit")}");
                Check($"{type}: Z exit cursor + right-click ejects the gunner, gun neutral", ok);
            }
            ZObject tower = null;
            foreach (var o in W.Objects) { if (o.Destroyed || o.Kind != ZKind.Cannon || o.Owner != Team.Red) continue; var tv = V(o); if (tv && tv.OnTower) { tower = o; break; } }
            if (tower != null)
            {
                rts.FlyTo(V(tower).transform.position); yield return new WaitForSeconds(1.8f);
                var tp = At(tower); input.Selection.Clear();
                yield return Click(tp, MouseButton.Left);
                yield return Hover(tp);
                string tc = CursorName(); int td = tower.Drivers.Count;
                yield return Click(tp, MouseButton.Right);
                yield return new WaitForSeconds(0.6f);
                Log($"fort tower {tower.Type}: selected {input.Selection.Count}, cursor '{tc}', drivers {td} -> {tower.Drivers.Count}, owner {tower.Owner}");
                Check("fort tower cannon: normal cursor, not ejectable", tc != "exit" && tower.Owner == Team.Red && tower.Drivers.Count == td);
            }
            else Log("no red fort tower cannon on this map (skipped)");
            input.Selection.Clear();
            Finish();
        }

        static int Played(float t0, string key) { int n = 0; foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && (e.outcome.StartsWith("PLAY") || e.src == "DOOR")) n++; return n; }

        void Finish()
        {
            sb.AppendLine("sound log (door / latch / exit / dome):");
            foreach (var e in OzSoundLog.Since(T0))
                if (e.key == "apc_ramp" || e.key == "apc_ramp_land" || e.key == "latch" || e.key == "exit" || e.key == "apc_dome" || e.key == "veh_apc_dome_loop")
                    sb.AppendLine($"  {e.t - T0,6:F2} {e.src} {e.key} {e.clip} {e.outcome}");
            Log($"RESULT {(fails == 0 ? "PASS" : "FAIL " + fails)}, {frames} frames, {Time.unscaledTime - T0:F0} s");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "proof_log.txt"), sb.ToString());
            Report = fails == 0 ? "PASS" : "FAIL " + fails;
        }

        Vector2Int Field()
        {
            int wt = W.Path.WidthPix >> 4, ht = W.Path.HeightPix >> 4; var best = new Vector2Int(wt / 2, ht / 2); float bd = float.MaxValue;
            for (int y = 12; y < ht - 14; y += 2)
                for (int x = 12; x < wt - 12; x += 2)
                {
                    bool ok = true;
                    for (int j = -8; j <= 12 && ok; j++) for (int i = -9; i <= 9 && ok; i++) ok = W.Path.TilePassable(x + i, y + j, false);
                    if (!ok) continue;
                    foreach (var o in W.Objects) if (!o.Destroyed && Mathf.Abs((o.CenterX >> 4) - x) < 16 && Mathf.Abs((o.CenterY >> 4) - y) < 16) { ok = false; break; }
                    if (!ok) continue;
                    float d = (x - wt / 2) * (x - wt / 2) + (y - ht / 2) * (y - ht / 2);
                    if (d < bd) { bd = d; best = new Vector2Int(x, y); }
                }
            if (rts) rts.FlyTo(ZGame.PxToWorld(best.x * 16 + 16, best.y * 16 + 16));
            Log($"test field tile {best.x},{best.y}");
            return best;
        }
    }
}
