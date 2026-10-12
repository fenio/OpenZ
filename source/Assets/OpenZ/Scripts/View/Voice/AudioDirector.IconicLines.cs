// OpenZ (GPL-3.0)
// Most of the lines simply joined existing pools through the sound table (order acks, the refusal, the game
// start, the nuke reaction, the help cries, vehicle enter / capture, fire out, the snipe, the victory cheers). Four moments had no
// event yet; this file gives them one, view-only and RARE (every one gated by chance + a long gap + Alan idle + Rachel quiet):
//   1. vo_vehicle_passenger: a squad of 2+ boards one of our vehicles: after the driver's own line a passenger complains ("Let me
//      drive, huh?", "Please, please let me drive.", "Hey, you said it was my turn to drive."). Hooked in EnteredLines.
//   2. vo_drive_scare: one of our crewed vehicles (2+ aboard, so there is a passenger) drives flat out on screen: "Slow down, slow
//      down, slow down.", "Careful.", "Whoa, careful, careful!", "I can't look.", "Whoa!"; when a robot (any side) is just ahead of it
//      also "Watch out, you're gonna hit that guy." / "Slow down, slow down. You're gonna hit that guy." (the "guy" lines only then).
//   3. vo_loud_gun: our heavy tank or howitzer fires on screen: its crew grumbles "Great. Shame it's so goddamn noisy." 0.6 s later.
//   4. vo_calm_chatter (radio, through RadioDiscipline like the help cries): a distress episode that called for help ended with the
//      group alive and unhit for 12 s: "Hey, as long as we're out of danger, who cares? Enjoy!" or "Close one. Premium blend,
//      please."; nobody of ours hit for 2 minutes and the music calm: "See, I told you this should be a nice little holiday for us.",
//      "Quiet day. Anyone got a can of fuel?" or "Hey! Who drank my rocket fuel?" (at most twice a game, 8 minutes apart).
// Every line goes through SayImpl: the speaking line is never cut (alan_main waits / drops, alan_alert speaks only into silence),
// the event bags never repeat a take twice in a row. Off in bot matches, test mode and automated checks. Reads the sim, never writes it.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    public partial class AudioDirector
    {
        public static bool IconicEnabled = true;
        /// <summary>Proof switch (IconicLinesProof.ForceHooks): every hook fires at its first eligible moment (chance 1, no gaps).</summary>
        public static bool IconicForce;
        public static float PassengerChance = 0.35f, PassengerGap = 60f;
        public static float DriveScareChance = 0.35f, DriveScareGap = 75f, DriveScarePerVehicle = 180f, DriveScareRetry = 45f, DriveFastSeconds = 3f;
        public static float LoudGunChance = 0.2f, LoudGunGap = 240f, LoudGunRetry = 30f;
        public static float ReliefChance = 0.5f, ReliefDelay = 12f, ReliefGap = 180f, ReliefWindow = 90f;
        public static float HolidayQuiet = 120f, HolidayChance = 0.6f, HolidayGap = 480f, HolidayMinGameTime = 90f; public static int HolidayMax = 2;
        public const string LineOutOfDanger = "Hey, as long as we're out of danger, who cares? Enjoy!";
        public const string LineHoliday = "See, I told you this should be a nice little holiday for us.";
        // pick from a small line bag (a line the table has, never the one this moment said last); the fuel jokes of the win and of the
        // crane's running gag need no code (they joined vo_victory_cheer / the victory crowd and vo_idle_x_crane through the table).
        public const string LineFuelQuietDay = "Quiet day. Anyone got a can of fuel?";
        public const string LineFuelWhoDrank = "Hey! Who drank my rocket fuel?";
        public const string LineFuelCloseOne = "Close one. Premium blend, please.";
        /// <summary>"out of danger" (the calm after a help call) and "holiday" (a long quiet stretch) line bags of vo_calm_chatter.</summary>
        public static readonly string[] ReliefLines = { LineOutOfDanger, LineFuelCloseOne };
        public static readonly string[] HolidayLines = { LineHoliday, LineFuelQuietDay, LineFuelWhoDrank };
        /// <summary>Hooks that asked for a line this session (the event's own Plays counter says whether it really started).</summary>
        public static int IconicPassengerAsks, IconicScareAsks, IconicLoudAsks, IconicReliefAsks, IconicHolidayAsks;

        ZGame iconicGame; float iconicNext, iconicHurtNext, iconicPassengerAt = -1000f, iconicScareAt = -1000f, iconicLoudAt = -1000f;
        float iconicLastHurt, iconicReliefAt = -1000f, iconicReliefSince, iconicHolidayAt = -1000f; int iconicHolidays;
        int iconicWatchId = -1, iconicWatchLevel, iconicReliefId = -1;
        string iconicReliefLast, iconicHolidayLast;
        readonly Dictionary<int, float> iconicFastSince = new Dictionary<int, float>(), iconicVehLast = new Dictionary<int, float>();
        readonly Dictionary<int, int> iconicHp = new Dictionary<int, int>();
        readonly HashSet<int> iconicSeen = new HashSet<int>();

        bool IconicAutomated => GameBoot.TestMode || CaptureKit.Enabled || GateBattery.Running
                                || (game && ((game.blueBot && game.redBot) || PlayerPrefs.GetInt("oz_botmatch", 0) == 1));
        bool IconicLive => IconicEnabled && voices && game != null && game.World != null && !W.GameOver && !game.paused && !gameOver
                           && !OpenZVictory.EndSequenceRunning && !AudioListener.pause && (IconicForce || !IconicAutomated);
        bool IconicRachel => (rachel != null && rachel.busy && rachel.src && rachel.src.isPlaying) || rachelQueue.Count > 0;
        bool IconicRoll(float chance) => IconicForce || UnityEngine.Random.value < chance;

        // ------------------------------------------------------------------ frame (Update hunk, after V3Tick)
        void IconicTick()
        {
            if (game != iconicGame)
            {   // a new level: fresh state (the static counters stay for the proof)
                iconicGame = game; iconicLastHurt = Time.unscaledTime; iconicHolidays = 0; iconicHolidayAt = iconicReliefAt = -1000f;
                iconicWatchId = iconicReliefId = -1; iconicFastSince.Clear(); iconicVehLast.Clear(); iconicHp.Clear();
            }
            if (!IconicLive) return;
            float now = Time.unscaledTime;
            if (now < iconicNext) return;
            iconicNext = now + 0.25f;
            if (now >= iconicHurtNext) { iconicHurtNext = now + 1f; IconicHurtScan(now); }
            IconicDrive(now);
            IconicCalm(now);
        }

        /// <summary>1 Hz: the last time one of our units lost health or died (a distress episode counts as being hit).</summary>
        void IconicHurtScan(float now)
        {
            if (distress != null) iconicLastHurt = now;
            iconicSeen.Clear();
            foreach (var o in W.Objects)
            {
                if (o.Owner != Player || !o.IsUnit) continue;
                iconicSeen.Add(o.Id);
                if (iconicHp.TryGetValue(o.Id, out var hp) && (o.Destroyed || o.Health < hp)) iconicLastHurt = now;
                iconicHp[o.Id] = o.Health;
            }
            List<int> gone = null;
            foreach (var kv in iconicHp) if (!iconicSeen.Contains(kv.Key)) (gone ??= new List<int>()).Add(kv.Key);
            if (gone == null) return;
            foreach (var id in gone)
            {
                var o = W.GetAny(id);
                if (o == null || o.Destroyed) iconicLastHurt = now;                                   // died (a robot that boarded is not hurt)
                iconicHp.Remove(id);
            }
        }

        // ------------------------------------------------------------------ 1. passenger (EnteredLines hunks)
        /// <summary>Our vehicle was boarded by a squad of 2+: after the driver's line (vo_vehicle_enter / the capture quip) a passenger
        /// complains. after = seconds after the boarding (the pending-line rule plays it right after the driver's line ends).</summary>
        void IconicBoarded(ZObject v, float after)
        {
            if (!IconicLive || v == null || v.Kind != ZKind.Vehicle || v.CrewAboard < 2 || !HasClips("vo_vehicle_passenger")) return;
            float now = Time.unscaledTime;
            if (!IconicForce && now - iconicPassengerAt < PassengerGap) return;
            iconicPassengerAt = now;
            if (!IconicRoll(PassengerChance)) return;
            Schedule(after, () =>
            {
                if (!IconicLive || v.Destroyed || v.Owner != Player || IconicRachel) return;
                IconicPassengerAsks++;
                Say("vo_vehicle_passenger");
            });
        }

        // ------------------------------------------------------------------ 2. reckless driving
        void IconicDrive(float now)
        {
            if (!HasClips("vo_drive_scare")) return;
            foreach (var v in W.Objects)
            {
                if (v.Kind != ZKind.Vehicle || v.Owner != Player || v.Destroyed || v.KillAt >= 0) continue;
                bool fast = v.IsMoving && v.MoveSpeed >= 14 && v.RealMoveSpeed >= 13.0;              // jeep 17, light / APC / crane 14 (Zod)
                if (!fast) { iconicFastSince.Remove(v.Id); continue; }
                if (!iconicFastSince.TryGetValue(v.Id, out var since)) iconicFastSince[v.Id] = since = now;
                if (v.CrewAboard < 2) continue;                                                       // nobody in the passenger seat
                if (!IconicForce && (now - iconicScareAt < DriveScareGap || (iconicVehLast.TryGetValue(v.Id, out var vl) && now < vl))) continue;
                if (!OnScreen(PosOf(v), 0.1f)) continue;
                bool guy = IconicGuyAhead(v);
                if (!guy && now - since < DriveFastSeconds) continue;
                if (AlanBusy || IconicRachel) continue;
                if (!IconicRoll(DriveScareChance)) { iconicVehLast[v.Id] = now + DriveScareRetry; continue; }   // this moment passed
                iconicVehLast[v.Id] = now + DriveScarePerVehicle; iconicScareAt = now;
                IconicScareAsks++;
                Say("vo_drive_scare", guy ? null : "!guy");                                          // "... hit that guy" only with a guy there
                return;
            }
        }

        /// <summary>A robot on foot (either side) within 3 m of the vehicle and in front of it (its heading from the sim's Dx / Dy).</summary>
        bool IconicGuyAhead(ZObject v)
        {
            float hx = v.Dx, hy = v.Dy, hl = Mathf.Sqrt(hx * hx + hy * hy);
            if (hl < 1e-4f) return false;
            foreach (var o in W.Objects)
            {
                if (o.Kind != ZKind.Robot || o.Destroyed || o.KillAt >= 0) continue;
                float dx = o.CenterX - v.CenterX, dy = o.CenterY - v.CenterY, d2 = dx * dx + dy * dy;
                if (d2 > 24f * 24f || d2 < 1f) continue;                                             // 24 px = 3 m
                if ((dx * hx + dy * hy) / (hl * Mathf.Sqrt(d2)) > 0.5f) return true;                 // within 60 deg of the heading
            }
            return false;
        }

        // ------------------------------------------------------------------ 3. loud gun (OnSimEvent hunk, after V3SimEvent)
        void IconicSimEvent(ZEvent e, ZObject a, ZObject b)
        {
            if (e.type != ZEventType.MissileFired || e.flag2 || a == null || a.Owner != Player) return;
            if (a.Type != "heavy" && a.Type != "howitzer") return;
            if (!IconicLive || !HasClips("vo_loud_gun")) return;
            float now = Time.unscaledTime;
            if (!IconicForce && now < iconicLoudAt) return;
            if (!OnScreen(PosOf(a), 0.1f) || AlanBusy || IconicRachel) return;
            if (!IconicRoll(LoudGunChance)) { iconicLoudAt = now + LoudGunRetry; return; }
            iconicLoudAt = now + LoudGunGap;
            IconicLoudAsks++;
            Schedule(0.6f, () => { if (IconicLive && !AlanBusy && !IconicRachel) Say("vo_loud_gun"); });   // after the boom
        }

        // ------------------------------------------------------------------ 4. calm (radio)
        void IconicCalm(float now)
        {
            if (!HasClips("vo_calm_chatter")) return;
            // a) out of danger: an episode that called at least once ended with the group alive; 12 s later, still unhit
            if (distress != null) { iconicWatchId = distress.id; iconicWatchLevel = distress.level; }
            else if (iconicWatchId >= 0)
            {
                var g = W.GetAny(iconicWatchId);
                if (iconicWatchLevel > 0 && g != null && !g.Destroyed && (IconicForce || now - iconicReliefAt >= ReliefGap)) { iconicReliefId = iconicWatchId; iconicReliefSince = now; }
                iconicWatchId = -1;
            }
            SyncRadio();
            if (iconicReliefId >= 0)
            {
                var g = W.GetAny(iconicReliefId);
                bool hitAgain = pressure.TryGetValue(iconicReliefId, out var pr) && pr.lastSeen > iconicReliefSince;
                if (g == null || g.Destroyed || hitAgain || distress != null || now - iconicReliefSince > ReliefWindow) iconicReliefId = -1;
                else if (now - iconicReliefSince >= ReliefDelay && now - iconicLastHurt >= ReliefDelay && Radio.CanCall(now) && RadioQuiet(now))
                {
                    iconicReliefId = -1; iconicReliefAt = now;
                    if (IconicRoll(ReliefChance))
                    {
                        var line = IconicPickLine(ReliefLines, iconicReliefLast);
                        if (line != null) { IconicReliefAsks++; if (IconicRadioLine("=" + line, now)) iconicReliefLast = line; }
                    }
                }
                return;
            }
            // b) a nice little holiday: nobody of ours hit for HolidayQuiet s, the music calm, the radio silent for a minute
            if (iconicHolidays >= HolidayMax || W.Time < HolidayMinGameTime || danger != Danger.Calm) return;
            if (now - iconicLastHurt < HolidayQuiet || (!IconicForce && now - iconicHolidayAt < HolidayGap)) return;
            if (!Radio.CanChatter(now) || !RadioQuiet(now)) return;
            iconicHolidayAt = now;                                                                    // one roll per quiet stretch
            if (IconicRoll(HolidayChance))
            {
                var line = IconicPickLine(HolidayLines, iconicHolidayLast);
                if (line != null) { iconicHolidays++; IconicHolidayAsks++; if (IconicRadioLine("=" + line, now)) iconicHolidayLast = line; }
            }
        }

        /// <summary>A random line of the bag that vo_calm_chatter really has (a loaded take), never the one said last when another is
        /// there; null = none loaded (then nothing is said: an unmatched "=line" filter would fall back to any take of the event).</summary>
        string IconicPickLine(string[] bag, string last)
        {
            var d = Def("vo_calm_chatter");
            if (d == null || d.lines == null || d.loaded == null) return null;
            var have = new List<string>(bag.Length);
            foreach (var l in bag) if (IconicHasLine(d, l)) have.Add(l);
            if (have.Count > 1 && last != null) have.Remove(last);
            return have.Count == 0 ? null : have[UnityEngine.Random.Range(0, have.Count)];
        }

        static bool IconicHasLine(SoundEventDef d, string line)
        {
            for (int i = 0; i < d.lines.Count && i < d.loaded.Count; i++)
                if (d.loaded[i] && string.Equals(d.lines[i], line, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Says a calm radio line; true = it started (then it counts as a radio line, no shut).</summary>
        bool IconicRadioLine(string filter, float now)
        {
            float before = lastLine.TryGetValue("vo_calm_chatter", out var lb) ? lb : -1f;
            Say("vo_calm_chatter", filter);
            if (lastLine.TryGetValue("vo_calm_chatter", out var la) && la > before) { Radio.Called(now, false); return true; }
            return false;
        }

        // ------------------------------------------------------------------ Play proof (IconicLinesProof)
        /// <summary>Says every take of the package through its own event and line filter, one after another (never over another line),
        /// and logs [IconicLines] OK / FAIL per take from the event's Plays counter.</summary>
        internal IEnumerator IconicProofRun(List<(string ev, string line)> takes, StringBuilder log, Action done)
        {
            foreach (var (ev, line) in takes)
            {
                float t0 = Time.unscaledTime;
                while ((AlanBusy || IconicRachel) && Time.unscaledTime - t0 < 8f) yield return null;
                int before = Plays.TryGetValue(ev, out var n0) ? n0 : 0;
                lastLine.Remove(ev);                                                                 // the proof paces itself
                Say(ev, "=" + line);
                yield return null;
                int after = Plays.TryGetValue(ev, out var n1) ? n1 : 0;
                string clip = alanMain.src.isPlaying && alanMain.def != null && alanMain.def.id == ev ? alanMain.src.clip.name
                            : alanAlert.src.isPlaying && alanAlert.def != null && alanAlert.def.id == ev ? alanAlert.src.clip.name : "-";
                string row = $"[IconicLines] {(after > before ? "OK  " : "FAIL")} {ev} \"{line}\" clip {clip}";
                log.AppendLine(row); Debug.Log(row);
                float t1 = Time.unscaledTime;
                while ((AlanBusy) && Time.unscaledTime - t1 < 6f) yield return null;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            done?.Invoke();
        }
    }

    /// <summary>
    ///   IconicLinesProof.PlayAll()        every new take through its event (voice on, not in the end sequence); IconicLinesProof.Last
    ///                                     holds the log when it is done ("[IconicLines] OK ..." per take, a FAIL = not played)
    ///   IconicLinesProof.ForceHooks(true) the 4 new moments fire at their first eligible moment (chance 1, no gaps; also in bot matches)
    ///   IconicLinesProof.Status()         the hooks' asks and the new events' plays
    /// </summary>
    public static class IconicLinesProof
    {
        public static readonly string[] NewEvents = { "vo_vehicle_passenger", "vo_drive_scare", "vo_loud_gun", "vo_calm_chatter" };
        public static readonly string[] Touched =
        {
            "vo_order_ack", "vo_select_generic", "vo_order_attack", "vo_order_no_way", "vo_game_start", "vo_nuke_reaction", "vo_under_attack_repeat",
            "vo_vehicle_enter", "vo_vehicle_captured", "vo_fire_out", "vo_snipe_success", "vo_victory_cheer",
            "vo_vehicle_passenger", "vo_drive_scare", "vo_loud_gun", "vo_calm_chatter",
            "vo_idle_x_crane",                                                                       // the rocket fuel joke of the crane's running gag
        };
        public static bool Running { get; private set; }
        public static string Last { get; private set; } = "";

        public static string PlayAll()
        {
            var ad = AudioDirector.I;
            if (!ad) return "no AudioDirector (enter Play on a level)";
            if (Running) return "already running";
            var takes = new List<(string, string)>();
            foreach (var ev in Touched)
            {
                var d = ad.Def(ev); if (d == null) { takes.Add((ev, "(event missing)")); continue; }
                var seen = new HashSet<string>();
                for (int i = 0; i < d.loaded.Count && i < d.lines.Count; i++)
                    if (d.loaded[i] && d.loaded[i].name.StartsWith("alan_il_") && seen.Add(d.lines[i])) takes.Add((ev, d.lines[i]));
            }
            var sb = new StringBuilder(); Running = true;
            ad.StartCoroutine(ad.IconicProofRun(takes, sb, () => { Running = false; Last = sb.ToString(); Debug.Log("[IconicLines] proof done\n" + Last); }));
            return $"playing {takes.Count} lines (~{takes.Count * 3} s); read IconicLinesProof.Last when Running is false";
        }

        public static void ForceHooks(bool on) { AudioDirector.IconicForce = on; Debug.Log("[IconicLines] force hooks " + on); }

        public static string Status()
        {
            var ad = AudioDirector.I; var sb = new StringBuilder();
            sb.Append($"asks: passenger {AudioDirector.IconicPassengerAsks}, drive scare {AudioDirector.IconicScareAsks}, loud gun {AudioDirector.IconicLoudAsks}, ");
            sb.Append($"out of danger {AudioDirector.IconicReliefAsks}, holiday {AudioDirector.IconicHolidayAsks}; plays:");
            foreach (var ev in NewEvents)
            {
                int n = ad && ad.Plays.TryGetValue(ev, out var k) ? k : 0; var d = ad ? ad.Def(ev) : null;
                sb.Append($" {ev} {n} ({(d == null ? "missing" : d.loaded.Count + " takes")})");
            }
            return sb.ToString();
        }
    }
}
