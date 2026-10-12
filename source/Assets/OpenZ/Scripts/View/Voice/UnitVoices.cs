using System;
using System.Collections.Generic;
using System.Text;

namespace OpenZ
{
    // ============================================================================================================================
    //
    // The per-type line picker AND the voice channel rules, plain C# (no UnityEngine: the offline harness in
    // say; the lines are the sound table events vo_select_x_<type>, vo_order_x_<type>_move / _attack / _refuse and vo_idle_x_<type>
    // (2 takes per line; the take is AudioDirector.Pick's "=line" filter, never the same clip twice in a row).
    //  - ADDED to the existing pools, never replacing them: a selection says a type line with SelectShare, an order with OrderShare,
    //    otherwise the existing vo_select_* / vo_order_* lines play exactly as before ("No way!" refusals stay the old lines).
    //  - random with no repeat of the last NoRepeat (6) lines per TYPE; a category smaller than that falls back to its least
    //    recently used line, so a line never follows itself. A line counts as said when it STARTS (a line that only waited and was
    //    then dropped or replaced never enters the history).
    //  - running gag: JokeAfter (3) or more quick clicks (<= QuickGap s apart) on the SAME unit say that type's next idle joke, in
    //    order 1 -> 2 -> 3 -> 1 (back to 1 after GagReset s without a joke of that type); each further joke needs another JokeAfter
    //    quick clicks. Every other quick re-click stays silent while Alan speaks (before AND after a joke: spam never queues lines).
    //  - cannons cannot move: a refused move order of a cannon-only selection says a "refuse" line; the crane cannot attack: its
    //    attack pool holds its repair lines, and an attack order of a crane-only selection says its refuse line. At most one refuse
    //    line every RefuseGap s.
    //  - never cuts a line: every line goes through AudioDirector.SayImpl on alan_main (the speaking line finishes; a different
    //    event waits, only the latest one, at most 1.1 s after the end; the same family is dropped). On top of that (review round):
    //    an order line is skipped while an order line speaks (right-click spam never chains lines) and while the speaking line
    //    still has more than OrderWaitMax s to go (no order acknowledgement arrives late): an order line starts at most
    //    OrderWaitMax s after the right-click; and at most one order acknowledgement every OrderGap s (right-click spam gets one
    //    answer, not one per click as soon as the last line ended).
    // ============================================================================================================================
    public sealed class UnitVoices
    {
        public enum Cat { Select = 0, Move = 1, Attack = 2, Refuse = 3, Idle = 4 }
        public static readonly string[] CatNames = { "select", "move", "attack", "refuse", "idle" };

        /// <summary>The sound table event of a type's category (vo_select_* keeps AudioDirector's select family).</summary>
        public static string EventFor(string type, Cat c)
        {
            switch (c)
            {
                case Cat.Select: return "vo_select_x_" + type;
                case Cat.Move: return "vo_order_x_" + type + "_move";
                case Cat.Attack: return "vo_order_x_" + type + "_attack";
                case Cat.Refuse: return "vo_order_x_" + type + "_refuse";
                default: return "vo_idle_x_" + type;
            }
        }

        /// <summary>The 17 types with lines: 6 robots, 7 crewed vehicles (medium = the mech), 4 cannons.</summary>
        public static readonly string[] Types = { "grunt", "psycho", "tough", "sniper", "pyro", "laser", "electrobot", "jeep", "light", "medium", "heavy",   // electrobot_v1: + electrobot
                                                  "missile_launcher", "apc", "crane", "gun", "gatling", "howitzer", "missile_cannon" };

        /// <summary>One picker for the whole game (outlives a level restart, like DeathVoices.Shared).</summary>
        public static readonly UnitVoices Shared = new UnitVoices();

        // ---- tuning (AudioDirector copies its inspector values in before every use)
        public double SelectShare = 0.6, OrderShare = 0.6, QuickGap = 1.2, GagReset = 90.0, RefuseGap = 4.0, OrderWaitMax = 0.8, OrderGap = 1.5;
        public int NoRepeat = 6, JokeAfter = 3;

