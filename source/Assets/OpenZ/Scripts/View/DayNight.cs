using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// lamp pools -> dawn, 12 minute cycle. Minimum brightness stays playable. Lamps: every child of LampRoot named
    /// "*lamp*" gets a warm point light that fades in at dusk.
    /// Night package: moonlit night keys (0.49 moonrise, 0.56-0.80 night, 0.86 pre-dawn), NightGrade.Apply (exposure,
    /// contrast, cool rim, moon shadows) after every Apply, and in level scenes NightLights owns the lamp lights (ExternalLamps).
    /// </summary>
    [ExecuteAlways]
    public partial class DayNight : MonoBehaviour
    {
        [Tooltip("Directional light used as the sun")] public Light sun;
        [Tooltip("Full cycle length in seconds (12 minutes)")] public float cycleSeconds = 720f;
        /// <summary>Sun steps in Play (degrees of elevation or azimuth): between steps the shadow map is perfectly still.</summary>
        // flickering holes were never the sun's speed (0.0083 deg azimuth per 60 fps frame) but slits torn into the shadow map by the
        // caster normal bias on flat-shaded meshes (shadow_fix 01, TeamLit); two-sided casters did not close them. With closed
        public static float SunStepDeg = 0f;
        Vector2 sunShown; bool sunStepInit;
        [Range(0, 1), Tooltip("0 = noon, 0.3 = golden sunset, 0.4 = blue hour, 0.55 = night, 0.9 = dawn")]
        public float timeOfDay = 0.08f;
        /// <summary>
        /// fading out); ZGame sets it in Awake. Noon follows ~1 min later, sunset ~5 min in, night ~7 min in (12 min cycle).</summary>
        /// 0.915 -> 0.935 (+29 game minutes: the key light
        /// 9 -> 21 deg up, still the warm dawn colour; the painted flyby dawn sky is centred on 0.93). Static: the briefing data file
        /// (Resources/OpenZ/Zod/briefing_shots.json, startTod) can tune it without a rebuild.
        public static float SunriseStart = 0.935f;
        /// <summary>
        /// A level with a cool palette (warmth below this, the city 0.35) gets this warmth around the sunrise (full from
        /// 0.90 to 1.00, back to its own by 0.05), so the city morning is the same warm golden dawn as the desert, not a grey night-blue wash.
        /// Below 0 = off.</summary>
        public static float MorningWarmth = 0.9f;
        /// <summary>
        /// city's dark concrete (flat ground gets sun x sin(elevation): 1.25 x sin 25 deg at the start against 1.9 x sin 58 deg at noon, a third of
        /// the light; frame mean 60 against 93 at noon), not the clock, the weather or the menu. A cool-palette level (warmth below 1) gets this
        /// much more sun (x1.7) and ambient (x1.35) in the morning (full from 0.90 to 0.945, gone by 0.99 as the sun climbs), so it starts in a
        /// bright warm morning like the desert. Statics (the briefing data file can tune them).</summary>
        public static float MorningSunLift = 0.7f, MorningAmbientLift = 0.35f;
        /// <summary>
        /// same sunrise light and the player gets the sunrise start time when the camera is handed back; it runs normally from there.</summary>
        public static bool HoldWhileBriefing = true;

        /// <summary>0..1: the morning light lift at t (1 from 0.90 to 0.945, in from 0.86, out by 0.99).</summary>
        public static float MorningLiftK(float t)
        {
            t = Mathf.Repeat(t, 1f);
            if (t < 0.86f) return 0f;
            if (t < 0.90f) return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.86f, 0.90f, t));
            if (t <= 0.945f) return 1f;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.945f, 0.99f, t));
        }
        /// <summary>The time of day the running level started at (ZGame.Awake; proofs).</summary>
        public static float LevelStartTod = -1f;
        public bool running = true;
        [Tooltip("Parent of the lamp props (DEC_*lamp*) that light up at night")] public Transform lampRoot;
        public float lampIntensity = 6f, lampRange = 12f;
        [Range(0, 1), Tooltip("1 = warm desert palette, lower = cooler, greyer light (city)")] public float warmth = 1f;
        /// <summary>Night package: NightLights lights the lamps in level scenes (set before the scene loads), so DayNight
        /// makes no lamp lights of its own in play mode.</summary>
        public static bool ExternalLamps;

        struct Key { public float t, elevation, intensity, lamps; public Color sun, sky, equator, ground, fog; }

        static Color C(float r, float g, float b) => new Color(r, g, b);
        // keyframes (sRGB colours), M12 palette
        static readonly Key[] Keys =
        {
            new Key { t = 0.00f, elevation = 58, intensity = 1.9f, lamps = 0, sun = C(1.00f, 0.92f, 0.78f), sky = C(0.58f, 0.74f, 0.96f), equator = C(0.88f, 0.75f, 0.58f), ground = C(0.66f, 0.47f, 0.27f), fog = C(0.90f, 0.80f, 0.64f) },
            new Key { t = 0.22f, elevation = 34, intensity = 1.8f, lamps = 0, sun = C(1.00f, 0.88f, 0.70f), sky = C(0.58f, 0.72f, 0.94f), equator = C(0.88f, 0.73f, 0.55f), ground = C(0.64f, 0.45f, 0.26f), fog = C(0.90f, 0.78f, 0.60f) },
            new Key { t = 0.32f, elevation = 18, intensity = 1.6f, lamps = 0.2f, sun = C(1.00f, 0.76f, 0.52f), sky = C(0.66f, 0.70f, 0.82f), equator = C(0.82f, 0.72f, 0.64f), ground = C(0.48f, 0.40f, 0.32f), fog = C(0.90f, 0.74f, 0.60f) },
            new Key { t = 0.42f, elevation = 8, intensity = 0.75f, lamps = 0.8f, sun = C(0.88f, 0.64f, 0.74f), sky = C(0.40f, 0.44f, 0.66f), equator = C(0.50f, 0.44f, 0.58f), ground = C(0.26f, 0.23f, 0.30f), fog = C(0.46f, 0.42f, 0.62f) },   // blue hour, a touch deeper (night package)
            new Key { t = 0.49f, elevation = 26, intensity = 0.38f, lamps = 1, sun = C(0.66f, 0.72f, 0.90f), sky = C(0.20f, 0.24f, 0.38f), equator = C(0.18f, 0.19f, 0.28f), ground = C(0.09f, 0.09f, 0.12f), fog = C(0.17f, 0.18f, 0.28f) },   // moonrise
            new Key { t = 0.56f, elevation = 48, intensity = 0.42f, lamps = 1, sun = C(0.68f, 0.76f, 0.94f), sky = C(0.13f, 0.16f, 0.26f), equator = C(0.12f, 0.13f, 0.19f), ground = C(0.06f, 0.06f, 0.08f), fog = C(0.10f, 0.11f, 0.17f) },   // moonlit night: cool, desaturated key with real shadows, low ambient (was a 0.32 saturated-blue wash)
            new Key { t = 0.80f, elevation = 44, intensity = 0.42f, lamps = 1, sun = C(0.68f, 0.76f, 0.94f), sky = C(0.13f, 0.16f, 0.26f), equator = C(0.12f, 0.13f, 0.19f), ground = C(0.06f, 0.06f, 0.08f), fog = C(0.10f, 0.11f, 0.17f) },
            new Key { t = 0.86f, elevation = 22, intensity = 0.36f, lamps = 0.8f, sun = C(0.70f, 0.72f, 0.90f), sky = C(0.24f, 0.26f, 0.40f), equator = C(0.26f, 0.24f, 0.32f), ground = C(0.12f, 0.11f, 0.13f), fog = C(0.30f, 0.28f, 0.40f) },   // pre-dawn
            new Key { t = 0.90f, elevation = 6, intensity = 0.9f, lamps = 0.5f, sun = C(1.00f, 0.72f, 0.60f), sky = C(0.50f, 0.52f, 0.70f), equator = C(0.74f, 0.60f, 0.58f), ground = C(0.34f, 0.28f, 0.26f), fog = C(0.74f, 0.62f, 0.60f) },
            new Key { t = 1.00f, elevation = 58, intensity = 1.9f, lamps = 0, sun = C(1.00f, 0.92f, 0.78f), sky = C(0.58f, 0.74f, 0.96f), equator = C(0.88f, 0.75f, 0.58f), ground = C(0.66f, 0.47f, 0.27f), fog = C(0.90f, 0.80f, 0.64f) },
        };

        readonly List<Light> lamps = new List<Light>();

        void OnEnable()
        {
            lamps.Clear();
            if (lampRoot == null) return;
            if (ExternalLamps && Application.isPlaying) return;                        // night package: NightLights lights the lamps
            foreach (Transform t in lampRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t.parent != lampRoot || !t.name.ToLower().Contains("lamp")) continue;
                var l = t.GetComponentInChildren<Light>();
                if (l == null)
                {
                    var go = new GameObject("LampLight") { hideFlags = HideFlags.DontSave };
                    go.transform.SetParent(t, false);
                    go.transform.localPosition = new Vector3(0, 3.6f, 0);
                    l = go.AddComponent<Light>();
                    l.type = LightType.Point; l.color = new Color(1f, 0.72f, 0.42f); l.range = lampRange; l.shadows = LightShadows.None;
                }
                lamps.Add(l);
            }
        }

        void Update()
        {
            bool briefing = HoldWhileBriefing && ZodFlyover.Live != null && ZodFlyover.Live.Active;
            float before = timeOfDay;
            if (running && Application.isPlaying && !briefing) timeOfDay = Mathf.Repeat(timeOfDay + Time.deltaTime / cycleSeconds, 1f);
            if (Application.isPlaying) timeOfDay = OzNightRain.Clock(this, before, timeOfDay, briefing);         // NIGHT & RAIN off: always day (OzNightRain.cs)
            Apply(timeOfDay);
        }

        Color Cool(Color c)
        {
            float g = c.grayscale; var cool = new Color(g * 0.96f, g * 1.0f, g * 1.06f);
            return Color.Lerp(cool, c, warmthNow);
        }

        float warmthNow = 1f;

        /// <summary>0..1: how much of MorningWarmth applies at t (1 from 0.90 to 1.00, eased in from 0.86, out by 0.05).</summary>
        public static float MorningK(float t)
        {
            t = Mathf.Repeat(t, 1f);
            if (t >= 0.86f) return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.86f, 0.90f, t));
            if (t >= 0.5f) return 0f;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.05f, t));               // the morning goes on past noon's 0 key, out by 0.05
        }

        public void Apply(float t)
        {
            int i = 0;
            while (i < Keys.Length - 2 && t > Keys[i + 1].t) i++;
            Key a = Keys[i], b = Keys[i + 1];
            float f = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a.t, b.t, t));
            warmthNow = MorningWarmth >= 0f && warmth < MorningWarmth ? Mathf.Lerp(warmth, MorningWarmth, MorningK(t)) : warmth;
            if (sun != null)
            {
                float el = Mathf.Lerp(a.elevation, b.elevation, f);
                // 100 deg when t wrapped (220 -> -40 = 320), so every shadow jumped once per day cycle: a full turn per cycle is continuous
                float az = -40f + 360f * Mathf.Repeat(t, 1f);
                // flickering slits). In Play the sun now moves in steps of SunStepDeg (same average speed); edit mode / jumps apply at once.
                var want = new Vector2(el, az);
                if (!Application.isPlaying || !sunStepInit || Mathf.Abs(Mathf.DeltaAngle(want.x, sunShown.x)) >= SunStepDeg
                    || Mathf.Abs(Mathf.DeltaAngle(want.y, sunShown.y)) >= SunStepDeg)
                { sunShown = want; sunStepInit = true; sun.transform.rotation = Quaternion.Euler(el, az, 0); }
                sun.color = Cool(Color.Lerp(a.sun, b.sun, f));
                sun.intensity = Mathf.Lerp(a.intensity, b.intensity, f);
                if (warmth < 0.999f) sun.intensity *= 1f + MorningSunLift * MorningLiftK(t);
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Cool(Color.Lerp(a.sky, b.sky, f));
            RenderSettings.ambientEquatorColor = Cool(Color.Lerp(a.equator, b.equator, f));
            RenderSettings.ambientGroundColor = Cool(Color.Lerp(a.ground, b.ground, f));
            RenderSettings.fogColor = Cool(Color.Lerp(a.fog, b.fog, f));
            var cam = Camera.main;
            if (cam != null) cam.backgroundColor = RenderSettings.fogColor;
            var sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty("_SkyTop"))
            {   // gradient sky: zenith = sky colour, horizon = haze colour (matches the atmosphere horizon haze)
                sky.SetColor("_SkyTop", RenderSettings.ambientSkyColor);
                sky.SetColor("_Horizon", RenderSettings.fogColor);
            }
            if (warmth < 0.999f && MorningAmbientLift > 0f)
            {
                float al = 1f + MorningAmbientLift * MorningLiftK(t);
                RenderSettings.ambientSkyColor *= al; RenderSettings.ambientEquatorColor *= al; RenderSettings.ambientGroundColor *= al;
            }
            float lampK = Mathf.Lerp(a.lamps, b.lamps, f);
            foreach (var l in lamps)
            {
                if (l == null) continue;
                l.intensity = lampIntensity * lampK;
                l.enabled = lampK > 0.02f;
            }
            NightGrade.Apply(sun, t);                                                  // night package: grade, cool rim, moon shadows, exposure
        }
    }
}
