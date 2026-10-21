#if UNITY_EDITOR
using System.Collections;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ
{
    /// <summary>
    /// K411 Play proof: the kickflip by itself (View/OzPlayableShuttle.KickLuck.cs), on Level 16 in Play (menu OpenZ/V1.1/Shuttle: K411
    /// Kickflip By Itself Proof (Level 16, Play), Editor/OzShuttleKickflipLuckMenu.cs). The chance is set to 100 % for the test and put back
    /// to 5 % at the end (also if it stops early). Real clicks through the game's own input (queued input events inside the game only, as
    /// the K344 proof does: no desktop input). Set-up: the CPU teams removed, morning (day clock 0.10, held), 60 fps. Stages:
    ///  L1 a far SINGLE right-click (42 .. 60 m), camera 70 m following: armed by luck (no double click seen), the whole K390 flip in the
    ///     steady cruise (start, end, the smooth trails, the long whoosh once, the five sounds); pictures at the pop, 180 deg, the catch;
    ///  L2 the next far single right-click: never twice in a row: not armed, no flip;
    ///  L3 a far order as a Shift queue (Shift held, one right-click, Shift let go): armed by luck once the queue is sent, the flip flown;
    ///  L4 a near single right-click (12 .. 17 m): no draw, no flip;
    ///  L5 the switch off (OzKickflipLuck.On = false), a far single right-click: no flip;
    ///  L6 the knobs back: 5 %, the switch on.
    /// Output: <see cref="Folder"/>. Test only.
    /// </summary>
    public sealed class OzShuttleKickflipLuckProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_kickflip_luck_proof");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_kickflip_luck_proof");
        }
        public static void Run()
        {
            Report = "running";
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
            new GameObject("OzShuttleKickflipLuckProof").AddComponent<OzShuttleKickflipLuckProof>();
        }

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; Camera cam;
        ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes, shots; string dir; bool restored;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>(); cam = Camera.main;
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void OnDestroy() { Restore(); }
        void Restore() { if (restored) return; restored = true; OzKickflipLuck.Chance = OzKickflipLuck.DefaultChance; OzKickflipLuck.On = OzKickflipLuck.DefaultOn; }   // K436: the luck flip is off by default now

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleKickflipLuckProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); shots++; Log($"picture {name}.png"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.unscaledDeltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.unscaledDeltaTime; yield return null; } }
        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        void Mouse0(Vector2 pos, bool right)
        {
            if (Mouse.current == null) return;
            var st = new MouseState { position = pos }; if (right) st = st.WithButton(MouseButton.Right);
            InputSystem.QueueStateEvent(Mouse.current, st);
        }
        void Shift(bool down) { if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, down ? new KeyboardState(Key.LeftShift) : new KeyboardState()); }
        IEnumerator RightClick(Vector2 pos) { Mouse0(pos, false); yield return Frames(2); Mouse0(pos, true); yield return Frames(2); Mouse0(pos, false); yield return Frames(2); }

        bool SpotOnScreen(float minM, float maxM, out Vector2Int px, out Vector2 screen)
        {
            px = default; screen = default;
            for (int ring = 0; ring < 6; ring++)
                for (int a = 0; a < 24; a++)
                {
                    float r = Mathf.Lerp(minM, maxM, ring / 5f) * 8f, t = a * Mathf.PI / 12f;
                    var q = W.ShuttleClamp(s.CenterX + (int)(Mathf.Cos(t) * r), s.CenterY + (int)(Mathf.Sin(t) * r));
                    if (!W.ShuttlePadFree(q.x, q.y, s, out _)) continue;
                    var wp = ZGame.PxToWorld(q.x, q.y); wp.y = g.GroundY(wp.x, wp.z);
                    var vp = cam.WorldToViewportPoint(wp);
                    if (vp.z <= 0f || vp.x < 0.18f || vp.x > 0.82f || vp.y < 0.3f || vp.y > 0.82f) continue;
                    if (!OzPlayableShuttle.LandableAt(W, s, wp, out _)) continue;
                    var sp = cam.WorldToScreenPoint(wp);
                    px = new Vector2Int(q.x, q.y); screen = new Vector2(sp.x, sp.y); return true;
                }
            return false;
        }

        void Select() { input.Selection.Clear(); input.Selection.Add(s); }
        void CamOn() { if (!rts) return; rts.FreeLookOff(); rts.follow = null; var c = ship.transform.position; rts.focus = new Vector3(c.x, 0f, c.z); rts.SetHeightImmediate(70f); }
        IEnumerator Landed() { yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && s.ShuttleRampPhase == ZShuttleRamp.Shut, 40f); }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || input == null || cam == null) { Log("no game (Level 16 in Play)"); fails++; Finish(); yield break; }
            s = W.PlayableShuttle; ship = s != null ? OzPlayableShuttle.Of(s) : null;
            if (s == null || !ship) { Log("no playable shuttle on this level (Level 16, the shuttle rule on)"); fails++; Finish(); yield break; }
            Log($"K411 kickflip-by-itself proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, world time {W.Time:F1} s");
            int bots = W.Bots.Count; W.Bots.Clear();
            var dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            OzKickflipLuck.Chance = 1.0; OzKickflipLuck.On = true;
            Log($"set-up: CPU removed {bots}, day 0.10 held; the chance set to {OzKickflipLuck.Chance * 100:F0} % for the test (back to {OzKickflipLuck.DefaultChance * 100:F0} % at the end)");
            yield return Wait(0.5f);
            yield return Order("L1 far single", 42f, 60f, false, true, true);
            yield return Order("L2 far single right after", 42f, 60f, false, false, false);
            yield return Order("L3 far Shift queue", 42f, 60f, true, true, false);
            yield return Order("L4 near single", 12f, 17f, false, false, false);
            OzKickflipLuck.On = false;
            yield return Order("L5 switch off, far single", 42f, 60f, false, false, false);
            Restore();
            Check($"L6 the knobs back: chance {OzKickflipLuck.Chance * 100:F0} % (5), switch on {OzKickflipLuck.On}", System.Math.Abs(OzKickflipLuck.Chance - 0.05) < 1e-9 && OzKickflipLuck.On == OzKickflipLuck.DefaultOn);   // K436: back to its default (off)
            Finish();
        }

        /// <summary>One order (a single right-click, or a Shift queue of one point) and its flight watched; flipWanted = it must flip by luck.</summary>
        IEnumerator Order(string tag, float minM, float maxM, bool queue, bool flipWanted, bool pictures)
        {
            yield return Landed();
            CamOn(); yield return Wait(0.6f);
            if (!SpotOnScreen(minM, maxM, out var px, out var sp) && !OzProofFarSpot.Frame(W, g, s, rts, cam, minM, maxM, out px, out sp, Log))   // K419b: searched round the ship and framed with it
            { Check($"{tag}: a free landing spot {minM:F0} .. {maxM:F0} m away on screen", false); yield break; }
            Select();
            var fl = ship.Kickflip;
            int starts0 = fl.Starts, ends0 = fl.Ends, picks0 = fl.LuckPicks, draws0 = fl.LuckDraws, rep0 = fl.LuckRepeatBlocked, off0 = fl.LuckOffSkipped, dbl0 = OzShuttleKickflip.ShipDoubles;
            int snd0 = ship.KickSounds, wh0 = ship.KickWhooshes, tr0 = ship.KickTrailFrames;
            float distM = Vector2.Distance(new Vector2(px.x, px.y), new Vector2(s.CenterX, s.CenterY)) / 8f;
            Log($"{tag}: {(queue ? "Shift + right-click" : "single right-click")} at screen ({sp.x:F0}, {sp.y:F0}) = px ({px.x}, {px.y}), {distM:F1} m from the ship");
            if (queue) { Shift(true); yield return Frames(3); }
            yield return RightClick(sp);
            if (queue) { yield return Frames(3); Shift(false); yield return Frames(3); }
            yield return null;
            bool tookGoal = (s.ShuttleGoalX, s.ShuttleGoalY) == W.ShuttleClamp(px.x, px.y) || Vector2.Distance(new Vector2(s.ShuttleGoalX, s.ShuttleGoalY), new Vector2(px.x, px.y)) / 8f <= 48f;
            Check($"{tag}: the order went to the rules as before (goal ({s.ShuttleGoalX}, {s.ShuttleGoalY})), no double click seen ({OzShuttleKickflip.ShipDoubles - dbl0})", tookGoal && OzShuttleKickflip.ShipDoubles == dbl0);
            bool armed = fl.LuckPicks == picks0 + 1;
            if (flipWanted) Check($"{tag}: armed by luck once (draws {fl.LuckDraws - draws0}, armed {fl.LuckPicks - picks0}; phase {fl.Phase})", armed && fl.LuckDraws == draws0 + 1);
            else Check($"{tag}: not armed (draws {fl.LuckDraws - draws0}, armed {fl.LuckPicks - picks0}, held back as right after a flip {fl.LuckRepeatBlocked - rep0}, switch off {fl.LuckOffSkipped - off0}; why: '{ship.KickLuckWhy}')", fl.LuckPicks == picks0);
            if (rts) { rts.follow = ship.transform; rts.SetHeightImmediate(70f); }                                    // K419b: back to 70 m after a far spot's framing
            bool[] took = new bool[3];
            float wait = 0f; bool started = false;
            while (wait < 45f)
            {
                yield return new WaitForEndOfFrame();
                wait += Time.unscaledDeltaTime;
                if (fl.Starts > starts0) started = true;
                if (pictures && started && ship.KickDrawn)
                {
                    float T = (float)fl.T, ar = Mathf.Abs((float)ship.KickNow.RollDeg); int want = -1;
                    if (!took[0] && T >= 0.25f) want = 0; else if (!took[1] && ar >= 180f) want = 1; else if (!took[2] && T >= (float)OzKickflipCurve.TotalSec - 0.1f) want = 2;
                    if (want >= 0) { took[want] = true; Shot($"k411_luck_{(want == 0 ? "pop" : want == 1 ? "180deg" : "catch")}_70m"); }
                }
                if (s.ShuttleState == ZShuttleState.Landed && wait > 3f && fl.Phase == OzKickflipPhase.Idle) break;
            }
            if (rts) rts.follow = null;
            if (flipWanted)
            {
                Check($"{tag}: the K390 flip flown by itself in the steady cruise: started {fl.Starts - starts0}, finished {fl.Ends - ends0}", fl.Starts == starts0 + 1 && fl.Ends == ends0 + 1);
                Check($"{tag}: its trails ({ship.KickTrailFrames - tr0} frames), the long whoosh once ({ship.KickWhooshes - wh0}) and the flip's sounds ({ship.KickSounds - snd0}, 5 or more)", ship.KickTrailFrames - tr0 > 30 && ship.KickWhooshes - wh0 == 1 && ship.KickSounds - snd0 >= 5);
            }
            else Check($"{tag}: no flip on the way ({fl.Starts - starts0})", fl.Starts == starts0);
        }

        void Finish()
        {
            Restore();
            Report = $"{passes} PASS, {fails} FAIL, {shots} pictures";
            Log("K411 kickflip-by-itself proof: " + Report + " -> " + dir);
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "k411_proof_log.txt"), sb.ToString()); } catch (System.Exception e) { Debug.LogWarning("[OzShuttleKickflipLuckProof] log not written: " + e.Message); }
            Destroy(gameObject);
        }
    }
}
#endif
