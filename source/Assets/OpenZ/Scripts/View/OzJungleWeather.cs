using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Level 16 weather: tropical rain by DAY, with wind, beside the city level's night storm (OzWeather.cs). ZGame adds it on
    /// level 16. VIEW ONLY: it reads the day clock, the camera and (read only) the map; it never writes anything of the rules
    /// and draws all its numbers from its own generators (OzWeatherPlan.Rng), never the rules' dice, never UnityEngine.Random.
    /// <list type="bullet">
    /// <item>When: OzRainSchedule (View/OzRainPlan.cs): never on the first day, from the second day one roll at each sunrise
    /// (a pause freezes the spell; the level-start briefing holds it).</item>
    /// <item>Wind first: 8 s before the first drops the level's wind turns to the spell's direction and rises to 5 m/s
    /// (OzJungleAir: flags, banners, building parts, the fog and the plants follow it), and it stays 15 s after the last.</item>
    /// <item>Rain: slanted streaks along the spell's wind in the camera's view volume and ring splashes on the ground in view
    /// (two capped particle systems, off when dry, never over the interface); on the water the water shader draws the drop
    /// rings (_OzRain). After the rain: drips under the tree crowns in view and a low steam while the ground dries.</item>
    /// <item>Wet look: the shared globals _OzWetness / _OzRain / _OzWetSky / _OzWetParams (wets in 12 s, dries in 60 s); on the
    /// jungle ground these puddles lie only on road and bare soil, grass only darkens and takes a soft sheen
    /// (Shaders/OpenZJungleWet.hlsl). At 0 every shader is exactly its dry self. The low, worn grass and the gravel paths have
    /// puddles of their own that grow while it rains and dry slowly after it (OzJungleWeather.LowPuddles.cs).</item>
    /// <item>Light: sun at 65 % and toward warm grey, sky light at 92 %, shadows half as strong; the haze 1.8 times thicker
    /// and greyer. No lightning: 2 to 4 soft far rumbles a spell.</item>
    /// <item>Sound (AudioDirector, Ambience): the downpour bed, rain on leaves under it, rain on water and on metal by what
    /// the camera sees, gusts in the trees on the wind's peaks, far thunder, drips after the rain. Five more are named and
    /// silent until their files exist (MISSING_SOUNDS.md of the kit).</item>
    /// <item>Options: the WEATHER switch (OzWeather.Enabled, saved as oz_weather). Off = none of it: no spell starts, a running
    /// one fades in 3 s with its wind, its lean, its wet ground and its sounds. The quiet sway of the leaves is not weather
    /// and stays, as the desert palms' does.</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(210)]
    public sealed partial class OzJungleWeather : MonoBehaviour
    {
        public static OzJungleWeather I { get; private set; }
        public const string BedId = "amb_l16_rain", LeavesId = "amb_l16_rain_leaves", WaterId = "amb_l16_rain_water", MetalId = "amb_l16_rain_metal",
            GustId = "amb_l16_wind_gust", ThunderId = "amb_l16_thunder_far", DripsId = "amb_l16_drips";
        /// <summary>Named, wired and silent until their files exist.</summary>
        public const string WindLoopId = "amb_l16_wind_trees", FirstDropsId = "amb_l16_rain_first_drops", PalmRattleId = "amb_l16_palm_rattle",
            FlagSnapId = "amb_l16_flag_snap", PlanksId = "amb_l16_rain_planks";

        [Header("Rain streaks (camera volume)")]
        public int maxDrops = 6000;
        [Tooltip("Drops alive at full rain in the far layer (capped by maxDrops); the mid and near layers: OzJungleWeather.RainLayers.cs (2,600 in all)")] public int dropsAtFull = 1500;
        public float dropSpeed = 19f, dropLife = 0.85f;
        [Tooltip("Sideways speed of the drops at full rain wind (m/s): 6.5 with 19 down = 19 degrees off the vertical")] public float slantSpeed = 6.5f;
        [Tooltip("Drop width per metre of view distance (thin: a wide drop reads as a white flake by day)")] public float dropWidth = 0.0007f;
        [Tooltip("Streak length per metre of view distance (long thin streaks read as rain at once from the default camera height)")] public float streakLength = 0.012f;
        public float rainGain = 1f;
        [Range(0, 1)] public float rainAlpha = 0.20f;
        [Tooltip("0 = pure light on top (night rain), towards 1 = covers what is behind: by day a streak must cover a little to show on bright ground")]
        [Range(0, 1)] public float streakCover = 1f;
        public Color streakColor = new Color(0.86f, 0.89f, 0.93f);
        [Header("Splashes (visible ground) and drips (under crowns, after the rain)")]
        public int maxSplashes = 1600;
        public float splashesPerSecond = 2400f, splashSize = 0.36f, dripsPerSecond = 60f, dripSize = 0.2f;
        [Tooltip("Splashes started in one frame at most")] public int splashesPerFrame = 120;
        [Tooltip("A drop on a road or a roof leaves a brighter, larger speck (x the alpha and x the size of one on grass)")] public float hardAlpha = 1.3f, hardSize = 1.15f;
        [Tooltip("Open water: the share of the drops that also leave a ring seen from the default camera height (the water itself draws only fine ones), and its size against a ground splash")]
        [Range(0, 1)] public float waterRingShare = 0.6f;
        public float waterRingSize = 2.2f;
        [Header("Wet look")]
        public float wetUpSeconds = 12f, dryDownSeconds = 60f;
        [Range(0, 0.6f)] public float dampDarken = 0.26f;
        [Range(0, 0.6f)] public float puddleCover = 0.25f;
        public float puddleReflect = 0.8f, rippleStrength = 1.2f;
        [Tooltip("Share of the damp darkening that grass and its transitional grass take")] [Range(0.3f, 1f)] public float grassDamp = 0.7f;
        [Range(0.3f, 1f)] public float puddleFilm = 0.55f;
        [Header("Rain light")]
        [Range(0, 0.6f)] public float sunDim = 0.35f;
        [Range(0, 0.4f)] public float ambientDim = 0.08f;
        [Range(0, 0.8f)] public float shadowSoften = 0.5f;
        [Range(0, 1f)] public float greyTint = 0.6f;
        [Header("Audio (x the event's own level)")]
        public float bedVolume = 1f, leavesShare = 0.6f, waterVolume = 0.9f, metalVolume = 0.8f, gustVolume = 0.8f, thunderVolume = 1f, dripsVolume = 1f;
        public float gustGapSeconds = 9f;

        // ------------------------------------------------------------------ state
        ZGame game; DayNight dn; RtsCamera rts; Camera cam;
        OzRainSchedule sched; int salt, forcedCount;
        OzRainSpell spell; float spellClock; bool forced, stopping; float stopK = 1f, stopFade = 3f; bool[] rumbled; bool dropsSounded, windSounded;
        float rain, wet, windK;
        OzWeatherPlan.Rng fx;
        float splashAcc, dripAcc, surfaceAt, crownsAt, waterShare, metalShare, plankShare, waterNow, metalNow, plankNow, lastGust = -100f, gustPrev, gustPrev2;
        readonly List<Vector4> dripCrowns = new List<Vector4>();                    // the tree crowns in view while the ground drips (looked up four times a second)
        readonly HashSet<string> silentLogged = new HashSet<string>();
        public OzRainSchedule Schedule => sched;
        public OzRainSpell Spell => spell;
        public bool SpellActive => spell != null;
        public float Rain => rain;
        public float Wetness => wet;
        public float WindK => windK;
        /// <summary>Seconds after the first drops of the running spell (negative while only the wind blows).</summary>
        public float SpellSeconds => spell != null ? spellClock - spell.WindLead : 0f;
        public int SpellsStarted { get; private set; }
        public int RumblesPlayed { get; private set; }
        public int GustsPlayed { get; private set; }
        public int SplashesEmitted { get; private set; }
        public int DripsEmitted { get; private set; }
        /// <summary>Tree crowns in view the last time the drips looked (0 = no tree in view: nothing drips).</summary>
        public int CrownsInView => dripCrowns.Count;
        public int SplashesOnWater { get; private set; }
        /// <summary>Rings drawn on open water, specks on a roof, specks on a road, buildings whose roof was read (the counted check).</summary>
        public int WaterRings { get; private set; }
        public int RoofSplashes { get; private set; }
        public int RoadSplashes { get; private set; }
        public int RoofsRead => roofList.Count;
        public int Drops => (streaks ? streaks.particleCount : 0) + LayerDrops;
        /// <summary>For the level's own ambience beds (the sound step of the level): how far they step down under the rain (dB, 0 dry .. -6).</summary>
        public static float BedDuckDb => I ? -6f * Mathf.Clamp01(I.rain) : 0f;

        // lighting bookkeeping (DayNight rewrites these every Update)
        bool touched; float sunBaseI, sunWrittenI, shBase, shWritten; Color sunBaseC, sunWrittenC;
        Color ambSkyBase, ambEqBase, ambGrBase, ambSkyW, ambEqW, ambGrW;

        static readonly int IdWet = Shader.PropertyToID("_OzWetness"), IdRain = Shader.PropertyToID("_OzRain"),
            IdWetSky = Shader.PropertyToID("_OzWetSky"), IdWetParams = Shader.PropertyToID("_OzWetParams"), IdJungleWet = Shader.PropertyToID("_OzJungleWet");
        static readonly int IdShape = Shader.PropertyToID("_Shape"), IdOpq = Shader.PropertyToID("_Opaqueness"), IdColor = Shader.PropertyToID("_Color"),
            IdIntensity = Shader.PropertyToID("_Intensity"), IdCore = Shader.PropertyToID("_CoreColor"), IdCoreMix = Shader.PropertyToID("_CoreMix"),
            IdUseVc = Shader.PropertyToID("_UseVertexColor"), IdSoft = Shader.PropertyToID("_SoftFade"), IdLit = Shader.PropertyToID("_Lit"),
            IdEmission = Shader.PropertyToID("_EmissionColor"), IdSquare = Shader.PropertyToID("_Squareness"), IdBandInner = Shader.PropertyToID("_BandInner"),
            IdBandSides = Shader.PropertyToID("_BandSides"), IdFireRamp = Shader.PropertyToID("_FireRamp"), IdFireOpacity = Shader.PropertyToID("_FireOpacity"),
            IdWrap = Shader.PropertyToID("_Wrap");

        Transform follow; ParticleSystem streaks, splashes; ParticleSystemRenderer streakR; Material streakMat, splashMat;
        ParticleSystem.EmitParams ep;
        readonly Dictionary<string, AudioSource> fallback = new Dictionary<string, AudioSource>();

        // ================================================================== lifecycle
        void Awake()
        {
            if (I && I != this) { Destroy(this); return; }
            I = this;
            ResetGlobals();
        }

        void Start()
        {
            game = ZGame.I;
            dn = FindFirstObjectByType<DayNight>();
            rts = FindFirstObjectByType<RtsCamera>();
            cam = Camera.main;
            if (PlayerPrefs.HasKey(OzWeather.SaltPref)) salt = PlayerPrefs.GetInt(OzWeather.SaltPref);            // a fixed salt: the same rain every time (tests)
            else { salt = PlayerPrefs.GetInt(OzWeather.MatchPref, 0) + 1; PlayerPrefs.SetInt(OzWeather.MatchPref, salt); }   // else a new salt per match
            sched = new OzRainSchedule(game ? game.seed : 1, salt, dn ? dn.cycleSeconds : 720f);
            fx = new OzWeatherPlan.Rng(OzWeatherPlan.Mix((ulong)(uint)salt, 0x5350_4C41_5348UL));
            var at = FindFirstObjectByType<Atmosphere>();
            if (at && at.preset != Atmosphere.Preset.Jungle) at.preset = Atmosphere.Preset.Jungle;                 // this level's own air (OzJungleAir.cs)
            BuildRendering();
            Debug.Log($"[OzJungleWeather] live on {(game ? game.level : "?")}: day rain {OzRainDayPlan.Chance:P0} from day {OzRainDayPlan.FirstRainDay}, {OzRainDayPlan.MinSeconds:0}-{OzRainDayPlan.MaxSeconds:0} s, seed {(game ? game.seed : 0)} / salt {salt}, option {(OzWeather.Enabled ? "ON" : "OFF")}, air {(at ? at.preset.ToString() : "none")}");
        }

        void OnDisable() { ResetGlobals(); StopAllAudio(0.2f); }

        void OnDestroy()
        {
            bool mine = I == this;
            if (mine) I = null;
            ResetGlobals();
            if (mine) OzJungleAir.Reset();                                              // nothing of this level's air and wind stays behind
            foreach (var m in new[] { streakMat, splashMat }) if (m) Destroy(m);
        }

        static void ResetGlobals()
        {
            Shader.SetGlobalFloat(IdWet, 0f); Shader.SetGlobalFloat(IdRain, 0f);
            Shader.SetGlobalVector(IdWetSky, Vector4.zero); Shader.SetGlobalVector(IdWetParams, Vector4.zero); Shader.SetGlobalVector(IdJungleWet, Vector4.zero);
            OzJungleAir.RainWindK = 0f; OzJungleAir.Rain = 0f; OzJungleAir.Steam = 0f;
            ResetLowPuddles();
        }

        // ================================================================== hooks for proofs and other steps
        /// <summary>A spell now: the wind rises at once, the first drops fall 8 s later. azimuth = the way the wind goes
        /// (degrees: 0 north, 90 east; below 0 = drawn like a planned one). makeDay: sets the clock to mid-morning when it is
        /// dusk or night.</summary>
        public void ForceSpell(float totalSeconds = 60f, float azimuth = -1f, bool makeDay = true)
        {
            if (!OzWeather.Enabled) { Debug.Log("[OzJungleWeather] WEATHER is off: no spell"); return; }   // off = none of it, not even a forced one
            if (makeDay && dn && dn.timeOfDay > 0.24f && dn.timeOfDay < 0.955f) { dn.timeOfDay = 0.03f; dn.Apply(0.03f); }
            var p = OzRainDayPlan.Forced(OzWeatherPlan.Mix(OzRainDayPlan.DaySeed(game ? game.seed : 1, salt, 9000), (ulong)(++forcedCount)), totalSeconds, azimuth);
            Begin(p, true);
        }
        public static bool Force(float totalSeconds = 60f) { if (!I || !OzWeather.Enabled) return false; I.ForceSpell(totalSeconds); return true; }

        /// <summary>Ends the running spell with a fade (the WEATHER option off: 3 s), rain and wind together.</summary>
        public void StopSpell(float fade = 3f)
        {
            if (spell == null) return;
            stopping = true; stopFade = Mathf.Max(0.1f, fade);
        }

        public string Status()
        {
            float t = dn ? dn.timeOfDay : NightGrade.TimeOfDay;
            var td = sched != null ? sched.Today : null;
            string plan = sched == null ? "no schedule" : td == null ? $"day {sched.Day}: no roll yet" : td.Rain ? $"day {sched.Day}: rain at clock {td.StartClock:F3} ({td.Total:0} s, wind to {td.WindAzimuth:0} deg, {td.Rumbles.Length} rumbles){(sched.TodayUsed ? " used" : "")}" : $"day {sched.Day}: dry";
            return $"[OzJungleWeather] clock {t:F3} play {(sched != null ? sched.PlaySeconds : 0f):0} s | {plan} | {(spell != null ? $"SPELL {(forced ? "forced " : "")}{SpellSeconds:0.0}/{spell.Total:0} s{(stopping ? " stopping" : "")}" : "no spell")} | rain {rain:F2} wet {wet:F2} wind {windK:F2} to ({OzJungleAir.RainWindDir.x:F2}, {OzJungleAir.RainWindDir.y:F2})"
                 + $" | drops {Drops}/{maxDrops} splashes {(splashes ? splashes.particleCount : 0)}/{maxSplashes} (emitted {SplashesEmitted}, on water {SplashesOnWater}, drips {DripsEmitted}; rings on water {WaterRings}, on roofs {RoofSplashes} of {RoofsRead} buildings, on roads {RoadSplashes})"
                 + $" | spells {SpellsStarted} rumbles {RumblesPlayed} gusts {GustsPlayed} | view: water {waterNow:F2} metal {metalNow:F2} planks {plankNow:F2} | option {(OzWeather.Enabled ? "ON" : "OFF")}"
                 + (fallback.Count > 0 ? " | own sound sources in use (the sound table has no events of this level)" : "");
        }

        // ================================================================== frame
        void LateUpdate()
        {
            if (!Application.isPlaying) return;
            float dt = Time.deltaTime;                                                // 0 while paused
            if (!cam) cam = Camera.main;
            if (!game) game = ZGame.I;
            float t = dn ? dn.timeOfDay : NightGrade.TimeOfDay;
            bool briefing = DayNight.HoldWhileBriefing && ZodFlyover.Live != null && ZodFlyover.Live.Active;   // the day clock stands: so does the count of play
            bool on = OzWeather.Enabled;
            var due = sched.Step(t, briefing ? 0f : dt, on, spell != null);
            if (due != null) Begin(due, false);

            if (spell != null && !on && !stopping) StopSpell(3f);                      // WEATHER off (a forced spell too)
            float targetRain = 0f, targetWind = 0f;
            if (spell != null)
            {
                spellClock += dt;
                float s = spellClock - spell.WindLead;
                targetRain = OzRainDayPlan.Intensity(spell, s); targetWind = OzRainDayPlan.WindK(spell, s);
                if (stopping) { stopK = Mathf.MoveTowards(stopK, 0f, dt / stopFade); targetRain *= stopK; targetWind *= stopK; }
                if (!stopping)
                {
                    // the wind's arrival is heard: a gust in the trees as the rain wind comes up (the later ones on its peaks)
                    if (!windSounded && targetWind > 0.25f) { windSounded = true; lastGust = Time.time; if (OneShot(GustId, gustVolume, 1f)) GustsPlayed++; }
                    if (!dropsSounded && s >= 0f) { dropsSounded = true; OneShot(FirstDropsId, 1f, 1f); }
                    for (int i = 0; i < spell.Rumbles.Length; i++)
                        if (!rumbled[i] && s >= spell.Rumbles[i].At) { rumbled[i] = true; Rumble(spell.Rumbles[i]); }
                }
                if (s > spell.Total + spell.WindTail || (stopping && stopK <= 0f)) EndSpell();
            }
            rain = targetRain; windK = targetWind;
            // wet in 12 s (95 % of the way), dry in 60 s to exactly 0 (a straight run down); the option off: dry in 3 s
            float wantWet = Mathf.Clamp01(rain * 1.1f);
            if (wantWet > wet) wet += (wantWet - wet) * (1f - Mathf.Exp(-dt * 3f / Mathf.Max(0.1f, wetUpSeconds)));
            else wet = Mathf.MoveTowards(wet, wantWet, dt / Mathf.Max(0.1f, on ? dryDownSeconds : 3f));
            if (wet < 0.002f && rain <= 0f) wet = 0f;

            // the wind of the level and its air (Atmosphere reads these in its next Update: flags, fog, plants follow)
            if (spell != null)
            {
                float az = spell.WindAzimuth * Mathf.Deg2Rad;
                OzJungleAir.RainWindDir = new Vector2(Mathf.Sin(az), Mathf.Cos(az));
            }
            OzJungleAir.RainWindK = windK;
            OzJungleAir.Rain = rain;
            OzJungleAir.Steam = wet * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(rain / 0.35f)));   // the ground steams while it dries: up as the rain fades, gone with the wetness

            ApplyLighting();
            ApplyGlobals();
            LowPuddles(dt);                                                           // rain puddles on the low grass (OzJungleWeather.LowPuddles.cs)
            UpdateParticles(dt);
            UpdateAudio(dt);
        }

        void Begin(OzRainSpell p, bool isForced)
        {
            spell = p; spellClock = 0f; forced = isForced; stopping = false; stopK = 1f;
            rumbled = new bool[p.Rumbles.Length]; dropsSounded = false; windSounded = false; silentLogged.Clear();
            SpellsStarted++;
            var sb = new System.Text.StringBuilder();
            foreach (var r in p.Rumbles) sb.Append($" {r.At:0}s");
            Debug.Log($"[OzJungleWeather] spell {(isForced ? "(forced) " : $"of day {p.Day} ")}starts: wind to {p.WindAzimuth:0} deg now, first drops in {p.WindLead:0} s, rain {p.Total:0} s (fade {p.FadeIn:0}/{p.FadeOut:0} s), rumbles at{sb}");
        }

        void EndSpell()
        {
            Debug.Log($"[OzJungleWeather] spell over after {spellClock:0} s ({RumblesPlayed} rumbles, {GustsPlayed} gusts so far)");
            spell = null; forced = false; stopping = false; stopK = 1f; rumbled = null;
        }

        // ================================================================== light and globals
        void ApplyLighting()
        {
            if (rain <= 0.0005f) { touched = false; return; }
            var sun = dn ? dn.sun : RenderSettings.sun;
            bool reuse = touched;
            if (sun)
            {   // DayNight rewrote the sun this frame -> that is the new base; else our last change is taken off (nothing piles up)
                if (!reuse || !Mathf.Approximately(sun.intensity, sunWrittenI) || sun.color != sunWrittenC) { sunBaseI = sun.intensity; sunBaseC = sun.color; }
                if (!reuse || !Mathf.Approximately(sun.shadowStrength, shWritten)) shBase = sun.shadowStrength;
                float g = sunBaseC.grayscale;
                var grey = new Color(g * 1.03f, g, g * 0.95f, sunBaseC.a);                 // warm grey
                float i = sunBaseI * (1f - sunDim * rain);
                Color c = Color.Lerp(sunBaseC, grey, greyTint * rain);
                float sh = shBase * (1f - shadowSoften * rain);
                sun.intensity = i; sun.color = c; sun.shadowStrength = sh; sunWrittenI = i; sunWrittenC = c; shWritten = sh;
            }
            if (!reuse || RenderSettings.ambientSkyColor != ambSkyW) ambSkyBase = RenderSettings.ambientSkyColor;
            if (!reuse || RenderSettings.ambientEquatorColor != ambEqW) ambEqBase = RenderSettings.ambientEquatorColor;
            if (!reuse || RenderSettings.ambientGroundColor != ambGrW) ambGrBase = RenderSettings.ambientGroundColor;
            float dim = 1f - ambientDim * rain;
            ambSkyW = ambSkyBase * dim; ambEqW = ambEqBase * dim; ambGrW = ambGrBase * dim;
            RenderSettings.ambientSkyColor = ambSkyW; RenderSettings.ambientEquatorColor = ambEqW; RenderSettings.ambientGroundColor = ambGrW;
            touched = true;
        }

        void ApplyGlobals()
        {
            Shader.SetGlobalFloat(IdWet, wet);
            Shader.SetGlobalFloat(IdRain, rain);
            if (wet <= 0f && rain <= 0f) { Shader.SetGlobalVector(IdWetSky, Vector4.zero); return; }
            Color sky = RenderSettings.fogColor.linear * 0.55f + (touched ? ambSkyBase : RenderSettings.ambientSkyColor).linear * 0.35f;
            Shader.SetGlobalVector(IdWetSky, new Vector4(sky.r, sky.g, sky.b, 0f));
            Shader.SetGlobalVector(IdWetParams, new Vector4(dampDarken, puddleCover, puddleReflect, Mathf.Max(0.001f, rippleStrength)));
            Shader.SetGlobalVector(IdJungleWet, new Vector4(grassDamp, puddleFilm, 0f, 0f));
        }

        // ================================================================== particles
        static void SetLin(Material m, int id, Color linear) => m.SetVector(id, new Vector4(linear.r, linear.g, linear.b, linear.a));

        static readonly List<ParticleSystemVertexStream> Streams = new List<ParticleSystemVertexStream>
        {   // as OzWeather (the shader reads TEXCOORD0.zw = age, stable random)
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.AgePercent, ParticleSystemVertexStream.StableRandomX,
        };

        void BuildRendering()
        {
            var lib = Resources.Load<Material>("OpenZ/FX/M_FxAdditive");                           // keeps the shader in player builds
            var shader = lib && lib.shader ? lib.shader : Shader.Find("OpenZ/FxAdditive");
            if (!shader) { Debug.LogWarning("[OzJungleWeather] OpenZ/FxAdditive missing: no rain particles (wet look, wind and sound still run)"); return; }
            streakMat = new Material(shader) { name = "FX_JungleRainStreak", renderQueue = 3040, hideFlags = HideFlags.DontSave };
            splashMat = new Material(shader) { name = "FX_JungleRainSplash", renderQueue = 3041, hideFlags = HideFlags.DontSave };
            Common(streakMat); Common(splashMat);
            // streaks: an ellipse stretched along the fall, lit by the day's sun and sky, covering a little so it shows on bright ground
            streakMat.SetFloat(IdShape, 1f); streakMat.SetFloat(IdSquare, 0f); streakMat.SetFloat(IdOpq, streakCover); streakMat.SetFloat(IdLit, 0.85f);
            SetLin(streakMat, IdColor, streakColor.linear); SetLin(streakMat, IdEmission, new Color(0.02f, 0.022f, 0.026f)); streakMat.SetFloat(IdCoreMix, 0f);
            // splashes: a flat ring on a horizontal quad, lit
            splashMat.SetFloat(IdShape, 5f); splashMat.SetFloat(IdBandInner, 0.68f); splashMat.SetFloat(IdBandSides, 0f); splashMat.SetFloat(IdOpq, 0.45f); splashMat.SetFloat(IdLit, 1f);
            SetLin(splashMat, IdColor, new Color(0.8f, 0.86f, 0.95f)); SetLin(splashMat, IdEmission, new Color(0.015f, 0.017f, 0.022f)); splashMat.SetFloat(IdIntensity, 1f);

            follow = new GameObject("OzJungleWeather_Follow").transform; follow.SetParent(transform, false);
            streaks = BuildSystem("JungleRainStreaks", streakMat, maxDrops, true);
            streakR = streaks.GetComponent<ParticleSystemRenderer>();
            splashes = BuildSystem("JungleRainSplashes", splashMat, maxSplashes, false);
            ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            streaks.gameObject.SetActive(false); splashes.gameObject.SetActive(false);
            BuildRainLayers(); StyleStreaks();                                                // depth layers and the see-through look (OzJungleWeather.RainLayers.cs)
        }

        static void Common(Material m)
        {
            m.SetFloat(IdUseVc, 1f); m.SetFloat(IdSoft, 0f); m.SetFloat(IdFireRamp, 0f); m.SetFloat(IdWrap, 0.5f);
            m.SetVector(IdFireOpacity, new Vector4(-1f, 0f, 0f, 0f)); m.SetFloat(IdIntensity, 1f);
            SetLin(m, IdCore, Color.white); m.SetFloat(IdCoreMix, 0f); SetLin(m, IdEmission, Color.black);
        }

        ParticleSystem BuildSystem(string name, Material mat, int capacity, bool streak)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();
            var main = ps.main;
            main.playOnAwake = false; main.loop = true; main.maxParticles = capacity;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.startSpeed = 0f; main.gravityModifier = 0f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            var sh = ps.shape;
            if (streak)
            {
                main.simulationSpace = ParticleSystemSimulationSpace.Custom; main.customSimulationSpace = follow;   // pans with the camera
                main.startLifetime = new ParticleSystem.MinMaxCurve(dropLife * 0.85f, dropLife * 1.15f);
                main.startColor = new Color(1f, 1f, 1f, rainAlpha);
                sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(40f, 30f, 40f);
                var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
                vel.y = new ParticleSystem.MinMaxCurve(-dropSpeed * 1.1f, -dropSpeed * 0.9f);
                vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
                var em = ps.emission; em.enabled = true; em.rateOverTime = 0f;
                r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = 1f; r.velocityScale = 0.02f; r.cameraVelocityScale = 0f;
            }
            else
            {
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.26f, 0.4f);
                sh.enabled = false;
                var em = ps.emission; em.enabled = false;
                var sol = ps.sizeOverLifetime; sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.25f, 0f, 2.5f), new Keyframe(1f, 1f)));
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
                r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            }
            col.color = new ParticleSystem.MinMaxGradient(g);
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.SetActiveVertexStreams(Streams);
            r.sortingFudge = streak ? 0f : 2f;
            go.SetActive(true);
            ps.Play();
            return ps;
        }

        /// <summary>The visible ground as an x/z box (the four viewport corners on the ground plane at the focus height).</summary>
        bool GroundBox(out Vector2 min, out Vector2 max, out float gy)
        {
            min = max = default; gy = 0f;
            if (!cam) return false;
            Vector3 focus = rts ? rts.focus : cam.transform.position + cam.transform.forward * 50f;
            gy = game ? game.GroundY(focus.x, focus.z) : 0f;
            var plane = new Plane(Vector3.up, new Vector3(0f, gy, 0f));
            min = new Vector2(float.MaxValue, float.MaxValue); max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                var ray = cam.ViewportPointToRay(new Vector3(i & 1, i >> 1, 0f));
                if (!plane.Raycast(ray, out float d)) d = 400f;
                var p = ray.GetPoint(Mathf.Min(d, 600f));
                min = Vector2.Min(min, new Vector2(p.x, p.z)); max = Vector2.Max(max, new Vector2(p.x, p.z));
            }
            return true;
        }

        void UpdateParticles(float dt)
        {
            if (!streaks || !splashes || !cam) return;
            bool raining = rain > 0.004f;
            bool dripping = !raining && wet > 0.05f;
            if (!dripping) { dripCrowns.Clear(); crownsAt = 0f; }
            if (!raining && !dripping && streaks.particleCount == 0 && splashes.particleCount == 0)
            {
                if (streaks.gameObject.activeSelf) { streaks.gameObject.SetActive(false); splashes.gameObject.SetActive(false); }   // nothing when dry
                return;
            }
            if (!streaks.gameObject.activeSelf) { streaks.gameObject.SetActive(true); splashes.gameObject.SetActive(true); streaks.Play(); splashes.Play(); }
            if (raining && Time.unscaledTime >= roofsAt) { roofsAt = Time.unscaledTime + 0.1f; LookForRoofs(); }   // one building at a time

            // ---- streaks: a box in the camera frustum from 30 % to 100 % of the view distance to the ground, slanted by the spell's wind
            var ct = cam.transform;
            follow.position = ct.position;
            Vector3 fwd = ct.forward;
            float h = rts ? rts.height : Mathf.Max(10f, ct.position.y);
            float D = Mathf.Clamp(h / Mathf.Max(0.2f, -fwd.y), 15f, 600f);
            float dNear = farFrom * D, dFar = 1.02f * D, dMid = 0.5f * (dNear + dFar);
            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanH = tanV * cam.aspect;
            float fall = dropSpeed * dropLife;
            Vector2 wd = OzJungleAir.RainWindDir; float slant = slantSpeed * windK;
            var drift = new Vector3(wd.x, 0f, wd.y) * (slant * dropLife);                 // how far a drop is carried while it falls
            streaks.transform.SetPositionAndRotation(ct.position + fwd * dMid + Vector3.up * (fall * 0.5f) - drift * 0.5f, ct.rotation);
            var sh = streaks.shape; sh.scale = new Vector3(2f * dFar * tanH * 1.08f + drift.magnitude, 2f * dMid * tanV * 1.3f + fall, dFar - dNear + drift.magnitude);
            var main = streaks.main;
            float size = dropWidth * dMid;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.75f, size * 1.25f);
            var vel = streaks.velocityOverLifetime;
            vel.x = new ParticleSystem.MinMaxCurve(wd.x * slant * 0.8f, wd.x * slant * 1.2f);
            vel.z = new ParticleSystem.MinMaxCurve(wd.y * slant * 0.8f, wd.y * slant * 1.2f);
            streakR.velocityScale = streakLength * dMid / Mathf.Max(1f, dropSpeed);
            var em = streaks.emission;
            float alive = Mathf.Min(dropsAtFull, maxDrops) * (raining ? rain : 0f);
            em.rateOverTime = alive / Mathf.Max(0.1f, dropLife);
            streakMat.SetFloat(IdIntensity, rainGain);
            streakMat.SetFloat(IdOpq, streakCover); SetLin(streakMat, IdColor, RainTint().linear); main.startColor = new Color(1f, 1f, 1f, rainAlpha);
            UpdateRainLayers(D, tanV, tanH, wd, slant, raining);                                 // the mid and near layers (OzJungleWeather.RainLayers.cs)   // live: the look can be tuned in Play

            if (dt <= 0f) return;
            if (!GroundBox(out var min, out var max, out _)) return;
            var fxp = OpenZFxPlayer.I;
            // ---- splashes on the visible ground (not on open water: the water shader draws the drop rings there)
            if (raining)
            {
                float s = splashSize * Mathf.Clamp(h / 70f, 0.6f, 2.5f);
                splashAcc += splashesPerSecond * rain * dt;
                int n = Mathf.Min((int)splashAcc, Mathf.Max(1, splashesPerFrame));
                splashAcc -= n;
                for (int i = 0; i < n; i++)
                {
                    float x = fx.Range(min.x, max.x), z = fx.Range(min.y, max.y);
                    var q = new Vector3(x, 0f, z);
                    var vp = cam.WorldToViewportPoint(q);
                    if (vp.x < -0.05f || vp.x > 1.05f || vp.y < -0.05f || vp.y > 1.05f) continue;
                    float gy = game ? game.GroundY(x, z) : 0f;
                    q.y = gy;
                    if (fxp ? fxp.WetAt(q, gy) : OpenZFxPlayer.IsWaterTile(q))
                    {   // open water: the water draws its own fine drop rings; a share of the drops also leaves a ring that is seen from the default height
                        SplashesOnWater++;
                        if (fx.Value() >= waterRingShare) continue;
                        q.y = fxp ? fxp.WaterYAt(q) : gy;
                        ep.position = q + Vector3.up * 0.04f;
                        ep.startSize = s * waterRingSize * fx.Range(0.8f, 1.3f);
                        ep.startLifetime = fx.Range(0.55f, 0.85f);
                        ep.startColor = new Color(1f, 1f, 1f, Mathf.Lerp(0.22f, 0.45f, rain));
                        splashes.Emit(ep, 1); WaterRings++;
                        continue;
                    }
                    // a roof over this place (a fort, a factory): the drop lands on it, not on the ground under it; roofs and roads are hard: a brighter speck
                    float roof = RoofAt(x, z); bool hard = false;
                    if (roof > gy + 0.3f) { q.y = roof; hard = true; RoofSplashes++; }
                    else if (OpenZFxPlayer.ClassAt(q) == ZClass.Road) { hard = true; RoadSplashes++; }
                    ep.position = q + Vector3.up * 0.05f;
                    ep.startSize = s * fx.Range(0.7f, 1.3f) * (hard ? hardSize : 1f);
                    ep.startLifetime = fx.Range(0.26f, 0.4f);
                    ep.startColor = new Color(1f, 1f, 1f, Mathf.Clamp01(Mathf.Lerp(0.25f, 0.5f, rain) * (hard ? hardAlpha : 1f)));
                    splashes.Emit(ep, 1); SplashesEmitted++;
                }
            }
            // ---- drips: small rings under the tree crowns in view, while the ground dries. Every ring is drawn from the crowns
            //      that stand in the visible ground (plus 4 m), looked up four times a second: most of the level's crowns are the
            //      jungle round the map, so a crown picked from all of them would nearly never be one in view
            else if (dripping && OzFloraWind.I)
            {
                if (Time.unscaledTime >= crownsAt) { crownsAt = Time.unscaledTime + 0.25f; OzFloraWind.I.CrownsIn(min, max, 4f, dripCrowns); }
                if (dripCrowns.Count == 0) { dripAcc = 0f; return; }
                dripAcc += dripsPerSecond * wet * dt;
                int n = Mathf.Min((int)dripAcc, 12);
                dripAcc -= n;
                float s = dripSize * Mathf.Clamp(h / 70f, 0.6f, 2.5f);
                for (int i = 0; i < n; i++)
                {
                    var c = dripCrowns[fx.Range(0, dripCrowns.Count)];
                    float a = fx.Range(0f, 6.2832f), rr = c.w * Mathf.Sqrt(fx.Value());
                    float x = c.x + Mathf.Cos(a) * rr, z = c.z + Mathf.Sin(a) * rr;
                    float gy = game ? game.GroundY(x, z) : c.y;
                    var q = new Vector3(x, gy, z);
                    if (fxp ? fxp.WetAt(q, gy) : OpenZFxPlayer.IsWaterTile(q)) continue;
                    ep.position = q + Vector3.up * 0.05f;
                    ep.startSize = s * fx.Range(0.7f, 1.2f);
                    ep.startLifetime = fx.Range(0.3f, 0.45f);
                    ep.startColor = new Color(1f, 1f, 1f, Mathf.Lerp(0.25f, 0.6f, wet));
                    splashes.Emit(ep, 1); DripsEmitted++;
                }
            }
        }

        // ================================================================== roofs
        // The rain also lands on what stands on the ground. Per building: the height of the faces of its main model that look up, on a
        // half-metre grid, read once when the building first stands in the rain (one building per look). A model that cannot be read
        // gets no grid: its drops fall on the ground under it, out of sight, as before.
        sealed class Roof { public ZObject o; public float x0, z0; public int w, h; public float[] y; }
        const float RoofCell = 0.5f;
        readonly Dictionary<int, Roof> roofs = new Dictionary<int, Roof>(); readonly List<Roof> roofList = new List<Roof>(); float roofsAt;
        static readonly List<Vector3> roofV = new List<Vector3>(); static readonly List<int> roofT = new List<int>();

        void LookForRoofs()
        {
            if (!game || game.World == null) return;
            var objs = game.World.Objects;
            for (int i = 0; i < objs.Count; i++)
            {
                var o = objs[i];
                if (o.Kind != ZKind.Building || o.IsBridge || o.Destroyed || roofs.ContainsKey(o.Id)) continue;
                roofs[o.Id] = null;                                                   // looked at, also when nothing can be read
                if (!game.StaticViews.TryGetValue(o.Id, out var go) || !go) return;
                MeshFilter best = null; float area = 0f;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (!mr || !mr.enabled || !mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
                    var sz = mr.bounds.size; if (sz.x * sz.z > area) { area = sz.x * sz.z; best = mf; }
                }
                if (!best || area < 6f) return;
                var r = ReadRoof(best, best.GetComponent<MeshRenderer>().bounds); r.o = o;
                roofs[o.Id] = r; roofList.Add(r);
                return;
            }
        }

        static Roof ReadRoof(MeshFilter mf, Bounds b)
        {
            var r = new Roof { x0 = b.min.x, z0 = b.min.z, w = Mathf.Max(1, Mathf.CeilToInt(b.size.x / RoofCell)), h = Mathf.Max(1, Mathf.CeilToInt(b.size.z / RoofCell)) };
            r.y = new float[r.w * r.h]; for (int i = 0; i < r.y.Length; i++) r.y[i] = float.MinValue;
            var mesh = mf.sharedMesh; var M = mf.transform.localToWorldMatrix;
            mesh.GetVertices(roofV); for (int i = 0; i < roofV.Count; i++) roofV[i] = M.MultiplyPoint3x4(roofV[i]);
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                mesh.GetTriangles(roofT, sub);
                for (int t = 0; t + 2 < roofT.Count; t += 3)
                {
                    Vector3 a = roofV[roofT[t]], bb = roofV[roofT[t + 1]], c = roofV[roofT[t + 2]];
                    var nrm = Vector3.Cross(bb - a, c - a); float len = nrm.magnitude;
                    if (len < 1e-7f || Mathf.Abs(nrm.y) / len < 0.5f) continue;              // near level faces only (the highest one over a cell counts)
                    int ix0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(bb.x, c.x)) - r.x0) / RoofCell), 0, r.w - 1), ix1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(a.x, Mathf.Max(bb.x, c.x)) - r.x0) / RoofCell), 0, r.w - 1);
                    int iz0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.z, Mathf.Min(bb.z, c.z)) - r.z0) / RoofCell), 0, r.h - 1), iz1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(a.z, Mathf.Max(bb.z, c.z)) - r.z0) / RoofCell), 0, r.h - 1);
                    float d = (bb.z - c.z) * (a.x - c.x) + (c.x - bb.x) * (a.z - c.z);
                    bool small = ix0 == ix1 && iz0 == iz1;
                    for (int iz = iz0; iz <= iz1; iz++)
                        for (int ix = ix0; ix <= ix1; ix++)
                        {
                            float px = r.x0 + (ix + 0.5f) * RoofCell, pz = r.z0 + (iz + 0.5f) * RoofCell, y;
                            if (small || Mathf.Abs(d) < 1e-9f) y = (a.y + bb.y + c.y) / 3f;      // a face inside one cell counts with its middle
                            else
                            {
                                float u = ((bb.z - c.z) * (px - c.x) + (c.x - bb.x) * (pz - c.z)) / d, v = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / d, k = 1f - u - v;
                                if (u < -0.02f || v < -0.02f || k < -0.02f) continue;             // the cell's middle is not under this face
                                y = u * a.y + v * bb.y + k * c.y;
                            }
                            int at = iz * r.w + ix; if (y > r.y[at]) r.y[at] = y;
                        }
                }
            }
            return r;
        }

        /// <summary>The roof over a place (world height), or float.MinValue where no read building stands.</summary>
        float RoofAt(float x, float z)
        {
            for (int i = 0; i < roofList.Count; i++)
            {
                var r = roofList[i]; if (r.o.Destroyed) continue;
                int ix = Mathf.FloorToInt((x - r.x0) / RoofCell), iz = Mathf.FloorToInt((z - r.z0) / RoofCell);
                if (ix < 0 || iz < 0 || ix >= r.w || iz >= r.h) continue;
                float y = r.y[iz * r.w + ix]; if (y > float.MinValue) return y;
            }
            return float.MinValue;
        }

        // ================================================================== sound
        Vector3 ListenerPos => CombatAudio.I && CombatAudio.I.ListenerRig ? CombatAudio.I.ListenerRig.position : (cam ? cam.transform.position : Vector3.zero);

        /// <summary>What the camera sees, four times a second: the share of water in view, metal (a fort, a factory, vehicles)
        /// and the plank bridge. The rain's surface layers follow it.</summary>
        void LookAround()
        {
            waterNow = metalNow = plankNow = 0f;
            if (!cam || !game || game.World == null) return;
            if (!GroundBox(out var min, out var max, out float gy)) return;
            int wetN = 0, all = 0;
            for (int iy = 0; iy < 4; iy++)
                for (int ix = 0; ix < 5; ix++)
                {
                    var q = new Vector3(Mathf.Lerp(min.x, max.x, (ix + 0.5f) / 5f), gy, Mathf.Lerp(min.y, max.y, (iy + 0.5f) / 4f));
                    var vp = cam.WorldToViewportPoint(q);
                    if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue;
                    all++; if (OpenZFxPlayer.IsWaterTile(q)) wetN++;
                }
            waterNow = all > 0 ? Mathf.Clamp01(wetN / (float)all * 2.5f) : 0f;            // 40 % of the view is water = full
            int metal = 0; bool planks = false;
            var objs = game.World.Objects;
            for (int i = 0; i < objs.Count; i++)
            {
                var o = objs[i];
                if (o.Destroyed || (o.Kind != ZKind.Building && o.Kind != ZKind.Vehicle)) continue;
                var p = game.CenterWorld(o, false);
                if (p.x < min.x || p.x > max.x || p.z < min.y || p.z > max.y) continue;
                var vp = cam.WorldToViewportPoint(p);
                if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue;
                if (o.Kind == ZKind.Building && o.Type != null && o.Type.StartsWith("bridge")) { planks = true; continue; }
                metal += o.Kind == ZKind.Building ? 3 : 1;
            }
            metalNow = Mathf.Clamp01(metal / 4f);
            plankNow = planks ? 1f : 0f;
        }

        void UpdateAudio(float dt)
        {
            float udt = Time.unscaledDeltaTime;
            if (rain > 0.002f || windK > 0.02f)
            {
                if (Time.unscaledTime >= surfaceAt) { surfaceAt = Time.unscaledTime + 0.25f; LookAround(); }
                float k = 1f - Mathf.Exp(-udt / 0.8f);
                waterShare += (waterNow - waterShare) * k; metalShare += (metalNow - metalShare) * k; plankShare += (plankNow - plankShare) * k;
            }
            float bed = Mathf.Sqrt(Mathf.Clamp01(rain)) * bedVolume;
            Loop("wx16:bed", BedId, bed, 1.5f);
            Loop("wx16:leaves", LeavesId, bed * leavesShare, 1.5f);
            Loop("wx16:water", WaterId, bed * waterShare * waterVolume, 1.5f);
            Loop("wx16:metal", MetalId, bed * metalShare * metalVolume, 1.5f);
            Loop("wx16:planks", PlanksId, bed * plankShare, 1.5f);
            Loop("wx16:wind", WindLoopId, Mathf.Clamp01(windK), 2f);
            Loop("wx16:drips", DripsId, rain > 0.3f ? 0f : Mathf.Clamp01(wet) * (1f - Mathf.Clamp01(rain * 3f)) * dripsVolume, 2.5f);
            if (dt <= 0f) return;                                                      // paused: nothing new
            // gusts in the trees on the peaks of the rain wind (the gust the plants take), at most every gustGapSeconds
            float g = OzFloraWind.I ? OzFloraWind.I.Gust : 0f;
            bool peak = gustPrev > gustPrev2 && gustPrev >= g && gustPrev > 0.9f;
            gustPrev2 = gustPrev; gustPrev = g;
            if (peak && windK > 0.6f && Time.time - lastGust >= gustGapSeconds)
            {
                lastGust = Time.time;
                if (OneShot(GustId, gustVolume * Mathf.Lerp(0.7f, 1f, fx.Value()), Mathf.Lerp(0.94f, 1.04f, fx.Value()))) GustsPlayed++;
                if (OzFloraWind.I && OzFloraWind.I.PalmInView(cam, 60f)) OneShot(PalmRattleId, 1f, 1f);
                if (FlagInView()) OneShot(FlagSnapId, 1f, 1f);
            }
        }

        bool FlagInView()
        {
            if (!cam || !game || game.World == null) return false;
            var objs = game.World.Objects;
            for (int i = 0; i < objs.Count; i++)
            {
                var o = objs[i];
                if (o.Type != "flag" && !(o.Kind == ZKind.Building && o.Type != null && o.Type.StartsWith("fort"))) continue;
                var vp = cam.WorldToViewportPoint(game.CenterWorld(o, false));
                if (vp.z > 0f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f) return true;
            }
            return false;
        }

        void Rumble(OzRainSpell.Rumble r)
        {
            float vol = r.Volume * 1.2f * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(rain)) * thunderVolume;   // 0.41 to 0.6 of the event's level: a soft far roll (the city's far rumble plays at 0.42)
            if (OneShot(ThunderId, vol, r.Pitch, r.Seed)) RumblesPlayed++;
        }

        // ---- the sound table first; without its events the Resources clips on own 2D sources; a sound that has no file yet is
        //      a named silent event in the sound log
        static readonly Dictionary<string, string[]> Clips = new Dictionary<string, string[]>
        {
            { BedId, new[] { "sd_rain_jungle_downpour_loop" } }, { LeavesId, new[] { "sd_rain_on_leaves_loop" } }, { WaterId, new[] { "sd_rain_on_water_loop" } },
            { MetalId, new[] { "sd_rain_on_metal_loop" } }, { DripsId, new[] { "sd_drips_after_rain_loop" } },
            { GustId, new[] { "sd_wind_gust_trees_a", "sd_wind_gust_trees_b" } }, { ThunderId, new[] { "sd_thunder_day_far_a", "sd_thunder_day_far_b" } },
            { WindLoopId, new[] { "sd_wind_trees_jungle_loop" } }, { FirstDropsId, new[] { "sd_rain_first_drops" } }, { PalmRattleId, new[] { "sd_palm_fronds_rattle_a" } },
            { FlagSnapId, new[] { "sd_flag_snap_wind_a" } }, { PlanksId, new[] { "sd_rain_on_planks_loop" } },
        };
        const string ClipDir = "OpenZ/Audio/Ambience/";
        const float FallbackGain = 0.25f;                                              // about the table's -12 dB

        AudioClip Fallback(string id, uint pick)
        {
            if (!Clips.TryGetValue(id, out var names)) return null;
            return Resources.Load<AudioClip>(ClipDir + names[(int)(pick % (uint)names.Length)]);
        }

        void Silent(string id, float vol)
        {
            if (!silentLogged.Add(id)) return;                                           // once per spell
            OzSoundLog.Add("WX", id, null, "SILENT no file yet", vol, ListenerPos);
        }

        void Loop(string key, string id, float vol, float fadeOut)
        {
            var ad = AudioDirector.I;
            bool table = ad && AudioDirector.Has(id);
            fallback.TryGetValue(id, out var src);
            if (vol > 0.004f)
            {
                if (table)
                {
                    if (!ad.LoopActive(key)) ad.StartLoop(key, id, ListenerPos, null, vol, 1f);
                    else ad.SetLoop(key, vol, 1f);
                    if (src && src.isPlaying) src.Stop();
                    return;
                }
                if (!src)
                {
                    var clip = Fallback(id, 0);
                    if (!clip) { Silent(id, vol); return; }
                    var go = new GameObject("OzJungleWeather_" + id); go.transform.SetParent(transform, false);
                    src = go.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 0f; src.priority = 24; src.loop = true; src.clip = clip;
                    fallback[id] = src;
                }
                src.volume = vol * FallbackGain;
                if (!src.isPlaying) { src.Play(); OzSoundLog.Add("WX", id, src.clip.name, "LOOP own source (no table event)", src.volume, ListenerPos); }
                return;
            }
            if (ad && ad.LoopActive(key)) ad.StopLoop(key, fadeOut);
            if (src && src.isPlaying) { src.volume = Mathf.MoveTowards(src.volume, 0f, Time.unscaledDeltaTime / Mathf.Max(0.05f, fadeOut)); if (src.volume <= 0f) src.Stop(); }
        }

        bool OneShot(string id, float vol, float pitch, uint pick = 0)
        {
            var ad = AudioDirector.I;
            if (ad && AudioDirector.Has(id)) return ad.Play(id, ListenerPos, -1, vol, pitch);
            var clip = Fallback(id, pick == 0 ? (uint)fx.Next() : pick);
            if (!clip) { Silent(id, vol); return false; }
            if (!fallback.TryGetValue("oneshot", out var src) || !src)
            {
                var go = new GameObject("OzJungleWeather_OneShots"); go.transform.SetParent(transform, false);
                src = go.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 0f; src.priority = 40;
                fallback["oneshot"] = src;
            }
            src.pitch = pitch; src.PlayOneShot(clip, vol * FallbackGain);
            OzSoundLog.Add("WX", id, clip.name, "PLAY own source (no table event)", vol * FallbackGain, ListenerPos);
            return true;
        }

        void StopAllAudio(float fade)
        {
            var ad = AudioDirector.I;
            if (ad) foreach (var key in new[] { "wx16:bed", "wx16:leaves", "wx16:water", "wx16:metal", "wx16:planks", "wx16:wind", "wx16:drips" }) if (ad.LoopActive(key)) ad.StopLoop(key, fade);
            foreach (var s in fallback.Values) if (s && s.isPlaying) s.Stop();
        }
    }
}
