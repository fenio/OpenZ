using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// view View/BoardingView.ShuttleQuick.cs + View/OzShuttleRampSound.cs). Level 16 in Play (EASY, the SHUTTLE box ON; menu OpenZ/V1.1/Shuttle:
    /// K422 Faster Boarding Proof (Level 16, Play), Editor/OzShuttleQuickBoardMenu.cs). The computer player's orders removed, the other teams
    /// hold their fire, the day clock held at morning, 60 fps, the normal RTS camera at 70 m on the ship. A fresh ship (the testing panel's
    /// drop) east of the robot factory and a red grunt squad of 4 behind it:
    ///  Q1 boarding (ENTER): the ramp's swings, the file, the ramp starting up behind the last one, the shut; every figure on its bench at the
    ///     shut and none still walking; the paces drawn (walking clip on the ramp and in the aisle, never the run); the door sounds on the
    ///     quicker ramp (the opening's clunk on the landing, the hydraulic fading as the ramp stops, the close's clunk on the shut, the latch),
    ///     a ramp-foot step per figure, "All aboard!"; boarding to latch against run 28.
    ///  Q2 unloading (EXIT): seated while the ramp starts down, getting up while it comes down (the first at the doorway as it lands), down the
    ///     ramp in a line, the ramp starting up behind the last one, the shut; the robots drawn on their places; the sounds; EXIT to latch
    ///     against run 28.
    /// Pictures (folder <see cref="Folder"/>): q1a .. q1d, q2a .. q2e. Test only.
    /// </summary>
    [DefaultExecutionOrder(210)]                                                                         // after ShuttleRearDoor (205): its shut counters of this frame are in
    public sealed class OzShuttleQuickBoardProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_faster_boarding_k422");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_faster_boarding_k422");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShuttleQuickBoardProof").AddComponent<OzShuttleQuickBoardProof>();
        }

        /// <summary>Run 28's rules (the exe the owner played): boarding of 4 = the leader's step to the foot at 3.2 m/s + 3 x 0.55 + 3.2 + 1.5 s
        /// (6.66 s headless), unloading of 4 = 1.5 + 3 x 0.55 + 3.2 + 1.5 s (7.85 s; 7.87 headless).</summary>
        public const float OldBoard4Rest = 6.35f, OldUnload4 = 7.85f;
        float OldBoard4;
        static readonly Vector2 Focus = new Vector2(150, -92);
        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; DayNight dn; Team me = Team.Red; BoardingView bv;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes, errors; string dir;
        readonly Dictionary<ZObject, int> heldFire = new Dictionary<ZObject, int>();
        // the ramp's moments as drawn (unscaled time) and in the rules (sim time), seen every frame
        ZObject watch; ZShuttleRamp lastPh; float uOpen0 = -1, uOpen1 = -1, uUp = -1, uShut = -1; double sOpen1 = -1, sUp = -1, sShut = -1;
        int seatedAtShut = -1, walkingAtShut = -1, waitingPeak;
        // the sim time one frame moves on, at each seen moment: the rules' moment lies within that last step (a slow frame is no false FAIL)
        double prevSim = -1, lagOpen1, lagUp, lagShut, lagNow;
        /// <summary>A rules moment seen once a frame: 0.03 s, or the sim step of the frame it was seen in + one tick when that is more.</summary>
        static double Tol(double lag) => System.Math.Max(0.03, lag + ZConst.TICK + 1e-6);

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>(); bv = BoardingView.I;
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            Application.logMessageReceived += OnLog;
            StartCoroutine(All());
        }
        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            foreach (var kv in heldFire) if (kv.Key != null && kv.Key.AttackRadius == 0) kv.Key.AttackRadius = kv.Value;
            heldFire.Clear();
        }
        void OnLog(string msg, string stack, LogType t) { if ((t == LogType.Error || t == LogType.Exception || t == LogType.Assert) && !msg.StartsWith("[OzShuttleQuickBoardProof]")) errors++; }

        void LateUpdate()
        {
            var s = watch; if (s == null || W == null) return;
            var ph = s.ShuttleRampPhase; float u = Time.unscaledTime;
            lagNow = prevSim >= 0 ? System.Math.Max(0.0, W.Time - prevSim) : 0.0; prevSim = W.Time;
            if (ph != lastPh)
            {
                if (ph == ZShuttleRamp.Opening && lastPh == ZShuttleRamp.Shut) uOpen0 = u;
                if (ph == ZShuttleRamp.Open) { uOpen1 = u; sOpen1 = W.Time; lagOpen1 = lagNow; }
                if (ph == ZShuttleRamp.Closing) { uUp = u; sUp = W.Time; lagUp = lagNow; }
                if (ph == ZShuttleRamp.Shut) { uShut = u; sShut = W.Time; lagShut = lagNow; seatedAtShut = ShuttleRearDoor.SeatedAtShut; walkingAtShut = bv ? bv.ShuttleGhostsFor(s.Id) : -1; }
                lastPh = ph;
            }
            if (bv) waitingPeak = Mathf.Max(waitingPeak, bv.ShuttleWaitingNow);
        }
        void Watch(ZObject s) { watch = s; lastPh = s.ShuttleRampPhase; uOpen0 = uOpen1 = uUp = uShut = -1; sOpen1 = sUp = sShut = -1; seatedAtShut = walkingAtShut = -1; waitingPeak = 0; lagOpen1 = lagUp = lagShut = 0; }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleQuickBoardProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log($"frame {name}.png (sim {W.Time:F2})"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        IEnumerator At(double simT, float max = 20f) { yield return WaitFor(() => W.Time >= simT - 1e-6, max); }
        void Cam(ZObject o, float h = 70f) { if (!rts || o == null) return; var p = ZGame.PxToWorld(o.CenterX, o.CenterY); rts.FreeLookOff(); rts.focus = new Vector3(p.x, 0f, p.z); rts.SetHeightImmediate(h); }
        static int Px(float x) => Mathf.Clamp(Mathf.FloorToInt(x * 8f / 16f), 1, 400) * 16 + 8;
        static bool Alive(ZObject o) => o != null && !o.Destroyed && o.KillAt < 0;
        static float FirstAt(float t0, string key, string src = null) { foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && (e.outcome.StartsWith("PLAY") || (src != null && e.src == src))) return e.t; return -1f; }
        static int Played(float t0, string key) { int n = 0; foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && e.outcome.StartsWith("PLAY")) n++; return n; }

        void HoldFire()
        {
            foreach (var o in W.Objects)
            {
                if (o == null || o.Destroyed || o.Owner == me || o.Owner == Team.Neutral || o.AttackRadius <= 0 || heldFire.ContainsKey(o)) continue;
                heldFire[o] = o.AttackRadius; o.AttackRadius = 0;
            }
        }

        ZObject Ship()
        {
            for (int i = 0; i < 24; i++)
            {
                float a = i * 1.1f, r = 18f + 9f * (i % 4);
                var p = Focus + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                var s = W.SpawnShuttle(Px(p.x), Px(-p.y), me, -90f, out _);
                if (s == null) continue;
                OzPlayableShuttle.SpawnView(g, s);
                return s;
            }
            return null;
        }

        ZObject Squad(ZObject s, int n)
        {
            var (bx, by) = s.ShuttleBackPoint(5);
            if (!OzCheatSpawner.FreeTile(W, bx >> 4, by >> 4, 1, true, out int tx, out int ty)) return null;
            var o = W.CreateRobotGroup("grunt", tx * 16, ty * 16, me, n); if (o == null) return null;
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }

        /// <summary>The paces drawn for the walks built since `from` (BoardingView.QuickLog lines): the worst against the wanted.</summary>
        string Paces(int from, bool enter, out bool ok)
        {
            ok = true; var lines = new List<string>();
            var log = BoardingView.QuickLog;
            int built = (enter ? BoardingView.QuickEnterBuilt : BoardingView.QuickExitBuilt) - from;          // the walks of this case: the newest lines
            for (int i = Mathf.Max(0, log.Count - built); i < log.Count; i++)
                if (log[i].StartsWith(enter ? "in " : "out")) lines.Add(log[i]);
            ok = lines.Count > 0;
            foreach (var l in lines)
            {
                float ramp = Num(l, "ramp "), aisle = Num(l, "aisle "), door = Num(l, enter ? "foot -> doorway " : "seat -> doorway "), all = Num(l, enter ? "foot -> seat " : "seat -> off the foot ");
                ok &= ramp >= 2.0f && ramp <= 2.4f && aisle <= 2.4f && Mathf.Abs(door - (float)(enter ? ZWorld.ShuttleBoardDoorSec : ZWorld.ShuttleExitDoorSec)) <= 0.02f
                      && (enter ? all <= (float)ZWorld.ShuttleRampInSec - 0.04f : Mathf.Abs(all - (float)ZWorld.ShuttleRampOutSec) <= 0.02f);
            }
            return string.Join(" | ", lines);
        }
        static float Num(string l, string key)
        {
            int i = l.IndexOf(key); if (i < 0) return -1f; i += key.Length;
            int j = i; while (j < l.Length && (char.IsDigit(l[j]) || l[j] == '.')) j++;
            return float.TryParse(l.Substring(i, j - i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : -1f;
        }

        // ------------------------------------------------------------------ Q1 boarding
        IEnumerator Q1(ZObject s, ZObject sq)
        {
            Log("Q1 boarding: a squad of 4 behind the ship, ENTER");
            Cam(s); Watch(s); yield return Wait(0.5f);
            float t0 = Time.unscaledTime; int steps0 = BoardingView.ShuttleSteps, in0 = BoardingView.QuickEnterBuilt, run0 = BoardingView.QuickRunClips;
            int opens0 = OzShuttleRampSound.DoorOpens, stopO0 = OzShuttleRampSound.HydraulicStopsOpen, stopS0 = OzShuttleRampSound.HydraulicStopsShut;
            W.SendWaypoints(me, sq, new[] { new ZWaypoint { mode = WpMode.Enter, refId = s.Id, x = s.CenterX, y = s.CenterY, playerGiven = true } });
            yield return WaitFor(() => { Cam(s); return s.ShuttleRampPhase == ZShuttleRamp.Opening && s.ShuttleRamp01 >= 0.5; }, 40f); Shot("q1a_ramp_opening_h70");
            yield return WaitFor(() => { Cam(s); return s.Passengers.Count > 0; }, 40f);
            double tb = W.Time; var feet = new List<double>(s.ShuttleFootTimes); double hold = s.ShuttleRampHoldUntil, last = feet.Count > 0 ? feet[feet.Count - 1] : tb;
            OldBoard4 = OldBoard4Rest + (feet.Count > 0 ? (float)((feet[0] - tb) * ZWorld.ShuttleWalkMps / 3.2) : 0f);   // the leader's step to the foot at run 28's 3.2 m/s
            yield return At(feet.Count > 1 ? feet[1] + 0.4 : tb + 0.6); Shot("q1b_file_on_the_ramp_h70");
            yield return WaitFor(() => sUp >= 0, 10f); yield return Wait(0.3f); Shot("q1c_ramp_up_behind_the_last_h70");
            yield return WaitFor(() => sShut >= 0, 10f); yield return Wait(0.4f); Shot("q1d_shut_latched_h70");
            yield return Wait(1.0f);
            double total = sShut - tb;
            float aOpen = FirstAt(t0, "shuttle_door_open"), aHyd = FirstAt(t0, "shuttle_ramp_hydraulic"), aClose = FirstAt(t0, "shuttle_door_close"), aLatch = FirstAt(t0, "latch", "DOOR"), aBoard = FirstAt(t0, "vo_shuttle_board");
            float clunkOff = aOpen > 0 && uOpen1 > 0 ? aOpen + OzShuttleRampSound.LastClunkAfter - uOpen1 : 99f, closeOff = aClose > 0 && uShut > 0 ? aClose + 0.5f - uShut : 99f;
            string paces = Paces(in0, true, out bool pacesOk);
            Log($"Q1 rules: boarded {s.Passengers.Count} at {tb:F2}; feet +{string.Join(" +", feet.ConvertAll(f => (f - tb).ToString("F2")))}; the ramp up +{sUp - tb:F2} (want +{last + ZWorld.ShuttleBoardUpSec - tb:F2}), shut +{sShut - tb:F2}; sat by +{hold - tb:F2}; boarding -> latch {total:F2} s (run 28: {OldBoard4:F2} s)");
            Log($"Q1 drawn: on the benches at the shut {seatedAtShut}, figures still walking at the shut {walkingAtShut}; ramp-foot steps {BoardingView.ShuttleSteps - steps0}; paces: {paces}");
            Log($"Q1 sounds (s after ENTER): door open {aOpen - t0:F2} (clunk {clunkOff:+0.00;-0.00} s vs the drawn landing), hydraulic {aHyd - t0:F2} (faded at the stop {OzShuttleRampSound.HydraulicStopsOpen - stopO0}x), door close {aClose - t0:F2} (clunk {closeOff:+0.00;-0.00} s vs the shut), latch {aLatch - t0:F2}, hydraulic faded at the shut {OzShuttleRampSound.HydraulicStopsShut - stopS0}x, All aboard {aBoard - t0:F2}");
            Check($"Q1 the squad boards; the ramp swings down in {ZWorld.ShuttleRampOpenSec:0.0#} s and shuts {ZWorld.ShuttleRampCloseSec:0.0#} s after it starts up (drawn: {uOpen1 - uOpen0:F2} / {uShut - uUp:F2} s, within 0.1 s at 60 fps)",
                  s.Passengers.Count == 4 && Mathf.Abs(uOpen1 - uOpen0 - (float)ZWorld.ShuttleRampOpenSec) <= 0.1f && Mathf.Abs(uShut - uUp - (float)ZWorld.ShuttleRampCloseSec) <= 0.1f);
            Check($"Q1 the ramp starts up as the last one is inside ({ZWorld.ShuttleBoardUpSec:F2} s after his foot) and shuts with every figure on its bench (4 seated, none walking)",
                  System.Math.Abs(sUp - (last + ZWorld.ShuttleBoardUpSec)) <= Tol(lagUp) && seatedAtShut == 4 && walkingAtShut == 0 && sShut >= hold);
            Check("Q1 the walks drawn: up the ramp 2.0 - 2.4 m/s and along the aisle at most 2.4 m/s (the walking clip, never the run), through the doorway on the rules' moment, seated within the rules' budget",
                  pacesOk && BoardingView.QuickRunClips == run0 && BoardingView.QuickEnterBuilt - in0 >= 4);
            Check("Q1 the sounds: door open with its clunk on the landing (within 0.08 s), the hydraulic with it and fading as the ramp stops, the close with its clunk on the shut (within 0.08 s), the latch, a ramp-foot step per figure, All aboard!",
                  aOpen > 0 && Mathf.Abs(clunkOff) <= 0.08f && aHyd >= aOpen - 0.05f && aHyd - aOpen <= 0.3f && OzShuttleRampSound.HydraulicStopsOpen > stopO0 && aClose > aHyd && Mathf.Abs(closeOff) <= 0.08f
                  && aLatch >= aClose && OzShuttleRampSound.HydraulicStopsShut > stopS0 && BoardingView.ShuttleSteps - steps0 >= 4 && aBoard > 0 && OzShuttleRampSound.DoorOpens > opens0);
            Check($"Q1 boarding to latch {total:F2} s: at least 30 % shorter than run 28's {OldBoard4:F2} s", total > 0 && total <= 0.70 * OldBoard4);
        }

        // ------------------------------------------------------------------ Q2 unloading
        IEnumerator Q2(ZObject s)
        {
            Log("Q2 unloading: EXIT");
            Cam(s); Watch(s); yield return Wait(0.5f);
            float t0 = Time.unscaledTime; int steps0 = BoardingView.ShuttleSteps, out0 = BoardingView.QuickExitBuilt, run0 = BoardingView.QuickRunClips;
            int stopO0 = OzShuttleRampSound.HydraulicStopsOpen, stopS0 = OzShuttleRampSound.HydraulicStopsShut;
            double te = W.Time; int n = s.Passengers.Count;
            bool taken = W.Eject(me, s);
            yield return WaitFor(() => s.ShuttleRamp01 > 0.05 || s.Passengers.Count == 0, 5f);
            int seatedWhileDown = bv ? bv.ShuttleWaitingNow : 0;
            yield return WaitFor(() => s.Passengers.Count == 0, 5f);
            double tx = W.Time; float rampAtExit = (float)s.ShuttleRamp01; double lagExit = System.Math.Max(lagNow, Time.deltaTime); var feet = new List<double>(s.ShuttleFootTimes);
            yield return At(tx + 0.3); Shot("q2a_getting_up_while_the_ramp_comes_down_h70");
            yield return WaitFor(() => sOpen1 >= 0, 5f); Shot("q2b_first_at_the_doorway_as_it_lands_h70");
            double firstDoor = feet.Count > 0 ? feet[0] - ZWorld.ShuttleRampOutSec + ZWorld.ShuttleExitDoorSec : -1;
            yield return At(feet.Count > 0 ? feet[0] - 0.4 : tx + 2f); Shot("q2c_down_the_ramp_in_a_line_h70");
            yield return WaitFor(() => sUp >= 0, 10f); yield return Wait(0.25f); Shot("q2d_ramp_up_behind_the_last_h70");
            yield return WaitFor(() => sShut >= 0, 10f); yield return Wait(1.2f); Shot("q2e_line_behind_ramp_shut_h70");
            double total = sShut - te, last = feet.Count > 0 ? feet[feet.Count - 1] : tx;
            // the robots drawn on their rules places behind the ship
            int onPlace = 0, outs = 0;
            foreach (var o in W.Objects)
            {
                if (!Alive(o) || !o.IsRobot || o.Owner != me) continue;
                if (Vector2.Distance(new Vector2(o.CenterX, o.CenterY), new Vector2(s.CenterX, s.CenterY)) / 8f > 16f) continue;
                outs++;
                if (g.Views.TryGetValue(o.Id, out var v) && v) { var want = ZGame.PxToWorld(o.CenterX, o.CenterY); var p = v.transform.position; if (new Vector2(p.x - want.x, p.z - want.z).magnitude <= 0.3f) onPlace++; }
            }
            float aOpen = FirstAt(t0, "shuttle_door_open"), aClose = FirstAt(t0, "shuttle_door_close"), aLatch = FirstAt(t0, "latch", "DOOR"), aOut = FirstAt(t0, "vo_shuttle_unload");
            float clunkOff = aOpen > 0 && uOpen1 > 0 ? aOpen + OzShuttleRampSound.LastClunkAfter - uOpen1 : 99f, closeOff = aClose > 0 && uShut > 0 ? aClose + 0.5f - uShut : 99f;
            string paces = Paces(out0, false, out bool pacesOk);
            Log($"Q2 rules: EXIT {(taken ? "taken" : "REFUSED")} at {te:F2}; let out +{tx - te:F2} with the ramp at {rampAtExit:F2} (want {ZWorld.ShuttleExitRamp01:F2}); the ramp down +{sOpen1 - te:F2}, the first at the doorway +{firstDoor - te:F2}; off the foot +{string.Join(" +", feet.ConvertAll(f => (f - te).ToString("F2")))}; the ramp up +{sUp - te:F2} (want +{last - ZWorld.ShuttleExitTipSec - te:F2}), shut +{sShut - te:F2}; EXIT -> latch {total:F2} s (run 28: {OldUnload4:F2} s)");
            Log($"Q2 drawn: seated as the ramp started down {seatedWhileDown} (peak {waitingPeak}); out {outs}, drawn on their places {onPlace}; ramp-foot steps {BoardingView.ShuttleSteps - steps0}; paces: {paces}");
            Log($"Q2 sounds (s after EXIT): Everybody out {aOut - t0:F2}, door open {aOpen - t0:F2} (clunk {clunkOff:+0.00;-0.00} s vs the landing), door close {aClose - t0:F2} (clunk {closeOff:+0.00;-0.00} s vs the shut), latch {aLatch - t0:F2}; hydraulic faded at the stop {OzShuttleRampSound.HydraulicStopsOpen - stopO0}x / at the shut {OzShuttleRampSound.HydraulicStopsShut - stopS0}x");
            Check($"Q2 EXIT: seated while the ramp starts down, let out at {ZWorld.ShuttleExitRamp01:F2} of its swing, the first at the doorway as it lands (within 0.03 s)",
                  taken && seatedWhileDown > 0 && Mathf.Abs(rampAtExit - (float)ZWorld.ShuttleExitRamp01) <= 0.03f + (float)(lagExit / ZWorld.ShuttleRampOpenSec) && System.Math.Abs(firstDoor - sOpen1) <= Tol(lagOpen1));
            Check("Q2 the walks drawn: the aisle at most 2.4 m/s, down the ramp 2.0 - 2.4 m/s (the walking clip), at the doorway and off the foot on the rules' moments",
                  pacesOk && BoardingView.QuickRunClips == run0 && BoardingView.QuickExitBuilt - out0 >= n);
            Check($"Q2 the ramp starts up as the last one steps off its tip ({ZWorld.ShuttleExitTipSec:F2} s before his foot), shuts {ZWorld.ShuttleRampCloseSec:0.0#} s later; all {n} out and drawn on their places behind the ship",
                  System.Math.Abs(sUp - (last - ZWorld.ShuttleExitTipSec)) <= Tol(lagUp) && System.Math.Abs(sShut - sUp - ZWorld.ShuttleRampCloseSec) <= Tol(System.Math.Max(lagUp, lagShut)) && outs >= n && onPlace >= n);
            Check("Q2 the sounds: Everybody out!, the door open with its clunk on the landing, the hydraulic fading as the ramp stops, the close on the shut, the latch, a ramp-foot step per figure",
                  aOut > 0 && aOpen > 0 && Mathf.Abs(clunkOff) <= 0.08f && OzShuttleRampSound.HydraulicStopsOpen > stopO0 && Mathf.Abs(closeOff) <= 0.08f && aLatch >= aClose && OzShuttleRampSound.HydraulicStopsShut > stopS0 && BoardingView.ShuttleSteps - steps0 >= n);
            Check($"Q2 EXIT to latch {total:F2} s: at least 30 % shorter than run 28's {OldUnload4:F2} s", total > 0 && total <= 0.70 * OldUnload4);
        }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || input == null || bv == null) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation || !ZWorld.ShuttleOn) { Log("no playable shuttle (level 16, the option SHUTTLE on, not 'Zod exact')"); fails++; Finish(); yield break; }
            me = g.player;
            int bots = W.Bots.Count; W.Bots.Clear(); HoldFire();
            dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            errors = 0;
            Log($"K422 faster boarding proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}; CPU removed {bots}, the other teams hold their fire ({heldFire.Count}); " +
                $"swings {ZWorld.ShuttleRampOpenSec:0.0#} / {ZWorld.ShuttleRampCloseSec:0.0#} s, file {ZWorld.ShuttleFileGapSec:F2} s, foot -> doorway {ZWorld.ShuttleBoardDoorSec:F2} s, seat -> doorway {ZWorld.ShuttleExitDoorSec:F2} s, quick ramp {ZWorld.ShuttleQuickRamp}");
            var s = Ship(); var sq = s != null ? Squad(s, 4) : null;
            if (s == null || sq == null) { Check("a ship dropped and a squad of 4 behind it", false); Finish(); yield break; }
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleRampPhase == ZShuttleRamp.Shut, 10f);
            yield return Wait(1.0f);
            yield return Q1(s, sq);
            yield return Wait(1.0f);
            if (s.Passengers.Count > 0) yield return Q2(s); else Check("Q2 a squad aboard for the exit", false);
            yield return Wait(1f);
            Check($"the console during the whole run: {errors} errors / exceptions", errors == 0);
            Finish();
        }

        void Finish()
        {
            watch = null;
            Log($"K422 faster boarding proof: {passes} PASS, {fails} FAIL (expected 11 PASS: Q1 5, Q2 5, the console 1)");
            Report = $"{passes} PASS, {fails} FAIL";
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "faster_boarding_proof_log.txt"), sb.ToString()); } catch (System.Exception e) { Debug.LogWarning("[OzShuttleQuickBoardProof] log not written: " + e.Message); }
            Destroy(gameObject, 1f);
        }
    }
}