        readonly Dictionary<string, List<string>> hist = new Dictionary<string, List<string>>();
        readonly Dictionary<string, int> gag = new Dictionary<string, int>();
        readonly Dictionary<string, double> lastJoke = new Dictionary<string, double>();
        int streakId = -1, streak, jokeBase; double lastClick = -1e9, lastRefuse = -1e9, lastOrder = -1e9;

        // ---- counters (check menu)
        public int Selects, Orders, Jokes, Refuses, Fallbacks, Silenced, OrdersSkipped;

        public void Reset()
        {
            hist.Clear(); gag.Clear(); lastJoke.Clear(); streakId = -1; streak = jokeBase = 0; lastClick = lastRefuse = lastOrder = -1e9;
            Selects = Orders = Jokes = Refuses = Fallbacks = Silenced = OrdersSkipped = 0;
        }

        List<string> H(string type) { if (!hist.TryGetValue(type ?? "", out var h)) hist[type ?? ""] = h = new List<string>(); return h; }

        /// <summary>Random line of the pool, never one of the type's last NoRepeat lines; when every line of the pool is that recent
        /// (small pools) the least recently used one. null = empty pool.</summary>
        public string Pick(string type, IList<string> pool, Func<double> rnd)
        {
            int n = pool?.Count ?? 0;
            if (n == 0) return null;
            if (n == 1) return pool[0];
            var h = H(type);
            var recent = new HashSet<string>();
            for (int i = h.Count - 1; i >= 0 && recent.Count < NoRepeat; i--) recent.Add(h[i]);
            var free = new List<string>(n);
            foreach (var l in pool) if (!recent.Contains(l)) free.Add(l);
            if (free.Count > 0)
            {
                int k = (int)(rnd() * free.Count); if (k >= free.Count) k = free.Count - 1;
                return free[k];
            }
            string best = pool[0]; int bestAt = int.MaxValue;                                   // every line is recent: least recently used
            foreach (var l in pool)
            {
                int at = h.LastIndexOf(l);
                if (at < bestAt) { bestAt = at; best = l; }
            }
            return best;
        }

        /// <summary>The line STARTED playing: it enters the type's history.</summary>
        public void Commit(string type, string line, Cat c)
        {
            if (string.IsNullOrEmpty(line)) return;
            var h = H(type); h.Add(line); if (h.Count > 32) h.RemoveAt(0);
            switch (c) { case Cat.Select: Selects++; break; case Cat.Idle: Jokes++; break; case Cat.Refuse: Refuses++; break; default: Orders++; break; }
        }

        /// <summary>A new selection whose speaker is unitId. click = a left click (not a box drag, group key or Ctrl+R/V/C/A). Returns
        /// the length of the quick-click streak on that unit (1 = first click). A joke does NOT restart the streak (review round: the
        /// click after a joke is still a quick re-click and stays silent while Alan speaks).</summary>
        public int OnSelect(int unitId, bool click, double now)
        {
            if (click && unitId >= 0 && unitId == streakId && now - lastClick <= QuickGap) streak++;
            else { streak = click ? 1 : 0; streakId = click ? unitId : -1; jokeBase = 0; }
            lastClick = click ? now : -1e9;
            return streak;
        }

        public int Streak => streak;
        /// <summary>JokeAfter quick clicks since the streak began or since the last joke.</summary>
        public bool JokeDue => streak >= 1 && streak - jokeBase >= JokeAfter;
        /// <summary>This selection is a quick re-click on the unit that is already the speaker.</summary>
        public bool QuickReclick => streak >= 2;

        /// <summary>The streak reached JokeAfter: the type's next joke in gag order, advancing the gag (offline harness).</summary>
        public string NextJoke(string type, IList<string> jokes, double now)
        {
            int gi = GagIndex(type, now);
            string line = PeekJoke(type, jokes, now);
            if (line != null) AdvanceJoke(type, jokes.Count, now, gi);
            return line;
        }

