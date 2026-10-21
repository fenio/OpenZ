using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of the announcer's strike group warning (DV107; View/OzStrikeWarningRule.cs). Two parts:
    /// (1) made-up timelines (seeded): the computer's whole record (windows opening, members joining and standing, groups leaving,
    /// target moves, releases), the player's radar won and lost now and then, and other voice lines of 1.5 to 4 s that come often
    /// and sometimes in bursts; (2) real matches (player holds, the three levels, strike groups ON): the computer's own record
    /// and the stand-in player's real radar, with the same made-up voice lines. Every line said is checked: a march started
    /// at most 6 s before it, the player owned a radar then and when it was said, no other voice playing, 5 minutes since the
    /// last one, never the same line twice in a row, all twenty before any comes again.
    /// </summary>
    public static class OzV11StrikeWarningCount
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const double LineSec = 2.7;                                                           // the warning lines are 2.2 to 2.9 s long

        sealed class Tl
        {
            public readonly List<(double t, ZBot.StrikeEventKind k)> events = new List<(double, ZBot.StrikeEventKind)>();
            public readonly List<(double a, double b)> radar = new List<(double, double)>();   // the player owns a radar in [a, b)
            public readonly List<(double a, double b)> voice = new List<(double, double)>();   // another voice line plays in [a, b)
            public double end;
            public bool Radar(double t) { foreach (var r in radar) if (t >= r.a && t < r.b) return true; return false; }
            public bool Voice(double t) { foreach (var v in voice) if (t >= v.a && t < v.b) return true; return false; }
        }

        sealed class Tally
        {
            public int timelines, marches, said, noRadar, inGap, tooLate, badNoMarch, badRadar, badOverlap, badGap, badRepeat, badBag, badLate, saidWhileForming;
            public double leastGap = double.MaxValue, mostLate;
        }

        static Tl MadeUp(int seed)
        {
            var r = new Random(seed * 7919 + 13); var tl = new Tl { end = 3600 };
            double t = 480 + r.Next(120);
            while (t < tl.end - 200)
            {   // a window opens, members join and stand, it leaves (sometimes it is given up instead), the target moves, it is released
                tl.events.Add((t, ZBot.StrikeEventKind.WindowOpened));
                int members = 2 + r.Next(6);
                for (int i = 0; i < members; i++) { tl.events.Add((t + 5 + i * 9, ZBot.StrikeEventKind.Joined)); tl.events.Add((t + 25 + i * 9, ZBot.StrikeEventKind.Settled)); }
                double form = t + 40 + r.Next(110);
                if (r.Next(4) == 0) tl.events.Add((form, ZBot.StrikeEventKind.GivenUp));
                else
                {
                    tl.events.Add((form, ZBot.StrikeEventKind.Formed));
                    tl.events.Add((form, ZBot.StrikeEventKind.Departed));
                    tl.events.Add((form + 20 + r.Next(30), ZBot.StrikeEventKind.Retargeted));
                    tl.events.Add((form + 60 + r.Next(60), ZBot.StrikeEventKind.Arrived));
                    tl.events.Add((form + 90 + r.Next(60), ZBot.StrikeEventKind.Released));
                }
                t = form + (r.Next(3) == 0 ? 40 + r.Next(100) : 150 + r.Next(450));             // often closer than 5 minutes (two computers, or a test)
            }
            tl.events.Sort((a, b) => a.t.CompareTo(b.t));
            double rt = 0; bool on = r.Next(3) != 0;
            while (rt < tl.end) { double len = 60 + r.Next(900); if (on) tl.radar.Add((rt, rt + len)); rt += len; on = !on; }
            double vt = 0;
            while (vt < tl.end)
            {
                vt += 2 + r.NextDouble() * 14;
                double len = r.Next(6) == 0 ? 6 + r.NextDouble() * 6 : 1.5 + r.NextDouble() * 2.5;   // sometimes a burst: lines queued back to back keep the voice busy 6 to 12 s
                tl.voice.Add((vt, vt + len)); vt += len;
            }
            return tl;
        }

        /// <summary>Runs the rule over one timeline in steps of 0.1 s (game time = real time here) and judges every line it says.</summary>
        static void Play(Tl tl, int seed, Tally T, List<string> notes)
        {
            T.timelines++;
            var rule = new OzStrikeWarningRule(20, seed);
            var said = new List<(double t, int line)>(); int ei = 0; double ourEnd = -1;
            var marches = tl.events.Where(e => OzStrikeWarningRule.MarchStart(e.k)).Select(e => e.t).ToList();
            T.marches += marches.Count;
            for (int step = 0; step * 0.1 <= tl.end; step++)
            {
                double t = step * 0.1;
                bool radar = tl.Radar(t), busy = tl.Voice(t) || t < ourEnd;
                for (; ei < tl.events.Count && tl.events[ei].t <= t + 1e-9; ei++) rule.Note(tl.events[ei].k, t, t, radar);
                int line = rule.Tick(t, t, radar, busy);
                if (line < 0) continue;
                said.Add((t, line)); ourEnd = t + LineSec;
                double m = marches.Where(x => x <= t + 1e-9).DefaultIfEmpty(-1).Max();
                if (m < 0 || t - m > OzStrikeWarningRule.LateSec + 1e-6) { T.badNoMarch++; notes.Add($"timeline {seed}: a line at {t:0.0} s with no march start in the 6 s before it"); }
                else { T.mostLate = Math.Max(T.mostLate, t - m); if (t - m > OzStrikeWarningRule.LateSec) T.badLate++; if (!tl.Radar(m)) T.badRadar++; }
                if (!radar) T.badRadar++;
                if (tl.Voice(t)) T.badOverlap++;
                bool forming = tl.events.Any(e => e.k == ZBot.StrikeEventKind.WindowOpened && e.t <= t) && !tl.events.Any(e => e.t <= t && e.t >= t - OzStrikeWarningRule.LateSec - 0.2 && OzStrikeWarningRule.MarchStart(e.k));
                if (forming) T.saidWhileForming++;
            }
            T.said += said.Count; T.noRadar += rule.NoRadar; T.inGap += rule.InGap; T.tooLate += rule.TooLate;
            for (int i = 1; i < said.Count; i++)
            {
                double gap = said[i].t - said[i - 1].t;
                T.leastGap = Math.Min(T.leastGap, gap);
                if (gap < OzStrikeWarningRule.GapSec - 1e-6) T.badGap++;
                if (said[i].line == said[i - 1].line) T.badRepeat++;
            }
            for (int b = 0; b * 20 < said.Count; b++)
            {
                var block = said.Skip(b * 20).Take(20).Select(x => x.line).ToList();
                if (block.Distinct().Count() != block.Count) T.badBag++;
            }
        }

        /// <summary>A real match (player holds, strike groups ON): the computer's record with the stand-in's real radar.</summary>
        static Tl Real(string settings, string map, string level, int seed, double minutes, out string what)
        {
            var tl = new Tl { end = minutes * 60 };
            using (OzV11StrikeCount.Game(true))
            {
                var w = ZWorld.Create(settings, map, seed, level == "L17");
                var blue = new ZBot(w, Team.Blue, seed + 101) { Difficulty = ZBotDifficulty.Normal };
                w.Bots.Add(blue);
                var S = OzV11StrikeCount.StandIn(w);
                int seen = 0; double since = -1; bool had = false;
                while (w.Time < tl.end && !w.GameOver)
                {
                    OzV11StrikeCount.StandInKeep(w, S);
                    w.Tick(ZConst.TICK); w.Events.Clear();
                    bool radar = w.Objects.Any(o => o.Kind == ZKind.Building && o.Type == "radar" && o.Owner == Team.Red && !o.Destroyed);
                    if (radar && !had) since = w.Time;
                    if (!radar && had) tl.radar.Add((since, w.Time));
                    had = radar;
                    for (; seen < blue.StrikeEvents.Count; seen++) tl.events.Add((blue.StrikeEvents[seen].time, blue.StrikeEvents[seen].kind));
                }
                if (had) tl.radar.Add((since, double.MaxValue));
                tl.end = w.Time;
            }
            var r = new Random(seed); double vt = 0;
            while (vt < tl.end) { vt += 2 + r.NextDouble() * 14; double len = 1.5 + r.NextDouble() * 2.5; tl.voice.Add((vt, vt + len)); vt += len; }
            int marches = tl.events.Count(e => OzStrikeWarningRule.MarchStart(e.k));
            what = $"{OzV11StrikeCount.LevelName(level)} seed {seed}: {tl.end / 60:0.0} min, {tl.events.Count} record entries, {marches} marches, the player owned a radar {tl.radar.Sum(x => Math.Min(x.b, tl.end) - x.a) / Math.Max(1, tl.end) * 100:0} % of the time";
            return tl;
        }

        public static string Run(string settings, Func<string, string> map, int timelines, int realSeeds, double realMinutes)
        {
            var sb = new StringBuilder(); var T = new Tally(); var notes = new List<string>();
            sb.AppendLine($"STRIKE GROUP WARNING (announcer), counted: {timelines} made-up timelines of 60 min and {realSeeds} real match(es) per level of {realMinutes:0} min; every 0.1 s.");
            for (int s = 1; s <= timelines; s++) Play(MadeUp(s), s, T, notes);
            int madeSaid = T.said, madeMarches = T.marches;
            var realLines = new List<string>();
            for (int s = 1; s <= realSeeds; s++)
                foreach (var lv in OzV11StrikeCount.Levels)
                {
                    var tl = Real(settings, map(lv), lv, s, realMinutes, out string what);
                    int before = T.said; Play(tl, 1000 + s, T, notes);
                    realLines.Add($"  {what}; lines said {T.said - before}");
                }
            sb.AppendLine($"made-up timelines: {madeMarches} marches, {madeSaid} lines said");
            foreach (var l in realLines) sb.AppendLine(l);
            sb.AppendLine($"in all: {T.marches} marches, {T.said} lines said; not said: no radar {T.noRadar}, inside 5 minutes of the last one {T.inGap}, more than 6 s late because a voice was busy {T.tooLate}");
            foreach (var n in notes.Take(10)) sb.AppendLine("  " + n);
            sb.AppendLine(OzV11Kit.Verdict(T.said > 0 && T.badNoMarch == 0 && T.saidWhileForming == 0, $"said only when a strike group starts to march (never while it forms): {T.said - T.badNoMarch}/{T.said} lines had a march start at most 6 s before them; said while a group only formed {T.saidWhileForming}"));
            sb.AppendLine(OzV11Kit.Verdict(T.badRadar == 0 && T.noRadar > 0, $"only with a radar: lines said without the player owning a radar (at the march start or when said) {T.badRadar}; marches not announced for want of a radar {T.noRadar}"));
            sb.AppendLine(OzV11Kit.Verdict(T.badGap == 0 && T.inGap > 0, $"at most once in 5 minutes: the least time between two lines {(T.leastGap == double.MaxValue ? "-" : T.leastGap.ToString("0.0", Inv) + " s")}; marches inside the 5 minutes, not announced {T.inGap}"));
            sb.AppendLine(OzV11Kit.Verdict(T.badOverlap == 0 && T.badLate == 0 && T.tooLate > 0, $"never over another voice line: lines started while another voice played {T.badOverlap}; the latest line {T.mostLate:0.0} s after its march start (limit 6 s); dropped as more than 6 s late {T.tooLate}"));
            sb.AppendLine(OzV11Kit.Verdict(T.badRepeat == 0 && T.badBag == 0 && T.said >= 40, $"random, no repeat: the same line twice in a row {T.badRepeat}; a line again before all twenty were said {T.badBag} (over {T.said} lines)"));
            bool pass = !sb.ToString().Split('\n').Any(l => l.StartsWith("FAIL"));
            sb.AppendLine((pass ? "PASS " : "FAIL ") + sb.ToString().Split('\n').Count(l => l.StartsWith("PASS  ")) + "/" + sb.ToString().Split('\n').Count(l => l.StartsWith("PASS  ") || l.StartsWith("FAIL  ")) + " checks");
            return sb.ToString();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Count Strike Group Warnings (f39 round 2)")]
        static void Menu() => OzV11Kit.RunMenu("f39 round 2: the announcer's strike group warning (300 made-up timelines and one real match per level of 30 min; about a minute)",
                                               () => Run(OzV11Kit.SettingsText, OzV11Kit.MapText, 300, 1, 30));
#endif
    }
}
