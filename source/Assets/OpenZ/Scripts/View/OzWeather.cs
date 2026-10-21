using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// <list type="bullet">
    /// <item>VIEW ONLY. ZGame adds it on L17. It reads the DayNight clock, the camera and (read only) the map's tile classes; it
    /// never writes Sim / ZWorld state and has its own seeded generator (OzWeatherPlan: SplitMix64 per match + night).</item>
    /// <item>Schedule: every night (DayNight 0.46-0.86) rolls one plan: 20 % chance, start inside the moonlit window, 60-120 s
    /// with ~10 s fade in / out, 2-5 spread-out flashes (+1-2 far rumbles), thunder 0.8-4 s after each flash by distance.
    /// Game time = the DayNight clock's time base (Time.deltaTime): pause freezes the shower, flashes and pending thunder.</item>
    /// <item>Rain: two capped Unity particle systems on the always-included OpenZ/FxAdditive shader (no new shader in the build):
    /// stretched streaks in the camera frustum volume (sized with the view distance, so the look holds at every zoom; custom
    /// simulation space that pans with the camera) and flat ring splashes on the visible ground (none on open water, where the
    /// water shader draws drop ripples). The HUD canvases are Screen Space Overlay: the rain is world geometry and can never draw
    /// over the UI.</item>
    /// <item>Wet look: shader globals _OzWetness (lags the rain: wets in ~12 s, dries ~45 s after), _OzRain, _OzWetSky,
    /// _OzWetParams, read by OpenZWet.hlsl in OpenZ/Sand (darker ground, puddles with sky / moon / lamp reflections and drop
    /// ripples), OpenZ/WaterStylized (drop ripples) and OpenZ/TeamLit (environment class darkens). 0 = dry = unchanged shaders;
    /// reset to 0 when the component goes away (scene change, leaving Play).</item>
    /// <item>Lightning: each flash adds a cool pulse to the moon (DayNight's sun) and the trilight ambient for one frame at a time
    /// (DayNight rewrites both every Update, this runs in LateUpdate; if nothing rewrote them the last addition is taken off
    /// first, so nothing accumulates), one unshadowed point light high on the flash's side for mid / close flashes, and for a
    /// close strike a bolt (LineRenderer, HDR, blooms) only where the view shows ground OUTSIDE the play area (never among the
    /// units: a strike must not read as an attack). REDUCED MOTION: flashes at 35 %, one flicker.</item>
    /// <item>Audio through AudioDirector (Ambience bus, 2D): amb_l17_rain loop (volume = sqrt(intensity)), amb_l17_thunder_far /
    /// amb_l17_thunder_close one-shots. Fallback: own 2D AudioSources with the Resources clips when the table lacks the events.</item>
    /// <item>Options: WEATHER toggle (PlayerPrefs oz_weather, default on) in the pause menu and the main menu options; off
    /// mid-storm = 3 s fade out. Debug: ForceStorm(), ForceFlash(), StopStorm(), Status() (OpenZ > Weather menu in the editor).</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(210)]
    public class OzWeather : MonoBehaviour
    {
        public static OzWeather I { get; private set; }
        public const string Pref = "oz_weather", SaltPref = "oz_weather_salt", MatchPref = "oz_weather_match";
        public const string RainId = "amb_l17_rain", ThunderFarId = "amb_l17_thunder_far", ThunderCloseId = "amb_l17_thunder_close";
        const string RainKey = "weather:rain";

        static int enabledCache = -1;
        /// <summary>WEATHER option (PlayerPrefs oz_weather, default on).</summary>
        public static bool Enabled
        {
            get { if (enabledCache < 0) enabledCache = PlayerPrefs.GetInt(Pref, 1); return enabledCache == 1; }
            set { enabledCache = value ? 1 : 0; PlayerPrefs.SetInt(Pref, enabledCache); PlayerPrefs.Save(); if (!value && I) I.StopStorm(3f); }
        }

        [Header("Schedule")]
        [Range(0, 1)] public float chancePerNight = 0.2f;
        public float minSeconds = 60f, maxSeconds = 120f, fadeSeconds = 10f;
        [Header("Rain streaks (camera volume)")]
        public int maxDrops = 2400;
        [Tooltip("Drops alive at full rain (capped by maxDrops)")] public int dropsAtFull = 2000;
        public float dropSpeed = 21f, dropLife = 0.85f;
        public Vector2 wind = new Vector2(1.6f, 0.7f);
        [Tooltip("Drop width per metre of view distance")] public float dropWidth = 0.00095f;
        [Tooltip("Streak length per metre of view distance")] public float streakLength = 0.0065f;
        public float rainGain = 0.9f;
        [Range(0, 1)] public float rainAlpha = 0.5f;
        [Header("Splashes (visible ground)")]
        public int maxSplashes = 700;
        public float splashesPerSecond = 520f, splashSize = 0.32f;
        [Header("Wet look")]
        public float wetUpSeconds = 12f, dryDownSeconds = 45f;
        [Range(0, 0.6f)] public float dampDarken = 0.3f;
        [Range(0, 0.6f)] public float puddleCover = 0.34f;
        public float puddleReflect = 1f, rippleStrength = 1f;
        [Header("Storm light")]
        [Range(0, 0.6f)] public float moonDim = 0.2f;
        [Range(0, 0.4f)] public float ambientDim = 0.08f;
        [Tooltip("Extra fog density at full rain (x Atmosphere's)")] public float fogBoost = 0.5f;
        public Color flashColor = new Color(0.78f, 0.84f, 1f);
        public float flashSun = 1.7f, flashAmbient = 0.85f, flashPoint = 0.9f;
        public bool bolts = true;
        [Header("Audio")]
        public float rainVolume = 1f, thunderVolume = 1f;

        // ------------------------------------------------------------------ state
        ZGame game; DayNight dn; RtsCamera rts; Camera cam;
        int salt, nightCount, forcedCount; bool inNight, tonightUsed; OzStormPlan tonight;
        OzStormPlan storm; float stormClock; bool forced, stopping; float stopK = 1f, stopFade = 3f; bool[] fired;
        float rain, wet;
        sealed class ActiveFlash { public OzStormPlan.Flash f; public float t; public bool bolt; public Vector3 strike; }
        readonly List<ActiveFlash> flashes = new List<ActiveFlash>();
        sealed class Thunder { public OzStormPlan.Flash f; public float left; }
        readonly List<Thunder> thunders = new List<Thunder>();
        public int StormsStarted { get; private set; }
        public int FlashesFired { get; private set; }
        public int ThundersPlayed { get; private set; }
        public float LastThunderDelay { get; private set; }
        public float Rain => rain;
        public float Wetness => wet;
        public bool StormActive => storm != null;
        public float FlashNow { get; private set; }

        // lighting bookkeeping (DayNight rewrites these every Update; see the class summary)
        bool touched; float sunBaseI, sunWrittenI; Color sunBaseC, sunWrittenC;
        Color ambSkyBase, ambEqBase, ambGrBase, ambSkyW, ambEqW, ambGrW;
        float fogBase, fogW, lowFogBase, lowFogW;

        static readonly int IdWet = Shader.PropertyToID("_OzWetness"), IdRain = Shader.PropertyToID("_OzRain"),
            IdWetSky = Shader.PropertyToID("_OzWetSky"), IdWetParams = Shader.PropertyToID("_OzWetParams"),
            IdFogDensity = Shader.PropertyToID("_FogDensity"), IdLowFog = Shader.PropertyToID("_LowFogDensity");
        static readonly int IdShape = Shader.PropertyToID("_Shape"), IdOpq = Shader.PropertyToID("_Opaqueness"), IdColor = Shader.PropertyToID("_Color"),
            IdIntensity = Shader.PropertyToID("_Intensity"), IdCore = Shader.PropertyToID("_CoreColor"), IdCoreMix = Shader.PropertyToID("_CoreMix"),
            IdUseVc = Shader.PropertyToID("_UseVertexColor"), IdSoft = Shader.PropertyToID("_SoftFade"), IdLit = Shader.PropertyToID("_Lit"),
            IdEmission = Shader.PropertyToID("_EmissionColor"), IdSquare = Shader.PropertyToID("_Squareness"), IdBandInner = Shader.PropertyToID("_BandInner"),
            IdBandSides = Shader.PropertyToID("_BandSides"), IdFireRamp = Shader.PropertyToID("_FireRamp"), IdFireOpacity = Shader.PropertyToID("_FireOpacity"),
            IdWrap = Shader.PropertyToID("_Wrap");

        // ------------------------------------------------------------------ rendering objects
        Transform follow; ParticleSystem streaks, splashes; ParticleSystemRenderer streakR; Material streakMat, splashMat, boltMat;
        Light flashLight; LineRenderer bolt; readonly LineRenderer[] branches = new LineRenderer[2];
        float splashAcc; ParticleSystem.EmitParams ep;
        AudioSource fbRain, fbThunder; bool fbWarned;

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
            if (PlayerPrefs.HasKey(SaltPref)) salt = PlayerPrefs.GetInt(SaltPref);                  // fixed salt: reproducible storms (tests)
            else { salt = PlayerPrefs.GetInt(MatchPref, 0) + 1; PlayerPrefs.SetInt(MatchPref, salt); }   // else a new salt per match (varied nights)
            BuildRendering();
            Debug.Log($"[OzWeather] live on {(game ? game.level : "?")}: {chancePerNight:P0} per night, {minSeconds:0}-{maxSeconds:0} s, seed {(game ? game.seed : 0)} / salt {salt}, option {(Enabled ? "ON" : "OFF")}");
        }

        void OnDisable() { ResetGlobals(); StopAudio(0.2f); }
        void OnDestroy() { if (I == this) I = null; ResetGlobals(); foreach (var m in new[] { streakMat, splashMat, boltMat }) if (m) Destroy(m); }

        static void ResetGlobals()
        {
            Shader.SetGlobalFloat(IdWet, 0f); Shader.SetGlobalFloat(IdRain, 0f);
            Shader.SetGlobalVector(IdWetSky, Vector4.zero); Shader.SetGlobalVector(IdWetParams, Vector4.zero);
        }

        // ================================================================== public hooks (Play proofs)
        /// <summary>Starts a shower now (fades in over fadeSeconds). makeNight: jumps the clock to 0.6 when it is not full night.</summary>
        public void ForceStorm(float totalSeconds = 70f, bool makeNight = true)
        {
            if (!Enabled) { Debug.Log("[OzWeather] NIGHT & RAIN is off: no storm"); return; }             // off = none of it, not even a forced one (OzNightRain.cs)
            if (makeNight && dn && (dn.timeOfDay < 0.52f || dn.timeOfDay > 0.8f)) { dn.timeOfDay = 0.6f; dn.Apply(0.6f); }
            var p = OzWeatherPlan.Forced(OzWeatherPlan.Mix(OzWeatherPlan.NightSeed(game ? game.seed : 1, salt, 9000), (ulong)(++forcedCount)), totalSeconds, fadeSeconds);
            Begin(p, true);
        }
        public static bool Force(float totalSeconds = 70f) { if (!I || !Enabled) return false; I.ForceStorm(totalSeconds); return true; }

        /// <summary>One flash now (with its thunder), rain or not: the lightning proof.</summary>
        public void ForceFlash(int kind = OzStormPlan.Close)
        {
            if (!Enabled) return;                                                     // NIGHT & RAIN off: no lightning (OzNightRain.cs)
            var r = new OzWeatherPlan.Rng(OzWeatherPlan.Mix(0xF1A5, (ulong)(++forcedCount)));
            var f = new OzStormPlan.Flash { Kind = kind, Azimuth = r.Range(0f, 360f), Seed = (uint)r.Next(), Pulses = kind == OzStormPlan.Close ? 3 : 2,
                Distance = kind == OzStormPlan.Close ? 320f : kind == OzStormPlan.Mid ? 600f : 1100f, Brightness = kind == OzStormPlan.Close ? 1f : kind == OzStormPlan.Mid ? 0.7f : 0.45f };
            f.Delay = OzWeatherPlan.ThunderDelay(f.Distance);
            Fire(f);
        }

        /// <summary>Ends the running shower with a fade (the WEATHER option off: 3 s).</summary>
        public void StopStorm(float fade = 3f)
        {
            if (storm == null) return;
            stopping = true; stopFade = Mathf.Max(0.1f, fade);
        }

        public string Status()
        {
            float t = dn ? dn.timeOfDay : NightGrade.TimeOfDay;
            string plan = tonight == null ? "no plan (day)" : tonight.Storm ? $"night {tonight.Night}: storm at t {tonight.StartT:F3} ({tonight.Total:0} s, {tonight.VisibleFlashes} flashes)" : $"night {tonight.Night}: dry";
            return $"[OzWeather] t {t:F3} | {plan} | {(storm != null ? $"STORM {(forced ? "forced " : "")}{stormClock:0.0}/{storm.Total:0} s{(stopping ? " stopping" : "")}" : "no storm")} | rain {rain:F2} wet {wet:F2} flash {FlashNow:F2}"
                 + $" | drops {(streaks ? streaks.particleCount : 0)}/{maxDrops} splashes {(splashes ? splashes.particleCount : 0)}/{maxSplashes}"
                 + $" | storms {StormsStarted} flashes {FlashesFired} thunders {ThundersPlayed} (last delay {LastThunderDelay:0.00} s) | option {(Enabled ? "ON" : "OFF")}"
                 + (fbRain || fbThunder ? " | FALLBACK audio sources in use" : "");
        }

        // ================================================================== frame
        void LateUpdate()
        {
            if (!Application.isPlaying) return;
            float dt = Time.deltaTime;                                                // 0 while paused (Time.timeScale 0)
            if (!cam) cam = Camera.main;
            if (!game) game = ZGame.I;
            float t = dn ? dn.timeOfDay : NightGrade.TimeOfDay;
            Schedule(t);

            if (storm != null && !Enabled && !stopping) StopStorm(3f);                  // WEATHER off (also for a forced shower)
            if (!Enabled) { thunders.Clear(); if (stopping && stopFade > 3f) stopFade = 3f; }   // NIGHT & RAIN off: a thunder still on its way is dropped; the rain fades in 3 s also when the way back to day passes the dawn (OzNightRain.cs)
            float target = 0f;
            if (storm != null)
            {
                stormClock += dt;
                target = OzWeatherPlan.Intensity(storm, stormClock);
                if (stopping) { stopK = Mathf.MoveTowards(stopK, 0f, dt / stopFade); target *= stopK; }
                for (int i = 0; i < storm.Flashes.Length; i++)
                    if (!fired[i] && stormClock >= storm.Flashes[i].At) { fired[i] = true; if (!stopping) Fire(storm.Flashes[i]); }
                if (stormClock > storm.Total || (stopping && stopK <= 0f)) EndStorm();
            }
            rain = target;
            float wantWet = Mathf.Clamp01(rain * 1.1f);
            wet += (wantWet - wet) * (1f - Mathf.Exp(-dt / Mathf.Max(0.1f, wantWet > wet ? wetUpSeconds : Enabled ? dryDownSeconds : OzNightRain.OffDrySeconds)));   // off: dry in a few seconds
            if (wet < 0.002f && rain <= 0f) wet = 0f;

            float F = StepFlashes(dt);
            FlashNow = F;
            ApplyLighting(F);
            ApplyGlobals(F);
            UpdateParticles(dt, F);
            UpdateAudio(dt);
        }

        void Schedule(float t)
        {
            bool nightNow = t >= OzWeatherPlan.NightStart && t < OzWeatherPlan.NightEnd;
            if (nightNow && !inNight)
            {
                nightCount++;
                float cycle = dn ? dn.cycleSeconds : 720f;
                tonight = OzWeatherPlan.ForNight(OzWeatherPlan.NightSeed(game ? game.seed : 1, salt, nightCount), nightCount, chancePerNight, cycle, minSeconds, maxSeconds, fadeSeconds);
                tonightUsed = storm != null;                                          // a forced shower already runs: no second one tonight
                Debug.Log($"[OzWeather] night {nightCount}: {(tonight.Storm ? $"STORM at t {tonight.StartT:F3}, {tonight.Total:0} s, {tonight.VisibleFlashes} flashes + {tonight.Flashes.Length - tonight.VisibleFlashes} rumbles" : "dry")}");
            }
            if (!nightNow && inNight) { tonight = null; if (storm != null && !forced) StopStorm(fadeSeconds); }   // the clock left the night (a jump): fade out
            inNight = nightNow;
            if (tonight != null && tonight.Storm && !tonightUsed && storm == null && nightNow && t >= tonight.StartT)
            {
                tonightUsed = true;
                if (Enabled && t < tonight.StartT + 0.03f) Begin(tonight, false);   // a clock jump far past the start skips tonight's shower
            }
        }

        void Begin(OzStormPlan p, bool isForced)
        {
            storm = p; stormClock = 0f; forced = isForced; stopping = false; stopK = 1f;
            fired = new bool[p.Flashes.Length];
            StormsStarted++;
            var sb = new System.Text.StringBuilder();
            foreach (var f in p.Flashes) sb.Append($" {f.At:0}s:{(f.Kind == OzStormPlan.Close ? "close" : f.Kind == OzStormPlan.Mid ? "mid" : f.Kind == OzStormPlan.Far ? "far" : "rumble")}+{f.Delay:0.0}");
            Debug.Log($"[OzWeather] storm {(isForced ? "(forced) " : "")}starts: {p.Total:0} s (fade {p.FadeIn:0}/{p.FadeOut:0} s), flashes{sb}");
        }

        void EndStorm()
        {
            Debug.Log($"[OzWeather] storm over after {stormClock:0} s ({FlashesFired} flashes, {ThundersPlayed} thunders so far)");
            storm = null; forced = false; stopping = false; stopK = 1f; fired = null;
        }

        // ================================================================== lightning
        void Fire(OzStormPlan.Flash f)
        {
            if (OzMotion.Reduced) { f.Brightness *= 0.35f; f.Pulses = 1; OzCalm.Note("lightning"); }   // CALM SCREEN: no strobe
            var a = new ActiveFlash { f = f };
            if (bolts && !OzMotion.Reduced && f.Kind == OzStormPlan.Close && cam) a.bolt = FindStrike(f.Seed, out a.strike);
            if (a.bolt) BuildBolt(a.strike, f.Seed);
            flashes.Add(a);
            thunders.Add(new Thunder { f = f, left = f.Delay });
            FlashesFired++;
        }

        float StepFlashes(float dt)
        {
            float sum = 0f; ActiveFlash lead = null; float leadV = 0f;
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                var a = flashes[i]; a.t += dt;
                if (a.t > (OzMotion.Reduced ? 1.6f : 1.2f)) { flashes.RemoveAt(i); continue; }
                float v = OzMotion.Reduced ? a.f.Brightness * 0.7f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(a.t / 1.6f)), 2f)
                                           : OzWeatherPlan.FlashLight(a.f, a.t);
                sum += v;
                if (v > leadV) { leadV = v; lead = a; }
            }
            // one unshadowed point light on the flash's side (mid / close), or over the strike point
            if (flashLight)
            {
                bool on = lead != null && leadV > 0.01f && (lead.f.Kind == OzStormPlan.Mid || lead.f.Kind == OzStormPlan.Close);
                flashLight.enabled = on;
                if (on)
                {
                    float h = rts ? rts.height : 70f;
                    Vector3 focus = rts ? rts.focus : (cam ? cam.transform.position : Vector3.zero);
                    Vector3 p;
                    if (lead.bolt) p = lead.strike + Vector3.up * 18f;
                    else { float az = lead.f.Azimuth * Mathf.Deg2Rad; p = focus + new Vector3(Mathf.Sin(az), 0f, Mathf.Cos(az)) * (0.55f * h) + Vector3.up * (0.6f * h + 10f); }
                    float d = Mathf.Max(10f, Vector3.Distance(p, focus));
                    flashLight.transform.position = p;
                    flashLight.range = d * 3f;
                    flashLight.intensity = flashPoint * leadV * d * d;                  // ~flashPoint x flash at the view centre (URP 1/d^2)
                    flashLight.color = flashColor;
                }
            }
            if (bolt)
            {
                ActiveFlash b = null; foreach (var a in flashes) if (a.bolt) b = a;
                bool show = b != null && b.t < 0.7f;
                bolt.enabled = show; foreach (var br in branches) if (br) br.enabled = show;
                if (show)
                {
                    float k = Mathf.Clamp01(OzWeatherPlan.FlashLight(b.f, b.t) / Mathf.Max(0.05f, b.f.Brightness));
                    var c = new Color(1f, 1f, 1f, k);
                    bolt.startColor = bolt.endColor = c;
                    foreach (var br in branches) if (br) br.startColor = br.endColor = new Color(1f, 1f, 1f, k * 0.7f);
                }
            }
            return Mathf.Min(1.4f, sum);
        }

        /// <summary>A strike point on visible ground OUTSIDE the play area (the map rectangle + 6 m), or false.</summary>
        bool FindStrike(uint seed, out Vector3 p)
        {
            p = default;
            if (!GroundBox(out var min, out var max, out float gy)) return false;
            Vector2 map = rts ? rts.mapSizeMeters : new Vector2(220, 256);
            var r = new OzWeatherPlan.Rng(seed);
            for (int i = 0; i < 24; i++)
            {
                float x = r.Range(min.x, max.x), z = r.Range(min.y, max.y);
                bool outside = x < -6f || x > map.x + 6f || z > 6f || z < -map.y - 6f;
                if (!outside) continue;
                var q = new Vector3(x, gy, z);
                var vp = cam.WorldToViewportPoint(q);
                if (vp.z <= 0f || vp.x < 0.08f || vp.x > 0.92f || vp.y < 0.12f || vp.y > 0.9f) continue;
                p = q; return true;
            }
            return false;
        }

        void BuildBolt(Vector3 g, uint seed)
        {
            if (!bolt) return;
            var r = new OzWeatherPlan.Rng(seed * 2654435761u + 7);
            float h = rts ? rts.height : 70f, w = Mathf.Clamp(h / 70f, 0.7f, 2.6f);
            const int n = 16;
            var pts = new Vector3[n + 1];
            Vector3 top = g + new Vector3(r.Range(-12f, 12f), 150f, r.Range(-12f, 12f));
            Vector3 off = Vector3.zero;
            for (int i = 0; i <= n; i++)
            {
                float u = i / (float)n;
                if (i > 0 && i < n) off += new Vector3(r.Range(-4.5f, 4.5f), 0f, r.Range(-4.5f, 4.5f));
                if (i == n) off = Vector3.zero;
                pts[i] = Vector3.Lerp(top, g, u) + off * Mathf.Sin(u * Mathf.PI);
            }
            bolt.positionCount = n + 1; bolt.SetPositions(pts);
            bolt.widthMultiplier = 0.9f * w;
            for (int b = 0; b < branches.Length; b++)
            {
                var br = branches[b]; if (!br) continue;
                int from = r.Range(3, n - 5), len = r.Range(4, 8);
                var bp = new Vector3[len + 1]; bp[0] = pts[from];
                Vector3 dir = new Vector3(r.Range(-1f, 1f), -1.4f, r.Range(-1f, 1f)).normalized;
                for (int i = 1; i <= len; i++) bp[i] = bp[i - 1] + dir * r.Range(5f, 9f) + new Vector3(r.Range(-2.5f, 2.5f), 0f, r.Range(-2.5f, 2.5f));
                br.positionCount = len + 1; br.SetPositions(bp); br.widthMultiplier = 0.45f * w;
            }
        }

        // ================================================================== lighting + globals
        void ApplyLighting(float F)
        {
            bool active = rain > 0.0005f || F > 0.0005f;
            if (!active) { touched = false; return; }
            var sun = dn ? dn.sun : RenderSettings.sun;
            bool reuse = touched;
            if (sun)
            {
                // DayNight rewrote the sun this frame -> that is the new base; else take our last addition off (nothing accumulates)
                if (!reuse || !Mathf.Approximately(sun.intensity, sunWrittenI) || sun.color != sunWrittenC) { sunBaseI = sun.intensity; sunBaseC = sun.color; }
                float i = sunBaseI * (1f - moonDim * rain) + F * flashSun;
                Color c = Color.Lerp(sunBaseC, flashColor, Mathf.Clamp01(F * 1.2f));
                sun.intensity = i; sun.color = c; sunWrittenI = i; sunWrittenC = c;
            }
            if (!reuse || RenderSettings.ambientSkyColor != ambSkyW) ambSkyBase = RenderSettings.ambientSkyColor;
            if (!reuse || RenderSettings.ambientEquatorColor != ambEqW) ambEqBase = RenderSettings.ambientEquatorColor;
            if (!reuse || RenderSettings.ambientGroundColor != ambGrW) ambGrBase = RenderSettings.ambientGroundColor;
            float dim = 1f - ambientDim * rain, fa = F * flashAmbient;
            ambSkyW = ambSkyBase * dim + flashColor * fa;
            ambEqW = ambEqBase * dim + flashColor * (fa * 0.8f);
            ambGrW = ambGrBase * dim + flashColor * (fa * 0.45f);
            RenderSettings.ambientSkyColor = ambSkyW; RenderSettings.ambientEquatorColor = ambEqW; RenderSettings.ambientGroundColor = ambGrW;
            // rain haze: Atmosphere writes the fog globals every Update
            float fd = Shader.GetGlobalFloat(IdFogDensity), lf = Shader.GetGlobalFloat(IdLowFog);
            if (!reuse || !Mathf.Approximately(fd, fogW)) fogBase = fd;
            if (!reuse || !Mathf.Approximately(lf, lowFogW)) lowFogBase = lf;
            fogW = fogBase * (1f + fogBoost * rain); lowFogW = lowFogBase * (1f + fogBoost * 0.6f * rain);
            Shader.SetGlobalFloat(IdFogDensity, fogW); Shader.SetGlobalFloat(IdLowFog, lowFogW);
            touched = true;
        }

        void ApplyGlobals(float F)
        {
            Shader.SetGlobalFloat(IdWet, wet);
            Shader.SetGlobalFloat(IdRain, rain);
            if (wet <= 0f && rain <= 0f) { Shader.SetGlobalVector(IdWetSky, Vector4.zero); return; }
            Color sky = RenderSettings.fogColor.linear * 0.55f + (touched ? ambSkyBase : RenderSettings.ambientSkyColor).linear * 0.35f + flashColor.linear * (F * 0.9f);
            Shader.SetGlobalVector(IdWetSky, new Vector4(sky.r, sky.g, sky.b, 0f));
            Shader.SetGlobalVector(IdWetParams, new Vector4(dampDarken, puddleCover, puddleReflect, Mathf.Max(0.001f, rippleStrength)));
        }

        // ================================================================== particles
        static void SetLin(Material m, int id, Color linear) => m.SetVector(id, new Vector4(linear.r, linear.g, linear.b, linear.a));

        static readonly List<ParticleSystemVertexStream> Streams = new List<ParticleSystemVertexStream>
        {   // as OpenZHitSparks / OpenZFxPlayer (the shader reads TEXCOORD0.zw = age, stable random)
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.AgePercent, ParticleSystemVertexStream.StableRandomX,
        };

        void BuildRendering()
        {
            var lib = Resources.Load<Material>("OpenZ/FX/M_FxAdditive");                           // keeps the shader in player builds
            var shader = lib && lib.shader ? lib.shader : Shader.Find("OpenZ/FxAdditive");
            if (!shader) { Debug.LogWarning("[OzWeather] OpenZ/FxAdditive missing: no rain particles (wet look, lightning and audio still run)"); return; }
            streakMat = new Material(shader) { name = "FX_RainStreak", renderQueue = 3040, hideFlags = HideFlags.DontSave };
            splashMat = new Material(shader) { name = "FX_RainSplash", renderQueue = 3041, hideFlags = HideFlags.DontSave };
            boltMat = new Material(shader) { name = "FX_LightningBolt", renderQueue = 3042, hideFlags = HideFlags.DontSave };
            Common(streakMat); Common(splashMat); Common(boltMat);
            // streaks: shape 1 ellipse stretched along the fall, half lit (moon + sky ambient: the lightning lights the rain too)
            streakMat.SetFloat(IdShape, 1f); streakMat.SetFloat(IdSquare, 0f); streakMat.SetFloat(IdOpq, 0f); streakMat.SetFloat(IdLit, 0.6f);
            SetLin(streakMat, IdColor, new Color(0.78f, 0.84f, 1f)); SetLin(streakMat, IdEmission, new Color(0.02f, 0.022f, 0.028f)); streakMat.SetFloat(IdCoreMix, 0f);
            // splashes: shape 5 flat ring on a horizontal quad, lit, a little over-blended so it reads on lit and dark ground
            splashMat.SetFloat(IdShape, 5f); splashMat.SetFloat(IdBandInner, 0.68f); splashMat.SetFloat(IdBandSides, 0f); splashMat.SetFloat(IdOpq, 0.35f); splashMat.SetFloat(IdLit, 1f);
            SetLin(splashMat, IdColor, new Color(0.8f, 0.86f, 0.95f)); SetLin(splashMat, IdEmission, new Color(0.015f, 0.017f, 0.022f)); splashMat.SetFloat(IdIntensity, 1f);
            // bolt: shape 1 near-box core on the line, HDR (blooms)
            boltMat.SetFloat(IdShape, 1f); boltMat.SetFloat(IdSquare, 0.85f); boltMat.SetFloat(IdOpq, 0f); boltMat.SetFloat(IdLit, 0f);
            SetLin(boltMat, IdColor, new Color(0.72f, 0.8f, 1f)); SetLin(boltMat, IdCore, new Color(1f, 1f, 1f)); boltMat.SetFloat(IdCoreMix, 0.7f); boltMat.SetFloat(IdIntensity, 7f);

            follow = new GameObject("OzWeather_Follow").transform; follow.SetParent(transform, false);
            streaks = BuildSystem("RainStreaks", streakMat, maxDrops, true);
            streakR = streaks.GetComponent<ParticleSystemRenderer>();
            splashes = BuildSystem("RainSplashes", splashMat, maxSplashes, false);
            ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };

            var lgo = new GameObject("OzWeather_FlashLight"); lgo.transform.SetParent(transform, false);
            flashLight = lgo.AddComponent<Light>(); flashLight.type = LightType.Point; flashLight.shadows = LightShadows.None;
            flashLight.renderMode = LightRenderMode.ForcePixel; flashLight.enabled = false;

            if (bolts)
            {
                bolt = NewLine("OzWeather_Bolt");
                for (int i = 0; i < branches.Length; i++) branches[i] = NewLine("OzWeather_BoltBranch" + i);
            }
        }

        static void Common(Material m)
        {
            m.SetFloat(IdUseVc, 1f); m.SetFloat(IdSoft, 0f); m.SetFloat(IdFireRamp, 0f); m.SetFloat(IdWrap, 0.5f);
            m.SetVector(IdFireOpacity, new Vector4(-1f, 0f, 0f, 0f)); m.SetFloat(IdIntensity, 1f);
            SetLin(m, IdCore, Color.white); m.SetFloat(IdCoreMix, 0f); SetLin(m, IdEmission, Color.black);
        }

        LineRenderer NewLine(string name)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.sharedMaterial = boltMat; lr.textureMode = LineTextureMode.Stretch; lr.alignment = LineAlignment.View;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false; lr.numCapVertices = 2;
            lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0.8f));
            lr.enabled = false;
            return lr;
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
                vel.x = new ParticleSystem.MinMaxCurve(wind.x * 0.7f, wind.x * 1.3f);
                vel.y = new ParticleSystem.MinMaxCurve(-dropSpeed * 1.1f, -dropSpeed * 0.9f);
                vel.z = new ParticleSystem.MinMaxCurve(wind.y * 0.7f, wind.y * 1.3f);
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

        void UpdateParticles(float dt, float F)
        {
            if (!streaks || !splashes || !cam) return;
            bool raining = rain > 0.004f && Enabled;
            if (!raining && streaks.particleCount == 0 && splashes.particleCount == 0)
            {
                if (streaks.gameObject.activeSelf) { streaks.gameObject.SetActive(false); splashes.gameObject.SetActive(false); }   // zero cost when dry
                return;
            }
            if (!streaks.gameObject.activeSelf) { streaks.gameObject.SetActive(true); splashes.gameObject.SetActive(true); streaks.Play(); splashes.Play(); }

            // ---- streaks: a box in the camera frustum from 30 % to 100 % of the view distance to the ground
            var ct = cam.transform;
            follow.position = ct.position;
            Vector3 fwd = ct.forward;
            float h = rts ? rts.height : Mathf.Max(10f, ct.position.y);
            float D = Mathf.Clamp(h / Mathf.Max(0.2f, -fwd.y), 15f, 600f);
            float dNear = 0.3f * D, dFar = 1.02f * D, dMid = 0.5f * (dNear + dFar);
            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanH = tanV * cam.aspect;
            float fall = dropSpeed * dropLife;
            streaks.transform.SetPositionAndRotation(ct.position + fwd * dMid + Vector3.up * (fall * 0.5f), ct.rotation);
            var sh = streaks.shape; sh.scale = new Vector3(2f * dFar * tanH * 1.08f, 2f * dMid * tanV * 1.3f + fall, dFar - dNear);
            var main = streaks.main;
            float size = dropWidth * dMid;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.75f, size * 1.25f);
            streakR.velocityScale = streakLength * dMid / Mathf.Max(1f, dropSpeed);
            var em = streaks.emission;
            float alive = Mathf.Min(dropsAtFull, maxDrops) * (raining ? rain : 0f);
            em.rateOverTime = alive / Mathf.Max(0.1f, dropLife);
            streakMat.SetFloat(IdIntensity, rainGain * (1f + 2.5f * F));

            // ---- splashes on the visible ground (not on open water: the water shader draws the drop ripples)
            if (!raining || dt <= 0f) return;
            if (!GroundBox(out var min, out var max, out _)) return;
            float s = splashSize * Mathf.Clamp(h / 70f, 0.6f, 2.5f);
            splashAcc += splashesPerSecond * rain * dt;
            int n = Mathf.Min((int)splashAcc, 40);
            splashAcc -= n;
            var fx = OpenZFxPlayer.I;
            for (int i = 0; i < n; i++)
            {
                float x = Random.Range(min.x, max.x), z = Random.Range(min.y, max.y);    // UnityEngine.Random: view-only cosmetics
                var q = new Vector3(x, 0f, z);
                var vp = cam.WorldToViewportPoint(q);
                if (vp.x < -0.05f || vp.x > 1.05f || vp.y < -0.05f || vp.y > 1.05f) continue;
                float gy = game ? game.GroundY(x, z) : 0f;
                q.y = gy;
                if (fx ? fx.WetAt(q, gy) : OpenZFxPlayer.IsWaterTile(q)) continue;
                ep.position = q + Vector3.up * 0.05f;
                ep.startSize = s * Random.Range(0.7f, 1.3f);
                ep.startLifetime = Random.Range(0.26f, 0.4f);
                ep.startColor = new Color(1f, 1f, 1f, Mathf.Lerp(0.35f, 0.75f, rain));
                splashes.Emit(ep, 1);
            }
        }

        // ================================================================== audio
        Vector3 ListenerPos => CombatAudio.I && CombatAudio.I.ListenerRig ? CombatAudio.I.ListenerRig.position : (cam ? cam.transform.position : Vector3.zero);

        void UpdateAudio(float dt)
        {
            var ad = AudioDirector.I;
            float vol = Mathf.Sqrt(Mathf.Clamp01(rain)) * rainVolume;
            if (rain > 0.002f)
            {
                if (ad && AudioDirector.Has(RainId))
                {
                    if (!ad.LoopActive(RainKey)) ad.StartLoop(RainKey, RainId, ListenerPos, null, vol, 1f);
                    else ad.SetLoop(RainKey, vol, 1f);
                    if (fbRain) fbRain.Stop();
                }
                else FallbackRain(vol);
            }
            else StopAudio(1.5f);
            if (dt <= 0f) return;                                                      // paused: pending thunder waits
            for (int i = thunders.Count - 1; i >= 0; i--)
            {
                var th = thunders[i]; th.left -= dt;
                if (th.left > 0f) continue;
                thunders.RemoveAt(i);
                PlayThunder(th.f);
            }
        }

        void PlayThunder(OzStormPlan.Flash f)
        {
            string id = f.Kind == OzStormPlan.Close ? ThunderCloseId : ThunderFarId;
            float vol = f.Kind == OzStormPlan.Close ? 1f : f.Kind == OzStormPlan.Mid ? 0.95f : f.Kind == OzStormPlan.Rumble ? 0.42f
                      : Mathf.Lerp(0.72f, 0.5f, Mathf.InverseLerp(850f, 1370f, f.Distance));
            float pitch = f.Kind == OzStormPlan.Close ? 1f : f.Kind == OzStormPlan.Mid ? 0.97f : 0.9f;
            if (storm != null) vol *= Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(rain));        // fades with the shower
            vol *= thunderVolume;
            var ad = AudioDirector.I;
            bool ok = ad && AudioDirector.Has(id) && ad.Play(id, ListenerPos, -1, vol, pitch);
            if (!ok) ok = FallbackThunder(f.Kind == OzStormPlan.Close ? new[] { "sd_thunder_close_a", "sd_thunder_close_b" } : new[] { "sd_thunder_far_a", "sd_thunder_far_b", "sd_thunder_mid_a" }, vol, pitch, f.Seed);
            if (ok) { ThundersPlayed++; LastThunderDelay = f.Delay; }
        }

        void StopAudio(float fade)
        {
            var ad = AudioDirector.I;
            if (ad && ad.LoopActive(RainKey)) ad.StopLoop(RainKey, fade);
            if (fbRain && fbRain.isPlaying) { fbRain.volume = Mathf.MoveTowards(fbRain.volume, 0f, Time.unscaledDeltaTime / Mathf.Max(0.05f, fade)); if (fbRain.volume <= 0f) fbRain.Stop(); }
        }

        AudioSource FallbackSource(string name)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>(); s.playOnAwake = false; s.spatialBlend = 0f; s.priority = 24;
            if (!fbWarned) { fbWarned = true; Debug.LogWarning("[OzWeather] AudioDirector has no weather events (table not patched?): own 2D sources play the Resources clips"); }
            return s;
        }

        void FallbackRain(float vol)
        {
            if (!fbRain) { fbRain = FallbackSource("OzWeather_RainFallback"); fbRain.clip = Resources.Load<AudioClip>("OpenZ/Audio/Ambience/sd_rain_city_loop"); fbRain.loop = true; }
            if (!fbRain.clip) return;
            fbRain.volume = vol * 0.22f;                                                // ~ the table's -13 dB
            if (!fbRain.isPlaying) fbRain.Play();
        }

        bool FallbackThunder(string[] names, float vol, float pitch, uint seed)
        {
            if (!fbThunder) fbThunder = FallbackSource("OzWeather_ThunderFallback");
            var clip = Resources.Load<AudioClip>("OpenZ/Audio/Ambience/" + names[(int)(seed % (uint)names.Length)]);
            if (!clip) return false;
            fbThunder.pitch = pitch; fbThunder.PlayOneShot(clip, vol * 0.35f);
            return true;
        }
    }
}
