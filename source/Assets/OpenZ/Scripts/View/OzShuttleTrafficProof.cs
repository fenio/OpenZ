using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K338 Play proof: several shuttles in the air (rule DV118's traffic, Sim/ZWorld.ShuttleTraffic.cs; the look, View/OzPlayableShuttle.Traffic.cs),
    /// on any level in Play (menu OpenZ/V1.1/Shuttle: K338 Air Traffic Proof, Editor/OzShuttleTrafficMenu.cs). Counted PASS / FAIL lines and
    /// full-screen pictures from the normal RTS camera at 70 m (uncropped), the camera on the middle of the ships. Set-up: the CPU teams removed
    /// (so the blue ships take orders, as a second player's would), morning (day clock 0.10, held). Stages:
    ///  T1 the testing panel's own drop (OzCheatShuttle.Spawn) of 4 shuttles, 2 red on one side, 2 blue on the other, about 80 m apart;
    ///  T2 they swap sides (red across to the blue side, blue across to the red side: head on and crossing in the middle). Every frame: the
    ///     rules' closest two ships in the air (middles), their silhouettes in the camera's picture, the motion off their lines turning back,
    ///     the avoid offset's hardest acceleration, each ship's drawn height; a strip of pictures every 0.5 s while they meet;
    ///  T3 all 4 sent to one point: they land round it, each on its own spot;
    ///  T4 night (day clock 0.62): they swap sides again: each ship's own landing lamps for its own descent, its nose searchlight in flight, its
    ///     own landing mark under it; pictures.
    /// Output: <see cref="Folder"/> (the work tree's records, milestones/shuttle_air_traffic_proof, found upward at run time). Test only.
    /// </summary>
    public sealed class OzShuttleTrafficProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_air_traffic_proof");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_air_traffic_proof");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShuttleTrafficProof").AddComponent<OzShuttleTrafficProof>();
        }

        ZGame g; ZWorld W; RtsCamera rts; DayNight dn; OpenZHud hud;
        readonly List<ZObject> ships = new List<ZObject>(); readonly List<OzPlayableShuttle> views = new List<OzPlayableShuttle>();
        readonly StringBuilder sb = new StringBuilder(); int fails, passes, frames; string dir;
        // per frame while watched
        bool watch; float minAirM = 999f, peakAcc, deepestPx, sameDeepestPx; int pictureOverlaps, sameLayerOverlaps, pairFrames, turnBacks, passFrames;
        readonly Dictionary<int, Vector2> lastDir = new Dictionary<int, Vector2>(); readonly Dictionary<int, float> lastTurn = new Dictionary<int, float>();
        readonly Dictionary<int, float> hMin = new Dictionary<int, float>(), hMax = new Dictionary<int, float>();
        // night
        readonly Dictionary<int, bool> landLamp = new Dictionary<int, bool>(), noseOn = new Dictionary<int, bool>(), markOn = new Dictionary<int, bool>();

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; rts = FindFirstObjectByType<RtsCamera>(); hud = OpenZHud.I;
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleTrafficProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); frames++; Log($"frame {name}.png"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { Follow(); t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { Follow(); t += Time.deltaTime; yield return null; } }
        static double MX(ZObject o) => o.LocX + o.XOver + o.WidthPix * 0.5;
        static double MY(ZObject o) => o.LocY + o.YOver + o.HeightPix * 0.5;

        /// <summary>The RTS camera at 70 m over the middle of the ships (in the air ones first).</summary>
        void Follow()
        {
            if (!rts || views.Count == 0) return;
            var live = views.Where(v => v && v.Obj != null && !v.Obj.Destroyed).ToList(); if (live.Count == 0) return;
            var air = live.Where(v => v.Obj.ShuttleAirborne).ToList(); var set = air.Count > 0 ? air : live;
            var c = Vector3.zero; foreach (var v in set) c += v.transform.position; c /= set.Count;
            rts.FreeLookOff(); rts.focus = Vector3.Lerp(rts.focus, new Vector3(c.x, 0f, c.z), 1f - Mathf.Exp(-Time.deltaTime * 3f)); rts.SetHeightImmediate(70f);
        }

        void Update()
        {
            if (!watch || W == null) return;
            var cam = Camera.main;
            for (int i = 0; i < ships.Count; i++)
            {
                var s = ships[i]; if (s == null || s.Destroyed || !s.ShuttleAirborne) continue;
                var v = views[i];
                // the motion off its line turning back (more than 120 degrees) within 3 s of its last turn
                var av = new Vector2((float)s.ShuttleAvoidVelXPx, (float)s.ShuttleAvoidVelYPx);
                if (av.magnitude > 0.8f)
                {
                    float now = (float)W.Time;
                    if (!lastDir.TryGetValue(s.Id, out var ld) || ld.sqrMagnitude < 1e-9f) lastTurn[s.Id] = now;
                    else if (Vector2.Dot(av, ld) < -0.5f * av.magnitude * ld.magnitude) { if (now - lastTurn[s.Id] < 3f) turnBacks++; lastTurn[s.Id] = now; }
                    lastDir[s.Id] = av;
                }
                peakAcc = Mathf.Max(peakAcc, (float)s.ShuttleAvoidAccelPx / 8f);
                if (v && s.ShuttleState == ZShuttleState.Cruise && s.ShuttleLift >= 1.0)
                {
                    float h = v.ShownHeight; hMin[s.Id] = hMin.TryGetValue(s.Id, out var a) ? Mathf.Min(a, h) : h; hMax[s.Id] = hMax.TryGetValue(s.Id, out var b) ? Mathf.Max(b, h) : h;
                }
                if (v && v.PassAddM != 0f) passFrames++;
                // night: each ship's own lights and mark
                var nl = v && v.Searchlights != null ? v.Searchlights.Night : null;
                if (nl != null && s.ShuttleState == ZShuttleState.Descend && nl.Plan.Landing > 0.5f) landLamp[s.Id] = true;
                if (nl != null && s.ShuttleState == ZShuttleState.Cruise && nl.Plan.Nose > 0.5f) noseOn[s.Id] = true;
                if (v && v.Shadow != null && v.Shadow.Shown && v.FeetHeight > 3f) markOn[s.Id] = true;
                for (int k = i + 1; k < ships.Count; k++)
                {
                    var o = ships[k]; if (o == null || o.Destroyed || !o.ShuttleAirborne) continue;
                    float d = (float)System.Math.Sqrt((MX(s) - MX(o)) * (MX(s) - MX(o)) + (MY(s) - MY(o)) * (MY(s) - MY(o))) / 8f;
                    minAirM = Mathf.Min(minAirM, d);
                    if (d < 22f && v && views[k])
                    {   // in the picture: every overlap (for information: also one flying over the other) and the same layer's (the verdict)
                        pairFrames++;
                        if (OzPlayableShuttle.HullsOverlap(cam, v, views[k]))
                        {
                            float px = OzPlayableShuttle.OverlapDepthPx(cam, v, views[k]);
                            pictureOverlaps++; deepestPx = Mathf.Max(deepestPx, px);
                            if (Mathf.Abs(v.transform.position.y - views[k].transform.position.y) < OzPlayableShuttle.SameLayerM) { sameLayerOverlaps++; sameDeepestPx = Mathf.Max(sameDeepestPx, px); }
                        }
                    }
                }
            }
        }

        void ResetWatch() { minAirM = 999f; peakAcc = 0f; deepestPx = sameDeepestPx = 0f; pictureOverlaps = sameLayerOverlaps = pairFrames = turnBacks = passFrames = 0; lastDir.Clear(); lastTurn.Clear(); }
        string PictureLine() => $"silhouettes overlapping in the picture at the same layer (drawn heights under {OzPlayableShuttle.SameLayerM:0.0} m apart) {sameLayerOverlaps} of {pairFrames} close pair-frames (the deepest {sameDeepestPx:F0} px); every overlap, also one flying over the other (for information) {pictureOverlaps} (the deepest {deepestPx:F0} px)";

        /// <summary>A free landing spot (ring by ring from (cx, cy), at most maxR px).</summary>
        int[] Spot(int cx, int cy, int maxR)
        {
            for (int r = 0; r <= maxR; r += 16)
                for (int a = 0; a < 32; a++)
                {
                    double t = a * System.Math.PI / 16;
                    var q = W.ShuttleClamp(cx + (int)(System.Math.Cos(t) * r), cy + (int)(System.Math.Sin(t) * r));
                    if (W.ShuttlePadFree(q.x, q.y, null, out _) && ships.All(o => (MX(o) - q.x) * (MX(o) - q.x) + (MY(o) - q.y) * (MY(o) - q.y) > 120.0 * 120.0)) return new[] { q.x, q.y };
                    if (r == 0) break;
                }
            return null;
        }

        bool Move(ZObject s, int x, int y) => W.SendWaypoints(s.Owner, s, new[] { new ZWaypoint { mode = WpMode.Move, refId = -1, x = x, y = y, playerGiven = true } });
        bool AllLanded() => ships.All(s => s == null || s.Destroyed || (s.ShuttleState == ZShuttleState.Landed && s.ShuttleLift <= 0));

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || hud == null) { Log("no game (a level in Play)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation) { Log("the shuttle rule is off ('Zod exact')"); fails++; Finish(); yield break; }
            Log($"K338 air traffic proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, world time {W.Time:F1} s; traffic on {ZWorld.ShuttleTrafficOn}, look on {OzPlayableShuttle.TrafficLook}");
            int bots = W.Bots.Count; W.Bots.Clear();
            dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            Log($"set-up: CPU removed {bots} (both teams take orders), day 0.10 held");
            yield return Wait(0.5f);
            yield return T1(); yield return T2(); yield return T3(); yield return T4();
            Finish();
        }

        // ------------------------------------------------------------------ T1: the panel's drop of 2 red + 2 blue
        int cx0, cy0;
        IEnumerator T1()
        {
            var own = W.PlayableShuttle; int bx = own != null ? own.CenterX : W.Map.WidthPix / 2, by = own != null ? own.CenterY : W.Map.HeightPix / 2;
            int[] Near(int x, int y) => Spot(x, y, 6 * 16);
            for (int tries = 0; tries < 40 && ships.Count < 4; tries++)
            {   // a middle with room for two pairs about 80 m apart
                ships.Clear();
                double a = tries * 0.9; int cx = bx + (int)(System.Math.Cos(a) * (200 + 40 * (tries / 7))), cy = by + (int)(System.Math.Sin(a) * (200 + 40 * (tries / 7)));
                var spots = new[] { Near(cx - 320, cy - 72), Near(cx - 320, cy + 72), Near(cx + 320, cy - 72), Near(cx + 320, cy + 72) };
                if (spots.Any(q => q == null)) continue;
                cx0 = cx; cy0 = cy;
                for (int i = 0; i < 4; i++)
                {
                    var s = OzCheatShuttle.Spawn(g, hud, i < 2 ? Team.Red : Team.Blue, new Vector2Int(spots[i][0], spots[i][1]));
                    if (s == null) break;
                    ships.Add(s); views.Add(OzPlayableShuttle.Of(s));
                }
            }
            Log($"dropped {ships.Count}: " + string.Join("; ", ships.Select(s => $"#{s.Id} {s.Owner} at {s.CenterX / 8},{-s.CenterY / 8} m")));
            Check("T1 the testing panel drops 4 shuttles (2 red, 2 blue), each drawn", ships.Count == 4 && views.All(v => v) && ships.Count(s => s.Owner == Team.Red) == 2);
            yield return Wait(1.5f);
            Shot("k338_t1_four_parked_h70");
        }

        // ------------------------------------------------------------------ T2: they swap sides (head on and crossing)
        IEnumerator T2()
        {
            if (ships.Count < 4) yield break;
            var starts = ships.Select(s => (s.CenterX, s.CenterY)).ToList();
            ResetWatch(); watch = true;
            Move(ships[0], starts[3].CenterX, starts[3].CenterY); Move(ships[1], starts[2].CenterX, starts[2].CenterY);
            Move(ships[2], starts[1].CenterX, starts[1].CenterY); Move(ships[3], starts[0].CenterX, starts[0].CenterY);
            yield return WaitFor(() => ships.All(s => s.ShuttleState == ZShuttleState.Cruise), 12f); yield return Wait(1.0f);
            Shot("k338_t2_all_up_h70");
            // the strip while they meet (every 0.5 s, 16 pictures)
            yield return WaitFor(() => { var d = PairsMin(); return d < 30f; }, 30f);
            for (int i = 0; i < 16; i++) { Shot($"k338_t2_meet_{i:00}"); yield return Wait(0.5f); }
            yield return WaitFor(AllLanded, 90f); yield return Wait(1.0f);
            watch = false;
            Shot("k338_t2_swapped_landed_h70");
            Log($"T2: closest two ships in the air {minAirM:F2} m (rules' middles); {PictureLine()}; giving way in height in {passFrames} ship-frames (the most {OzPlayableShuttle.PassPeakM:F2} m); turn-backs within 3 s {turnBacks}; hardest avoid acceleration {peakAcc:F2} m/s^2");
            string hs = string.Join("; ", views.Where(v => v).Select(v => $"#{v.Obj.Id} cruise step {v.CruiseAddM:+0.00;-0.00} m, bob phase {v.BobPhaseSec:F2} s, drawn {(hMin.TryGetValue(v.Obj.Id, out var a) ? a : 0):F1}..{(hMax.TryGetValue(v.Obj.Id, out var b) ? b : 0):F1} m"));
            Log("T2 heights: " + hs);
            Check("T2 all 4 landed on the other side", AllLanded() && ships.All(s => !s.Destroyed));
            Check("T2 no two ships in the air came closer than 9 m (the hulls never touch)", minAirM >= (float)ZWorld.ShuttleAirSepM - 0.01f);
            Check($"T2 two hulls at the same layer never overlapped in the picture ({sameLayerOverlaps} of {pairFrames} frames with two ships within 22 m, the deepest {sameDeepestPx:F0} px)", sameLayerOverlaps == 0);
            Check("T2 no back-and-forth: the motion off a line never turned back within 3 s", turnBacks == 0);
            Check("T2 never a hard push: the avoid offset at most 2 m/s^2", peakAcc <= (float)ZWorld.ShuttleAvoidAccMax + 0.05f);
            var steps = views.Where(v => v).Select(v => v.CruiseAddM).ToList();
            bool distinct = true; for (int i = 0; i < steps.Count; i++) for (int k = i + 1; k < steps.Count; k++) if (Mathf.Abs(steps[i] - steps[k]) < 0.05f && Mathf.Abs(views[i].BobPhaseSec - views[k].BobPhaseSec) < 0.3f) distinct = false;
            var vw = views.Where(v => v).ToList();
            bool sized = vw.Where(v => !v.TrafficReference).All(v => Mathf.Abs(v.CruiseAddM) >= OzPlayableShuttle.CruiseVarMinM - 0.01f && Mathf.Abs(v.CruiseAddM) <= OzPlayableShuttle.CruiseVarMaxM + 0.01f);
            Check("T2 each ship cruises at its own height and bobs at its own moment (the reference ship, level 16's own or the first one dropped, keeps the base look; every other one 0.8..1.5 m up or down; no two in step)", distinct && sized);
        }

        float PairsMin()
        {
            float best = 999f;
            for (int i = 0; i < ships.Count; i++) for (int k = i + 1; k < ships.Count; k++)
                {
                    var a = ships[i]; var b = ships[k]; if (a.Destroyed || b.Destroyed) continue;
                    best = Mathf.Min(best, (float)System.Math.Sqrt((MX(a) - MX(b)) * (MX(a) - MX(b)) + (MY(a) - MY(b)) * (MY(a) - MY(b))) / 8f);
                }
            return best;
        }

        // ------------------------------------------------------------------ T3: all to one point
        IEnumerator T3()
        {
            if (ships.Count < 4) yield break;
            ResetWatch(); watch = true;
            var (px, py) = W.ShuttleClamp(cx0, cy0);
            foreach (var s in ships) Move(s, px, py);
            yield return Wait(4.0f); Shot("k338_t3_swarm_on_the_way_h70");
            yield return WaitFor(AllLanded, 120f); yield return Wait(1.0f);
            watch = false;
            Shot("k338_t3_swarm_landed_round_the_point_h70");
            float closest = 999f;
            for (int i = 0; i < ships.Count; i++) for (int k = i + 1; k < ships.Count; k++)
                    closest = Mathf.Min(closest, (float)System.Math.Sqrt((MX(ships[i]) - MX(ships[k])) * (MX(ships[i]) - MX(ships[k])) + (MY(ships[i]) - MY(ships[k])) * (MY(ships[i]) - MY(ships[k]))) / 8f);
            Log($"T3: all sent to {px / 8},{-py / 8} m; landed at " + string.Join("; ", ships.Select(s => $"#{s.Id} {s.CenterX / 8},{-s.CenterY / 8} m{(s.ShuttleSpotMoved ? " (a free spot round it)" : " (the point)")}")) + $"; closest two standing {closest:F1} m; closest in the air {minAirM:F2} m; {PictureLine()}");
            Check("T3 a swarm sent to one point lands round it, every ship on its own spot (at least 15 m apart)", AllLanded() && closest >= (float)ZWorld.ShuttleClaimM - 0.5f);
            Check("T3 on the way in: never closer than 9 m in the air", minAirM >= (float)ZWorld.ShuttleAirSepM - 0.01f);
            Check($"T3 on the way in: no same-layer overlap in the picture ({sameLayerOverlaps} of {pairFrames} frames, the deepest {sameDeepestPx:F0} px)", sameLayerOverlaps == 0);
        }

        // ------------------------------------------------------------------ T4: night: each ship's own lights and mark
        IEnumerator T4()
        {
            if (ships.Count < 4) yield break;
            if (dn) { dn.timeOfDay = 0.62f; dn.Apply(0.62f); }
            yield return Wait(1.5f);
            landLamp.Clear(); noseOn.Clear(); markOn.Clear(); ResetWatch(); watch = true;
            var spots = new List<int[]>();
            foreach (var s in ships) { var q = Spot(s.CenterX + (s.Owner == Team.Red ? 640 : -640), s.CenterY, 8 * 16); spots.Add(q); }
            for (int i = 0; i < ships.Count; i++) if (spots[i] != null) Move(ships[i], spots[i][0], spots[i][1]);
            yield return WaitFor(() => ships.All(s => s.ShuttleState == ZShuttleState.Cruise), 12f); yield return Wait(2.0f);
            Shot("k338_t4_night_all_up_h70");
            yield return WaitFor(() => ships.Any(s => s.ShuttleState == ZShuttleState.Descend), 60f); yield return Wait(0.6f);
            Shot("k338_t4_night_landing_lamps_h70");
            yield return WaitFor(AllLanded, 90f); yield return Wait(1.0f);
            watch = false;
            Shot("k338_t4_night_landed_h70");
            Log($"T4 night: landing lamps on in its own descent {landLamp.Count} of {ships.Count}, nose searchlight in flight {noseOn.Count}, its own mark under it in flight {markOn.Count}; closest in the air {minAirM:F2} m; {PictureLine()}");
            Check("T4 at night every ship lights its own landing lamps for its own descent, its nose searchlight in flight, and shows its own landing mark", landLamp.Count == ships.Count && noseOn.Count == ships.Count && markOn.Count == ships.Count);
            Check("T4 at night too: never closer than 9 m in the air, all landed", minAirM >= (float)ZWorld.ShuttleAirSepM - 0.01f && AllLanded());
            Check($"T4 at night too: no same-layer overlap in the picture ({sameLayerOverlaps} of {pairFrames} frames, the deepest {sameDeepestPx:F0} px)", sameLayerOverlaps == 0);
            if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); }
        }

        void Finish()
        {
            watch = false;
            Report = $"{(fails == 0 ? "PASS" : "FAIL")} {passes} passed, {fails} failed, {frames} frames";
            sb.Insert(0, Report + "\n");
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "air_traffic_proof_log.txt"), sb.ToString()); }
            catch (System.Exception ex) { Debug.LogWarning("[OzShuttleTrafficProof] " + ex.Message); }
            Debug.Log("[OzShuttleTrafficProof] " + Report + " -> " + dir);
            Destroy(gameObject);
        }
    }
}
