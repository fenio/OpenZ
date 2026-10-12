using System;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    // ============================================================================================================================
    // music_v3 host: AudioDirector feeds MusicCore (plain C#, MusicCore.cs) and plays what it asks on 4 streaming voices of its
    // own. In a level MusicCore owns ALL the music (regular playlists, fort-attack drama, end music); the music_v2 A / B voices
    // then stay idle (SwitchMusic returns, the fixed end-music timers are skipped) and only play the main menu.
    //  - Inputs per frame: real dt (fades, playback), SIM seconds (the difference of W.Time: the attack holds, like the fort's hit
    //    record) and play seconds (real time while the game runs: the drama length and the interlude), oz_music, pause, Zod's
    //    danger state as the calm / battle mood (CALM -> calm; ATTACKING and FORT -> battle: one lone unit never brings the
    //    drama), a VIEW-SIDE reading of both forts every 0.25 s (MusicFortReader: reads W.Objects, writes nothing: the sim stays
    //    deterministic), the sim's game over, the end screen (the same test OpenZEndScreen uses), the sting length, Zod speaking
    //    (ZodBriefing.Speaking) and the shared music duck (Rachel / Alan).
    //  - Output: volume = MusicCore gain (fade x clip gain x the deepest duck) x master x the Music bus WITHOUT its duck
    //    (MusicCore already took the deepest of it and Zod's) x the pause dip; the 900 Hz pause low-pass like the A / B voices.
    //  - Zod: ZodBriefing no longer adds its -8 dB music duck (ZodMusicDuckOwned); his subtle eased duck is MusicCore's on these
    //    voices and zodBusDuck (same class, same rule) on the Music bus itself (BusGainDb hook: the deepest of it and the shared
    //    duck), so the menu music and the Music-bus one-shots (the victory / defeat stings) dip under him too.
    //  - Sound coverage: every track start counts its event (mus_<lv>_calm / _attacking, mus_fort_danger, mus_victory_lap,
    //    mus_menu) as a trigger and a play, like PlayMusic / SwitchMusic did.
    //  - Resume proof: 0.25 s after every unpause the voice must have advanced by that time (a restart or a jump shows at once).
    //  - Logs: every MusicCore event as "[MusicV3] ..." (Console) and OzSoundLog "MUS"; OpenZ > Music v3 menu (Status, forced
    //    attacks, Zod line, overlay, scenarios, track check).
    // ============================================================================================================================
    public partial class AudioDirector
    {
        [Header("Music v3 (fort-attack drama, resume, subtle Zod duck)")]
        [Tooltip("music_v3: MusicCore owns the in-game music (regular playlists + dramatic fort-attack music + end music); off = music_v2 as before (switchable in Play: the voices fade out, music_v2 takes over)")]
        public bool musicV3 = true;
        [Tooltip("music_v3: Zod's lines duck the music very subtly (MusicCoreConfig zodDuckDb, eased) instead of ZodBriefing's -8 dB")]
        public bool zodSubtleDuck = true;
        [Tooltip("music_v3: detector thresholds, fade times, Zod duck (MusicCore.cs)")]
        public MusicCoreConfig musicV3Config = new MusicCoreConfig();

        /// <summary>ZodBriefing (music_v3 patch): true = do not add the -8 dB music duck, the director applies its subtle one.</summary>
        public static bool ZodMusicDuckOwned => I && I.zodSubtleDuck;
        /// <summary>OpenZ > Music v3 > Overlay: a small status box in the Game view.</summary>
        public static bool MusicV3Overlay;

        bool MusicV3Owns => musicV3 && game != null && !mv3Failed;
        MusicCore mv3; UnityMusicOut mv3Out; bool mv3Failed, mv3Retiring;
        readonly MusicDuck zodBusDuck = new MusicDuck();
        float mv3NextRead; FortReading mv3Ours = FortReading.Quiet, mv3Theirs = FortReading.Quiet;
        float mv3ForceUntil = -1f; int mv3ForceKind; double mv3LastSimTime = -1;
        int mv3DramaSaved; bool mv3FirstSaved; public static string MusicFirstTrack;

        /// <summary>BusGainDb hook (music_v3): the Music bus duck = the deepest of the shared duck (Rachel / Alan) and Zod's subtle
        /// eased duck, never their sum. Without zodSubtleDuck ZodBriefing's own -8 dB duck is in duckMusic as before.</summary>
        float MusicDuckDb() => zodSubtleDuck ? Mathf.Min(duckMusic, zodBusDuck.Db(musicV3Config.zodDuckDb)) : duckMusic;

        /// <summary>Update hook (after MusicPlaylistTick).</summary>
        void MusicV3Tick(float dt)
        {
            var cfg = musicV3Config;
            zodBusDuck.Step(dt, zodSubtleDuck && ZodBriefing.Speaking, AudioListener.pause, cfg.zodAttack, cfg.zodRelease);
            if (!MusicV3Owns || game.World == null) { if (mv3 != null) RetireMusicV3(dt); return; }
            if (mv3 == null && !BuildMusicV3()) return;
            var w = game.World;
            bool paused = AudioListener.pause || Time.timeScale <= 0f || game.paused;
            double simNow = w.Time;
            float simDt = mv3LastSimTime < 0 ? 0f : (float)Math.Max(0.0, Math.Min(1.0, simNow - mv3LastSimTime));   // the sim's own seconds (game speed included)
            mv3LastSimTime = simNow;
            if (Time.unscaledTime >= mv3NextRead && !paused)
            {
                mv3NextRead = Time.unscaledTime + 0.25f;
                Team me = Player, them = me == Team.Red ? Team.Blue : Team.Red;
                mv3Ours = ReadFort(me); mv3Theirs = ReadFort(them);
                if (Time.unscaledTime < mv3ForceUntil)
                {   // OpenZ > Music v3 > Force ...: a synthetic reading for the Play proof (view only, the sim is untouched)
                    var big = new FortReading { alive = true, score = 9f, units = 5, outerScore = 9f, sinceHit = 0f };          // a big attack (not heavy)
                    var heavy = new FortReading { alive = true, score = 14f, units = 6, outerScore = 14f, sinceHit = 0f };      // >= 3 units worth >= 12, hitting
                    var one = new FortReading { alive = true, score = 5f, units = 1, outerScore = 5f, sinceHit = 0f };
                    var idle = new FortReading { alive = true, score = 0f, units = 0, outerScore = 12f, sinceHit = 999f };      // parked, not engaged
                    switch (mv3ForceKind)
                    {
                        case 1: mv3Ours = big; break;
                        case 2: mv3Theirs = big; break;
                        case 3: mv3Ours = one; break;
                        case 4: mv3Ours = heavy; break;
                        case 5: mv3Ours = idle; break;
                    }
                }
            }
            int result = w.Winner == Player ? 1 : w.Winner == Team.Neutral ? 0 : -1;
            var inp = new MusicInput
            {
                dt = Mathf.Min(dt, 0.1f), gameDt = paused ? 0f : simDt, playDt = paused ? 0f : Mathf.Min(dt, 0.1f), enabled = MusicOn, paused = paused, battle = danger != Danger.Calm,
                gameOver = w.GameOver, endScreen = w.GameOver && !OpenZVictory.HoldEndScreen, result = result,
                stingSeconds = w.GameOver && result != 0 ? ClipLen(result > 0 ? "ter_victory_sfx" : "ter_defeat_sfx") : 0f,
                zodSpeaking = zodSubtleDuck && ZodBriefing.Speaking, otherDuckDb = duckMusic, ours = mv3Ours, theirs = mv3Theirs,
            };
            mv3.Tick(inp);
            if (!mv3FirstSaved && !string.IsNullOrEmpty(mv3.FirstPicked))
            {
                mv3FirstSaved = true; MusicFirstTrack = mv3.FirstPicked;
                try { PlayerPrefs.SetString("oz_music_first_" + (City ? "l17" : "l02"), mv3.FirstPicked); } catch (Exception) { }
                Debug.Log($"[MusicV3] level start music: {mv3.FirstPicked} (random from the whole pool, not {mv3.AvoidFirst})");
            }
            if (mv3.DramaPicks != mv3DramaSaved) SaveDramaBag();                                              // a dramatic track was picked: the next match and the next start of the game go on from here
            float bus = masterVolume * Db(BusGainDb("Music") - MusicDuckDb() + pauseMusicDb);
            mv3Out.Apply(bus, pauseMusicDb < 0f);
        }

        /// <summary>musicV3 switched off (or the level lost its ZGame) with MusicCore live: its voices fade out 0.6 s and stop, then
        /// music_v2 takes over (UpdateMusic starts it: musicDef is null).</summary>
        void RetireMusicV3(float dt)
        {
            if (!mv3Retiring) { mv3Retiring = true; mv3.Retire(); }
            mv3.Tick(new MusicInput { dt = Mathf.Min(dt, 0.1f), enabled = false, ours = FortReading.Quiet, theirs = FortReading.Quiet });
            mv3Out.Apply(masterVolume * Db(BusGainDb("Music") - MusicDuckDb() + pauseMusicDb), pauseMusicDb < 0f);
            if (mv3.AudibleVoices > 0) return;
            mv3Out.StopAll();
            mv3 = null; mv3Retiring = false; mv3LastSimTime = -1;
            Debug.Log("[MusicV3] retired: the music_v3 voices are stopped, music_v2 plays");
            if (game != null && game.World != null && game.World.GameOver && MusicOn)                       // after the game over UpdateMusic no longer switches: music_v2's end music now
            { bool won = game.World.Winner == Player; PlayMusic(Def(EndMusicId(won)), -1, 2f, EndMusicDb(won)); }
        }

        /// <summary>The drama shuffle bag (MusicCore.DramaLast / DramaBag) goes into the saved settings after every pick, so the next
        /// match and the next start of the game go on in the same round and never open with the dramatic track heard last.</summary>
        void SaveDramaBag()
        {
            mv3DramaSaved = mv3.DramaPicks;
            try { PlayerPrefs.SetString("oz_music_drama_last", mv3.DramaLast); PlayerPrefs.SetString("oz_music_drama_bag", mv3.DramaBag); PlayerPrefs.Save(); } catch (Exception) { }
            Debug.Log($"[MusicV3] dramatic track picked: {mv3.DramaLast} (saved; still to play in this round: {(mv3.DramaBag.Length > 0 ? mv3.DramaBag : "none, the next pick opens a new round")})");
        }

        float ClipLen(string id) { var d = Def(id); return d != null && d.loaded.Count > 0 && d.loaded[0] ? d.loaded[0].length : 0f; }

        bool BuildMusicV3()
        {
            string lv = City ? "l17" : "l02";
            var tracks = new List<MusicTrack>(); var clips = new List<AudioClip>();
            var extra = MusicV3Extras();
            AddPool(tracks, clips, Def("mus_" + lv + "_calm"), MusicPool.Calm, extra);
            AddPool(tracks, clips, Def("mus_" + lv + "_attacking"), MusicPool.Battle, extra);
            AddPool(tracks, clips, Def("mus_fort_danger"), MusicPool.Drama, extra);
            AddPool(tracks, clips, Def("mus_victory_lap"), MusicPool.Victory, extra);
            AddPool(tracks, clips, Def("mus_menu"), MusicPool.Menu, extra);
            if (!tracks.Exists(t => t.pool == MusicPool.Calm) && !tracks.Exists(t => t.pool == MusicPool.Battle))
            { mv3Failed = true; Debug.LogWarning("[MusicV3] no calm / battle clips loaded: music_v2 plays instead"); return false; }
            if (musicA != null && (musicA.busy || musicB.busy)) { FadeMusicOut(1f); Debug.Log("[MusicV3] the music_v2 voices were playing: faded out 1 s (MusicCore takes over)"); }
            if (mv3Out == null) mv3Out = new UnityMusicOut(this); else mv3Out.StopAll();
            mv3Out.SetClips(clips);
            mv3 = new MusicCore(musicV3Config, mv3Out, tracks, Environment.TickCount);
            try { mv3.AvoidFirst = PlayerPrefs.GetString("oz_music_first_" + lv, ""); } catch (Exception) { }
            mv3FirstSaved = false;
            try { mv3.DramaLast = PlayerPrefs.GetString("oz_music_drama_last", ""); mv3.DramaBag = PlayerPrefs.GetString("oz_music_drama_bag", ""); } catch (Exception) { }   // the drama shuffle bag goes on where the last match left it
            mv3DramaSaved = mv3.DramaPicks;
            mv3LastSimTime = -1;
            int z = tracks.FindAll(t => t.zopen).Count;
            Debug.Log($"[MusicV3] live: {lv}, {tracks.Count} tracks (calm {mv3.PoolCount(MusicPool.Calm)}, battle {mv3.PoolCount(MusicPool.Battle)}, drama {mv3.PoolCount(MusicPool.Drama)}, " +
                      $"victory {mv3.PoolCount(MusicPool.Victory)}, menu {mv3.PoolCount(MusicPool.Menu)}; {z} added), music {(MusicOn ? "on" : "OFF (oz_music)")}, Zod duck {musicV3Config.zodDuckDb:0.0} dB");
            if (z == 0) Debug.LogWarning("[MusicV3] NO added (zopen) tracks in this level's playlists: the sound table lost them (was the music v2 import run again after music v3?). Import music v3 again, then OpenZ > Music v3 > Check tracks.");
            return true;
        }

        void AddPool(List<MusicTrack> tracks, List<AudioClip> clips, SoundEventDef d, MusicPool pool, Dictionary<string, MusicV3Meta> meta)
        {
            if (d == null || d.loaded == null) return;
            for (int i = 0; i < d.loaded.Count; i++)
            {
                var c = d.loaded[i]; if (!c) continue;
                var m = MetaOf(c);
                meta.TryGetValue(c.name, out var x);
                string line = d.lines != null && i < d.lines.Count ? d.lines[i] : "";
                tracks.Add(new MusicTrack
                {
                    id = clips.Count, name = c.name, pool = pool, length = c.length, tail = MusicXfade(c), gainDb = d.volDb + d.gains[i],
                    entries = m != null ? m.entries : null, zopen = line == "zopen", guest = line == "guest", eventId = d.id,
                    dramaIn = pool == MusicPool.Drama && x != null ? x.dramaIn : 0f, intensity = x != null ? x.intensity : 0f,
                });
                clips.Add(c);
            }
        }

        [Serializable] class MusicV3Meta { public string n; public float dramaIn, intensity; }
        [Serializable] class MusicV3MetaTable { public MusicV3Meta[] tracks; }
        Dictionary<string, MusicV3Meta> MusicV3Extras()
        {
            var r = new Dictionary<string, MusicV3Meta>();
            var ta = Resources.Load<TextAsset>(MusicMetaPath);
            if (ta) { var t = JsonUtility.FromJson<MusicV3MetaTable>(ta.text); if (t?.tracks != null) foreach (var m in t.tracks) if (!string.IsNullOrEmpty(m.n)) r[m.n] = m; }
            return r;
        }

        /// <summary>Sound coverage: a MusicCore track start counts its event like PlayMusic (plays) and SwitchMusic (triggers) did.</summary>
        void CountMusicV3(string eventId) { if (string.IsNullOrEmpty(eventId)) return; Count(Triggers, eventId); Count(Plays, eventId); Played++; }

        // ------------------------------------------------------------------ the view-side fort reading (MusicFortReader: never writes the sim)
        FortReading ReadFort(Team side) => MusicFortReader.Read(W, side, musicV3Config);

        // ------------------------------------------------------------------ public (menu / overlay / tests)
        public string MusicV3Status()
        {
            if (!MusicV3Owns) return "[MusicV3] not active here (" + (game == null ? "menu scene: music_v2 plays the menu" : mv3Failed ? "no clips" : "musicV3 off") + ")";
            if (mv3 == null) return "[MusicV3] starting";
            return "[MusicV3] " + mv3.Status() + $"  forts: ours {mv3Ours}, theirs {mv3Theirs}{(Time.unscaledTime < mv3ForceUntil ? $" (FORCED {mv3ForceKind}, {mv3ForceUntil - Time.unscaledTime:0} s left)" : "")}\n  " +
                   (mv3Out != null ? mv3Out.Describe() : "");
        }
        /// <summary>kind 1 = big attack on our fort (9 pts, 5 units, hitting), 2 = on the enemy fort, 3 = ONE unit shooting our fort (must NOT
        /// start the drama), 4 = HEAVY attack on our fort (14 pts, 6 units: may cut the interlude to 25 s), 5 = idle units parked by our
        /// fort (12 pts, not engaged: must never start or hold the drama), 0 = clear.</summary>
        public void MusicV3Force(int kind, float seconds) { mv3ForceKind = kind; mv3ForceUntil = kind == 0 ? -1f : Time.unscaledTime + seconds; mv3NextRead = 0f; Debug.Log($"[MusicV3] force {kind} for {seconds:0} s (view-side reading only)"); }
        public void MusicV3Skip() { if (mv3 != null) mv3.Skip(); else Debug.Log("[MusicV3] not active here (the main menu plays music_v2: use Music v2 > Skip)"); }
        public MusicCore MusicV3Core => mv3;

        void OnGUI()
        {
            if (!MusicV3Overlay || mv3 == null) return;
            var s = MusicV3Status();
            var r = new Rect(12, 12, 860, 18 + 15 * (1 + s.Split('\n').Length));
            GUI.color = new Color(0f, 0f, 0f, 0.72f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
            GUI.Label(new Rect(r.x + 8, r.y + 6, r.width - 16, r.height - 12), s);
        }

        // ------------------------------------------------------------------ the 4 streaming voices
        sealed class UnityMusicOut : IMusicOut
        {
            readonly AudioDirector ad; List<AudioClip> clips = new List<AudioClip>();
            readonly Voice[] v = new Voice[4]; readonly bool[] paused = new bool[4]; readonly float[] gain = new float[4], since = new float[4];
            readonly int[] parkSamples = new int[4]; readonly string[] name = new string[4];
            readonly float[] checkAt = new float[4], unpausedAt = new float[4]; readonly int[] unpausedSample = new int[4];
            public UnityMusicOut(AudioDirector ad) { this.ad = ad; for (int i = 0; i < 4; i++) checkAt[i] = -1f; }
            public void SetClips(List<AudioClip> c) { clips = c ?? new List<AudioClip>(); }

            AudioSource S(int i)
            {
                if (v[i] == null)
                {
                    v[i] = ad.NewVoice("MusicV3_" + i);
                    var s = v[i].src;
                    s.loop = false; s.spatialBlend = 0f; s.priority = 0; s.dopplerLevel = 0f; s.spread = 0f; s.volume = 0f;
                    s.ignoreListenerPause = true; v[i].ignorePause = true; v[i].lp.cutoffFrequency = 900f;
                }
                return v[i].src;
            }

            public void Play(int i, MusicTrack t, double at)
            {
                var s = S(i); var c = t.id >= 0 && t.id < clips.Count ? clips[t.id] : null;
                s.Stop(); paused[i] = false; gain[i] = 0f; s.volume = 0f; checkAt[i] = -1f;
                if (!c) { name[i] = null; return; }
                s.clip = c; name[i] = c.name; s.Play();
                s.time = Mathf.Clamp((float)at, 0f, Mathf.Max(0f, c.length - 0.05f));
                since[i] = UnityEngine.Time.unscaledTime;
                ad.CountMusicV3(t.eventId);
                OzSoundLog.Add("MUS", t.eventId ?? "music_v3", c.name, $"PLAY @ {at:0.0} s", 0f);
            }
            public void Pause(int i)
            {
                var s = S(i); parkSamples[i] = s.timeSamples; s.Pause(); paused[i] = true; checkAt[i] = -1f;
                OzSoundLog.Add("MUS", "music_v3", name[i], $"PAUSE @ {parkSamples[i]} smp", 0f);
            }
            public void Resume(int i)
            {
                var s = S(i); int before = s.timeSamples; s.UnPause(); paused[i] = false;
                float t = UnityEngine.Time.unscaledTime; since[i] = t;
                unpausedAt[i] = t; unpausedSample[i] = before; checkAt[i] = t + 0.25f;                  // the real proof comes 0.25 s later (Apply)
                Debug.Log($"[MusicV3] resume {name[i]}: unpaused at sample {before} (paused at {parkSamples[i]}); the playback check follows in 0.25 s");
                OzSoundLog.Add("MUS", "music_v3", name[i], $"RESUME @ {before} smp (paused {parkSamples[i]})", 0f);
            }
            public void Stop(int i) { var s = S(i); s.Stop(); s.clip = null; paused[i] = false; gain[i] = 0f; s.volume = 0f; name[i] = null; checkAt[i] = -1f; }
            public void Gain(int i, float g) { gain[i] = g; }
            public double Time(int i) { var s = S(i); return s.clip ? s.timeSamples / (double)s.clip.frequency : 0.0; }
            public bool Ended(int i) { var s = S(i); return !paused[i] && s.clip && !s.isPlaying && UnityEngine.Time.unscaledTime - since[i] > 0.5f; }
            public void Log(string line) => Debug.Log("[MusicV3] " + line);

            public void StopAll() { for (int i = 0; i < v.Length; i++) if (v[i] != null) Stop(i); }

            public void Apply(float bus, bool lp)
            {
                for (int i = 0; i < v.Length; i++)
                {
                    if (v[i] == null) continue;
                    v[i].src.volume = Mathf.Clamp01(gain[i] * bus);
                    v[i].lp.enabled = lp; v[i].lp.cutoffFrequency = 900f;
                    if (checkAt[i] >= 0f && UnityEngine.Time.unscaledTime >= checkAt[i]) ResumeCheck(i);
                }
            }

            /// <summary>0.25 s after an unpause: the clip must have moved on from the paused sample by about the time since (a restart
            /// reads near 0, a jump or a stuck voice reads far off).</summary>
            void ResumeCheck(int i)
            {
                checkAt[i] = -1f;
                var s = v[i].src; if (!s.clip) return;
                float el = UnityEngine.Time.unscaledTime - unpausedAt[i];
                int now = s.timeSamples, f = s.clip.frequency;
                long moved = (long)now - parkSamples[i], expect = (long)(el * f);
                long drift = (long)unpausedSample[i] - parkSamples[i];                                                // one DSP buffer may pass between the read and the pause (L17 proof: 941 smp = 21 ms)
                bool ok = Math.Abs(drift) <= (long)(0.05f * f) && moved > 0 && Math.Abs(moved - expect) <= (long)(0.1f * f) && s.isPlaying;
                string line = $"[MusicV3] resume check {name[i]}: paused at sample {parkSamples[i]}, {el:0.00} s after the unpause at sample {now}: moved {moved} samples ({moved / (double)f:0.000} s, expected {el:0.000} s; pause drift {drift * 1000.0 / f:0} ms, max 50) -> {(ok ? "OK (resumed, not restarted)" : "FAIL")}";
                if (ok) Debug.Log(line); else Debug.LogError(line);
                OzSoundLog.Add("MUS", "music_v3", name[i], ok ? "RESUME OK" : "RESUME FAIL", 0f);
            }

            public string Describe()
            {
                var sb = new System.Text.StringBuilder("sources:");
                for (int i = 0; i < v.Length; i++)
                {
                    if (v[i] == null) { sb.Append($" v{i} -"); continue; }
                    var s = v[i].src;
                    sb.Append($" v{i} {(s.clip ? s.clip.name : "-")} {(paused[i] ? "PAUSED" : s.isPlaying ? "on" : "off")} vol {s.volume:0.000} t {(s.clip ? s.time : 0f):0.0};");
                }
                return sb.ToString();
            }
        }
    }
}
