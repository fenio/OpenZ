using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    ///   cannot beat the -18 LUFS voices, plus category buses: every weapon / explosion / impact / collapse lands >= 6 dB under
    ///   Alan / Rachel at every zoom (closest view, dead centre) and >= 12 dB while a line plays (SFX duck -6 dB);
    /// - Zod's rule that only sources in or near the view are heard. Only UI and voices are 2D.
    /// clamp(h x .08, 3, 14) m over the focus (was x .2, 6-30: nothing on the ground was ever at full level); world / foley keys use the
    /// RTS "presence" curves (full level within 6 m, -8 dB at the screen edge of the default view, silent at 150 m, x zoom); quiet
    /// off-screen combat is quieter and muffled instead of culled (the "distant battle" rule, up to 1.5 screens out).
    /// </summary>
    public class CombatAudio : MonoBehaviour
    {
        public static CombatAudio I { get; private set; }
        [Range(0, 1)] public float volume = 0.8f;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly Dictionary<(string, int), float> last = new Dictionary<(string, int), float>();
        public int Played { get; private set; }
        public readonly Dictionary<string, int> PlayCounts = new Dictionary<string, int>();

        // ------------------------------------------------------------------ mix (LEGACY_MATCH.md table 6)
        public enum Bus { Weapons, Explosions, Impacts, Destruction, Vehicles, World }
        public enum Profile { Local, Field, Broad, PresenceS, Presence, PresenceL }   // audio_v2: + the RTS presence curves (world / foley / ambience)
        /// <summary>Linear bus gains under the voices (Alan / Rachel play 2D at .85-1.0 x volume, never attenuated).</summary>
        public static float BusGain(Bus b)
        {
            switch (b)
            {
                // audio_v2: the lower listener rig makes the screen centre 3-6 dB louder at the default view, so the combat buses come
                case Bus.Weapons: return 0.35f;
                case Bus.Explosions: return 0.56f;
                case Bus.Impacts: return 0.40f;      // -8.0 dB (battle lift; was .34, -9.4 dB)    (was .31: the subtle metal hits were inaudible)
                case Bus.Destruction: return 0.45f;
                case Bus.Vehicles: return 0.60f;     // -4.4 dB    (was .55)
                default: return 0.70f;               // world / foley -3.1 dB
            }
        }
        public struct KeyInfo { public Bus bus; public Profile prof; public float range; public int prio, maxVoices; }
        static KeyInfo K(Bus b, Profile p, float r, int prio, int max) => new KeyInfo { bus = b, prof = p, range = r, prio = prio, maxVoices = max };
        /// <summary></summary>
        public static KeyInfo Info(string key)
        {
            switch (key)
            {
                case "rifle": case "burst": case "bolt": case "hero_shot": return K(Bus.Weapons, Profile.Local, 100, 1, 4);   // item 225: the hero squad's shot (OzHeroShotSound), the bolt's profile
                case "mg": return K(Bus.Weapons, Profile.Local, 100, 1, 6);                          // weapons_v2: psycho MG bursts (one clip per burst; tails overlap)
                case "sniper_tail": return K(Bus.Weapons, Profile.Field, 150, 1, 2);                 // weapons_v2: the sniper's valley echo (carries further)
                case "backblast": return K(Bus.Weapons, Profile.Local, 100, 2, 2);                   // weapons_v2: bazooka back-blast
                case "servo": return K(Bus.World, Profile.PresenceS, 100, 1, 2);                     // weapons_v2: bazooka raise, sniper kneel / prone foley
                case "sniper": return K(Bus.Weapons, Profile.Local, 100, 2, 2);
                case "laser": return K(Bus.Weapons, Profile.Local, 100, 1, 3);
                case "laser_hit": return K(Bus.Impacts, Profile.Local, 85, 3, 2);
                case "flame": return K(Bus.Weapons, Profile.Field, 110, 2, 2);
                case "cannon": case "rocket": return K(Bus.Weapons, Profile.Local, 100, 2, 3);
                case "mech_rocket": case "mc_rocket": return K(Bus.Weapons, Profile.Local, 100, 2, 3);   // sound_v3: the mech pods / missile-cannon roar no longer steal the Tough's 3 "rocket" voices
                case "mech_flight": return K(Bus.Weapons, Profile.Local, 95, 2, 3);                      // sound_v3: mech / missile-cannon rockets in flight
                case "shell_flight": return K(Bus.Weapons, Profile.Local, 90, 1, 2);                     // sound_v3: tank shells in flight
                case "launch": return K(Bus.Weapons, Profile.Local, 100, 2, 2);
                case "rocket_flight": return K(Bus.Weapons, Profile.Local, 100, 2, 3);
                case "grenade": return K(Bus.Weapons, Profile.Local, 100, 2, 2);
                // under every blast): cannon booms (tank guns, the GUN cannon, howitzer, fort guns) sit on the explosions bus
                case "tankshot": return K(Bus.Explosions, Profile.Field, 110, 3, 3);
                case "heavyshot": return K(Bus.Explosions, Profile.Field, 110, 3, 2);
                case "fortgun": return K(Bus.Explosions, Profile.Field, 135, 4, 2);
                case "fortgun_boom": return K(Bus.Explosions, Profile.Broad, 160, 5, 2);
                case "howitzer_boom": return K(Bus.Explosions, Profile.Field, 130, 4, 3);
                case "gunshot": return K(Bus.Explosions, Profile.Field, 115, 3, 3);                // GUN cannon / howitzer / fort gun
                case "shell_whoosh": return K(Bus.Weapons, Profile.Local, 90, 4, 3);
                case "shell_whistle": return K(Bus.Explosions, Profile.Field, 120, 4, 3);          // NB2: incoming howitzer shells               // mlrs_heavy_v1: the heavy tank's own voices
                case "mlrs_salvo": return K(Bus.Weapons, Profile.Local, 100, 2, 4);              // mlrs_heavy_v1: the 7 cosmetic ripple launches (4 voices: every rocket heard)
                case "mlrs_whoosh": return K(Bus.Weapons, Profile.Local, 95, 3, 4);
                case "mlrs_salvo_blast": return K(Bus.Explosions, Profile.Field, 115, 4, 4);     // mlrs_heavy_v1: their landings, under real blasts (prio 5)
                case "explosion": return K(Bus.Explosions, Profile.Field, 115, 5, 4);
                case "heavy": case "vdeath": return K(Bus.Explosions, Profile.Field, 135, 5, 3);
                case "fortboom": return K(Bus.Explosions, Profile.Broad, 150, 5, 3);
                case "nuke": return K(Bus.Explosions, Profile.Broad, 400, 6, 2);                    // fort death: heard across the map
                case "tank_impact": case "debris": return K(Bus.Impacts, Profile.Field, 110, 3, 3);
                case "rocket_armor": return K(Bus.Impacts, Profile.Field, 110, 3, 2);
                case "ricochet": case "sniped": case "driverhit": return K(Bus.Impacts, Profile.Local, 100, 3, 2);
                case OpenZHitSparks.SoundKey: return K(Bus.Impacts, Profile.Local, 90, 1, 3);          // hitsparks_v1: varied bullet ricochets / armour pings
                case "rock": return K(Bus.Destruction, Profile.Field, 110, 4, 2);
                case "collapse": return K(Bus.Destruction, Profile.Broad, 150, 4, 2);
                case "settle": case "weld": return K(Bus.Destruction, Profile.Field, 110, 2, 2);
                case "prop": return K(Bus.Destruction, Profile.Local, 100, 2, 3);
                case "mech_step": return K(Bus.Vehicles, Profile.Field, 85, 3, 2);
                case MechFoley.ServoKey: return K(Bus.Vehicles, Profile.Field, 85, 2, 2);
                case MechFoley.StompKey: return K(Bus.Vehicles, Profile.Field, 95, 3, 2);
                case "engine_start": return K(Bus.Vehicles, Profile.Field, 85, 3, 2);
                case "engine": return K(Bus.Vehicles, Profile.Field, 85, 2, 4);
                // audio_v2: small hits were on the World gunshot curve (default), world cues now on the presence curves
                case "metal_ping": case "pickup": case "grenade_land": return K(Bus.Impacts, Profile.Field, 110, 1, 2);
                case "turret": return K(Bus.Impacts, Profile.Field, 110, 3, 2);
                case RobotPopDeath.SoundKey: return K(Bus.Destruction, Profile.Field, 110, 3, 3);   // deaths_mech_v1: robot pop deaths (rp_pop_a-f)
                case "wreck_crash": return K(Bus.Destruction, Profile.Field, 120, 4, 2);          // deaths_mech_v1: a flipped wreck lands
                case "flag": case "ter_flag_capture": case "ter_zone_gained": return K(Bus.World, Profile.PresenceL, 150, 3, 2);
                default: return K(Bus.World, Profile.Presence, 150, 2, 3);                                  // doors, built, swim, capture, repair ...
            }
        }
        [Tooltip("SFX duck while Alan / Rachel speak (dB)")] public float voiceDuckDb = -6f;
        [Tooltip("SFX attenuation at the full overview (dB), 0 at the close view")] public float overviewDb = -7f;
        [Tooltip("Pitch variance of the presentation samples (+-)")] public float pitchVariance = 0.035f;
        [Tooltip("Loudness leveller: clips louder than this (integrated LUFS) are turned down ...")] public float levelRefLufs = -16f;
        [Tooltip("... by (LUFS - ref) x (1 - 1 / ratio); 2 keeps the legacy order (heavy tank still above the light tank)")] public float levelRatio = 2f;
        [Header("audio_v2")]
        [Tooltip("Listener rig height over the focus = clamp(camera height x k, min, max) m (was x .2, 6-30)")] public float rigHeightK = 0.08f, rigMin = 3f, rigMax = 14f;
        [Tooltip("World / vehicle SFX duck while a line from the legacy CombatAudio.Voice plays (dB)")] public float legacyWorldDuckDb = -2f;
        [Tooltip("Off-screen combat (weapons, explosions, destruction) up to distantBand screens beyond the 35 % margin: quieter + muffled instead of culled")] public bool distantBattle = true;
        public float distantBand = 1.5f, distantNearDb = -8f, distantFarDb = -16f, distantNearHz = 2600f, distantFarHz = 1100f, distantGunGap = 0.3f, distantVirtualM = 12f;
        public int distantMax = 3;
        /// <summary>audio_v2: extra linear gain per key (the fort moment is the loudest of the match; the subtle metal hits reach the floor).</summary>
        public static readonly Dictionary<string, float> KeyGain = new Dictionary<string, float> { ["nuke"] = 1.8f, ["fortboom"] = 1.3f, ["metal_ping"] = 2.5f };
        /// <summary>
        /// sound (Resources/OpenZ/Audio/Sfx; swim: a lifted copy). value = (resource, integrated LUFS for the leveller).</summary>
        public static readonly Dictionary<string, (string res, float lufs)> Lifts = new Dictionary<string, (string, float)>
        {
            ["flag-capture-1"] = ("OpenZ/Audio/Sfx/flag-capture-1", -21f), ["spawn"] = ("OpenZ/Audio/Sfx/flag-capture-1", -21f),
            ["v3-armor-ricochet-a"] = ("OpenZ/Audio/Sfx/v3-armor-ricochet-a", -15f), ["v3-grenade-bounce-a"] = ("OpenZ/Audio/Sfx/v3-grenade-bounce-a", -19.3f),
            ["exit"] = ("OpenZ/Audio/Sfx/exit", -20.7f), ["swim"] = ("OpenZ/Audio/Sfx/legacy_swim_lift", -18.7f),
            ["electro-zip-2"] = ("OpenZ/Audio/Sfx/electro-zip-2", -16.7f), ["electro-zip-3"] = ("OpenZ/Audio/Sfx/electro-zip-3", -12.6f),
            // ripple / missile-cannon launch was silent. The sound pack's copy of the SAME take (corr .999) is +8.9 dB (I -18.7, M max -17.0)
            ["mlrs-launch-v2-20260909"] = ("OpenZ/Audio/Sfx/mlrs-launch-v2-20260909", -18.7f),
            // Clip() looked in Sfx/ and every hatch OPEN dropped silent ("DROP clip not loaded"); levelled like the close clank (ref -12.1)
            ["s3_hatch_open_a"] = ("OpenZ/Audio/Sfx/SoundV3/s3_hatch_open_a", -12.1f), ["s3_hatch_open_b"] = ("OpenZ/Audio/Sfx/SoundV3/s3_hatch_open_b", -12.1f),
            ["s3_hatch_open_c"] = ("OpenZ/Audio/Sfx/SoundV3/s3_hatch_open_c", -12.1f),
        };
        /// <summary>
        /// launch takes to the loudest one's momentary max after the leveller (v3 -2.4 - 5.65 = -8.1; v1 -10.3 - 1.65 = -12.0 -> +3.9 dB;
        /// v2 lifted copy -17.0, no trim -> +8.9 dB; ffmpeg ebur128). Every user of the bank (MLRS rocket 1 + ripple, missile cannon).</summary>
        public static readonly Dictionary<string, float> TakeGain = new Dictionary<string, float>
        {
            ["mlrs-launch-v1-20260909"] = 1.57f, ["mlrs-launch-v2-20260909"] = 2.79f,
            ["s3_hatch_open_a"] = 1.62f, ["s3_hatch_open_b"] = 1.58f, ["s3_hatch_open_c"] = 2.11f,   // to the -13.6 LUFS metal-clank reference (measured -17.8 / -17.6 / -20.1)
            // incoming whistles to -7.8 LUFS, booms to -10.7 LUFS
            ["oz_incoming_fort_a"] = 1.11f, ["oz_incoming_fort_b"] = 1.22f, ["oz_incoming_fort_c"] = 1f, ["oz_incoming_fort_d"] = 1.06f, ["oz_incoming_fort_e"] = 1.33f,
            ["oz_incoming_howitzer_a"] = 1f, ["oz_incoming_howitzer_b"] = 1.17f, ["oz_incoming_howitzer_c"] = 1.27f, ["oz_incoming_howitzer_d"] = 1.12f, ["oz_incoming_howitzer_e"] = 1f,
            ["oz_boom_fort_a"] = 1.27f, ["oz_boom_fort_b"] = 1f, ["oz_boom_fort_c"] = 1.08f, ["oz_boom_howitzer_a"] = 1.22f, ["oz_boom_howitzer_b"] = 1.06f, ["oz_boom_howitzer_c"] = 1.27f,
        };
        void ApplyLifts()
        {
            foreach (var kv in Lifts)
            {
                var c = Resources.Load<AudioClip>(kv.Value.res); if (!c) continue;
                clips[kv.Key] = c; ClipLufs[c.name] = kv.Value.lufs;
            }
        }

        /// <summary>Integrated loudness of every non-voice clip in Resources/OpenZ/Sfx + the legacy_match clips (ffmpeg ebur128,
        /// short clips looped x4; measured 26 Sep 2026). Voices (alan_*, rachel_*) are all -18 LUFS and never levelled.</summary>
        public static readonly Dictionary<string, float> ClipLufs = new Dictionary<string, float>
        {
            ["rocket"] = -4.0f, ["heavy-tank-4"] = -4.4f, ["heavy-tank-3"] = -4.7f, ["mlrs-launch-v3-20260909"] = -4.7f, ["heavy-tank-1"] = -4.9f,
            ["heavy-tank-5"] = -5.3f, ["heavy-tank-2"] = -6.0f, ["nuke-descent-20260909"] = -7.1f, ["bolt2"] = -7.1f, ["v3-rocket-armor-a"] = -7.3f,
            ["legacy-nuke"] = -7.4f, ["legacy-nuke-long"] = -6.2f, ["heavy"] = -8.2f, ["building-settle-20260909"] = -8.3f, ["v3-collapse-large-b"] = -8.8f,
            ["bolt3"] = -9.3f, ["v3-collapse-large-a"] = -9.4f, ["building-collapse-20260909"] = -9.5f, ["bolt1"] = -9.5f,
            ["v3-engine-start-a"] = -9.9f, ["explosion"] = -10.2f, ["v3-rifle-burst-a"] = -10.3f, ["v3-rifle-burst-b"] = -10.4f,
            ["v3-rifle-burst-c"] = -10.5f, ["v3-rifle-single-b"] = -10.5f, ["v3-rifle-single-a"] = -11.4f, ["grenade"] = -11.6f,
            ["atom-repair-wrench"] = -11.8f, ["v3-capture-raise"] = -12.0f, ["tank-loop"] = -12.0f, ["tank"] = -12.0f,
            ["final-metal-land-c"] = -12.1f, ["final-metal-land-a"] = -12.2f, ["v3-collapse-medium-a"] = -12.3f, ["cannon"] = -12.4f,
            ["cannon-1"] = -12.4f, ["collapse"] = -12.5f, ["v3-rock-break-a"] = -12.5f, ["mlrs-launch-v1-20260909"] = -12.7f,
            ["v3-rifle-single-c"] = -13.0f, ["explosion-v1-20260909"] = -13.5f, ["v3-capture-start"] = -13.5f, ["v3-tank-shot-b"] = -13.5f,
            ["v3-tank-impact-a"] = -14.2f, ["legacy-sniper"] = -15.6f, ["flame_loop1"] = -15.7f, ["v3-collapse-small-b"] = -15.7f,
            ["bolt4"] = -15.8f, ["explosion-v2-20260909"] = -16.0f, ["v3-flag-success"] = -16.7f, ["v3-tank-impact-b"] = -16.9f,
            ["car-loop"] = -17.3f, ["car"] = -17.3f, ["laser-impact-electric-a"] = -17.5f, ["laser-impact-electric-b"] = -17.9f,
            ["v3-tank-impact-c"] = -18.1f, ["v3-collapse-medium-b"] = -18.7f, ["mech-servo-2"] = -18.9f, ["death"] = -19.5f,
            ["mech-servo-1"] = -19.6f, ["final-metal-land-b"] = -19.7f, ["vehicle-capture-sep11"] = -19.9f, ["rocket-blast-sep11"] = -20.8f,
            ["enter"] = -22.2f, ["explosion-v3-20260909"] = -22.9f, ["v3-rock-break-b"] = -24.3f, ["swim"] = -25.2f,
            ["v3-collapse-small-a"] = -25.7f, ["exit"] = -26.5f, ["mlrs-launch-v2-20260909"] = -27.5f, ["flag-capture-1"] = -29.0f,
            ["spawn"] = -29.0f, ["v3-armor-ricochet-a"] = -29.1f, ["v3-grenade-bounce-a"] = -31.6f,
            [PlacementPop.PlacedClip] = -15.1f, [PlacementPop.PopClip] = -13.5f,
        };

        /// <summary>Linear leveller gain for a clip (1 for unknown / quieter clips).</summary>
        public float LevelTrim(string clipName)
        {
            if (clipName == null || !ClipLufs.TryGetValue(clipName, out var lufs) || lufs <= levelRefLufs) return 1f;
            return Mathf.Pow(10f, -(lufs - levelRefLufs) * (1f - 1f / Mathf.Max(1f, levelRatio)) / 20f);
        }

        class Src { public AudioSource s; public AudioLowPassFilter lp; public string key; public float baseVol, start; public int prio; public Bus bus; public Profile prof; public bool distant; }
        readonly List<Src> pool = new List<Src>();
        readonly List<AudioSource> pool2D = new List<AudioSource>();
        int next2D;
        float duck = 1f, duckWorld = 1f, lastDistantGun = -10f;                                                   // audio_v2: per-bus duck
        readonly List<(float until, float c, float w)> ducks = new List<(float, float, float)>();
        RtsCamera rts; Transform rig; float rigRetryAt;
        public float ZoomGain { get; private set; } = 1f;
        public float RangeScale { get; private set; } = 1f;
        public float Duck => duck;
        /// <summary>audio_v2: the duck on the world / vehicle side (engines, doors, foley): lighter than on the combat buses.</summary>
        public float DuckWorld => duckWorld;
        public float DuckOf(Bus b) => b == Bus.World || b == Bus.Vehicles ? duckWorld : duck;
        public Transform ListenerRig => rig;
        /// <summary>DC11: duck every SFX by voiceDuckDb for `seconds` (AudioDirector calls it for each Alan / Rachel line it plays).</summary>
        public void DuckFor(float seconds) => DuckFor(seconds, voiceDuckDb, legacyWorldDuckDb);
        /// <summary>audio_v2: duck the combat buses by combatDb and the world / vehicle buses by worldDb for `seconds` (Alan -4 / -1,
        /// Rachel -6 / -2; AudioDirector.PlayVoice). The deepest active request wins.</summary>
        public void DuckFor(float seconds, float combatDb, float worldDb) { ducks.Add((Time.unscaledTime + seconds, combatDb, worldDb)); }

        static AnimationCurve[] gainCurves, lpCurves;

        void Awake()
        {
            I = this;
            foreach (var c in Resources.LoadAll<AudioClip>("OpenZ/Sfx")) clips[c.name] = c;
            ApplyLifts();                                                                                    // audio_v2
            BuildCurves();
            for (int i = 0; i < 24; i++)
            {
                var go = new GameObject("Sfx" + i); go.transform.SetParent(transform, false);
                var a = go.AddComponent<AudioSource>(); a.playOnAwake = false; a.spatialBlend = 1f; a.dopplerLevel = 0; a.spread = 60f;
                var lp = go.AddComponent<AudioLowPassFilter>();
                pool.Add(new Src { s = a, lp = lp, key = "" });
                Configure(a, lp, Profile.Local, 100f);
            }
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Sfx2D" + i); go.transform.SetParent(transform, false);
                var a = go.AddComponent<AudioSource>(); a.playOnAwake = false; a.spatialBlend = 0f; a.dopplerLevel = 0; a.priority = 32;
                pool2D.Add(a);
            }
            if (!GetComponent<LegacyEngineAudio>()) gameObject.AddComponent<LegacyEngineAudio>();
        }

        static void BuildCurves()
        {
            if (gainCurves != null) return;
            gainCurves = new AnimationCurve[6]; lpCurves = new AnimationCurve[6];
            float[] refRange = { 100f, 115f, 150f };
            for (int p = 3; p < 6; p++)
            {   // audio_v2 presence curves on 150 m (x RangeScale): full level within 6 m (the listener rig), -12 / -8 / -5 dB at 45 m
                // (the screen edge at the default 70 m view), then on, silent at 150 m; gentle low-pass with distance
                var g = new AnimationCurve(); var l = new AnimationCurve();
                float edgeDb = p == 3 ? -12f : p == 4 ? -8f : -5f;
                for (int i = 0; i <= 48; i++)
                {
                    float x = i / 48f, d = x * 150f;
                    float gain = d <= 6f ? 1f : Mathf.Pow(10f, edgeDb * Mathf.Pow((d - 6f) / 39f, 0.9f) / 20f);
                    gain *= 1f - Mathf.Pow(x, 6f);
                    g.AddKey(x, gain); l.AddKey(x, Mathf.Clamp01((2500f + 19500f * Mathf.Pow(1f - x, 1.6f)) / 22000f));
                }
                gainCurves[p] = g; lpCurves[p] = l;
            }
            for (int p = 0; p < 3; p++)
            {
                var g = new AnimationCurve(); var l = new AnimationCurve();
                for (int i = 0; i <= 32; i++)
                {
                    float x = i / 32f, d = x * refRange[p], gain, cutoff;
                    if (p == 0)
                    {
                        float b = Mathf.Max(0f, d - 5f), inv = 1f / (1f + b * 0.07f + b * b * 0.006f), hor = Mathf.Max(0.05f, 1f - Mathf.Pow(x, 3.2f));
                        gain = d <= 5f ? 1f : Mathf.Max(0.012f, inv * hor);
                        float prog = Mathf.Clamp01((d - 5f) / Mathf.Max(1f, refRange[p] - 5f));
                        cutoff = 900f + 17100f * Mathf.Pow(1f - prog, 1.7f);
                    }
                    else if (p == 1)
                    {   // fieldSoundMix (:341-345)
                        gain = Mathf.Pow(1f - x, 1.1f) / (1f + Mathf.Max(0f, d - 5f) * 0.022f);
                        cutoff = 4000f + 14000f * Mathf.Pow(1f - x, 0.8f);
                    }
                    else
                    {   // collapseSoundMix (:335-338)
                        gain = Mathf.Pow(1f - x, 0.55f) / (1f + Mathf.Max(0f, d - 5f) * 0.009f);
                        cutoff = 5000f + 13000f * Mathf.Pow(1f - x, 0.8f);
                    }
                    g.AddKey(x, gain); l.AddKey(x, Mathf.Clamp01(cutoff / 22000f));
                }
                gainCurves[p] = g; lpCurves[p] = l;
            }
        }

        /// <summary></summary>
        public void Configure(AudioSource a, AudioLowPassFilter lp, Profile prof, float range)
        {
            BuildCurves();
            a.spatialBlend = 1f; a.rolloffMode = AudioRolloffMode.Custom;
            a.minDistance = 1f; a.maxDistance = Mathf.Max(8f, range);
            a.SetCustomCurve(AudioSourceCurveType.CustomRolloff, gainCurves[(int)prof]);
            if (lp) lp.customCutoffCurve = lpCurves[(int)prof];
        }

        /// <summary>A clip by name: OpenZ/Sfx (loaded at Awake), else the sound-design set in OpenZ/Audio/Sfx (sd_*, loaded on first use;
        /// NB2 found the howitzer whistle silent because only OpenZ/Sfx was ever loaded).</summary>
        public AudioClip Clip(string name)
        {
            if (name == null) return null;
            if (clips.TryGetValue(name, out var c)) return c;
            if (missing.Contains(name)) return null;
            c = Resources.Load<AudioClip>("OpenZ/Audio/Sfx/" + name);
            if (c) clips[name] = c; else missing.Add(name);
            return c;
        }
        readonly HashSet<string> missing = new HashSet<string>();
        public bool HasClip(string name) => Clip(name) != null;

        /// <summary></summary>
        public float LoopBusGain => volume * BusGain(Bus.Vehicles) * ZoomGain * duckWorld;                 // audio_v2: engines take the world duck
        static float StepDuck(float cur, float target)
        {   // attack .1 s, release .6 s
            float tc = target < cur ? 0.1f : 0.6f;
            return Mathf.Lerp(cur, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime / tc));
        }

        /// <summary>DC11: set by AudioDirector; returns true when it played the UI sound itself (HUD click, rally flag click).</summary>
        public static System.Func<string, float, bool> Play2DRoute;
        /// <summary>Non-positional one-shot (nuke, flag capture of the player's zones, UI): always audible.</summary>
        public void Play2D(string name, float vol)
        {
            if (Play2DRoute != null && Play2DRoute(name, vol)) return;
            if (Play2DRoute == null && name != null && name.StartsWith("ui_") && AudioDirector.EnsureLive()) { AudioDirector.UI(name); return; }   // audio_v2: no director yet -> boot it
            var clip = Clip(name); if (!clip) return;
            var src = pool2D[next2D]; next2D = (next2D + 1) % pool2D.Count;
            src.clip = clip; src.volume = vol * volume; src.pitch = 1f;
            src.Play();
            Played++; PlayCounts[name] = PlayCounts.TryGetValue(name, out var n) ? n + 1 : 1;
        }
        // ------------------------------------------------------------------ voices (plan/05: robots = Alan, announcer = Rachel)
        public static readonly string[] AlanSelect = { "alan_yes_sir_1", "alan_yes_sir_2", "alan_okay_1", "alan_okay_2" };
        public static readonly string[] AlanOrder = { "alan_on_my_way_1", "alan_on_my_way_2", "alan_moving_out_1", "alan_moving_out_2", "alan_right_away_1",
            "alan_right_away_2", "alan_you_got_it_1", "alan_you_got_it_2", "alan_lets_go_lads_1", "alan_lets_go_lads_2", "alan_yeehaw_1", "alan_yeehaw_2" };
        AudioSource alanSrc, rachelSrc;
        readonly Dictionary<string, string> lastVoice = new Dictionary<string, string>();

        /// <summary>DC11: set by AudioDirector (one Alan, one Rachel): true / false = handled there, null = play it here.</summary>
        public static System.Func<string, string[], float, bool, bool?> VoiceRoute;
        /// <summary>2D voice line on its own filtered source: Alan (robots) gets a light metallic radio filter, Rachel
        /// (announcer) a radio high-pass. Random clip, never the same one twice in a row, per-key throttle. SFX duck under it.</summary>
        public bool Voice(string key, string[] names, float vol, float throttle, bool alan)
        {
            if (VoiceRoute != null) { var routed = VoiceRoute(key, names, vol, alan); if (routed.HasValue) return routed.Value; }
            if (throttle > 0 && last.TryGetValue((key, -7), out var t) && Time.unscaledTime - t < throttle) return false;
            var avail = new List<string>(); foreach (var n in names) if (Clip(n)) avail.Add(n);
            if (avail.Count == 0) return false;
            if (avail.Count > 1 && lastVoice.TryGetValue(key, out var prev)) avail.Remove(prev);
            var name = avail[Random.Range(0, avail.Count)]; lastVoice[key] = name;
            var src = alan ? (alanSrc ??= VoiceSource("VoiceAlan", 250f, true)) : (rachelSrc ??= VoiceSource("VoiceRachel", 200f, false));
            src.Stop(); src.clip = clips[name]; src.volume = vol * volume; src.Play();
            DuckFor(src.clip.length + 0.15f);                                                               // audio_v2: per-bus duck
            last[(key, -7)] = Time.unscaledTime;
            Played++; PlayCounts[key] = PlayCounts.TryGetValue(key, out var c) ? c + 1 : 1;
            return true;
        }

        AudioSource VoiceSource(string name, float highPass, bool metallic)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var a = go.AddComponent<AudioSource>(); a.playOnAwake = false; a.spatialBlend = 0; a.dopplerLevel = 0; a.priority = 16;
            go.AddComponent<AudioHighPassFilter>().cutoffFrequency = highPass;
            if (metallic) { go.AddComponent<AudioDistortionFilter>().distortionLevel = 0.12f; var ch = go.AddComponent<AudioChorusFilter>(); ch.dryMix = 0.8f; ch.wetMix1 = 0.25f; ch.wetMix2 = 0.2f; ch.wetMix3 = 0f; ch.delay = 8f; ch.rate = 0.6f; ch.depth = 0.05f; }
            return a;
        }

        // ------------------------------------------------------------------ listener rig, zoom, duck
        void EnsureRig()
        {
            if (rig || Time.unscaledTime < rigRetryAt) return;
            rigRetryAt = float.MaxValue;
            // DC11: CombatAudio keeps the listener rig and the master compressor when AudioDirector is imported (it reads ListenerRig)
            rts = FindFirstObjectByType<RtsCamera>(); if (!rts) { rigRetryAt = Time.unscaledTime + 1f; return; }   // no camera yet: retry each second
            var go = new GameObject("AudioListenerRig"); rig = go.transform;
            foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) l.enabled = false;
            go.AddComponent<AudioListener>();
            go.AddComponent<LegacyMixCompressor>();
            go.AddComponent<OzLoudnessMeter>();                                                              // audio_v2: after the master = what the player hears
        }

        void LateUpdate()
        {
            EnsureRig();
            if (!rts) rts = FindFirstObjectByType<RtsCamera>();
            float h = rts ? rts.height : 70f;
            if (rig && rts)
            {
                var f = rts.focus; float gy = ZGame.I ? ZGame.I.GroundY(f.x, f.z) : 0f;
                rig.position = new Vector3(f.x, gy + Mathf.Clamp(h * rigHeightK, rigMin, rigMax), f.z);   // audio_v2: was clamp(h x .2, 6, 30)
                rig.rotation = Quaternion.Euler(0f, rts.transform.eulerAngles.y, 0f);
            }
            float lo = RtsCamera.SpanClosest, hi = RtsCamera.SpanWidest;                                    // the old zoom span 22..230 m: the same gain at every height as before the new zoom limits (RtsCamera.ZoomLimits.cs)
            ZoomGain = Mathf.Pow(10f, overviewDb * Mathf.InverseLerp(lo, hi, h) / 20f);
            RangeScale = Mathf.Clamp(h / 70f, 0.6f, 3.3f);
            float nowU = Time.unscaledTime, cDb = 0f, wDb = 0f;                                             // audio_v2: per-bus duck
            for (int i = ducks.Count - 1; i >= 0; i--) { if (ducks[i].until < nowU) { ducks.RemoveAt(i); continue; } cDb = Mathf.Min(cDb, ducks[i].c); wDb = Mathf.Min(wDb, ducks[i].w); }
            duck = StepDuck(duck, Mathf.Pow(10f, cDb / 20f)); duckWorld = StepDuck(duckWorld, Mathf.Pow(10f, wDb / 20f));
            foreach (var v in pool) if (v.s.isPlaying) v.s.volume = v.baseVol * ZoomGain * DuckOf(v.bus);
        }

        // ------------------------------------------------------------------ one-shots
        Src Pick(string key, KeyInfo ki)
        {
            int live = 0; Src oldestSame = null;
            foreach (var v in pool)
                if (v.s.isPlaying && v.key == key) { live++; if (oldestSame == null || v.start < oldestSame.start) oldestSame = v; }
            if (live >= ki.maxVoices) return oldestSame;                                                     // per-event limit: steal the oldest
            foreach (var v in pool) if (!v.s.isPlaying) return v;
            Src best = null;
            foreach (var v in pool)
                if (v.prio <= ki.prio && (best == null || v.prio < best.prio || (v.prio == best.prio && v.start < best.start))) best = v;
            return best;
        }

        /// <summary>Play one of `names` (random) at `pos` unless the same key played for this emitter within `throttle` s.</summary>
        public void Play(string key, Vector3 pos, float vol, float throttle, int emitter, params string[] names) => PlayPitched(key, pos, vol, throttle, emitter, 1f, names);

        /// <summary>Positional (3D, distance from the camera) but not culled when off screen: world events everyone must hear
        /// (nuke, flag captures). the project rules: only UI, the announcer and robot voices are 2D.</summary>
        public void PlayWide(string key, Vector3 pos, float vol, params string[] names)
        {
            wide = true;
            try { PlayPitched(key, pos, vol, 0f, -1, 1f, names); } finally { wide = false; }
        }
        bool wide;

        float distantHz = 2000f;
        static readonly Dictionary<int, AnimationCurve> flatCutoff = new Dictionary<int, AnimationCurve>();
        /// <summary>audio_v2: a constant low-pass for the distant-battle voices (bucketed by 100 Hz, cached).</summary>
        static AnimationCurve FlatCutoff(float hz)
        {
            int b = Mathf.RoundToInt(hz / 100f);
            if (!flatCutoff.TryGetValue(b, out var c)) { float v = Mathf.Clamp01(b * 100f / 22000f); c = new AnimationCurve(new Keyframe(0f, v), new Keyframe(1f, v)); flatCutoff[b] = c; }
            return c;
        }

        /// <summary></summary>
        public void PlayPitched(string key, Vector3 pos, float vol, float throttle, int emitter, float pitch, params string[] names)
        {
            lastSrc = null;
            if (names == null || names.Length == 0) { OzSoundLog.Add("CA", key, null, "DROP no clip names", 0f, pos); return; }
            var cam = Camera.main;
            float distantDb = 0f; bool distant = false; Vector3 at = pos;
            if (cam && !wide)
            {
                var vp = cam.WorldToViewportPoint(pos);
                if (vp.z < 0 || vp.x < -0.35f || vp.x > 1.35f || vp.y < -0.35f || vp.y > 1.35f)            // Zod: only sounds in view ...
                {   // ... audio_v2: off-screen combat is quieter and muffled, not gone (the distant-battle rule)
                    var kb = Info(key).bus;
                    if (!distantBattle || vp.z < 0 || !rig || (kb != Bus.Weapons && kb != Bus.Explosions && kb != Bus.Destruction)) { OzSoundLog.Add("CA", key, names[0], "DROP off screen", 0f, pos); return; }
                    float outBy = Mathf.Max(Mathf.Max(-0.35f - vp.x, vp.x - 1.35f), Mathf.Max(-0.35f - vp.y, vp.y - 1.35f));
                    if (outBy > distantBand) { OzSoundLog.Add("CA", key, names[0], "DROP too far off screen", 0f, pos); return; }
                    int live = 0; foreach (var v in pool) if (v.distant && v.s.isPlaying) live++;
                    if (live >= distantMax) { OzSoundLog.Add("CA", key, names[0], "DROP distant cap", 0f, pos); return; }
                    if (kb == Bus.Weapons) { if (Time.unscaledTime - lastDistantGun < distantGunGap) { OzSoundLog.Add("CA", key, names[0], "DROP distant gun gap", 0f, pos); return; } lastDistantGun = Time.unscaledTime; }
                    float k = Mathf.Clamp01(outBy / Mathf.Max(0.01f, distantBand));
                    distantDb = Mathf.Lerp(distantNearDb, distantFarDb, k); distant = true;
                    var dir = pos - rig.position; dir.y = 0f;
                    at = rig.position + (dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward) * distantVirtualM * RangeScale;
                    distantHz = Mathf.Lerp(distantNearHz, distantFarHz, k);
                }
            }
            if (throttle > 0 && last.TryGetValue((key, emitter), out var t) && Time.time - t < throttle) { OzSoundLog.Add("CA", key, names[0], "DROP throttle", 0f, pos); return; }
            var clip = Clip(names.Length > 1 ? OzSoundV3.PickName(key, names) : names[0]);          // sound_v3: never the last take(s) again
            if (!clip)
            {   // a missing variant falls back to any present one of the bank
                foreach (var n in names) { clip = Clip(n); if (clip) break; }
                if (!clip) { OzSoundLog.Add("CA", key, string.Join("|", names), "DROP clip not loaded", 0f, pos); return; }
            }
            last[(key, emitter)] = Time.time;
            var ki = Info(key);
            float g = vol * volume * BusGain(ki.bus) * LevelTrim(clip.name) * (KeyGain.TryGetValue(key, out var kg) ? kg : 1f) * OzMixTrim.CaLin(key);
            if (TakeGain.TryGetValue(clip.name, out var tg)) g *= tg;
            if (distant) g *= Mathf.Pow(10f, distantDb / 20f);
            if (ki.bus == Bus.Explosions)
            {   // many blasts at once: 1/sqrt(n) (the M12 busy-shake rule), on top of the master compressor
                int n = 0; foreach (var v in pool) if (v.s.isPlaying && v.bus == Bus.Explosions && Time.time - v.start < 0.35f) n++;
                g /= Mathf.Sqrt(n + 1);
            }
            var src = Pick(key, distant ? new KeyInfo { bus = ki.bus, prof = ki.prof, range = ki.range, prio = 0, maxVoices = ki.maxVoices } : ki); if (src == null) { OzSoundLog.Add("CA", key, clip.name, "DROP no free voice", g, pos); return; }   // audio_v2
            src.s.Stop();
            if (distant) { Configure(src.s, src.lp, Profile.Field, 115f * RangeScale); if (src.lp) src.lp.customCutoffCurve = FlatCutoff(distantHz); }   // audio_v2
            else Configure(src.s, src.lp, ki.prof, ki.range * RangeScale);
            src.distant = distant;
            src.s.transform.position = at; src.s.clip = clip;
            src.s.pitch = pitch > 0f ? pitch * Random.Range(1f - pitchVariance, 1f + pitchVariance) : 1f;
            src.s.priority = distant ? 240 : 128 - ki.prio * 16;
            src.baseVol = g; src.key = key; src.start = Time.time; src.prio = distant ? 0 : ki.prio; src.bus = ki.bus; src.prof = ki.prof;
            src.s.volume = g * ZoomGain * DuckOf(ki.bus);
            src.s.Play();
            OzSoundLog.Add("CA", key, clip.name, (distant ? "PLAY distant " : "PLAY ") + ki.bus, src.s.volume, at);
            lastSrc = src;
            Played++; PlayCounts[key] = PlayCounts.TryGetValue(key, out var c) ? c + 1 : 1;
        }

        Src lastSrc;
        /// <summary>
        /// then the source flies the projectile's own path (from -> to over `seconds`, the FX ballistic arc with `gravity`), starting
        /// `startAt` s into the clip (past the launch attack the muzzle copy already plays), and fades out over 0.15 s on arrival
        /// (the impact takes over). Off-screen (distant) plays stay on their virtual spot. View only, no sim state.</summary>
        public void PlayAlong(string key, Vector3 from, Vector3 to, float seconds, float gravity, float vol, int emitter, float pitch, float startAt, params string[] names)
        {
            PlayPitched(key, from, vol, 0f, emitter, pitch, names);
            var src = lastSrc; lastSrc = null;
            if (src == null || src.distant || !src.s.clip) return;
            if (startAt > 0f && startAt < src.s.clip.length - 0.1f) src.s.time = startAt;
            StartCoroutine(Along(src, src.start, key, from, to, Mathf.Max(0.05f, seconds), gravity));
        }
        System.Collections.IEnumerator Along(Src src, float start0, string key, Vector3 from, Vector3 to, float seconds, float gravity)
        {
            var v = (to - from) / seconds; v.y += gravity * seconds * 0.5f;
            float t = 0f, fade = 0.15f, vol0 = src.baseVol; bool arrived = false;
            while (src.s.isPlaying && src.key == key && Mathf.Approximately(src.start, start0))       // stolen / restarted: let go
            {
                t += Time.deltaTime;
                float k = Mathf.Min(t, seconds);
                src.s.transform.position = from + v * k + Vector3.down * (0.5f * gravity * k * k);
                if (t > seconds) { if (!arrived) { arrived = true; OzSoundLog.Add("CA", key, src.s.clip ? src.s.clip.name : null, $"ALONG arrived after {seconds:0.00} s, {Vector3.Distance(from, to):0.0} m", src.s.volume, src.s.transform.position); } float f = 1f - (t - seconds) / fade; if (f <= 0f) { src.s.Stop(); yield break; } src.baseVol = vol0 * f; }
                yield return null;
            }
        }
    }
}
