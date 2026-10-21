using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenZ
{
    // ============================================================================================================================
    // polish round: POLISH_PLAN.md, runtime table: Resources/OpenZ/Audio/openz_sound_events.json).
    //
    // category buses (weapons .30 / explosions .26 / impacts .31 / destruction .25 / vehicles .55 / world .70), the loudness
    // AudioDirector never mutes CombatAudio, never builds a listener and never plays a sound the live views already play.
    //
    // Who plays what (no double sounds):
    //  CombatAudio + LegacyMatch / LegacyEngineAudio and the live views (unchanged): every shot, launch, projectile impact, explosion,
    //    robot / vehicle / cannon / crate death, ricochet, driver hit / snipe, building collapse / settle / debris (RulesView,
    //    BuildingBreaker), rock and prop breaks (CombatView, RockPillar, PropBreakables), the nuke (NukeFx), engines and the mech
    //    steps (LegacyEngineAudio), boarding foley (CombatView EnterSequence, BoardingView), exit, grenade pickup / land, wading
    //    splash (OpenZWading), flag touch / raise / gained cue (FlagView, RulesView fallback), built / placed / repair / crane /
    //    revived one-shots (RulesView). AudioDirector only COUNTS these for the coverage report (CombatAudio.PlayCounts), and an
    //    tracks, turret servo, engine layers) stay silent.
    //  AudioDirector: music (Zod CALM / ATTACKING / FORT states, PlayerPrefs oz_music toggle, DC16), ambience beds / river /
    //    lamps / critter timers, Rachel (every ann_* line, one queue) and Alan (every vo_* line, main / alert / end channels) incl.
    //    the lines the live views ask CombatAudio.Voice for (routed here by CombatAudio.VoiceRoute), UI sounds (2D; HUD clicks and
    //    Play2D calls routed by CombatAudio.Play2DRoute), and the world layers nobody else plays: robot footsteps / squad march /
    //    wading loop / water steps, Tough steps, tank lids, casings, gatling spin, burning ground, wreck and building fire loops,
    //    creaks, factory doors / hum / accents (FactoryViews.AudioEvent), radar loop, fort alarm, gun stored, repair / crane work,
    //    building captured, bridge rebuild, structure groan / glass, booms along a dying building (RulesView.Booms is silent), water
    //    splashes and plunges, ragdoll / chunk impacts, lamps and searchlights (NightLights.AudioEvent), critters
    //    (AmbientLife.AudioEvent), radar dish / repair hoist (BuildingAnimView.AudioEvent).
    //  Every AudioDirector world sound goes through CombatAudio's mix: its bus gain, leveller (measured clip LUFS,
    //  Rachel line calls CombatAudio.DuckFor, so gunfire and explosions sit >= 6 dB under the voices (legacy_match model).
    //  Only UI, the announcer and the robot voices are 2D.
    //  (production window, HUD toasts / minimap ping, pause menu + end screen, PlayerInput refusals / box / cannon placement, camera
    //  jumps, rock chips + crumbles, sandbags, unit-cap lines, the Zod no-way refusal). dst_bridge_collapse, bld_building_revived,
    //  One sound per UI action: a specific cue replaces the generic HUD click of the same frame. FireEveryEvent() = coverage test.
    //  - P0 boot: every Single scene load gets its own director IN that scene (SceneManager.sceneLoaded); the dying scene's director
    //    retires (T19: menu -> level ran with NO director: no ambience, UI, footsteps, doors, Rachel). UI() boots one if missing.
    //    Start logs "[AudioDirector] live: ..." for the menu -> level smoke test (tools/smoke_boot_log.py).
    //  - Mix: world / foley / ambience spots on CombatAudio's presence curves, table re-levelled to category targets at the default
    //    view (tools/mix_v2_model.py), beds -38..-40 LUFS always (priority 24), per-bus voice duck (Alan -4 / -1, Rachel -6 / -2),
    //    Alan -3 dB / Rachel -6 dB buses, ambience -2 dB in fights (was -4).
    //  - Triggers: single footsteps up to 95 m camera height (budget 12 / s, on screen), tracks / tyres under the 3 nearest moving
    //    vehicles, fan spin-up at most every 20 s per factory, deselect tick, group recall only when a group was recalled, a soft
    //    menu bed while music is off; announcer groups (manufactured 8 s, territory 4 s, production 5 s): one line per burst.
    // Coverage (plan/04 5b): Triggers / Plays per event id plus CombatAudio's play counts for the events it owns;
    // CoverageReport() -> markdown PASS / WARN / FAIL, CoverageSweep(), WriteCoverageReport(path).
    // ============================================================================================================================

    [Serializable] public class SoundClipRef { public string res; public float gainDb; public bool standin; public string line; }
    [Serializable] public class SoundBusDef { public string name; public float volumeDb; }
    [Serializable]
    public class SoundEventDef
    {
        public string id, bus, channel, status;
        public bool is3D, noRepeat, loop, inViewOnly, offscreen, coverage;
        public float blend, minDist, maxDist, spread, volDb, volVarDb, pitchVar, pitchMul, cooldown, gap, doppler, maxCamHeight;
        public float duckMusic, duckSfx, duckAmb, duckRobots;
        public int maxVoices, priority, rank;
        public string[] routes;
        public SoundClipRef[] clips;
        [NonSerialized] public List<AudioClip> loaded;
        [NonSerialized] public List<float> gains;
        [NonSerialized] public List<string> lines;
        [NonSerialized] public List<bool> standins;
        [NonSerialized] public List<int> bag;
        [NonSerialized] public int lastIdx = -1;
        [NonSerialized] public float lastPlay = -100f;
    }
    [Serializable] public class SoundTable { public int version; public SoundBusDef[] buses; public SoundEventDef[] events; }
    [Serializable] public class ClipLevel { public string n; public float l; }
    [Serializable] public class ClipLevelTable { public ClipLevel[] clips; }

    [DefaultExecutionOrder(40)]
    public partial class AudioDirector : MonoBehaviour
    {
        public static AudioDirector I { get; private set; }
        public const string TablePath = "OpenZ/Audio/openz_sound_events";
        public const string LevelsPath = "OpenZ/Audio/openz_clip_levels";
        /// <summary>DC16: adaptive music on in normal play (1, default), off for the recorded takes (0). Main menu options may expose it.</summary>
        public const string MusicPref = "oz_music";

        [Header("Master")]
        [Range(0, 1)] public float masterVolume = 1f;
        [Tooltip("Play stand-ins for NEW items that are not generated yet (manifest status 'standin')")] public bool useStandins = true;
        [Header("Automatic sources")]
        public bool simEvents = true;
        public bool footsteps = true, voices = true, music = true, ambience = true, uiPolling = true, physicsImpacts = true, vehicleFoley = true;
        [Tooltip("Route the live views' CombatAudio.Voice / Play2D calls here (one Alan, one Rachel, one UI owner)")] public bool routeLegacyVoices = true;
        [Header("Voices")]
        [Tooltip("Alan: metallic radio (high-pass 250 Hz + distortion 0.12 + light chorus, as CombatAudio.VoiceSource)")] public bool alanRadioFilter = true;
        [Tooltip("Rachel: optional high-pass 200 Hz radio feel")] public bool rachelRadioFilter = false;
        [Header("Robot deaths (death_voices_v1)")]
        [Tooltip("Robot deaths pick a cry or a voiceless body death by death type (DeathVoices); off = the legacy single 'death' clip")] public bool deathVoices = true;
        [Tooltip("Share of deaths that are voiceless (metal crack, short circuit, servo dying ...) while the cry limiter is open: bullet, blast, fire, laser, crush")]
        [Range(0, 1)] public float voicelessBullet = 0.4f, voicelessBlast = 0.6f, voicelessFire = 0.3f, voicelessLaser = 0.4f, voicelessCrush = 0.7f;
        [Tooltip("Death cries across the whole battlefield: at most this many in any 1 s")] [Range(1, 4)] public int deathCriesPerSecond = 2;
        [Tooltip("Seconds between two death cries anywhere")] public float deathCryGap = 0.35f;
        [Tooltip("A death clip never comes back within this many deaths (cries, bodies and grunts each)")] public int deathNoRepeat = 8;
        [Tooltip("Chance that a non-lethal hit on a robot on screen grunts (limiter: 0.9 s across the map, none right after a death cry)")] [Range(0, 1)] public float hitGruntChance = 0.2f;
        [Tooltip("Seconds before the same robot grunts again")] public float hitGruntPerRobot = 4f;
        [Header("Budget")]
        public int oneShotSources = 28;
        public int maxFireLoops = 6, maxRiverEmitters = 3;

        /// <summary>DC16 music toggle (PlayerPrefs oz_music, default on). Setting it fades the music out / back in at once.</summary>
        public static bool MusicEnabled
        {
            get => !SessionMusicOff && PlayerPrefs.GetInt(MusicPref, 1) == 1;                             // menu_exe_v1: -take is a session override (AudioDirector.MenuExe.cs)
            set { SessionMusicOff = false; PlayerPrefs.SetInt(MusicPref, value ? 1 : 0); PlayerPrefs.Save(); if (I) I.OnMusicToggle(value); }   // menu_exe_v1: a real switch ends the session override
        }
        bool MusicOn => music && MusicEnabled;

        // ------------------------------------------------------------------ table
        readonly Dictionary<string, SoundEventDef> defs = new Dictionary<string, SoundEventDef>();
        readonly Dictionary<string, float> busDb = new Dictionary<string, float>();          // OzAudio (pause / main menu sliders) writes this by reflection
        readonly Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, float> clipLufs = new Dictionary<string, float>();
        float tableSfxDb;
        public bool Loaded { get; private set; }

        // ------------------------------------------------------------------ counters (coverage)
        public readonly Dictionary<string, int> Triggers = new Dictionary<string, int>();
        public readonly Dictionary<string, int> Plays = new Dictionary<string, int>();
        public readonly Dictionary<string, int> SweepPlays = new Dictionary<string, int>();
        public int Played { get; private set; }
        bool sweeping;
        readonly Dictionary<string, int> caBaseline = new Dictionary<string, int>();         // CombatAudio.PlayCounts at ResetCounters

        // ------------------------------------------------------------------ voices (sources)
        class Voice
        {
            public AudioSource src; public AudioLowPassFilter lp; public SoundEventDef def;
            public float baseLin = 1f, fade = 1f, fadeTarget = 1f, fadeSpeed = 10f, offLin = 1f, level = 1f, start;
            public int emitter; public Transform follow; public bool loop, busy, ignorePause, combatMix; public string key;
            public bool followed;
            public float range;                                                                              // audio_v2: curve range before the zoom scale (0 = own curve / 2D)
        }
        readonly List<Voice> pool = new List<Voice>();
        readonly Dictionary<string, Voice> loops = new Dictionary<string, Voice>();
        readonly Dictionary<(string, int), float> lastEmit = new Dictionary<(string, int), float>();

        ZGame game; RtsCamera rts; PlayerInput input; DayNight dayNight; Camera cam;
        static AnimationCurve rolloff;
        float caBaseVolume = -1f; CombatAudio caSeen;
        static CombatAudio CA => CombatAudio.I;

        // ============================================================================================================================
        // Ownership: events the live CombatAudio already plays (legacy_match sound_overrides.json + the live views)
        // ============================================================================================================================
        sealed class CaRoute { public string key; public string[] bank; public float gain, pitch; public bool wide; public string[] count; }
        static CaRoute R(string key, string[] bank, float gain, float pitch = 1f, bool wide = false, params string[] count) =>
            new CaRoute { key = key, bank = bank, gain = gain, pitch = pitch, wide = wide, count = count.Length > 0 ? count : new[] { key } };
        public static readonly string[] BankLaserFire = { "electro-zip-2", "electro-zip-3" };
        public static readonly string[] BankLaser = { "laser-impact-electric-a", "laser-impact-electric-b" };
        static readonly string[] BankFlame = { "flame_loop1" },
            BankRicochet = { "v3-armor-ricochet-a", "s3_ricochet_a", "s3_ricochet_b", "s3_ricochet_c", "s3_bullethit_1" }, BankNuke = { "legacy-nuke-long" }, BankRock = { "v3-rock-break-a", "v3-rock-break-b" },
            BankSettle = { "building-settle-20260909" }, BankEngineStart = OzSoundV3.BankEngineStart;                                          // sound_v3: 4 takes

        /// <summary></summary>
        static readonly Dictionary<string, CaRoute> Combat = new Dictionary<string, CaRoute>
        {
            ["wpn_grunt_rifle"] = R("bolt", LegacyMatch.BankBolt, LegacyMatch.GainBolt),
            ["wpn_psycho_mg"] = R("mg", WeaponsV2.BankMg, WeaponsV2.GainMg),                                // weapons_v2: heavy MG bursts (was the grunt's bolt)
            ["wpn_jeep_mg"] = R("bolt", LegacyMatch.BankBolt, LegacyMatch.GainBolt),
            ["wpn_sniper_rifle"] = R("sniper", WeaponsV2.BankSniper, WeaponsV2.GainSniper, 1f, false, "sniper", "sniper_tail"),   // weapons_v2: crack + echo
            ["wpn_gatling_fire"] = R("burst", LegacyMatch.BankRifleBurst, LegacyMatch.GainDefault),
            ["wpn_laser_fire"] = R("laser", WeaponsV2.BankLaserFire, WeaponsV2.GainLaserFire),
            ["imp_laser"] = R("laser_hit", WeaponsV2.BankLaserZap, WeaponsV2.GainLaserZap),
            ["wpn_apc_crew"] = R("bolt", LegacyMatch.BankBolt, LegacyMatch.GainBolt, 1f, false, "bolt", "sniper", "burst", "rifle", "rocket", "laser", "flame"),
            ["wpn_tough_bazooka"] = R("rocket", LegacyMatch.BankRocket, LegacyMatch.GainDefault),
            ["wpn_tough_backblast"] = R("backblast", WeaponsV2.BankBackblast, WeaponsV2.GainBackblast),        // weapons_v2: back-blast whoosh + thump
            ["proj_rocket_flight"] = R("rocket_flight", LegacyMatch.BankRocket, LegacyMatch.GainRocketFlight),
            ["wpn_missile_cannon"] = R("mc_rocket", OzSoundV3.BankMcRoar, LegacyMatch.GainDefault, 1f, false, "mc_rocket", "launch", "mech_flight"),   // sound_v3
            ["wpn_mlrs"] = R("launch", LegacyMatch.BankMlrsLaunch, LegacyMatch.GainDefault, 0f),
            ["wpn_light_tank"] = R("tankshot", LegacyMatch.BankTankShot, LegacyMatch.GainTankShot),
            ["wpn_medium_tank"] = R("mech_rocket", OzSoundV3.BankMechRocket, OzSoundV3.GainMechRocket, 1f, false, "mech_rocket", "mech_flight"),
            ["wpn_heavy_tank"] = R("heavyshot", LegacyMatch.BankHeavyTankShot, LegacyMatch.GainHeavyTankShot),   // mlrs_heavy_v1: own CombatAudio key
            ["wpn_gun_cannon"] = R("cannon", LegacyMatch.BankCannon, LegacyMatch.GainDefault),
            ["wpn_howitzer"] = R("cannon", LegacyMatch.BankCannon, LegacyMatch.GainDefault),
            ["wpn_fortgun"] = R("fortgun", LegacyMatch.BankHeavyTankShot, LegacyMatch.GainHeavyTankShot, 0.86f),
            ["wpn_grenade_throw"] = R("grenade", LegacyMatch.BankGrenade, LegacyMatch.GainDefault),
            ["imp_rocket_armor"] = R("rocket_armor", LegacyMatch.BankRocketArmor, LegacyMatch.GainRocketArmor),
            ["imp_bullet_metal"] = R("ricochet", OpenZFxCatalog.RicochetClips, OpenZFxCatalog.RicochetVolume, 1f, false, "ricochet", OpenZHitSparks.SoundKey, "metal_ping"),   // hitsparks_v1
            ["imp_bullet_robot"] = R(OpenZHitSparks.SoundKey, OpenZHitSparks.Bank, OpenZHitSparks.BankGain),
            ["imp_snipe"] = R("sniped", BankRicochet, 1f),
            ["imp_driver_hit"] = R("driverhit", BankRicochet, 0.8f),
            ["imp_snipe_pop"] = R(PlacementPop.PopKey, OzSoundV3.BankSnipePop, PlacementPop.SnipePopGain),
            ["exp_small"] = R("explosion", LegacyMatch.BankExplosion, LegacyMatch.GainExplosion, 1f, false, "explosion", "tank_impact"),
            ["exp_medium"] = R("explosion", LegacyMatch.BankExplosion, LegacyMatch.GainExplosion, 1f, false, "explosion", "tank_impact", "heavy"),
            ["exp_large"] = R("heavy", LegacyMatch.BankHeavy, LegacyMatch.GainHeavy, 1f, false, "heavy", "tank_impact", "explosion"),
            ["exp_vehicle_death"] = R("vdeath", LegacyMatch.BankHeavy, LegacyMatch.GainHeavy),
            ["exp_cannon_death"] = R("vdeath", LegacyMatch.BankHeavy, LegacyMatch.GainHeavy),
            ["exp_fort_side_booms"] = R("fortboom", LegacyMatch.BankExplosion, LegacyMatch.GainExplosion, 1f, true),
            ["nuke_flash"] = R("nuke", BankNuke, 1.4f, 1f, true),
            ["dst_collapse_large"] = R("collapse", LegacyMatch.BankCollapse, LegacyMatch.GainCollapse),
            ["dst_collapse_medium"] = R("collapse", LegacyMatch.BankCollapse, LegacyMatch.GainCollapse),
            ["dst_rock_break"] = R("rock", BankRock, 0.9f),
            ["dst_debris_settle"] = R("settle", BankSettle, 0.8f),
            ["dst_turret_crash"] = R("debris", LegacyMatch.BankTankImpact, 0.7f, 1f, false, "debris", "turret"),
            // death_voices_v1: death_robot / death_robot_melt (and the new death_* events) play from AudioDirector.DeathSound: a cry
            ["veh_engine_start"] = R("engine_start", BankEngineStart, 0.5f),
            ["brd_capture_start"] = R("capture_start", OzSoundV3.BankCaptureStart, 0.7f),   // sound_v3
            ["brd_vehicle_enter"] = R("enter", OzSoundV3.BankEnter, 0.65f),
            ["brd_vehicle_capture"] = R("capture", OzSoundV3.BankCapture, 0.9f),
            ["brd_gun_mount"] = R("gun_enter", new[] { "mech-servo-1", "mech-servo-2" }, 0.72f),
            ["brd_exit"] = R("exit", new[] { "exit" }, 0.8f),
            ["rob_grenade_pickup"] = R("pickup", OzSoundV3.BankPickup, 0.8f),   // sound_v3: the crate
            ["rob_water_enter"] = R("swim", OzSoundV3.BankSwim, OpenZFxCatalog.WadeSwimVolume),
            ["ter_flag_capture"] = R("ter_flag_capture", new[] { "flag-capture-1", "flag-capture-2", "s3_flag_raise_tight", "s3_flag_capture_d" }, 0.8f, 1f, false, "ter_flag_capture", "flag"),
            ["ter_zone_gained"] = R("ter_zone_gained", OzSoundV3.BankZoneGained, 0.55f, 1f, true, "ter_zone_gained", "flag"),
            ["bld_cannon_placed"] = R(PlacementPop.PlacedKey, new[] { PlacementPop.PlacedClip }, PlacementPop.PlacedGain),
            ["bld_repair_in"] = R("repair", OzSoundV3.BankWrench, 0.9f),
            ["bld_repair_out"] = R("repairout", BankEngineStart, 0.8f),
            ["veh_crane_arm"] = R("crane", OzSoundV3.BankWrench, 0.9f),
            ["bld_rollout_robot"] = R("built", OzSoundV3.BankRolloutRobot, 0.8f),   // sound_v3: was the flag-capture clip
            ["bld_rollout_vehicle"] = R("built", BankEngineStart, 0.8f),
        };
        /// <summary>Engine loops = LegacyEngineAudio (tank.mp3 / car.mp3 banks, mech steps for the medium tank).</summary>
        static readonly Dictionary<string, string> EngineOwned = new Dictionary<string, string>
        {
            ["veh_engine_jeep"] = "car", ["veh_engine_light"] = "tank", ["veh_engine_heavy"] = "tank", ["veh_engine_mlrs"] = "tank",
            ["veh_engine_apc"] = "tank", ["veh_engine_crane"] = "tank", ["veh_engine_medium"] = "mech_step",
        };
        /// <summary></summary>
        static readonly HashSet<string> Silent = new HashSet<string>
        {
            "proj_shell_whistle", "veh_turret_traverse",                                        // sound_v3: wpn_breech_clank plays now (fort gun + tank / gun reloads); the fort turret has its own heavy events                    // SOUND-bundle: proj_rocket_flight plays now (the Tough's bazooka rocket)
            "veh_rev",                                                                            // sound_v3: tracks squeal / suspension / engine shutdown / brake play now (OzSoundV3.VehicleTick, EngineDies)
            // among the vehicle sounds, T19 showed tanks and jeeps driving past with no track or tyre sound)
        };
        /// <summary>Coverage rows that are silent on purpose (reported n/a with the reason, never FAIL).</summary>
        static readonly Dictionary<string, string> ByDesign = new Dictionary<string, string>
        {
        };
        /// <summary>
        /// </summary>
        static readonly Dictionary<string, string> LegacyRule = new Dictionary<string, string>
        {
            ["wpn_gatling_fire"] = "legacy_match: weapon = legacy rifle burst; sd_gatling_burst_a/b stay unused",
        };
        /// <summary>
        /// because they are mixer rules. The coverage reports mark them n/a with this reason instead of FAIL.</summary>
        static readonly Dictionary<string, string> NotInPlay = new Dictionary<string, string>
        {
            // weapons_v2: wpn_tough_backblast plays (WeaponsV2Runner, CombatAudio key "backblast")
            // pyro_fire_v2: wpn_pyro_flame_start / wpn_pyro_flame_stop play when a Pyro's jet starts / ends (OpenZPyroFire)
            ["wpn_grenade_bounce"] = "DC11: GrenadeFx plays the legacy grenade land (wtr_grenade_plop on water)",
            ["imp_bullet_sand"] = "DC11: the legacy map has no bullet ground impacts",
            ["imp_bullet_stone"] = "DC11: the legacy map has no bullet ground impacts",
            ["nuke_descent"] = "DC11: NukeFx plays the legacy nuke descent",
            ["nuke_shockwave"] = "DC11: NukeFx plays legacy-nuke + side booms",
            ["nuke_rumble_tail"] = "DC11: NukeFx plays the pitched collapse rumble",
            ["nuke_debris_rain"] = "DC11: NukeFx side booms + rumble cover the debris",
            ["exp_debris_rain"] = "DC11: explosions are the legacy banks (legacy_match); only nuke_debris_rain routed here",
            ["exp_grenade_crate_chain"] = "DC11: LegacyMatch.CrateDeath plays the crate chain",
            ["dst_hut_break"] = "DC11: CombatView plays LegacyMatch.PropDeath + the legacy rock break",
            ["dst_map_object_break"] = "DC11: CombatView plays LegacyMatch.PropDeath + the legacy rock break",
            ["dst_debris_concrete"] = "DC11: BuildingBreaker plays its own debris impacts (routes of the two above)",
            ["dst_debris_metal"] = "DC11: BuildingBreaker plays its own debris impacts (routes of the two above)",
            ["dst_debris_wood"] = "DC11: BuildingBreaker plays its own debris impacts (routes of the two above)",
            ["brd_ladder"] = "DC11: BoardingView plays the boarding foley",
            ["brd_seat"] = "DC11: BoardingView plays the boarding foley",
            ["brd_hatch_close"] = "DC11: BoardingView plays the boarding foley",
            ["brd_apc_load"] = "DC11: BoardingView plays the boarding foley",
            ["exp_distant_rule"] = "mixer rule (no clip)", ["veh_damaged"] = "mixer rule (no clip)",
            ["mus_pause"] = "mixer rule (no clip)", ["mus_end"] = "mixer rule (no clip)",
        };
        /// <summary>UI_STYLE_GUIDE section 10 ids -> table ids.</summary>
        static readonly Dictionary<string, string> Alias = new Dictionary<string, string>
        { ["ui_tick"] = "ui_hover", ["ui_back"] = "ui_cancel", ["ui_open"] = "ui_panel_open", ["ui_close"] = "ui_panel_close", ["ui_select_card"] = "ui_level_select" };

        static string Canon(string id) => id != null && Alias.TryGetValue(id, out var a) ? a : id;
        public static bool OwnedByCombatAudio(string id) => id != null && (Combat.ContainsKey(id) || EngineOwned.ContainsKey(id));

        // ------------------------------------------------------------------ boot
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (I || FindFirstObjectByType<AudioDirector>()) return;
            BootIn(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), false);
        }

        // ---- audio_v2 P0 (T19): Unity runs AfterSceneLoad boots for the FIRST scene only, and the menu loads the level with
        //      LoadSceneAsync(Single): one frame later (OzSceneFlow) the menu director was still alive, so the level got none.
        //      Now every Single load creates the director inside the loaded scene and the old scene's director hands over at once.
        static bool quitting, takeover;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InstallSceneHook()
        {
            quitting = false;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoadedBoot;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoadedBoot;
            Application.quitting -= OnQuitting; Application.quitting += OnQuitting;
        }
        static void OnQuitting() => quitting = true;
        static void OnSceneLoadedBoot(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (mode != UnityEngine.SceneManagement.LoadSceneMode.Single || quitting || !s.IsValid()) return;
            if (I && I.gameObject.scene == s) return;
            BootIn(s, true);
        }
        static void BootIn(UnityEngine.SceneManagement.Scene s, bool handOver)
        {
            if (quitting || !Application.isPlaying) return;
            if (s.IsValid() && s.name == "Intro") return;
            var go = new GameObject("AudioDirector");
            if (s.IsValid() && s.isLoaded && go.scene != s) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, s);
            takeover = handOver;
            try { go.AddComponent<AudioDirector>(); } finally { takeover = false; }
        }
        /// <summary>audio_v2: boots a director when none is live (a UI sound asked for before the scene hook ran, or during a scene
        /// change). True when one is live afterwards.</summary>
        public static bool EnsureLive()
        {
            if (I) return true;
            if (quitting || !Application.isPlaying) return false;
            BootIn(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), false);
            return I;
        }
        void Retire()
        {
            Unhook();
            foreach (var s in GetComponentsInChildren<AudioSource>(true)) if (s) s.Stop();
            enabled = false;
            Destroy(gameObject);
        }

        void Awake()
        {
            if (I && I != this)
            {
                if (!takeover || I.gameObject.scene == gameObject.scene) { Destroy(gameObject); return; }   // a second director in the same scene
                var old = I; I = this; old.Retire();                                                   // audio_v2: the old scene hands over
            }
            I = this;
            rolloff = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.15f, 0.95f), new Keyframe(0.35f, 0.7f), new Keyframe(0.6f, 0.35f),
                                         new Keyframe(0.85f, 0.1f), new Keyframe(1f, 0f));
            LoadTable();
            for (int i = 0; i < oneShotSources; i++) pool.Add(NewVoice("OneShot" + i));
            BuildChannels();
        }

        void Start()
        {
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            rts = FindFirstObjectByType<RtsCamera>();
            input = FindFirstObjectByType<PlayerInput>();
            dayNight = FindFirstObjectByType<DayNight>();
            cam = Camera.main;
            ApplyUserVolumes();
            Hook();
            Debug.Log($"[AudioDirector] live: scene {gameObject.scene.name}, {(game != null ? "level " + game.level : "menu")}, {defs.Count} events, "
                      + $"CombatAudio {(CombatAudio.I ? "yes" : "no")}, voice route {(CombatAudio.VoiceRoute != null ? "hooked" : "none")} (audio_v2)");
            if (game != null)
            {
                BuildAmbienceEmitters();
                // zod_cameo: the Commander Zod briefing holds the level-start sting and lines until it hands the level over
                if (HoldStartLines != null && HoldStartLines()) startLinesHeld = true; else PlayStartLines();
            }
            else if (MusicOn) StartMenuMusic();
            else if (MenuMusicOffNote()) { }                                                             // menu_exe_v1: logs why the menu is quiet (always false)
            else if (menuAmbience) StartMenuBed();                                                      // audio_v2: the T19 menu was digital silence
        }
        [Tooltip("audio_v2: a soft desert dusk bed under the main menu while music is off")] public bool menuAmbience = true;

        /// <summary>zod_cameo: true while a cutscene (the Commander Zod level briefing) holds the level-start sting and lines.</summary>
        public static Func<bool> HoldStartLines;
        bool startLinesHeld, startLinesPlayed;
        /// <summary>zod_cameo: the level-start sting, Rachel's start line and Alan's game-start line (once per level; the briefing
        /// releases them when it hands the level to the player, Update plays them as soon as nothing holds them any more).</summary>
        public void PlayStartLines()
        {
            if (startLinesPlayed || game == null) return;
            startLinesPlayed = true; startLinesHeld = false;
            Schedule(0.3f, () => Announce(StartLine())); Schedule(0.2f, () => UI("ter_game_start_sfx"));
            StartCoroutine(AlanAfterRachel("vo_game_start", 1.2f));
        }

        /// <summary>A line that must follow Rachel: waits at least `minDelay`, then until no voice plays or is queued (max 8 s), + 0.3 s.</summary>
        System.Collections.IEnumerator AlanAfterRachel(string id, float minDelay)
        {
            yield return new WaitForSecondsRealtime(minDelay);
            float s = Time.unscaledTime;
            while ((VoiceBusy || rachelQueue.Count > 0) && Time.unscaledTime - s < 8f) yield return null;
            yield return new WaitForSecondsRealtime(0.3f);
            Say(id);
        }
        /// <summary>zod_cameo: linear gain of a voice bus for a 2D voice player outside the director (Commander Zod on
        /// "Voice/Commander"): Master + Voice (the VOICE slider) + the sub-bus when the table has one; no duck.</summary>
        public float VoiceBusLinear(string bus = "Voice") => masterVolume * Db(BusGainDb(bus));
        void StartMenuBed() { if (HasClips("amb_l02_dusk")) StartLoop("bed:menu", "amb_l02_dusk", ListenerPos, null, Db(-4f)); }

        /// <summary></summary>
        string StartLine()
        {
            var hud = OpenZHud.I;
            bool banner = hud && hud.missionIntro && PlayerPrefs.GetInt("oz_hud_intro", 1) == 1 && !OzMotion.Reduced;
            return banner && HasClips("ann_level_start") && !TeamColors.Swap ? "ann_level_start" : "ann_battle_start";
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            Unhook();
            if (AudioListener.pause) AudioListener.pause = false;
        }

        void LoadTable()
        {
            var ta = Resources.Load<TextAsset>(TablePath);
            if (!ta) { Debug.LogWarning("[AudioDirector] " + TablePath + ".json missing: import the sound tables into Assets/OpenZ/Resources/OpenZ/Audio"); return; }
            var t = JsonUtility.FromJson<SoundTable>(ta.text);
            foreach (var b in t.buses) busDb[b.name] = b.volumeDb;
            // legacy_match mix_overrides_for_AudioDirector: voices at 0 dB (the SFX side is CombatAudio.BusGain, one source of truth)
            // audio_v2: Alan -3 dB (his T19 level: the legacy lines played at .72), Rachel 3 dB under him (subtle); the master no longer
            // squashes the voices, and Normalize is off for the voice clips (apply_audio_v2.py --import-settings)
            busDb["Voice/Announcer"] = -6f; busDb["Voice/Robots"] = -3f;
            tableSfxDb = busDb.TryGetValue("SFX", out var s0) ? s0 : 0f;
            var lv = Resources.Load<TextAsset>(LevelsPath);
            if (lv) { var lt = JsonUtility.FromJson<ClipLevelTable>(lv.text); if (lt?.clips != null) foreach (var c in lt.clips) clipLufs[c.n] = c.l; }
            foreach (var d in t.events)
            {
                d.loaded = new List<AudioClip>(); d.gains = new List<float>(); d.lines = new List<string>(); d.standins = new List<bool>();
                bool skipClips = OwnedByCombatAudio(d.id) || Silent.Contains(d.id);                 // CombatAudio's own banks play these
                if (d.clips != null && !skipClips)
                    foreach (var c in d.clips)
                    {
                        if (c.standin && !useStandins) continue;
                        var clip = Clip(c.res);
                        if (!clip) continue;
                        d.loaded.Add(clip); d.gains.Add(c.gainDb); d.lines.Add(c.line ?? ""); d.standins.Add(c.standin);
                    }
                if (d.routes == null) d.routes = new string[0];
                if (d.maxVoices <= 0) d.maxVoices = 1;
                if (d.pitchMul <= 0) d.pitchMul = 1f;
                if (d.bus == null) d.bus = "SFX/World";
                defs[d.id] = d;
            }
            Loaded = true;
        }

        AudioClip Clip(string res)
        {
            if (string.IsNullOrEmpty(res)) return null;
            if (clipCache.TryGetValue(res, out var c)) return c;
            c = Resources.Load<AudioClip>(res);
            clipCache[res] = c;
            return c;
        }

        public SoundEventDef Def(string id) => id != null && defs.TryGetValue(id, out var d) ? d : null;
        bool HasClips(string id) { var d = Def(id); return d != null && d.loaded.Count > 0; }

        Voice NewVoice(string name)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var v = new Voice { src = go.AddComponent<AudioSource>(), lp = go.AddComponent<AudioLowPassFilter>() };
            v.src.playOnAwake = false; v.lp.cutoffFrequency = 22000f; v.lp.enabled = false;
            return v;
        }

        /// <summary>Pause / main menu sliders (PlayerPrefs oz_vol_music / _effects / _voice) through the live OzAudio, which offsets
        /// this director's bus table; SFX also scales CombatAudio.volume (Update).</summary>
        void ApplyUserVolumes()
        {
            try
            {
                OzAudio.Set("Music", PlayerPrefs.GetFloat("oz_vol_music", 1f));
                OzAudio.Set("SFX", PlayerPrefs.GetFloat("oz_vol_effects", 1f));
                OzAudio.Set("Announcer", PlayerPrefs.GetFloat("oz_vol_voice", 1f));
                OzAudio.Set("Robots", PlayerPrefs.GetFloat("oz_vol_robots", 1f));                      // ... and ROBOT VOICES (defaults 100 % = the mix as before)
            }
            catch (Exception ex) { Debug.LogWarning("[AudioDirector] user volumes: " + ex.Message); }
        }

        // ============================================================================================================================
        // Public API (hooks)
        // ============================================================================================================================
        /// <summary>World one-shot (3D) at a position. emitter = object id for the per-emitter cooldown. CombatAudio-owned ids are forwarded.</summary>
        public static void Fire(string id, Vector3 pos, int emitter = -1, float volMul = 1f, float pitchMul = 1f) { if (I) I.Play(id, pos, emitter, volMul, pitchMul); }
        /// <summary>2D UI / interface sound (OzSfx.Ui, the main menu, the HUD click via CombatAudio.Play2DRoute).</summary>
        public static void UI(string id, float volMul = 1f, float pitchMul = 1f)
        {
            if (!I && !EnsureLive()) return;                                                             // audio_v2: boot one instead of silence
            id = Canon(id);
            float now = Time.unscaledTime;
            if (I.lastUi.TryGetValue(id, out var t) && now - t < 0.05f) return;                       // one click reported by two hooks
            // HUD click of the same frame, whichever arrives first (HudButton plays its click right after OnClick).
            int f = Time.frameCount;
            if (id == "ui_click" && I.specificFrame == f) return;
            if (id == "ui_minimap_ping") { I.pendingPing = f; return; }                                   // decided after the sim events (LateUpdate)
            I.lastUi[id] = now;
            if (!GenericUi.Contains(id) && I.Def(id)?.bus == "UI")
            {
                I.specificFrame = f;
                if (I.clickFrame == f && I.clickVoice != null && I.clickVoice.busy) { I.clickVoice.src.Stop(); I.clickVoice.busy = false; }
            }
            if (id == "ui_order_invalid") I.invalidFrame = f;                                            // PollInput: no move / attack cue for it
            I.lastStarted = null;
            bool played = I.Play(id, I.ListenerPos, -1, volMul, pitchMul);
            if (id == "ui_click") { I.clickFrame = f; I.clickVoice = I.lastStarted; }
            if (played && Persist.Contains(id)) I.HandOver(I.lastStarted);
        }
        readonly Dictionary<string, float> lastUi = new Dictionary<string, float>();
        int specificFrame = -1, clickFrame = -1, invalidFrame = -1, pendingPing = -1; Voice clickVoice, lastStarted; bool forceAll;
        /// <summary></summary>
        static readonly HashSet<string> GenericUi = new HashSet<string>
        { "ui_click", "ui_hover", "ui_alert", "ui_minimap_ping", "ui_transmission", "ui_toast", "ui_count_tick", "ui_slider_tick", "ter_zone_gained", "ter_zone_lost", "ter_game_start_sfx" };
        /// <summary>
        /// DontDestroyOnLoad source instead of being cut by the scene change.</summary>
        static readonly HashSet<string> Persist = new HashSet<string> { "ui_confirm", "ui_cancel" };
        static AudioSource uiTail;
        void HandOver(Voice v)
        {
            if (v == null || !v.src || !v.src.clip) return;
            if (!uiTail)
            {
                var go = new GameObject("AudioDirector_UiTail"); DontDestroyOnLoad(go);
                uiTail = go.AddComponent<AudioSource>(); uiTail.playOnAwake = false; uiTail.spatialBlend = 0f; uiTail.ignoreListenerPause = true; uiTail.priority = 16;
            }
            uiTail.clip = v.src.clip; uiTail.volume = v.src.volume; uiTail.pitch = v.src.pitch; uiTail.Play();
            v.src.Stop(); v.busy = false;
        }

        /// <summary>
        /// </summary>
        public static bool Has(string id)
        {
            if (!I || !I.Loaded || id == null) return false;
            id = Canon(id); var d = I.Def(id);
            return d != null && !Silent.Contains(id) && (d.loaded.Count > 0 || d.routes.Length > 0 || Combat.ContainsKey(id));
        }

        /// <summary>
        /// press) and the view travels more than 18 m; minimap drags, short hops and scripted flights (nuke framing) stay quiet.</summary>
        public static void CameraJump(Vector3 from, Vector3 to)
        {
            if (!I) return;
            var d = to - from; d.y = 0f;
            if (d.sqrMagnitude < 18f * 18f) return;
            var m = Mouse.current; var kb = Keyboard.current;
            bool asked = (m != null && (m.leftButton.wasPressedThisFrame || m.leftButton.wasReleasedThisFrame || m.rightButton.wasReleasedThisFrame))
                         || (kb != null && kb.anyKey.wasPressedThisFrame);
            if (asked) UI("ui_camera_jump");
        }
        /// <summary>Alan line (player team only): routed to the main / alert / end channel. lineFilter picks lines containing it.</summary>
        public static void Say(string id, string lineFilter = null) { if (I) I.SayImpl(ElectroVoices.Route(id), lineFilter); }   // electrobot_v1: ElectroVoices.Route
        /// <summary>Rachel line: goes through the announcer queue (rank, cooldown, duplicate drop).</summary>
        public static void Announce(string id) { if (I) I.AnnounceImpl(id); }

        /// <summary>
        /// Announcer setting Full / Less / Minimal: Less keeps only important lines with longer cooldowns, Minimal only critical ones
        /// (fort under attack, victory, defeat, key captures). Saved between sessions (PlayerPrefs oz_announcer 0 / 1 / 2).</summary>
        public static class Chatter
        {
            public static readonly string[] Names = { "FULL", "LESS", "MINIMAL" };
            public static int Level { get => Mathf.Clamp(PlayerPrefs.GetInt("oz_announcer", 0), 0, 2); set { PlayerPrefs.SetInt("oz_announcer", Mathf.Clamp(value, 0, 2)); PlayerPrefs.Save(); } }
            static readonly HashSet<string> Critical = new HashSet<string> { "ann_fort_under_attack", "ann_fort_lost", "ann_enemy_fort_destroyed", "ann_victory", "ann_defeat",
                "ann_mission_completed", "ann_nuke_incoming", "ann_nuke_detected", "ann_nuke_launched", "ann_nuke_explain", "ann_territory_captured", "ann_territory_lost", "ann_base_under_attack" };
            static readonly HashSet<string> Routine = new HashSet<string> { "ann_starting_manufacture", "ann_manufacturing_cancelled", "ann_production_paused",
                "ann_production_resumed", "ann_production_queued", "ann_grenades_collected", "ann_starting_repair", "ann_unit_limit", "ann_youre_losing", "ann_bridge_repaired", "ann_l17_tip" };
            public static bool Allows(string id)
            {
                int l = Level;
                if (l == 0 || id == null) return true;
                if (l == 2) return Critical.Contains(id);
                return !Routine.Contains(id);
            }
        }
        /// <summary>
        /// Robot chatter setting Full / Less / Minimal: Less raises the cooldowns and drops idle / repeat-click lines, Minimal keeps only
        /// order acknowledgements and key events (PlayerPrefs oz_robot_chatter 0 / 1 / 2, default Full = exactly as before). Alan's alan_main / alan_alert lines only:
        /// death cries, yelps and the ending lines always play.</summary>
        public static class RobotChatter
        {
            public static readonly string[] Names = { "FULL", "LESS", "MINIMAL" };
            public static int Level { get => Mathf.Clamp(PlayerPrefs.GetInt("oz_robot_chatter", 0), 0, 2); set { PlayerPrefs.SetInt("oz_robot_chatter", Mathf.Clamp(value, 0, 2)); PlayerPrefs.Save(); Debug.Log($"[AudioDirector] robot chatter: {Names[Mathf.Clamp(value, 0, 2)]} (oz_robot_chatter, saved)"); } }
            /// <summary>Less: every robot line's cooldown x this (selection lines at least LessSelectGap s).</summary>
            public const float LessCooldownMul = 2.5f, LessSelectGap = 1.5f;
            /// <summary>Idle chatter and banter Less drops (with the vo_idle_x_* repeat-click jokes).</summary>
            static readonly HashSet<string> Idle = new HashSet<string> { "vo_calm_chatter", "vo_taunt", "vo_kill_quip", "vo_boom", "vo_boom_brad", "vo_push", "vo_loud_gun", "vo_drive_scare",
                "vo_vehicle_passenger", "vo_target_destroyed", "vo_hurt", "vo_building_events", "vo_electro_zap", "vo_electro_kill" };
            /// <summary>Key events Minimal keeps (besides every order acknowledgement vo_order_*).</summary>
            static readonly HashSet<string> Key = new HashSet<string> { "vo_under_attack", "vo_promoted", "vo_nuke_reaction", "vo_nuke_delivery", "vo_game_start",
                "vo_territory_taken", "vo_vehicle_captured", "vo_gun_captured", "vo_low_health", "vo_snipe_success", "vo_sniper_warning", "vo_grenade_warning", "vo_electro_spawn" };
            public static int Skipped;
            public static bool Allows(string id)
            {
                int l = Level;
                if (l == 0 || id == null) return true;
                if (l == 2) return id.StartsWith("vo_order", System.StringComparison.Ordinal) || Key.Contains(id);
                return !id.StartsWith("vo_idle_x_", System.StringComparison.Ordinal) && !Idle.Contains(id);
            }
            public static float Cooldown(string id, float cd)
            {
                if (Level != 1) return cd;
                float c = cd * LessCooldownMul;
                return id != null && id.StartsWith("vo_select", System.StringComparison.Ordinal) ? Mathf.Max(c, LessSelectGap) : c;
            }
        }
        /// <summary>endings_v1: Rachel line whose text contains lineFilter (LineMatch rules), e.g. ann_defeat "Mission failed".</summary>
        public static void Announce(string id, string lineFilter) { if (I) I.AnnounceImpl(id, lineFilter); }
        /// <summary>Physics impact (debris, ragdolls): volume from the relative speed (bible: clamp01(0.3 + v * 0.06)).</summary>
        public static void Impact(string id, Vector3 pos, float relativeSpeed, int emitter) { if (I && id != null && relativeSpeed > 2f) I.Play(id, pos, emitter, Mathf.Clamp01(0.3f + relativeSpeed * 0.06f), 1f); }
        /// <summary>Adds impact relays to every Rigidbody under root (ragdolls, chunks). id null = splash only; splashId plays when a body hits water.</summary>
        public static void AttachImpacts(GameObject root, string id, string splashId = null)
        {
            if (!root) return;
            foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
                if (!rb.GetComponent<AudioImpactRelay>()) { var r = rb.gameObject.AddComponent<AudioImpactRelay>(); r.eventId = id; r.splashId = splashId; }
        }

        public int StartLoop(string key, string id, Vector3 pos, Transform follow = null, float volMul = 1f, float pitchMul = 1f)
        {
            var d = Def(id); Count(Triggers, id);
            if (d == null || d.loaded.Count == 0 || Silent.Contains(id)) return 0;
            if (d.bus == "Music" && !MusicOn && !forceAll) return 0;
            if (loops.TryGetValue(key, out var v) && v.busy && v.def == d) { v.fadeTarget = 1f; SetLoop(key, volMul, pitchMul); return 1; }
            v = v ?? NewVoice("Loop_" + key);
            int idx = Pick(d, null);
            v.def = d; v.key = key; v.loop = true; v.busy = true; v.follow = follow; v.followed = follow != null; v.emitter = -1; v.start = Time.time; v.offLin = 1f;
            ConfigureSource(v, d, d.loaded[idx]);
            v.src.transform.position = follow ? follow.position : pos;
            v.baseLin = Db(d.volDb + d.gains[idx] + OzMixTrim.AdDb(d.id)) * volMul;
            v.src.pitch = d.pitchMul * pitchMul;
            v.src.loop = true; v.fade = 0f; v.fadeTarget = 1f; v.fadeSpeed = 1f / 0.15f;
            v.src.volume = 0f;                                                                            // starts silent and fades in: a new source is at volume 1 until this frame's mix pass, and a loop started after that pass (by another component) played its first frame at full volume
            v.src.Play();
            v.src.time = UnityEngine.Random.Range(0f, Mathf.Max(0f, d.loaded[idx].length - 0.05f));
            loops[key] = v;
            Count(Plays, id); Played++; if (sweeping) Count(SweepPlays, id);
            OzSoundLog.Add("AD", id, d.loaded[idx].name, "LOOP " + d.bus, v.baseLin, follow ? follow.position : pos);
            return 1;
        }
        public void SetLoop(string key, float volMul, float pitchMul, Vector3? pos = null)
        {
            if (!loops.TryGetValue(key, out var v) || !v.busy) return;
            int idx = Mathf.Max(0, v.def.loaded.IndexOf(v.src.clip));
            v.baseLin = Db(v.def.volDb + v.def.gains[idx] + OzMixTrim.AdDb(v.def.id)) * volMul;
            v.src.pitch = v.def.pitchMul * pitchMul;
            if (pos.HasValue && !v.follow) v.src.transform.position = pos.Value;
        }
        public void StopLoop(string key, float fadeOut = 0.25f)
        {
            if (!loops.TryGetValue(key, out var v) || !v.busy) return;
            v.fadeTarget = 0f; v.fadeSpeed = 1f / Mathf.Max(0.02f, fadeOut);
        }
        public bool LoopActive(string key) => loops.TryGetValue(key, out var v) && v.busy && v.fadeTarget > 0f;

        // ============================================================================================================================
        // Core one-shot playback
        // ============================================================================================================================
        Vector3 ListenerPos
        {
            get
            {
                var ca = CA;
                if (ca && ca.ListenerRig) return ca.ListenerRig.position;
                return cam ? cam.transform.position : Vector3.zero;
            }
        }

        /// <summary>
        /// by id); shooter-less layers (breech, whistle, tracks ...) stay silent.</summary>
        public bool Play(string id, Vector3 pos, int emitter = -1, float volMul = 1f, float pitchMul = 1f, string lineFilter = null)
        {
            id = Canon(id);
            if (id != null && Combat.TryGetValue(id, out var cr)) return Forward(id, cr, pos, emitter, volMul, pitchMul);
            var d = Def(id);
            Count(Triggers, id);
            if (d == null || Silent.Contains(id)) { OzSoundLog.Add("AD", id, null, d == null ? "DROP no event" : "DROP silent by design", 0f, pos); return false; }
            if (d.bus == "Music" && !MusicOn && !forceAll) return false;
            if (d.loaded.Count == 0)
            {
                if (d.routes.Length > 0) { bool any = false; foreach (var r in d.routes) any |= Play(r, pos, emitter, volMul, pitchMul); if (any) { Count(Plays, id); if (sweeping) Count(SweepPlays, id); } return any; }
                OzSoundLog.Add("AD", id, null, "DROP no clips loaded", 0f, pos); return false;
            }
            if (d.maxCamHeight > 0 && rts && rts.height > d.maxCamHeight && !sweeping) { OzSoundLog.Add("AD", id, null, "DROP camera too high", 0f, pos); return false; }
            float now = Time.unscaledTime;
            if (!sweeping && d.cooldown > 0 && lastEmit.TryGetValue((id, emitter), out var t) && now - t < d.cooldown) { OzSoundLog.Add("AD", id, null, "DROP cooldown", 0f, pos); return false; }   // emitter -1 = one shared slot (UI, ambience)
            if (!sweeping && d.gap > 0 && now - d.lastPlay < d.gap) { OzSoundLog.Add("AD", id, null, "DROP gap", 0f, pos); return false; }
            float offLin = 1f;
            if (d.is3D && d.inViewOnly && !sweeping && cam)
            {
                var vp = cam.WorldToViewportPoint(pos);
                bool inView = vp.z > 0 && vp.x > -0.35f && vp.x < 1.35f && vp.y > -0.35f && vp.y < 1.35f;       // Zod PlayWavRestricted, as CombatAudio
                if (!inView)
                {
                    bool near = vp.z > 0 && vp.x > -1.5f && vp.x < 2.5f && vp.y > -1.5f && vp.y < 2.5f;
                    if (!(d.offscreen && near)) { OzSoundLog.Add("AD", id, null, "DROP off screen", 0f, pos); return false; }
                    offLin = Db(-9f);                                                                        // distant battle, turn to it
                }
            }
            int live = 0; Voice oldest = null;                                                               // per-event voice limit: steal the oldest
            foreach (var pv in pool) if (pv.busy && pv.def == d && pv.src.isPlaying) { live++; if (oldest == null || pv.start < oldest.start) oldest = pv; }
            if (live >= d.maxVoices && oldest != null) oldest.src.Stop();
            var v = Grab(d);
            if (v == null) { OzSoundLog.Add("AD", id, null, "DROP no free voice", 0f, pos); return false; }
            int idx = Pick(d, lineFilter);
            lastEmit[(id, emitter)] = now;
            d.lastPlay = now;
            v.def = d; v.busy = true; v.loop = false; v.follow = null; v.emitter = emitter; v.start = Time.unscaledTime; v.key = null;
            ConfigureSource(v, d, d.loaded[idx]);
            v.src.transform.position = d.is3D ? pos : ListenerPos;
            float db = d.volDb + d.gains[idx] + OzMixTrim.AdDb(d.id) + (d.volVarDb > 0 ? UnityEngine.Random.Range(-d.volVarDb, d.volVarDb) : 0f);
            v.baseLin = Db(db) * volMul; v.offLin = offLin; v.fade = v.fadeTarget = 1f;
            v.src.pitch = d.pitchMul * pitchMul * (1f + (d.pitchVar > 0 ? UnityEngine.Random.Range(-d.pitchVar, d.pitchVar) : 0f));
            v.src.loop = false;
            Apply(v);
            v.src.Play();
            OzSoundLog.Add("AD", id, v.src.clip ? v.src.clip.name : null, "PLAY " + d.bus, v.src.volume, v.src.transform.position);
            lastStarted = v;
            Count(Plays, id); Played++;
            if (sweeping) Count(SweepPlays, id);
            return true;
        }

        bool Forward(string id, CaRoute cr, Vector3 pos, int emitter, float volMul, float pitchMul)
        {
            Count(Triggers, id);
            var ca = CA; if (!ca) return false;
            float pitch = cr.pitch > 0f ? cr.pitch * pitchMul : 0f;
            if (cr.wide || sweeping) ca.PlayWide(cr.key, pos, cr.gain * volMul, cr.bank);
            else ca.PlayPitched(cr.key, pos, cr.gain * volMul, 0f, emitter, pitch, cr.bank);
            if (sweeping) Count(SweepPlays, id);
            return true;
        }

        /// <summary>A CombatAudio-owned event seen in the sim: counted for the coverage report, never played here.</summary>
        void Owned(string id) { if (id != null) Count(Triggers, id); }

        static CombatAudio.Bus MapBus(string bus)
        {
            switch (bus)
            {
                case "SFX/Weapons": return CombatAudio.Bus.Weapons;
                case "SFX/Explosions": return CombatAudio.Bus.Explosions;
                case "SFX/Impacts": return CombatAudio.Bus.Impacts;
                case "SFX/Destruction": return CombatAudio.Bus.Destruction;
                case "SFX/Vehicles": return CombatAudio.Bus.Vehicles;
                default: return CombatAudio.Bus.World;                                                      // SFX/Foley, SFX/World
            }
        }

        /// <summary>
        /// (collapse / plunge broad 150), impacts field 110, vehicles field 85, weapons local 100; foley / world local with the
        /// designed range.</summary>
        static void Profile(SoundEventDef d, out CombatAudio.Profile prof, out float range)
        {
            switch (d.bus)
            {
                case "SFX/Explosions": prof = CombatAudio.Profile.Field; range = 115f; break;
                case "SFX/Destruction":
                    bool big = d.id.Contains("crash_large") || d.id.Contains("plunge") || d.id.Contains("bridge");
                    prof = big ? CombatAudio.Profile.Broad : CombatAudio.Profile.Field; range = big ? 150f : 110f; break;
                case "SFX/Impacts": prof = CombatAudio.Profile.Field; range = 110f; break;
                case "SFX/Vehicles": prof = CombatAudio.Profile.Field; range = 85f; K436FlipProfile(d, ref prof, ref range); break;   // K436: the flip's rows on the broad curve (AudioDirector.K436.cs)
                case "SFX/Weapons": prof = CombatAudio.Profile.Local; range = 100f; break;
                default: prof = PresenceOf(d); range = PresenceRange; break;
            }
        }
        /// <summary>audio_v2: world / foley / ambience spots on CombatAudio's presence curves (150 m x zoom): small emitters (designed
        /// maxDist under 40 m: steps, lamps, creaks) fall off faster, large ones (120 m and more: gates, alarms, bridges) slower.</summary>
        const float PresenceRange = 150f;
        static CombatAudio.Profile PresenceOf(SoundEventDef d) =>
            d.maxDist > 0f && d.maxDist < 40f ? CombatAudio.Profile.PresenceS : d.maxDist >= 120f ? CombatAudio.Profile.PresenceL : CombatAudio.Profile.Presence;

        void ConfigureSource(Voice v, SoundEventDef d, AudioClip clip)
        {
            var s = v.src;
            s.clip = clip;
            s.priority = Mathf.Clamp(d.priority, 0, 256);
            s.dopplerLevel = d.doppler;
            s.spread = d.spread;
            var ca = CA;
            v.combatMix = ca && d.bus.StartsWith("SFX");
            v.level = v.combatMix ? LevelTrim(clip) : 1f;
            if (v.combatMix && d.is3D)
            {
                Profile(d, out var prof, out var range);
                v.lp.enabled = true;
                ca.Configure(s, v.lp, prof, range * ca.RangeScale);
                s.spatialBlend = 1f; v.range = range;
            }
            else if (d.is3D && ca)
            {   // audio_v2: ambience spots (river, critters, lamps, searchlights) on the presence curve too (their own curve reached 0 at
                // 14-45 m, under the 14 m listener: silent)
                v.lp.enabled = true;
                ca.Configure(s, v.lp, PresenceOf(d), PresenceRange * ca.RangeScale);
                s.spatialBlend = 1f; v.range = PresenceRange;
            }
            else
            {
                v.lp.enabled = false; v.range = 0f;
                s.spatialBlend = d.is3D ? 1f : 0f;
                s.minDistance = Mathf.Max(0.5f, d.minDist);
                s.maxDistance = Mathf.Max(s.minDistance + 1f, d.maxDist);
                if (d.is3D) { s.rolloffMode = AudioRolloffMode.Custom; s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, rolloff); }
            }
            v.ignorePause = d.bus == "UI" || d.bus == "Music" || d.bus.StartsWith("Voice");
            s.ignoreListenerPause = v.ignorePause;
        }

        /// <summary>CombatAudio's loudness leveller on AudioDirector clips (measured integrated LUFS, openz_clip_levels.json).</summary>
        float LevelTrim(AudioClip c)
        {
            var ca = CA;
            if (!ca || !c || !clipLufs.TryGetValue(c.name, out var lufs) || lufs <= ca.levelRefLufs) return 1f;
            return Mathf.Pow(10f, -(lufs - ca.levelRefLufs) * (1f - 1f / Mathf.Max(1f, ca.levelRatio)) / 20f);
        }

        /// <summary>
        /// "=line" = exactly that line (help_cries: the radio's line bag picks a whole line, "!" and all).</summary>
        static bool LineMatch(string line, string filter)
        {
            if (line == null) return false;
            if (filter.Length > 1 && filter[0] == '=') return string.Equals(line, filter.Substring(1), StringComparison.OrdinalIgnoreCase);
            int x = filter.IndexOf('!');
            string inc = x >= 0 ? filter.Substring(0, x) : filter, exc = x >= 0 ? filter.Substring(x + 1) : null;
            return line.IndexOf(inc, StringComparison.OrdinalIgnoreCase) >= 0
                && (string.IsNullOrEmpty(exc) || line.IndexOf(exc, StringComparison.OrdinalIgnoreCase) < 0);
        }

        static int NamesColourCount(SoundEventDef d) { int c = 0; for (int i = 0; i < d.loaded.Count && i < d.lines.Count; i++) if (TeamColors.NamesColour(d.lines[i])) c++; return c; }
        int forcePick = -1;                                                                              // death_voices_v1: DeathVoices chose the clip
        int Pick(SoundEventDef d, string lineFilter)
        {
            int n = d.loaded.Count;
            if (forcePick >= 0) { int f = forcePick; forcePick = -1; if (f < n) return d.lastIdx = f; }
            if (n == 1) return 0;
            if (!string.IsNullOrEmpty(lineFilter))
            {
                var m = new List<int>();
                for (int i = 0; i < n; i++) if (LineMatch(d.lines[i], lineFilter) && i != d.lastIdx) m.Add(i);
                if (m.Count == 0) for (int i = 0; i < n; i++) if (LineMatch(d.lines[i], lineFilter)) m.Add(i);
                if (TeamColors.Swap && m.Exists(i => !TeamColors.NamesColour(d.lines[i]))) m.RemoveAll(i => TeamColors.NamesColour(d.lines[i]));   // colour lines out while swapped
                if (m.Count > 0) return d.lastIdx = m[UnityEngine.Random.Range(0, m.Count)];
            }
            if (d.bag == null || d.bag.Count == 0)
            {   // shuffled bag = round robin without the same clip twice in a row
                d.bag = Enumerable.Range(0, n).OrderBy(_ => UnityEngine.Random.value).ToList();
                if (d.noRepeat && d.bag[0] == d.lastIdx && n > 1) { d.bag.RemoveAt(0); d.bag.Add(d.lastIdx); }
            }
            int k = d.bag[0]; d.bag.RemoveAt(0);
            // While the team colours are swapped, a take that names a colour ("GG, blue!", "Blue's not your colour!") is skipped for
            // the next one of the bag (the other lines stay neutral); only if every take names one it plays.
            if (TeamColors.Swap && d.lines != null && d.lines.Count >= n && NamesColourCount(d) < n)
                for (int guard = 0; guard < 2 * n && TeamColors.NamesColour(d.lines[k]); guard++)
                {
                    if (d.bag.Count == 0) d.bag = Enumerable.Range(0, n).OrderBy(_ => UnityEngine.Random.value).ToList();
                    k = d.bag[0]; d.bag.RemoveAt(0);
                }
            return d.lastIdx = k;
        }

        Voice Grab(SoundEventDef d)
        {
            foreach (var v in pool) if (!v.busy || !v.src.isPlaying) { v.busy = false; return v; }
            Voice best = null;                                           // steal: lowest priority first (highest number), then quietest
            foreach (var v in pool)
            {
                if (v.def == null || v.def.priority < d.priority) continue;
                if (best == null || v.def.priority > best.def.priority || (v.def.priority == best.def.priority && v.src.volume < best.src.volume)) best = v;
            }
            if (best != null) best.src.Stop();
            return best;
        }

        // ============================================================================================================================
        // Mix: SFX through CombatAudio's buses / zoom / duck; music, ambience, UI and voices on this director's bus table
        // ============================================================================================================================
        struct Duck { public float music, amb, robots, until, attack, release; }
        readonly List<Duck> ducks = new List<Duck>();
        float duckMusic, duckAmb, duckRobots, combatAmbDuck;

        /// <summary>Music / ambience / robot-voice ducks (sfx is CombatAudio's voice duck; the parameter is kept for callers).</summary>
        public void AddDuck(float music, float sfx, float amb, float robots, float seconds, float attack = 0.15f, float release = 0.8f, bool exemptNuke = false)
            => ducks.Add(new Duck { music = music, amb = amb, robots = robots, until = Time.unscaledTime + seconds, attack = attack, release = release });

        void StepDucks(float dt)
        {
            float m = 0, a = 0, r = 0, att = 0.15f, rel = 0.8f;
            float now = Time.unscaledTime;
            ducks.RemoveAll(x => x.until < now);
            foreach (var x in ducks) { m = Mathf.Min(m, x.music); a = Mathf.Min(a, x.amb); r = Mathf.Min(r, x.robots); att = Mathf.Min(att, x.attack); rel = Mathf.Max(rel, x.release); }
            a = Mathf.Min(a, combatAmbDuck);
            duckMusic = Smooth(duckMusic, m, att, rel, dt); duckAmb = Smooth(duckAmb, a, att, rel, dt); duckRobots = Smooth(duckRobots, r, att, rel, dt);
        }
        static float Smooth(float cur, float target, float attack, float release, float dt)
        {
            float tc = target < cur ? attack : release;
            return Mathf.Lerp(cur, target, 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, tc)));
        }

        float BusGainDb(string bus)
        {
            float db = busDb.TryGetValue("Master", out var mdb) ? mdb : 0f;
            if (bus == null) return db;
            int slash = bus.IndexOf('/');
            string top = slash > 0 ? bus.Substring(0, slash) : bus;
            if (busDb.TryGetValue(top, out var tdb)) db += tdb;
            if (slash > 0 && busDb.TryGetValue(bus, out var sdb)) db += sdb;
            switch (top)
            {
                case "Music": db += MusicDuckDb(); break;   /* music_v3: the deepest of the shared duck and Zod's subtle eased duck (menu A / B, stings) */
                case "Ambience": db += duckAmb; break;
                case "Voice": if (bus == "Voice/Robots") db += duckRobots; break;
            }
            return db;
        }

        void Apply(Voice v)
        {
            var d = v.def; if (d == null) return;
            var ca = CA;
            float g;
            if (v.combatMix && ca)
                g = v.baseLin * v.level * v.offLin * v.fade * masterVolume * ca.volume * CombatAudio.BusGain(MapBus(d.bus)) * ca.ZoomGain * ca.DuckOf(MapBus(d.bus));   // audio_v2: per-bus duck
            else
                g = v.baseLin * v.offLin * v.fade * masterVolume * Db(BusGainDb(d.bus)) * ShuttleLineUnduck(v);   // K256e: a shuttle line is never ducked
            if (v.loop && v.range > 0f && ca) v.src.maxDistance = Mathf.Max(8f, v.range * ca.RangeScale);   // audio_v2: loops follow the zoom
            v.src.volume = Mathf.Clamp01(g);
        }

        // ============================================================================================================================
        // Frame
        // ============================================================================================================================
        void Update()
        {
            if (!Loaded) return;
            float dt = Time.unscaledDeltaTime;
            TickPendingAlan();
            ShuttleHeldTick();                                                                             // DV118: the robot lines held behind the shuttle landing line
            ShuttleRound3Tick();                                                                           // K256e: the shuttle line held behind Rachel (View/AudioDirector.ShuttleRound3.cs)
            if (!game && ZGame.I) { game = ZGame.I; BuildAmbienceEmitters(); }
            if (!cam) cam = Camera.main;
            if (!rts) rts = FindFirstObjectByType<RtsCamera>();
            SyncCombatVolume();
            StepDucks(dt);
            RunSchedule();
            if (startLinesHeld && (HoldStartLines == null || !HoldStartLines())) PlayStartLines();   // zod_cameo: the briefing handed over
            if (game != null && game.World != null)
            {
                if (vehicleFoley) { UpdateVehicles(); UpdateVehicleSurface(); }                            // audio_v2: + tracks / tyres
                if (footsteps) UpdateRobots(Time.deltaTime);
                UpdateWrecks();
                UpdateGatlings();
                UpdateBuildingLoops();
                UpdateNightLights();
                PollCombatLoops();
                V3Tick();                                                                              // sound_v3
                IconicTick();                                                                          // robot_iconic_lines_v1: passenger / reckless driving / loud gun / calm radio
                if (uiPolling) PollInput();
                if (voices) UpdateVoiceRules();
                UpdateMusic();                                                   // danger state + fort alarm always; the music itself only with oz_music on
                if (physicsImpacts) ScanPhysics();
            }
            if (ambience) UpdateAmbience(dt);
            StepMusicVoices(dt);
            MusicPlaylistTick();                                                   // music_v2: next track at the tail
            MusicV3Tick(dt);                                                       // music_v3: MusicCore (fort-attack drama, resume, end music, Zod duck)
            BoomTick();
            UpdateChannels();
            if (uiPolling) PollHud();
            foreach (var v in pool) { if (v.busy && !v.src.isPlaying && !AudioListener.pause) v.busy = false; if (v.busy) Apply(v); }
            List<string> dead = null;
            foreach (var kv in loops)
            {
                var v = kv.Value; if (!v.busy) continue;
                v.fade = Mathf.MoveTowards(v.fade, v.fadeTarget, v.fadeSpeed * dt);
                if (v.follow) v.src.transform.position = v.follow.position;
                else if (v.followed && v.fadeTarget > 0f) { v.fadeTarget = 0f; v.fadeSpeed = 1f / 0.3f; LoopOrphansStopped++; }
                if (v.fadeTarget <= 0f && v.fade <= 0.001f) { v.src.Stop(); v.busy = false; (dead ??= new List<string>()).Add(kv.Key); continue; }
                Apply(v);
            }
            if (dead != null) foreach (var k in dead) { var v = loops[k]; loops.Remove(k); Destroy(v.src.gameObject); }
            LoopAuditTick();
        }

        /// <summary>The EFFECTS slider (OzAudio -> busDb["SFX"]) also scales the live CombatAudio master (0.8 at 50 %, 1.6 at 100 %).</summary>
        void SyncCombatVolume()
        {
            var ca = CA; if (!ca) return;
            if (ca != caSeen) { caSeen = ca; caBaseVolume = ca.volume > 0f ? ca.volume : 0.8f; Hook(); }
            float user = (busDb.TryGetValue("SFX", out var s) ? s : tableSfxDb) - tableSfxDb;
            ca.volume = caBaseVolume * Db(user);
        }

        void LateUpdate()
        {
            if (Loaded && simEvents && game != null && game.World != null)
            {
                var ev = game.World.Events;                       // read only: CombatView (order 50) clears the list after us
                for (int i = 0; i < ev.Count; i++) { try { OnSimEvent(ev[i]); } catch (Exception ex) { Debug.LogException(ex); } }
                frameBuildings.Clear();
                SnapshotOwners();
            }
            if (pendingPing >= 0)
            {
                //   and at most every 8 s (OpenZHud re-pings every 2 s while a fight lasts: the sound marks a new attack, like Alan's 8 s rule)
                pendingPing = -1; float nowU = Time.unscaledTime;
                bool alert = lastUi.TryGetValue("ui_alert", out var ta) && nowU - ta < 1.5f;
                bool recent = lastUi.TryGetValue("ui_minimap_ping", out var tp) && nowU - tp < 8f;
                if (!alert && !recent) { lastUi["ui_minimap_ping"] = nowU; Play("ui_minimap_ping", ListenerPos, -1); }
            }
        }

        // ------------------------------------------------------------------ scheduler (scaled time: pauses with the game)
        readonly List<(float t, Action a)> sched = new List<(float, Action)>();
        public void Schedule(float delay, Action a) => sched.Add((Time.time + delay, a));
        void RunSchedule()
        {
            if (sched.Count == 0) return;
            float now = Time.time;
            for (int i = sched.Count - 1; i >= 0; i--)
                if (sched[i].t <= now) { var a = sched[i].a; sched.RemoveAt(i); try { a(); } catch (Exception ex) { Debug.LogException(ex); } }
        }

        // ============================================================================================================================
        // Sim events (voices, announcer and the non-shooter layers; the combat sounds are CombatView's)
        // ============================================================================================================================
        Team Player => game ? game.player : Team.Red;
        ZWorld W => game.World;
        Vector3 Px(int x, int y) { var p = ZGame.PxToWorld(x, y); p.y = game.GroundY(p.x, p.z) + 0.3f; return p; }
        Vector3 PosOf(ZObject o) { if (o == null) return ListenerPos; return game.Views.TryGetValue(o.Id, out var v) && v ? v.transform.position + Vector3.up * 0.8f : game.CenterWorld(o) + Vector3.up * 0.8f; }
        ZClass ClassAt(Vector3 p)
        {
            var map = W?.Map; if (map == null) return ZClass.Ground;
            var px = ZGame.WorldToPx(p); return map.ClassAt(px.x >> 4, px.y >> 4);
        }
        bool Wet(Vector3 p) { var c = ClassAt(p); return c == ZClass.Water || c == ZClass.DeepWater; }
        bool City => game && game.level == "L17";
        bool Jungle => game && game.level == "L16";

        /// <summary>Level 16, the ground under a point for steps and wheels: "road", "water", "mud" (a ground tile that touches a
        /// water tile: the rim of a swamp pool) or "grass". Any other level: null.</summary>
        public static string JungleGround(Vector3 p)
        {
            var g = ZGame.I; if (!g || g.level != "L16" || g.World == null || g.World.Map == null) return null;
            var map = g.World.Map; int tx = (int)(p.x * 0.5f), ty = (int)(-p.z * 0.5f);
            if (tx < 0 || ty < 0 || tx >= map.width || ty >= map.height) return "grass";
            var c = map.ClassAt(tx, ty);
            if (c == ZClass.Road) return "road";
            if (c == ZClass.Water || c == ZClass.DeepWater) return "water";
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = tx + dx, y = ty + dy; if ((dx == 0 && dy == 0) || x < 0 || y < 0 || x >= map.width || y >= map.height) continue;
                    var n = map.ClassAt(x, y); if (n == ZClass.Water || n == ZClass.DeepWater) return "mud";
                }
            return "grass";
        }
        static string TypeOf(ZObject a) => a == null ? "" : a.Kind == ZKind.Vehicle && a.Type == "apc" ? a.CrewWeaponType : a.Type;   // apc_v1 DV15
        bool OnScreen(Vector3 p, float margin = 0.1f)
        {
            if (!cam) return true;
            var vp = cam.WorldToViewportPoint(p);
            return vp.z > 0 && vp.x > -margin && vp.x < 1f + margin && vp.y > -margin && vp.y < 1f + margin;
        }

        // owners as of the previous frame (Entered / OwnerChanged need the owner before the flip)
        readonly Dictionary<int, Team> ownerSnap = new Dictionary<int, Team>(), lastSide = new Dictionary<int, Team>();
        float nextOwnerSnap;
        void SnapshotOwners()
        {
            foreach (var o in W.Objects)
            {
                if (o.Kind != ZKind.Vehicle && o.Kind != ZKind.Cannon && o.Kind != ZKind.Building) continue;
                ownerSnap[o.Id] = o.Owner;
                if (o.Owner != Team.Neutral) lastSide[o.Id] = o.Owner;
            }
        }
        Team Before(ZObject o) => o != null && ownerSnap.TryGetValue(o.Id, out var t) ? t : Team.Neutral;
        Team LastSide(ZObject o) => o != null && lastSide.TryGetValue(o.Id, out var t) ? t : Team.Neutral;

        readonly Dictionary<int, float> gatLast = new Dictionary<int, float>(), snipedAt = new Dictionary<int, float>();
        readonly Dictionary<int, Vector3> gatPos = new Dictionary<int, Vector3>();
        int burnSeq;
        void UpdateGatlings()
        {
            if (gatLast.Count == 0) return;
            foreach (var id in gatLast.Keys.ToList())
                if (Time.time - gatLast[id] > 1.2f) { Play("wpn_gatling_spindown", gatPos.TryGetValue(id, out var gp) ? gp : ListenerPos, id); gatLast.Remove(id); }
        }
        void BurnGround(Vector3 at)
        {   // imp_flame_ground: one crackle loop per burning patch (4 s), nearest by the event's voice limit
            var d = Def("imp_flame_ground"); if (d == null || d.loaded.Count == 0) return;
            if (loops.Keys.Count(k => k.StartsWith("burn:") && LoopActive(k)) >= d.maxVoices) return;
            string key = "burn:" + (++burnSeq);
            StartLoop(key, "imp_flame_ground", at);
            Schedule(4f, () => StopLoop(key, 0.8f));
        }
        readonly HashSet<int> frameBuildings = new HashSet<int>();
        float lastNuke = -100f;

        void OnSimEvent(ZEvent e)
        {
            var a = W.GetAny(e.objId); var b = W.GetAny(e.otherId);
            V3SimEvent(e, a, b);                                                                     // sound_v3
            IconicSimEvent(e, a, b);                                                                 // robot_iconic_lines_v1
            switch (e.type)
            {
                case ZEventType.Hitscan: OnHitscan(e, a, b); break;
                case ZEventType.MissileFired: OnMissileFired(e, a, b); break;
                case ZEventType.DriverShot: UnderAttack(a, b, e.flag); break;                             // DV87: shot at by a tank's hatch driver: the same alert rules as any shot (the shot's own sound: BoardingView.DriverFire.cs)
                case ZEventType.MissileExploded: OnExploded(e, a, b); break;
                case ZEventType.Death: OnDeath(e, a, b); break;
                case ZEventType.Sniped: OnSniped(e, a, b); break;
                case ZEventType.DriverHit: Owned("imp_driver_hit"); break;
                case ZEventType.Entered: OnEntered(a, b); break;
                case ZEventType.Ejected: OnEjected(a, b); break;
                case ZEventType.GrenadesPicked:
                    Owned("rob_grenade_pickup");
                    if (a != null && a.Owner == Player) { if (AlanAlertBusy) Announce("ann_grenades_collected"); else Say("vo_grenades_collected"); }
                    break;
                case ZEventType.OwnerChanged: OnOwnerChanged(a); break;
                case ZEventType.Revived: OnRevived(a); break;
                case ZEventType.FlagCaptured: OnFlag(e); break;
                case ZEventType.UnitBuilt:
                    if (a != null && b != null)
                    {
                        var p = game.CenterWorld(b);
                        if (!FactoryLive) { Play(DoorEvent(b, true), p, b.Id); Schedule(3f, () => Play(DoorEvent(b, false), p, b.Id)); }
                        Owned(a.Kind == ZKind.Vehicle ? "bld_rollout_vehicle" : "bld_rollout_robot");         // RulesView plays "built"
                        if (b.Owner == Player) Announce(a.Kind == ZKind.Vehicle ? "ann_vehicle_manufactured" : "ann_robot_manufactured");
                        frameBuildings.Add(-b.Id);
                    }
                    break;
                case ZEventType.CannonStored:
                    if (a != null) { Play("bld_gun_stored", game.CenterWorld(a), a.Id); if (a.Owner == Player) Announce("ann_gun_manufactured"); frameBuildings.Add(-a.Id); }
                    break;
                case ZEventType.CannonPlaced: Owned("bld_cannon_placed"); break;                               // RulesView plays "placed"
                case ZEventType.RepairIn:
                    if (b != null)
                    {
                        var p = game.CenterWorld(b);
                        Owned("bld_repair_in");                                                                   // RulesView: the wrench
                        for (float t = 1.6f; t < 5f; t += UnityEngine.Random.Range(1.6f, 2.4f)) Schedule(t, () => { if (b.RepairingUnit) Play("bld_repair_work", p, b.Id); });   // DV62: not after a call-off
                        if (a != null && a.Owner == Player && !ShuttleRepairLine(a, "ann_starting_repair")) Announce("ann_starting_repair");   // K256e: never for the shuttle
                    }
                    break;
                case ZEventType.RepairOut:
                    Owned("bld_repair_out");
                    if (a != null && a.Owner == Player && !ShuttleRepairLine(a, "ann_vehicle_repaired")) Announce("ann_vehicle_repaired");   // K256e: never for the shuttle
                    OpenZVoiceV3.RepairedUnit(a);                                                                 // voice_lines_v3: its fire is out
                    break;
                case ZEventType.CraneAnim:
                    if (b != null)
                    {
                        Owned("veh_crane_arm");                                                                   // RulesView: the wrench
                        if (e.flag) craneWork[b.Id] = (game.CenterWorld(b), Time.time + 1.8f); else craneWork.Remove(b.Id);
                    }
                    break;
                case ZEventType.FortEntered: Play("brd_fort_breach", b != null ? game.CenterWorld(b) : PosOf(a), e.otherId); break;
                case ZEventType.TeamEliminated:
                    // fx_v2 CHECK: with the strike the fort still stands at +1.5 s (the bomb lands at +3.4 s); OpenZVictory says "Enemy eliminated." after the blast
                    if ((Team)(int)e.value != Player && !OpenZVictory.OwnsEndSequence) Schedule(1.5f, () => Announce("ann_enemy_fort_destroyed"));
                    break;
                case ZEventType.GameOver: OnGameOver(e.value < 0 ? Team.Neutral : (Team)(int)e.value); break;
                case ZEventType.ProductionChanged:
                    if (a != null) OnProduction(a, a.Owner == Player && !frameBuildings.Contains(-a.Id) && !frameBuildings.Contains(a.Id));
                    break;
            }
        }

        // ---- weapons: the shots are CombatView's (LegacyMatch.Shot); here only the coverage count, casings, gatling spin, burning
        //      ground, bullets into water and the voice rules
        static string ShotEvent(string type)
        {
            switch (type)
            {
                case "grunt": return "wpn_grunt_rifle"; case "sniper": return "wpn_sniper_rifle"; case "psycho": return "wpn_psycho_mg";
                case "laser": return "wpn_laser_fire"; case "jeep": return "wpn_jeep_mg"; case "gatling": return "wpn_gatling_fire";
                case "pyro": return "wpn_pyro_flame_loop"; default: return "wpn_grunt_rifle";
            }
        }

        void OnHitscan(ZEvent e, ZObject a, ZObject t)
        {
            if (a == null) return;
            string type = TypeOf(a); bool apc = a.Kind == ZKind.Vehicle && a.Type == "apc";
            if (apc) { Owned("wpn_apc_crew"); Count(Plays, "wpn_apc_crew"); }
            Owned(ShotEvent(type));
            var from = PosOf(a); var to = t != null ? PosOf(t) : Px(e.x, e.y);
            if (type == "pyro") { if (!OpenZPyroFire.OwnsSound && UnityEngine.Random.value < 0.25f) BurnGround(to); }   // pyro_fire_v2: its burning ground plays imp_flame_ground itself
            else if (type == "gatling")
            {
                if (!gatLast.TryGetValue(a.Id, out var gl) || Time.time - gl > 1.5f) Play("wpn_gatling_spinup", from, a.Id);
                gatLast[a.Id] = Time.time; gatPos[a.Id] = from;
            }
            if ((type == "psycho" || type == "jeep" || type == "gatling") && UnityEngine.Random.value < (type == "gatling" ? 0.6f : 0.3f)) Play("wpn_casings", from, a.Id);   // GC1: the gatling's brass stream tinkles more often
            if (type == "laser") Owned("imp_laser");
            else if (type != "pyro")
            {
                if (e.flag && t != null)
                {
                    bool bolt = type == "grunt" || type == "psycho" || type == "jeep";
                    if (!e.flag2 && !bolt && (t.Kind == ZKind.Vehicle || t.Kind == ZKind.Cannon)) Owned("imp_bullet_metal");   // OpenZFxPlayer ricochet
                    if (t.Kind == ZKind.Robot) Owned("imp_bullet_robot");                                                       // by design silent
                }
                else
                {
                    var miss = to + new Vector3(UnityEngine.Random.Range(-1.6f, 1.6f), 0, UnityEngine.Random.Range(-1.6f, 1.6f));
                    if (Wet(miss)) Play("imp_bullet_water", miss, e.otherId);                              // PL7: bullets into the river
                }
            }
            ShuttleHullHit(type, t, e.flag);                                                                // K256e: laser / electro / flame hits on the shuttle's hull are heard
            if (e.flag && t != null && t.Kind == ZKind.Robot) HitGrunt(t);                                  // death_voices_v1: a grunt now and then
            if (t != null) UnderAttack(a, t, e.flag);
        }

        void OnMissileFired(ZEvent e, ZObject a, ZObject t)
        {
            if (a == null) return;
            var from = PosOf(a); var to = Px(e.x, e.y);
            string type = TypeOf(a); bool apc = a.Kind == ZKind.Vehicle && a.Type == "apc";
            if (e.flag)
            {
                Play("wpn_grenade_pin", from, a.Id);
                Owned("wpn_grenade_throw");
                if (Wet(to)) Schedule(Mathf.Max(0.2f, e.value * 0.88f), () => Play("wtr_grenade_plop", to, -1));
                if (a.Owner == Player && OnScreen(from) && UnityEngine.Random.value < 0.34f) Say("vo_grenade_throw");
            }
            else
            {
                if (apc) { Owned("wpn_apc_crew"); Count(Plays, "wpn_apc_crew"); }
                switch (type)
                {
                    case "tough": Owned("wpn_tough_bazooka"); Owned("proj_rocket_flight"); if (a.Kind == ZKind.Robot) Owned("wpn_tough_backblast"); break;   // weapons_v2 + SOUND-bundle flight
                    case "light": Owned("wpn_light_tank"); break;
                    case "medium": Owned("wpn_medium_tank"); break;
                    case "heavy": Owned("wpn_heavy_tank"); break;
                    case "missile_launcher": Owned("wpn_mlrs"); break;
                    case "missile_cannon": Owned("wpn_missile_cannon"); break;
                    case "howitzer": Owned("wpn_howitzer"); break;
                    default: Owned(a.Kind == ZKind.Cannon ? "wpn_gun_cannon" : "wpn_medium_tank"); break;
                }
            }
            if (t != null) UnderAttack(a, t, false);
        }

        void OnExploded(ZEvent e, ZObject a, ZObject t)
        {
            var at = Px(e.x, e.y);
            bool wet = Wet(at);
            if (e.flag2)
            {   // turret / map-object debris lands (CombatView "debris"); a turret into the river plunges
                Owned("dst_turret_crash");
                if (wet && !(a != null && (a.Kind == ZKind.Vehicle || a.Kind == ZKind.Cannon))) Play("wtr_vehicle_plunge", at, -1, 0.7f);   // b27: a vehicle's or a gun's turret makes this sound itself through the shared water landing rule (OzWaterLanding)
                return;
            }
            Owned(e.radius <= 40 ? "exp_small" : e.radius <= 80 ? "exp_medium" : "exp_large");
            if (wet) Play(e.radius <= 40 && HasClips("exp_water_small") ? "exp_water_small" : "exp_water", at, -1);   // PL7: splash layer
            if (t != null && !e.flag && (t.Kind == ZKind.Vehicle || t.Kind == ZKind.Cannon)) Owned("imp_rocket_armor");
            if (t != null && t.Type == "rock" && !t.Destroyed) Play("dst_rock_chip", at + Vector3.up * 1.5f, t.Id);
            if (t != null && t.Kind == ZKind.Robot) HitGrunt(t);                                              // death_voices_v1
            if (t != null && a != null) UnderAttack(a, t, true);
        }

        // ---- deaths
        readonly List<float> killTimes = new List<float>();
        readonly List<(float t, Vector3 p)> rockDeaths = new List<(float, Vector3)>();
        float lastRockCrash = -100f;
        float alarmStartedAt = -1000f, alarmStoppedAt = -1000f, alarmLastThreat = -1000f; int alarmWave;
        /// <summary>
        /// One short klaxon burst when an attack on our fort starts (AlarmBurst s = 2 cycles of the 2.75 s clip,
        /// then a 1.5 s fade), then quiet for the rest of that attack. Re-armed only after AlarmCalm s without any enemy near the fort
        /// (the old code re-armed each time the debounced danger dipped for a moment, so a siege retriggered it again and again), or at
        /// once for a much bigger new wave (at least twice the enemies of the last burst and AlarmBigMin more, AlarmBigGap s after it).
        /// Rachel's "fort under attack" line keeps FortAlert's own gate; music_v3's drama stays the main tension cue.</summary>
        public const float AlarmBurst = 5.5f, AlarmCalm = 50f, AlarmBigGap = 20f; public const int AlarmBigMin = 6;
        /// <summary>Checks: klaxon bursts started and their summed seconds (start to the stop call) this session.</summary>
        public static int AlarmStarts { get; private set; }
        public static float AlarmSeconds { get; private set; }
        void OnDeath(ZEvent e, ZObject o, ZObject killer)
        {
            if (o == null) return;
            NoteLoss(o);                                                                        // help_cries: a lost member = heavy fire
            var p = PosOf(o);
            switch (o.Kind)
            {
                case ZKind.Robot:
                {
                    float wave = BlastWave.RobotDelay(ZGame.I, e, o, out _, out _);                             // combat_nb_v1 NB3: the cry comes with the throw
                    if (wave > 0.015f) StartCoroutine(RobotDeathSoundLater(wave, e, o, killer, p));
                    else RobotDeathSound(e, o, killer, p);
                }
                    if (o.Owner == Player)
                    {
                        if (killer != null && TypeOf(killer) == "sniper" && killer.Owner != Player && UnityEngine.Random.value < 0.5f) Say("vo_sniper_warning");
                    }
                    break;
                case ZKind.Vehicle:
                    if (VehicleDeathLater(e, o, p)) break;                                              // version 1.1 item 202: with its held explosion (AudioDirector.Kc5b.cs)
                    Owned("exp_vehicle_death");
                    if (o.Type == "light" || o.Type == "medium" || o.Type == "heavy") Play("dst_turret_toss", p + Vector3.up * 2f, o.Id);
                    // b27: the plunge sound that stood here (at the death, on a water tile) is made by the wreck itself when it really falls into the water (OzWaterLanding, UnitView.BeginWreck)
                    wrecks[o.Id] = (p, Time.time);
                    break;
                case ZKind.Cannon:
                    if (VehicleDeathLater(e, o, p)) break;                                              // version 1.1 item 202: with its held explosion
                    Owned("exp_cannon_death"); wrecks[o.Id] = (p, Time.time); break;
                case ZKind.Building: BuildingDeath(o, killer); break;
                default:
                    if (o.Type == "flag") break;
                    if (o.Type == "rock") RockDeath(p);
                    break;
            }
            // Alan kill logic (zplayer_events.cpp:927-959): our killer = the A unit or within 100 px of it
            if (killer != null && killer.Owner == Player && o.Owner != Player && o.Owner != Team.Neutral && o.IsUnit)
            {
                float now = Time.time;
                killTimes.Add(now); killTimes.RemoveAll(x => now - x > 6f);
                var quip = Def("vo_kill_quip");
                bool quipCooling = quip != null && lastLine.TryGetValue("vo_kill_quip", out var ql) && Time.unscaledTime - ql < Mathf.Max(1f, quip.cooldown);
                if (BoomCandidate(killer, o, p)) { }                                                    // a good kill on camera: the cheer comes when the fight there is over, no quip now
                else if (killer.LastAttackPlayerGiven) Say("vo_target_destroyed");
                else if (killTimes.Count >= 3 && quipCooling && OnScreen(p)) { Say("vo_taunt"); killTimes.Clear(); }
                else if (distress == null || killer.Id == distress.id || Near(killer, W.Get(distress.id), 100)) Say("vo_kill_quip");
            }
        }

        //      Every robot
        //      death: DeathVoices picks a cry (death_cry_<type>: Alan, the robot chain baked in) or a voiceless body death
        //      (death_robot / _blast / _melt / _zap / _crush: metal crack, crunch, servo dying, sparks + parts, pops, short circuits,
        //      fizzing power-down) by death type, no repeat of the last 8, at most 2 cries per second across the map; 3D at the robot.
        public static DeathVoices Deaths => DeathVoices.Shared;
        /// <summary>The death tables are loaded: LegacyMatch.RobotDeath and CombatView.Sniped leave the robot death sound to the director
        /// </summary>
        public static bool DeathVoicesLive => I && I.Loaded && I.deathVoices && I.HasClips("death_robot") && I.HasClips("death_cry_bullet");
        /// <summary>For a run-over / crush mechanic (Zod has none): mark the robot just before it dies and its death plays the crush pools.</summary>
        public static void MarkCrushed(int robotId) { if (I) I.crushMarks[robotId] = Time.time; }
        /// <summary>
        /// has Rachel's "Driver sniped" or Alan's "Gotcha" in the same frame; a cry on top was three voices at once). False = not live
        /// (play the fallback).</summary>
        public static bool DriverDeath(Vector3 p, int id)
        {
            if (!DeathVoicesLive) return false;
            I.DeathSound(p, DeathVoices.Cause.Bullet, id, true);
            return true;
        }
        /// <summary>Check menu (Play mode): one death of this type at the camera focus, through the real path (limiter included).</summary>
        public static void PreviewDeath(DeathVoices.Cause c) { if (DeathVoicesLive) I.DeathSound(I.ListenerPos, c, -1); }
        readonly Dictionary<int, float> crushMarks = new Dictionary<int, float>(), gruntAt = new Dictionary<int, float>();
        readonly Dictionary<SoundEventDef, List<string>> clipNames = new Dictionary<SoundEventDef, List<string>>();
        static readonly Func<double> DeathRnd = () => UnityEngine.Random.value;
        List<string> ClipNames(SoundEventDef d)
        {
            if (d == null) return null;
            if (!clipNames.TryGetValue(d, out var l)) { l = d.loaded.Select(c => c ? c.name : "").ToList(); clipNames[d] = l; }
            return l;
        }

        /// <summary>Death type from the sim: crush = a falling turret / wreck / rock chunk (Zod DeathDebris, lands 3-10 s after its owner
        /// died) or MarkCrushed; fire = pyro fire in the last 1.5 s; laser = a laser's hitscan; blast = a missile / shell / grenade in the
        /// last 1.5 s; everything else a bullet.</summary>
        DeathVoices.Cause DeathCauseOf(ZEvent e, ZObject o, ZObject killer)
        {
            if (o.Squashed) return DeathVoices.Cause.Crush;                                                // DV65: driven over by an enemy vehicle
            if (crushMarks.TryGetValue(o.Id, out var ct)) { crushMarks.Remove(o.Id); if (Time.time - ct < 1.5f) return DeathVoices.Cause.Crush; }
            if (killer != null && killer.Id != o.Id && killer.Destroyed && e.flag2 && killer.KillAt >= 0 && W.Time - killer.KillAt >= 2.0
                && (killer.Kind == ZKind.Vehicle || killer.Kind == ZKind.Cannon || (killer.Type ?? "").StartsWith("map_object")))
                return DeathVoices.Cause.Crush;
            if (e.flag) return DeathVoices.Cause.Fire;
            if (!e.flag2 && killer != null && TypeOf(killer) == "laser") return DeathVoices.Cause.Laser;
            if (e.flag2) return DeathVoices.Cause.Blast;
            return DeathVoices.Cause.Bullet;
        }

        /// <summary>combat_nb_v1 NB3: a splash death's cry / body sound when the robot is thrown (BlastWave delay).</summary>
        System.Collections.IEnumerator RobotDeathSoundLater(float wait, ZEvent e, ZObject o, ZObject killer, Vector3 p)
        {
            yield return new WaitForSeconds(wait);
            RobotDeathSound(e, o, killer, p);
        }

        void RobotDeathSound(ZEvent e, ZObject o, ZObject killer, Vector3 p)
        {
            if (o.Eaten) return;                                                                        // level 16: the crocodile's snap, crunch and the cut-off cry are its own sounds (CrocodileView.cs)
            if (!DeathVoicesLive)
            {
                Owned(e.flag ? "death_robot_melt" : "death_robot");
                if (o.Squashed) { Play("runover_splat", p, o.Id); return; }
                if (o.Owner == Player && OnScreen(p) && UnityEngine.Random.value < 0.3f) Play("vo_death_yelp", p, -1);
                return;
            }
            if (o.Squashed)
            {
                // the mechanical body crunch (death_robot_crush clips: metal crack, crunch, clatter); never a human-like cry / "ah" on a squash
                Play("runover_splat", p, o.Id);
                DeathSound(p, DeathVoices.Cause.Crush, o.Id, true);
                return;
            }
            DeathSound(p, DeathCauseOf(e, o, killer), o.Id);
        }

        /// <summary>One robot death: cry, voiceless body death or (limiter) nothing; 3D at the robot. A cry the view culls falls back
        /// to the voiceless clip; the limiter only counts what really played.</summary>
        void DeathSound(Vector3 p, DeathVoices.Cause cause, int id, bool voiceless = false)
        {
            var dv = Deaths; SyncDeathTuning(dv);
            float now = Time.time;
            string cryId = DeathVoices.CryEvent(cause), bodyId = DeathVoices.BodyEvent(cause);
            var cry = Def(cryId); var body = Def(bodyId);
            if (cry == null || cry.loaded.Count == 0) { cryId = DeathVoices.CryEvent(DeathVoices.Cause.Bullet); cry = Def(cryId); }
            if (body == null || body.loaded.Count == 0) { bodyId = "death_robot"; body = Def(bodyId); }
            dv.Noted(cause);
            Count(Triggers, cryId); Count(Triggers, bodyId);
            var cn = ClipNames(cry); var bn = ClipNames(body);
            var plan = dv.Decide(cause, now, cn, bn, DeathRnd, voiceless);
            if (plan.kind == DeathVoices.Kind.Voice && PlayForced(cryId, plan.clip, p + Vector3.up * 0.4f, id)) { dv.Commit(DeathVoices.Kind.Voice, cause, cn[plan.clip], now); return; }
            if (plan.bodyClip >= 0 && PlayForced(bodyId, plan.bodyClip, p, id)) { dv.Commit(DeathVoices.Kind.Body, cause, bn[plan.bodyClip], now); return; }
            dv.Commit(DeathVoices.Kind.Drop, cause, null, now);
        }

        bool PlayForced(string id, int idx, Vector3 pos, int emitter)
        {
            forcePick = idx;
            bool ok = Play(id, pos, emitter);
            forcePick = -1;
            return ok;
        }

        /// <summary>A hit that does not kill: now and then a short grunt at the robot (on screen, 3D, limiter in DeathVoices).</summary>
        void HitGrunt(ZObject t)
        {
            if (!deathVoices || t == null || t.Destroyed || t.Kind != ZKind.Robot || !HasClips(DeathVoices.GruntEvent)) return;
            if (UnityEngine.Random.value >= hitGruntChance) return;
            float now = Time.time;
            if (gruntAt.TryGetValue(t.Id, out var last) && now - last < hitGruntPerRobot) return;
            var p = PosOf(t);
            if (!OnScreen(p)) return;
            var dv = Deaths; SyncDeathTuning(dv);
            var names = ClipNames(Def(DeathVoices.GruntEvent));
            int idx = dv.PickGrunt(names, now, DeathRnd);
            if (idx < 0) return;
            if (PlayForced(DeathVoices.GruntEvent, idx, p + Vector3.up * 0.4f, t.Id)) { gruntAt[t.Id] = now; dv.CommitGrunt(names[idx], now); }
        }

        void SyncDeathTuning(DeathVoices dv)
        {
            dv.VoicelessShare[0] = voicelessBullet; dv.VoicelessShare[1] = voicelessBlast; dv.VoicelessShare[2] = voicelessFire;
            dv.VoicelessShare[3] = voicelessLaser; dv.VoicelessShare[4] = voicelessCrush;
            dv.CriesPerWindow = Mathf.Max(1, deathCriesPerSecond); dv.CryGap = Mathf.Max(0f, deathCryGap); dv.NoRepeat = Mathf.Max(0, deathNoRepeat);
        }

        /// <summary>
        /// within 1.5 s, and the settling tail (PL6).</summary>
        void RockDeath(Vector3 p)
        {
            Owned("dst_rock_break");
            float now = Time.time;
            rockDeaths.RemoveAll(x => now - x.t > 1.5f);
            bool wall = rockDeaths.Any(x => (x.p - p).sqrMagnitude < 12f * 12f);
            rockDeaths.Add((now, p));
            if (wall && now - lastRockCrash > 3f) { lastRockCrash = now; Play("dst_rock_crash_large", p, -1); }
            Schedule(0.8f, () => Play("dst_rock_settle", p, -1));
        }

        void BuildingDeath(ZObject o, ZObject killer)
        {
            var c = game.CenterWorld(o) + Vector3.up;
            if (o.IsFort) { Nuke(c, o.Owner == Player); return; }
            if (o.IsBridge)
            {
                Play(Jungle && HasClips("dst_bridge_collapse_wood") ? "dst_bridge_collapse_wood" : "dst_bridge_collapse", c, o.Id);
                // b27: the two blind splash sounds that stood here are gone: every bridge chunk that falls into the water makes its own splash, rings and sound (OzWaterLanding)
                for (int i = 0; i < 6; i++) Schedule(1.2f * i / 6f, () => Play("exp_building_booms", c + UnityEngine.Random.insideUnitSphere * 5f, -1));
                Announce("ann_bridge_destroyed");
                return;
            }
            V3BuildingDestroyed(o);                                                                  // sound_v3: Rachel for OUR factory / radar / repair
            bool big = o.Type.Contains("factory");
            Owned(big ? "dst_collapse_large" : "dst_collapse_medium");                                       // RulesView: the collapse
            Play("dst_structure_groan", c, o.Id);
            if (big) Schedule(0.45f, () => Play("dst_glass", c, o.Id));
            int n = o.Type == "radar" ? 4 + UnityEngine.Random.Range(0, 3) : o.Type == "repair" ? 6 + UnityEngine.Random.Range(0, 3) : 8 + UnityEngine.Random.Range(0, 3);
            for (int i = 0; i < n; i++) Schedule(0.3f + 2.5f * i / n, () => Play("exp_building_booms", c + UnityEngine.Random.insideUnitSphere * 4f, -1));   // RulesView.Booms is silent
            Owned("dst_debris_settle");                                                                      // BuildingBreaker: the settle
            burning[o.Id] = (c, Time.time);
            if (killer != null && killer.Owner == Player && o.Owner != Player) Say("vo_building_events", "destroyed");
        }

        void Nuke(Vector3 at, bool ours)
        {
            if (Time.time - lastNuke < 10f) return;                        // fort_front + fort_back die together
            lastNuke = Time.time;
            Owned("nuke_flash");                                           // NukeFx: descent + blast, side booms ("fortboom"), the pitched collapse rumble
            // fx_v2: the blast comes when the strike's bomb lands (OpenZNukeStrike), so the duck and the ear ring wait for it
            float hold = OpenZVoiceV3.PlanNuke(ours);                                   // voice_lines_v3: the enemy fort's bomber waits for Rachel + Zod's red button
            float hit = OpenZNukeStrike.Enabled ? OpenZNukeStrike.ExpectedImpactDelay + hold : 0f;
            Action ring = () => { AddDuck(-12f, 0f, -12f, 0f, 2.5f, 0.05f, 6f); Play("nuke_ear_ring", at, -1); };
            if (OpenZNukeStrike.Enabled && hold > 0f) OpenZVoiceV3.OnNukeImpact(ring); else Schedule(hit, ring);   // voice_lines_v3: held launch = unknown delay   // bible: 0.05 s, hold 2.5 s, release 6 s
            // loss "Warning! Nuke incoming!" ran straight into "Our fort has fallen!" (heard as one garbled line, after the fort was already
            // down and while the launch alarm + the bomber show the nuke). Now ONE clear line: ann_fort_lost = "Warning! Our fort has been destroyed!"
            // "Warning! Our fort has been destroyed!" is gone from the loss completely (we see it and hear our robots scream); "Mission failed." stays
            if (ours) Announce("ann_nuke_detected");
            else if (hold <= 0f) Announce("ann_nuke_launched");                  // voice_lines_v3: with the intro Rachel explains it (ann_nuke_explain)
            if (!OpenZVictory.OwnsEndSequence)                              // fx_v2: the end sequence owns the voices after the blast
                Schedule(2.5f, () => { if (ours) Say("vo_nuke_reaction"); else Say("vo_kill_quip"); });
        }

        void OnSniped(ZEvent e, ZObject v, ZObject by)
        {
            Owned("imp_snipe");                                                                               // CombatView: ricochet + death
            Owned("imp_snipe_pop");                                                                           // placepop_v1: PlacementPop.Sniped (Atom pop)
            if (v != null) snipedAt[v.Id] = Time.time;
            if (v != null) OzSoundV3.EngineDies(v, PosOf(v));                                       // sound_v3: the engine dies with the driver
            if ((Team)(int)e.value == Player) Announce("ann_driver_sniped");
            else if (by != null && by.Owner == Player && !(v != null && BoomCandidate(by, v, PosOf(v)))) Say("vo_snipe_success");   // a driver shot out on camera may get the cheer instead
        }

        // ---- the cheer after a good kill ("Boom!")
        /// <summary>A short cheer from the player's own unit after a good kill. Asked for only when one of the player's units
        /// destroys an enemy vehicle or gun or shoots out a driver (never for a robot) and the kill is on screen; BoomChance of
        /// those are taken. The cheer is then spoken by the killer's own voice once the fight there is over: no enemy that can
        /// shoot within BoomEnemyM of the killer and the killer attacking nothing for BoomQuiet s, the killer still alive and on
        /// screen, the announcer, the commander and the robots silent. It waits for that at most BoomWait s and is dropped
        /// otherwise. Never two cheers within BoomGap s in the whole game. The electrobot keeps its own kill lines.</summary>
        public static float BoomGap = 32f, BoomQuiet = 1.5f, BoomEnemyM = 26f, BoomWait = 7f, BoomChance = 0.7f;
        /// <summary>Checks: cheers spoken, good kills on camera that asked, cheers dropped while waiting.</summary>
        public static int Booms, BoomAsked, BoomDropped;
        float lastBoom = -100f, boomAt, boomQuietSince = -1f; int boomKiller = -1; string boomWhat;

        bool BoomCandidate(ZObject killer, ZObject victim, Vector3 p)
        {
            if (killer == null || victim == null || killer.Owner != Player || killer.Type == ZElectro.Type) return false;
            if (victim.Kind != ZKind.Vehicle && victim.Kind != ZKind.Cannon) return false;         // never for a robot killed in a robot fight
            if (!OnScreen(p, 0f)) return false;                                                     // only a kill the player sees
            float now = Time.unscaledTime;
            if (boomKiller >= 0 || now - lastBoom < BoomGap) return false;
            BoomAsked++;
            if (UnityEngine.Random.value > BoomChance) return false;                                // not every win gets one
            boomKiller = killer.Id; boomAt = now; boomQuietSince = -1f; boomWhat = victim.Type;
            return true;
        }

        void BoomTick()
        {
            if (boomKiller < 0) return;
            float now = Time.unscaledTime; var k = W.Get(boomKiller);
            if (k == null || k.Destroyed || gameOver || now - boomAt > BoomWait) { boomKiller = -1; BoomDropped++; return; }
            bool fight = k.AttackObject != null;
            if (!fight)
                foreach (var e in W.Objects)
                {
                    if (e == null || e.Destroyed || !e.IsUnit || e.Owner == Team.Neutral || e.Owner == Player || !e.CanAttack()) continue;
                    if (Near(e, k, (int)(BoomEnemyM * 8f))) { fight = true; break; }
                }
            if (fight) { boomQuietSince = -1f; return; }                                            // the fight there goes on
            if (boomQuietSince < 0f) boomQuietSince = now;
            if (now - boomQuietSince < BoomQuiet) return;
            if (!OnScreen(PosOf(k), 0f)) { boomKiller = -1; BoomDropped++; return; }                // the camera left
            if ((rachel.busy && rachel.src && rachel.src.isPlaying) || rachelQueue.Count > 0 || AlanBusy || (ExternalVoiceBusy != null && ExternalVoiceBusy())) return;   // never over another voice: wait
            string id = UnitVoices.SecondVoice(k.Type) ? "vo_boom_brad" : "vo_boom";
            boomKiller = -1; lastBoom = now; Booms++;
            Say(id);
            OzSoundLog.Add("VO", id, null, "CHEER by " + k.Type + " #" + k.Id + " after a " + boomWhat + " kill, " + (now - boomAt).ToString("0.0") + " s after it");
            Debug.Log("[OpenZ] cheer " + id + " by " + k.Type + " #" + k.Id + " after a " + boomWhat + " kill, " + (now - boomAt).ToString("0.0") + " s after it; cheers so far " + Booms);
        }

        // ---- boarding / capture: the foley is CombatView's EnterSequence + BoardingView; here the lines
        void OnEntered(ZObject r, ZObject v)
        {
            if (v == null) return;
            Owned("brd_capture_start"); Owned("brd_vehicle_capture");
            Owned(v.Kind == ZKind.Cannon ? "brd_gun_mount" : "brd_vehicle_enter");
            if (v.Kind == ZKind.Vehicle) Owned("veh_engine_start");
            var before = Before(v); var lastSide = LastSide(v);
            OpenZVoiceV3.NoteEntered(r, v, before);                                                         // voice_lines_v3: L17 car-park tips
            CaptureHold.When(v.Id, () => EnteredLines(r, v, before, lastSide));                             // boarding_nb_v1 NB17: said once the robot sits inside
        }

        void EnteredLines(ZObject r, ZObject v, Team before, Team lastSide)
        {
            if (v.Destroyed || v.Owner == Team.Neutral) return;                                             // NB17: lost again (sniped / destroyed) during the climb-in
            if (r != null && r.Owner == Player)
            {
                if (before == Player) { if (v.Kind == ZKind.Vehicle && !HvBoarded(v) && !v.IsShuttle) { Say("vo_vehicle_enter"); IconicBoarded(v, 0.25f); } }   // DV118: the shuttle says its own "All aboard!" (OzPlayableShuttle)   // KC4b: ALAN & BRAD say their own boarding lines   // robot_iconic_lines_v1: + a passenger (squad of 2+, rare)
                else if (v.Kind == ZKind.Cannon) { Say("vo_gun_captured"); V3GunCaptured(); }             // sound_v3: + Rachel "Gun captured."
                else if (v.Kind == ZKind.Vehicle && HvBoarded(v)) HvCaptured();                            // KC4b: ALAN & BRAD capture with their own lines, the announcer after them (AudioDirector.HeroVoices.cs)
                else if (!AlanBusy && OpenZVoiceV3.CaptureQuip())
                {
                    Schedule(OpenZVoiceV3.CaptureQuipDelay, () => Say("vo_vehicle_captured"));
                    if (OpenZVoiceV3.CaptureRachelToo()) Schedule(OpenZVoiceV3.CaptureRachelAfterQuip, () => Announce("ann_vehicle_captured"));
                    else IconicBoarded(v, OpenZVoiceV3.CaptureQuipDelay + 0.25f);                         // robot_iconic_lines_v1: only when Rachel stays quiet
                }
                else Schedule(OpenZVoiceV3.CaptureRachelDelay, () => Announce("ann_vehicle_captured"));
            }
            else if (r != null && r.Owner != Team.Neutral && lastSide == Player)
                Announce(v.Kind == ZKind.Cannon ? "ann_gun_lost" : "ann_vehicle_lost");
        }

        void OnEjected(ZObject v, ZObject leader)
        {
            Owned("brd_exit");                                                                                // CombatView plays "exit"
            if (v == null) return;
            var p = PosOf(v);
            if (v.Type == "apc") Play("veh_apc_ramp", p, v.Id);
            if (!v.IsShuttle) Schedule(0.45f, () => Play("rob_land", p, v.Id));                          // the shuttle's crew walks down its ramp (the ramp foot's step clank): nobody jumps down
            bool sniped = snipedAt.TryGetValue(v.Id, out var st) && Time.time - st < 1.5f;
            if (leader != null && leader.Owner == Player && !v.Destroyed && !sniped && !v.IsShuttle) Say("vo_vehicle_exit");   // DV118: the shuttle says its own "Everybody out!"
        }

        // ---- buildings changing hands (zone flips): the recolour cue + Rachel's building lines
        void OnOwnerChanged(ZObject a)
        {
            if (a == null || a.Kind != ZKind.Building) return;
            var before = Before(a); var now = a.Owner;
            if (!FlagLive)
            {   // FlagView sends its own staggered building cue (OnFlagSound)
                int n = frameBuildings.Count; frameBuildings.Add(a.Id);
                var p = game.CenterWorld(a);
                Schedule(0.08f * n, () => Play("bld_building_captured", p, a.Id));
            }
            if (before == now) return;
            bool robotF = a.Type == "robot_factory", vehicleF = a.Type == "vehicle_factory", repair = a.Type == "repair";
            if (now == Player)
            {
                string line = robotF ? "ann_robot_factory_captured" : vehicleF ? "ann_vehicle_factory_captured" : repair ? "ann_repair_captured" : null;
                if (line != null) Schedule(1.2f, () => Announce(line));
            }
            else if (before == Player)
            {
                string line = robotF || vehicleF ? "ann_factory_lost" : repair ? "ann_repair_lost" : null;
                if (line != null) Schedule(1.2f, () => Announce(line));
            }
        }

        void OnRevived(ZObject a)
        {
            if (a == null || a.Kind != ZKind.Building) return;
            if (burning.TryGetValue(a.Id, out var bf) && Time.time - bf.t < 30f)
            {
                burning.Remove(a.Id); StopLoop("fire:" + a.Id, 0.8f);                                       // the fire loop ran on for 30 s after a rebuild
                var fc = game.CenterWorld(a);
                OpenZVoiceV3.FireOut(fc, a.Owner == Player || OnScreen(fc, 0.3f));
            }
            if (!a.IsBridge) Play("bld_building_revived", game.CenterWorld(a), a.Id);
            var c = game.CenterWorld(a);
            if (a.IsBridge)
            {
                Play(Jungle && HasClips("bld_bridge_rebuild_wood") ? "bld_bridge_rebuild_wood" : "bld_bridge_rebuild", c, a.Id);
                bool near = OnScreen(c, 0.3f) || W.Objects.Any(o => o.Owner == Player && o.IsMobile && !o.Destroyed && Near(o, a, 400));
                if (near) Announce("ann_bridge_repaired");
            }
            if (a.Owner == Player || (a.IsBridge && OnScreen(c, 0.3f))) Say("vo_building_events", a.IsBridge ? "Bridge" : "repaired!Bridge");
        }

        // ---- territory (FlagView + RulesView play the flag sounds; FlagView asks CombatAudio.Voice for Rachel -> routed here)
        float lastTerritoryAnn = -100f;
        void OnFlag(ZEvent e)
        {
            var team = (Team)(int)e.value; var old = (Team)e.radius;
            bool flagView = FlagLive;
            if (!flagView) { Owned("ter_flag_capture"); if (team == Player) Owned("ter_zone_gained"); }   // RulesView fallback plays them
            if (team == Player && !flagView)
            {
                if (Time.time - lastTerritoryAnn >= 6f) { lastTerritoryAnn = Time.time; Announce("ann_territory_captured"); }   // a repeat stays silent
                if (e.flag) Schedule(1.2f, () => Announce("ann_radar_online"));                           // FlagCaptured.flag = the zone has a radar
            }
            if (old == Player && team != Player)
            {
                if (!flagView) { UI("ter_zone_lost"); Announce("ann_territory_lost"); }
                if (e.flag) Schedule(1.2f, () => Announce("ann_radar_lost"));
            }
        }

        // ---- production heuristics (preferred: the production window calls Announce itself and sets manualProductionAnnounce)
        readonly Dictionary<int, (bool on, string unit)> prod = new Dictionary<int, (bool, string)>();
        void OnProduction(ZObject b, bool announce)
        {
            prod.TryGetValue(b.Id, out var was);
            var now = (b.Building, b.CurUnit);
            prod[b.Id] = now;
            if (manualProductionAnnounce || !announce) return;
            if (now.Building && (!was.on || was.unit != now.CurUnit)) Announce("ann_starting_manufacture");
            else if (!now.Building && was.on) Announce("ann_manufacturing_cancelled");
        }
        /// <summary>Set true when the production window calls Announce itself (then ProductionChanged is ignored).</summary>
        public static bool manualProductionAnnounce;

        // ---- game over (Zod end animations: sting -> Rachel +0.8 s -> one Alan face every 1.2 s, max 6)
        bool gameOver;
        void OnGameOver(Team winner)
        {
            if (gameOver) return; gameOver = true;
            if (LoopActive("fortalarm"))
            {   // lights_tail_v1 review fix: UpdateMusic returns once gameOver is set, so a klaxon burst still running now (a victory inside
                // its 5.5 s) was never stopped and looped behind the end screen: stop it here with its ALARM STOP line (the beacons spin down)
                float at = Time.time; StopLoop("fortalarm", 1f); alarmStoppedAt = at; AlarmSeconds += at - alarmStartedAt;
                OzSoundLog.Add("AD", "bld_fort_alarm", null, "ALARM STOP after " + (at - alarmStartedAt).ToString("F1") + " s (game over)", 0f, null);
            }
            bool won = winner == Player;
            FadeMusicOut(1.2f);
            AddDuck(0f, -6f, -6f, 0f, 8f, 0.3f, 2f);
            if (OpenZVictory.OwnsEndSequence)
            {   // fx_v2: OpenZVictory plays the lines one after another after the nuke, then the sting with the end screen
                Schedule(18f, () => { if (MusicOn && !MusicV3Owns) PlayMusic(Def(EndMusicId(won)), -1, 2f, EndMusicDb(won)) /* music_v2; music_v3: MusicCore plays the end music at the end screen */; });
                return;
            }
            UI(won ? "ter_victory_sfx" : "ter_defeat_sfx"); UI(won ? "ui_victory_hit" : "ui_defeat_hit");   /* sound_v3: the hit also with music off */                                             // Music bus: follows the oz_music toggle
            Schedule(0.8f, () => Announce(won ? "ann_victory" : "ann_defeat"));
            int faces = 0;
            foreach (var o in W.Objects) if (o.Owner == Player && o.IsUnit && !o.Destroyed) faces++;
            faces = Mathf.Clamp(won ? faces : 6, 1, 6);
            for (int i = 0; i < faces; i++) Schedule(2.2f + 1.2f * i, () => Say(won ? "vo_win_lines" : "vo_lose_lines"));
            Schedule(9f, () => { if (MusicOn && !MusicV3Owns) PlayMusic(Def(EndMusicId(won)), -1, 2f, EndMusicDb(won)) /* music_v2; music_v3: MusicCore plays the end music at the end screen */; });
        }

        // ============================================================================================================================
        // Voice channels (2D; every line ducks CombatAudio's SFX -6 dB through CombatAudio.DuckFor)
        // ============================================================================================================================
        Voice alanMain, alanAlert, rachel;
        struct Line { public SoundEventDef d; public float t; public string f; }              // endings_v1: f = line filter
        readonly List<Line> rachelQueue = new List<Line>();
        readonly Dictionary<string, float> lastLine = new Dictionary<string, float>();
        float alanMainLast = -10f;
        /// <summary>The quiet time after a robot select / order / idle line ends before the next one may start (seconds).</summary>
        public const float AlanMainGap = 3f;
        float alanMainUntil = -100f;

        void BuildChannels()
        {
            alanMain = NewVoice("Voice_AlanMain"); alanAlert = NewVoice("Voice_AlanAlert"); rachel = NewVoice("Voice_Rachel");
            foreach (var v in new[] { alanMain, alanAlert })
            {
                var hp = v.src.gameObject.AddComponent<AudioHighPassFilter>(); hp.cutoffFrequency = 250f; hp.enabled = alanRadioFilter;
                var ds = v.src.gameObject.AddComponent<AudioDistortionFilter>(); ds.distortionLevel = 0.12f; ds.enabled = alanRadioFilter;   // same as CombatAudio.VoiceSource
                var ch = v.src.gameObject.AddComponent<AudioChorusFilter>(); ch.dryMix = 0.8f; ch.wetMix1 = 0.25f; ch.wetMix2 = 0.2f; ch.wetMix3 = 0f; ch.delay = 8f; ch.rate = 0.6f; ch.depth = 0.05f; ch.enabled = alanRadioFilter;
            }
            var rh = rachel.src.gameObject.AddComponent<AudioHighPassFilter>(); rh.cutoffFrequency = 200f; rh.enabled = rachelRadioFilter;
        }

        bool AlanBusy => (alanMain.busy && alanMain.src && alanMain.src.isPlaying) || (alanAlert.busy && alanAlert.src && alanAlert.src.isPlaying);
        bool AlanAlertBusy => alanAlert.busy && alanAlert.src && alanAlert.src.isPlaying;
        /// <summary>fx_v2: true while an Alan or Rachel line plays or Rachel still has queued lines (OpenZVictory never overlaps them).</summary>
        public static bool VoiceBusy => I && I.VoiceBusyImpl();
        bool VoiceBusyImpl() =>
            (alanMain != null && alanMain.busy && alanMain.src && alanMain.src.isPlaying) || (alanAlert != null && alanAlert.busy && alanAlert.src && alanAlert.src.isPlaying)
            || (rachel != null && rachel.busy && rachel.src && rachel.src.isPlaying) || rachelQueue.Count > 0;

        void SayImpl(string id, string filter)
        {
            var d = Def(id); Count(Triggers, id);
            if (d == null || !voices) return;
            if (OpenZVictory.MutesVoice(id)) return;                                  // fx_v2 CHECK: only the end sequence speaks while it runs
            if (ExternalVoiceBusy != null && ExternalVoiceBusy()) return;               // zod_cameo: Alan never talks over Commander Zod
            bool robotLine = d.channel == "alan_main" || d.channel == "alan_alert";
            if (robotLine && !RobotChatter.Allows(id)) { RobotChatter.Skipped++; OzSoundLog.Add("dir", id, null, "robot chatter " + RobotChatter.Names[RobotChatter.Level] + ": skipped"); return; }
            float cooldown = robotLine ? RobotChatter.Cooldown(id, d.cooldown) : d.cooldown;
            if (cooldown > 0 && lastLine.TryGetValue(id, out var lt) && Time.unscaledTime - lt < cooldown) return;
            if (ShuttleLineWaitsForRachel(id, filter)) return;                                              // K256e: a shuttle line never under Rachel: held, said after her (View/AudioDirector.ShuttleRound3.cs)
            if (robotLine && ShuttleHoldsRobotLine(id, filter)) return;                                    // DV118: no robot line over the shuttle landing line: held, said after it (View/AudioDirector.ShuttleVoice.cs)
            if (d.channel == "alan_main")
            {
                // Never cut a unit's line mid-word: the current line finishes and the next one is skipped or queued (only the latest
                // pending line plays, and only if it is still relevant within about a second; Zod restarted the portrait).
                // A line of the same event as the one speaking (box-select spam) is dropped; a different one (the order after a select) waits.
                // Robot lines never stack: while one is speaking, and for AlanMainGap seconds after it ends, a new select / order / idle
                // line is dropped (not queued), so repeated clicks do not make the robots talk nonstop. A line is still never cut mid-word.
                if ((alanMain.busy && alanMain.src.isPlaying) || (!shuttleReleasing && Time.unscaledTime < alanMainUntil + AlanMainGap))
                {
                    OzSoundLog.Add("dir", id, null, "dropped (robot line gap)");
                    return;
                }
                if (!PlayVoice(d, alanMain, filter)) return;
                alanMainLast = Time.unscaledTime; alanMainId = id; alanMainUntil = Time.unscaledTime + Mathf.Max(0f, alanMainClipLeft());
            }
            else if (d.channel == "alan_alert" || d.channel == "alan_end")
            {
                if (d.channel == "alan_alert" && AlanBusy)
                {
                    // death cry: it waits for Alan (at most PromoWait s, its own cooldown kept) and plays before any queued chatter
                    if (id == "vo_promoted") { pendingPromo = id; pendingPromoFilter = filter; pendingPromoAt = Time.unscaledTime; OzSoundLog.Add("dir", id, null, "waits (Alan busy)"); }
                    return;
                }
                if (alanAlert.busy && alanAlert.src.isPlaying) alanAlert.src.Stop();
                if (!PlayVoice(d, alanAlert, filter)) return;
            }
            else { Play(id, ListenerPos, -1, 1f, 1f, filter); return; }                // yelps (2D one-shots)
            lastLine[id] = Time.unscaledTime;
            AddDuck(d.duckMusic != 0 ? d.duckMusic : -4f, 0f, 0f, 0f, VoiceLen(), 0.1f, 0.6f);
        }

        string pendingAlan, pendingAlanFilter, alanMainId; float pendingAlanAt;
        float alanMainClipLeft() => alanMain.src && alanMain.src.clip ? (alanMain.src.clip.length - alanMain.src.time) / Mathf.Max(0.1f, Mathf.Abs(alanMain.src.pitch)) : 0f;
        static string Family(string id) => id != null && id.StartsWith("vo_select") ? "vo_select" : id;
        /// <summary>PA7: the latest line that waited for Alan's current line plays once it ends, at most PendingAlanMax s after that
        /// line's end (pendingAlanAt = the moment the speaking line was due to end when the new one was asked).</summary>
        public const float PendingAlanMax = 1.1f;
        string pendingPromo, pendingPromoFilter; float pendingPromoAt; public const float PromoWait = 4f;
        void TickPendingAlan()
        {
            if (pendingPromo != null && alanMain != null)
            {
                if (Time.unscaledTime - pendingPromoAt > PromoWait) { OzSoundLog.Add("dir", pendingPromo, null, "dropped (waited " + PromoWait + " s)"); pendingPromo = null; }
                else if (!AlanBusy)
                {
                    var pid = pendingPromo; var pf = pendingPromoFilter; pendingPromo = null;
                    if (pendingAlan != null && Family(pendingAlan) == "vo_select") pendingAlan = null;            // chatter yields to the promotion
                    SayImpl(pid, pf); return;
                }
            }
            if (pendingAlan == null || alanMain == null) return;
            if (Time.unscaledTime - pendingAlanAt > PendingAlanMax) { pendingAlan = null; return; }
            if (alanMain.src && alanMain.src.isPlaying) return;
            var id = pendingAlan; var f = pendingAlanFilter; pendingAlan = null; alanMainLast = -1f;
            SayImpl(id, f);
        }

        float VoiceLen() => 0.2f + (alanMain.src.isPlaying ? alanMain.src.clip.length : alanAlert.src.isPlaying ? alanAlert.src.clip.length : 1f);

        bool PlayVoice(SoundEventDef d, Voice ch, string filter)
        {
            if (d.loaded.Count == 0) return false;
            if (!AlertGate.Check(d.id, out var held)) { OzSoundLog.Add("VO", d.id, null, "DROP " + held, 0f); return false; }   // repeated alerts: once per group per 30 s, 7 s apart (skipped, not queued)
            int idx = Pick(d, filter);
            ch.def = d; ch.busy = true; ch.loop = false; ch.start = Time.unscaledTime; ch.offLin = 1f; ch.fade = ch.fadeTarget = 1f;
            ConfigureSource(ch, d, d.loaded[idx]);
            ch.src.spatialBlend = 0f; ch.combatMix = false; ch.lp.enabled = false;
            ch.baseLin = Db(d.volDb + d.gains[idx]);
            ch.src.pitch = d.pitchMul * (1f + (d.pitchVar > 0 ? UnityEngine.Random.Range(-d.pitchVar, d.pitchVar) : 0f));
            Apply(ch); ch.src.Play();
            var ca = CA;
            if (ca)
            {   // audio_v2: Alan ducks the combat buses -4 dB and the world -1 dB, Rachel -6 / -2 (was -6 dB on every SFX incl. doors, foley)
                bool r = ch == rachel;
                ca.DuckFor(ch.src.clip.length / Mathf.Max(0.5f, Mathf.Abs(ch.src.pitch)) + 0.15f, r ? -6f : -4f, r ? -2f : -1f);
            }
            Count(Plays, d.id); Played++; if (sweeping) Count(SweepPlays, d.id);
            AlertGate.Played(d.id);
            OzSoundLog.Add("VO", d.id, ch.src.clip ? ch.src.clip.name : null, "PLAY " + d.bus + (ch == rachel ? " rachel" : ch == alanMain ? " alan_main" : " alan_alert"), ch.src.volume);   // voice_emotion_v1: voice lines in the sound log
            return true;
        }

        /// <summary>audio_v2: announcer groups: a line is dropped when a line of its group played or is queued within the window.</summary>
        static readonly Dictionary<string, (string name, float window)> AnnGroups = new Dictionary<string, (string, float)>
        {
            ["ann_robot_manufactured"] = ("made", 8f), ["ann_vehicle_manufactured"] = ("made", 8f), ["ann_gun_manufactured"] = ("made", 8f),
            ["ann_territory_captured"] = ("territory", 4f), ["ann_territory_lost"] = ("territory", 4f),
            ["ann_starting_manufacture"] = ("production", 5f), ["ann_manufacturing_cancelled"] = ("production", 5f),
        };
        readonly Dictionary<string, float> groupLast = new Dictionary<string, float>();

        /// <summary>Status lines by kind. A line of a kind is not spoken again within KindGap s of the last spoken line of that kind:
        /// the repeat is dropped, never queued, and no other voice takes it. Lines of different kinds still follow each other.
        /// Warnings (the fort or the base under attack, the nuke lines, a lost fort, the end lines) have no kind here and are never
        /// dropped by this rule.</summary>
        public static float KindGap = 15f;
        static readonly Dictionary<string, string> AnnKinds = new Dictionary<string, string>
        {
            ["ann_territory_captured"] = "territory", ["ann_territory_lost"] = "territory_lost",
            ["ann_robot_factory_captured"] = "factory", ["ann_vehicle_factory_captured"] = "factory", ["ann_repair_captured"] = "factory",
            ["ann_factory_lost"] = "factory_lost", ["ann_repair_lost"] = "factory_lost",
            ["ann_radar_online"] = "radar", ["ann_radar_lost"] = "radar_lost",
            ["ann_vehicle_captured"] = "unit_captured", ["ann_gun_captured"] = "unit_captured",
            ["ann_robot_manufactured"] = "made", ["ann_vehicle_manufactured"] = "made", ["ann_gun_manufactured"] = "made",
        };
        readonly Dictionary<string, float> kindLast = new Dictionary<string, float>();
        /// <summary>Checks: repeats dropped by the kind rule; kindDropped: the last request was dropped by it.</summary>
        public static int KindDrops;
        bool kindDropped;

        bool AnnounceImpl(string id, string lineFilter = null)
        {
            var d = Def(id); Count(Triggers, id);
            kindDropped = false;
            if (d == null || !voices) return false;
            if (OpenZVictory.MutesVoice(id)) return false;                            // fx_v2 CHECK: only the end sequence speaks while it runs
            float now = Time.unscaledTime;
            if (!Chatter.Allows(id)) return false;
            if (AnnKinds.TryGetValue(id, out var kind))
            {   // the same kind of status line spoken (or waiting) a moment ago: this one stays silent
                bool again = kindLast.TryGetValue(kind, out var kt) && now - kt < KindGap;
                if (!again) foreach (var q in rachelQueue) if (AnnKinds.TryGetValue(q.d.id, out var qk) && qk == kind) { again = true; break; }
                if (again) { kindDropped = true; KindDrops++; return false; }
            }
            float gapMul = Chatter.Level == 1 ? 2.5f : 1f;                                // Less: every kept line waits 2.5x its cooldown
            if (lastLine.TryGetValue(id, out var lt) && now - lt < Mathf.Max(4f * gapMul, d.cooldown * gapMul)) return false;   // same line within 4 s dropped
            foreach (var q in rachelQueue) if (q.d == d) return false;
            if (AnnGroups.TryGetValue(id, out var grp))
            {   // audio_v2: one line per burst (T19: fort + robot factory finish in the same tick; a contested flag flips every 0.2 s)
                if (groupLast.TryGetValue(grp.name, out var gt) && now - gt < grp.window) return false;
                foreach (var q in rachelQueue) if (AnnGroups.TryGetValue(q.d.id, out var qg) && qg.name == grp.name) return false;
                groupLast[grp.name] = now;
            }
            lastLine[id] = now;
            rachelQueue.Add(new Line { d = d, t = now, f = lineFilter });
            rachelQueue.Sort((x, y) => x.d.rank.CompareTo(y.d.rank));
            while (rachelQueue.Count > 3) rachelQueue.RemoveAt(rachelQueue.Count - 1);
            return true;
        }

        void UpdateChannels()
        {
            float now = Time.unscaledTime;
            foreach (var ch in new[] { alanMain, alanAlert, rachel }) { if (ch.busy && !ch.src.isPlaying && !AudioListener.pause) ch.busy = false; if (ch.busy) Apply(ch); }
            rachelQueue.RemoveAll(q => now - q.t > 4f && q.d.rank > 3);                          // stale lines dropped (ranks 1-3 kept)
            if (rachelQueue.Count == 0) return;
            var next = rachelQueue[0];
            if (rachel.busy && rachel.src.isPlaying)
            {
                if (next.d.rank < rachel.def.rank && now - rachel.start > 0.3f) rachel.src.Stop();   // a higher rank may cut after 0.3 s
                else return;
            }
            if (RachelWaitsForShuttleLine(now)) return;                                                     // K256e: every Rachel line waits while a shuttle line plays + 0.3 s (View/AudioDirector.ShuttleRound3.cs)
            if (AlanBusy && (now - next.t < 0.6f || next.d.id == "ann_shuttle_landing") && !ShuttleLandingStepsAside(now, ref next)) return;   // DV118: the landing line waits for Alan; a line that may speak goes first                                         // Rachel waits at most 0.6 s for Alan
            if (ExternalVoiceBusy != null && ExternalVoiceBusy() && now - next.t < 12f) return;  // zod_cameo: Rachel waits for Commander Zod
            rachelQueue.RemoveAt(0);
            if (PlayVoice(next.d, rachel, next.f))
            {
                AddDuck(next.d.duckMusic != 0 ? next.d.duckMusic : -6f, 0f, next.d.duckAmb != 0 ? next.d.duckAmb : -3f,
                        next.d.duckRobots != 0 ? next.d.duckRobots : -6f, rachel.src.clip.length + 0.1f, 0.15f, 0.8f);
                if (AnnKinds.TryGetValue(next.d.id, out var spoken)) kindLast[spoken] = now;        // the kind's quiet time runs from the spoken line
            }
        }

        /// <summary>CombatAudio.VoiceRoute: the live views' Alan / Rachel requests. null = CombatAudio plays it itself.</summary>
        bool? RouteVoice(string key, string[] names, float vol, bool alan)
        {
            if (!voices || !routeLegacyVoices) return null;
            switch (key)
            {
                case "select": case "order_ack": return uiPolling ? true : (bool?)null;        // PlayerInput.cs:306,320 -> PollInput plays vo_select_* / vo_order_*
                case "driver_hurt": Say("vo_hurt"); return true;                                 // CombatView.cs:190
                case "driver_down": case "driver_sniped": return true;                           // CombatView.cs:207-208 -> OnSniped (ann_driver_sniped / vo_snipe_success)
                case "vehicle_captured": return true;                                            // CombatView.cs:289 -> OnEntered (ann_vehicle_captured)
            }
            if (key != null && key.StartsWith("ann_") && Def(key) != null)
            {   // FlagView.Voice (territory captured / lost, radar online, fort under attack)
                bool queued = AnnounceImpl(key);
                if (!queued && !kindDropped && key == "ann_territory_captured") Say("vo_territory_taken");      // a repeat inside the kind's quiet time stays silent
                return true;
            }
            return SayRaw(key, names, alan) ? true : (bool?)null;
        }

        /// <summary>A line only CombatAudio knows (its clip names in Resources/OpenZ/Sfx): played on our channels so two Alans
        /// or two Rachels never talk over each other.</summary>
        bool SayRaw(string key, string[] names, bool alan)
        {
            var ca = CA; if (!ca || names == null || key == null) return false;
            string id = "raw_" + key;
            if (!defs.TryGetValue(id, out var d))
            {
                d = new SoundEventDef
                {
                    id = id, bus = alan ? "Voice/Robots" : "Voice/Announcer", channel = alan ? "alan_alert" : "rachel", status = "legacy", rank = 5,
                    maxVoices = 1, pitchMul = 1f, noRepeat = true, routes = new string[0], cooldown = 1f,
                    loaded = new List<AudioClip>(), gains = new List<float>(), lines = new List<string>(), standins = new List<bool>(),
                };
                foreach (var n in names) { var c = ca.Clip(n); if (c) { d.loaded.Add(c); d.gains.Add(0f); d.lines.Add(n); d.standins.Add(false); } }
                defs[id] = d;
            }
            if (d.loaded.Count == 0) return false;
            if (alan) SayImpl(id, null); else AnnounceImpl(id);
            return true;
        }

        /// <summary>CombatAudio.Play2DRoute: HUD clicks (OpenZHud.ClickSound = "ui_click") and the rally flag click (OpenZRallyPoints.cs:87).</summary>
        bool Route2D(string name, float vol)
        {
            if (name == null) return false;
            if (name == "v3-capture-raise") { UI("ui_rally_set"); return true; }
            var d = Def(Canon(name));
            if (d != null && d.bus == "UI") { UI(name); return true; }
            return false;
        }

        // ---- Alan alert rules (zplayer_events.cpp:802-818, zhud.cpp:574-592) + base under attack + low health
        float lastHurt = -100f, nextPushCheck, nextLosingCheck, nextHealthCheck;
        readonly HashSet<int> lowHealth = new HashSet<int>();
        void UnderAttack(ZObject attacker, ZObject victim, bool hit)
        {
            if (attacker == null || victim == null) return;
            if (victim.Owner != Player || attacker.Owner == Player || attacker.Owner == Team.Neutral) return;
            if (victim.Kind == ZKind.Building)
            {
                if (!victim.IsFort && (victim.IsProducer || victim.Type == "radar" || victim.Type == "repair")) Announce("ann_base_under_attack");
                NotePressure(victim, victim, attacker);                                     // help_cries: heavy-fire tracker
                Distress(victim, victim);                                                   // a robot nearby shouts too (never over Rachel)
                return;
            }
            var v = victim.Leader ?? victim;
            float now = Time.time;
            NotePressure(v, victim, attacker);                                              // help_cries: heavy-fire tracker
            Distress(v, victim);
            if (hit && victim.Kind == ZKind.Robot && victim.MaxHealth > 0 && victim.Health < victim.MaxHealth / 2 && now - lastHurt > 3f && UnityEngine.Random.value < 0.25f)
            {   // help_cries: a yelp the player sees stays local; one from off screen is radio chatter (rare, never during an episode)
                if (OnScreen(PosOf(victim))) { lastHurt = now; Say("vo_hurt"); }
                else if (offscreenHurtChatter && distress == null) HurtChatter();
            }
        }

        void UpdateVoiceRules()
        {
            float now = Time.time;
            UpdateDistress();
            if (now > nextLosingCheck) { nextLosingCheck = now + 1f; LosingCheck(); }
            if (now > nextPushCheck) { nextPushCheck = now + 5f; PushCheck(); }
            if (now > nextHealthCheck) { nextHealthCheck = now + 0.5f; HealthCheck(); }
            if (now > nextCapCheck) { nextCapCheck = now + 0.5f; CapCheck(); }
            int fortFired = FortAlert.Report(danger == Danger.Fort);
            if (fortFired != fortAnnSeen) { fortAnnSeen = fortFired; if (!FlagLive) { UI("ui_alert"); Announce("ann_fort_under_attack"); } else UI("ui_fort_alarm"); }   // sound_v3: the 2D alarm with FlagView live
        }
        float nextFortAnn, lastLosing = -100f, lastPush = -100f; int fortAnnSeen;
        /// <summary>
        /// One gate for Rachel's losing lines
        /// (ann_youre_losing; Zod repeats them every 8 s, zplayer.cpp:1137) and Alan's push lines (vo_push). Deviation (audio only).</summary>
        public const float StatusLineGap = 60f;
        /// <summary>
        /// No losing line (ann_youre_losing) and no push line (vo_push: "Let's finish 'em off!") before 7 min of sim time.</summary>
        public static float StatusLineEarliest = 420f;
        public static int StatusGated;
        float lastStatusLine = -1000f;

        //      Radio discipline for every Alan line that comes from a fight the player is not watching:
        //      * one distress episode at a time: a player group (or building) hit by the enemy while the camera is not on it and the
        //        group is not simply handling its own fight (firing back under light fire). Its first call comes ~1.2 s after the
        //        first hit: a cry for help (vo_under_attack_repeat) under heavy fire (>= 12 attacks or >= 3 attackers in 8 s, a
        //        member under half health or lost) or 35 % of the time, else the plain alert (vo_under_attack). A later call only
        //        while the fire stays heavy and nobody came: always a cry ("I said we're under attack!" only as a follow-up). At
        //        most 3 calls; the episode ends when the hits stop for 6 s, the camera comes, the group gets an order or is gone.
        //        12 s between episodes, 30 s (60 s after 3 calls) before the same group calls again.
        //      * RadioDiscipline (shared, outlives a level restart): after an alert / cry the radio stays shut 30-60 s (random),
        //        never more than 2 radio lines in any 60 s, >= 8 s between two; only into silence: no Alan, Rachel (playing or
        //        queued), Commander Zod (ExternalVoiceBusy / RegisterExternalVoice / any zod_* clip) or other voice line, 0.8 s
        //        after the last one ended; never while paused or during the end sequence.
        //      * a shuffle bag of LINES per event: no line repeats until every line was used (the take of the line is random).
        //      * vo_hurt of a unit off screen is chatter on the same radio: only without an episode, after a minute of radio silence.
        [Header("Radio discipline (help_cries)")]
        [Tooltip("Seconds the radio stays shut after an alert / cry for help (random between x and y)")] public Vector2 radioGap = new Vector2(30f, 60f);
        [Tooltip("Most radio lines (alerts, cries, off-screen hurt chatter) in any 60 s")] public int radioPerMinute = 2;
        [Tooltip("Chance that the first call of an episode is a cry for help although the fire is still light")] [Range(0f, 1f)] public float cryChanceLight = 0.35f;
        [Tooltip("Off-screen vo_hurt as rare radio chatter (off: only units on screen yelp)")] public bool offscreenHurtChatter = true;

        sealed class DistressEp { public int id; public Vector3 pos; public float first, lastHit, next; public int level; public bool building; }
        DistressEp distress; float distressCalmUntil, lastVoiceBusy = -100f; readonly Dictionary<int, float> distressQuiet = new Dictionary<int, float>();
        const int MaxEpisodeCalls = 3;
        /// <summary>Other voices (Commander Zod cameo) can report that they are speaking; distress shouts wait for them.</summary>
        public static System.Func<bool> ExternalVoiceBusy;
        bool AnyVoiceBusy => AlanBusy || (rachel.busy && rachel.src.isPlaying) || (ExternalVoiceBusy != null && ExternalVoiceBusy());
        static RadioDiscipline Radio => RadioDiscipline.Shared;
        void SyncRadio() { var r = Radio; r.GapMin = radioGap.x; r.GapMax = radioGap.y; r.PerMinute = Mathf.Max(1, radioPerMinute); }

        /// <summary>The player is looking at it (4 % inside the view).</summary>
        bool Watching(Vector3 at)
        {
            if (!cam) return false;
            var vp = cam.WorldToViewportPoint(at);
            return vp.z > 0 && vp.x > 0.04f && vp.x < 0.96f && vp.y > 0.04f && vp.y < 0.96f;
        }

        // ---- heavy-fire tracker: the attacks on each player group (or building) over the last 8 s
        sealed class Pressure { public readonly Queue<(float t, int by)> hits = new Queue<(float t, int by)>(); public float lastHurt = -100f, lastLoss = -100f, lastSeen; }
        readonly Dictionary<int, Pressure> pressure = new Dictionary<int, Pressure>();
        readonly HashSet<int> attackersScratch = new HashSet<int>();
        float nextPressureSweep;

        void NotePressure(ZObject group, ZObject victim, ZObject attacker)
        {
            float now = Time.unscaledTime;
            if (!pressure.TryGetValue(group.Id, out var p)) pressure[group.Id] = p = new Pressure();
            p.hits.Enqueue((now, attacker.Id)); p.lastSeen = now;
            while (p.hits.Count > 0 && (now - p.hits.Peek().t > 8f || p.hits.Count > 48)) p.hits.Dequeue();
            if (victim.MaxHealth > 0 && victim.Health * 2 < victim.MaxHealth) p.lastHurt = now;
            if (now > nextPressureSweep)
            {
                nextPressureSweep = now + 5f;
                var old = pressure.Where(kv => now - kv.Value.lastSeen > 12f).Select(kv => kv.Key).ToList();
                foreach (var k in old) pressure.Remove(k);
            }
        }

        /// <summary>A player unit died: its group is losing people (heavy fire for the next 10 s).</summary>
        void NoteLoss(ZObject o)
        {
            if (o.Owner != Player || !o.IsUnit) return;
            if (pressure.TryGetValue((o.Leader ?? o).Id, out var p) || pressure.TryGetValue(o.Id, out p)) p.lastLoss = Time.unscaledTime;
        }

        bool Heavy(int id)
        {
            if (!pressure.TryGetValue(id, out var p)) return false;
            float now = Time.unscaledTime;
            while (p.hits.Count > 0 && now - p.hits.Peek().t > 8f) p.hits.Dequeue();
            if (p.hits.Count == 0) return false;                                                 // nobody is shooting at them any more
            if (now - p.lastLoss < 10f || now - p.lastHurt < 8f || p.hits.Count >= 12) return true;
            attackersScratch.Clear();
            foreach (var h in p.hits) if (attackersScratch.Add(h.by) && attackersScratch.Count >= 3) return true;
            return false;
        }

        /// <summary>help_cries: a voice player outside the director (the Commander Zod cameo, a cutscene) registers its AudioSource;
        /// the radio never talks over it. Unregistered sources playing a zod_ / rachel_ / alan_ clip are found by a scan (2 Hz,
        /// only while a call is waiting to go out).</summary>
        public static void RegisterExternalVoice(AudioSource s) { if (s && !externalVoices.Contains(s)) externalVoices.Add(s); }
        static readonly List<AudioSource> externalVoices = new List<AudioSource>();
        float nextVoiceScan; bool voiceScanBusy;
        bool OtherVoicePlaying(float now)
        {
            externalVoices.RemoveAll(s => !s);
            foreach (var s in externalVoices) if (s.isPlaying) return true;
            if (now < nextVoiceScan) return voiceScanBusy;
            nextVoiceScan = now + 0.5f; voiceScanBusy = false;
            foreach (var s in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            {
                if (!s || !s.isPlaying || !s.clip || s == alanMain.src || s == alanAlert.src || s == rachel.src) continue;
                var n = s.clip.name;
                if (n.StartsWith("zod_", StringComparison.OrdinalIgnoreCase) || n.StartsWith("rachel_", StringComparison.OrdinalIgnoreCase)
                    || (n.StartsWith("alan_", StringComparison.OrdinalIgnoreCase) && !DeathClip(n))) { voiceScanBusy = true; break; }
            }
            return voiceScanBusy;
        }

        static bool DeathClip(string n) => n.StartsWith("alan_death_", StringComparison.OrdinalIgnoreCase) || n.StartsWith("alan_hit_", StringComparison.OrdinalIgnoreCase);   // death_voices_v1 review
        /// <summary>Radio only into silence: no voice playing or queued, 0.8 s after the last one, not paused, not in the end sequence.</summary>
        bool RadioQuiet(float now)
        {
            if (AudioListener.pause || Time.timeScale <= 0f || OpenZVictory.EndSequenceRunning || gameOver) return false;
            if (AnyVoiceBusy || rachelQueue.Count > 0) { lastVoiceBusy = now; return false; }
            if (now - lastVoiceBusy < 0.8f) return false;
            if (OtherVoicePlaying(now)) { lastVoiceBusy = now; return false; }
            return true;
        }

        void Distress(ZObject group, ZObject victim)
        {
            float now = Time.unscaledTime;
            var at = game.CenterWorld(victim);
            if (distress != null && distress.id == group.Id) { distress.lastHit = now; distress.pos = at; return; }
            if (distress != null && now - distress.lastHit < 6f) return;                         // one episode at a time
            if (now < distressCalmUntil || (distressQuiet.TryGetValue(group.Id, out var q) && now < q)) return;
            bool building = group.Kind == ZKind.Building;
            if (Watching(at)) return;                                                             // the player already watches this fight
            if (!building && group.AttackObject != null && !Heavy(group.Id)) return;              // firing back under light fire: no call
            distress = new DistressEp { id = group.Id, pos = at, first = now, lastHit = now, next = now + 1.2f, building = building };
        }

        void UpdateDistress()
        {
            float now = Time.unscaledTime;
            if (AnyVoiceBusy || rachelQueue.Count > 0) lastVoiceBusy = now;
            var d = distress; if (d == null) return;
            var u = W.GetAny(d.id);
            bool ordered = input && input.MarkerTime > d.first && input.Selection.Any(s => s == u || s.Leader == u);
            bool over = u == null || u.Destroyed || now - d.lastHit > 6f || ordered || Watching(d.pos);
            if (over || d.level >= MaxEpisodeCalls)
            {
                distressQuiet[d.id] = now + (d.level >= MaxEpisodeCalls ? 60f : 30f); distressCalmUntil = now + 12f; distress = null; return;
            }
            if (now < d.next) return;
            d.next = now + 0.5f;                                                                  // waiting for the radio: look twice a second
            bool heavy = Heavy(d.id);
            if (d.level > 0 && !heavy) return;                                                   // a follow-up only while the fire stays heavy
            SyncRadio();
            if (!Radio.CanCall(now) || !RadioQuiet(now)) return;
            bool cry = d.level > 0 || heavy || UnityEngine.Random.value < cryChanceLight;
            string id = cry && HasClips("vo_under_attack_repeat") ? "vo_under_attack_repeat" : "vo_under_attack";
            var def = Def(id);
            string line = def != null ? Radio.Peek(id, def.lines, d.level > 0) : null;
            if (line == null) return;
            float before = lastLine.TryGetValue(id, out var lb) ? lb : -1f;
            lastLine.Remove(id);                                                                 // the radio paces itself, not the event cooldown
            Say(id, "=" + line);
            if (lastLine.TryGetValue(id, out var la) && la > before)
            {
                if (d.level == 0) UI("ui_alert");
                Radio.Played(id, line); Radio.Called(now, true);
                DistressPlays++; if (id == "vo_under_attack_repeat") HelpCries++;
                if (!d.building) NoteHelpCall(d.id);
                d.level++; d.next = now + 1f;
            }
            else if (before >= 0f) lastLine[id] = before;                                        // refused (busy): retry in 0.5 s
        }

        /// <summary>An off-screen hurt yelp as radio chatter: only after a whole minute of radio silence (then counts like any line).</summary>
        void HurtChatter()
        {
            float now = Time.unscaledTime;
            SyncRadio();
            if (!Radio.CanChatter(now) || !RadioQuiet(now)) return;
            float before = lastLine.TryGetValue("vo_hurt", out var lb) ? lb : -1f;
            Say("vo_hurt");
            if (lastLine.TryGetValue("vo_hurt", out var la) && la > before) { lastHurt = Time.time; Radio.Called(now, false); }
        }
        /// <summary>Distress shouts played this session (parity sound check).</summary>
        public int DistressPlays { get; private set; }
        /// <summary>help_cries: cries for help (vo_under_attack_repeat) played this session.</summary>
        public int HelpCries { get; private set; }
        /// <summary>
        /// The
        /// last player group whose distress call (vo_under_attack / vo_under_attack_repeat) went out on the radio, and when (unscaled s).</summary>
        public static int HelpCallId { get; private set; } = -1;
        public static float HelpCallAt { get; private set; } = -100f;
        public static int HelpCallCount { get; private set; }
        /// <summary>NB35: a call for help from group `id` went out now (UpdateDistress; probes may call it to stage one).</summary>
        public static void NoteHelpCall(int id) { HelpCallId = id; HelpCallAt = Time.unscaledTime; HelpCallCount++; }

        //      constants.h:21): "Unit limit reached." + "Production on hold." when the player's team hits the cap while producing,
        //      "Production resumed." when it drops below again. A state must hold 1.5 s and cap lines are >= 15 s apart, so a unit
        //      dying at the cap and the next one rolling out never flip-flop the announcer.
        float nextCapCheck, capSince, capLineAt = -100f; bool capOn, capSaid;
        void CapCheck()
        {
            bool building = false;
            foreach (var o in W.Objects) if (o.IsProducer && o.Owner == Player && !o.Destroyed && o.Building) { building = true; break; }
            bool cap = building && W.UnitLimitReached(Player);
            float now = Time.time;
            if (cap != capOn) { capOn = cap; capSince = now; }
            if (gameOver || cap == capSaid || now - capSince < 1.5f || now - capLineAt < 15f) return;
            capSaid = cap; capLineAt = now;
            if (cap) { Announce("ann_unit_limit"); Announce("ann_production_paused"); }
            else if (building) Announce("ann_production_resumed");
        }

        void HealthCheck()
        {   // polish vo_low_health: a player unit under 25 % (once per unit until it is back over 50 %)
            foreach (var o in W.Objects)
            {
                if (!o.IsUnit || o.Owner != Player || o.MaxHealth <= 0) continue;
                if (o.Destroyed) { lowHealth.Remove(o.Id); continue; }
                float k = (float)o.Health / o.MaxHealth;
                if (k >= 0.5f) lowHealth.Remove(o.Id);
                else if (k < 0.25f && lowHealth.Add(o.Id) && OnScreen(PosOf(o), 0.2f)) Say("vo_low_health");
            }
        }

        void LosingCheck()
        {   // zplayer.cpp:1098-1141: weakest enemy has > 1.7x our units AND > 1.7x our flag fraction; then 8 s pause
            if (Time.time - lastLosing < 8f || Time.time - lastStatusLine < StatusLineGap || gameOver) return;
            if (W.Time < StatusLineEarliest) { StatusGated++; return; }
            var units = new Dictionary<Team, int>(); var flags = new Dictionary<Team, int>();
            foreach (var o in W.Objects) if (o.IsUnit && !o.Destroyed && o.Owner != Team.Neutral) units[o.Owner] = (units.TryGetValue(o.Owner, out var n) ? n : 0) + 1;
            foreach (var f in W.Flags) if (f.Owner != Team.Neutral) flags[f.Owner] = (flags.TryGetValue(f.Owner, out var n) ? n : 0) + 1;
            units.TryGetValue(Player, out var ours); flags.TryGetValue(Player, out var ourFlags);
            if (ours == 0) return;
            int weakU = int.MaxValue, weakF = int.MaxValue;
            foreach (var kv in units) if (kv.Key != Player) { weakU = Mathf.Min(weakU, kv.Value); weakF = Mathf.Min(weakF, flags.TryGetValue(kv.Key, out var ff) ? ff : 0); }
            if (weakU == int.MaxValue) return;
            if (weakU > 1.7f * ours && weakF > 1.7f * ourFlags) { lastLosing = lastStatusLine = Time.time; Announce("ann_youre_losing"); }
        }

        void PushCheck()
        {
            if (Time.time - lastPush < 20f || Time.time - lastStatusLine < StatusLineGap || gameOver) return;
            if (W.Time < StatusLineEarliest) { StatusGated++; return; }
            var enemyFort = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner != Player && o.Owner != Team.Neutral && !o.Destroyed);
            int ours = 0, theirs = 0; bool close = false;
            foreach (var o in W.Objects)
            {
                if (!o.IsUnit || o.Destroyed || o.Owner == Team.Neutral) continue;
                if (o.Owner == Player) { ours++; if (enemyFort != null && o.IsMobile && Near(o, enemyFort, 250)) close = true; }
                else theirs++;
            }
            if (ours > 0 && (close || theirs < 0.3f * ours)) { lastPush = lastStatusLine = Time.time; Say("vo_push"); finalPush = close; }
        }

        static bool Near(ZObject a, ZObject b, int px)
        {
            if (a == null || b == null) return false;
            double dx = a.CenterX - b.CenterX, dy = a.CenterY - b.CenterY; return dx * dx + dy * dy <= (double)px * px;
        }

        // ============================================================================================================================
        // Polled input (PlayerInput public state) -> UI sounds + selection / order voices
        // ============================================================================================================================
        float lastSelectTime = -10f, lastMarkerTime = -10f; Vector3 lastMarkerPos; bool lastPaused; float lastSpeed = -1f; ZObject lastPlacing;
        void PollInput()
        {
            if (!input) input = FindFirstObjectByType<PlayerInput>();
            if (input)
            {
                bool recall = RecallKeyThisFrame();
                if (input.SelectTime != lastSelectTime && input.SelectTime > 0f)
                {
                    lastSelectTime = input.SelectTime;
                    string v3cue = V3SelectCue(recall, out bool v3voice);                                        // sound_v3: deselect / select-all / mouse recall
                    if (input.Selection.Count > 0) UI(v3cue);       // audio_v2: a recalled group gets its own cue; an empty result (a group whose units all died, Ctrl+R with no robots) stays silent
                    if (v3voice) SelectionVoice();
                }
                // audio_v2: deselect tick = a left click in the world that ends with an empty selection after a non-empty one
                var ms = Mouse.current;
                if (ms != null)
                {
                    if (ms.leftButton.wasPressedThisFrame) selBeforePress = prevSelCount;
                    if (ms.leftButton.wasReleasedThisFrame)
                    {
                        if (selBeforePress > 0 && input.Selection.Count == 0 && input.Placing == null) UI("ui_deselect");
                        selBeforePress = 0;
                    }
                }
                prevSelCount = input.Selection.Count;
                if (input.MarkerTime != lastMarkerTime && input.MarkerTime > 0f)
                {
                    lastMarkerTime = input.MarkerTime; lastMarkerPos = input.MarkerPos;
                    bool attack = input.Selection.Any(u => u.Waypoints.Count > 0 && u.Waypoints[u.Waypoints.Count - 1].mode == WpMode.Attack)
                                  || (input.Hover != null && input.Hover.Owner != Player && input.Hover.Owner != Team.Neutral && input.Hover.IsUnit);
                    if (invalidFrame != Time.frameCount)
                    {
                        UI(V3OrderCue(attack));                                                                   // sound_v3: capture / enter / pickup / repair / exit cues
                        ElectroVoices.Ordered(input.Selection);                                                // electrobot_v1: his move / attack pools (Say routes this frame's classic line)
                        NoteWalkOrder(input.Selection);
                        if (input.Selection.Count > 0 && !V3EjectOrder())                                        // sound_v3: OnEjected says the exit line
                        {   // unit_voices_v1: a per-type move / attack / repair line (share unitOrderShare), else the classic line; "No way!" stays classic
                            bool noWay = attack && NoWayOrder();
                            if (!UvOrder(attack, noWay)) ClassicOrderLine(attack, noWay);                       // the shared pool of the answering unit's voice
                        }
                        if (input.Selection.Count > 0) EngineRev.Ordered(input.Selection);
                    }
                    else UvRefused(attack);                                                                 // unit_voices_v1: a cannon asked to move / a crane asked to attack
                }
                else if (input.MarkerPos != lastMarkerPos && input.Selection.Count > 0)
                { lastMarkerPos = input.MarkerPos; UI("ui_order_queue", 1f, 1.12f); }                  // Shift-queued waypoint (+2 semitones)
                if (input.Placing != lastPlacing) { if (input.Placing != null) UI("ui_cannon_place_arm"); lastPlacing = input.Placing; }
            }
            var kb = Keyboard.current;
            if (kb != null)
            {
                bool ctrl = kb.ctrlKey.isPressed;
                for (int d = 1; d <= 9; d++)
                    if (kb[(Key)((int)Key.Digit1 + d - 1)].wasPressedThisFrame)
                    {
                        if (ctrl) { if (input && input.Selection.Count > 0) UI("ui_group_store"); }
                        // audio_v2: the recall cue plays with the selection change (above), never for an empty group
                        break;
                    }
                HudKeyTick();                                                                        // menu_exe_v1: ui_hud_toggle on the bound HIDE THE HUD key (AudioDirector.MenuExe.cs), was F10 only
            }
            if (game.paused != lastPaused)
            {
                lastPaused = game.paused; UI(game.paused ? "ui_pause" : "ui_resume"); SetPaused(game.paused);
                if (!gameOver) Announce(game.paused ? "ann_game_paused" : "ann_game_resumed");
            }
            if (lastSpeed >= 0f && !Mathf.Approximately(lastSpeed, game.gameSpeed)) UI("ui_game_speed");
            lastSpeed = game.gameSpeed;
        }

        int prevSelCount, selBeforePress;
        /// <summary>audio_v2: a group key (1-9, 0) pressed this frame without Ctrl.</summary>
        static bool RecallKeyThisFrame()
        {
            var kb = Keyboard.current; if (kb == null || kb.ctrlKey.isPressed) return false;
            for (int d = 0; d < 10; d++) if (kb[(Key)((int)Key.Digit1 + d)].wasPressedThisFrame) return true;   // Digit1..Digit9, then Digit0
            return false;
        }

        static readonly Dictionary<string, string> Plural = new Dictionary<string, string>
        { { "grunt", "Grunts" }, { "psycho", "Psychos" }, { "sniper", "Snipers" }, { "tough", "Toughs" }, { "laser", "Lasers" }, { "pyro", "Pyros" } };

        void SelectionVoice()
        {   // Zod ZObject::PlaySelectedAnim: HUD unit = random member; robots 50 % generic / 50 % '<Type>s ...', vehicles + cannons generic
            if (input.Selection.Count == 0) return;
            var u = UvSelect();                                                   // unit_voices_v1: per-type lines + running gag (null = it spoke / spam stays silent)
            if (u == null) return;
            if (u.Kind == ZKind.Robot && UnityEngine.Random.value < 0.5f && Plural.TryGetValue(u.Type, out var pl)) Say("vo_select_type", pl);
            else if (!ElectroVoices.Select(u)) Say(ClassicSelectEvent(u.Type));  // electrobot_v1: his own lines + running gag when unit_voices_v1 does not own them; the shared pool of the unit's voice
        }

        //      -> the portrait refuses ("No way!", "Forget it!", "Get outta here!"; zobject.cpp:417-426) instead of acknowledging.
        //      zunitrating.cpp:57-128, the UCR_WILL_DIE rows (attacker -> victim types); every other pair is even or a win.
        static readonly Dictionary<string, HashSet<string>> WillDie = new Dictionary<string, HashSet<string>>
        {
            ["grunt"] = new HashSet<string> { "psycho", "sniper", "tough", "pyro", "laser", "electrobot", "gatling", "gun", "howitzer", "missile_cannon", "jeep", "light", "medium", "heavy", "missile_launcher" },
            ["psycho"] = new HashSet<string> { "tough", "pyro", "laser", "electrobot", "missile_cannon", "medium", "heavy", "missile_launcher" },
            ["sniper"] = new HashSet<string> { "tough", "pyro", "laser", "electrobot", "missile_cannon", "missile_launcher" },
            ["tough"] = new HashSet<string> { "missile_cannon", "missile_launcher" },
            ["electrobot"] = new HashSet<string> { "missile_cannon", "missile_launcher" },   // electrobot_v1: the Tough's row (same health)
            ["pyro"] = new HashSet<string> { "missile_cannon", "missile_launcher" },
            ["laser"] = new HashSet<string> { "missile_cannon", "missile_launcher" },
            ["jeep"] = new HashSet<string> { "tough", "pyro", "laser", "electrobot", "gun", "howitzer", "missile_cannon", "light", "medium", "heavy", "missile_launcher" },
            ["light"] = new HashSet<string> { "missile_cannon", "missile_launcher" },
            ["medium"] = new HashSet<string> { "missile_launcher" },
            ["missile_launcher"] = new HashSet<string> { "howitzer" },
        };
        bool NoWayOrder()
        {
            if (input.Selection.Count != 1) return false;
            var u = input.Selection[0];
            if (u.Waypoints.Count == 0 || !WillDie.TryGetValue(u.Type ?? "", out var loses)) return false;
            var w = u.Waypoints[u.Waypoints.Count - 1];                                    // the order just sent (a single right-click = Zod's dev list [0])
            if (w.mode != WpMode.Attack) return false;
            var v = W.Get(w.refId);
            return v != null && loses.Contains(v.Type ?? "");
        }

        float pauseMusicDb;
        void SetPaused(bool p)
        {
            AudioListener.pause = p;                                           // UI, music and voices ignore the listener pause
            if (musicA != null) { musicA.lp.enabled = p; musicA.lp.cutoffFrequency = 900f; musicB.lp.enabled = p; musicB.lp.cutoffFrequency = 900f; }
            pauseMusicDb = p ? -4f : 0f;
        }

        // ============================================================================================================================
        // Vehicles: engines are LegacyEngineAudio's; here the tank lids (Zod: open while engaging, lets the driver be sniped)
        // ============================================================================================================================
        readonly Dictionary<int, bool> lids = new Dictionary<int, bool>();
        /// <summary></summary>
        public void LidSound(bool open, Vector3 pos, int id, float vol = 1f) { if (vehicleFoley) Play(open ? "veh_lid_open" : "veh_lid_close", pos, id, vol); }
        void UpdateVehicles()
        {
            foreach (var kv in game.Views)
            {
                var v = kv.Value; if (!v || !v.isActiveAndEnabled || v.Obj == null || v.Obj.Kind != ZKind.Vehicle || !v.Obj.HasLid) continue;
                if (BoardingView.LidVoiced.Contains(v.Obj.Id)) { lids.Remove(v.Obj.Id); continue; }
                bool open = v.Obj.LidOpen;
                if (lids.TryGetValue(v.Obj.Id, out var was) && was != open && !v.Dying) Play(open ? "veh_lid_open" : "veh_lid_close", v.transform.position, v.Obj.Id);
                lids[v.Obj.Id] = open;
            }
        }

        // ---- audio_v2: tracks / tyres under the moving crewed vehicles nearest the camera focus (on screen, 3 at most). The mech (medium
        //      tank) walks (LegacyEngineAudio's metal steps). Engines stay LegacyEngineAudio's.
        [Tooltip("audio_v2: tracks / tyre loops under the 3 nearest moving crewed vehicles on screen")] public bool vehicleSurfaceLoops = true;
        float nextSurf;
        void UpdateVehicleSurface()
        {
            if (!vehicleSurfaceLoops || Time.time < nextSurf) return;
            nextSurf = Time.time + 0.25f;
            var lp = ListenerPos; var keep = new HashSet<string>();
            var near = game.Views.Values.Where(v => v && v.isActiveAndEnabled && !v.Dying && v.Obj != null && v.Obj.Kind == ZKind.Vehicle && v.Obj.Owner != Team.Neutral
                                                    && v.Obj.IsMoving && SurfaceEvent(v.Obj.Type) != null && OnScreen(v.transform.position, 0.05f))
                                        .OrderBy(v => (v.transform.position - lp).sqrMagnitude).Take(3).ToList();
            foreach (var v in near)
            {
                string ev = SurfaceEvent(v.Obj.Type);
                if (ev == "veh_wheels_sand" && (City || ClassAt(v.transform.position) == ZClass.Road)) ev = "veh_wheels_asphalt";
                else if (ev == "veh_wheels_sand" && Jungle)
                {   // level 16: wheels on grass, in the mud beside a pool, in water they ford
                    string jg = JungleGround(v.transform.position);
                    string want = jg == "water" ? "veh_wheels_water" : jg == "mud" ? "veh_wheels_mud" : "veh_wheels_grass";
                    if (HasClips(want)) ev = want;
                }
                string key = "surf:" + v.Obj.Id; keep.Add(key);
                if (Jungle && LoopActive(key) && surfEv.TryGetValue(key, out var surfWas) && surfWas != ev) StopLoop(key, 0.25f);   // the ground changed under it: the next look starts the other loop
                else if (!LoopActive(key)) { StartLoop(key, ev, v.transform.position, v.transform); surfEv[key] = ev; }
            }
            foreach (var k in loops.Keys.ToList()) if (k.StartsWith("surf:") && !keep.Contains(k)) StopLoop(k, 0.35f);
        }
        readonly Dictionary<string, string> surfEv = new Dictionary<string, string>();
        static string SurfaceEvent(string type)
        {
            switch (type)
            {
                case "heavy": return I && I.HasClips("veh_tracks_heavy_loop") ? "veh_tracks_heavy_loop" : "veh_tracks_loop";   // sound_v3: its own heavier tread loop
                case "light": case "missile_launcher": return "veh_tracks_loop";
                case "jeep": case "apc": case "crane": return "veh_wheels_sand";
                default: return null;
            }
        }

        /// <summary>Coverage for the loops CombatAudio's LegacyEngineAudio plays: the 4 crewed vehicles nearest the camera focus
        /// (its own rule) while it has active loops; the medium tank (mech) = its footsteps (CombatAudio "mech_step").</summary>
        float nextLoopPoll;
        void PollCombatLoops()
        {
            if (Time.time < nextLoopPoll) return;
            nextLoopPoll = Time.time + 0.5f;
            var eng = LegacyEngineAudio.I; if (!eng || eng.ActiveLoops == 0 || !rts) return;
            var f = rts.focus;
            var near = game.Views.Values.Where(v => v && v.isActiveAndEnabled && !v.Dying && v.Obj != null && v.Obj.Kind == ZKind.Vehicle && v.Obj.Owner != Team.Neutral
                                                     && LegacyMatch.EngineLoop(v.Obj) != null)
                                         .OrderBy(v => (new Vector2(v.transform.position.x - f.x, v.transform.position.z - f.z)).sqrMagnitude).Take(eng.maxAudible);
            foreach (var v in near)
            {
                string ev = EngineEventOf(v.Obj.Type);
                if (ev != null) { Count(Triggers, ev); Count(Plays, ev); }
            }
        }
        static string EngineEventOf(string type)
        {
            switch (type)
            {
                case "jeep": return "veh_engine_jeep"; case "light": return "veh_engine_light"; case "heavy": return "veh_engine_heavy";
                case "apc": return "veh_engine_apc"; case "missile_launcher": return "veh_engine_mlrs"; case "crane": return "veh_engine_crane";
                default: return null;
            }
        }

        // ---- wrecks and burning buildings (nearest N fire loops) + creaks + crane / repair work
        readonly Dictionary<int, (Vector3 p, float t)> wrecks = new Dictionary<int, (Vector3, float)>(), burning = new Dictionary<int, (Vector3, float)>();
        readonly Dictionary<int, (Vector3 p, float next)> craneWork = new Dictionary<int, (Vector3, float)>();
        float nextFireSort, nextCreak;
        void UpdateWrecks()
        {
            float now = Time.time;
            foreach (var k in craneWork.Keys.ToList())
            {
                var c = craneWork[k];
                if (now >= c.next) { Play("veh_crane_work", c.p, k); craneWork[k] = (c.p, now + UnityEngine.Random.Range(1.8f, 3f)); }
            }
            if (now < nextFireSort) return;
            nextFireSort = now + 0.5f;
            var lp = ListenerPos;
            float wreckFire = WreckFx.SmoulderSeconds;
            var all = wrecks.Where(x => now - x.Value.t < wreckFire).Concat(burning.Where(x => now - x.Value.t < 30f))
                            .OrderBy(x => (x.Value.p - lp).sqrMagnitude).Take(maxFireLoops).Select(x => x.Key).ToHashSet();
            foreach (var x in wrecks.Concat(burning))
            {
                string key = "fire:" + x.Key;
                if (all.Contains(x.Key))
                {
                    if (!LoopActive(key)) { V3FireStart(x.Key, x.Value.p, x.Value.t); StartLoop(key, "dst_fire_loop", x.Value.p); }   // sound_v3: the flare-up
                    if (wrecks.ContainsKey(x.Key)) { float age = now - x.Value.t; SetLoop(key, age < 7f ? 1f : Mathf.Lerp(1f, 0.35f, (age - 7f) / Mathf.Max(1f, wreckFire - 7f)), 1f); }   // Zod's 5-7 s burn, then the smoulder
                }
                else StopLoop(key, 1.5f);
            }
            if (now > nextCreak && all.Count > 0 && rts && rts.height < footstepMaxHeight)                 // audio_v2: was 60 m
            {
                nextCreak = now + UnityEngine.Random.Range(4f, 9f);
                int id = all.ElementAt(UnityEngine.Random.Range(0, all.Count));
                var p = wrecks.TryGetValue(id, out var w) ? w.p : burning[id].p;
                Play("dst_wreck_creak", p, id);
            }
        }

        // ---- Zod zplayer.cpp:1144-1194: one shared factory loop while an owned producing factory / fort is on screen (FactoryViews'
        //      hum events take over while it runs); the radar loop + ping likewise
        float nextBld, nextPing;
        void UpdateBuildingLoops()
        {
            if (Time.time < nextBld || !cam) return;
            nextBld = Time.time + 0.5f;
            ZObject hum = null, radar = null; float hd = float.MaxValue, rd = float.MaxValue; var lp = ListenerPos;
            foreach (var o in W.Objects)
            {
                if (o.Kind != ZKind.Building || o.Owner != Player || o.Destroyed) continue;
                bool prodOn = o.IsProducer && o.Building, isRadar = o.Type == "radar";
                if (!prodOn && !isRadar) continue;
                var p = game.CenterWorld(o); var vp = cam.WorldToViewportPoint(p);
                if (vp.z < 0 || vp.x < -0.1f || vp.x > 1.1f || vp.y < -0.1f || vp.y > 1.1f) continue;
                float d2 = (p - lp).sqrMagnitude;
                if (prodOn && d2 < hd) { hd = d2; hum = o; }
                if (isRadar && d2 < rd) { rd = d2; radar = o; }
            }
            // activated or starts / changes production gets a short fan spin-up; the rest of the time it is quiet.
            foreach (var o in W.Objects)
            {
                if (o.Kind != ZKind.Building || (o.Type != "robot_factory" && o.Type != "vehicle_factory")) continue;
                string key = o.Destroyed || o.Owner != Player || !o.Building ? null : o.Owner + ":" + o.CurUnit;   // the player's factories only (audio_v2 audit: CPU switches spammed)
                bool seen = prodState.TryGetValue(o.Id, out var was);
                prodState[o.Id] = key;
                if (!seen || key == null || key == was || !prodScanned) continue;
                var fp = game.CenterWorld(o); var fvp = cam.WorldToViewportPoint(fp);
                if (fvp.z < 0 || fvp.x < -0.1f || fvp.x > 1.1f || fvp.y < -0.1f || fvp.y > 1.1f) continue;
                if (fanLast.TryGetValue(o.Id, out var fl) && Time.time - fl < fanCooldown) continue;          // audio_v2: at most every 20 s per factory
                fanLast[o.Id] = Time.time;
                FanSpinUp(o, fp);
            }
            prodScanned = true;
            if (radar != null)
            {
                var rp = game.CenterWorld(radar) + Vector3.up * 3f;
                if (!LoopActive("radar")) StartLoop("radar", "bld_radar_loop", rp);
                SetLoop("radar", 1f, 1f, rp);
                if (Time.time > nextPing) { nextPing = Time.time + 4f; Play("bld_radar_ping", rp, radar.Id); }
            }
            else StopLoop("radar", 0.6f);
        }

        // ---- HUD polling: hover and the production window
        HudButton lastHot, hotTicked; float hotTickedAt = -10f; OpenZProductionWindow prodWin; bool lastProdOpen; float nextHudFind;
        void PollHud()
        {
            var hud = OpenZHud.I;
            if (hud)
            {
                var hot = hud.HotButton;
                if (hot != null && !ReferenceEquals(hot, lastHot) && !(ReferenceEquals(hot, hotTicked) && Time.unscaledTime - hotTickedAt < 1f)) UI("ui_hover");   // OpenZHud.HotButton = button under the pointer
                if (hot != null) { hotTicked = hot; hotTickedAt = Time.unscaledTime; }
                lastHot = hot;
            }
            if (!prodWin && Time.unscaledTime >= nextHudFind) { nextHudFind = Time.unscaledTime + 2f; prodWin = FindFirstObjectByType<OpenZProductionWindow>(); if (prodWin) lastProdOpen = prodWin.IsOpen; }
            if (prodWin)
            {
                bool open = prodWin.IsOpen;
                if (open != lastProdOpen) UI(open ? "ui_panel_open" : "ui_panel_close");
                lastProdOpen = open;
            }
        }

        // ============================================================================================================================
        // Robots: footsteps (surface), squad march loops (3+), wading loop / water steps, servo, run start, Tough steps
        // (OpenZWading plays the water-entry splash; CombatView / BoardingView the boarding)
        // ============================================================================================================================
        class RobState { public Vector3 last; public float dist, still; public bool running, init; }
        [Tooltip("audio_v2: single robot footsteps up to this camera height (m)")] public float footstepMaxHeight = 95f;
        [Tooltip("audio_v2: footstep budget for the whole map")] public float stepsPerSecond = 12f;
        float stepTokens = 12f;
        readonly Dictionary<int, RobState> rob = new Dictionary<int, RobState>();
        readonly List<ZObject> bridges = new List<ZObject>();
        bool OnBridge(ZObject o)
        {
            foreach (var b in bridges) if (!b.Destroyed && o.CenterX >= b.LocX && o.CenterX < b.LocX + b.WidthPix && o.CenterY >= b.LocY && o.CenterY < b.LocY + b.HeightPix) return true;
            return false;
        }
        //      * a squad's march / wade loop and a robot's footsteps play only for the player's robots ordered in the last
        //        WalkSoundHold s (selected + a command: NoteWalkOrder), on screen; then they ease out over WalkSoundFade s, never loop on;
        //      * at most MaxMarchLoops march / wade loops and MaxStepVoices stepping robots at once, the ones nearest the screen centre;
        //      * unordered / CPU robots: no loop, no servo / run-start; a rare quiet single step on screen (AmbientStepsPerSecond map-wide).
        public static float WalkSoundHold = 3.5f, WalkSoundFade = 1.5f, AmbientStepsPerSecond = 0.5f, AmbientStepGain = 0.35f;
        public static int MaxMarchLoops = 2, MaxStepVoices = 3;
        public static int WalkPeakLoops, WalkLoopStarts, WalkOrderedSteps, WalkAmbientSteps, WalkOrders;
        readonly Dictionary<int, float> walkOrderAt = new Dictionary<int, float>();
        readonly List<(ZObject o, RobState s, Vector3 pos, float step, ZClass cls, bool water, bool running, float g, float cd)> walkers = new List<(ZObject, RobState, Vector3, float, ZClass, bool, bool, float, float)>();
        readonly HashSet<int> stepAllowed = new HashSet<int>();
        float ambientStepTokens;
        /// <summary>PlayerInput order (AudioDirector's marker hook): these robots' walking may be heard for the next few seconds.</summary>
        void NoteWalkOrder(IEnumerable<ZObject> sel)
        {
            if (sel == null) return;
            foreach (var u in sel) { if (u == null || u.Kind != ZKind.Robot) continue; var l = u.Leader ?? u; walkOrderAt[l.Id] = Time.time; walkOrderAt[u.Id] = Time.time; }
            WalkOrders++;
        }
        float WalkGain(ZObject o, ZObject lead)
        {
            if (o == null || o.Owner != Player) return 0f;
            float t = -1000f;
            if (walkOrderAt.TryGetValue(lead.Id, out var a)) t = a;
            if (walkOrderAt.TryGetValue(o.Id, out var b) && b > t) t = b;
            float age = Time.time - t;
            return age < WalkSoundHold ? 1f : Mathf.Clamp01(1f - (age - WalkSoundHold) / Mathf.Max(0.05f, WalkSoundFade));
        }
        static float CentreDist2(Vector3 p)
        {
            var c = Camera.main; if (!c) return 0f;
            var vp = c.WorldToViewportPoint(p); return (vp.x - 0.5f) * (vp.x - 0.5f) + (vp.y - 0.5f) * (vp.y - 0.5f);
        }

        void UpdateRobots(float dt)
        {
            if (dt <= 0f) return;
            bool close = !rts || rts.height < footstepMaxHeight;                                             // audio_v2: was 60 m (no single steps at the 70 m default)
            stepTokens = Mathf.Min(stepsPerSecond, stepTokens + dt * stepsPerSecond);
            ambientStepTokens = Mathf.Min(1f, ambientStepTokens + dt * AmbientStepsPerSecond);
            var marching = new Dictionary<int, (Vector3 sum, int n, bool water, float g)>();
            walkers.Clear();
            foreach (var kv in game.Views)
            {
                var v = kv.Value; if (!v || !v.isActiveAndEnabled || v.Obj == null || v.Obj.Kind != ZKind.Robot || v.Dying) continue;
                var o = v.Obj;
                if (!rob.TryGetValue(o.Id, out var s)) rob[o.Id] = s = new RobState { last = v.transform.position };
                var pos = v.transform.position; float step = (pos - s.last).magnitude; s.last = pos;
                if (step > 3f) { s.dist = 0; continue; }                                       // teleport (exit, spawn)
                var cls = ClassAt(pos); bool water = cls == ZClass.Water || cls == ZClass.DeepWater;
                bool moving = step / dt > 0.25f;
                var lead = o.Leader ?? o; bool running = lead.IsRunning || o.FortRushOn || o.YieldRunOn;                      // boarding_nb_v1 DV34
                float g = WalkGain(o, lead);
                bool heard = g > 0f && OnScreen(pos, 0.02f);
                if (moving && s.still > 1f && heard) Play("rob_servo", pos + Vector3.up, o.Id);  // starts moving (an ordered robot only)
                s.still = moving ? 0f : s.still + dt;
                if (s.init && running && !s.running && lead == o && heard) Play("rob_run_start", pos + Vector3.up, o.Id);
                s.running = running; s.init = true;
                int squad = 1 + lead.Minions.Count;
                if (moving && squad >= 3)
                {
                    if (heard) { marching.TryGetValue(lead.Id, out var m); marching[lead.Id] = (m.sum + pos, m.n + 1, m.water || water, Mathf.Max(m.g, g)); }
                    continue;
                }
                if (!moving || !close) continue;
                s.dist += step;
                walkers.Add((o, s, pos, step, cls, water, running, heard ? g : 0f, CentreDist2(pos)));
            }
            // the stepping robots that may be heard: ordered + on screen, the MaxStepVoices nearest the screen centre
            stepAllowed.Clear();
            foreach (var w in walkers.Where(x => x.g > 0f).OrderBy(x => x.cd).Take(MaxStepVoices)) stepAllowed.Add(w.o.Id);
            foreach (var w in walkers)
            {
                var o = w.o; var s = w.s;
                float stride = o.Type == "tough" ? 0.9f : w.running ? 0.8f : 0.6f;
                if (s.dist < stride) continue;
                s.dist = 0;
                string ev = w.water ? "rob_step_water" : o.Type == "tough" ? "mech_tough_step" : o.IsFortDefender ? "rob_step_metal" : OnBridge(o) ? (City ? "rob_step_metal" : "rob_step_wood")
                          : City || w.cls == ZClass.Road ? "rob_step_concrete" : Jungle ? (JungleGround(w.pos) == "mud" && HasClips("rob_step_mud") ? "rob_step_mud" : HasClips("rob_step_grass") ? "rob_step_grass" : "rob_step_sand") : "rob_step_sand";
                if (stepTokens < 1f || !OnScreen(w.pos, 0.02f)) continue;                                   // audio_v2: on screen, 12 steps / s at most
                if (stepAllowed.Contains(o.Id))
                {
                    stepTokens -= 1f; WalkOrderedSteps++;
                    Play(ev, w.pos, o.Id, (w.running ? Db(2f) : 1f) * w.g, w.running ? 1.05f : 1f);
                }
                else if (ambientStepTokens >= 1f)
                {   // everyone else: a rare, quiet single step (the battlefield is not silent, never a carpet of steps)
                    ambientStepTokens -= 1f; stepTokens -= 1f; WalkAmbientSteps++;
                    Play(ev, w.pos, o.Id, AmbientStepGain, 1f);
                }
            }
            // one loop per ordered moving squad (bible: never 4 x footsteps), wading loop in water; MaxMarchLoops nearest the screen centre
            var live = new HashSet<string>();
            foreach (var kv in marching.OrderBy(x => CentreDist2(x.Value.sum / x.Value.n)).Take(MaxMarchLoops))
            {
                var c = kv.Value.sum / kv.Value.n;
                string key = (kv.Value.water ? "wade:" : "march:") + kv.Key; live.Add(key);
                string ev = kv.Value.water ? "rob_wade" : "rob_squad_march";
                if (!LoopActive(key)) { StartLoop(key, ev, c); WalkLoopStarts++; if (!kv.Value.water) { var d = Def(ev); if (d != null && d.loaded.Count > 1) ForceClip(key, d, City || ClassAt(c) == ZClass.Road ? 1 : 0); } }
                SetLoop(key, kv.Value.g, 1f, c);                                                          // eases out with the order window
            }
            if (live.Count > WalkPeakLoops) WalkPeakLoops = live.Count;
            foreach (var k in loops.Keys.ToList()) if ((k.StartsWith("march:") || k.StartsWith("wade:")) && !live.Contains(k)) StopLoop(k, 0.4f);
        }
        void ForceClip(string key, SoundEventDef d, int idx)
        {
            if (!loops.TryGetValue(key, out var v) || idx >= d.loaded.Count) return;
            if (v.src.clip == d.loaded[idx]) return;
            v.src.clip = d.loaded[idx]; v.src.Play(); v.src.time = UnityEngine.Random.Range(0f, Mathf.Max(0f, d.loaded[idx].length - 0.05f));
        }

        // ============================================================================================================================
        // Physics impacts: relays on ragdolls and rock chunks; building pieces only get the water splash (BuildingBreaker plays
        // their impacts itself)
        // ============================================================================================================================
        float nextScan; readonly HashSet<int> relayed = new HashSet<int>();
        void ScanPhysics()
        {
            if (Time.time < nextScan) return;
            nextScan = Time.time + 0.5f;
            foreach (var r in FindObjectsByType<RobotRagdoll>(FindObjectsSortMode.None))
                if (relayed.Add(r.GetInstanceID())) AttachImpacts(r.gameObject, "dst_ragdoll_impact", "dst_ragdoll_splash");
            foreach (var p in FindObjectsByType<BuildingBreaker.Piece>(FindObjectsSortMode.None))
                if (relayed.Add(p.GetInstanceID())) AttachImpacts(p.gameObject, null, "dst_debris_splash");
            foreach (var c in FindObjectsByType<RockChunkLife>(FindObjectsSortMode.None))
                if (relayed.Add(c.GetInstanceID())) AttachImpacts(c.gameObject, "dst_rock_chunks", HasClips("wtr_debris_plop") ? "wtr_debris_plop" : "dst_debris_splash");
        }

        // ============================================================================================================================
        // Music: Zod danger levels (zmusic_engine.cpp) -> CALM / ATTACKING / FORT with debounce + hold, crossfades, stings.
        // DC16: PlayerPrefs oz_music (MusicEnabled) switches all of it (and the Music-bus stings) off for the takes.
        // ============================================================================================================================
        enum Danger { Calm, Attacking, Fort }
        /// <summary>lights_tail_v1 (FortAlarmBeacons): our fort is under attack right now (Zod danger level FORT, debounced like the
        /// music); false once the game is over (the danger freezes there), so the alarm beacons never stay on behind an end screen.</summary>
        public static bool FortDanger => I && !I.gameOver && I.danger == Danger.Fort;
        Danger danger = Danger.Calm, pending = Danger.Calm; float pendingSince, switchedAt = -100f, nextDanger; bool finalPush;
        Voice musicA, musicB; bool musicOnA;
        SoundEventDef musicDef;

        void OnMusicToggle(bool on)
        {
            if (!on) { FadeMusicOut(0.6f); if (game == null && menuAmbience) StartMenuBed(); return; }
            if (!music) return;
            StopLoop("bed:menu", 1f);                                                                          // audio_v2
            musicDef = null;
            if (game == null) StartMenuMusic(); else SwitchMusic();
        }

        void EnsureMusic()
        {
            if (musicA != null) return;
            musicA = NewVoice("MusicA"); musicB = NewVoice("MusicB");
            musicA.lp.cutoffFrequency = musicB.lp.cutoffFrequency = 900f;
        }

        void StartMenuMusic()
        {
            var d = Def("mus_menu"); if (d == null || d.loaded.Count == 0) return;
            int take = PlayerPrefs.GetInt("oz_menu_take", 0); PlayerPrefs.SetInt("oz_menu_take", (take + 1) % Mathf.Max(1, d.loaded.Count));
            PlayMusic(d, take % d.loaded.Count, 1.5f, MenuMusicDb); MenuMusicStarted(d);   // menu_exe_v1: under the UI, logged (Player.log + OzSoundLog)
        }

        void PlayMusic(SoundEventDef d, int idx, float xfade, float extraDb, bool randomEntry = false)
        {
            if (d == null || d.loaded.Count == 0 || !MusicOn) return;
            EnsureMusic();
            idx = MusicPick(d, idx);                                                  // music_v2: playlist pick / resume (idx < 0), else that clip
            var from = musicOnA ? musicA : musicB; var to = musicOnA ? musicB : musicA; musicOnA = !musicOnA;
            if (from.busy) { from.fadeTarget = 0f; from.fadeSpeed = 1f / Mathf.Max(0.05f, xfade); }
            to.def = d; to.busy = true; to.loop = true; to.start = Time.unscaledTime;
            ConfigureSource(to, d, d.loaded[idx]);
            to.src.loop = !MusicPlaylistOn(d); /* music_v2: a playlist crossfades on, one clip loops */ to.src.spatialBlend = 0f; to.combatMix = false; to.baseLin = Db(d.volDb + d.gains[idx] + extraDb);
            to.fade = 0f; to.fadeTarget = 1f; to.fadeSpeed = 1f / Mathf.Max(0.05f, xfade);
            to.src.volume = 0f;                                                                          // fades in from silence (as StartLoop): never a first frame at the source's old volume
            float len = d.loaded[idx].length;
            to.src.Play();
            to.src.time = MusicEntry(d, idx, len, randomEntry, extraDb);   /* music_v2: resume / section cue */   // bar-aligned-ish entry (Zod jumps to a random entry)
            musicDef = d;
            Count(Plays, d.id); Played++;
        }

        void FadeMusicOut(float t) { EnsureMusic(); foreach (var m in new[] { musicA, musicB }) if (m.busy) { m.fadeTarget = 0f; m.fadeSpeed = 1f / t; } musicDef = null; }

        void StepMusicVoices(float dt)
        {
            if (musicA == null) return;
            foreach (var m in new[] { musicA, musicB })
            {
                if (!m.busy) continue;
                m.fade = Mathf.MoveTowards(m.fade, m.fadeTarget, m.fadeSpeed * dt);
                if (m.fadeTarget <= 0 && m.fade <= 0.001f) { m.src.Stop(); m.busy = false; continue; }
                m.src.volume = Mathf.Clamp01(m.baseLin * m.fade * masterVolume * Db(BusGainDb("Music") + pauseMusicDb));
            }
        }

        void UpdateMusic()
        {
            float now = Time.time;
            if (gameOver) return;
            if (now >= nextDanger)
            {
                nextDanger = now + 0.25f;
                var ourFort = W.Objects.Where(o => o.IsFort && o.Owner == Player).ToList();
                bool fortDead = ourFort.Count > 0 && ourFort.All(o => o.Destroyed);
                Danger lvl = Danger.Calm;
                if (!fortDead)
                {
                    foreach (var o in W.Objects)
                    {
                        if (!o.IsUnit || o.Destroyed || o.Owner == Team.Neutral || o.Owner == Player) continue;
                        foreach (var f in ourFort) if (!f.Destroyed && Near(o, f, 250)) { lvl = Danger.Fort; break; }
                        if (lvl == Danger.Fort) break;
                    }
                    if (lvl == Danger.Calm)
                        foreach (var o in W.Objects)
                        {
                            var t = o.AttackObject; if (t == null || o.Destroyed) continue;
                            if ((o.Owner != Player && o.Owner != Team.Neutral && t.Owner == Player) || (o.Owner == Player && t.Owner != Team.Neutral && t.Owner != Player)) { lvl = Danger.Attacking; break; }
                        }
                }
                if (lvl != pending) { pending = lvl; pendingSince = now; }
                float debounce = fortDead ? 15f : 3f;
                float hold = danger == Danger.Calm ? 5f : danger == Danger.Attacking ? 7f : 3f;
                if (pending != danger && now - pendingSince >= debounce && now - switchedAt >= hold) { danger = pending; switchedAt = now; SwitchMusic(); }
                else if (musicDef == null) SwitchMusic();
                // bigger wave (see AlarmBurst); a dip in the danger no longer re-arms it
                bool alarmOn = LoopActive("fortalarm");
                if (danger == Danger.Fort && ourFort.Count > 0)
                {
                    int near = 0;
                    foreach (var o in W.Objects)
                    {
                        if (!o.IsUnit || o.Destroyed || o.Owner == Team.Neutral || o.Owner == Player) continue;
                        foreach (var f in ourFort) if (!f.Destroyed && Near(o, f, 250)) { near++; break; }
                    }
                    bool fresh = now - alarmLastThreat >= AlarmCalm;
                    bool bigger = alarmWave > 0 && near >= Mathf.Max(2 * alarmWave, alarmWave + AlarmBigMin) && now - alarmStoppedAt >= AlarmBigGap;
                    alarmLastThreat = now;
                    if (!alarmOn && (fresh || bigger))
                    {
                        StartLoop("fortalarm", "bld_fort_alarm", game.CenterWorld(ourFort[0])); alarmStartedAt = now; alarmWave = Mathf.Max(1, near); AlarmStarts++; alarmOn = true;
                        OzSoundLog.Add("AD", "bld_fort_alarm", null, "ALARM START " + (fresh ? "new attack" : "bigger wave") + " enemies " + near, 0f, game.CenterWorld(ourFort[0]));
                    }
                }
                if (alarmOn && (danger != Danger.Fort || now - alarmStartedAt >= AlarmBurst))
                {
                    StopLoop("fortalarm", danger == Danger.Fort ? 1.5f : 1f); alarmStoppedAt = now; AlarmSeconds += now - alarmStartedAt;
                    OzSoundLog.Add("AD", "bld_fort_alarm", null, "ALARM STOP after " + (now - alarmStartedAt).ToString("F1") + " s", 0f, null);
                }
                combatAmbDuck = danger == Danger.Calm ? 0f : -2f;                                          // audio_v2: beds always softly present (was -4)
            }
        }

        void SwitchMusic()
        {
            if (!MusicOn || game == null) return;
            if (MusicV3Owns) return;   /* music_v3: MusicCore owns the in-game music */ string lv = City ? "l17" : "l02";
            SoundEventDef d; int idx = -1;   /* music_v2: the playlist picks */
            if (danger == Danger.Fort || (finalPush && danger != Danger.Calm)) { d = Def("mus_fort_danger"); if (!MusicPlaylistOn(d)) idx = City ? 1 : 0;   /* music_v2 */ }
            else d = Def(danger == Danger.Attacking ? "mus_" + lv + "_attacking" : "mus_" + lv + "_calm");
            if (d == musicDef && d != null) return;
            Count(Triggers, d?.id);
            PlayMusic(d, idx, danger == Danger.Fort ? 1f : 2f, 0f, true);
        }

        // ============================================================================================================================
        // Ambience: beds per level with the day/night crossfade, positional river / harbour / lamps, random critters and gusts
        // ============================================================================================================================
        readonly List<Vector3> waterEmitters = new List<Vector3>(), lampEmitters = new List<Vector3>(), huts = new List<Vector3>();
        float nextGust, nextCrow, nextOwl, nextCritter, nextGull, nextBat, nextFoghorn, nextHoist, nextAmbSort;

        readonly List<Vector3> streamEmitters = new List<Vector3>(); bool streamBuilt;
        /// <summary>Level 16: one place per 16 m cell of the map that holds stream water (drawn water over a land tile).</summary>
        void BuildStreamEmitters()
        {
            streamBuilt = true; streamEmitters.Clear();
            if (game == null || game.World == null || game.World.Map == null) return;
            var map = game.World.Map; const int block = 8;
            for (int by = 0; by < map.height; by += block)
                for (int bx = 0; bx < map.width; bx += block)
                {
                    int n = 0; float sx = 0f, sy = 0f, sz = 0f;
                    for (int y = by; y < Mathf.Min(by + block, map.height); y++)
                        for (int x = bx; x < Mathf.Min(bx + block, map.width); x++)
                        {
                            var c = map.ClassAt(x, y); if (c == ZClass.Water || c == ZClass.DeepWater) continue;
                            var p = new Vector3(x * 2f + 1f, 0f, -(y * 2f + 1f));
                            if (!OzWaterLanding.OpenWaterAt(p, out float wy)) continue;
                            n++; sx += p.x; sy += wy; sz += p.z;
                        }
                    if (n >= 3) streamEmitters.Add(new Vector3(sx / n, sy / n, sz / n));
                }
            Debug.Log($"[OpenZ Audio] level 16: {streamEmitters.Count} stream places for amb_l16_stream");
        }

        void BuildAmbienceEmitters()
        {
            streamBuilt = false;
            waterEmitters.Clear(); lampEmitters.Clear(); huts.Clear();
            if (game == null || game.World == null || game.World.Map == null) return;
            var map = game.World.Map; const int block = 12;                              // ~24 m cells along the river / canal
            for (int by = 0; by < map.height; by += block)
                for (int bx = 0; bx < map.width; bx += block)
                {
                    int n = 0; float sx = 0, sy = 0;
                    for (int y = by; y < Mathf.Min(by + block, map.height); y++)
                        for (int x = bx; x < Mathf.Min(bx + block, map.width); x++)
                        { var c = map.ClassAt(x, y); if (c == ZClass.Water || c == ZClass.DeepWater) { n++; sx += x; sy += y; } }
                    if (n >= 6) { var p = ZGame.PxToWorld((sx / n) * 16 + 8, (sy / n) * 16 + 8); p.y = game.GroundY(p.x, p.z); waterEmitters.Add(p); }
                }
            if (!dayNight) dayNight = FindFirstObjectByType<DayNight>();
            if (dayNight && dayNight.lampRoot)
                foreach (Transform t in dayNight.lampRoot) if (t.name.ToLower().Contains("lamp")) lampEmitters.Add(t.position + Vector3.up * 3.5f);
            foreach (var o in game.World.Objects) if (o.Type != null && o.Type.Contains("hut")) huts.Add(game.CenterWorld(o));
            bridges.Clear(); foreach (var o in game.World.Objects) if (o.IsBridge) bridges.Add(o);
            float now = Time.time;
            nextGust = now + 8f; nextCrow = now + 15f; nextOwl = now + 40f; nextCritter = now + 20f; nextGull = now + 20f; nextBat = now + 15f; nextFoghorn = now + 60f; nextHoist = now + 30f;
        }

        static float Ramp(float t, float a, float b) => Mathf.Clamp01((t - a) / (b - a));

        void UpdateAmbience(float dt)
        {
            if (game == null) return;
            if (!dayNight) dayNight = FindFirstObjectByType<DayNight>();
            float t = dayNight ? dayNight.timeOfDay : 0.08f;
            // 0 noon, 0.3 sunset, 0.4 blue hour, 0.55 night, 0.9 dawn (DayNight.cs); ~20 s crossfades at the 12 min cycle
            float wDay = 1f - Ramp(t, 0.26f, 0.32f) + Ramp(t, 0.86f, 0.94f);
            float wNight = Ramp(t, 0.42f, 0.50f) - Ramp(t, 0.86f, 0.94f);
            float wDusk = Mathf.Clamp01(1f - wDay - wNight);
            bool jungleBeds = Jungle && HasClips("amb_l16_day") && HasClips("amb_l16_swamp") && HasClips("amb_l16_night");
            if (City) { Bed("amb_l17_day", wDay + wDusk); Bed("amb_l17_night_fog", wNight); }
            else if (jungleBeds) { Bed("amb_l16_day", wDay * OzJungleLife.DayBedShare); Bed("amb_l16_swamp", wDusk); Bed("amb_l16_night", wNight); }   // level 16: the jungle by day, the swamp at dusk, the jungle night
            else { Bed("amb_l02_day", wDay); Bed("amb_l02_dusk", wDusk); Bed("amb_l02_night", wNight); }
            float now = Time.time; var lp = ListenerPos;
            if (now > nextAmbSort)
            {
                nextAmbSort = now + 1f;
                string riverEv = City ? "amb_l17_harbour" : "amb_l02_river";
                var near = waterEmitters.Select((p, i) => (p, i)).OrderBy(x => (x.p - lp).sqrMagnitude).Take(maxRiverEmitters).Select(x => x.i).ToHashSet();
                for (int i = 0; i < waterEmitters.Count; i++) { string k = "water:" + i; if (near.Contains(i)) { if (!LoopActive(k)) StartLoop(k, riverEv, waterEmitters[i]); } else StopLoop(k, 2f); }
                if (Jungle && HasClips("amb_l16_stream"))
                {   // level 16: the ravine streams babble (drawn water over land tiles: no water tile, so no river place of their own)
                    if (!streamBuilt && Time.timeSinceLevelLoad > 3f) BuildStreamEmitters();
                    var nearS = streamEmitters.Select((p, i) => (p, i)).OrderBy(x => (x.p - lp).sqrMagnitude).Take(2).Select(x => x.i).ToHashSet();
                    for (int i = 0; i < streamEmitters.Count; i++) { string k = "stream:" + i; if (nearS.Contains(i)) { if (!LoopActive(k)) StartLoop(k, "amb_l16_stream", streamEmitters[i]); } else StopLoop(k, 2f); }
                }
                bool closeZoom = !rts || rts.height < footstepMaxHeight;                                     // audio_v2: was 60 m / 35 m
                var nf = closeZoom ? W.Flags.Where(f => (game.CenterWorld(f) - lp).sqrMagnitude < 45f * 45f).OrderBy(f => (game.CenterWorld(f) - lp).sqrMagnitude).Take(3).Select(f => f.Id).ToHashSet() : new HashSet<int>();
                foreach (var f in W.Flags) { string k = "flag:" + f.Id; if (nf.Contains(f.Id)) { if (!LoopActive(k)) StartLoop(k, "bld_flag_flap", game.CenterWorld(f) + Vector3.up * 4f); } else StopLoop(k, 1f); }
                bool lampsOn = t > 0.38f && t < 0.93f;
                var nl = lampEmitters.Select((p, i) => (p, i)).OrderBy(x => (x.p - lp).sqrMagnitude).Take(3).Select(x => x.i).ToHashSet();
                for (int i = 0; i < lampEmitters.Count; i++) { string k = "lamp:" + i; if (lampsOn && City && nl.Contains(i)) { if (!LoopActive(k)) StartLoop(k, "amb_l17_lamp_buzz", lampEmitters[i]); } else StopLoop(k, 1f); }
            }
            Vector3 Around(float r0, float r1) { var a = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(r0, r1); var p = lp + new Vector3(a.x, 0, a.y); p.y = game.GroundY(p.x, p.z) + 2f; return p; }
            bool night = wNight > 0.5f, day = wDay > 0.5f;
            bool critters = AmbientLife.I;                                                             // the critters package calls its own eagles / gulls
            if (Jungle && HasClips("amb_l16_bird_call"))
            {   // level 16: bird calls from the trees by day, an insect swell now and then (no sand gust, no desert crow)
                if (now > nextCrow) { nextCrow = now + UnityEngine.Random.Range(12f, 26f); if (!night && !OzJungleLife.BirdsAway) Play("amb_l16_bird_call", Around(15f, 45f) + Vector3.up * 6f, -1); }
                if (now > nextGust) { nextGust = now + UnityEngine.Random.Range(30f, 60f); Play("amb_l16_insect_swell", lp, -1); }
            }
            else if (!City)
            {
                if (now > nextGust) { nextGust = now + UnityEngine.Random.Range(12f, 30f); if (!night) Play("amb_l02_sand_gust", lp, -1, 1f, 1f); }
                if (now > nextCrow) { nextCrow = now + UnityEngine.Random.Range(15f, 20f); if (!critters && day && UnityEngine.Random.value < 0.34f) Play("amb_l02_crow", Around(15f, 40f), -1); }
                if (now > nextOwl) { nextOwl = now + UnityEngine.Random.Range(40f, 90f); if (night) Play("amb_l02_owl", Around(25f, 60f), -1); }
                if (now > nextCritter) { nextCritter = now + UnityEngine.Random.Range(20f, 40f); var h = huts.OrderBy(p => (p - lp).sqrMagnitude).FirstOrDefault(); if (huts.Count > 0 && (h - lp).sqrMagnitude < 40f * 40f + 900f) Play("amb_l02_hut_critters", h, -1); }
            }
            else
            {
                if (now > nextGust) { nextGust = now + UnityEngine.Random.Range(15f, 35f); Play("amb_l17_wind_gust", lp, -1, 1f, 0.84f); }
                if (now > nextGull) { nextGull = now + UnityEngine.Random.Range(20f, 45f); if (!critters && day && waterEmitters.Count > 0) Play("amb_l17_gulls", waterEmitters.OrderBy(p => (p - lp).sqrMagnitude).First() + Vector3.up * 8f, -1); }
                if (now > nextCrow) { nextCrow = now + UnityEngine.Random.Range(25f, 50f); if (!night) Play("amb_l17_crows", Around(15f, 40f), -1); }
                if (now > nextBat) { nextBat = now + UnityEngine.Random.Range(15f, 20f); if (night && UnityEngine.Random.value < 0.34f) Play("amb_l17_bats", Around(10f, 30f), -1); }
                if (now > nextFoghorn) { nextFoghorn = now + UnityEngine.Random.Range(60f, 120f); if (night) Play("amb_l17_foghorn", lp, -1); }
                if (now > nextHoist) { nextHoist = now + UnityEngine.Random.Range(30f, 60f); Play("amb_l17_hoist", Around(20f, 50f), -1); }
            }
        }

        void Bed(string id, float w)
        {
            string key = "bed:" + id;
            var ca = CA;
            if (w > 0.01f) { if (!LoopActive(key)) StartLoop(key, id, ListenerPos, ca ? ca.ListenerRig : null); SetLoop(key, Mathf.Sqrt(Mathf.Clamp01(w)), 1f, ListenerPos); }   // equal-power-ish
            else StopLoop(key, 2f);
        }

        // ---- night lights: searchlight / floodlight hums (nearest 2), faulty lamps flicker within 25 m
        float nextNight, nextFlicker;
        void UpdateNightLights()
        {
            if (Time.time < nextNight) return;
            nextNight = Time.time + 0.5f;
            var nl = NightLights.I;
            if (!nl || nl.Fixtures == null) { StopPrefix("srch:", 1f); StopPrefix("flood:", 1f); return; }
            var lp = ListenerPos;
            // lights qualified, so no searchlight / flood hum and no faulty-lamp crackle ever played); a faulty lamp mid-dip still counts
            var lit = nl.Fixtures.Where(f => f != null && f.Level > 0.5f && (f.Out > 0.5f || (f.Faulty && f.Damage < 2 && !f.GlassBroken)) && (f.Head || f.Root)).ToList();
            Vector3 P(NightFixture f) => (f.Head ? f.Head : f.Root).position;
            var srch = lit.Where(f => f.Kind == NightKind.Searchlight).OrderBy(f => (P(f) - lp).sqrMagnitude).Take(2).ToList();
            var flood = City ? new List<NightFixture>() : lit.Where(f => f.Kind == NightKind.Flood).OrderBy(f => (P(f) - lp).sqrMagnitude).Take(2).ToList();
            KeepLoops("srch:", "ngt_searchlight_hum", srch, P);
            KeepLoops("flood:", "ngt_floodlight_hum", flood, P);
            if (Time.time > nextFlicker)
            {
                nextFlicker = Time.time + UnityEngine.Random.Range(6f, 14f);
                var f = lit.Where(x => x.Faulty && (P(x) - lp).sqrMagnitude < 25f * 25f + 400f).OrderBy(x => (P(x) - lp).sqrMagnitude).FirstOrDefault();
                if (f != null) Play("ngt_lamp_flicker", P(f), -1);
            }
        }
        void KeepLoops(string prefix, string ev, List<NightFixture> set, Func<NightFixture, Vector3> pos)
        {
            var keep = new HashSet<string>();
            foreach (var f in set) { string k = prefix + f.GetHashCode(); keep.Add(k); if (!LoopActive(k)) StartLoop(k, ev, pos(f)); SetLoop(k, 1f, 1f, pos(f)); }
            foreach (var k in loops.Keys.ToList()) if (k.StartsWith(prefix) && !keep.Contains(k)) StopLoop(k, 1f);
        }
        void StopPrefix(string prefix, float fade) { foreach (var k in loops.Keys.ToList()) if (k.StartsWith(prefix)) StopLoop(k, fade); }

        // ============================================================================================================================
        // Hooks into the live views (all in Assets/OpenZ/Scripts now, so referenced directly)
        // ============================================================================================================================
        bool hooked;
        bool FactoryLive => FactoryViews.I;
        bool FlagLive => FlagView.Active;

        void Hook()
        {
            if (!hooked)
            {
                hooked = true;
                FactoryViews.AudioEvent += OnFactoryAudio;
                FlagView.Sound += OnFlagSound;
                FlagView.BuiltInSounds = true;
                BuildingAnimView.AudioEvent += OnBuildingAnim;
                NightLights.AudioEvent += OnNightAudio;
                AmbientLife.AudioEvent += OnCritter;
                if (OpenZHud.ClickSound == "enter" || OpenZHud.ClickSound == "v3-grenade-bounce-a") OpenZHud.ClickSound = "ui_click";   // -> Play2DRoute
            }
            if (routeLegacyVoices) { CombatAudio.VoiceRoute = RouteVoice; CombatAudio.Play2DRoute = Route2D; }
        }
        void Unhook()
        {
            if (!hooked) return;
            hooked = false;
            FactoryViews.AudioEvent -= OnFactoryAudio;
            FlagView.Sound -= OnFlagSound;
            BuildingAnimView.AudioEvent -= OnBuildingAnim;
            NightLights.AudioEvent -= OnNightAudio;
            AmbientLife.AudioEvent -= OnCritter;
            if (CombatAudio.VoiceRoute == (Func<string, string[], float, bool, bool?>)RouteVoice) CombatAudio.VoiceRoute = null;
            if (CombatAudio.Play2DRoute == (Func<string, float, bool>)Route2D) CombatAudio.Play2DRoute = null;
            // audio_v2: the HUD click stays "ui_click" (CombatAudio.Play2D boots a director for ui_* names; "enter" was a boarding thump)
        }

        /// <summary>Door event by door style (FactoryDoorSpec): robot factory split doors, vehicle factory roller shutter, fort gate.</summary>
        string DoorEvent(ZObject b, bool open)
        {
            string t = b?.Type ?? "";
            string ev = t == "vehicle_factory" ? (open ? "bld_shutter_open" : "bld_shutter_close")
                      : t.StartsWith("fort") ? (open ? "bld_fort_gate_open" : "bld_fort_gate_close")
                      : (open ? "bld_door_open" : "bld_door_close");
            return HasClips(ev) ? ev : (open ? "bld_door_open" : "bld_door_close");
        }

        void OnFactoryAudio(string key, ZObject b, Vector3 pos)
        {   // FactoryViews.AudioEvent keys: factory_door_open, factory_door_close, factory_unit_out, factory_hum_on / off, rally_set
            switch (key)
            {
                case "factory_door_open": Play(DoorEvent(b, true), pos, b?.Id ?? -1); break;
                case "factory_door_close": Play(DoorEvent(b, false), pos, b?.Id ?? -1); break;
                case "factory_hum_on": case "factory_hum_off": break;                                      // bug #5: no endless loop; FanSpinUp on production start
                case "factory_unit_out": OzSoundV3.FactoryUnitOut(b, pos); if (b != null && b.Owner == Player) OzSoundV3.FactoryReason(b); break;
                case "rally_set": UI("ui_rally_set"); break;
            }
        }
        float humAccent = float.MaxValue; Vector3 humPos;
        [Tooltip("audio_v2: a factory fan spin-up at most every N s (production start / unit switch of the player's factory)")] public float fanCooldown = 20f;
        readonly Dictionary<int, float> fanLast = new Dictionary<int, float>();
        readonly Dictionary<int, string> prodState = new Dictionary<int, string>(); bool prodScanned; float humStopAt = float.MaxValue;

        /// <summary>Bug #5: one fan spin-up (the factory hum clip for ~3.5 s, then a slow fade) when production starts.</summary>
        void FanSpinUp(ZObject b, Vector3 pos)
        {
            string ev = b.Type == "vehicle_factory" ? "bld_vehicle_factory_hum" : "bld_factory_hum";
            StopLoop("hum", 0.1f); StartLoop("hum", ev, pos); SetLoop("hum", 1.4f, 1f, pos);
            humStopAt = Time.time + 3.5f; humPos = pos;
            OzSoundV3.FactoryReason(b); FanSpinUps++;
        }
        public static int FanSpinUps;

        void OnFlagSound(FlagView.SoundEvent e)
        {   // FlagView plays ter_flag_capture / ter_flag_raise / ter_zone_gained itself (CombatAudio); ter_zone_lost and the building
            // recolour cue have no clip there, so they play here in time with its sweep; ann_* arrive through CombatAudio.VoiceRoute
            switch (e.id)
            {
                case FlagView.SndCapture: case FlagView.SndGained: Owned(e.id); break;
                case FlagView.SndRaise: Play(FlagView.SndRaise, e.position, e.objectId); break;
                case FlagView.SndZoneLost: UI("ter_zone_lost"); break;
                case FlagView.SndBuilding: Play("bld_building_captured", e.position, e.objectId); break;
            }
        }

        void OnBuildingAnim(string key, ZObject b, Vector3 pos)
        {
            switch (key)
            {
                case "radar_dish_start": Play("bld_radar_dish_start", pos, b?.Id ?? -1); break;
                case "radar_dish_stop": Play("bld_radar_dish_stop", pos, b?.Id ?? -1); break;
                case "radar_dish_toss": Play("bld_radar_dish_toss", pos, b?.Id ?? -1); break;
                case "radar_dish_land": { var ca = CA; if (!Play("bld_radar_dish_land", pos, b?.Id ?? -1) && ca) ca.Play("turret", pos, 1f, 0.2f, b?.Id ?? -1, LegacyMatch.BankMechStep); break; }
                case "repair_hoist_down": case "repair_hoist_up": Play("bld_repair_hoist", pos, b?.Id ?? -1); break;
            }
        }

        static readonly Dictionary<string, string> NightMap = new Dictionary<string, string>
        {
            ["lamp_spark"] = "ngt_lamp_spark", ["lamp_glass"] = "ngt_lamp_glass", ["lamp_fall"] = "ngt_lamp_fall", ["lamp_land"] = "ngt_lamp_land",
            ["light_fizzle"] = "ngt_light_fizzle", ["searchlight_on"] = "ngt_searchlight_on",
        };
        void OnNightAudio(string key, Vector3 pos) { if (NightMap.TryGetValue(key, out var ev)) Play(ev, pos, -1); }

        void OnCritter(string id, Vector3 pos, float vol) => Play(id == "amb_l02_crow" ? "amb_l02_eagle_cry" : id, pos, -1, vol);

        // ============================================================================================================================
        // Coverage (plan/04 5b)
        // ============================================================================================================================
        public void ResetCounters()
        {
            Triggers.Clear(); Plays.Clear(); SweepPlays.Clear(); Played = 0; caBaseline.Clear();
            var ca = CA; if (ca) foreach (var kv in ca.PlayCounts) caBaseline[kv.Key] = kv.Value;
        }
        int CaPlays(CaRoute cr)
        {
            var ca = CA; if (!ca) return 0;
            int n = 0;
            foreach (var k in cr.count) n += (ca.PlayCounts.TryGetValue(k, out var c) ? c : 0) - (caBaseline.TryGetValue(k, out var b) ? b : 0);
            return n;
        }

        /// <summary>Markdown table: every coverage event -> owner, clips, triggers, plays. FAIL = no clip or never heard; WARN = only
        /// stand-ins or only heard in the sweep; n/a = the other level, or silent by design (reason given). includeAll lists every event.</summary>
        public string CoverageReport(bool includeAll = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine("| event | owner | status | clips | stand-in | triggers | plays | sweep | result |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            int fail = 0, warn = 0, pass = 0, na = 0;
            var ca = CA;
            foreach (var d in defs.Values.Where(x => !x.id.StartsWith("raw_")).OrderBy(x => x.id))
            {
                if (!d.coverage && !includeAll) continue;
                if (game != null && (City ? d.id.Contains("_l02_") : d.id.Contains("_l17_"))) { sb.AppendLine($"| {d.id} | - | {d.status} | {d.loaded.Count} |  | - | - | - | n/a (other level) |"); na++; continue; }
                if (ByDesign.TryGetValue(d.id, out var why)) { sb.AppendLine($"| {d.id} | - | {d.status} | 0 |  | {Get(Triggers, d.id)} | 0 | 0 | n/a (by design: {why}) |"); na++; continue; }
                if (Silent.Contains(d.id)) { sb.AppendLine($"| {d.id} | - | disabled | 0 |  | {Get(Triggers, d.id)} | 0 | 0 | n/a (legacy_match: not in the legacy map) |"); na++; continue; }
                if (Get(Triggers, d.id) == 0 && NotInPlay.TryGetValue(d.id, out var nip)) { sb.AppendLine($"| {d.id} | - | {d.status} | {d.loaded.Count} |  | 0 | 0 | {Get(SweepPlays, d.id)} | n/a ({nip}) |"); na++; continue; }
                string owner, res; int clips, trig = Get(Triggers, d.id), pl, sw = Get(SweepPlays, d.id); bool standOnly = false;
                if (Combat.TryGetValue(d.id, out var cr))
                {
                    owner = "CombatAudio:" + cr.key + (LegacyRule.TryGetValue(d.id, out var arule) ? " (" + arule + ")" : "");
                    clips = ca ? cr.bank.Count(n => ca.HasClip(n)) : 0;
                    pl = CaPlays(cr);
                    res = !ca ? "n/a (no CombatAudio in this scene)" : clips == 0 ? "FAIL" : pl <= 0 || (trig == 0 && sw == 0) ? "FAIL" : pl - sw <= 0 || trig == 0 ? "WARN" : "PASS";
                }
                else if (EngineOwned.TryGetValue(d.id, out var bank))
                {
                    owner = "LegacyEngineAudio:" + bank;
                    clips = !ca ? 0 : bank == "mech_step" ? LegacyMatch.BankMechStep.Count(n => ca.HasClip(n)) : (ca.HasClip(bank + "-loop") || ca.HasClip(bank) ? 1 : 0);
                    pl = bank == "mech_step" ? (ca && ca.PlayCounts.TryGetValue("mech_step", out var ms) ? ms - (caBaseline.TryGetValue("mech_step", out var mb) ? mb : 0) : 0) + Get(Plays, d.id) : Get(Plays, d.id);
                    if (bank == "mech_step" && pl > 0 && trig == 0) trig = pl;
                    res = !ca ? "n/a (no CombatAudio in this scene)" : clips == 0 ? "FAIL" : pl <= 0 ? "FAIL" : pl - sw <= 0 ? "WARN" : "PASS";
                }
                else
                {
                    owner = "AudioDirector";
                    pl = Get(Plays, d.id);
                    bool routed = d.routes.Length > 0 && d.routes.All(r => (Def(r) != null && Def(r).loaded.Count > 0) || Combat.ContainsKey(r));
                    clips = d.loaded.Count; standOnly = clips > 0 && d.standins.All(x => x);
                    res = (clips == 0 && !routed) || trig == 0 ? "FAIL" : standOnly || (pl - sw) <= 0 ? "WARN" : "PASS";
                }
                if (res == "FAIL") fail++; else if (res == "WARN") warn++; else if (res == "PASS") pass++; else na++;
                sb.AppendLine($"| {d.id} | {owner} | {d.status} | {clips} | {(standOnly ? "yes" : "")} | {trig} | {pl} | {sw} | {res} |");
            }
            sb.Insert(0, $"Sound coverage: {pass} PASS, {warn} WARN, {fail} FAIL, {na} n/a (AudioDirector re-based on CombatAudio, {DateTime.Now:yyyy-MM-dd HH:mm})\n\n");
            return sb.ToString();
        }
        static int Get(Dictionary<string, int> d, string k) => d.TryGetValue(k, out var n) ? n : 0;

        /// <summary>Fires every coverage event once through the real playback path, 0.15 s apart: AudioDirector events at the listener,
        /// their channels. For events a one-minute bot match cannot produce (victory, defeat, nuke). Counted separately (SweepPlays).</summary>
        public System.Collections.IEnumerator CoverageSweep()
        {
            sweeping = true;
            var ca = CA;
            foreach (var d in defs.Values.Where(x => x.coverage).ToList())
            {
                if (game != null && (City ? d.id.Contains("_l02_") : d.id.Contains("_l17_"))) continue;
                if (ByDesign.ContainsKey(d.id) || Silent.Contains(d.id)) continue;
                if (Combat.TryGetValue(d.id, out var cr)) Forward(d.id, cr, ListenerPos, -1, 1f, 1f);
                else if (EngineOwned.TryGetValue(d.id, out var bank))
                {
                    if (ca)
                    {
                        if (bank == "mech_step") ca.PlayWide("mech_step", ListenerPos, LegacyMatch.GainMechStep, LegacyMatch.BankMechStep);
                        else ca.PlayWide("engine", ListenerPos, 0.3f, ca.HasClip(bank + "-loop") ? bank + "-loop" : bank);
                        Count(SweepPlays, d.id); Count(Plays, d.id); Count(Triggers, d.id);
                    }
                }
                else if (d.loop) { StartLoop("sweep:" + d.id, d.id, ListenerPos); yield return new WaitForSecondsRealtime(0.15f); StopLoop("sweep:" + d.id, 0.05f); }
                else if (d.channel == "rachel" || (d.channel ?? "").StartsWith("alan")) { Count(Triggers, d.id); PlayVoice(d, d.channel == "rachel" ? rachel : alanAlert, null); }
                else if (d.bus == "Music") { Count(Triggers, d.id); if (MusicOn) Play(d.id, ListenerPos, -1); else { Count(Plays, d.id); Count(SweepPlays, d.id); } }
                else Play(d.id, ListenerPos, -1);
                yield return new WaitForSecondsRealtime(0.15f);
            }
            sweeping = false;
        }

        /// <summary>Fires EVERY event of the table once through its real playback path (AudioDirector one-shot, loop or Alan / Rachel
        /// gaps and the music toggle bypassed, checks that a source really started with a non-zero volume, and writes one row per event
        /// to reportPath: audio / n/a (legacy_match silent, mixer rule) / FAIL + why; the last column says who triggers it in play.</summary>
        public System.Collections.IEnumerator FireEveryEvent(string reportPath, float gap = 0.15f)
        {
            if (!Loaded) { Debug.LogWarning("[AudioDirector] coverage test: " + TablePath + ".json is not loaded"); yield break; }
            var ca = CA; bool wasSweeping = sweeping, listenerPaused = AudioListener.pause;
            // the in-play coverage counters (CoverageReport) must not see the test: snapshot here, restore at the end
            var keepT = new Dictionary<string, int>(Triggers); var keepP = new Dictionary<string, int>(Plays); var keepS = new Dictionary<string, int>(SweepPlays);
            int keepPlayed = Played; var caKeep = ca ? new Dictionary<string, int>(ca.PlayCounts) : null;
            sweeping = true; forceAll = true; AudioListener.pause = false;
            var sb = new StringBuilder();
            sb.AppendLine("| event | bus | dim | owner | fired | detail | in play |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            int ok = 0, na = 0, fail = 0; var failed = new List<string>();
            var all = defs.Values.Where(x => !x.id.StartsWith("raw_")).OrderBy(x => x.id).ToList();
            foreach (var d in all)
            {
                string owner = "AudioDirector", res, det;
                Voice probe = null; bool isLoop = false;
                int caBefore = ca ? ca.Played : 0, before = Played;
                if (Silent.Contains(d.id)) { owner = "-"; res = "n/a"; det = "legacy_match: the legacy map has none"; }
                else if (d.status == "rule" && d.loaded.Count == 0 && d.routes.Length == 0) { owner = "-"; res = "n/a"; det = "mixer rule, no clip"; }
                else
                {
                    if (Combat.TryGetValue(d.id, out var route)) { owner = "CombatAudio:" + route.key; if (ca) ca.PlayWide(route.key, ListenerPos, route.gain, route.bank); }
                    else if (EngineOwned.TryGetValue(d.id, out var engine))
                    {
                        owner = "LegacyEngineAudio:" + engine;
                        if (ca) { if (engine == "mech_step") ca.PlayWide("mech_step", ListenerPos, LegacyMatch.GainMechStep, LegacyMatch.BankMechStep); else ca.PlayWide("engine", ListenerPos, 0.3f, ca.HasClip(engine + "-loop") ? engine + "-loop" : engine); }
                    }
                    else if (d.loop && d.loaded.Count > 0) { owner = "AudioDirector loop"; isLoop = true; StartLoop("test:" + d.id, d.id, ListenerPos); loops.TryGetValue("test:" + d.id, out probe); }
                    else if (d.channel == "rachel" || (d.channel ?? "").StartsWith("alan"))
                    {
                        owner = "AudioDirector " + d.channel;
                        var ch = d.channel == "rachel" ? rachel : d.channel == "alan_main" ? alanMain : alanAlert;
                        if (PlayVoice(d, ch, null)) probe = ch;
                    }
                    else
                    {
                        if (d.loaded.Count == 0 && d.routes.Length > 0) owner = "route -> " + string.Join(" + ", d.routes);
                        lastStarted = null; Play(d.id, ListenerPos, -1); probe = lastStarted;
                    }
                    if (isLoop) yield return new WaitForSecondsRealtime(gap);                               // loops fade in over 0.15 s
                    bool started = probe != null && probe.src && probe.src.isPlaying && probe.src.clip;
                    float vol = started ? probe.src.volume : 0f;
                    if (started && vol > 0.0005f) { res = "audio"; det = $"{probe.src.clip.name}, vol {vol:0.000}, {(probe.src.spatialBlend > 0.5f ? "3D" : "2D")}"; }
                    else if (started) { res = "FAIL"; det = probe.src.clip.name + " started at volume 0 (bus, slider or masterVolume muted?)"; }
                    else if (ca && ca.Played > caBefore) { res = "audio"; det = "CombatAudio started " + (ca.Played - caBefore) + " source(s)"; }
                    else if (Played > before) { res = "audio"; det = "route played"; }
                    else
                    {
                        res = "FAIL";
                        det = (Combat.ContainsKey(d.id) || EngineOwned.ContainsKey(d.id)) && !ca ? "no CombatAudio in this scene (run it in a level)"
                            : d.loaded.Count == 0 && d.routes.Length == 0 && !Combat.ContainsKey(d.id) && !EngineOwned.ContainsKey(d.id) ? "no clip loaded (Resources/OpenZ/Audio imported?)"
                            : "no source started (clip missing from the bank or every voice busy)";
                    }
                    if (isLoop) StopLoop("test:" + d.id, 0.05f);
                }
                string play = NotInPlay.TryGetValue(d.id, out var why) ? "not triggered: " + why : LegacyRule.TryGetValue(d.id, out var rule) ? rule : "";
                if (res == "audio") ok++; else if (res == "n/a") na++; else { fail++; failed.Add(d.id); Debug.LogWarning($"[AudioDirector] coverage test FAIL {d.id}: {det}"); }
                sb.AppendLine($"| {d.id} | {d.bus} | {(d.is3D ? "3D" : "2D")} | {owner} | {res} | {det} | {play} |");
                if (!isLoop) yield return new WaitForSecondsRealtime(gap);
            }
            sweeping = wasSweeping; forceAll = false; AudioListener.pause = listenerPaused;
            Triggers.Clear(); foreach (var kv in keepT) Triggers[kv.Key] = kv.Value;
            Plays.Clear(); foreach (var kv in keepP) Plays[kv.Key] = kv.Value;
            SweepPlays.Clear(); foreach (var kv in keepS) SweepPlays[kv.Key] = kv.Value;
            Played = keepPlayed;
            if (ca && caKeep != null)                                                                        // CombatAudio plays of the test move into the baseline
                foreach (var kv in ca.PlayCounts)
                {
                    int delta = kv.Value - (caKeep.TryGetValue(kv.Key, out var b0) ? b0 : 0);
                    if (delta > 0) caBaseline[kv.Key] = (caBaseline.TryGetValue(kv.Key, out var b1) ? b1 : 0) + delta;
                }
            string head = $"Fire-every-event test: {ok} produced audio, {na} n/a, {fail} FAIL of {all.Count} events "
                        + $"(level {(game != null ? game.level : "none")}, music {(MusicEnabled ? "on" : "off")}, {DateTime.Now:yyyy-MM-dd HH:mm})";
            try { System.IO.File.WriteAllText(reportPath, "# OpenZ sound: fire every event once\n\n" + head + "\n\n" + sb); }
            catch (Exception ex) { Debug.LogWarning("[AudioDirector] coverage test: " + ex.Message); }
            Debug.Log("[AudioDirector] " + head + (fail > 0 ? ". FAIL: " + string.Join(", ", failed) : "") + " -> " + reportPath);
        }

        public void WriteCoverageReport(string path) { System.IO.File.WriteAllText(path, "# OpenZ sound coverage\n\n" + CoverageReport(true)); }

        // ------------------------------------------------------------------ utils
        static float Db(float db) => Mathf.Pow(10f, db / 20f);
        static void Count(Dictionary<string, int> d, string id) { if (id != null) d[id] = d.TryGetValue(id, out var n) ? n + 1 : 1; }

        void FixedUpdate()
        {
            if (Time.time > humStopAt) { humStopAt = float.MaxValue; StopLoop("hum", 1.4f); }        // bug #5: spin-up ends, no accent spam
        }
    }

    /// <summary>Plays the AudioDirector impact event when this body hits something (per-body 0.15 s cooldown), or the splash event
    /// when it hits water (tile class 2 / 4 under the contact). eventId null = splash only.</summary>
    public class AudioImpactRelay : MonoBehaviour
    {
        public string eventId, splashId; float last; bool splashed;
        // Now one budget per body root (a ragdoll, a debris piece): >= 0.2 s apart, at most 6, and bone-on-bone contacts ignored
        static readonly Dictionary<int, (float last, int n)> perRoot = new Dictionary<int, (float, int)>();
        const float RootGap = 0.2f; const int RootMax = 6;
        static readonly Dictionary<string, Queue<float>> perEvent = new Dictionary<string, Queue<float>>();
        const float EventWindow = 1f; const int EventMax = 6;
        void OnCollisionEnter(Collision c)
        {
            if (Time.time - last < 0.15f || c.contactCount == 0) return;
            var root = transform.root;
            if (c.transform && c.transform.root == root) return;
            int rid = root.GetInstanceID();
            if (perRoot.TryGetValue(rid, out var st) && Time.time - st.last > 3f) st = (0f, 0);         // pooled ragdolls: a new fall, a new budget
            if (st.n > 0 && (Time.time - st.last < RootGap || st.n >= RootMax)) return;
            // chunks): at most EventMax impacts of one event in any EventWindow seconds
            if (!perEvent.TryGetValue(eventId ?? "", out var q)) perEvent[eventId ?? ""] = q = new Queue<float>();
            while (q.Count > 0 && Time.time - q.Peek() > EventWindow) q.Dequeue();
            if (q.Count >= EventMax) return;
            q.Enqueue(Time.time);
            if (perRoot.Count > 512) perRoot.Clear();
            perRoot[rid] = (Time.time, st.n + 1);
            last = Time.time;
            var p = c.GetContact(0).point; float v = c.relativeVelocity.magnitude;
            var g = ZGame.I;
            if (!splashed && splashId != null && g != null && g.World != null && g.World.Map != null)
            {   // b27: under a drawn water surface (not: on a water tile, which may be a bridge deck or a dry bank) the shared water landing rule
                // answers: silent for a piece it holds (that piece splashed at the surface), a splash at the surface with this sound for any other body
                if (OzWaterLanding.RelaySplash(transform, p, splashId)) { splashed = true; return; }
            }
            AudioDirector.Impact(eventId, p, v, GetInstanceID());
        }
    }
}
