using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OpenZ
{
    // ============================================================================================================================
    // The in-game music as ONE plain C# state machine: no UnityEngine call in this file, so the headless harness
    // only feeds it inputs (time, the view-side fort readings, Zod's danger mood, game over / end screen, Zod speaking) and turns
    // its commands into AudioSource Play / Pause / UnPause / Stop / volume. It never writes sim state.
    //
    // Layers: Off -> Regular <-> ToDrama -> Drama -> ToRegular -> Regular ... ; End (the end screen: end music).
    //  - Regular: the shuffled playlists of music_v2 (calm / battle from Zod's danger state), each cycle alternating the level's
    //    own songs (one guest of the other level per cycle) with the added songs of the same mood ("zopen" clips); a song
    //    crossfades into the next at its measured tail; never the same song twice in a row; a mood switch crossfades 2 s and a
    //    song left within resumeWindow comes back where it was.
    //  - Drama (DramaDetector): a BIG attack on either fort (>= 2 engaged units worth >= 6, held 3 s on a leaky hold) fades the
    //    regular song out (3 s, equal power) and PAUSES it (its exact sample is kept), the drama fades in (2 s, 0.75 s later).
    //    It ends when no engaged attacker worth >= 3 is left for 12 s (24 / 36 / 48 s for the 2nd / 3rd / 4th drama of a siege)
    //    with no fort hit for 10 s, or when no big attack was seen for 60 s (a lone unit left shelling), and never before 30 s
    //    (45 / 60 / 75 / 90 s in a siege);
    //    then the drama fades out (4 s) and pauses and the SAME regular song unpauses 1 s later from the same sample (3 s in).
    //    Anti-flap: after a fade back the regular song plays at least 45 s before a new drama (25 / 35 / 45 s for a heavy attack
    //    in a siege: >= 3 units worth >= 12 hitting the fort); an attack that keeps coming back in waves makes each drama of the siege hold
    //    longer, so the waves end up in one drama. A drama track that ends mid-attack crossfades into a different one; a drama
    //    that comes back within 3 min continues its paused track.
    //  - Game over: the detector freezes. A drama that is playing (or was fading out less than half-way) plays on through the
    //    fort's fall, the nuke and the celebration; a big attack under way at the fall starts it. The end screen fades everything
    //    out (2.5 s) under the sting; the end music (victory lap then the menu songs / the menu songs) fades in 1 s before the
    //    sting ends.
    //  - oz_music off: every voice fades out 0.6 s; the regular, drama and end voices are PAUSED (kept); on again they resume
    //    where they were (nothing restarts).
    //  - Voices: 4 slots; a slot is Idle, Pending (a start / unpause waiting for its delay), Playing or Paused (a silent
    //    bookmark). At most two slots are audible, and two only while one of them fades. Every transition retargets the current
    //    levels (a reversal mid-fade continues from where it is: no jumps); a voice already on its way out when a new
    //    transition starts is sped up (0.6 s) so a third voice never sounds.
    //  - Zod: a subtle duck (-2.5 dB, eased 0.8 s in / 1.5 s out) only while his window is up; with the other voice ducks the
    //    deepest wins (never summed); while the game is paused only the release runs.
    // ============================================================================================================================

    public enum MusicLayer { Off, Regular, ToDrama, Drama, ToRegular, End }
    public enum MusicPool { Calm, Battle, Drama, Victory, Menu }

    /// <summary>One playable music clip of this level (the host maps id -> AudioClip).</summary>
    public sealed class MusicTrack
    {
        public int id; public string name; public MusicPool pool;
        public float length, tail = 4f, gainDb, dramaIn; public float[] entries;
        /// <summary>Measured intensity (music_v2 scale: level, onsets, tempo, brightness; openz_music_meta.json "intensity").</summary>
        public float intensity;
        /// <summary>The sound event this clip came from (the sound coverage report counts its triggers / plays).</summary>
        public string eventId;
        /// <summary>zopen = one of the added zopenmusic / zopendramatic tracks (sound table line "zopen"); guest = music_v2 guest.</summary>
        public bool zopen, guest;
        public override string ToString() => name;
    }

    /// <summary>A view-side reading of one fort (never written back): alive, the weighted strength and count of hostile mobile
    /// units ENGAGED at it (within rIn and attacking something of that side near the fort, or heading into the fort area; any
    /// within rCore while the fort takes hits), the strength of every hostile mobile unit within rOut (overlay only: no rule
    /// reads it), and the game seconds since the fort last took an enemy hit.</summary>
    public struct FortReading
    {
        public bool alive; public float score, outerScore, sinceHit; public int units;
        public static FortReading Quiet => new FortReading { alive = true, sinceHit = 999f };
        public override string ToString() => alive ? $"{score:0.#}/{units}u (out {outerScore:0.#}, hit {(sinceHit >= 999f ? "-" : sinceHit.ToString("0.0", CultureInfo.InvariantCulture) + " s")})" : "no fort";
    }

    public struct MusicInput
    {
        /// <summary>dt: real (unscaled) seconds of this frame: fades and playback. gameDt: SIM seconds of this frame (0 while
        /// paused): the attack holds (enter / exit / hits). playDt: real seconds while the game runs (0 while paused): the musical
        /// minimums (drama length, the interlude after a fade back); 0 = use gameDt.</summary>
        public float dt, gameDt, playDt;
        public bool enabled, paused, battle, gameOver, endScreen, zodSpeaking;
        /// <summary>+1 win, -1 loss, 0 draw (read at the end screen).</summary>
        public int result;
        /// <summary>Length of the end-screen sting (0 = none, a draw).</summary>
        public float stingSeconds;
        /// <summary>The director's shared music duck (Rachel / Alan lines), dB &lt;= 0.</summary>
        public float otherDuckDb;
        public FortReading ours, theirs;
    }

    /// <summary>What the state machine asks of the audio side. Voices are 0..3. Play starts at volume 0.</summary>
    public interface IMusicOut
    {
        void Play(int voice, MusicTrack track, double at);
        void Pause(int voice);
        void Resume(int voice);
        void Stop(int voice);
        void Gain(int voice, float linear);
        double Time(int voice);
        /// <summary>True once a started, unpaused clip has played to its end by itself.</summary>
        bool Ended(int voice);
        void Log(string line);
    }

    [Serializable]
    public sealed class MusicCoreConfig
    {
        // ---- drama detector (Zod px, 1 tile = 16 px; weights of hostile mobile units; cannons, cranes and the fort defender never count)
        /// <summary>rIn: attackers counted within 16 tiles; rCore: the fort area (11 tiles) their target / waypoint must be in;
        /// rOut: the overlay's "out" figure only.</summary>
        public float rIn = 256f, rCore = 176f, rOut = 320f;
        public float wRobot = 1f, wJeep = 2f, wApc = 2f, wLight = 3f, wMedium = 4f, wMissile = 4f, wHeavy = 5f, wOther = 2f;
        public int minUnits = 2;
        /// <summary>HOT: >= minUnits engaged worth >= hotScore (>= hotScoreUnderFire while the fort took a hit within hitWindow s).
        /// WARM (the drama holds): engaged strength >= warmScore. HEAVY (may cut the interlude short): >= heavyUnits worth >= heavyScore hitting the fort.</summary>
        public float hotScore = 6f, hotScoreUnderFire = 4f, hitWindow = 4f, warmScore = 3f, calmAfterHit = 10f;
        public float heavyScore = 12f; public int heavyUnits = 3;
        /// <summary>enterHold: hot game seconds to start (leaky: a cold reading drains the hold as fast as a hot one fills it, hotDecay 1:
        /// an attack hot more than half of the time builds up, one below half never does).</summary>
        public float enterHold = 3f, hotDecay = 1f;
        /// <summary>The fade back: exitHold seconds without a warm fort (counted on BOTH clocks: never sooner in sim nor in real
        /// time; + exitHoldChain per earlier drama of the same siege, i.e. one that ended within siegeMemory s, up to exitHoldMax),
        /// no hit for calmAfterHit s, and at least minDrama s of drama (+ minDramaChain per earlier drama of the siege, up to
        /// minDramaMax); or hotLapse game seconds without a hot reading (a lone unit left shelling never holds the drama).</summary>
        public float exitHold = 12f, exitHoldChain = 12f, exitHoldMax = 48f, minDrama = 30f, minDramaChain = 15f, minDramaMax = 90f, hotLapse = 60f, siegeMemory = 180f;
        /// <summary>Anti-flap (play seconds after a fade back began): a new drama waits for cooldown s; a heavy attack may come
        /// back after heavyInterlude s (+ heavyInterludeChain per earlier drama of the siege, up to cooldown), so the resumed song is
        /// really heard before the next switch and a siege of heavy waves switches less and less.</summary>
        public float cooldown = 300f, heavyInterlude = 25f, heavyInterludeChain = 10f;   // cooldown: at most one drama per 5 minutes
        // ---- transitions (real seconds)
        public float toDramaOut = 3f, toDramaIn = 2f, toDramaDelay = 0.75f;
        public float toRegularOut = 4f, toRegularIn = 3f, toRegularDelay = 1f;
        public float moodXfade = 2f, moodGrace = 20f, resumeWindow = 180f, resumeMinLeft = 10f;
        public float dramaResumeWindow = 180f, dramaResumeMinLeft = 30f;
        public float minXfade = 2.5f, maxXfade = 8f, quickOut = 0.6f, startFade = 2f, endedFade = 1f, offFade = 0.6f;
        public float endFade = 2.5f, endLead = 0.3f, endFadeIn = 1f, endNoSting = 3f;
        /// <summary>dramaDb: the drama sits this much above the regular songs (the attack must never sound weaker than what it replaces).</summary>
        public float dramaDb = 1.5f, victoryDb = -3f, menuDb = -6f;
        /// <summary>Not read by the drama pick any more (the drama pool is a shuffle bag: MusicCore.PickDrama); the scenario report
        /// still counts with it how many dramas are weaker than the song they replace.</summary>
        public float dramaIntensityTol = 0.15f;
        // ---- game over
        /// <summary>At the game over the drama plays on when it is on, when its fade back is less than half-way
        /// (gameOverReverseLevel), or when a big attack has been hot for gameOverHotHold s at the fall; never within gameOverGrace
        /// s of a fade back (no drama -> regular -> drama). dramaOnGameOver (off by default): a drama for every game
        /// over, gameOverDramaDelay s after the fall.</summary>
        public float gameOverReverseLevel = 0.5f, gameOverHotHold = 1f, gameOverGrace = 20f, gameOverDramaDelay = 2.5f;
        public bool dramaOnGameOver = false;
        // ---- Zod's duck (very subtle, eased)
        public float zodDuckDb = -2.5f, zodAttack = 0.8f, zodRelease = 1.5f;
    }

    /// <summary>Eased duck: amount 0..1 moves linearly (attack / release seconds for the whole way), the dB follows smoothstep.
    /// Continuous and reversible mid-way; while the game is paused the attack is frozen but a release still runs (the music never
    /// stays ducked through a pause).</summary>
    public sealed class MusicDuck
    {
        public float amount;
        public void Step(float dt, bool on, bool paused, float attack, float release)
        {
            if (dt <= 0f || (paused && on)) return;
            amount += (on ? dt / Math.Max(0.05f, attack) : -dt / Math.Max(0.05f, release));
            amount = amount < 0f ? 0f : amount > 1f ? 1f : amount;
        }
        public float Db(float depth) => depth * amount * amount * (3f - 2f * amount);
    }

    /// <summary>Big-attack hysteresis on both forts (frozen while paused and after game over). Attack holds run on sim seconds,
    /// the musical minimums (drama length, interlude, siege memory) on play seconds.</summary>
    public sealed class DramaDetector
    {
        public bool Wanted { get; private set; }
        /// <summary>1 our fort, 2 their fort (the one that is hot, ours first), 0 none.</summary>
        public int Fort { get; private set; }
        public float HotFor { get; private set; }
        public float ColdFor { get; private set; }
        public float DramaFor { get; private set; }
        public float SinceReturn { get; private set; } = 999f;
        public float SinceHot { get; private set; } = 999f;
        public int Starts { get; private set; }
        public int Ends { get; private set; }
        /// <summary>Earlier dramas of this siege (each ended within siegeMemory s of the next start).</summary>
        public int Chain { get; private set; }
        /// <summary>Why the last flip happened (log / overlay).</summary>
        public string Why { get; private set; } = "";

        /// <summary>HOT: >= minUnits engaged hostile units with a weighted strength >= hotScore (>= hotScoreUnderFire while the fort
        /// took an enemy hit within hitWindow s). One lone unit, however strong, is never hot.</summary>
        public static bool Hot(in FortReading f, MusicCoreConfig c) =>
            f.alive && f.units >= c.minUnits && (f.score >= c.hotScore || (f.sinceHit <= c.hitWindow && f.score >= c.hotScoreUnderFire));
        /// <summary>HEAVY: >= heavyUnits engaged worth >= heavyScore while the fort takes hits (may shorten the interlude).</summary>
        public static bool Heavy(in FortReading f, MusicCoreConfig c) => f.alive && f.units >= c.heavyUnits && f.score >= c.heavyScore && f.sinceHit <= c.hitWindow;
        /// <summary>WARM: engaged strength >= warmScore (the drama holds; idle units nearby never count, they are not engaged).</summary>
        public static bool Warm(in FortReading f, MusicCoreConfig c) => f.alive && f.score >= c.warmScore;
        /// <summary>LOW (kept for callers): not warm.</summary>
        public static bool Low(in FortReading f, MusicCoreConfig c) => !Warm(f, c);
        /// <summary>CALM: no enemy hit on the fort for calmAfterHit s (a gun shelling it from afar keeps the drama).</summary>
        public static bool Calm(in FortReading f, MusicCoreConfig c) => !f.alive || f.sinceHit >= c.calmAfterHit;

        public float ExitHoldNow(MusicCoreConfig c) => Math.Min(c.exitHoldMax, c.exitHold + c.exitHoldChain * Chain);
        public float MinDramaNow(MusicCoreConfig c) => Math.Min(c.minDramaMax, c.minDrama + c.minDramaChain * Chain);
        /// <summary>The interlude a heavy attack must wait now (the siege so far: Chain of the drama that just ended).</summary>
        public float HeavyInterludeNow(MusicCoreConfig c) => Math.Min(c.cooldown, c.heavyInterlude + c.heavyInterludeChain * Chain);
        /// <summary>True while a new drama must wait (the regular interlude after a fade back).</summary>
        public bool Waiting(MusicCoreConfig c) => !Wanted && Ends > 0 && SinceReturn < c.cooldown;

        /// <summary>One clock for both (the calibration tools).</summary>
        public bool Step(float dt, in FortReading ours, in FortReading theirs, MusicCoreConfig c) => Step(dt, dt, ours, theirs, c);

        /// <summary>-> true when Wanted flipped this step.</summary>
        public bool Step(float gameDt, float playDt, in FortReading ours, in FortReading theirs, MusicCoreConfig c)
        {
            if (gameDt <= 0f && playDt <= 0f) return false;
            bool hotO = Hot(ours, c), hotT = Hot(theirs, c), hot = hotO || hotT;
            SinceReturn += playDt;
            SinceHot = hot ? 0f : SinceHot + gameDt;
            HotFor = hot ? Math.Min(c.enterHold, HotFor + gameDt) : Math.Max(0f, HotFor - c.hotDecay * gameDt);   // leaky, never banked beyond the hold
            if (!Wanted)
            {
                if (HotFor < c.enterHold) return false;
                bool free = Ends == 0 || SinceReturn >= c.cooldown;
                if (!free) return false;                                                            // the interlude between two dramas: no exception, a heavy attack waits too
                Wanted = true; Fort = hotO ? 1 : 2; ColdFor = 0f; Starts++; DramaFor = 0f;
                Chain = Ends > 0 && SinceReturn <= c.siegeMemory ? Chain + 1 : 0;
                Why = !free ? $"heavy attack ({SinceReturn:0} s after the last fade back)" : Chain > 0 ? $"big attack (drama {Chain + 1} of this siege)" : "big attack";
                return true;
            }
            DramaFor += playDt;
            if (hotO) Fort = 1; else if (hotT) Fort = 2;
            bool warm = Warm(ours, c) || Warm(theirs, c);
            ColdFor = warm ? 0f : ColdFor + (playDt > 0f ? Math.Min(gameDt, playDt) : gameDt);        // calm on both clocks (2x speed: real seconds)
            if (DramaFor < MinDramaNow(c)) return false;
            bool over = ColdFor >= ExitHoldNow(c) && Calm(ours, c) && Calm(theirs, c);
            bool lapse = SinceHot >= c.hotLapse;
            if (!over && !lapse) return false;
            Why = over ? $"calm {ColdFor:0} s (hold {ExitHoldNow(c):0} s), drama {DramaFor:0} s (min {MinDramaNow(c):0} s)" : $"no big attack for {SinceHot:0} s, drama {DramaFor:0} s";
            Wanted = false; SinceReturn = 0f; HotFor = 0f; Ends++;
            return true;
        }
    }

    public sealed class MusicCore
    {
        enum SlotState { Idle, Pending, Playing, Paused }
        enum AtSilence { Stop, Park }
        sealed class Slot
        {
            public int idx; public MusicTrack track; public SlotState state; public AtSilence atSilence;
            public float level, target, rate, extraDb;
            public double startAt, startPos, parkedAt, parkedPos; public bool pendingResume;
        }
        sealed class Bag
        {
            public readonly List<MusicTrack> own = new List<MusicTrack>(), guests = new List<MusicTrack>(), zopen = new List<MusicTrack>();
            public readonly List<MusicTrack> orderA = new List<MusicTrack>(), orderB = new List<MusicTrack>();
            public readonly HashSet<MusicTrack> played = new HashSet<MusicTrack>();
            public bool nextZopen; public MusicTrack last, prev, lastGuest, lastA, lastB;
            public int Count => own.Count + guests.Count + zopen.Count;
        }
        struct Memo { public MusicTrack track; public double pos, at; }

        public readonly MusicCoreConfig C;
        readonly IMusicOut O;
        readonly Random rnd;
        readonly Slot[] slots = new Slot[4];
        readonly Bag[] bags = new Bag[5];
        readonly Memo[] memo = new Memo[5];
        readonly List<MusicPool> endSeq = new List<MusicPool>();
        Slot reg, drama, endS;
        double now, endMusicAt, gameOverAt = -1, layerSince, moodGraceUntil, endScreenSince = -1, endDramaAt;
        int endIdx; bool firstRegular = true, lastBattle, gameOverSeen, zodWas, endEntered, endDrama, retired;
        float duckDbNow;

        public MusicLayer Layer { get; private set; } = MusicLayer.Off;
        public readonly DramaDetector Detector = new DramaDetector();
        public readonly MusicDuck ZodDuck = new MusicDuck();
        public double Now => now;
        /// <summary>The duck applied to every voice this frame (the deepest of Zod's and the director's), dB.</summary>
        public float DuckDb => duckDbNow;
        /// <summary>Checks: started tracks, unpauses, pauses, stolen voices (must stay 0), same track twice in a row (must stay 0).</summary>
        public int Starts { get; private set; }
        public int Resumes { get; private set; }
        public int Parks { get; private set; }
        public int Steals { get; private set; }
        public int BackToBack { get; private set; }
        public int DramaStarts { get; private set; }
        public int DramaEnds { get; private set; }
        public MusicTrack LastStarted { get; private set; }
        /// <summary>After the game over: whether the drama carries the ending (latched on the game-over frame).</summary>
        public bool GameOverSeen => gameOverSeen;
        public bool EndDrama => endDrama;
        public readonly List<string> History = new List<string>();

        public MusicCore(MusicCoreConfig config, IMusicOut output, IList<MusicTrack> tracks, int seed)
        {
            C = config ?? new MusicCoreConfig(); O = output; rnd = new Random(seed);
            for (int i = 0; i < slots.Length; i++) slots[i] = new Slot { idx = i };
            for (int i = 0; i < bags.Length; i++) bags[i] = new Bag();
            foreach (var t in tracks)
            {
                var b = bags[(int)t.pool];
                (t.zopen ? b.zopen : t.guest ? b.guests : b.own).Add(t);
            }
            foreach (var b in bags) if (b.own.Count == 0 && b.guests.Count > 0) { b.own.AddRange(b.guests); b.guests.Clear(); }
        }

        public int PoolCount(MusicPool p) => bags[(int)p].Count;
        public int AudibleVoices => AudibleCount();

        // ================================================================================================================ tick
        public void Tick(in MusicInput i)
        {
            float dt = Math.Min(0.25f, Math.Max(0f, i.dt));                                      // a load hitch never jumps a fade
            now += dt;
            if (retired) { StepSlots(dt); return; }
            lastBattle = i.battle;
            if (i.endScreen && endScreenSince < 0) endScreenSince = now;
            ZodDuck.Step(dt, i.zodSpeaking, i.paused, C.zodAttack, C.zodRelease);
            if (i.zodSpeaking != zodWas) { zodWas = i.zodSpeaking; Note(i.zodSpeaking ? "zod speaks: music duck " + C.zodDuckDb.ToString("0.0", CultureInfo.InvariantCulture) + " dB" : "zod done: duck releases"); }
            if (i.gameOver && !gameOverSeen) LatchGameOver();
            float gdt = i.paused ? 0f : Math.Max(0f, i.gameDt), pdt = i.paused ? 0f : (i.playDt > 0f ? i.playDt : gdt);
            if (!i.gameOver && Detector.Step(gdt, pdt, i.ours, i.theirs, C))
                Note(Detector.Wanted ? $"BIG ATTACK on {(Detector.Fort == 1 ? "our" : "their")} fort: {Detector.Why} (ours {i.ours}, theirs {i.theirs})" : $"attack over ({Detector.Why})");
            bool wantDrama = PoolCount(MusicPool.Drama) > 0 && (gameOverSeen ? endDrama && now >= endDramaAt : Detector.Wanted);   // no drama clips: the regular music just goes on

            if (!i.enabled)
            {
                if (Layer != MusicLayer.Off) GoOff();
            }
            else
            {
                if (i.endScreen && !endEntered) EnterEnd(i);
                switch (Layer)
                {
                    case MusicLayer.Off:
                        if (AudibleCount() > 0) break;                                               // an off-fade still sounding
                        if (endEntered) ResumeEnd();
                        else if (wantDrama) ResumeOrStartDrama();
                        else ResumeOrStartRegular();
                        break;
                    case MusicLayer.Regular:
                        if (wantDrama) { BeginToDrama(); break; }
                        RegularTick(i);
                        break;
                    case MusicLayer.ToDrama:
                        if (!wantDrama) { BeginToRegular(); break; }
                        DramaTick();
                        if (drama != null && drama.state == SlotState.Playing && drama.level >= 1f && !OthersAudible(drama)) SetLayer(MusicLayer.Drama);
                        break;
                    case MusicLayer.Drama:
                        if (!wantDrama) { BeginToRegular(); break; }
                        DramaTick();
                        break;
                    case MusicLayer.ToRegular:
                        if (wantDrama) { BeginToDrama(); break; }
                        if (reg != null && reg.state == SlotState.Playing && O.Ended(reg.idx)) { Release(reg); reg = null; }
                        if (reg == null) { StartRegular(lastBattle ? MusicPool.Battle : MusicPool.Calm, 0.0, C.endedFade, 0.0, false); }
                        if (reg != null && reg.state == SlotState.Playing && reg.level >= 1f && !OthersAudible(reg)) { SetLayer(MusicLayer.Regular); moodGraceUntil = now + C.moodGrace; }
                        break;
                    case MusicLayer.End:
                        EndTick();
                        break;
                }
            }
            duckDbNow = Math.Min(Math.Min(0f, i.otherDuckDb), ZodDuck.Db(C.zodDuckDb));
            StepSlots(dt);
        }

        /// <summary>The game-over frame: decide once whether the drama carries the ending (the detector is frozen from here).</summary>
        void LatchGameOver()
        {
            gameOverSeen = true; gameOverAt = now; endDramaAt = now;
            string why;
            if (Layer == MusicLayer.ToDrama || Layer == MusicLayer.Drama || (Layer == MusicLayer.Off && Detector.Wanted)) { endDrama = true; why = "the drama plays on to the end screen"; }
            else if (Layer == MusicLayer.ToRegular && drama != null && drama.state == SlotState.Playing && drama.level >= C.gameOverReverseLevel)
            { endDrama = true; why = $"the drama was fading out ({drama.level:0.00}): it comes back and plays on to the end screen"; }
            else if (Detector.SinceReturn < C.gameOverGrace) { endDrama = false; why = $"the drama faded back {Detector.SinceReturn:0} s ago: the regular song plays on to the end screen"; }
            else if (Detector.HotFor >= C.gameOverHotHold) { endDrama = true; why = "a big attack is under way: the drama starts at the fall"; }
            else if (C.dramaOnGameOver) { endDrama = true; endDramaAt = now + C.gameOverDramaDelay; why = $"dramaOnGameOver: the drama starts {C.gameOverDramaDelay:0.0} s after the fall"; }
            else { endDrama = false; why = "the regular song plays on to the end screen"; }
            Note($"game over ({Layer}): detector frozen, {why}");
        }

        // ================================================================================================================ regular
        void StartRegularFromSilence()
        {
            var pool = lastBattle ? MusicPool.Battle : MusicPool.Calm;
            if (PoolCount(pool) == 0) pool = pool == MusicPool.Battle ? MusicPool.Calm : MusicPool.Battle;
            if (PoolCount(pool) == 0) return;
            StartRegular(pool, 0.0, C.startFade, 0.0, false);
            SetLayer(MusicLayer.Regular);
            moodGraceUntil = now;
        }

        /// <summary>A fresh regular track on a new slot (from `at`, else its start / a section cue), fading in over `fade`.</summary>
        void StartRegular(MusicPool pool, double delay, float fade, double at, bool cue)
        {
            if (PoolCount(pool) == 0) { pool = pool == MusicPool.Battle ? MusicPool.Calm : MusicPool.Battle; if (PoolCount(pool) == 0) return; }
            var t = firstRegular ? PickFirst(pool) : Pick(pool);
            double pos = firstRegular ? 0.0 : cue ? EntryCue(t) : at;
            firstRegular = false;
            reg = Start(t, pos, delay, fade, 0f, "regular");
        }

        void RegularTick(in MusicInput i)
        {
            if (reg == null) { StartRegular(lastBattle ? MusicPool.Battle : MusicPool.Calm, 0.0, C.endedFade, 0.0, false); return; }
            if (reg.state != SlotState.Playing) return;
            if (O.Ended(reg.idx))
            {   // ended before its tail was caught (a short clip): next of the same mood at once
                var pool = reg.track.pool; Release(reg); reg = null;
                StartRegular(i.gameOver ? pool : (lastBattle ? MusicPool.Battle : MusicPool.Calm), 0.0, C.endedFade, 0.0, false);
                return;
            }
            if (reg.target < 1f) return;
            float x = Xfade(reg.track);
            if (Left(reg) <= x)
            {   // the natural ending: crossfade into the next song of the same mood (its measured tail)
                var old = reg; FadeTo(old, 0f, x, AtSilence.Stop);
                reg = null;
                var pool = i.gameOver || now < moodGraceUntil ? old.track.pool : (lastBattle ? MusicPool.Battle : MusicPool.Calm);
                StartRegular(pool, 0.0, x, 0.0, false);
                return;
            }
            // No mid-track mood switch: a regular track always plays to its natural ending, and the branch above
            // then picks the next track from the mood at that moment (calm or battle).
        }

        void MoodSwitch(MusicPool want)
        {
            var old = reg;
            float left = (float)Left(old);
            memo[(int)old.track.pool] = left > Xfade(old.track) + 20f ? new Memo { track = old.track, pos = O.Time(old.idx), at = now } : default;
            FadeTo(old, 0f, C.moodXfade, AtSilence.Stop);
            reg = null;
            var m = memo[(int)want];
            if (m.track != null && now - m.at <= C.resumeWindow && m.track != LastStarted)
            {
                memo[(int)want] = default;
                reg = Start(m.track, Math.Max(0.0, m.pos - 1.0), 0.0, C.moodXfade, 0f, "regular (mood " + want + ", back where it was)", resumed: true);
                Note($"mood {old.track.pool} -> {want}: {m.track.name} continues at {m.pos - 1.0:0.0} s");
                return;
            }
            memo[(int)want] = default;
            Note($"mood {old.track.pool} -> {want}");
            StartRegular(want, 0.0, C.moodXfade, 0.0, true);
        }

        /// <summary>A section start that leaves most of the track (music_v2 MusicEntry; the meta keeps only cues at full level), else 0.</summary>
        double EntryCue(MusicTrack t)
        {
            if (t.entries == null || t.entries.Length == 0) return 0.0;
            float room = Math.Max(45f, t.length * 0.45f);
            var ok = new List<float>();
            foreach (var e in t.entries) if (e >= 0f && e <= t.length - room) ok.Add(e);
            return ok.Count > 0 ? ok[rnd.Next(ok.Count)] : 0.0;
        }

        // ================================================================================================================ drama
        void StartDramaFromSilence()
        {
            if (PoolCount(MusicPool.Drama) == 0) { StartRegularFromSilence(); return; }
            var t = PickDrama(null, false);
            drama = Start(t, t.dramaIn, 0.0, C.toDramaIn, C.dramaDb, "drama");
            DramaStarts++;
            SetLayer(MusicLayer.ToDrama);
        }

        void BeginToDrama()
        {
            if (PoolCount(MusicPool.Drama) == 0) return;                                          // no drama clips: the regular music just goes on
            var from = Layer;
            SetLayer(MusicLayer.ToDrama);
            DramaStarts++;
            var replacing = reg != null && reg.track != null ? reg.track : null;
            foreach (var s in slots) if (s.state == SlotState.Playing && s != reg && s != drama && s.target <= 0f) QuickOut(s);   // a tail / mood fade in flight
            bool regAudible = false;
            if (reg != null)
            {
                if (reg.state == SlotState.Pending) { if (reg.pendingResume) reg.state = SlotState.Paused; else { reg.state = SlotState.Idle; reg.track = null; reg = null; } }
                else if (reg.state == SlotState.Playing) { FadeTo(reg, 0f, C.toDramaOut, AtSilence.Park); regAudible = reg.level > 0.001f; }
            }
            double delay = regAudible ? C.toDramaDelay : 0.0;
            if (drama != null && drama.state == SlotState.Playing) { FadeTo(drama, 1f, C.toDramaIn, AtSilence.Park); Note($"{from} -> ToDrama: {drama.track.name} comes back up from {Gain01(drama):0.00}"); return; }
            if (drama != null && drama.state == SlotState.Pending) return;
            if (drama != null && drama.state == SlotState.Paused && now - drama.parkedAt <= C.dramaResumeWindow && drama.track.length - drama.parkedPos >= C.dramaResumeMinLeft)
            {
                ScheduleResume(drama, delay, C.toDramaIn);
                Note($"{from} -> ToDrama: {drama.track.name} continues at {drama.parkedPos:0.0} s (paused {now - drama.parkedAt:0} s ago)");
                return;
            }
            if (drama != null) { Release(drama); drama = null; }
            var t = PickDrama(replacing, false);
            drama = Start(t, t.dramaIn, delay, C.toDramaIn, C.dramaDb, "drama");
            Note($"{from} -> ToDrama: regular out {C.toDramaOut:0.#} s, {t.name} in {C.toDramaIn:0.#} s from +{delay:0.00} s");
        }

        void DramaTick()
        {
            if (drama == null) { var t0 = PickDrama(null, true); drama = Start(t0, t0.dramaIn, 0.0, C.endedFade, C.dramaDb, "drama"); return; }
            if (drama.state != SlotState.Playing) return;
            if (O.Ended(drama.idx))
            {
                Release(drama); drama = null;
                var t1 = PickDrama(null, true); drama = Start(t1, t1.dramaIn, 0.0, C.endedFade, C.dramaDb, "drama (next)");
                return;
            }
            if (drama.target < 1f || OthersAudible(drama)) return;
            float x = Xfade(drama.track);
            if (Left(drama) <= x)
            {   // a drama track ends while the attack goes on: crossfade into a DIFFERENT drama track
                var old = drama; FadeTo(old, 0f, x, AtSilence.Stop);
                var t = PickDrama(null, true);
                drama = Start(t, t.dramaIn, 0.0, x, C.dramaDb, "drama (next)");
            }
        }

        void BeginToRegular()
        {
            var from = Layer;
            SetLayer(MusicLayer.ToRegular);
            DramaEnds++;
            foreach (var s in slots) if (s.state == SlotState.Playing && s != reg && s != drama && s.target <= 0f) QuickOut(s);
            bool dramaAudible = false;
            if (drama != null)
            {
                if (drama.state == SlotState.Pending) { if (drama.pendingResume) drama.state = SlotState.Paused; else { drama.state = SlotState.Idle; drama.track = null; drama = null; } }
                else if (drama.state == SlotState.Playing) { FadeTo(drama, 0f, C.toRegularOut, AtSilence.Park); dramaAudible = drama.level > 0.001f; }
            }
            double delay = dramaAudible ? C.toRegularDelay : 0.0;
            if (reg != null && reg.state == SlotState.Playing) { FadeTo(reg, 1f, C.toRegularIn, AtSilence.Park); Note($"{from} -> ToRegular: {reg.track.name} comes back up from {Gain01(reg):0.00}"); return; }
            if (reg != null && reg.state == SlotState.Pending) return;
            if (reg != null && reg.state == SlotState.Paused)
            {
                if (Resumable(reg))
                {
                    ScheduleResume(reg, delay, C.toRegularIn);
                    Note($"{from} -> ToRegular: drama out {C.toRegularOut:0.#} s, {reg.track.name} RESUMES at {reg.parkedPos:0.000} s from +{delay:0.0} s");
                    return;
                }
                Note($"{from} -> ToRegular: {reg.track.name} had {reg.track.length - reg.parkedPos:0.0} s left: the next song starts instead");
                Release(reg); reg = null;
            }
            StartRegular(lastBattle ? MusicPool.Battle : MusicPool.Calm, delay, C.toRegularIn, 0.0, false);
        }

        bool Resumable(Slot s) => s.track.length - s.parkedPos >= Math.Max(C.resumeMinLeft, Xfade(s.track) + C.toRegularIn + 1f);

        // ================================================================================================================ off / on
        /// <summary>oz_music off: everything fades out; the regular, drama and end voices are paused and kept (bookmarks).</summary>
        void GoOff()
        {
            foreach (var s in slots)
            {
                bool role = s == reg || s == drama || s == endS;
                switch (s.state)
                {
                    case SlotState.Pending:
                        if (role && s.pendingResume) s.state = SlotState.Paused;                        // a bookmark waiting to unpause stays one
                        else { Unrole(s); Release(s); }
                        break;
                    case SlotState.Playing:
                        FadeTo(s, 0f, C.offFade, role ? AtSilence.Park : AtSilence.Stop);
                        break;
                    case SlotState.Paused:
                        if (!role) Release(s);
                        break;
                }
            }
            var from = Layer;
            SetLayer(MusicLayer.Off);
            Note($"music off (oz_music) in {from}: everything fades out {C.offFade.ToString("0.0", CultureInfo.InvariantCulture)} s, the songs are kept paused");
        }

        void ResumeOrStartRegular()
        {
            if (reg != null && reg.state == SlotState.Paused && Resumable(reg))
            {
                ScheduleResume(reg, 0.0, C.startFade);
                SetLayer(MusicLayer.Regular); moodGraceUntil = now + C.moodGrace;
                Note($"music on: {reg.track.name} RESUMES at {reg.parkedPos:0.000} s");
                return;
            }
            if (reg != null) { Release(reg); reg = null; }
            StartRegularFromSilence();
        }

        void ResumeOrStartDrama()
        {
            if (drama != null && drama.state == SlotState.Paused && now - drama.parkedAt <= C.dramaResumeWindow && drama.track.length - drama.parkedPos >= C.dramaResumeMinLeft)
            {
                ScheduleResume(drama, 0.0, C.startFade);
                DramaStarts++; SetLayer(MusicLayer.ToDrama);
                Note($"music on: {drama.track.name} RESUMES at {drama.parkedPos:0.000} s (the attack goes on)");
                return;
            }
            if (drama != null) { Release(drama); drama = null; }
            StartDramaFromSilence();
        }

        void ResumeEnd()
        {
            SetLayer(MusicLayer.End);
            if (endS != null && endS.state == SlotState.Paused) { ScheduleResume(endS, 0.0, C.endFadeIn); Note($"music on: {endS.track.name} RESUMES at {endS.parkedPos:0.000} s (end music)"); }
            else Note("music on: the end music comes in on its schedule");
        }

        /// <summary>The host hands the music back (musicV3 off in Play mode): everything fades out 0.6 s and is released.</summary>
        public void Retire()
        {
            foreach (var s in slots)
            {
                if (s.state == SlotState.Paused || s.state == SlotState.Pending) Release(s);
                else if (s.state == SlotState.Playing) FadeTo(s, 0f, C.offFade, AtSilence.Stop);
            }
            reg = drama = endS = null;
            SetLayer(MusicLayer.Off); retired = true;
            Note("retired: every voice fades out " + C.offFade.ToString("0.0", CultureInfo.InvariantCulture) + " s (music_v2 takes over)");
        }

        void Unrole(Slot s) { if (s == reg) reg = null; if (s == drama) drama = null; if (s == endS) endS = null; }

        // ================================================================================================================ end
        void EnterEnd(in MusicInput i)
        {
            endEntered = true;
            bool sounding = AudibleCount() > 0;
            SetLayer(MusicLayer.End);
            foreach (var s in slots)
            {
                if (s.state == SlotState.Paused || s.state == SlotState.Pending) Release(s);
                else if (s.state == SlotState.Playing) FadeTo(s, 0f, C.endFade, AtSilence.Stop);
            }
            reg = drama = null; endS = null;
            double lead = i.stingSeconds > 0f ? Math.Max(0.5, i.stingSeconds - C.endLead) : C.endNoSting;
            double late = endScreenSince >= 0 ? now - endScreenSince : 0.0;                       // music switched on again on the end screen
            endMusicAt = now + Math.Max(Math.Max(0.0, lead - late), sounding ? C.endFade : 0.0);
            endSeq.Clear();
            if (i.result > 0 && PoolCount(MusicPool.Victory) > 0) endSeq.Add(MusicPool.Victory);
            if (PoolCount(MusicPool.Menu) > 0) endSeq.Add(MusicPool.Menu);
            endIdx = 0;
            Note($"END SCREEN ({(i.result > 0 ? "win" : i.result < 0 ? "loss" : "draw")}): music out {C.endFade:0.#} s under the sting, end music in {endMusicAt - now:0.0} s" +
                 (gameOverAt >= 0 ? $" ({now - gameOverAt:0.0} s after the game over)" : ""));
        }

        void EndTick()
        {
            if (endSeq.Count == 0) return;
            if (endS == null)
            {
                if (now < endMusicAt) return;
                var pool = endSeq[endIdx];
                var t = Pick(pool);
                endS = Start(t, 0.0, 0.0, C.endFadeIn, PoolDb(pool), $"end music {pool}, {(endScreenSince >= 0 ? now - endScreenSince : 0.0):0.0} s after the end screen");
                return;
            }
            if (endS.state != SlotState.Playing) return;
            if (O.Ended(endS.idx)) { var p0 = NextEndPool(); Release(endS); var t0 = Pick(p0); endS = Start(t0, 0.0, 0.0, C.endedFade, PoolDb(p0), "end music"); return; }
            float x = Xfade(endS.track);
            if (endS.target >= 1f && !OthersAudible(endS) && Left(endS) <= x)
            {
                var p = NextEndPool();
                var old = endS; FadeTo(old, 0f, x, AtSilence.Stop);
                var t = Pick(p); endS = Start(t, 0.0, 0.0, x, PoolDb(p), "end music");
            }
        }

        MusicPool NextEndPool() { if (endIdx + 1 < endSeq.Count) endIdx++; return endSeq[endIdx]; }
        float PoolDb(MusicPool p) => p == MusicPool.Victory ? C.victoryDb : p == MusicPool.Menu ? C.menuDb : p == MusicPool.Drama ? C.dramaDb : 0f;

        /// <summary>Listening check: crossfade the regular playlist to its next song now (Regular only).</summary>
        public void Skip()
        {
            if (Layer != MusicLayer.Regular || reg == null || reg.state != SlotState.Playing) { Note("skip: only while the regular music plays"); return; }
            var old = reg; FadeTo(old, 0f, 1.5f, AtSilence.Stop); reg = null;
            StartRegular(old.track.pool, 0.0, 1.5f, 0.0, false);
        }

        // ================================================================================================================ slots
        Slot Start(MusicTrack t, double pos, double delay, float fade, float extraDb, string why, bool resumed = false)
        {
            var s = Alloc();
            s.track = t; s.extraDb = extraDb; s.level = 0f; s.target = 1f; s.rate = 1f / Math.Max(0.05f, fade); s.atSilence = AtSilence.Park;
            s.pendingResume = false; s.startPos = Math.Max(0.0, Math.Min(pos, Math.Max(0.0, t.length - 1.0)));
            s.startAt = now + delay; s.state = SlotState.Pending;
            if (!resumed)
            {
                if (LastStarted != null && LastStarted.name == t.name) BackToBack++;
                LastStarted = t; Starts++;
            }
            Note($"start v{s.idx} {t.name} @ {s.startPos:0.0} s ({why}), in {fade:0.0#} s{(delay > 0 ? $" from +{delay:0.00} s" : "")}");
            if (delay <= 0.0) Fire(s);
            return s;
        }

        void ScheduleResume(Slot s, double delay, float fade)
        {
            s.state = SlotState.Pending; s.pendingResume = true; s.startAt = now + delay;
            s.target = 1f; s.rate = 1f / Math.Max(0.05f, fade); s.atSilence = AtSilence.Park; s.level = 0f;
            if (delay <= 0.0) Fire(s);
        }

        void Fire(Slot s)
        {
            if (s.pendingResume) { Resumes++; Note($"resume v{s.idx} {s.track.name} @ {s.parkedPos:0.000} s (paused {now - s.parkedAt:0.0} s)"); O.Resume(s.idx); }
            else O.Play(s.idx, s.track, s.startPos);
            s.pendingResume = false; s.state = SlotState.Playing; s.level = 0f;
            O.Gain(s.idx, 0f);
        }

        Slot Alloc()
        {
            foreach (var s in slots) if (s.state == SlotState.Idle) return s;
            Slot pick = null;
            foreach (var s in slots) if (s.state == SlotState.Playing && s.target <= 0f && s != reg && s != drama && s != endS && (pick == null || s.level < pick.level)) pick = s;
            if (pick == null) foreach (var s in slots) if (s.state == SlotState.Paused && s != reg) { pick = s; break; }
            if (pick == null) foreach (var s in slots) if (s != reg && s != drama && s != endS) { pick = s; break; }
            if (pick == null) pick = slots[0];
            Steals++;
            Note($"WARN voice steal: v{pick.idx} {pick.track?.name}");
            Unrole(pick);
            Release(pick);
            return pick;
        }

        void Release(Slot s)
        {
            if (s.state != SlotState.Idle && s.state != SlotState.Pending) O.Stop(s.idx);
            else if (s.state == SlotState.Pending && s.pendingResume) O.Stop(s.idx);                // a paused bookmark waiting to unpause
            s.state = SlotState.Idle; s.track = null; s.level = s.target = 0f; s.pendingResume = false;
        }

        void FadeTo(Slot s, float target, float seconds, AtSilence at)
        {
            s.target = target; s.rate = 1f / Math.Max(0.05f, seconds); s.atSilence = at;
        }

        void QuickOut(Slot s) { s.target = 0f; s.rate = Math.Max(s.rate, 1f / Math.Max(0.05f, C.quickOut)); s.atSilence = AtSilence.Stop; }

        void StepSlots(float dt)
        {
            float duckLin = Db(duckDbNow);
            foreach (var s in slots)
            {
                if (s.state == SlotState.Pending && now >= s.startAt) Fire(s);
                if (s.state != SlotState.Playing) continue;
                if (s.level < s.target) s.level = Math.Min(s.target, s.level + s.rate * dt);
                else if (s.level > s.target) s.level = Math.Max(s.target, s.level - s.rate * dt);
                if (s.target <= 0f && s.level <= 0f)
                {
                    O.Gain(s.idx, 0f);
                    if (s.atSilence == AtSilence.Park && !O.Ended(s.idx))                              // a clip that ended in its fade has nothing to keep
                    {
                        s.parkedPos = O.Time(s.idx); s.parkedAt = now;
                        O.Pause(s.idx); s.state = SlotState.Paused; Parks++;
                        Note($"pause v{s.idx} {s.track.name} @ {s.parkedPos:0.000} s (kept for the resume)");
                    }
                    else
                    {
                        var name = s.track?.name;
                        Unrole(s);
                        Release(s);
                        Note($"stop v{s.idx} {name} (faded out)");
                    }
                    continue;
                }
                O.Gain(s.idx, Gain01(s) * Db(s.track.gainDb + s.extraDb) * duckLin);
            }
            // a paused drama bookmark expires after the resume window (frees its voice)
            if (drama != null && drama.state == SlotState.Paused && Layer != MusicLayer.ToDrama && Layer != MusicLayer.Drama && now - drama.parkedAt > C.dramaResumeWindow)
            { Note($"drop the paused drama {drama.track.name} (older than {C.dramaResumeWindow:0} s)"); Release(drama); drama = null; }
        }

        static float Gain01(Slot s) => (float)Math.Sin(s.level * Math.PI * 0.5);                // equal power: level 0..1 -> sin
        static float Db(float db) => (float)Math.Pow(10.0, db / 20.0);
        double Left(Slot s) => s.track.length - O.Time(s.idx);
        float Xfade(MusicTrack t)
        {
            float x = t.tail > 0f ? t.tail : 4f;
            float cap = t.length * 0.25f;
            return Math.Max(Math.Min(C.minXfade, cap), Math.Min(Math.Min(x, cap), C.maxXfade));
        }

        int AudibleCount() { int n = 0; foreach (var s in slots) if (s.state == SlotState.Playing && s.level > 0.001f) n++; return n; }
        bool OthersAudible(Slot me) { foreach (var s in slots) if (s != me && s.state == SlotState.Playing && s.level > 0.001f) return true; return false; }
        bool AnyFadingOut() { foreach (var s in slots) if (s.state == SlotState.Playing && s.target <= 0f) return true; return false; }

        void SetLayer(MusicLayer l) { if (Layer == l) return; Layer = l; layerSince = now; }

        // ================================================================================================================ playlists
        /// <summary>Next track of a pool: calm / battle / victory / menu alternate the level's own songs (+ one guest per cycle, never
        /// first) with the added zopen songs; every source cycles all of its tracks before one repeats; never the track heard last.</summary>
        /// <summary>
        /// The match's first track is drawn uniformly from the WHOLE pool (the level's own songs, guests and the added zopen
        /// songs), never AvoidFirst (the track that opened the previous match, AudioDirector remembers it). Pick() used to open every
        /// match with one of the level's few own songs.</summary>
        public string AvoidFirst;
        public string FirstPicked { get; private set; }
        MusicTrack PickFirst(MusicPool pool)
        {
            var b = bags[(int)pool];
            var all = new List<MusicTrack>(); all.AddRange(b.own); all.AddRange(b.guests); all.AddRange(b.zopen);
            if (all.Count > 1 && !string.IsNullOrEmpty(AvoidFirst)) all.RemoveAll(x => x.name == AvoidFirst);
            if (all.Count == 0) return Pick(pool);
            var t = all[rnd.Next(all.Count)];
            b.nextZopen = !t.zopen;
            b.prev = b.last; b.last = t; if (t.zopen) b.lastB = t; else b.lastA = t;
            FirstPicked = t.name;
            Note($"first track: {t.name} (random of {all.Count} in {pool}{(string.IsNullOrEmpty(AvoidFirst) ? "" : ", not " + AvoidFirst)})");
            return t;
        }

        MusicTrack Pick(MusicPool pool)
        {
            var b = bags[(int)pool];
            bool useZ = b.nextZopen;
            if (useZ && b.zopen.Count == 0) useZ = false;
            if (!useZ && b.own.Count + b.guests.Count == 0) useZ = true;
            var t = Draw(b, useZ) ?? Draw(b, !useZ);
            if (t == null)
            {   // a single-track pool (or both sources exhausted by the no-repeat rule): take anything, a repeat only if nothing else exists
                var all = new List<MusicTrack>(); all.AddRange(b.own); all.AddRange(b.guests); all.AddRange(b.zopen);
                t = all.Find(x => LastStarted == null || x.name != LastStarted.name) ?? all[0];
            }
            b.nextZopen = !t.zopen;
            b.prev = b.last; b.last = t; if (t.zopen) b.lastB = t; else b.lastA = t;
            return t;
        }

        /// <summary>The drama pool's shuffle bag, kept across matches. The host keeps both strings in the saved settings (like
        /// AvoidFirst): DramaLast = the drama track that played last, DramaBag = the names still to play in this round, comma
        /// separated (empty = a new round). The host sets both after it built the core and saves them whenever DramaPicks moved.</summary>
        public string DramaLast = "", DramaBag = "";
        public int DramaPicks { get; private set; }

        /// <summary>The drama pool: a shuffle bag that outlives the match. Every pick is random among the tracks not played yet in
        /// this round, never the drama track that played last (also across the end of a round, a match restart and a new start of
        /// the game: DramaLast) and, where the round allows, not the one before it either (no A B A). When every track has played, a
        /// new round starts. A pool of one track simply plays that track.</summary>
        MusicTrack PickDrama(MusicTrack replacing, bool next)
        {
            var b = bags[(int)MusicPool.Drama];
            var all = new List<MusicTrack>(); all.AddRange(b.own); all.AddRange(b.guests); all.AddRange(b.zopen);
            if (all.Count == 0) return null;
            var left = new List<MusicTrack>();                                                     // this round's tracks still to play (a name the pool no longer has is dropped)
            foreach (var n in (DramaBag ?? "").Split(',')) { var t = all.Find(x => x.name == n); if (t != null && !left.Contains(t)) left.Add(t); }
            bool Rep(MusicTrack t) => t.name == DramaLast || t == b.last || (LastStarted != null && t.name == LastStarted.name);
            var elig = left.FindAll(t => !Rep(t));
            if (elig.Count == 0) { left.Clear(); left.AddRange(all); elig = left.FindAll(t => !Rep(t)); }   // a new round: every track again, never opening with the last one
            if (elig.Count == 0) elig = left;                                                       // a pool of one track: it simply plays again
            var cand = elig.FindAll(t => t != b.prev);
            if (cand.Count == 0) cand = elig;
            var pick = cand[rnd.Next(cand.Count)];
            left.Remove(pick);
            var names = new List<string>(); foreach (var t in left) names.Add(t.name);
            DramaBag = string.Join(",", names.ToArray()); DramaLast = pick.name; DramaPicks++;
            b.prev = b.last; b.last = pick;
            Note($"drama pick: {pick.name} (random of {cand.Count}; {left.Count} of {all.Count} still to play in this round)");
            return pick;
        }

        /// <summary>Play check: one drama pick through the function the game uses (PickDrama) without starting the track. Returns the
        /// track's name; the bag moves on exactly as in a match, and the host saves it on its next tick.</summary>
        public string DebugPickDrama() { var t = PickDrama(null, false); return t != null ? t.name : null; }

        MusicTrack Draw(Bag b, bool zopen)
        {
            var order = zopen ? b.orderB : b.orderA;
            int distinct = zopen ? b.zopen.Count : b.own.Count + b.guests.Count;
            if (distinct == 0) return null;
            for (int pass = 0; pass < 2; pass++)
            {
                if (order.Count == 0 || pass == 1) Refill(b, zopen);
                for (int k = 0; k < order.Count; k++)
                {
                    var c = order[k];
                    bool rep = IsRepeat(b, c) || (distinct > 1 && c == (zopen ? b.lastB : b.lastA));   // nor this source's last (no A B A)
                    if (rep && distinct > 1) continue;
                    if (rep) return null;                                                          // this source has only that one track
                    order.RemoveAt(k); return c;
                }
            }
            return null;
        }

        bool IsRepeat(Bag b, MusicTrack c) => c == b.last || (LastStarted != null && c.name == LastStarted.name);

        void Refill(Bag b, bool zopen)
        {
            if (zopen) { var z = new List<MusicTrack>(b.zopen); Shuffle(z); b.orderB.AddRange(z); return; }
            var own = new List<MusicTrack>(b.own); Shuffle(own);
            if (b.guests.Count > 0)
            {
                var g = b.guests[rnd.Next(b.guests.Count)];
                if (g == b.lastGuest && b.guests.Count > 1) g = b.guests[(b.guests.IndexOf(g) + 1 + rnd.Next(b.guests.Count - 1)) % b.guests.Count];
                b.lastGuest = g;
                own.Insert(own.Count == 0 ? 0 : 1 + rnd.Next(own.Count), g);                       // never first: the level's own sound opens a cycle
            }
            b.orderA.AddRange(own);
        }

        void Shuffle(List<MusicTrack> l) { for (int i = l.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); var x = l[i]; l[i] = l[j]; l[j] = x; } }

        // ================================================================================================================ status
        void Note(string s)
        {
            string line = $"{now,8:0.00}  {s}";
            History.Add(line); if (History.Count > 40) History.RemoveAt(0);
            O?.Log(line);
        }

        public string Status()
        {
            var sb = new StringBuilder();
            var d = Detector;
            sb.Append($"music_v3 {Layer} ({now - layerSince:0} s), drama wanted {d.Wanted}{(d.Wanted ? " fort " + (d.Fort == 1 ? "ours" : "theirs") : "")}, ")
              .Append($"hot {d.HotFor:0.0} s, calm {d.ColdFor:0.0}/{d.ExitHoldNow(C):0} s, drama {d.DramaFor:0}/{d.MinDramaNow(C):0} s, since return {Math.Min(999f, d.SinceReturn):0} s{(d.Waiting(C) ? $" (interlude: next drama in {C.cooldown - d.SinceReturn:0} s, heavy in {Math.Max(0f, d.HeavyInterludeNow(C) - d.SinceReturn):0} s)" : "")}, siege chain {d.Chain}\n");
            sb.Append($"  duck {duckDbNow:0.0} dB (zod {ZodDuck.Db(C.zodDuckDb):0.0}), starts {Starts}, resumes {Resumes}, pauses {Parks}, dramas {DramaStarts}/{DramaEnds}, back-to-back {BackToBack}, steals {Steals}{(gameOverSeen ? $", game over: drama to the end {endDrama}" : "")}\n");
            foreach (var s in slots)
            {
                if (s.state == SlotState.Idle) { sb.Append($"  v{s.idx}: idle\n"); continue; }
                string role = s == reg ? "regular" : s == drama ? "drama" : s == endS ? "end" : "out";
                double t = s.state == SlotState.Paused ? s.parkedPos : s.state == SlotState.Pending ? (s.pendingResume ? s.parkedPos : s.startPos) : O.Time(s.idx);
                sb.Append($"  v{s.idx}: {s.state} {role} {s.track?.name} @ {t:0.0}/{s.track?.length:0} s, gain {Gain01(s):0.00} -> {s.target:0}\n");
            }
            return sb.ToString();
        }
    }
}
