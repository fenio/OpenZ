using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted driver-snipe check: an Editor menu, headless sim on a flat map, no scene, no Play mode. Zod sources: the roll
    /// zobject.cpp:2187-2199 (hit, then roll &lt;= attack_snipe_chance = driver, else hull), the chances zsettings.cpp
    /// (grunt / psycho 0.3, sniper 0.8, laser 0.6, jeep / gatling 0.4), the 1081 HP driver pool zvehicle.cpp:155-168.
    /// Part 1 (share): the target cannot die and does not shoot, a tank's lid is held open; counted: hits landed and the
    /// driver hits among them. Part 2 (fights): everything mortal, the target shoots back, the lid follows the rule; counted
    /// over Fights fights: driver sniped, hull destroyed first, attackers lost (dead, or their own driver sniped; the last
    /// column counts the own-driver cases among them), no result within the time cap. Two entries: the OpenZ rules (ZWorld.SnipeDeviation on) and Zod exact (off); the switches are
    /// restored afterwards. Vehicles stand and fire (no duel manoeuvres), as in SnipeParityTests.
    /// </summary>
    public static class OpenZSnipeCount
    {
        const int CX = 640, CY = 640, Big = 1_000_000_000, ShareSeeds = 4, Fights = 40;
        const double ShareSeconds = 60, FightCap = 60;

        static readonly (string attacker, string target)[] Pairs =
        {
            ("psycho", "jeep"), ("psycho", "gatling"), ("psycho", "light"),
            ("jeep", "jeep"), ("jeep", "gatling"), ("jeep", "light"),
            ("grunt", "jeep"), ("grunt", "gatling"), ("grunt", "light"), ("gatling", "light"), ("gatling", "jeep"), ("sniper", "jeep"), ("sniper", "light"),
        };

        [MenuItem("OpenZ/Deviations/Count Driver Snipes (OpenZ rules)")]
        static void CountOpenZ() => Run(true);

        [MenuItem("OpenZ/Deviations/Count Driver Snipes (Zod exact)")]
        static void CountZod() => Run(false);

        [MenuItem("OpenZ/Deviations/Count Driver Snipes (OpenZ rules, 200 fights)")]
        static void CountOpenZLong() => Run(true, 200, 1000);

        static string settings, flat;
        static string Settings => settings ??= Resources.Load<TextAsset>("OpenZ/default_settings").text;
        static string Flat => flat ??= BuildFlat(96, 96);

        static string BuildFlat(int w, int h)
        {
            var sb = new StringBuilder("{\"file\":\"snipe_flat\",\"planet\":\"desert\",\"width\":" + w + ",\"height\":" + h + ",\"classes\":[");
            sb.Append(string.Join(",", Enumerable.Repeat("0", w * h))).Append("],\"tiles\":[").Append(string.Join(",", Enumerable.Repeat("0", w * h)));
            sb.Append("],\"zones\":[{\"x\":0,\"y\":0,\"w\":" + w + ",\"h\":" + h + "}],\"objects\":[]}");
            return sb.ToString();
        }

        static ZWorld NewWorld(int seed) { var w = ZWorld.Create(Settings, Flat, seed); w.Events.Clear(); return w; }

        static ZObject Place(ZWorld w, string t, int cx, int cy, Team team) =>
            ZBuildLists.IsRobot(t) ? w.CreateRobotGroup(t, cx - 8, cy - 8, team)
                                   : w.CreateObject(ZBuildLists.IsCannon(t) ? "cannon" : "vehicle", t, cx - 16, cy - 16, team);

        static IEnumerable<ZObject> Squad(ZObject o) { yield return o; foreach (var m in o.Minions) yield return m; }

        static void Order(ZWorld w, ZObject a, ZObject t) =>
            w.SendWaypoints(a.Owner, a, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = t.Id, x = t.CenterX, y = t.CenterY } });

        /// <summary>Part 1: hits on the target and the driver hits among them (the share is the snipe chance in use).</summary>
        static (long hits, long driver) Share(string attacker, string target)
        {
            long hits = 0, driver = 0;
            for (int seed = 1; seed <= ShareSeeds; seed++)
            {
                var w = NewWorld(seed);
                var a = Place(w, attacker, CX, CY, Team.Red);
                var t = Place(w, target, CX + 80, CY, Team.Blue);
                t.Damage = 0; t.MoveSpeed = 0; t.RealMoveSpeed = 0; t.MaxHealth = t.Health = Big; t.Drivers[0] = Big;
                foreach (var m in Squad(a)) m.MaxHealth = m.Health = Big;
                Order(w, a, t);
                while (w.Time < ShareSeconds)
                {
                    if (t.HasLid && t.Drivers.Count > 0) t.LidOpen = true;                   // only the roll is counted here
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events) if (e.type == ZEventType.Hitscan && e.otherId == t.Id && e.flag) { hits++; if (e.flag2) driver++; }
                    w.Events.Clear();
                }
            }
            return (hits, driver);
        }

        struct Tally { public int sniped, hull, lost, none, ownSniped; public double time; }

        /// <summary>Part 2: real fights, one attacker unit (a full squad for robots) against one crewed target that shoots back.</summary>
        static Tally Fight(string attacker, string target, int fights = Fights, int seedBase = 100)
        {
            var r = new Tally();
            for (int seed = 1; seed <= fights; seed++)
            {
                var w = NewWorld(seedBase + seed);
                var t = Place(w, target, CX + 80, CY, Team.Blue); t.MoveSpeed = 0; t.RealMoveSpeed = 0;
                var a = Place(w, attacker, CX, CY, Team.Red);
                var squad = Squad(a).ToList();
                Order(w, a, t);
                int outcome = 3;
                while (w.Time < FightCap && outcome == 3)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (e.objId != t.Id || outcome != 3) continue;
                        if (e.type == ZEventType.Sniped) outcome = 0;
                        else if (e.type == ZEventType.Death) outcome = 1;
                    }
                    w.Events.Clear();
                    if (outcome != 3) break;
                    bool lost = a.Kind == ZKind.Robot ? squad.All(m => m.Destroyed) : a.Destroyed || a.Owner == Team.Neutral;
                    if (lost) { outcome = 2; if (a.Kind != ZKind.Robot && !a.Destroyed) r.ownSniped++; }
                }
                if (outcome == 0) { r.sniped++; r.time += w.Time; }
                else if (outcome == 1) r.hull++;
                else if (outcome == 2) r.lost++;
                else r.none++;
            }
            return r;
        }

        /// <summary>The counts asked for, per 40 fights with the OpenZ rules: printed next to each row.</summary>
        static readonly Dictionary<(string, string), string> Wanted = new Dictionary<(string, string), string>
        {
            [("psycho", "jeep")] = "about 39", [("psycho", "gatling")] = "12-16", [("psycho", "light")] = "8-10",
            [("jeep", "jeep")] = "about 21", [("jeep", "gatling")] = "about 8", [("jeep", "light")] = "6-8",
            [("gatling", "light")] = "about 12", [("grunt", "jeep")] = "as before (16)", [("grunt", "gatling")] = "about 4", [("grunt", "light")] = "0-1",
        };

        static void Run(bool openz, int fights = Fights, int seedBase = 100)
        {
            bool snipe = ZWorld.SnipeDeviation, duel = ZWorld.DuelDeviation;
            var sb = new StringBuilder();
            try
            {
                ZWorld.SnipeDeviation = openz; ZWorld.DuelDeviation = false;
                sb.AppendLine($"[OpenZ] driver snipe count, {(openz ? "OpenZ rules (SnipeDeviation on)" : "Zod exact (SnipeDeviation off)")}");
                sb.AppendLine($"share: {ShareSeeds} x {ShareSeconds:0} s, target cannot die, a tank's lid held open | fights: {fights} per pair (seeds {seedBase + 1}-{seedBase + fights}), all mortal, cap {FightCap:0} s, lid by the rule");
                sb.AppendLine("attacker -> target | hits | driver hits | share | Zod chance | chance in use | HP per driver hit | fights: driver sniped | per 40 | wanted per 40 | hull first | attackers lost | no result | mean s to the snipe | of the lost: own driver sniped");
                var zodWorld = NewWorld(1);
                foreach (var (attacker, target) in Pairs)
                {
                    var (hits, driver) = Share(attacker, target);
                    var f = Fight(attacker, target, fights, seedBase);
                    double zod = zodWorld.S.Unit(attacker).attack_snipe_chance;
                    var vw = NewWorld(1);
                    ZObject va = Place(vw, attacker, CX, CY, Team.Red), vt = Place(vw, target, CX + 80, CY, Team.Blue);
                    double inUse = ZWorld.SnipeChanceOn(va, vt); int perHit = vw.DriverHitDamage(va, vt);
                    double share = hits > 0 ? (double)driver / hits : 0.0, mean = f.sniped > 0 ? f.time / f.sniped : 0.0;
                    string wanted = openz && Wanted.TryGetValue((attacker, target), out string wv) ? wv : "-";
                    sb.AppendLine($"{attacker} -> {target} | {hits} | {driver} | {share:0.000} | {zod:0.00} | {inUse:0.00} | {perHit} | {f.sniped}/{fights} | {40.0 * f.sniped / fights:0.0} | {wanted} | {f.hull}/{fights} | {f.lost}/{fights} | {f.none}/{fights} | {mean:0.0} | {f.ownSniped}");
                }
            }
            finally { ZWorld.SnipeDeviation = snipe; ZWorld.DuelDeviation = duel; }
            Debug.Log(sb.ToString());
        }
    }
}
