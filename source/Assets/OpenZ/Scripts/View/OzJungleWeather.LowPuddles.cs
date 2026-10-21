using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Level 16 rain puddles on the low, worn grass and on the gravel paths, beside the road puddles of the wet look (the rain
    /// itself is unchanged):
    /// <list type="bullet">
    /// <item>Where: Resources/OpenZ/Ground/L16_low_puddles.png, the map at 8 points per metre, R = the depth of a hollow (0 = never
    /// water), B = a gravel path's puddle. Made from the ground's own data: the transitional grass of the grass fields and of the
    /// patches round the rocks and the buildings (shallower along the roads' verges); a few irregular hollows per patch, deepest in
    /// a patch's middle; and up to three in the middle lane of each longer gravel path. Never on roads, water, rock, ruins, huts,
    /// under a building or on a slope (the shader also keeps them to flat ground); the low grass's never in a path's middle. On a
    /// path the water takes the path's darker earth and is drawn as far as the gravel is.</item>
    /// <item>When: a water level of their own. It rises while it rains (full after about lowFillSeconds of full rain, so the
    /// puddles grow all through a spell) and falls slowly after it (dry lowDrySeconds after full, longer than the damp ground):
    /// the deepest points fill first, so a puddle grows out of a small spot and shrinks back into it. A pause stops it; the
    /// WEATHER option off dries them in 3 s with the rest of the wet look.</item>
    /// <item>Look: Shaders/OpenZJungleWet.hlsl, OZJLowPuddleAlbedo: flat dark water (the ground's colour without its fine detail,
    /// darker and browner), a damp darker margin round each one; the sky's reflection and the rain's rings are the wet look's sheen.
    /// While they outlast the damp ground, this keeps the sky colour of the wet look written so they keep their reflection.</item>
    /// </list>
    /// Global _OzLowPuddle (x level, y reach, z film, w edge softness); 0 = none (every other level and a dry level).
    /// </summary>
    public sealed partial class OzJungleWeather
    {
        [Header("Rain puddles on the low grass and the gravel paths")]
        [Tooltip("Seconds of full rain until the puddles on the low grass and the paths are full (they grow all through the spell)")] public float lowFillSeconds = 80f;
        [Tooltip("Seconds from full to dry after the rain (the damp ground dries in dryDownSeconds)")] public float lowDrySeconds = 150f;
        [Tooltip("Below this rain the puddles do not grow (they dry)")] [Range(0, 0.6f)] public float lowRainFrom = 0.2f;
        [Tooltip("Share of a hollow's depth that is water at the full level (more = larger puddles)")] [Range(0.2f, 0.9f)] public float lowReach = 0.6f;
        [Tooltip("The water film on the ground's colour (lower = darker water)")] [Range(0.2f, 1f)] public float lowFilm = 0.35f;
        [Tooltip("Softness of a puddle's edge (in the hollow's depth)")] [Range(0.01f, 0.2f)] public float lowEdge = 0.05f;
        [Tooltip("x the level (1 = as made; lower = smaller puddles)")] [Range(0, 1.5f)] public float lowAmount = 1f;

        const string LowMaskPath = "OpenZ/Ground/L16_low_puddles";
        static readonly int IdLowPuddle = Shader.PropertyToID("_OzLowPuddle"), IdLowMask = Shader.PropertyToID("_OzLowPuddleMask");
        float lowLevel, lowHold = -1f;
        bool lowTried;
        Texture2D lowMask;

        /// <summary>The water level of the puddles on the low grass and the gravel paths (0 dry .. 1 full).</summary>
        public float LowPuddleLevel => lowLevel;
        public bool LowPuddleMaskLoaded => lowMask != null;

        /// <summary>Proof pictures: holds the puddles of the low grass and the paths at this level (0..1) whatever the rain does; below 0 = back to the rain.</summary>
        public static void HoldLowPuddles(float level) { if (I) I.lowHold = level; }

        void LowPuddles(float dt)
        {
            if (!lowTried)
            {
                lowTried = true;
                lowMask = Resources.Load<Texture2D>(LowMaskPath);
                if (lowMask)
                {
                    Shader.SetGlobalTexture(IdLowMask, lowMask);
                    Debug.Log($"[OzJungleWeather] puddles on the low grass and the gravel paths: picture {lowMask.width}x{lowMask.height}, full after {lowFillSeconds:0} s of rain, dry {lowDrySeconds:0} s after it");
                }
                else Debug.LogWarning($"[OzJungleWeather] {LowMaskPath} missing: no puddles on the low grass and the paths (the road puddles stay)");
            }
            bool on = OzWeather.Enabled;
            if (lowHold >= 0f) lowLevel = Mathf.Clamp01(lowHold);
            else if (on && rain > lowRainFrom)
                lowLevel = Mathf.Min(1f, lowLevel + dt * Mathf.Clamp01((rain - lowRainFrom) / Mathf.Max(0.05f, 0.8f - lowRainFrom)) / Mathf.Max(1f, lowFillSeconds));
            else lowLevel = Mathf.MoveTowards(lowLevel, 0f, dt / Mathf.Max(0.1f, on ? lowDrySeconds : 3f));
            float lv = lowMask ? Mathf.Clamp01(lowLevel * lowAmount) : 0f;
            Shader.SetGlobalVector(IdLowPuddle, new Vector4(lv, lowReach, lowFilm, lowEdge));
            if (lv > 0f && wet <= 0f && rain <= 0f)
            {   // the wet look stops writing its sky colour once the damp ground is dry: the puddles keep their reflection while they last
                Color sky = RenderSettings.fogColor.linear * 0.55f + RenderSettings.ambientSkyColor.linear * 0.35f;
                Shader.SetGlobalVector(IdWetSky, new Vector4(sky.r, sky.g, sky.b, 0f));
                Shader.SetGlobalVector(IdWetParams, new Vector4(dampDarken, puddleCover, puddleReflect, Mathf.Max(0.001f, rippleStrength)));
            }
        }

        static void ResetLowPuddles() => Shader.SetGlobalVector(IdLowPuddle, Vector4.zero);

        /// <summary>Proof line: the level of the puddles on the low grass and the paths and their numbers.</summary>
        public string LowPuddleStatus() =>
            $"[OzJungleWeather] puddles of the low grass and the paths: level {lowLevel:F2}{(lowHold >= 0f ? " (held)" : "")} x amount {lowAmount:F2}, reach {lowReach:F2}, film {lowFilm:F2}, edge {lowEdge:F2}, " +
            $"fill {lowFillSeconds:0} s, dry {lowDrySeconds:0} s, from rain {lowRainFrom:F2}, picture {(lowMask ? $"{lowMask.width}x{lowMask.height}" : (lowTried ? "MISSING" : "not loaded yet"))} | rain {rain:F2} wet {wet:F2}";
    }
}
