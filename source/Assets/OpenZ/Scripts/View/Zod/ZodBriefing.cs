// OpenZ (GPL-3.0) - Commander Zod cameo director (not a playable unit; never blocks input beyond the briefing's skip key).
//  1. Level start briefing: the sim waits (ZGame disabled: no ticks, the CPU gets no head start), the camera does a slow map
//     fly-over (ZodFlyover) timed to the words, Zod talks from the kit comms window (ZodComms + ZodStage feed) with subtitles in
//     the letterbox bar and gestures timed to the words (zod_beats.json). SPACE / ENTER / ESC / a click skips at once. Then the
//     bars retract, the camera lands on the player's start view, and the normal level start runs: the HUD builds, its mission
//     banner drops, the HUD slides in (OpenZHud intro), AudioDirector plays the start sting + Rachel + Alan (held until now).
//     (also a level opened directly in the editor). Never in bot matches, test mode (oz_testmode) or with oz_zod_cameo = 0.
//  2. Victory / defeat punchline over the end screen: after fx_v2's end sequence (OpenZVictory holds the screen until its
//     Rachel / Alan lines are done) the screen shows, and 1.3 s later, into silence (no Alan / Rachel playing or queued), Zod
//     delivers one random line of the group (never the same twice in a row) in a compact window beside the stat plates.
//  3. Idle barb: 60 s of real time without any key, click or wheel (unpaused, HUD shown) -> one line, max once per match.
//  4. Main menu greeting with the 30th-anniversary stamp, once per session; level-select quips (zod_select_L02 / L17) the first
//     time a card stays focused. Leaving the view fades Zod out (he never talks over another screen).
// Audio: one 2D AudioSource on the AudioDirector voice bus (Voice/Commander: master + the VOICE slider, voiceDb), ducking music /
// ambience / Alan's bus and the combat mix like Rachel. Rachel waits and Alan stays quiet while Zod speaks
// (AudioDirector.ExternalVoiceBusy, zod_cameo patch), and Zod only starts into silence (AudioDirector.VoiceBusy).
// from the first frame to the hand-back (preroll 0.55 s, tail 0.35 s, the camera is home before the last word). The feed is a
// live field transmission: ZodStage's hand-held camera with zoom punches / pull-out / kicks, ZodComms' signal bursts, and a
// radio static sound (Resources/OpenZ/Zod/Sfx/comms_static_*: open, mid-line bursts, close). New beat fx: "punch", "pull",
// "thrust" (the fist at the camera, one per GO!), "glitch", "kick". The salute is gone from every line.
// into the RTS camera; one slow move per shot, cuts on his line breaks (PlanCine follows the line clock); a skip dips and cuts home.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    [DefaultExecutionOrder(-60)]
    public sealed partial class ZodBriefing : MonoBehaviour
    {
        public static ZodBriefing I { get; private set; }
        public const string PrefCameo = "oz_zod_cameo", PrefBriefing = "oz_zod_briefing", PrefIdle = "oz_zod_idle";
        /// <summary>The whole cameo (PlayerPrefs oz_zod_cameo, default on).</summary>
        public static bool Enabled => PlayerPrefs.GetInt(PrefCameo, 1) == 1;

        [Header("Voice (AudioDirector bus Voice/Commander)")]
        [Tooltip("Zod's level on the voice bus (dB). Rachel sits at -6, Alan at -3; the lines are -18 LUFS like theirs")] public float voiceDb = -2f;
        public float duckMusicDb = -8f, duckAmbienceDb = -5f, duckAlanBusDb = -8f;
        [Header("Timing")]
        [Tooltip("Real seconds without any key, click or wheel before the idle barb")] public float idleSeconds = 60f;
        [Tooltip("Seconds after the end screen shows before the punchline")] public float endDelay = 1.3f;
        [Tooltip("Silence (no Alan / Rachel) needed before Zod starts")] public float quietBefore = 0.7f;
        public float menuDelay = 1.8f, quipFocus = 0.8f, briefingPreroll = 0.55f, linePreroll = 0.45f;                // v2: 1.1 / 0.75 -> 0.55 / 0.45
        [Header("Transmission static (v2)")]
        [Tooltip("Static when the window opens / closes, dB relative to Zod's voice gain (the clips are -20 LUFS)")] public float staticEdgeDb = -1f;
        [Tooltip("Mid-line static bursts, dB relative to Zod's voice gain (the clips are -22 LUFS; they sit in his pauses, 12-16 dB under his words)")] public float staticMidDb = -5f;

        enum Mode { None, Level, Menu }
        public enum Kind { Briefing, Victory, Defeat, Idle, Greeting, Quip, Debug, Nuke }
        enum Phase { Pending, Briefing, Handing, Play }

        // ---- session state (a session = one Play / one run of the build)
        static int singleLoads; static bool greeted, pendingFromFlow, hooked;
        static readonly HashSet<string> briefed = new HashSet<string>(), quipped = new HashSet<string>();

        Mode mode; Phase phase = Phase.Play; bool fromFlow;
        ZGame game; OpenZHud hud; PlayerInput input; RtsCamera rts; OpenZMainMenu menu;
        ZodLibrary lib; ZodStage stage; ZodComms comms; ZodFlyover fly; AudioSource voice, staticEdge, staticMid;
        readonly Dictionary<string, AudioClip> statics = new Dictionary<string, AudioClip>();
        Perf perf;
        bool punchDone, idleDone, skipped;
        float idleT, lastBusyAt = -100f, endSeenAt = -1f, menuStartAt, lastMarker = -10f, nextDuck, focusSince; int lastFocus = -1;
        FieldInfo fView, fSide, fFocus;

        sealed class Perf
        {
            public Kind kind; public string id; public ZodLine line; public ZodBeatLine beats; public AudioClip clip; public ZodLayout layout;
            public float t, preroll, end, voiceEnd, fade = 1f, fadeSpeed; public bool voiceOn, done, closing;
            public readonly List<(float t, ZodGesture g)> gestures = new List<(float, ZodGesture)>();
            public readonly List<(float t, ZodShot s)> shots = new List<(float, ZodShot)>();
            public readonly List<(float t, ZodFx f)> fx = new List<(float, ZodFx)>();
            public int gi, si, fi;
            public void Abort(float seconds) { if (fadeSpeed <= 0f) fadeSpeed = 1f / Mathf.Max(0.05f, seconds); }
        }

        // ================================================================================================================ boot
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            singleLoads = 0; greeted = false; briefed.Clear(); quipped.Clear();
            SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
            AudioDirector.HoldStartLines = HoldingStart;                                            // zod_cameo patch: the briefing holds the start lines
            if (!hooked)
            {
                hooked = true;
                var prev = AudioDirector.ExternalVoiceBusy;                                          // help_cries hook: other voices wait for Zod
                AudioDirector.ExternalVoiceBusy = prev == null ? (Func<bool>)ZodSpeaking : () => ZodSpeaking() || prev();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterFirstScene()
        {
            // scene open when Play starts raises no sceneLoaded, so the menu was not counted and the first menu -> level load
            // looked like "level opened directly"; the first scene always counts now
            singleLoads = Mathf.Max(singleLoads, 1);
            if (!I) Install(SceneManager.GetActiveScene(), false);
        }

        static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            if (m != LoadSceneMode.Single) return;
            singleLoads++;
            Install(s, singleLoads > 1);
        }

        static void Install(Scene s, bool fromFlow)
        {
            if (!Application.isPlaying || !s.IsValid() || !Enabled) return;
            if (I && I.gameObject.scene == s) return;
            var go = new GameObject("ZodCameo");
            if (go.scene != s) SceneManager.MoveGameObjectToScene(go, s);
            pendingFromFlow = fromFlow;
            go.AddComponent<ZodBriefing>();
        }

        /// <summary>AudioDirector.HoldStartLines: true until the briefing has handed the level to the player.</summary>
        static bool HoldingStart() => I && I.mode == Mode.Level && I.phase != Phase.Play;
        /// <summary>AudioDirector.ExternalVoiceBusy: Zod's window is up and his line is not over (paused = not speaking).</summary>
        static bool ZodSpeaking() => I && I.perf != null && !I.perf.done && I.perf.t < I.perf.voiceEnd + 0.15f && !AudioListener.pause;
        /// <summary>True while Commander Zod has the floor (his window is up and the line is not over).</summary>
        public static bool Speaking => ZodSpeaking();
        /// <summary>True while the level-start briefing owns the game (sim waiting, camera flying).</summary>
        public static bool BriefingActive => I && I.mode == Mode.Level && (I.phase == Phase.Briefing || I.phase == Phase.Handing);

        void Awake()
        {
            if (I && I != this && I.gameObject.scene == gameObject.scene) { Destroy(gameObject); return; }
            I = this; fromFlow = pendingFromFlow; pendingFromFlow = false;
            lib = ZodLibrary.Shared;
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            menu = game ? null : FindFirstObjectByType<OpenZMainMenu>();
            mode = game ? Mode.Level : menu ? Mode.Menu : Mode.None;
            if (mode == Mode.None || !lib.Ok) { Destroy(gameObject); return; }
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false; voice.loop = false; voice.spatialBlend = 0f; voice.priority = 8; voice.dopplerLevel = 0f;
            voice.bypassReverbZones = true; voice.ignoreListenerPause = false;
            AudioDirector.RegisterExternalVoice(voice);
            staticEdge = NewStaticSource(); staticMid = NewStaticSource();                                  // v2: the transmission's radio static
            if (mode == Mode.Level)
            {
                if (WantsBriefing(out var why)) phase = Phase.Pending;
                else Debug.Log("[ZodCameo] level " + game.level + ": no briefing (" + why + ")");
            }
        }

        AudioSource NewStaticSource()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.loop = false; s.spatialBlend = 0f; s.priority = 40; s.dopplerLevel = 0f; s.bypassReverbZones = true;
            s.ignoreListenerPause = false;
            return s;
        }

        /// <summary>v2: radio static on its own 2D source at Zod's voice gain + db (random pitch 0.93-1.07 so repeats differ).</summary>
        void PlayStatic(AudioSource src, string name, float db)
        {
            if (!src) return;
            if (!statics.TryGetValue(name, out var c)) statics[name] = c = Resources.Load<AudioClip>("OpenZ/Zod/Sfx/comms_static_" + name);
            if (!c) return;
            src.Stop(); src.clip = c; src.pitch = UnityEngine.Random.Range(0.93f, 1.07f);
            src.volume = Mathf.Clamp01(VoiceGain() * Mathf.Pow(10f, db / 20f)); src.Play();
        }

        bool Bots => game && ((game.blueBot && game.redBot) || PlayerPrefs.GetInt("oz_botmatch", 0) == 1);

        /// <summary>A capture take (CaptureKit: -showcase / -demo / -cinematic / -maptop / Ctrl+Shift+F12) or an automated check
        /// (gate battery, night luma, combat sound, idle legs, combat view, input self test) is running: Zod stays off the air.</summary>
        bool Automated
        {
            get
            {
                if (Time.unscaledTime < nextAutoCheck) return automated;
                nextAutoCheck = Time.unscaledTime + 1f;
                automated = CaptureKit.Enabled || GateBattery.Running || NightLumaCheck.Running || FindFirstObjectByType<OzCombatSoundCheck>()
                            || FindFirstObjectByType<IdleLegsCheck>() || FindFirstObjectByType<CombatViewCheck>();
#if UNITY_EDITOR
                automated |= FindFirstObjectByType<InputSelfTest>();                                    // editor-only harness
#endif
                return automated;
            }
        }
        float nextAutoCheck = -1f; bool automated;

        bool WantsBriefing(out string why)
        {
            int pref = PlayerPrefs.GetInt(PrefBriefing, 1);
            why = pref <= 0 ? "oz_zod_briefing = 0" : Bots ? "bot match" : GameBoot.TestMode ? "test mode" : CaptureKit.Enabled ? "capture kit"
                : Automated ? "an automated check is running"                                                // CHECK: the gate battery etc. never wait 20 s for Zod
                : pref != 2 && !fromFlow ? "level opened directly (oz_zod_briefing = 2 forces it)"
                : lib.Clip("zod_brief_" + game.level) == null ? "no line zod_brief_" + game.level : null;
            return why == null;
        }

        void Start()
        {
            if (mode == Mode.Level)
            {
                hud = OpenZHud.I ? OpenZHud.I : FindFirstObjectByType<OpenZHud>();
                input = FindFirstObjectByType<PlayerInput>();
                rts = FindFirstObjectByType<RtsCamera>();
                if (phase == Phase.Pending) StartCoroutine(BriefingRoutine());
                else if (!Bots) StartCoroutine(Prebuild(1.5f));
            }
            else
            {
                menuStartAt = Time.unscaledTime;
                var t = typeof(OpenZMainMenu); const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
                fView = t.GetField("view", F); fSide = t.GetField("side", F); fFocus = t.GetField("focusCard", F);
                if (fView == null) Debug.LogWarning("[ZodCameo] OpenZMainMenu.view not found: greeting only, no level-select quips");
                StartCoroutine(Prebuild(0.3f));
            }
        }

        /// <summary>Builds the stage off the critical moment (the model + map board take a frame), rendering off.</summary>
        IEnumerator Prebuild(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            EnsureStage(mode == Mode.Level ? game.level : "L02");
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            if (mode == Mode.Level && phase != Phase.Play) Cursor.visible = true;
            comms?.Destroy();
        }

        // ================================================================================================================ briefing
        IEnumerator BriefingRoutine()
        {
            phase = Phase.Briefing;
            bool handed = false, hudStarted = false;
            try                                                                                         // CHECK: no input lock, whatever breaks
            {
                string id = "zod_brief_" + game.level;
                EnsureStage(game.level); EnsureComms(1f);
                game.enabled = false;                                                                   // the sim waits: nobody gets a head start
                if (input) input.enabled = false;
                if (hud && hud.Canvas == null) hud.Prebuild();
                hudStarted = hud && hud.Canvas != null;
                if (hud) { if (hudStarted) hud.SetVisible(false); hud.enabled = false; }                // not started yet: its Start (build + intro) waits
                fly = rts ? new ZodFlyover(rts, game) : null;
                if (fly != null) fly.VoiceOn = () => voice && voice.isPlaying;
                bool flying = fly != null && fly.Begin("overview");
                Cursor.visible = false;
                var def = Array.Find(OzLevelDef.All, d => d.Id == game.level);
                comms.Letterbox(true, "MISSION BRIEFING", "OPERATION " + game.level + " · " + (def != null ? def.Name : game.level));
                bool ok = StartPerf(Kind.Briefing, id, ZodLayout.Briefing(), briefingPreroll);
                if (ok && flying && perf != null)                                                       // cinematic briefing: shot lengths + cuts on the line breaks
                    fly.PlanCine(perf.preroll, perf.end, perf.line, () => perf != null && !perf.done ? perf.t + perf.preroll : float.NaN);
                Debug.Log($"[ZodCameo] briefing {id}: {(ok ? "playing" : "LINE MISSING")}, fly-over {(flying ? "on" : "off (no RtsCamera)")}, sim waits, HUD {(hudStarted ? "hidden" : "deferred")}");
                float started = Time.unscaledTime, alive = 0f;
                float budget = perf != null ? perf.preroll + perf.end + 6f : 0f;                        // CHECK watchdog: line + 6 s of unpaused time
                while (perf != null && !perf.done)
                {
                    if (!Paused) alive += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                    if (alive > budget)
                    {   // the line never finished (a broken clip / stage / window): hand the level back instead of waiting forever
                        Debug.LogWarning($"[ZodCameo] briefing watchdog: {id} still running after {alive:F0} s, handing the level back");
                        if (voice) voice.Stop(); perf.done = true; skipped = true; break;
                    }
                    GcHeadroom(perf);
                    if (!skipped && Time.unscaledTime - started > 0.35f && SkipPressed()) { skipped = true; perf.Abort(0.22f); AudioDirector.UI("ui_cancel");   /* sound_v3 */ Debug.Log($"[ZodCameo] briefing skipped at {perf.t:F1} s"); }
                    yield return null;
                }
                // hand-back: bars out, camera lands on the start view, sim + input + HUD back
                phase = Phase.Handing;
                comms.Letterbox(false);
                if (flying)
                {
                    fly.HandBack(skipped);                                                              // cinematic briefing (orbit flight: SetShot home 0.22 / 0.55 s)
                    float t0 = Time.unscaledTime;
                    while (fly.HomeError > 0.04f && Time.unscaledTime - t0 < 1.6f) yield return null;
                    fly.End();
                }
                game.enabled = true;
                Cursor.visible = true;
                yield return null;                                                                      // the skip press never reaches the game
                if (input) input.enabled = true;
                if (hud) { hud.enabled = true; if (hudStarted) hud.SetVisible(true); }
                briefed.Add(game.level);
                phase = Phase.Play;                                                                     // AudioDirector: start sting, Rachel, Alan
                handed = true;
                Debug.Log($"[ZodCameo] briefing handed over ({(skipped ? "skipped" : "complete")}): sim running, input + HUD back");
            }
            finally { if (!handed) EmergencyHandBack(hudStarted); }                                     // an exception above: never leave the game locked
        }

        /// <summary>CHECK: error path of the briefing. Whatever broke, the player gets the level back: sim, input, HUD, camera,
        /// cursor, start lines (phase Play).</summary>
        void EmergencyHandBack(bool hudStarted)
        {
            Debug.LogWarning("[ZodCameo] briefing aborted by an error: handing the level back to the player");
            try { if (voice) voice.Stop(); StartLineStop("STOP (briefing aborted)"); if (perf != null) perf.done = true; perf = null; } catch (Exception) { }
            try { comms?.Letterbox(false); comms?.Close(); } catch (Exception) { }
            try { if (stage) stage.SetLive(false); } catch (Exception) { }
            try { fly?.End(); } catch (Exception) { }
            if (game) game.enabled = true;
            Cursor.visible = true;
            if (input) input.enabled = true;
            try { if (hud) { hud.enabled = true; if (hudStarted) hud.SetVisible(true); } } catch (Exception) { }
            if (game) briefed.Add(game.level);
            phase = Phase.Play;
        }

        /// <summary>
        /// player runs the incremental GC; never a blocking collect), so the hand-over's 4-8 MB allocation burst starts on a fresh heap.
        /// GC timing only: nothing drawn, heard or simulated changes.</summary>
        bool gcHeadroomDone;
        void GcHeadroom(Perf p)
        {
            if (gcHeadroomDone || p == null || p.done || p.end - p.t > 3f) return;
            if (!UnityEngine.Scripting.GarbageCollector.isIncremental) { gcHeadroomDone = true; return; }
            if (!UnityEngine.Scripting.GarbageCollector.CollectIncremental(1000000UL)) gcHeadroomDone = true;   // false = the cycle is complete
        }

        static bool SkipPressed()
        {
            var kb = Keyboard.current; var m = Mouse.current;
            return (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame))
                || (m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame));
        }

        static bool AnyInput()
        {
            var kb = Keyboard.current; var m = Mouse.current;
            return (kb != null && kb.anyKey.wasPressedThisFrame)
                || (m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame || Mathf.Abs(m.scroll.ReadValue().y) > 0.01f));
        }

        // ================================================================================================================ lines
        bool hqHooked;
        /// <summary></summary>
        void OnHqBlast(float k)
        {
            if (comms == null || perf == null || perf.closing) return;
            comms.Glitch(0.55f + 0.5f * k, 0.18f + 0.14f * k);
            PlayStatic(staticMid, UnityEngine.Random.value < 0.5f ? "burst" : "burst2", staticMidDb + 3f * k);
        }
        /// <summary>Play check: HQ fire / gaze state of the stage.</summary>
        public static string DebugHq() => I && I.stage ? I.stage.HqStatus() + (I.perf != null ? $" | line {I.perf.id} ({I.perf.kind}) t={I.perf.t:F2}" : " | no line") : "no stage";

        void EnsureStage(string level)
        {
            if (!stage) { stage = ZodStage.Create(transform, level); if (stage.Rig) stage.Rig.voice = voice; stage.SetLive(false); }
            else stage.SetBoardLevel(level);
        }

        void EnsureComms(float uiScale)
        {
            comms ??= new ZodComms(transform);
            comms.SetUiScale(uiScale);
        }

        bool StartPerf(Kind kind, string id, ZodLayout layout, float preroll)
        {
            var line = lib.Line(id); var clip = lib.Clip(id);
            if (line == null || !clip) { Debug.LogWarning("[ZodCameo] line " + id + " missing (voice clip or lines.json)"); return false; }
            var beats = lib.Beats(id);
            float jokeLead = JokeLead(kind); preroll += jokeLead;
            var p = new Perf { kind = kind, id = id, line = line, beats = beats, clip = clip, layout = layout, preroll = preroll };
            p.voiceEnd = Mathf.Min(clip.length, line.duration > 0f ? line.duration : clip.length);
            p.end = p.voiceEnd + beats.tail;
            Collect(p.gestures, line, beats.gestures, id);
            Collect(p.shots, line, beats.shots, id);
            Collect(p.fx, line, beats.fx, id);
            p.t = -preroll;
            perf = p;
            EnsureStage(stage ? stage.BoardLevel : mode == Mode.Level ? game.level : "L02");
            EnsureComms(1f);                                                                            // reference layout (the HUD's UI scale never pushes the windows into it)
            var px = comms.WellPixels(layout);
            stage.EnsureFeed(px.x, px.y);
            stage.SetLive(true);
            stage.SetFraming(beats.framing); stage.SnapFraming();
            StartBoys(kind, line, preroll);                                                             // zod_briefings_v3: the two soldiers' backs, then the push-in
            AddAlanCueFx(p);
            stage.GazeBoys = BoysFor(kind, line);
            if (!hqHooked) { hqHooked = true; stage.HqBlasted += OnHqBlast; }
            stage.HqFire(kind == Kind.Defeat);
            stage.Alarm(kind == Kind.Nuke);
            stage.SetBed(string.IsNullOrEmpty(beats.preroll) ? "idle" : beats.preroll, 0.15f);
            comms.Open(layout, stage.HasModel ? stage.Feed : null);
            JokeBegin(p, jokeLead);
            PlayStatic(staticEdge, "open", staticEdgeDb);                                               // v2: the channel opens in static
            voice.Stop(); voice.clip = clip; voice.volume = VoiceGain();
            if (stage.Rig) stage.Rig.voice = voice;
            Debug.Log($"[ZodCameo] {kind}: {id} ({clip.length:F1} s, {p.gestures.Count} gestures, {p.shots.Count} shots, {p.fx.Count} fx)");
            return true;
        }

        /// <summary>
        /// After ANY defeat line Zod adds the push
        /// take) in the same open transmission (no close / reopen: the HQ keeps burning), PushGap s after the first line's tail, ducked
        /// like his other lines (the same voice source and duck). Skipped when the first line was skipped / aborted.</summary>
        public const string PushId = "zod_lose_push";
        public static int AlanCuts { get; private set; }
        public static float PushGap = 0.15f;
        public static int PushesPlayed { get; private set; }
        bool ChainPush(Perf p)
        {
            var line = lib.Line(PushId); var clip = lib.Clip(PushId);
            if (line == null || !clip) { Debug.LogWarning("[ZodCameo] " + PushId + " missing (voice clip or lines.json): the defeat window closes"); return false; }
            var beats = lib.Beats(PushId);
            var q = new Perf { kind = p.kind, id = PushId, line = line, beats = beats, clip = clip, layout = p.layout, preroll = PushGap };
            q.voiceEnd = Mathf.Min(clip.length, line.duration > 0f ? line.duration : clip.length);
            q.end = q.voiceEnd + beats.tail;
            Collect(q.gestures, line, beats.gestures, PushId);
            Collect(q.shots, line, beats.shots, PushId);
            Collect(q.fx, line, beats.fx, PushId);
            q.t = -PushGap;
            perf = q;
            voice.Stop(); voice.clip = clip; voice.volume = VoiceGain();
            if (stage) { if (stage.Rig) stage.Rig.voice = voice; stage.SetBed(string.IsNullOrEmpty(beats.preroll) ? "idle" : beats.preroll, 0.2f); }
            PushesPlayed++;
            Debug.Log($"[ZodCameo] Defeat push after {p.id}: {PushId} ({clip.length:F1} s, {q.gestures.Count} gestures, {q.fx.Count} fx)");
            return true;
        }

        static void Collect<T>(List<(float, T)> into, ZodLine line, T[] beats, string id) where T : ZodBeatAt
        {
            if (beats == null) return;
            foreach (var b in beats)
            {
                float t = ZodLibrary.Resolve(line, b);
                if (float.IsNaN(t)) { Debug.LogWarning($"[ZodCameo] {id}: beat word '{b.word}' #{b.n} not in the line (skipped)"); continue; }
                into.Add((t, b));
            }
            into.Sort((a, c) => a.Item1.CompareTo(c.Item1));
        }

        float VoiceGain()
        {
            float bus = AudioDirector.I ? AudioDirector.I.VoiceBusLinear("Voice/Commander") : PlayerPrefs.GetFloat("oz_vol_voice", 1f);
            return Mathf.Clamp01(bus * Mathf.Pow(10f, voiceDb / 20f));
        }

        bool Paused => AudioListener.pause || (hud && hud.Pause && hud.Pause.IsOpen) || (mode == Mode.Level && game && game.paused && phase == Phase.Play);

        void TickPerf(float dt)
        {
            var p = perf; if (p == null) return;
            if (!Paused) JokeTick(p);
            bool paused = Paused;
            if (stage) stage.Frozen = paused;
            if (!paused)
            {
                if (!p.voiceOn)
                {
                    p.t += dt;
                    if (p.t >= 0f && p.fadeSpeed <= 0f)
                    {
                        voice.volume = VoiceGain(); voice.Play(); p.voiceOn = true; p.t = 0f;
                        // starts talking in the end transmission, and stays low under the end screen
                        if (p.kind == Kind.Victory || p.kind == Kind.Defeat) OzFortInferno.DuckAll(-24f, 2.5f, "zod " + p.id);
                    }
                }
                else if (voice.isPlaying) p.t = voice.timeSamples / (float)Mathf.Max(1, p.clip.frequency);
                else p.t += dt;
                // v2: the opening static gets out of the way of his first word (0.12 s fade). REVIEW fix: this block used to sit
                // between the if and its else-if above, which advanced the preroll clock twice per frame (0.55 s preroll -> 0.28 s,
                // pre-roll beats early) and froze the line clock while the static faded.
                if (staticEdge && staticEdge.isPlaying && p.voiceOn && !p.closing)
                {
                    staticEdge.volume = Mathf.MoveTowards(staticEdge.volume, 0f, dt / 0.12f * Mathf.Max(0.05f, VoiceGain()));
                    if (staticEdge.volume <= 0.001f) staticEdge.Stop();
                }
                // beats
                while (p.gi < p.gestures.Count && p.gestures[p.gi].t <= p.t)
                {
                    var g = p.gestures[p.gi++].g;
                    if (g.bed) stage.SetBed(g.clip, g.fade); else stage.OneShot(g.clip, g.fade, g.hold);
                    if (!g.bed && g.clip == "facepalm" && stage.HqFireOn) stage.HqBigBlast();
                }
                while (p.si < p.shots.Count && p.shots[p.si].t <= p.t) { var s = p.shots[p.si++].s; if (fly != null && fly.Active) fly.SetShot(s.shot, s.smooth); }
                while (p.fi < p.fx.Count && p.fx[p.fi].t <= p.t) DoFx(p.fx[p.fi++].f);
                TickSlam(p);                                                                                    // voice_lines_v3: clunk + launch on the hit
                if (stage) stage.KlaxonUnderWords = p.voiceOn && ZodLibrary.CueAt(p.line, p.t, 0.05f) != null;
                if (p.fadeSpeed > 0f)
                {
                    p.fade = Mathf.MoveTowards(p.fade, 0f, dt * p.fadeSpeed);
                    if (p.fade <= 0f) { voice.Stop(); p.done = true; }
                }
                if (p.t >= p.end) p.done = true;
            }
            voice.volume = VoiceGain() * p.fade;
            StartLineTick(p);                                                                           // the announcer line in the quiet start of the level briefing
            comms.SetCue(p.voiceOn && p.fadeSpeed <= 0f ? ZodLibrary.CueAt(p.line, p.t) : null);
            comms.SetVu(stage ? Mathf.Max(stage.Mouth01, stage.AlanMouth01) : 0f);
            if (stage) stage.Push = p.t / Mathf.Max(1f, p.voiceEnd);
            if (p.kind == Kind.Briefing) comms.SetProgress((p.t + p.preroll) / Mathf.Max(1f, p.end + p.preroll));
            if (voice.isPlaying && !paused && Time.unscaledTime >= nextDuck)
            {   // like Rachel: music, ambience and Alan's bus down, the combat mix -6 / -2 dB (refreshed while he talks)
                nextDuck = Time.unscaledTime + 0.3f;
                if (AudioDirector.I) AudioDirector.I.AddDuck(AudioDirector.ZodMusicDuckOwned ? 0f : duckMusicDb, 0f, duckAmbienceDb, duckAlanBusDb, 0.55f, 0.2f, 1.0f);   // music_v3: the director's subtle eased Zod duck on the music
                if (CombatAudio.I) CombatAudio.I.DuckFor(0.55f, -6f, -2f);
            }
            if (p.done && !p.closing && p.kind == Kind.Defeat && p.id != PushId && p.fadeSpeed <= 0f && ChainPush(p)) return;
            if (p.done && !p.closing)
            {
                p.closing = true;
                EndSlam();                                                                                      // voice_lines_v3
                if (stage) { stage.GazeBoys = false; stage.HqFire(false); stage.Alarm(false); }
                comms.Close();                                                                          // v2: burst, then the CRT-off fold
                PlayStatic(staticEdge, "close", staticEdgeDb);
                stage.SetBed("idle", 0.4f);
            }
        }

        void DoFx(ZodFx f)
        {
            if (f == null || string.IsNullOrEmpty(f.fx)) return;
            switch (f.fx)
            {
                case "badge": comms.StampBadge(); break;
                case "puff": stage.Puff(Mathf.Max(0.3f, f.value)); break;
                case "exhale": stage.Exhale(f.value); break;
                case "chatter": stage.Chatter(f.value); break;
                // v2 transmission fx
                case "punch": stage.PunchIn(f.value, f.hold > 0f ? f.hold : 0.55f); break;
                case "pull": stage.PullOut(f.value); break;
                case "thrust": stage.Thrust(f.value); break;
                case "kick": stage.Kick(f.value); break;
                case "slam": DoSlam(f); break;                                                               // voice_lines_v3: the fist on the red button
                case "alan":
                    if (AlanFront(f)) break;
                    if (stage.BoysCut(f.hold > 0f ? f.hold : 2f, f.value < 0f)) { comms.Glitch(0.3f, 0.07f); AlanCuts++; Debug.Log($"[ZodCameo] {perf?.id}: cut to Alan ({(f.value < 0f ? "shy, looks down" : "talks")}, {f.hold:F2} s)"); }
                    break;
                case "glitch":
                    comms.Glitch(f.value, f.hold > 0f ? f.hold : 0.16f + 0.1f * f.value);
                    stage.Kick(0.3f * f.value);
                    PlayStatic(staticMid, UnityEngine.Random.value < 0.5f ? "burst" : "burst2", staticMidDb + 6f * Mathf.Log10(Mathf.Max(0.1f, f.value)));
                    break;
                default:
                    if (f.fx.StartsWith("framing:")) stage.SetFraming(f.fx.Substring(8));
                    break;
            }
        }

        // ================================================================================================================ frame
        void Update()
        {
            float now = Time.unscaledTime;
            if (AudioDirector.VoiceBusy) lastBusyAt = now;
            TickPerf(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
            if (perf != null && perf.closing && comms != null && comms.Closed) { perf = null; if (stage) stage.SetLive(false); }
            if (mode == Mode.Level && phase == Phase.Play && game && game.World != null) LevelRules(now);
            else if (mode == Mode.Menu) MenuRules(now);
        }

        void LateUpdate()
        {
            if (fly != null && fly.Active && !(AudioListener.pause)) fly.Tick(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
            comms?.Tick();
        }

        bool Quiet(float seconds) => Time.unscaledTime - lastBusyAt >= seconds && perf == null;

        void LevelRules(float now)
        {
            var W = game.World;
            if (!punchDone && W.GameOver)
            {
                idleDone = true;
                var es = hud ? hud.EndScreen : null;
                if (Bots || W.Winner == Team.Neutral || !es || Automated) punchDone = true;             // no human, a draw, no end screen, a check
                else if (es.IsShown)
                {
                    if (endSeenAt < 0f) endSeenAt = now;
                    if (now - endSeenAt >= endDelay && Quiet(quietBefore))
                    {
                        punchDone = true;
                        bool won = W.Winner == game.player;
                        string id = won ? PickVictory() : lib.Pick("defeat");
                        if (id != null) { EnsureComms(1f); StartPerf(won ? Kind.Victory : Kind.Defeat, id, ZodLayout.EndScreen(comms.CanvasWidth), linePreroll); }
                    }
                }
            }
            if (!idleDone && !W.GameOver)
            {
                bool allowed = PlayerPrefs.GetInt(PrefIdle, 1) == 1 && !Bots && !GameBoot.TestMode && hud && hud.enabled && hud.Canvas && hud.hudVisible && !hud.Modal
                               && game.enabled && !game.paused && !Automated;
                if (!allowed) return;
                if (AnyInput() || (input && input.MarkerTime != lastMarker)) { idleT = 0f; if (input) lastMarker = input.MarkerTime; }
                else idleT += Time.unscaledDeltaTime;
                if (idleT >= idleSeconds && Quiet(0.9f))
                {
                    idleDone = true;
                    string id = lib.Pick("idle");
                    if (id != null) { EnsureComms(1f); StartPerf(Kind.Idle, id, ZodLayout.InGame(), linePreroll); }
                }
            }
        }

        string MenuView() { try { return fView?.GetValue(menu)?.ToString() ?? "Main"; } catch (Exception) { return "Main"; } }
        string MenuSide() { try { return fSide?.GetValue(menu)?.ToString() ?? "None"; } catch (Exception) { return "None"; } }
        int MenuFocus() { try { return fFocus != null ? (int)fFocus.GetValue(menu) : -1; } catch (Exception) { return -1; } }

        void MenuRules(float now)
        {
            if (!menu) return;
            string view = MenuView(), side = MenuSide();
            if (perf != null)
            {
                bool leave = perf.kind == Kind.Greeting ? view != "Main" || side != "None" : perf.kind == Kind.Quip && view != "Select";
                if (leave && perf.fadeSpeed <= 0f) { perf.Abort(0.3f); Debug.Log("[ZodCameo] menu view changed: " + perf.id + " fades out"); }
                return;
            }
            if (comms != null && !comms.Closed) return;
            if (GameBoot.TestMode || Automated) return;                                                 // CHECK: capture takes, gate battery menu pass
            if (!greeted && view == "Main" && side == "None" && now - menuStartAt >= menuDelay && Quiet(0.3f))
            {
                greeted = true;
                EnsureStage("L02");
                StartPerf(Kind.Greeting, "zod_menu_greeting", ZodLayout.Menu(), linePreroll);
                return;
            }
            if (view == "Select" && fFocus != null)
            {
                int f = MenuFocus();
                if (f != lastFocus) { lastFocus = f; focusSince = now; }
                if (f < 0 || f >= OzLevelDef.All.Length) return;
                string lvl = OzLevelDef.All[f].Id;
                if (quipped.Contains(lvl) || now - focusSince < quipFocus || !Quiet(0.3f)) return;
                string id = "zod_select_" + lvl;
                if (lib.Line(id) == null) { quipped.Add(lvl); return; }
                quipped.Add(lvl);
                EnsureStage(lvl);
                StartPerf(Kind.Quip, id, ZodLayout.Quip(), 0.55f);
            }
            else lastFocus = -1;
        }

        // ================================================================================================================ checks
        /// <summary>Play-mode check (an editor script): runs the level briefing now (fly-over, sim waits, skip works).</summary>
        public static string DebugBriefing()
        {
            if (!I || I.mode != Mode.Level) return "no level ZodBriefing in this scene";
            if (I.phase != Phase.Play || I.perf != null) return "busy: " + I.phase + (I.perf != null ? " " + I.perf.id : "");
            I.skipped = false; I.StartCoroutine(I.BriefingRoutine());
            return "briefing started: " + I.game.level;
        }

        /// <summary>Plays one line in the window its group uses (victory / defeat = end-screen layout, idle = in-game, menu ...).</summary>
        public static string DebugLine(string id)
        {
            if (!I) return "no ZodBriefing in this scene";
            if (I.perf != null) return "busy: " + I.perf.id;
            var line = I.lib.Line(id); if (line == null) return "unknown line " + id;
            I.EnsureComms(1f);
            var lay = line.group == "victory" || line.group == "defeat" ? ZodLayout.EndScreen(I.comms.CanvasWidth) : line.group == "menu" ? ZodLayout.Menu()
                    : line.group == "map_select" ? ZodLayout.Quip() : ZodLayout.InGame();
            return I.StartPerf(Kind.Debug, id, lay, I.linePreroll) ? "playing " + id : "failed " + id;
        }

        /// <summary>
        /// so the HQ fire (defeat), the gaze at the boys, the alarm (nuke) and the defeat push come on as in the game. A nuke slam only
        /// logs (no strike).</summary>
        public static string DebugLineAs(string id, Kind kind)
        {
            if (!I) return "no ZodBriefing in this scene";
            if (I.perf != null) return "busy: " + I.perf.id;
            if (I.lib.Line(id) == null) return "unknown line " + id;
            I.EnsureComms(1f);
            ZodLayout lay; float pre = I.linePreroll;
            switch (kind)
            {
                case Kind.Briefing: lay = ZodLayout.Briefing(); pre = I.briefingPreroll; break;
                case Kind.Victory: case Kind.Defeat: lay = ZodLayout.EndScreen(I.comms.CanvasWidth); break;
                case Kind.Nuke: lay = ZodLayout.InGame(); I.nukeSlam = () => Debug.Log("[ZodCameo] DebugLineAs: slam (no strike launched)"); break;
                default: lay = ZodLayout.InGame(); break;
            }
            return I.StartPerf(kind, id, lay, pre) ? "playing " + id + " as " + kind : "failed " + id;
        }
        /// <summary>Play check: the line playing now and its clock (s from the voice start; negative in the preroll), or null.</summary>
        public static string DebugLineId => I && I.perf != null ? I.perf.id : null;
        public static float DebugLineT => I && I.perf != null ? I.perf.t : -99f;

        /// <summary>A random victory (true) or defeat line in the end-screen window, now.</summary>
        public static string DebugPunchline(bool win) => I ? DebugLine(I.lib.Pick(win ? "victory" : "defeat")) : "no ZodBriefing";
        /// <summary>The idle barb now (normally after 60 s without input).</summary>
        public static string DebugIdle() => I ? DebugLine(I.lib.Pick("idle")) : "no ZodBriefing";

        /// <summary>v2 Play check: fire one transmission effect on the line that is playing now ("punch", "pull", "thrust", "glitch",
        /// "kick"), e.g. <c>return OpenZ.ZodBriefing.DebugFx("thrust", 0.8f);</c> during DebugIdle().</summary>
        public static string DebugFx(string fx, float value = 1f)
        {
            if (!I || I.perf == null || !I.stage) return "no line playing (start one with DebugIdle / DebugLine)";
            I.DoFx(new ZodFx { fx = fx, value = value });
            return $"fx {fx} {value:F2}: fov {I.stage.FovNow:F1}, thrust {I.stage.Thrusting}, burst {(I.comms != null ? I.comms.Burst : 0f):F2}";
        }

        /// <summary>One-line state for the Play check.</summary>
        public static string Status()
        {
            if (!I) return "ZodBriefing: none in this scene (cameo " + (Enabled ? "on" : "off") + ")";
            var s = I.stage;
            return $"ZodBriefing: mode {I.mode}, phase {I.phase}, fromMenu {I.fromFlow}, perf {(I.perf != null ? I.perf.id + " t=" + I.perf.t.ToString("F2") : "none")}, "
                 + $"stage {(s ? (s.HasModel ? "model" : "NO MODEL") + ", layer " + s.Layer + ", live " + s.Live + ", clip " + s.CurrentClip : "not built")}, "
                 + $"briefed [{string.Join(",", briefed)}], greeted {greeted}, idle {I.idleT:F0}/{I.idleSeconds:F0} s, voice gain {I.VoiceGain():F2}"
                 + (s ? $", fov {s.FovNow:F1}, thrust {s.Thrusting}" : "") + (I.comms != null ? $", burst {I.comms.Burst:F2}" : "");
        }
    }
}
