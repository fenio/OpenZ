using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenZ
{
    // ============================================================================================================================
    // unit_voices_v1: the per-type unit lines on top of AudioDirector's selection / order voices (see UnitVoices.cs for the rules).
    // Hooks (apply.py): SelectionVoice -> UvSelect (speaker, running gag, a type line or the classic pools), PollInput's order voice
    // -> UvOrder (a type move / attack / repair line or the classic pools), a refused order -> UvRefused (cannons asked to move,
    // a crane asked to attack). Every line is an Alan line through SayImpl on alan_main, with the robot filter of that channel at
    // runtime: the PA7 rule holds (the speaking line always finishes; a different event waits for it, at most 1.1 s after its end;
    // a line of the same family is dropped), so no new line ever cuts a line mid-word. What to say is decided in UnitVoices
    // (DecideSelect / DecideOrder, the same code the offline channel simulation checks):
    //  - a quick re-click on the speaking unit stays silent while Alan talks (before and after a joke: nothing queues behind it);
    //  - an order line is skipped while an order line speaks (right-click spam never chains lines) and while the speaking line has
    //    more than unitOrderWaitMax s left (no late acknowledgement): an order line starts at most unitOrderWaitMax s after the click;
    //  - at most one order acknowledgement every unitOrderGap s (right-click spam gets one answer, not one per click).
    // A line counts as said (no-repeat history, the gag, the console proof) when it STARTS, not when it only waits.
    // Proof in Play: the console says "[unit_voices] live: ..." once when the pools load and "[unit_voices] first <event>: ...
    // (playing)" the first time each pool really plays.
    // ============================================================================================================================
    public partial class AudioDirector
    {
        [Header("Unit voices (unit_voices_v1)")]
        [Tooltip("Per-type select / order lines and running-gag jokes ADDED to the classic Alan pools (off = the classic lines only)")]
        public bool unitVoices = true;
        [Tooltip("Share of selections that say a type line (the rest keep the classic 'Unit reporting!' / '<Type>s reporting!' pools)")]
        [Range(0, 1)] public float unitSelectShare = 0.6f;
        [Tooltip("Share of move / attack orders that say a type line (the rest keep the classic order pools; 'No way!' refusals stay classic)")]
        [Range(0, 1)] public float unitOrderShare = 0.6f;
        [Tooltip("Quick clicks on the same unit before its type says the next running-gag joke")] [Range(2, 10)] public int unitJokeClicks = 3;
        [Tooltip("Seconds between two clicks that still count as 'quick'")] public float unitQuickClick = 1.2f;
        [Tooltip("A line never comes back within this many lines of the same type")] [Range(1, 12)] public int unitNoRepeat = 6;
        [Tooltip("An order line waits for the speaking line only when that line ends within this many seconds; otherwise the order " +
                 "stays silent (the click sound acknowledges it): an order line never starts later than this after the right-click")]
        [Range(0f, 1.1f)] public float unitOrderWaitMax = 0.8f;
        [Tooltip("At most one order acknowledgement (type or classic line) every this many seconds: right-click spam gets one answer")]
        [Range(0f, 4f)] public float unitOrderGap = 1.5f;
        [Tooltip("Console: one line when the pools load and one the first time each pool plays (proof that the new pools play)")]
        public bool unitVoicesLog = true;

        int uvSpeaker = -1;                                             // the unit that answered the last selection (keeps answering its orders)
        ZObject uvOrderUnit;                                            // the unit that answers the order being voiced now (null: none picked)
        readonly Dictionary<string, List<string>> uvLines = new Dictionary<string, List<string>>();
        readonly Dictionary<string, SoundEventDef> uvLinesOf = new Dictionary<string, SoundEventDef>();
        readonly HashSet<string> uvFirst = new HashSet<string>();
        bool uvBootLogged;
        string uvWatchKey;                                              // the waiting line being watched (one at a time: only the latest waits)

        UnitVoices Uv()
        {
            var uv = UnitVoices.Shared;
            uv.SelectShare = unitSelectShare; uv.OrderShare = unitOrderShare; uv.JokeAfter = Mathf.Max(2, unitJokeClicks);
            uv.QuickGap = unitQuickClick; uv.NoRepeat = Mathf.Max(1, unitNoRepeat); uv.OrderWaitMax = Mathf.Clamp(unitOrderWaitMax, 0f, PendingAlanMax); uv.OrderGap = Mathf.Max(0f, unitOrderGap);
            if (!uvBootLogged && Loaded) UvBootLog();
            return uv;
        }

        void UvBootLog()
        {
            uvBootLogged = true;
            int types = 0, events = 0, lines = 0, clips = 0;
            foreach (var t in UnitVoices.Types)
            {
                bool any = false;
                for (int c = 0; c < UnitVoices.CatNames.Length; c++)
                {
                    var d = Def(UnitVoices.EventFor(t, (UnitVoices.Cat)c));
                    if (d == null || d.loaded == null || d.loaded.Count == 0) continue;
                    any = true; events++; clips += d.loaded.Count; lines += UvLines(t, (UnitVoices.Cat)c).Count;
                }
                if (any) types++;
            }
            if (!unitVoicesLog) return;
            if (types > 0) Debug.Log($"[unit_voices] live: {types} types, {events} pools, {lines} lines, {clips} clips (select {unitSelectShare:P0}, orders {unitOrderShare:P0}, joke after {unitJokeClicks} quick clicks, order lines wait at most {unitOrderWaitMax:0.0} s, one order answer per {unitOrderGap:0.0} s)");
            else Debug.LogWarning("[unit_voices] no vo_*_x_* pools in the sound table: only the classic lines play (apply unit_voices_v1)");
        }

        /// <summary>The distinct lines of a type's category (the loaded clips of its event; 2 takes share one line), in table order
        /// (the jokes: gag order). Rebuilt when the table reloads.</summary>
        List<string> UvLines(string type, UnitVoices.Cat c)
        {
            string id = UnitVoices.EventFor(type, c);
            var d = Def(id);
            if (uvLines.TryGetValue(id, out var l) && uvLinesOf.TryGetValue(id, out var of) && of == d) return l;
            l = new List<string>();
            if (d != null && d.lines != null) foreach (var s in d.lines) if (!string.IsNullOrEmpty(s) && !l.Contains(s)) l.Add(s);
            uvLines[id] = l; uvLinesOf[id] = d;
            return l;
        }

        bool UvCanSpeak(string id) => Loaded && voices && unitVoices && !OpenZVictory.MutesVoice(id) && (ExternalVoiceBusy == null || !ExternalVoiceBusy()) && RobotChatter.Allows(id);

        bool AlanMainSpeaking => alanMain != null && alanMain.busy && alanMain.src && alanMain.src.isPlaying;

        UnitVoices.Channel UvChannel() => new UnitVoices.Channel
        { speaking = AlanMainSpeaking, id = alanMainId, left = AlanMainSpeaking ? alanMainClipLeft() : 0.0 };

        /// <summary>alan_main plays this event and this line right now.</summary>
        bool UvPlaysNow(string id, string line)
        {
            if (!AlanMainSpeaking || alanMainId != id || alanMain.def == null) return false;
            var d = alanMain.def;
            return d.lines != null && d.lastIdx >= 0 && d.lastIdx < d.lines.Count && d.lines[d.lastIdx] == line;
        }

        enum UvSaid { Dropped, Playing, Waiting }

        /// <summary>Says a type line (the take: Pick's "=line" filter) through SayImpl, so it never cuts the speaking line. Playing =
        /// it started now; Waiting = it waits for the speaking line (PA7) and is committed only if it really starts; Dropped = the
        /// same family speaks, cooldown or muted. gag = a joke's place in the running gag (UnitVoices.GagIndex when it was asked).</summary>
        UvSaid UvSay(string type, UnitVoices.Cat c, string line, int gag = -1)
        {
            if (string.IsNullOrEmpty(line)) return UvSaid.Dropped;
            string id = UnitVoices.EventFor(type, c);
            if (!UvCanSpeak(id)) return UvSaid.Dropped;
            float t0 = Time.unscaledTime;
            SayImpl(id, "=" + line);
            if (lastLine.TryGetValue(id, out var lt) && lt >= t0 && UvPlaysNow(id, line)) { UvStarted(type, c, line, id, false, gag); return UvSaid.Playing; }
            if (pendingAlan == id && pendingAlanFilter == "=" + line) { UvWatch(type, c, line, id, t0, gag); return UvSaid.Waiting; }
            return UvSaid.Dropped;
        }

        void UvWatch(string type, UnitVoices.Cat c, string line, string id, float t0, int gag)
        {
            string key = id + "|" + line;
            if (uvWatchKey == key) return;                                                     // already watched (a re-asked waiting line)
            uvWatchKey = key;
            StartCoroutine(UvWatchPending(type, c, line, id, t0, key, gag));
        }

        /// <summary>A waiting line: committed when TickPendingAlan starts it; nothing when it is replaced by a newer waiting line or
        /// expires (it never played, so it never enters the history, never advances the gag and never logs as proof).</summary>
        System.Collections.IEnumerator UvWatchPending(string type, UnitVoices.Cat c, string line, string id, float t0, string key, int gag)
        {
            string f = "=" + line;
            while (pendingAlan == id && pendingAlanFilter == f) yield return null;
            if (uvWatchKey == key) uvWatchKey = null;
            if (lastLine.TryGetValue(id, out var lt) && lt >= t0 && UvPlaysNow(id, line)) UvStarted(type, c, line, id, true, gag);
        }

        void UvStarted(string type, UnitVoices.Cat c, string line, string id, bool waited, int gag)
        {
            var uv = Uv(); double now = Time.unscaledTimeAsDouble;
            uv.Commit(type, line, c);
            if (c == UnitVoices.Cat.Idle) uv.AdvanceJoke(type, UvLines(type, c).Count, now, gag);
            if (unitVoicesLog && uvFirst.Add(id))
                Debug.Log($"[unit_voices] first {id}: \"{line}\" (playing{(waited ? ", after the line it waited for" : "")}; {UvLines(type, c).Count} lines in the pool)");
        }

        ZObject UvPickSpeaker(List<ZObject> cands)
        {
            if (cands.Count == 0) return null;
            foreach (var o in cands) if (o.Id == uvSpeaker) return o;
            return cands[UnityEngine.Random.Range(0, cands.Count)];
        }

        /// <summary>SelectionVoice: picks the speaker (a quick re-click keeps the same one), runs the running gag and the type lines.
        /// Returns the unit the classic pools should speak for, or null when this layer spoke (or stays silent on purpose).</summary>
        /// <summary>SOUND-bundle Play proof (View/SoundBundleProof.cs, editor harness): the frame in which a harness selection counts as a
        /// left-button click release (the harness cannot press the real mouse). -1 = never; nothing in the game sets it.</summary>
        public static int DebugClickFrame = -1;
        ZObject UvSelect()
        {
            var sel = input.Selection;
            ZObject prev = null;
            if (uvSpeaker >= 0) foreach (var o in sel) if (o != null && o.Id == uvSpeaker && !o.Destroyed) { prev = o; break; }
            // a click = a left-button release (not a group key); it counts for the running gag on ONE unit: a single selection, or the
            // double-click result (PlayerInput: every own unit of the clicked type on screen) that still holds the previous speaker
            var ms = Mouse.current;
            bool mouse = ((ms != null && ms.leftButton.wasReleasedThisFrame) || DebugClickFrame == Time.frameCount) && !RecallKeyThisFrame();
            bool sameType = prev != null;
            if (sameType) foreach (var o in sel) if (o == null || o.Type != prev.Type) { sameType = false; break; }
            bool click = mouse && (sel.Count == 1 || sameType);
            var u = mouse && prev != null && (sel.Count == 1 || sameType) ? prev : sel[UnityEngine.Random.Range(0, sel.Count)];
            if (sel.Count == 1) u = sel[0];
            uvSpeaker = u.Id;
            if (!unitVoices || !UnitVoices.Known(u.Type)) return u;
            var uv = Uv();
            double now = Time.unscaledTimeAsDouble;
            uv.OnSelect(u.Id, click, now);
            string jid = UnitVoices.EventFor(u.Type, UnitVoices.Cat.Idle);
            var jokes = UvLines(u.Type, UnitVoices.Cat.Idle);
            var pool = UvLines(u.Type, UnitVoices.Cat.Select);
            switch (uv.DecideSelect(jokes.Count > 0 && UvCanSpeak(jid), pool.Count > 0, UvChannel(), () => UnityEngine.Random.value))
            {
                case UnitVoices.Act.Nothing: return null;
                case UnitVoices.Act.Joke:                                                    // plays now or waits for the speaking line;
                    UvSay(u.Type, UnitVoices.Cat.Idle, uv.PeekJoke(u.Type, jokes, now), uv.GagIndex(u.Type, now));   // a dropped joke comes on the next quick click
                    return null;
                case UnitVoices.Act.TypeLine:
                    if (UvSay(u.Type, UnitVoices.Cat.Select, uv.Pick(u.Type, pool, () => UnityEngine.Random.value)) != UvSaid.Dropped) return null;
                    uv.Fallbacks++; return u;
                default: return u;                                                              // the classic pools
            }
        }

        /// <summary>PollInput's order voice. true = this layer answered or decided on silence (a type line plays or waits, or the order
        /// stays silent: right-click spam / the unit still talks): the classic line is skipped. false = the classic line plays.</summary>
        bool UvOrder(bool attack, bool noWay)
        {
            uvOrderUnit = null;
            if (!unitVoices || input.Selection.Count == 0) return false;
            var uv = Uv(); Rnd rnd = () => UnityEngine.Random.value;
            var took = new List<ZObject>();
            foreach (var o in input.Selection)
                if (o != null && !o.Destroyed && o.Waypoints.Count > 0 && (attack || o.Kind != ZKind.Cannon)) took.Add(o);   // a cannon never answers a move order
            var u = UvPickSpeaker(took);
            uvOrderUnit = u;
            if (u == null || !UnitVoices.Known(u.Type))                                          // no type voice: the channel rules still hold for the classic line
                return uv.DecideOrder(noWay, false, false, UvChannel(), () => rnd(), Time.unscaledTimeAsDouble) == UnitVoices.OrderAct.Nothing;
            uvSpeaker = u.Id;
            var w = u.Waypoints[u.Waypoints.Count - 1];
            bool crane = u.Kind == ZKind.Vehicle && u.Type == "crane";
            UnitVoices.Cat c;
            if (crane) c = w.mode == WpMode.CraneRepair ? UnitVoices.Cat.Attack                     // the crane's "attack" pool = its repair lines
                         : (w.mode == WpMode.Attack || attack) ? UnitVoices.Cat.Refuse : UnitVoices.Cat.Move;   // a crane sent at an enemy complains
            else c = w.mode == WpMode.Attack || attack ? UnitVoices.Cat.Attack : UnitVoices.Cat.Move;
            var pool = UvLines(u.Type, c);
            string id = UnitVoices.EventFor(u.Type, c);
            bool has = pool.Count > 0 && UvCanSpeak(id);
            switch (uv.DecideOrder(noWay, has, c == UnitVoices.Cat.Refuse, UvChannel(), () => rnd(), Time.unscaledTimeAsDouble))
            {
                case UnitVoices.OrderAct.Nothing: return true;                                  // silent: spam / the unit still talks / no refuse pool
                case UnitVoices.OrderAct.NoWay: return false;                                   // the classic "No way!"
                case UnitVoices.OrderAct.Refuse:
                {
                    double now = Time.unscaledTimeAsDouble;
                    if (uv.RefuseFree(now) && UvSay(u.Type, c, uv.Pick(u.Type, pool, () => rnd())) != UvSaid.Dropped) uv.RefuseSaid(now);
                    return true;                                                                // never "Let's get 'em!" from a crane
                }
                case UnitVoices.OrderAct.TypeLine:
                    if (UvSay(u.Type, c, uv.Pick(u.Type, pool, () => rnd())) != UvSaid.Dropped) return true;
                    uv.Fallbacks++; return false;
                default: return false;                                                          // the classic order pools
            }
        }

        delegate float Rnd();

        /// <summary>The shared select pool for a unit of this type: the first voice's, or the second voice's for its types.</summary>
        public static string ClassicSelectEvent(string type) => UnitVoices.SecondVoice(type) ? "vo_select_generic_brad" : "vo_select_generic";
        public const string OwnAttackLine = "own attack line";
        /// <summary>The shared order line for a unit of this type. The first voice: its move, attack and refusal pools. A type of
        /// the second voice: its own shared move pool; for an attack order one of the type's own attack lines (OwnAttackLine);
        /// for the refusal nothing (null): that voice has no such line, and the first voice must not answer for it.</summary>
        public static string ClassicOrderEvent(string type, bool attack, bool noWay)
        {
            if (!UnitVoices.SecondVoice(type)) return noWay ? "vo_order_no_way" : attack ? "vo_order_attack" : "vo_order_ack";
            return noWay ? null : attack ? OwnAttackLine : "vo_order_ack_brad";
        }

        /// <summary>PollInput: the shared line of an order the type layer left to the shared pools.</summary>
        void ClassicOrderLine(bool attack, bool noWay)
        {
            var u = uvOrderUnit;
            if (u == null) foreach (var o in input.Selection) if (o != null && !o.Destroyed) { u = o; break; }
            string id = ClassicOrderEvent(u != null ? u.Type : null, attack, noWay);
            if (id == null) return;
            if (id == OwnAttackLine)
            {
                var pool = UvLines(u.Type, UnitVoices.Cat.Attack);
                if (pool.Count > 0) UvSay(u.Type, UnitVoices.Cat.Attack, Uv().Pick(u.Type, pool, () => UnityEngine.Random.value));
                return;
            }
            Say(id);
        }

        /// <summary>A refused order (ui_order_invalid): cannons asked to move and a crane asked to attack complain (RefuseGap apart, never
        /// behind an order line, never later than unitOrderWaitMax after the click).</summary>
        void UvRefused(bool attack)
        {
            if (!unitVoices || input.Selection.Count == 0) return;
            bool cannons = true, cranes = true;
            foreach (var o in input.Selection)
            {
                if (o == null) continue;
                if (o.Kind != ZKind.Cannon) cannons = false;
                if (!(o.Kind == ZKind.Vehicle && o.Type == "crane")) cranes = false;
            }
            if (!((cannons && !attack) || (cranes && attack))) return;
            var uv = Uv(); double now = Time.unscaledTimeAsDouble;
            if (!uv.RefusedMayTalk(UvChannel(), now)) return;
            var u = UvPickSpeaker(new List<ZObject>(input.Selection));
            if (u == null || !UnitVoices.Known(u.Type)) return;
            uvSpeaker = u.Id;
            var pool = UvLines(u.Type, UnitVoices.Cat.Refuse);
            if (pool.Count > 0 && UvSay(u.Type, UnitVoices.Cat.Refuse, uv.Pick(u.Type, pool, () => UnityEngine.Random.value)) != UvSaid.Dropped) uv.RefuseSaid(now);
        }

        /// <summary>Checks: a type line now (e.g. <c>return OpenZ.AudioDirector.DebugUnitLine("jeep", "idle");</c>) + the counters.
        /// Same path as the game (SayImpl on alan_main): while Alan speaks, the line waits for him or is dropped.</summary>
        public static string DebugUnitLine(string type, string cat)
        {
            if (!I) return "no AudioDirector";
            if (!UnitVoices.TryCat(cat, out var c)) return "cat = select / move / attack / refuse / idle";
            if (!UnitVoices.Known(type)) return "type = " + string.Join(" / ", UnitVoices.Types);
            var pool = I.UvLines(type, c);
            if (pool.Count == 0) return $"no lines for {type} {cat} ({UnitVoices.EventFor(type, c)} missing: apply unit_voices_v1)";
            var uv = I.Uv(); double now = Time.unscaledTimeAsDouble;
            var line = c == UnitVoices.Cat.Idle ? uv.PeekJoke(type, pool, now) : uv.Pick(type, pool, () => UnityEngine.Random.value);
            var r = I.UvSay(type, c, line, c == UnitVoices.Cat.Idle ? uv.GagIndex(type, now) : -1);
            return $"{type} {cat}: \"{line}\" ({(r == UvSaid.Playing ? "playing" : r == UvSaid.Waiting ? "waits for the speaking line" : "dropped: Alan speaks the same family, cooldown or muted")}) ({pool.Count} lines) | {UnitVoices.Shared.Summary()}";
        }

        /// <summary>The check menu's live block: pools loaded per type + what was said this session.</summary>
        public static string UnitVoicesLiveReport()
        {
            if (!I) return "no AudioDirector";
            var sb = new System.Text.StringBuilder();
            foreach (var t in UnitVoices.Types)
            {
                var parts = new List<string>();
                for (int c = 0; c < UnitVoices.CatNames.Length; c++)
                {
                    int n = I.UvLines(t, (UnitVoices.Cat)c).Count;
                    if (n > 0) parts.Add($"{UnitVoices.CatNames[c]} {n}");
                }
                sb.AppendLine($"  - {t}: {(parts.Count > 0 ? string.Join(", ", parts) : "NO POOLS")}");
            }
            sb.AppendLine("  - " + UnitVoices.Shared.Summary());
            return sb.ToString();
        }
    }
}