        /// <summary>The joke NextJoke would say, without advancing the gag (AudioDirector advances it only when the joke starts).</summary>
        public string PeekJoke(string type, IList<string> jokes, double now)
        {
            int n = jokes?.Count ?? 0;
            if (n == 0) return null;
            return jokes[GagIndex(type, now) % n];
        }

        /// <summary>A joke started: the gag moves on from the joke that was SAID (said = the gag index when it was asked, which
        /// PeekJoke chose it by; a joke that waited behind a line keeps its place in the gag even if the reset time passed while it
        /// waited). said &lt; 0 = the index at <paramref name="now"/>.</summary>
        public void AdvanceJoke(string type, int count, double now, int said = -1)
        {
            if (count <= 0) return;
            gag[type] = ((said >= 0 ? said : GagIndex(type, now)) + 1) % count; lastJoke[type] = now; jokeBase = streak;
        }

        public int GagIndex(string type, double now) =>
            gag.TryGetValue(type ?? "", out int g) && lastJoke.TryGetValue(type ?? "", out double lj) && now - lj <= GagReset ? g : 0;

        public static bool Known(string type) => type != null && Array.IndexOf(Types, type) >= 0;
        /// <summary>The unit types whose own lines are recorded in the second, deeper voice. They never answer from the shared
        /// pools of the first voice: they have their own shared select, move and cheer pools (the "_brad" events).</summary>
        public static readonly string[] SecondVoiceTypes = { "psycho", "laser", "heavy", "missile_launcher" };
        public static bool SecondVoice(string type) => type != null && Array.IndexOf(SecondVoiceTypes, type) >= 0;
        public static bool IsCannonType(string type) => type == "gun" || type == "gatling" || type == "howitzer" || type == "missile_cannon";   // electrobot_v1: by name (the list grew)

        public static bool TryCat(string name, out Cat c)
        {
            int i = Array.IndexOf(CatNames, (name ?? "").Trim().ToLowerInvariant());
            c = i >= 0 ? (Cat)i : Cat.Select;
            return i >= 0;
        }

        /// <summary>A refused order may complain (RefuseGap s between two complaints).</summary>
        public bool RefuseFree(double now) => now - lastRefuse >= RefuseGap;
        public void RefuseSaid(double now) { lastRefuse = now; }

        public string Summary() => $"unit lines: {Selects} select, {Orders} order, {Refuses} refuse, {Jokes} jokes; {Fallbacks} classic; " +
                                   $"{Silenced} spam clicks kept silent, {OrdersSkipped} order lines skipped (spam / unit still talking)";

        // ============================================================================================================================
        // The channel rules (one place: AudioDirector.UnitVoices.cs asks them, Simulate checks them against a model of SayImpl)
        // ============================================================================================================================
        /// <summary>What Alan's main channel is doing: speaking, the event id of the speaking line, seconds left.</summary>
        public struct Channel { public bool speaking; public string id; public double left; }

        /// <summary>= AudioDirector.Family: every vo_select* event is one family (box-select spam is dropped).</summary>
        public static string Family(string id) => id != null && id.StartsWith("vo_select", StringComparison.Ordinal) ? "vo_select" : id;
        public static bool IsOrderLine(string id) => id != null && id.StartsWith("vo_order", StringComparison.Ordinal);
        public static bool IsJoke(string id) => id != null && id.StartsWith("vo_idle_x_", StringComparison.Ordinal);

        public enum Act { Nothing, Joke, TypeLine, Classic }
        public enum OrderAct { Nothing, TypeLine, Classic, Refuse, NoWay }

        /// <summary>After OnSelect: what the selection says. Joke = the next joke (plays now, or waits for the speaking line: the
        /// select line, then the punchline); Nothing = a quick re-click while Alan speaks (never queued, also after a joke), or a
        /// select line speaking (the same family, dropped by SayImpl anyway).</summary>
        public Act DecideSelect(bool hasJokes, bool hasSelect, Channel ch, Func<double> rnd)
        {
            if (JokeDue && hasJokes)
            {
                if (ch.speaking && IsJoke(ch.id)) { Silenced++; return Act.Nothing; }           // the previous joke still speaks
                return Act.Joke;
            }
            if (QuickReclick && ch.speaking) { Silenced++; return Act.Nothing; }
            if (ch.speaking && Family(ch.id) == "vo_select") { Silenced++; return Act.Nothing; }
            if (hasSelect && rnd() < SelectShare) return Act.TypeLine;
            Fallbacks++; return Act.Classic;
        }

