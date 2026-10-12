using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted grunt mirror fights for the rifle pair rule (deviation DV78, Sim/ZObject.RiflePair.cs): an Editor menu, headless sim
    /// on a flat map, no scene, no Play mode (the pattern of OpenZSnipeCount). Part 1 (per hit): the target cannot die and does
    /// not shoot; counted: hits landed and the HP one hit removes, for the grunt pair and for every control pair. Part 2 (fights):
    /// two fresh units, everything mortal, both fire; counted over Fights fights: seconds until one side is dead, the time of each
    /// death on the way, shots and hits, who is left. Mirror rows: two grunt squads without grenades standing in range, and one
    /// squad ordered to attack from out of range. Two entries: the OpenZ rules (ZWorld.RiflePairDeviation on) and Zod exact (off);
    /// the switch is restored afterwards.
    /// </summary>
    public static class OpenZGruntMirrorCount
    {
        const int CX = 640, CY = 640, Big = 1000000000, Fights = 20, SeedBase = 200;
        const double HitSeconds = 30, FightCap = 600;

        /// <summary>The control pairs: a grunt squad against every other target, and other attackers against a grunt squad.</summary>
        static readonly string[][] Controls =
        {
            new[] { "grunt", "psycho" }, new[] { "grunt", "sniper" }, new[] { "grunt", "tough" }, new[] { "grunt", "pyro" }, new[] { "grunt", "laser" },
            new[] { "grunt", "jeep" }, new[] { "grunt", "light" }, new[] { "grunt", "medium" }, new[] { "grunt", "gatling" }, new[] { "grunt", "gun" },
            new[] { "psycho", "grunt" }, new[] { "sniper", "grunt" }, new[] { "pyro", "grunt" }, new[] { "laser", "grunt" }, new[] { "jeep", "grunt" }, new[] { "gatling", "grunt" },
        };

        [MenuItem("OpenZ/Deviations/Count Grunt Mirror Fights (OpenZ rules)")]
        static void CountOpenZ() { Debug.Log(Report(true)); }

        [MenuItem("OpenZ/Deviations/Count Grunt Mirror Fights (Zod exact)")]
        static void CountZod() { Debug.Log(Report(false)); }

        static string settings, flat;
        static string Settings { get { return settings ?? (settings = Resources.Load<TextAsset>("OpenZ/default_settings").text); } }
        static string Flat { get { return flat ?? (flat = BuildFlat(96, 96)); } }

        static string BuildFlat(int w, int h)
        {
            var sb = new StringBuilder("{\"file\":\"mirror_flat\",\"planet\":\"desert\",\"width\":" + w + ",\"height\":" + h + ",\"classes\":[");
            sb.Append(string.Join(",", Enumerable.Repeat("0", w * h))).Append("],\"tiles\":[").Append(string.Join(",", Enumerable.Repeat("0", w * h)));
            sb.Append("],\"zones\":[{\"x\":0,\"y\":0,\"w\":" + w + ",\"h\":" + h + "}],\"objects\":[]}");
            return sb.ToString();
        }

        static ZWorld NewWorld(int seed) { var w = ZWorld.Create(Settings, Flat, seed); w.Events.Clear(); return w; }

        static ZObject Place(ZWorld w, string t, int cx, int cy, Team team)
        {
            return ZBuildLists.IsRobot(t) ? w.CreateRobotGroup(t, cx - 8, cy - 8, team)
                                          : w.CreateObject(ZBuildLists.IsCannon(t) ? "cannon" : "vehicle", t, cx - 16, cy - 16, team);
        }

        static List<ZObject> Squad(ZObject o) { var l = new List<ZObject> { o }; l.AddRange(o.Minions); return l; }

        static void Order(ZWorld w, ZObject a, ZObject t)
        {
            w.SendWaypoints(a.Owner, a, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = t.Id, x = t.CenterX, y = t.CenterY } });
        }

        static bool Lost(List<ZObject> side) { return side.All(m => m.Destroyed || (m.Kind != ZKind.Robot && m.Owner == Team.Neutral)); }

        /// <summary>Part 1: hull hits of `attacker` on `target` and the HP they removed (driver hits are not counted).</summary>
        public static void PerHit(string attacker, string target, out long hits, out long hp)
        {
            hits = 0; hp = 0;
            var w = NewWorld(1);
            var a = Place(w, attacker, CX, CY, Team.Red);
            var t = Place(w, target, CX + 80, CY, Team.Blue);
            var ts = Squad(t); var ids = new HashSet<int>(ts.Select(m => m.Id));
            foreach (var m in ts) { m.Damage = 0; m.MoveSpeed = 0; m.RealMoveSpeed = 0; m.MaxHealth = m.Health = Big; }
            foreach (var m in Squad(a)) m.MaxHealth = m.Health = Big;
            Order(w, a, t);
            while (w.Time < HitSeconds)
            {
                w.Tick(ZConst.TICK);
                foreach (var e in w.Events) if (e.type == ZEventType.Hitscan && ids.Contains(e.otherId) && e.flag && !e.flag2) hits++;
                w.Events.Clear();
            }
            foreach (var m in ts) hp += Big - m.Health;
        }

        public struct Tally
        {
            public int fights, redWins, blueWins, none, grenades;
            public double time, min, max, redLeft, blueLeft, fromShot;
            public long shots, hits;
            public double[] deathAt; public int[] deathN;
        }

        /// <summary>Part 2: `fights` fights of a red `a` against a blue `b`, `gap` px apart. order = red gets an attack order (else both
        /// stand and engage by themselves); redGrenades = grenades given to the red leader.</summary>
        public static Tally Fight(string a, string b, int gap, bool order, int redGrenades, int fights = Fights)
        {
            var r = new Tally { min = double.MaxValue, deathAt = new double[8], deathN = new int[8] };
            for (int seed = 1; seed <= fights; seed++)
            {
                var w = NewWorld(SeedBase + seed);
                var ro = Place(w, a, CX, CY, Team.Red);
                var bo = Place(w, b, CX + gap, CY, Team.Blue);
                var red = Squad(ro); var blue = Squad(bo);
                if (redGrenades > 0) ro.Grenades = redGrenades;
                if (order) Order(w, ro, bo);
                var ids = new HashSet<int>(red.Select(m => m.Id)); foreach (var m in blue) ids.Add(m.Id);
                int deaths = 0; bool rl = false, bl = false; double firstShot = -1;
                while (w.Time < FightCap && !rl && !bl)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if ((e.type == ZEventType.Hitscan || e.type == ZEventType.MissileFired) && ids.Contains(e.objId) && firstShot < 0) firstShot = w.Time;
                        if (e.type == ZEventType.Hitscan && ids.Contains(e.objId)) { r.shots++; if (e.flag) r.hits++; }
                        else if (e.type == ZEventType.MissileFired && ids.Contains(e.objId) && e.flag) r.grenades++;
                        else if (e.type == ZEventType.Death && ids.Contains(e.objId)) { if (deaths < r.deathAt.Length) { r.deathAt[deaths] += w.Time; r.deathN[deaths]++; } deaths++; }
                    }
                    w.Events.Clear();
                    rl = Lost(red); bl = Lost(blue);
                }
                r.fights++;
                if (!rl && !bl) { r.none++; continue; }
                if (bl && !rl) r.redWins++; else if (rl && !bl) r.blueWins++;
                r.time += w.Time; if (w.Time < r.min) r.min = w.Time; if (w.Time > r.max) r.max = w.Time; r.fromShot += w.Time - System.Math.Max(0.0, firstShot);
                r.redLeft += red.Count(m => !m.Destroyed); r.blueLeft += blue.Count(m => !m.Destroyed);
            }
            return r;
        }

        static string F(double v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }

        static string Row(string label, Tally t)
        {
            int done = t.fights - t.none;
            var deaths = new List<string>();
            for (int i = 0; i < t.deathAt.Length; i++) if (t.deathN[i] > 0) deaths.Add(F(t.deathAt[i] / t.deathN[i]) + " (" + t.deathN[i] + ")");
            return label + " | " + t.fights + " | " + (done > 0 ? F(t.time / done) : "-") + " | " + (done > 0 ? F(t.fromShot / done) : "-") + " | " + (done > 0 ? F(t.min) + " - " + F(t.max) : "-") + " | " + t.redWins + " / " + t.blueWins + " / " + t.none
                 + " | " + (done > 0 ? F(t.redLeft / done) + " / " + F(t.blueLeft / done) : "-") + " | " + F((double)t.shots / t.fights) + " / " + F((double)t.hits / t.fights) + " | " + F((double)t.grenades / t.fights)
                 + " | " + (deaths.Count > 0 ? string.Join(", ", deaths.ToArray()) : "-");
        }

        /// <summary>The whole count as text. openz = the rifle pair rule on (the game), else Zod exact.</summary>
        public static string Report(bool openz)
        {
            bool was = ZWorld.RiflePairDeviation;
            var sb = new StringBuilder();
            try
            {
                ZWorld.RiflePairDeviation = openz;
                var gw = NewWorld(1); var g = gw.CreateObject("robot", "grunt", CX, CY, Team.Red);
                sb.AppendLine("[OpenZ] grunt mirror fights, " + (openz ? "OpenZ rules (RiflePairDeviation on)" : "Zod exact (RiflePairDeviation off)"));
                sb.AppendLine("grunt: " + g.MaxHealth + " HP, rifle " + g.Damage + " HP per hit (Zod), hit chance " + g.DamageChance.ToString("0.00", CultureInfo.InvariantCulture) + ", one shot every " + g.AttackSpeed.ToString("0.00", CultureInfo.InvariantCulture)
                              + " s, range " + g.AttackRadius + " px, squad of " + gw.S.Unit("grunt").group_amount + "; pair table: grunt on grunt " + ZWorld.RiflePair[("grunt", "grunt")] + " HP");
                sb.AppendLine();
                sb.AppendLine("PER HIT (" + HitSeconds.ToString("0") + " s, the target cannot die and does not shoot; hull / body hits only)");
                sb.AppendLine("attacker -> target | hits | HP removed | HP per hit");
                var pairs = new List<string[]> { new[] { "grunt", "grunt" } }; pairs.AddRange(Controls);
                foreach (var p in pairs)
                {
                    long hits, hp; PerHit(p[0], p[1], out hits, out hp);
                    sb.AppendLine(p[0] + " -> " + p[1] + " | " + hits + " | " + hp + " | " + (hits > 0 ? ((double)hp / hits).ToString("0.00", CultureInfo.InvariantCulture) : "-"));
                }
                sb.AppendLine();
                sb.AppendLine("FIGHTS (" + Fights + " per row, seeds " + (SeedBase + 1) + "-" + (SeedBase + Fights) + ", all mortal, both fire, cap " + FightCap.ToString("0") + " s)");
                sb.AppendLine("row | fights | mean s until one side is dead | mean s from the first shot | shortest - longest s | red won / blue won / no result | left alive red / blue (mean) | shots / hits per fight | grenades per fight | mean s of the 1st, 2nd ... death (fights that had it)");
                sb.AppendLine(Row("MIRROR grunt squad vs grunt squad, no grenades, both standing 80 px apart", Fight("grunt", "grunt", 80, false, 0)));
                sb.AppendLine(Row("MIRROR grunt squad vs grunt squad, no grenades, red ordered to attack from 240 px", Fight("grunt", "grunt", 240, true, 0)));
                sb.AppendLine(Row("grunt squad WITH 6 grenades vs grunt squad, both standing 80 px apart", Fight("grunt", "grunt", 80, false, 6)));
                foreach (var p in Controls) sb.AppendLine(Row(p[0] + " vs " + p[1] + ", red ordered to attack from 80 px", Fight(p[0], p[1], 80, true, 0)));
            }
            finally { ZWorld.RiflePairDeviation = was; }
            return sb.ToString();
        }

        /// <summary>Calibration: the two mirror rows with the pair value replaced by each of `values` (the table is restored).</summary>
        public static string Calibrate(string values)
        {
            bool was = ZWorld.RiflePairDeviation; int old = ZWorld.RiflePair[("grunt", "grunt")];
            var sb = new StringBuilder("[OpenZ] grunt mirror calibration\nrow | fights | mean s | from the first shot | shortest - longest | red / blue / none | left red / blue | shots / hits | grenades | deaths at\n");
            try
            {
                ZWorld.RiflePairDeviation = true;
                foreach (var s in values.Split(','))
                {
                    int v = int.Parse(s.Trim(), CultureInfo.InvariantCulture);
                    ZWorld.RiflePair[("grunt", "grunt")] = v;
                    sb.AppendLine(Row(v + " HP standing", Fight("grunt", "grunt", 80, false, 0)));
                    sb.AppendLine(Row(v + " HP ordered", Fight("grunt", "grunt", 240, true, 0)));
                }
            }
            finally { ZWorld.RiflePairDeviation = was; ZWorld.RiflePair[("grunt", "grunt")] = old; }
            return sb.ToString();
        }
    }
}
