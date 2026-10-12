using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    /// <summary>
    /// (its night keys are the moonlit ones from the night package); DayNight.Apply calls NightGrade.Apply every frame:
    /// <list type="bullet">
    /// <item>globals: _OZNight (0 day .. 1 night, the Atmosphere curve), _OZNightRim (TeamLit's cool moonlit rim on units,
    /// vehicles and buildings), _OZBeamHazeBoost (NightBeam: the city's mystery fog shows more beam than the desert)</item>
    /// <item>the moon (DayNight's sun at night) casts softer, lighter shadows (shadowStrength 0.82 day -> 0.62 night)</item>
    /// <item>moon / ambient scale knobs (NightLights inspector) on top of the keys, for live tuning</item>
    /// <item>play mode: a runtime global Volume (priority 20, weight = night) with the night exposure, contrast, cooler
    /// white balance, lower saturation, lower bloom threshold (lamps and beams bloom) and a stronger vignette; ACES stays.
    /// The OpenZ_PostFX asset is never modified.</item>
    /// </list>
    /// Timeline (DayNight.timeOfDay): 0 noon, 0.32 golden dusk, 0.42 blue hour, 0.49 moonrise, 0.56-0.80 moonlit night,
    /// 0.86 pre-dawn, 0.90 dawn. Lights switch on from 0.335 (NightLights, staggered) and off from 0.895.
    /// </summary>
    public static class NightGrade
    {
        static readonly int IdNight = Shader.PropertyToID("_OZNight"), IdRim = Shader.PropertyToID("_OZNightRim"),
            IdHaze = Shader.PropertyToID("_OZBeamHazeBoost");

        // ------------------------------------------------------------------ tunables (NightLights pushes its inspector values here)
        public static float MoonScale = 1f, AmbientScale = 1f;
        public static float ShadowStrengthDay = 0.82f, ShadowStrengthNight = 0.62f;
        public static float Exposure = -0.1f, Contrast = 14f, Saturation = -12f, Temperature = -9f, Tint = 2f;
        public static Color Filter = new Color(0.95f, 0.97f, 1f, 1f);
        public static float BloomThreshold = 0.95f, BloomIntensity = 1.25f, Vignette = 0.24f;
        public static Color Rim = new Color(0.10f, 0.14f, 0.26f, 1f);    // linear, x _OZNight in TeamLit
        public static Color Fill = new Color(0.30f, 0.33f, 0.40f, 1f);
        static readonly int IdFill = Shader.PropertyToID("_OZNightFill");
        public static float HazeBoost;                                    // level haze - 1 (NightLights)

        /// <summary>Last applied values.</summary>
        public static float TimeOfDay { get; private set; } = 0.08f;
        public static float NightK { get; private set; }

        /// <summary>0 by day .. 1 at night (same curve as Atmosphere): up over 0.40-0.52, down over 0.84-0.92.</summary>
        public static float Night(float t) =>
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.40f, 0.52f, t)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.84f, 0.92f, t)));

        /// <summary>True while the lamps of a fixture with this switch-on / switch-off time should burn.</summary>
        public static bool LightsOn(float t, float onAt, float offAt) => t >= onAt && t < offAt;

        static Volume volume; static VolumeProfile profile;
        static ColorAdjustments ca; static WhiteBalance wb; static Bloom bloom; static UnityEngine.Rendering.Universal.Vignette vig;

        /// <summary>Called by DayNight.Apply after it set the sun, ambient and fog for time t.</summary>
        public static void Apply(Light sun, float t)
        {
            TimeOfDay = t;
            float n = Night(t); NightK = n;
            Shader.SetGlobalFloat(IdNight, n);
            Shader.SetGlobalColor(IdRim, Rim);
            Shader.SetGlobalColor(IdFill, Fill);
            Shader.SetGlobalFloat(IdHaze, HazeBoost);
            if (sun)
            {
                sun.shadowStrength = Mathf.Lerp(ShadowStrengthDay, ShadowStrengthNight, n);
                if (MoonScale != 1f) sun.intensity *= Mathf.Lerp(1f, MoonScale, n);
            }
            if (AmbientScale != 1f)
            {
                float a = Mathf.Lerp(1f, AmbientScale, n);
                RenderSettings.ambientSkyColor *= a; RenderSettings.ambientEquatorColor *= a; RenderSettings.ambientGroundColor *= a;
            }
            if (!Application.isPlaying) return;
            EnsureVolume();
            if (!volume) return;
            volume.weight = n;
            ca.postExposure.Override(Exposure); ca.contrast.Override(Contrast); ca.saturation.Override(Saturation); ca.colorFilter.Override(Filter);
            wb.temperature.Override(Temperature); wb.tint.Override(Tint);
            bloom.threshold.Override(BloomThreshold); bloom.intensity.Override(BloomIntensity);
            vig.intensity.Override(Vignette);
        }

        static void EnsureVolume()
        {
            if (volume) return;
            if (!profile)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "OZ_NightGrade (runtime)"; profile.hideFlags = HideFlags.DontSave;
                ca = profile.Add<ColorAdjustments>(false);
                wb = profile.Add<WhiteBalance>(false);
                bloom = profile.Add<Bloom>(false);
                vig = profile.Add<UnityEngine.Rendering.Universal.Vignette>(false);
            }
            // volume survived leaving play mode: the editor kept the last night grade and every play session stacked one
            // more priority-20 night volume on top.
            var go = new GameObject("OZ_NightGradeVolume");
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 20; volume.weight = 0f; volume.sharedProfile = profile;
            if (!quitHooked) { quitHooked = true; Application.quitting += Release; }
        }

        static bool quitHooked;

        /// <summary>Leaving play mode / quitting: drop the runtime volume and its profile (nothing leaks into edit mode).</summary>
        static void Release()
        {
            quitHooked = false;
            Application.quitting -= Release;
            if (volume) Object.Destroy(volume.gameObject);
            if (profile)
            {
                foreach (var c in profile.components) if (c) Object.Destroy(c);
                Object.Destroy(profile);
            }
            volume = null; profile = null; ca = null; wb = null; bloom = null; vig = null;
        }
    }
}