        /// <summary>An accepted order at <paramref name="now"/>: Nothing = the speaking line has more than OrderWaitMax s left (the
        /// acknowledgement would come late), an order line speaks, or the last order acknowledgement was asked less than OrderGap s
        /// ago (right-click spam: one answer per burst): the type and the classic line both stay silent; NoWay = Zod's refusal (the
        /// classic "No way!" line, as before; it may wait behind a short order line); Refuse = a crane sent at an enemy.</summary>
        public OrderAct DecideOrder(bool noWay, bool hasPool, bool refuse, Channel ch, Func<double> rnd, double now)
        {
            if (ch.speaking && ch.left > OrderWaitMax) { OrdersSkipped++; return OrderAct.Nothing; }
            if (noWay) return OrderAct.NoWay;
            if (ch.speaking && IsOrderLine(ch.id)) { OrdersSkipped++; return OrderAct.Nothing; }
            if (now - lastOrder < OrderGap) { OrdersSkipped++; return OrderAct.Nothing; }
            if (refuse) { if (!hasPool) return OrderAct.Nothing; lastOrder = now; return OrderAct.Refuse; }
            lastOrder = now;
            if (hasPool && rnd() < OrderShare) return OrderAct.TypeLine;
            Fallbacks++; return OrderAct.Classic;
        }

        /// <summary>A refused order (a cannon asked to move, a crane asked to attack) may complain now.</summary>
        public bool RefusedMayTalk(Channel ch, double now) =>
            RefuseFree(now) && !(ch.speaking && (IsOrderLine(ch.id) || ch.left > OrderWaitMax));

        // ============================================================================================================================
        // Model of Alan's main channel (AudioDirector.SayImpl + TickPendingAlan, PA7), for the offline checks
        // ============================================================================================================================
        public sealed class ChannelModel
        {
            public sealed class Line { public string id, line, kind; public double asked, start, end; public bool waited, quick; public string waitedBehind; public int gagIndex = -1; }
            public double PendingMax = 1.1, SpamGuard = 0.25;
            public Func<string, double> Cooldown = id => 0.25;
            public Func<string, double> Length = id => 1.0;
            public Action<Line> Started;                 // the line starts (commit / advance the gag here)
            public Action<Line, string> Queued;          // the line waits behind the speaking event id
            public readonly List<Line> Played = new List<Line>();
            Line cur, pend; double pendDue, lastStart = -10;
            readonly Dictionary<string, double> last = new Dictionary<string, double>();

            public bool Speaking(double t) => cur != null && t < cur.end;
            public Channel State(double t) { Advance(t); return new Channel { speaking = Speaking(t), id = cur?.id, left = Speaking(t) ? cur.end - t : 0 }; }

            /// <summary>0 = played now, 1 = waits, 2 = dropped (SayImpl's order: cooldown, busy -> other family waits / same family
            /// dropped, 0.25 s same-family spam guard).</summary>
            public int Say(Line l, double t)
            {
                Advance(t);
                if (last.TryGetValue(l.id, out var lt) && t - lt < Cooldown(l.id)) return 2;
                if (Speaking(t))
                {
                    if (Family(l.id) == Family(cur.id)) return 2;
                    pend = l; pendDue = cur.end; l.waitedBehind = cur.id; Queued?.Invoke(l, cur.id); return 1;
                }
                if (cur != null && t - lastStart < SpamGuard && Family(l.id) == Family(cur.id)) return 2;
                Start(l, t, false); return 0;
            }

            void Start(Line l, double t, bool waited)
            {
                l.start = t; l.end = t + Math.Max(0.05, Length(l.id)); l.waited = waited;
                if (cur != null && t < cur.end - 1e-9) throw new InvalidOperationException("a line was cut");     // never happens by design
                cur = l; lastStart = t; last[l.id] = t; Played.Add(l); Started?.Invoke(l);
            }

