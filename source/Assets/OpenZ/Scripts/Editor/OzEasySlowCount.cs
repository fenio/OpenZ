using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// (ZWorld.Create + Tick, no scene, nothing saved; public members only) on levels 2, 16 and 17 with the game's rules and enemy strike
    /// groups on; red = the strike check's stand-in player (OzV11StrikeCount.StandIn: four held territories, a guarded gate and a large
    /// gathering kept at strength), blue = the computer. Every match is played twice, with the switch ZWorld.EasyCpuSlowerOn on and off:
    ///  E1 the first order round: on = 1.3 x off (59.8 s against 46 s), never before 58.5 s;
    ///  E2 production is not slowed: the first production change at the same moment on and off (45 s start, 2 s clock);
    ///  E3 every gap between two order rounds of the same flag tier: on = its tick count x 2.6 s, off = the same count x 2 s (exactly 1.3 x);
    ///     and the rounds of the first 20 minutes: about 30 % fewer (off / on 1.15 to 1.5);
    ///     sooner than its window's opening + 1.3 x the time it took to get ready, within the rule's 1 s step) and nobody joined it then;
    ///     the members' mean wait at the fort, on against off;
    ///  E6 MEDIUM, HARD and a computer match: the whole world's fingerprint (every object's place, health, owner and orders, once a second
    ///     and at the end) with the switch on EQUALS the one with it off (the rule never reaches them);
    ///  E7 deterministic: the same EASY match twice, the same fingerprint;
    ///  E8 the switches are back as they were after the check.
    /// </summary>
    public static class OzEasySlowCount
    {
        public static int Seeds = 2;
        public static double Minutes = 20;
        public static readonly string[] Levels = { "L02", "L16", "L17" };

        public sealed class Run
        {
            public string level, kind; public int seed; public bool on;
            public double firstRound = -1, firstOrder = -1, firstBuild = -1, end; public bool over; public string winner = "";
            public int rounds, rounds20, orders, orders10, orders20, finish, buildChanges, planLooks, holds;
            public ulong fp;
            public readonly List<(double gap, double d, bool stable)> gaps = new List<(double, double, bool)>();
            public readonly List<double> sweeps = new List<double>(), windows = new List<double>(), formed = new List<double>(), waits = new List<double>();
            public readonly List<(int group, double opened, double ready, double left)> holdLog = new List<(int, double, double, double)>();
            public int joinedLate; public string error;
        }

        static string F(double v, string f = "0.0") => v.ToString(f, CultureInfo.InvariantCulture);
        static string Clock(double s) => s < 0 ? "-" : $"{(int)(s / 60)}:{(int)(s % 60):00}";

        /// <summary>One match. kind: easy (blue Easy, red's tech +5, L17 start crews), medium (blue Zod computer, red's tech +2), hard (blue Zod
        /// computer, the forts' door missile cannons), cpu (computer vs computer, both Zod). on = ZWorld.EasyCpuSlowerOn.</summary>
        public static Run Match(string settings, string map, string level, int seed, string kind, bool on, double seconds)
        {
            var R = new Run { level = level, seed = seed, kind = kind, on = on };
            using (OzV11StrikeCount.Game(true))
            {
                OzV11Kit.TrySet(typeof(ZWorld), "BotHeavyDeviation", true);                             // the computer's unit mix, as in a game
                ZWorld.EasyCpuSlowerOn = on;
                ZWorld.L17CrewsDeviation = kind == "easy";
                ZWorld.FortDoorGunDeviation = kind == "hard";
                try
                {
                    var w = ZWorld.Create(settings, map, seed, level == "L17");
                    var blue = new ZBot(w, Team.Blue, seed + 101) { Difficulty = kind == "easy" ? ZBotDifficulty.Easy : ZBotDifficulty.Normal };
                    w.Bots.Add(blue);
                    OzV11StrikeCount.StandInState stand = null;
                    if (kind == "cpu") w.Bots.Add(new ZBot(w, Team.Red, seed));
                    else
                    {
                        w.TechBonus[(int)Team.Red] = kind == "easy" ? 5 : kind == "medium" ? 2 : 0;
                        stand = OzV11StrikeCount.StandIn(w);
                    }
                    w.Events.Clear();
                    ulong fp = 1469598103934665603UL; double nextFold = 1.0;
                    int lastRounds = 0, lastSweeps = 0; double lastRoundAt = -1, lastD = -1; bool stable = true;
                    while (w.Time < seconds && !w.GameOver)
                    {
                        w.Tick(ZConst.TICK); w.Events.Clear();
                        if (stand != null) OzV11StrikeCount.StandInKeep(w, stand);
                        if (R.firstOrder < 0 && blue.Orders > 0) R.firstOrder = w.Time;
                        if (R.firstBuild < 0 && blue.BuildChanges > 0) R.firstBuild = w.Time;
                        if (blue.Rounds != lastRounds)
                        {
                            if (R.firstRound < 0) R.firstRound = w.Time;
                            if (lastRoundAt >= 0) R.gaps.Add((w.Time - lastRoundAt, blue.LastDelay, stable && lastD == blue.LastDelay));
                            lastRounds = blue.Rounds; lastRoundAt = w.Time; lastD = blue.LastDelay; stable = true;
                        }
                        else if (lastRoundAt >= 0 && blue.LastDelay != lastD) stable = false;                // the flag tier changed while it waited
                        if (w.IdleSweepLog.Count != lastSweeps) { lastSweeps = w.IdleSweepLog.Count; R.sweeps.Add(w.Time); }
                        if (w.Time >= 600 - 1e-6 && R.orders10 == 0) R.orders10 = Math.Max(1, blue.Orders);
                        if (w.Time >= 1200 - 1e-6 && R.orders20 == 0) { R.orders20 = Math.Max(1, blue.Orders); R.rounds20 = blue.Rounds; }
                        if (w.Time >= nextFold) { nextFold += 1.0; fp = OzV11StrikeCount.Fold(fp, w); }
                    }
                    R.fp = OzV11StrikeCount.Fold(fp, w);
                    R.end = w.Time; R.over = w.GameOver; R.winner = w.GameOver ? w.Winner.ToString() : "";
                    R.rounds = blue.Rounds; R.orders = blue.Orders; R.finish = blue.FinishOrders; R.buildChanges = blue.BuildChanges;
                    if (R.rounds20 == 0) R.rounds20 = blue.Rounds;
                    if (R.orders20 == 0) R.orders20 = blue.Orders;
                    R.planLooks = blue.EasyPlanLooks; R.holds = blue.EasyStrikeHolds; R.holdLog.AddRange(blue.EasyStrikeHoldLog);
                    var joined = new Dictionary<int, List<double>>(); var formedAt = new Dictionary<int, double>();
                    foreach (var e in blue.StrikeEvents)
                    {
                        if (e.kind == ZBot.StrikeEventKind.WindowOpened) R.windows.Add(e.time);
                        if (e.kind == ZBot.StrikeEventKind.Formed) { R.formed.Add(e.time); formedAt[e.group] = e.time; }
                        if (e.kind == ZBot.StrikeEventKind.Joined) { if (!joined.TryGetValue(e.group, out var l)) joined[e.group] = l = new List<double>(); l.Add(e.time); }
                    }
                    foreach (var kv in formedAt) if (joined.TryGetValue(kv.Key, out var l)) foreach (var t in l) R.waits.Add(kv.Value - t);
                    foreach (var h in R.holdLog) if (joined.TryGetValue(h.group, out var l)) R.joinedLate += l.Count(t => t > h.ready + 1e-6);
                }
                catch (Exception ex) { R.error = ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace; }
            }
            return R;
        }

        static string Line(Run r) =>
            $"    {r.level} seed {r.seed} {r.kind.ToUpperInvariant()} switch {(r.on ? "on " : "off")}: {(r.over ? "won by " + r.winner + " at " + Clock(r.end) : "not over at " + Clock(r.end))}; first order round {F(r.firstRound, "0.00")} s, first order {F(r.firstOrder, "0.00")} s, first production change {F(r.firstBuild, "0.00")} s; " +
            $"rounds {r.rounds} (first 20 min {r.rounds20}), orders {r.orders} (10 min {r.orders10}, 20 min {r.orders20}), fort finish {r.finish}, production changes {r.buildChanges}; " +
            $"strike windows {string.Join(" ", r.windows.Select(Clock))}{(r.windows.Count == 0 ? "none" : "")}, groups left {string.Join(" ", r.formed.Select(Clock))}{(r.formed.Count == 0 ? "none" : "")}, mean wait at the fort {(r.waits.Count == 0 ? "-" : F(r.waits.Average()) + " s of " + r.waits.Count)}{(r.holds > 0 ? ", stood longer " + r.holds + "x" : "")}; " +
            $"idle sweeps {string.Join(" ", r.sweeps.Select(Clock))}{(r.sweeps.Count == 0 ? "none" : "")}; fingerprint {r.fp:x16}{(r.error != null ? "; ERROR " + r.error.Split('\n')[0] : "")}";

        /// <summary>Expected gap of two rounds of the same tier: the round delay (tier delay x 2.5, x 1.3 when on) rounded up to the clock.</summary>
        static double Expected(double d, bool on)
        {
            double tick = ZBot.AI_INTERVAL * ZBot.EASY_AI_INTERVAL_SCALE * (on ? ZWorld.EasyDecisionSlow : 1.0);
            double delay = d * ZBot.EASY_ORDER_DELAY_SCALE * (on ? ZWorld.EasyDecisionSlow : 1.0);
            return Math.Ceiling(delay / tick - 1e-6) * tick;
        }

        public static string Check(string settings, Func<string, string> map, int seeds = -1, double minutes = -1, Action<string> progress = null, string[] levels = null)
        {
            var Levels = levels ?? OzEasySlowCount.Levels;
            if (seeds < 1) seeds = Seeds;
            if (minutes <= 0) minutes = Minutes;
            double secs = minutes * 60; bool sw0 = ZWorld.EasyCpuSlowerOn; double ev0 = ZWorld.IdleSweepEvery, st0 = ZWorld.IdleSweepStillSec;
            var sb = new StringBuilder(); var v = new List<string>();
            sb.AppendLine($"K434 EASY COMPUTER, BIG DECISIONS 30 % SLOWER (DV127): x{F(ZWorld.EasyDecisionSlow, "0.0#")} on every strategic timer of the Easy computer; {Levels.Length} levels x {seeds} seeds, each match {F(minutes, "0")} min at most, switch on and off; red = the strike check's stand-in player.");
            sb.AppendLine($"The Easy numbers: order look every {F(ZBot.AI_INTERVAL * ZBot.EASY_AI_INTERVAL_SCALE * ZWorld.EasyDecisionSlow, "0.0")} s (was {F(ZBot.AI_INTERVAL * ZBot.EASY_AI_INTERVAL_SCALE, "0.0")} s), first order no sooner than {F(ZBot.EASY_START_DELAY * ZWorld.EasyDecisionSlow, "0.0")} s (was {F(ZBot.EASY_START_DELAY, "0")} s), round delays x{F(ZBot.EASY_ORDER_DELAY_SCALE * ZWorld.EasyDecisionSlow, "0.00")} (was x{F(ZBot.EASY_ORDER_DELAY_SCALE, "0.0")}); production look every {F(ZBot.AI_INTERVAL * ZBot.EASY_AI_INTERVAL_SCALE, "0.0")} s from {F(ZBot.EASY_START_DELAY, "0")} s (unchanged); strike windows from {Clock(ZWorld.StrikeStartSec * ZWorld.EasyDecisionSlow)} (was {Clock(ZWorld.StrikeStartSec)}), {Clock(ZWorld.StrikeGapSec * ZWorld.EasyDecisionSlow)} apart (was {Clock(ZWorld.StrikeGapSec)}); idle sweep every {Clock(ZWorld.IdleSweepEvery * ZWorld.EasyDecisionSlow)} (was {Clock(ZWorld.IdleSweepEvery)}).");
            var easy = new List<(Run on, Run off)>(); var same = new List<(Run on, Run off)>(); Run again = null;
            {
                foreach (var lv in Levels)
                {
                    string m = map(lv);
                    sb.AppendLine(); sb.AppendLine($"{lv}");
                    for (int seed = 1; seed <= seeds; seed++)
                    {
                        var a = Match(settings, m, lv, seed, "easy", true, secs); var b = Match(settings, m, lv, seed, "easy", false, secs);
                        easy.Add((a, b)); sb.AppendLine(Line(a)); sb.AppendLine(Line(b)); progress?.Invoke(Line(a).Trim());
                    }
                    if (lv == Levels[0]) { again = Match(settings, m, lv, 1, "easy", true, secs); sb.AppendLine(Line(again) + "  (again)"); }
                    foreach (var kind in new[] { "medium", "hard", "cpu" })
                    {
                        var a = Match(settings, m, lv, 1, kind, true, secs); var b = Match(settings, m, lv, 1, kind, false, secs);
                        same.Add((a, b)); sb.AppendLine(Line(a)); sb.AppendLine(Line(b)); progress?.Invoke(Line(a).Trim());
                    }
                }
            }
            sb.AppendLine();
            var all = easy.SelectMany(p => new[] { p.on, p.off }).Concat(same.SelectMany(p => new[] { p.on, p.off })).ToList();
            if (again != null) all.Add(again);
            v.Add(OzV11Kit.Verdict(all.All(r => r.error == null), $"no exception in {all.Count} matches"));
            // E1
            var e1 = easy.Select(p => (p.on.firstRound, p.off.firstRound)).ToList();
            v.Add(OzV11Kit.Verdict(e1.All(x => x.Item1 >= ZBot.EASY_START_DELAY * ZWorld.EasyDecisionSlow - 1e-6 && x.Item2 > 0 && Math.Abs(x.Item1 - x.Item2 * ZWorld.EasyDecisionSlow) < 0.05),
                $"E1 the first order round: on {string.Join(", ", e1.Select(x => F(x.Item1, "0.00")))} s against off {string.Join(", ", e1.Select(x => F(x.Item2, "0.00")))} s (on = 1.3 x off, never before {F(ZBot.EASY_START_DELAY * ZWorld.EasyDecisionSlow)} s)"));
            // E2
            v.Add(OzV11Kit.Verdict(easy.All(p => p.on.firstBuild > 0 && Math.Abs(p.on.firstBuild - p.off.firstBuild) < 1e-6 && p.on.firstBuild >= ZBot.EASY_START_DELAY - 1e-6),
                $"E2 production not slowed: the first production change on {string.Join(", ", easy.Select(p => F(p.on.firstBuild, "0.00")))} s = off {string.Join(", ", easy.Select(p => F(p.off.firstBuild, "0.00")))} s"));
            // E3
            int gOn = 0, gOnOk = 0, gOff = 0, gOffOk = 0; string bad = "";
            foreach (var p in easy)
            {
                foreach (var g in p.on.gaps.Where(x => x.stable)) { gOn++; if (Math.Abs(g.gap - Expected(g.d, true)) < 0.02) gOnOk++; else if (bad.Length < 300) bad += $" on {p.on.level}/{p.on.seed} {F(g.gap, "0.00")} s (tier {F(g.d, "0")}, want {F(Expected(g.d, true), "0.0")})"; }
                foreach (var g in p.off.gaps.Where(x => x.stable)) { gOff++; if (Math.Abs(g.gap - Expected(g.d, false)) < 0.02) gOffOk++; else if (bad.Length < 300) bad += $" off {p.off.level}/{p.off.seed} {F(g.gap, "0.00")} s (tier {F(g.d, "0")}, want {F(Expected(g.d, false), "0.0")})"; }
            }
            var tiers = new[] { 4.0, 5.0, 8.0, 12.0 };
            v.Add(OzV11Kit.Verdict(gOn > 0 && gOff > 0 && gOnOk == gOn && gOffOk == gOff,
                $"E3 every gap of two rounds in the same flag tier: on {gOnOk}/{gOn} exactly as the rule ({string.Join(" / ", tiers.Select(d => F(Expected(d, true), "0.0")))} s by tier), off {gOffOk}/{gOff} ({string.Join(" / ", tiers.Select(d => F(Expected(d, false), "0.0")))} s): exactly 1.3 x{(bad.Length > 0 ? "; NOT:" + bad : "")}"));
            double rOn = easy.Sum(p => p.on.rounds20), rOff = easy.Sum(p => p.off.rounds20);
            double meanOn = easy.SelectMany(p => p.on.gaps).Select(g => g.gap).DefaultIfEmpty(0).Average(), meanOff = easy.SelectMany(p => p.off.gaps).Select(g => g.gap).DefaultIfEmpty(0).Average();
            v.Add(OzV11Kit.Verdict(rOn > 0 && rOff / rOn >= 1.15 && rOff / rOn <= 1.5,
                $"E3 order rounds in the first 20 minutes (or to the end of a shorter match): on {rOn} against off {rOff} (off / on {F(rOff / Math.Max(1, rOn), "0.00")}, about 30 % fewer); mean gap {F(meanOn)} s against {F(meanOff)} s; orders given on {easy.Sum(p => p.on.orders20)} against off {easy.Sum(p => p.off.orders20)}"));
            // E4
            var wOn = easy.SelectMany(p => p.on.windows).ToList(); var wOff = easy.SelectMany(p => p.off.windows).ToList();
            double leastGapOn = double.MaxValue; foreach (var p in easy) for (int i = 1; i < p.on.formed.Count; i++) leastGapOn = Math.Min(leastGapOn, p.on.formed[i] - p.on.formed[i - 1]);
            var holds = easy.SelectMany(p => p.on.holdLog).ToList(); var left = holds.Where(h => h.left >= 0).ToList();
            int holdOk = left.Count(h => h.left - h.opened >= (h.ready - h.opened) * ZWorld.EasyDecisionSlow - 1.01);
            int formedOn = easy.Sum(p => p.on.formed.Count), late = easy.Sum(p => p.on.joinedLate);
            var waitOn = easy.SelectMany(p => p.on.waits).ToList(); var waitOff = easy.SelectMany(p => p.off.waits).ToList();
            v.Add(OzV11Kit.Verdict((wOn.Count == 0 || wOn.Min() >= ZWorld.StrikeStartSec * ZWorld.EasyDecisionSlow - 1e-6) && (wOff.Count == 0 || wOff.Min() >= ZWorld.StrikeStartSec - 1e-6) && leastGapOn >= ZWorld.StrikeGapSec * ZWorld.EasyDecisionSlow - 1e-6,
                $"E4 strike windows: on the earliest {(wOn.Count == 0 ? "-" : Clock(wOn.Min()))} of {wOn.Count} (never before {Clock(ZWorld.StrikeStartSec * ZWorld.EasyDecisionSlow)}), off the earliest {(wOff.Count == 0 ? "-" : Clock(wOff.Min()))} of {wOff.Count}; the least time between two groups on {(leastGapOn == double.MaxValue ? "- (no match had two)" : Clock(leastGapOn))} (at least {Clock(ZWorld.StrikeGapSec * ZWorld.EasyDecisionSlow)})"));
            v.Add(OzV11Kit.Verdict(formedOn == 0 || (holdOk == left.Count && late == 0 && left.Count == formedOn),
                $"E4 every Easy group stood its extra time: {holdOk}/{left.Count} left no sooner than opening + 1.3 x its time to get ready (groups left {formedOn}, stood longer {holds.Count}); joined after it was ready {late}; members' mean wait at the fort on {(waitOn.Count == 0 ? "-" : F(waitOn.Average()) + " s (" + waitOn.Count + ")")} against off {(waitOff.Count == 0 ? "-" : F(waitOff.Average()) + " s (" + waitOff.Count + ")")}"));
            // E5
            bool sweepOk = easy.All(p => p.on.sweeps.Select((t, i) => Math.Abs(t - ZWorld.IdleSweepEvery * ZWorld.EasyDecisionSlow * (i + 1)) < 0.02).All(x => x)
                                      && p.off.sweeps.Select((t, i) => Math.Abs(t - ZWorld.IdleSweepEvery * (i + 1)) < 0.02).All(x => x));
            v.Add(OzV11Kit.Verdict(sweepOk && easy.Any(p => p.on.sweeps.Count > 0),
                $"E5 the idle sweep: on at {string.Join(" ", easy.SelectMany(p => p.on.sweeps).Distinct().OrderBy(t => t).Select(Clock))}, off at {string.Join(" ", easy.SelectMany(p => p.off.sweeps).Distinct().OrderBy(t => t).Select(Clock))}"));
            // E6
            v.Add(OzV11Kit.Verdict(same.All(p => p.on.fp == p.off.fp && p.on.error == null),
                $"E6 MEDIUM / HARD / computer match untouched: the world's fingerprint switch on = off in {same.Count(p => p.on.fp == p.off.fp)}/{same.Count} matches ({string.Join(", ", same.Select(p => p.on.level + " " + p.on.kind + " " + (p.on.fp == p.off.fp ? "=" : "DIFFERS")))})"));
            // E7
            var first = easy.FirstOrDefault().on;
            v.Add(OzV11Kit.Verdict(again != null && first != null && again.fp == first.fp, $"E7 deterministic: {Levels[0]} seed 1 EASY on twice, fingerprints {first?.fp:x16} / {again?.fp:x16}"));
            // E8
            v.Add(OzV11Kit.Verdict(ZWorld.EasyCpuSlowerOn == sw0 && ZWorld.IdleSweepEvery == ev0 && ZWorld.IdleSweepStillSec == st0, $"E8 the switches back as they were: EasyCpuSlowerOn {ZWorld.EasyCpuSlowerOn}, idle sweep {F(ZWorld.IdleSweepEvery, "0")} / {F(ZWorld.IdleSweepStillSec, "0")} s"));
            foreach (var l in v) sb.AppendLine(l);
            int pass = v.Count(l => l.StartsWith("PASS")), fail = v.Count(l => l.StartsWith("FAIL"));
            sb.AppendLine($"{pass} PASS, {fail} FAIL");
            return sb.ToString();
        }
    }
}
