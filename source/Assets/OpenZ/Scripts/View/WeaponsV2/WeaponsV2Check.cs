using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// weapons_v2 Play-mode stage (menu OpenZ/Weapons v2/Stage Weapon Show, or WeaponsV2Check.Begin()): one RED squad of each robot
    /// weapon (grunt, psycho, tough, laser, sniper) facing away from BLUE targets (three unkillable harmless heavy tanks as punching
    /// bags, a killable blue grunt squad, snipers 8 tiles out for the long shot), Zod's passive engage only (no orders). The camera
    /// grunt bolts + bolt sounds; psycho bursts / rounds per second / casings / MG sounds; laser bolts / zaps / sounds; sniper aimed
    /// shots vs skipped rolls, kill shots, the visible cadence, the stances picked and reached, sight frames, crack + echo sounds;
    /// Tough rockets, raise holds, the time from the sim's engage to the first rocket, how far the OLD spawn point (CombatView's
    /// pre-WeaponHold muzzle) was from the tube's Muzzle socket; missing clips / FX channels.
    /// </summary>
    public class WeaponsV2Check : MonoBehaviour
    {
        public static float Duration = 30f;
        ZGame game; ZWorld W => game.World;
        readonly List<ZObject> red = new List<ZObject>();
        readonly Dictionary<int, float> engagedAt = new Dictionary<int, float>();
        readonly Dictionary<int, List<float>> sniperShots = new Dictionary<int, List<float>>();
        readonly Dictionary<int, float> maxPose = new Dictionary<int, float>();
        bool running; public string Phase = "idle"; int unsteady0;

        public static WeaponsV2Check Begin()
        {
            if (!ZGame.I || ZGame.I.World == null) { Debug.LogWarning("WeaponsV2Check: no running ZGame (enter Play mode on L02 or L17)"); return null; }
            var c = FindFirstObjectByType<WeaponsV2Check>(); if (!c) c = ZGame.I.gameObject.AddComponent<WeaponsV2Check>();
            if (!c.running) { c.running = true; c.StartCoroutine(c.Run()); }
            return c;
        }

        (int, int) Spot(int tx, int ty, bool robot)
        {
            for (int r = 0; r < 8; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        int x = tx + dx, y = ty + dy;
                        bool ok = robot ? W.Path.TilePassable(x, y, true)
                                        : W.Path.TilePassable(x, y, false) && W.Path.TilePassable(x + 1, y, false) && W.Path.TilePassable(x, y + 1, false) && W.Path.TilePassable(x + 1, y + 1, false);
                        if (ok) return (x, y);
                    }
            return (tx, ty);
        }

        ZObject Squad(string type, int tx, int ty, Team t)
        {
            var (x, y) = Spot(tx, ty, true);
            var o = W.CreateRobotGroup(type, x * 16, y * 16, t); game.SpawnUnitView(o); foreach (var m in o.Minions) game.SpawnUnitView(m);
            return o;
        }

        ZObject Unit(string cat, string type, int tx, int ty, Team t)
        {
            var (x, y) = Spot(tx, ty, false);
            var o = W.CreateObject(cat, type, x * 16, y * 16, t); game.SpawnUnitView(o);
            return o;
        }

        IEnumerable<ZObject> Members(ZObject leader) { yield return leader; foreach (var m in leader.Minions) if (m != null) yield return m; }

        IEnumerator Run()
        {
            game = ZGame.I;
            var run = WeaponsV2Runner.Ensure();
            var au = CombatAudio.I;
            var r0 = Snapshot(run); var p0 = au ? new Dictionary<string, int>(au.PlayCounts) : new Dictionary<string, int>();
            unsteady0 = run ? run.UnsteadyLaunches : 0;
            int ox = game.level == "L17" ? W.Map.width / 2 - 8 : 19, oy = game.level == "L17" ? W.Map.height / 2 - 4 : 50;
            Phase = "staging";
            for (int i = 0; i < 3; i++)
            {   // punching bags: never die, never shoot
                var bag = Unit("vehicle", "heavy", ox + 2 + i * 5, oy, Team.Blue);
                bag.MaxHealth = bag.Health = 1000000000; bag.Damage = 0;
            }
            Squad("grunt", ox + 7, oy - 1, Team.Blue);
            string[] robots = { "grunt", "psycho", "tough", "laser" };
            for (int i = 0; i < robots.Length; i++) red.Add(Squad(robots[i], ox + 1 + i * 3, oy + 5, Team.Red));
            red.Add(Squad("sniper", ox + 14, oy + 8, Team.Red));                                   // 8 tiles: a long shot (range 9)
            var cam = FindFirstObjectByType<RtsCamera>();
            if (cam) { cam.FlyTo(ZGame.PxToWorld((ox + 8) * 16, (oy + 4) * 16)); cam.ZoomTo(45f); }             // tile -> px -> world (x / 8, -y / 8)
            Phase = "fighting";
            float t0 = Time.time;
            while (Time.time - t0 < Duration)
            {
                foreach (var l in red)
                    foreach (var o in Members(l))
                    {
                        if (o.AttackObject != null && !engagedAt.ContainsKey(o.Id)) engagedAt[o.Id] = Time.time;
                        if (o.Type == "sniper" && game.Views.TryGetValue(o.Id, out var v) && v && v.TryGetComponent(out RobotWeaponPose p))
                            maxPose[o.Id] = Mathf.Max(maxPose.TryGetValue(o.Id, out var m) ? m : 0f, p.PoseWeight);
                    }
                yield return null;
            }
            Phase = "done";
            Report(run, r0, p0, au);
        }

        static int[] Snapshot(WeaponsV2Runner r) => r == null ? new int[13] : new[] { r.GruntShots, r.MgBursts, r.MgRounds, r.Casings, r.LaserShots, r.LaserBolts, r.Zaps,
            r.SniperShots, r.SniperSkipped, r.SniperKillShots, r.Launches, r.Holds, r.HoldsReleased };

        void Report(WeaponsV2Runner run, int[] r0, Dictionary<string, int> p0, CombatAudio au)
        {
            var d = Snapshot(run); for (int i = 0; i < d.Length; i++) d[i] -= r0[i];
            int Plays(string k) => (au && au.PlayCounts.TryGetValue(k, out var n) ? n : 0) - (p0.TryGetValue(k, out var m) ? m : 0);
            var sb = new StringBuilder();
            sb.AppendLine("# weapons_v2 check (staged robot weapon show)");
            sb.AppendLine();
            sb.AppendLine($"{System.DateTime.Now:yyyy-MM-dd HH:mm}, level {game.level}, {Duration:F0} s, game speed {game.gameSpeed:F2}. Red grunt / psycho / tough / laser squads and a sniper squad " +
                          $"8 tiles out, blue punching bags + a blue grunt squad; Zod's passive engage only. Tough prepare DV11: {(ZWorld.ToughPrepareDeviation ? $"ON ({ZWorld.ToughPrepareSeconds:0.0} s)" : "off (Zod exact)")}.");
            var missing = new List<string>(); int miss = WeaponsV2.MissingClips(au, missing);
            bool channels = System.Array.Exists(OpenZFxCatalog.Channels, c => c.name == "wv2_laser");
            sb.AppendLine($"Clips missing in CombatAudio: **{(miss < 0 ? "no CombatAudio" : miss.ToString())}** {(missing.Count > 0 ? "(" + string.Join(", ", missing) + ")" : "")}; " +
                          $"wv2 FX channels in the catalog: **{(channels ? "yes" : "NO (fallback channels)")}**; beam material: {(Resources.Load<Material>("OpenZ/WeaponsV2/M_WV2_Beam") ? "yes" : "runtime fallback")}.");
            sb.AppendLine();
            sb.AppendLine("| Weapon | Measured | Sounds (CombatAudio plays) | Pass |");
            sb.AppendLine("|---|---|---|---|");
            float mgRate = d[1] > 0 ? d[2] / Mathf.Max(0.01f, d[1] * 0.7f) : 0f;
            sb.AppendLine($"| grunt | {d[0]} legacy bolts from the muzzle socket | bolt {Plays("bolt")} | {(d[0] > 0 && Plays("bolt") > 0 ? "PASS" : "**FAIL**")} |");
            sb.AppendLine($"| psycho | {d[1]} MG bursts, {d[2]} tracer rounds ({(d[1] > 0 ? d[2] / (float)d[1] : 0):F1} per burst, ~{mgRate:F0} rounds/s in a burst), {d[3]} casings | mg {Plays("mg")}, casings via AudioDirector | " +
                          $"{(d[1] > 0 && d[2] >= d[1] * 6 && Plays("mg") > 0 ? "PASS" : "**FAIL**")} |");
            sb.AppendLine($"| laser | {d[4]} shots, {d[5]} moving bolts ({(d[4] > 0 ? d[5] / (float)d[4] : 0):F1} per shot), {d[6]} zaps | laser {Plays("laser")}, laser_hit {Plays("laser_hit")} | " +
                          $"{(d[4] > 0 && d[5] >= d[4] && d[6] > 0 && Plays("laser") > 0 && Plays("laser_hit") > 0 ? "PASS" : "**FAIL**")} |");
            int stances = 0, kneel = 0, prone = 0, stand = 0; float reached = 0f;
            foreach (var l in red) foreach (var o in Members(l))
                    if (o.Type == "sniper" && game.Views.TryGetValue(o.Id, out var v) && v && v.TryGetComponent(out RobotWeaponPose p))
                    { kneel += p.StanceKneels; prone += p.StanceProne; stand += p.StanceStand; stances++; }
            foreach (var kv in maxPose) reached = Mathf.Max(reached, kv.Value);
            float cadence = d[7] > 1 ? Duration / d[7] : 0f;
            sb.AppendLine($"| sniper | {d[7]} aimed shots ({d[9]} kill shots), {d[8]} rolls drawn as aim only, sight frames {run?.SightFrames:F0}; stances picked: kneel {kneel}, prone {prone}, stand {stand} " +
                          $"(max pose weight reached {reached:F2}) | sniper {Plays("sniper")}, echo {Plays("sniper_tail")}, servo / thud {Plays("servo")} | " +
                          $"{(d[7] > 0 && Plays("sniper") > 0 && Plays("sniper_tail") > 0 && (kneel + prone == 0 || reached > 0.9f) ? "PASS" : "**FAIL**")} |");
            string first = "";
            if (run != null)
                foreach (var l in red) foreach (var o in Members(l))
                        if (o.Type == "tough" && engagedAt.TryGetValue(o.Id, out var ea) && run.FirstLaunchAt.TryGetValue(o.Id, out var fl)) first += $"{(fl - ea) * 1000f:F0} ms ";
            bool dv11 = ZWorld.ToughPrepareDeviation;
            bool steady = run != null && run.UnsteadyLaunches - unsteady0 == 0 && (!dv11 || d[11] == 0);
            sb.AppendLine($"| tough | {d[10]} rockets from the Muzzle socket; {d[11]} first-rocket holds for the raise (released {d[12]}, longest {run?.MaxHoldMs:F0} ms; DV11 on: 0 expected); " +
                          $"{(run != null ? run.UnsteadyLaunches - unsteady0 : -1)} rockets left before the tube was on the target (0 expected); " +
                          $"first rocket after the sim's engage: {first}(DV11 on: ~{ZWorld.ToughPrepareSeconds * 1000 / Mathf.Max(0.25f, game.gameSpeed):F0} ms real at this game speed); old spawn point was up to {run?.MaxOldSpawnError:F2} m off the drawn tube's muzzle | rocket {Plays("rocket")}, backblast {Plays("backblast")}, servo {Plays("servo")} | " +
                          $"{(d[10] > 0 && Plays("rocket") > 0 && Plays("backblast") > 0 && steady ? "PASS" : "**FAIL**")} |");
            sb.AppendLine();
            sb.AppendLine("Look at: psycho = a stream of many tracers in bursts with casings flying, clearly not the grunt's single orange bolt; " +
                          "tough = bazooka carried muzzle-up, raised and settled before the first rocket, rocket out of the tube front on the RIGHT shoulder, back-blast out of the rear, " +
                          "the body kicked back; laser = moving green dashes (not a line) that bloom by day, a green flash + sparks + arcs + zap on hit; sniper = laser sight + glint, " +
                          "then a crack with a long tracer / trail and an echo, kneeling or prone at long range, standing up to move.");
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "..", "work", "records"));
            string text = sb.ToString();
            if (Application.isEditor && Directory.Exists(Path.GetDirectoryName(dir))) { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "weapons_v2_check.md"), text); }
            Debug.Log("[OpenZ] weapons_v2 check\n" + text);
            running = false;
        }
    }
}
