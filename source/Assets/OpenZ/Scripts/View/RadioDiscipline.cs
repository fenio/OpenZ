// Plain C# (no UnityEngine), so the same rules run in the editor check and in the offline harness:
//   * limiter: after an alert / cry for help the radio stays shut for a random GapMin..GapMax s (30-60); never more than PerMinute
//     (2) radio lines in any 60 s; >= MinSpacing (8 s) between any two; chatter (an off-screen "Ow!") only after a whole minute
//     of radio silence (ChatterQuiet) and it never shuts the radio for the cries.
//   * line bags: per event a shuffle bag over the distinct LINES (not the clips / takes): no line repeats until every line was
//     used, and the line just played never opens the next round. "I said ..." lines are follow-ups: they wait in the bag for the
//     second call of an episode.
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenZ
{
    public sealed class RadioDiscipline
    {
        /// <summary>The game's radio (AudioDirector). Static: the bags and the limiter outlive a level restart.</summary>
        public static readonly RadioDiscipline Shared = new RadioDiscipline();

        public float GapMin = 30f, GapMax = 60f, MinSpacing = 8f, ChatterQuiet = 60f;
        public int PerMinute = 2;

        readonly List<float> calls = new List<float>();
        float shutUntil = float.NegativeInfinity, last = float.NegativeInfinity;
        readonly Random rng;

        public RadioDiscipline(int? seed = null) { rng = seed.HasValue ? new Random(seed.Value) : new Random(); }

        /// <summary>Radio lines sent since start (alerts, cries, chatter).</summary>
        public int Lines { get; private set; }
        public float ShutUntil => shutUntil;
        public float LastCall => last;

        void Sync(float now)
        {
            if (now + 1f < last) { calls.Clear(); shutUntil = last = float.NegativeInfinity; }   // the clock went back (new Play session)
            calls.RemoveAll(t => now - t >= 60f);
        }

        /// <summary>An alert or a cry for help may go out now.</summary>
        public bool CanCall(float now) { Sync(now); return calls.Count < Math.Max(1, PerMinute) && now - last >= MinSpacing && now >= shutUntil; }

        /// <summary>Low-priority chatter (an off-screen hurt yelp) may go out now.</summary>
        public bool CanChatter(float now) { Sync(now); return calls.Count == 0 && now - last >= Math.Max(MinSpacing, ChatterQuiet); }

        /// <summary>A radio line went out. shut = an alert / cry: the radio stays shut for GapMin..GapMax s.</summary>
        public void Called(float now, bool shut)
        {
            Sync(now);
            calls.Add(now); last = now; Lines++;
            if (shut)
            {
                float a = Math.Min(GapMin, GapMax), b = Math.Max(GapMin, GapMax);
                shutUntil = now + a + (float)rng.NextDouble() * (b - a);
            }
        }

        public int CallsInLastMinute(float now) { Sync(now); return calls.Count; }

        // ---- line bags
        sealed class Bag { public readonly List<string> order = new List<string>(); public string last; public int rounds; }
        readonly Dictionary<string, Bag> bags = new Dictionary<string, Bag>();
        static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

        /// <summary>"I said we're under attack!" only makes sense as the second call of an episode.</summary>
        public static bool FollowUpOnly(string line) => line != null && line.StartsWith("I said", StringComparison.OrdinalIgnoreCase);

        /// <summary>The next line of this event's bag (not removed until Played). lines = the event's clip lines (takes repeat a
        /// line; empty ones are ignored). followUp = the episode already called once.</summary>
        public string Peek(string key, IEnumerable<string> lines, bool followUp)
        {
            var all = (lines ?? Enumerable.Empty<string>()).Where(l => !string.IsNullOrEmpty(l)).Distinct(Cmp).ToList();
            if (all.Count == 0) return null;
            if (!bags.TryGetValue(key, out var bag)) bags[key] = bag = new Bag();
            var known = new HashSet<string>(all, Cmp);
            bag.order.RemoveAll(l => !known.Contains(l));                                       // a line left the table
            var pick = First(bag, followUp);
            if (pick != null) return pick;
            var waiting = new HashSet<string>(bag.order, Cmp);                                   // follow-ups still waiting stay in front
            var fresh = all.Where(l => !waiting.Contains(l)).OrderBy(_ => rng.Next()).ToList();
            int firstOk = fresh.FindIndex(l => followUp || !FollowUpOnly(l));
            if (firstOk >= 0 && fresh.Count > 1 && Cmp.Equals(fresh[firstOk], bag.last))
            {   // the line just played never opens the next round
                var l = fresh[firstOk]; fresh.RemoveAt(firstOk); fresh.Add(l);
            }
            bag.order.AddRange(fresh); bag.rounds++;
            return First(bag, followUp);
        }

        static string First(Bag bag, bool followUp)
        {
            foreach (var l in bag.order) if (followUp || !FollowUpOnly(l)) return l;
            return null;
        }

        /// <summary>The line went out: it leaves the bag until every other line had its turn.</summary>
        public void Played(string key, string line)
        {
            if (line == null) return;
            if (!bags.TryGetValue(key, out var bag)) bags[key] = bag = new Bag();
            int i = bag.order.FindIndex(l => Cmp.Equals(l, line));
            if (i >= 0) bag.order.RemoveAt(i);
            bag.last = line;
        }

        public void Reset() { calls.Clear(); shutUntil = last = float.NegativeInfinity; bags.Clear(); Lines = 0; }

        // ---- self-check (editor menu + offline harness): a long stretch of non-stop heavy fights, a call attempt every 0.5 s
        public sealed class SimResult
        {
            public int calls, chatter, cries, maxPerMinute, repeatsBeforeRound, sameTwice, followUpFirst, linesUsed, linesTotal, linesWanted;
            public float minGap = float.PositiveInfinity, minCryGap = float.PositiveInfinity, meanCryGap;
            public string firstLines;
            public bool Ok(RadioDiscipline r) => maxPerMinute <= Math.Max(1, r.PerMinute) && minGap >= r.MinSpacing - 1e-3f
                && minCryGap >= Math.Min(r.GapMin, r.GapMax) - 1e-3f && repeatsBeforeRound == 0 && sameTwice == 0 && followUpFirst == 0
                && linesUsed >= linesWanted;
            public override string ToString() =>
                $"calls {calls} (cries/alerts {cries}, chatter {chatter}), max per 60 s {maxPerMinute}, min gap {minGap:0.0} s, " +
                $"min gap between cries {minCryGap:0.0} s (mean {meanCryGap:0.0} s), lines used {linesUsed}/{linesTotal} (want >= {linesWanted}), " +
                $"repeats before the round was through {repeatsBeforeRound}, same line twice in a row {sameTwice}, 'I said' as a first call {followUpFirst}";
        }

        /// <summary>minutes of non-stop fighting: every 0.5 s a call is wanted (half the episodes get a follow-up call), and every
        /// 3 s an off-screen hurt yelp asks for chatter (in the game only while no episode runs, so this is the worst case).</summary>
        public static SimResult Simulate(IList<string> lines, float minutes = 30f, int seed = 1, float gapMin = 30f, float gapMax = 60f, int perMinute = 2)
        {
            var r = new RadioDiscipline(seed) { GapMin = gapMin, GapMax = gapMax, PerMinute = perMinute };
            var res = new SimResult();
            var times = new List<float>(); var cryTimes = new List<float>(); var played = new List<string>();
            var distinct = lines.Where(l => !string.IsNullOrEmpty(l)).Distinct(Cmp).ToList();
            res.linesTotal = distinct.Count;
            bool followUp = false; var rnd = new Random(seed + 7);
            for (float t = 0f; t < minutes * 60f; t += 0.5f)
            {
                if (Math.Abs(t % 3f) < 1e-3f && r.CanChatter(t)) { r.Called(t, false); times.Add(t); res.chatter++; }
                if (!r.CanCall(t)) continue;
                var line = r.Peek("cry", lines, followUp);
                if (line == null) continue;
                if (!followUp && FollowUpOnly(line)) res.followUpFirst++;
                r.Played("cry", line); r.Called(t, true);
                times.Add(t); cryTimes.Add(t); played.Add(line); res.cries++;
                followUp = !followUp && rnd.NextDouble() < 0.5;                                  // half the episodes get a second call
            }
            res.calls = times.Count;
            for (int i = 0; i < times.Count; i++)
            {
                int n = 0; for (int j = i; j < times.Count && times[j] - times[i] < 60f; j++) n++;
                res.maxPerMinute = Math.Max(res.maxPerMinute, n);
                if (i > 0) res.minGap = Math.Min(res.minGap, times[i] - times[i - 1]);
            }
            for (int i = 1; i < cryTimes.Count; i++) res.minCryGap = Math.Min(res.minCryGap, cryTimes[i] - cryTimes[i - 1]);
            res.meanCryGap = cryTimes.Count > 1 ? (cryTimes[cryTimes.Count - 1] - cryTimes[0]) / (cryTimes.Count - 1) : 0f;
            // rounds: no line (bar the follow-up-only ones, which wait for a second call) plays again before every other line did
            var regular = distinct.Where(l => !FollowUpOnly(l)).ToList();
            var used = new HashSet<string>(Cmp);
            for (int i = 0; i < played.Count; i++)
            {
                if (i > 0 && Cmp.Equals(played[i], played[i - 1])) res.sameTwice++;
                if (FollowUpOnly(played[i])) continue;
                if (!used.Add(played[i])) res.repeatsBeforeRound++;
                if (regular.All(used.Contains)) used.Clear();
            }
            res.linesUsed = played.Distinct(Cmp).Count();
            res.linesWanted = Math.Min(regular.Count, res.cries - played.Count(FollowUpOnly));
            res.firstLines = string.Join(" | ", played.Take(8));
            return res;
        }
    }
}
