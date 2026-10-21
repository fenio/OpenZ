using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// carrier UNDER FIRE, their drawn place every frame, on level 16 in Play (menu OpenZ/V1.1/Boarding: K441 Exit Walk Proof,
    /// Editor/OzExitWalkMenu.cs). Set-up: the CPU teams removed, morning (day clock 0.10, held). In every stage a blue grunt squad of 3 stands
    /// about 10 m to the side of where the robots will stand and is ordered onto that spot (it shoots whatever it meets). Stages:
    ///  B  BEFORE (both K441 switches off: BoardingView.ExitWalkKeepOn, ZWorld.ExitWalkHoldOn): a red grunt squad of 4 boards the shuttle,
    ///     it flies to a free spot, EXIT on the touchdown frame (the game's own PlayerInput.EjectUnit): the old jump measured;
    ///  S  AFTER (both on): the same with a fresh squad at another spot;
    ///  A  an APC beside it (a red grunt squad of 3), EXIT under fire;
    ///  G  Zod's own crew exit: a red gatling gun's gunner EJECTed under fire (the rules let him fire at once: his drawn shots wait).
    /// Every frame, for each robot let out: its drawn place (its exit figure while it walks, then its own model) and the step between two
    /// frames (flat), the pace (the step over the frame's game time); the hand-over (the gap and the turn between the figure and the model
    /// when the model takes over: BoardingView's own measure); the rules' shots by and at it (World.Events in Update) while its figure walks.
    /// Counted PASS / FAIL lines, a CSV trace per stage (frame, times, robot, state, place, step) and full-screen pictures from the normal RTS
    /// camera. Output: <see cref="Folder"/> (the work tree's records, milestones/exit_walk_k441, found upward at run time). Test only.
    /// K441b: each stage's log also names the shuttle walks from the ramp foot that the view planned again on the ship as drawn
    /// (BoardingView.ExitWalkReplan: the metres, the legs, the time at rule DV132's pace, and the time it had been keyed at the let-out).
    /// </summary>
    [DefaultExecutionOrder(75)]
    public sealed class OzExitWalkProof : MonoBehaviour
    {
        public static string Report = "idle";
        /// <summary>AFTER: the fastest a drawn robot may move between two frames (m/s of game time; the chase pace is 4.5, the eased steps of the scripted walks peak near 4) and the
        /// largest gap / turn at a hand-over.</summary>
        public static float MaxPace = 6.0f, MaxHandGapM = 0.06f, MaxHandTurnDeg = 8f;

        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "exit_walk_k441");
            return System.IO.Path.Combine(Application.persistentDataPath, "exit_walk_k441");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzExitWalkProof").AddComponent<OzExitWalkProof>();
        }

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; Team me = Team.Red; BoardingView bv;
        ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes; string dir;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly List<int> ids = new List<int>(), idsU = new List<int>();

        sealed class Bot { public int id; public bool walked, ended; public Vector3 last; public bool hasLast; public float endT = -1f; public int shotsWalking, shotsAfter, shotAtWalking; public float maxStep, maxPace; public double outAt = -1, endSim = -1; }
        sealed class Stage
        {
            public string tag; public readonly Dictionary<int, Bot> bots = new Dictionary<int, Bot>(); public float maxStep, maxPace; public int frames, steps0, held0, rel0, ended0, rp0;
            public float handGap, handTurn; public int held, released, notHeld, ended, steps; public readonly StringBuilder csv = new StringBuilder(); public float worstAt = -1f; public int worstId = -1;
        }
        Stage cur, lastStage;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>(); bv = BoardingView.I;
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        /// <summary>The rules' shots of this frame (after ZGame's tick, before CombatView clears them): by / at a robot of the stage.</summary>
        void Update()
        {
            if (cur == null || W == null || !bv) return;
            bv.ExitWalkIds(idsU);
            foreach (var id in idsU) if (!cur.bots.ContainsKey(id)) cur.bots[id] = new Bot { id = id, outAt = OutAt(id) };   // registered the frame its walk starts
            foreach (var e in W.Events)
            {
                if (e.type != ZEventType.Hitscan && e.type != ZEventType.MissileFired) continue;
                if (cur.bots.TryGetValue(e.objId, out var a)) { if (BoardingView.ExitWalkFigure(a.id, out _, out _) != 0) a.shotsWalking++; else a.shotsAfter++; }
                if (cur.bots.TryGetValue(e.otherId, out var b) && BoardingView.ExitWalkFigure(b.id, out _, out _) != 0) b.shotAtWalking++;
            }
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzExitWalkProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log("   picture " + name + ".png"); }
        UnitView V(ZObject o) => o != null && g.Views.TryGetValue(o.Id, out var v) ? v : null;
        /// <summary>K441b: rule DV132's out moment of a robot whose walk starts now (sim seconds; -1 = none: no DV132 hold).</summary>
        double OutAt(int id) { var o = W != null ? W.GetAny(id) : null; return o != null && o.ExitOutAt >= 0 ? o.ExitOutAt : -1; }
        static bool Alive(ZObject o) => o != null && !o.Destroyed && o.KillAt < 0;
        static string F(double v) => v.ToString("0.00", Inv);
        static string F3(double v) => v.ToString("0.000", Inv);
        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
        static bool Drawn(UnitView v)
        {
            if (!v || !v.gameObject.activeInHierarchy) return false;
            foreach (var r in v.GetComponentsInChildren<Renderer>()) if (r.enabled && !(r is ParticleSystemRenderer) && !(r is TrailRenderer) && !(r is LineRenderer)) return true;
            return false;
        }

        void Spawned(ZObject lead) { if (lead == null) return; g.SpawnUnitView(lead); foreach (var m in lead.Minions) g.SpawnUnitView(m); }

        /// <summary>A free tile middle (the ship can land there now) between minR and maxR px from (cx, cy), away from `avoid` by `apart` px.</summary>
        int[] Pad(int cx, int cy, int minR, int maxR, int[] avoid, int apart)
        {
            long best = long.MaxValue; int[] r = null; int e = ZWorld.ShuttleEdgeTiles;
            for (int ty = e; ty < W.Map.height - e; ty++)
                for (int tx = e; tx < W.Map.width - e; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8; long d = (long)(x - cx) * (x - cx) + (long)(y - cy) * (y - cy);
                    if (d >= best || d < (long)minR * minR || d > (long)maxR * maxR) continue;
                    if (avoid != null && (long)(x - avoid[0]) * (x - avoid[0]) + (long)(y - avoid[1]) * (y - avoid[1]) < (long)apart * apart) continue;
                    if (W.ShuttleNeverLands(x, y, s, out _) || !W.ShuttlePadFree(x, y, s, out _)) continue;
                    best = d; r = new[] { x, y };
                }
            return r;
        }

        /// <summary>A blue grunt squad of 3 about 10 m to the side of (px, py), ordered onto that spot (attack-move).</summary>
        ZObject Enemy(int px, int py, float sideX, float sideY)
        {
            foreach (int sgn in new[] { 1, -1 })
                foreach (int dist in new[] { 80, 96, 64 })
                {
                    int ex = (px + (int)(sgn * sideX * dist)) >> 4, ey = (py + (int)(sgn * sideY * dist)) >> 4;
                    if (!OzCheatSpawner.FreeTile(W, ex, ey, 1, true, out int tx, out int ty)) continue;
                    var b = W.CreateRobotGroup("grunt", tx * 16, ty * 16, Team.Blue, 3);
                    if (b == null) continue;
                    Spawned(b);
                    W.SendWaypoints(Team.Blue, b, new[] { ZWaypoint.Move(px, py, true) });
                    Log($"   the enemy: a blue grunt squad of 3 at {F(dist / 8f)} m to the side, ordered onto the spot");
                    return b;
                }
            Log("   no spot for the enemy");
            return null;
        }

        void Kill(ZObject lead) { if (lead == null) return; foreach (var m in new List<ZObject>(OzV11Squad(lead))) if (Alive(m)) { m.DamageHealth(m.Health); W.UpdateObjectHealth(m); } }
        static IEnumerable<ZObject> OzV11Squad(ZObject o) { yield return o; foreach (var m in o.Minions) yield return m; }

        void Begin(string tag)
        {
            cur = new Stage { tag = tag, steps0 = BoardingView.ShuttleSteps, held0 = BoardingView.ExitShotsHeld, rel0 = BoardingView.ExitShotsReleased, ended0 = BoardingView.ExitWalksEnded, rp0 = BoardingView.ExitReplanned };
            BoardingView.ExitWorstHandGapM = 0f; BoardingView.ExitWorstHandTurnDeg = 0f;
            cur.csv.AppendLine("frame,real_s,game_s,robot,state,x,y,z,step_m,pace_mps,model_x,model_z");
        }

        /// <summary>One exit watched every frame until every walk is over (+0.8 s) or maxSec: each robot's drawn place and its steps.</summary>
        IEnumerator Watch(float maxSec, string cam, bool picOnRamp)
        {
            var st = cur; float t = 0f, quiet = 0f; bool any = false, picA = false, picB = false;
            while (t < maxSec)
            {
                yield return new WaitForEndOfFrame();
                t += Time.deltaTime; st.frames++;
                double simNow = W.Time;
                float sdt = Time.deltaTime * (g.paused ? 0f : Mathf.Max(0.05f, g.gameSpeed));             // the frame's game time, as BoardingView steps its figures (the rules' clock moves in whole 10 ms ticks: 1 or 2 a frame at 60 fps)
                bv.ExitWalkIds(ids);
                foreach (var id in ids) if (!st.bots.ContainsKey(id)) st.bots[id] = new Bot { id = id, outAt = OutAt(id) };
                bool walking = ids.Count > 0;
                if (walking) { any = true; quiet = 0f; } else if (any) { quiet += Time.deltaTime; if (quiet >= 0.8f) break; }
                int onRamp = 0;
                foreach (var b in st.bots.Values)
                {
                    var o = W.GetAny(b.id); var v = V(o);
                    int state = BoardingView.ExitWalkFigure(b.id, out var fig, out _);
                    Vector3 p; bool has;
                    if (state == 2) { p = fig; has = true; b.walked = true; onRamp++; }
                    else if (state == 0 && b.walked && v && Drawn(v)) { p = v.transform.position; has = true; if (!b.ended) { b.ended = true; b.endT = t; b.endSim = simNow; } }
                    else { has = false; p = Vector3.zero; }
                    if (!has) { b.hasLast = false; continue; }
                    bool watch = state == 2 || (b.ended && t - b.endT <= 0.4f);                       // the walk, the hand-over and 0.4 s after it
                    if (b.hasLast && watch && sdt > 1e-5f)
                    {
                        float step = Flat(p, b.last), pace = step / sdt;
                        if (step > b.maxStep) b.maxStep = step;
                        if (pace > b.maxPace) b.maxPace = pace;
                        if (pace > st.maxPace) { st.maxPace = pace; st.worstAt = t; st.worstId = b.id; }
                        if (step > st.maxStep) st.maxStep = step;
                        var mp = v ? v.transform.position : Vector3.zero;
                        st.csv.AppendLine($"{st.frames},{F3(t)},{F3(simNow)},{b.id},{state},{F3(p.x)},{F3(p.y)},{F3(p.z)},{F3(step)},{F(pace)},{F3(mp.x)},{F3(mp.z)}");
                    }
                    b.last = p; b.hasLast = true;
                }
                if (picOnRamp && !picA && onRamp > 0 && t > 1.6f) { picA = true; Shot($"k441_{st.tag}_a_out_under_fire_{cam}"); }
                if (picOnRamp && !picB && picA && t > 3.4f) { picB = true; Shot($"k441_{st.tag}_b_walking_to_spots_{cam}"); }
            }
            yield return null;
            Shot($"k441_{st.tag}_c_all_out_{cam}");
            st.handGap = BoardingView.ExitWorstHandGapM; st.handTurn = BoardingView.ExitWorstHandTurnDeg;
            st.held = BoardingView.ExitShotsHeld - st.held0; st.released = BoardingView.ExitShotsReleased - st.rel0; st.ended = BoardingView.ExitWalksEnded - st.ended0;
            st.steps = BoardingView.ShuttleSteps - st.steps0;
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"k441_trace_{st.tag}.csv"), st.csv.ToString());
            int shotsW = 0, shotsA = 0, atW = 0; foreach (var b in st.bots.Values) { shotsW += b.shotsWalking; shotsA += b.shotsAfter; atW += b.shotAtWalking; }
            var per = new StringBuilder(); foreach (var b in st.bots.Values) per.Append($" #{b.id} {F(b.maxStep)} m / {F(b.maxPace)} m/s;");
            Log($"   {st.tag}: {st.bots.Count} robots over {st.frames} frames; largest step in one frame {F(st.maxStep)} m, fastest {F(st.maxPace)} m/s (robot #{st.worstId} at {F(st.worstAt)} s);{per}");
            Log($"   {st.tag}: hand-overs (BoardingView) worst gap {F3(st.handGap)} m, worst turn {F(st.handTurn)} deg, walks ended on their robots {st.ended}; the rules' shots by them while walking {shotsW}, after {shotsA}, at them while walking {atW}; drawn shots held {st.held}, drawn after the walk {st.released}; ramp-foot step clanks {st.steps}; walks paced up to be out by the rules' moment so far {BoardingView.ExitPacedUp} (at most x{F(BoardingView.ExitPaceMaxSeen)}); trace k441_trace_{st.tag}.csv");
            int rp = BoardingView.ExitReplanned - st.rp0, rpLog = BoardingView.ExitReplanLog.Count;
            Log($"   {st.tag}: K441b walks from the ramp foot planned again on the ship as drawn {rp} (the most taken off a walk so far {F(BoardingView.ExitReplanCutMaxS)} s, added {F(BoardingView.ExitReplanAddMaxS)} s; switch BoardingView.ExitReplanOn {BoardingView.ExitReplanOn}, lead {F(BoardingView.ExitDueLeadSec)} s)");
            for (int i = Mathf.Max(0, rpLog - rp); i < rpLog; i++) Log("      " + BoardingView.ExitReplanLog[i]);
            foreach (var b in st.bots.Values)
                if (b.outAt >= 0) Log($"      #{b.id}: rule DV132's out moment {F(b.outAt)} s, its figure on its spot {(b.endSim >= 0 ? F(b.endSim) + " s (" + F(b.outAt - b.endSim) + " s before the robot may act; wanted >= 0)" : "never seen")}");
            lastStage = st; cur = null;
        }

        int Shots(Stage st, System.Func<Bot, int> f) { int n = 0; foreach (var b in st.bots.Values) n += f(b); return n; }

        /// <summary>A red grunt squad of 4 boards the ship where it stands, it flies to a free pad, the enemy is placed at the touchdown, EXIT.</summary>
        IEnumerator ShuttleStage(string tag, bool on, int[] avoid, System.Action<int[]> padOut)
        {
            BoardingView.ExitWalkKeepOn = on; ZWorld.ExitWalkHoldOn = on; lastStage = null;
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne && s.ShuttleLevelled && s.ShuttleRampPhase == ZShuttleRamp.Shut && W.Time >= s.ShuttleRampHoldUntil, 30f);
            var (bx, by) = s.ShuttleBackPoint(6);
            int ftx, fty; OzCheatSpawner.FreeTile(W, bx >> 4, by >> 4, 1, true, out ftx, out fty);
            var lead = W.CreateRobotGroup("grunt", ftx * 16, fty * 16, me, 4);
            Spawned(lead);
            yield return Wait(0.5f);
            Cam(V(s) ? V(s).transform.position : Vector3.zero, 45f);
            W.SendWaypoints(me, lead, new[] { new ZWaypoint { mode = WpMode.Enter, x = s.CenterX, y = s.CenterY, refId = s.Id, playerGiven = true } });
            yield return WaitFor(() => s.Passengers.Count > 0, 60f);
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Shut && W.Time >= s.ShuttleRampHoldUntil, 20f);
            Check($"{tag}0 a squad of 4 boarded the shuttle ({s.Passengers.Count}); the switches {(on ? "ON (after)" : "OFF (before)")}", s.Passengers.Count == 4);
            yield return Wait(0.5f);
            var q = Pad(s.CenterX, s.CenterY, 320, 900, avoid, 640);
            padOut(q);
            if (q == null) { Check($"{tag} a landing place", false); yield break; }
            OzPlayableShuttle.ProofRoughNext = false;
            W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = q[0], y = q[1], playerGiven = true } });
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Descend, 60f);
            Cam(ship.transform.position, 45f);
            yield return WaitFor(() => s.ShuttleState != ZShuttleState.Descend, 30f);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed, 10f);
            var (ax, ay) = s.ShuttleBackPoint(1); var (cx, cy) = s.ShuttleBackPoint(4); var (mx, my) = s.ShuttleBackPoint(2);
            float dx = cx - ax, dy = cy - ay, l = Mathf.Max(1f, Mathf.Sqrt(dx * dx + dy * dy)); dx /= l; dy /= l;
            var blue = Enemy(mx, my, -dy, dx);
            Begin(tag);
            bool taken = input.EjectUnit(s);
            Log($"   {tag}: landed ({(s.ShuttleRoughLanding ? "a crash landing" : "a normal landing")}) at world time {F(W.Time)} s; EXIT taken {taken}");
            Check($"{tag}0 EXIT taken on the touchdown with the enemy placed ({(blue != null ? "yes" : "no")})", taken && blue != null);
            yield return Watch(25f, "h45", true);
            Kill(blue);
        }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || input == null || !bv) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation || W.PlayableShuttle == null) { Log("no playable shuttle (level 16, the shuttle rule on, the option SHUTTLE on)"); fails++; Finish(); yield break; }
            me = g.player; s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s);
            if (!ship) { Log("the shuttle has no view"); fails++; Finish(); yield break; }
            bool keep0 = BoardingView.ExitWalkKeepOn, hold0 = ZWorld.ExitWalkHoldOn;
            Log($"K441 exit walk proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, world time {F(W.Time)} s; switches view {keep0} / rule {hold0}");
            int bots = W.Bots.Count; W.Bots.Clear();
            var dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            Log($"set-up: CPU removed {bots}, day 0.10 held");
            Check("E0 the K441 switches are on in the game (view BoardingView.ExitWalkKeepOn, rule ZWorld.ExitWalkHoldOn)", keep0 && hold0);

            // ---- B: before (both off): the old jump
            int[] padB = null, padS = null;
            yield return ShuttleStage("B", false, null, p => padB = p);
            var sB = lastStage;
            // ---- S: after (both on)
            yield return ShuttleStage("S", true, padB, p => padS = p);
            var sS = lastStage;
            BoardingView.ExitWalkKeepOn = true; ZWorld.ExitWalkHoldOn = true;
            if (sB != null) Check($"B BEFORE (switches off) the old jump under fire: the largest step of a drawn robot in one frame {F(sB.maxStep)} m, fastest {F(sB.maxPace)} m/s (wanted: over {F(MaxPace)} m/s, the bug of item 443 reproduced)", sB.maxPace > MaxPace);
            if (sS != null)
            {
                Check($"S1 AFTER the walks play out: the largest step of a drawn robot in one frame {F(sS.maxStep)} m, fastest {F(sS.maxPace)} m/s (at most {F(MaxPace)})", sS.bots.Count == 4 && sS.maxPace <= MaxPace);
                Check($"S2 every walk ended on its robot: {sS.ended} of {sS.bots.Count}, worst hand-over gap {F3(sS.handGap)} m (at most {F(MaxHandGapM)}), worst turn {F(sS.handTurn)} deg (at most {F(MaxHandTurnDeg)})", sS.ended >= sS.bots.Count && sS.bots.Count > 0 && sS.handGap <= MaxHandGapM && sS.handTurn <= MaxHandTurnDeg);
                Check($"S3 no robot fired and nobody shot at one while its figure walked out (rule DV132: by {Shots(sS, b => b.shotsWalking)}, at {Shots(sS, b => b.shotAtWalking)}); once out they fight (shots after {Shots(sS, b => b.shotsAfter)})", Shots(sS, b => b.shotsWalking) == 0 && Shots(sS, b => b.shotAtWalking) == 0 && Shots(sS, b => b.shotsAfter) > 0);
                Check($"S4 the ramp-foot step clank of every figure kept ({sS.steps} for {sS.bots.Count})", sS.steps >= sS.bots.Count);
            }

            // ---- A: an APC beside the ship
            yield return Wait(0.5f);
            int ax0 = (s.CenterX >> 4) + 7, ay0 = (s.CenterY >> 4) + 7;
            OzCheatSpawner.FreeTile(W, ax0, ay0, 2, false, out int atx, out int aty);
            var apc = W.CreateObject("vehicle", "apc", atx * 16, aty * 16, me);
            if (apc != null) g.SpawnUnitView(apc);
            yield return Wait(0.6f);
            var door = apc != null ? ApcRearDoor.Of(V(apc)) : null;
            Check($"A0 an APC with its rear door ({(apc != null ? "#" + apc.Id : "none")}, door {door != null})", apc != null && door != null);
            if (apc != null && door != null)
            {
                int rx = atx, ry = aty + 6; OzCheatSpawner.FreeTile(W, rx, ry, 1, true, out rx, out ry);
                var sq = W.CreateRobotGroup("grunt", rx * 16, ry * 16, me, 3);
                Spawned(sq);
                yield return Wait(0.6f);
                Cam(V(apc).transform.position, 32f);
                W.SendWaypoints(me, sq, new[] { new ZWaypoint { mode = WpMode.Enter, refId = apc.Id, x = apc.CenterX, y = apc.CenterY, playerGiven = true } });
                yield return WaitFor(() => apc.Passengers.Count > 0, 30f);
                yield return WaitFor(() => door.IsShut, 10f);
                Check($"A0 a grunt squad boarded the APC ({apc.Passengers.Count})", apc.Passengers.Count > 0);
                yield return Wait(0.5f);
                var blueA = Enemy(apc.CenterX, apc.CenterY, 0.7071f, 0.7071f);
                lastStage = null; Begin("A");
                bool takenA = input.EjectUnit(apc);
                Check("A0 EXIT taken on the APC under fire", takenA && blueA != null);
                yield return Watch(15f, "h32", false);
                var sA = lastStage ?? new Stage { tag = "A" };
                Check($"A1 the APC's file walks out: largest step {F(sA.maxStep)} m, fastest {F(sA.maxPace)} m/s (at most {F(MaxPace)}); ended on their robots {sA.ended} of {sA.bots.Count}, worst gap {F3(sA.handGap)} m, turn {F(sA.handTurn)} deg",
                      sA.bots.Count > 0 && sA.maxPace <= MaxPace && sA.ended >= sA.bots.Count && sA.handGap <= MaxHandGapM && sA.handTurn <= MaxHandTurnDeg);
                Check($"A2 no shot by or at them while walking out (by {Shots(sA, b => b.shotsWalking)}, at {Shots(sA, b => b.shotAtWalking)})", Shots(sA, b => b.shotsWalking) == 0 && Shots(sA, b => b.shotAtWalking) == 0);
                Kill(blueA);
            }

            // ---- G: Zod's own crew exit, a gatling gun's gunner
            yield return Wait(0.5f);
            int gx0 = (s.CenterX >> 4) - 8, gy0 = (s.CenterY >> 4) + 7;
            OzCheatSpawner.FreeTile(W, gx0, gy0, 2, false, out int gtx, out int gty);
            var gun = W.CreateObject("cannon", "gatling", gtx * 16, gty * 16, me);
            if (gun != null) g.SpawnUnitView(gun);
            yield return Wait(1.5f);                                                                      // its gunner sits down
            if (gun != null)
            {
                Cam(V(gun) ? V(gun).transform.position : Vector3.zero, 32f);
                var blueG = Enemy(gun.CenterX, gun.CenterY, -0.7071f, 0.7071f);
                yield return Wait(0.4f);
                lastStage = null; Begin("G");
                bool takenG = input.EjectUnit(gun);
                Check("G0 the gunner EJECTed under fire", takenG && blueG != null);
                yield return Watch(8f, "h32", false);
                var sG = lastStage ?? new Stage { tag = "G" };
                Check($"G1 Zod's own crew exit plays out too: largest step {F(sG.maxStep)} m, fastest {F(sG.maxPace)} m/s (at most {F(MaxPace)}); ended on its robot {sG.ended} of {sG.bots.Count}, gap {F3(sG.handGap)} m, turn {F(sG.handTurn)} deg",
                      sG.bots.Count > 0 && sG.maxPace <= MaxPace && sG.ended >= sG.bots.Count && sG.handGap <= MaxHandGapM && sG.handTurn <= MaxHandTurnDeg);
                Log($"   G: the rules let him fire at once (Zod): his rules' shots while his figure walked {Shots(sG, b => b.shotsWalking)}, their drawn shots held {sG.held}, drawn after the walk {sG.released} (listen: each held shot is heard when it is drawn)");
                Kill(blueG);
            }
            Log("   look at: B_c (before) the robots stood on their spots at once; S_a the first figure on the ramp while the enemy fires, nobody on the spots yet; S_b the figures walking to their spots; S_c every robot on its spot, turned to the fight");
            BoardingView.ExitWalkKeepOn = keep0; ZWorld.ExitWalkHoldOn = hold0;
            Finish();
        }

        void Finish()
        {
            OzPlayableShuttle.ProofRoughNext = false;
            Log($"K441 result: {passes} PASS, {fails} FAIL");
            Report = fails == 0 ? $"PASS ({passes})" : $"FAIL ({fails} of {passes + fails})";
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "exit_walk_proof_log.txt"), sb.ToString());
            StopAllCoroutines();
            Destroy(gameObject);
        }
    }
}
