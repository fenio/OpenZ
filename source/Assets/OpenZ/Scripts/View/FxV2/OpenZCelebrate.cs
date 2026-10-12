using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The winners' crowd. OpenZVictory.StartCheer hands over the cheering
    /// robots; from then until the end screen, short Alan cheers ("Yeee-haa!", "Yeaaah!", "Oh yeah!", "We won!", "Who's the boss?" ...)
    /// come from single robots of that crowd, one robot at a time with its own pitch, positional (the source follows the robot, on
    /// CombatAudio's presence curve, SpatialBlend 0.85 so an off-centre crowd never goes silent), with the same light metallic robot
    /// filter as Alan's radio voice. When the CPU wins, its robots cheer and taunt the player ("You've blown it!", "My granny shoots
    /// straighter!"). "Blue's not your colour!" only when red beats blue.
    /// <list type="bullet">
    /// <item><b>Spaced</b>: a new cheer every BurstGap (first BurstSeconds) then Gap seconds, at most MaxConcurrent at once, at most
    /// MaxUnderLead (quieter by UnderLeadDb) while one of the sequence's own Alan lines plays; a robot speaks again only after
    /// RobotCooldown; a shuffle bag, no line twice in a row.</item>
    /// <item><b>Never over Rachel</b>: OpenZVictory sets HoldForRachel before each of Rachel's end lines (it also waits until Busy is
    /// false, max 2.5 s) and clears it when her line has finished; no cheer starts in between.</item>
    /// <item>Stops at the end screen (fades 0.5 s: the sting stays clean) and when the scene ends. Pause freezes it.</item>
    /// </list>
    /// Clips: Resources/OpenZ/Audio/Voice/alan_*_1/_2 (recorded "Alan", already imported); missing ones are skipped.
    /// Bus: AudioDirector's Voice/Robots gain (the VOICE slider). Status(): what played.
    /// </summary>
    [DefaultExecutionOrder(210)]                                                             // after OpenZCheer (200) lifted the robots
    public sealed class OpenZCelebrate : MonoBehaviour
    {
        public static OpenZCelebrate I { get; private set; }
        public static bool Enabled = true;
        public static float BurstSeconds = 3.2f, BurstGapMin = 0.3f, BurstGapMax = 0.7f, GapMin = 0.6f, GapMax = 1.5f;
        public static int MaxConcurrent = 2, MaxUnderLead = 1, Sources = 4;
        public static float VolumeDb = -2f, UnderLeadDb = -6f, SpatialBlend = 0.85f, RobotCooldown = 3f, WhoopShare = 0.45f, MaxSeconds = 22f;
        public static float PitchMin = 0.93f, PitchMax = 1.1f, PitchJitter = 0.025f, FadeOut = 0.5f;

        /// <summary>OpenZVictory: one of Rachel's end lines is due or playing: no new cheer starts.</summary>
        public static bool HoldForRachel;
        /// <summary>A cheer is playing (OpenZVictory waits for it before Rachel speaks).</summary>
        public static bool Busy => I && I.PlayingCount() > 0;
        public static int Played => I ? I.played : 0;

        const string VoiceRoot = "OpenZ/Audio/Voice/";
        /// <summary>Short whoops (either side).</summary>
        static readonly string[] Whoops = { "alan_yeehaa_win", "alan_yeah_win", "alan_oh_yeah", "alan_alright_win", "alan_yeehaw", "alan_woohoo_we_won",
            "alan_emo_yeehaa", "alan_emo_yeah", "alan_emo_woohoo_we_won", "alan_emo_yeehaa_smashed_em" };
        /// <summary>Victory lines (either side).</summary>
        static readonly string[] Cheers =
        {
            "alan_we_won", "alan_weve_done_it", "alan_nice_one_win", "alan_whos_the_boss", "alan_too_easy", "alan_easy_peasy",
            "alan_excellent", "alan_cool", "alan_wipe_out", "alan_smokin", "alan_thats_toast", "alan_this_patch_is_ours",
            "alan_whoa_look_at_that",
            "alan_we_did_it", "alan_nice_one_lads", "alan_easy_gg", "alan_what_are_you_doing_after_this", "alan_time_for_rocket_fuel",
            "alan_thats_how_its_done", "alan_party_at_the_factory", "alan_told_you_wed_win", "alan_best_day_ever",
            "alan_polish_my_trophy",
            "alan_il_cheers", "alan_il_total_wipeout",
            "alan_il_fuel_high_octane", "alan_il_fuel_boss_buying",
        };
        /// <summary>Only when the player won (his robots).</summary>
        static readonly string[] PlayerWon = { "alan_end_of_the_line", "alan_theyre_on_the_run" };
        /// <summary>Only when the CPU won: its robots taunt the player.</summary>
        static readonly string[] CpuWon = { "alan_youve_blown_it", "alan_is_that_all_youve_got", "alan_my_granny_shoots_straighter", "alan_you_call_that_shooting", "alan_come_on_tin_cans" };
        /// <summary>Only when red beat blue.</summary>
        static readonly string[] RedBeatBlue = { "alan_blues_not_your_colour", "alan_gg_blue" };
        /// <summary>
        /// Every robot speaks with the same Alan voice,
        /// so a CPU crowd shouting "Yeee-haa! Nice one!" sounded like OUR robots cheering our defeat. When the player lost, the crowd
        /// has no whoops and no victory lines: only Z's loss lines (despair + insults at the commander) and the CPU taunts above.</summary>
        static readonly string[] PlayerLost =
        {
            "alan_youre_crap", "alan_youve_blown_it", "alan_we_hate_you", "alan_moron", "alan_its_over", "alan_rubbish", "alan_i_want_my_mum",
            "alan_emo_nooo_weve_lost_it", "alan_emo_oh_no_weve_lost_it", "alan_emo_lost_it_all",
            "alan_lose_blown_it", "alan_lose_shambles", "alan_lose_commander", "alan_lose_stuffed", "alan_lose_disaster", "alan_lose_useless",
        };
        public static bool LossCrowd => I && I.lossCrowd;
        bool lossCrowd;

        sealed class Src { public AudioSource s; public AudioLowPassFilter lp; public Transform follow; public float baseVol, fade = 1f, fadeTarget = 1f; public bool busy; }
        sealed class Take { public string key; public AudioClip clip; }

        ZGame game; Team team = Team.Neutral; bool running, stopping; float startTime, nextAt;
        readonly List<UnitView> crowd = new List<UnitView>();
        readonly Dictionary<int, float> spokeAt = new Dictionary<int, float>();
        readonly List<Src> pool = new List<Src>();
        readonly List<Take> whoops = new List<Take>(), lines = new List<Take>();
        readonly List<Take> bagW = new List<Take>(), bagL = new List<Take>();
        string lastKey; int played; readonly List<string> log = new List<string>();

        // ================================================================================================ API
        /// <summary>OpenZVictory.StartCheer: the winners' cheering robots (on foot). Other units of the team stand in when none.</summary>
        public static void Begin(ZGame g, Team winner, IEnumerable<UnitView> robots)
        {
            if (!Enabled || g == null || winner == Team.Neutral) return;
            var c = g.GetComponent<OpenZCelebrate>(); if (!c) c = g.gameObject.AddComponent<OpenZCelebrate>();
            c.StartCrowd(g, winner, robots);
        }

        /// <summary>OpenZVictory.Release: the end screen is up: no new cheer, the playing ones fade out.</summary>
        public static void End() { HoldForRachel = false; if (I) I.StopCrowd(); }

        public static string Status() => I == null ? "OpenZCelebrate: not started"
            : $"OpenZCelebrate: {(I.running ? "running" : "stopped")}, team {I.team}, crowd {I.crowd.Count}, takes {I.whoops.Count} whoops + {I.lines.Count} lines, played {I.played}: {string.Join(" | ", I.log)}";

        // ================================================================================================ life
        void Awake() { if (I && I != this) { Destroy(this); return; } I = this; }
        void OnDestroy() { if (I == this) { I = null; HoldForRachel = false; } }

        void StartCrowd(ZGame g, Team winner, IEnumerable<UnitView> robots)
        {
            game = g; team = winner;
            crowd.Clear();
            if (robots != null) foreach (var v in robots) if (v && !crowd.Contains(v)) crowd.Add(v);
            if (crowd.Count == 0)
                foreach (var kv in g.Views) { var v = kv.Value; if (v && v.Obj != null && v.Obj.Owner == winner && v.Obj.IsUnit && !v.Obj.Destroyed) crowd.Add(v); }
            LoadBanks(winner == g.player, winner == Team.Red);
            BuildPool();
            running = whoops.Count + lines.Count > 0 && crowd.Count > 0; stopping = false;
            startTime = Time.time; nextAt = Time.time + 0.15f;
            Debug.Log($"[OpenZ] celebration: {crowd.Count} {winner} robots, {whoops.Count} whoop takes + {lines.Count} line takes{(running ? "" : " (nothing to play)")}");
        }

        void StopCrowd()
        {
            running = false;
            if (stopping) return; stopping = true;
            foreach (var p in pool) if (p.busy) p.fadeTarget = 0f;
        }

        // ================================================================================================ banks
        void LoadBanks(bool playerWon, bool redWon)
        {
            whoops.Clear(); lines.Clear(); bagW.Clear(); bagL.Clear();
            lossCrowd = !playerWon;
            if (lossCrowd)
            {
                foreach (var k in PlayerLost) Add(lines, k);
                foreach (var k in CpuWon) Add(lines, k);
                return;
            }
            foreach (var k in Whoops) Add(whoops, k);
            foreach (var k in Cheers) Add(lines, k);
            foreach (var k in playerWon ? PlayerWon : CpuWon) Add(lines, k);
            if (redWon) foreach (var k in RedBeatBlue) Add(lines, k);
        }

        static void Add(List<Take> to, string key)
        {
            for (int n = 1; n <= 2; n++)
            {
                var c = Resources.Load<AudioClip>(VoiceRoot + key + "_" + n);
                if (c) to.Add(new Take { key = key, clip = c });
            }
        }

        Take Next(bool whoop)
        {
            var bank = whoop ? whoops : lines; var bag = whoop ? bagW : bagL;
            if (bank.Count == 0) { bank = whoop ? lines : whoops; bag = whoop ? bagL : bagW; }
            if (bank.Count == 0) return null;
            if (bag.Count == 0) bag.AddRange(bank);
            for (int tries = 0; tries < 6; tries++)
            {
                int i = Random.Range(0, bag.Count);
                var t = bag[i];
                if ((t.key == lastKey || (OpenZVoiceV3.CrowdAvoids(t.key) && bag.Exists(x => x.key != lastKey && !OpenZVoiceV3.CrowdAvoids(x.key)))) && bag.Count > 1) continue;   // never the same line twice in a row; REVIEW (voice_lines_v3): never the lead's own victory lines
                bag.RemoveAt(i);
                bag.RemoveAll(x => x.key == t.key);                                                   // the other take of that line waits for the next round
                return t;
            }
            var f = bag[0]; bag.RemoveAt(0); return f;
        }

        // ================================================================================================ voices
        void BuildPool()
        {
            if (pool.Count > 0) return;
            for (int i = 0; i < Mathf.Max(1, Sources); i++)
            {
                var go = new GameObject("CelebrationVoice_" + i); go.transform.SetParent(transform, false);
                var a = go.AddComponent<AudioSource>(); a.playOnAwake = false; a.loop = false; a.dopplerLevel = 0f; a.priority = 24; a.spread = 30f;
                go.AddComponent<AudioHighPassFilter>().cutoffFrequency = 220f;                          // Alan's robot character (CombatAudio.VoiceSource)
                go.AddComponent<AudioDistortionFilter>().distortionLevel = 0.1f;
                var ch = go.AddComponent<AudioChorusFilter>(); ch.dryMix = 0.8f; ch.wetMix1 = 0.25f; ch.wetMix2 = 0.2f; ch.wetMix3 = 0f; ch.delay = 8f; ch.rate = 0.6f; ch.depth = 0.05f;
                var lp = go.AddComponent<AudioLowPassFilter>(); lp.cutoffFrequency = 22000f;
                pool.Add(new Src { s = a, lp = lp });
            }
        }

        int PlayingCount() { int n = 0; foreach (var p in pool) if (p.busy && p.s && p.s.isPlaying) n++; return n; }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float bus = AudioDirector.I ? AudioDirector.I.VoiceBusLinear("Voice/Robots") : 1f;
            foreach (var p in pool)
            {
                if (!p.busy) continue;
                if (!p.s.isPlaying && !AudioListener.pause && Time.timeScale > 0f) { p.busy = false; p.follow = null; continue; }
                if (p.follow) p.s.transform.position = p.follow.position + Vector3.up * 1.5f;
                p.fade = Mathf.MoveTowards(p.fade, p.fadeTarget, dt / Mathf.Max(0.05f, FadeOut));
                if (p.fade <= 0.001f) { p.s.Stop(); p.busy = false; p.follow = null; continue; }
                p.s.volume = Mathf.Clamp01(p.baseVol * p.fade * bus);
            }
            if (!running || Time.timeScale <= 0f) return;                                             // the pause menu freezes the crowd
            float now = Time.time;
            if (now - startTime > MaxSeconds) { StopCrowd(); return; }
            if (now < nextAt) return;
            if (HoldForRachel) { nextAt = now + 0.1f; return; }                                      // Rachel's end line: never under her
            bool lead = AudioDirector.VoiceBusy;                                                       // one of the sequence's own Alan lines
            if (PlayingCount() >= (lead ? MaxUnderLead : MaxConcurrent)) { nextAt = now + 0.12f; return; }
            if (PlayOne(lead, bus))
            {
                bool burst = now - startTime < BurstSeconds;
                nextAt = now + (burst ? Random.Range(BurstGapMin, BurstGapMax) : Random.Range(GapMin, GapMax));
            }
            else nextAt = now + 0.4f;                                                                  // nobody free to speak yet
        }

        bool PlayOne(bool underLead, float bus)
        {
            var v = PickRobot(); if (!v) return false;
            var take = Next(Random.value < WhoopShare); if (take == null) return false;
            Src src = null; foreach (var p in pool) if (!p.busy) { src = p; break; }
            if (src == null) return false;
            var a = src.s;
            a.clip = take.clip;
            a.spatialBlend = SpatialBlend;
            var ca = CombatAudio.I;
            if (ca) ca.Configure(a, src.lp, CombatAudio.Profile.PresenceL, 150f * ca.RangeScale);        // the world's presence curve (x zoom)
            else { a.rolloffMode = AudioRolloffMode.Logarithmic; a.minDistance = 18f; a.maxDistance = 220f; src.lp.cutoffFrequency = 22000f; }
            a.spatialBlend = SpatialBlend;                                                              // Configure sets 1: keep the 2D share
            int id = v.Obj != null ? v.Obj.Id : v.GetInstanceID();
            a.pitch = RobotPitch(id) * (1f + Random.Range(-PitchJitter, PitchJitter));
            src.baseVol = Mathf.Pow(10f, (VolumeDb + (underLead ? UnderLeadDb : 0f)) / 20f);
            src.fade = src.fadeTarget = 1f; src.busy = true; src.follow = v.transform;
            a.transform.position = v.transform.position + Vector3.up * 1.5f;
            a.volume = Mathf.Clamp01(src.baseVol * bus);
            a.Play();
            if (ca) ca.DuckFor(take.clip.length / Mathf.Max(0.5f, a.pitch) + 0.1f, -2f, -1f);          // the battle noise leans back a little
            spokeAt[id] = Time.time; lastKey = take.key; played++;
            if (log.Count < 40) log.Add($"{take.key}@{id}{(underLead ? " (under lead)" : "")}");
            OzSoundLog.Add("CELEB", take.key, take.clip.name, "PLAY Voice/Robots", a.volume, a.transform.position);
            return true;
        }

        /// <summary>A robot of the crowd that has not spoken for RobotCooldown: on screen first, then the nearest to the listener.</summary>
        UnitView PickRobot()
        {
            crowd.RemoveAll(v => !v || v.Obj == null || v.Obj.Destroyed || v.Dying || !v.isActiveAndEnabled);
            if (crowd.Count == 0) return null;
            var cam = Camera.main; var ca = CombatAudio.I;
            Vector3 lis = ca && ca.ListenerRig ? ca.ListenerRig.position : cam ? cam.transform.position : Vector3.zero;
            UnitView best = null; float bestScore = float.MaxValue; float now = Time.time;
            foreach (var v in crowd)
            {
                if (spokeAt.TryGetValue(v.Obj.Id, out var t) && now - t < RobotCooldown) continue;
                if (Speaking(v.transform)) continue;                                                    // one voice per robot
                bool onScreen = false;
                if (cam) { var vp = cam.WorldToViewportPoint(v.transform.position); onScreen = vp.z > 0f && vp.x > 0.02f && vp.x < 0.98f && vp.y > 0.02f && vp.y < 0.98f; }
                float d = (v.transform.position - lis).magnitude;
                float score = (onScreen ? 0f : 400f) + d + Random.Range(0f, 25f);                       // random among the near ones
                if (score < bestScore) { bestScore = score; best = v; }
            }
            return best;
        }

        bool Speaking(Transform t) { foreach (var p in pool) if (p.busy && p.follow == t) return true; return false; }

        static float RobotPitch(int id)
        {
            float x = Mathf.Sin((id + 3) * 12.9898f) * 43758.5453f; x -= Mathf.Floor(x);
            return Mathf.Lerp(PitchMin, PitchMax, x);
        }
    }
}
