using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K431 Play proof (item 430): the rings, frame and health bar of robots getting out of a carrier follow their drawn figures
    /// (View/BoardingView.ExitRings.cs), on level 16 in Play (menu OpenZ/V1.1/Boarding: K431 Exit Rings Proof, Editor/OzExitRingsMenu.cs).
    /// Set-up: the CPU teams removed, morning (day clock 0.10, held). Stages:
    ///  S the shuttle: a red grunt squad of 4 boards (selected while it walks in), the ship flies to a free spot, EXIT on the touchdown
    ///    frame (the game's own PlayerInput.EjectUnit); the squad getting out is kept selected;
    ///  A an APC beside it: a red grunt squad boards (selected), EXIT; the squad getting out is kept selected.
    /// Every frame of each exit, for each robot with an exit walk: inside the hull = not marked and its own model hidden on its spot; out =
    /// marked at its figure; the walk over = marked at its own model, shown. All through the proof (the boardings too): no selected robot
    /// near the carrier is marked while nothing is drawn where it is marked. Counted PASS / FAIL lines and full-screen pictures from the
    /// normal RTS camera. Output: <see cref="Folder"/> (the work tree's records, milestones/exit_rings_k431, found upward at run time). Test only.
    /// </summary>
    public sealed class OzExitRingsProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "exit_rings_k431");
            return System.IO.Path.Combine(Application.persistentDataPath, "exit_rings_k431");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzExitRingsProof").AddComponent<OzExitRingsProof>();
        }

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; Team me = Team.Red; BoardingView bv;
        ZObject s, lead, watchCarrier; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes; string dir;
        int invFrames, invBad; string invFirstBad;
        readonly List<int> ids = new List<int>();

        sealed class Stats
        {
            public string tag; public int frames, insideFrames, insideShown, outFrames, robots, endOk, endBad, held0, held1, onFig0, onFig1; public float firstOutGap = -1f, maxOutGap;
            public readonly HashSet<int> seen = new HashSet<int>(), wentOut = new HashSet<int>();
        }

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>(); bv = BoardingView.I;
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
            StartCoroutine(Invariant());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzExitRingsProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        void Select(params ZObject[] os) { input.Selection.Clear(); foreach (var o in os) if (o != null) input.Selection.Add(o); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log("   picture " + name + ".png"); }
        UnitView V(ZObject o) => o != null && g.Views.TryGetValue(o.Id, out var v) ? v : null;
        static bool Alive(ZObject o) => o != null && !o.Destroyed && o.KillAt < 0;
        static string F(double v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
        static bool Drawn(UnitView v)
        {
            if (!v || !v.gameObject.activeInHierarchy) return false;
            foreach (var r in v.GetComponentsInChildren<Renderer>()) if (r.enabled && !(r is ParticleSystemRenderer) && !(r is TrailRenderer) && !(r is LineRenderer)) return true;
            return false;
        }

        /// <summary>All through the proof: a selected robot (or a member of a selected squad) near the watched carrier that the overlays
        /// mark (FigureAt true) where nothing is drawn: its own model hidden and no figure marked in its place.</summary>
        IEnumerator Invariant()
        {
            var list = new List<ZObject>();
            while (true)
            {
                yield return new WaitForEndOfFrame();
                if (W == null || input == null || watchCarrier == null) continue;
                var cv = V(watchCarrier); if (!cv) continue;
                list.Clear();
                foreach (var o in input.Selection) { if (o == null || o.Kind != ZKind.Robot) continue; list.Add(o); foreach (var m in o.Minions) list.Add(m); }
                if (list.Count == 0) continue;
                invFrames++;
                foreach (var o in list)
                {
                    if (!Alive(o)) continue;
                    var v = V(o); if (!v || Flat(v.transform.position, cv.transform.position) > 40f) continue;
                    int st = BoardingView.ExitFigureState(o, out _);
                    if (st == 0 && !Drawn(v)) { invBad++; if (invFirstBad == null) invFirstBad = $"{o.Type} #{o.Id} at world time {F(W.Time)} s"; }
                }
            }
        }

        /// <summary>One exit, watched every frame until every walk is over (+0.6 s) or maxSec.</summary>
        IEnumerator Watch(Stats st, ZObject carrier, float maxSec, string camNote)
        {
            st.held0 = BoardingView.ExitRingsHeld; st.onFig0 = BoardingView.ExitRingsOnFigure;
            float t = 0f, quiet = 0f; bool shotA = false, shotB = false, any = false; float firstOutAt = -1f;
            while (t < maxSec)
            {
                yield return new WaitForEndOfFrame();
                t += Time.deltaTime;
                bv.ExitFigureIds(ids);
                if (ids.Count == 0) { if (any) { quiet += Time.deltaTime; if (quiet >= 0.6f) break; } continue; }
                any = true; quiet = 0f; st.frames++;
                bool allInside = true;
                foreach (var id in ids)
                {
                    var o = W.GetAny(id); if (o == null) continue;
                    st.seen.Add(id);
                    var lo = o.Leader ?? o; if (!input.Selection.Contains(lo)) input.Selection.Add(lo);   // the squad getting out stays selected (rings, frame, bar)
                    var v = V(o);
                    int state = BoardingView.ExitFigureState(o, out var fig);
                    if (state == 1)
                    {
                        st.insideFrames++;
                        if (v && Drawn(v)) st.insideShown++;                                            // its own model must still be hidden on its spot
                    }
                    else if (state == 2)
                    {
                        allInside = false; st.outFrames++;
                        float gap = v ? Flat(fig, v.transform.position) : 0f;
                        if (st.wentOut.Add(id)) { if (st.firstOutGap < 0f) st.firstOutGap = gap; st.maxOutGap = Mathf.Max(st.maxOutGap, gap); }
                        if (firstOutAt < 0f) firstOutAt = t;
                    }
                    else allInside = false;
                }
                if (!shotA && allInside && t >= 0.6f) { shotA = true; Shot($"k431_{st.tag}_a_inside_no_rings_{camNote}"); }
                if (!shotB && firstOutAt >= 0f && t >= firstOutAt + 0.25f) { shotB = true; Shot($"k431_{st.tag}_b_first_out_ring_at_figure_{camNote}"); }
            }
            st.held1 = BoardingView.ExitRingsHeld; st.onFig1 = BoardingView.ExitRingsOnFigure;
            yield return null;
            Shot($"k431_{st.tag}_c_all_out_rings_on_spots_{camNote}");
            st.robots = st.seen.Count;
            foreach (var id in st.seen)
            {
                var o = W.GetAny(id); if (!Alive(o)) continue;
                var v = V(o); var p = v ? v.transform.position : Vector3.zero; var p0 = p;
                bool marked = BoardingView.FigureAt(o, ref p);
                if (marked && (p - p0).sqrMagnitude < 1e-6f && Drawn(v) && BoardingView.ExitFigureState(o, out _) == 0) st.endOk++; else st.endBad++;
            }
            Log($"   {st.tag}: {st.robots} robots got out over {st.frames} frames; inside (not marked) {st.insideFrames} robot-frames, own model shown meanwhile {st.insideShown}; marked at the figure {st.outFrames} robot-frames ({st.wentOut.Count} robots), the first mark at the figure {F(st.firstOutGap)} m from its spot (largest {F(st.maxOutGap)}); overlay asks: held +{st.held1 - st.held0}, at the figure +{st.onFig1 - st.onFig0}; after: {st.endOk} marked at their own models, {st.endBad} not");
        }

        void Checks(Stats st)
        {
            Check($"{st.tag}1 inside the hull: no ring / frame / bar ({st.insideFrames} robot-frames held, overlay asks held +{st.held1 - st.held0}) while each robot's own model stays hidden on its spot (shown meanwhile: {st.insideShown})",
                  st.robots > 0 && st.insideFrames > 0 && st.insideShown == 0 && st.held1 - st.held0 > 0);
            Check($"{st.tag}2 out: marked at the walking figure ({st.wentOut.Count} of {st.robots} robots, {st.outFrames} robot-frames, overlay asks +{st.onFig1 - st.onFig0}); the first mark stood {F(st.firstOutGap)} m from the robot's spot (largest {F(st.maxOutGap)}, at least 0.5 m: not on the spot)",
                  st.robots > 0 && st.wentOut.Count == st.robots && st.outFrames > 0 && st.onFig1 - st.onFig0 > 0 && st.maxOutGap >= 0.5f);
            Check($"{st.tag}3 the walks over: every robot marked at its own model, drawn ({st.endOk} of {st.robots})", st.robots > 0 && st.endOk == st.robots && st.endBad == 0);
        }

        /// <summary>A free tile middle (the ship can land there now) between minR and maxR px from (cx, cy), the nearest first.</summary>
        int[] Free(int cx, int cy, int minR, int maxR)
        {
            long best = long.MaxValue; int[] r = null; int e = ZWorld.ShuttleEdgeTiles;
            for (int ty = e; ty < W.Map.height - e; ty++)
                for (int tx = e; tx < W.Map.width - e; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8; long d = (long)(x - cx) * (x - cx) + (long)(y - cy) * (y - cy);
                    if (d >= best || d < (long)minR * minR || d > (long)maxR * maxR) continue;
                    if (W.ShuttleNeverLands(x, y, s, out _) || !W.ShuttlePadFree(x, y, s, out _)) continue;
                    best = d; r = new[] { x, y };
                }
            return r;
        }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || input == null || !bv) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation || W.PlayableShuttle == null) { Log("no playable shuttle (level 16, the shuttle rule on, the option SHUTTLE on)"); fails++; Finish(); yield break; }
            me = g.player; s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s);
            if (!ship) { Log("the shuttle has no view"); fails++; Finish(); yield break; }
            Log($"K431 exit rings proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, world time {F(W.Time)} s; switch {BoardingView.ExitRingsFollowOn}");
            int bots = W.Bots.Count; W.Bots.Clear();
            var dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            Log($"set-up: CPU removed {bots}, day 0.10 held");
            Check("E0 the K431 switch is on", BoardingView.ExitRingsFollowOn);

            // ---- S: the shuttle
            watchCarrier = s;
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne && s.ShuttleLevelled && s.ShuttleRampPhase == ZShuttleRamp.Shut && W.Time >= s.ShuttleRampHoldUntil, 30f);
            var (bx, by) = s.ShuttleBackPoint(6);
            lead = W.CreateRobotGroup("grunt", bx - 8, by - 8, me, 4);
            if (lead != null) { g.SpawnUnitView(lead); foreach (var m in lead.Minions) g.SpawnUnitView(m); }
            yield return Wait(0.5f);
            Select(lead);                                                                                 // selected while it walks in: the boarding is watched by the invariant
            Cam(V(s) ? V(s).transform.position : Vector3.zero, 45f);
            W.SendWaypoints(me, lead, new[] { new ZWaypoint { mode = WpMode.Enter, x = s.CenterX, y = s.CenterY, refId = s.Id, playerGiven = true } });
            yield return WaitFor(() => s.Passengers.Count > 0, 60f);
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Shut && W.Time >= s.ShuttleRampHoldUntil, 20f);
            Check($"S0 a squad of 4 boarded the shuttle ({s.Passengers.Count})", s.Passengers.Count == 4);
            yield return Wait(0.5f);
            var q = Free(s.CenterX, s.CenterY, 320, 700);
            if (q == null) { Check("S a landing place", false); Finish(); yield break; }
            OzPlayableShuttle.ProofRoughNext = false;
            Select(s); W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = q[0], y = q[1], playerGiven = true } });
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Descend, 60f);
            Cam(ship.transform.position, 45f);
            yield return WaitFor(() => s.ShuttleState != ZShuttleState.Descend, 30f);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed, 10f);
            bool taken = input.EjectUnit(s);
            Log($"   landed ({(s.ShuttleRoughLanding ? "a crash landing" : "a normal landing")}) at world time {F(W.Time)} s; EXIT taken {taken}");
            Check("S0 EXIT taken on the touchdown", taken);
            Select();                                                                                     // the squad getting out is selected by Watch
            var ss = new Stats { tag = "S" };
            yield return WaitFor(() => s.Passengers.Count == 0, 15f);                                      // the rules let them out (the exit walks begin)
            yield return Watch(ss, s, 20f, "h45");
            Checks(ss);

            // ---- A: an APC beside the ship
            yield return Wait(0.5f);
            int ax = (s.CenterX >> 4) + 6, ay = (s.CenterY >> 4) + 6;
            OzCheatSpawner.FreeTile(W, ax, ay, 2, false, out ax, out ay);
            var apc = W.CreateObject("vehicle", "apc", ax * 16, ay * 16, me);
            if (apc != null) g.SpawnUnitView(apc);
            yield return Wait(0.6f);
            var door = apc != null ? ApcRearDoor.Of(V(apc)) : null;
            Check($"A0 an APC with its rear door ({(apc != null ? "#" + apc.Id : "none")}, door {door != null})", apc != null && door != null);
            if (apc == null || door == null) { Finish(); yield break; }
            watchCarrier = apc;
            int rx = ax, ry = ay + 6; OzCheatSpawner.FreeTile(W, rx, ry, 1, true, out rx, out ry);
            var sq = W.CreateRobotGroup("grunt", rx * 16, ry * 16, me, -1);
            if (sq != null) { g.SpawnUnitView(sq); foreach (var m in sq.Minions) g.SpawnUnitView(m); }
            yield return Wait(0.6f);
            Select(sq);
            Cam(V(apc).transform.position, 32f);
            W.SendWaypoints(me, sq, new[] { new ZWaypoint { mode = WpMode.Enter, refId = apc.Id, x = apc.CenterX, y = apc.CenterY, playerGiven = true } });
            yield return WaitFor(() => apc.Passengers.Count > 0, 30f);
            yield return WaitFor(() => door.IsShut, 10f);
            Check($"A0 a grunt squad boarded the APC ({apc.Passengers.Count})", apc.Passengers.Count > 0);
            yield return Wait(0.5f);
            Select(apc);
            bool takenA = input.EjectUnit(apc);
            Check("A0 EXIT taken on the APC", takenA);
            Select();
            var sa = new Stats { tag = "A" };
            yield return WaitFor(() => apc.Passengers.Count == 0, 5f);
            yield return Watch(sa, apc, 15f, "h32");
            Checks(sa);

            Check($"INV no selected robot near the carrier was marked where nothing is drawn ({invFrames} frames watched, the boardings and both exits; first bad: {invFirstBad ?? "none"})", invFrames > 0 && invBad == 0);
            Log("   look at: the a pictures show no ring / frame / bar on the empty ground behind the ramp; the b pictures the ring under the first robot walking down the ramp; the c pictures every ring under its robot");
            Finish();
        }

        void Finish()
        {
            OzPlayableShuttle.ProofRoughNext = false; watchCarrier = null;
            Log($"K431 result: {passes} PASS, {fails} FAIL");
            Report = fails == 0 ? $"PASS ({passes})" : $"FAIL ({fails} of {passes + fails})";
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "exit_rings_proof_log.txt"), sb.ToString());
            StopAllCoroutines();
            Destroy(gameObject);
        }
    }
}
