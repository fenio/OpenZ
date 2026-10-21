using System.Collections;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K407 Play proof: every shuttle landing has exactly ONE landing voice, the robot's ("Touchdown!" / "Nice landing!" / "Coming in hot!")
    /// OR Rachel's ("Landing ... percent successful."), never both (View/OzPlayableShuttle.LandingVoice.cs), on level 16 in Play (menu
    /// OpenZ/V1.1/Shuttle: K407 One Landing Voice Proof, Editor/OzOneLandingVoiceMenu.cs). Set-up: the CPU teams removed, morning (day
    /// clock 0.10, held), ANNOUNCER and ROBOT CHATTER on FULL (put back at the end), the normal RTS camera at 70 m on the ship for every
    /// descent (so Rachel may speak). Six landings 20 to 44 m apart (orders through the rules, no desktop input), the fourth a crash landing
    /// (the rules' proof switch), 8 s rest after each. For each landing the sound log's voice rows from the start of its descent to 7 s after
    /// the touchdown: the PLAY rows of vo_shuttle_landed / vo_shuttle_hot (the robot) and ann_shuttle_landing (Rachel), and the director's
    /// "landing voice:" row. Checks:
    ///  V1 every landing exactly one landing voice played (1 robot row XOR 1 Rachel row), 0 doubles, 0 silent;
    ///  V2 both voices used in the six; no run longer than 2 of the same;
    ///  V3 the crash landing too: its one voice is "Coming in hot!" before the contact OR Rachel's 85 / 60 percent line, never both.
    /// Output: <see cref="Folder"/> (the work tree's records, milestones/one_landing_voice_k407, found upward at run time): the log and the
    /// sound log rows (CSV). Test only.
    /// </summary>
    public sealed class OzOneLandingVoiceProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "one_landing_voice_k407");
            return System.IO.Path.Combine(Application.persistentDataPath, "one_landing_voice_k407");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzOneLandingVoiceProof").AddComponent<OzOneLandingVoiceProof>();
        }

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; Team me = Team.Red;
        ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); readonly StringBuilder csv = new StringBuilder("landing,t_s,src,key,clip,outcome\n");
        int fails, passes; string dir; int ann0, chat0; float T0;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            ann0 = AudioDirector.Chatter.Level; chat0 = AudioDirector.RobotChatter.Level; T0 = Time.unscaledTime;
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzOneLandingVoiceProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        void Select(params ZObject[] os) { input.Selection.Clear(); foreach (var o in os) if (o != null) input.Selection.Add(o); }
        bool Move(int x, int y) => W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        static bool Played(OzSoundLog.Entry e) => e.src == "VO" && e.outcome != null && e.outcome.StartsWith("PLAY");
        static bool RobotKey(string k) => k == "vo_shuttle_landed" || k == "vo_shuttle_hot";

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
            if (W == null || input == null) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation || W.PlayableShuttle == null) { Log("no playable shuttle (level 16, the shuttle rule on, the option SHUTTLE on)"); fails++; Finish(); yield break; }
            me = g.player; s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s);
            if (!ship) { Log("the shuttle has no view"); fails++; Finish(); yield break; }
            int bots = W.Bots.Count; W.Bots.Clear();
            var dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            AudioDirector.Chatter.Level = 0; AudioDirector.RobotChatter.Level = 0; OzPlayableShuttle.ProofRachelEvery = false;
            Log($"K407 one landing voice proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, world time {W.Time:F1} s; CPU removed {bots}; ANNOUNCER / ROBOT CHATTER FULL (were {AudioDirector.Chatter.Names[ann0]} / {AudioDirector.RobotChatter.Names[chat0]}); run cap {OzShuttleLandingVoice.MaxRun}, Rachel gap {OzShuttleLandingVoice.RachelGapSec:F0} s; ann_shuttle_landing cooldown {(AudioDirector.I && AudioDirector.I.Def("ann_shuttle_landing") != null ? AudioDirector.I.Def("ann_shuttle_landing").cooldown.ToString("F0") + " s" : "?")}");
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne && s.ShuttleLevelled, 30f); yield return Wait(1.0f);
            int r0 = OzPlayableShuttle.LandingRachelRefused;
            var voices = new System.Collections.Generic.List<string>();
            int ok1 = 0, crashOk = -1;
            for (int k = 1; k <= 6; k++)
            {
                bool crash = k == 4;
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && !ship.CrashOn, 30f);
                var q = Free(s.CenterX, s.CenterY, 320, 700); if (q == null) { Check($"V0 landing {k}: a landing place", false); continue; }
                OzPlayableShuttle.ProofRoughNext = crash;
                Select(s); int l0 = ship.Landings; Move(q[0], q[1]);
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Descend, 60f);
                float t0 = Time.unscaledTime;
                Cam(ship.transform.position, 70f);
                yield return WaitFor(() => ship.Landings > l0, 20f);
                Cam(ship.transform.position, 70f);
                float touch = Time.unscaledTime;
                yield return Wait(7f);
                var rows = OzSoundLog.Since(t0 - 0.05f);
                var robot = rows.Where(e => Played(e) && RobotKey(e.key)).ToList();
                var rach = rows.Where(e => Played(e) && e.key == "ann_shuttle_landing").ToList();
                var pick = rows.Where(e => e.src == "dir" && e.outcome != null && e.outcome.StartsWith("landing voice")).ToList();
                foreach (var e in rows.Where(e => RobotKey(e.key) || e.key == "ann_shuttle_landing"))
                    csv.Append(k).Append(',').Append((e.t - T0).ToString("F2")).Append(',').Append(e.src).Append(',').Append(e.key).Append(',').Append(e.clip ?? "").Append(',').Append((e.outcome ?? "").Replace(',', ';')).Append('\n');
                string who = robot.Count == 1 && rach.Count == 0 ? "robot" : rach.Count == 1 && robot.Count == 0 ? "Rachel" : robot.Count + rach.Count == 0 ? "NONE" : "DOUBLE";
                voices.Add(who);
                Log($"landing {k}{(crash ? " (crash)" : "")}: landed {ship.Landings > l0}, rough {s.ShuttleRoughLanding}; robot rows {robot.Count} [{string.Join(" | ", robot.Select(e => $"{e.key} {e.clip} at {e.t - touch:+0.00;-0.00} s"))}], Rachel rows {rach.Count} [{string.Join(" | ", rach.Select(e => $"{e.clip} at {e.t - touch:+0.00;-0.00} s"))}]; pick: {string.Join(" | ", pick.Select(e => e.outcome))} -> {who}");
                if (who == "robot" || who == "Rachel") ok1++;
                if (crash) crashOk = (who == "robot" && robot[0].key == "vo_shuttle_hot" && robot[0].t <= touch + 0.05f) || (who == "Rachel" && (rach[0].clip ?? "").Length > 0) ? 1 : 0;
                OzPlayableShuttle.ProofRoughNext = false;
                yield return Wait(1.0f);
            }
            int run = 0, maxRun = 0; string last = "";
            foreach (var v in voices) { run = v == last ? run + 1 : 1; last = v; if (run > maxRun) maxRun = run; }
            foreach (var l in OzPlayableShuttle.LandingVoiceLog.Skip(System.Math.Max(0, OzPlayableShuttle.LandingVoiceLog.Count - 8))) Log("   pick: " + l);
            Log($"voices: {string.Join(", ", voices)}; longest run {maxRun}; Rachel lines refused after the pick +{OzPlayableShuttle.LandingRachelRefused - r0}");
            Check("V1 every landing exactly one landing voice (one robot row XOR one Rachel row; 0 doubles, 0 silent), 6 of 6", voices.Count == 6 && ok1 == 6);
            Check("V2 both voices used, no run longer than 2", voices.Contains("robot") && voices.Contains("Rachel") && maxRun <= 2);
            Check("V3 the crash landing: \"Coming in hot!\" before the contact OR Rachel's line, never both", crashOk == 1);
            Finish();
        }

        void Finish()
        {
            OzPlayableShuttle.ProofRoughNext = false; OzPlayableShuttle.ProofRachelEvery = false;
            AudioDirector.Chatter.Level = ann0; AudioDirector.RobotChatter.Level = chat0;
            Log($"K407 one landing voice proof: {passes} PASS, {fails} FAIL (ANNOUNCER / ROBOT CHATTER put back: {AudioDirector.Chatter.Names[ann0]} / {AudioDirector.RobotChatter.Names[chat0]})");
            Report = fails == 0 ? $"PASS {passes}" : $"FAIL {fails} of {passes + fails}";
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "one_landing_voice_proof_log.txt"), sb.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "one_landing_voice_sound_rows.csv"), csv.ToString());
            Destroy(gameObject);
        }
    }
}