            /// <summary>TickPendingAlan at the moment the speaking line ends: the latest waiting line plays (if still within
            /// PendingMax of the end it waited for, and not blocked by its cooldown).</summary>
            public void Advance(double t)
            {
                while (pend != null && cur != null && cur.end <= t)
                {
                    var p = pend; pend = null; double st = cur.end;
                    if (st - pendDue > PendingMax) break;
                    if (last.TryGetValue(p.id, out var lt) && st - lt < Cooldown(p.id)) break;
                    Start(p, st, true);
                }
            }
        }

        // ============================================================================================================================
        // Offline self-check (tools/harness + the editor check menu): random play sessions through the SAME decision code as
        // AudioDirector.UnitVoices.cs and the channel model above.
        // ============================================================================================================================
        public sealed class SimResult { public bool pass; public string report; }

        static readonly string[] ClassicSelect = { "vo_select_generic", "vo_select_type" };

        /// <summary>pools: event id -> line texts (the distinct lines of the patched table's clips); lengths: event id -> clip lengths
        /// in seconds (the new and the classic events; missing = 1.0 s).</summary>
        public static SimResult Simulate(IDictionary<string, List<string>> pools, IDictionary<string, List<double>> lengths, int seed, int actions)
        {
            var rng = new Random(seed);
            Func<double> rnd = () => rng.NextDouble();
            var uv = new UnitVoices();
            var ch = new ChannelModel();
            ch.Length = id => lengths != null && lengths.TryGetValue(id, out var l) && l.Count > 0 ? l[rng.Next(l.Count)] : 1.0;
            ch.Cooldown = id => id.StartsWith("vo_select", StringComparison.Ordinal) ? 0.25 : id.StartsWith("vo_idle_x_", StringComparison.Ordinal) ? 0.5
                                : id.EndsWith("_refuse", StringComparison.Ordinal) ? 1.0 : 0.8;
            var sb = new StringBuilder();
            int typeSel = 0, classicSel = 0, jokes = 0, badJoke = 0, badOrder = 0, repeats = 0, sameTwice = 0, lruOk = 0;
            int quickQueued = 0, selectBehindJoke = 0, orderBehindOrder = 0, lateOrders = 0, orderLines = 0, spamClicks = 0, rightSpam = 0;
            var lags = new List<double>();
            var jokeSeq = new Dictionary<string, int>(); var jokeAt = new Dictionary<string, double>();
            double t = 0; int unit = -1; string type = null;
            List<string> P(string ty, Cat c) => pools.TryGetValue(EventFor(ty, c), out var l) ? l : new List<string>();
            string TypeOf(string id) { foreach (var ty in Types) if (id.EndsWith("_x_" + ty) || id.Contains("_x_" + ty + "_")) return ty; return null; }
            ch.Queued = (l, behind) =>
            {
                if (l.quick && l.kind != "joke") quickQueued++;
                if (l.quick && IsJoke(behind) && Family(l.id) == "vo_select") selectBehindJoke++;
                if (IsOrderLine(l.id) && IsOrderLine(behind) && l.kind != "noway") orderBehindOrder++;
            };
            ch.Started = l =>
            {
                string ty = TypeOf(l.id);
                if (l.kind == "joke")
                {
                    var jk = P(ty, Cat.Idle); int n = Math.Max(1, jk.Count);
                    // the gag rule re-stated: the next joke after the type's previous one, or joke 1 when the click came more than
                    // GagReset s after the previous joke started
                    int expect = jokeSeq.TryGetValue(ty, out int prevIdx) && jokeAt.TryGetValue(ty, out double prevAt) && l.asked - prevAt <= uv.GagReset ? (prevIdx + 1) % n : 0;
                    if (jk.Count > 0 && (l.gagIndex % n != expect || l.line != jk[expect])) badOrder++;
                    jokeSeq[ty] = l.gagIndex % n; jokeAt[ty] = l.start;
                    uv.Commit(ty, l.line, Cat.Idle); uv.AdvanceJoke(ty, jk.Count, l.start, l.gagIndex); jokes++;
                }
                else if (ty != null && l.line != null)
                {
                    TryCat(l.kind, out var c);
                    CheckPick(uv, ty, l.line, P(ty, c), ref repeats, ref sameTwice, ref lruOk);
                    uv.Commit(ty, l.line, c);
                }
                if (IsOrderLine(l.id)) { orderLines++; lags.Add(l.start - l.asked); if (l.start - l.asked > uv.OrderWaitMax + 1e-6) lateOrders++; }
                if (Family(l.id) == "vo_select") { if (l.kind == "select") typeSel++; else classicSel++; }
            };
            for (int a = 0; a < actions; a++)
            {
                double r = rng.NextDouble();
                if (r < 0.45)
                {   // a selection: a burst of quick clicks on one unit (35 %), a fresh click (45 %), a box / group selection (20 %)
                    double k = rng.NextDouble();
                    bool spam = k < 0.35 && unit >= 0, click = k < 0.8;
                    if (!spam) { unit = rng.Next(40); type = Types[unit % Types.Length]; }
                    int clicks = spam ? 2 + rng.Next(6) : 1;
                    for (int i = 0; i < clicks; i++)
                    {
                        t += spam ? 0.15 + rng.NextDouble() * 0.9 : 1.0 + rng.NextDouble() * 5;
                        uv.OnSelect(unit, click, t);
                        bool quick = uv.QuickReclick;
                        if (quick) spamClicks++;
                        var jk = P(type, Cat.Idle); var sel = P(type, Cat.Select);
                        bool due = uv.JokeDue; int streakBefore = uv.Streak;
                        var act = uv.DecideSelect(jk.Count > 0, sel.Count > 0, ch.State(t), rnd);
                        if (act == Act.Joke)
                        {
                            if (!due || streakBefore < uv.JokeAfter) badJoke++;
                            ch.Say(new ChannelModel.Line { id = EventFor(type, Cat.Idle), line = uv.PeekJoke(type, jk, t), gagIndex = uv.GagIndex(type, t), kind = "joke", asked = t, quick = quick }, t);
                        }
                        else if (act == Act.TypeLine)
                            ch.Say(new ChannelModel.Line { id = EventFor(type, Cat.Select), line = uv.Pick(type, sel, rnd), kind = "select", asked = t, quick = quick }, t);
                        else if (act == Act.Classic)
                            ch.Say(new ChannelModel.Line { id = ClassicSelect[rng.Next(2)], kind = "classic", asked = t, quick = quick }, t);
                    }
                }
                else if (r < 0.85 && unit >= 0)
                {   // orders for the selected unit: one right-click (70 %) or right-click spam (30 %), sometimes right after the select
                    bool spam = rng.NextDouble() < 0.3;
                    int n = spam ? 3 + rng.Next(5) : 1;
                    if (spam) rightSpam++;
                    bool cannon = IsCannonType(type);
                    for (int i = 0; i < n; i++)
                    {
                        t += i == 0 ? 0.1 + rng.NextDouble() * 1.5 : 0.12 + rng.NextDouble() * 0.4;
                        bool noWay = !cannon && rng.NextDouble() < 0.05;
                        if (cannon && rng.NextDouble() < 0.3)
                        {   // a refused move order of a cannon
                            if (uv.RefusedMayTalk(ch.State(t), t) && P(type, Cat.Refuse).Count > 0)
                                if (ch.Say(new ChannelModel.Line { id = EventFor(type, Cat.Refuse), line = uv.Pick(type, P(type, Cat.Refuse), rnd), kind = "refuse", asked = t }, t) < 2) uv.RefuseSaid(t);
                            continue;
                        }
                        Cat oc = cannon ? Cat.Attack : (rng.NextDouble() < 0.5 ? Cat.Move : Cat.Attack);
                        bool refuse = type == "crane" && oc == Cat.Attack && rng.NextDouble() < 0.5;
                        if (refuse) oc = Cat.Refuse;
                        var pool = P(type, oc);
                        var act = uv.DecideOrder(noWay, pool.Count > 0, refuse, ch.State(t), rnd, t);
                        if (act == OrderAct.NoWay) ch.Say(new ChannelModel.Line { id = "vo_order_no_way", kind = "noway", asked = t }, t);
                        else if (act == OrderAct.Refuse) { if (uv.RefuseFree(t) && ch.Say(new ChannelModel.Line { id = EventFor(type, oc), line = uv.Pick(type, pool, rnd), kind = "refuse", asked = t }, t) < 2) uv.RefuseSaid(t); }
                        else if (act == OrderAct.TypeLine) ch.Say(new ChannelModel.Line { id = EventFor(type, oc), line = uv.Pick(type, pool, rnd), kind = CatNames[(int)oc], asked = t }, t);
                        else if (act == OrderAct.Classic) ch.Say(new ChannelModel.Line { id = oc == Cat.Move ? "vo_order_ack" : "vo_order_attack", kind = "classicorder", asked = t }, t);
                    }
                }
                else t += 0.5 + rng.NextDouble() * 6;                                              // nothing happens for a while
            }
            ch.State(t + 10);
            int overlaps = 0;
            for (int i = 1; i < ch.Played.Count; i++) if (ch.Played[i].start < ch.Played[i - 1].end - 1e-9) overlaps++;
            lags.Sort();
            double lagMed = lags.Count > 0 ? lags[lags.Count / 2] : 0, lagMax = lags.Count > 0 ? lags[lags.Count - 1] : 0;
            bool pass = badJoke == 0 && badOrder == 0 && repeats == 0 && sameTwice == 0 && jokes > 0 && quickQueued == 0 && selectBehindJoke == 0
                        && orderBehindOrder == 0 && lateOrders == 0 && overlaps == 0 && typeSel > 0 && classicSel > 0;
            double share = typeSel + classicSel > 0 ? (double)typeSel / (typeSel + classicSel) : 0;
            sb.AppendLine($"seed {seed}, {actions} actions, {ch.Played.Count} lines played: select lines {typeSel} type / {classicSel} classic ({share:P0} type), " +
                          $"{jokes} jokes (early: {badJoke}, out of gag order: {badOrder}); quick re-clicks {spamClicks}, queued behind a line: {quickQueued}; " +
                          $"select lines queued behind a joke: {selectBehindJoke}; right-click spam bursts {rightSpam}, order lines queued behind an order line: {orderBehindOrder}; " +
                          $"order lines {orderLines}, right-click -> line start median {lagMed:0.00} s, max {lagMax:0.00} s (limit {uv.OrderWaitMax:0.0}), late: {lateOrders}; " +
                          $"lines cut / overlapping: {overlaps}; a recent line picked while a fresher one was free: {repeats}; same line twice in a row: {sameTwice}; LRU fallbacks {lruOk}");
            return new SimResult { pass = pass, report = sb.ToString() };
        }

        /// <summary>The pick rule: not in the type's last NoRepeat lines while a pool line outside them exists, else the pool's least
        /// recently used line; never the type's previous line (pool >= 2).</summary>
        static void CheckPick(UnitVoices uv, string type, string line, IList<string> pool, ref int repeats, ref int sameTwice, ref int lruOk)
        {
            if (pool == null || pool.Count == 0 || !pool.Contains(line)) return;
            var h = uv.H(type);
            var recent = new HashSet<string>();
            for (int i = h.Count - 1; i >= 0 && recent.Count < uv.NoRepeat; i--) recent.Add(h[i]);
            bool anyFree = false; foreach (var l in pool) if (!recent.Contains(l)) anyFree = true;
            if (anyFree && recent.Contains(line)) repeats++;
            if (!anyFree)
            {
                lruOk++;
                foreach (var l in pool) if (h.LastIndexOf(l) < h.LastIndexOf(line)) repeats++;
            }
            if (pool.Count >= 2 && h.Count > 0 && h[h.Count - 1] == line) sameTwice++;
        }
    }
}
