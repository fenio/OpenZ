using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The way good top-down games show jungle
    /// rain): THIN, SHORT, see-through grey-blue streaks, about half as many as before (2,600 at full rain against 5,200), slanted
    /// with the spell's wind, in three depth layers so it reads as depth and not as a sheet of lines over the picture:
    /// <list type="table">
    /// <item>FAR (55 to 102 % of the view distance): 1,500 fine streaks, opacity 0.20, the main system of OzJungleWeather.cs.</item>
    /// <item>MID (30 to 55 %): 850 streaks, opacity 0.17, a little wider and faster.</item>
    /// <item>NEAR (12 to 30 %): 250 larger, fainter (0.12), faster streaks close to the camera.</item>
    /// </list>
    /// Every streak is drawn "over" with that opacity (never added light: no white line on dark ground), in a cool grey-blue taken
    /// from the sky (the haze colour of the moment, pulled toward grey-blue, at most 0.8 bright), half lit by the scene. The rain is
    /// carried by the world: the wet darker ground with its sheen, softer splash rings, the grey veil, the sound (OzJungleWeather.cs).
    /// The layers share the streak material (one colour, one opacity rule) and switch off with the rain.
    /// </summary>
    public sealed partial class OzJungleWeather
    {
        [Header("Rain depth layers (the main streak system is the far layer)")]
        [Tooltip("The main (far) layer starts at this share of the view distance")] public float farFrom = 0.55f;
        public int midDrops = 850, nearDrops = 250;
        [Range(0, 1)] public float midAlpha = 0.17f, nearAlpha = 0.12f;
        public float midFrom = 0.3f, nearFrom = 0.12f;
        [Tooltip("x the far layer's width / length / fall speed")] public Vector3 midScale = new Vector3(1.2f, 1.15f, 1.1f), nearScale = new Vector3(1.8f, 1.4f, 1.3f);
        [Tooltip("The streaks' grey-blue (stored colour) the sky's haze is pulled toward, how far, and the brightest a streak may be")]
        public Color rainBlue = new Color(0.56f, 0.63f, 0.72f); [Range(0, 1)] public float skyPull = 0.6f; public float tintMax = 0.8f;
        [Range(0, 1)] public float streakLit = 0.5f;

        ParticleSystem midStreaks, nearStreaks;
        public int LayerDrops => (midStreaks ? midStreaks.particleCount : 0) + (nearStreaks ? nearStreaks.particleCount : 0);
        /// <summary>Proof: the streak colour of this frame (stored values) and the opacity of the three layers.</summary>
        public Color RainTintNow { get; private set; }
        public string LayersStatus() => $"rain layers: far {(streaks ? streaks.particleCount : 0)}/{Mathf.Min(dropsAtFull, maxDrops)} a {rainAlpha:F2}, mid {(midStreaks ? midStreaks.particleCount : 0)}/{midDrops} a {midAlpha:F2}, near {(nearStreaks ? nearStreaks.particleCount : 0)}/{nearDrops} a {nearAlpha:F2}; tint ({RainTintNow.r:F2}, {RainTintNow.g:F2}, {RainTintNow.b:F2}); cover {streakCover:F2}";

        void BuildRainLayers()
        {
            if (!streakMat) return;
            midStreaks = BuildSystem("JungleRainStreaksMid", streakMat, Mathf.Max(64, midDrops + midDrops / 4), true);
            nearStreaks = BuildSystem("JungleRainStreaksNear", streakMat, Mathf.Max(32, nearDrops + nearDrops / 4), true);
            midStreaks.gameObject.SetActive(false); nearStreaks.gameObject.SetActive(false);
        }

        /// <summary>The streaks' colour: the haze of the moment pulled toward the grey-blue, never brighter than tintMax.</summary>
        Color RainTint()
        {
            var sky = RenderSettings.fogColor;
            var c = Color.Lerp(sky, rainBlue, skyPull);
            float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (m > tintMax) c *= tintMax / m;
            c.a = 1f; RainTintNow = c;
            return c;
        }

        void StyleStreaks()
        {
            if (!streakMat) return;
            streakMat.SetFloat(IdLit, streakLit);
        }

        /// <summary>Called by UpdateParticles after the far layer: the two nearer layers in their own depth bands.</summary>
        void UpdateRainLayers(float D, float tanV, float tanH, Vector2 wd, float slant, bool raining)
        {
            Layer(midStreaks, D, midFrom, farFrom, midDrops, midAlpha, midScale, tanV, tanH, wd, slant, raining);
            Layer(nearStreaks, D, nearFrom, midFrom, nearDrops, nearAlpha, nearScale, tanV, tanH, wd, slant, raining);
        }

        void Layer(ParticleSystem ps, float D, float from, float to, int drops, float alpha, Vector3 k, float tanV, float tanH, Vector2 wd, float slant, bool raining)
        {
            if (!ps) return;
            bool want = raining || ps.particleCount > 0;
            if (want != ps.gameObject.activeSelf) { ps.gameObject.SetActive(want); if (want) ps.Play(); }
            if (!want) return;
            var ct = cam.transform;
            float dNear = from * D, dFar = to * D, dMid = 0.5f * (dNear + dFar);
            float speed = dropSpeed * k.z, fall = speed * dropLife;
            var drift = new Vector3(wd.x, 0f, wd.y) * (slant * k.z * dropLife);
            ps.transform.SetPositionAndRotation(ct.position + ct.forward * dMid + Vector3.up * (fall * 0.5f) - drift * 0.5f, ct.rotation);
            var sh = ps.shape; sh.scale = new Vector3(2f * dFar * tanH * 1.08f + drift.magnitude, 2f * dMid * tanV * 1.3f + fall, dFar - dNear + drift.magnitude);
            var main = ps.main;
            float size = dropWidth * k.x * dMid;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.75f, size * 1.25f);
            main.startColor = new Color(1f, 1f, 1f, alpha);
            var vel = ps.velocityOverLifetime;
            vel.x = new ParticleSystem.MinMaxCurve(wd.x * slant * k.z * 0.8f, wd.x * slant * k.z * 1.2f);
            vel.y = new ParticleSystem.MinMaxCurve(-speed * 1.1f, -speed * 0.9f);
            vel.z = new ParticleSystem.MinMaxCurve(wd.y * slant * k.z * 0.8f, wd.y * slant * k.z * 1.2f);
            ps.GetComponent<ParticleSystemRenderer>().velocityScale = streakLength * k.y * dMid / Mathf.Max(1f, speed);
            var em = ps.emission;
            em.rateOverTime = raining ? Mathf.Min(drops, ps.main.maxParticles) * rain / Mathf.Max(0.1f, dropLife) : 0f;
        }
    }
}
