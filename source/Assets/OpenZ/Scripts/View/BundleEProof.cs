using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// gun (idle frame + one engaged bearing, commander shots and sound keys per combo; HW1 shows there: tough / pyro / laser
    /// commanders fire their own weapon); (2) NB16 / NB17 + BS1: two red grunt squads board a neutral jeep (door) and a light tank
    /// (roof hatch): the robot on the vehicle boards, the capture cue waits for the climb-in, the door / lid latch is heard;
    /// (3) GP57: a tough squad boards a crewed red APC as passengers, the APC fires, EXIT CREW through the HUD (OpenZHud.ExitSelected,
    /// the button / E key path) pops them out with the latch + exit sounds; (4) NB24: a crane under attack raises its driver's gun
    /// </summary>
    public class BundleEProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static bool SkipCt1;
        public static void Run() { Report = "running"; new GameObject("BundleEProof").AddComponent<BundleEProof>(); }

        ZGame g; ZWorld W; readonly StringBuilder sb = new StringBuilder(); int fails; string dir;
        readonly List<ZObject> spawned = new List<ZObject>();

        void Start()
        {
            g = ZGame.I; W = g.World;
            dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/milestones/bundle_main_E"));
            System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string s) { sb.AppendLine(s); Debug.Log("[BundleEProof] " + s); }
        void Check(string what, bool ok) { if (!ok) fails++; Log((ok ? "PASS " : "FAIL ") + what); }

        ZObject Spawn(string cat, string type, int tx, int ty, Team team, int amount = -1)
        {
            var o = cat == "robot" ? W.CreateRobotGroup(type, tx * 16, ty * 16, team, amount) : W.CreateObject(cat, type, tx * 16, ty * 16, team);
            if (o == null) { Log($"spawn {cat}:{type} failed"); return null; }
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            spawned.Add(o); return o;
        }

        bool Order(ZObject u, WpMode mode, ZObject t)
        {
            if (u == null || t == null) return false;
            var wp = new ZWaypoint { mode = mode, refId = t.Id, x = t.CenterX, y = t.CenterY, attackTo = true };
            return W.SendWaypoints(u.Owner, u, new[] { wp }) && u.Waypoints.Count > 0;
        }

        static bool Gone(ZObject m) => m == null || m.Destroyed || m.KillAt >= 0 || (ZGame.I && ZGame.I.World.Get(m.Id) != m);
        static int Played(float t0, string key) { int n = 0; foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && e.outcome.StartsWith("PLAY")) n++; return n; }
        static float FirstPlay(float t0, string key) { foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && e.outcome.StartsWith("PLAY")) return e.t; return -1f; }
        static string Keys(float t0)
        {
            var d = new SortedDictionary<string, int>();
            foreach (var e in OzSoundLog.Since(t0)) if (e.outcome != null && e.outcome.StartsWith("PLAY")) { d.TryGetValue(e.key, out int n); d[e.key] = n + 1; }
            var s = new StringBuilder(); foreach (var kv in d) s.Append(kv.Key).Append(" x").Append(kv.Value).Append(' ');
            return s.ToString().Trim();
        }

        Vector2Int Field()
        {
            int wt = W.Path.WidthPix >> 4, ht = W.Path.HeightPix >> 4; var best = new Vector2Int(wt / 2, ht / 2); float bd = float.MaxValue;
            for (int y = 10; y < ht - 10; y += 2)
                for (int x = 10; x < wt - 10; x += 2)
                {
                    bool ok = true;
                    for (int j = -5; j <= 5 && ok; j++) for (int i = -7; i <= 7 && ok; i++) ok = W.Path.TilePassable(x + i, y + j, false);
                    if (!ok) continue;
                    foreach (var o in W.Objects) if (!o.Destroyed && Mathf.Abs((o.CenterX >> 4) - x) < 14 && Mathf.Abs((o.CenterY >> 4) - y) < 12) { ok = false; break; }
                    if (!ok) continue;
                    float d = (x - wt / 2) * (x - wt / 2) + (y - ht / 2) * (y - ht / 2);
                    if (d < bd) { bd = d; best = new Vector2Int(x, y); }
                }
            var rc = FindFirstObjectByType<RtsCamera>(); if (rc) rc.FlyTo(ZGame.PxToWorld(best.x * 16 + 16, best.y * 16 + 16));
            return best;
        }

        IEnumerator All()
        {
            float T0 = Time.unscaledTime;
            Log($"bundle_main_E proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, prefs oz_board_nearest={PlayerPrefs.GetInt("oz_board_nearest", 1)} oz_capture_after_board={PlayerPrefs.GetInt("oz_capture_after_board", 1)}");
            // ---- (1) CT1 + HW1
            if (!SkipCt1)
            {
                int s0 = BoardingView.CommanderShots, r0 = BoardingView.CommanderRockets, f0 = BoardingView.CommanderFlames, l0 = BoardingView.CommanderLasers;
                CrewSeatCheck.Crews = new[] { "grunt", "psycho", "tough", "sniper", "pyro", "laser", "electrobot" };
                CrewSeatCheck.Types = new[] { "jeep", "light", "medium", "heavy", "apc", "crane", "missile_launcher", "gun", "gatling", "howitzer", "missile_cannon" };
                CrewSeatCheck.Bearings = 1; CrewSeatCheck.IdleShot = true; CrewSeatCheck.EngageWaitVehicle = 3f;
                CrewSeatCheck.SubDir = "bundle_main_E/ct1"; CrewSeatCheck.ReportName = "milestones/bundle_main_E/ct1_crew_matrix.md";
                CrewSeatCheck.Run();
                while (CrewSeatCheck.Report == "running") yield return null;
                string head = CrewSeatCheck.Report.Split('\n')[0];
                Log($"CT1 CrewSeatCheck: {head} ({CrewSeatCheck.Crews.Length} crews x {CrewSeatCheck.Types.Length} types, table ct1_crew_matrix.md, frames ct1/)");
                Log($"HW1 commander shots {BoardingView.CommanderShots - s0}: rockets {BoardingView.CommanderRockets - r0} (landed {BoardingView.CommanderRocketsLanded}), flames {BoardingView.CommanderFlames - f0}, lasers {BoardingView.CommanderLasers - l0}");
                Check("HW1 tough commander fired his own rocket", BoardingView.CommanderRockets - r0 > 0);
                Check("HW1 pyro commander fired his own flame", BoardingView.CommanderFlames - f0 > 0);
                Check("HW1 laser commander fired his own beam", BoardingView.CommanderLasers - l0 > 0);
                CrewSeatCheck.Crews = null; CrewSeatCheck.Bearings = 8; CrewSeatCheck.IdleShot = false;
                CrewSeatCheck.SubDir = "crew_seats"; CrewSeatCheck.ReportName = "crew_seat_check.md"; CrewSeatCheck.EngageWaitVehicle = 1.6f;
                yield return new WaitForSeconds(0.5f);
            }

            // ---- (2) NB16 / NB17 / BS1
            var f = Field();
            Log($"test field tile {f.x},{f.y}");
            OzCheatSpawner.FreeTile(W, f.x - 4, f.y - 2, 2, false, out int jx, out int jy);
            var jeep = Spawn("vehicle", "jeep", jx, jy, Team.Neutral);
            OzCheatSpawner.FreeTile(W, f.x + 4, f.y - 2, 2, false, out int lx, out int ly);
            var tank = Spawn("vehicle", "light", lx, ly, Team.Neutral);
            var sq1 = Spawn("robot", "grunt", jx, jy + 5, Team.Red, 3);
            var sq2 = Spawn("robot", "grunt", lx, ly + 5, Team.Red, 3);
            yield return new WaitForSeconds(0.8f);
            float tb = Time.unscaledTime; int rel0 = CaptureHold.ReleasedByAnimation, latch0 = BoardingView.Latches;
            Check("enter order jeep", Order(sq1, WpMode.Enter, jeep));
            Check("enter order light tank", Order(sq2, WpMode.Enter, tank));
            var pairs = new[] { (jeep, sq1, "jeep"), (tank, sq2, "light tank") };
            var flip = new float[2] { -1f, -1f }; var rel = new float[2] { -1f, -1f }; var nearest = new int[2] { -1, -1 }; var entered = new int[2] { -1, -1 };
            var members = new List<ZObject>[2];
            for (int i = 0; i < 2; i++) { members[i] = new List<ZObject> { pairs[i].Item2 }; members[i].AddRange(pairs[i].Item2.Minions); }
            float tw = 0f;
            while (tw < 16f && (rel[0] < 0f || rel[1] < 0f))
            {
                for (int i = 0; i < 2; i++)
                {
                    var v = pairs[i].Item1; if (v == null) continue;
                    if (flip[i] < 0f)
                    {
                        if (v.Owner != Team.Neutral)
                        {
                            flip[i] = Time.unscaledTime;
                            foreach (var m in members[i]) if (Gone(m)) { entered[i] = m.Id; break; }
                        }
                        else
                        {   // the member nearest the vehicle this frame (sim pixels)
                            long bd = long.MaxValue;
                            foreach (var m in members[i]) { if (Gone(m)) continue; long dx = m.CenterX - v.CenterX, dy = m.CenterY - v.CenterY; if (dx * dx + dy * dy < bd) { bd = dx * dx + dy * dy; nearest[i] = m.Id; } }
                        }
                    }
                    else if (rel[i] < 0f && !CaptureHold.IsHeld(v.Id)) rel[i] = Time.unscaledTime;
                }
                tw += Time.deltaTime; yield return null;
            }
            yield return new WaitForSeconds(1.2f);
            for (int i = 0; i < 2; i++)
            {
                var v = pairs[i].Item1; string n = pairs[i].Item3;
                float cap = flip[i] > 0f ? FirstPlay(flip[i] - 0.05f, "capture") : -1f;
                bool leader = entered[i] == pairs[i].Item2.Id;
                Log($"{n}: owner flip {(flip[i] > 0 ? (flip[i] - tb).ToString("F2") : "never")} s after the order, boarded by #{entered[i]} ({(leader ? "leader" : "minion")}), nearest the frame before #{nearest[i]}, hold released {(rel[i] > 0 ? (rel[i] - flip[i]).ToString("F2") : "-")} s after the flip, capture cue {(cap > 0 ? (cap - flip[i]).ToString("F2") : "none")} s after the flip");
                Check($"NB16 {n}: the robot nearest the vehicle boarded", entered[i] >= 0 && entered[i] == nearest[i]);
                Check($"NB17 {n}: capture cue only after the climb-in (hold released, cue >= release)", flip[i] > 0f && rel[i] > 0f && cap > 0f && cap >= rel[i] - 0.05f);
            }
            Log($"CaptureHold: {CaptureHold.Status()} (released by the animation +{CaptureHold.ReleasedByAnimation - rel0})");
            string kb = Keys(tb);
            Log($"boarding sounds: {kb}");
            Check("BS1 latch heard on the jeep door / tank lid", BoardingView.Latches - latch0 >= 2 && Played(tb, "latch") >= 2);
            Check("BS1 enter + capture + hatch sounds heard", Played(tb, "enter") >= 1 && Played(tb, "capture") >= 2 && Played(tb, "hatch") >= 1);

            // ---- (3) GP57 APC: board, fire, EXIT via the HUD
            OzCheatSpawner.FreeTile(W, f.x, f.y + 3, 2, false, out int ax, out int ay);
            var apc = Spawn("vehicle", "apc", ax, ay, Team.Red);
            var sq3 = Spawn("robot", "tough", ax - 5, ay, Team.Red, 3);
            yield return new WaitForSeconds(0.6f);
            float ta = Time.unscaledTime;
            Check("enter order APC (tough squad)", Order(sq3, WpMode.Enter, apc));
            tw = 0f; while (tw < 14f && apc != null && apc.Passengers.Count == 0) { tw += Time.deltaTime; yield return null; }
            Log($"APC passengers {apc?.Passengers.Count} {apc?.PassengerType}, drivers {apc?.Drivers.Count}, boarded after {tw:F1} s; sounds {Keys(ta)}");
            Check("GP57 APC took the tough squad as passengers", apc != null && apc.Passengers.Count >= 1 && apc.PassengerType == "tough");
            var dummy = W.CreateRobotGroup("grunt", (ax + 7) * 16, ay * 16, Team.Blue, 1);
            dummy.MaxHealth *= 50; dummy.SetHealth(dummy.MaxHealth); dummy.MoveSpeed = 0; dummy.Damage = 0; g.SpawnUnitView(dummy); spawned.Add(dummy);
            g.Views.TryGetValue(apc.Id, out var apcView);
            float fire0 = apcView ? apcView.LastFireAt : -100f; int fires = 0; float tf = Time.unscaledTime;
            apc.AttackObject = dummy;
            tw = 0f; while (tw < 7f) { if (apcView && apcView.LastFireAt > fire0) { fires++; fire0 = apcView.LastFireAt; } tw += Time.deltaTime; yield return null; }
            apc.AttackObject = null;
            Log($"APC fired {fires} drawn shots in 7 s at the dummy; sounds {Keys(tf)}");
            Check("GP57 APC fires with its passengers' weapon (drawn shots + rocket sound)", fires >= 1 && Played(tf, "rocket") >= 1);
            var input = FindFirstObjectByType<PlayerInput>(); var hud = FindFirstObjectByType<OpenZHud>();
            float te = Time.unscaledTime; int toughs0 = 0;
            foreach (var o in W.Objects) if (!o.Destroyed && o.Kind == ZKind.Robot && o.Type == "tough" && o.Owner == Team.Red) toughs0++;
            if (input && hud) { input.Selection.Clear(); input.Selection.Add(apc); hud.ExitSelected(); }
            yield return new WaitForSeconds(1.5f);
            int toughs1 = 0; foreach (var o in W.Objects) if (!o.Destroyed && o.Kind == ZKind.Robot && o.Type == "tough" && o.Owner == Team.Red) toughs1++;
            Log($"EXIT CREW (HUD): passengers now {apc.Passengers.Count}, APC owner {apc.Owner}, drivers {apc.Drivers.Count}, red toughs {toughs0} -> {toughs1}; sounds {Keys(te)}");
            Check("GP57 EXIT via the HUD: passengers out, driver stays, APC stays red", input && hud && apc.Passengers.Count == 0 && apc.Owner == Team.Red && apc.Drivers.Count > 0 && toughs1 > toughs0);
            Check("BS1 exit: latch + exit sounds", Played(te, "latch") >= 1 && Played(te, "exit") >= 1);
            if (input) input.Selection.Clear();

            // ---- (4) NB24 crane driver shoots back
            OzCheatSpawner.FreeTile(W, f.x + 6, f.y + 4, 2, false, out int cx, out int cy);
            var crane = Spawn("vehicle", "crane", cx, cy, Team.Red);
            crane.MaxHealth *= 20; crane.SetHealth(crane.MaxHealth);
            var att = Spawn("robot", "grunt", cx, cy + 5, Team.Blue, 1);
            yield return new WaitForSeconds(0.5f);
            float tc = Time.unscaledTime; int cs0 = BoardingView.CommanderShots;
            Check("blue grunt attack order on the crane", Order(att, WpMode.Attack, crane));
            g.Views.TryGetValue(crane.Id, out var craneView);
            float cf0 = craneView ? craneView.LastFireAt : -100f; int cfires = 0; bool gunUp = false;
            tw = 0f; while (tw < 9f) { gunUp |= crane.CraneGunUp; if (craneView && craneView.LastFireAt > cf0) { cfires++; cf0 = craneView.LastFireAt; } tw += Time.deltaTime; yield return null; }
            Log($"crane: gun up {gunUp}, drawn shots {cfires}, commander view shots {BoardingView.CommanderShots - cs0}; sounds {Keys(tc)}");
            Check("NB24 crane driver pops up and shoots back", gunUp && cfires >= 1);

            // ---- clean up + report
            foreach (var o in spawned) { if (o == null || o.Destroyed) continue; foreach (var m in o.Minions) if (!m.Destroyed) { m.SetHealth(0); W.UpdateObjectHealth(m); } o.SetHealth(0); W.UpdateObjectHealth(o); }
            Report = (fails == 0 ? "PASS" : $"FAIL ({fails})") + $" in {Time.unscaledTime - T0:F0} s\n" + sb;
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "proof_log.txt"), "bundle_main_E proof (H9)\n" + Report);
            Debug.Log("[BundleEProof] " + Report.Split('\n')[0]);
            Destroy(gameObject);
        }
    }
}
