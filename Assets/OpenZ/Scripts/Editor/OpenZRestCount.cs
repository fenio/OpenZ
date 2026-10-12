using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted idle-rest check (DV75, part of the driver-sniping deviation DC2): an Editor menu, headless sim on a flat map, no
    /// scene, no Play mode; the set-up of OpenZSnipeCount (vehicles stand and fire, no duel manoeuvres).
    /// Part 1, state checks (OK / FAIL lines): no rest in Zod exact; which types rest after the calm time and which of them can be
    /// sniped; an order ends the rest on the next tick; a hit ends it IdleRestHitWakeSeconds later; a veteran crew never rests.
    /// Part 2, share: hits on a target that cannot die and does not shoot, and the driver hits among them, once with the crew
    /// resting (kept resting) and once with the lid held open and no rest. The ratio of the two shares is the rest factor in use.
    /// Part 3, ambushes: a crewed target stands alone for the calm time, then one attacker unit (a full squad for robots) appears
    /// some px away with an attack order; everything mortal, the target shoots back. Counted over Fights ambushes: driver sniped,
    /// hull destroyed first, attackers lost, no result; once on the resting target and once on the same target with the rest held
    /// off (the same wait). Also counted: the hits and driver hits that landed while the crew was still resting.
    /// Every switch and value touched is restored afterwards.
    /// </summary>
    public static class OpenZRestCount
    {
        const int CX = 640, CY = 640, Big = 1_000_000_000, Fights = 40;
        const double FightCap = 60, ShareSeconds = 30, Never = 1e9;

        static readonly (string attacker, string target)[] Pairs =
        {
            ("psycho", "light"), ("jeep", "light"), ("grunt", "light"), ("sniper", "light"), ("laser", "light"),
            ("psycho", "medium"), ("sniper", "medium"), ("sniper", "heavy"),
        };
        /// <summary>px between attacker and target when the attacker appears: 100 = inside both weapon ranges, 240 = out of the
        /// target's sight, the attacker walks in.</summary>
        static readonly int[] Gaps = { 100, 240 };

        [MenuItem("OpenZ/Deviations/Count Resting Driver Snipes")]
        static void Count() => Debug.Log(Report());

        static string settings, flat;
        static string Settings => settings ??= Resources.Load<TextAsset>("OpenZ/default_settings").text;
        static string Flat => flat ??= BuildFlat(96, 96);

        static string BuildFlat(int w, int h)
        {
            var sb = new StringBuilder("{\"file\":\"rest_flat\",\"planet\":\"desert\",\"width\":" + w + ",\"height\":" + h + ",\"classes\":[");
            sb.Append(string.Join(",", Enumerable.Repeat("0", w * h))).Append("],\"tiles\":[").Append(string.Join(",", Enumerable.Repeat("0", w * h)));
            sb.Append("],\"zones\":[{\"x\":0,\"y\":0,\"w\":" + w + ",\"h\":" + h + "}],\"objects\":[]}");
            return sb.ToString();
        }

        /// <summary>A new flat world with one red squad parked far off (900 px from the test spot, out of sight and range, never
        /// moving): with units of one team only the game is over after a second and nothing rests or fights.</summary>
        static ZWorld NewWorld(int seed)
        {
            var w = ZWorld.Create(Settings, Flat, seed);
            var keeper = w.CreateRobotGroup("grunt", 1400, 1400, Team.Red);
            foreach (var m in Squad(keeper)) { m.MoveSpeed = 0; m.RealMoveSpeed = 0; }
            w.Events.Clear();
            return w;
        }

        static ZObject Place(ZWorld w, string t, int cx, int cy, Team team) =>
            ZBuildLists.IsRobot(t) ? w.CreateRobotGroup(t, cx - 8, cy - 8, team)
                                   : w.CreateObject(ZBuildLists.IsCannon(t) ? "cannon" : "vehicle", t, cx - 16, cy - 16, team);

        static IEnumerable<ZObject> Squad(ZObject o) { yield return o; foreach (var m in o.Minions) yield return m; }

        static void Order(ZWorld w, ZObject a, ZObject t) =>
            w.SendWaypoints(a.Owner, a, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = t.Id, x = t.CenterX, y = t.CenterY } });

        static void Run(ZWorld w, double until) { while (w.Time < until) { w.Tick(ZConst.TICK); w.Events.Clear(); } }

        /// <summary>The calm time after which every vehicle of a resting type rests (the longest spread + 1 s).</summary>
        static double RestBy => ZWorld.IdleRestSeconds + ZWorld.IdleRestSpreadSeconds + 1.0;

        static void Line(StringBuilder sb, bool ok, string what) => sb.AppendLine((ok ? "OK   " : "FAIL ") + what);

        /// <summary>Part 1: the state rules.</summary>
        static void Checks(StringBuilder sb, double restBy)
        {
            ZWorld.SnipeDeviation = false;
            var w = NewWorld(1); var t = Place(w, "light", CX, CY, Team.Blue);
            Run(w, restBy + 30);
            Line(sb, !t.Resting && w.RestsStarted == 0, $"Zod exact: no rest after {w.Time:0} s of calm (resting {t.Resting}, rests begun {w.RestsStarted}, can be sniped {t.CanBeSniped()})");
            ZWorld.SnipeDeviation = true;
            foreach (var type in new[] { "light", "medium", "heavy", "crane", "missile_launcher", "jeep", "apc" })
            {
                w = NewWorld(2); t = Place(w, type, CX, CY, Team.Blue);
                Run(w, restBy);
                bool want = ZWorld.IdleRestTypes.Contains(type), wantSnipe = type == "light" || type == "medium" || type == "heavy" || type == "jeep";
                Line(sb, t.Resting == want && t.CanBeSniped() == wantSnipe, $"{type}: resting {t.Resting} after {w.Time:0} s of calm (since {t.RestingSince:0.0} s), can be sniped {t.CanBeSniped()}, Zod lid open {t.LidOpen}");
            }
            w = NewWorld(3); t = Place(w, "light", CX, CY, Team.Blue);
            Run(w, restBy);
            bool before = t.Resting;
            w.SendWaypoints(Team.Blue, t, new List<ZWaypoint> { ZWaypoint.Move(CX + 64, CY, true) });
            w.Tick(ZConst.TICK); w.Events.Clear();
            Line(sb, before && !t.Resting, $"an order ends the rest on the next tick (resting before {before}, after {t.Resting})");

            w = NewWorld(4); t = Place(w, "light", CX, CY, Team.Blue);
            Run(w, restBy);
            before = t.Resting; double hitAt = w.Time;
            t.DamageHealth(1);
            w.Tick(ZConst.TICK); w.Events.Clear();
            bool held = t.Resting || ZWorld.IdleRestHitWakeSeconds < ZConst.TICK;
            Run(w, hitAt + ZWorld.IdleRestHitWakeSeconds + 0.05);
            Line(sb, before && held && !t.Resting, $"a hit ends the rest {ZWorld.IdleRestHitWakeSeconds:0.00} s later (resting before {before}, one tick after the hit {held}, at the end {t.Resting})");

            w = NewWorld(5); t = Place(w, "light", CX, CY, Team.Blue); t.Veteran = true;
            ZWorld.VeteranDeviation = true;
            Run(w, restBy);
            Line(sb, !t.Resting, $"a veteran crew never rests (resting {t.Resting})");
        }

        /// <summary>Part 2: hits on the target and the driver hits among them (the share is the snipe chance in use).</summary>
        static (long hits, long driver) Share(string attacker, string target, bool rest, double restBy)
        {
            long hits = 0, driver = 0;
            double keepS = ZWorld.IdleRestSeconds, keepW = ZWorld.IdleRestWakeSeconds, keepH = ZWorld.IdleRestHitWakeSeconds;
            try
            {
                if (rest) { ZWorld.IdleRestWakeSeconds = Never; ZWorld.IdleRestHitWakeSeconds = Never; } else ZWorld.IdleRestSeconds = Never;   // kept resting / never resting
                var w = NewWorld(7);
                var t = Place(w, target, CX + 80, CY, Team.Blue);
                t.Damage = 0; t.MoveSpeed = 0; t.RealMoveSpeed = 0; t.MaxHealth = t.Health = Big; t.Drivers[0] = Big;
                Run(w, restBy);
                var a = Place(w, attacker, CX, CY, Team.Red);
                foreach (var m in Squad(a)) m.MaxHealth = m.Health = Big;
                Order(w, a, t);
                double end = w.Time + ShareSeconds;
                while (w.Time < end)
                {
                    if (!rest && t.HasLid && t.Drivers.Count > 0) t.LidOpen = true;          // only the roll is counted here
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events) if (e.type == ZEventType.Hitscan && e.otherId == t.Id && e.flag) { hits++; if (e.flag2) driver++; }
                    w.Events.Clear();
                }
            }
            finally { ZWorld.IdleRestSeconds = keepS; ZWorld.IdleRestWakeSeconds = keepW; ZWorld.IdleRestHitWakeSeconds = keepH; }
            return (hits, driver);
        }

        struct Tally { public int sniped, hull, lost, none, rested; public long restHits, restDriver; public double time; }

        /// <summary>Part 3: ambushes on a target that stood calm for restBy s; rest = false holds the rest off (the control).</summary>
        static Tally Fight(string attacker, string target, int gap, bool rest, double restBy, int fights)
        {
            var r = new Tally();
            double keepS = ZWorld.IdleRestSeconds;
            try
            {
                if (!rest) ZWorld.IdleRestSeconds = Never;
                for (int seed = 1; seed <= fights; seed++)
                {
                    var w = NewWorld(100 + seed);
                    var t = Place(w, target, CX + gap, CY, Team.Blue); t.MoveSpeed = 0; t.RealMoveSpeed = 0;
                    Run(w, restBy);
                    if (t.Resting) r.rested++;
                    double t0 = w.Time;
                    var a = Place(w, attacker, CX, CY, Team.Red);
                    var squad = Squad(a).ToList();
                    Order(w, a, t);
                    int outcome = 3;
                    while (w.Time - t0 < FightCap && outcome == 3)
                    {
                        bool resting = t.Resting;
                        w.Tick(ZConst.TICK);
                        foreach (var e in w.Events)
                        {
                            if (resting && e.type == ZEventType.Hitscan && e.otherId == t.Id && e.flag) { r.restHits++; if (e.flag2) r.restDriver++; }
                            if (e.objId != t.Id || outcome != 3) continue;
                            if (e.type == ZEventType.Sniped) outcome = 0;
                            else if (e.type == ZEventType.Death) outcome = 1;
                        }
                        w.Events.Clear();
                        if (outcome != 3) break;
                        bool lost = a.Kind == ZKind.Robot ? squad.All(m => m.Destroyed) : a.Destroyed || a.Owner == Team.Neutral;
                        if (lost) outcome = 2;
                    }
                    if (outcome == 0) { r.sniped++; r.time += w.Time - t0; }
                    else if (outcome == 1) r.hull++;
                    else if (outcome == 2) r.lost++;
                    else r.none++;
                }
            }
            finally { ZWorld.IdleRestSeconds = keepS; }
            return r;
        }

        /// <summary>The deviations list's probe: ambushes from 100 px, driver sniped on the resting target and on the target with the
        /// rest held off.</summary>
        public static (int rest, int fresh, int n) Probe(string attacker, string target, int n)
        {
            bool snipe = ZWorld.SnipeDeviation, duel = ZWorld.DuelDeviation;
            try
            {
                ZWorld.SnipeDeviation = true; ZWorld.DuelDeviation = false;
                double restBy = RestBy;
                return (Fight(attacker, target, 100, true, restBy, n).sniped, Fight(attacker, target, 100, false, restBy, n).sniped, n);
            }
            finally { ZWorld.SnipeDeviation = snipe; ZWorld.DuelDeviation = duel; }
        }

        public static string Report()
        {
            bool snipe = ZWorld.SnipeDeviation, duel = ZWorld.DuelDeviation, vet = ZWorld.VeteranDeviation;
            var sb = new StringBuilder();
            try
            {
                ZWorld.SnipeDeviation = true; ZWorld.DuelDeviation = false;
                double restBy = RestBy;
                sb.AppendLine($"[OpenZ] idle rest count (DV75): calm {ZWorld.IdleRestSeconds:0} s + up to {ZWorld.IdleRestSpreadSeconds:0} s, wake {ZWorld.IdleRestWakeSeconds:0.0} s after an enemy is seen, {ZWorld.IdleRestHitWakeSeconds:0.00} s after a hit, snipe chance x{ZWorld.RestSnipeFactor:0.00} (cap {ZWorld.RestSnipeCap:0.00})");
                Checks(sb, restBy);
                ZWorld.SnipeDeviation = true; ZWorld.VeteranDeviation = vet;
                sb.AppendLine($"share, {ShareSeconds:0} s per row, the target cannot die and does not shoot");
                sb.AppendLine("attacker -> target | RESTING: hits | driver hits | share | LID HELD OPEN, NO REST: hits | driver hits | share | ratio of the shares");
                foreach (var (attacker, target) in Pairs)
                {
                    var (h1, d1) = Share(attacker, target, true, restBy);
                    var (h0, d0) = Share(attacker, target, false, restBy);
                    double s1 = h1 > 0 ? (double)d1 / h1 : 0.0, s0 = h0 > 0 ? (double)d0 / h0 : 0.0;
                    sb.AppendLine($"{attacker} -> {target} | {h1} | {d1} | {s1:0.000} | {h0} | {d0} | {s0:0.000} | {(s0 > 0 ? s1 / s0 : 0.0):0.00}");
                }
                sb.AppendLine($"ambushes, {Fights} per row, all mortal, the target shoots back, cap {FightCap:0} s after the attacker appears");
                sb.AppendLine("attacker -> target | gap px | RESTING: resting at the start | driver sniped | hull first | attackers lost | no result | mean s to the snipe | hits while resting | driver hits among them | NO REST: driver sniped | hull first | attackers lost | no result | mean s to the snipe");
                foreach (var (attacker, target) in Pairs)
                    foreach (int gap in Gaps)
                    {
                        var a = Fight(attacker, target, gap, true, restBy, Fights);
                        var b = Fight(attacker, target, gap, false, restBy, Fights);
                        double ma = a.sniped > 0 ? a.time / a.sniped : 0.0, mb = b.sniped > 0 ? b.time / b.sniped : 0.0;
                        sb.AppendLine($"{attacker} -> {target} | {gap} | {a.rested}/{Fights} | {a.sniped}/{Fights} | {a.hull}/{Fights} | {a.lost}/{Fights} | {a.none}/{Fights} | {ma:0.0} | {a.restHits} | {a.restDriver} | {b.sniped}/{Fights} | {b.hull}/{Fights} | {b.lost}/{Fights} | {b.none}/{Fights} | {mb:0.0}");
                    }
            }
            finally { ZWorld.SnipeDeviation = snipe; ZWorld.DuelDeviation = duel; ZWorld.VeteranDeviation = vet; }
            return sb.ToString();
        }
    }
}
