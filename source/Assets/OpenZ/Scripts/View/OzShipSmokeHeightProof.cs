using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K413 Play proof (the flying shuttle's smoke at its own height, not on the ground under it: View/FxV3/OpenZDamageStates.ShipLate.cs,
    /// View/OzPlayableShuttle.PosedPuffs.cs), on level 16 in Play (menu OpenZ/V1.1/Shuttle: K413 Smoke Height Proof,
    /// Editor/OzShipSmokeHeightMenu.cs). Counted PASS / FAIL lines, a CSV row for every frame that laid a puff and full-screen pictures from
    /// the normal RTS camera at 70 m. Set-up: the CPU teams' orders removed, the other teams hold their fire, morning (day clock 0.10,
    /// held), 60 fps, the full smoke set (as on HIGH). Health is set straight in the rules (test only) and given back at the end.
    /// The probe (LateUpdate at order 206, after the smoke step at 205): each frame that laid damage puffs (K393's beat, trail and flame
    /// tongues), each frame's lowest emitter point against that same frame's drawn hull's lowest point (the lowest corner of its box)
    /// and the ground under it; the worst of those frames in the last 1.5 s counts (a trail puff lies between two of them).
    /// <list type="bullet">
    /// <item>H1 battered (45 %), 10 s at cruise: no puff born lower than 1 m under the hull's lowest point. Picture K413_air_smoke_70m.</item>
    /// <item>H2 burning (20 %), 10 s at cruise: the same. Picture K413_air_fire_70m.</item>
    /// <item>H3 the old path (the test switch off), 4 s battered at cruise: the puffs ARE born under the ship near the ground (the bug
    /// seen again, so the probe can see it). Picture K413_before_old_path_70m.</item>
    /// <item>H4 standing battered: the puffs from the hull's top, 4.0 .. 5.6 m over the ground (K393's look unchanged). Picture
    /// K413_ground_smoke_70m.</item>
    /// <item>H5 a crash landing (the rules' proof switch): K408's engine cut lays its two pod puffs on the drawn ship (not under 1 m below
    /// the hull's belly, 3 m or more over the ground) and K256e's crash wisps come from the drawn low pod. Picture K413_crash_cut_70m.</item>
    /// </list>
    /// <see cref="Folder"/>
    /// </summary>
    [DefaultExecutionOrder(206)]
    public sealed class OzShipSmokeHeightProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_smoke_height_k413");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_smoke_height_k413");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShipSmokeHeightProof").AddComponent<OzShipSmokeHeightProof>();
        }

        /// <summary>The limits: no puff born lower than this under the hull's lowest point (m), and the stricter twin line (m).</summary>
        public const float LowLimitM = 1f, TwinLimitM = 2f;
        public const float CruiseSec = 10f, BeforeSec = 4f, WindowSec = 1.5f;

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; DayNight dn; Team me = Team.Red; ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(), csv = new StringBuilder("clock_s,stage,path,state,airborne,puffs,lowest_emitter_over_ground_m,hull_lowest_over_ground_m,belly_over_ground_m,hull_top_over_ground_m,emitter_minus_hull_lowest_m\n");
        int fails, passes; string dir; bool follow;
        readonly Dictionary<ZObject, int> heldFire = new Dictionary<ZObject, int>();

        // ------------------------------------------------------------------ the probe
        sealed class Tally { public string name; public int puffs, low, low2, frames; public float airSec, lowest = float.MaxValue, lowestOverGround = float.MaxValue, highestOverGround = float.MinValue; }
        // each frame's lowest emitter against THAT frame's drawn hull and ground (a climb of up to 9 m/s inside the window must not count
        // an emitter of 1.5 s ago against the hull of now: that would read a correct take-off as puffs under the ship)
        Tally cur; int lastPuffs = -1; readonly Queue<(float t, float margin, float overGround)> win = new Queue<(float t, float margin, float overGround)>();

        void LateUpdate()
        {
            if (follow && ship) Cam(ship.transform.position, 70f);
            if (s == null || !ship) return;
            int now = OpenZDamageStates.ShipBeatPuffs + OpenZDamageStates.ShipTrailPuffs + OpenZDamageStates.ShipFlyingFlames;
            int d = lastPuffs < 0 ? 0 : now - lastPuffs; if (d < 0) d = now; lastPuffs = now;
            int n = Mathf.Clamp(OpenZDamageStates.ShipPointsUsed, 0, 3);
            int stage = OpenZDamageStates.StageOf(s.Id);
            float t = Time.time;
            var tf = ship.transform; var root = tf.position; float gy = g.GroundY(root.x, root.z);
            float lowCorner = float.MaxValue;
            var mn = OzShuttleAim.HullMin; var mx = OzShuttleAim.HullMax;
            for (int c = 0; c < 8; c++)
                lowCorner = Mathf.Min(lowCorner, tf.TransformPoint(new Vector3((c & 1) == 0 ? mn.x : mx.x, (c & 2) == 0 ? mn.y : mx.y, (c & 4) == 0 ? mn.z : mx.z)).y);
            float belly = tf.TransformPoint(new Vector3(0f, mn.y, 0f)).y, top = tf.TransformPoint(new Vector3(0f, mx.y, 0f)).y;
            if (stage > 0 && n > 0)
            {
                float low = float.MaxValue;
                for (int i = 0; i < n; i++) low = Mathf.Min(low, OpenZDamageStates.ShipLastPoints[i].y);
                if (stage >= 2) low = Mathf.Min(low, OpenZDamageStates.ShipLastPoints[0].y - 0.4f);           // the flame tongue starts 0.4 m under the pod's point
                win.Enqueue((t, low - lowCorner, low - gy));
            }
            while (win.Count > 0 && t - win.Peek().t > WindowSec) win.Dequeue();
            if (cur == null) return;
            cur.frames++;
            if (s.ShuttleAirborne && s.ShuttleState == ZShuttleState.Cruise) cur.airSec += Time.deltaTime;
            if (d <= 0 || win.Count == 0) return;
            float margin = float.MaxValue, og = float.MaxValue;
            foreach (var e in win) { margin = Mathf.Min(margin, e.margin); og = Mathf.Min(og, e.overGround); }
            cur.puffs += d;
            if (margin < -LowLimitM) cur.low += d;
            if (margin < -TwinLimitM) cur.low2 += d;
            cur.lowest = Mathf.Min(cur.lowest, margin);
            cur.lowestOverGround = Mathf.Min(cur.lowestOverGround, og); cur.highestOverGround = Mathf.Max(cur.highestOverGround, og);
            csv.Append(t.ToString("F3")).Append(',').Append(cur.name).Append(',').Append(OpenZDamageStates.ShipDrawnPose ? "drawn" : "old").Append(',').Append(s.ShuttleState).Append(',')
               .Append(s.ShuttleAirborne ? 1 : 0).Append(',').Append(d).Append(',').Append(og.ToString("F2")).Append(',').Append((lowCorner - gy).ToString("F2")).Append(',')
               .Append((belly - gy).ToString("F2")).Append(',').Append((top - gy).ToString("F2")).Append(',').Append(margin.ToString("F2")).Append('\n');
        }

        Tally Begin(string name) { win.Clear(); lastPuffs = -1; cur = new Tally { name = name }; return cur; }
        Tally End() { var t = cur; cur = null; return t; }
        static string Of(Tally t) => $"{t.puffs} puffs in {t.frames} frames ({t.airSec:F1} s at cruise); under the hull's lowest point by more than {LowLimitM:F0} m: {t.low}, by more than {TwinLimitM:F0} m: {t.low2}; the lowest emitter {(t.lowest < float.MaxValue ? t.lowest.ToString("+0.00;-0.00") : "-")} m from the hull's lowest point, {(t.lowestOverGround < float.MaxValue ? t.lowestOverGround.ToString("F2") : "-")} .. {(t.highestOverGround > float.MinValue ? t.highestOverGround.ToString("F2") : "-")} m over the ground";

        // ------------------------------------------------------------------ helpers (the K393 / K408 proofs' own)
        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }
        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShipSmokeHeightProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log($"frame {name}.png"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        bool Move(int x, int y) => W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        void Hp(ZObject o, float k) { if (o != null && !o.Destroyed) { o.SetHealth(Mathf.Max(1, Mathf.RoundToInt(o.MaxHealth * k))); W.UpdateObjectHealth(o); } }
        void SetClock(float t) { if (dn) { dn.timeOfDay = t; dn.Apply(t); } }
        bool Landed() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne && !ship.CrashOn;

        /// <summary>A free tile middle (the ship can land there now) between minR and maxR px from (cx, cy), the nearest first.</summary>
        int[] Free(int cx, int cy, int minR, int maxR)
        {
            long best = long.MaxValue; int[] r = null; int e = ZWorld.ShuttleEdgeTiles;
            for (int ty = e; ty < W.Map.height - e; ty++)
                for (int tx = e; tx < W.Map.width - e; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8; long dd = (long)(x - cx) * (x - cx) + (long)(y - cy) * (y - cy);
                    if (dd >= best || dd < (long)minR * minR || dd > (long)maxR * maxR) continue;
                    if (W.ShuttleNeverLands(x, y, s, out _) || !W.ShuttlePadFree(x, y, s, out _)) continue;
                    best = dd; r = new[] { x, y };
                }
            return r;
        }

        void HoldFire()
        {
            foreach (var o in W.Objects)
            {
                if (o == null || o.Destroyed || o.Owner == me || o.Owner == Team.Neutral || o.AttackRadius <= 0 || heldFire.ContainsKey(o)) continue;
                heldFire[o] = o.AttackRadius; o.AttackRadius = 0;
            }
        }
        void OnDestroy()
        {
            follow = false; OpenZDamageStates.ProofLite = -1; OpenZDamageStates.ShipDrawnPose = true; OzPlayableShuttle.PosedPuffsOn = true; OzPlayableShuttle.ProofRoughNext = false;
            foreach (var kv in heldFire) if (kv.Key != null && kv.Key.AttackRadius == 0) kv.Key.AttackRadius = kv.Value;
            heldFire.Clear();
        }

        /// <summary>Health k with a clean change of the smoke path: through full health (stage 0 for a few frames: no trail line from the old
        /// spots to the new ones), then the switch, then k.</summary>
        IEnumerator Stage(float k, bool drawn)
        {
            Hp(s, 1f); yield return Wait(0.3f);
            OpenZDamageStates.ShipDrawnPose = drawn;
            yield return null; yield return null;
            Hp(s, k); yield return null;
        }

        /// <summary>Keeps the ship at cruise between two landable spots (a new goal 30 m before the old one, a new flight at once after a
        /// landing) until `sec` s at cruise are measured; the picture `shot` half-way.</summary>
        IEnumerator Cruise(int[] a, int[] b, float sec, float max, string shot)
        {
            bool toB = true; float waited = 0f, lastOrder = 0f; bool shotDone = shot == null;
            Move(b[0], b[1]);
            while (waited < max && cur.airSec < sec)
            {
                yield return null; waited += Time.deltaTime;
                var goal = toB ? b : a;
                float dx = (s.CenterX - goal[0]) / 8f, dy = (s.CenterY - goal[1]) / 8f;
                if (waited - lastOrder >= 1.5f && ((s.ShuttleAirborne && dx * dx + dy * dy < 30f * 30f) || Landed()))
                {
                    toB = !toB; var ng = toB ? b : a; Move(ng[0], ng[1]); lastOrder = waited;
                }
                if (!shotDone && cur.airSec >= sec * 0.5f) { shotDone = true; Shot(shot); }
            }
        }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || input == null) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation || W.PlayableShuttle == null) { Log("no playable shuttle (level 16, the shuttle rule on)"); fails++; Finish(); yield break; }
            me = g.player; s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s);
            if (!ship || !OpenZDamageStates.I) { Log("the shuttle has no view, or no damage states"); fails++; Finish(); yield break; }
            int bots = W.Bots.Count; W.Bots.Clear(); HoldFire();
            OpenZDamageStates.ProofLite = 0;                                                              // the full set whatever the player's GRAPHICS QUALITY
            dn = FindFirstObjectByType<DayNight>(); if (dn) { SetClock(0.10f); dn.running = false; }
            Log($"K413 smoke height proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, preset {OzQuality.Name}; CPU removed {bots}, the other teams hold their fire ({heldFire.Count}), day 0.10 held; shuttle #{s.Id} ({s.MaxHealth} HP); the drawn-pose path {OpenZDamageStates.ShipDrawnPose}, the posed puffs {OzPlayableShuttle.PosedPuffsOn}");
            Check("H0 the fix is on (the smoke step after the drawn pose, the crash puffs after the pose)", OpenZDamageStates.ShipDrawnPose && OzPlayableShuttle.PosedPuffsOn);

            Hp(s, 1f);
            yield return WaitFor(() => Landed(), 30f); yield return Wait(0.8f);
            var a = Free(s.CenterX, s.CenterY, 0, 40); var b = Free(s.CenterX, s.CenterY, 560, 900);
            if (a == null) a = new[] { s.CenterX, s.CenterY };
            if (b == null) { Check("a landing spot 70 .. 110 m away for the cruise", false); Finish(); yield break; }
            Log($"cruise between px {a[0]},{a[1]} and {b[0]},{b[1]} ({Mathf.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1])) / 8f:F0} m)");
            follow = true;

            // H1 battered at cruise (the fix)
            Begin("H1_battered"); yield return Stage(0.45f, true);
            yield return Cruise(a, b, CruiseSec, 60f, "K413_air_smoke_70m");
            var h1 = End(); Log("H1 battered: " + Of(h1));
            Check($"H1 battered (45 %), {h1.airSec:F1} s at cruise: no puff born more than {LowLimitM:F0} m under the hull's lowest point ({h1.low} of {h1.puffs}; the stricter twin line {TwinLimitM:F0} m: {h1.low2})",
                  h1.airSec >= CruiseSec - 0.5f && h1.puffs >= 50 && h1.low == 0 && h1.low2 == 0);

            // H2 burning at cruise (the fix)
            Begin("H2_burning"); yield return Stage(0.20f, true);
            yield return Cruise(a, b, CruiseSec, 60f, "K413_air_fire_70m");
            var h2 = End(); Log("H2 burning: " + Of(h2));
            Check($"H2 burning (20 %), {h2.airSec:F1} s at cruise: no puff born more than {LowLimitM:F0} m under the hull's lowest point ({h2.low} of {h2.puffs}; the twin line {TwinLimitM:F0} m: {h2.low2})",
                  h2.airSec >= CruiseSec - 0.5f && h2.puffs >= 50 && h2.low == 0 && h2.low2 == 0);

            // H3 the old path, for the record (the bug must show, so the probe can see it)
            Begin("H3_old_path"); yield return Stage(0.45f, false);
            yield return Cruise(a, b, BeforeSec, 30f, "K413_before_old_path_70m");
            var h3 = End(); Log("H3 the old path: " + Of(h3));
            yield return Stage(1f, true);
            Check($"H3 the old path (test switch off): its puffs born under the flying ship near the ground ({h3.low} of {h3.puffs} more than {LowLimitM:F0} m under the hull's lowest point): the bug the owner saw, seen by the probe",
                  h3.puffs > 0 && h3.low > h3.puffs / 2);

            // H4 standing battered (the fix: K393's look on the ground unchanged)
            yield return WaitFor(() => Landed(), 40f);
            if (!Landed()) { Move(a[0], a[1]); yield return WaitFor(() => Landed(), 40f); }
            yield return Wait(1f);
            follow = false; Cam(ship.transform.position, 70f);
            Begin("H4_standing"); yield return Stage(0.45f, true);
            yield return Wait(2.5f);
            Shot("K413_ground_smoke_70m"); yield return null; yield return null;
            var h4 = End(); Log("H4 standing: " + Of(h4));
            Check($"H4 standing at 45 %: the puffs from the hull's top ({h4.lowestOverGround:F2} .. {h4.highestOverGround:F2} m over the ground, wanted 4.0 .. 5.6), none under the hull",
                  h4.puffs >= 8 && h4.low == 0 && h4.lowestOverGround >= 4.0f && h4.highestOverGround <= 5.6f);
            Hp(s, 1f); yield return Wait(0.5f);

            // H5 a crash landing: K408's engine cut and K256e's wisps on the drawn ship
            yield return WaitFor(() => Landed() && s.ShuttleLevelled, 30f); yield return Wait(0.8f);
            var q = Free(s.CenterX, s.CenterY, 320, 700);
            if (q == null) Check("H5 a landing place for the crash landing", false);
            else
            {
                OzPlayableShuttle.PosedReset(); int cuts0 = OzPlayableShuttle.CrashDropCuts, puffs0 = OzPlayableShuttle.CrashDropPuffs;
                OzPlayableShuttle.ProofRoughNext = true;
                Move(q[0], q[1]); follow = true;
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Descend, 60f);
                float feetAtCut = -1f;
                yield return WaitFor(() => OzPlayableShuttle.PosedCuts > 0 || Landed(), 12f);
                if (OzPlayableShuttle.PosedCuts > 0) { feetAtCut = ship.FeetHeight; Shot("K413_crash_cut_70m"); }
                yield return WaitFor(() => ship.CrashOn, 5f);
                yield return WaitFor(() => !ship.CrashOn, 12f);
                follow = false; OzPlayableShuttle.ProofRoughNext = false;
                int cutRows = OzPlayableShuttle.PosedRows.Count(r => r.Contains(",cut,")), wispRows = OzPlayableShuttle.PosedRows.Count(r => r.Contains(",wisp,"));
                float cutLowGround = float.MaxValue, wispLow = float.MaxValue, wispHigh = float.MinValue;
                foreach (var r in OzPlayableShuttle.PosedRows)
                {
                    var f = r.Split(','); if (f.Length < 6) continue;
                    float og = float.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture);
                    if (f[1] == "cut") cutLowGround = Mathf.Min(cutLowGround, og);
                    else if (f[1] == "wisp") { wispLow = Mathf.Min(wispLow, og); wispHigh = Mathf.Max(wispHigh, og); }
                }
                Log($"H5 crash landing: the cut {OzPlayableShuttle.CrashDropCuts - cuts0}x (K408's puffs +{OzPlayableShuttle.CrashDropPuffs - puffs0}), laid after the pose {OzPlayableShuttle.PosedCuts}x, the pads {feetAtCut:F2} m up at the cut; the cut's puffs at least {(cutLowGround < float.MaxValue ? cutLowGround.ToString("F2") : "-")} m over the ground; the lowest posed puff {(OzPlayableShuttle.PosedLowestOverBellyM < float.MaxValue ? OzPlayableShuttle.PosedLowestOverBellyM.ToString("+0.00;-0.00") : "-")} m from the hull's belly; wisps {wispRows} at {(wispLow < float.MaxValue ? wispLow.ToString("F2") : "-")} .. {(wispHigh > float.MinValue ? wispHigh.ToString("F2") : "-")} m over the ground");
                Check($"H5 the engine cut's two puffs on the drawn ship ({cutRows} puffs, at least {(cutLowGround < float.MaxValue ? cutLowGround.ToString("F2") : "-")} m over the ground, wanted >= 3; none more than {LowLimitM:F0} m under the belly) and K408's own count +2",
                      cutRows == 2 && OzPlayableShuttle.CrashDropPuffs - puffs0 == 2 && cutLowGround >= 3f && OzPlayableShuttle.PosedLowestOverBellyM >= -LowLimitM);
                Check($"H5 K256e's crash wisps from the drawn low pod ({wispRows}, {(wispLow < float.MaxValue ? wispLow.ToString("F2") : "-")} .. {(wispHigh > float.MinValue ? wispHigh.ToString("F2") : "-")} m over the ground, wanted 0.5 .. 4)",
                      wispRows >= 1 && wispLow >= 0.5f && wispHigh <= 4f);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "posed_puffs.csv"), "clock_s,what,over_ground_m,belly_over_ground_m,hull_top_over_ground_m,minus_belly_m\n" + string.Join("\n", OzPlayableShuttle.PosedRows) + "\n");
            }
            Finish();
        }

        void Finish()
        {
            follow = false; cur = null; OpenZDamageStates.ProofLite = -1; OpenZDamageStates.ShipDrawnPose = true; OzPlayableShuttle.PosedPuffsOn = true; OzPlayableShuttle.ProofRoughNext = false;
            if (s != null && !s.Destroyed) Hp(s, 1f);
            Log($"K413 smoke height proof: {passes} PASS, {fails} FAIL (expected 7 PASS)");
            Report = $"{passes} PASS, {fails} FAIL";
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "smoke_height_proof_log.txt"), sb.ToString());
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "smoke_height_puffs.csv"), csv.ToString());
            }
            catch (System.Exception e) { Debug.LogWarning("[OzShipSmokeHeightProof] log not written: " + e.Message); }
            Destroy(gameObject, 1f);
        }
    }
}
