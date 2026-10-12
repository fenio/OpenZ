using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// combatview test stage (play mode, L02 open field south-west of the centre bridges like FightStage, or the map centre
    /// on L17): every armed unit type in RED lines; spawned views face south (prefab rotation, model front -z), the BLUE
    /// targets stand NORTH of them, so every gun has to turn around before its first shot. Blue: three unkillable, harmless heavy tanks (punching bags for every weapon),
    /// a killable grunt squad and a killable jeep (ragdolls, wreck, turret toss, pop-outs, scorch). One red grunt squad
    /// turn, the longest hold, the worst muzzle error at the moment each shot left, plus grenades thrown, scorch marks and
    /// pop-outs. Start: menu OpenZ/Stage Weapon Line-Up (combatview), or CombatViewCheck.Begin() in play mode.
    /// Uses only the normal sim API (CreateObject / CreateRobotGroup, game.SpawnUnitView), no orders: Zod's passive engage.
    /// </summary>
    public class CombatViewCheck : MonoBehaviour
    {
        public const float Duration = 30f;
        ZGame game; ZWorld W => game.World;
        readonly List<ZObject> red = new List<ZObject>(); ZObject nades;
        readonly Dictionary<int, ZObject> member = new Dictionary<int, ZObject>();   // sim id of every staged red unit (squad members too) -> its line-up entry
        bool running; public string Phase = "idle";

        public static CombatViewCheck Begin()
        {
            if (!ZGame.I || ZGame.I.World == null) { Debug.LogWarning("CombatViewCheck: no running ZGame"); return null; }
            var c = FindFirstObjectByType<CombatViewCheck>(); if (!c) c = ZGame.I.gameObject.AddComponent<CombatViewCheck>();
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

        IEnumerator Run()
        {
            game = ZGame.I;
            UnitAim.ResetStats();
            int s0 = ScorchDecals.I ? ScorchDecals.I.Count : 0, p0 = PopOut.Popped, g0 = GrenadeFx.Thrown;
            // L02: the FightStage field (tiles 19-35 x 50-58); L17: around the map centre. Blue row oy (north), robots
            // oy + 4, vehicles and cannons oy + 6 (all within every attack range, 7.5-10 tiles)
            int ox = game.level == "L17" ? W.Map.width / 2 - 8 : 19, oy = game.level == "L17" ? W.Map.height / 2 - 4 : 50;
            Phase = "staging";
            for (int i = 0; i < 3; i++)
            {   // punching bags: never die, never shoot
                var bag = Unit("vehicle", "heavy", ox + 2 + i * 5, oy, Team.Blue);
                bag.MaxHealth = bag.Health = 1000000000; bag.Damage = 0;
            }
            Squad("grunt", ox + 5, oy - 1, Team.Blue);
            Unit("vehicle", "jeep", ox + 10, oy - 1, Team.Blue);
            string[] robots = { "grunt", "psycho", "sniper", "tough", "pyro", "laser" };
            for (int i = 0; i < robots.Length; i++) red.Add(Squad(robots[i], ox + 1 + i * 2, oy + 4, Team.Red));
            nades = Squad("grunt", ox + 13, oy + 4, Team.Red); nades.Grenades = 6; red.Add(nades);
            string[] vehicles = { "jeep", "light", "medium", "heavy", "missile_launcher" };
            for (int i = 0; i < vehicles.Length; i++) red.Add(Unit("vehicle", vehicles[i], ox + i * 4, oy + 6, Team.Red));
            string[] cannons = { "gatling", "gun", "howitzer", "missile_cannon" };
            for (int i = 0; i < cannons.Length; i++) red.Add(Unit("cannon", cannons[i], ox + 2 + i * 4, oy + 6, Team.Red));
            foreach (var o in red) { member[o.Id] = o; foreach (var m in o.Minions) member[m.Id] = o; }
            Phase = "fighting";
            yield return new WaitForSeconds(Duration);
            Phase = "done";
            Report(s0, p0, g0);
        }

        void Report(int s0, int p0, int g0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# combatview check (staged weapon line-up)");
            sb.AppendLine();
            sb.AppendLine($"{System.DateTime.Now:yyyy-MM-dd HH:mm}, level {game.level}, {Duration:F0} s. Every red gun starts facing away from its targets.");
            // included); the level's own units (L02 armies, the blue targets) fire too and are listed apart, outside the verdict
            int n = 0, h = 0, to = 0, sn = 0; float sum = 0f, worst = 0f; string worstWho = "-";
            var rows = new StringBuilder();
            foreach (var o in red)
            {
                int rn = 0, rh = 0, rto = 0, rsn = 0, members = 0; float rhold = 0f, rerr = 0f; string how = "";
                foreach (var kv in UnitAim.StatById)
                {
                    ZObject g; if (!member.TryGetValue(kv.Key, out g) || g != o) continue;
                    var st = kv.Value; members++;
                    rn += st.Shots; rh += st.Held; rto += st.Timeouts; rsn += st.Snaps; sum += st.SumErr; rhold = Mathf.Max(rhold, st.MaxHoldMs);
                    if (st.MaxErr > rerr) { rerr = st.MaxErr; how = st.WorstHow; }
                    if (st.MaxErr > worst) { worst = st.MaxErr; worstWho = $"red {st.Type} #{kv.Key} ({st.WorstHow})"; }
                }
                n += rn; h += rh; to += rto; sn += rsn;
                float tol = UnitAim.SlewFor(o.Type, o.Kind).tol;
                rows.AppendLine($"| {o.Type}{(o == nades ? " (grenades)" : "")} | {members} | {rn} | {rh} | {rhold:F0} ms | {rerr:F1} deg{(rerr > tol ? " OVER" : "")} | {how} | {tol:F0} deg | {rto} / {rsn} |");
            }
            sb.AppendLine($"Staged red line-up: **{n} visual shots, {h} held for the turn, {to} released off target, {sn} snapped onto the target at the wait cap (MZ1)** " +
                          $"(muzzle error when the shot left: mean {(n > 0 ? sum / n : 0f):F1} deg, worst {worst:F1} deg = {worstWho}). Grenades thrown {GrenadeFx.Thrown - g0}, " +
                          $"scorch marks alive {(ScorchDecals.I ? ScorchDecals.I.Count : 0) - s0} (cap {ScorchDecals.MaxMarks}), pop-outs {PopOut.Popped - p0}.");
            sb.AppendLine();
            sb.AppendLine("| Unit | Members fired | Shots | Held | Longest hold | Worst error at fire | Worst shot | Tolerance | Off-target / snapped |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            sb.Append(rows);
            // everything else that fired in the level during the test, grouped by owner + type
            var other = new SortedDictionary<string, UnitAim.ShotStat>(); int on = 0; float oworst = 0f; string oWho = "-";
            foreach (var kv in UnitAim.StatById)
            {
                if (member.ContainsKey(kv.Key)) continue;
                var st = kv.Value; string key = st.Owner + " " + st.Type; UnitAim.ShotStat agg;
                if (!other.TryGetValue(key, out agg)) other[key] = agg = new UnitAim.ShotStat { Type = st.Type, Owner = st.Owner, Kind = st.Kind };
                agg.Shots += st.Shots; agg.Held += st.Held; agg.Timeouts += st.Timeouts; agg.Snaps += st.Snaps; agg.MaxHoldMs = Mathf.Max(agg.MaxHoldMs, st.MaxHoldMs);
                if (st.MaxErr > agg.MaxErr) { agg.MaxErr = st.MaxErr; agg.WorstHow = $"#{kv.Key}, {st.WorstHow}"; }
                on += st.Shots; if (st.MaxErr > oworst) { oworst = st.MaxErr; oWho = $"{key} #{kv.Key} ({st.WorstHow})"; }
            }
            sb.AppendLine();
            sb.AppendLine($"Not staged (the level's own units and the blue targets; outside the verdict): {on} shots, worst {oworst:F1} deg = {oWho}.");
            if (other.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("| Owner + unit | Shots | Held | Longest hold | Worst error at fire | Worst shot | Tolerance | Off-target / snapped |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|");
                foreach (var kv in other)
                {
                    var a = kv.Value; float tol = UnitAim.SlewFor(a.Type, a.Kind).tol;
                    sb.AppendLine($"| {kv.Key} | {a.Shots} | {a.Held} | {a.MaxHoldMs:F0} ms | {a.MaxErr:F1} deg{(a.MaxErr > tol ? " OVER" : "")} | {a.WorstHow} | {tol:F0} deg | {a.Timeouts} / {a.Snaps} |");
                }
            }
            sb.AppendLine();
            sb.AppendLine($"Cross-check, every UnitAim in the level: {UnitAim.StatShots} shots (= {n} staged + {on} not staged), worst {UnitAim.StatMaxError:F1} deg = {UnitAim.StatWorstWho}.");
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "..", "work", "records"));
            string text = sb.ToString();
            if (Application.isEditor && Directory.Exists(Path.GetDirectoryName(dir))) { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "combatview_check.md"), text); }
            Debug.Log("[OpenZ] combatview check\n" + text);
            running = false;
        }
    }
}
