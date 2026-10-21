using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    // ============================================================================================================================
    // DV108 hero squads, KC4b: the ALAN & BRAD squad's own voice lines (their running gags of the original cutscenes, each in its own
    // robot's voice), on top of the unit voices (AudioDirector.UnitVoices.cs). Only this squad says them; every other unit is as before.
    //  - select (UvSelect hook): one of the two (at random, the other one than last time twice as often) says one of its own lines;
    //    apart (one died, one sits in a vehicle or gun, the squad split: View/OzHeroPair.cs) only the one selected speaks, his own lines;
    //  - move / attack (UvOrder hook): one of its own move or attack lines; Alan's refusal stays the classic "No way!" (his voice),
    //    Brad has none (the second voice never answers a refusal), as for every unit of the second voice;
    //  - boarding a vehicle (EnteredLines hook, own or captured): Alan begs to drive ("Let me drive!") and, when Brad sits at the
    //    wheel or rides along, Brad answers right after him ("Too late!"); Brad alone: "Up, up, and away!"; Alan at the wheel: the
    //    classic driving lines (his own voice). A captured vehicle: the announcer's "Vehicle captured" as after a grunt's capture quip
    //    (OpenZVoiceV3.CaptureRachelToo after their exchange; no hero line started: the announcer as for a grunt's capture).
    // Every line goes through SayImpl on alan_main, so it never starts over another robot line (the speaking line finishes; the
    // channel's gap holds); Brad's answer starts only when Alan's line has ended and nothing else speaks. Each event's lines come
    // in a shuffled order and none comes back before every line of that event was said; a line counts as
    // said when it starts. The lines are the table events vo_select_hero_<member>, vo_order_hero_<member>_move / _attack,
    // vo_hero_<member>_board and vo_hero_brad_answer (Resources/OpenZ/Audio/Voice/Hero). No table rows: the old behaviour.
    // ============================================================================================================================
    public partial class AudioDirector
    {
        /// <summary>Play checks: hero lines started (select, move, attack, board, answers).</summary>
        public static int HeroLinesSaid;
        public static readonly int[] HeroLinesBy = new int[5];
        /// <summary>The chance that Brad answers Alan's boarding line.</summary>
        public static float HeroAnswerChance = 0.8f;
        /// <summary>Seconds between the end of Alan's boarding line and Brad's answer.</summary>
        public static float HeroAnswerGap = 0.2f;

        static readonly Dictionary<string, List<string>> hvBag = new Dictionary<string, List<string>>();
        static readonly Dictionary<string, string> hvLast = new Dictionary<string, string>();
        float hvLastOrder = -10f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void HvReset() { HeroLinesSaid = 0; HeroCaptures = HeroCaptureAnnounced = 0; for (int i = 0; i < HeroLinesBy.Length; i++) HeroLinesBy[i] = 0; hvBag.Clear(); hvLast.Clear(); }

        static string HvEvent(string member, string cat) =>
            cat == "select" ? "vo_select_hero_" + member : cat == "board" ? "vo_hero_" + member + "_board" : cat == "answer" ? "vo_hero_" + member + "_answer" : "vo_order_hero_" + member + "_" + cat;

        bool HvHas(string id) { var d = Def(id); return d != null && d.lines != null && d.lines.Count > 0 && d.loaded != null && d.loaded.Count > 0; }

        /// <summary>The next line of the event: a shuffled bag of its distinct lines, refilled when every line was said (never the
        /// last one again first).</summary>
        string HvNext(string id)
        {
            var d = Def(id); if (d == null || d.lines == null) return null;
            if (!hvBag.TryGetValue(id, out var bag) || bag.Count == 0)
            {
                bag = new List<string>();
                foreach (var s in d.lines) if (!string.IsNullOrEmpty(s) && !bag.Contains(s)) bag.Add(s);
                for (int i = bag.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); var x = bag[i]; bag[i] = bag[j]; bag[j] = x; }
                if (bag.Count > 1 && hvLast.TryGetValue(id, out var last) && bag[0] == last) { bag.RemoveAt(0); bag.Add(last); }
                hvBag[id] = bag;
            }
            return bag.Count > 0 ? bag[0] : null;
        }

        void HvStarted(string id, string line, int cat)
        {
            if (hvBag.TryGetValue(id, out var bag)) bag.Remove(line);
            hvLast[id] = line; HeroLinesSaid++; HeroLinesBy[cat]++;
            OzSoundLog.Add("VO", id, line, "HERO LINE");
        }

        /// <summary>Says the event's next line through SayImpl (never over a speaking line). True = it started.</summary>
        bool HvSay(string id, int cat)
        {
            var line = HvNext(id); if (line == null) return false;
            float t0 = Time.unscaledTime;
            SayImpl(id, "=" + line);
            if (lastLine.TryGetValue(id, out var lt) && lt >= t0 && alanMainId == id) { HvStarted(id, line, cat); return true; }
            return false;
        }

        /// <summary>Brad's answer to Alan's boarding line: right after it (the channel's gap is for new lines, not for the second half
        /// of one exchange), only when nothing speaks.</summary>
        void HvAnswer(string id)
        {
            var d = Def(id); var line = HvNext(id);
            if (d == null || line == null || !voices || alanMain == null) return;
            if (OpenZVictory.MutesVoice(id) || (ExternalVoiceBusy != null && ExternalVoiceBusy()) || !RobotChatter.Allows(id)) return;
            if (AlanBusy || ShuttleRachelLanding) { OzSoundLog.Add("dir", id, null, "dropped (a line speaks)"); return; }   // DV118: never over the shuttle landing line
            if (!PlayVoice(d, alanMain, "=" + line)) return;
            alanMainLast = Time.unscaledTime; alanMainId = id; alanMainUntil = Time.unscaledTime + Mathf.Max(0f, alanMainClipLeft());
            lastLine[id] = Time.unscaledTime;
            AddDuck(d.duckMusic != 0 ? d.duckMusic : -4f, 0f, 0f, 0f, VoiceLen(), 0.1f, 0.6f);
            HvStarted(id, line, 4);
        }

        string hvLastSpeaker;
        /// <summary>Who of the squad answers (a squad is selected by its leader, so Brad alone would always speak): a live member of the
        /// kind in this squad (View/OzHeroPair.cs: apart = only u), at random, the other one than last time twice as often.</summary>
        ZObject HvSpeaker(ZObject u)
        {
            var live = OzHeroPair.Mates(u);
            if (live.Count <= 1) return u;
            var other = live.Find(x => x.HeroMemberName != hvLastSpeaker) ?? live[0];
            var pick = Random.value < 0.67f ? other : live[Random.Range(0, live.Count)];
            hvLastSpeaker = pick.HeroMemberName;
            return pick;
        }

        /// <summary>UvSelect hook: a hero answers a selection with its own line. True = handled (the classic and type pools stay silent).</summary>
        bool HvSelect(ZObject u)
        {
            if (u == null || !u.IsHero) return false;
            string id = HvEvent(HvSpeaker(u).HeroMemberName, "select");
            if (!HvHas(id)) return false;
            if (AlanMainSpeaking) return true;                                                     // a quick re-click while it speaks stays silent
            HvSay(id, 0);
            return true;
        }

        /// <summary>UvOrder hook: a hero answers a move / attack order with its own line. True = handled (or silent on purpose).</summary>
        bool HvOrder(ZObject u, bool attack, bool noWay)
        {
            if (u == null || !u.IsHero || u.Waypoints.Count == 0) return false;
            string m = HvSpeaker(u).HeroMemberName;
            bool atk = attack || u.Waypoints[u.Waypoints.Count - 1].mode == WpMode.Attack;
            string id = HvEvent(m, atk ? "attack" : "move");
            if (!HvHas(id)) return false;
            if (noWay) return m != "alan";                                                         // Alan: the classic "No way!" (his voice); Brad: none
            if (AlanMainSpeaking && (alanMainClipLeft() > unitOrderWaitMax || UnitVoices.IsOrderLine(alanMainId))) return true;   // never late, never chained
            if (Time.unscaledTime - hvLastOrder < unitOrderGap) return true;                       // right-click spam: one answer
            hvLastOrder = Time.unscaledTime;
            HvSay(id, atk ? 2 : 1);
            return true;
        }

        /// <summary>EnteredLines hook: heroes got into the vehicle v (own or captured). True = a hero line was asked (the generic boarding
        /// lines stay silent); false = no hero aboard, or Alan alone at the wheel (the classic "I'm driving!" lines, his own voice).</summary>
        bool HvBoarded(ZObject v)
        {
            hvBoardSaid = false; hvExchangeLeft = 0f;
            if (v == null || v.Kind != ZKind.Vehicle || v.HeroesAboard == 0) return false;
            string driver = null; bool alanIn = false, bradIn = false;
            if (v.HeroDriverSeats != null)
                for (int i = 0; i < v.Drivers.Count && i < v.HeroDriverSeats.Count; i++)
                {
                    var s = v.HeroDriverSeats[i]; if (s == null || s.Kind == null || s.Member < 0 || s.Member >= s.Kind.Members.Length) continue;
                    var n = s.Kind.Members[s.Member]; if (i == 0) driver = n;
                    if (n == "alan") alanIn = true; if (n == "brad") bradIn = true;
                }
            if (v.HeroPassengerSeats != null)
                for (int i = 0; i < v.Passengers.Count && i < v.HeroPassengerSeats.Count; i++)
                {
                    var s = v.HeroPassengerSeats[i]; if (s == null || s.Kind == null || s.Member < 0 || s.Member >= s.Kind.Members.Length) continue;
                    var n = s.Kind.Members[s.Member]; if (n == "alan") alanIn = true; if (n == "brad") bradIn = true;
                }
            if (driver == "alan") return false;
            bool alanNear = alanIn;                                                                // Alan outside, by the vehicle Brad took (a jeep has one seat)
            if (!alanNear && game && game.World != null)
                foreach (var o in game.World.Objects)
                    if (o.IsHero && o.HeroMemberName == "alan" && o.Owner == v.Owner && !o.Destroyed && o.KillAt < 0
                        && (o.CenterX - v.CenterX) * (double)(o.CenterX - v.CenterX) + (o.CenterY - v.CenterY) * (double)(o.CenterY - v.CenterY) < 160.0 * 160.0) { alanNear = true; break; }
            string alanId = HvEvent("alan", "board"), bradId = HvEvent("brad", "board"), answerId = HvEvent("brad", "answer");
            if (alanNear && HvHas(alanId))
            {
                if (!HvSay(alanId, 3)) return true;                                                // something speaks: silent, never a generic line over the heroes
                hvBoardSaid = true; hvExchangeLeft = Mathf.Max(0f, alanMainClipLeft());
                if ((bradIn || driver == "brad") && HvHas(answerId) && Random.value < HeroAnswerChance)
                {
                    float wait = (alanMainClipLeft() + HeroAnswerGap) * Mathf.Max(0.01f, Time.timeScale);   // Schedule runs on game time
                    Schedule(wait, () => HvAnswer(answerId));
                    hvExchangeLeft += HeroAnswerGap + HeroAnswerLen;
                }
                return true;
            }
            if (bradIn && HvHas(bradId)) { hvBoardSaid = HvSay(bradId, 3); hvExchangeLeft = hvBoardSaid ? Mathf.Max(0f, alanMainClipLeft()) : 0f; return true; }
            return false;
        }

        /// <summary>The length of Brad's answer counted for the announcer's wait (s; the answers are under 2 s).</summary>
        public static float HeroAnswerLen = 2.0f;
        /// <summary>Seconds between the end of their exchange and the announcer's "Vehicle captured".</summary>
        public static float HeroAnnounceGap = 0.4f;
        bool hvBoardSaid; float hvExchangeLeft;
        /// <summary>Play checks: captures by the heroes, and how many of them the announcer followed.</summary>
        public static int HeroCaptures, HeroCaptureAnnounced;

        /// <summary>EnteredLines hook, a vehicle captured by the heroes (after HvBoarded said their lines): the announcer's "Vehicle
        /// captured" as for a grunt squad. Their line(s) said = the capture quip's path (with OpenZVoiceV3.CaptureRachelToo's chance,
        /// HeroAnnounceGap s after their exchange); nothing said = the plain capture path (always, after OpenZVoiceV3.CaptureRachelDelay).</summary>
        void HvCaptured()
        {
            HeroCaptures++;
            float ts = Mathf.Max(0.01f, Time.timeScale);
            if (!hvBoardSaid) { HeroCaptureAnnounced++; Schedule(OpenZVoiceV3.CaptureRachelDelay, () => Announce("ann_vehicle_captured")); return; }
            if (!OpenZVoiceV3.CaptureRachelToo()) return;
            HeroCaptureAnnounced++;
            Schedule((hvExchangeLeft + HeroAnnounceGap) * ts, () => Announce("ann_vehicle_captured"));
        }
    }
}
