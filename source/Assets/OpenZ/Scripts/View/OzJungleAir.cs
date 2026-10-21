using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The air of level 16, the third preset beside the desert's and the city's (Atmosphere.Preset.Jungle; Atmosphere.Update
    /// calls <see cref="Apply"/> in its place). VIEW ONLY: shader globals, nothing of the rules.
    /// <list type="bullet">
    /// <item>By day the air is EXACTLY the one the level has had since it was built (the city's day values: the same haze, no
    /// low mist, the fog pass on the same branch), so the approved ground and the matched water look as they did.</item>
    /// <item>Steam: over the swamp water at sunrise, sunset and at night; over the ground (in patches) and the water while
    /// it dries after rain. None at midday. It goes through the fog pass's banks, which are integrated exactly along every
    /// step of the view ray: no grain. (The sampled low mist of the desert level gets about one sample in a layer this
    /// thin from the game camera and shows as a stipple: it is not used here.) No city fog banks at night.</item>
    /// <item>The level's wind: 2.5 m/s (flags, banners, building parts, palms and the plants read it as strength 0.75). In a
    /// rain spell the weather object turns it to the spell's direction and raises it to 5 m/s (strength 1.5) through
    /// <see cref="RainWindK"/> and <see cref="RainWindDir"/>: one wind for everything, in the same frame. The direction
    /// TURNS (the short way round) while the speed rises: the wind never passes through a calm and never flips.</item>
    /// <item>The fog's drift is the wind's path, not wind x time: when the wind turns, the haze and the steam turn with it
    /// and do not race across the screen (_OZFogShift, 0 on the two other levels).</item>
    /// <item>Rain: the haze 1.8 times thicker and greyer; after a spell the steam while the ground dries.</item>
    /// </list>
    /// Statics, so Play can tune them without a rebuild; the numbers are starting values.
    /// </summary>
    public static class OzJungleAir
    {
        // ---- the haze (day = the values the level was approved with)
        public static Color HazeDay = new Color(0.86f, 0.88f, 0.9f), HazeNight = new Color(0.60f, 0.70f, 0.78f), HazeRain = new Color(0.78f, 0.80f, 0.80f);
        public static float DensityDay = 0.0007f, DensityNight = 0.00045f;
        public static float Falloff = 25f, NoiseScale = 0.035f, NoiseAmount = 0.8f;
        public static float SunScatter = 1.0f, SunScatterGolden = 1.6f, SunG = 0.4f, SunGGolden = 0.6f;
        // ---- steam (extinction per metre at the ground). By day: none, the midday air is the approved one.
        public static float SteamDawn = 0.09f, SteamNight = 0.06f;                 // over the swamp water only: at sunrise / sunset, at night
        public static float AfterRainSteam = 0.12f, AfterRainWater = 0.08f;        // while the ground dries: over land (in patches), over water
        public static float SteamHeight = 1.8f, SteamWaterDepth = 2.6f;            // the steam's top over land / over water (m)
        public static float SteamScale = 0.07f, SteamThreshold = 0.42f, SteamSoftness = 0.22f, SteamWaterCover = 0.22f;   // patches of about 14 m with clear gaps over land, nearly closed over water
        public static float SteamDrift = 0.7f, SteamDetail = 0.4f, SteamDetailScale = 2.3f;                                 // drift x wind, the wisps' share and scale
        public static float SteamStepShare = 0.5f, SteamShareFull = 0.02f;         // the fog pass gives the steam layer up to half of its steps, growing with the steam (none at 0: no jump when the steam comes)
        // ---- the ground under it (as it has been since the level was built: no change of the approved look)
        public static float GroundSmoothDay = 0.05f, GroundSmoothNight = 0.5f;
        // ---- wind
        public static Vector3 CalmWind = new Vector3(OzRainDayPlan.CalmWindX, 0f, OzRainDayPlan.CalmWindZ);   // 2.5 m/s on the bearing of the two other levels
        public static float RainWindSpeed = OzRainDayPlan.RainWindSpeed;
        // ---- rain
        public static float RainFog = 2.4f, RainSteam = 0.6f;                      // a light grey veil toward the far side of the view in rain

        /// <summary>Written by the weather object once a frame: the rain wind 0..1 and the way it goes (unit, world x / z).</summary>
        public static float RainWindK; public static Vector2 RainWindDir = new Vector2(1f, 0f);
        /// <summary>Written by the weather object: rain 0..1 (thicker, greyer haze) and the steam after a spell 0..1.</summary>
        public static float Rain, Steam;
        /// <summary>For a camera flight that wants ground mist over the swamp (the level-start fly-over): 0 none .. 1 a low mist
        /// like the one after rain. Whoever sets it sets it back to 0.</summary>
        public static float ExtraSteam;

        /// <summary>The wind of this frame (m/s, world).</summary>
        public static Vector3 WindNow { get; private set; }
        /// <summary>The steam of this frame: extinction per metre at the ground over land and over water (0 = none).</summary>
        public static float SteamLand { get; private set; }
        public static float SteamWater { get; private set; }
        /// <summary>True from the first frame the preset ran in this scene.</summary>
        public static bool Live { get; private set; }

        static readonly int IdColor = Shader.PropertyToID("_FogColor"), IdDensity = Shader.PropertyToID("_FogDensity"),
            IdFalloff = Shader.PropertyToID("_FogHeightFalloff"), IdNoiseScale = Shader.PropertyToID("_FogNoiseScale"),
            IdNoiseAmount = Shader.PropertyToID("_FogNoiseAmount"), IdWind = Shader.PropertyToID("_FogWind"),
            IdSun = Shader.PropertyToID("_FogSunScatter"), IdG = Shader.PropertyToID("_FogG"), IdLow = Shader.PropertyToID("_LowFogDensity"),
            IdLowH = Shader.PropertyToID("_LowFogHeight"), IdLowW = Shader.PropertyToID("_LowFogWaterBoost"),
            IdLowMode = Shader.PropertyToID("_LowFogMode"), IdLowBanks = Shader.PropertyToID("_LowFogBanks"), IdLowShape = Shader.PropertyToID("_LowFogShape"),
            IdLowBanks2 = Shader.PropertyToID("_LowFogBanks2"), IdShimmer = Shader.PropertyToID("_ShimmerAmount"),
            IdSmoothness = Shader.PropertyToID("_Smoothness"), IdShift = Shader.PropertyToID("_OZFogShift");

        static double driftX, driftZ, driftAt; static bool driftOn; static int driftFrame = -1;

        /// <summary>One frame of the jungle air (called by Atmosphere.Update in the place of its own branch). Returns how much
        /// the level's lamps light the fog (0 by day .. 1 at night).</summary>
        public static float Apply(Atmosphere a, float t, float night, float golden, float blue)
        {
            Live = true;
            float rain = Mathf.Clamp01(Rain) * (1f - night), steam = Mathf.Max(Mathf.Clamp01(Steam), Mathf.Clamp01(ExtraSteam));
            Shader.SetGlobalColor(IdColor, Color.Lerp(Color.Lerp(HazeDay, HazeNight, night), HazeRain, rain));
            Shader.SetGlobalFloat(IdDensity, Mathf.Lerp(DensityDay, DensityNight, night) * (1f + RainFog * rain));
            Shader.SetGlobalFloat(IdFalloff, Falloff);
            Shader.SetGlobalFloat(IdNoiseScale, NoiseScale); Shader.SetGlobalFloat(IdNoiseAmount, NoiseAmount);
            Shader.SetGlobalFloat(IdSun, Mathf.Lerp(SunScatter, SunScatterGolden, golden)); Shader.SetGlobalFloat(IdG, Mathf.Lerp(SunG, SunGGolden, golden));
            // steam: through the banks of the fog pass (mode 1, as the level's air has run since it was built; integrated exactly
            // per step). Over water: dawn, dusk, night, and while the ground dries; over land: only while the ground dries.
            // By day both are 0: the pass then takes the very branch it takes today.
            float water = Mathf.Max(SteamNight * night, SteamDawn * Mathf.Max(golden, blue)) * (1f + RainSteam * rain) + AfterRainWater * steam;
            float land = AfterRainSteam * steam;
            float low = Mathf.Max(water, land);
            SteamWater = water; SteamLand = land;
            Shader.SetGlobalFloat(IdLow, low); Shader.SetGlobalFloat(IdLowH, SteamHeight); Shader.SetGlobalFloat(IdLowW, low > 0f ? water / low : 0f);
            Shader.SetGlobalFloat(IdLowMode, 1f);
            Shader.SetGlobalVector(IdLowBanks, new Vector4(SteamScale, SteamThreshold, SteamSoftness, SteamWaterDepth));
            Shader.SetGlobalVector(IdLowShape, new Vector4(low > 0f ? land / low : 0f, 0f, SteamWaterCover, SteamStepShare * Mathf.Clamp01(low / Mathf.Max(1e-6f, SteamShareFull))));   // land factor, beyond the map edge: land, cover bonus over water, step share
            Shader.SetGlobalVector(IdLowBanks2, new Vector4(SteamDrift, SteamDetail, Atmosphere.SkyFloorY, SteamDetailScale));
            Shader.SetGlobalFloat(IdShimmer, 0f);
            if (a && a.groundMaterial)
            {
                float sm = Mathf.Lerp(GroundSmoothDay, GroundSmoothNight, night);
                if (Mathf.Abs(a.groundMaterial.GetFloat(IdSmoothness) - sm) > 0.0005f) a.groundMaterial.SetFloat(IdSmoothness, sm);
            }

            // the one wind of the level: its direction turns to the spell's while its speed rises (never through a calm, never a flip)
            float k = Application.isPlaying ? Mathf.Clamp01(RainWindK) : 0f;
            OzRainDayPlan.BlendWind(CalmWind.x, CalmWind.z, RainWindDir.x, RainWindDir.y, RainWindSpeed, k, out float wx, out float wz);
            var w = new Vector3(wx, 0f, wz);
            WindNow = w;
            Shader.SetGlobalVector(IdWind, new Vector4(w.x, 0f, w.z, 0f));

            // the fog's drift = the path the wind has blown (the shader adds wind x time: the shift makes up the difference)
            if (Application.isPlaying)
            {
                double now = Time.time;
                if (!driftOn) { driftOn = true; driftX = w.x * now; driftZ = w.z * now; driftAt = now; }
                else if (Time.frameCount != driftFrame) { double dt = now - driftAt; driftX += w.x * dt; driftZ += w.z * dt; driftAt = now; }
                driftFrame = Time.frameCount;
                Shader.SetGlobalVector(IdShift, new Vector4((float)(driftX - w.x * now), 0f, (float)(driftZ - w.z * now), 0f));
            }
            else Shader.SetGlobalVector(IdShift, Vector4.zero);
            return night * 0.8f;
        }

        /// <summary>Leaves nothing of this level behind (the weather object calls it when it goes away; a scene change).</summary>
        public static void Reset()
        {
            RainWindK = 0f; Rain = 0f; Steam = 0f; ExtraSteam = 0f; RainWindDir = new Vector2(1f, 0f);
            SteamLand = 0f; SteamWater = 0f;
            driftOn = false; driftFrame = -1; Live = false;
            Shader.SetGlobalVector(IdShift, Vector4.zero);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Boot() { Reset(); UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= Unloaded; UnityEngine.SceneManagement.SceneManager.sceneUnloaded += Unloaded; }
        static void Unloaded(UnityEngine.SceneManagement.Scene s)
        {   // the level itself went away (not a scene that was only laid over it for a while)
            if (!ZGame.I || ZGame.I.level != "L16" || s.name.StartsWith("L16", System.StringComparison.Ordinal)) Reset();
        }
    }
}
