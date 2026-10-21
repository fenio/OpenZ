using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of kit b2_electrobot: one red Electrobot (the player's) on flat open ground against blue attackers, 20 runs
    /// per row, each row with the rule off (the game before version 1.1) and on. A run ends when he dies, when every attacker is
    /// dead, or after 90 s. Counted per run: bolts fired; "charged and idle" = seconds with an enemy inside his 144 px while his
    /// gun is charged and not winding up (time he could shoot and does not); seconds walking a dodge; reversals (a step pointing
    /// more than 90 degrees away from the step before it).
    /// </summary>
    public static class OzV11ElectrobotCount
    {
        public const int Runs = 20;
        public const double Cap = 90;
        const int CX = 640, CY = 640;

        sealed class Stat
        {
            public int runs, won, lost, none; public long ticks, inRange, chargedIdle, dodge, moving, bolts, charges, fizzles, reversals, firstBoltTicks, firstBoltN;
            public double time;
        }

        delegate void Setup(ZWorld w, ZObject e, List<ZObject> enemies);

        /// <summary>chase = the row's only enemy keeps leaving his range: there he walks after it with a charged gun.</summary>
        static (string name, Setup setup, bool chase)[] Rows() => new (string, Setup, bool)[]
        {
            ("1 grunt squad (3 grunts) attacks him", (w, e, en) => Send(w, e, en, ("grunt", 300, 0)), false),
            ("3 grunt squads attack from three sides", (w, e, en) => Send(w, e, en, ("grunt", 300, 0), ("grunt", -260, 150), ("grunt", 0, -300)), false),
            ("heavy tank attacks (its range = his)", (w, e, en) => Send(w, e, en, ("heavy", 300, 0)), false),
            ("Mobile Missile attacks (out-ranges him)", (w, e, en) => Send(w, e, en, ("missile_launcher", 300, 0)), false),
            ("howitzer 190 px away (out-ranges him)", (w, e, en) => en.Add(OzV11Kit.Place(w, "howitzer", CX + 190, CY, Team.Blue)), false),
            ("howitzer 190 px away + 1 grunt squad attacks", (w, e, en) => { en.Add(OzV11Kit.Place(w, "howitzer", CX + 190, CY, Team.Blue)); Send(w, e, en, ("grunt", -300, 0)); }, false),
            ("missile cannon 150 px away + 1 grunt squad attacks", (w, e, en) => { en.Add(OzV11Kit.Place(w, "missile_cannon", CX + 150, CY, Team.Blue)); Send(w, e, en, ("grunt", -300, 0)); }, false),
            ("heavy tank + light tank + 1 grunt squad attack", (w, e, en) => Send(w, e, en, ("heavy", 300, -40), ("light", -300, 30), ("grunt", 0, 300)), false),
            ("enemies drive past 170 px away (a jeep and a grunt squad on move orders)", (w, e, en) =>
            {
                foreach (var (ty, dy) in new[] { ("jeep", -170), ("grunt", 170) })
                {
                    var g = OzV11Kit.Place(w, ty, CX - 400, CY + dy, Team.Blue); en.Add(g);
                    w.SendWaypoints(Team.Blue, g, new[] { new ZWaypoint { mode = WpMode.Move, refId = -1, x = CX + 500, y = CY + dy, attackTo = false, playerGiven = true } });
                }
            }, false),
            ("a jeep drives up and down a line 182 px away, 8 legs (move orders, it never attacks)", (w, e, en) =>
            {
                var j = OzV11Kit.Place(w, "jeep", CX + 182, CY - 330, Team.Blue); en.Add(j);
                var legs = new List<ZWaypoint>();
                for (int k = 0; k < 8; k++) legs.Add(new ZWaypoint { mode = WpMode.Move, refId = -1, x = CX + 182, y = CY + (k % 2 == 0 ? 330 : -330), attackTo = false, playerGiven = true });
                w.SendWaypoints(Team.Blue, j, legs);
            }, true),
            ("his order: attack-move past 2 idle grunt squads", (w, e, en) => { en.Add(OzV11Kit.Place(w, "grunt", CX + 200, CY - 100, Team.Blue)); en.Add(OzV11Kit.Place(w, "grunt", CX + 260, CY + 110, Team.Blue)); OzV11Kit.PlayerMove(w, e, CX + 520, CY); }, false),
            ("his order: attack a gun 420 px away, a Tough squad stands by the way", (w, e, en) => { var c = OzV11Kit.Place(w, "gun", CX + 420, CY, Team.Blue); en.Add(c); en.Add(OzV11Kit.Place(w, "tough", CX + 150, CY + 130, Team.Blue)); OzV11Kit.PlayerAttack(w, e, c); }, false),
        };

        static void Send(ZWorld w, ZObject e, List<ZObject> en, params (string type, int dx, int dy)[] units)
        {
            foreach (var (type, dx, dy) in units) { var g = OzV11Kit.Place(w, type, CX + dx, CY + dy, Team.Blue); en.Add(g); OzV11Kit.CpuAttack(w, g, e); }
        }

        /// <summary>The whole check. settings = default_settings. Returns the report.</summary>
        public static string Run(string settings, int runs = Runs)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"One red Electrobot on open ground, {runs} runs per row, cap {Cap:0} s, the calm start (DV69) switched off for the check.");
            sb.AppendLine("row | rule | he wins / dies / no end | mean s | bolts per run | bolts per minute of the run | charged and idle with an enemy in range, s per run | s walking a dodge per run | s moving per run | reversals per run | windups | fizzles | s from the first enemy in range to the first bolt");
            bool had = OzV11Kit.TrySet(typeof(ZWorld), "ElectroSteadyDeviation", true);
            ZWorld.CalmStartDeviation = false;                                       // the fights start at once: not inside the 20 s calm start (DV69), which holds every self-started walk of the player's units
            string map = OzV11Kit.Plain(80, 80);
            double idleOff = 0, idleOn = 0, dodgeOn = 0, dodgeOff = 0, worstIdleOn = 0, chaseIdleOn = 0; long boltsOff = 0, boltsOn = 0; int fewerBolts = 0, fewerOther = 0, n = 0; double revOn = 0, revOff = 0; var fewerRun = new List<string>();
            foreach (var (name, setup, chase) in Rows())
            {
                Stat off = null, on = null;
                foreach (bool rule in new[] { false, true })
                {
                    if (rule && !had) continue;
                    OzV11Kit.TrySet(typeof(ZWorld), "ElectroSteadyDeviation", rule);
                    var st = new Stat();
                    for (int seed = 1; seed <= runs; seed++) One(settings, map, seed, setup, st);
                    if (rule) on = st; else off = st;
                    double r = Math.Max(1, st.runs);
                    sb.AppendLine($"{name} | {(rule ? "on" : "off")} | {st.won} / {st.lost} / {st.none} | {st.time / r:0.0} | {st.bolts / r:0.00} | {60.0 * st.bolts / Math.Max(1.0, st.time):0.0} | {st.chargedIdle * ZConst.TICK / r:0.00} | {st.dodge * ZConst.TICK / r:0.00} | {st.moving * ZConst.TICK / r:0.00} | {st.reversals / r:0.00} | {st.charges / r:0.00} | {st.fizzles / r:0.00} | {(st.firstBoltN > 0 ? (st.firstBoltTicks * ZConst.TICK / st.firstBoltN).ToString("0.00") : "-")}");
                }
                n++;
                idleOff += off.chargedIdle * ZConst.TICK / runs; dodgeOff += off.dodge * ZConst.TICK / runs; boltsOff += off.bolts; revOff += (double)off.reversals / runs;
                if (on == null) continue;
                double io = on.chargedIdle * ZConst.TICK / runs;
                idleOn += io; if (!chase) worstIdleOn = Math.Max(worstIdleOn, io); else chaseIdleOn = Math.Max(chaseIdleOn, io); dodgeOn += on.dodge * ZConst.TICK / runs; boltsOn += on.bolts; revOn += (double)on.reversals / runs;
                if (on.bolts / Math.Max(1.0, on.time) < off.bolts / Math.Max(1.0, off.time) - 1e-9) fewerBolts++;
                if (on.bolts < off.bolts)
                {   // fewer bolts in the whole run: accepted only where a howitzer out-ranges him (before, he lived on by dodging and got a bolt off now and then)
                    fewerRun.Add($"{name} {(double)off.bolts / runs:0.00} -> {(double)on.bolts / runs:0.00} bolts per run, he dies in {off.lost} -> {on.lost} of {runs} runs after {off.time / runs:0.0} -> {on.time / runs:0.0} s");
                    if (!name.StartsWith("howitzer")) fewerOther++;
                }
            }
            OzV11Kit.TrySet(typeof(ZWorld), "ElectroSteadyDeviation", true);
            if (!had) { sb.AppendLine("The rule is not installed (ZWorld.ElectroSteadyDeviation not found): only the 'off' rows were run."); return sb.ToString(); }
            sb.AppendLine(OzV11Kit.Verdict(dodgeOn == 0, $"he never walks a dodge with the rule on (s per run, summed over the rows: off {dodgeOff:0.0}, on {dodgeOn:0.0})"));
            sb.AppendLine(OzV11Kit.Verdict(worstIdleOn <= 1.0 && chaseIdleOn <= 5.0, $"charged and idle with an enemy in range: at most 1.0 s per run in every row with the rule on (worst row {worstIdleOn:0.00} s), at most 5.0 s in the row whose only enemy keeps leaving his range, where he walks after it ({chaseIdleOn:0.00} s); summed over the rows: off {idleOff:0.0} s, on {idleOn:0.0} s"));
            sb.AppendLine(OzV11Kit.Verdict(fewerBolts == 0 && fewerOther == 0 && boltsOn > boltsOff, $"he fires more: bolts over all rows off {boltsOff}, on {boltsOn}; rows in which he fires fewer bolts per second of the run than before: {fewerBolts}; " +
                $"rows in which he fires fewer bolts in the whole run: {fewerRun.Count}{(fewerRun.Count > 0 ? " (" + string.Join("; ", fewerRun) + ")" : "")}, of them outside the howitzer rows, where the shorter life is accepted: {fewerOther}"));
            sb.AppendLine(OzV11Kit.Verdict(revOn <= revOff, $"no more turning about than before (reversals per run, summed over the rows: off {revOff:0.0}, on {revOn:0.0})"));
            return sb.ToString();
        }

        static void One(string settings, string map, int seed, Setup setup, Stat st)
        {
            var w = OzV11Kit.World(settings, map, seed);
            var e = OzV11Kit.Place(w, "electrobot", CX, CY, Team.Red);
            var en = new List<ZObject>();
            setup(w, e, en);
            var all = new List<ZObject>(); foreach (var x in en) all.AddRange(OzV11Kit.Squad(x));
            var m = new OzV11Kit.Motion(e);
            long firstIn = -1; bool gotBolt = false; int outcome = 2;
            while (w.Time < Cap)
            {
                w.Tick(ZConst.TICK);
                foreach (var ev in w.Events)
                {
                    if (ev.objId != e.Id) continue;
                    if (ev.type == ZEventType.ElectroBolt) { st.bolts++; if (!gotBolt && firstIn >= 0) { gotBolt = true; st.firstBoltTicks += w.TickIndex - firstIn; st.firstBoltN++; } }
                    else if (ev.type == ZEventType.ElectroFizzle) st.fizzles++;
                    else if (ev.type == ZEventType.ElectroCharge) st.charges++;
                }
                w.Events.Clear();
                if (!OzV11Kit.Alive(e)) { outcome = 1; break; }
                if (all.All(x => !OzV11Kit.Alive(x) || x.Owner != Team.Blue)) { outcome = 0; break; }
                st.ticks++;
                bool moved = m.Step(e, w.Time);
                if (moved) { st.moving++; if (e.Waypoints.Count > 0 && e.Waypoints[0].mode == WpMode.Dodge) st.dodge++; }
                bool inRange = all.Any(x => OzV11Kit.Alive(x) && x.Owner == Team.Blue && OzV11Kit.Dist(e, x) <= e.AttackRadius);
                if (!inRange) continue;
                if (firstIn < 0) firstIn = w.TickIndex;
                st.inRange++;
                if (e.ElPhase == ElectroPhase.Charged) st.chargedIdle++;
            }
            st.runs++; st.time += w.Time; st.reversals += m.Reversals;
            if (outcome == 0) st.won++; else if (outcome == 1) st.lost++; else st.none++;
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Count Electrobot Fights (b2)")]
        static void Menu() => OzV11Kit.RunMenu("b2 electrobot", () => Run(OzV11Kit.SettingsText));
#endif
    }
}
